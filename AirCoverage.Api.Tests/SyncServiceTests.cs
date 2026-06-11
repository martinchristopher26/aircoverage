using AirCoverage.Api.Ado;
using AirCoverage.Api.Data;
using AirCoverage.Api.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AirCoverage.Api.Tests;

public class SyncServiceTests
{
    private static CacheDbContext NewCache()
    {
        var options = new DbContextOptionsBuilder<CacheDbContext>().UseSqlite("Data Source=:memory:").Options;
        var ctx = new CacheDbContext(options);
        ctx.Database.OpenConnection();
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static AdoWorkItem Wi(int id, string state) => new(
        id, "T", "d", 2, state, null, "AirCoverage", $"https://x/{id}", new DateTime(2026, 1, 1), new DateTime(2026, 1, 2));

    [Fact]
    public async Task DeltaAsync_upserts_changed_open_items_and_prunes_closed()
    {
        var cache = NewCache();
        cache.Items.Add(new CachedItem { Id = 1, Title = "old", Status = "New" });
        await cache.SaveChangesAsync();

        var client = Substitute.For<IAzureDevOpsClient>();
        client.QueryIdsChangedSinceAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new[] { 1, 2 });
        client.GetWorkItemsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Wi(1, "Closed"), Wi(2, "Active") });

        var sync = new CacheSynchronizer(cache, client, Options.Create(new AdoOptions()));
        await sync.DeltaAsync(CancellationToken.None);

        Assert.Null(await cache.Items.FindAsync(1)); // closed -> pruned
        Assert.NotNull(await cache.Items.FindAsync(2)); // active -> upserted
        Assert.NotNull((await cache.SyncState.SingleAsync()).LastSuccessfulSync);
    }

    [Fact]
    public async Task ReconcileAsync_drops_orphans_not_in_open_set()
    {
        var cache = NewCache();
        cache.Items.Add(new CachedItem { Id = 1, Status = "New" });
        cache.Items.Add(new CachedItem { Id = 2, Status = "New" });
        await cache.SaveChangesAsync();

        var client = Substitute.For<IAzureDevOpsClient>();
        client.QueryOpenIdsAsync(Arg.Any<CancellationToken>()).Returns(new[] { 2 });
        client.GetWorkItemsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Wi(2, "Active") });

        var sync = new CacheSynchronizer(cache, client, Options.Create(new AdoOptions()));
        await sync.ReconcileAsync(CancellationToken.None);

        Assert.Null(await cache.Items.FindAsync(1)); // orphan dropped
        Assert.NotNull(await cache.Items.FindAsync(2));
    }
}
