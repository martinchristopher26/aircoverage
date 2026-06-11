# Design Spec — Replace SQLite store-of-record with Azure DevOps

- **Status:** Approved (design) — ready for implementation planning
- **Date:** 2026-06-11
- **Author:** Air Coverage team (via Claude Code)
- **Supersedes data layer of:** the initial scaffold (SQLite via EF Core as system-of-record)

## 1. Summary

Azure DevOps (ADO) becomes the **single source of truth** for Air Coverage items.
SQLite is **demoted from system-of-record to a read-through cache** that holds only
the *open* items, so API pulls stay small and the queue stays snappy. Writes go
straight to the ADO REST API; the cache is kept current via write-through plus a
cheap delta poll and a periodic reconcile. **Closed/Resolved items fall off** the
active queue (pruned from the cache) for cleanup and to bound the data we pull.

Air Coverage items are **work items identified by a tag (default `AirCoverage`) and/or
an Area Path**, on an **Agile** process. The backend authenticates to ADO with a
**Personal Access Token (PAT) used in every environment, including local dev**,
isolated behind a single seam so it can be swapped to Entra ID (OAuth / managed
identity) later without touching the data model — mirroring the app's existing
v1→v2 auth philosophy.

## 2. Goals / Non-goals

**Goals**
- ADO is authoritative; no business data is stored as system-of-record locally.
- Reads (queue list, tabs, filtering, sort) remain instant — served from cache.
- Cache "seems pretty up to date" with minimal ADO traffic.
- Closed/Resolved items drop out of the active queue automatically.
- PAT auth working everywhere; clean swap path to Entra later.
- Introduce an `IItemStore` seam so the data layer is swappable and testable.

**Non-goals (this phase)**
- ADO Service Hooks / webhooks (documented as an upgrade, not built).
- Replacing the app's user auth (shared credential → cookie) — unchanged.
- Replacing the free-text developer list with full ADO identity management (a v2 follow-up).
- Bidirectional sync of arbitrary ADO fields beyond the mapped set.

## 3. Architecture

```
            writes (create / update / state / delete)
  SPA ──▶ /api/items ──▶ IItemStore ───────────────────▶  Azure DevOps REST API
                            │  ▲                               │  (PAT auth)
                  reads     │  │ write-through                │
                            ▼  │                               ▼
                       SQLite cache  ◀── SyncService (delta poll + reconcile)
                       (open items only)
```

- **`IItemStore`** — the seam the endpoints depend on. The endpoints currently call
  `AppDbContext` directly; extracting this interface (over the existing SQLite code)
  is the first, behavior-preserving step.
- **`AzureDevOpsClient`** — the only component that knows ADO exists. Wraps WIQL,
  batched work-item GET, and JSON-Patch create/update.
- **SQLite cache** — mirrors open items; powers fast client-side filtering and live
  tab counts (unchanged SPA behavior).
- **`SyncService`** — a hosted `BackgroundService` that keeps the cache fresh and
  prunes items that have left the open set.

### Components (new/changed)

| Component | Responsibility |
|---|---|
| `IItemStore` | `GetItems`, `GetItem`, `Create`, `Update`, `Delete` — the API's view of storage |
| `AdoItemStore : IItemStore` | Orchestrates: reads from cache, writes through to ADO + cache |
| `AzureDevOpsClient` | Raw ADO REST calls (WIQL, batch get, JSON-Patch), field (un)mapping |
| `ItemMapper` | Pure mapping AC ⇄ ADO work-item fields (no I/O; unit-tested) |
| `SyncService` | Background delta poll + periodic full reconcile; maintains watermark |
| Cache `DbContext` | `CachedItem` + `SyncState` (watermark / last-sync time) |
| `AdoOptions` | Bound config (org, project, PAT, tag, area path, WIT, poll interval) |
| Delegating handler / `IAdoCredentialProvider` | Injects PAT today; Entra swap point |

## 4. What an item *is*

An Air Coverage item is a **work item carrying tag `AirCoverage`** (configurable) and/or
a configured **Area Path**, of type **Bug** (default; configurable to Issue), on the
Agile process. Because the item *is* an ADO work item:

- The display **Item #** is the **real ADO work-item ID** and deep-links to
  `https://dev.azure.com/{org}/{project}/_workitems/edit/{id}`. The legacy `AC-####`
  scheme is **retired**.
- The old **Ticket** column's "ADO #…" is redundant (the item is the ADO ticket).
  ConnectWise references, if tracked, move to a tag/custom field.

## 5. Field mapping (AC ⇄ ADO Agile)

| AC field | ADO field | Notes |
|---|---|---|
| `Number` (display) | `System.Id` | Real work-item ID; deep-links to ADO. `AC-####` retired. |
| `Title` | `System.Title` | 1:1 |
| `Description` | `System.Description` | ADO stores **HTML**; app uses plain text → sanitize/convert both ways |
| `Priority` Critical/High/Medium/Low | `Microsoft.VSTS.Common.Priority` 1/2/3/4 | ADO priority numeric, 1 = highest. (Bug *Severity* is a configurable alternative.) |
| `Status` New/In Progress/Resolved/Closed | `System.State` New/Active/Resolved/Closed | Direct map |
| `Status` = **Waiting** | `System.State`=Active **+ tag `Waiting`** | Agile has no Waiting state. Read: Active + `Waiting` tag ⇒ "Waiting" |
| `Assignee` | `System.AssignedTo` | Identity object; read = displayName. Assignment needs a real ADO identity (name→identity resolution) |
| `RequestedBy` (source) | **tag `source:<value>`** | Tag convention avoids process customization |
| `TicketType`/`TicketRef` (ConnectWise) | tag `cw:<ref>` | ADO linkage is intrinsic now |
| `Received` | `System.CreatedDate` | read-only |
| `Updated` | `System.ChangedDate` | read-only; also the sync watermark |

## 6. Read path & queries

- **Open queue** (default view): served entirely from the SQLite cache → identical
  snappy filtering/tabs/sort to today, with no per-keystroke ADO calls.
- Cache is populated by WIQL, then a batched detail fetch:
  ```sql
  SELECT [System.Id] FROM WorkItems
  WHERE [System.Tags] CONTAINS 'AirCoverage'
    AND [System.State] NOT IN ('Closed','Resolved')
  ORDER BY [Microsoft.VSTS.Common.Priority] ASC, [System.CreatedDate] ASC
  ```
  Then `GET /_apis/wit/workitems?ids=…&fields=…` in batches of ≤200.
- **Closed / Resolved / All tabs**: not cached (closed items fall off). Served by a
  **bounded on-demand** ADO query — items changed/closed in the **last 30 days**
  (configurable window) — fetched only when those tabs are opened.

## 7. Write path

- **Create** → `POST` JSON-Patch document adding the `AirCoverage` tag/area + mapped
  fields; ADO assigns the ID; result is written through into the cache.
- **Update / status advance** → `PATCH` JSON-Patch of changed fields. State transitions
  may carry **rules** (e.g., Resolved requires a reason) → handle `400/409` and surface
  a clear error. "Waiting" = set `Active` + add `Waiting` tag.
- **Delete** → **remove the item from the queue by removing the `AirCoverage` tag**
  (the work item survives in ADO); *not* a hard delete. Then evict from cache.
- Every successful write updates the cache synchronously (write-through) so the acting
  user sees their change instantly; if the change moved the item to Resolved/Closed,
  it is pruned from the cache.

## 8. Cache & freshness

Three mechanisms, each cheap by design:

1. **Write-through** — user edits update the cache synchronously (instant for the actor).
2. **Delta poll** — `SyncService` runs every `Ado:PollSeconds` (default 60s):
   WIQL `WHERE [System.ChangedDate] >= @lastSync AND <tag/area>` returns only changed
   IDs → batch fetch → upsert; items now Resolved/Closed are pruned. Small payloads.
3. **Periodic full reconcile** (default every 5 min): diff the current open-ID set from
   ADO against the cache to drop orphans (hard-deletes / de-tagged items a delta misses).

A `SyncState` row stores the `lastSync` watermark and last successful sync time; the
latter is surfaced to the UI as a staleness indicator ("synced 12s ago").

**Upgrade path (not built):** ADO **Service Hooks** → app endpoint for near-real-time
updates if 60s ever feels stale.

## 9. Auth — PAT everywhere, Entra-ready

- **Config keys:** `Ado:OrgUrl`, `Ado:Project`, `Ado:Pat`, `Ado:Tag` (default
  `AirCoverage`), `Ado:AreaPath` (optional), `Ado:WorkItemType` (default `Bug`),
  `Ado:PollSeconds` (default 60), `Ado:ClosedWindowDays` (default 30).
- **HTTP:** `IHttpClientFactory`-registered client with
  `Authorization: Basic base64(":" + PAT)` and **Polly** for `429 Retry-After` and
  transient retries.
- **Secret delivery:** env `Ado__Pat` / Docker secret in containers; .NET user-secrets
  for local dev. Local uses the same mechanism (a dev PAT) — no special-casing.
- **Entra swap point:** PAT is injected by one delegating handler /
  `IAdoCredentialProvider`. Swapping to Entra OAuth client-credentials or a managed
  identity is a single-class change; nothing else moves.

## 10. Resilience

- **ADO unreachable** → serve cache (stale) and flag staleness in the UI; writes return
  a clear "ADO unavailable" error (no silent data loss).
- **429** → Polly honors `Retry-After`.
- **401/403** → surfaced as a PAT/permission configuration problem.
- **Per-item mapping failures** logged individually; one malformed work item does not
  fail the whole sync.

## 11. Frontend impact (small)

- **Item #** becomes a clickable deep link to the ADO work item.
- **Ticket** column simplified (the item *is* the ADO ticket; ConnectWise optional).
- **Closed/Resolved/All tabs** become bounded on-demand views (§6).
- A subtle **"synced Xs ago"** staleness indicator.
- **Assignee** dropdown ideally sourced from ADO identities; may remain the static dev
  list for this phase.

## 12. Changes to the current codebase

- **Remove:** `Data/DbSeeder.cs` (no seeding business data into ADO);
  `NextNumberAsync` in `Endpoints/ItemsEndpoints.cs` (ADO owns IDs).
- **Repurpose:** `Data/AppDbContext.cs` → cache schema (`CachedItem` + `SyncState`);
  new migration replacing `InitialCreate`.
- **Refactor:** `Endpoints/ItemsEndpoints.cs` → depend on `IItemStore` (same routes &
  response shapes).
- **Add:** `IItemStore`, `AdoItemStore`, `AzureDevOpsClient`, `ItemMapper`,
  `SyncService`, `AdoOptions`, PAT delegating handler, Polly/HttpClient registration.
- **Untouched:** cookie/user auth, HTTPS/Kestrel setup, the SPA's overall structure.

## 13. Testing strategy

- **`ItemMapper`** — pure unit tests for every field, including Waiting↔tag, priority
  numbering, HTML↔text, source/cw tags.
- **`AdoItemStore`** — tested against a **mocked `AzureDevOpsClient`** (read/write/prune
  logic, write-through, eviction on close).
- **`AzureDevOpsClient`** — HTTP-level tests with **WireMock** stubbing ADO responses
  (WIQL shape, batch fetch, JSON-Patch payloads, 429 handling).
- **`SyncService`** — delta upsert, prune-on-close, reconcile-drops-orphans, watermark
  advance, failure leaves cache intact.
- **Integration (opt-in, flagged)** — against a real ADO test project using a dev PAT.

## 14. Phased rollout (each phase shippable & testable)

1. **Extract `IItemStore`** over the existing SQLite code — pure refactor, no behavior
   change, existing tests stay green.
2. **ADO read path** — `AzureDevOpsClient` + WIQL + `ItemMapper`; cache populated from
   ADO; reads served from cache.
3. **ADO write path** — create / update / status advance / delete-as-detag, with rule
   (`400/409`) handling.
4. **Freshness** — `SyncService` delta poll + write-through + periodic reconcile +
   staleness watermark.
5. **Closed-fall-off + closed-tab behavior** — pruning + bounded on-demand closed views;
   frontend tab/link/staleness updates.
6. **Cutover & cleanup** — remove seeding & number-gen; finalize config; wire the PAT
   as a Docker secret; update README.

## 15. Resolved decisions (defaults accepted)

1. **Display ID** = ADO work-item ID; `AC-####` retired.
2. **Waiting** = `System.State`=Active + `Waiting` tag.
3. **Source / RequestedBy** = `source:<value>` tag.
4. **Delete** = remove from queue (detag); work item survives.
5. **Closed/Resolved/All tabs** = bounded on-demand query (last 30 days).
6. **Work item type** = `Bug` (default, configurable); identification by **tag**
   `AirCoverage` (Area Path optional).

## 16. Risks & open items

- **State-transition rules** (Resolved reason, required fields) vary by process config →
  surface ADO's validation errors verbatim; don't pre-validate exhaustively.
- **Assignee identity resolution** — free-text names → ADO identities is fuzzy; phase
  keeps the static list, full resolution is a v2 follow-up.
- **Description HTML** — round-tripping HTML↔plain text can lose formatting; acceptable
  for this tool (plain-text editor).
- **Rate limits** — mitigated by cache + delta polls + Polly; revisit poll interval if
  the queue/org is large.
- **Reconcile cost** — full open-ID reconcile is one cheap WIQL; safe at expected scale.

## 17. Future (post-this-phase)

- ADO **Service Hooks** for push-based freshness.
- **Entra ID** auth to ADO (drop the PAT) via the credential seam.
- App user identity (v2 SSO) → default `AssignedTo` to the signed-in user; real
  identity-backed assignee picker.
