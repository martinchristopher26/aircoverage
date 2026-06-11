using AirCoverage.Api.Abstractions;

namespace AirCoverage.Api.Ado;

public static class ItemMapper
{
    public const string WaitingTag = "Waiting";
    public const string SourcePrefix = "source:";
    public const string CwPrefix = "cw:";

    private static readonly Dictionary<int, string> NumberToPriority =
        new() { [1] = "Critical", [2] = "High", [3] = "Medium", [4] = "Low" };
    private static readonly Dictionary<string, int> PriorityToNumber =
        new() { ["Critical"] = 1, ["High"] = 2, ["Medium"] = 3, ["Low"] = 4 };
    private static readonly Dictionary<string, string> StateToStatus =
        new() { ["New"] = "New", ["Active"] = "In Progress", ["Resolved"] = "Resolved", ["Closed"] = "Closed" };

    public static List<string> ParseTags(string tags) =>
        tags.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public static ItemDto ToDto(AdoWorkItem wi)
    {
        var tags = ParseTags(wi.Tags);
        var status = StateToStatus.GetValueOrDefault(wi.State, "New");
        if (status == "In Progress" && tags.Contains(WaitingTag)) status = "Waiting";

        var source = tags.FirstOrDefault(t => t.StartsWith(SourcePrefix, StringComparison.OrdinalIgnoreCase));
        var cw = tags.FirstOrDefault(t => t.StartsWith(CwPrefix, StringComparison.OrdinalIgnoreCase));

        return new ItemDto(
            Id: wi.Id,
            Number: wi.Id.ToString(),
            Title: wi.Title,
            Description: HtmlText.ToPlainText(wi.Description),
            Priority: wi.Priority is int p && NumberToPriority.TryGetValue(p, out var prio) ? prio : "Low",
            Status: status,
            RequestedBy: source is null ? "" : source[SourcePrefix.Length..].Trim(),
            Assignee: wi.AssignedToDisplayName ?? "",
            TicketType: cw is null ? "" : "ConnectWise",
            TicketRef: cw is null ? "" : cw[CwPrefix.Length..].Trim(),
            Url: wi.Url,
            Received: wi.CreatedDate,
            Updated: wi.ChangedDate);
    }
}
