using Microsoft.EntityFrameworkCore;

namespace AirCoverage.Api.Data;

public class CacheDbContext : DbContext
{
    public CacheDbContext(DbContextOptions<CacheDbContext> options) : base(options) { }

    public DbSet<CachedItem> Items => Set<CachedItem>();
    public DbSet<SyncState> SyncState => Set<SyncState>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<CachedItem>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).ValueGeneratedNever(); // ADO owns the id
        });
        b.Entity<SyncState>().HasKey(s => s.Id);
    }
}
