# Contrarian — The Case AGAINST the SurrealDB Migration

**Persona:** Asymmetric Research Squad — Contrarian
**Date:** 2026-05-20
**Verdict (preview):** **RECONSIDER-FUNDAMENTALLY** — proceed only after a one-week Postgres-graph spike that the spec currently skips.

> Methodological note: the user explicitly asked the contrarian to do its hardest work and to be talked OUT of this decision if it's defensible. I am steel-manning Postgres on purpose; I am NOT pretending the seam, G1–G7, or the team's own analysis don't exist. I'm arguing they're solving a problem the project does not yet have, using a tool that has not yet earned the trust the spec extends to it.

---

## 1. The 5 strongest arguments against migration

### Argument 1 — The seam EXISTS, so the engine choice is now an OPTION, not an OBLIGATION
The whole `architecture-decoupling` Tier 1 was justified as "this makes the SurrealDB migration a contained change rather than a rewrite." Granted. **But once the seam exists, it ALSO makes Postgres-the-status-quo a contained, defensible choice** — and one with vastly more operational maturity. The seam should be reframed not as "the runway to SurrealDB" but as "the optionality to evaluate engines on evidence." The spec treats the seam as a one-way ratchet toward SurrealDB; that is a non-sequitur. Per-aggregate interfaces work for `EfStorageProvider` exactly as well as for a `SurrealStorageProvider`.

**Counterfactual — if you stay on Postgres:** you skip ~4–5 weeks of engine swap, you keep `pg_dump`/PITR/logical replication/`pg_stat_statements`/connection pooling/RLS/EF Core observability, and you can spend that same 4–5 weeks on `mcp-surface` (the actual product differentiator) OR shipping wallet UX. Critically, you also keep the entire 537-test integration suite running on `Testcontainers.PostgreSql` with a one-week fix to the destructive-teardown issue, instead of a ground-up harness rebuild against a database whose .NET test-container ergonomics are unproven at this scale.

### Argument 2 — `surrealdb.net` is a pre-1.0 SDK from a small vendor and the spec under-prices that risk
The spec acknowledges G4 (pin the version, one-file blast radius) but treats it as a containment problem. It is also a **trajectory problem**. The .NET SDK has been at 0.10.x since April 2024; as of mid-2026 it has STILL not shipped 1.0, while the server has shipped 2.x and 3.x. That gap is not narrowing. Live-query bugs (GitHub issues [#5068 hang/lockup](https://github.com/surrealdb/surrealdb/issues/5068), [#5014 empty events](https://github.com/surrealdb/surrealdb/issues/5014), [#5160 "CRITICAL: Live Queries stop working in new version"](https://github.com/surrealdb/surrealdb/issues/5160), [#3602 / #4026 parameters don't work in LIVE WHERE](https://github.com/surrealdb/surrealdb/issues/3602)) directly touch the LIVE-query saga trigger the migration spec promises in "Carried over." That is not a peripheral feature; it is the load-bearing replacement for the polling `ISagaTrigger`. The seam contains the SDK but **does not contain the wire-protocol breakage** when SurrealDB 4.x requires a feature the 0.x .NET SDK never gets, or when the maintainer pool (one or two people) becomes unavailable.

**Counterfactual — if you stay on Postgres:** Npgsql 8.x is maintained by a large community, ships in lockstep with PG releases, and you have a written contract with PostgreSQL's wire-protocol stability that is older than most of the engineering team. The risk surface "SDK vendor lags server" is structurally absent.

### Argument 3 — The "no stored balance, therefore not a financial ledger" framing is rhetorical, not architectural
The spec leans heavily on: chain = source of truth → store is "audit/orchestration" → SurrealDB's durability story is acceptable. **This collapses on inspection.** The store holds:

- **idempotency keys** for irreversible chain ops (G2),
- the **consumed-VAA ledger** for the bridge,
- **bridge state machine** transitions,
- the **operation log** the reconciliation path (G7) reads to decide whether to re-attempt a chain action.

If SurrealKV loses a "VAA consumed" write under the documented fsync-edge-case + crash window ([cf8.gg post on SurrealDB sacrificing durability for benchmarks](https://blog.cf8.gg/surrealdbs-ch/)) the Wormhole VAA gets replayed and **real value moves twice**. The integrity requirement on those rows is not "best-effort audit"; it is **exactly-once, durable, totally-ordered, financial-grade**. Calling that "not a financial ledger" is wordplay. G1 (force `SURREAL_SYNC_DATA=true`) helps but is a global knob — a single dev or ops misconfiguration silently re-enables eventual durability for the entire process. Postgres's `synchronous_commit=on` is the same knob, except (a) it is the default; (b) thousands of DBAs and millions of deployments have stress-tested the failure modes; (c) Postgres replication+PITR give you a recovery floor SurrealKV does not yet have.

**Counterfactual — if you stay on Postgres:** the consumed-VAA ledger is a unique constraint on `(emitter_chain, emitter_address, sequence)` inside a single multi-statement transaction with `synchronous_commit=on`. The replay safety property reduces to "the database is correct" — a property no one questions for Postgres.

### Argument 4 — The "documented fallback to Postgres" is comforting fiction
The spec section "Documented fallback (not chosen)" reads: *"If G1/G2/G7 fail load/chaos testing: Postgres for the audit ledger only, SurrealDB for graph/MCP. The seam makes this contained."* This is the **classic "we'll switch back" promise that always becomes "we're stuck."** By the time wave-2 + wave-3 land:

- Quest DAG is modeled in `RELATE` edges and `->`/`<-` traversals (SurrealQL-specific syntax),
- Saga trigger reads SurrealDB LIVE queries (vendor-specific change feed),
- Idempotency/state-transition guards use SurrealDB's single-statement `UPDATE…WHERE` semantics,
- Backup uses `surreal export` and a custom restore drill,
- Integration test harness is rebuilt against a SurrealDB testcontainer,
- 10k+ lines of adapter code, migrations, schema declarations are SurrealDB-flavored.

The "contained swap" promise assumes the seam stays narrow. It will NOT stay narrow once the team optimizes for graph traversal performance — they will reach through it. **Every successful "we built an abstraction so we can swap" decision in software history was built when the abstraction was still naive enough to be honest.** Once the team has spent two months making SurrealDB fast, the seam will leak its assumptions everywhere.

**Counterfactual — if you stay on Postgres:** there is no fallback to plan, no chaos test to wait on, no swap path to leave open.

### Argument 5 — MCP justification is post-hoc; "graph-native" is unproven for THIS workload
The data-engine-decision memory and the spec both cite the MCP surface as a load-bearing reason. But `mcp-surface` is **wave-3, gated on this migration finishing**. Sequencing matters: **you cannot use a benefit that is downstream of the cost to justify paying the cost.** The honest version is: "we predict MCP+graph will be valuable, and we are betting 4–5 weeks of engine swap on that prediction before we have a single AI-workflow customer." A defensible variant builds MCP-over-Postgres first (Postgres has Cypher via Apache AGE and several MCP server reference implementations already), validates demand, and only THEN migrates if Postgres is the bottleneck.

The "graph-native is 10x DX win" steelman: quest DAG traversal in SurrealQL (`SELECT ->depends_on->QuestNode FROM $start`) is genuinely more readable than the equivalent recursive CTE, and `RELATE` edges remove the join boilerplate the holon polyhierarchy currently has. **Counter-steelman:** the quest DAG is **bounded, small, and validated** (`QuestDagValidator` enforces acyclicity and reachability ahead of time). A bounded DAG is the workload Postgres recursive CTEs and `ltree` were designed for — and you only ever traverse it during quest execution, not at hot path scale. The "10x DX win" is more likely a 1.5x readability win on a few dozen query sites, paid for with a 10x increase in operational unfamiliarity. Apache AGE in Postgres ([apache/age](https://github.com/apache/age)) gives you OpenCypher inside the same connection pool, with the same transactions, monitored by the same tools, backed up by the same `pg_dump`, replicated by the same logical-replication slot — for zero new operational surface.

**Counterfactual — if you stay on Postgres:** the graph workload is served by Apache AGE (OpenCypher) or `ltree` (DAG traversal) inside the existing connection and transaction; the MCP server is built directly on the existing schema; SurrealDB becomes a thing to reconsider in v2 of the product, after real usage data exists.

---

## 2. The cheapest test that would settle this in a week

**A one-week Postgres-graph spike** — currently absent from `surrealdb-migration/plan.md` — that would either kill or strengthen the contrarian case:

1. **Day 1–2:** Implement quest-DAG traversal on Postgres using BOTH (a) `ltree` materialized paths and (b) Apache AGE Cypher. Implement the same traversal in SurrealDB using `RELATE` edges. Use a synthetic DAG of realistic size (≤ 5k nodes, ≤ 50k edges — generous upper bound for a quest catalog).
2. **Day 3:** Implement the consumed-VAA ledger + idempotency-key store on Postgres (unique constraint + advisory lock) and on SurrealDB (G2 single-field conditional update). Run a duplicate-VAA chaos test against both: 1000 concurrent attempts to redeem the same VAA. Measure: exactly-one chain effect? p99 latency? failure modes under `kill -9` mid-write?
3. **Day 4:** Implement a saga trigger on Postgres `LISTEN/NOTIFY` (or `pg_cron` + `SKIP LOCKED` for due-step polling) and on SurrealDB LIVE query. Measure: notification latency, missed-event rate under restart, behavior under [issue #5068](https://github.com/surrealdb/surrealdb/issues/5068) reproduction attempt.
4. **Day 5:** Score on three axes — **lines of adapter code, p99 latency at expected scale, recovery behavior after `kill -9`** — and decide.

**Cost:** one engineer-week. **Information gained:** dispositive on the central claim of the migration (graph + LIVE-query + idempotency are easier/better in SurrealDB). The 4–5 week migration without this spike is a bet placed without seeing the cards.

---

## 3. What it would take to change MY mind

I will admit the migration is right if wave-2 (or this one-week spike) produces ALL of:

1. **Apache AGE / `ltree` on Postgres requires materially more code** than `RELATE`/`->` to express the holon polyhierarchy and quest DAG traversal — measured as adapter LOC, not aesthetic preference.
2. **A duplicate-VAA chaos test (1000 concurrent, mid-test `kill -9`) on SurrealDB with G1+G2 enforced** produces exactly-one chain effect with the same reliability as Postgres with `synchronous_commit=on` + unique constraint. Specifically: zero double-spends across 100 trial runs.
3. **A LIVE-query saga trigger does NOT exhibit issues #5068 / #5014 / #5160 / #3602** under a 24-hour soak test with 100 concurrent subscriptions and 10 events/sec. If it does, the polling `ISagaTrigger` must remain — at which point one of the headline benefits of the migration evaporates.
4. **The .NET SDK has shipped 1.0** (or a hard commitment with a date), so the "pre-1.0, single vendor" risk reduces to ordinary vendor risk.
5. **The `surreal export` → wipe → import drill round-trips ALL versioned-data tables** including the consumed-VAA ledger, with byte-for-byte equivalence on critical fields. The spec notes this needs "confirm versioned-data export behavior on the chosen version" — that confirmation must come back positive.

Any ONE of these failing should trigger the documented fallback. The contrarian position is that the project is not currently set up to detect these failures honestly: the team will be 6 weeks into the migration before the gates are exercised, and sunk cost will color the read.

---

## 4. Verdict

**RECONSIDER-FUNDAMENTALLY.**

Not HALT. The seam is good work and should ship. The api-safety-hardening guardrails are independently correct. But the leap from "we have a seam" to "we migrate to SurrealDB" should be gated on the **one-week Postgres-graph spike** above, executed BEFORE writing a single SurrealQL adapter. The current plan's "documented fallback to Postgres" is a politeness, not a real option, because it requires noticing the failure AFTER 4–5 weeks of investment. The contrarian remedy is cheap: front-load a one-week bake-off and let evidence (not architectural sympathy for graph databases) make the call.

If the spike confirms the migration thesis: **PROCEED** with high confidence, and the 4–5 weeks are well spent because you now have data. If the spike disconfirms it: **STAY ON POSTGRES**, add Apache AGE for graph traversal, build `mcp-surface` over Postgres, save 4–5 weeks, and revisit SurrealDB in 12 months when (a) the .NET SDK is 1.0+, (b) there is a real AI-workflow customer demanding it, and (c) Postgres is the demonstrated bottleneck — none of which is currently true.

The strongest argument the user could give me to override this verdict is: *"We've already done the bake-off informally and we know the answer."* If true, document the bake-off results in the spec. If not done, do it before paying for the migration.

---

## Sources

- [SurrealDB is sacrificing data durability to make benchmarks look better (blog.cf8.gg)](https://blog.cf8.gg/surrealdbs-ch/) — original durability critique
- [SurrealKV README — durability modes](https://github.com/surrealdb/surrealkv/blob/main/README.md) — Eventual vs Immediate
- [SurrealKV limitations — issue #7](https://github.com/surrealdb/surrealkv/issues/7)
- [SurrealDB issue #5068 — Live query events hanging/locking up](https://github.com/surrealdb/surrealdb/issues/5068)
- [SurrealDB issue #5160 — CRITICAL! Live Queries stop working in new version](https://github.com/surrealdb/surrealdb/issues/5160)
- [SurrealDB issue #5014 — Live Queries events are empty](https://github.com/surrealdb/surrealdb/issues/5014)
- [SurrealDB issue #3602 — parameters do not work with live query](https://github.com/surrealdb/surrealdb/issues/3602)
- [SurrealDB issue #4026 — Live Query WHERE clause should process Params](https://github.com/surrealdb/surrealdb/issues/4026)
- [SurrealDB issue #4872 — SurrealKV export missing versions](https://github.com/surrealdb/surrealdb/issues/4872)
- [SurrealDB .NET SDK on NuGet (still 0.10.x)](https://www.nuget.org/packages/SurrealDb.Net)
- [SurrealDB .NET SDK releases](https://github.com/surrealdb/surrealdb.net/releases)
- [Apache AGE — OpenCypher inside Postgres](https://github.com/apache/age)
- [KuzuDB — embedded graph alternative](https://github.com/kuzudb/kuzu) (note: no first-party .NET binding as of 2026-05)
- [SurrealDB Series A extension — $23M, Feb 2026](https://thetechfounders.co.uk/news/surrealdb-raises-23m-in-series-a-funding/) — vendor IS funded (counter-evidence to vendor-risk argument)
- [SurrealDB at $5M ARR with 45 employees (getlatka)](https://getlatka.com/companies/surrealdb.com) — small but viable
