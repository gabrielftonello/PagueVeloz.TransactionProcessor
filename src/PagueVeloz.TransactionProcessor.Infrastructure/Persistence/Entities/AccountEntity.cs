namespace PagueVeloz.TransactionProcessor.Infrastructure.Persistence.Entities;

public sealed class AccountEntity
{
  public string AccountId { get; set; } = default!;
  public string ClientId { get; set; } = default!;
  public string Currency { get; set; } = default!;
  public long Balance { get; set; }
  public long ReservedBalance { get; set; }
  public long CreditLimit { get; set; }
  public int Status { get; set; }
  public long LedgerSequence { get; set; }

  public byte[] RowVersion { get; set; } = default!;
}
