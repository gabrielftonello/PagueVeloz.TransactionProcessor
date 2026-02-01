using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PagueVeloz.TransactionProcessor.Domain;

namespace PagueVeloz.TransactionProcessor.Api.Middleware;

public sealed class ApiExceptionMiddleware
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  private readonly RequestDelegate _next;
  private readonly IWebHostEnvironment _env;

  public ApiExceptionMiddleware(RequestDelegate next, IWebHostEnvironment env)
  {
    _next = next;
    _env = env;
  }

  public async Task Invoke(HttpContext ctx)
  {
    try
    {
      await _next(ctx);
    }
    catch (Exception ex)
    {
      var includeDetails = _env.IsDevelopment() || _env.IsEnvironment("Test");
      var (status, error, message) = Map(ex, includeDetails);

      if (ctx.Response.HasStarted)
        throw;

      ctx.Response.Clear();
      ctx.Response.StatusCode = status;
      ctx.Response.ContentType = "application/json";
      ctx.Response.Headers["X-TraceId"] = ctx.TraceIdentifier;

      var payload = JsonSerializer.Serialize(new
      {
        error,
        message,
        trace_id = ctx.TraceIdentifier,
        exception = includeDetails ? ex.GetType().FullName : null,
        exception_chain = includeDetails ? Flatten(ex).ToArray() : null,
        sql = includeDetails ? TrySql(ex) : null
      }, JsonOptions);

      await ctx.Response.WriteAsync(payload);
    }
  }

  private static (int status, string error, string message) Map(Exception ex, bool includeDetails)
  {
    if (ex is DomainException de)
    {
      if (IsAlreadyExists(de.Message))
        return (StatusCodes.Status409Conflict, "conflict", de.Message);

      return (StatusCodes.Status400BadRequest, "domain_error", de.Message);
    }

    if (ex is DbUpdateException due && IsUniqueViolation(due))
      return (StatusCodes.Status409Conflict, "conflict",
        includeDetails ? (due.InnerException?.Message ?? due.Message) : "Resource already exists.");

    if (IsUniqueViolation(ex))
      return (StatusCodes.Status409Conflict, "conflict",
        includeDetails ? ex.Message : "Resource already exists.");

    return (StatusCodes.Status500InternalServerError, "internal_error",
      includeDetails ? ex.Message : "An unexpected error occurred.");
  }

  private static bool IsAlreadyExists(string message) =>
    message.Contains("already exists", StringComparison.OrdinalIgnoreCase);

  private static SqlException? FindSqlException(Exception ex)
  {
    for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
    {
      if (cur is SqlException sql)
        return sql;
    }
    return null;
  }

  private static bool IsUniqueViolation(Exception ex)
  {
    var sql = FindSqlException(ex);
    return sql is not null && (sql.Number == 2627 || sql.Number == 2601);
  }

  private static IEnumerable<object> Flatten(Exception ex)
  {
    for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
      yield return new { type = cur.GetType().FullName, message = cur.Message };
  }

  private static object? TrySql(Exception ex)
  {
    var sql = FindSqlException(ex);
    return sql is null ? null : new { number = sql.Number, message = sql.Message };
  }
}
