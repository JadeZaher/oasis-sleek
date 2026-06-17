// SPDX-License-Identifier: UNLICENSED

namespace OASIS.WebAPI.Core;

/// <summary>
/// The signer-resolution context threaded into the value-moving
/// <see cref="OASIS.WebAPI.Interfaces.IBlockchainProvider"/> methods
/// (<c>MintAsync</c> / <c>BurnAsync</c> / <c>TransferAsync</c>) so the provider
/// signs with the RIGHT key (value-path-wiring C1 / decision D1).
///
/// <para>It carries only identity — never key material. The provider hands it to
/// <see cref="OASIS.WebAPI.Interfaces.Managers.IKeyCustodyService"/>:
/// <list type="bullet">
/// <item><b>User-wallet op</b> (<see cref="IsPlatform"/> = <c>false</c>): resolves
/// via <c>WithSigningKeyAsync(<see cref="WalletId"/>, <see cref="AvatarId"/>, …)</c>,
/// inheriting the custody IDOR guard (the caller must own the wallet).</item>
/// <item><b>Platform / ASA-admin op</b> (<see cref="IsPlatform"/> = <c>true</c>):
/// create / destroy-burn / clawback / opt-in of the platform account — resolves
/// via <c>WithPlatformSigningKeyAsync(true, …)</c>.</item>
/// </list>
/// </para>
///
/// <para><b>Interim safety:</b> a value-moving call that arrives WITHOUT a
/// <see cref="SigningContext"/> (a <c>null</c> argument) is treated as a
/// platform/ASA-admin op (the only direct callers are platform-authority paths:
/// ASA-create, wrapped-mint, soulbound, opt-in). A user-wallet op MUST arrive
/// with <c>IsPlatform = false</c> and a real <see cref="WalletId"/>; the provider
/// returns a CLEAR ERROR (never a platform-key mis-sign) for any user context it
/// cannot resolve.</para>
/// </summary>
/// <param name="AvatarId">The authenticated owner of the moving wallet (custody
/// IDOR authority). <see cref="System.Guid.Empty"/> on a platform op.</param>
/// <param name="WalletId">The platform-managed wallet whose key signs a user op.
/// <see cref="System.Guid.Empty"/> on a platform op.</param>
/// <param name="IsPlatform"><c>true</c> for an ASA-admin op signed by the platform
/// key; <c>false</c> for a per-user custodial op.</param>
public readonly record struct SigningContext(Guid AvatarId, Guid WalletId, bool IsPlatform)
{
    /// <summary>An ASA-admin / platform-authority signing context.</summary>
    public static SigningContext Platform { get; } =
        new(Guid.Empty, Guid.Empty, IsPlatform: true);

    /// <summary>
    /// A per-user custodial signing context. Both ids must be non-empty; the
    /// provider routes this through the custody IDOR-guarded resolver.
    /// </summary>
    public static SigningContext ForUser(Guid avatarId, Guid walletId) =>
        new(avatarId, walletId, IsPlatform: false);

    /// <summary>
    /// True when this is a per-user context that actually carries a resolvable
    /// wallet + avatar. A user context missing either id is unresolvable and the
    /// provider must fail closed rather than fall back to the platform key.
    /// </summary>
    public bool IsResolvableUserContext =>
        !IsPlatform && WalletId != Guid.Empty && AvatarId != Guid.Empty;
}
