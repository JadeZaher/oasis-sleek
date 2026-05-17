# AGENTS.md — oasis-sleek repeated-ops context

.NET 8 WebAPI (`OASIS.WebAPI.csproj` at root) + Next.js frontend + `@oasis/wallet-sdk`.
This file is the operational cheat-sheet for build / test / local stack. Keep it
accurate; it is read on every session.

## Container runtime

This machine has **podman** (no docker). All commands below use `podman`;
substitute `docker` if present. DB container name: `oasis-postgres`,
image `postgres:16-alpine`.

## Local stack — `start.ps1`

Full stack = PostgreSQL + .NET API (`:5000`) + Next.js frontend (`:3000`).

```powershell
.\start.ps1 -Mode up      # spin everything up (creates oasis-postgres, builds+runs API, runs frontend)
.\start.ps1 -Mode down    # stop API/frontend, stop & REMOVE the DB container, clean logs
```

- DB host port resolves to **5441** by default (avoids the machine's native
  PostgreSQL on 5432); `start.ps1` rewrites `appsettings.json` `Port=` to match.
- `-Mode down` **removes** the container (data lost). To keep data, stop without
  removing: `podman stop oasis-postgres` then `podman start oasis-postgres`.

## Database

- `docker-compose.yml` defines only Postgres 16 (`oasis`/`oasis`/`oasis123`).
- Connection (config-driven): `appsettings.json` →
  `ConnectionStrings:OASISDatabase` = `Host=localhost;Port=5441;Database=oasis;Username=oasis;Password=oasis123`.
- Bring up just the DB (idempotent, **persistent** data):
  ```powershell
  podman start oasis-postgres 2>$null; if (-not $?) {
    podman run -d --name oasis-postgres -e POSTGRES_DB=oasis -e POSTGRES_USER=oasis `
      -e POSTGRES_PASSWORD=oasis123 -p 5441:5432 postgres:16-alpine }
  ```
- psql into it: `podman exec -it oasis-postgres psql -U oasis -d oasis`
- Schema is created by the API on boot: `Program.cs` runs
  `db.Database.Migrate()` (EF migrations in `Migrations/`). First boot against
  an empty `oasis` DB builds everything; thereafter it is a no-op and **data
  persists between runs**.
- A separate **native PostgreSQL 16** also runs on `localhost:5432`
  (superuser `postgres`). It is unrelated to `oasis-postgres`; do not confuse
  the two (5432 native vs 5441 container).

## Build

```powershell
dotnet build OASIS.WebAPI.csproj -c Debug          # production API only (fast gate)
dotnet build oasis-sleek.sln    -c Debug           # whole solution (incl. test projects)
```

- Green = **0 errors**. There are **17 pre-existing baseline warnings**
  (SolanaProvider nullability, SearchManager, a CS1998) — not regressions; do
  not chase them. Adding NEW warnings is a regression.
- Do **not** run the frontend typecheck — it is known pre-existing noise. The
  gates are `dotnet build` + SDK `tsc` only.
- EF migration: `dotnet ef migrations add <Name> --project OASIS.WebAPI.csproj`.
  If `dotnet ef` fails locking `bin\Debug\net8.0\OASIS.WebAPI.dll`, a stale dev
  server holds it — `Get-Process dotnet,OASIS.WebAPI | Stop-Process -Force`
  first, then rebuild fresh.

## Test — `tests/run-tests.ps1` (single entry point)

```powershell
.\tests\run-tests.ps1                               # unit + integration (Debug)
.\tests\run-tests.ps1 -Configuration Release
.\tests\run-tests.ps1 -Live -LiveUrl https://localhost:5001   # + live HTTP harness
.\tests\run-tests.ps1 -Mutation                     # Stryker.NET -> tests/StrykerOutput
```

`run-tests.ps1` **auto-spins the persistent `oasis-postgres` container**
(idempotent: starts if stopped, creates if missing, leaves a running one alone
so data persists) before the suites.

Targeted runs:

```powershell
# Unit suite (authoritative gate): 493 tests, ~10s, no external deps
dotnet test tests/OASIS.WebAPI.Tests/OASIS.WebAPI.Tests.csproj -c Debug

# One class / filter
dotnet test tests/OASIS.WebAPI.Tests/OASIS.WebAPI.Tests.csproj -c Debug `
  --filter "FullyQualifiedName~CrossChainBridgeServiceTests"
```

- Unit project uses EF-InMemory for fast tests **and** `Microsoft.EntityFrameworkCore.Sqlite`
  for tests that need real UNIQUE constraints / `ExecuteUpdateAsync` rowcounts
  (idempotency, bridge concurrency, reconciliation). Shared test infra lives in
  `tests/OASIS.WebAPI.Tests/TestSupport/` (`SqliteTestContext`,
  `FakeIdempotencyStore`) — reuse it, don't re-roll per-file copies.
- **Integration tests (`OASIS.WebAPI.IntegrationTests`) are a known DEFERRED
  follow-up.** They were built for disposable per-factory EF-InMemory DBs; the
  factory now points at the persistent Postgres, but the harness still does
  destructive teardown + parallel collections that race a shared DB. Treat the
  **unit suite (493/493, includes all safety tests) as the authoritative gate.**
  Follow-up scope: disable xUnit parallelization, remove destructive
  `EnsureDeleted` teardown, migrate the persistent DB once, per-test data
  isolation. See `conductor/tracks/api-safety-hardening/RESIDUAL-RISK-RUNBOOK.md`.

## Conventions for agents

- Config-driven over hardcoded; tests load real `appsettings.json`.
- SDK and .NET providers stay mirrored; new chains via the plugin interfaces
  (`ChainProvider` / `IBlockchainProvider`, `DexAdapter`).
- Self-documenting code; extract test helpers over verbose narrative comments.
- Bridge moves real value — never weaken an exactly-once / replay assertion to
  make a test pass; fix the cause. Pre-launch safety surface + ops runbook:
  `conductor/tracks/api-safety-hardening/RESIDUAL-RISK-RUNBOOK.md`.
