using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PagueVeloz.TransactionProcessor.Application.Abstractions;
using PagueVeloz.TransactionProcessor.Application.Contracts.Accounts;
using PagueVeloz.TransactionProcessor.Domain;
using PagueVeloz.TransactionProcessor.Domain.Accounts;

namespace PagueVeloz.TransactionProcessor.Application.Commands.CreateAccount;

public sealed class CreateAccountHandler : IRequestHandler<CreateAccountCommand, AccountResponse>
{
  private readonly IAccountRepository _accounts;
  private readonly IUnitOfWork _uow;

  public CreateAccountHandler(IAccountRepository accounts, IUnitOfWork uow)
  {
    _accounts = accounts;
    _uow = uow;
  }

  public async Task<AccountResponse> Handle(CreateAccountCommand request, CancellationToken ct)
  {
    var req = request.Request;

    var accountId = string.IsNullOrWhiteSpace(req.AccountId)
      ? $"ACC-{Guid.NewGuid():N}"[..12].ToUpperInvariant()
      : req.AccountId!.Trim();

    var existing = await _accounts.GetAsync(accountId, ct);
    if (existing is not null)
      throw new DomainException($"Account '{accountId}' already exists.");

    var account = new Account(
      accountId: accountId,
      clientId: req.ClientId.Trim(),
      currency: req.Currency.Trim().ToUpperInvariant(),
      balance: req.InitialBalance,
      reservedBalance: 0,
      creditLimit: req.CreditLimit,
      status: AccountStatus.Active,
      ledgerSequence: 0
    );

    await _accounts.AddAsync(account, ct);

    try
    {
      await _uow.SaveChangesAsync(ct);
    }
    catch (DbUpdateException ex) when (IsUniqueViolation(ex))
    {
      throw new DomainException($"Account '{accountId}' already exists.");
    }

    return new AccountResponse(
      AccountId: account.AccountId,
      ClientId: account.ClientId,
      Currency: account.Currency,
      Balance: account.Balance,
      ReservedBalance: account.ReservedBalance,
      AvailableBalance: account.AvailableBalance,
      CreditLimit: account.CreditLimit,
      Status: account.Status.ToString().ToLowerInvariant()
    );
  }

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
}
