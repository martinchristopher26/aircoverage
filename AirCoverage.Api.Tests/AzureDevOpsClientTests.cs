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

    private AzureDevOpsClient NewClient(string project = "Proj", int parentWorkItemId = 0)
    {
        var options = Options.Create(new AdoOptions
        {
            OrgUrl = _server.Urls[0], Project = project, Tag = "AirCoverage", WorkItemType = "Bug",
            ParentWorkItemId = parentWorkItemId,
        });
        var http = new HttpClient { BaseAddress = new Uri(_server.Urls[0]) };
        return new AzureDevOpsClient(http, options);
    }

    // Regression: a leading-slash request path against a BaseAddress with an org segment
    // (e.g. https://dev.azure.com/JustFOIA) drops the org. BuildUrl must preserve it.
    [Theory]
    [InlineData("https://dev.azure.com/JustFOIA", "JustFOIA%20Core/_apis/wit/wiql", "https://dev.azure.com/JustFOIA/JustFOIA%20Core/_apis/wit/wiql")]
    [InlineData("https://dev.azure.com/JustFOIA/", "_apis/wit/workitems", "https://dev.azure.com/JustFOIA/_apis/wit/workitems")]
    public void BuildUrl_preserves_org_path_segment(string orgUrl, string path, string expected)
    {
        Assert.Equal(expected, AzureDevOpsClient.BuildUrl(orgUrl, path));
    }

    [Fact]
    public async Task QueryMemberIdsAsync_unions_tagged_and_descendants_excluding_epic_and_root()
    {
        // The project name contains a space; the client URL-encodes it to "JustFOIA%20Core"
        // on the wire. WireMock matches against the DECODED path, hence the space here.
        const string wiqlPath = "/JustFOIA Core/_apis/wit/wiql";

        // Tag query (flat WorkItems WIQL) → a single tagged id.
        _server.Given(Request.Create().WithPath(wiqlPath).UsingPost()
                .WithBody(b => b!.Contains("System.Tags")))
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("""{"workItems":[{"id":7}]}"""));

        // Link query (recursive WorkItemLinks WIQL) → root self-entry (null rel) + 2 descendants.
        _server.Given(Request.Create().WithPath(wiqlPath).UsingPost()
                .WithBody(b => b!.Contains("WorkItemLinks")))
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("""
                {"workItemRelations":[
                  {"rel":null,"target":{"id":22691}},
                  {"rel":"System.LinkTypes.Hierarchy-Forward","source":{"id":22691},"target":{"id":8}},
                  {"rel":"System.LinkTypes.Hierarchy-Forward","source":{"id":8},"target":{"id":9}}
                ]}
                """));

        var ids = await NewClient(project: "JustFOIA Core", parentWorkItemId: 22691)
            .QueryMemberIdsAsync(CancellationToken.None);

        // Union of {7} (tagged) and {8,9} (descendants); 22691 (epic + null-rel root) excluded.
        Assert.Equal(new[] { 7, 8, 9 }, ids.OrderBy(i => i).ToArray());
    }

    [Fact]
    public async Task GetWorkItemsAsync_flattens_fields()
    {
        _server.Given(Request.Create().WithPath("/_apis/wit/workitems").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""
            {"value":[{"id":7,"url":"https://x/7",
              "_links":{"html":{"href":"https://dev.azure.com/JustFOIA/JustFOIA%20Core/_workitems/edit/7"}},
              "fields":{
              "System.Title":"T","System.Description":"<div>d</div>",
              "System.State":"Active","System.Tags":"AirCoverage; Waiting",
              "System.WorkItemType":"Bug",
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
        Assert.Equal("Bug", wi.WorkItemType);
        // Url must be the human web URL (_links.html.href), NOT the REST api url ("https://x/7").
        Assert.Equal("https://dev.azure.com/JustFOIA/JustFOIA%20Core/_workitems/edit/7", wi.Url);
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
