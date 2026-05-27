-- ============================================================
-- Tables: forked_from + executes  (native RELATION edges)
-- Layer:  Graph (between quest_run and quest_node_execution)
-- Guardrail: G6 SCHEMAFULL; G2 -- the `executes` edge is the only way to
--            express the many-to-many copy-by-reference fork semantic
--            without an intermediate join table.
-- Source: SURREAL-SCHEMA-HINTS.md §6 (verbatim hand-off).
-- ============================================================

-- ============================================================
-- 6.1 `forked_from` -- lineage edge (quest_run -> quest_run)
-- ============================================================
-- Mirrors the scalar parent_run_id on quest_run for cheap multi-hop ancestor
-- walks like `->forked_from->forked_from->...`. The scalar field is the
-- AUTHORITATIVE pointer; the RELATE edge is the optimisation for native
-- graph traversal. Both must be kept in sync at write time -- every fork
-- creates the child quest_run row AND a forked_from edge in the same
-- multi-statement request.
--
-- No extra fields on the edge -- the scalar `parent_run_id` carries the
-- same information for non-graph queries.
-- ============================================================

DEFINE TABLE forked_from SCHEMAFULL TYPE RELATION FROM quest_run TO quest_run;

-- ============================================================
-- 6.2 `executes` -- copy-by-reference for fork pre-history
-- ============================================================
-- Carrier of the "copy-by-reference for nodes with execution_order < forkPoint"
-- semantic (ADR §2.3).
--
-- Write contract:
--   1. When a node execution is first created for a run,
--      RELATE run -> executes -> exec is created alongside the row.
--   2. When a fork happens, for every quest_node_execution where
--      node_id.execution_order < forkPoint, an additional
--      RELATE child_run -> executes -> exec is created. The exec row is
--      NOT duplicated.
--
-- Why a RELATE edge instead of a scalar: the relationship is many-to-many
-- (one execution row, many runs that reference it) and the native graph form
-- is the only way to express that without an intermediate join table.
-- Scalar run_id on quest_node_execution would force duplication on fork.
-- ============================================================

DEFINE TABLE executes SCHEMAFULL TYPE RELATION FROM quest_run TO quest_node_execution;
