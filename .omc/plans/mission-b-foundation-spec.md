# Mission B — Architecture-Decoupling Foundation Contract Spec

> Authoritative blueprint produced by the foundation-design architect (read-only, opus),
> evidence-cited against current post-Mission-A code. Phase-1 authors the contract files;
> Phase-2 parallel workers implement against them; Phase-3 integrates shared files.
> The api-safety unit suite + `scripts/passoff.ps1` stay green UNCHANGED — that is the regression gate.

## Strategic decisions (coordinator-ratified)

1. Per-aggregate interfaces under `Interfaces/Stores/`: `IAvatarStore`, `IWalletStore`,
   `IHolonStore`, `IBlockchainOperationStore`, `ISTARStore`, `IQuestStore`, `INftStore`,
   `IBridgeStore` + `Models/Bridge/BridgeStatusMutation.cs`. NOT a generic `IRepository<T>`.
2. `IQuestRepository` + `QuestRepository` = dead code (zero production ctor consumers;
   QuestManager uses ProviderContext.CurrentProvider, QuestInstantiator uses OASISDbContext).
   Pure deletion in Phase-3.
3. `IBridgeStore`: author contract + `EfBridgeStore` (thin pass-through). DO NOT migrate
   `CrossChainBridgeService`/`ReconciliationService` onto it in Tier 1 — they keep direct
   `OASISDbContext _db`. The api-safety SQLite tests bind to that shape; migrating is the
   highest-risk/lowest-value part and the SurrealDB track's job. Contract still ships.
4. `ProviderContext` + provider-selection + decorator + ProviderSelection/* + InMemoryStorageProvider:
   DELETE entirely (single-provider reality → selection is dead weight). Managers inject
   concrete `I*Store`. Retain `IProviderHealthMonitor` only for `/health`.
5. `IQuestNodeHandler { QuestNodeType NodeType {get;} ; Task<OASISResult<QuestNode>> HandleAsync(Quest, QuestNode, CancellationToken ct=default); }`
   + `IQuestNodeHandlerRegistry { bool TryGet(QuestNodeType, out IQuestNodeHandler); }`.
   34 scoped handlers (one per QuestNodeType). QuestManager ctor 9→3
   (`IQuestStore, IQuestDagValidator, IQuestNodeHandlerRegistry`). The entire quest
   vertical (QuestManager.cs, Handlers/*, QuestNodeHandlerRegistry, Models/Quest/NodeConfigs.cs,
   Services/Quest/QuestNodeResults.cs, QuestNodeJson.cs) is W3-owned (avoids QuestManager.cs
   shared-ownership; only W3 consumes the quest helpers).
6. ExecutionOrder: `QuestDagValidator.cs:98-102` is the single authority; W3 deletes the
   duplicate `QuestManager.cs:187-196` during its rewrite.
7. SwapManager static cache → injected `IMemoryCache` (SizeLimit + per-entry Size=1 +
   sliding/absolute expiry ~2min). Every cache write MUST `.SetSize(1)` or it throws.
8. OTel deps (only sanctioned new deps): `OpenTelemetry.Extensions.Hosting`,
   `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`,
   `OpenTelemetry.Exporter.OpenTelemetryProtocol`. `/health` is framework-native (no package).
9. Gate: sibling `scripts/passoff-architecture.ps1` that INVOKES `scripts/passoff.ps1`
   (asserts exit 0 = safety regression) + adds: warnings ≤17 / zero-new, zero god-iface refs
   (grep `IOASISStorageProvider|IOASISStorageProviderNFTExtensions|IQuestRepository|\bProviderContext\b|\.CurrentProvider\b` over *.cs excl bin/obj/conductor/.pi/.omc → 0), `/health` 200 smoke.

## Phase-1 foundation files (NEW only, no edits to existing files, build stays green)

`Interfaces/Stores/IAvatarStore.cs`, `IWalletStore.cs`, `IHolonStore.cs`,
`IBlockchainOperationStore.cs`, `ISTARStore.cs`, `IQuestStore.cs`, `INftStore.cs`,
`IBridgeStore.cs`; `Models/Bridge/BridgeStatusMutation.cs`;
`Interfaces/Quest/IQuestNodeHandler.cs`, `Interfaces/Quest/IQuestNodeHandlerRegistry.cs`.

Signatures per the architect spec (see §1.2/§1.3/§3 of the design — coordinator holds the
full text; executors get exact signatures in their task prompts). Match real types:
`IAvatar/IWallet/IHolon/IBlockchainOperation/ISTARODK`, NFT `IAvatarNFT/IHolonNFTBinding/
IWalletNFTBinding`, `Quest/QuestTemplate/QuestNodeTemplate` (OASIS.WebAPI.Models.Quest.*),
`HolonQueryRequest`, `OASISResult<T>`, `BridgeTransactionResult`, `BridgeStatus`,
`ConsumedVaaRecord`, `BlockchainOperation`. Verify types/namespaces against real code.

## Phase-2 partition (5 disjoint workers — author build once per wave, never concurrent)

- W1 EF adapters: `Providers/Stores/Ef*Store.cs` (8 new files incl. EfBridgeStore). Lift bodies
  from `EfStorageProvider.cs` verbatim (esp. SaveQuestAsync graph child-sync :393-421).
- W2 manager migrations: `Managers/{Avatar,Wallet,Holon,Nft,STAR,Search,BlockchainOperation}Manager.cs`,
  `Managers/AvatarNFTService.cs` — drop ProviderContext, inject I*Store, delete activation guards,
  remove the `(IOASISStorageProviderNFTExtensions)` cast.
- W3 quest vertical: `Services/Quest/Handlers/*` (34), `Services/Quest/QuestNodeHandlerRegistry.cs`,
  `Managers/QuestManager.cs`, `Models/Quest/NodeConfigs.cs`, `Services/Quest/QuestNodeResults.cs`,
  `Services/Quest/QuestNodeJson.cs`, `tests/.../Quest/Handlers/*`. ExecutionOrder dup deletion here.
- W4 hygiene: `Managers/SwapManager.cs` (IMemoryCache), `Services/QuestDagValidator.cs` (confirm
  sole ExecutionOrder writer), AutoFailOver/AutoReplication enum removal from OASISRequest/Models.Requests.
- W5 observability: NEW `Observability/{OpenTelemetryExtensions,HealthCheckExtensions,
  StorageHealthCheck,ProviderHealthMonitorHealthCheck}.cs` (extension methods only; does NOT edit Program.cs).

## Phase-3 sequential integration (single author, ordered)

1. Delete dead code: IOASISStorageProvider(+NFTExtensions), IQuestRepository, QuestRepository,
   EfStorageProvider, ProviderContext, ProviderDecorator, HealthRecordingProviderDecorator,
   Core/ProviderSelection/*, IProviderSelectionStrategy, InMemoryStorageProvider.
2. `Program.cs` DI: remove god/ProviderContext/decorator/selection/QuestRepo wiring; add
   8 `AddScoped<I*Store,Ef*Store>`, 34 `AddScoped<IQuestNodeHandler,*>` + registry,
   `AddMemoryCache(o=>o.SizeLimit=…)`, W5's `AddOasisObservability()`/`MapOasisHealth()`.
3. Fix test fixtures mocking the god interface (NOT the api-safety safety tests — those bind
   to OASISDbContext/SQLite, untouched): Wallet/Avatar/STAR/Search/Nft/Holon/BlockchainOperation
   manager tests + delete ProviderContext*Tests.
4. OASISDbContext.cs — likely no change (stores wrap same DbSets); watch-file only.

## Hard invariants (every worker)
- Never weaken/skip a safety/exactly-once/replay/reconciliation/idempotency assertion.
- Greenfield: no compat/migration/dual-write shims. Homebake: only the 4 OTel deps.
- No overengineering; self-documenting; extract shared helpers over duplication.
- Disjoint file ownership; no concurrent dotnet builds (bin/obj lock).
- IBridgeStore/EfBridgeStore must NEVER assert==1 / retry / read-modify-write — it returns
  the affected-row count verbatim; status policy stays in the caller.
