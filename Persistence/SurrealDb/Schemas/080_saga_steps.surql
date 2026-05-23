-- ============================================================
-- Table: saga_steps
-- Aggregate: SagaStepRecord (Models/Sagas/SagaStepRecord.cs)
-- Guardrail: G6 SCHEMAFULL, G2 conditional-claim single-winner primitive
-- Note: Durable transactional-outbox / saga-step record. One row == one step of one saga instance.
--       The step processor only ever asks "what is due?" and claims a row with a conditional UPDATE --
--       the proven api-safety-hardening single-winner primitive.
-- Note: G2 single-winner claim:
--       UPDATE saga_steps:<id> SET status='InProgress', claimed_at=now
--       WHERE status='Pending' AND next_run_at<=now RETURN AFTER.
--       Under N concurrent processors at most one row mutates (AffectedCount==1); the rest see zero
--       affected and back off. The conditional predicate (NOT optimistic-concurrency) is the arbiter,
--       so behaviour is identical on Postgres, SQLite, and SurrealDB.
-- Note: Crash-safe re-entry -- an InProgress row whose claimed_at is older than the lease/visibility
--       timeout is reclaimed atomically:
--       UPDATE saga_steps SET status='Pending', claimed_at=NONE
--       WHERE status='InProgress' AND claimed_at < lease_cutoff.
--       Handlers key irreversible effects on step_idempotency_key (via IIdempotencyStore) so a resumed
--       step is an idempotent replay, never a double-run.
-- Note: Generic by construction -- no bridge/domain types appear here. saga_name/step_name are free
--       strings and payload is opaque JSON. The bridge becomes one consumer with zero schema change.
-- Note: step_idempotency_key is NON-unique here -- dedup of irreversible effects happens in handlers
--       via IIdempotencyStore, NOT via a unique constraint (the outbox legitimately holds many rows
--       per correlation: forward steps + a compensation step + retries).
-- ============================================================

DEFINE TABLE saga_steps SCHEMAFULL;

-- Core identity
DEFINE FIELD id ON TABLE saga_steps TYPE string
    ASSERT $value != NONE AND $value != "";

-- Instance correlation key (NON-unique -- many rows per instance: forwards + compensation + retries)
DEFINE FIELD correlation_key ON TABLE saga_steps TYPE string
    ASSERT $value != NONE AND $value != "";

-- Saga definition + step name (free strings -- generic outbox)
DEFINE FIELD saga_name ON TABLE saga_steps TYPE string
    ASSERT $value != NONE AND $value != "";
DEFINE FIELD step_name ON TABLE saga_steps TYPE string
    ASSERT $value != NONE AND $value != "";

-- Per-step idempotency key (handler keys irreversible effect on THIS value via IIdempotencyStore --
-- stable across retries/reclaims)
DEFINE FIELD step_idempotency_key ON TABLE saga_steps TYPE string
    ASSERT $value != NONE AND $value != "";

-- Opaque payload (serialized typed step input)
DEFINE FIELD payload ON TABLE saga_steps TYPE string;

-- Lifecycle state (StepStatus enum) -- drives the G2 conditional claim
DEFINE FIELD status ON TABLE saga_steps TYPE string
    ASSERT $value INSIDE ["Pending", "InProgress", "Completed", "Compensating", "DeadLettered"]
    DEFAULT "Pending";

-- Compensation flag (forward steps dead-letter on exhaustion, compensation steps dead-letter immediately)
DEFINE FIELD is_compensation ON TABLE saga_steps TYPE bool
    DEFAULT false;

-- Attempts consumed so far
DEFINE FIELD attempt_count ON TABLE saga_steps TYPE int
    DEFAULT 0;

-- Earliest UTC time the step may be claimed (pushed out by backoff on retry)
DEFINE FIELD next_run_at ON TABLE saga_steps TYPE datetime;

-- Lease/visibility-timeout tracking (NONE when not claimed)
DEFINE FIELD claimed_at ON TABLE saga_steps TYPE option<datetime>;

-- Diagnostics
DEFINE FIELD last_error ON TABLE saga_steps TYPE option<string>;
DEFINE FIELD output ON TABLE saga_steps TYPE option<string>;

-- Mirror of status==DeadLettered for cheap querying
DEFINE FIELD dead_lettered ON TABLE saga_steps TYPE bool
    DEFAULT false;

-- Timestamps
DEFINE FIELD created_at ON TABLE saga_steps TYPE datetime;
DEFINE FIELD updated_at ON TABLE saga_steps TYPE datetime;

-- ── Indexes ──────────────────────────────────────────────────

-- Saga-instance lookup (non-unique -- many rows per instance)
DEFINE INDEX saga_steps_correlation_key
    ON TABLE saga_steps
    FIELDS correlation_key;

-- "Due steps" hot-path composite (G2 claim scan: status==Pending AND next_run_at<=now)
DEFINE INDEX saga_steps_due_scan
    ON TABLE saga_steps
    FIELDS status, next_run_at;

-- Stale-lease reclaim composite (status==InProgress AND claimed_at < lease_cutoff)
DEFINE INDEX saga_steps_lease_scan
    ON TABLE saga_steps
    FIELDS status, claimed_at;

-- Per-step idempotency-key lookup (non-unique by design -- dedup happens in IIdempotencyStore)
DEFINE INDEX saga_steps_idempotency_key
    ON TABLE saga_steps
    FIELDS step_idempotency_key;
