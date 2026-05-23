-- ============================================================
-- Table: avatar
-- Aggregate: Avatar (Models/Avatar.cs)
-- Guardrail: G6 SCHEMAFULL
-- Note: Wallets navigation list is NOT persisted here — owned by IWalletStore via wallet.avatar_id FK.
-- ============================================================

DEFINE TABLE avatar SCHEMAFULL;

DEFINE FIELD id ON TABLE avatar TYPE string;
DEFINE FIELD username ON TABLE avatar TYPE string
    ASSERT $value != NONE AND $value != "";
DEFINE FIELD email ON TABLE avatar TYPE string
    ASSERT $value != NONE AND $value != "";
DEFINE FIELD password_hash ON TABLE avatar TYPE string
    ASSERT $value != NONE AND $value != "";
DEFINE FIELD title ON TABLE avatar TYPE option<string>;
DEFINE FIELD first_name ON TABLE avatar TYPE option<string>;
DEFINE FIELD last_name ON TABLE avatar TYPE option<string>;
DEFINE FIELD created_date ON TABLE avatar TYPE datetime;
DEFINE FIELD last_beamed_in_date ON TABLE avatar TYPE option<datetime>;
DEFINE FIELD is_active ON TABLE avatar TYPE bool
    DEFAULT true;
DEFINE FIELD is_verified ON TABLE avatar TYPE bool
    DEFAULT false;
DEFINE FIELD karma ON TABLE avatar TYPE int
    DEFAULT 0;
DEFINE FIELD level ON TABLE avatar TYPE int
    DEFAULT 1;

-- ── Indexes ──────────────────────────────────────────────────

DEFINE INDEX avatar_username
    ON TABLE avatar
    FIELDS username
    UNIQUE;

DEFINE INDEX avatar_email
    ON TABLE avatar
    FIELDS email
    UNIQUE;
