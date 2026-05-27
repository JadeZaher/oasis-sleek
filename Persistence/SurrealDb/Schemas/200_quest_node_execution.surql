-- ============================================================
-- Table: quest_node_execution
-- Aggregate: QuestNodeExecution (Models/Quest/QuestNodeExecution.cs)
-- Guardrail: G6 SCHEMAFULL; G2 exactly-once claim primitive
--            (api-safety-hardening). One row per (run, node).
-- Note: Per-(run, node) execution row. Replaces the in-place mutation of
--       QuestNode.State / Output / Error that prevented re-runs from
--       preserving the prior attempt's outputs.
-- Note: On a fork, the same execution row can be referenced by both parent
--       and child run via the `executes` RELATE edge (defined in
--       230_quest_graph_edges.surql §6.2) for nodes whose
--       execution_order < forkPoint (copy-by-reference, no duplication).
--       Scalar run_id is the originating run; cross-run reference happens
--       through the RELATE edge.
-- Note: G2 claim primitive (TryClaimPendingAsync) is a conditional UPDATE
--       that only succeeds when current state == "Pending":
--           UPDATE quest_node_execution
--              SET state = 'Running', started_at = time::now()
--              WHERE run_id = $run_id
--                AND node_id = $node_id
--                AND state = 'Pending'
--              RETURN AFTER;
--       Empty-result case = lost race (already claimed by another worker).
--       The (run_id, node_id) UNIQUE index is what makes the claim atomically
--       safe -- a concurrent CREATE for the same (run, node) pair fails the
--       constraint rather than producing a duplicate row.
-- Note: output stores the JSON-serialised OASISResult<T> from the handler
--       call. error carries the failure message when state = "Failed". Both
--       are null in non-terminal states.
-- Source: SURREAL-SCHEMA-HINTS.md §5 (verbatim hand-off, including the
--         conditional-UPDATE claim primitive at lines 228-237).
-- ============================================================

DEFINE TABLE quest_node_execution SCHEMAFULL;

-- Execution row identity (record id is the Guid('N') of QuestNodeExecution.Id).
DEFINE FIELD id ON TABLE quest_node_execution TYPE string
    ASSERT $value != NONE AND $value != "";

-- Owning run (originating run; cross-run references happen via the
-- `executes` RELATE edge defined in 230_quest_graph_edges.surql).
DEFINE FIELD run_id ON TABLE quest_node_execution TYPE string
    ASSERT $value != NONE AND $value != "";

-- Quest definition node this execution corresponds to (Guid('N') hex of QuestNode.Id).
DEFINE FIELD node_id ON TABLE quest_node_execution TYPE string
    ASSERT $value != NONE AND $value != "";

-- QuestNodeState enum name.
DEFINE FIELD state ON TABLE quest_node_execution TYPE string
    ASSERT $value INSIDE ["Pending", "Running", "Succeeded", "Failed", "Skipped", "Cancelled"];

-- Serialised OASISResult<T> from the handler call (null until Succeeded).
DEFINE FIELD output ON TABLE quest_node_execution TYPE option<string>;

-- Failure message when state is "Failed".
DEFINE FIELD error ON TABLE quest_node_execution TYPE option<string>;

-- Wall-clock time at which the row entered "Running".
DEFINE FIELD started_at ON TABLE quest_node_execution TYPE datetime;

-- Wall-clock time at which the row reached a terminal state
-- (null while non-terminal).
DEFINE FIELD ended_at ON TABLE quest_node_execution TYPE option<datetime>;

-- ── Indexes ──────────────────────────────────────────────────

-- Natural key: one row per (run, node) -- exact-match lookup and the
-- atomic-safety anchor for the G2 conditional-claim primitive.
DEFINE INDEX quest_node_execution_run_node
    ON TABLE quest_node_execution
    FIELDS run_id, node_id
    UNIQUE;

DEFINE INDEX quest_node_execution_by_run
    ON TABLE quest_node_execution
    FIELDS run_id;

DEFINE INDEX quest_node_execution_by_node
    ON TABLE quest_node_execution
    FIELDS node_id;

DEFINE INDEX quest_node_execution_by_state
    ON TABLE quest_node_execution
    FIELDS state;
