using AirCoverage.Api.Ado;
using AirCoverage.Api.Data;
using AirCoverage.Api.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AirCoverage.Api.Sync;

/// <summary>Timer-free cache sync logic (unit-testable).</summary>
public class CacheSynchronizer
{
    private readonly CacheDbContext _cache;
    private readonly IAzureDevOpsClient _ado;
    private readonly AdoOptions _opt;
    private readonly ILogger<CacheSynchronizer> _log;

    public CacheSynchronizer(CacheDbContext cache, IAzureDevOpsClient ado, IOptions<AdoOptions> options,
        ILogger<CacheSynchronizer> log)
    {
        _cache = cache; _ado = ado; _opt = options.Value; _log = log;
    }

    /// <summary>
    /// Full membership sync: fetches the current member set (tagged OR descendant of the
    /// parent epic), upserts the open members into the cache, and prunes every cache row
    /// that is no longer an open member (closed, de-tagged, or left the subtree).
    /// </summary>
    public async Task SyncAsync(CancellationToken ct)
    {
        // Capture before querying ADO so the grace window covers any write-through that
        // landed while the (eventually consistent) membership query was being evaluated.
        var startedAt = DateTime.UtcNow;
        var graceCutoff = startedAt - TimeSpan.FromSeconds(_opt.IndexingGraceSeconds);

        var memberIds = await _ado.QueryMemberIdsAsync(ct);
        var work = await _ado.GetWorkItemsAsync(memberIds, ct);

        // Restrict the queue to the configured work-item types BEFORE upsert/prune, so
        // disallowed types (Epic/Feature/Task/…) are never cached and any previously-cached
        // disallowed row (e.g. an item retyped in ADO) is pruned on this resync.
        var included = work
            .Where(w => _opt.IncludedTypes.Contains(w.WorkItemType, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var wi in included) await ApplyAsync(wi, ct);

        // The authoritative set of rows the cache should retain: included members that are still open.
        var openMemberIds = included
            .Where(w => !AdoItemStore.ClosedStatuses.Contains(w.State, StringComparer.OrdinalIgnoreCase))
            .Select(w => w.Id)
            .ToHashSet();

        // Guard against a transient/degraded ADO response (200 with an empty member set)
        // wiping a populated cache. A legitimately-empty epic also yields an empty cache,
        // so this only triggers on the suspicious populated-cache + empty-response case.
        if (memberIds.Count == 0 && await _cache.Items.AnyAsync(ct))
        {
            _log.LogWarning("Membership query returned 0 items; skipping prune to avoid wiping a populated cache.");
        }
        else
        {
            // Prune rows that are no longer open members, EXCEPT rows written locally
            // (write-through) within the grace window: ADO's WIQL index may not yet
            // reflect a just-created/updated item. Genuinely stale rows (closed, de-tagged,
            // left the subtree) written longer ago are still pruned.
            var stale = await _cache.Items
                .Where(i => !openMemberIds.Contains(i.Id)
                         && (i.CacheWrittenAt == null || i.CacheWrittenAt < graceCutoff))
                .ToListAsync(ct);
            _cache.Items.RemoveRange(stale);
        }

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
        existing.CacheWrittenAt = DateTime.UtcNow; // local write marker (same semantics as write-through)
    }

    private async Task<SyncState> GetStateAsync(CancellationToken ct)
    {
        var state = await _cache.SyncState.FirstOrDefaultAsync(s => s.Id == 1, ct);
        if (state is null) { state = new SyncState { Id = 1 }; _cache.SyncState.Add(state); }
        return state;
    }
}
