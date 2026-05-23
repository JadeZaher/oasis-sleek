# Mission B — Phase-3 Sequential Integration Punch List

Accumulated from Phase-2 worker reports. Phase-3 is a SINGLE sequential author
(coordinator) — delete dead code → wire Program.cs DI → fix fixtures → build green.
The api-safety safety suite (CrossChainBridgeService/Reconciliation/Idempotency/Secp256k1)
stays green UNCHANGED — regression gate.

## Phase-3 ordered steps (baseline from architect spec §4.1)
1. Delete dead code: `Interfaces/IOASISStorageProvider.cs`, `Interfaces/IOASISStorageProviderNFTExtensions.cs`,
   `Interfaces/IQuestRepository.cs`, `Services/Quest/QuestRepository.cs`, `Providers/EfStorageProvider.cs`,
   `Core/ProviderContext.cs`, `Core/Decorators/ProviderDecorator.cs`, `Core/Decorators/HealthRecordingProviderDecorator.cs`,
   `Core/ProviderSelection/*`, `IProviderSelectionStrategy`, `Providers/InMemoryStorageProvider.cs`.
2. Program.cs DI: remove ProviderContext/god/decorator/selection/QuestRepository wiring;
   add 8 `AddScoped<I*Store,Ef*Store>`, 34 `AddScoped<IQuestNodeHandler,*>` + `IQuestNodeHandlerRegistry`,
   `AddMemoryCache(o => o.SizeLimit = <bounded, e.g. 1024>)`, W5's `AddOasisObservability(config)` +
   `AddOasisHealthChecks()` + `app.MapOasisHealth()` (+ correlation middleware if W5 exposes one).
   Optional: move `db.Database.Migrate()` to a gated path (best-effort; greenfield-acceptable interim).
3. Fix test fixtures mocking the god interface (NOT api-safety safety tests): Wallet/Avatar/STAR/
   Search/Nft/Holon/BlockchainOperation manager tests → mock the new I*Store; delete ProviderContext*Tests.
4. OASISDbContext.cs — likely no change (stores wrap same DbSets); watch-file only.

## Flagged by W4 (SwapManager/hygiene)
- **`Validators/OASISRequestValidator.cs:14-18`** — has `RuleFor(x=>x.AutoFailOverMode).IsInEnum()` +
  `RuleFor(x=>x.AutoReplicationMode).IsInEnum()`. Those OASISRequest props are DELETED. Phase-3 MUST
  remove those two RuleFor blocks. (This validator file is NOT in the dead-code delete list — it stays.)
- **`Core/AutoFailOverMode.cs` and `Core/AutoReplicationMode.cs`** — W4 emptied them to tombstone
  comments (couldn't delete files). Phase-3: DELETE these two now-empty files outright.
- W4-confirmed: `QuestDagValidator.Validate()` (Services/QuestDagValidator.cs:97-102) is the SOLE
  ExecutionOrder authority, mutates quest.Nodes in place — W3's deletion of QuestManager.cs:187-196 is safe.
- Other AutoFailOver/AutoReplication usages are only in ProviderContext.cs + ProviderContext*Tests
  (all Phase-3-deleted) — safe.

## Flagged by W5 (OTel/health) — exact Program.cs wiring
- `builder.Services.AddOasisObservability(builder.Configuration);`
- `builder.Services.AddOasisHealthChecks();`
- `app.UseOasisRequestCorrelation();`  (pipeline: after UseRouting, before MapControllers)
- `app.MapOasisHealth();`  (endpoint mapping — exposes GET /health)
- Available (not auto-wired): `OpenTelemetryExtensions.ActivitySourceName` / `.ActivitySource`.
- New appsettings keys (document in GO-TO-PROD §2): `OpenTelemetry:ServiceName` (def "OASIS.WebAPI"),
  `OpenTelemetry:Otlp:Endpoint` (none ⇒ SDK default / env, never throws), `OpenTelemetry:Otlp:Protocol` (def "grpc").
- **RETAIN `IProviderHealthMonitor` + `Core/ProviderHealthMonitor.cs`** (NOT in dead-code delete list —
  `ProviderHealthMonitorHealthCheck` depends on it). Note: with the decorator deleted nothing records
  scores ⇒ GetScores() empty ⇒ check reports Healthy("no degradation recorded"); StorageHealthCheck
  (CanConnectAsync) is the meaningful one. Flag for architect review (vestigial-but-graceful, acceptable Tier 1).

## Phase-4 architect-review notes (design deviations to validate, not Phase-3 actions)
- **W1 EfBridgeStore.TryTransitionBridgeStatusAsync**: EF Core 8 `ExecuteUpdateAsync` takes an
  `Expression<Func<SetPropertyCalls<T>,...>>` (expression tree — no statement-body `if`), so
  conditional per-field chaining isn't compilable. W1 used the value-selector form
  `b => m != null && m.Field != null ? m.Field : b.Field` (untouched columns write their own
  current value IN-SQL; no app-side read/RMW; raw rowcount returned; no assert/retry/auto-advance).
  Behaviorally equivalent; only affects the SurrealDB-precondition contract (EfBridgeStore is NOT
  consumed by CrossChainBridgeService/ReconciliationService in Tier 1 — they stay on direct _db).
  Architect: confirm equivalence acceptable.

- **W2 NftManager ProviderName**: `_providerContext.CurrentProvider.ProviderName` → hardcoded
  literal `"PostgreSQL"` (byte-identical to the deleted `EfStorageProvider.ProviderName => "PostgreSQL"`).
  Acceptable Tier 1 (single provider); when SurrealDB lands, source this from config/store. Architect: confirm.
- W2 retained all inert `OASISRequest? request` params in public manager signatures (controllers +
  Phase-3 test fixtures depend on them) — by design, not debt.

## Independent review verdict (Phase-4b, opus, read-only): APPROVE-WITH-SIMPLIFICATIONS
- CRITICAL: none. HIGH: none. Exactly-once invariant intact (value-path services + safety tests
  byte-identical to Mission A 1b25f50, verified). Build 0/17, 532/532 independently reproduced.
- Acceptance (a)-(d) PASS; (e) PARTIAL = **M1** (apply): correlation reaches OTLP spans but NOT
  console logs by default — `appsettings.json` Logging lacks `Console:IncludeScopes:true`.
  FIX: add `"Console": { "IncludeScopes": true }` under Logging (config-driven; satisfies the
  "correlation in logs" acceptance clause). Apply AFTER the architecture gate run finishes
  (avoid racing its live API boot reading appsettings.json), then re-run the gate.
- **M2** (documented debt → GO-TO-PROD/runbook + surrealdb-migration): deleted EfStorageProvider/
  InMemory tests gave the only direct store-CRUD/query coverage; no Ef*StoreTests replacement
  (bodies are proven verbatim lifts ⇒ no logic regression). Add minimal SQLite Ef*StoreTests in
  surrealdb-migration / integration suite.
- **L1** (documented debt): ProviderHealthMonitorHealthCheck vestigial (nothing records scores ⇒
  always Healthy, graceful, never throws). Acceptable Tier-1; re-wire/remove in surrealdb-migration.
- **L2** (documented debt, already accepted): inline `db.Database.Migrate()` Program.cs — greenfield interim.
- N1-N3 cosmetic — leave (N3 = serialize-before-IsError is verbatim-preserved on purpose).

## (further worker findings appended as they report)
