using AirCoverage.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AirCoverage.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Item> Items => Set<Item>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Item>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.Number).IsRequired().HasMaxLength(20);
            e.HasIndex(i => i.Number).IsUnique();
            e.Property(i => i.Title).IsRequired().HasMaxLength(300);
            e.Property(i => i.Priority).IsRequired().HasMaxLength(20);
            e.Property(i => i.Status).IsRequired().HasMaxLength(20);
            e.Property(i => i.RequestedBy).HasMaxLength(120);
            e.Property(i => i.Assignee).HasMaxLength(120);
            e.Property(i => i.TicketType).HasMaxLength(20);
            e.Property(i => i.TicketRef).HasMaxLength(60);
        });
    }
}
