using System.Text.Json;
using MediatR;
using PagueVeloz.TransactionProcessor.Application.Abstractions;
using PagueVeloz.TransactionProcessor.Application.Contracts.Transactions;

namespace PagueVeloz.TransactionProcessor.Application.Commands.EnqueueTransaction;

public sealed class EnqueueTransactionHandler : IRequestHandler<EnqueueTransactionCommand, TransactionResponse>
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  private readonly ICommandQueue _queue;
  private readonly IClock _clock;
  private readonly IUnitOfWork _uow;

  public EnqueueTransactionHandler(ICommandQueue queue, IClock clock, IUnitOfWork uow)
  {
    _queue = queue;
    _clock = clock;
    _uow = uow;
  }

  public async Task<TransactionResponse> Handle(EnqueueTransactionCommand request, CancellationToken ct)
  {
    var cmdId = Guid.NewGuid();
    var payload = JsonSerializer.Serialize(request.Request, JsonOptions);

    await _queue.EnqueueAsync(new QueuedTransactionCommand(cmdId, payload, _clock.UtcNow), ct);

    await _uow.SaveChangesAsync(ct);

    return new TransactionResponse(
      TransactionId: $"{request.Request.ReferenceId}-PENDING",
      Status: "pending",
      Balance: 0,
      ReservedBalance: 0,
      AvailableBalance: 0,
      Timestamp: _clock.UtcNow.UtcDateTime.ToString("O"),
      ErrorMessage: null
    );
  }
}
