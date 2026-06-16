// SPDX-License-Identifier: UNLICENSED

using OASIS.WebAPI.Models.Kyc;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Interfaces.Managers;

/// <summary>
/// Reusable KYC access guard. Other managers inject this and call
/// <see cref="RequireVerifiedAsync"/> before a sensitive operation (e.g.
/// wallet generation or NFT mint). The gate reads the avatar's KYC submission
/// status (the most-recent APPROVED submission) rather than a denormalized
/// level on the avatar table.
///
/// FROZEN CONTRACT — a sibling lane codes against these signatures verbatim.
/// </summary>
public interface IKycGateService
{
    /// <summary>
    /// Succeeds (non-error <c>Result == true</c>) when the avatar has an APPROVED
    /// KYC submission. Otherwise returns an error whose <c>Message</c> starts with
    /// <see cref="KycAuthorizationError.Forbidden"/> so a controller can translate
    /// it to 403 — carrying the generic
    /// <see cref="KycAuthorizationError.VerificationRequiredMessage"/>.
    /// </summary>
    Task<OASISResult<bool>> RequireVerifiedAsync(Guid avatarId);

    /// <summary>
    /// The avatar's current effective KYC status (the most-recent submission's
    /// status). Returns an error when the avatar has no submission.
    /// </summary>
    Task<OASISResult<KycStatus>> GetKycStatusAsync(Guid avatarId);
}
