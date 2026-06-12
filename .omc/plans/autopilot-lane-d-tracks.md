# Autopilot Lane D — Author 2 Conductor Tracks from 2026-06-10 Audit

## Context

### Original Request
Autopilot Lane D worker assignment: author two new conductor tracks (`self-audit-one-fix` and `surreal-schema-package-retro`) capturing 2026-06-10 audit findings, and append two rows to `conductor/tracks.md`. The Prometheus planner cannot write to `conductor/**`, so this plan exists for an executor agent to carry out.

### Why this is a plan, not a direct execution
Prometheus identity constraints forbid writing files outside `.omc/*.md`. The requested writes are to:
- `conductor/tracks/self-audit-one-fix/spec.md`
- `conductor/tracks/self-audit-one-fix/plan.md`
- `conductor/tracks/surreal-schema-package-retro/spec.md`
- `conductor/tracks/surreal-schema-package-retro/plan.md`
- `conductor/tracks.md` (append two rows)

All five are outside the Prometheus write surface. An executor must perform the writes.

### Audit context (read-only references)
- Source of truth: `c:\Users\atooz\Programming\Projects\oasis-sleek\.omc\audit\AUDIT-SUMMARY.md`
- Per-lane reports: `.omc/audit/w1-dotnet-backend.md`, `w2-sdk-frontend.md`, `w3-surrealdb-toolkit.md`, `w4-tests.md`, `w5-tracks-docs.md`
- Format calibration: `conductor/tracks/api-safety-hardening/spec.md`, `conductor/tracks/dapp-composition/spec.md`

### Voice/format conventions (observed in api-safety-hardening/spec.md)
- Heading levels: `# Track Name — Specification` then `## Goal`, `## <Section>`, `### <Subsection>`.
- File:line citations in backticks throughout body text (e.g. `Models/Wallet.cs:6-19`).
- Acceptance items use prose bullets in `api-safety-hardening`, but the user explicitly requested `[ ]` checkboxes for these two new tracks — match that requirement.
- "Out of scope" / "Dependencies" / "Tier" sections at end.
- No emoji.

---

## Work Objectives

### Core Objective
Land two new conductor tracks and a tracks.md update that close the audit-findings loop without scope-creeping into refactors.

### Deliverables
1. `conductor/tracks/self-audit-one-fix/spec.md` (~150-300 lines)
2. `conductor/tracks/self-audit-one-fix/plan.md` (~80-150 lines)
3. `conductor/tracks/surreal-schema-package-retro/spec.md` (~200-400 lines)
4. `conductor/tracks/surreal-schema-package-retro/plan.md` (~80-150 lines)
5. `conductor/tracks.md` — append two pending rows, no other edits.

### Definition of Done
- All four track files exist and follow the spec format observed in `api-safety-hardening/spec.md`.
- All code claims in the specs cite `file:line` — verified to exist at write time.
- `tracks.md` Pending section contains the two new rows, placed after `data-backfill-migrations` (so they survive Lane B's deletion of the surrealql-* rows).
- No other rows in `tracks.md` are touched.
- No files outside the five enumerated paths are touched.

---

## Must Have

- Quote `file:line` exactly as written in audit reports (W1/W2/W3).
- Verify each referenced file still exists at write time (`Read` or `Glob` before citing).
- Acceptance criteria as `[ ]` checkboxes (per user request).
- Match existing tracks' voice: terse, evidence-led, no marketing language.
- `tracks.md` row format matches existing pipe-delimited single-line style.

## Must NOT Have

- No edits to any file outside the five enumerated paths.
- No deletion of the `surrealql-toolkit` rows (Lane B owns those).
- No edits to the Shipped section of `tracks.md`.
- No new template invented — match `api-safety-hardening/spec.md` and `dapp-composition/spec.md`.
- No emoji.
- No `frontend/` typecheck triggered (per global memory `no-frontend-typecheck`).
- No SDK build, `.NET` build, or test runs during this track authoring (these are doc files only).

---

## Task Flow and Dependencies

```
Phase 0: Calibration (read-only)
  └─> Phase 1: Track 1 (self-audit-one-fix)
        └─> Phase 2: Track 2 (surreal-schema-package-retro)
              └─> Phase 3: tracks.md append
                    └─> Phase 4: Verification
```

Phases are sequential because each phase's verification informs the next (e.g. confirming a file:line still exists before citing it in a later track).

---

## Detailed TODOs

### Phase 0 — Calibration (read-only)

- [ ] Read `conductor/tracks/api-safety-hardening/spec.md` in full (~5.6 KB). Note heading hierarchy, citation style, "Goal / Confirmed defects / Required behavior / Dependencies" structure.
- [ ] Read `conductor/tracks/dapp-composition/spec.md` in full. Note variations in section ordering and tier-line placement.
- [ ] Read `.omc/audit/AUDIT-SUMMARY.md` in full. Note the 9 SDK/frontend findings from W2 and the surreal schema retro context from W3.
- [ ] Read `.omc/audit/w2-sdk-frontend.md` and `.omc/audit/w3-surrealdb-toolkit.md` for the exact file:line quotes the spec must cite.
- [ ] Read `conductor/tracks.md` Pending section to identify the insertion point (after `data-backfill-migrations`, before `surrealql-toolkit`).
- [ ] Acceptance: notes captured for spec voice, citation style, and tracks.md row format.

### Phase 1 — Track 1: `self-audit-one-fix`

**Goal:** Bundle 9 small code bugs from the audit into one closable track. Not a refactor — one fix per finding.

#### 1.1 Verify cited files still exist

- [ ] `Read` (head only) `sdk/oasis-wallet/src/dex/jupiter.ts` and confirm line ~122 contains `Buffer.from(data.swapTransaction, "base64")`.
- [ ] `Read` `sdk/oasis-wallet/src/core/encoding.ts` and confirm `base64Decode` export exists.
- [ ] `Read` `frontend/src/app/(dashboard)/swap/page.tsx` around lines 67 and 90-110.
- [ ] `Read` `sdk/oasis-wallet/src/dex/tinyman.ts` around lines 168 and 232.
- [ ] `Read` `frontend/src/app/(dashboard)/settings/page.tsx` around lines 43, 46.
- [ ] `Read` `sdk/oasis-wallet/src/algorand/provider.ts` around lines 416-417 and 492-504.
- [ ] `Read` `sdk/oasis-wallet/src/api/api-version.ts` and grep for `NFT_LIST`.
- [ ] `Read` `sdk/oasis-wallet/src/api/client.ts` and confirm `listNfts` is absent.
- [ ] `Read` `OASIS.WebAPI/Controllers/NftController.cs` to confirm whether a list endpoint exists.
- [ ] `Read` `sdk/oasis-wallet/src/client/holon-query.ts` for `getComposite` path.
- [ ] `Read` `frontend/src/lib/oasis-hooks.ts` around line 132 for the `useWallets` raw-request bug.
- [ ] Acceptance: every file:line about to be cited has been confirmed present; any drift is recorded in the plan so the spec quotes the as-of-today reality, not the audit's snapshot.

#### 1.2 Write `conductor/tracks/self-audit-one-fix/spec.md`

Spec outline (target ~150-300 lines):

- `# Self Audit One Fix — Specification`
- `## Goal` — One paragraph: close 9 small code bugs from the 2026-06-10 audit as one Tier 0.5 track. NOT a refactor. One fix per finding.
- `## Background` — Point at `.omc/audit/AUDIT-SUMMARY.md`. State that this track converts the audit's 9 W2 findings into a closable unit so they're tracked rather than scattered as TODOs.
- `## Confirmed defects (file:line evidence)` — 9 subsections, one per finding. Each subsection:
  - `### N. <Short title>` (e.g. `### 1. Jupiter adapter Buffer.from() cross-platform violation`)
  - File:line citation in backticks.
  - 1-3 sentences describing the defect.
  - "Fix:" sentence describing the resolution.
  - Cross-platform invariant referenced for findings that touch it (1, 5).
- The 9 findings:
  1. Jupiter `Buffer.from()` at `sdk/oasis-wallet/src/dex/jupiter.ts:122` — replace with `base64Decode` from `core/encoding.ts`. Also remove spurious `requiresSigning: true` field from returned `UnsignedTransaction`.
  2. Swap page broken at `frontend/src/app/(dashboard)/swap/page.tsx:67` (404 endpoint) and `:90-110` (mock build). Decision required: backend `SwapController` quote+build pair OR rewire to `oasis.wallet.getSwapQuote()` / `oasis.wallet.buildSwap()`. Pick one; do not leave the mock.
  3. Tinyman decimal hardcoding at `sdk/oasis-wallet/src/dex/tinyman.ts:168` — derive `decimals.assetIn` / `decimals.assetOut` from indexer ASA metadata. Plus remove dead unused-slippage comment at `:232`.
  4. Settings page chains type mismatch at `frontend/src/app/(dashboard)/settings/page.tsx:43` — treat `OasisWallet.chains` as `string[]`; remove `?? {}` and unnecessary optional chain. Plus line 46: replace the `(oasis as unknown as { config?: ... }).config?.apiUrl` cast with a proper public accessor on `OasisClient` (`getApiUrl()` or session-adapter state).
  5. Algorand `encodeTransaction` msgpack gap at `sdk/oasis-wallet/src/algorand/provider.ts:492-504` and `:416-417` — implement canonical msgpack OR document "wallet-adapter only" and remove dead Ed25519 native signing helper. Pick one; do not leave half-implementation.
  6. SDK `listNfts()` missing — `NFT_LIST` path in `api/api-version.ts` (if still present post Lane C) is unimplemented in `OasisApiClient`. Confirm whether `GET /api/nft` returns a list via `NftController.cs`. If yes, add `listNfts()` to `sdk/oasis-wallet/src/api/client.ts`. If no, document why no list endpoint exists.
  7. SDK `updateSTARODK` missing — backend has create + delete but no PUT. Acceptance: either add `PUT /api/starodk/:id` + SDK `updateSTARODK()` OR document that STARODK is immutable after creation.
  8. `HolonQueryBuilder.getComposite()` path mismatch at `sdk/oasis-wallet/src/client/holon-query.ts` — confirm backend route; either change SDK path to match controller or add a `/compose` endpoint to `HolonController`.
  9. `useWallets` hook duplicates SDK logic at `frontend/src/lib/oasis-hooks.ts:132` — use `oasis.api.listWallets({ avatarId })` instead of `oasis.api.request("GET", "/api/wallet?avatarId=...")`.
- `## Acceptance criteria` — `[ ]` checkboxes:
  - [ ] Finding 1: Jupiter base64Decode swap + spurious field removed.
  - [ ] Finding 2: Swap page wired to real backend OR SDK DEX adapter (decision recorded in plan.md).
  - [ ] Finding 3: Tinyman decimals derived from ASA metadata; dead comment removed.
  - [ ] Finding 4: Settings page chains[] handling correct; `getApiUrl()` accessor introduced.
  - [ ] Finding 5: Algorand msgpack implemented OR native-signing helper removed with doc note.
  - [ ] Finding 6: `listNfts()` added OR documented absence.
  - [ ] Finding 7: `updateSTARODK` added OR documented immutability.
  - [ ] Finding 8: `getComposite()` path/backend reconciled.
  - [ ] Finding 9: `useWallets` uses `oasis.api.listWallets`.
  - [ ] All fixes have a test (unit or vitest) where applicable.
  - [ ] SDK build + .NET build green.
  - [ ] No `Buffer` / `btoa` / `atob` / `window` / `document` references introduced into `sdk/oasis-wallet/src/`.
  - [ ] `tracks.md` row for this track moves to `[x]` Shipped.
- `## Out of scope` — Refactors. New features. Coverage holes from W4. Frontend typecheck (per memory).
- `## Tier` — Tier 0.5 (pre-launch hygiene).
- `## Dependencies` — None. Independent of api-safety-hardening, surrealdb-migration.

#### 1.3 Write `conductor/tracks/self-audit-one-fix/plan.md`

Plan outline (target ~80-150 lines), 3 phases:

- `# Self Audit One Fix — Plan`
- `## Phase 1 — SDK fixes`
  - Cite spec findings 1, 3, 5, 6, 7, 8.
  - `[ ]` task per file edit.
  - Build gate: `cd sdk/oasis-wallet && pnpm build && pnpm test`.
- `## Phase 2 — Frontend fixes`
  - Cite spec findings 2, 4, 9.
  - `[ ]` task per file edit.
  - No frontend typecheck (per memory).
- `## Phase 3 — Verification`
  - `[ ]` SDK build + 82 SDK tests green.
  - `[ ]` .NET build + 503 unit tests green.
  - `[ ]` Grep `sdk/oasis-wallet/src/` for `Buffer`, `btoa`, `atob`, `window`, `document` — no hits.
  - `[ ]` Move tracks.md row to Shipped.

### Phase 2 — Track 2: `surreal-schema-package-retro`

**Goal:** Retrospective + as-built reference for what replaced `surrealdb-schema-source-gen`. Knowledge capture, not implementation.

#### 2.1 Verify cited surfaces

- [ ] `Read` `packages/Oasis.SurrealDb.Client/Schema/SurrealAttributes.cs` and confirm `[SurrealTable]`, `[SurrealIndex]`, `[SurrealRelation]` exist.
- [ ] `Read` `packages/Oasis.SurrealDb.Schema/Generator/AttributeSchemaScanner.cs` and confirm class shape.
- [ ] `Read` `packages/Oasis.SurrealDb.Schema/Generator/SurqlEmitter.cs` (head).
- [ ] `Read` `packages/Oasis.SurrealDb.Schema/Generator/MermaidFlowchartEmitter.cs` (head).
- [ ] `Glob` `Persistence/SurrealDb/Models/*.cs` — confirm 26 POCOs (per W3).
- [ ] `Glob` `Persistence/SurrealDb/Generated/Schemas/*.surql` — confirm 26.
- [ ] `Glob` `Persistence/SurrealDb/Generated/Flowcharts/*.flowchart.mermaid` — confirm 7.
- [ ] `Read` `packages/Oasis.SurrealDb.Schema/Migration/MigrationRunner.cs` (head) for CLI command set.
- [ ] `Grep` `AttributePocoByteEquivalenceTests` to locate test class.
- [ ] `Read` `RUNBOOK.md` §4 and §8 to capture exact stale references.
- [ ] `Read` `conductor/tracks/surrealdb-schema-source-gen/spec.md` to capture the old description being superseded.
- [ ] Acceptance: all citations verified present.

#### 2.2 Write `conductor/tracks/surreal-schema-package-retro/spec.md`

Spec outline (target ~200-400 lines):

- `# Surreal Schema Package Retro — Specification`
- `## Goal` — Retro + as-built reference for the C#-first schema pipeline that replaced `surrealdb-schema-source-gen` on 2026-06-03. Closes by being absorbed into `Persistence/SurrealDb/CONVENTION.md`.
- `## Background`
  - What the old pipeline was: Roslyn IIncrementalGenerator reading `.mermaid` files in `Persistence/SurrealDb/Schemas/source/`, emitting C# POCOs + typed `SurrealQuery<T>` + `RecordId<T>`. Wave 1 of the toolkit family. Spec described 889+ tests and ongoing FK-emission work.
  - What changed on 2026-06-03: pipeline inverted. `.mermaid` source dir deleted. `Oasis.SurrealDb.SourceGen` package emptied (and being removed by Lane C). New flow: decorated POCOs in `Persistence/SurrealDb/Models/` → `AttributeSchemaScanner` → `SurqlEmitter` + `MermaidFlowchartEmitter` → 26 generated `.surql` files in `Persistence/SurrealDb/Generated/Schemas/`.
- `## Retrospective — why Mermaid-first didn't work` (synthesize from W3 + reading):
  - Authoring friction: maintaining `.mermaid` in parallel with consuming C# code was a constant double-edit.
  - Parser ownership: hand-rolled Mermaid parser is a non-trivial dependency; the C# attribute surface is free (Roslyn already has it).
  - Tooling: Roslyn IIncrementalGenerator is built for in-IDE feedback on C# inputs — running over a non-C# input file fought the model.
  - Drift risk: `.surql` could drift from POCO if the generator failed silently.
  - Greenfield advantage: no customers / no live data (memory `greenfield-prelaunch-no-compat`) made the pivot cheap.
- `## As-built architecture (authoritative)`
  - Attributes: `packages/Oasis.SurrealDb.Client/Schema/SurrealAttributes.cs` defines `[SurrealTable(name, schemafull, aggregate)]`, `[SurrealIndex]`, `[SurrealRelation]`.
  - POCOs: `Persistence/SurrealDb/Models/*.cs` — 26 as of 2026-06-10.
  - Scanner: `packages/Oasis.SurrealDb.Schema/Generator/AttributeSchemaScanner.cs` reflects POCOs at runtime via `oasis-surreal generate-from-assembly`.
  - Emitters: `SurqlEmitter.cs` (one `.surql` per table) + `MermaidFlowchartEmitter.cs` (one `.flowchart.mermaid` per slice + master).
  - Outputs: `Persistence/SurrealDb/Generated/Schemas/*.surql` (26) + `Persistence/SurrealDb/Generated/Flowcharts/*.flowchart.mermaid` (7).
  - Acceptance gate: `AttributePocoByteEquivalenceTests` replaces the old generator suite.
  - Migration runner: `packages/Oasis.SurrealDb.Schema/Migration/MigrationRunner.cs` + CLI `migrate up | status | dry-run | reset`. `migrate down` intentionally stubbed (manual rollback pre-launch).
- `## Where this lives in the codebase`
  - Client + Schema + Analyzer packages remain.
  - Retired SourceGen package removed (Lane C).
  - `surrealql-toolkit` umbrella + 4 siblings archived (Lane B).
- `## Lessons / principles`
  1. Greenfield + no users → invert pipelines early, don't wrap them.
  2. Source of truth lives where authoring naturally happens — C# code here, not diagrams.
  3. Visualisation output is fine; visualisation input is a trap when the consumer is code.
  4. A Roslyn analyzer (SRDB0001) catches one specific failure mode (string-interpolated SurrealQL) — keep this; it's orthogonal to schema-gen.
- `## Acceptance criteria` — `[ ]` checkboxes:
  - [ ] `Persistence/SurrealDb/CONVENTION.md` exists and absorbs the "as-built architecture" section so future contributors don't need to read the retro to understand the system.
  - [ ] `tracks.md` `surrealdb-schema-source-gen` row links to this retro.
  - [ ] `RUNBOOK.md` §4 and §8 references to deleted Mermaid source dir + SourceGen package are updated to point at the C#-first surface (W5 audit enumerates specific stale references).
  - [ ] No code in the repo references `.mermaid` source files or the SourceGen package once this track closes.
- `## Out of scope` — Re-litigating the pivot. New schema features. Documentation work only.
- `## Tier` — Tier 1.6 (matching `surrealdb-schema-source-gen`).
- `## Dependencies` — Lane B (toolkit archive) and Lane C (SourceGen package removal) should close first or in parallel; this track absorbs their cleanup into the canonical doc.

#### 2.3 Write `conductor/tracks/surreal-schema-package-retro/plan.md`

Plan outline (target ~80-150 lines), 3 phases:

- `# Surreal Schema Package Retro — Plan`
- `## Phase 1 — Draft Persistence/SurrealDb/CONVENTION.md`
  - `[ ]` Create CONVENTION.md if missing.
  - `[ ]` Absorb "As-built architecture" section from spec.md.
  - `[ ]` Include POCO authoring example (`[SurrealTable]` decoration walkthrough).
  - `[ ]` Include `oasis-surreal generate-from-assembly` invocation reference.
- `## Phase 2 — Audit RUNBOOK.md`
  - `[ ]` Read RUNBOOK §4 and §8.
  - `[ ]` Replace stale references to `Persistence/SurrealDb/Schemas/source/*.mermaid` with the C#-first surface.
  - `[ ]` Replace `Oasis.SurrealDb.SourceGen` package references with `Oasis.SurrealDb.Schema`.
- `## Phase 3 — Cross-link superseded track`
  - `[ ]` Add SUPERSEDED banner at top of `conductor/tracks/surrealdb-schema-source-gen/spec.md` pointing at this retro.
  - `[ ]` Grep repo for `.mermaid` source references; resolve or document.
  - `[ ]` Move `tracks.md` row to Shipped.

### Phase 3 — `tracks.md` append

#### 3.1 Identify insertion point

- [ ] `Read` `conductor/tracks.md` Pending section.
- [ ] Locate the `data-backfill-migrations` row.
- [ ] Confirm the `surrealql-toolkit` row immediately follows (Lane B will delete this row; placing the new rows AFTER `data-backfill-migrations` and BEFORE `surrealql-toolkit` means they survive Lane B's deletion).

#### 3.2 Append two rows

- [ ] Append exact rows (pipe-delimited single line) AFTER `data-backfill-migrations`:

```
| [self-audit-one-fix](tracks/self-audit-one-fix/spec.md) | `[ ]` | **Tier 0.5 — pre-launch hygiene.** Bundles 9 small code bugs caught by the 2026-06-10 audit (Jupiter `Buffer.from` cross-platform violation, broken swap page, Tinyman decimal hardcode, settings page type bug, Algorand msgpack gap, missing SDK methods). One fix per finding; no scope creep. See `.omc/audit/AUDIT-SUMMARY.md`. |
| [surreal-schema-package-retro](tracks/surreal-schema-package-retro/spec.md) | `[ ]` | **Tier 1.6 — knowledge capture.** Retro + as-built reference for the C#-first schema pipeline that replaced `surrealdb-schema-source-gen` on 2026-06-03 (Mermaid→POCO deleted; `AttributeSchemaScanner` + `SurqlEmitter` in `Oasis.SurrealDb.Schema` is authoritative). Closes by absorbing into `Persistence/SurrealDb/CONVENTION.md` + fixing stale RUNBOOK §4/§8 references. |
```

- [ ] Do NOT touch any other row.
- [ ] Do NOT touch the Shipped section.
- [ ] Do NOT delete the surrealql-* rows (Lane B owns those).

### Phase 4 — Verification

- [ ] `Glob` the five paths and confirm all exist.
- [ ] `Read` each spec.md and plan.md; confirm `[ ]` checkboxes present and section structure matches `api-safety-hardening/spec.md`.
- [ ] `Grep` for any newly introduced file:line citations and verify each is real with a quick `Read`.
- [ ] `git diff conductor/tracks.md` — confirm exactly 2 lines added, 0 removed.
- [ ] Confirm no files outside the five enumerated paths were modified.

---

## Commit Strategy

Single commit at end of track:

```
docs(tracks): author self-audit-one-fix + surreal-schema-package-retro tracks

- Capture 9 small SDK/frontend code bugs from 2026-06-10 audit as Tier 0.5
  closable track (self-audit-one-fix).
- Capture C#-first schema pipeline retro + as-built reference for what
  replaced surrealdb-schema-source-gen on 2026-06-03 (surreal-schema-package-retro).
- Append two Pending rows to tracks.md (placed before surrealql-toolkit so
  Lane B's deletion does not affect them).
```

---

## Success Criteria

- All five files exist and are coherent prose.
- All `file:line` citations verified present at commit time.
- `tracks.md` diff is +2 lines, 0 deletions.
- No files outside the enumerated set are touched.
- Spec voice matches `api-safety-hardening/spec.md` calibration.

---

## Handoff

This plan is ready for an executor. Run:

`/oh-my-claudecode:start-work autopilot-lane-d-tracks`

The executor will spawn against this plan, perform the file writes, and run Phase 4 verification before reporting back.
