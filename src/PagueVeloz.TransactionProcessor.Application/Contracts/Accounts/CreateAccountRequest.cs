namespace PagueVeloz.TransactionProcessor.Application.Contracts.Accounts;

/// <summary>
/// Request de criação de conta.
/// </summary>
/// <param name="ClientId">Identificador do cliente (um cliente pode ter N contas).</param>
/// <param name="AccountId">Opcional: permite definir um ID externo (ex: ACC-001). Se omitido, será gerado.</param>
/// <param name="InitialBalance">Saldo inicial em centavos.</param>
/// <param name="CreditLimit">Limite de crédito em centavos.</param>
/// <param name="Currency">Moeda da conta (ex: BRL).</param>
public sealed record CreateAccountRequest(
  string ClientId,
  string? AccountId,
  long InitialBalance,
  long CreditLimit,
  string Currency
);
