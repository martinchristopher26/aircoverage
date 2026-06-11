# Air Coverage Tracker

An internal dev-support queue for JustFOIA engineering: a priority-sorted list of
"Air Coverage" items worked through statuses (New → In Progress → Waiting →
Resolved → Closed). This is the production app built from the design handoff in
[`design_handoff_air_coverage/`](design_handoff_air_coverage/).

- **Backend:** ASP.NET Core Minimal API (.NET 8 LTS)
- **Database:** SQLite via EF Core (one portable file; swap to SQL Server/Postgres later)
- **Frontend:** Vue 3 + Vite + TypeScript SPA (Pinia for state)
- **Auth (v1):** shared credential → Secure HttpOnly cookie (`RequireAuthorization` on `/api/items*`)
- **Transport:** **HTTPS only on port 8443** — a self-signed `localhost` cert is auto-generated on first run (swap in a real cert for production; see [TLS](#tls--certificates))
- **Packaging:** one Docker container serving the API + the built SPA over HTTPS on a single port

## Quick start (Docker — recommended)

The image build is fully self-contained (Node 20 + .NET 8 SDK inside the build),
so you do **not** need Node or .NET installed locally to run it.

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

The SQLite database **and** the generated cert live on the named volume
`aircoverage-data` (mounted at `/data`), so your data and cert **survive
`docker compose down` / `up`**. To wipe both (and regenerate a fresh cert):
`docker compose down -v`.

## Local development (without Docker)

Run the API and the Vite dev server in two terminals. The Vite dev server proxies
`/api` to the API, so the auth cookie works across both.

> **Node 18+ required for local SPA dev.** This machine currently has Node 14,
> which modern Vite does not support. Upgrade Node (e.g. via `nvm`) for the
> frontend dev loop below. The Docker path above is unaffected — it uses Node 20
> internally.

**Terminal 1 — API (https://localhost:8443):**
```bash
cd AirCoverage.Api
dotnet run
```
On first run it applies the EF migration, seeds the sample queue into
`AirCoverage.Api/aircoverage.db`, and generates `AirCoverage.Api/aircoverage-dev.pfx`.

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
├─ AirCoverage.Api/          # Minimal API + EF Core; serves the built SPA (wwwroot)
│  ├─ Program.cs             # DI, cookie auth, static files, endpoint wiring
│  ├─ Models/Item.cs         # data model
│  ├─ Data/                  # AppDbContext, seeder, design-time factory
│  ├─ Endpoints/             # ItemsEndpoints.cs, AuthEndpoints.cs
│  ├─ Dtos/Dtos.cs           # request/response records
│  └─ Migrations/            # EF Core migrations (InitialCreate)
├─ web/                      # Vue 3 + Vite + TS SPA → builds to ../AirCoverage.Api/wwwroot (in Docker)
│  └─ src/
│     ├─ stores/             # Pinia: auth.ts, items.ts
│     ├─ components/         # LoginView, TopBar, PageHeader, QueueCard, QueueTable, ItemModal
│     ├─ api/client.ts       # fetch wrapper (credentials: 'include')
│     ├─ lib/format.ts       # days-open, date, initials, status colors
│     └─ types.ts            # Item/Priority/Status types + tab config
├─ Dockerfile               # multi-stage: build SPA → publish API → runtime image
└─ docker-compose.yml       # single-host run with the SQLite data volume
```

## API surface

All `/api/items*` routes require authentication (the cookie set by login).

| Method | Route                 | Purpose                                            |
|--------|-----------------------|----------------------------------------------------|
| GET    | `/api/items`          | List, sorted Critical→Low then oldest-first. Optional `?status=&priority=&q=` |
| GET    | `/api/items/{id}`     | One item                                           |
| POST   | `/api/items`          | Create (server assigns the next `AC-####` number)  |
| PUT    | `/api/items/{id}`     | Update (status changes, edits; stamps `Updated`)   |
| DELETE | `/api/items/{id}`     | Delete                                             |
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

## Toward v2 (SSO)

Auth is isolated in `AuthEndpoints.cs` + the cookie setup in `Program.cs`. Swapping
to OIDC / Entra ID later means changing how identity is established (and optionally
replacing the free-text `Assignee` with real accounts) — the data model and items
endpoints don't change.
