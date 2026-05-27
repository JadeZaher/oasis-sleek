-- ============================================================
-- Table: quest_edge
-- Aggregate: QuestEdge (Models/Quest/QuestEdge.cs)
-- Guardrail: G6 SCHEMAFULL; definition-side directed control-flow edge
--            between two quest_node rows in the same quest.
-- Note: Conservative scalar form (per SURREAL-SCHEMA-HINTS.md §3). The
--       optional native `RELATE source_node -> control_flow -> target_node`
--       alternative is intentionally NOT taken in this round so QuestDagValidator's
--       iterative scalar-field queries remain unchanged. A future surrealdb-migration
--       round may promote to a native edge for cheaper graph traversal; either shape
--       preserves intra-iteration DAG semantics.
-- Note: condition is populated only when edge_type = "Conditional"; the
--       ConditionNodeHandler evaluates the expression against the upstream
--       node's serialised output to gate edge activation.
-- Source: SURREAL-SCHEMA-HINTS.md §3 (verbatim hand-off, scalar form).
-- ============================================================

DEFINE TABLE quest_edge SCHEMAFULL;

-- Core identity (record id is the Guid('N') of QuestEdge.Id)
DEFINE FIELD id ON TABLE quest_edge TYPE string
    ASSERT $value != NONE AND $value != "";

-- Owning quest (Guid('N') hex of the parent Quest.Id)
DEFINE FIELD quest_id ON TABLE quest_edge TYPE string
    ASSERT $value != NONE AND $value != "";

-- Upstream node (Guid('N') hex of the source QuestNode.Id)
DEFINE FIELD source_node_id ON TABLE quest_edge TYPE string
    ASSERT $value != NONE AND $value != "";

-- Downstream node (Guid('N') hex of the target QuestNode.Id)
DEFINE FIELD target_node_id ON TABLE quest_edge TYPE string
    ASSERT $value != NONE AND $value != "";

-- Optional condition expression (only with edge_type = "Conditional").
DEFINE FIELD condition ON TABLE quest_edge TYPE option<string>;

-- QuestEdgeType enum name.
DEFINE FIELD edge_type ON TABLE quest_edge TYPE string
    ASSERT $value INSIDE ["Control", "Conditional"];

-- ── Indexes ──────────────────────────────────────────────────

DEFINE INDEX quest_edge_by_quest
    ON TABLE quest_edge
    FIELDS quest_id;

DEFINE INDEX quest_edge_by_source
    ON TABLE quest_edge
    FIELDS source_node_id;

DEFINE INDEX quest_edge_by_target
    ON TABLE quest_edge
    FIELDS target_node_id;
