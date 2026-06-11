# Handoff: JustFOIA Air Coverage Tracker

## Overview
A lightweight internal **dev support queue** for the engineering team to work a
prioritized list of "Air Coverage" support items from intake to closed. It
replaces the awkward mix of Teams chats, Azure DevOps, and the legacy ConnectWise
ticketing system for the narrow job of **"work the most urgent items off a list."**

Core loop: an item comes in → it shows up in a priority-sorted queue → a dev
opens it, assigns themselves, advances its status (New → In Progress → Waiting →
Resolved → Closed), and it drops off the open list.

## About the Design Files
The files in `design/` are **design references created in HTML/Vue** — a
high-fidelity, fully interactive prototype showing the intended look and behavior.
**They are not production code to ship directly.**

The task is to **recreate this design in a real, deployable application** using
the architecture agreed with the team (documented in `ARCHITECTURE.md`):
- **Frontend:** Vue 3 + Vite SPA
- **Backend:** ASP.NET Core Minimal API (.NET 8 LTS)
- **Persistence:** SQLite via EF Core (single portable file; upgrade path to SQL Server/Postgres)
- **Packaging:** Docker container (required) — multi-stage build, SQLite file on a mounted volume
- **Auth v1:** shared username/password → cookie/JWT; **v2:** swap to OIDC/Entra ID SSO

The prototype happens to already be written in Vue 3, so much of the component
logic in `design/app.js` can be lifted into real Vue SFCs — but the data layer
(`localStorage`) must be replaced with calls to the .NET API.

## Fidelity
**High-fidelity (hifi).** Final colors, typography, spacing, layout, and
interactions are all intentional and match the existing JustFOIA product chrome
(forest-green top bar, white rounded content card, dense data table, pill badges).
Recreate the UI to match these specs closely.

## Screens / Views

### 1. Login
- **Purpose:** Gate the app behind a shared team credential (v1).
- **Layout:** Full-viewport centered card (max-width 380px) on a light-gray page
  with a subtle green radial glow at top-center. Card: white, 10px radius, soft
  shadow, ~38px padding.
- **Components:**
  - **Logo tile** — 60×60px, 12px radius, green `#1f9d58` background, white "JF"
    (the "J" italic), green drop shadow. Centered.
  - **Title** "Air Coverage" (20px, 700) + subtitle "Dev support queue · sign in
    to continue" (13px, muted `#8a9099`).
  - **Username / Password fields** — uppercase 12px/600 labels, inputs with
    `#e7e9ec` border, 7px radius, `#fbfbfc` fill; on focus green border + 3px
    green focus ring (`rgba(31,157,88,.15)`), white fill.
  - **Error line** — red `#e1402f`, 12.5px, reserved 16px height.
  - **Sign in button** — full-width, green `#1f9d58`, white, 7px radius, 600;
    hover `#178a4b`.
  - **Hint footer** — separated by top border, shows shared user/pass in `<code>`
    chips. (Remove in production.)
- **Behavior:** Submit checks credentials. v1 prototype creds: user `devteam`,
  pass `aircoverage`. On success sets an auth flag and reveals the app; on
  failure shows "Incorrect username or password."

### 2. App Chrome (top bar + page header)
- **Top bar** — 58px tall, green `#1f9d58`, sticky. Contains:
  - **"JF" mark** (22px, 700, "J" italic, white) on the left.
  - **Nav pills** — "Air Coverage" (active: white 1.5px border, faint white fill)
    and "My Items". Uppercase 13px/600, 18px radius, hover faint white fill.
    *(Dashboard, Reports, search, messages, and alert icons were intentionally
    removed — keep the bar minimal.)*
  - **Account avatar** on the right — 30px circle, click opens a menu showing
    "Dev Team / devteam" and a "Sign out" action.
- **Page header** — white, bottom border, ~16px/28px padding:
  - **"Air Coverage"** title (21px, 700, `#2c3136`).
  - **"+ Add New"** action — green link-style, uppercase 13px/700, with a 22px
    circular outlined "+" glyph. Opens the add-item modal.
  - Right side: export and help icons (help icon tinted green).

### 3. Main Queue (the core screen)
- **Purpose:** See and work the prioritized list; click any row for detail.
- **Layout:** Centered content (max-width 1480px, 26px padding) holding one white
  card (10px radius, soft shadow, `#eef0f2` border).
  - **Filter bar** — funnel icon + full-width text input ("Filter by title, ID,
    source, or assignee…") over a 1.5px bottom rule, plus a kebab menu icon.
  - **Status tabs** — Open · New · In Progress · Waiting · Resolved · Closed · All.
    Active tab: green text + 2.5px green underline. Each tab has a count chip
    (gray default; green-tinted when active). Default tab is **Open**.
  - **Data table** — columns: Priority · Item # · Title/Summary · Status ·
    Assigned · Source · Ticket · Days Open.
- **Components / cell specs:**
  - **Priority** — colored dot + label, 12.5px/600. Colors: Critical `#d93535`,
    High `#e8833a`, **Medium `#3070c9` (blue)**, Low `#7a8694`.
  - **Item #** — e.g. `AC-1042`, green `#1b8a4c`, underlined, 600 (acts as a link
    to open detail).
  - **Title/Summary** — `#2c3136` title with a 1-line muted description preview
    (`#8a9099`, 12px, ellipsis).
  - **Status** — colored dot + label. Colors: New `#3b73c4`, In Progress
    `#1f9d58`, Waiting `#d8a72b`, Resolved `#2e8b57`, Closed `#9aa0a7` (Closed
    label muted).
  - **Assigned** — pill. Assigned: green-tinted fill + green border + 16px circle
    avatar with initials. Unassigned: white fill, gray border, "Unassigned".
  - **Source** — plain text (e.g. On-call, Support, Customer, Eng) or "—".
  - **Ticket** — link-chip icon + "ADO #48211" / "ConnectWise #88204", or "—".
  - **Days Open** — tabular number; turns red `#e1402f` when ≥14 days and the
    item is not Resolved/Closed.
  - Rows: hover `#f8fafb`, 1px `#eef0f2` bottom border, entire row clickable.
  - Empty state: centered checklist icon + contextual message.

### 4. Item Detail / Edit Modal
- **Purpose:** View and edit a single item; advance its status.
- **Layout:** Centered overlay (`rgba(28,33,38,.46)`), white modal (max-width
  620px, 12px radius, large shadow, subtle pop-in animation). Header / body / footer.
- **Components:**
  - **Header** — green item # label (12px/700) + title (18px/700) + "×" close.
  - **Status flow** (edit mode) — "Status — click to advance" label over a row of
    5 status buttons (dot + label). Current status highlighted green; clicking any
    sets the new status.
  - **Two-column field grid:** Priority (select), Assigned developer (select,
    blank = Unassigned), Requested by/source (text), Linked ticket (type select:
    None/ADO/ConnectWise + reference text, disabled until a type is chosen),
    Description (full-width textarea).
  - **Meta row** (edit mode) — Received date + days open, and Last updated datetime.
  - **Footer** — "Delete" (red ghost, left), item # note, "Cancel", and "Save
    changes" / "Create item" (green; disabled until a title exists).
- **Add mode** differs: title is an editable input in the header, Status is a
  select (defaults New), and the footer button reads "Create item". New items
  auto-generate the next `AC-####` number.

## Interactions & Behavior
- **Auth:** login reveals the app; "Sign out" returns to the login card. Auth flag
  persisted (prototype uses localStorage `ac_auth_v1`).
- **Sorting:** default sort is **Priority** (Critical → High → Medium → Low), then
  **Received ascending** (oldest first) as a tiebreak — this is the "most urgent →
  least urgent" rule. Clicking the **Priority** or **Days Open** header toggles
  sort direction.
- **Filtering:** the search box matches title, ID, source, assignee, and
  description (case-insensitive). Status tabs filter by status; "Open" = not
  Resolved and not Closed; "All" = everything.
- **Status advance:** clicking a status button in the detail modal updates the
  working copy; saving persists it and stamps `updated`.
- **Create / edit / delete:** modal edits a deep copy; Save commits, Cancel/× and
  Esc discard. Delete removes the item.
- **Days-open aging:** computed from `received` to now (or to `updated` if Closed);
  ≥14 days open and still active renders red.
- **Animations:** modal pop-in (~0.16s ease-out). Keep subtle; respect reduced motion.
- **Responsive:** below 720px the nav pills hide, content padding tightens, the
  table scrolls horizontally, and the modal field grid collapses to one column.

## State Management
- `authed` (bool) — login gate. Persisted.
- `items` (array) — the queue. **In production this is server state** fetched from
  `GET /api/items` and mutated via POST/PUT/DELETE; the prototype persists to
  localStorage `ac_items_v1`.
- `search` (string), `activeTab` (string), `sortKey` ('priority'|'received'),
  `sortDir` (1|-1) — view state.
- `editing` (object|null) + `mode` ('edit'|'add') — modal state (deep copy of the
  item being edited).
- Derived: filtered-by-tab → filtered-by-search → sorted list; open count;
  critical-open count; per-tab counts.

### Data model (see ARCHITECTURE.md for the C# version)
```
id/Number   "AC-1042" display id
title       string
description string
priority    Critical | High | Medium | Low
status      New | In Progress | Waiting | Resolved | Closed
requestedBy string (source)
assignee    string (dev name; blank = unassigned)
ticketType  ADO | ConnectWise | null
ticketRef   string (e.g. "#48211")
received    ISO datetime
updated     ISO datetime | null
```

## Design Tokens
**Colors**
- Green primary `#1f9d58`; dark `#178a4b`; link `#1b8a4c`
- Red / alert / past-due `#e1402f`
- Page bg `#f4f5f6`; card `#ffffff`; borders `#e7e9ec` / `#eef0f2`
- Text `#303133` / `#2c3136`; muted `#8a9099` / `#aab0b7`
- Priority: Critical `#d93535`, High `#e8833a`, Medium `#3070c9`, Low `#7a8694`
- Status: New `#3b73c4`, In Progress `#1f9d58`, Waiting `#d8a72b`, Resolved `#2e8b57`, Closed `#9aa0a7`

**Typography** — Open Sans (400/600/700, plus 400 italic). Base 14px / line-height
1.45. Scale used: 11–12px labels (uppercase, .4–.6px tracking), 13–14.5px body,
18px modal title, 20–21px page/login titles, 22px JF mark.

**Spacing / radius / shadow**
- Radius: 7px inputs/buttons, 10px cards, 12px modal, 14–18px pills/nav.
- Card shadow `0 1px 3px rgba(16,24,40,.06), 0 1px 2px rgba(16,24,40,.04)`.
- Popover/modal shadow `0 12px 40px rgba(16,24,40,.18), 0 2px 8px rgba(16,24,40,.08)`.
- Focus ring `0 0 0 3px rgba(31,157,88,.13–.15)`.
- Top bar 58px; page-header padding 16px/28px; content padding 26px (14px mobile).

## Assets
- **Fonts:** Open Sans via Google Fonts.
- **Icons:** inline SVG (stroke-based, ~2px), no icon library dependency.
- **Logo:** text-based "JF" mark (no image file).
No external images — safe to recreate with the codebase's icon system if preferred.

## Files
- `design/Air Coverage Tracker.html` — full prototype markup + all CSS (the visual
  source of truth).
- `design/app.js` — Vue 3 app: state, sorting/filtering, modal logic, seed data.
- `ARCHITECTURE.md` — backend/DB/Docker plan and prototype→production mapping.
- `CLAUDE.md` — project bootstrap instructions for Claude Code (build order, stack,
  conventions).

## Suggested build order (for Claude Code)
1. Scaffold the solution per `ARCHITECTURE.md` (Minimal API + EF Core + SQLite,
   Vue 3 + Vite under `web/`, multi-stage `Dockerfile`, `docker-compose.yml`).
2. Implement the `Item` model + EF Core context + migration; seed sample data.
3. Build the API endpoints (`/api/items` CRUD, `/api/auth/login|logout`).
4. Recreate the UI: chrome → queue table (sorting/filter/tabs) → detail/add modal
   → login gate. Match the tokens above.
5. Wire the SPA to the API (replace localStorage with fetch).
6. Containerize and verify the SQLite volume persists across restarts.
