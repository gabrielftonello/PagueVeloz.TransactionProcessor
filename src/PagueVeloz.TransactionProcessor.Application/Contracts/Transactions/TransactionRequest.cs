namespace PagueVeloz.TransactionProcessor.Application.Contracts.Transactions;

/// <summary>
/// Request de operação financeira.
/// </summary>
/// <param name="Operation">Tipo de operação: credit, debit, reserve, capture, reversal, transfer.</param>
/// <param name="AccountId">Identificador da conta origem (ou única conta, para operações não-transfer).</param>
/// <param name="Amount">Valor em centavos (inteiro).</param>
/// <param name="Currency">Moeda (ex: BRL).</param>
/// <param name="ReferenceId">Identificador único para idempotência.</param>
/// <param name="Metadata">Metadados opcionais (campos livres).</param>
/// <param name="TargetAccountId">Conta destino (apenas para transfer).</param>
/// <param name="RelatedReferenceId">Referência de uma transação anterior (apenas para reversal/capture quando aplicável).</param>
public sealed record TransactionRequest(
  string Operation,
  string AccountId,
  long Amount,
  string Currency,
  string ReferenceId,
  Dictionary<string, object?>? Metadata,
  string? TargetAccountId,
  string? RelatedReferenceId
);
