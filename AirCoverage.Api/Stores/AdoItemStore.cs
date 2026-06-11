using AirCoverage.Api.Abstractions;
using AirCoverage.Api.Ado;
using AirCoverage.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AirCoverage.Api.Stores;

public class AdoItemStore : IItemStore
{
    private static readonly string[] ClosedStatuses = { "Closed", "Resolved" };

    private readonly CacheDbContext _cache;
    private readonly IAzureDevOpsClient _ado;
    private readonly AdoOptions _opt;

    public AdoItemStore(CacheDbContext cache, IAzureDevOpsClient ado, IOptions<AdoOptions> options)
    {
        _cache = cache;
        _ado = ado;
        _opt = options.Value;
    }

    public async Task<IReadOnlyList<ItemDto>> GetItemsAsync(ItemScope scope, CancellationToken ct)
    {
        if (scope == ItemScope.Open)
        {
            var rows = await _cache.Items.AsNoTracking().ToListAsync(ct);
            return rows.Select(ToDto).ToList();
        }

        // Closed/Resolved are not cached: bounded on-demand query.
        var ids = await _ado.QueryClosedIdsAsync(_opt.ClosedWindowDays, ct);
        var work = await _ado.GetWorkItemsAsync(ids, ct);
        return work.Select(ItemMapper.ToDto).ToList();
    }

    public async Task<ItemDto?> GetItemAsync(int id, CancellationToken ct)
    {
        var cached = await _cache.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (cached is not null) return ToDto(cached);
        var wi = await _ado.GetWorkItemAsync(id, ct);
        return wi is null ? null : ItemMapper.ToDto(wi);
    }

    public async Task<ItemDto> CreateAsync(ItemInput input, CancellationToken ct)
    {
        var ops = ItemMapper.ToPatch(input, existingTags: "", _opt.Tag);
        var wi = await _ado.CreateAsync(ops, ct);
        await UpsertCacheAsync(wi, ct);
        return ItemMapper.ToDto(wi);
    }

    public async Task<ItemDto?> UpdateAsync(int id, ItemInput input, CancellationToken ct)
    {
        var existingTags = await ExistingTagsAsync(id, ct);
        if (existingTags is null) return null;
        var ops = ItemMapper.ToPatch(input, existingTags, _opt.Tag);
        var wi = await _ado.UpdateAsync(id, ops, ct);
        await UpsertCacheAsync(wi, ct);
        return ItemMapper.ToDto(wi);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var existingTags = await ExistingTagsAsync(id, ct);
        if (existingTags is null) return false;

        var keptTags = ItemMapper.ParseTags(existingTags)
            .Where(t => !t.Equals(_opt.Tag, StringComparison.OrdinalIgnoreCase));
        var ops = new List<JsonPatchOperation>
        {
            new("add", "/fields/System.Tags", string.Join("; ", keptTags)),
        };
        await _ado.UpdateAsync(id, ops, ct);
        await EvictAsync(id, ct);
        return true;
    }

    private async Task<string?> ExistingTagsAsync(int id, CancellationToken ct)
    {
        var cached = await _cache.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (cached is not null) return cached.Tags;
        var wi = await _ado.GetWorkItemAsync(id, ct);
        return wi?.Tags;
    }

    private async Task UpsertCacheAsync(AdoWorkItem wi, CancellationToken ct)
    {
        if (ClosedStatuses.Contains(wi.State)) { await EvictAsync(wi.Id, ct); return; }

        var dto = ItemMapper.ToDto(wi);
        var row = await _cache.Items.FirstOrDefaultAsync(i => i.Id == wi.Id, ct);
        if (row is null) { row = new CachedItem { Id = wi.Id }; _cache.Items.Add(row); }
        row.Title = dto.Title; row.Description = dto.Description; row.Priority = dto.Priority;
        row.Status = dto.Status; row.RequestedBy = dto.RequestedBy; row.Assignee = dto.Assignee;
        row.TicketType = dto.TicketType; row.TicketRef = dto.TicketRef; row.Tags = wi.Tags;
        row.Url = dto.Url; row.Received = dto.Received; row.Updated = dto.Updated;
        await _cache.SaveChangesAsync(ct);
    }

    private async Task EvictAsync(int id, CancellationToken ct)
    {
        var row = await _cache.Items.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (row is not null) { _cache.Items.Remove(row); await _cache.SaveChangesAsync(ct); }
    }

    private static ItemDto ToDto(CachedItem c) => new(
        c.Id, c.Id.ToString(), c.Title, c.Description, c.Priority, c.Status,
        c.RequestedBy, c.Assignee, c.TicketType, c.TicketRef, c.Url, c.Received, c.Updated);
}
