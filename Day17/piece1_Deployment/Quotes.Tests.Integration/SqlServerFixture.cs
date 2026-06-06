using Testcontainers.MsSql;
using Xunit;

namespace Quotes.Tests.Integration;

/// <summary>
/// Starts one SQL Server 2022 container for the entire test suite and tears it down after.
/// Shared via xUnit's collection fixture so all test classes in [Collection("SqlServer")]
/// reuse the same running container instead of spinning up one per class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}

public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    /// <summary>
    /// Master connection string for the running SQL Server instance.
    /// Each IntegrationFixture derives its own per-test-class database from this.
    /// </summary>
    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
