-- ============================================================
-- Table: quest_run
-- Aggregate: QuestRun (Models/Quest/QuestRun.cs)
-- Guardrail: G6 SCHEMAFULL; runtime canonical lineage carrier.
--            One row per execution attempt of a quest.
-- Note: Replaces the in-place mutation of Quest.Status that prevented re-runs
--       from preserving prior attempt state -- see quest-temporal-fork-model
--       ADR §1. Re-running a Succeeded quest creates a NEW root run
--       (parent_run_id = null); forking a Running run creates a CHILD run
--       with parent_run_id set.
-- Note: Lineage forms a TREE via parent_run_id, not a DAG. The forked_from
--       RELATE edge (defined in 230_quest_graph_edges.surql §6.1) mirrors
--       parent_run_id for native graph-traversal queries. Both must be kept
--       in sync at write time (write contract: every fork CREATEs the
--       quest_run row AND RELATEs child -> forked_from -> parent in the
--       same multi-statement request).
-- Note: State-machine invariants are enforced in QuestManager, not in the DB:
--         Pending -> Running    (first node claim succeeds)
--         Running -> Succeeded  (all terminal nodes report Succeeded)
--         Running -> Failed     (any node reports Failed, or supervisor calls
--                                MarkRunFailedAsync with fail_reason)
--         Running -> Forked     (fork API succeeds; parent transitions to
--                                terminal "Forked"; child created "Pending")
--         Running -> Cancelled  (explicit cancellation)
--       Succeeded / Failed / Forked / Cancelled are TERMINAL.
-- Note: fail_reason carries the supervisor-driven audit string; internal-error
--       failures leave fail_reason null and write the error on the failed
--       quest_node_execution row instead.
-- Source: SURREAL-SCHEMA-HINTS.md §4 (verbatim hand-off).
-- ============================================================

DEFINE TABLE quest_run SCHEMAFULL;

-- Run identity (record id is the Guid('N') of QuestRun.Id) -- unique across
-- all runs of all quests.
DEFINE FIELD id ON TABLE quest_run TYPE string
    ASSERT $value != NONE AND $value != "";

-- Quest definition this run executes (Guid('N') hex of Quest.Id).
DEFINE FIELD quest_id ON TABLE quest_run TYPE string
    ASSERT $value != NONE AND $value != "";

-- Avatar that initiated this run (denormalised for query convenience).
DEFINE FIELD avatar_id ON TABLE quest_run TYPE string
    ASSERT $value != NONE AND $value != "";

-- QuestRunStatus enum name.
DEFINE FIELD status ON TABLE quest_run TYPE string
    ASSERT $value INSIDE ["Pending", "Running", "Succeeded", "Failed", "Forked", "Cancelled"];

-- Wall-clock time at which the run row was created.
DEFINE FIELD started_at ON TABLE quest_run TYPE datetime;

-- Wall-clock time at which the run reached a terminal state
-- (null while non-terminal).
DEFINE FIELD ended_at ON TABLE quest_run TYPE option<datetime>;

-- ── Lineage (tree -- see ADR §2.4) ──────────────────────────

-- Parent run id if this run was forked from another; null for root runs
-- (initial executions and re-runs).
DEFINE FIELD parent_run_id ON TABLE quest_run TYPE option<string>;

-- Node at which the fork occurred. Set iff this is a child fork run.
-- Nodes with ExecutionOrder < forkPoint.ExecutionOrder are inherited by
-- reference from the parent's quest_node_execution rows via the `executes`
-- RELATE edge (no recompute, no duplication).
DEFINE FIELD forked_at_node_id ON TABLE quest_run TYPE option<string>;

-- Free-form audit reason supplied when the fork was triggered.
DEFINE FIELD fork_reason ON TABLE quest_run TYPE option<string>;

-- ── Supervisor-driven failure audit ─────────────────────────

-- Free-form audit reason when a supervisor explicitly marked the run failed
-- (distinct from internal-error path which leaves this null and writes the
-- failure on the per-node execution row instead).
DEFINE FIELD fail_reason ON TABLE quest_run TYPE option<string>;

-- ── Indexes ──────────────────────────────────────────────────

DEFINE INDEX quest_run_by_quest
    ON TABLE quest_run
    FIELDS quest_id;

DEFINE INDEX quest_run_by_avatar
    ON TABLE quest_run
    FIELDS avatar_id;

DEFINE INDEX quest_run_by_status
    ON TABLE quest_run
    FIELDS status;

DEFINE INDEX quest_run_by_parent
    ON TABLE quest_run
    FIELDS parent_run_id;
