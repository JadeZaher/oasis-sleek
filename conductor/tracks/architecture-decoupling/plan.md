# Architecture Decoupling & Observability — Plan

## Tasks

### Persistence seam
1. [ ] Design per-aggregate interfaces: `IAvatarStore`, `IWalletStore`, `IHolonStore`, `IQuestStore`, `INftStore`, `IBridgeStore` — graph-aware methods, intention-revealing (no generic `IRepository<T>`)
2. [ ] Implement EF-backed adapters for the new interfaces (interim — replaced in `surrealdb-migration`)
3. [ ] Migrate all managers off `IOASISStorageProvider` / `ProviderContext.CurrentProvider` onto the new interfaces
4. [ ] Migrate `QuestManager` off `IQuestRepository` onto `IQuestStore`
5. [ ] Delete `IOASISStorageProvider`, `IOASISStorageProviderNFTExtensions`, `IQuestRepository`, `QuestRepository`, `EfStorageProvider`'s god-interface surface
6. [ ] Keep provider-selection/decorator/health infra adapted to the new seam (or document its removal)

### QuestManager decomposition
7. [ ] Define `IQuestNodeHandler` (handle one `QuestNodeType`, returns `OASISResult<QuestNode>`)
8. [ ] Implement one handler per node type (34) wrapping the existing manager calls
9. [ ] Register handlers in DI as a `QuestNodeType → IQuestNodeHandler` map; replace the `ExecuteNodeInternalAsync` switch (`QuestManager.cs:317-631`) with a registry lookup
10. [ ] Reduce `QuestManager` ctor deps to what orchestration still needs
11. [ ] Handler unit tests (each type → correct effect; default → failure)

### Bug + hygiene
12. [ ] Single authoritative `ExecutionOrder` computation; remove the duplicate (`QuestDagValidator.cs:97-102` vs `QuestManager.cs:187-196`)
13. [ ] Replace `SwapManager` static cache with `IMemoryCache` (size limit + sliding/absolute expiry)
14. [ ] `ProviderContext.Activate()` returns the provider; remove mutable scoped `CurrentProvider`
15. [ ] Implement or delete `AutoFailOverMode`/`AutoReplicationMode`
16. [ ] Move `db.Database.Migrate()` out of `Program.cs:243` into a gated migration command/job

### Observability
17. [ ] Add OpenTelemetry (traces + metrics) with request correlation IDs in structured logs
18. [ ] Add `AddHealthChecks` + `MapHealthChecks` `/health` exposing `IProviderHealthMonitor` scores + dependency checks
19. [ ] Verify traces span Controller → Manager → store → chain provider

### Verification
20. [ ] `dotnet build` — zero warnings
21. [ ] All tests passing; mutation score not regressed
22. [ ] Confirm zero remaining references to the deleted god interface
