namespace AirCoverage.Api.Abstractions;

/// <summary>The shape the SPA consumes. id is the ADO work-item id.</summary>
public record ItemDto(
    int Id,
    string Number,
    string Title,
    string Description,
    string Priority,
    string Status,
    string RequestedBy,
    string Assignee,
    string TicketType,
    string TicketRef,
    string Url,
    DateTime Received,
    DateTime? Updated);

/// <summary>Editable fields accepted on create/update.</summary>
public record ItemInput(
    string Title,
    string? Description,
    string Priority,
    string Status,
    string? RequestedBy,
    string? Assignee,
    string? TicketType,
    string? TicketRef);

public enum ItemScope { Open, Closed }

public interface IItemStore
{
    Task<IReadOnlyList<ItemDto>> GetItemsAsync(ItemScope scope, CancellationToken ct);
    Task<ItemDto?> GetItemAsync(int id, CancellationToken ct);
    Task<ItemDto> CreateAsync(ItemInput input, CancellationToken ct);
    Task<ItemDto?> UpdateAsync(int id, ItemInput input, CancellationToken ct);
    Task<bool> DeleteAsync(int id, CancellationToken ct);
}
