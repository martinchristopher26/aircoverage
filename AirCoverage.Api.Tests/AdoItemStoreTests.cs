using AirCoverage.Api.Abstractions;
using AirCoverage.Api.Ado;
using AirCoverage.Api.Data;
using AirCoverage.Api.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AirCoverage.Api.Tests;

public class AdoItemStoreTests
{
    private static CacheDbContext NewCache()
    {
        var options = new DbContextOptionsBuilder<CacheDbContext>()
            .UseSqlite("Data Source=:memory:").Options;
        var ctx = new CacheDbContext(options);
        ctx.Database.OpenConnection();
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static AdoWorkItem Wi(int id, string state, string tags) => new(
        id, "T", "<div>d</div>", 2, state, "Alex Reyes", tags, $"https://x/{id}",
        new DateTime(2026, 1, 1), new DateTime(2026, 1, 2));

    private static AdoItemStore NewStore(CacheDbContext cache, IAzureDevOpsClient client) =>
        new(cache, client, Options.Create(new AdoOptions { Tag = "AirCoverage" }));

    [Fact]
    public async Task GetItems_open_reads_from_cache()
    {
        var cache = NewCache();
        cache.Items.Add(new CachedItem { Id = 5, Title = "Cached", Priority = "High", Status = "New" });
        await cache.SaveChangesAsync();
        var store = NewStore(cache, Substitute.For<IAzureDevOpsClient>());

        var items = await store.GetItemsAsync(ItemScope.Open, CancellationToken.None);

        Assert.Equal("Cached", Assert.Single(items).Title);
    }

    [Fact]
    public async Task Create_writes_through_to_cache()
    {
        var cache = NewCache();
        var client = Substitute.For<IAzureDevOpsClient>();
        client.CreateAsync(Arg.Any<IReadOnlyList<JsonPatchOperation>>(), Arg.Any<CancellationToken>())
            .Returns(Wi(11, "New", "AirCoverage"));
        var store = NewStore(cache, client);

        var dto = await store.CreateAsync(
            new ItemInput("New item", "d", "High", "New", "Support", "", "", ""), CancellationToken.None);

        Assert.Equal(11, dto.Id);
        Assert.Equal(1, await cache.Items.CountAsync());
    }

    [Fact]
    public async Task Update_to_closed_evicts_from_cache()
    {
        var cache = NewCache();
        cache.Items.Add(new CachedItem { Id = 7, Title = "Open", Status = "New", Tags = "AirCoverage" });
        await cache.SaveChangesAsync();
        var client = Substitute.For<IAzureDevOpsClient>();
        client.UpdateAsync(7, Arg.Any<IReadOnlyList<JsonPatchOperation>>(), Arg.Any<CancellationToken>())
            .Returns(Wi(7, "Closed", "AirCoverage"));
        var store = NewStore(cache, client);

        await store.UpdateAsync(7, new ItemInput("Open", "d", "High", "Closed", "", "", "", ""), CancellationToken.None);

        Assert.Equal(0, await cache.Items.CountAsync());
    }

    [Fact]
    public async Task Delete_detags_in_ado_and_evicts()
    {
        var cache = NewCache();
        cache.Items.Add(new CachedItem { Id = 8, Title = "X", Tags = "AirCoverage; source:Eng" });
        await cache.SaveChangesAsync();
        var client = Substitute.For<IAzureDevOpsClient>();
        client.GetWorkItemAsync(8, Arg.Any<CancellationToken>()).Returns(Wi(8, "New", "AirCoverage; source:Eng"));
        client.UpdateAsync(8, Arg.Any<IReadOnlyList<JsonPatchOperation>>(), Arg.Any<CancellationToken>())
            .Returns(Wi(8, "New", "source:Eng"));
        var store = NewStore(cache, client);

        var ok = await store.DeleteAsync(8, CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(0, await cache.Items.CountAsync());
        await client.Received().UpdateAsync(8,
            Arg.Is<IReadOnlyList<JsonPatchOperation>>(ops =>
                !((string)ops.Single(o => o.path == "/fields/System.Tags").value!).Contains("AirCoverage")),
            Arg.Any<CancellationToken>());
    }
}
