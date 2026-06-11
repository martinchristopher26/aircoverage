using AirCoverage.Api.Data;
using AirCoverage.Api.Dtos;
using AirCoverage.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AirCoverage.Api.Endpoints;

public static class ItemsEndpoints
{
    public static IEndpointRouteBuilder MapItemsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/items").RequireAuthorization();

        // List — default "most urgent -> least urgent": priority rank, then oldest first.
        // Optional filters mirror the documented API surface (?status=&priority=&q=).
        group.MapGet("/", async (AppDbContext db, string? status, string? priority, string? q) =>
        {
            IQueryable<Item> query = db.Items;

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(i => i.Status == status);
            if (!string.IsNullOrWhiteSpace(priority))
                query = query.Where(i => i.Priority == priority);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(i =>
                    i.Title.ToLower().Contains(term) ||
                    i.Number.ToLower().Contains(term) ||
                    (i.RequestedBy != null && i.RequestedBy.ToLower().Contains(term)) ||
                    (i.Assignee != null && i.Assignee.ToLower().Contains(term)) ||
                    i.Description.ToLower().Contains(term));
            }

            var items = await query
                .OrderBy(i => i.Priority == "Critical" ? 0
                            : i.Priority == "High" ? 1
                            : i.Priority == "Medium" ? 2 : 3)
                .ThenBy(i => i.Received)
                .ToListAsync();

            return Results.Ok(items);
        });

        group.MapGet("/{id:int}", async (int id, AppDbContext db) =>
            await db.Items.FindAsync(id) is { } item ? Results.Ok(item) : Results.NotFound());

        group.MapPost("/", async (ItemInput input, AppDbContext db) =>
        {
            var error = Validate(input);
            if (error is not null) return Results.BadRequest(new { error });

            var now = DateTime.UtcNow;
            var item = new Item
            {
                Number = await NextNumberAsync(db),
                Title = input.Title.Trim(),
                Description = input.Description ?? "",
                Priority = input.Priority,
                Status = input.Status,
                RequestedBy = NullIfBlank(input.RequestedBy),
                Assignee = NullIfBlank(input.Assignee),
                TicketType = NullIfBlank(input.TicketType),
                TicketRef = NullIfBlank(input.TicketRef),
                Received = now,
                Updated = now,
            };

            db.Items.Add(item);
            await db.SaveChangesAsync();
            return Results.Created($"/api/items/{item.Id}", item);
        });

        group.MapPut("/{id:int}", async (int id, ItemInput input, AppDbContext db) =>
        {
            var item = await db.Items.FindAsync(id);
            if (item is null) return Results.NotFound();

            var error = Validate(input);
            if (error is not null) return Results.BadRequest(new { error });

            item.Title = input.Title.Trim();
            item.Description = input.Description ?? "";
            item.Priority = input.Priority;
            item.Status = input.Status;
            item.RequestedBy = NullIfBlank(input.RequestedBy);
            item.Assignee = NullIfBlank(input.Assignee);
            item.TicketType = NullIfBlank(input.TicketType);
            item.TicketRef = NullIfBlank(input.TicketRef);
            item.Updated = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return Results.Ok(item);
        });

        group.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var item = await db.Items.FindAsync(id);
            if (item is null) return Results.NotFound();
            db.Items.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return app;
    }

    private static readonly string[] ValidPriorities = { "Critical", "High", "Medium", "Low" };
    private static readonly string[] ValidStatuses = { "New", "In Progress", "Waiting", "Resolved", "Closed" };

    private static string? Validate(ItemInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title)) return "Title is required.";
        if (!ValidPriorities.Contains(input.Priority)) return "Invalid priority.";
        if (!ValidStatuses.Contains(input.Status)) return "Invalid status.";
        return null;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Generates the next AC-#### display id from the current max (small table; in-memory parse).
    private static async Task<string> NextNumberAsync(AppDbContext db)
    {
        var numbers = await db.Items.Select(i => i.Number).ToListAsync();
        var max = numbers
            .Select(n => int.TryParse(new string(n.Where(char.IsDigit).ToArray()), out var v) ? v : 0)
            .DefaultIfEmpty(1042)
            .Max();
        return $"AC-{max + 1}";
    }
}
