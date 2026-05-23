# OASIS Sleek — Runbook

**Last updated:** 2026-05-23
**Branch:** `api-safety-hardening`
**Last commit:** `d318bcb feat(surrealdb-convention)`
**Suite:** 535/535 unit green; 0 build warnings introduced by recent work.

This document is the day-to-day reference for the active work. For
historical track-by-track context see [conductor/tracks.md](conductor/tracks.md).
For the SurrealDB entity convention see
[Persistence/SurrealDb/CONVENTION.md](Persistence/SurrealDb/CONVENTION.md).

---

## 1. Status snapshot

### Recently shipped (last 48h)

- **`92ede75`** — 8 `.mermaid` schemas authored for the missing quest +
  dapp-composition entities. Roslyn source generator emits 8 new POCOs
  into `OASIS.WebAPI.Generated.SurrealDb.*`.
- **`92ede75`** — Full `dapp-composition` slice end-to-end on
  source-gen'd POCOs (manager, 2 controllers, 5 validators, STAR
  integration). 12 new unit tests; `[~]` track status.
- **`d318bcb`** — `Persistence/SurrealDb/CONVENTION.md` codifies the
  partial-class extension pattern. Applied to `DappSeries`/
  `DappSeriesQuest`; ~30 lines of conversion ceremony eliminated from
  the dapp-composition manager.

### In flight (parallel /ultrapilot session, ~1h ago last touched)

- **`surrealdb-migration` wave-2 quest stores** — 1595 lines authored
  by parallel /ultrapilot:
  - [Providers/Stores/Surreal/SurrealQuestStore.cs](Providers/Stores/Surreal/SurrealQuestStore.cs) (806 lines)
  - [Providers/Stores/Surreal/SurrealQuestRunStore.cs](Providers/Stores/Surreal/SurrealQuestRunStore.cs) (373 lines)
  - [Providers/Stores/Surreal/SurrealQuestNodeExecutionStore.cs](Providers/Stores/Surreal/SurrealQuestNodeExecutionStore.cs) (416 lines)
  - 3 corresponding integration test files
  - Currently untracked in git. Consume **hand-written** `Models.Quest.*`
    types, not the source-gen'd POCOs.
- **`230_quest_graph_edges.mermaid` + `.surql`** — RELATE edge schemas
  for `forked_from` + `executes` (from [SURREAL-SCHEMA-HINTS.md §6](conductor/tracks/quest-temporal-fork-model/SURREAL-SCHEMA-HINTS.md)).
- **Auto-emitted `.surql` files** for schemas 080–200 — appeared during
  this session; likely from a build hook or generator-driven emission.

### Blocked / pending decisions

- **Quest aggregate cutover to generated POCOs** — gated on the wave-2
  stores either pivoting to consume generated POCOs or being adapted
  post-merge. See §5.
- **Mermaid visualization restructure** — agreed pattern (aggregate
  slice files + auto-generated master diagram + FK emission to `.surql`)
  but not yet implemented. See §4.
- **`quest-api` endpoint gaps** — 18 missing endpoints, 12 missing
  manager methods. Better to land after the Quest cutover so the new
  endpoints sit on the post-cutover surface. See §5.

---

## 2. Conventions in force

| Convention | Source | Applies to |
|---|---|---|
| SurrealDB entity = source-gen'd POCO + partial extensions | [Persistence/SurrealDb/CONVENTION.md](Persistence/SurrealDb/CONVENTION.md) | All new SurrealDB-backed aggregates |
| No EF Core migrations on new work | [memory/greenfield-prelaunch-no-compat](.claude/projects/c--Users-atooz-Programming-Projects-oasis-sleek/memory/greenfield-prelaunch-no-compat.md) | Pre-launch, no customers/data |
| Integration tests on testcontainer Postgres | [memory/integration-tests-persistent-postgres](.claude/projects/c--Users-atooz-Programming-Projects-oasis-sleek/memory/integration-tests-persistent-postgres.md) | All `OASIS.WebAPI.IntegrationTests` |
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
the **same namespace** -- pattern documented in CONVENTION.md §3.1.
DTOs and in-memory transients stay in `OASIS.WebAPI.Models.*`. The
4 hand-written legacy entities (`Wallet`, `BlockchainOperation`,
`ConsumedVaaRecord`, `IdempotencyRecord`) cut over inside
`surrealdb-migration` wave-2; the 8 hand-written Quest aggregate
entities cut over in a coordinated follow-up after wave-2 ships.

---

## 4. Mermaid visualization restructure (new — agreed but not yet built)

**Goal:** elevate the 22 isolated single-table `.mermaid` files into a
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
is checked into git so GitHub renders it inline on the repo
landing page. Authors edit slices; readers consume the master.

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
   native graph edges. Coordinate with `surrealdb-migration` wave-2
   for the `.surql` ownership boundary.

**Sequencing:** Phase B (author slice files as docs only, no
generator changes) lands first to validate the visual model. Phase C
(generator updates) lands in its own focused slice once Phase B is
validated and the wave-2 work is settled.

### 4.4 Migration of existing 22 files

Phase B authors new `aggregates/*.mermaid` files in a directory the
generator does **not** read (avoid duplicate POCO emission).
Existing `Persistence/SurrealDb/Schemas/source/*.mermaid` keeps
emitting POCOs. Phase C migrates the generator to consume
`aggregates/` and deletes the 22 single-table files. POCOs remain
identical -- only the schema authoring layout changes.

---

## 5. Coordination map for in-flight work

```
        ┌─────────────────────────────────────────────┐
        │   Wave-2 quest stores (1595 lines, parallel)  │
        │   SurrealQuestStore.cs                        │
        │   SurrealQuestRunStore.cs                     │
        │   SurrealQuestNodeExecutionStore.cs           │
        │   + 3 integration tests                       │
        │   currently uses Models.Quest.* (hand-written)│
        └────────────────────┬───────────────────────┘
                             │ (1) commit wave-2 stores
                             ▼
        ┌─────────────────────────────────────────────┐
        │   Quest aggregate cutover                     │
        │   Add partial-class extensions for Quest,     │
        │   QuestNode, QuestEdge, QuestDependency,      │
        │   QuestRun, QuestNodeExecution                │
        │   Delete hand-written Models/Quest/*.cs       │
        │   Rewire wave-2 stores + 34 handlers +        │
        │   755-line QuestManager + tests               │
        │   ~7-9h                                       │
        └────────────────────┬───────────────────────┘
                             │ (2) cutover complete
                             ▼
        ┌─────────────────────────────────────────────┐
        │   quest-api endpoint gaps                     │
        │   18 missing endpoints + 12 missing manager   │
        │   methods (node/edge/dependency CRUD,         │
        │   activate, execute-next, execution-state,    │
        │   topological-order, complete/fail-quest,     │
        │   instantiate-from-template, publicOnly       │
        │   filter, list-by-status/dappSeriesId)        │
        │   ~2-3h on the post-cutover surface           │
        └────────────────────┬───────────────────────┘
                             │ (3) endpoints close
                             ▼
        ┌─────────────────────────────────────────────┐
        │   dapp-composition close-out                  │
        │   Integration tests (testcontainer Postgres)  │
        │   Swagger smoke verification                  │
        │   Track `[~]` -> `[x]`                        │
        │   ~1-2h                                       │
        └─────────────────────────────────────────────┘
```

**Why this order:**

1. Wave-2 first so the cutover has stable Surreal-backed stores to
   pivot on. Don't refactor a moving target.
2. Cutover before endpoint gaps so the new endpoints sit on the
   post-cutover surface (saves ~3h of duplicate rework).
3. dapp-composition integration tests after the Quest cutover so the
   real Surreal-backed Quest pipeline can drive an end-to-end
   compose → generate → deploy test.

---

## 6. Phased plan (next ~4 weeks)

| Phase | Work | Effort | Sequencing |
|---|---|---|---|
| **A. Runbook + tracks consolidation** (THIS COMMIT) | RUNBOOK.md, tracks.md prune | 1-2h | Done in this slice |
| **B. Mermaid aggregate slices (visualization-only)** | Author 6 `aggregates/*.mermaid` files + concat script for `docs/domain.generated.mermaid`. Generator unchanged. | 1-2h | Next session |
| **C. Generator: multi-table parsing + FK emission** | Roslyn parser update for multi-table files + relationship recognition + `.surql` FK ASSERT + RELATION emission. Migrate generator to read from `aggregates/`. Delete the 22 single-table files. | 4-6h | After Phase B |
| **D. Wave-2 commit + integration** | Commit the 3 SurrealQuest stores + tests + `230_quest_graph_edges.*`. Run integration tests. | 1h coord + 30min commit | After /ultrapilot signals done |
| **E. Quest aggregate cutover to generated POCOs** | Partial-class extensions + delete hand-written + rewire wave-2 stores + 34 handlers + QuestManager + tests. Aliases vanish. | 7-9h | After D |
| **F. quest-api endpoint gaps** | 18 missing endpoints + 12 missing manager methods on the post-cutover surface | 2-3h | After E |
| **G. dapp-composition close-out** | Integration tests against testcontainer Postgres + Swagger smoke | 1-2h | After F |
| **H. Frontend demo harness `frontend-demo-harness` track** | shadcn/ui demo harness, 6 phases | 8-10 days | Independent; can start any time |
| **I. `durable-saga-orchestration` Tier 1** | Reusable durable-saga + transactional-outbox module | TBD | After surrealdb-migration |
| **J. `mcp-surface` Tier 3** | MCP server over SurrealDB graph | TBD | After surrealdb-migration |

---

## 7. Open questions / pending decisions

1. **Aggregate boundary for the slice files** — section §4.1 proposes
   6 slices. Edge case: `dapp_series_quest` references `quest`
   (different slice). Two answers: (a) declare cross-slice
   relationships in the slice that *owns* the FK side, (b) require a
   master slice for cross-aggregate joins. Default to (a) until we
   hit pain.
2. **Mermaid syntax for cross-aggregate refs** — mermaid `erDiagram`
   does not natively support qualified entity names from other
   diagrams. Workaround: all aggregates emit into the same global
   namespace; the concat step deduplicates entity declarations.
   Document this in CONVENTION.md when Phase B lands.
3. **Generator change ordering** — should the generator switch to
   `aggregates/` consumption (Phase C) precede or follow the wave-2
   commit (Phase D)? Recommend: Phase C *after* D so wave-2 work
   doesn't need to chase a moving generator. Generator changes
   require coordinated `.surql` regeneration which is wave-2's domain.

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
| "Which track is which?" | [conductor/tracks.md](conductor/tracks.md) |
