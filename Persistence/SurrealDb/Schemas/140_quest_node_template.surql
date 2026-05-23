-- ============================================================
-- Table: quest_node_template
-- Aggregate: QuestNodeTemplate (Models/Quest/QuestNodeTemplate.cs)
-- Guardrail: G6 SCHEMAFULL; read-only catalog of reusable node definitions
-- Note: Reusable node definition shared across QuestTemplates. A
--       QuestTemplate's embedded node slot (template_node) references a
--       QuestNodeTemplate by id and supplies ParamOverrides; QuestInstantiator
--       merges DefaultConfig + ParamOverrides + caller parameters into the
--       runtime Quest's QuestNode.Config.
-- Note: node_type is the QuestNodeType enum serialised as its C# name (e.g.
--       "HolonCreate", "WalletGet"). Asserted INSIDE the full enum value set
--       so a schema-drift typo is rejected at INSERT time rather than at the
--       dispatch layer. Keep this list in lockstep with
--       Models/Quest/QuestEnums.cs:QuestNodeType.
-- ============================================================

DEFINE TABLE quest_node_template SCHEMAFULL;

-- Core identity (record id is the Guid('N') of QuestNodeTemplate.Id)
DEFINE FIELD id ON TABLE quest_node_template TYPE string
    ASSERT $value != NONE AND $value != "";

-- Caller-supplied label
DEFINE FIELD name ON TABLE quest_node_template TYPE string;

-- QuestNodeType enum name (e.g. HolonCreate, NftMint). Keep in lockstep with
-- Models/Quest/QuestEnums.cs:QuestNodeType.
DEFINE FIELD node_type ON TABLE quest_node_template TYPE string
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

-- Optional description
DEFINE FIELD description ON TABLE quest_node_template TYPE option<string>;

-- JSON default config blob (string-encoded)
DEFINE FIELD default_config ON TABLE quest_node_template TYPE string;

-- JSON-Schema for config validation
DEFINE FIELD config_schema ON TABLE quest_node_template TYPE string;

-- JSON-Schema for upstream-input contract
DEFINE FIELD input_schema ON TABLE quest_node_template TYPE string;

-- JSON-Schema for produced-output contract
DEFINE FIELD output_schema ON TABLE quest_node_template TYPE string;

-- Semantic version (free string)
DEFINE FIELD version ON TABLE quest_node_template TYPE string;

-- Owner avatar (Guid('N') hex)
DEFINE FIELD author_avatar_id ON TABLE quest_node_template TYPE string
    ASSERT $value != NONE AND $value != "";

-- Marketplace visibility flag
DEFINE FIELD is_public ON TABLE quest_node_template TYPE bool
    DEFAULT false;

-- Free-form tags
DEFINE FIELD tags ON TABLE quest_node_template FLEXIBLE TYPE array;

-- ── Indexes ──────────────────────────────────────────────────

-- Authors browsing their own node templates in the marketplace UI.
DEFINE INDEX quest_node_template_by_author
    ON TABLE quest_node_template
    FIELDS author_avatar_id;

-- Filtering the marketplace by operation kind.
DEFINE INDEX quest_node_template_by_type
    ON TABLE quest_node_template
    FIELDS node_type;
