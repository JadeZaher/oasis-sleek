# SurrealDB Migration — Plan

## Tasks

### Foundation
1. [ ] Pin `surrealdb.net` exact version; add a build/test check that fails on version drift (G4)
2. [ ] Stand up SurrealDB locally + as a test container; integration test harness
3. [ ] Define `SCHEMAFULL` schemas for value tables (wallet, bridge tx, swap state, NFT ownership, operation log) with field types + asserts (G6); schemaless for holon/quest flexible attrs
4. [ ] Build a parameterized SurrealQL query layer; add a lint/review gate banning string-interpolated queries (G3)

### Adapter behind the seam
5. [ ] Implement SurrealDB adapters for every per-aggregate interface from `architecture-decoupling` (IAvatarStore/IWalletStore/IHolonStore/IQuestStore/INftStore/IBridgeStore)
6. [ ] Implement single-field conditional state transitions for bridge/op records (`UPDATE … WHERE status=expected`, assert one row) — preserve G2 semantics from `api-safety-hardening`
7. [ ] Port the consumed-VAA ledger + idempotency-key store to SurrealDB unique constraints
8. [ ] Preserve chain reconciliation (G7) against SurrealDB-stored state

### Graph remodel
9. [ ] Model quest nodes/edges via `RELATE` edges; reimplement DAG validation (acyclicity, reachability) using SurrealDB graph queries or retained iterative validator
10. [ ] Model holon polyhierarchy via graph edges; port query/propagate/compose/move-subtree
11. [ ] Single authoritative `ExecutionOrder` (carry the `architecture-decoupling` fix forward)

### Operations (guardrails)
12. [ ] Deploy config: `SURREAL_SYNC_DATA=true` / Immediate durability (G1)
13. [ ] Scheduled `surreal export` backup job + documented, periodically-run restore drill (G5)
14. [ ] Schema migration via gated job (`surrealdb-migrations` or `surrealkit`), not app boot
15. [ ] Wire OpenTelemetry/metrics to SurrealDB calls (uses `architecture-decoupling` observability)

### Remove EF
16. [ ] Delete `OASISDbContext`, `EfStorageProvider`, `Migrations/`, `InMemoryStorageProvider`, Npgsql + EF Core packages
17. [ ] Remove `db.Database.Migrate()` path entirely
18. [ ] Confirm zero EF/Npgsql references remain

### Pre-cutover gate (all must PASS)
19. [ ] Crash/power-loss test green (G1+G7)
20. [ ] Idempotency/TOCTOU test green (G2)
21. [ ] Reconciliation drill green (G7)
22. [ ] Restore drill green (G5)
23. [ ] Injection suite green (G3)
24. [ ] SDK-pin test green (G4)

### Verification
25. [ ] Port full test suite to the SurrealDB harness; all passing
26. [ ] `dotnet build` — zero warnings
27. [ ] Sign-off: every guardrail G1–G7 demonstrably met (evidence linked)
