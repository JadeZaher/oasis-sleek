namespace OASIS.WebAPI.Core;

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
public static class OasisScopes
{
    /// <summary>May create/list child avatars and issue child credentials.</summary>
    public const string TenantProvision = "tenant:provision";

    /// <summary>A child credential may create/manage wallets for its avatar.</summary>
    public const string WalletManage = "wallet:manage";

    /// <summary>A child credential may mint/transfer NFTs for its avatar.</summary>
    public const string NftMint = "nft:mint";
}
