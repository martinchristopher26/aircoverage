using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace AirCoverage.Api.Ado;

public class AzureDevOpsClient : IAzureDevOpsClient
{
    private const string ApiVersion = "api-version=7.1";
    private readonly HttpClient _http;
    private readonly AdoOptions _opt;

    public AzureDevOpsClient(HttpClient http, IOptions<AdoOptions> options)
    {
        _http = http;
        _opt = options.Value;
        if (_http.BaseAddress is null && !string.IsNullOrWhiteSpace(_opt.OrgUrl))
            _http.BaseAddress = new Uri(_opt.OrgUrl);
    }

    private string TagFilter =>
        $"[System.Tags] CONTAINS '{_opt.Tag}'" +
        (string.IsNullOrWhiteSpace(_opt.AreaPath) ? "" : $" AND [System.AreaPath] UNDER '{_opt.AreaPath}'");

    public Task<IReadOnlyList<int>> QueryOpenIdsAsync(CancellationToken ct) =>
        WiqlIdsAsync($"SELECT [System.Id] FROM WorkItems WHERE {TagFilter} " +
                     "AND [System.State] NOT IN ('Closed','Resolved') " +
                     "ORDER BY [Microsoft.VSTS.Common.Priority] ASC, [System.CreatedDate] ASC", ct);

    public Task<IReadOnlyList<int>> QueryIdsChangedSinceAsync(DateTime sinceUtc, CancellationToken ct) =>
        WiqlIdsAsync($"SELECT [System.Id] FROM WorkItems WHERE {TagFilter} " +
                     $"AND [System.ChangedDate] >= '{sinceUtc:yyyy-MM-ddTHH:mm:ssZ}'", ct);

    public Task<IReadOnlyList<int>> QueryClosedIdsAsync(int windowDays, CancellationToken ct) =>
        WiqlIdsAsync($"SELECT [System.Id] FROM WorkItems WHERE {TagFilter} " +
                     "AND [System.State] IN ('Closed','Resolved') " +
                     $"AND [System.ChangedDate] >= @today - {windowDays} " +
                     "ORDER BY [System.ChangedDate] DESC", ct);

    private async Task<IReadOnlyList<int>> WiqlIdsAsync(string query, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync(
            $"/{_opt.Project}/_apis/wit/wiql?{ApiVersion}", new { query }, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
        var arr = json?["workItems"]?.AsArray();
        return arr is null ? Array.Empty<int>()
            : arr.Select(n => (int)n!["id"]!).ToList();
    }

    public async Task<IReadOnlyList<AdoWorkItem>> GetWorkItemsAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return Array.Empty<AdoWorkItem>();
        var result = new List<AdoWorkItem>(ids.Count);
        foreach (var batch in ids.Chunk(200))
        {
            var fields = "System.Title,System.Description,System.State,System.Tags," +
                         "Microsoft.VSTS.Common.Priority,System.AssignedTo,System.CreatedDate,System.ChangedDate";
            var url = $"/_apis/wit/workitems?ids={string.Join(',', batch)}&fields={fields}&{ApiVersion}";
            var json = await _http.GetFromJsonAsync<JsonObject>(url, ct);
            foreach (var node in json?["value"]?.AsArray() ?? new JsonArray())
                result.Add(Flatten(node!.AsObject()));
        }
        return result;
    }

    public async Task<AdoWorkItem?> GetWorkItemAsync(int id, CancellationToken ct)
    {
        var resp = await _http.GetAsync($"/_apis/wit/workitems/{id}?{ApiVersion}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var node = await resp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
        return Flatten(node!);
    }

    public Task<AdoWorkItem> CreateAsync(IReadOnlyList<JsonPatchOperation> ops, CancellationToken ct) =>
        PatchAsync(HttpMethod.Post, $"/{_opt.Project}/_apis/wit/workitems/${_opt.WorkItemType}?{ApiVersion}", ops, ct);

    public Task<AdoWorkItem> UpdateAsync(int id, IReadOnlyList<JsonPatchOperation> ops, CancellationToken ct) =>
        PatchAsync(HttpMethod.Patch, $"/_apis/wit/workitems/{id}?{ApiVersion}", ops, ct);

    private async Task<AdoWorkItem> PatchAsync(HttpMethod method, string url, IReadOnlyList<JsonPatchOperation> ops, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(ops), Encoding.UTF8, "application/json-patch+json"),
        };
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var node = await resp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
        return Flatten(node!);
    }

    private static AdoWorkItem Flatten(JsonObject node)
    {
        var f = node["fields"]!.AsObject();
        string? S(string k) => f.TryGetPropertyValue(k, out var v) ? v?.ToString() : null;
        int? I(string k) => f.TryGetPropertyValue(k, out var v) && v is not null ? (int)v! : null;
        return new AdoWorkItem(
            Id: (int)node["id"]!,
            Title: S("System.Title") ?? "",
            Description: S("System.Description") ?? "",
            Priority: I("Microsoft.VSTS.Common.Priority"),
            State: S("System.State") ?? "New",
            AssignedToDisplayName: f["System.AssignedTo"]?["displayName"]?.ToString(),
            Tags: S("System.Tags") ?? "",
            Url: node["url"]?.ToString() ?? "",
            CreatedDate: DateTime.Parse(S("System.CreatedDate") ?? DateTime.UtcNow.ToString("o")).ToUniversalTime(),
            ChangedDate: S("System.ChangedDate") is { } cd ? DateTime.Parse(cd).ToUniversalTime() : null);
    }
}
