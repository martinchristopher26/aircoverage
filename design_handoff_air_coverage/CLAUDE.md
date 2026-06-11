# CLAUDE.md — JustFOIA Air Coverage Tracker

Project bootstrap instructions for Claude Code. Read `README.md` (full design
spec) and `ARCHITECTURE.md` (backend/DB/Docker plan) alongside this file before
writing code.

## What we're building
An internal dev support queue: a priority-sorted list of "Air Coverage" items
worked through statuses (New → In Progress → Waiting → Resolved → Closed). See
`README.md` for the complete, high-fidelity design spec — colors, typography,
layout, and behavior are all defined there and should be matched closely.

The HTML/Vue files in `design/` are **design references**, not code to ship. Lift
the component logic where useful, but replace the localStorage data layer with
the real API.

## Stack (agreed — see ARCHITECTURE.md)
- **Frontend:** Vue 3 + Vite SPA (TypeScript preferred).
- **Backend:** ASP.NET Core Minimal API, .NET 8 LTS.
- **DB:** SQLite via EF Core. One file, on a mounted volume in the container.
  Connection string from `ConnectionStrings__Default`.
- **Packaging:** Docker is required. Multi-stage build (build SPA → publish API →
  small runtime image serving the SPA as static files on a single port).
- **Auth:** v1 shared username/password → cookie or JWT; design so v2 can swap to
  OIDC/Entra ID SSO without touching the data model.

## Target project shape
```
AirCoverage/
├─ AirCoverage.Api/          # Minimal API + EF Core; serves wwwroot (built SPA)
│  ├─ Program.cs
│  ├─ Data/AppDbContext.cs
│  ├─ Models/Item.cs
│  └─ Migrations/
├─ web/                      # Vue 3 + Vite SPA  → builds to AirCoverage.Api/wwwroot
├─ Dockerfile               # multi-stage
└─ docker-compose.yml       # data volume for the SQLite file
```

## Conventions
- Match the design tokens in `README.md` exactly (green `#1f9d58`, page `#f4f5f6`,
  Open Sans, Medium priority is **blue** `#3070c9`, etc.). Centralize them as CSS
  variables / a theme module.
- Keep the top bar minimal: only the JF mark, "Air Coverage" + "My Items" pills,
  and the account avatar. No search/messages/alerts/Reports.
- Default queue sort: priority (Critical→Low) then oldest-first. Default tab: Open.
- Items use a human display id `AC-####` separate from the DB surrogate key.
- Status is click-to-advance in the detail modal.
- Don't over-build: this is a focused tool, not a full ticketing system. Resist
  adding fields/screens beyond the spec without asking.

## Build order
1. Scaffold the solution + Dockerfile + docker-compose per ARCHITECTURE.md.
2. `Item` model + `AppDbContext` + initial migration + seed data.
3. API: `GET/POST/PUT/DELETE /api/items`, `POST /api/auth/login|logout`.
   Sorting/filtering done in the query.
4. SPA: chrome → queue (sort/filter/tabs) → detail & add modal → login gate.
5. Wire SPA to API (replace localStorage with fetch).
6. Containerize; confirm the SQLite volume survives `docker compose down/up`.

## Definition of done (v1)
- Log in with the shared credential, see the priority-sorted Open queue.
- Add, open, edit, advance status, assign, link a ticket, and delete an item.
- Data persists in SQLite across container restarts.
- Runs from a single `docker compose up`.
