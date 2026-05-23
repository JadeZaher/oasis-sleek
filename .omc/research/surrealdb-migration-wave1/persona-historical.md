# Persona: HISTORICAL — SurrealDB Migration (Wave 1)

**Lens:** What does history teach us about single-database-engine commitments
for financial-adjacent / orchestration systems? When have similar bets paid off,
and when have they ended in expensive migrations away?

**Date:** 2026-05-20

---

## TL;DR

History has a clear pattern for single-database-engine bets: they survive when
the engine is *boring and battle-tested* (Postgres, MySQL) or when the chosen
engine has *enough company runway to outlast the migration risk window* (5+
years). They fail when the bet rides on (a) a small vendor with thin runway,
(b) a "one engine for everything" pitch that collides with the workload's
actual physics, or (c) a non-relational engine forced to do relational work.

OASIS's bet is on a small-but-funded UK vendor (~$44M total raised, ~37–45
headcount as of 2026, Series A extension Feb 2026) with a 3.0 GA release and
named enterprise customers (Tencent, NVIDIA, Verizon, Samsung Ads, Saks). That
puts it *meaningfully* better-positioned than the historical cautionary tales,
but the spec's own G4 concern — the `surrealdb.net` SDK pinned at 0.10.2 from
**April 2024** — is the single artefact that most closely resembles the early
warning signs of every database postmortem on this list. The migration spec's
seam + pin + fallback are correctly addressing the real historical risk.

**Verdict: PROCEED-WITH-MITIGATIONS** (all seven guardrails are load-bearing,
not nice-to-have; G4 and the documented Postgres-audit-ledger fallback are the
two most important).

---

## 1. Five historical patterns that bear directly on this decision

### Pattern A — Small-vendor reactive database collapse (RethinkDB, 2017)

**Case:** RethinkDB shipped a beautifully-designed reactive database with live
queries — the *exact* feature OASIS is buying SurrealDB for (LIVE queries
replacing the `ISagaTrigger` poller). It died not because the tech was bad but
because the company couldn't monetize the open-source engine and ran out of
runway after 7 years. The founders' postmortem ("Why we failed") is the most
cited warning in the database-vendor-risk literature.

**OASIS implication:** The structural risk that killed RethinkDB (single
vendor + open-source-only revenue + reactive-query niche) is *attenuated but
not eliminated* for SurrealDB. SurrealDB has commercial Cloud + Enterprise
tiers and just raised $23M in Feb 2026 on top of an existing $21M, so its
runway profile is materially better than RethinkDB's. But the spec's G4 + the
documented Postgres-fallback are the right hedge — RethinkDB users who had a
seam and a fallback survived; those who hand-wrote queries against driver
internals did not.

### Pattern B — Small vendor pulls the rug (FoundationDB, 2015)

**Case:** Apple acquired FoundationDB in March 2015 and immediately pulled
public downloads and open-source repos, leaving production users stranded for
3 years until the 2018 re-open-sourcing. Companies running FDB in production
had no upgrade path, no support, and in some cases no legal redistribution
right to the binaries they already had.

**OASIS implication:** The acquisition-and-shutdown risk is materially lower
for SurrealDB given Series A extension funding and named Tencent/Verizon
deployments, but the *shape* of the risk is identical. The spec's seam
(architecture-decoupling) is exactly what FDB victims wished they had. If a
hostile acquirer (or a pivot-to-AI-only Cloud) happens, OASIS has a one-file
blast radius to the Postgres fallback — that is the correct lesson from FDB.

### Pattern C — Multi-model promise collapses under enterprise gravity (OrientDB → SAP → abandoned, 2017–2021)

**Case:** OrientDB pitched the same "document + graph + key-value in one
engine" story SurrealDB pitches today. CallidusCloud acquired OrientDB in
2017; SAP acquired CallidusCloud in 2018 for $2.4B; SAP officially dropped
commercial support **Sept 1, 2021**. The founder forked it to ArcadeDB. Users
who built on OrientDB's enterprise edition had to migrate or fork. ArangoDB,
the other multi-model contemporary, is still alive (~$58M total funding,
Series B 2021) but its adoption flat-lined and it pivoted hard into AI/GraphRAG
in 2025.

**OASIS implication:** This is the most directly analogous historical case.
The multi-model pitch consistently *underperforms* the marketing because
real workloads are heterogeneous and one engine ends up being mediocre at
all three. SurrealDB's edge over OrientDB is (a) better timing — the AI-agent
memory wave gives it real workloads, not just press releases, and (b) commercial
discipline — Series A extension implies investor confidence, where OrientDB
was already exit-stage by 2017. Still: OrientDB had named enterprise customers
too, right up until it didn't. The fallback in G+1 (Postgres audit ledger,
SurrealDB graph/MCP) is the only mitigation history validates.

### Pattern D — The "use the boring engine, build the smart layer" winners (Uber Schemaless, Stripe, Block)

**Case:** Uber's 2014 architectural inflection point was the choice between a
novel datastore and *MySQL + custom Schemaless layer*. They picked MySQL
explicitly because "stable, maintainable, battle-tested" beat "novel
capability" at production scale. Stripe similarly stayed on Postgres + Mongo
while building all the interesting bits in application code. Block (Square)
runs core ledger on Postgres. The pattern: when the system is financial-
adjacent and the cost of being wrong is high, *the engine is boring and the
cleverness is in the layer above it*.

**OASIS implication:** This is the strongest counter-argument to the
single-SurrealDB bet. The spec's framing — "blockchain = source of truth for
value, SurrealDB = audit/orchestration" — is OASIS's version of the
Schemaless-on-MySQL framing, with SurrealDB playing MySQL's role. The
question is whether SurrealDB is "boring enough" to play that role. In May
2026 the honest answer is: *not yet, but on a credible trajectory*. The 3.0
GA + Tencent/NVIDIA references move it closer to "boring"; the SDK pin
problem (Pattern E) shows it's not there yet.

### Pattern E — The SDK as canary in the coal mine (Diaspora, MongoDB-era postmortems)

**Case:** The recurring micro-pattern in MongoDB postmortems (Diaspora 2013
being the most cited) is not "the database is wrong" — it's "the *client
library / ORM* lagged the engine, and the application paid the integration
debt." Diaspora's actual failure mode was relational data forced through an
immature document model with thin SDK support. The engine kept shipping; the
.NET / Ruby driver did not keep up.

**OASIS implication:** This is the single most-load-bearing historical pattern
for OASIS. The spec already names it (G4): `surrealdb.net` is at 0.10.2,
pre-1.0, and reportedly stale since April 2024 (the spec's claim — two
separate web fetches in 2026 returned conflicting dates of April 2024 vs
April 2026 for v0.10.2, so the cadence claim needs *direct verification by
inspecting commit log on GitHub at decision time, not trusting cached
metadata*). If the .NET SDK is genuinely 25 months stale while the core
engine moved through 3.0 GA, that is the *exact* shape of the Diaspora /
early-MongoDB failure mode and arguably the highest single risk in the spec.

---

## 2. Risk delta: where would this commitment have ended badly vs paid off historically?

**Would have paid off:**
- A team adopting *MySQL/Postgres + custom application layer* in 2014 (Uber).
  OASIS's seam + fallback approximates this defensive posture.
- A team adopting a small-vendor engine *with a seam, a pin, and a documented
  rollback target* (rare but exists — most FDB users who survived had this).
- A team using SurrealDB **only** for graph + MCP context (its genuine
  strength) while keeping value-table state in Postgres. The spec's "fallback
  not chosen" describes this exactly.

**Would have ended badly:**
- A team adopting RethinkDB in 2014 for reactive queries with no abstraction
  layer, no fallback, hand-written driver code. (3 years later: complete
  rewrite required.)
- A team adopting OrientDB in 2017 because the multi-model pitch matched
  their architecture diagram. (By 2021: rewrite or fork.)
- A team that trusted the SDK cadence and built application code directly
  against driver internals rather than the abstraction seam. (Diaspora 2013;
  many MongoDB 2.x users 2014.)

The single biggest historical predictor of "ended badly" is *not* the engine —
it's whether the team had a seam, a pin, and a documented fallback. The OASIS
spec has all three. That is the strongest historical signal in its favor.

---

## 3. The "if you read one postmortem before locking wave-2" recommendation

**Read this:** "RethinkDB: why we failed" — Slava Akhmechet, 2017.
<https://news.ycombinator.com/item?id=13421608> (HN thread with founder
participation) and Gil Tayar's summary
<https://medium.com/@giltayar/summarizing-rethinkdb-why-we-failed-7eeff6cc7107>.

**Why this one and not the others:** SurrealDB's pitch (reactive LIVE
queries + multi-model + small UK company) is structurally closer to RethinkDB's
than to any other dead database. The RethinkDB postmortem is the canonical
analysis of *why a beautifully-engineered small-vendor reactive database can
fail to monetize even when the tech works*, which is the precise risk OASIS
is taking. Pair it with the OrientDB / SAP support-drop GitHub issue
<https://github.com/orientechnologies/orientdb/issues/9734> for the
multi-model-promise side of the same coin.

---

## 4. Verdict

**PROCEED-WITH-MITIGATIONS**

The spec's guardrails (G1–G7) and the documented Postgres-audit-ledger
fallback are not optional polish — they are the *exact* mitigations history
identifies as the difference between "survived" and "rewrote everything" when
similar bets went wrong. Specifically:

1. **G4 (SDK pin + seam) is the most load-bearing guardrail.** Before
   committing to wave-2, verify directly from the `surrealdb.net` GitHub
   commit log (not cached metadata) whether the SDK has shipped any meaningful
   commits in the last 6 months. If it genuinely has been silent since April
   2024 while the engine shipped 3.0 GA, the SDK pin is necessary but
   insufficient — request a Tier-1 maintenance commitment from SurrealDB
   Labs sales/DevRel before locking in.

2. **The "blockchain = truth for value, SurrealDB = audit/orchestration"
   framing only reduces migration pain if the audit ledger is genuinely
   reconstructible from chain confirmations** (G7). It does *not* if regulators
   ever ask "show me your immutable history of who clicked what when" — at
   that point the SurrealDB store *is* the source of truth and as load-bearing
   as a financial ledger. G7 (chain reconciliation mandatory) is therefore
   the second-most-load-bearing guardrail.

3. **The documented Postgres fallback is the single best risk-management
   decision in the spec.** History does not record many cases of teams that
   adopted a novel database, hit problems, and *had a pre-architected fallback
   sitting on the shelf*. Most ended up doing 6–18 month panic rewrites.
   Do not let the fallback rot — keep the seam exercised (even just one
   in-memory provider behind it in CI) so the Postgres path can be reconstructed
   in days, not months, if needed.

The 4–5 week effort estimate is realistic for a greenfield team with no live
data; the historical pattern is that teams with live data who try to do this
in 4–5 weeks end up in the cautionary tales. OASIS's greenfield status is
genuinely defensive here.

---

## Sources

- [SurrealDB Raises $23M in Additional Series A Funding (Feb 2026) — FinSMEs](https://www.finsmes.com/2026/02/surrealdb-raises-23m-in-additional-series-a-funding.html)
- [SurrealDB raises $23M, launches update to fuel agentic AI — TechTarget](https://www.techtarget.com/searchdatamanagement/news/366639042/SurrealDB-raises-23M-launches-update-to-fuel-agentic-AI)
- [SurrealDB 2026 Company Profile — Tracxn](https://tracxn.com/d/companies/surrealdb/__BT2Y89TLe1oD9nDEcJ1L4Z5kn5B5oQCUXTPFJ5kJTz8)
- [SurrealDB Case Studies (Saks, Tencent, Verizon, Samsung, Permit.io)](https://surrealdb.com/casestudies)
- [Introducing SurrealDB 3.0 — SurrealDB Blog](https://surrealdb.com/blog/introducing-surrealdb-3-0--the-future-of-ai-agent-memory)
- [surrealdb.net GitHub Releases (v0.10.2 cadence)](https://github.com/surrealdb/surrealdb.net/releases)
- [NuGet Gallery — SurrealDb.Net package](https://www.nuget.org/packages/SurrealDb.Net)
- [RethinkDB is shutting down (2016)](https://rethinkdb.com/blog/rethinkdb-shutdown/)
- [RethinkDB: why we failed — Hacker News thread (2017)](https://news.ycombinator.com/item?id=13421608)
- [Summarizing "RethinkDB: why we failed" — Gil Tayar, Medium](https://medium.com/@giltayar/summarizing-rethinkdb-why-we-failed-7eeff6cc7107)
- [What Happened to RethinkDB — Failory](https://www.failory.com/cemetery/rethinkdb)
- [Apple Acquires FoundationDB — TechCrunch (Mar 2015)](https://techcrunch.com/2015/03/24/apple-acquires-durable-database-company-foundationdb/)
- [FoundationDB — Wikipedia (acquisition + 2018 re-open-sourcing)](https://en.wikipedia.org/wiki/FoundationDB)
- [SAP dropped support for OrientDB on Sept 1 2021 — GitHub Issue #9734](https://github.com/orientechnologies/orientdb/issues/9734)
- [OrientDB — Wikipedia (CallidusCloud → SAP → ArcadeDB fork)](https://en.wikipedia.org/wiki/OrientDB)
- [ArangoDB Company Profile (2026, $58.6M total funding) — Tracxn](https://tracxn.com/d/companies/arangodb/__eycDXd5JCO4Qxm7PAvH4y36r8nD4kGwwDssuRZctX7I)
- [Designing Schemaless, Uber Engineering's Scalable Datastore Using MySQL — Uber Blog](https://www.uber.com/en-IN/blog/schemaless-part-one-mysql-datastore/)
- [The Architecture of Schemaless — Uber Blog (Part 2)](https://www.uber.com/us/en/blog/schemaless-part-two-architecture/)
- [Why You Should Never Use MongoDB — Diaspora postmortem context (Hacker News 2013)](https://news.ycombinator.com/item?id=6712703)
- [re: Why You Should Never Use MongoDB — Ayende @ Rahien (modeling-not-engine analysis)](https://ayende.com/blog/164483/re-why-you-should-never-use-mongodb)
- [A shared database is still an anti-pattern — Ben Morris](https://www.ben-morris.com/a-shared-database-is-still-an-anti-pattern-no-matter-what-the-justification/)
- [Why FinTech Is Moving to Distributed SQL Databases — CockroachLabs](https://www.cockroachlabs.com/blog/fintech-distributed-sql/)
