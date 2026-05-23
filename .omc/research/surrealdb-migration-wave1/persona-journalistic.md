# Persona: JOURNALISTIC — Independent Investigation

Squad: Asymmetric Research, OASIS SurrealDB migration (wave-1).
Brief: "Talk to actual SurrealDB users at scale. Is there ANY production deployment story for a financial-adjacent system on SurrealKV? Is the .NET SDK actually viable?"

---

## 1. Production deployment evidence

**Named customers (per SurrealDB marketing) — and what the marketing actually says vs. what the page says:**

- **Saks Fifth Avenue** — "powers Saks Fifth Avenue's AI-driven product recommendation engine with **1.5 million weekly recommendations** … on a **single SurrealDB node**" ([surrealdb.com/customer/saks](https://surrealdb.com/customer/saks), via [surrealdb.com/casestudies](https://surrealdb.com/casestudies)). Read journalistically: ~2.5 requests/sec sustained against an embedded vector model. This is a recommendations read workload — not durability-critical, not financial, no QPS or fsync guarantees claimed. The "single node" framing is a feature, not a stress test.
- **Permit.io** — "tens of millions to hundreds of millions of identity relationships … resolving permissions … in <1ms" ([surrealdb.com/customer/permitio](https://surrealdb.com/customer/permitio)). No storage backend, version, durability config, or write-throughput disclosed. Authorisation reads (cacheable, replay-safe) — opposite of an irreversible-write workload.
- **Verizon, Walmart, Samsung, Nvidia, Tencent, ING** — name-drops in the Series A press cycle ([TechTarget 2026-02-17](https://www.techtarget.com/searchdatamanagement/news/366639042/SurrealDB-raises-23M-launches-update-to-fuel-agentic-AI)). No published case studies for any of these. ING is the only financial-services name; there is no public technical artefact showing it in a money path.

**The GH discussion specifically asking "Who's using surrealdb and why?" ([github.com/orgs/surrealdb/discussions/61](https://github.com/orgs/surrealdb/discussions/61))** returned only prototypers ("My plan is to create a prototype"; "I plan to use SurrealDB in the next projects"). The COO replied "We ourselves have been using SurrealDB … for the last 4 years" — internal use, not a customer attestation.

**Financial-adjacent verdict:** I could find **zero** named production deployments of SurrealDB (any backend, any version) for: idempotency/replay on irreversible ops, cross-chain bridge state, custodial wallet metadata, or any audit-grade ledger. The closest analog SurrealDB itself markets is the [Finance and FinTech solutions page](https://surrealdb.com/solutions/finance-and-fintech) — which is aspirational marketing copy, not a customer list.

---

## 2. `surrealdb.net` SDK viability

Hard numbers from NuGet ([nuget.org/packages/SurrealDb.Net](https://www.nuget.org/packages/SurrealDb.Net)) and GitHub ([github.com/surrealdb/surrealdb.net](https://github.com/surrealdb/surrealdb.net)):

| Metric | Value |
|---|---|
| Current version | **0.10.2** (still 0.x, no GA semver promise) |
| Released | **2026-04-24** (12 months after 0.9.0) |
| Total downloads | 66.6K all-time |
| Stars / open issues | 133 / 31 |
| Maintainer | SurrealDB Labs org (official) |
| Release cadence | 12 releases since Sep 2023, every 2–6 months |

**Correction to the spec.** `spec.md` G4 calls the SDK "pre-1.0 (~0.10.2, stale ~Apr 2024)." NuGet's version history shows 0.10.2 shipped **2026-04-24**, not 2024 — it's current within ~1 month of writing, not 2 years stale. The same correction applies to the in-repo `docker-compose.surrealdb.yml` comment ("SDK 0.10.2 (Apr 2024)"). The "still 0.x after ~2.5 years of development" critique is the right one — staleness is not.

**Compared to peers** (all official, all SurrealDB Labs):
- TypeScript `surrealdb` ([npmjs.com/package/surrealdb](https://www.npmjs.com/package/surrealdb)) — at **2.0.3 GA**, 59 dependent packages.
- Rust `surrealdb` ([crates.io/crates/surrealdb](https://crates.io/crates/surrealdb)) — past 1.0, monthly stable cadence ("starting with v1.1.0, stable releases are published on the second Tuesday of every month" — [SurrealDB blog](https://surrealdb.com/blog/introducing-nightly-and-beta-rust-crates)).
- .NET is the **only first-party SDK still in 0.x while the server is at 3.0**.

**SDK ↔ server compatibility lag (load-bearing):** the in-repo learnings note "SurrealDb.Net 0.10.2 … targets SurrealDB server 1.x protocol", and the compose file pins `surrealdb/surrealdb:v1.5.4`. Meanwhile mainline SurrealDB is **3.0 GA** (Feb 2026, [SurrealDB blog](https://surrealdb.com/blog/introducing-surrealdb-3-0--the-future-of-ai-agent-memory)). Wave-1 is committing OASIS to a **two-major-version-behind server** because the .NET SDK can't talk to anything newer. Issues #5001 and #5199 below were filed against 2.x — the 1.x line will receive only essential patches.

**Open-issue health:** 31 open including recent unfixed bugs — #246 (May 2026) "Stackoverflow exception with recursive queries", #236 (Apr 2026) "Deserialization bug on the WebSocket query/result". 60 days between major issue and resolution is not abnormal for this SDK.

**Honest verdict on `surrealdb.net`:** **viable with caveats**, sliding toward "use at your own risk" for value-path code. Official, actively maintained, but: still 0.x; lags server major versions; forces the deployment to a 2-major-versions-behind server; SDK API breakage is normal in 0.x and the spec's `[0.10.2]` pin + MSBuild guard is the right mitigation. **G4's exact-pin requirement is necessary, not paranoid.**

---

## 3. SurrealKV durability — and what the wave-1 compose actually does

**Documented behavior (1.x):** per [docs/.../surrealkv](https://surrealdb.com/docs/surrealdb/installation/running/surrealkv), durability is configured via a **URI query parameter** on the connection string, not an env var:

> `surreal start --user root --pass secret "surrealkv://path/to/db?versioned=true&sync=every&retention=30d"` — and `sync` accepts `never` | `every` | `<duration>` (e.g. `5s`). `every` syncs before completing and confirming each transaction (most durable).

**Documented behavior (2.x):** the env var `SURREAL_SYNC_DATA=true` is the cross-backend switch.

**The wave-1 compose file ([docker-compose.surrealdb.yml](docker-compose.surrealdb.yml)) is misconfigured for G1.** It runs `surrealdb:v1.5.4` with `surrealkv://data/oasis.db` and sets `SURREAL_SYNC_DATA: "true"` + `SURREAL_KV_ROCKSDB_SYNC: "true"`. On 1.x:
- `SURREAL_SYNC_DATA` did not exist yet — it's a 2.x knob.
- `SURREAL_KV_ROCKSDB_SYNC` only affects `rocksdb://` URIs; this deployment uses `surrealkv://`.
- The correct 1.x switch is `surrealkv://data/oasis.db?sync=every` in the connection URI itself.

**G1 (durability forced on) is currently a no-op on this image.** This is the single largest blocker the journalistic lane found; it directly contradicts the spec's non-negotiable. (Worker-A's `learnings.md` shows the team flagged the version skew but landed on "set both for forward-compatibility" — neither switch applies to the chosen `1.x + surrealkv://` combo.)

**Independent corroboration of the risk:** Harrison Burt (chillfish8, [blog.cf8.gg/surrealdbs-ch/](https://blog.cf8.gg/surrealdbs-ch/), 2025-08-23): "this variable defaults to **false**, and there is _zero_ warning of this behaviour in the documentation." His piece — re-litigated on [HN id=45001908](https://news.ycombinator.com/item?id=45001908) and [Lobsters](https://lobste.rs/s/8tycd0/surrealdb_is_sacrificing_data) — also catches SurrealDB's own benchmarks forcing durability for MongoDB but not for themselves: "they force Mongo to ensure data is durable, but Surreal and Arango do not."

**Known SurrealKV data-integrity incidents (GitHub, labelled `topic:surrealkv`):**
- **#5001** — "SurrealDB won't start after unexpected shutdown while using surrealkv … only solution is to wipe the data directory, and restore from a backup." ([github.com/.../issues/5001](https://github.com/surrealdb/surrealdb/issues/5001)) — closed but is exactly the failure mode G1 is meant to prevent.
- **#5199** — "The current state of SurrealDB, with its apparent degradation in operational consistency, does not align with the reliability expectations required for production deployments." Closed as `noissue` (i.e., "won't fix as filed"). ([github.com/.../issues/5199](https://github.com/surrealdb/surrealdb/issues/5199))
- **#4872** — versioned data lost on export/import ([github.com/.../issues/4872](https://github.com/surrealdb/surrealdb/issues/4872)) — bears directly on G5 (backup/restore drill).
- **#6257** — "CLI surql export produces broken/incorrectly escaped SQL" — also G5. ([github.com/.../issues/6257](https://github.com/surrealdb/surrealdb/issues/6257))
- **#4755** — vector insert over a size threshold panics SurrealKV specifically.

**SurrealKV repo itself:** v0.21.2 (May 2026), 525 stars, README has **no** production-readiness disclaimer ([github.com/surrealdb/surrealkv](https://github.com/surrealdb/surrealkv)) — but is still <1.0.

---

## 4. Three independent critical voices

1. **Harrison Burt (chillfish8)**, ["SurrealDB is sacrificing data durability to make benchmarks look better"](https://blog.cf8.gg/surrealdbs-ch/), 2025-08-23. Load-bearing: *"users running SurrealKV or RocksDB backends … MUST EXPLICITLY set SURREAL_SYNC_DATA=true … otherwise the instance is NOT crash safe and can very easily corrupt."* SurrealDB devs gave no documented response on the corruption reports he cites.
2. **GeorgeCurtis** on HN ["why not surrealdb?"](https://news.ycombinator.com/item?id=43977294): *"General consensus is it's really slow, I like the concept of surreal though"* — his bare-bones graph-DB benchmark was "1–2 orders of magnitude faster than surreal." Reinforced by **riku_iki**: 5-second `count(*)` on 5M rows.
3. **cosmic_quanta** on [HN id=45001908](https://news.ycombinator.com/item?id=45001908): *"I know Kyle Kingsbury is probably backed up for years, but I wouldn't use a NewSQL DB without a Jepsen report."* — **No SurrealDB Jepsen report exists.** This is the field's standard durability-attestation bar; SurrealDB has not cleared it.

---

## 5. SurrealDB Labs (the company)

- **Funding:** $44M total — $23M Series A extension Feb 2026 on top of $15M Series A on top of $6M seed ([SiliconANGLE 2026-02-17](https://siliconangle.com/2026/02/17/surrealdb-raises-23m-expand-ai-native-multi-model-database/), [SurrealDB blog](https://surrealdb.com/blog/surrealdb-raises-23m-series-a-extension-to-power-the-ai-native-database-era)). Investors: FirstMark, Georgian, Chalfen, Begin. Not endangered short-term.
- **Headcount ~45**, single-country (London/UK concentrated) ([finsmes 2026-02](https://www.finsmes.com/2026/02/surrealdb-raises-23m-in-additional-series-a-funding.html)).
- **Open-core/licensing:** no licence change announced; product is Apache-2.0/BUSL-style mixed (worth tracking — Redis/Elastic precedents are recent).
- **Strategic pivot signal:** the 3.0 launch language ("the context layer for AI agents") indicates a marketing pivot to agent-memory, away from the OLTP/realtime-web positioning of 1.0. Engineering attention is following the AI workload, not financial-grade durability.

---

## The single piece of evidence that would change my assessment

**A named production deployment running SurrealKV on the value path with a public crash/chaos test result** — or, equivalently, a published Jepsen-style report. Neither exists. (The Saks/Permit case studies are read-heavy non-financial workloads; neither discloses durability config.)

---

## Verdict: **SLOW-DOWN-AND-VERIFY**

Wave-1 should not pass its own G1 gate as currently shipped, and the engine choice has zero published precedent in the workload class OASIS is putting on it.

**Concrete next-block actions (do not require abandoning the migration):**

1. **Fix the compose file before any further wave-1 work**: switch to `surrealkv://data/oasis.db?sync=every`; drop the two env vars that don't apply to 1.x + surrealkv. Add a startup assertion in the harness that confirms `sync=every` is the effective mode (parse server config or log). Without this, G1 is a doc claim with no runtime backing.
2. **Run an explicit crash test against the fixed config** — `docker kill -9` mid-write × N, restart, verify no `#5001`-style "Invalid transaction record ID" loss. This is the pre-cutover gate already in the spec; promote it from "later" to "before more code lands."
3. **Reframe G4's "stale" justification** to "still 0.x; lags server major versions by 2." The factual error in the spec ("Apr 2024") gives reviewers an easy out to dismiss the SDK concern, when the structural concern (0.x, server-version lag) is the real one and is stronger.
4. **Treat the absence of a financial-adjacent precedent as load-bearing**, not flavour text. The documented fallback in the spec (Postgres for audit ledger, SurrealDB for graph/MCP) should be promoted from "if G1/G2/G7 fail" to "default until a chaos test proves SurrealKV survives kill-9 on this config." Greenfield without users is the right window to verify, not to skip.

The migration is defensible as a graph + MCP + flexible-attribute play. It is not yet defensible as the sole engine under bridge / wallet / saga lifecycle records — and the wave-1 compose file plus the spec's mis-dated SDK premise are evidence the team is moving faster than the verification can support. Slow down ~1 sprint, fix the durability config, run the crash drill, then proceed.
