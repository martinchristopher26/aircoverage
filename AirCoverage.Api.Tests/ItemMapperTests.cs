using AirCoverage.Api.Ado;

namespace AirCoverage.Api.Tests;

public class ItemMapperTests
{
    private static AdoWorkItem Wi(string state, string tags, int? priority = 2) => new(
        Id: 42, Title: "T", Description: "<div>d</div>", Priority: priority, State: state,
        AssignedToDisplayName: "Alex Reyes", Tags: tags, Url: "https://x/42",
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
}
