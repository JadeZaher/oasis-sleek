-- ============================================================
-- Table: quest_node
-- Aggregate: QuestNode (Models/Quest/QuestNode.cs)
-- Guardrail: G6 SCHEMAFULL; definition-side step inside a quest DAG.
-- Note: Each row is one step inside a quest DAG. config is a JSON-serialized
--       request model (e.g. HolonCreateModel, NftMintRequest) that the
--       matching IQuestNodeHandler deserializes at execution time. node_type
--       asserts INSIDE the full QuestNodeType enum so a schema-drift typo is
--       rejected at INSERT time rather than at the dispatch layer. Keep the
--       enum value list in lockstep with Models/Quest/QuestEnums.cs:QuestNodeType.
-- Note: is_entry / is_terminal are derived from the edge graph (an entry
--       node has no incoming control edges) and cached on the row for fast
--       quest-start lookup. execution_order is the topological position
--       computed by QuestDagValidator on activation; the (quest_id,
--       execution_order) composite index is used by the executor to walk
--       nodes in dependency order.
-- Note: state / output / error were intentionally removed when
--       quest-temporal-fork-model split per-run state into
--       quest_node_execution -- see SURREAL-SCHEMA-HINTS.md §2 "Removed
--       from quest_node".
-- Source: SURREAL-SCHEMA-HINTS.md §2 (verbatim hand-off).
-- ============================================================

DEFINE TABLE quest_node SCHEMAFULL;

-- Core identity (record id is the Guid('N') of QuestNode.Id)
DEFINE FIELD id ON TABLE quest_node TYPE string
    ASSERT $value != NONE AND $value != "";

-- Owning quest (Guid('N') hex of the parent Quest.Id)
DEFINE FIELD quest_id ON TABLE quest_node TYPE string
    ASSERT $value != NONE AND $value != "";

-- Reusable node template this node was instantiated from
-- (null for hand-authored nodes)
DEFINE FIELD node_template_id ON TABLE quest_node TYPE option<string>;

-- QuestNodeType enum name (e.g. HolonCreate, NftMint). Keep in lockstep
-- with Models/Quest/QuestEnums.cs:QuestNodeType.
DEFINE FIELD node_type ON TABLE quest_node TYPE string
    ASSERT $value INSIDE [
        "HolonCreate", "HolonUpdate", "HolonDelete", "HolonGet", "HolonQuery",
        "HolonInteract", "HolonGetChildren", "HolonGetPeers", "HolonGetAncestors",
        "HolonGetDescendants", "HolonPropagate", "HolonCompose", "HolonClone",
        "HolonMoveSubtree",
        "NftMint", "NftTransfer", "NftBurn", "NftGet", "NftQuery", "NftGetMetadata",
        "WalletCreate", "WalletUpdate", "WalletDelete", "WalletGet", "WalletQuery",
        "WalletSetDefault", "WalletGetPortfolio",
        "StarGenerate", "StarDeploy",
        "Search", "AvatarNFTGetComposite", "BlockchainExecute",
        "Condition", "ComposeOutputs"
    ];

-- Caller-supplied label
DEFINE FIELD name ON TABLE quest_node TYPE string;

-- Node-specific JSON config blob (string-encoded; deserialised to the matching
-- request model at execution time).
DEFINE FIELD config ON TABLE quest_node TYPE string;

-- Entry point (no incoming control edges) -- cached from DAG analysis.
DEFINE FIELD is_entry ON TABLE quest_node TYPE bool
    DEFAULT false;

-- Terminal node (no outgoing control edges) -- cached from DAG analysis.
DEFINE FIELD is_terminal ON TABLE quest_node TYPE bool
    DEFAULT false;

-- Topological position (0-based; computed during DAG validation).
DEFINE FIELD execution_order ON TABLE quest_node TYPE int;

-- ── Indexes ──────────────────────────────────────────────────

-- Lookups by owning quest (fan-out fetch of a quest's node graph).
DEFINE INDEX quest_node_by_quest
    ON TABLE quest_node
    FIELDS quest_id;

-- Topological-sort and fork-point selection: ordered traversal of a quest's
-- nodes by execution position.
DEFINE INDEX quest_node_by_order
    ON TABLE quest_node
    FIELDS quest_id, execution_order;
