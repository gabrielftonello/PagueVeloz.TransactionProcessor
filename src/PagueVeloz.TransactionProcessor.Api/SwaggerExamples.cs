using Swashbuckle.AspNetCore.Filters;
using PagueVeloz.TransactionProcessor.Application.Contracts.Accounts;
using PagueVeloz.TransactionProcessor.Application.Contracts.Transactions;

public sealed class CreateAccountRequestExamples : IExamplesProvider<CreateAccountRequest>
{
  public CreateAccountRequest GetExamples() => new(
    ClientId: "CLI-DEMO",
    AccountId: "ACC-DEMO-001",
    InitialBalance: 10000,
    CreditLimit: 0,
    Currency: "BRL"
  );
}

public sealed class TransactionRequestExamples : IExamplesProvider<TransactionRequest>
{
  public TransactionRequest GetExamples() => new(
    Operation: "credit",
    AccountId: "ACC-DEMO-001",
    TargetAccountId: null,
    Amount: 1000,
    Currency: "BRL",
    ReferenceId: "TXN-DEMO-001",
    RelatedReferenceId: null,
    Metadata: new Dictionary<string, object?> { ["source"] = "swagger" }
  );
}

public sealed class TransactionResponseExamples : IExamplesProvider<TransactionResponse>
{
  public TransactionResponse GetExamples() => new(
    TransactionId: "TXN-DEMO-001",
    Status: "success",
    Balance: 11000,
    ReservedBalance: 0,
    AvailableBalance: 11000,
    Timestamp: DateTime.UtcNow.ToString("O"),
    ErrorMessage: null
  );
}

public sealed class AccountResponseExamples : IExamplesProvider<AccountResponse>
{
  public AccountResponse GetExamples() => new(
    ClientId: "CLI-DEMO",
    AccountId: "ACC-DEMO-001",
    Currency: "BRL",
    Balance: 11000,
    ReservedBalance: 0,
    AvailableBalance: 11000,
    CreditLimit: 0,
    Status: "active"
  );
}
