using AirCoverage.Api.Ado;
using AirCoverage.Api.Data;
using AirCoverage.Api.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AirCoverage.Api.Sync;

/// <summary>Timer-free cache sync logic (unit-testable).</summary>
public class CacheSynchronizer
{
    private readonly CacheDbContext _cache;
    private readonly IAzureDevOpsClient _ado;
    private readonly AdoOptions _opt;

    public CacheSynchronizer(CacheDbContext cache, IAzureDevOpsClient ado, IOptions<AdoOptions> options)
    {
        _cache = cache; _ado = ado; _opt = options.Value;
    }

    /// <summary>
    /// Full membership sync: fetches the current member set (tagged OR descendant of the
    /// parent epic), upserts the open members into the cache, and prunes every cache row
    /// that is no longer an open member (closed, de-tagged, or left the subtree).
    /// </summary>
    public async Task SyncAsync(CancellationToken ct)
    {
        var memberIds = await _ado.QueryMemberIdsAsync(ct);
        var work = await _ado.GetWorkItemsAsync(memberIds, ct);

        foreach (var wi in work) await ApplyAsync(wi, ct);

        // The authoritative set of rows the cache should retain: members that are still open.
        var openMemberIds = work
            .Where(w => !AdoItemStore.ClosedStatuses.Contains(w.State, StringComparer.OrdinalIgnoreCase))
            .Select(w => w.Id)
            .ToHashSet();

        var stale = await _cache.Items
            .Where(i => !openMemberIds.Contains(i.Id))
            .ToListAsync(ct);
        _cache.Items.RemoveRange(stale);

        var state = await GetStateAsync(ct);
        state.LastSuccessfulSync = DateTime.UtcNow;
        await _cache.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(AdoWorkItem wi, CancellationToken ct)
    {
        var existing = await _cache.Items.FirstOrDefaultAsync(i => i.Id == wi.Id, ct);
        if (AdoItemStore.ClosedStatuses.Contains(wi.State, StringComparer.OrdinalIgnoreCase))
        {
            if (existing is not null) _cache.Items.Remove(existing);
            return;
        }
        var dto = ItemMapper.ToDto(wi);
        if (existing is null) { existing = new CachedItem { Id = wi.Id }; _cache.Items.Add(existing); }
        existing.Title = dto.Title; existing.Description = dto.Description; existing.Priority = dto.Priority;
        existing.Status = dto.Status; existing.RequestedBy = dto.RequestedBy; existing.Assignee = dto.Assignee;
        existing.TicketType = dto.TicketType; existing.TicketRef = dto.TicketRef; existing.Tags = wi.Tags;
        existing.Url = dto.Url; existing.Received = dto.Received; existing.Updated = dto.Updated;
    }

    private async Task<SyncState> GetStateAsync(CancellationToken ct)
    {
        var state = await _cache.SyncState.FirstOrDefaultAsync(s => s.Id == 1, ct);
        if (state is null) { state = new SyncState { Id = 1 }; _cache.SyncState.Add(state); }
        return state;
    }
}
