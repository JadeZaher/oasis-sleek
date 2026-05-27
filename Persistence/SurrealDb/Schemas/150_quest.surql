-- ============================================================
-- Table: quest
-- Aggregate: Quest (Models/Quest/Quest.cs)
-- Guardrail: G6 SCHEMAFULL; definition-side workflow shape.
-- Note: Runtime state (status / completed_date) lives on quest_run --
--       see SURREAL-SCHEMA-HINTS.md §1 "Removed from quest". The Quest
--       definition itself is shape-only and immutable in-flight; one
--       Quest definition can have many QuestRun attempt rows (fork tree).
-- Note: nodes / edges / dependencies live in their own tables (quest_node,
--       quest_edge, quest_dependency) keyed by quest_id -- they are queried
--       independently of the parent and mutated through Add/Remove endpoints.
--       Embedding them would force the controller-side node/edge CRUD
--       endpoints to read-modify-write the entire quest row.
-- Note: metadata is a free-form string->string map for caller-supplied tags
--       (e.g. UI color, sort hint). Persisted as a SurrealDB object so
--       arbitrary keys remain queryable.
-- Source: SURREAL-SCHEMA-HINTS.md §1 (verbatim, materialised here per the
--         surrealdb-migration task 9 hand-off contract).
-- ============================================================

DEFINE TABLE quest SCHEMAFULL;

-- Core identity (record id is the Guid('N') of Quest.Id)
DEFINE FIELD id ON TABLE quest TYPE string
    ASSERT $value != NONE AND $value != "";

-- Owner avatar (Guid('N') hex)
DEFINE FIELD avatar_id ON TABLE quest TYPE string
    ASSERT $value != NONE AND $value != "";

-- Caller-supplied label
DEFINE FIELD name ON TABLE quest TYPE string
    ASSERT $value != NONE AND $value != "";

-- Optional free-text description
DEFINE FIELD description ON TABLE quest TYPE option<string>;

-- Source template id when instantiated from a QuestTemplate
-- (null for hand-authored quests)
DEFINE FIELD template_id ON TABLE quest TYPE option<string>;

-- Owning DappSeries when this quest is part of a composed dApp
-- (null for standalone quests)
DEFINE FIELD dapp_series_id ON TABLE quest TYPE option<string>;

-- Free-form caller-supplied metadata (string -> string map)
DEFINE FIELD metadata ON TABLE quest FLEXIBLE TYPE object;

-- Embedding vector for HNSW semantic search (384-dimensional, MiniLM-style)
DEFINE FIELD embedding ON TABLE quest TYPE option<array<float, 384>>;

-- Definition birthdate -- STAYS on the definition, not a runtime artifact
DEFINE FIELD created_date ON TABLE quest TYPE datetime;

-- ── Indexes ──────────────────────────────────────────────────

DEFINE INDEX quest_by_avatar
    ON TABLE quest
    FIELDS avatar_id;

DEFINE INDEX quest_by_template
    ON TABLE quest
    FIELDS template_id;

DEFINE INDEX quest_by_dapp_series
    ON TABLE quest
    FIELDS dapp_series_id;
