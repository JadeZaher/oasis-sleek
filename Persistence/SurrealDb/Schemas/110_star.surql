-- ============================================================
-- Table: star_odk
-- Aggregate: STARODK (Models/STARODK.cs)
-- Guardrail: G6 SCHEMAFULL
-- Note: Generated code + deployment config can be large strings;
--       SurrealDB has no per-field size limit but operators should
--       monitor row size.
-- ============================================================

DEFINE TABLE star_odk SCHEMAFULL;

DEFINE FIELD id ON TABLE star_odk TYPE string
    ASSERT $value != NONE AND $value != "";
DEFINE FIELD name ON TABLE star_odk TYPE string
    ASSERT $value != NONE AND $value != "";
DEFINE FIELD description ON TABLE star_odk TYPE string;
DEFINE FIELD public_key ON TABLE star_odk TYPE option<string>;
DEFINE FIELD private_key_hash ON TABLE star_odk TYPE option<string>;
DEFINE FIELD avatar_id ON TABLE star_odk TYPE option<string>;

-- BoundHolonIds (list of holon-id strings)
DEFINE FIELD bound_holon_ids ON TABLE star_odk TYPE option<array<string>>;
DEFINE FIELD target_chain ON TABLE star_odk TYPE option<string>;
DEFINE FIELD generated_code ON TABLE star_odk TYPE option<string>;
DEFINE FIELD deployment_config ON TABLE star_odk TYPE option<string>;
DEFINE FIELD created_date ON TABLE star_odk TYPE datetime;
DEFINE FIELD modified_date ON TABLE star_odk TYPE option<datetime>;
DEFINE FIELD is_active ON TABLE star_odk TYPE bool
    DEFAULT true;

-- ── Indexes ──────────────────────────────────────────────────

DEFINE INDEX star_avatar_id
    ON TABLE star_odk
    FIELDS avatar_id;

DEFINE INDEX star_target_chain
    ON TABLE star_odk
    FIELDS target_chain;

DEFINE INDEX star_is_active
    ON TABLE star_odk
    FIELDS is_active;
