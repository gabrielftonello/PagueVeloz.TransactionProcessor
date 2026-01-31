namespace PagueVeloz.TransactionProcessor.Application.Contracts.Transactions;

/// <summary>
/// Resultado do processamento de uma transação (sincrona) ou aceitação (assíncrona).
/// </summary>
/// <param name="TransactionId">Identificador da transação processada.</param>
/// <param name="Status">Status: success, failed, pending.</param>
/// <param name="Balance">Saldo total após a operação (pode ficar negativo quando há uso de limite).</param>
/// <param name="ReservedBalance">Saldo reservado após a operação.</param>
/// <param name="AvailableBalance">Saldo disponível após a operação.</param>
/// <param name="Timestamp">Timestamp UTC ISO-8601.</param>
/// <param name="ErrorMessage">Mensagem de erro quando status=failed.</param>
public sealed record TransactionResponse(
  string TransactionId,
  string Status,
  long Balance,
  long ReservedBalance,
  long AvailableBalance,
  string Timestamp,
  string? ErrorMessage
);
