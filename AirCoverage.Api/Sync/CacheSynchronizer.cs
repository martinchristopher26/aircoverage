using AirCoverage.Api.Ado;
using AirCoverage.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AirCoverage.Api.Sync;

/// <summary>Timer-free cache sync logic (unit-testable).</summary>
public class CacheSynchronizer
{
    private static readonly string[] Closed = { "Closed", "Resolved" };
    private readonly CacheDbContext _cache;
    private readonly IAzureDevOpsClient _ado;
    private readonly AdoOptions _opt;

    public CacheSynchronizer(CacheDbContext cache, IAzureDevOpsClient ado, IOptions<AdoOptions> options)
    {
        _cache = cache; _ado = ado; _opt = options.Value;
    }

    public async Task DeltaAsync(CancellationToken ct)
    {
        var state = await GetStateAsync(ct);
        var since = state.LastChangedWatermark ?? DateTime.UtcNow.AddDays(-_opt.ClosedWindowDays);
        var ids = await _ado.QueryIdsChangedSinceAsync(since, ct);
        var work = await _ado.GetWorkItemsAsync(ids, ct);
        foreach (var wi in work) await ApplyAsync(wi, ct);

        var maxChanged = work.Select(w => w.ChangedDate ?? DateTime.MinValue).DefaultIfEmpty(since).Max();
        state.LastChangedWatermark = maxChanged > since ? maxChanged : since;
        state.LastSuccessfulSync = DateTime.UtcNow;
        await _cache.SaveChangesAsync(ct);
    }

    public async Task ReconcileAsync(CancellationToken ct)
    {
        var openIds = (await _ado.QueryOpenIdsAsync(ct)).ToHashSet();
        var work = await _ado.GetWorkItemsAsync(openIds.ToList(), ct);
        foreach (var wi in work) await ApplyAsync(wi, ct);

        var orphans = await _cache.Items.Where(i => !openIds.Contains(i.Id)).ToListAsync(ct);
        _cache.Items.RemoveRange(orphans);

        var state = await GetStateAsync(ct);
        state.LastSuccessfulSync = DateTime.UtcNow;
        await _cache.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(AdoWorkItem wi, CancellationToken ct)
    {
        var existing = await _cache.Items.FirstOrDefaultAsync(i => i.Id == wi.Id, ct);
        if (Closed.Contains(wi.State))
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
