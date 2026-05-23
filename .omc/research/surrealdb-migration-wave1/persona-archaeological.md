# Archaeological Persona — SurrealDB Migration

**Scope:** Dig into SurrealDB's own GitHub issues, official docs, and security
advisories to surface the production footguns that the marketing buries. The
OASIS spec pins **server 1.5.4 + `surrealdb.net` 0.10.2** and depends on
`surrealdb-migrations`/`surrealkit`, `surreal export`, LIVE queries, and
`RELATE` traversal. Each of those four pillars has load-bearing problems that
are visible in the upstream tracker.

---

## Top 10 Footguns, Ranked by Severity for OASIS

### 1. The pinned tooling (`Odonno/surrealdb-migrations`) is ARCHIVED. SurrealKit (its replacement) does not document data backfill or rollback for breaking changes.

- **Evidence:** `Odonno/surrealdb-migrations` README: *"The project is archived
  in favor of Surrealkit as it is now the official migration tool for
  SurrealDB."* Archived 2026-04-11 (last release v2.4.0, 2025-11-30).
  https://github.com/Odonno/surrealdb-migrations
- **SurrealKit gap:** README explicitly separates `sync` (dev) from `rollout`
  (prod), but does not address **data backfill, online migrations, or
  rollback of breaking schema changes**. Only 50 stars, 9 open issues —
  immature for a "first-class migration job" gate.
  https://github.com/surrealdb/surrealkit
- **OASIS implication:** G5 mandates "schema via gated migration job
  (`surrealdb-migrations`/`surrealkit`)." The spec is pointing at the
  archived tool and an immature replacement.
- **Mitigation:** Pick SurrealKit (the official path), version-pin it, but
  write the OASIS migration runner as a thin wrapper that exposes a `before`
  / `after` hook for **manual backfill scripts**. Treat SurrealKit as a
  schema-DDL applicator only, not a data-evolution tool.

### 2. SCHEMAFULL "ADD field with default" does NOT backfill existing rows. ASSERT clauses do NOT validate existing rows on read.

- **Evidence:** Schemafull CRUD docs and community reports: defaults apply
  only on **new inserts** or on **explicit update**, not retroactively to
  existing records. Confirmed by multiple sources including the official
  fundamentals course. Real-world Reddit/Discord reports: "if you change a
  field's type from int to string using overwrite, existing records still
  have the old type … you must manually update."
  https://surrealdb.com/learn/fundamentals/schemafull/schemafull-crud ·
  https://surrealdb.com/docs/surrealql/statements/define/field
- **OASIS implication:** Every G6 SCHEMAFULL table (wallet, bridge tx, swap
  state, NFT ownership, operation log) will accumulate **partially-conforming
  rows** the moment any field is added, ASSERT tightened, or type widened.
  Reads will silently return mixed shapes; the .NET deserializer (already
  buggy — see #4) will throw on the bad rows.
- **Mitigation:** Treat every schema migration as a **3-step ritual**: (1)
  add field as OPTIONAL, (2) backfill via explicit `UPDATE`, (3) re-define
  field with `ASSERT $value != NONE`. Test the ritual in CI as part of the
  G5 restore drill. Wave-1 task plan must include the backfill harness.

### 3. LIVE queries are explicitly **single-node only** and have **best-effort ordering only** — and the spec depends on LIVE for the saga trigger in wave-2.

- **Evidence:** Official LIVE SELECT docs: *"Currently, LIVE SELECT is only
  supported in single-node deployments, with multi-node support being
  actively developed."* And: *"While a best effort is made to assure
  ordering is correct, a strict correctness is not yet in place for a full
  guarantee … some messages may be received out of order."*
  https://surrealdb.com/docs/surrealql/statements/live · Issue #5070 (still
  OPEN): *"When `LIVE SELECT ...` from a table on surreal node 1, and a
  update is emitted from node 2, node 1 wont receive the event."*
  https://github.com/surrealdb/surrealdb/issues/5070
- **Compounding bug:** Issue #5068 — *"Live query events
  hanging/locking up across the whole SurrealDB process"* — regression
  introduced in 2.0.0-alpha, fixed in PR #5383 but originally said to occur
  "between few minutes – weeks" in production.
  https://github.com/surrealdb/surrealdb/issues/5068
- **OASIS implication:** "Carried over: Saga trigger → LIVE queries" (spec
  line 83) is built on an **at-most-once, best-effort-ordered, single-node**
  primitive. If OASIS ever scales past one node, sagas silently drop. Even
  on one node, sagas can receive out-of-order events — saga handlers must
  be idempotent **and** commutative, which most are not.
- **Mitigation:** Keep `ISagaTrigger` polling implementation as the
  **production default**. Make the LIVE implementation an **optional
  optimization** behind a feature flag, gated on a chaos test that
  kill-9's the LIVE consumer mid-stream and verifies G7 reconciliation
  catches the gap. Do NOT remove polling.

### 4. The pinned .NET SDK (0.10.2) has TWO open data-loss/data-corruption bugs as of May 2026.

- **Evidence (Issue #234, OPEN):** *"the second tuple element is silently
  lost when the first element is a CBOR-tagged value"* (e.g. `NONE` or
  `RecordId`). Data loss without exception. Affects SurrealDB 3.0.1 +
  SurrealDb.Net 0.9.0; no fix landed yet.
  https://github.com/surrealdb/surrealdb.net/issues/234
- **Evidence (Issue #246, OPEN):** *"Stackoverflow exception with recursive
  queries … triggered when using `result.GetValue<T>(int index)`."* Filed
  2026-05-06 against 0.10.2 + Surreal 3.0.5. PR #248 linked but not merged.
  https://github.com/surrealdb/surrealdb.net/issues/246
- **Evidence (Issue #185, OPEN):** SemaphoreSlim release-without-enter
  causes `SemaphoreFullException` under cancellation. Touches connection
  management, idle reconnect.
  https://github.com/surrealdb/surrealdb.net/issues/185
- **Evidence (Issue #236, OPEN):** CBOR deserialization fails on even
  `RETURN 1;` over WebSocket in some configurations.
  https://github.com/surrealdb/surrealdb.net/issues/236
- **OASIS implication:** OASIS has tuples-of-records all over the graph
  layer (`RELATE` returns), recursive holon hierarchies, and uses
  cancellation tokens everywhere. All three CRITICAL open bugs touch
  OASIS-shaped code.
- **Mitigation:** G4 says "pin exact version; one-file blast radius via
  seam." That seam must include **defensive deserialization** — every
  `GetValue<T>` call wrapped, every recursive shape tested in CI against a
  live container, every tuple shape avoided in favor of named records.

### 5. The pinned server version 1.5.4 is two major versions behind (current is 3.0.5) and the upgrade path requires `surreal fix` against on-disk format changes.

- **Evidence:** As of 2026-05, stable is 3.0.5 (released Feb 2026 line).
  1.5.0 shipped May 2024. SurrealDB's security policy explicitly says:
  *"Urgent security patches will be released for the **latest** SurrealDB
  minor release."* https://github.com/surrealdb/surrealdb/blob/main/SECURITY.md
  · https://surrealdb.com/releases
- **Migration cost:** `surreal fix` rewrites on-disk format. Issue #4677:
  *"importing relations with bracket notation fails on 2.0.0-beta.1"* — the
  1.x→2.x fix tool has had data-loss bugs.
  https://github.com/surrealdb/surrealdb/issues/4677
- **OASIS implication:** Pinning 1.5.4 puts OASIS on a version that is
  almost certainly **outside the security-patch window** AND requires a
  format conversion every major step. A pre-launch decision to start on
  1.5.4 means a forced 1→2→3 migration before public launch.
- **Mitigation:** **Start on 2.x at minimum** (probably 2.6.5, the last
  stable 2.x). Re-pin G4 to a current minor. Test `surreal fix` end-to-end
  on a production-shaped snapshot before any wave-2 work.

### 6. `surreal export` is the only backup mechanism, has had multiple file-corruption bugs, and is not snapshot-consistent.

- **Evidence (Issue #6257):** *"CLI surql export produces broken/incorrectly
  escaped SQL … cannot be re-imported"* — hex-id quoting bug. Closed but
  showed the format has rough edges in 2.3.7.
  https://github.com/surrealdb/surrealdb/issues/6257
- **Evidence (Issue #5065):** 3.8 GB export file refused to import — *"The
  protocol or storage engine does not support backups on this architecture"*
  + UTF-8 panic. https://github.com/surrealdb/surrealdb/issues/5065
- **Evidence (Issue #4872):** *"Currently no versioned data is migrated
  during export/import operations with Surreal v.2.0.1 this is a major
  issue."* (Closed via PR #4985, but proves the surface is undertested.)
  https://github.com/surrealdb/surrealdb/issues/4872
- **No PITR. No WAL shipping. No incremental backup.** `surreal export` is
  full-dump-only. There is no published guidance on snapshot isolation
  during export.
- **OASIS implication:** G5 says "scheduled `surreal export` + periodically
  exercised restore drill" — but OASIS has no plan for point-in-time
  recovery, no plan for what happens if a corrupt export silently makes it
  through, and no plan for backup at >5 GB (the published failure point).
- **Mitigation:** Add to G5: (a) export-then-immediately-import-into-a-tmp-db
  validation step after every backup, (b) explicit max-export-size budget
  with alerting before reaching it, (c) cold-snapshot the underlying
  RocksDB/SurrealKV files in parallel as a belt-and-braces backup.

### 7. SurrealKV defaults to **`Eventual` durability** — published, well-known data-loss footgun.

- **Evidence:** Independent blog (widely circulated on Lobsters/HN):
  *"If you are running any SurrealDB instance backed by the RocksDB or
  SurrealKV storage backends you MUST EXPLICITLY set
  `SURREAL_SYNC_DATA=true` … otherwise your instance is NOT crash safe and
  can very easily corrupt."* https://blog.cf8.gg/surrealdbs-ch/
- **Confirmed by upstream README:** `Eventual` mode commits to the kernel
  buffer without fsync. https://github.com/surrealdb/surrealkv
- **OASIS implication:** G1 ALREADY catches this — spec mandates
  `SURREAL_SYNC_DATA=true` + crash-durability CI test. **Good.** The
  remaining risk is that someone forgets to set the env var in a new
  environment (staging, ephemeral test containers).
- **Mitigation:** G1 already correct. Add a runtime self-check: on startup,
  query the server config and **refuse to boot** if SYNC_DATA != true (or
  configured durability != Immediate). One-line guard.

### 8. RELATE graph traversal has documented O(n) failure modes; recursion depth caps at 256 by default with no app-level cycle detection.

- **Evidence (Issue #5615):** Traversal + INSIDE took **80.37 seconds** on
  10,000 records. Closed with labels including `topic:performance` —
  fixed for some shapes, but proves the query planner doesn't reliably use
  indexes on graph paths.
  https://github.com/surrealdb/surrealdb/issues/5615
- **Evidence (Snyk SNYK-RUST-SURREALDBCORE-10079740):** Infinite-loop CVE
  via `DEFINE FUNCTION` with nested FOR loops, fixed in 2.0.5/2.1.5/2.2.2.
  Confirms that the server does **not** universally protect against
  unbounded recursion. https://security.snyk.io/vuln/SNYK-RUST-SURREALDBCORE-10079740
- **Recursion limit is 256** with no signaled-error behavior — query just
  stops. https://surrealdb.com/learn/tour/page-28
- **OASIS implication:** Quest DAG validation (acyclicity, reachability,
  single ExecutionOrder) **cannot** be delegated to RELATE traversal alone.
  A user-input DAG with a 257-hop chain will silently return partial
  results. The "graph remodel payoff" in the spec needs to keep the
  application-level DAG validator, not replace it.
- **Mitigation:** Amend the graph-remodel goal: **RELATE is the storage,
  not the validator.** Keep in-app acyclicity / reachability / topological
  sort. Use RELATE for `->`/`<-` neighbor lookup only, with explicit depth
  bounds in every query.

### 9. SurrealQL parser has had two CVEs around uncontrolled recursion / infinite loops; both require auth, but the API key seam exposes the surface.

- **Evidence:** SNYK-RUST-SURREALDB-6180571 — uncontrolled recursion via
  SurrealQL parser, stack overflow crash.
  https://security.snyk.io/vuln/SNYK-RUST-SURREALDB-6180571 ·
  SNYK-RUST-SURREALDBCORE-10079740 — infinite loop via DEFINE FUNCTION.
- **OASIS implication:** OASIS exposes API-key authenticated endpoints
  that ultimately translate to SurrealQL queries. If any path lets caller
  input reach a query that's parsed (not just parameter-bound), the
  attacker can DoS the whole server. G3 (parameterized queries only)
  protects against injection but NOT against complexity attacks if any
  user-controlled field gets baked into recursive graph traversal depth.
- **Mitigation:** G3 needs a sub-clause: **no user-controlled recursion
  depths.** Hardcode `UNTIL` / depth caps on every RELATE query.

### 10. SurrealDB 3.0 introduced documented performance REGRESSIONS that were not caught before release.

- **Evidence (Issue #6800, OPEN):** *"Simple WHERE: 2000x slower"
  (1ms in 2.4.0 → 2000ms in 3.0)"* — index not used, falls back to full
  table scan. *"Nested vs Top-level gap widening (6x in 2.3.10 → 22x in
  3.0)"*. https://github.com/surrealdb/surrealdb/issues/6800
- **OASIS implication:** The version-pin discipline (G4) protects OASIS
  for now, but the discipline must extend to a documented **upgrade
  protocol** — never accept a major version without re-running the OASIS
  performance suite. SurrealDB does not yet have a stable performance
  contract across versions.
- **Mitigation:** Add to the pre-cutover gate: a benchmark suite checked
  into the repo. Re-run on every SurrealDB upgrade. Fail the gate on >2x
  regression.

---

## "If you only verify ONE thing before wave-2 starts"

**Build a 4-hour crash + restore + saga-resume chaos drill on the chosen
version BEFORE any saga code is rewritten.** Specifically:

1. Stand up the pinned SurrealDB (recommended **2.6.5**, not 1.5.4) with
   `SURREAL_SYNC_DATA=true`.
2. Seed ~10 GB of realistic OASIS data (wallets, bridge tx, holons, quest
   runs with deep RELATE).
3. Run a saga to its midpoint, `kill -9` the SurrealDB process.
4. Restart, verify the orchestration record survived (G1) and saga can
   resume via G7 reconciliation.
5. `surreal export` → wipe → `surreal import` → assert byte-equal restore
   on every value-table (G5).
6. Subscribe a LIVE query, push 10,000 writes from a second connection,
   verify the consumer received all of them in commit order. **If it
   missed any: the wave-2 LIVE-saga rewrite is forbidden.**

This drill exercises footguns #1, #2, #3, #6, #7, #8 simultaneously. Any
one failure invalidates a wave-2 assumption.

---

## Verdict: **PROCEED-WITH-WAVE-2-AMENDMENTS**

The wave-1 plan (persistence seam, schema, integration-test harness rebuild,
crash-durability test) is sound and the right move. The graph remodel and
LIVE-saga rewrite as currently specified in wave-2 are **NOT safe to ship as
written**. Required amendments:

1. **Re-pin G4** to a 2.x line (2.6.5) — 1.5.4 is outside the security-patch
   window and forces a triple-migration before public launch.
2. **Replace `surrealdb-migrations` with SurrealKit in G5** and own the
   data-backfill harness in-tree.
3. **Demote LIVE queries to an opt-in trigger** in `ISagaTrigger` — keep
   polling as production default until the documented single-node + best-
   effort-ordering caveats are gone from upstream docs.
4. **Keep the application-level DAG validator** — RELATE replaces storage
   of edges, not the validator (single-ExecutionOrder, acyclicity,
   reachability all stay in app code).
5. **Add a defensive deserialization layer** around `surrealdb.net`
   `GetValue<T>` — three open bugs (#234, #246, #236) cause silent data
   loss or stack overflow on OASIS-shaped data.
6. **Add an "export-then-reimport-validate" sub-step** to G5; no backup is
   considered taken until it round-trips.

If those six amendments land in the wave-2 spec, this is a defensible
migration. Without them, the team is rebuilding the same exactly-once /
reconciliation work that `api-safety-hardening` just finished — this time
on a less mature substrate with worse tooling, and discovering it under
load.

---

## Sources

- [Odonno/surrealdb-migrations (archived)](https://github.com/Odonno/surrealdb-migrations)
- [surrealdb/surrealkit](https://github.com/surrealdb/surrealkit)
- [LIVE SELECT docs — single-node, best-effort ordering](https://surrealdb.com/docs/surrealql/statements/live)
- [Issue #5070 — LIVE queries don't work cross-node (OPEN)](https://github.com/surrealdb/surrealdb/issues/5070)
- [Issue #5068 — LIVE events hang the whole process](https://github.com/surrealdb/surrealdb/issues/5068)
- [Issue #2748 — Poll-based LIVE queries requested (OPEN)](https://github.com/surrealdb/surrealdb/issues/2748)
- [Issue #5615 — 80s graph traversal](https://github.com/surrealdb/surrealdb/issues/5615)
- [Issue #6800 — 3.0 performance regressions (OPEN)](https://github.com/surrealdb/surrealdb/issues/6800)
- [Issue #5065 — large export file import broken](https://github.com/surrealdb/surrealdb/issues/5065)
- [Issue #6257 — surql export produces broken SQL](https://github.com/surrealdb/surrealdb/issues/6257)
- [Issue #4872 — versioned data lost on export](https://github.com/surrealdb/surrealdb/issues/4872)
- [Issue #4677 — surreal fix data corruption on 1.x→2.x](https://github.com/surrealdb/surrealdb/issues/4677)
- [surrealdb.net Issue #234 — tuple element silently lost (OPEN)](https://github.com/surrealdb/surrealdb.net/issues/234)
- [surrealdb.net Issue #246 — StackOverflow on recursive queries (OPEN)](https://github.com/surrealdb/surrealdb.net/issues/246)
- [surrealdb.net Issue #236 — WebSocket CBOR deserialization broken (OPEN)](https://github.com/surrealdb/surrealdb.net/issues/236)
- [surrealdb.net Issue #185 — SemaphoreSlim bug (OPEN)](https://github.com/surrealdb/surrealdb.net/issues/185)
- [SurrealDB durability blog — SYNC_DATA must be true](https://blog.cf8.gg/surrealdbs-ch/)
- [SurrealKV README — Eventual is default](https://github.com/surrealdb/surrealkv)
- [DEFINE FIELD docs](https://surrealdb.com/docs/surrealql/statements/define/field)
- [SCHEMAFULL CRUD fundamentals](https://surrealdb.com/learn/fundamentals/schemafull/schemafull-crud)
- [Snyk: Infinite loop in surrealdb-core](https://security.snyk.io/vuln/SNYK-RUST-SURREALDBCORE-10079740)
- [Snyk: Uncontrolled Recursion in surrealdb](https://security.snyk.io/vuln/SNYK-RUST-SURREALDB-6180571)
- [SurrealDB Security Policy — patches only on latest minor](https://github.com/surrealdb/surrealdb/blob/main/SECURITY.md)
- [SurrealDB releases](https://surrealdb.com/releases)
