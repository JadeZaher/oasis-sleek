-- ============================================================
-- Table: api_key
-- Aggregate: ApiKey (Models/ApiKey.cs)
-- Guardrail: G6 SCHEMAFULL, G2 UNIQUE-on-key_hash insert-wins lookup
-- Note: Authentication ledger. The handler hashes the inbound X-Api-Key header
--       (SHA-256) and looks up key_hash via the unique index. The raw key never
--       reaches storage. key_prefix is the first 16 chars of the raw key for
--       caller display only.
-- Note: B3 nullable-collision review -- api_key_unique_hash UNIQUE on key_hash is
--       NOT NULL (ASSERT != NONE AND != ""), so SurrealDB's NULL-equals-NULL
--       collision semantics are not in play -- correct dedup by construction.
-- Note: Soft-delete via revoked_at + is_active. Handlers MUST check both
--       is_active AND revoked_at IS NONE AND (expires_at IS NONE OR
--       expires_at > now) before granting authentication. last_used_at is
--       updated fire-and-forget from the handler -- TouchLastUsedAsync must
--       never throw.
-- ============================================================

DEFINE TABLE api_key SCHEMAFULL;

-- Core identity (record id is the Guid('N') of ApiKey.Id)
DEFINE FIELD id ON TABLE api_key TYPE string
    ASSERT $value != NONE AND $value != "";

-- Owner avatar (Guid('N') hex); indexed for ListByAvatar
DEFINE FIELD avatar_id ON TABLE api_key TYPE string
    ASSERT $value != NONE AND $value != "";

-- Caller-supplied label (free string)
DEFINE FIELD name ON TABLE api_key TYPE string;

-- SHA-256(raw key) hex -- the dedup key; UNIQUE
DEFINE FIELD key_hash ON TABLE api_key TYPE string
    ASSERT $value != NONE AND $value != "";

-- Display prefix (first 16 chars of raw key, never the secret)
DEFINE FIELD key_prefix ON TABLE api_key TYPE string;

-- Creation timestamp (UTC)
DEFINE FIELD created_date ON TABLE api_key TYPE datetime;

-- Optional expiry (NONE = no expiry)
DEFINE FIELD expires_at ON TABLE api_key TYPE option<datetime>;

-- Last-used timestamp (fire-and-forget; may lag under load)
DEFINE FIELD last_used_at ON TABLE api_key TYPE option<datetime>;

-- Soft-delete marker (NONE = active)
DEFINE FIELD revoked_at ON TABLE api_key TYPE option<datetime>;

-- Active flag mirror for cheap WHERE filtering
DEFINE FIELD is_active ON TABLE api_key TYPE bool
    DEFAULT true;

-- Optional comma-separated scopes (empty/NONE = full access)
DEFINE FIELD scopes ON TABLE api_key TYPE option<string>;

-- ── Indexes ──────────────────────────────────────────────────

-- Primary auth lookup: handler hashes the inbound key and probes this index.
-- UNIQUE so a hash collision (cryptographically improbable) is surfaced at
-- INSERT time rather than silently allowing a parallel key.
DEFINE INDEX api_key_unique_hash
    ON TABLE api_key
    FIELDS key_hash
    UNIQUE;

-- Per-avatar listing for the management UI.
DEFINE INDEX api_key_by_avatar
    ON TABLE api_key
    FIELDS avatar_id;
