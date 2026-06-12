using AirCoverage.Api.Ado;
using AirCoverage.Api.Data;
using AirCoverage.Api.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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

    private static CacheSynchronizer NewSync(CacheDbContext cache, IAzureDevOpsClient client, AdoOptions? opt = null) =>
        new(cache, client, Options.Create(opt ?? new AdoOptions()), NullLogger<CacheSynchronizer>.Instance);

    private static AdoWorkItem Wi(int id, string state, string workItemType = "Bug") => new(
        id, "T", "d", 2, state, null, "AirCoverage", $"https://x/{id}", workItemType,
        new DateTime(2026, 1, 1), new DateTime(2026, 1, 2));

    [Fact]
    public async Task SyncAsync_upserts_open_members_and_prunes_closed()
    {
        var cache = NewCache();
        // Old write marker so the (closed) non-member row is past the grace window and gets pruned.
        cache.Items.Add(new CachedItem { Id = 1, Title = "old", Status = "New", CacheWrittenAt = DateTime.UtcNow.AddMinutes(-10) });
        await cache.SaveChangesAsync();

        var client = Substitute.For<IAzureDevOpsClient>();
        client.QueryMemberIdsAsync(Arg.Any<CancellationToken>()).Returns(new[] { 1, 2 });
        client.GetWorkItemsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Wi(1, "Closed"), Wi(2, "Active") });

        var sync = NewSync(cache, client);
        await sync.SyncAsync(CancellationToken.None);

        Assert.Null(await cache.Items.FindAsync(1)); // closed member -> not stored
        Assert.NotNull(await cache.Items.FindAsync(2)); // open member -> upserted
        Assert.NotNull((await cache.SyncState.SingleAsync()).LastSuccessfulSync);
    }

    [Fact]
    public async Task SyncAsync_drops_cache_row_that_is_no_longer_a_member()
    {
        var cache = NewCache();
        // Old write markers so both non-member rows are past the grace window.
        cache.Items.Add(new CachedItem { Id = 1, Status = "New", CacheWrittenAt = DateTime.UtcNow.AddMinutes(-10) }); // lost the tag / left the subtree
        cache.Items.Add(new CachedItem { Id = 2, Status = "New", CacheWrittenAt = DateTime.UtcNow.AddMinutes(-10) });
        await cache.SaveChangesAsync();

        var client = Substitute.For<IAzureDevOpsClient>();
        // 1 is no longer a member; only 2 is returned by the membership query.
        client.QueryMemberIdsAsync(Arg.Any<CancellationToken>()).Returns(new[] { 2 });
        client.GetWorkItemsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Wi(2, "Active") });

        var sync = NewSync(cache, client);
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

        var sync = NewSync(cache, client);
        await sync.SyncAsync(CancellationToken.None);

        Assert.NotNull((await cache.SyncState.SingleAsync()).LastSuccessfulSync);
    }

    [Fact]
    public async Task SyncAsync_protects_freshly_written_non_member_but_prunes_old_one()
    {
        // Regression for the WIQL eventual-consistency prune race: a row written via
        // write-through moments ago is absent from the membership query, but must NOT be
        // pruned. An equally-non-member row written long ago is still pruned.
        var cache = NewCache();
        cache.Items.Add(new CachedItem { Id = 1, Status = "New", CacheWrittenAt = DateTime.UtcNow });               // fresh: protected
        cache.Items.Add(new CachedItem { Id = 2, Status = "New", CacheWrittenAt = DateTime.UtcNow.AddMinutes(-10) }); // old: dropped
        await cache.SaveChangesAsync();

        var client = Substitute.For<IAzureDevOpsClient>();
        // Neither 1 nor 2 is reported as a member; only 3 (an unrelated open member) is.
        client.QueryMemberIdsAsync(Arg.Any<CancellationToken>()).Returns(new[] { 3 });
        client.GetWorkItemsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Wi(3, "Active") });

        var sync = NewSync(cache, client);
        await sync.SyncAsync(CancellationToken.None);

        Assert.NotNull(await cache.Items.FindAsync(1)); // freshly written -> survives the prune
        Assert.Null(await cache.Items.FindAsync(2));     // written long ago -> genuinely stale, pruned
        Assert.NotNull(await cache.Items.FindAsync(3));  // open member -> present
    }

    [Fact]
    public async Task SyncAsync_caches_only_included_types_and_prunes_disallowed()
    {
        // The queue is restricted to IncludedTypes (User Story, Bug). Members of other
        // types (Epic, Feature, Task, …) are never cached; a previously-cached
        // disallowed-type row is pruned on the next resync.
        var cache = NewCache();
        // A row cached earlier that is now a disallowed type (e.g. it was retyped to Feature).
        cache.Items.Add(new CachedItem { Id = 4, Status = "New", CacheWrittenAt = DateTime.UtcNow.AddMinutes(-10) });
        await cache.SaveChangesAsync();

        var client = Substitute.For<IAzureDevOpsClient>();
        client.QueryMemberIdsAsync(Arg.Any<CancellationToken>()).Returns(new[] { 1, 2, 3, 4 });
        client.GetWorkItemsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                Wi(1, "Active", "User Story"), // included
                Wi(2, "Active", "Bug"),        // included
                Wi(3, "Active", "Epic"),       // excluded type -> not cached
                Wi(4, "Active", "Feature"),    // excluded type -> not cached + prune the stale row
            });

        var sync = NewSync(cache, client);
        await sync.SyncAsync(CancellationToken.None);

        Assert.NotNull(await cache.Items.FindAsync(1)); // User Story cached
        Assert.NotNull(await cache.Items.FindAsync(2)); // Bug cached
        Assert.Null(await cache.Items.FindAsync(3));     // Epic not cached
        Assert.Null(await cache.Items.FindAsync(4));     // Feature not cached AND previously-cached row pruned
        Assert.Equal(2, await cache.Items.CountAsync());
    }

    [Fact]
    public async Task SyncAsync_skips_prune_when_membership_empty_but_cache_populated()
    {
        // A transient/degraded ADO response (200 with no members) must not wipe a
        // populated cache. The rows survive and LastSuccessfulSync is still recorded.
        var cache = NewCache();
        cache.Items.Add(new CachedItem { Id = 1, Status = "New", CacheWrittenAt = DateTime.UtcNow.AddMinutes(-10) });
        cache.Items.Add(new CachedItem { Id = 2, Status = "New", CacheWrittenAt = DateTime.UtcNow.AddMinutes(-10) });
        await cache.SaveChangesAsync();

        var client = Substitute.For<IAzureDevOpsClient>();
        client.QueryMemberIdsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<int>());
        client.GetWorkItemsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AdoWorkItem>());

        var sync = NewSync(cache, client);
        await sync.SyncAsync(CancellationToken.None);

        Assert.NotNull(await cache.Items.FindAsync(1)); // not pruned despite being absent from the empty member set
        Assert.NotNull(await cache.Items.FindAsync(2));
        Assert.Equal(2, await cache.Items.CountAsync());
        Assert.NotNull((await cache.SyncState.SingleAsync()).LastSuccessfulSync);
    }
}
