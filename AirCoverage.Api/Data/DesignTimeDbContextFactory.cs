using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AirCoverage.Api.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CacheDbContext>
{
    public CacheDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CacheDbContext>()
            .UseSqlite("Data Source=aircoverage.db")
            .Options;
        return new CacheDbContext(options);
    }
}
