# Air Coverage Tracker — Architecture & Backend Recommendation

This document answers the "which backend?" question and explains how the
prototype maps onto a real, portable, cheap-to-host app you can grow over time.

---

## TL;DR recommendation (matches your selections)

| Concern        | Recommendation                                  | Why |
|----------------|-------------------------------------------------|-----|
| **API layer**  | **ASP.NET Core Minimal API** (.NET 8 LTS)       | Lightest .NET option, single project, cross-platform, you already run .NET everywhere |
| **Persistence**| **SQLite via EF Core**                          | One portable file, real SQL, near-zero-change upgrade path to SQL Server / Postgres |
| **Frontend**   | **Vue 3 + Vite** SPA                            | What this prototype is written in; static files, host anywhere |
| **Packaging**  | **Docker container** (required)                 | Build once, run identically anywhere — local, CI, any cloud |
| **Auth (v1)**  | Shared username/password → cookie/JWT           | Simple to share with the dev team now |
| **Auth (v2)**  | Swap to **OIDC / Entra ID (SSO)**               | Add later without touching the data model |

The whole thing is a **single .NET process that serves both the API and the
built Vue static files**, reading/writing **one SQLite file on disk**. That is
about as cheap and portable as it gets: a small App Service, a container, or
even a tiny VM.

---

## Why Minimal API over the alternatives

- **vs. Controllers / Web API:** Controllers are great for large apps with lots
  of conventions, filters, and shared base classes. For ~6 endpoints this is
  ceremony you don't need. Minimal API keeps the entire surface in a handful of
  `app.MapGet/MapPost` lines you can read in one screen. You can always graduate
  to controllers later — they coexist in the same project.
- **vs. Azure Functions / serverless:** Cheapest *idle* cost and scales to zero,
  but adds cold-starts, a different local-dev story, and makes the "one process
  serving the SPA + a file-based DB" model awkward (Functions don't keep a local
  file around). Good option if you later want pure pay-per-use; not the simplest
  starting point.

## Why SQLite over a raw JSON file

You asked about a JSON file or a lightweight portable DB. A JSON file works for a
toy, but:

- **Concurrent writes corrupt it.** Two devs hitting "Save" at once can clobber
  each other. SQLite handles concurrent access safely.
- **No querying.** Sorting/filtering by priority, status, age means loading and
  scanning the whole file in memory every request.
- **No migration story.** Adding a field later is manual surgery.

SQLite fixes all three and is *still just one file* you can copy, back up, or
commit. EF Core maps your C# model to it. When you outgrow it, change the
connection string + provider (`UseSqlServer` / `UseNpgsql`) and your queries and
models stay the same.

> Alternative if you love the JSON mental model: **LiteDB** (embedded .NET
> document DB, single file, no SQL). Perfectly fine here — I lean SQLite only
> because the upgrade path to SQL Server is so frictionless given your stack.

---

## Suggested project shape

```
AirCoverage/
├─ AirCoverage.Api/            # ASP.NET Core Minimal API (.NET 8)
│  ├─ Program.cs               # endpoints + DI + static-file serving
│  ├─ Data/
│  │  ├─ AppDbContext.cs       # EF Core DbContext
│  │  └─ aircoverage.db        # SQLite file (gitignored; or seed a copy)
│  ├─ Models/Item.cs
│  └─ Migrations/
├─ web/                        # Vue 3 + Vite SPA (this prototype, productionised)
│  └─ dist/                    # built static files served by the API
├─ Dockerfile                  # multi-stage build (SPA + API → runtime image)
└─ docker-compose.yml          # local/single-host run with a data volume
```

## Data model (matches the prototype)

```csharp
public class Item
{
    public int      Id          { get; set; }   // surrogate key
    public string   Number      { get; set; }   // "AC-1042" display id
    public string   Title       { get; set; }
    public string   Description { get; set; }
    public string   Priority    { get; set; }   // Critical | High | Medium | Low
    public string   Status      { get; set; }   // New | In Progress | Waiting | Resolved | Closed
    public string?  RequestedBy { get; set; }
    public string?  Assignee    { get; set; }
    public string?  TicketType  { get; set; }   // ADO | ConnectWise | null
    public string?  TicketRef   { get; set; }
    public DateTime Received    { get; set; }
    public DateTime? Updated    { get; set; }
}
```
> Priority/Status are stored as strings for simplicity; promote them to enums or
> a lookup table if/when you need referential guarantees.

## API surface (Minimal API)

```
GET    /api/items                 # list (optionally ?status=&priority=&q=)
GET    /api/items/{id}            # one
POST   /api/items                 # create
PUT    /api/items/{id}            # update (status changes, edits)
DELETE /api/items/{id}            # delete
POST   /api/auth/login            # shared creds -> sets auth cookie / JWT
POST   /api/auth/logout
```

Sorting "most urgent → least urgent" is done in the query: order by priority
rank, then `Received` ascending (oldest first). The prototype already implements
exactly this logic in `app.js` (`visibleItems`).

## Auth path

- **v1 (now):** one shared username/password. On login, issue an auth cookie (or
  short JWT). Gate the `/api/items*` routes with `RequireAuthorization()`.
- **v2 (later):** add `AddAuthentication().AddOpenIdConnect(...)` for Entra ID /
  your IdP. The data model and endpoints don't change; you're swapping how the
  identity is established and (optionally) replacing the free-text `Assignee`
  with real user accounts.

## Hosting (cheap & portable)

Any of these work because it's one self-contained process + one file:
- **Azure App Service (Basic/B1)** or a container on **Azure Container Apps**.
- A small **Linux VM / droplet** running the published binary behind nginx.
- A **Docker** image (`dotnet publish` → tiny runtime image) you can drop anywhere.

Back up = copy the `.db` file. Move hosts = copy the binary + the `.db` file.

## Containerization (required)

The app **must** ship as a Docker container and the deployment target is a
container host. This keeps "runs on my machine" identical to "runs in prod,"
makes the cheap-hosting options (Azure Container Apps, Container Instances, any
VM with Docker, k8s later) interchangeable, and means a deploy is just pulling a
new image tag.

**Build model:** multi-stage Dockerfile — build the Vue SPA, build/publish the
.NET API, then copy both into a small runtime image. The Vite `dist/` is served
as static files by the same API process, so the container exposes a single port.

```dockerfile
# --- build the Vue SPA ---
FROM node:20-alpine AS web
WORKDIR /web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build            # outputs /web/dist

# --- build & publish the .NET API ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api
WORKDIR /src
COPY AirCoverage.Api/*.csproj AirCoverage.Api/
RUN dotnet restore AirCoverage.Api/AirCoverage.Api.csproj
COPY AirCoverage.Api/ AirCoverage.Api/
COPY --from=web /web/dist AirCoverage.Api/wwwroot   # SPA served as static files
RUN dotnet publish AirCoverage.Api/AirCoverage.Api.csproj -c Release -o /app

# --- tiny runtime image ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=api /app ./
ENV ASPNETCORE_URLS=http://+:8080
VOLUME /data                     # SQLite file lives on a mounted volume
ENV ConnectionStrings__Default="Data Source=/data/aircoverage.db"
EXPOSE 8080
ENTRYPOINT ["dotnet", "AirCoverage.Api.dll"]
```

**SQLite + containers — the one gotcha:** containers are ephemeral, so the `.db`
file **must** live on a mounted volume (not inside the image layer) or every
redeploy wipes the data. The `VOLUME /data` + connection string above handle
this. Back up = snapshot/copy that volume.

**Local dev / single-host** with `docker-compose.yml`:

```yaml
services:
  aircoverage:
    build: .
    ports:
      - "8080:8080"
    volumes:
      - aircoverage-data:/data     # persists the SQLite file across restarts
    restart: unless-stopped
volumes:
  aircoverage-data:
```

**Deploy flow:** `docker build` → push to a registry (ACR / GHCR / Docker Hub)
→ the container host pulls the new tag. CI builds the image on merge; the host
runs whatever tag you promote. When SQLite is outgrown, point the connection
string at a managed SQL Server / Postgres and drop the `/data` volume — the
image doesn't change.

Back up = copy the `.db` file (or snapshot the volume). Move hosts = run the same
image elsewhere and mount the data volume.

---

## How the prototype maps to production

| Prototype (this repo)                 | Production                                  |
|---------------------------------------|---------------------------------------------|
| `app.js` Vue logic                    | Lift into Vue SFCs under `web/`, build w/ Vite |
| `localStorage` read/write             | `fetch('/api/items')` calls to the API      |
| Seed data in `seed()`                 | EF Core seeding / a migration               |
| Hard-coded `SHARED_USER/PASS`         | `/api/auth/login` + cookie/JWT              |
| Sort/filter in `visibleItems`         | Same logic, run as an EF Core query         |

The look, the workflow, the fields, and the sort behaviour are the spec — the
prototype is meant to be the thing your team rebuilds against.
