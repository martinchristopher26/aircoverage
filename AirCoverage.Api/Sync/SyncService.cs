using AirCoverage.Api.Data;
using Microsoft.Extensions.Options;
using AirCoverage.Api.Ado;

namespace AirCoverage.Api.Sync;

public class SyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly AdoOptions _opt;
    private readonly ILogger<SyncService> _log;

    public SyncService(IServiceScopeFactory scopes, IOptions<AdoOptions> options, ILogger<SyncService> log)
    {
        _scopes = scopes; _opt = options.Value; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastReconcile = DateTime.MinValue;
        // Initial full reconcile so the cache is warm at startup.
        await RunReconcileAsync(stoppingToken);
        lastReconcile = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_opt.PollSeconds), stoppingToken);
                using var scope = _scopes.CreateScope();
                var sync = scope.ServiceProvider.GetRequiredService<CacheSynchronizer>();
                await sync.DeltaAsync(stoppingToken);

                if ((DateTime.UtcNow - lastReconcile).TotalSeconds >= _opt.ReconcileSeconds)
                {
                    await RunReconcileAsync(stoppingToken);
                    lastReconcile = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "Cache sync iteration failed; will retry."); }
        }
    }

    private async Task RunReconcileAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<CacheSynchronizer>();
            await sync.ReconcileAsync(ct);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Cache reconcile failed; serving existing cache."); }
    }
}
