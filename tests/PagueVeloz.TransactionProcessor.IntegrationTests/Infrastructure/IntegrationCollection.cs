using Xunit;

namespace PagueVeloz.TransactionProcessor.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationTestFixture>
{
  public const string Name = "integration";
}
