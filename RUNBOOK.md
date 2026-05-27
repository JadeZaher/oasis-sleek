# OASIS Sleek — Runbook

**Last updated:** 2026-05-27
**Branch:** `api-safety-hardening` (5 commits ahead of upstream)
**Last commit:** `295d67c feat(mcp-surface): Tier-3 read-only MCP surface`
**Suite:** 540/540 unit green; 0 errors, 19 warnings (baseline).

This document is the day-to-day reference for the active work. For
historical track-by-track context see [conductor/tracks.md](conductor/tracks.md).
For the SurrealDB entity convention see
[Persistence/SurrealDb/CONVENTION.md](Persistence/SurrealDb/CONVENTION.md).

---

## 1. Status snapshot

### Recently shipped (last 5 commits)

- **`295d67c`** — `mcp-surface` track closed. Read-only MCP server at
  `/mcp` (ModelContextProtocol.AspNetCore 1.3.0) behind JWT+ApiKey
  multi-scheme. 5 tools (quest reachability, holon traversal, NFT graph,
  avatar-scoped read, HNSW vector search). +5 unit tests (540/540 green),
  13 integration tests gated on E1. Write tools deferred; runtime
  evidence + F9–F12 latent-item review pending E1 image fix.
- **`24a7403`** — `surrealdb-migration` Phase D (wave-2 commit).
  3 SurrealQuest stores (1595 LOC) + 6 `.surql` schemas (150/160/170/
  190/200/230) + 28 integration tests. G2 single-winner claim primitive
  + fork write-pairing via BEGIN/COMMIT. DI flipped at
  [Program.cs:267-298](Program.cs#L267-L298). Task 9 closed.
- **`8f1eee1`** — RUNBOOK.md + tracks.md consolidation.
- **`d318bcb`** — `CONVENTION.md` partial-class extension pattern.
- **`92ede75`** — 8 source-gen'd POCOs for quest + dapp-composition;
  dapp-composition slice end-to-end.

### Working tree

Clean — only `conductor/.conductor_session_log` modified
(auto-generated, ignored in commits). Nothing else in flight.

### Active phase

**Phase B — Mermaid aggregate slices (visualization-only).** See §4 for
the target shape and §6 for the phase plan. No generator changes;
authors `aggregates/*.mermaid` slice files + a concat script that
produces `docs/domain.generated.mermaid` checked into git for GitHub
inline rendering.

### Pending decisions

- **Phase C trigger** — generator multi-table parsing + FK emission
  lands after Phase B is validated (see §4.3). Recommended order:
  Phase B → Phase C → Phase E (Quest cutover) so the generator settles
  on the new authoring layout before the Quest aggregate moves to
  source-gen'd POCOs.
- **Environment E1 unblocker** — `surrealdb/surrealdb:v1.5.4` slim
  image lacks `surrealkv`; integration tests across the repo
  SkippableFact-skip cleanly. Fix: pin to `v1.5.4-dev` or swap the
  start URI to `rocksdb://data/oasis.db?sync=every`. Affects ALL
  integration tests, not just MCP / wave-2.

---

## 2. Conventions in force

| Convention | Source | Applies to |
|---|---|---|
| SurrealDB entity = source-gen'd POCO + partial extensions | [Persistence/SurrealDb/CONVENTION.md](Persistence/SurrealDb/CONVENTION.md) | All new SurrealDB-backed aggregates |
| No EF Core migrations on new work | [memory/greenfield-prelaunch-no-compat](.claude/projects/c--Users-atooz-Programming-Projects-oasis-sleek/memory/greenfield-prelaunch-no-compat.md) | Pre-launch, no customers/data |
| Integration tests on testcontainer Postgres / SurrealDB | [memory/integration-tests-persistent-postgres](.claude/projects/c--Users-atooz-Programming-Projects-oasis-sleek/memory/integration-tests-persistent-postgres.md) | All `OASIS.WebAPI.IntegrationTests` |
| Bridge tier-0 hardening invariants | [api-safety-hardening RESIDUAL-RISK-RUNBOOK §4](conductor/tracks/api-safety-hardening/RESIDUAL-RISK-RUNBOOK.md) | Bridge value flow |
| TDD on bug fixes + features | [conductor/skills/tdd-workflow](conductor/) | Default |

---

## 3. SurrealDB convention recap

Full doc: [Persistence/SurrealDb/CONVENTION.md](Persistence/SurrealDb/CONVENTION.md). One-paragraph version:

`.mermaid` schemas are the source of truth. The Roslyn source generator
at [packages/Oasis.SurrealDb.SourceGen/](packages/Oasis.SurrealDb.SourceGen)
emits partial POCOs into `OASIS.WebAPI.Generated.SurrealDb.<Entity>`.
Ergonomic helpers (`Guid` ⇄ `string("N")`, `IDictionary` ⇄ `JsonElement`,
domain predicates, factories) live as sibling partial-class files in
the **same namespace** — pattern documented in CONVENTION.md §3.1.
DTOs and in-memory transients stay in `OASIS.WebAPI.Models.*`. The
4 hand-written legacy entities (`Wallet`, `BlockchainOperation`,
`ConsumedVaaRecord`, `IdempotencyRecord`) cut over inside
`surrealdb-migration` wave-2; the 8 hand-written Quest aggregate
entities cut over in **Phase E** (see §6) after the mermaid layout
(Phase B) + generator updates (Phase C) settle.

---

## 4. Mermaid visualization restructure (active — Phase B starting)

**Goal:** elevate the 24 isolated single-table `.mermaid` files into a
true visual data model. The user observation: mermaid's value is the
relationship arrows, not the per-table annotations.

### 4.1 Target shape — per-aggregate slice files

| Slice file | Entities | Relationships in slice |
|---|---|---|
| `aggregates/quest.mermaid` | quest, quest_node, quest_edge, quest_dependency, quest_run, quest_node_execution | quest ‖--o{ quest_node, quest ‖--o{ quest_edge, quest ‖--o{ quest_dependency, quest_run ‖--o{ quest_node_execution, quest_run o\|--o\| quest_run (parent), quest_node ‖--o{ quest_node_execution (executes) |
| `aggregates/quest_templates.mermaid` | quest_template, quest_node_template | quest_template o{--‖ quest_node_template (refs) |
| `aggregates/dapp_composition.mermaid` | dapp_series, dapp_series_quest | dapp_series ‖--o{ dapp_series_quest, dapp_series_quest }o--‖ quest (cross-slice) |
| `aggregates/bridge.mermaid` | bridge_tx, saga_steps, consumed_vaa_ledger, idempotency_key_store, operation_log | bridge_tx ‖--o{ saga_steps |
| `aggregates/wallet_nft.mermaid` | wallet, nft_ownership, swap_state | wallet ‖--o{ nft_ownership (owns) |
| `aggregates/identity.mermaid` | avatar, api_key, holon, star | avatar ‖--o{ api_key, avatar ‖--o{ holon |

### 4.2 Build step — auto-generated master

A new build step (Powershell or .NET tool) concatenates all
`aggregates/*.mermaid` into `docs/domain.generated.mermaid`. The master
is checked into git so GitHub renders it inline on the repo landing
page. Authors edit slices; readers consume the master.

### 4.3 Generator changes (Phase C — substantial)

The Roslyn `IIncrementalGenerator` at
[packages/Oasis.SurrealDb.SourceGen/](packages/Oasis.SurrealDb.SourceGen)
needs three updates:

1. **Multi-table per file** — currently parses one entity per
   `.mermaid` via `AdditionalTextsProvider`. Migrate to parse
   multiple `erDiagram` table blocks per file. POCO emission stays
   1:1 with table blocks (one `.g.cs` per table); the change is just
   in the parser.
2. **Relationship parsing** — recognize mermaid relationship lines
   (`||--o{`, `||--||`, `o|--o|`, `}o--||`) and store them in the
   schema model.
3. **FK emission to `.surql`** — emit `ASSERT type::is::record($value,
   <target_table>)` clauses on FK fields, and `DEFINE TABLE
   <edge_name> SCHEMAFULL TYPE RELATION FROM <a> TO <b>` blocks for
   native graph edges. Aligns with surrealdb-migration F6 follow-up
   (FK columns as `record<table>` not bare `string`).

**Sequencing:** Phase B (author slice files as docs only, no
generator changes) lands first to validate the visual model. Phase C
(generator updates) lands in its own focused slice once Phase B is
validated.

### 4.4 Migration of existing 24 files

Phase B authors new `aggregates/*.mermaid` files in a directory the
generator does **not** read (avoid duplicate POCO emission). Existing
`Persistence/SurrealDb/Schemas/source/*.mermaid` keeps emitting POCOs.
Phase C migrates the generator to consume `aggregates/` and deletes
the 24 single-table files. POCOs remain identical — only the schema
authoring layout changes.

---

## 5. Forward sequencing — what unblocks what

```
        ┌─────────────────────────────────────────────┐
        │   Phase B (HERE) — Mermaid aggregate slices  │
        │   6 aggregates/*.mermaid + concat script      │
        │   docs/domain.generated.mermaid checked in    │
        │   Generator unchanged                         │
        │   ~1-2h                                       │
        └────────────────────┬───────────────────────┘
                             │ (1) visual model validated
                             ▼
        ┌─────────────────────────────────────────────┐
        │   Phase C — Generator multi-table + FK        │
        │   Parser: multi-table per file                │
        │   Recognize relationship arrows               │
        │   Emit FK ASSERTs + RELATION blocks to .surql │
        │   Migrate generator to read aggregates/       │
        │   Delete 24 single-table .mermaid files       │
        │   ~4-6h                                       │
        └────────────────────┬───────────────────────┘
                             │ (2) generator on new layout
                             ▼
        ┌─────────────────────────────────────────────┐
        │   Phase E — Quest aggregate cutover           │
        │   Partial-class extensions for Quest,         │
        │   QuestNode, QuestEdge, QuestDependency,      │
        │   QuestRun, QuestNodeExecution                │
        │   Delete hand-written Models/Quest/*.cs       │
        │   Rewire wave-2 stores + 34 handlers +        │
        │   755-line QuestManager + tests               │
        │   ~7-9h                                       │
        └────────────────────┬───────────────────────┘
                             │ (3) cutover complete
                             ▼
        ┌─────────────────────────────────────────────┐
        │   Phase F — quest-api endpoint gaps           │
        │   18 missing endpoints + 12 missing manager   │
        │   methods (node/edge/dependency CRUD,         │
        │   activate, execute-next, execution-state,    │
        │   topological-order, complete/fail-quest,     │
        │   instantiate-from-template, publicOnly       │
        │   filter, list-by-status/dappSeriesId)        │
        │   ~2-3h on the post-cutover surface           │
        └────────────────────┬───────────────────────┘
                             │ (4) endpoints close
                             ▼
        ┌─────────────────────────────────────────────┐
        │   Phase G — dapp-composition close-out        │
        │   Integration tests (SurrealDB testcontainer) │
        │   Swagger smoke verification                  │
        │   Track `[~]` → `[x]`                         │
        │   ~1-2h                                       │
        └─────────────────────────────────────────────┘
```

**Why this order:**

1. Phase B before C so we ratify the visual layout before paying
   generator-rewrite cost on the wrong shape.
2. Phase C before E so the Quest cutover targets the final
   generator surface, not a moving one.
3. Phase E before F so the new endpoints sit on the post-cutover
   surface (saves ~3h of duplicate rework).
4. dapp-composition integration tests after the Quest cutover so the
   real Surreal-backed Quest pipeline can drive an end-to-end
   compose → generate → deploy test.

---

## 6. Phased plan (next ~3 weeks)

| Phase | Work | Effort | Status |
|---|---|---|---|
| A. Runbook + tracks consolidation | RUNBOOK.md, tracks.md prune | 1-2h | ✓ Shipped 2026-05-23 (`8f1eee1`) |
| **B. Mermaid aggregate slices (visualization-only)** | Author 6 `aggregates/*.mermaid` files + concat script for `docs/domain.generated.mermaid`. Generator unchanged. | 1-2h | **ACTIVE** |
| C. Generator: multi-table parsing + FK emission | Roslyn parser update for multi-table files + relationship recognition + `.surql` FK ASSERT + RELATION emission. Migrate generator to read from `aggregates/`. Delete the 24 single-table files. | 4-6h | After B |
| D. Wave-2 commit + integration | Commit the 3 SurrealQuest stores + tests + `230_quest_graph_edges.*`. | 1h | ✓ Shipped 2026-05-27 (`24a7403`) |
| E. Quest aggregate cutover to generated POCOs | Partial-class extensions + delete hand-written + rewire wave-2 stores + 34 handlers + QuestManager + tests. Aliases vanish. | 7-9h | After C |
| F. quest-api endpoint gaps | 18 missing endpoints + 12 missing manager methods on the post-cutover surface | 2-3h | After E |
| G. dapp-composition close-out | Integration tests against testcontainer Postgres + Swagger smoke | 1-2h | After F |
| H. Frontend demo harness `frontend-demo-harness` track | shadcn/ui demo harness, 6 phases | 8-10 days | Independent; can start any time |
| I. `durable-saga-orchestration` Tier 1 | Reusable durable-saga + transactional-outbox module | TBD | After surrealdb-migration (done) |
| J. `mcp-surface` Tier 3 | MCP server over SurrealDB graph | — | ✓ Shipped 2026-05-25 (`295d67c`) |

---

## 7. Open questions / pending decisions

1. **Aggregate boundary for the slice files** — §4.1 proposes 6
   slices. Edge case: `dapp_series_quest` references `quest`
   (different slice). Two answers: (a) declare cross-slice
   relationships in the slice that *owns* the FK side, (b) require a
   master slice for cross-aggregate joins. Default to (a) until we
   hit pain.
2. **Mermaid syntax for cross-aggregate refs** — mermaid `erDiagram`
   does not natively support qualified entity names from other
   diagrams. Workaround: all aggregates emit into the same global
   namespace; the concat step deduplicates entity declarations.
   Document this in CONVENTION.md when Phase B lands.
3. **Concat tooling** — PowerShell vs `dotnet tool` vs MSBuild target.
   PowerShell keeps the dependency surface zero (Windows-native);
   MSBuild target couples it to `dotnet build` so it can't drift.
   Default to MSBuild target (regen on build) since dev-machine
   touches `.mermaid` slices but rarely touches PowerShell.

---

## 8. Where to look for what

| Question | Document |
|---|---|
| "What's the right C# pattern for a new SurrealDB entity?" | [Persistence/SurrealDb/CONVENTION.md](Persistence/SurrealDb/CONVENTION.md) |
| "How do I add a new field to an existing entity?" | The relevant `.mermaid` in [Persistence/SurrealDb/Schemas/source/](Persistence/SurrealDb/Schemas/source/) + rebuild |
| "Where is the source generator?" | [packages/Oasis.SurrealDb.SourceGen/](packages/Oasis.SurrealDb.SourceGen) |
| "What does the API surface look like?" | [PROVIDERS.md](PROVIDERS.md) + [API_SYNC.md](API_SYNC.md) |
| "What invariants does the bridge enforce?" | [conductor/tracks/api-safety-hardening/RESIDUAL-RISK-RUNBOOK.md](conductor/tracks/api-safety-hardening/RESIDUAL-RISK-RUNBOOK.md) |
| "What's the quest temporal model?" | [conductor/tracks/quest-temporal-fork-model/ADR.md](conductor/tracks/quest-temporal-fork-model/) |
| "How are quest tables intended to live in SurrealDB?" | [conductor/tracks/quest-temporal-fork-model/SURREAL-SCHEMA-HINTS.md](conductor/tracks/quest-temporal-fork-model/SURREAL-SCHEMA-HINTS.md) |
| "What's the MCP surface look like?" | [conductor/tracks/mcp-surface/CATALOG.md](conductor/tracks/mcp-surface/CATALOG.md) |
| "Which track is which?" | [conductor/tracks.md](conductor/tracks.md) |
