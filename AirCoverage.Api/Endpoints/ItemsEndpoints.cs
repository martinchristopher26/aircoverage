using AirCoverage.Api.Abstractions;

namespace AirCoverage.Api.Endpoints;

public static class ItemsEndpoints
{
    public static IEndpointRouteBuilder MapItemsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/items").RequireAuthorization();

        group.MapGet("/", async (IItemStore store, string? scope, CancellationToken ct) =>
        {
            var itemScope = string.Equals(scope, "closed", StringComparison.OrdinalIgnoreCase)
                ? ItemScope.Closed : ItemScope.Open;
            return Results.Ok(await store.GetItemsAsync(itemScope, ct));
        });

        group.MapGet("/{id:int}", async (int id, IItemStore store, CancellationToken ct) =>
            await store.GetItemAsync(id, ct) is { } dto ? Results.Ok(dto) : Results.NotFound());

        group.MapPost("/", async (ItemInput input, IItemStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.Title)) return Results.BadRequest(new { error = "Title is required." });
            var dto = await store.CreateAsync(input, ct);
            return Results.Created($"/api/items/{dto.Id}", dto);
        });

        group.MapPut("/{id:int}", async (int id, ItemInput input, IItemStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.Title)) return Results.BadRequest(new { error = "Title is required." });
            return await store.UpdateAsync(id, input, ct) is { } dto ? Results.Ok(dto) : Results.NotFound();
        });

        group.MapDelete("/{id:int}", async (int id, IItemStore store, CancellationToken ct) =>
            await store.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        return app;
    }
}
