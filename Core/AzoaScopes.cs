namespace AZOA.WebAPI.Core;

/// <summary>
/// Central v1 scope vocabulary layered on the existing CSV <c>ApiKey.Scopes</c>
/// field (<c>Models/ApiKey.cs:29</c>) — no schema change to scope storage, only a
/// defined vocabulary and a single enforcement point
/// (<see cref="ClaimsPrincipalExtensions.HasScope"/> + the <c>TenantScope</c>
/// authorization policy).
///
/// IMPORTANT: an empty/NONE <c>Scopes</c> CSV still means "full access" for
/// <em>non-tenant legacy</em> keys (<c>Models/ApiKey.cs:26-28</c>), but a tenant
/// key MUST carry <see cref="TenantProvision"/> explicitly — "empty = full" does
/// NOT silently grant tenant powers (the policy checks for the literal scope).
/// </summary>
public static class AzoaScopes
{
    /// <summary>May create/list child avatars and issue child credentials.</summary>
    public const string TenantProvision = "tenant:provision";

    /// <summary>A child credential may create/manage wallets for its avatar.</summary>
    public const string WalletManage = "wallet:manage";

    /// <summary>A child credential may mint/transfer NFTs for its avatar.</summary>
    public const string NftMint = "nft:mint";

    // ── tenant-consent-delegation: value-signing scopes (H4) ──────────────────
    // These authorize a tenant-driven action that DECRYPTS a user's signing key.
    // They are EXCLUDED from the no-UX Participation standing grant (a value action
    // requires a deliberate UserExplicit grant) and are the scopes the custody seam
    // checks a live ConsentGrant against before key decrypt (AC4).

    /// <summary>Tenant may drive a token swap that signs with the user's key.</summary>
    public const string SwapSign = "swap:sign";

    /// <summary>Tenant may drive a value transfer that signs with the user's key.</summary>
    public const string TransferSign = "transfer:sign";

    /// <summary>Tenant may drive a grant/mint-to-actor that signs (platform or user key).</summary>
    public const string GrantSign = "grant:sign";

    /// <summary>Tenant may drive a fungible-token (ASA) create that signs.</summary>
    public const string TokenCreateSign = "token:create:sign";

    // ── tenant-consent-delegation: non-value participation scopes ──────────────

    /// <summary>Tenant may execute quests for the user (non-value; safe in a
    /// Participation standing grant).</summary>
    public const string QuestExecute = "quest:execute";

    /// <summary>
    /// The value-signing scopes a tenant must NOT obtain via a no-UX Participation
    /// grant (H4) — each requires a deliberate <c>UserExplicit</c> grant. A grant
    /// request that carries any of these under <c>Participation</c> origin is rejected.
    /// </summary>
    public static readonly System.Collections.Generic.IReadOnlySet<string> ValueSigningScopes =
        new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        {
            SwapSign, TransferSign, GrantSign, TokenCreateSign,
        };
}
