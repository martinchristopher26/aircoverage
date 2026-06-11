namespace AirCoverage.Api.Data;

/// <summary>A mirror of an open ADO work item. Id IS the ADO work-item id.</summary>
public class CachedItem
{
    public int Id { get; set; }              // ADO work-item id (not generated)
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Priority { get; set; } = "Medium";
    public string Status { get; set; } = "New";
    public string RequestedBy { get; set; } = "";
    public string Assignee { get; set; } = "";
    public string TicketType { get; set; } = "";
    public string TicketRef { get; set; } = "";
    public string Tags { get; set; } = "";   // raw System.Tags, for write read-modify-write
    public string Url { get; set; } = "";
    public DateTime Received { get; set; }
    public DateTime? Updated { get; set; }
}
