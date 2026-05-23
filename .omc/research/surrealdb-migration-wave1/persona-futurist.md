# FUTURIST — Projecting OASIS SurrealDB Migration to 2028

**Vantage:** 2026-05. Wave-1 locks the constraints that govern through 2028+.

---

## The most likely 2028 state

By Q2-2028, SurrealDB is most likely in **State B-leaning-C**: shipped 2.x.x
through 2027, version 3.0 promised but slipping, the company has pivoted hard
to **SurrealDB Cloud** (hosted) because OSS monetization failed, and the
self-hosted server gets quarterly rather than monthly releases. `surrealdb.net`
is still pre-1.0 (~0.14.x) and lags the server by 6–9 months. The OASIS team
is on .NET 10 LTS (shipped Nov-2025, supported through Nov-2028), considering
the .NET 12 LTS jump for Nov-2027. The chain layer (Wormhole, Algorand,
Solana) has had two named MEV/bridge incidents that made the **audit DB more
load-bearing than the 2026 spec assumed** — G7 reconciliation is firing
weekly, not monthly. MCP is still alive but is one of 2–3 standards (a
"MCP-2" or Google-led variant exists); Postgres ships a first-party MCP
server, eroding the "MCP-native" differentiator. The team is 8–12 engineers,
4 of whom were hired in 2027 and have never written SurrealQL before joining.

**Probability sketch on SurrealDB futures (May-2026 calibration):**
A (mature 3.x, F500): 15% · **B (stagnant 2.x, cloud pivot): 45%** ·
C (acquired, freeze): 25% · D (shutdown, fork): 15%.
**Leading indicator that distinguishes them:** the gap between server release
cadence and `surrealdb.net` SDK release cadence. If by Q4-2026 the SDK is
still pinned <1.0 and >2 server minors behind, you are in B/C. If 1.0 GA
ships with same-week SDK parity, you are in A.

---

## Three highest-likelihood failure modes

**FM-1 — The SDK pin becomes a runtime incompatibility (probability ~55%, hits 2027).**
`surrealdb.net 0.10.2` predates .NET 9 by 6 months. .NET 10 (Nov-2025) is
fine; .NET 11 (Nov-2026) introduces analyzer/source-gen breakage that the
abandoned 0.10.2 doesn't compile against. The team is forced to either
unpin (blowing past G4) or fork the SDK. **2026–2027 warning sign:** any
`<NoWarn>` or `<TargetFramework>net8.0</TargetFramework>` carve-out added
to the SurrealDB adapter project while the rest of the solution moves to
net10. The `Microsoft.CodeAnalysis.Testing.Verifiers.XUnit 1.1.2` downgrade
already in `build-fix` is the canary — the same pressure will hit
surrealdb.net first.

**FM-2 — Cloud pivot strands the self-hosted deploy (probability ~40%, hits late-2027).**
SurrealDB Labs announces Cloud-first roadmap; G1 (`SURREAL_SYNC_DATA=true`
durability), G5 (`surreal export`/restore) and the schema-migration tool
(`surrealdb-migrations`/`surrealkit`) are deprioritized for self-hosters.
The team's CI durability test starts failing on point releases. **2026–2027
warning sign:** the vendor stops attending self-hosted issues on GitHub, or
the docs site reorganizes around Cloud. By the time the runbook bullet
"periodically-exercised restore drill" turns red in CI, you have 6 months,
not 18.

**FM-3 — Graph remodel becomes the lock-in (probability ~50%, hits any time).**
Tasks 9–10 (RELATE edges for quest DAG + holon polyhierarchy) are the
SurrealDB payoff and *also* the thing that makes the fallback ("Postgres
audit ledger, SurrealDB graph") undo-able. By 2028, quest/holon graph code
is 3–8k lines of SurrealQL traversal (`->`, `<-`, `..`) with no clean
equivalent in Postgres+`pgRouting` or Neo4j-Cypher. The "documented
fallback" in spec line 93–95 becomes mythical. **2026–2027 warning sign:**
ratio of SurrealQL-specific traversal code to portable repository-pattern
code crosses 20%, or any quest/holon query that can only be expressed as
multi-hop SurrealQL recursion lands in a hot path.

---

## Three things to LOCK IN wave-1/wave-2 (2028 dev will thank you)

1. **A standing "30-day Postgres bring-up" CI job.** Not a fallback
   implementation — a *contract test* that asserts every value-table
   interface (`IWalletStore`/`IBridgeStore`/`INftStore`/operation-log) has a
   stub Postgres adapter that compiles and passes the same per-aggregate
   contract tests as the SurrealDB adapter. Wallet/bridge/swap/NFT are
   value-flow; they MUST stay portable. Quest/holon graph can be Surreal-only.
   This is the difference between FM-3 being a 6-month migration and an
   18-month one.

2. **SurrealQL-as-strings in exactly one file per aggregate, behind typed
   methods.** G3 says "parameterized queries only" but the 2028 reviewer
   wants stronger: every `db.Query(sql, params)` call lives in
   `*SurrealStore.cs` files, fingerprinted, and a CI gate counts them.
   Drift > 10% per quarter triggers review. When you migrate or fork, you
   need to find every query in under an hour, not a week.

3. **Pin the schema-migration tool version AND fork it into-tree at wave-2.**
   `surrealdb-migrations` / `surrealkit` is a small Rust binary maintained
   by 1–2 people. Vendor it, build from source in CI, commit the binary
   hash. When the vendor goes dark in 2027, your schema pipeline survives.
   Cost: 2 days. Value in 2028: the difference between "migration runs" and
   "migration is a P0 incident."

---

## Three things to AVOID LOCKING IN wave-1/wave-2

1. **Don't make SurrealDB LIVE queries the only saga trigger.** Task 8a
   replaces polling `ISagaTrigger` with LIVE queries. Keep the polling
   `ISagaTrigger` implementation alive and unit-tested, not deleted. If
   SurrealDB LIVE queries develop a memory leak under load (a known
   2024–2025 issue class), you flip back in a config toggle, not a sprint.

2. **Don't bake SurrealDB record-IDs (`table:ulid`) into external API
   contracts.** Internal: fine. Public JSON-RPC/REST responses returning
   `wallet:xyz123` strings to the SDK / wallet client = irreversible lock-in.
   Use opaque UUIDs at the API boundary; the SDK in 2028 must not need to
   know the storage engine.

3. **Don't let MCP-context tables share storage decisions with value
   tables.** The spec rightly says MCP-context is schemaless. Keep it in a
   *separately-namespaced* SurrealDB database/namespace so that if 2027
   brings a better MCP backend (vector DB, native LLM-context store, or a
   competing protocol), you peel off MCP without touching wallet/bridge.

---

## The 2028 incident report (most likely Y-Z-W)

> "Q3-2028: 14-hour outage during Algorand chain reorg. Root cause:
> `surrealdb.net 0.10.2` (pinned 2024-04, never bumped) leaked websocket
> connections under reconnection storm, exhausted FDs. Compounded by:
> schema-migration tool `surrealdb-migrations` unmaintained since 2027,
> blocking the hotfix DDL we needed to add an index. We should have, in
> 2026, kept Postgres-shaped contract tests for value tables and vendored
> the migration tool."

---

## 2028 architectural-review one-liner

**"Graph remodel was worth it. Pinning a pre-1.0 SDK behind a one-engineer
seam was not — we paid for it in Q3-2028 and again when we needed to
acqui-hire."**

---

## Verdict

**PROCEED-WITH-EXIT-RAMP-READY.**

The graph payoff is real and the seam design is sound, but the SurrealDB
vendor-risk distribution (45% State B + 25% State C + 15% State D = 85%
chance of a sub-optimal vendor by 2028) requires wave-1 to fund the three
LOCK-IN items above as first-class scope, not "we'll get to it." The
documented fallback (spec line 93–95) is only real if value-table contract
tests against a Postgres stub are running in CI from week 1. Without that,
the fallback is a sentence in a markdown file — and the 2028 developer
will curse the 2026 team for it.
