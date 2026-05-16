# SurrealDB Migration — Specification

## Goal
Tier 2: replace EF Core + PostgreSQL + `InMemoryStorageProvider` with
**SurrealDB as the single primary data engine**, behind the persistence seam
from `architecture-decoupling`. Greenfield decision (no production data) —
defensible because there is no migration/dual-write/rollback of live data. The
store is an orchestration/audit layer over the chain (chain = source of truth
for value), which is SurrealDB's strength, not a financial ledger (its weakness).

Realistic effort: **~4–5 weeks** (full storage layer + graph remodel + test
port + guardrails), not the "5–7 days" an earlier draft claimed.

## Removed by decision
PostgreSQL, EF Core (`OASISDbContext`, `EfStorageProvider`, migrations, Npgsql),
`InMemoryStorageProvider`. **No SpacetimeDB, no in-memory hot layer, no
hot/cold split** — premature optimization, consciously deferred.

## Non-negotiable guardrails (acceptance criteria, not best-effort)
- **G1 — Durability forced on.** SurrealKV defaults to `Eventual` (no `fsync`
  before commit). Deploy config sets `SURREAL_SYNC_DATA=true` / `Immediate`;
  crash-durability test in CI. Protects the audit/orchestration record (there
  is no stored balance), which without G7 is equally dangerous.
- **G2 — Idempotency + conditional state guards.** NOT balance CAS (no stored
  balance). (a) deterministic/client idempotency key on every irreversible
  chain op, persisted + checked before broadcast; (b) single-field conditional
  state transitions (`UPDATE x SET status=B WHERE status=A`, assert one row).
  SurrealDB supports this natively. (Idempotency itself is delivered in
  `api-safety-hardening`; this track preserves it through the engine swap.)
- **G3 — Parameterized queries only.** No C# string interpolation into
  SurrealQL ever; SDK `Query(sql, params)` / typed methods; lint + review gate.
- **G4 — Pin the SDK behind the seam.** `surrealdb.net` is pre-1.0 (~0.10.2,
  stale ~Apr 2024). Pin exact version; one-file blast radius via the
  `architecture-decoupling` seam; integration tests vs a real container.
- **G5 — Backup/restore is first-class.** Scheduled `surreal export` +
  periodically **exercised** restore drill; schema via gated migration job
  (`surrealdb-migrations`/`surrealkit`), not app boot. Confirm versioned-data
  export behavior on the chosen version.
- **G6 — Value tables `SCHEMAFULL`.** Wallets, bridge tx, swap state, NFT
  ownership, operation log: enforced schema + asserts. Schemaless only for
  holon/quest flexible attributes and MCP context.
- **G7 — Chain reconciliation mandatory.** Re-derive op/bridge truth from
  chain confirmations, never trust the local lifecycle flag (delivered in
  `api-safety-hardening`; must remain green post-migration).

## Graph remodel (the payoff)
Map quest DAG and holon polyhierarchy to native SurrealDB `RELATE` edges +
`->`/`<-` traversal, replacing in-app graph code where it simplifies. Preserve
the iterative DAG validation guarantees (acyclicity, reachability,
single-`ExecutionOrder`).

## Pre-cutover gate (must pass, not just measure)
- Crash/power-loss: committed orchestration/audit record survives `kill -9` +
  restart and is reconcilable (G1+G7).
- Idempotency/TOCTOU: duplicate + concurrent irreversible op (faucet / bridge
  redeem) → exactly one chain effect (G2).
- Reconciliation drill: kill mid-op → recovery re-derives chain truth (G7).
- Restore drill: export → wipe → import → integrity assertions pass (G5).
- Injection suite: hostile input through every query path (G3).
- SDK-pin: build fails if `surrealdb.net` version drifts (G4).

## Documented fallback (not chosen)
If G1/G2/G7 fail load/chaos testing: Postgres for the audit ledger only,
SurrealDB for graph/MCP. The seam makes this contained. Not the target.

## Dependencies
Requires `architecture-decoupling` (the seam). Requires
`api-safety-hardening` (idempotency/reconciliation must exist before swapping
engines under value paths). Blocks `mcp-surface`.
