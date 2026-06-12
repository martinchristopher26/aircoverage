using AirCoverage.Api.Abstractions;
using AirCoverage.Api.Ado;

namespace AirCoverage.Api.Tests;

public class ItemMapperTests
{
    private static AdoWorkItem Wi(string state, string tags, int? priority = 2) => new(
        Id: 42, Title: "T", Description: "<div>d</div>", Priority: priority, State: state,
        AssignedToDisplayName: "Alex Reyes", Tags: tags, Url: "https://x/42", WorkItemType: "Bug",
        CreatedDate: new DateTime(2026, 1, 1), ChangedDate: new DateTime(2026, 1, 2));

    [Fact]
    public void ToDto_maps_active_to_in_progress()
    {
        var dto = ItemMapper.ToDto(Wi("Active", "AirCoverage"));
        Assert.Equal("In Progress", dto.Status);
        Assert.Equal("High", dto.Priority);
        Assert.Equal("42", dto.Number);
        Assert.Equal("d", dto.Description);
        Assert.Equal("https://x/42", dto.Url);
    }

    [Fact]
    public void ToDto_active_with_waiting_tag_is_waiting()
    {
        var dto = ItemMapper.ToDto(Wi("Active", "AirCoverage; Waiting"));
        Assert.Equal("Waiting", dto.Status);
    }

    [Fact]
    public void ToDto_extracts_source_and_connectwise_tags()
    {
        var dto = ItemMapper.ToDto(Wi("New", "AirCoverage; source:Support; cw:#88204"));
        Assert.Equal("Support", dto.RequestedBy);
        Assert.Equal("ConnectWise", dto.TicketType);
        Assert.Equal("#88204", dto.TicketRef);
    }

    [Fact]
    public void ToDto_no_priority_defaults_to_low()
    {
        var dto = ItemMapper.ToDto(Wi("New", "AirCoverage", priority: null));
        Assert.Equal("Low", dto.Priority);
    }

    private static ItemInput Input(string status = "Waiting", string? source = "Support",
        string? cw = "#1", string assignee = "Alex Reyes") =>
        new("Title", "body\nline2", "High", status, source, assignee, cw is null ? "" : "ConnectWise", cw ?? "");

    [Fact]
    public void ToPatch_sets_state_active_and_waiting_tag_for_waiting()
    {
        var ops = ItemMapper.ToPatch(Input(status: "Waiting"), existingTags: "AirCoverage", "AirCoverage");
        var tags = (string)ops.Single(o => o.path == "/fields/System.Tags").value!;
        Assert.Equal("Active", ops.Single(o => o.path == "/fields/System.State").value);
        Assert.Contains("Waiting", tags);
        Assert.Equal(2, (int)ops.Single(o => o.path == "/fields/Microsoft.VSTS.Common.Priority").value!);
    }

    [Fact]
    public void ToPatch_in_progress_removes_waiting_tag()
    {
        var ops = ItemMapper.ToPatch(Input(status: "In Progress"), existingTags: "AirCoverage; Waiting", "AirCoverage");
        var tags = (string)ops.Single(o => o.path == "/fields/System.Tags").value!;
        Assert.DoesNotContain("Waiting", tags);
        Assert.Equal("Active", ops.Single(o => o.path == "/fields/System.State").value);
    }

    [Fact]
    public void ToPatch_encodes_source_and_cw_tags_and_preserves_required_tag()
    {
        var ops = ItemMapper.ToPatch(Input(source: "Eng", cw: "#9"), existingTags: "AirCoverage; source:Old; cw:#1", "AirCoverage");
        var tags = (string)ops.Single(o => o.path == "/fields/System.Tags").value!;
        Assert.Contains("AirCoverage", tags);
        Assert.Contains("source:Eng", tags);
        Assert.Contains("cw:#9", tags);
        Assert.DoesNotContain("source:Old", tags);
        Assert.DoesNotContain("cw:#1", tags);
    }

    [Fact]
    public void ToPatch_sets_assignedto_and_html_description()
    {
        var ops = ItemMapper.ToPatch(Input(), existingTags: "AirCoverage", "AirCoverage");
        Assert.Equal("Alex Reyes", ops.Single(o => o.path == "/fields/System.AssignedTo").value);
        Assert.Equal("body<br>line2", ops.Single(o => o.path == "/fields/System.Description").value);
    }

    // --- New tests locking the review fixes ---

    // Fix 1: null-safe tag parsing — empty Tags string does not throw
    [Fact]
    public void ToDto_empty_tags_does_not_throw_and_yields_empty_fields()
    {
        var dto = ItemMapper.ToDto(Wi("New", ""));
        Assert.Equal("", dto.RequestedBy);
        Assert.Equal("", dto.TicketType);
        Assert.Equal("", dto.TicketRef);
        Assert.Equal("New", dto.Status);
    }

    // Fix 1: ParseTags is null-safe (signature accepts string?)
    [Fact]
    public void ParseTags_null_returns_empty_list()
    {
        var result = ItemMapper.ParseTags(null!);
        Assert.Empty(result);
    }

    // Fix 2: case-insensitive priority — lowercase "high" maps to 2
    [Fact]
    public void ToPatch_lowercase_priority_maps_correctly()
    {
        var input = new ItemInput("Title", "desc", "high", "New", null, "Alex", "", "");
        var ops = ItemMapper.ToPatch(input, existingTags: "AirCoverage", "AirCoverage");
        Assert.Equal(2, (int)ops.Single(o => o.path == "/fields/Microsoft.VSTS.Common.Priority").value!);
    }

    // Fix 3: case-insensitive status — "waiting" (lowercase) sets state Active
    [Fact]
    public void ToPatch_lowercase_waiting_status_sets_state_active()
    {
        var input = new ItemInput("Title", "desc", "High", "waiting", "Support", "Alex", "ConnectWise", "#1");
        var ops = ItemMapper.ToPatch(input, existingTags: "AirCoverage", "AirCoverage");
        Assert.Equal("Active", ops.Single(o => o.path == "/fields/System.State").value);
        var tags = (string)ops.Single(o => o.path == "/fields/System.Tags").value!;
        Assert.Contains("Waiting", tags);
    }

    // Fix 3 (ToPatch Waiting check): no source tag when RequestedBy is null
    [Fact]
    public void ToPatch_no_source_tag_when_requestedby_is_null()
    {
        var ops = ItemMapper.ToPatch(Input(source: null), existingTags: "AirCoverage", "AirCoverage");
        var tags = (string)ops.Single(o => o.path == "/fields/System.Tags").value!;
        Assert.DoesNotContain("source:", tags);
    }

    // Fix: no cw tag when TicketRef is empty
    [Fact]
    public void ToPatch_no_cw_tag_when_ticketref_is_empty()
    {
        var ops = ItemMapper.ToPatch(Input(cw: null), existingTags: "AirCoverage", "AirCoverage");
        var tags = (string)ops.Single(o => o.path == "/fields/System.Tags").value!;
        Assert.DoesNotContain("cw:", tags);
    }

    // Fix 5: mis-cased requiredTag is replaced by canonical form
    [Fact]
    public void ToPatch_miscased_required_tag_is_replaced_by_canonical()
    {
        var ops = ItemMapper.ToPatch(Input(source: null, cw: null, status: "New"),
            existingTags: "aircoverage; source:Old", requiredTag: "AirCoverage");
        var tags = (string)ops.Single(o => o.path == "/fields/System.Tags").value!;
        // Must contain the canonical casing
        Assert.Contains("AirCoverage", tags);
        // Must not contain the mis-cased copy
        var tagList = tags.Split(';', StringSplitOptions.TrimEntries);
        Assert.DoesNotContain("aircoverage", tagList, StringComparer.Ordinal);
        // Exactly one AirCoverage entry (case-insensitive)
        Assert.Single(tagList, t => t.Equals("AirCoverage", StringComparison.OrdinalIgnoreCase));
    }

    // Fix 4: ToDto treats Active item with lowercase "waiting" tag as status Waiting
    [Fact]
    public void ToDto_active_with_lowercase_waiting_tag_is_waiting()
    {
        var dto = ItemMapper.ToDto(Wi("Active", "AirCoverage; waiting"));
        Assert.Equal("Waiting", dto.Status);
    }

    // Fix 6: duplicate unmanaged tags are de-duped
    [Fact]
    public void ToPatch_duplicate_unmanaged_tags_are_deduped()
    {
        var ops = ItemMapper.ToPatch(Input(source: null, cw: null, status: "New"),
            existingTags: "AirCoverage; ExtraTag; ExtraTag", requiredTag: "AirCoverage");
        var tags = (string)ops.Single(o => o.path == "/fields/System.Tags").value!;
        var tagList = tags.Split(';', StringSplitOptions.TrimEntries);
        Assert.Equal(tagList.Length, tagList.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
