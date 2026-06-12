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
        // Initial sync so the cache is warm at startup.
        await RunSyncAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_opt.PollSeconds), stoppingToken);
                await RunSyncAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "Cache sync iteration failed; will retry."); }
        }
    }

    private async Task RunSyncAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<CacheSynchronizer>();
            await sync.SyncAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { _log.LogWarning(ex, "Cache sync failed; serving existing cache."); }
    }
}
