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

    public void Dispose() => _server.Dispose();
}
