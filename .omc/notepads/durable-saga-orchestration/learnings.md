# durable-saga-orchestration — Phase 1 learnings

## Module location / namespace
- Code: `Services/Sagas/` namespace `OASIS.WebAPI.Sagas` (mirrors `Services/Reconciliation/`).
- Entity: `Models/Sagas/SagaStepRecord.cs` namespace `OASIS.WebAPI.Models.Sagas`.
- Tests: `tests/OASIS.WebAPI.Tests/Sagas/`.

## Reused primitives (NOT reinvented)
- Exactly-once: handlers gate irreversible effects on the existing
  `IIdempotencyStore` via a STABLE per-step key `saga:{correlation}:{stepName}`
  (`SagaKeys`). No second idempotency mechanism.
- Single-winner: `ExecuteUpdateAsync … WHERE Id==id AND Status==Pending AND
  NextRunAt<=now` + assert exactly one row — identical discipline to
  `ReconciliationService` / `IdempotencyStore`. The conditional predicate (NOT
  the optimistic xmin token) is the arbiter ⇒ identical guarantee on
  PostgreSQL + SQLite.
- Scope-per-operation: `EfSagaStore` resolves its own `OASISDbContext` per call
  via `IServiceScopeFactory` (same pattern as `IdempotencyStore`).
- Hosted shape: `SagaProcessorHostedService` generalizes
  `ReconciliationHostedService` (per-tick scope, options-bound, crash-safe,
  honors stopping token). Timing isolated in the swappable
  `ISagaTrigger`/`PollingSagaTrigger` (SurrealDB LIVE-query later = 1 class).

## Gotchas
- `SqliteTestDbContext` ONLY remapped `BridgeTransactionResult.Version`
  (xmin/xid has no SQLite equiv). A new xmin-mapped entity MUST be added there
  too — added `SagaStepRecord.Version` remap or SQLite EnsureCreated fails.
- Generic continuation: the saga-instance PAYLOAD flows unchanged through
  forward steps. Do NOT seed the next step's payload with the prev step's
  output (that consumer-specific coupling broke JSON deserialization of the
  next step). Output is recorded per-record for observability only.
- Full-jitter backoff is uniform in [0, ceiling] — a test asserting a
  MINIMUM delay (or "not due now") after one failure is INHERENTLY FLAKY
  (jitter can legitimately be ~1ms). Assert: moved forward + within the
  attempt-N ceiling; prove growth by comparing the ceiling across attempts.

## Migration
- `dotnet ef migrations add AddSagaOutbox --project OASIS.WebAPI.csproj` from
  repo root worked without needing to kill stale dotnet (no OASIS.WebAPI host
  was running; MSBuild/VBCSCompiler dotnet.exe did not lock the dll).
- Result `Migrations/20260518003457_AddSagaOutbox.cs` is purely additive:
  Up = CreateTable("SagaSteps") + 4 indexes; Down = DropTable. Stacks on
  20260516224425. No existing table altered.

## Verification baseline
- Pre-existing: 17 WebAPI build warnings; 537 unit tests green.
- After Phase 1: 17 warnings (0 new), 547 tests green (537 + 10 new), saga +
  idempotency + reconciliation classes non-flaky 3x.
- NO bridge/wormhole/crosschain/reconciliation source file modified.
