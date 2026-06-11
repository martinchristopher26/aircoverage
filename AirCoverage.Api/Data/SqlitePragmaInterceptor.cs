using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AirCoverage.Api.Data;

/// <summary>
/// Applies SQLite PRAGMAs on every connection open so that the user-request
/// CacheDbContext and the background-sync CacheDbContext (separate connections to
/// the same file) cooperate instead of throwing SQLITE_BUSY:
///   * journal_mode=WAL  — persistent per-file mode; readers don't block writers.
///   * busy_timeout=5000  — per-connection; wait up to 5s for a write lock instead
///                          of failing immediately.
/// Running both on every open is correct and harmless (WAL is idempotent; the
/// busy_timeout must be set per connection).
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string Pragmas = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = Pragmas;
        cmd.ExecuteNonQuery();
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = Pragmas;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }
}
