using System.Net;
using AirCoverage.Api.Ado;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace AirCoverage.Api.Tests;

public class AzureDevOpsClientTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    private AzureDevOpsClient NewClient()
    {
        var options = Options.Create(new AdoOptions
        {
            OrgUrl = _server.Urls[0], Project = "Proj", Tag = "AirCoverage", WorkItemType = "Bug",
        });
        var http = new HttpClient { BaseAddress = new Uri(_server.Urls[0]) };
        return new AzureDevOpsClient(http, options);
    }

    [Fact]
    public async Task QueryOpenIdsAsync_posts_wiql_and_returns_ids()
    {
        _server.Given(Request.Create().WithPath("/Proj/_apis/wit/wiql").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("""{"workItems":[{"id":7},{"id":9}]}"""));

        var ids = await NewClient().QueryOpenIdsAsync(CancellationToken.None);

        Assert.Equal(new[] { 7, 9 }, ids);
    }

    [Fact]
    public async Task GetWorkItemsAsync_flattens_fields()
    {
        _server.Given(Request.Create().WithPath("/_apis/wit/workitems").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""
            {"value":[{"id":7,"url":"https://x/7","fields":{
              "System.Title":"T","System.Description":"<div>d</div>",
              "System.State":"Active","System.Tags":"AirCoverage; Waiting",
              "Microsoft.VSTS.Common.Priority":2,
              "System.AssignedTo":{"displayName":"Alex Reyes"},
              "System.CreatedDate":"2026-01-01T00:00:00Z","System.ChangedDate":"2026-01-02T00:00:00Z"}}]}
            """));

        var items = await NewClient().GetWorkItemsAsync(new[] { 7 }, CancellationToken.None);

        var wi = Assert.Single(items);
        Assert.Equal("T", wi.Title);
        Assert.Equal("Active", wi.State);
        Assert.Equal(2, wi.Priority);
        Assert.Equal("Alex Reyes", wi.AssignedToDisplayName);
        Assert.Equal("AirCoverage; Waiting", wi.Tags);
    }

    // New test 1: GetWorkItemAsync returns null on a 404 from ADO.
    [Fact]
    public async Task GetWorkItemAsync_returns_null_on_404()
    {
        _server.Given(Request.Create().WithPath("/_apis/wit/workitems/999").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404).WithBody("""{"message":"Work item 999 does not exist"}"""));

        var result = await NewClient().GetWorkItemAsync(999, CancellationToken.None);

        Assert.Null(result);
    }

    // New test 2: A 400 from ADO on CreateAsync throws and the message contains the ADO error body.
    [Fact]
    public async Task CreateAsync_throws_with_ado_error_body_on_400()
    {
        const string adoErrorBody = """{"message":"TF401232: Work item type 'Bug' does not exist in project."}""";
        _server.Given(Request.Create().WithPath("/Proj/_apis/wit/workitems/$Bug").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(400).WithBody(adoErrorBody));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => NewClient().CreateAsync(Array.Empty<JsonPatchOperation>(), CancellationToken.None));

        Assert.Contains("TF401232", ex.Message);
    }

    // New test 3: Flatten does NOT throw when optional fields (AssignedTo, ChangedDate) are absent.
    [Fact]
    public async Task GetWorkItemsAsync_tolerates_absent_optional_fields()
    {
        _server.Given(Request.Create().WithPath("/_apis/wit/workitems").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""
            {"value":[{"id":42,"url":"https://x/42","fields":{
              "System.Title":"Minimal","System.State":"New",
              "System.CreatedDate":"2026-03-01T00:00:00Z"}}]}
            """));

        var items = await NewClient().GetWorkItemsAsync(new[] { 42 }, CancellationToken.None);

        var wi = Assert.Single(items);
        Assert.Equal(42, wi.Id);
        Assert.Null(wi.AssignedToDisplayName);
        Assert.Null(wi.ChangedDate);
    }

    // New test 4: CreateAsync round-trip — stub the POST and assert the returned AdoWorkItem.Id.
    [Fact]
    public async Task CreateAsync_returns_work_item_with_correct_id()
    {
        _server.Given(Request.Create().WithPath("/Proj/_apis/wit/workitems/$Bug").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""
            {"id":55,"url":"https://x/55","fields":{
              "System.Title":"New bug","System.State":"New",
              "System.CreatedDate":"2026-05-01T00:00:00Z"}}
            """));

        var wi = await NewClient().CreateAsync(Array.Empty<JsonPatchOperation>(), CancellationToken.None);

        Assert.Equal(55, wi.Id);
        Assert.Equal("New bug", wi.Title);
    }

    public void Dispose() => _server.Dispose();
}
