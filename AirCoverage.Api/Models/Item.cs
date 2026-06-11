namespace AirCoverage.Api.Models;

/// <summary>
/// A single Air Coverage support item. Mirrors the prototype data model, with a
/// surrogate <see cref="Id"/> key separate from the human display id <see cref="Number"/>.
/// </summary>
public class Item
{
    public int Id { get; set; }                 // surrogate key (used by the API)
    public string Number { get; set; } = "";    // "AC-1042" display id
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Priority { get; set; } = "Medium";   // Critical | High | Medium | Low
    public string Status { get; set; } = "New";        // New | In Progress | Waiting | Resolved | Closed
    public string? RequestedBy { get; set; }
    public string? Assignee { get; set; }
    public string? TicketType { get; set; }     // ADO | ConnectWise | null
    public string? TicketRef { get; set; }
    public DateTime Received { get; set; }
    public DateTime? Updated { get; set; }
}
