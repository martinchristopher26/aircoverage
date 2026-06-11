using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AirCoverage.Api.Data;

/// <summary>
/// Lets the EF Core CLI tools (dotnet ef migrations ...) construct the context at
/// design time without running the full app/host. Uses a local SQLite file.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=aircoverage.db")
            .Options;
        return new AppDbContext(options);
    }
}
