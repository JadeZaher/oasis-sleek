# Two-Owner Parallel Launch — One-Shot Prompt

**Paste the entire content below this line into a fresh Claude Code session.** It is self-contained: full permissions asserted, both tracks scoped, parallel execution pattern specified, check-in gates defined.

---

You are the coordinator for the next ~2-week parallel execution of two independent Conductor tracks on the OASIS Sleek project. Working directory is `c:\Users\atooz\Programming\Projects\oasis-sleek` on branch `api-safety-hardening`. Today's date will differ from when this prompt was written (2026-05-21) — adjust references accordingly.

## Standing authorization (you do not need to ask)

The user has pre-authorized you for:
- Read / Edit / Write / Glob / Grep on any path EXCEPT the forbidden surface listed below
- Bash and PowerShell for: `dotnet build`, `dotnet test`, `dotnet restore`, `dotnet pack`, `dotnet new`, `dotnet sln`, `dotnet add`, `git status`, `git diff`, `git log`, `git add`, `git commit`, `git checkout -b`, `mkdir`, `rm` (only within paths you created or own per the partitioning below), `powershell -File scripts/...`, `docker compose ...` / `podman compose ...`
- Spawning background agents (Agent tool, `run_in_background: true`) for parallel work
- Invoking OMC skills: `ultrapilot`, `code-review`, `tdd`, `build-fix`, `security-review`, `deepsearch`
- Reading the persona research at `.omc/research/surrealdb-migration-wave1/persona-*.md` (these are the evidence base for the architectural decisions)

You do NOT have authorization for: `git push`, `git push --force`, `git reset --hard`, `git rebase -i`, `--no-verify`, modifying CI/CD config files, modifying `~/.claude/`, modifying paths outside the project working directory, deleting code under `Models/Quest/**` or the other forbidden paths below, publishing NuGet packages to public feeds. If any of these become necessary, stop and ask the user.

## Two tracks to execute in parallel

### Owner A — `surrealdb-client-package` sub-wave 1.5a (~2 weeks of work; ~5 parallel workers)
- Spec: `conductor/tracks/surrealdb-client-package/spec.md`
- Plan: `conductor/tracks/surrealdb-client-package/plan.md`
- Scope: ONLY Phases 1–7 (sub-wave 1.5a + sign-off). Skip Phases 8–11 (WebSocket / LIVE / saga adoption) — those are sub-wave 1.5b, deferred to a later launch
- Acceptance for this run: sub-wave 1.5a sign-off (task 40 in the plan)

### Owner B — `quest-temporal-fork-model` (~1 week of work; ~2 parallel workers)
- Spec: `conductor/tracks/quest-temporal-fork-model/spec.md`
- Plan: `conductor/tracks/quest-temporal-fork-model/plan.md`
- Scope: ALL plan tasks 1–18
- Acceptance for this run: task 18 sign-off — ADR merged, `SURREAL-SCHEMA-HINTS.md` merged, all tests green, `dotnet build` zero warnings

## Launch pattern (highest-leverage parallel)

Spawn TWO parallel work streams as background agents using the Agent tool with `run_in_background: true`. Do NOT invoke `/oh-my-claudecode:ultrapilot` directly — its 5-worker cap fits one track but not both. Instead, replicate the ultrapilot file-ownership pattern manually across ~7 total background agents:

**Owner A swarm (5 background workers, file-partitioned):**
- **A1** — Phases 1–2 (bootstrap + client core: HTTP transport, JSON config, transactions, connection pool). Owns `packages/Oasis.SurrealDb.Client/Transport/`, `Json/`, `Connection/` (all new) + the package csproj
- **A2** — Phase 3 (query builder + identifier safety + multi-statement). Owns `packages/Oasis.SurrealDb.Client/Query/` (relocated from `OASIS.WebAPI/Core/SurrealDb/Query/`). MUST NOT touch OASIS source paths until A5 integration phase
- **A3** — Phase 4 (Mermaid parser + generator + migration runner + CLI). Owns `packages/Oasis.SurrealDb.Schema/` (all new) + the CLI tool csproj
- **A4** — Phase 5 (analyzer relocation + H3 bypass-closure extension). Owns `packages/Oasis.SurrealDb.Analyzer/` (relocated from `analyzers/SurrealQlSafetyAnalyzer/`)
- **A5** — Phase 6 (OASIS integration: delete vendor SDK, rewire references, re-author 7 schemas as `.mermaid`, apply B1+B2+B3 fixes inline, update pass-off). **Sequential dependency on A1+A2+A3+A4 — launch this one AFTER the others complete.** Owns `OASIS.WebAPI.csproj`, `Directory.Build.props` (new), `Persistence/SurrealDb/Schemas/source/` (new), `scripts/passoff-surrealdb-wave1.ps1` (edits)

**Owner B swarm (2 background workers, sequential):**
- **B1** — Tasks 1–9: ADR + `SURREAL-SCHEMA-HINTS.md` + domain models (`QuestRun.cs`, `QuestNodeExecution.cs`, enum extensions) + store interfaces (`IQuestRunStore`, `IQuestNodeExecutionStore`) + InMemory adapters + DI registration. Owns `Models/Quest/Quest*Run*.cs` + `Models/Quest/Quest*Execution*.cs` (NEW files only — existing `Quest.cs`/`QuestNode.cs` get field-removals only) + `Interfaces/Stores/IQuest*Store.cs` (new) + relevant Provider adapter (`Providers/InMemoryStorageProvider.cs` quest-run/exec portions only)
- **B2** — Tasks 10–16 (QuestManager rewrite to thread runId + test port). Sequential dependency on B1. Owns `Managers/QuestManager.cs` (modifications), `Services/Quest/` (modifications), all `tests/OASIS.WebAPI.Tests/Quest/` (test updates + new tests for fork/lineage)

**Coordination:** Owners A and B touch disjoint paths. The only theoretical conflict is `Program.cs` DI registration (B adds `IQuestRunStore` registration, A5 may touch DI). Resolve by giving B1 ownership of Program.cs's quest-registration block ONLY; A5 owns SurrealDB-client registration block ONLY. Both edit the file but in non-overlapping regions — merge-friendly.

## Cross-cutting forbidden surface (BOTH owners)

NO worker touches `quest_run` / `quest_node_execution` / `quest_node` / `quest_edge` / `quest_template` / `quest_dependency` in any **`.surql`** schema file. (Owner B writes the schema-hints DOC; Owner A consumes it later for the eventual schema generation — but this run does not produce any quest `.surql` files because that's `surrealdb-migration` task 9 territory, which is gated on Owner B's hand-off doc + the package's Mermaid tooling.)

Owner A MUST NOT touch: any `Models/Quest/**` file, `Services/QuestDagValidator.cs`, `Managers/QuestManager.cs`, `Services/Quest/QuestInstantiator.cs` (Owner B's territory).
Owner B MUST NOT touch: `packages/Oasis.SurrealDb.*`, `analyzers/SurrealQlSafetyAnalyzer/` (Owner A's territory), `Persistence/SurrealDb/Schemas/`, `scripts/surrealdb/`, `docker-compose.surrealdb.yml`.

## Hard guardrails (constant)

Run continuously, do not let any commit / merge break them:
- `dotnet build` exit 0, ≤17 warnings baseline
- `dotnet test tests/OASIS.WebAPI.Tests` → 618+ passing, 0 failed, 0 skipped (Owner A's package-suite tests add to the count; Owner B's fork-model tests add too)
- `powershell -NoProfile -File scripts/passoff-surrealdb-wave1.ps1` → exit 0 throughout. Owner A's Phase 6 will update sections 2/3/7; verify they still pass after the edits
- Pre-existing 537 api-safety tests + 81 wave-1 tests stay 100% green
- Bridge value paths remain fail-closed per `conductor/tracks/api-safety-hardening/RESIDUAL-RISK-RUNBOOK.md` §4
- Do NOT run frontend typecheck (per project memory `no-frontend-typecheck`)

## Commit discipline

Commit each phase as it completes with conventional-commit messages:
- `feat(surrealdb-client-package): Phase N — <what>`
- `feat(quest-temporal-fork-model): task N — <what>`
- One commit per phase / per task group; do NOT batch unrelated changes
- Run pass-off gate before each commit; refuse to commit if any gate fails
- Use HEREDOC for commit messages per the standard pattern; sign with `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>`
- Do NOT push to remote (user-only action)

## Check-in protocol

Run autonomously through both swarms. Check in with the user **only** at these natural breakpoints:

1. **After spawning all background agents** — one short status: "Owner A spawned 4 workers (A1–A4 parallel; A5 deferred until A1–A4 complete). Owner B spawned 1 worker (B1; B2 deferred until B1 complete). All in background. Will report when first wave completes."
2. **When A1–A4 + B1 all complete** — synthesize a check-in with: package-suite test counts, fork-model test counts, build status, any blockers surfaced. Ask whether to proceed with A5 (integration) + B2 (QuestManager rewrite).
3. **When A5 + B2 complete** — final synthesis: total test count, pass-off gate result, sub-wave 1.5a sign-off status, fork-model sign-off status, what `SURREAL-SCHEMA-HINTS.md` looks like, recommended next action (probably: launch sub-wave 1.5b OR resume `surrealdb-migration` wave-2 adapter work using the new package).

Do NOT check in for: routine task progress, completed individual tasks within a phase, expected build warnings within baseline.

## Failure mode handling

- A single worker failing does NOT stop the swarm. Capture the failure, mark the owner's status, continue other workers, surface the failure in the next check-in
- If the pass-off gate breaks at any commit, the offending worker rolls back its last commit and re-attempts the task with the gate failure as additional context
- If a CRITICAL guardrail breaks (e.g. test count drops below 618 due to your work), STOP all workers, surface to the user immediately with the diff and the failing test names

## End-of-run report format

When both owners hit acceptance, return ONE message containing:
- Owner A status: phases complete + total LOC added + test count delta + pass-off gate status
- Owner B status: tasks complete + LOC added + test count delta + `SURREAL-SCHEMA-HINTS.md` quote-of-the-canonical-schema-tables
- Combined: total test count, build status, pass-off green
- Conflict review: which Program.cs / shared-file edits had to be merged, how
- Recommended next action with rationale
- `.omc/ultrapilot-state.json` updated to reflect new state

## First action when you receive this prompt

1. Confirm you can read all the spec/plan files (Owner A spec, Owner A plan, Owner B spec, Owner B plan)
2. Read `.omc/ultrapilot-state.json` for current state
3. Read at minimum `persona-archaeological.md` + `persona-journalistic.md` from `.omc/research/surrealdb-migration-wave1/` (the two highest-signal personas — they inform B1 fix specifics)
4. Verify the pass-off gate currently exits 0: `powershell -NoProfile -File scripts/passoff-surrealdb-wave1.ps1`
5. Initialize `TodoWrite` with one todo per owner-phase
6. Spawn all parallel-eligible background agents in a SINGLE message (one Agent tool call per worker, all in the same response — this maximizes concurrency)
7. Return the spawn confirmation status update and wait for completions

Go.
