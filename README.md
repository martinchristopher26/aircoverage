# Air Coverage Tracker

An internal dev-support queue for JustFOIA engineering: a priority-sorted list of
"Air Coverage" items worked through statuses (New → In Progress → Waiting →
Resolved → Closed). This is the production app built from the design handoff in
[`design_handoff_air_coverage/`](design_handoff_air_coverage/).

- **Backend:** ASP.NET Core Minimal API (.NET 8 LTS)
- **Data backend:** Azure DevOps (system of truth) + SQLite cache (open items only)
- **Frontend:** Vue 3 + Vite + TypeScript SPA (Pinia for state)
- **Auth (v1):** shared credential → Secure HttpOnly cookie (`RequireAuthorization` on `/api/items*`)
- **Transport:** **HTTPS only on port 8443** — a self-signed `localhost` cert is auto-generated on first run (swap in a real cert for production; see [TLS](#tls--certificates))
- **Packaging:** one Docker container serving the API + the built SPA over HTTPS on a single port

## Quick start (Docker — recommended)

The image build is fully self-contained (Node 20 + .NET 8 SDK inside the build),
so you do **not** need Node or .NET installed locally to run it.

1. Copy the example env file and fill in your ADO credentials (see [Data backend](#data-backend-azure-devops)):
   ```bash
   cp .env.example .env
   # Edit .env with your real ADO_ORG_URL, ADO_PROJECT, and ADO_PAT
   ```

2. Start the container:
   ```bash
   docker compose up --build
   ```

Then open **https://localhost:8443** and sign in:

| Username  | Password      |
|-----------|---------------|
| `devteam` | `aircoverage` |

> **Self-signed cert:** your browser will show a one-time "not secure / not
> trusted" warning — expected for a self-signed dev cert. Click through
> (Chrome/Edge: *Advanced → Proceed to localhost*). To trust it permanently,
> import the generated `.pfx` (see [TLS](#tls--certificates)) into your OS/browser
> trust store.

The SQLite cache **and** the generated cert live on the named volume
`aircoverage-data` (mounted at `/data`), so your data and cert **survive
`docker compose down` / `up`**. To wipe both (and regenerate a fresh cert):
`docker compose down -v`.

## Data backend (Azure DevOps)

Azure DevOps is the **single source of truth** for all Air Coverage items. SQLite
is used only as a **read-through cache of open items** so the queue stays snappy
with no per-request ADO calls.

### What an item is

An Air Coverage item is an **ADO work item carrying the `AirCoverage` tag**
(configurable via `Ado:Tag`; an Area Path filter is also optional via `Ado:AreaPath`).
The default work item type is `Bug` (configurable via `Ado:WorkItemType`). The
displayed Item # is the real ADO work-item ID and deep-links directly to the ADO
board for that item.

### PAT scope required

Create a **Personal Access Token** in Azure DevOps with the following scope:

- **Work Items: Read & Write**

All environments (Docker, local `dotnet run`, CI) use the same PAT mechanism;
swapping to Entra ID (managed identity / OAuth client credentials) later is a
single-class change (see [Toward v2](#toward-v2-entra--sso)).

### Configuring via `.env` (docker compose)

```bash
cp .env.example .env
```

Edit `.env` and fill in:

```
ADO_ORG_URL=https://dev.azure.com/your-org
ADO_PROJECT=YourProject
ADO_PAT=your-pat-with-work-items-read-write
```

`docker compose up` picks these up automatically. `.env` is git-ignored;
`.env.example` is tracked and safe to commit.

### Configuring via user-secrets (local `dotnet run`)

```bash
cd AirCoverage.Api
dotnet user-secrets init
dotnet user-secrets set "Ado:OrgUrl" "https://dev.azure.com/your-org"
dotnet user-secrets set "Ado:Project" "YourProject"
dotnet user-secrets set "Ado:Pat" "your-pat"
```

User-secrets are stored outside the repo and are never committed.

### How closed/resolved items work

**Closed and Resolved items fall off the active queue.** When an item is moved to
Closed or Resolved (in the app or directly in ADO), it is pruned from the SQLite
cache. The Closed, Resolved, and All tabs fetch a **bounded on-demand view** of
items changed in the last 30 days (configurable via `Ado:ClosedWindowDays`) directly
from ADO — only when those tabs are opened.

### Cache freshness

Three lightweight mechanisms keep the cache current:

1. **Write-through** — every create/update/status change the app makes is reflected
   in the cache instantly (the acting user always sees their own change immediately).
2. **Delta poll** (default every 60 s, `Ado:PollSeconds`) — a WIQL query for items
   changed since the last watermark fetches only what changed. Items now
   Closed/Resolved are pruned.
3. **Periodic full reconcile** (default every 5 min, `Ado:ReconcileSeconds`) — diffs
   the full open-ID set from ADO against the cache and drops any orphans (items
   hard-deleted or de-tagged in ADO that a delta might miss).

A **"synced Xs ago"** indicator in the page header reflects the last successful sync.

### All config keys

| Key | Default | Description |
|-----|---------|-------------|
| `Ado:OrgUrl` | — | `https://dev.azure.com/your-org` |
| `Ado:Project` | — | ADO project name |
| `Ado:Pat` | — | Personal Access Token (Work Items: Read & Write) |
| `Ado:Tag` | `AirCoverage` | Tag that identifies queue items |
| `Ado:AreaPath` | (none) | Optional area path filter (in addition to tag) |
| `Ado:WorkItemType` | `Bug` | Work item type used when creating items |
| `Ado:PollSeconds` | `60` | Delta poll interval |
| `Ado:ReconcileSeconds` | `300` | Full reconcile interval |
| `Ado:ClosedWindowDays` | `30` | Look-back window for closed/resolved on-demand views |

Env-var equivalents use double-underscore: e.g. `Ado__Pat`, `Ado__OrgUrl`.

## Local development (without Docker)

Run the API and the Vite dev server in two terminals. The Vite dev server proxies
`/api` to the API, so the auth cookie works across both.

> **Node 18+ required for local SPA dev.** This machine currently has Node 14,
> which modern Vite does not support. Upgrade Node (e.g. via `nvm`) for the
> frontend dev loop below. The Docker path above is unaffected — it uses Node 20
> internally.

**Terminal 1 — API (https://localhost:8443):**

First set up ADO credentials via user-secrets (see [above](#configuring-via-user-secrets-local-dotnet-run)), then:

```bash
cd AirCoverage.Api
dotnet run
```

On first run it applies the EF migration (creates the SQLite cache at
`AirCoverage.Api/aircoverage.db`) and generates `AirCoverage.Api/aircoverage-dev.pfx`.
The `SyncService` background worker performs an initial full reconcile against ADO
to warm the cache, then polls every 60 s.

**Terminal 2 — SPA dev server (https://localhost:5173):**
```bash
cd web
npm install
npm run dev
```
Open the Vite dev server for hot-reload development. The dev server proxies `/api`
to the HTTPS API and accepts its self-signed cert.

> **Dev cookie over HTTPS:** because the API now sets a **Secure** auth cookie, the
> Vite dev server must also be HTTPS for login to stick through the proxy. Enable it:
> ```bash
> npm install -D @vitejs/plugin-basic-ssl
> ```
> then in `vite.config.ts` add `import basicSsl from '@vitejs/plugin-basic-ssl'` and
> put `basicSsl()` in `plugins` (it only affects `npm run dev`, not the build). The
> Docker path needs none of this — it serves the SPA and API same-origin over HTTPS.

> Targeting note: the project targets `net8.0`. This machine has the .NET 10 SDK,
> so the csproj sets `<RollForward>LatestMajor</RollForward>` to let `dotnet run`
> use the .NET 10 runtime locally. In the Docker image (real .NET 8 runtime) this
> is a no-op.

## Project structure

```
AirCoverage/
├─ AirCoverage.Api/          # Minimal API + EF Core cache; serves the built SPA (wwwroot)
│  ├─ Program.cs             # DI, cookie auth, static files, endpoint wiring
│  ├─ Abstractions/          # IItemStore seam + ItemDto / ItemInput contracts
│  ├─ Ado/                   # AzureDevOpsClient, ItemMapper, AdoOptions, PatAuthHandler
│  ├─ Stores/                # AdoItemStore (cache reads + ADO write-through)
│  ├─ Sync/                  # SyncService + CacheSynchronizer (delta poll + reconcile)
│  ├─ Data/                  # CacheDbContext, CachedItem, SyncState
│  ├─ Endpoints/             # ItemsEndpoints.cs, AuthEndpoints.cs, SyncEndpoints.cs
│  ├─ Dtos/Dtos.cs           # auth request/response records (LoginRequest, UserResponse)
│  └─ Migrations/            # EF Core migrations (CacheSchema)
├─ web/                      # Vue 3 + Vite + TS SPA → builds to ../AirCoverage.Api/wwwroot (in Docker)
│  └─ src/
│     ├─ stores/             # Pinia: auth.ts, items.ts
│     ├─ components/         # LoginView, TopBar, PageHeader, QueueCard, QueueTable, ItemModal
│     ├─ api/client.ts       # fetch wrapper (credentials: 'include')
│     ├─ lib/format.ts       # days-open, date, initials, status colors
│     └─ types.ts            # Item/Priority/Status types + tab config
├─ Dockerfile               # multi-stage: build SPA → publish API → runtime image
├─ docker-compose.yml       # single-host run; passes ADO config from .env
├─ .env.example             # copy to .env and fill in ADO credentials
└─ docs/
   ├─ specs/                # design specs
   └─ plans/               # implementation plans
```

## API surface

All `/api/items*` routes require authentication (the cookie set by login).

| Method | Route                 | Purpose                                            |
|--------|-----------------------|----------------------------------------------------|
| GET    | `/api/items`          | List open items, sorted Critical→Low then oldest-first. Optional `?scope=closed` for the closed view |
| GET    | `/api/items/{id}`     | One item (cache first, then ADO)                  |
| POST   | `/api/items`          | Create (ADO assigns the work-item ID; write-through to cache) |
| PUT    | `/api/items/{id}`     | Update (status changes, edits; stamps `Updated`; write-through) |
| DELETE | `/api/items/{id}`     | Remove from queue by stripping the `AirCoverage` tag; evicts from cache |
| GET    | `/api/sync/status`    | `{ lastSync: ISO-timestamp }` — used by the staleness indicator |
| POST   | `/api/auth/login`     | Shared creds → sets the HttpOnly auth cookie       |
| POST   | `/api/auth/logout`    | Clears the cookie                                  |
| GET    | `/api/auth/me`        | Current auth state (401 when signed out)           |

The shared credential is configurable via `Auth:Username` / `Auth:Password`
(appsettings or env vars `Auth__Username` / `Auth__Password`).

## TLS / certificates

The app listens on **HTTPS only, port 8443** (bound in `Program.cs` via
`ConfigureKestrel`; configurable with `Https:Port`).

- **Default (dev/internal):** on first start, `Security/DevCertificate.cs` generates
  a self-signed cert for `localhost` (SANs: `localhost`, `127.0.0.1`, `::1`) and
  writes it to `DevCert:Path` — `/data/aircoverage-dev.pfx` in Docker (persisted on
  the volume), or `AirCoverage.Api/aircoverage-dev.pfx` locally. Password defaults to
  `aircoverage-dev` (`DevCert:Password`). Subsequent starts reuse it.
- **Bring your own cert (production):** provide a PFX via standard Kestrel config and
  the generator is bypassed automatically:
  ```yaml
  # docker-compose.yml
  environment:
    - Kestrel__Certificates__Default__Path=/data/your-cert.pfx
    - Kestrel__Certificates__Default__Password=...
  volumes:
    - ./certs/your-cert.pfx:/data/your-cert.pfx:ro
  ```
  In real deployments TLS is also commonly terminated at a reverse proxy / ingress
  (nginx, Azure Container Apps, etc.) — point it at the container's 8443.

Change the port by setting `Https__Port` (env) and updating the published port in
`docker-compose.yml`.

## Toward v2 (Entra + SSO)

**ADO auth:** PAT auth is isolated in `Ado/PatAuthHandler.cs` — a single delegating
handler. Swapping to Entra ID OAuth (managed identity or client credentials) is a
one-class change at that seam; no endpoints, store, or sync logic changes.

**App auth:** cookie auth is isolated in `AuthEndpoints.cs` + the cookie setup in
`Program.cs`. Swapping to OIDC / Entra ID later means changing how identity is
established (and optionally replacing the free-text `Assignee` with real accounts) —
the data model and items endpoints don't change.
