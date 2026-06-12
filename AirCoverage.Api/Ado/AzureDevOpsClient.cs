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

    // Fix 1: escape single quotes so tag/area-path values containing apostrophes
    // can't produce malformed WIQL (e.g. "O'Brien" → "O''Brien").
    private static string WiqlEscape(string s) => s.Replace("'", "''");

    // The project name can contain spaces (e.g. "JustFOIA Core"); escape it for the URL path.
    private string ProjectPath => Uri.EscapeDataString(_opt.Project);

    /// <summary>
    /// Returns the deduped union of the queue's two membership sources:
    /// work items carrying the configured tag, and (if configured) every descendant
    /// of the parent epic at any depth. The parent epic itself is excluded.
    /// </summary>
    public async Task<IReadOnlyList<int>> QueryMemberIdsAsync(CancellationToken ct)
    {
        var ids = new HashSet<int>();

        var tagged = await WiqlIdsAsync(
            $"SELECT [System.Id] FROM WorkItems WHERE [System.Tags] CONTAINS '{WiqlEscape(_opt.Tag)}'", ct);
        foreach (var id in tagged) ids.Add(id);

        if (_opt.ParentWorkItemId > 0)
        {
            var descendants = await WiqlLinkTargetIdsAsync(
                $"SELECT [System.Id] FROM WorkItemLinks WHERE [Source].[System.Id] = {_opt.ParentWorkItemId} " +
                "AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward' MODE (Recursive)",
                excludeId: _opt.ParentWorkItemId, ct);
            foreach (var id in descendants) ids.Add(id);
        }

        return ids.ToList();
    }

    private async Task<IReadOnlyList<int>> WiqlIdsAsync(string query, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync(
            $"/{ProjectPath}/_apis/wit/wiql?{ApiVersion}", new { query }, ct);
        // Fix 4: surface ADO error bodies instead of bare EnsureSuccessStatusCode
        await EnsureAdoSuccessAsync(resp, ct);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
        var arr = json?["workItems"]?.AsArray();
        return arr is null ? Array.Empty<int>()
            : arr.Select(n => (int)n!["id"]!).ToList();
    }

    // Parses a recursive WorkItemLinks WIQL response: target ids from workItemRelations[],
    // excluding the null-rel root self-entry and the supplied excludeId (the epic itself).
    private async Task<IReadOnlyList<int>> WiqlLinkTargetIdsAsync(string query, int excludeId, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync(
            $"/{ProjectPath}/_apis/wit/wiql?{ApiVersion}", new { query }, ct);
        await EnsureAdoSuccessAsync(resp, ct);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
        var arr = json?["workItemRelations"]?.AsArray();
        if (arr is null) return Array.Empty<int>();
        return arr
            .Where(n => n!["rel"] is not null)                  // skip the root self-entry
            .Select(n => (int)n!["target"]!["id"]!)
            .Where(id => id != excludeId)                       // skip the epic itself
            .Distinct()
            .ToList();
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
            // Fix 4: switch from GetFromJsonAsync to GetAsync + EnsureAdoSuccessAsync so
            // error bodies are surfaced; then read JSON manually.
            var resp = await _http.GetAsync(url, ct);
            await EnsureAdoSuccessAsync(resp, ct);
            var json = await resp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
            // Fix 3: null-safe body check
            if (json is null)
                throw new InvalidOperationException($"ADO returned an empty body for {resp.RequestMessage?.RequestUri}");
            foreach (var node in json["value"]?.AsArray() ?? new JsonArray())
                result.Add(Flatten(node!.AsObject()));
        }
        return result;
    }

    public async Task<AdoWorkItem?> GetWorkItemAsync(int id, CancellationToken ct)
    {
        var resp = await _http.GetAsync($"/_apis/wit/workitems/{id}?{ApiVersion}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        // Fix 4: surface ADO error bodies for non-404 failures
        await EnsureAdoSuccessAsync(resp, ct);
        var node = await resp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
        // Fix 3: null-safe body check
        if (node is null)
            throw new InvalidOperationException($"ADO returned an empty body for {resp.RequestMessage?.RequestUri}");
        return Flatten(node);
    }

    public Task<AdoWorkItem> CreateAsync(IReadOnlyList<JsonPatchOperation> ops, CancellationToken ct) =>
        PatchAsync(HttpMethod.Post, $"/{ProjectPath}/_apis/wit/workitems/${_opt.WorkItemType}?{ApiVersion}", ops, ct);

    public Task<AdoWorkItem> UpdateAsync(int id, IReadOnlyList<JsonPatchOperation> ops, CancellationToken ct) =>
        PatchAsync(HttpMethod.Patch, $"/_apis/wit/workitems/{id}?{ApiVersion}", ops, ct);

    private async Task<AdoWorkItem> PatchAsync(HttpMethod method, string url, IReadOnlyList<JsonPatchOperation> ops, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(ops), Encoding.UTF8, "application/json-patch+json"),
        };
        var resp = await _http.SendAsync(req, ct);
        // Fix 4: surface ADO error bodies instead of bare EnsureSuccessStatusCode
        await EnsureAdoSuccessAsync(resp, ct);
        var node = await resp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
        // Fix 3: null-safe body check
        if (node is null)
            throw new InvalidOperationException($"ADO returned an empty body for {resp.RequestMessage?.RequestUri}");
        return Flatten(node);
    }

    // Fix 4: helper that reads and includes the ADO error body in the exception message.
    private static async Task EnsureAdoSuccessAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync(ct);
        var snippet = body.Length > 1024 ? body[..1024] : body;
        throw new HttpRequestException(
            $"ADO request to {resp.RequestMessage?.RequestUri} failed with {(int)resp.StatusCode} {resp.ReasonPhrase}: {snippet}");
    }

    private static AdoWorkItem Flatten(JsonObject node)
    {
        // Fix 3: informative exception when fields is absent
        var fieldsNode = node["fields"]
            ?? throw new InvalidOperationException($"ADO work item {node["id"]} has no 'fields'");
        var f = fieldsNode.AsObject();
        string? S(string k) => f.TryGetPropertyValue(k, out var v) ? v?.ToString() : null;
        // Fix 2: robust integer parsing via string round-trip; tolerates any numeric serialization
        int? I(string k) => int.TryParse(S(k), out var n) ? n : (int?)null;
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
