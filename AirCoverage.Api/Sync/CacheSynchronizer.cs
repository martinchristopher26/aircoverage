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

    public async Task DeltaAsync(CancellationToken ct)
    {
        // Capture the query-start instant BEFORE querying so the next delta resumes
        // from here regardless of how long this run takes.
        var startedAt = DateTime.UtcNow;
        var state = await GetStateAsync(ct);

        // Floor: resume from the last watermark, or seed an initial window on first run.
        var floor = state.LastChangedWatermark ?? startedAt.AddDays(-_opt.ClosedWindowDays);
        // Overlap the previous boundary to cover ADO indexing lag / clock skew.
        var since = floor - TimeSpan.FromSeconds(_opt.WatermarkOverlapSeconds);

        var ids = await _ado.QueryIdsChangedSinceAsync(since, ct);
        var work = await _ado.GetWorkItemsAsync(ids, ct);
        foreach (var wi in work) await ApplyAsync(wi, ct);

        // Advance to the query-start instant (monotonic); the overlap on the next
        // query covers anything committed-but-not-yet-indexed at this point.
        state.LastChangedWatermark = startedAt;
        state.LastSuccessfulSync = DateTime.UtcNow;
        await _cache.SaveChangesAsync(ct);
    }

    public async Task ReconcileAsync(CancellationToken ct)
    {
        // Snapshot instant BEFORE the open-ids query so concurrent creates/updates
        // landing after this point are not treated as orphans.
        var startedAt = DateTime.UtcNow;

        var openIds = (await _ado.QueryOpenIdsAsync(ct)).ToHashSet();
        var work = await _ado.GetWorkItemsAsync(openIds.ToList(), ct);
        foreach (var wi in work) await ApplyAsync(wi, ct);

        // Drop only rows that are both absent from the open set AND untouched since the
        // snapshot. A row written concurrently after startedAt must survive.
        var orphans = await _cache.Items
            .Where(i => !openIds.Contains(i.Id) && (i.Updated == null || i.Updated < startedAt))
            .ToListAsync(ct);
        _cache.Items.RemoveRange(orphans);

        var state = await GetStateAsync(ct);
        // Seed the watermark from the reconcile point so the first delta after startup
        // continues from here instead of re-deriving a ClosedWindowDays window.
        state.LastChangedWatermark = startedAt;
        state.LastSuccessfulSync = DateTime.UtcNow;
        await _cache.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(AdoWorkItem wi, CancellationToken ct)
    {
        var existing = await _cache.Items.FirstOrDefaultAsync(i => i.Id == wi.Id, ct);
        if (AdoItemStore.ClosedStatuses.Contains(wi.State))
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
