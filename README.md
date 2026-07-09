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

### Item catalog & economy (Phase 2)

- **Catalog** (`CatalogItem`): magic + mundane items in one table, `Imported` or `Custom`.
  Viewable by DMs, editable by CAs. Reference files (`/data/*.json`) are only a feed —
  a CA **import** upserts by stable key (name + rarity; multi-tier items like Belt of
  Giant Strength are separate rows per rarity), shows a diff preview, and **deactivates**
  items missing from the new file (never deletes — owned copies and history survive).
  Custom items and the CA **campaign price override** are never touched by imports;
  the override is also how "Varies/Priceless" items (85 in the current file) become
  sellable — the catalog's *Unpriced* filter lists them.
- **Rarity tiers** (`RarityTiers`): Common 1–4, Uncommon 5–8, Rare 9–12, Very Rare 13–16,
  Legendary/Artifact 17–20 — aligned with the proficiency tiers. Filters the adventure
  reward picker (optional override) and produces **flags, not blocks**: out-of-tier
  rewards are surfaced to the reviewing CA; out-of-tier purchases warn the buyer.
- **Reward collection**: after a DM completes a session, each credited player collects on
  the session page — guaranteed gold hits the character's balance, each choose-1-of-N pick
  mints a real `ItemInstance` (catalog-backed) or a ledger note (free text). Once only.
- **Inventory & ledger**: characters carry an integer gold balance, owned items, and an
  append-only `LedgerEntry` history (every acquisition, sale, and purchase), all on the
  character page. The CA **audit page** is the same ledger campaign-wide.
- **Marketplace**: quick-sell for half value (instance retired), or fixed-price listings
  other players buy for one of their characters — gold moves between purses, the item
  changes hands, both sides ledgered. Optimistic concurrency (listing `Version` token)
  guarantees two buyers can't buy one listing. Bidding/auctions deliberately deferred;
  a `Bids` collection can attach to `MarketListing` later without rework.

### Bestiary, encounters & the DM screen (Phase 3)

- **Bestiary** (`Monster`): SRD monster records imported from `/data/srd_5e_monsters_ext.json`
  with the same CA refresh pipeline as the item catalog (diff preview, upsert by name,
  retire-don't-delete). Critical columns (CR, AC, HP, type) feed lists and filtering; the
  complete source record is preserved as JSON so stat blocks render every trait, action,
  and legendary action.
- **Encounters** replace the old free-text monster stat blocks on adventures. Each adventure
  carries 0–N encounters — title, read-aloud text, DM description, free-text NPCs, and
  bestiary-backed monsters with head-counts. All sections optional; **order is display-only**
  (mysteries and sandboxes are first-class). The encounter builder's monster picker is
  search-as-you-type, CR-capped at the adventure's max level + 2 by default (boss headroom,
  no dragons in starter dungeons) with a show-all override. The Phase 3 migration converted
  existing stat-block text into starter encounters — nothing authored was lost.
- **DM Screen** (`/dm/screen`, DM menu): upcoming + recently run sessions, each opening a
  run view built for the table — the party's critical numbers up top (name, class, AC, HP,
  passive perception, saves via the DDB stat headers), encounters as tabs, read-aloud text
  in a distinct "speak this" treatment, and full stat blocks expanded.
- **DM tool seam** (`IDmScreenTool`): future tools (initiative tracker, monster HP/condition
  tracking) register in DI and appear as extra DM-screen tabs — one component + one
  registration, no screen changes.

### Reserved extension points (deferred phases)

- **DM screen tools** — initiative tracker, HP/conditions; `IDmScreenTool` is the seam.
- **Marketplace bidding/auctions** — listings are the attachment point.
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
| `Catalog:SeedDirectory` | Where the dev seeder finds `magic-items.json` / `equipment.json` (default: repo `/data`) |
| `InitialAdmin:Email/Password/DisplayName` | First-run bootstrap of a Campaign Admin in non-dev environments (see below) |
| `DataProtection:KeyPath` | Optional durable path for DataProtection keys; blank lets App Service auto-persist to `%HOME%` |

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

## Deploying to Azure

A low-cost testing footprint: **App Service B1** (WebSockets require at least Basic — the Free
tier can't run Blazor Server), **Azure SQL serverless** (auto-pauses when idle; the free offer
may cover light testing), and **Blob Storage** for images. Azure SignalR Service and Key Vault
are intentionally skipped at this scale.

**How the app boots in production** — two modes:
- **Default:** `ProductionInitializer` applies EF migrations, ensures roles, and — if
  `InitialAdmin:*` is set and that email is unused — creates one bootstrap Campaign Admin.
  No demo data; the CA loads reference data via the in-app import screens.
- **`SeedDemoData=true`** (for the hosted test/demo site): seeds the full demo campaign —
  reference data plus users, characters, adventures, sessions, and economy. The reference
  JSON files ship in the publish output (`data/`), so no manual import is needed. Demo logins
  use password `Passw0rd!` (e.g. `admin@westmarch.local`). Leave `InitialAdmin:*` unset in
  this mode.

**One-time provisioning** (Azure CLI; replace names/region):

```bash
az group create -n westmarch-rg -l eastus2

# Azure SQL — serverless, auto-pause. Try the free offer first; otherwise GP_S_Gen5_1.
az sql server create -g westmarch-rg -n westmarch-sql --admin-user wmadmin --admin-password '<STRONG-PW>'
az sql db create -g westmarch-rg -s westmarch-sql -n WestMarch \
  --edition GeneralPurpose --compute-model Serverless --family Gen5 --capacity 1 --auto-pause-delay 60
az sql server firewall-rule create -g westmarch-rg -s westmarch-sql \
  -n allow-azure --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0   # "Allow Azure services"

# Storage for announcement images
az storage account create -g westmarch-rg -n westmarchimg --sku Standard_LRS

# App Service on Linux B1, with WebSockets on
az appservice plan create -g westmarch-rg -n westmarch-plan --is-linux --sku B1
az webapp create -g westmarch-rg -p westmarch-plan -n <APP-NAME> --runtime "DOTNETCORE:10.0"
az webapp config set -g westmarch-rg -n <APP-NAME> --web-sockets-enabled true
az webapp config appsettings set -g westmarch-rg -n <APP-NAME> --settings \
  ASPNETCORE_ENVIRONMENT=Production \
  ConnectionStrings__DefaultConnection="<AZURE-SQL-ADO-CONNECTION-STRING>" \
  ImageStore__Provider=AzureBlob \
  ImageStore__AzureBlob__ConnectionString="<STORAGE-CONNECTION-STRING>" \
  InitialAdmin__Email="you@example.com" \
  InitialAdmin__Password="<STRONG-PW>" \
  InitialAdmin__DisplayName="Campaign Admin"
```

**Deploying:** run [`./deploy.ps1`](deploy.ps1) (requires `az login`). It publishes a Release
build, packages it correctly for Linux, uploads it to blob storage, and restarts the app
(run-from-package). Edit the parameter defaults at the top of the script for other resource names.

**CI:** [.github/workflows/ci.yml](.github/workflows/ci.yml) builds and runs the test suite on
every push and PR to `main`. (Deployment is intentionally a separate manual step via `deploy.ps1`;
push-to-deploy CI can be added later with an Azure service principal.)

After a non-seeded first deploy: sign in as the InitialAdmin, open **Campaign Admin → Import
Items** and **Import Monsters**, upload the three `/data` files, then use the People manager to
promote DMs. (With `SeedDemoData=true`, all of this is already populated.)
Clear the `InitialAdmin__Password` setting once you're in if you prefer.

## Phase roadmap

- **Phase 1:** users + additive roles, Discord/local auth, characters with required
  DDB links + leveling progress, adventure authoring lifecycle (Draft → Ready for Review →
  Approved) with tags and structured rewards, sessions with DM assignment / sign-up /
  completion credits, real-time boards, calendar + LFP + needs-a-DM views, CA announcements
  with images, CA people manager, tests.
- **Phase 2:** item catalog with CA file imports + custom items + price
  overrides, catalog-backed adventure rewards with tier filtering and CA review flags,
  post-session reward collection, character gold/inventory/ledger, player marketplace
  (quick-sell + fixed-price listings), CA audit ledger.
- **Phase 3 (this build):** SRD bestiary with CA imports, structured encounters
  (read-aloud, NPCs, CR-filtered monster picker) replacing free-text stat blocks, and the
  DM screen — a run view with the party's critical stats and encounters as tabs, plus the
  `IDmScreenTool` seam for future table tools.
- **Phase 4+ (designed for, not built):** DM screen tools (initiative, HP/conditions),
  marketplace bidding/auctions, reservable session resources (store tables), further auth
  providers, deeper DDB import beyond the stat header.
