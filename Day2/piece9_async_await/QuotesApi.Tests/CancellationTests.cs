using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuotesApi.Data;
using QuotesApi.Models;
using Xunit;

namespace QuotesApi.Tests;

// ---------------------------------------------------------------------------
// Factory
// ---------------------------------------------------------------------------

/// <summary>
/// Spins up the real ASP.NET Core host against an isolated temp SQLite file
/// so migrations run normally without touching the dev database.
/// </summary>
internal sealed class ApiFixture : WebApplicationFactory<Program>
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"q-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Override connection string before services are registered so
        // InfrastructureExtensions picks up the temp path.
        builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPath}"
            }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(_dbPath); } catch { /* best-effort cleanup */ }
    }
}

// ---------------------------------------------------------------------------
// Fake repository
// ---------------------------------------------------------------------------

/// <summary>
/// A collection repository that hangs inside the first mutating/reading call
/// until its CancellationToken fires, then propagates the cancellation.
/// This lets tests cancel mid-request and assert the token reaches the repo.
/// </summary>
internal sealed class HangingCollectionRepository : ICollectionRepository
{
    private readonly TaskCompletionSource _serverStarted;
    private readonly Action _onCancelled;

    public HangingCollectionRepository(
        TaskCompletionSource serverStarted,
        Action? onCancelled = null)
    {
        _serverStarted = serverStarted;
        _onCancelled = onCancelled ?? (() => { });
    }

    public async Task<Collection?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        _serverStarted.TrySetResult();
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        }
        catch (OperationCanceledException)
        {
            _onCancelled();
            throw;
        }
    }

    public async Task AddAsync(Collection collection, CancellationToken ct)
    {
        _serverStarted.TrySetResult();
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            _onCancelled();
            throw;
        }
    }

    public Task UpdateAsync(Collection collection, CancellationToken ct) => Task.CompletedTask;
    public Task DeleteAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public class CancellationTests
{
    // Helper — builds a factory that injects the hanging repo so the DB is
    // never hit by the request under test.
    private static WebApplicationFactory<Program> BuildFactory(HangingCollectionRepository repo)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"q-test-{Guid.NewGuid():N}.db");

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.ConfigureAppConfiguration((_, cfg) =>
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = $"Data Source={dbPath}"
                    }));

                b.ConfigureTestServices(services =>
                {
                    // Replace the real scoped repo with our hanging singleton.
                    services.RemoveAll<ICollectionRepository>();
                    services.AddSingleton<ICollectionRepository>(repo);
                });
            });
    }

    // -----------------------------------------------------------------------
    // Test 1 — GET /api/collections/{id}
    // -----------------------------------------------------------------------
    [Fact]
    public async Task GetCollection_WhenClientCancels_ServerTokenIsCancelledAndRequestFails()
    {
        // Arrange
        var serverStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var factory = BuildFactory(
            new HangingCollectionRepository(
                serverStarted,
                onCancelled: () => serverCancelled.TrySetResult()));

        using var client = factory.CreateClient();
        using var cts = new CancellationTokenSource();

        // Act — fire request, wait until server is blocked inside the repo, then cancel
        var requestTask = client.GetAsync($"/api/collections/{Guid.NewGuid()}", cts.Token);

        await serverStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        // Assert (client side) — operation did not complete normally
        await Assert.ThrowsAsync<TaskCanceledException>(() => requestTask);

        // Assert (server side) — CancellationToken propagated all the way to the repository
        // WaitAsync throws if the signal never arrives within the timeout.
        await serverCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    // -----------------------------------------------------------------------
    // Test 2 — POST /api/collections/
    // -----------------------------------------------------------------------
    [Fact]
    public async Task PostCollection_WhenClientCancels_ServerTokenIsCancelledAndRequestFails()
    {
        // Arrange
        var serverStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var factory = BuildFactory(
            new HangingCollectionRepository(
                serverStarted,
                onCancelled: () => serverCancelled.TrySetResult()));

        using var client = factory.CreateClient();
        using var cts = new CancellationTokenSource();

        var body = JsonContent.Create(new { name = "Cancellation Test", ownerId = "user-1" });

        // Act
        var requestTask = client.PostAsync("/api/collections/", body, cts.Token);

        await serverStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        // Assert (client side)
        await Assert.ThrowsAsync<TaskCanceledException>(() => requestTask);

        // Assert (server side)
        await serverCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
