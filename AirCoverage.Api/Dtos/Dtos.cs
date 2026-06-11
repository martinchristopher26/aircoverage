namespace AirCoverage.Api.Dtos;

/// <summary>Editable fields accepted on create/update. The server owns Id, Number,
/// Received and Updated.</summary>
public record ItemInput(
    string Title,
    string? Description,
    string Priority,
    string Status,
    string? RequestedBy,
    string? Assignee,
    string? TicketType,
    string? TicketRef
);

public record LoginRequest(string Username, string Password);

public record UserResponse(string Username, string DisplayName);
