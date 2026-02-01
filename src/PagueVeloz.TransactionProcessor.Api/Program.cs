using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PagueVeloz.TransactionProcessor.Api.Json;
using PagueVeloz.TransactionProcessor.Application;
using PagueVeloz.TransactionProcessor.Application.Commands.CreateAccount;
using PagueVeloz.TransactionProcessor.Application.Commands.EnqueueTransaction;
using PagueVeloz.TransactionProcessor.Application.Commands.ProcessTransaction;
using PagueVeloz.TransactionProcessor.Application.Contracts.Accounts;
using PagueVeloz.TransactionProcessor.Application.Contracts.Transactions;
using PagueVeloz.TransactionProcessor.Application.Queries.GetAccount;
using PagueVeloz.TransactionProcessor.Application.Queries.GetTransactionByReference;
using PagueVeloz.TransactionProcessor.Domain;
using PagueVeloz.TransactionProcessor.Infrastructure;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence;
using Serilog;
using Serilog.Formatting.Compact;
using Swashbuckle.AspNetCore.Filters;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) =>
{
  lc.ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter());

  var logPath = ctx.Configuration["Logging:FilePath"];
  if (!string.IsNullOrWhiteSpace(logPath))
  {
    lc.WriteTo.File(
      formatter: new RenderedCompactJsonFormatter(),
      path: logPath,
      rollingInterval: RollingInterval.Day,
      shared: true,
      flushToDiskInterval: TimeSpan.FromSeconds(1));
  }
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers().AddJsonOptions(options =>
{
  options.JsonSerializerOptions.PropertyNamingPolicy = new SnakeCaseLowerNamingPolicy();
  options.JsonSerializerOptions.DictionaryKeyPolicy = options.JsonSerializerOptions.PropertyNamingPolicy;
  options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
  o.SwaggerDoc("v1", new OpenApiInfo
  {
    Title = "PagueVeloz Transaction Processor",
    Version = "v1",
    Description =
      "API do núcleo transacional (desafio PagueVeloz).\n\n" +
      "Operações suportadas: credit, debit, reserve, capture, reversal, transfer.\n" +
      "Idempotência garantida por reference_id (repetir o mesmo comando devolve o mesmo resultado)."
  });

  var apiXml = Path.Combine(AppContext.BaseDirectory, "PagueVeloz.TransactionProcessor.Api.xml");
  if (File.Exists(apiXml)) o.IncludeXmlComments(apiXml);

  var appXml = Path.Combine(AppContext.BaseDirectory, "PagueVeloz.TransactionProcessor.Application.xml");
  if (File.Exists(appXml)) o.IncludeXmlComments(appXml);

  o.CustomSchemaIds(t => t.FullName);
  o.ExampleFilters();
});

builder.Services.AddSwaggerExamplesFromAssemblies(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly());

builder.Services.AddHealthChecks();

builder.Services.ConfigureHttpJsonOptions(options =>
{
  options.SerializerOptions.PropertyNamingPolicy = new SnakeCaseLowerNamingPolicy();
  options.SerializerOptions.DictionaryKeyPolicy = options.SerializerOptions.PropertyNamingPolicy;
  options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddOpenTelemetry()
  .ConfigureResource(r => r.AddService("PagueVeloz.TransactionProcessor"))
  .WithTracing(tracerProviderBuilder =>
  {
    tracerProviderBuilder
      .AddAspNetCoreInstrumentation()
      .AddHttpClientInstrumentation();

    var otlp = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
    if (!string.IsNullOrWhiteSpace(otlp))
    {
      tracerProviderBuilder.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
    }
  })
  .WithMetrics(meterProviderBuilder =>
  {
    meterProviderBuilder
      .AddAspNetCoreInstrumentation()
      .AddHttpClientInstrumentation()
      .AddRuntimeInstrumentation()
      .AddProcessInstrumentation();

    var otlp = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
    if (!string.IsNullOrWhiteSpace(otlp))
    {
      meterProviderBuilder.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
    }
  });

var app = builder.Build();

var includeDetails = app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Test");

app.UseExceptionHandler(errorApp =>
{
  errorApp.Run(async context =>
  {
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    var ex = feature?.Error;

    if (ex is not null)
    {
      Log.Error(ex,
        "Unhandled exception on {Method} {Path}. TraceId={TraceId}",
        context.Request.Method,
        context.Request.Path,
        context.TraceIdentifier);
    }

    if (ex is ValidationException vex)
    {
      context.Response.StatusCode = StatusCodes.Status400BadRequest;
      await context.Response.WriteAsJsonAsync(new
      {
        error = "validation_error",
        details = vex.Errors.Select(e => new { field = e.PropertyName, message = e.ErrorMessage })
      });
      return;
    }

    if (ex is DomainException de)
    {
      if (IsAlreadyExistsMessage(de.Message))
      {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new
        {
          error = "conflict",
          message = includeDetails ? de.Message : "Resource already exists."
        });
        return;
      }

      context.Response.StatusCode = StatusCodes.Status400BadRequest;
      await context.Response.WriteAsJsonAsync(new
      {
        error = "domain_error",
        message = includeDetails ? de.Message : "Invalid request."
      });
      return;
    }

    if (ex is DbUpdateException due && IsUniqueViolation(due))
    {
      context.Response.StatusCode = StatusCodes.Status409Conflict;
      await context.Response.WriteAsJsonAsync(new
      {
        error = "conflict",
        message = includeDetails ? (due.InnerException?.Message ?? due.Message) : "Resource already exists.",
        exception = includeDetails ? due.GetType().FullName : null,
        trace_id = context.TraceIdentifier,
        exception_chain = includeDetails ? Flatten(due).ToArray() : null,
        sql = includeDetails ? TrySql(due) : null
      });
      return;
    }

    if (ex is InvalidOperationException ioeExists && IsAlreadyExistsMessage(ioeExists.Message))
    {
      context.Response.StatusCode = StatusCodes.Status409Conflict;
      await context.Response.WriteAsJsonAsync(new
      {
        error = "conflict",
        message = includeDetails ? ioeExists.Message : "Resource already exists."
      });
      return;
    }

    if (ex is InvalidOperationException ioe &&
        ioe.Message.StartsWith("Account '", StringComparison.Ordinal) &&
        ioe.Message.EndsWith("' not found.", StringComparison.Ordinal))
    {
      context.Response.StatusCode = StatusCodes.Status404NotFound;
      await context.Response.WriteAsJsonAsync(new
      {
        error = "not_found",
        message = includeDetails ? ioe.Message : "Resource not found."
      });
      return;
    }

    if (ex is NotSupportedException ||
        ex is ArgumentException ||
        (ex is InvalidOperationException ioe2 &&
         ioe2.Message.Contains("operation", StringComparison.OrdinalIgnoreCase)))
    {
      context.Response.StatusCode = StatusCodes.Status400BadRequest;
      await context.Response.WriteAsJsonAsync(new
      {
        error = "validation_error",
        details = new[]
        {
          new { field = "Request.Operation", message = includeDetails ? ex!.Message : "Invalid operation." }
        }
      });
      return;
    }

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;

    await context.Response.WriteAsJsonAsync(new
    {
      error = "internal_error",
      trace_id = context.TraceIdentifier,
      message = includeDetails ? ex?.Message : "Unexpected error.",
      exception = includeDetails ? ex?.GetType().FullName : null,
      exception_chain = includeDetails ? Flatten(ex).ToArray() : null,
      sql = includeDetails ? TrySql(ex) : null
    });
  });
});

app.UseSerilogRequestLogging();

app.MapHealthChecks("/health/live")
  .WithTags("Health")
  .WithSummary("Health check (liveness)")
  .WithDescription("Indica se o processo está saudável.");

app.MapHealthChecks("/health/ready")
  .WithTags("Health")
  .WithSummary("Health check (readiness)")
  .WithDescription("Indica se a API está pronta para receber tráfego.");

app.MapSwagger();
app.UseSwaggerUI();

using (var scope = app.Services.CreateScope())
{
  var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
  await db.Database.EnsureCreatedAsync();
}

var accounts = app.MapGroup("/api/accounts").WithTags("Accounts");

accounts.MapPost("",
    [SwaggerRequestExample(typeof(CreateAccountRequest), typeof(CreateAccountRequestExamples))]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(AccountResponseExamples))]
    async (CreateAccountRequest req, IMediator mediator, CancellationToken ct) =>
    {
      var res = await mediator.Send(new CreateAccountCommand(req), ct);
      return Results.Created($"/api/accounts/{res.AccountId}", res);
    })
  .WithName("CreateAccount")
  .WithSummary("Criar conta")
  .WithDescription(
    "Cria uma conta para um cliente.\n\n" +
    "- Um cliente pode possuir N contas.\n" +
    "- initial_balance e credit_limit são em centavos.\n" +
    "- Se account_id não for informado, será gerado automaticamente.")
  .Produces<AccountResponse>(StatusCodes.Status201Created)
  .Produces(StatusCodes.Status400BadRequest)
  .Produces(StatusCodes.Status409Conflict)
  .Produces(StatusCodes.Status500InternalServerError);

accounts.MapGet("{accountId}",
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(AccountResponseExamples))]
    async (string accountId, IMediator mediator, CancellationToken ct) =>
    {
      var res = await mediator.Send(new GetAccountQuery(accountId), ct);
      return res is null ? Results.NotFound() : Results.Ok(res);
    })
  .WithName("GetAccount")
  .WithSummary("Consultar conta")
  .WithDescription("Retorna os saldos (total, reservado e disponível), limite e status da conta.")
  .Produces<AccountResponse>(StatusCodes.Status200OK)
  .Produces(StatusCodes.Status404NotFound)
  .Produces(StatusCodes.Status500InternalServerError);

accounts.MapGet("{accountId}/transactions", async (string accountId, IMediator mediator, CancellationToken ct) =>
  {
    var account = await mediator.Send(new GetAccountQuery(accountId), ct);
    if (account is null)
      return Results.NotFound();

    var history = await mediator.Send(
      new PagueVeloz.TransactionProcessor.Application.Queries.GetAccountTransactions.GetAccountTransactionsQuery(accountId),
      ct);

    return Results.Ok(history ?? new List<PagueVeloz.TransactionProcessor.Application.Contracts.Transactions.TransactionResponse>());
  })
  .WithName("GetAccountTransactions")
  .WithSummary("Consultar histórico de transações da conta")
  .WithDescription("Retorna as transações registradas para a conta informada.")
  .Produces<List<PagueVeloz.TransactionProcessor.Application.Contracts.Transactions.TransactionResponse>>(StatusCodes.Status200OK)
  .Produces(StatusCodes.Status404NotFound)
  .Produces(StatusCodes.Status500InternalServerError);

var transactions = app.MapGroup("/api/transactions").WithTags("Transactions");

transactions.MapPost("",
    [SwaggerRequestExample(typeof(TransactionRequest), typeof(TransactionRequestExamples))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(TransactionResponseExamples))]
    async (TransactionRequest req, IMediator mediator, CancellationToken ct) =>
    {
      var res = await mediator.Send(new ProcessTransactionCommand(req), ct);
      return ToProcessHttpResult(res);
    })
  .WithName("ProcessTransaction")
  .WithSummary("Processar transação (síncrono)")
  .WithDescription(
    "Processa a operação imediatamente e retorna o estado final.\n\n" +
    "**Idempotência:** repetir a mesma request (mesmo reference_id) devolve o mesmo resultado.")
  .Produces<TransactionResponse>(StatusCodes.Status200OK)
  .Produces<TransactionResponse>(StatusCodes.Status400BadRequest)
  .Produces<TransactionResponse>(StatusCodes.Status404NotFound)
  .Produces(StatusCodes.Status500InternalServerError);

transactions.MapPost("enqueue",
    [SwaggerRequestExample(typeof(TransactionRequest), typeof(TransactionRequestExamples))]
    [SwaggerResponseExample(StatusCodes.Status202Accepted, typeof(TransactionResponseExamples))]
    async (TransactionRequest req, IMediator mediator, CancellationToken ct) =>
    {
      var res = await mediator.Send(new EnqueueTransactionCommand(req), ct);
      return ToEnqueueHttpResult(req, res);
    })
  .WithName("EnqueueTransaction")
  .WithSummary("Enfileirar transação (assíncrono)")
  .WithDescription(
    "Aceita a operação e publica em background (worker).\n\n" +
    "Útil para simular alto volume e processamento assíncrono. " +
    "O resultado pode ser consultado depois via GET /api/transactions/{reference_id}.")
  .Produces<TransactionResponse>(StatusCodes.Status202Accepted)
  .Produces<TransactionResponse>(StatusCodes.Status400BadRequest)
  .Produces<TransactionResponse>(StatusCodes.Status404NotFound)
  .Produces(StatusCodes.Status500InternalServerError);

transactions.MapGet("{referenceId}",
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(TransactionResponseExamples))]
    async (string referenceId, IMediator mediator, CancellationToken ct) =>
    {
      var res = await mediator.Send(new GetTransactionByReferenceQuery(referenceId), ct);
      return res is null ? Results.NotFound() : Results.Ok(res);
    })
  .WithName("GetTransactionByReference")
  .WithSummary("Consultar transação por reference_id")
  .WithDescription("Retorna o resultado persistido para o reference_id (idempotência / auditoria).")
  .Produces<TransactionResponse>(StatusCodes.Status200OK)
  .Produces(StatusCodes.Status404NotFound)
  .Produces(StatusCodes.Status500InternalServerError);

app.Run();

static bool IsNotFoundMessage(string? msg)
{
  if (string.IsNullOrWhiteSpace(msg)) return false;

  if (string.Equals(msg, "Resource not found.", StringComparison.Ordinal)) return true;

  if (msg.StartsWith("Account '", StringComparison.Ordinal) &&
      msg.EndsWith("' not found.", StringComparison.Ordinal))
    return true;

  if (msg.StartsWith("Original transaction '", StringComparison.Ordinal) &&
      msg.EndsWith("' not found.", StringComparison.Ordinal))
    return true;

  if (msg.StartsWith("Transaction '", StringComparison.Ordinal) &&
      msg.EndsWith("' not found.", StringComparison.Ordinal))
    return true;

  return false;
}

static IResult ToProcessHttpResult(TransactionResponse res)
{
  if (string.Equals(res.Status, "success", StringComparison.OrdinalIgnoreCase))
    return Results.Ok(res);

  if (IsNotFoundMessage(res.ErrorMessage))
    return Results.NotFound(res);

  return Results.BadRequest(res);
}

static IResult ToEnqueueHttpResult(TransactionRequest req, TransactionResponse res)
{
  if (string.Equals(res.Status, "failed", StringComparison.OrdinalIgnoreCase))
  {
    if (IsNotFoundMessage(res.ErrorMessage))
      return Results.NotFound(res);

    return Results.BadRequest(res);
  }

  return Results.Accepted($"/api/transactions/{req.ReferenceId}", res);
}

static bool IsAlreadyExistsMessage(string? msg)
{
  if (string.IsNullOrWhiteSpace(msg)) return false;
  return msg.Contains("already exists", StringComparison.OrdinalIgnoreCase);
}

static SqlException? FindSqlException(Exception ex)
{
  for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
  {
    if (cur is SqlException sql)
      return sql;
  }
  return null;
}

static bool IsUniqueViolation(Exception ex)
{
  var sql = FindSqlException(ex);
  return sql is not null && (sql.Number == 2627 || sql.Number == 2601);
}

static IEnumerable<object> Flatten(Exception? ex)
{
  for (var cur = ex; cur is not null; cur = cur.InnerException)
    yield return new { type = cur.GetType().FullName, message = cur.Message };
}

static object? TrySql(Exception? ex)
{
  for (var cur = ex; cur is not null; cur = cur.InnerException)
  {
    if (cur is SqlException sql)
      return new { number = sql.Number, message = sql.Message };
  }
  return null;
}
