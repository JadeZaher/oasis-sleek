# SYSTEMS Persona — Second-Order Effects of the SurrealDB Migration

The spec is internally coherent on G1–G7. The blind spots are **outside the gate set** — in the
ripples the gates assume someone else has thought about. Tracing 10:

## 1. The SDK pin ripple (`surrealdb.net [0.10.2]` exact)

- **CVE response procedure is undefined.** A pin-exact policy means `dotnet update` is forbidden.
  Spec G4 says "pin"; it doesn't say *who decides when a CVE warrants unpinning*, or what tests gate
  the bump. Affected file: `oasis-sleek.csproj` (PackageReference Version pin) and a missing
  `SECURITY.md` lane. **Wave-2 mitigation:** one paragraph in `RESIDUAL-RISK-RUNBOOK.md` defining
  the unpin path: CVE → spike branch → re-run `passoff.ps1` + integration suite + crash-durability
  test → review gate → bump.
- **Transitive surface is unowned.** `surrealdb.net 0.10.2` pulls in MessagePack, System.Text.Json,
  WebSocket clients; .NET 9 STJ source-gen has breaking shifts. The pin freezes the *direct* dep
  but `<PackageReference>` floats transitives — needs `<PackageLockFile>` or
  `RestorePackagesWithLockFile=true`. Cost: silent transitive upgrade defeats the "one-file blast
  radius". **Wave-2:** add `packages.lock.json` + CI check that lockfile is committed.
- **Mono-repo client-version matrix:** SDK 0.10.2 ↔ surreal server 1.x — what server major does the
  podman container in `tests/run-tests.ps1` pin? Spec doesn't say. If server bumps minor without
  SDK bump, websocket protocol drift → silent test failures. Cost: ~1 day debug per drift.

## 2. Developer onboarding ripple

- **Realistic ramp:** Postgres mental model = ~0 days for any backend hire. SurrealDB =
  2–3 weeks to be productive, longer to be safe (LIVE-query semantics, RELATE traversal,
  SCHEMAFULL/LESS interplay, fsync flags). For a 3–5 engineer team this compounds: every PR review
  on persistence code is now bottlenecked on the 1–2 engineers who actually understand
  SurrealQL semantics.
- **Hiring pitch in 2027:** "we run SurrealDB in production" reads as a *negative* signal to senior
  backend candidates who have been burned by pre-1.0 datastores (RethinkDB, FoundationDB pre-Apple,
  CockroachDB pre-1.0). It also reads as a *positive* signal to a smaller pool of graph-curious
  engineers — net is a tighter talent funnel. Affected: hiring docs, role: tech lead. **Wave-2:**
  publish `docs/surrealql-cheatsheet.md` + a 1hr internal onboarding video; cite it in the JD to
  flip the signal from "weird" to "we know the costs and own them."

## 3. Observability ripple

Spec mentions `OpenTelemetry/metrics to SurrealDB calls` (task 15) but **only at the
`ISurrealExecutor` boundary**. What's missing:

- **Query plan visibility:** SurrealDB has `EXPLAIN`, but no `auto_explain`-equivalent. No slow-
  query log threshold + capture. **Wave-2:** add an executor decorator that auto-runs `EXPLAIN` on
  any query >50ms and ships it as a span attribute.
- **Index usage stats:** Postgres `pg_stat_user_indexes` tells you a dead index. SurrealDB does
  not expose this. Indexes get added, never get removed → schema rot.
- **Connection pool metrics:** `surrealdb.net` 0.10.2's pool internals are not OTel-instrumented;
  you'll instrument *call latency* and miss *queue depth*. Costs you the early-warning signal
  before a pool exhaustion incident.
- **Lock contention:** SurrealDB has no `pg_locks` view. The conditional-update assertion
  ("affected one row") is the only signal — and only at app layer.

**Wave-2 mitigation:** expand task 15 to: executor latency + EXPLAIN spans + pool gauges +
per-table affected-row counters. Without these, an incident in prod is debugged blind.

## 4. Backup tooling ripple

`surreal export` is a logical dump (full DB scan, single stream). Concrete costs:

- **No incremental, no PITR, no streaming WAL ship.** At 1GB it's fine; at 100GB the dump is
  10s of minutes and the restore is *hours* (re-parse SurrealQL inserts row-by-row).
- **RPO/RTO realistic:** RPO = "since last full dump" (hours, not minutes). RTO = full-dataset
  walltime restore. Postgres has `pg_basebackup` + WAL streaming → RPO ~seconds. The G5 gate
  measures one drill at small scale; at launch scale it silently regresses.
- **The "audit/orchestration record" framing is load-bearing here:** if it's truly
  "chain = source of truth", you can tolerate higher RPO. If it isn't — saga state, idempotency
  records, consumed-VAA ledger are *all* in this DB and *all* are durability-critical for
  exactly-once. They aren't reconstructable from chain alone.

**Wave-2:** measure restore wall time at 10× expected launch volume; if >15 min, build a
filesystem-snapshot fallback (zfs/btrfs snapshot of SurrealKV dir + restore drill on snapshot).

## 5. Homebake ripple

Project memory captures the team preference for own DEX / own signing / own bridge primitives.
Stack composition after this migration:
- Custom DEX adapter logic
- Custom signing in SDK
- Custom Wormhole VAA verifier (`Secp256k1VaaSignatureVerifier`)
- Custom idempotency + reconciliation spine
- Pre-1.0 SurrealDB as the sole engine

That's **5 load-bearing custom pieces**, all on the safety-critical path. If 3 engineers leave in
24 months, the on-call rotation has nobody who has read all 5. The bus factor on `passoff.ps1`'s
green checkmark is ~2 people today. **Wave-2:** require a "two-engineer review" rule for any
change to the value path (`CrossChainBridgeService`, `ReconciliationService`,
`Secp256k1VaaSignatureVerifier`, `IBridgeStore` SurrealDB adapter) and document the bus factor
explicitly in `architecture-decoupling`'s seam doc.

## 6. MCP-surface ripple

Wave-3 builds MCP on SurrealDB. LLMs issuing queries → **SurrealQL injection via prompt-
injection** is now a real attack surface. G3 (banning string interpolation in C#) does **not**
help here — the LLM *is* the string interpolator. Concrete missing pieces:

- A whitelist/grammar gate on LLM-generated queries (only `SELECT` on whitelisted tables, no
  `RELATE`, no `DELETE`, no `LIVE`, no `DEFINE`).
- A separate SurrealDB user with read-only role on a curated *view* table set (G6 SCHEMAFULL
  doesn't restrict access patterns).
- Per-MCP-call audit log of the generated query.

**Wave-2 mitigation:** add task 28 to the plan: "MCP query firewall + read-only role +
audit log" — gated *before* MCP is exposed to any external LLM. The spec hints at MCP being a
future track; the safety design must land in this migration so the schema supports it.

## 7. Bridge × SurrealDB fsync ripple

Plain quantification: bridge `ReverseBridgeAsync` releases value; faucet dispenses test funds.
**Real economic exposure:**
- Faucet daily cap (config `Blockchain:Faucet:DailyCap`) is the immediate blast radius per
  duplicate dispense. Single VAA replay = single mint of bridged amount (testnet sizes today;
  production = uncapped per-tx).
- If `SURREAL_SYNC_DATA=true` is misconfigured (env var unset on one of N replicas, or default
  changes between SurrealDB versions) and a `ConsumedVaaRecord` insert is "committed" in memory
  but lost on crash, the VAA can be redeemed twice. Per-redeem blast = full bridged amount.
- **Legal/regulatory:** a misconfigured *environment variable* causing a double-mint reads in a
  post-mortem as "negligent operations" not "software bug" — which materially affects insurance
  and (depending on jurisdiction) personal officer liability for financial intermediaries.

**Wave-2 mitigation:** boot-time assertion that **fails the process** if the fsync env is not set
to the safe value (don't just default, *assert*). Combine with crash-durability test in CI that
fails the build if the assertion is removed. Today's task 12 says "deploy config sets it" —
move it to *application boot-time hard fail*.

## 8. Integration-test harness ripple

CI now needs SurrealDB container per test run. Concrete numbers:
- Container boot ~10–20s warm, ~30–45s cold (pulling image). 30 PR builds/day × 30s =
  ~15 min/day pure boot. Negligible.
- **Local-dev DX:** every contributor needs podman/docker + the right SurrealDB image version
  matching the SDK pin. First-day onboarding pain. **Wave-2:** add a `scripts/dev-setup.ps1`
  that idempotently spins the container + asserts version match.
- **GitHub Actions cost:** if the harness is rebuilt to use `surrealdb` as a service container
  (GHA-native), cost is ~0. If it spins via testcontainers-dotnet inside a job, latency is
  swallowed inside the runner minute — acceptable.

## 9. api-safety-hardening invariant preservation ripple

The api-safety track shipped: exactly-once on bridge redeem, deterministic idempotency keys,
VAA replay ledger, atomic state transitions, reconciliation, FluentValidation, rate limiting,
secp256k1 verifier. The migration must preserve **each one** through an engine swap.

Multiplied gate count: G1–G7 × ~9 invariants ≈ 30 gate-passes. The spec collapses these into
"the unit suite (537+/now 532/532) is the authoritative gate" — which is correct *only if* the
SurrealDB-backed `Ef*Store`-equivalents are exercised by those same tests. Addendum 3's M2 debt
("no direct EF CRUD/query-filter coverage replaces the deleted StorageProviderTests") is the
gap that will bite here: **the safety tests mock the stores**, so a SurrealDB store with subtly
wrong affected-row semantics passes the unit suite and breaks exactly-once in production.

**Wave-2 mitigation:** before merging task 5 (SurrealDB adapters for `I*Store`), write
SurrealDB-backed `*StoreTests` for *at minimum* `IBridgeStore.AtomicStatusTransitionAsync`
(asserts exactly one row affected) and `IIdempotencyStore.TryClaimAsync` (asserts unique-key
collision is positively typed). These were the M2 debt items; the migration is when they
become safety-critical, not architectural-nice-to-have.

## 10. Frontend / SDK ripple

Engine swap leakage to `@oasis/wallet-sdk` + `frontend/`:
- **Error shapes:** EF + Npgsql throw `DbUpdateException` with PG-specific inner codes; the
  controllers map these to HTTP. SurrealDB SDK errors are differently shaped. If any error
  detail leaks to the API contract (e.g. structured error codes in `OASISResult.ErrorCode`),
  the SDK's error handler may regress.
- **Latency profile:** WebSocket-based SurrealDB SDK has different p99 tail than pooled
  Npgsql. The SDK's `requestBare()` timeout config in `oasis-client.ts` may need re-tuning.
- **Response payloads:** if any controller currently returns EF-shaped DTOs (anonymous objects
  from LINQ projections), the projection logic moves to SurrealQL `SELECT` and the JSON shape
  may drift.

**Wave-2:** snapshot-test the API surface (e.g. record JSON responses for every endpoint pre-
migration; assert byte-identical post-migration). Without this, SDK + frontend versioning gets
entangled with the engine swap and the seam's "one-file blast radius" promise is broken.

---

## Top 3 systemic blind spots

1. **No CVE/SDK-bump procedure.** G4 says "pin"; nothing says "how do we unpin safely when the
   pre-1.0 SDK ships a fix?" This is a *security operations* blind spot — the right answer is
   ~1 page in `RESIDUAL-RISK-RUNBOOK.md`, not absence.
2. **MCP-surface security isn't designed in.** Wave-3's MCP track is mentioned as a dependent
   ("blocks `mcp-surface`") but the SurrealDB schema work here doesn't carve out the read-only
   role / view-table layer that MCP will need. Retrofitting it later means re-touching every
   SCHEMAFULL table.
3. **API-contract regression isn't gated.** The seam contains the *engine* but not the *response
   shape*. SDK + frontend can silently break when EF DTOs morph into SurrealQL DTOs.

## Load-bearing decisions the spec treats as small choices

- **SurrealKV vs RocksDB backend** — wave-1 picks SurrealKV via `SURREAL_SYNC_DATA=true` AND
  `SURREAL_KV_ROCKSDB_SYNC=true` (the OR-of-flags is the hedge: same script works whichever
  backend the container compiles in). This hedge is a *signal the team isn't sure* — and the
  backend choice has different fsync semantics, different export performance, different crash
  recovery behavior. Pick one. Document why.
- **WebSocket vs HTTP transport** for `surrealdb.net` — pre-1.0 SDK supports both with
  different reconnection semantics. Spec doesn't say which. The reconnection logic matters for
  the LIVE-query saga trigger (task 8a).
- **Single-node vs cluster** — `surreal export` semantics differ; LIVE queries behave
  differently. Spec implies single-node; not stated.
- **"Schemafull for value tables" granularity** — `ASSERT` clauses are runtime checks; the
  spec doesn't define which fields get which assertions. The bridge `Status` enum check is
  load-bearing for the conditional-update guarantee.
- **The integration-test harness rebuild** is listed as task 2 (single bullet) — it is in
  reality a 1–2 week project on its own.
- **`db.Database.Migrate()` removal** (task 17) presumes the SurrealDB migration job is
  production-ready; the choice between `surrealdb-migrations` and `surrealkit` is one line in
  the spec but they have very different ops semantics (gated job vs library).

## Verdict

**ARCHITECTURE-OK-BUT-FILL-GAPS.**

The core architecture (single seam, guardrails G1–G7, gating tests, documented fallback) is
sound and defensible *for a greenfield pre-launch system*. What's missing is the second-order
operational scaffolding: CVE unpin procedure, MCP security carveouts, API-contract regression
gating, fsync hard-fail at boot, SurrealDB-backed store tests for the safety primitives, and
backup walltime measurement at realistic scale. None of these are architecture rewrites — each
is a 1–3 day task that, if not done in wave-2, becomes an incident in wave-4.
