using AirCoverage.Api.Abstractions;

namespace AirCoverage.Api.Ado;

public static class ItemMapper
{
    public const string WaitingTag = "Waiting";
    public const string SourcePrefix = "source:";
    public const string CwPrefix = "cw:";

    private static readonly Dictionary<int, string> NumberToPriority =
        new() { [1] = "Critical", [2] = "High", [3] = "Medium", [4] = "Low" };
    // Fix 2: case-insensitive priority map
    private static readonly Dictionary<string, int> PriorityToNumber =
        new(StringComparer.OrdinalIgnoreCase) { ["Critical"] = 1, ["High"] = 2, ["Medium"] = 3, ["Low"] = 4 };
    // Fix 3: case-insensitive status map
    private static readonly Dictionary<string, string> StateToStatus =
        new(StringComparer.OrdinalIgnoreCase) { ["New"] = "New", ["Active"] = "In Progress", ["Resolved"] = "Resolved", ["Closed"] = "Closed" };

    // Fix 1: null-safe tag parsing
    public static List<string> ParseTags(string? tags) =>
        (tags ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public static ItemDto ToDto(AdoWorkItem wi)
    {
        var tags = ParseTags(wi.Tags);
        var status = StateToStatus.GetValueOrDefault(wi.State, "New");
        // Fix 4: case-insensitive Waiting detection in ToDto
        if (status == "In Progress" && tags.Any(t => t.Equals(WaitingTag, StringComparison.OrdinalIgnoreCase)))
            status = "Waiting";

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

    // Fix 3: case-insensitive status map
    private static readonly Dictionary<string, string> StatusToState =
        new(StringComparer.OrdinalIgnoreCase) { ["New"] = "New", ["In Progress"] = "Active", ["Waiting"] = "Active",
                ["Resolved"] = "Resolved", ["Closed"] = "Closed" };

    /// <summary>
    /// Builds a JSON-Patch document for create or update. <paramref name="existingTags"/>
    /// is the work item's current System.Tags ("" for create); <paramref name="requiredTag"/>
    /// is AdoOptions.Tag, always preserved. Managed tags (Waiting, source:, cw:) are
    /// recomputed from the input; all other existing tags are kept.
    /// </summary>
    public static List<JsonPatchOperation> ToPatch(ItemInput input, string existingTags, string requiredTag)
    {
        var status = input.Status;
        var state = StatusToState.GetValueOrDefault(status, "New");

        // Fix 5: also remove any mis-cased copy of requiredTag from existing tags
        var tags = ParseTags(existingTags)
            .Where(t => !t.Equals(WaitingTag, StringComparison.OrdinalIgnoreCase)
                     && !t.StartsWith(SourcePrefix, StringComparison.OrdinalIgnoreCase)
                     && !t.StartsWith(CwPrefix, StringComparison.OrdinalIgnoreCase)
                     && !t.Equals(requiredTag, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Fix 5: unconditionally add the canonical requiredTag
        tags.Add(requiredTag);
        // Fix 3: case-insensitive Waiting check in ToPatch
        if (string.Equals(input.Status, "Waiting", StringComparison.OrdinalIgnoreCase))
            tags.Add(WaitingTag);
        if (!string.IsNullOrWhiteSpace(input.RequestedBy))
            tags.Add(SourcePrefix + input.RequestedBy.Trim());
        if (string.Equals(input.TicketType, "ConnectWise", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(input.TicketRef))
            tags.Add(CwPrefix + input.TicketRef.Trim());

        // Fix 6: de-dupe tags (canonical requiredTag casing was added first, Distinct preserves first occurrence)
        tags = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var ops = new List<JsonPatchOperation>
        {
            new("add", "/fields/System.Title", input.Title.Trim()),
            new("add", "/fields/System.Description", HtmlText.ToHtml(input.Description)),
            new("add", "/fields/System.State", state),
            new("add", "/fields/Microsoft.VSTS.Common.Priority", PriorityToNumber.GetValueOrDefault(input.Priority, 4)),
            new("add", "/fields/System.Tags", string.Join("; ", tags)),
            new("add", "/fields/System.AssignedTo", input.Assignee ?? ""),
        };
        return ops;
    }
}
