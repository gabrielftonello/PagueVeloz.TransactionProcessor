namespace PagueVeloz.TransactionProcessor.Application.Contracts.Accounts;

/// <summary>
/// Estado atual de uma conta.
/// </summary>
/// <param name="AccountId">Identificador da conta.</param>
/// <param name="ClientId">Identificador do cliente.</param>
/// <param name="Currency">Moeda da conta.</param>
/// <param name="Balance">Saldo total.</param>
/// <param name="ReservedBalance">Saldo reservado.</param>
/// <param name="AvailableBalance">Saldo disponível para uso (balance - reserved).</param>
/// <param name="CreditLimit">Limite de crédito.</param>
/// <param name="Status">Status: active, inactive, blocked.</param>
public sealed record AccountResponse(
  string AccountId,
  string ClientId,
  string Currency,
  long Balance,
  long ReservedBalance,
  long AvailableBalance,
  long CreditLimit,
  string Status
);
