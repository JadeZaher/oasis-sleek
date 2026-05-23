# Analogical Persona — SurrealDB Migration Wave 1

Scope: closest analogs to this *specific* commitment (small pre-launch team, greenfield, graph-native multi-model engine, chain-is-truth audit ledger, MCP/AI surface goal, single-engine bet, fsync-forced durability, parameterized-query Roslyn analyzer). Not "DB migration" generically.

## Analog 1 — FaunaDB customers (single-engine novel-DB bet that ended)
Closest analog. Fauna sold itself as the "operational database for a new generation" — multi-model (relational + document + temporal + graph-ish), proprietary query language (FQL), pre-1.0 SDK ergonomics on several language clients, small team relative to the surface area. Shut down May 30 2025 affecting 195+ databases and 3000+ teams. ([InfoQ](https://www.infoq.com/news/2025/03/fauna-shuts-down/), [HN thread](https://news.ycombinator.com/item?id=43414742))
- What they did differently from OASIS that mattered: Fauna was a *managed-service-only* lock-in. SurrealDB is OSS Business Source — OASIS can self-host the binary forever even if SurrealDB Labs folds. That meaningfully reduces (but does not eliminate) the FaunaDB failure mode.
- Quote: "Open Source solution you can chose to migrate to is what really offers you safety." — Peter Zaitsev on the HN postmortem.

## Analog 2 — RethinkDB users (graph/realtime novel-DB bet, vendor died)
RethinkDB pitched "realtime + push-query" as its differentiator (similar to the SurrealDB LIVE-query story OASIS is betting on for saga triggers). Shut down 2016 after seven years; SageMathCloud rewrote *both* realtime + DB layers onto Postgres after the shutdown. ([RethinkDB shutdown post](https://rethinkdb.com/blog/rethinkdb-shutdown/), [CoCalc rewrite](https://blog.cocalc.com/2017/02/09/rethinkdb-vs-postgres.html))
- What they did differently: SageMathCloud had no seam — RethinkDB's push semantics were embedded everywhere, so the migration was a *rewrite*. OASIS's `architecture-decoupling` seam + `ISagaTrigger` pluggable interface is exactly the absent control they wished they had.
- Lesson: the seam is non-negotiable. OASIS already has it; do not let it erode.

## Analog 3 — Oxide Computer + CockroachDB (single-engine bet, vendor pivoted)
Oxide picked CockroachDB for its 24/7 cloud control plane. When Cockroach Labs went proprietary (Aug 2024), Oxide froze on CRDB 22.1/22.2 and self-maintains a fork rather than migrate. ([RFD 508](https://rfd.shared.oxide.computer/rfd/0508), [Runtime news](https://www.runtime.news/after-cockroach-labs-went-proprietary-one-customer-took-matters-into-its-own-hands/))
- What they did differently: Oxide explicitly *rejected* Postgres because "Postgres seems oriented towards use cases with periods of low activity, not a 24/7/365 duty cycle" (RFD 508). They accepted fork-maintenance cost. OASIS has neither Oxide-scale eng bandwidth to maintain a SurrealDB fork nor a 24/7 hard requirement justifying that risk.
- Most uncomfortable parallel: Oxide also said in RFD 110 that CRDB was the only thing that met their requirements — same conviction OASIS has about SurrealDB. Conviction does not insulate from vendor pivot.

## Analog 4 — Linear (boring-DB greenfield bet that won)
Linear is a small team running a graph-shaped product (issue trees, projects, cycles, sub-issues) entirely on Cloud SQL Postgres, even at multi-region scale. Their answer to graph traversal was *partitioning the issues table into 300 segments* and recursive CTEs — not a graph DB. ([Linear multi-region](https://linear.app/now/how-we-built-multi-region-support-for-linear), [Google Cloud blog](https://cloud.google.com/blog/products/databases/product-workflow-tool-linear-uses-google-cloud-databases))
- What they did differently: chose the boring engine knowing they would hand-write the graph layer. The trade was: more app code, zero DB-vendor risk, every hire already knows it.
- The OASIS counter-claim — "we get RELATE edges and `->`/`<-` traversal for free" — is real but small relative to wallet/bridge/swap tables, which are flatly relational.

## Analog 5 — Wormhole Guardian + Chainlink CCIP (chain-is-truth, DB-is-audit)
The closest *architectural* analog. Wormhole guardians store off-chain attestations/observations in BigTable + Firestore (per the wormhole-dashboard repo), not Postgres, not a novel multi-model engine. Chainlink CCIP DON nodes use OCR consensus + commodity infra; the *chain* is truth and off-chain state is recoverable observation. ([Wormhole docs](https://wormhole.com/docs/protocol/architecture/), [CCIP architecture](https://docs.chain.link/ccip/concepts/architecture/overview))
- What they did differently: both treat off-chain DB as *fungible* — any KV/SQL store will do because chain reconciliation is the truth restoration mechanism. OASIS's G7 (chain reconciliation mandatory) matches this exactly, which is good. But Wormhole and Chainlink *chose boring stores precisely because* they trust reconciliation — they got the freedom OASIS is reaching for via SurrealDB without taking on a novel-engine risk.

## Analog 6 — Roslyn analyzer for SQL injection (CA2100 + SQLInjectionAnalyzer)
Microsoft's CA2100 already does taint-style detection of non-parameterized SQL in C#. Independent custom analyzers (KleinMichalGit/SQLInjectionAnalyzer) exist with multi-scope and sink/cleaning tracking. ([CA2100 docs](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca2100), [SQLInjectionAnalyzer](https://github.com/KleinMichalGit/SQLInjectionAnalyzer))
- What they did differently: CA2100 is general — it doesn't know SurrealQL. A custom SRDB0001 *is* warranted because the sink is `db.Query(string, params)` with SurrealQL semantics CA2100 cannot model. But CA2100's known false-positive and memory issues (issues #4735, #7468) are warnings: keep SRDB0001 *small and sink-list-driven*, not tainted-flow-tracking.

## Pattern: 3 things successful analogs (Linear, Wormhole, CCIP) did that OASIS is NOT doing yet
1. **Picked boring for value paths.** Wormhole/Chainlink/Linear all isolate vendor risk by putting commodity stores under money-touching code. OASIS plans single-engine for both audit ledger *and* graph. The documented fallback (Postgres for audit, Surreal for graph) is the analog-supported posture; commit to it preemptively or keep the seam *exercised* with a second-engine smoke test, not just documented.
2. **Treated "we'll add graph later" as fine.** Linear ships graph-shaped product on Postgres recursive CTEs. OASIS's wallet/bridge/swap tables don't need RELATE; only quest DAG and holon polyhierarchy do. The graph payoff covers a minority of tables.
3. **Bounded SDK/vendor blast radius by *running* the failover drill.** Wormhole's recoverable-from-chain posture is *exercised continuously* in production. G7 reconciliation must be a chaos-tested CI gate, not a runbook bullet.

## Pattern: 3 things failed analogs (Fauna, RethinkDB, Oxide-on-CRDB) did that OASIS IS doing
1. **Bet on a pre-1.0/early-1.x SDK as the lone client.** `surrealdb.net` ~0.10.2, stale ~Apr 2024 — same posture as the abandoned Fauna .NET driver in 2023.
2. **Adopted a novel multi-model engine to avoid composing two boring ones.** Fauna's "one engine for all models" pitch is verbatim the SurrealDB pitch. The composition cost OASIS is avoiding (Postgres + small graph cache) is real but bounded; the single-engine-vendor-pivot cost is unbounded.
3. **Committed before durability story was production-validated.** SurrealDB known-issues page still lists schema/SDK gaps; SurrealKV defaults to `Eventual`. G1 (force `Immediate`) is correct but the burden of proving SurrealKV honors fsync under crash is on OASIS, not on the vendor — same burden that bit early RethinkDB and Fauna users.

## Single most relevant analog
**Oxide Computer + CockroachDB (RFD 508).** URL: <https://rfd.shared.oxide.computer/rfd/0508>. Closest because: small mission-critical team, single-engine conviction documented in a prior RFD ("nothing else meets our requirements"), durability-sensitive workload, ended up frozen on an old version self-maintaining a fork after a vendor pivot they could not control. OASIS has *less* engineering bandwidth than Oxide and *more* exposure (value flows, not internal control plane). Reading RFD 508 alongside RFD 110 is the cheapest pre-mortem available.

## Verdict
**PROCEED-WITH-PATTERNS-ADDED.** The seam, G1/G3/G7, and the documented Postgres-audit fallback are aligned with successful analogs. Two patterns to bolt on before cutover:
- **Pattern A (from Wormhole/CCIP):** make G7 chain-reconciliation a chaos-tested CI gate that kills the DB mid-op every build, not a runbook bullet. This is the actual insurance policy against the Fauna/RethinkDB/Oxide failure mode — if reconciliation is real, the DB becomes fungible.
- **Pattern B (from Oxide RFD 508):** write a one-page "if SurrealDB Labs pivots or the .NET SDK is abandoned, here is the 90-day exit" — name the fallback engine, name the seam boundary, name who owns the fork. Oxide's pain was not having this until the day they needed it.
