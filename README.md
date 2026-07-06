# The West Marches — Campaign Manager

A production-quality web application for running a **West Marches**-style D&D campaign: a large
shared player pool, many independently scheduled sessions, a rotating roster of DMs, and
centralized administration — wrapped in a custom "campaign atlas" UI rather than a stock admin panel.

## Architecture

**Stack:** ASP.NET Core (.NET 10 LTS) · Blazor Web App (Interactive Server) · EF Core + SQL Server ·
ASP.NET Core Identity · SignalR · xUnit.

```
src/
├─ WestMarch.Domain           entities + LevelingEngine (pure, zero dependencies)
├─ WestMarch.Application      service interfaces & implementations, validation,
│                             service-layer authorization, repository interfaces,
│                             ICurrentUser / IImageStore / IDdbCharacterAdapter seams
├─ WestMarch.Infrastructure   EF Core (SQL Server), Identity, repositories,
│                             dev seeder, DDB adapter, local-disk & Azure Blob image stores
└─ WestMarch.Web              Blazor components, SignalR hub, auth pipeline, DI root
tests/
└─ WestMarch.Tests            leveling engine, authorization policies, service-layer authz
```

Dependency rule: `Web → Application → Domain`; `Infrastructure` implements `Application`
interfaces and is wired only at the composition root. **Swapping SQL Server for another
provider is contained to `Infrastructure.DependencyInjection`** — everything upstream
depends on repository/service interfaces (the repositories deliberately avoid
provider-specific constructs; the test suite runs the same code on SQLite).

### Why Blazor (Interactive Server) and not a React SPA

- The per-session message board rides the same SignalR machinery Blazor Server already uses.
- Nearly every screen is role-gated; one set of policies covers endpoint, component
  (`<AuthorizeView>`), **and service-layer** authorization — no duplicated client-side rules,
  no token pipeline.
- Form-heavy admin CRUD (adventures, announcements, people) with server validation and
  no API/DTO/client-model triplication.
- One deployable, no CORS, no Node toolchain.

### Identity & roles

- `ApplicationUser` is the single principal; Identity's external-login table gives
  **one user ↔ many login methods**. Discord OAuth (hand-wired `AddOAuth`, no Discord types in
  the user model) and local email/password ship in Phase 1; a future Google/etc. provider is
  one more registration call.
- A Discord user with a shared email gets an account **created automatically on first arrival** —
  no username/password step.
- Roles are additive Identity roles: implicit **Player**, plus **DM** and **CampaignAdmin**.
  Policies: `RequireDM` (DM ∨ CA), `RequireCA`. Enforced in components *and* in application
  services via `ICurrentUser` (see `ServiceAuthorizationTests`).

### Leveling rule engine

`LevelingEngine` (Domain) encodes: *a character advances after a number of successful sessions
equal to its proficiency bonus at its current level* (+2 ≤ L4 … +6 ≥ L17). Progress is displayed
to owners as rune pips ("1 of 4 sessions toward level 12"). `SessionCredit` rows are the
permanent audit trail; the DM's session-completion flow awards them.

### Sessions & message board

Players or DMs create sessions against **Approved** adventures. DM-less sessions are first-class:
flagged on the calendar and listed on the DM-only "Needs a DM" board. Sign-up mismatches
(level range, full table) are **flagged, not blocked** — West Marches tables self-organize, and
the DM can drop signups. The board is real time: posts go through `ISessionService`
(authorization + persistence) and fan out via `SessionBoardHub`; the browser connects with the
Identity cookie, so the hub is `[Authorize]`d naturally.

### D&D Beyond adapter (optional, isolated)

`IDdbCharacterAdapter` fetches best-effort stat headers (AC, HP, passive perception, saves, gold,
magic items, spells) from DDB's unsupported character JSON endpoint. It is feature-flagged, cached,
timeout-bounded, and **never throws** — any failure renders the DM view with just the required
D&D Beyond link. Nothing in the core app depends on it.

### Reserved extension points (deferred phases)

- **Item/reference catalog** — `RewardOption` is a real object with free-text description,
  optional external URL, and a reserved `CatalogItemId` FK. Options graduate to catalog
  references without schema rework.
- **Reservable resources** (store tables, seats) — sessions expose the attachment seam;
  a `Resource` + `SessionResourceAllocation` pair slots in additively.
- **More auth providers** — one registration call each.

## Running locally

Prereqs: .NET 10 SDK, SQL Server LocalDB (or any SQL Server — edit the connection string).

```bash
dotnet run --project src/WestMarch.Web
```

On first run in Development the app migrates the database and seeds a full demo campaign.

**Seeded accounts** (password for all: `Passw0rd!`):

| Email | Roles |
|---|---|
| admin@westmarch.local | Campaign Admin + DM ("Mira the Cartographer") |
| dm@westmarch.local, dm2@westmarch.local | DM |
| player1–4@westmarch.local | Player |

```bash
dotnet test          # leveling engine, policies, service-layer authorization
```

## Configuration

All settings are environment-driven (`appsettings.{Environment}.json` / env vars / user secrets):

| Key | Purpose |
|---|---|
| `ConnectionStrings:DefaultConnection` | SQL Server (LocalDB by default) |
| `Features:DdbStatHeaders` | **Feature flag** for the DDB stat-header adapter (off in prod default, on in dev) |
| `Ddb:*` | Adapter base URL, timeout, cache durations |
| `ImageStore:Provider` | `Local` (dev, serves from `wwwroot/media`) or `AzureBlob` |
| `ImageStore:AzureBlob:*` | Blob connection string + container |
| `Authentication:Discord:ClientId/ClientSecret` | Discord OAuth app (callback `/signin-discord`); button hides when unset |

Local-account email confirmation is disabled (no email round-trip for a game community);
wire an `IEmailSender<ApplicationUser>` and re-enable `RequireConfirmedAccount` for production.

## Hosting (recommended: Azure)

| Need | Service |
|---|---|
| App + WebSockets (Blazor circuits, hub) | App Service — enable WebSockets + ARR affinity |
| Database | Azure SQL Database (serverless fits bursty game-night traffic) |
| Announcement images | Azure Blob Storage (`ImageStore:Provider=AzureBlob`) |
| Secrets | App Service config / Key Vault |
| Scale-out later | Azure SignalR Service (config-level change) |

Register the Discord redirect URI per environment: `https://<host>/signin-discord`.

## Phase roadmap

- **Phase 1 (this build):** users + additive roles, Discord/local auth, characters with required
  DDB links + leveling progress, adventure authoring lifecycle (Draft → Ready for Review →
  Approved) with tags and structured rewards, sessions with DM assignment / sign-up /
  completion credits, real-time boards, calendar + LFP + needs-a-DM views, CA announcements
  with images, CA people manager, tests.
- **Phase 2+ (designed for, not built):** CA-managed item/reference catalog (reward options
  become references), reservable session resources (store tables), further auth providers,
  deeper DDB import beyond the stat header.
