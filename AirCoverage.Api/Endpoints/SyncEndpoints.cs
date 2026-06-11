using AirCoverage.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AirCoverage.Api.Endpoints;

public static class SyncEndpoints
{
    public static IEndpointRouteBuilder MapSyncApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sync/status", async (CacheDbContext cache, CancellationToken ct) =>
        {
            var state = await cache.SyncState.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, ct);
            return Results.Ok(new { lastSync = state?.LastSuccessfulSync });
        }).RequireAuthorization();
        return app;
    }
}
