# Negative-Space — What the Spec Does Not Say

**Persona:** Asymmetric Research Squad — Negative-Space
**Date:** 2026-05-20
**Verdict (preview):** **APPROVE-AFTER-FILLING-BLOCKERS** — three named blockers must close as wave-2 amendments before the persistence seam is poured against SurrealDB; the remaining gaps belong in the wave-3 plan, not wave-2 concrete.

Methodological note: the contrarian argues *against* migrating; the analogical/historical lanes argue *for*. This lane assumes the decision is made and asks: **of all the things wave-2 implementers will reach for in week 2 and find absent from the spec, which ones?** Each gap below is a concrete missing decision, not a vibe.

---

## Gaps

### G-A. Performance budgets are entirely absent
- **Gap.** Spec says "preserve correctness" but defines no p50/p95/p99 latency budget for wallet read, holon graph traversal, saga LIVE-query latency, or bridge state-transition write. There is no "if SurrealDB is 3x slower than the EF/Postgres baseline, that is a pre-cutover-gate failure" line.
- **Why it matters.** Without a budget the cutover gate cannot fail on performance. Wave-3 then ships a measurable regression with no mechanism to roll back to the Postgres fallback, because the fallback trigger ("G1/G2/G7 fail load/chaos testing") explicitly excludes latency.
- **Wave-2 addition.** Add to **Pre-cutover gate**: "Wallet `GET /balance` p99 ≤ 250 ms over 10-min sustained 50 RPS; quest reachability traversal p99 ≤ 150 ms on the seeded fixture set; saga LIVE-query end-to-end (commit → handler claim) p99 ≤ 500 ms; bridge state-transition `UPDATE WHERE status=A` p99 ≤ 50 ms. Captured against the same baseline numbers EF/Postgres produces today (recorded in `tracks/surrealdb-migration/BASELINE-LATENCIES.md` during wave-2 task 1)."

### G-B. No warm-up / first-prod-transaction procedure
- **Gap.** Pre-launch greenfield eliminates data migration risk but the very first real cross-chain bridge through SurrealDB is also the very first time a SurrealKV write durably participates in a value path. No staging-against-real-RPC smoke test is named.
- **Why it matters.** A latent SDK bug or sync-flag misconfiguration manifests on the first real VAA, not on the unit suite.
- **Wave-2 addition.** Add to **Pre-cutover gate**: "Staging environment runs ≥ 72 h against testnet RPC endpoints (Wormhole testnet, Algorand testnet, Solana devnet) with synthetic bridge traffic at 1 op/min, surfaces zero idempotency-violation alerts, zero reconciliation divergences, zero LIVE-query gap events. Pre-launch checklist gates production cutover on this run completing green."

### G-C. SurrealDB transaction/isolation semantics for multi-statement bridge writes are unspecified
- **Gap.** Bridge redeem requires: insert consumed-VAA row + UPDATE bridge_tx status + append operation_log row, in one atomic unit. SurrealDB supports `BEGIN TRANSACTION / COMMIT / CANCEL`, with optimistic concurrency and version-vector conflict detection. The spec defines G2 (conditional-update guard) but does not specify whether the bridge writes one statement, three statements in a transaction, or three independent statements wrapped by the saga outbox.
- **Why it matters.** Under concurrent retry, the wrong choice silently breaks the exactly-once guarantee that G2/G7 promise to preserve.
- **Wave-2 addition.** Add a new section **Transaction model** to the spec: "Every irreversible bridge step writes (a) the conditional state transition, (b) the consumed-VAA ledger row, (c) the outbox/operation-log row inside one `BEGIN…COMMIT` block. On commit conflict the saga returns NOT-FATAL and the step is re-claimed; the idempotency key prevents duplicate chain effect. Isolation level documented as the SurrealDB default (snapshot/optimistic) with a CI test that simulates a write conflict and asserts the retry path does not produce a duplicate chain call."

### G-D. Connection management is undefined
- **Gap.** Pool size, per-request vs pooled, command timeout, retry/backoff on transient transport errors, keep-alive — none named. `ISurrealExecutor` abstracts the choice away but the choice still has to be made and load-tested.
- **Why it matters.** Default pool sizes in pre-1.0 SDKs are often "1 shared connection." Under 50 RPS that produces head-of-line blocking masquerading as SurrealDB being slow.
- **Wave-2 addition.** Add to **Carried over**: "`SurrealExecutorOptions` defaults: `MaxPoolSize=32`, `CommandTimeoutMs=5000`, `ConnectAttempts=3` with exponential backoff (250/500/1000 ms), `KeepAliveSeconds=30`. Configurable via `appsettings.json`; CI integration suite asserts behaviour under simulated transport drops."

### G-E. Multi-environment isolation strategy is undefined
- **Gap.** Spec mentions one container. Dev (per-developer vs shared), CI (ephemeral vs persistent), staging (shared), production (dedicated) each have different concurrency, durability, and namespace requirements. SurrealDB has `NS` + `DB` two-level isolation — the spec never picks which dimension separates environments vs tenants.
- **Why it matters.** A wrong choice leaks staging traffic into prod namespace queries, or makes CI flake under shared-DB contention exactly the way the EF harness already failed.
- **Wave-2 addition.** Add **Environment topology** subsection: "Per-environment dedicated `NS` (`oasis_dev`, `oasis_ci`, `oasis_staging`, `oasis_prod`); single `DB` (`main`) within each NS; per-developer override via env-var `OASIS_SURREAL_NS=oasis_dev_<username>` resolved at app boot; CI test isolation via per-collection ephemeral NS created/torn down per test class (mirrors the integration-test harness rebuild task 2)."

### G-F. RPO / RTO targets are unstated for non-chain-derivable state
- **Gap.** G7 covers value (re-derive from chain). It does NOT cover: API keys (hashed, cannot be re-issued silently), quest definitions, holon polyhierarchy edges, NFT bindings to avatars, MCP context. The spec gives RPO/RTO for nothing.
- **Why it matters.** A SurrealKV cataclysm at month 6 forces "revoke all API keys, ask every avatar to re-create their quests" — a customer-visible event the spec promises will not happen but does not engineer against.
- **Wave-2 addition.** Add **Recovery targets** subsection: "RPO ≤ 5 min for non-chain-derivable state (API keys, quest defs, holon polyhierarchy, NFT-avatar bindings, MCP context) via `surreal export` every 5 min to object storage with versioning; RTO ≤ 30 min from cold-restore-and-replay-since-last-export. Chain-derived value state has RPO = 0 (G7 derives from chain). Restore drill exercises both paths quarterly."

### G-G. SurrealDB auth model and how API authenticates to it are unspecified
- **Gap.** SurrealDB has `USER`/`ROLE`/`SCOPE` (with record-level access). The .NET API authenticates avatars via JWT/API-key and then makes SurrealDB calls — as which user? Root? A `system` user with database-level access? Per-avatar SCOPE? The spec says nothing.
- **Why it matters.** Root credentials in `appsettings` are a different blast radius than a system user with `DEFINE TABLE PERMISSIONS`. Wave-3 MCP raises the stakes — LLM-issued queries can hit SurrealDB through the same connection.
- **Wave-2 addition.** Add **SurrealDB auth** subsection: "API connects as a non-root `system` user (`oasis_api`) with table-level `SELECT/CREATE/UPDATE/DELETE` only on the schemas declared in migrations; no `DEFINE`/`REMOVE` rights (migrations run via a separate `oasis_migrator` user invoked only by the gated migration job, G5). Root credentials live only in the bootstrap secret used by the migration job. Avatar-scoping is enforced **in the C# manager layer**, not via SurrealDB SCOPE (SCOPE is reserved for the wave-3 MCP boundary). Connection string template + secret-management contract recorded in `tracks/surrealdb-migration/DEPLOYMENT-SECRETS.md`."

### G-H. MCP runtime-injection defense is not addressed at all
- **Gap.** G3 covers compile-time C# string interpolation. MCP exposes SurrealQL-shaped traversal to LLMs; the LLM can compose query bodies that the analyzer never sees because they are constructed at runtime by another model. The spec is silent on this distinct vector.
- **Why it matters.** This is the single most likely future incident class. An LLM-as-confused-deputy injection is not theoretical.
- **Wave-2 addition.** Add to **G3** explicitly: "G3a — MCP tools never accept raw SurrealQL strings from agents; tools expose typed parameter interfaces only, and the tool implementation composes the query in C# via parameterized methods. A separate `mcp-surface` ADR will detail the LLM-input firewall and execute-against-a-restricted-`SurrealQueryShape` allowlist; foundation work for that allowlist lives here." (Cross-referenced in `mcp-surface/spec.md` as a non-negotiable.)

### G-I. Observability is named but the SurrealDB-specific signals are not enumerated
- **Gap.** Architecture-decoupling says "OpenTelemetry wrapped around `ISurrealExecutor`." It does NOT name: slow query log, query-plan capture (`EXPLAIN`), connection-pool gauges, LIVE-query subscriber count, LIVE-query event-emit lag, `surreal export` job duration/last-success age, transaction conflict counter, storage write-amplification.
- **Why it matters.** When SurrealDB is slow at 3 a.m. ops cannot answer "why" with the same fluency as `pg_stat_statements`. Tracking spans alone is not enough.
- **Wave-2 addition.** Add **Observability surface** subsection enumerating the metric/log/trace set above, with named OTel metric identifiers (`surreal.query.duration`, `surreal.live.lag`, `surreal.transaction.conflicts`, `surreal.pool.in_use`, `surreal.export.last_success_ts_seconds`). Slow-query threshold = 100 ms; logged with the SurrealQL body and parameter hash (never the raw values).

### G-J. Cost / hosting model is unstated
- **Gap.** Self-managed VM? Containerised on Fly/Railway? SurrealDB Cloud (does it exist commercially? at what tier?)? The spec is silent and the data-engine-decision memory says "sole engine" without a deployment SKU.
- **Why it matters.** Cost surprise at wave-3 cutover; also impacts the backup-storage cost line item for G5.
- **Wave-2 addition.** Add **Hosting target**: "Production runs as a single-node `surrealdb/surrealdb:latest-alpine` container on the existing Railway project (`oasis-surrealdb`), with persistent volume on SurrealKV, single replica wave-1, cluster mode evaluated post-launch only when warranted. Backup target: existing Railway object-storage bucket `oasis-surreal-backups`; estimated wave-1 monthly cost ≤ \$30 ingress + storage."

### G-K. SDK / frontend contract surface is uncharted territory
- **Gap.** 76-test `@oasis/wallet-sdk`, 60+ endpoints, Next.js frontend. The engine swap changes response shapes whenever a controller returns SurrealDB row IDs (which are `table:id` strings, not GUIDs) or pagination cursors. The spec does not state whether SDK consumers see any difference.
- **Why it matters.** Frontend regression in wave-3 with no SDK version bump = silent breakage for any external SDK consumer.
- **Wave-2 addition.** Add **External contract invariant**: "All controller response DTOs preserve their pre-migration JSON shape; SurrealDB record IDs are translated to GUIDs at the persistence-seam boundary; pagination cursors remain opaque base64 (not raw `id:rid` strings). A new contract test (`OASIS.WebAPI.IntegrationTests/Contracts/ResponseShapeStabilityTests.cs`) captures golden JSON for every endpoint pre-migration and asserts byte-for-byte equivalence post-migration."

### G-L. The Postgres fallback decision has no time budget or pass/fail criteria
- **Gap.** "If G1/G2/G7 fail load/chaos testing: Postgres for the audit ledger only." Who declares failure? Within what window? What chaos suite specifically (Toxiproxy? `pumba`? hand-rolled `kill -9` loops?)? Wave-3 EF deletion locks in the SurrealDB choice — when does the option close?
- **Why it matters.** A vague fallback is no fallback. Without a budget, the team will sunk-cost-itself past the point of revert.
- **Wave-2 addition.** Add **Fallback decision protocol**: "Chaos test suite specification published in `tracks/surrealdb-migration/CHAOS-PLAN.md` (toxiproxy network drops, container `SIGKILL` loops, SurrealKV disk-full injection, 100 RPS sustained load). Wave-2 deliverable. Pass/fail criteria are the same as the Pre-cutover gate (G-A, G-B). Decision owner: project tech lead, in writing, before wave-3 task that deletes EF code starts. Hard deadline: 14 calendar days after pre-cutover gate completes. After deadline, fallback is forfeit and `EfStorageProvider` is removed."

### G-M. The persistence-seam shape may not cover SurrealDB-only query patterns
- **Gap.** The seam was sized for EF-era patterns (CRUD + LINQ). SurrealDB-native features wave-3 will reach for — LIVE queries, `RELATE` traversal, HNSW vector search, recursive `->`/`<-` — are not in the per-aggregate interface inventory in architecture-decoupling.
- **Why it matters.** Either the seam leaks (every new feature widens it) or the seam blocks the very capabilities the migration was justified by.
- **Wave-2 addition.** Add to architecture-decoupling acceptance: "Per-aggregate interfaces include `WatchAsync(predicate, ct) → IAsyncEnumerable<TEvent>` (LIVE query shape), `TraverseAsync(start, edgeKind, depth, ct) → IAsyncEnumerable<T>` (graph traversal shape), `VectorSearchAsync(query, k, ct) → IReadOnlyList<TScored>` (vector shape). Stubbed/not-implemented for EF; concrete in SurrealDB impl. Documented in `IPersistenceSeam.md` as the canonical shape." (Cross-referenced from surrealdb-migration as the wave-1 interface contract.)

### G-N. `frontend-demo-harness` track interaction is unaddressed
- **Gap.** The pending `frontend-demo-harness` (shadcn/ui, 6 phases, 38+ tests) sits on the same API surface that wave-3 reshapes. Spec is silent on which lands first or what the migration owes it.
- **Why it matters.** If the demo harness ships first, it freezes response shapes the migration then has to match (helpful). If the migration ships first, the demo harness rewrites for a moving target (wasteful).
- **Wave-2 addition.** Add to **Dependencies**: "`frontend-demo-harness` either lands before wave-3 task that deletes EF (in which case G-K contract tests gate the migration on its endpoint shapes) or is paused until wave-3 cutover completes. Decision recorded in `conductor/tracks.md` before wave-2 starts."

### G-O. LIVE-query reliability fallback is undefined
- **Gap.** Polling `ISagaTrigger` exists today and is being deleted in wave-2. Known SurrealDB LIVE-query bugs (issues #5068, #5014, #5160, #3602) include events being dropped or the subscription silently dying. Spec promises "zero saga/handler code change" but says nothing about belt-and-suspenders.
- **Why it matters.** A lost LIVE event = a saga step never claimed = a stuck bridge or stuck quest.
- **Wave-2 addition.** Add to **Carried over**: "`PollingSagaTrigger` is retained as a fallback in production, gated by config `Saga:TriggerMode=Live|Poll|Both`. Default `Both` for the first 90 days post-cutover: LIVE drives latency, polling sweeps every 30 s as the convergence safety net. Switching to `Live`-only is an explicit ops decision after dead-letter rate proves stable. A counter `saga.steps_claimed_by_poll_after_live` exposes any LIVE miss."

### G-P. Team-size and concurrent-track assumption is implicit
- **Gap.** "4–5 weeks realistic effort" but no headcount or in-flight-track assumption. With one engineer and three other tracks active it becomes a quarter.
- **Why it matters.** Sequencing failures and the wrong tracks land in the wrong order against the migration.
- **Wave-2 addition.** Add **Capacity assumption**: "Assumes one engineer at ≥ 80% allocation for 5 weeks, no concurrent work on `mcp-surface`, `frontend-demo-harness`, or new chain integrations during weeks 2–4 (cutover window). If allocation drops below this, wave-2 deliverable G-L's deadline extends by the same proportion."

### G-Q. Backup encryption + PII handling is not addressed
- **Gap.** `surreal export` produces a dump; the spec puts it on a schedule but never says "encrypted at rest with KMS-managed key" or "PII columns redacted before storage." Avatar profiles include emails, possibly names.
- **Why it matters.** GDPR exposure if Railway storage credentials leak; right-to-erasure for avatar data has to traverse the backup set.
- **Wave-2 addition.** Add to **G5**: "Backups encrypted at rest (Railway-managed bucket encryption + a layer of `age` encryption with the key in a separate vault; bucket and key are owned by different IAM principals). Right-to-erasure path documented: revoke avatar → tombstone in live DB → next backup is the last to contain the rows → all earlier backups expire on the 35-day rotation."

### G-R. The "exit ramp" cost is not quantified
- **Gap.** "Seam makes Postgres-fallback contained" — but contained to how many LOC, how many person-days, how much downtime?
- **Why it matters.** "Contained" is a feeling. Implementers will discover the number the hard way.
- **Wave-2 addition.** Add **Exit ramp estimate**: "Reverting from SurrealDB to a Postgres audit-ledger fallback is a single new persistence adapter (`PostgresAuditLedgerProvider`) implementing the audit-ledger subset of the seam, estimated ≤ 1,500 LOC, ≤ 10 engineer-days, ≤ 1 h downtime (drain saga queue, switch config, restart). Quest/holon graph stays on SurrealDB. Number is challenged and updated at the wave-1 architecture review."

---

## Top 3 blockers (must close before wave-2 starts pouring concrete)

1. **G-A — Performance budgets.** Without numeric targets the pre-cutover gate is uncheckable; the entire wave-2 implementation drifts.
2. **G-C — Transaction/isolation model for multi-statement bridge writes.** Wrong choice silently invalidates G2; this is the load-bearing safety property the migration promises to preserve.
3. **G-M — Persistence-seam shape covers LIVE/traverse/vector.** If the architecture-decoupling seam ships without these shapes, wave-3 either leaks the seam (defeating the whole Tier-1 effort) or cannot adopt the SurrealDB features the migration was justified by.

## Top 3 wave-3-deferrable gaps (worth recording now, not blocking wave-2)

1. **G-H — MCP runtime-injection defense.** Belongs in `mcp-surface` ADR. Wave-2 needs only the cross-reference promise.
2. **G-Q — Backup encryption + PII path.** Real but addressable before public launch, not before wave-2 code starts.
3. **G-R — Exit-ramp LOC quantification.** Useful trust-building artifact; not a blocker.

## Verdict

**APPROVE-AFTER-FILLING-BLOCKERS.** The spec is intellectually honest about its non-negotiables (G1–G7) and its biggest known fallback (Postgres for audit ledger only), but it ships with three load-bearing absences (G-A, G-C, G-M) that would manifest in wave-2 implementation as ambiguity, then in wave-3 cutover as silent regression. Close those three as wave-2 amendments to the spec — published as `tracks/surrealdb-migration/AMENDMENTS-2026-05-20.md` — and the migration can proceed. The remaining 15 gaps are recorded for completeness and folded into the per-task plan, not the gate.
