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
    public async Task SyncAsync_upserts_open_members_and_prunes_closed()
    {
        var cache = NewCache();
        cache.Items.Add(new CachedItem { Id = 1, Title = "old", Status = "New" });
        await cache.SaveChangesAsync();

        var client = Substitute.For<IAzureDevOpsClient>();
        client.QueryMemberIdsAsync(Arg.Any<CancellationToken>()).Returns(new[] { 1, 2 });
        client.GetWorkItemsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Wi(1, "Closed"), Wi(2, "Active") });

        var sync = new CacheSynchronizer(cache, client, Options.Create(new AdoOptions()));
        await sync.SyncAsync(CancellationToken.None);

        Assert.Null(await cache.Items.FindAsync(1)); // closed member -> not stored
        Assert.NotNull(await cache.Items.FindAsync(2)); // open member -> upserted
        Assert.NotNull((await cache.SyncState.SingleAsync()).LastSuccessfulSync);
    }

    [Fact]
    public async Task SyncAsync_drops_cache_row_that_is_no_longer_a_member()
    {
        var cache = NewCache();
        cache.Items.Add(new CachedItem { Id = 1, Status = "New" }); // lost the tag / left the subtree
        cache.Items.Add(new CachedItem { Id = 2, Status = "New" });
        await cache.SaveChangesAsync();

        var client = Substitute.For<IAzureDevOpsClient>();
        // 1 is no longer a member; only 2 is returned by the membership query.
        client.QueryMemberIdsAsync(Arg.Any<CancellationToken>()).Returns(new[] { 2 });
        client.GetWorkItemsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Wi(2, "Active") });

        var sync = new CacheSynchronizer(cache, client, Options.Create(new AdoOptions()));
        await sync.SyncAsync(CancellationToken.None);

        Assert.Null(await cache.Items.FindAsync(1)); // non-member dropped
        Assert.NotNull(await cache.Items.FindAsync(2));
    }

    [Fact]
    public async Task SyncAsync_sets_last_successful_sync()
    {
        var cache = NewCache();
        var client = Substitute.For<IAzureDevOpsClient>();
        client.QueryMemberIdsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<int>());
        client.GetWorkItemsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AdoWorkItem>());

        var sync = new CacheSynchronizer(cache, client, Options.Create(new AdoOptions()));
        await sync.SyncAsync(CancellationToken.None);

        Assert.NotNull((await cache.SyncState.SingleAsync()).LastSuccessfulSync);
    }
}
