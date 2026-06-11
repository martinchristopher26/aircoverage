# Azure DevOps Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace SQLite-as-system-of-record with Azure DevOps as the single source of truth, keeping SQLite only as a read-through cache of open items.

**Architecture:** Endpoints depend on an `IItemStore` seam. `AdoItemStore` serves reads from a SQLite cache and writes through to the ADO REST API (`AzureDevOpsClient`). A `SyncService` background worker keeps the cache fresh (write-through + 60s delta poll + 5-min reconcile) and prunes Closed/Resolved items so they fall off the queue. Auth to ADO is a PAT used in every environment, isolated behind a delegating handler so it can become Entra later.

**Tech Stack:** .NET 8 Minimal API, EF Core + SQLite (cache), `Microsoft.Extensions.Http.Resilience` (Polly) for retries/429, xUnit + NSubstitute + WireMock.Net for tests, ADO REST API `api-version=7.1`.

**Reference spec:** `docs/specs/2026-06-11-ado-backend-design.md`

---

## File Structure

**New (backend):**
- `AirCoverage.Api/Abstractions/IItemStore.cs` — store seam + `ItemDto` / `ItemInput` contracts
- `AirCoverage.Api/Ado/AdoOptions.cs` — bound config (org, project, PAT, tag, area, WIT, intervals)
- `AirCoverage.Api/Ado/AdoModels.cs` — `AdoWorkItem`, `JsonPatchOperation`
- `AirCoverage.Api/Ado/IAzureDevOpsClient.cs` — ADO operations interface
- `AirCoverage.Api/Ado/AzureDevOpsClient.cs` — REST implementation (WIQL, batch get, JSON-Patch)
- `AirCoverage.Api/Ado/ItemMapper.cs` — pure AC ⇄ ADO field mapping
- `AirCoverage.Api/Ado/HtmlText.cs` — minimal HTML ⇄ plain-text helpers
- `AirCoverage.Api/Ado/PatAuthHandler.cs` — delegating handler injecting the PAT (Entra swap point)
- `AirCoverage.Api/Stores/AdoItemStore.cs` — cache-read + ADO-write orchestration
- `AirCoverage.Api/Sync/SyncService.cs` — background delta poll + reconcile
- `AirCoverage.Api/Data/CachedItem.cs` — cache entity
- `AirCoverage.Api/Data/SyncState.cs` — sync watermark entity

**Modified (backend):**
- `AirCoverage.Api/Data/AppDbContext.cs` → repurposed to cache (`CacheDbContext`)
- `AirCoverage.Api/Data/DesignTimeDbContextFactory.cs` — point at `CacheDbContext`
- `AirCoverage.Api/Endpoints/ItemsEndpoints.cs` — depend on `IItemStore`; add closed scope
- `AirCoverage.Api/Endpoints/SyncEndpoints.cs` (new) — `GET /api/sync/status`
- `AirCoverage.Api/Program.cs` — DI: options, HttpClient+resilience, store, sync service
- `AirCoverage.Api/appsettings.json` — `Ado` section
- `AirCoverage.Api/AirCoverage.Api.csproj` — add resilience package

**Removed (backend):**
- `AirCoverage.Api/Data/DbSeeder.cs`
- `AirCoverage.Api/Models/Item.cs` (replaced by `CachedItem` + `ItemDto`)
- `AirCoverage.Api/Dtos/Dtos.cs` `ItemInput` (moved into `Abstractions/IItemStore.cs`); keep `LoginRequest`/`UserResponse`
- `NextNumberAsync` in `ItemsEndpoints.cs`

**New (tests):** `AirCoverage.Api.Tests/` project with
`HtmlTextTests.cs`, `ItemMapperTests.cs`, `AzureDevOpsClientTests.cs`, `AdoItemStoreTests.cs`, `SyncServiceTests.cs`.

**Frontend (modified):**
- `web/src/types.ts`, `web/src/stores/items.ts`, `web/src/components/QueueTable.vue`, `web/src/components/PageHeader.vue`, `web/src/api/client.ts`

---

## Phase 0 — Setup

### Task 1: Initialize git + solution

**Files:** repository root.

- [ ] **Step 1: Init repo and confirm ignore rules**

Run:
```bash
cd /c/Users/cmahon/source/AirCoverage
git init
git add .
git commit -m "chore: baseline before ADO backend work"
```
Expected: a commit is created; `git status` is clean. (`.gitignore` already excludes `bin/`, `obj/`, `*.db`, `*.pfx`, `node_modules/`, `web/dist/`, `AirCoverage.Api/wwwroot/`.)

- [ ] **Step 2: Create a solution and add the API project**

Run:
```bash
dotnet new sln -n AirCoverage
dotnet sln add AirCoverage.Api/AirCoverage.Api.csproj
```
Expected: `AirCoverage.sln` created; project added.

- [ ] **Step 3: Commit**

```bash
git add AirCoverage.sln
git commit -m "chore: add solution file"
```

### Task 2: Create the test project

**Files:**
- Create: `AirCoverage.Api.Tests/AirCoverage.Api.Tests.csproj`
- Create: `AirCoverage.Api.Tests/SmokeTests.cs`

- [ ] **Step 1: Scaffold the xUnit project and add packages**

Run:
```bash
dotnet new xunit -n AirCoverage.Api.Tests
dotnet sln add AirCoverage.Api.Tests/AirCoverage.Api.Tests.csproj
dotnet add AirCoverage.Api.Tests/AirCoverage.Api.Tests.csproj reference AirCoverage.Api/AirCoverage.Api.csproj
dotnet add AirCoverage.Api.Tests/AirCoverage.Api.Tests.csproj package NSubstitute
dotnet add AirCoverage.Api.Tests/AirCoverage.Api.Tests.csproj package WireMock.Net
dotnet add AirCoverage.Api.Tests/AirCoverage.Api.Tests.csproj package Microsoft.EntityFrameworkCore.Sqlite
```

- [ ] **Step 2: Pin the test project to net8.0 with roll-forward**

Edit `AirCoverage.Api.Tests/AirCoverage.Api.Tests.csproj` so the `<PropertyGroup>` contains:
```xml
<TargetFramework>net8.0</TargetFramework>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<IsPackable>false</IsPackable>
<RollForward>LatestMajor</RollForward>
```

- [ ] **Step 3: Replace the generated test with a smoke test**

Replace the contents of `AirCoverage.Api.Tests/UnitTest1.cs` (delete it) with `AirCoverage.Api.Tests/SmokeTests.cs`:
```csharp
namespace AirCoverage.Api.Tests;

public class SmokeTests
{
    [Fact]
    public void Harness_Runs()
    {
        Assert.True(true);
    }
}
```
Run: `rm AirCoverage.Api.Tests/UnitTest1.cs` (if present).

- [ ] **Step 4: Run tests**

Run: `dotnet test`
Expected: PASS, 1 test passed.

- [ ] **Step 5: Commit**

```bash
git add AirCoverage.Api.Tests AirCoverage.sln
git commit -m "test: add xUnit test project (NSubstitute, WireMock.Net)"
```

---

## Phase 1 — Contracts & pure helpers (no I/O)

### Task 3: Define the store contracts

**Files:**
- Create: `AirCoverage.Api/Abstractions/IItemStore.cs`

- [ ] **Step 1: Write the contracts**

```csharp
namespace AirCoverage.Api.Abstractions;

/// <summary>The shape the SPA consumes. id is the ADO work-item id.</summary>
public record ItemDto(
    int Id,
    string Number,
    string Title,
    string Description,
    string Priority,
    string Status,
    string RequestedBy,
    string Assignee,
    string TicketType,
    string TicketRef,
    string Url,
    DateTime Received,
    DateTime? Updated);

/// <summary>Editable fields accepted on create/update.</summary>
public record ItemInput(
    string Title,
    string? Description,
    string Priority,
    string Status,
    string? RequestedBy,
    string? Assignee,
    string? TicketType,
    string? TicketRef);

public enum ItemScope { Open, Closed }

public interface IItemStore
{
    Task<IReadOnlyList<ItemDto>> GetItemsAsync(ItemScope scope, CancellationToken ct);
    Task<ItemDto?> GetItemAsync(int id, CancellationToken ct);
    Task<ItemDto> CreateAsync(ItemInput input, CancellationToken ct);
    Task<ItemDto?> UpdateAsync(int id, ItemInput input, CancellationToken ct);
    Task<bool> DeleteAsync(int id, CancellationToken ct);
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build AirCoverage.Api/AirCoverage.Api.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add AirCoverage.Api/Abstractions/IItemStore.cs
git commit -m "feat: add IItemStore seam and DTO contracts"
```

### Task 4: HTML ⇄ plain-text helper

**Files:**
- Create: `AirCoverage.Api/Ado/HtmlText.cs`
- Test: `AirCoverage.Api.Tests/HtmlTextTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using AirCoverage.Api.Ado;

namespace AirCoverage.Api.Tests;

public class HtmlTextTests
{
    [Fact]
    public void ToPlainText_strips_tags_and_decodes_entities()
    {
        Assert.Equal("a & b", HtmlText.ToPlainText("<div>a &amp; b</div>"));
    }

    [Fact]
    public void ToPlainText_converts_breaks_to_newlines()
    {
        Assert.Equal("line1\nline2", HtmlText.ToPlainText("line1<br>line2"));
    }

    [Fact]
    public void ToPlainText_null_is_empty()
    {
        Assert.Equal("", HtmlText.ToPlainText(null));
    }

    [Fact]
    public void ToHtml_encodes_and_converts_newlines()
    {
        Assert.Equal("a &amp; b<br>c", HtmlText.ToHtml("a & b\nc"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter HtmlTextTests`
Expected: FAIL — `HtmlText` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System.Net;
using System.Text.RegularExpressions;

namespace AirCoverage.Api.Ado;

/// <summary>
/// ADO stores Description as HTML; the app edits plain text. These conversions are
/// intentionally lossy-but-safe for a plain-text editor.
/// </summary>
public static partial class HtmlText
{
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var withBreaks = BreakRegex().Replace(html, "\n");
        var noTags = TagRegex().Replace(withBreaks, "");
        return WebUtility.HtmlDecode(noTags).Trim();
    }

    public static string ToHtml(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return WebUtility.HtmlEncode(text).Replace("\n", "<br>");
    }

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter HtmlTextTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add AirCoverage.Api/Ado/HtmlText.cs AirCoverage.Api.Tests/HtmlTextTests.cs
git commit -m "feat: add HTML/plain-text helper for ADO descriptions"
```

### Task 5: ADO models + options

**Files:**
- Create: `AirCoverage.Api/Ado/AdoModels.cs`
- Create: `AirCoverage.Api/Ado/AdoOptions.cs`

- [ ] **Step 1: Write the models and options**

`AdoModels.cs`:
```csharp
namespace AirCoverage.Api.Ado;

/// <summary>A work item flattened to the fields we use.</summary>
public record AdoWorkItem(
    int Id,
    string Title,
    string Description,
    int? Priority,
    string State,
    string? AssignedToDisplayName,
    string Tags,
    string Url,
    DateTime CreatedDate,
    DateTime? ChangedDate);

/// <summary>One operation in a JSON-Patch document sent to ADO.</summary>
public record JsonPatchOperation(string op, string path, object? value);
```

`AdoOptions.cs`:
```csharp
namespace AirCoverage.Api.Ado;

public class AdoOptions
{
    public const string Section = "Ado";

    public string OrgUrl { get; set; } = "";          // https://dev.azure.com/yourorg
    public string Project { get; set; } = "";
    public string Pat { get; set; } = "";
    public string Tag { get; set; } = "AirCoverage";
    public string? AreaPath { get; set; }
    public string WorkItemType { get; set; } = "Bug";
    public int PollSeconds { get; set; } = 60;
    public int ReconcileSeconds { get; set; } = 300;
    public int ClosedWindowDays { get; set; } = 30;
}
```

- [ ] **Step 2: Build**

Run: `dotnet build AirCoverage.Api/AirCoverage.Api.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add AirCoverage.Api/Ado/AdoModels.cs AirCoverage.Api/Ado/AdoOptions.cs
git commit -m "feat: add ADO models and options"
```

### Task 6: ItemMapper — read mapping (ADO → DTO)

**Files:**
- Create: `AirCoverage.Api/Ado/ItemMapper.cs`
- Test: `AirCoverage.Api.Tests/ItemMapperTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
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
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter ItemMapperTests`
Expected: FAIL — `ItemMapper` does not exist.

- [ ] **Step 3: Implement the read half (plus tag helpers used later)**

```csharp
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
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter ItemMapperTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add AirCoverage.Api/Ado/ItemMapper.cs AirCoverage.Api.Tests/ItemMapperTests.cs
git commit -m "feat: map ADO work items to ItemDto"
```

### Task 7: ItemMapper — write mapping (Input → JSON-Patch)

**Files:**
- Modify: `AirCoverage.Api/Ado/ItemMapper.cs`
- Modify: `AirCoverage.Api.Tests/ItemMapperTests.cs`

- [ ] **Step 1: Add the failing tests**

Append to `ItemMapperTests`:
```csharp
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
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter ItemMapperTests`
Expected: FAIL — `ToPatch` not defined.

- [ ] **Step 3: Implement `ToPatch` in `ItemMapper`**

Add these members to `ItemMapper`:
```csharp
    private static readonly Dictionary<string, string> StatusToState =
        new() { ["New"] = "New", ["In Progress"] = "Active", ["Waiting"] = "Active",
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

        var tags = ParseTags(existingTags)
            .Where(t => !t.Equals(WaitingTag, StringComparison.OrdinalIgnoreCase)
                     && !t.StartsWith(SourcePrefix, StringComparison.OrdinalIgnoreCase)
                     && !t.StartsWith(CwPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!tags.Any(t => t.Equals(requiredTag, StringComparison.OrdinalIgnoreCase)))
            tags.Add(requiredTag);
        if (status == "Waiting")
            tags.Add(WaitingTag);
        if (!string.IsNullOrWhiteSpace(input.RequestedBy))
            tags.Add(SourcePrefix + input.RequestedBy.Trim());
        if (string.Equals(input.TicketType, "ConnectWise", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(input.TicketRef))
            tags.Add(CwPrefix + input.TicketRef.Trim());

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
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter ItemMapperTests`
Expected: PASS (8 tests total).

- [ ] **Step 5: Commit**

```bash
git add AirCoverage.Api/Ado/ItemMapper.cs AirCoverage.Api.Tests/ItemMapperTests.cs
git commit -m "feat: map ItemInput to ADO JSON-Patch document"
```

---

## Phase 2 — ADO REST client

### Task 8: ADO client interface + PAT handler + options binding

**Files:**
- Create: `AirCoverage.Api/Ado/IAzureDevOpsClient.cs`
- Create: `AirCoverage.Api/Ado/PatAuthHandler.cs`
- Modify: `AirCoverage.Api/AirCoverage.Api.csproj`
- Modify: `AirCoverage.Api/appsettings.json`

- [ ] **Step 1: Add the resilience package**

Run:
```bash
dotnet add AirCoverage.Api/AirCoverage.Api.csproj package Microsoft.Extensions.Http.Resilience
```

- [ ] **Step 2: Write the interface**

`IAzureDevOpsClient.cs`:
```csharp
namespace AirCoverage.Api.Ado;

public interface IAzureDevOpsClient
{
    Task<IReadOnlyList<int>> QueryOpenIdsAsync(CancellationToken ct);
    Task<IReadOnlyList<int>> QueryIdsChangedSinceAsync(DateTime sinceUtc, CancellationToken ct);
    Task<IReadOnlyList<int>> QueryClosedIdsAsync(int windowDays, CancellationToken ct);
    Task<IReadOnlyList<AdoWorkItem>> GetWorkItemsAsync(IReadOnlyCollection<int> ids, CancellationToken ct);
    Task<AdoWorkItem?> GetWorkItemAsync(int id, CancellationToken ct);
    Task<AdoWorkItem> CreateAsync(IReadOnlyList<JsonPatchOperation> ops, CancellationToken ct);
    Task<AdoWorkItem> UpdateAsync(int id, IReadOnlyList<JsonPatchOperation> ops, CancellationToken ct);
}
```

- [ ] **Step 3: Write the PAT delegating handler**

`PatAuthHandler.cs`:
```csharp
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace AirCoverage.Api.Ado;

/// <summary>
/// Injects the PAT as Basic auth on every ADO request. This is the single seam to
/// replace when moving to Entra ID (swap for an OAuth/managed-identity token handler).
/// </summary>
public class PatAuthHandler : DelegatingHandler
{
    private readonly string _header;

    public PatAuthHandler(IOptions<AdoOptions> options)
    {
        var pat = options.Value.Pat;
        _header = Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + pat));
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _header);
        return base.SendAsync(request, ct);
    }
}
```

- [ ] **Step 4: Add the `Ado` config section**

Edit `AirCoverage.Api/appsettings.json` — add after the `"Https"` block:
```json
  "Ado": {
    "OrgUrl": "https://dev.azure.com/REPLACE-ORG",
    "Project": "REPLACE-PROJECT",
    "Pat": "",
    "Tag": "AirCoverage",
    "AreaPath": null,
    "WorkItemType": "Bug",
    "PollSeconds": 60,
    "ReconcileSeconds": 300,
    "ClosedWindowDays": 30
  },
```

- [ ] **Step 5: Build**

Run: `dotnet build AirCoverage.Api/AirCoverage.Api.csproj`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add AirCoverage.Api/Ado/IAzureDevOpsClient.cs AirCoverage.Api/Ado/PatAuthHandler.cs AirCoverage.Api/AirCoverage.Api.csproj AirCoverage.Api/appsettings.json
git commit -m "feat: add ADO client interface, PAT handler, config"
```

### Task 9: AzureDevOpsClient implementation

**Files:**
- Create: `AirCoverage.Api/Ado/AzureDevOpsClient.cs`
- Test: `AirCoverage.Api.Tests/AzureDevOpsClientTests.cs`

- [ ] **Step 1: Write the failing test (WireMock-stubbed ADO)**

```csharp
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
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter AzureDevOpsClientTests`
Expected: FAIL — `AzureDevOpsClient` does not exist.

- [ ] **Step 3: Implement the client**

```csharp
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
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter AzureDevOpsClientTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add AirCoverage.Api/Ado/AzureDevOpsClient.cs AirCoverage.Api.Tests/AzureDevOpsClientTests.cs
git commit -m "feat: implement Azure DevOps REST client (WIQL, batch, patch)"
```

---

## Phase 3 — Cache schema & store

### Task 10: Cache entities + CacheDbContext

**Files:**
- Create: `AirCoverage.Api/Data/CachedItem.cs`
- Create: `AirCoverage.Api/Data/SyncState.cs`
- Modify: `AirCoverage.Api/Data/AppDbContext.cs` (rename to `CacheDbContext`)
- Modify: `AirCoverage.Api/Data/DesignTimeDbContextFactory.cs`

- [ ] **Step 1: Write the entities**

`CachedItem.cs`:
```csharp
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
```

`SyncState.cs`:
```csharp
namespace AirCoverage.Api.Data;

public class SyncState
{
    public int Id { get; set; }                 // singleton row, Id = 1
    public DateTime? LastChangedWatermark { get; set; }
    public DateTime? LastSuccessfulSync { get; set; }
}
```

- [ ] **Step 2: Repurpose the context**

Replace the entire contents of `AirCoverage.Api/Data/AppDbContext.cs` with (and rename the file to `CacheDbContext.cs`):
```csharp
using Microsoft.EntityFrameworkCore;

namespace AirCoverage.Api.Data;

public class CacheDbContext : DbContext
{
    public CacheDbContext(DbContextOptions<CacheDbContext> options) : base(options) { }

    public DbSet<CachedItem> Items => Set<CachedItem>();
    public DbSet<SyncState> SyncState => Set<SyncState>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<CachedItem>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).ValueGeneratedNever(); // ADO owns the id
        });
        b.Entity<SyncState>().HasKey(s => s.Id);
    }
}
```
Run: `git mv AirCoverage.Api/Data/AppDbContext.cs AirCoverage.Api/Data/CacheDbContext.cs` (then paste the content above).

- [ ] **Step 3: Update the design-time factory**

Replace `AirCoverage.Api/Data/DesignTimeDbContextFactory.cs` body so it builds `CacheDbContext`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AirCoverage.Api.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CacheDbContext>
{
    public CacheDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CacheDbContext>()
            .UseSqlite("Data Source=aircoverage.db")
            .Options;
        return new CacheDbContext(options);
    }
}
```

- [ ] **Step 4: Delete the old model and seeder**

Run:
```bash
git rm AirCoverage.Api/Models/Item.cs AirCoverage.Api/Data/DbSeeder.cs
```
(Build will break until `Program.cs` and endpoints are updated in later tasks — that's expected; do not commit yet.)

- [ ] **Step 5: Commit (WIP, builds after Task 14)**

Defer the commit until the build is green again (end of Task 14). For now:
```bash
git add AirCoverage.Api/Data
git commit -m "feat: cache schema (CachedItem, SyncState); retire Item model + seeder" --no-verify
```
(`--no-verify` only because no build-hook exists; the project intentionally doesn't compile mid-refactor. If your team has a pre-commit build hook, instead reorder Tasks 10–14 onto a branch and commit once at the end.)

### Task 11: Regenerate the migration

**Files:** `AirCoverage.Api/Migrations/*`

- [ ] **Step 1: Remove old migrations and add the cache migration**

Run:
```bash
git rm -r AirCoverage.Api/Migrations
dotnet ef migrations add CacheSchema --project AirCoverage.Api --output-dir Migrations
```
Expected: "Build started... Build succeeded. Done." (Tasks 12–14 must already be drafted if the project doesn't compile — if `dotnet ef` fails to build, complete Tasks 12–14 first, then run this step.)

> **Sequencing note:** EF migration generation requires the project to compile. If you are working strictly top-to-bottom, do Tasks 12–14 (store, endpoints, Program.cs) before running this step, then return here.

- [ ] **Step 2: Commit**

```bash
git add AirCoverage.Api/Migrations
git commit -m "feat: regenerate EF migration for cache schema"
```

### Task 12: AdoItemStore

**Files:**
- Create: `AirCoverage.Api/Stores/AdoItemStore.cs`
- Test: `AirCoverage.Api.Tests/AdoItemStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using AirCoverage.Api.Abstractions;
using AirCoverage.Api.Ado;
using AirCoverage.Api.Data;
using AirCoverage.Api.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AirCoverage.Api.Tests;

public class AdoItemStoreTests
{
    private static CacheDbContext NewCache()
    {
        var options = new DbContextOptionsBuilder<CacheDbContext>()
            .UseSqlite("Data Source=:memory:").Options;
        var ctx = new CacheDbContext(options);
        ctx.Database.OpenConnection();
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static AdoWorkItem Wi(int id, string state, string tags) => new(
        id, "T", "<div>d</div>", 2, state, "Alex Reyes", tags, $"https://x/{id}",
        new DateTime(2026, 1, 1), new DateTime(2026, 1, 2));

    private static AdoItemStore NewStore(CacheDbContext cache, IAzureDevOpsClient client) =>
        new(cache, client, Options.Create(new AdoOptions { Tag = "AirCoverage" }));

    [Fact]
    public async Task GetItems_open_reads_from_cache()
    {
        var cache = NewCache();
        cache.Items.Add(new CachedItem { Id = 5, Title = "Cached", Priority = "High", Status = "New" });
        await cache.SaveChangesAsync();
        var store = NewStore(cache, Substitute.For<IAzureDevOpsClient>());

        var items = await store.GetItemsAsync(ItemScope.Open, CancellationToken.None);

        Assert.Equal("Cached", Assert.Single(items).Title);
    }

    [Fact]
    public async Task Create_writes_through_to_cache()
    {
        var cache = NewCache();
        var client = Substitute.For<IAzureDevOpsClient>();
        client.CreateAsync(Arg.Any<IReadOnlyList<JsonPatchOperation>>(), Arg.Any<CancellationToken>())
            .Returns(Wi(11, "New", "AirCoverage"));
        var store = NewStore(cache, client);

        var dto = await store.CreateAsync(
            new ItemInput("New item", "d", "High", "New", "Support", "", "", ""), CancellationToken.None);

        Assert.Equal(11, dto.Id);
        Assert.Equal(1, await cache.Items.CountAsync());
    }

    [Fact]
    public async Task Update_to_closed_evicts_from_cache()
    {
        var cache = NewCache();
        cache.Items.Add(new CachedItem { Id = 7, Title = "Open", Status = "New", Tags = "AirCoverage" });
        await cache.SaveChangesAsync();
        var client = Substitute.For<IAzureDevOpsClient>();
        client.UpdateAsync(7, Arg.Any<IReadOnlyList<JsonPatchOperation>>(), Arg.Any<CancellationToken>())
            .Returns(Wi(7, "Closed", "AirCoverage"));
        var store = NewStore(cache, client);

        await store.UpdateAsync(7, new ItemInput("Open", "d", "High", "Closed", "", "", "", ""), CancellationToken.None);

        Assert.Equal(0, await cache.Items.CountAsync());
    }

    [Fact]
    public async Task Delete_detags_in_ado_and_evicts()
    {
        var cache = NewCache();
        cache.Items.Add(new CachedItem { Id = 8, Title = "X", Tags = "AirCoverage; source:Eng" });
        await cache.SaveChangesAsync();
        var client = Substitute.For<IAzureDevOpsClient>();
        client.GetWorkItemAsync(8, Arg.Any<CancellationToken>()).Returns(Wi(8, "New", "AirCoverage; source:Eng"));
        client.UpdateAsync(8, Arg.Any<IReadOnlyList<JsonPatchOperation>>(), Arg.Any<CancellationToken>())
            .Returns(Wi(8, "New", "source:Eng"));
        var store = NewStore(cache, client);

        var ok = await store.DeleteAsync(8, CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(0, await cache.Items.CountAsync());
        await client.Received().UpdateAsync(8,
            Arg.Is<IReadOnlyList<JsonPatchOperation>>(ops =>
                !((string)ops.Single(o => o.path == "/fields/System.Tags").value!).Contains("AirCoverage")),
            Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter AdoItemStoreTests`
Expected: FAIL — `AdoItemStore` does not exist.

- [ ] **Step 3: Implement the store**

```csharp
using AirCoverage.Api.Abstractions;
using AirCoverage.Api.Ado;
using AirCoverage.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AirCoverage.Api.Stores;

public class AdoItemStore : IItemStore
{
    private static readonly string[] ClosedStatuses = { "Closed", "Resolved" };

    private readonly CacheDbContext _cache;
    private readonly IAzureDevOpsClient _ado;
    private readonly AdoOptions _opt;

    public AdoItemStore(CacheDbContext cache, IAzureDevOpsClient ado, IOptions<AdoOptions> options)
    {
        _cache = cache;
        _ado = ado;
        _opt = options.Value;
    }

    public async Task<IReadOnlyList<ItemDto>> GetItemsAsync(ItemScope scope, CancellationToken ct)
    {
        if (scope == ItemScope.Open)
        {
            var rows = await _cache.Items.AsNoTracking().ToListAsync(ct);
            return rows.Select(ToDto).ToList();
        }

        // Closed/Resolved are not cached: bounded on-demand query.
        var ids = await _ado.QueryClosedIdsAsync(_opt.ClosedWindowDays, ct);
        var work = await _ado.GetWorkItemsAsync(ids, ct);
        return work.Select(ItemMapper.ToDto).ToList();
    }

    public async Task<ItemDto?> GetItemAsync(int id, CancellationToken ct)
    {
        var cached = await _cache.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (cached is not null) return ToDto(cached);
        var wi = await _ado.GetWorkItemAsync(id, ct);
        return wi is null ? null : ItemMapper.ToDto(wi);
    }

    public async Task<ItemDto> CreateAsync(ItemInput input, CancellationToken ct)
    {
        var ops = ItemMapper.ToPatch(input, existingTags: "", _opt.Tag);
        var wi = await _ado.CreateAsync(ops, ct);
        await UpsertCacheAsync(wi, ct);
        return ItemMapper.ToDto(wi);
    }

    public async Task<ItemDto?> UpdateAsync(int id, ItemInput input, CancellationToken ct)
    {
        var existingTags = await ExistingTagsAsync(id, ct);
        if (existingTags is null) return null;
        var ops = ItemMapper.ToPatch(input, existingTags, _opt.Tag);
        var wi = await _ado.UpdateAsync(id, ops, ct);
        await UpsertCacheAsync(wi, ct);
        return ItemMapper.ToDto(wi);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var existingTags = await ExistingTagsAsync(id, ct);
        if (existingTags is null) return false;

        var keptTags = ItemMapper.ParseTags(existingTags)
            .Where(t => !t.Equals(_opt.Tag, StringComparison.OrdinalIgnoreCase));
        var ops = new List<JsonPatchOperation>
        {
            new("add", "/fields/System.Tags", string.Join("; ", keptTags)),
        };
        await _ado.UpdateAsync(id, ops, ct);
        await EvictAsync(id, ct);
        return true;
    }

    private async Task<string?> ExistingTagsAsync(int id, CancellationToken ct)
    {
        var cached = await _cache.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (cached is not null) return cached.Tags;
        var wi = await _ado.GetWorkItemAsync(id, ct);
        return wi?.Tags;
    }

    private async Task UpsertCacheAsync(AdoWorkItem wi, CancellationToken ct)
    {
        if (ClosedStatuses.Contains(wi.State)) { await EvictAsync(wi.Id, ct); return; }

        var dto = ItemMapper.ToDto(wi);
        var row = await _cache.Items.FirstOrDefaultAsync(i => i.Id == wi.Id, ct);
        if (row is null) { row = new CachedItem { Id = wi.Id }; _cache.Items.Add(row); }
        row.Title = dto.Title; row.Description = dto.Description; row.Priority = dto.Priority;
        row.Status = dto.Status; row.RequestedBy = dto.RequestedBy; row.Assignee = dto.Assignee;
        row.TicketType = dto.TicketType; row.TicketRef = dto.TicketRef; row.Tags = wi.Tags;
        row.Url = dto.Url; row.Received = dto.Received; row.Updated = dto.Updated;
        await _cache.SaveChangesAsync(ct);
    }

    private async Task EvictAsync(int id, CancellationToken ct)
    {
        var row = await _cache.Items.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (row is not null) { _cache.Items.Remove(row); await _cache.SaveChangesAsync(ct); }
    }

    private static ItemDto ToDto(CachedItem c) => new(
        c.Id, c.Id.ToString(), c.Title, c.Description, c.Priority, c.Status,
        c.RequestedBy, c.Assignee, c.TicketType, c.TicketRef, c.Url, c.Received, c.Updated);
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter AdoItemStoreTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add AirCoverage.Api/Stores/AdoItemStore.cs AirCoverage.Api.Tests/AdoItemStoreTests.cs
git commit -m "feat: AdoItemStore (cache reads, write-through, close-eviction, detag-delete)"
```

---

## Phase 4 — Sync, endpoints, wiring

### Task 13: SyncService

**Files:**
- Create: `AirCoverage.Api/Sync/SyncService.cs`
- Create: `AirCoverage.Api/Sync/CacheSynchronizer.cs`
- Test: `AirCoverage.Api.Tests/SyncServiceTests.cs`

> The pollable logic lives in a plain `CacheSynchronizer` so it's unit-testable without timers; `SyncService` is the thin `BackgroundService` that calls it on a schedule.

- [ ] **Step 1: Write the failing tests**

```csharp
using AirCoverage.Api.Ado;
using AirCoverage.Api.Data;
using AirCoverage.Api.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AirCoverage.Api.Tests;

public class SyncServiceTests
{
    private static CacheDbContext NewCache()
    {
        var options = new DbContextOptionsBuilder<CacheDbContext>().UseSqlite("Data Source=:memory:").Options;
        var ctx = new CacheDbContext(options);
        ctx.Database.OpenConnection();
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static AdoWorkItem Wi(int id, string state) => new(
        id, "T", "d", 2, state, null, "AirCoverage", $"https://x/{id}", new DateTime(2026, 1, 1), new DateTime(2026, 1, 2));

    [Fact]
    public async Task DeltaAsync_upserts_changed_open_items_and_prunes_closed()
    {
        var cache = NewCache();
        cache.Items.Add(new CachedItem { Id = 1, Title = "old", Status = "New" });
        await cache.SaveChangesAsync();

        var client = Substitute.For<IAzureDevOpsClient>();
        client.QueryIdsChangedSinceAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new[] { 1, 2 });
        client.GetWorkItemsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Wi(1, "Closed"), Wi(2, "Active") });

        var sync = new CacheSynchronizer(cache, client, Options.Create(new AdoOptions()));
        await sync.DeltaAsync(CancellationToken.None);

        Assert.Null(await cache.Items.FindAsync(1)); // closed -> pruned
        Assert.NotNull(await cache.Items.FindAsync(2)); // active -> upserted
        Assert.NotNull((await cache.SyncState.SingleAsync()).LastSuccessfulSync);
    }

    [Fact]
    public async Task ReconcileAsync_drops_orphans_not_in_open_set()
    {
        var cache = NewCache();
        cache.Items.Add(new CachedItem { Id = 1, Status = "New" });
        cache.Items.Add(new CachedItem { Id = 2, Status = "New" });
        await cache.SaveChangesAsync();

        var client = Substitute.For<IAzureDevOpsClient>();
        client.QueryOpenIdsAsync(Arg.Any<CancellationToken>()).Returns(new[] { 2 });
        client.GetWorkItemsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Wi(2, "Active") });

        var sync = new CacheSynchronizer(cache, client, Options.Create(new AdoOptions()));
        await sync.ReconcileAsync(CancellationToken.None);

        Assert.Null(await cache.Items.FindAsync(1)); // orphan dropped
        Assert.NotNull(await cache.Items.FindAsync(2));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter SyncServiceTests`
Expected: FAIL — `CacheSynchronizer` does not exist.

- [ ] **Step 3: Implement `CacheSynchronizer` and `SyncService`**

`CacheSynchronizer.cs`:
```csharp
using AirCoverage.Api.Ado;
using AirCoverage.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AirCoverage.Api.Sync;

/// <summary>Timer-free cache sync logic (unit-testable).</summary>
public class CacheSynchronizer
{
    private static readonly string[] Closed = { "Closed", "Resolved" };
    private readonly CacheDbContext _cache;
    private readonly IAzureDevOpsClient _ado;
    private readonly AdoOptions _opt;

    public CacheSynchronizer(CacheDbContext cache, IAzureDevOpsClient ado, IOptions<AdoOptions> options)
    {
        _cache = cache; _ado = ado; _opt = options.Value;
    }

    public async Task DeltaAsync(CancellationToken ct)
    {
        var state = await GetStateAsync(ct);
        var since = state.LastChangedWatermark ?? DateTime.UtcNow.AddDays(-_opt.ClosedWindowDays);
        var ids = await _ado.QueryIdsChangedSinceAsync(since, ct);
        var work = await _ado.GetWorkItemsAsync(ids, ct);
        foreach (var wi in work) await ApplyAsync(wi, ct);

        var maxChanged = work.Select(w => w.ChangedDate ?? DateTime.MinValue).DefaultIfEmpty(since).Max();
        state.LastChangedWatermark = maxChanged > since ? maxChanged : since;
        state.LastSuccessfulSync = DateTime.UtcNow;
        await _cache.SaveChangesAsync(ct);
    }

    public async Task ReconcileAsync(CancellationToken ct)
    {
        var openIds = (await _ado.QueryOpenIdsAsync(ct)).ToHashSet();
        var work = await _ado.GetWorkItemsAsync(openIds.ToList(), ct);
        foreach (var wi in work) await ApplyAsync(wi, ct);

        var orphans = await _cache.Items.Where(i => !openIds.Contains(i.Id)).ToListAsync(ct);
        _cache.Items.RemoveRange(orphans);

        var state = await GetStateAsync(ct);
        state.LastSuccessfulSync = DateTime.UtcNow;
        await _cache.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(AdoWorkItem wi, CancellationToken ct)
    {
        var existing = await _cache.Items.FirstOrDefaultAsync(i => i.Id == wi.Id, ct);
        if (Closed.Contains(wi.State))
        {
            if (existing is not null) _cache.Items.Remove(existing);
            return;
        }
        var dto = ItemMapper.ToDto(wi);
        if (existing is null) { existing = new CachedItem { Id = wi.Id }; _cache.Items.Add(existing); }
        existing.Title = dto.Title; existing.Description = dto.Description; existing.Priority = dto.Priority;
        existing.Status = dto.Status; existing.RequestedBy = dto.RequestedBy; existing.Assignee = dto.Assignee;
        existing.TicketType = dto.TicketType; existing.TicketRef = dto.TicketRef; existing.Tags = wi.Tags;
        existing.Url = dto.Url; existing.Received = dto.Received; existing.Updated = dto.Updated;
    }

    private async Task<SyncState> GetStateAsync(CancellationToken ct)
    {
        var state = await _cache.SyncState.FirstOrDefaultAsync(s => s.Id == 1, ct);
        if (state is null) { state = new SyncState { Id = 1 }; _cache.SyncState.Add(state); }
        return state;
    }
}
```

`SyncService.cs`:
```csharp
using AirCoverage.Api.Data;
using Microsoft.Extensions.Options;
using AirCoverage.Api.Ado;

namespace AirCoverage.Api.Sync;

public class SyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly AdoOptions _opt;
    private readonly ILogger<SyncService> _log;

    public SyncService(IServiceScopeFactory scopes, IOptions<AdoOptions> options, ILogger<SyncService> log)
    {
        _scopes = scopes; _opt = options.Value; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastReconcile = DateTime.MinValue;
        // Initial full reconcile so the cache is warm at startup.
        await RunReconcileAsync(stoppingToken);
        lastReconcile = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_opt.PollSeconds), stoppingToken);
                using var scope = _scopes.CreateScope();
                var sync = scope.ServiceProvider.GetRequiredService<CacheSynchronizer>();
                await sync.DeltaAsync(stoppingToken);

                if ((DateTime.UtcNow - lastReconcile).TotalSeconds >= _opt.ReconcileSeconds)
                {
                    await RunReconcileAsync(stoppingToken);
                    lastReconcile = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "Cache sync iteration failed; will retry."); }
        }
    }

    private async Task RunReconcileAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<CacheSynchronizer>();
            await sync.ReconcileAsync(ct);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Cache reconcile failed; serving existing cache."); }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter SyncServiceTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add AirCoverage.Api/Sync AirCoverage.Api.Tests/SyncServiceTests.cs
git commit -m "feat: cache synchronizer (delta + reconcile) and background SyncService"
```

### Task 14: Endpoints + Program.cs wiring

**Files:**
- Modify: `AirCoverage.Api/Endpoints/ItemsEndpoints.cs`
- Create: `AirCoverage.Api/Endpoints/SyncEndpoints.cs`
- Modify: `AirCoverage.Api/Program.cs`
- Modify: `AirCoverage.Api/Dtos/Dtos.cs`

- [ ] **Step 1: Trim `Dtos.cs` to auth-only records**

Replace `AirCoverage.Api/Dtos/Dtos.cs` with:
```csharp
namespace AirCoverage.Api.Dtos;

public record LoginRequest(string Username, string Password);
public record UserResponse(string Username, string DisplayName);
```
(`ItemInput` now lives in `Abstractions/IItemStore.cs`.)

- [ ] **Step 2: Rewrite `ItemsEndpoints.cs` to use `IItemStore`**

Replace the entire file with:
```csharp
using AirCoverage.Api.Abstractions;

namespace AirCoverage.Api.Endpoints;

public static class ItemsEndpoints
{
    public static IEndpointRouteBuilder MapItemsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/items").RequireAuthorization();

        group.MapGet("/", async (IItemStore store, string? scope, CancellationToken ct) =>
        {
            var itemScope = string.Equals(scope, "closed", StringComparison.OrdinalIgnoreCase)
                ? ItemScope.Closed : ItemScope.Open;
            return Results.Ok(await store.GetItemsAsync(itemScope, ct));
        });

        group.MapGet("/{id:int}", async (int id, IItemStore store, CancellationToken ct) =>
            await store.GetItemAsync(id, ct) is { } dto ? Results.Ok(dto) : Results.NotFound());

        group.MapPost("/", async (ItemInput input, IItemStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.Title)) return Results.BadRequest(new { error = "Title is required." });
            var dto = await store.CreateAsync(input, ct);
            return Results.Created($"/api/items/{dto.Id}", dto);
        });

        group.MapPut("/{id:int}", async (int id, ItemInput input, IItemStore store, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.Title)) return Results.BadRequest(new { error = "Title is required." });
            return await store.UpdateAsync(id, input, ct) is { } dto ? Results.Ok(dto) : Results.NotFound();
        });

        group.MapDelete("/{id:int}", async (int id, IItemStore store, CancellationToken ct) =>
            await store.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        return app;
    }
}
```

- [ ] **Step 3: Add the sync-status endpoint**

`SyncEndpoints.cs`:
```csharp
using AirCoverage.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AirCoverage.Api.Endpoints;

public static class SyncEndpoints
{
    public static IEndpointRouteBuilder MapSyncApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sync/status", async (CacheDbContext cache, CancellationToken ct) =>
        {
            var state = await cache.SyncState.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, ct);
            return Results.Ok(new { lastSync = state?.LastSuccessfulSync });
        }).RequireAuthorization();
        return app;
    }
}
```

- [ ] **Step 4: Update `Program.cs` DI + startup**

In `AirCoverage.Api/Program.cs`:

(a) Replace the EF/DbContext registration block:
```csharp
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=aircoverage.db";
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
```
with:
```csharp
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=aircoverage.db";
builder.Services.AddDbContext<CacheDbContext>(options => options.UseSqlite(connectionString));

// ADO options + authenticated, resilient HTTP client + store + background sync.
builder.Services.Configure<AdoOptions>(builder.Configuration.GetSection(AdoOptions.Section));
builder.Services.AddTransient<PatAuthHandler>();
builder.Services.AddHttpClient<IAzureDevOpsClient, AzureDevOpsClient>()
    .AddHttpMessageHandler<PatAuthHandler>()
    .AddStandardResilienceHandler();   // Polly: retries (incl. 429), timeout, circuit breaker
builder.Services.AddScoped<IItemStore, AdoItemStore>();
builder.Services.AddScoped<CacheSynchronizer>();
builder.Services.AddHostedService<SyncService>();
```

(b) Add the using directives at the top:
```csharp
using AirCoverage.Api.Abstractions;
using AirCoverage.Api.Ado;
using AirCoverage.Api.Stores;
using AirCoverage.Api.Sync;
```

(c) Replace the migrate-and-seed block:
```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    DbSeeder.Seed(db);
}
```
with (migrate only — ADO seeds the data via the first reconcile):
```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CacheDbContext>();
    db.Database.Migrate();
}
```

(d) Register the sync endpoints next to the others:
```csharp
app.MapAuthApi();
app.MapItemsApi();
app.MapSyncApi();
```

- [ ] **Step 5: Run the EF migration now that the project compiles**

(Complete Task 11 here if you deferred it.) Run:
```bash
dotnet build AirCoverage.Api/AirCoverage.Api.csproj
```
Expected: Build succeeded.

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test`
Expected: PASS (all suites).

- [ ] **Step 7: Commit**

```bash
git add AirCoverage.Api/Endpoints AirCoverage.Api/Program.cs AirCoverage.Api/Dtos/Dtos.cs
git commit -m "feat: wire IItemStore/ADO/sync into endpoints and DI"
```

---

## Phase 5 — Frontend

### Task 15: Item-number deep link + types

**Files:**
- Modify: `web/src/types.ts`
- Modify: `web/src/components/QueueTable.vue`

- [ ] **Step 1: Add `url` to the `Item` type**

In `web/src/types.ts`, add to both `Item` and `ItemDraft` interfaces:
```ts
  url: string
```
(Place it next to `ticketRef`. For `ItemDraft`, it is optional context only — add `url?: string`.)

- [ ] **Step 2: Normalize `url` in the items store**

In `web/src/stores/items.ts`, inside `normalize(...)`, add:
```ts
    url: (raw.url as string) ?? '',
```

- [ ] **Step 3: Make the Item # a deep link**

In `web/src/components/QueueTable.vue`, replace the Item # cell:
```html
<td><span class="id-link" @click.stop="items.openDetail(it)">{{ it.number }}</span></td>
```
with:
```html
<td>
  <a v-if="it.url" class="id-link" :href="it.url" target="_blank" rel="noopener" @click.stop>{{ it.number }}</a>
  <span v-else class="id-link" @click.stop="items.openDetail(it)">{{ it.number }}</span>
</td>
```

- [ ] **Step 4: Commit**

```bash
git add web/src/types.ts web/src/stores/items.ts web/src/components/QueueTable.vue
git commit -m "feat(web): deep-link Item # to the ADO work item"
```

### Task 16: On-demand closed tabs + staleness indicator

**Files:**
- Modify: `web/src/stores/items.ts`
- Modify: `web/src/components/PageHeader.vue`
- Modify: `web/src/api/client.ts` (no change if `get` already exists — verify)

- [ ] **Step 1: Add closed-scope loading + staleness to the items store**

In `web/src/stores/items.ts`:

(a) Add state fields:
```ts
    closedItems: [] as Item[],
    closedLoaded: false,
    lastSync: null as string | null,
```

(b) Add an action to fetch closed items on demand and a sync-status fetch:
```ts
    async loadClosed() {
      const data = await api.get<Record<string, unknown>[]>('/api/items?scope=closed')
      this.closedItems = data.map(normalize)
      this.closedLoaded = true
    },

    async loadSyncStatus() {
      try {
        const s = await api.get<{ lastSync: string | null }>('/api/sync/status')
        this.lastSync = s.lastSync
      } catch {
        /* ignore */
      }
    },
```

(c) Make `filteredByTab` use closed items for closed scopes. Replace the getter with:
```ts
    filteredByTab(state): Item[] {
      const closedScope = ['Resolved', 'Closed', 'all'].includes(state.activeTab)
      const source = closedScope ? [...state.items, ...state.closedItems] : state.items
      return source.filter((it) => {
        if (state.activeTab === 'all') return true
        if (state.activeTab === 'open') return it.status !== 'Closed' && it.status !== 'Resolved'
        return it.status === state.activeTab
      })
    },
```

(d) In the `setActiveTab` flow: add an action and call it from the component instead of assigning `activeTab` directly:
```ts
    async setTab(key: string) {
      this.activeTab = key
      if (['Resolved', 'Closed', 'all'].includes(key) && !this.closedLoaded) {
        await this.loadClosed()
      }
    },
```

- [ ] **Step 2: Use `setTab` in the tab bar**

In `web/src/components/QueueCard.vue`, change the tab button handler:
```html
@click="items.activeTab = t.key"
```
to:
```html
@click="items.setTab(t.key)"
```

- [ ] **Step 3: Show "synced Xs ago" in the page header**

In `web/src/components/PageHeader.vue`:

(a) In `<script setup>` add:
```ts
import { computed, onMounted, onUnmounted, ref } from 'vue'
const now = ref(Date.now())
let timer: number | undefined
onMounted(() => {
  items.loadSyncStatus()
  timer = window.setInterval(() => { now.value = Date.now(); items.loadSyncStatus() }, 15000)
})
onUnmounted(() => { if (timer) clearInterval(timer) })
const syncedAgo = computed(() => {
  if (!items.lastSync) return ''
  const secs = Math.max(0, Math.round((now.value - new Date(items.lastSync).getTime()) / 1000))
  return secs < 60 ? `synced ${secs}s ago` : `synced ${Math.round(secs / 60)}m ago`
})
```

(b) In the template, add inside `.page-head-right` (before the export icon):
```html
<span class="sync-note" v-if="syncedAgo">{{ syncedAgo }}</span>
```

(c) Add to `web/src/style.css`:
```css
.sync-note { font-size: 11.5px; color: var(--jf-muted-2); }
```

- [ ] **Step 4: Commit**

```bash
git add web/src
git commit -m "feat(web): on-demand closed tabs + cache staleness indicator"
```

> **Verification (requires Node 18+ — this host has Node 14):** `cd web && npm install && npm run build` should succeed. The Docker image build (`docker compose build`) also exercises this via Node 20 and is the canonical check.

---

## Phase 6 — Cutover & docs

### Task 17: PAT secret wiring + README + compose

**Files:**
- Modify: `docker-compose.yml`
- Modify: `README.md`

- [ ] **Step 1: Pass the PAT + ADO config into the container**

In `docker-compose.yml`, add an `environment` block to the `aircoverage` service:
```yaml
    environment:
      - Ado__OrgUrl=${ADO_ORG_URL}
      - Ado__Project=${ADO_PROJECT}
      - Ado__Pat=${ADO_PAT}
      - Ado__Tag=AirCoverage
      - Ado__WorkItemType=Bug
```
Create `.env.example` at the repo root:
```bash
ADO_ORG_URL=https://dev.azure.com/your-org
ADO_PROJECT=YourProject
ADO_PAT=your-pat-with-work-items-read-write
```
And confirm `.env` is git-ignored — add to `.gitignore`:
```
# Local secrets
.env
```

- [ ] **Step 2: Document the ADO backend in the README**

Add a "Data backend (Azure DevOps)" section to `README.md` covering: items are tagged work items; the PAT scopes needed (Work Items: Read & Write); how to set `.env`; that closed items fall off; and the cache/sync behavior. (Write the prose to match the spec sections 4–9.)

- [ ] **Step 3: Local secret for `dotnet run`**

Document using user-secrets locally:
```bash
cd AirCoverage.Api
dotnet user-secrets init
dotnet user-secrets set "Ado:OrgUrl" "https://dev.azure.com/your-org"
dotnet user-secrets set "Ado:Project" "YourProject"
dotnet user-secrets set "Ado:Pat" "your-pat"
```

- [ ] **Step 4: Commit**

```bash
git add docker-compose.yml .env.example .gitignore README.md
git commit -m "docs+ops: wire ADO PAT config; document the ADO backend"
```

### Task 18: End-to-end verification against ADO

- [ ] **Step 1: Build the image**

Run: `docker compose build`
Expected: build succeeds (SPA + API).

- [ ] **Step 2: Run with real ADO config**

Run: `cp .env.example .env` and fill in a real org/project/PAT, then `docker compose up -d`.
Expected: container starts; logs show the initial reconcile (a WIQL + batch fetch).

- [ ] **Step 3: Verify reads**

Run:
```bash
curl -sk -c cookies.txt -X POST https://localhost:8443/api/auth/login -H "Content-Type: application/json" -d '{"username":"devteam","password":"aircoverage"}' >/dev/null
curl -sk -b cookies.txt https://localhost:8443/api/items | head -c 400
```
Expected: JSON array of your tagged open ADO work items (or `[]` if none tagged yet).

- [ ] **Step 4: Verify a round-trip create**

Run:
```bash
curl -sk -b cookies.txt -X POST https://localhost:8443/api/items -H "Content-Type: application/json" \
  -d '{"title":"AC smoke test","description":"created via API","priority":"High","status":"New","requestedBy":"Eng","assignee":"","ticketType":"","ticketRef":""}'
```
Expected: `201` with an `id` equal to a **real ADO work-item id**; the work item exists in ADO carrying the `AirCoverage` tag. Confirm it appears in `GET /api/items`.

- [ ] **Step 5: Verify close-fall-off**

PUT the same item with `"status":"Closed"`, then `GET /api/items` — it should no longer appear in the open list; `GET /api/items?scope=closed` should include it (within the 30-day window).

- [ ] **Step 6: Tear down**

Run: `docker compose down` (keep the volume) or `docker compose down -v` (fresh).

- [ ] **Step 7: Commit any doc fixes discovered during verification**

```bash
git add -A && git commit -m "docs: verification notes for ADO backend"
```

---

## Self-Review (completed during authoring)

- **Spec coverage:** §3 architecture → Tasks 3,8,9,12,13,14. §5 mapping → Tasks 4,6,7. §6 reads/closed-tabs → Tasks 12,16. §7 writes/delete-as-detag → Task 12. §8 freshness → Task 13. §9 PAT auth → Tasks 8,17. §10 resilience → Task 14 (`AddStandardResilienceHandler`). §11 frontend → Tasks 15,16. §12 code changes → Tasks 10,14. §13 testing → Tasks 4,6,7,9,12,13. §14 phased rollout = phases here. §15 decisions baked into mapping/store. ✅
- **Placeholder scan:** no TBD/TODO; every code step shows full code; commands have expected output. ✅
- **Type consistency:** `ItemDto`/`ItemInput` (Task 3) used consistently in store (12), endpoints (14), tests; `AdoWorkItem`/`JsonPatchOperation` (Task 5) used in client (9), mapper (6,7), store (12), sync (13); `CachedItem`/`SyncState` + `CacheDbContext` consistent across 10/12/13/14. `ToPatch(input, existingTags, requiredTag)` signature matches all call sites. ✅
- **Known sequencing caveat:** the mid-refactor build gap (Tasks 10–14) is called out explicitly with guidance; EF migration (Task 11) notes the compile dependency.
