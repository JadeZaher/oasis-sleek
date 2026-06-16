// SPDX-License-Identifier: UNLICENSED

using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Interfaces.Stores;
using OASIS.WebAPI.Models.Kyc;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Managers;

/// <summary>
/// Reads the KYC ledger (<see cref="IKycStore"/>) to answer the gate questions.
/// The avatar is "verified" iff its most-recent submission is APPROVED — so a
/// later REJECTED/PENDING re-submission correctly closes the gate again. No
/// avatar-table column is consulted (D3).
/// </summary>
public sealed class KycGateService : IKycGateService
{
    private readonly IKycStore _store;

    public KycGateService(IKycStore store)
    {
        _store = store;
    }

    public async Task<OASISResult<bool>> RequireVerifiedAsync(Guid avatarId)
    {
        var latest = await _store.GetLatestSubmissionByAvatarAsync(avatarId);
        if (latest.IsError)
            return new OASISResult<bool> { IsError = true, Result = false, Message = latest.Message, Exception = latest.Exception };

        if (latest.Result is { Status: KycStatus.APPROVED })
            return new OASISResult<bool> { Result = true, Message = "Success" };

        // No submission, or the latest is not APPROVED — gate closed.
        return new OASISResult<bool>
        {
            IsError = true,
            Result  = false,
            Message = $"{KycAuthorizationError.Forbidden}{KycAuthorizationError.VerificationRequiredMessage}"
        };
    }

    public async Task<OASISResult<KycStatus>> GetKycStatusAsync(Guid avatarId)
    {
        var latest = await _store.GetLatestSubmissionByAvatarAsync(avatarId);
        if (latest.IsError)
            return new OASISResult<KycStatus> { IsError = true, Message = latest.Message, Exception = latest.Exception };

        if (latest.Result is null)
            return new OASISResult<KycStatus>
            {
                IsError = true,
                Message = $"{KycAuthorizationError.NotFound}No KYC submission found for this avatar."
            };

        return new OASISResult<KycStatus> { Result = latest.Result.Status, Message = "Success" };
    }
}
