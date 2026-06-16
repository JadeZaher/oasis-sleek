// SPDX-License-Identifier: UNLICENSED

using OASIS.WebAPI.Models.Kyc;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Interfaces.Providers;

/// <summary>
/// Provider-agnostic verification seam. This is the swap point between the
/// in-house manual reviewer and an external KYC vendor — selected by the
/// <c>Kyc:Provider</c> configuration value. The manager depends only on this
/// interface, never on a concrete provider.
/// </summary>
public interface IKycProviderService
{
    /// <summary>
    /// Begins a provider session for the avatar's documents and returns the
    /// provider session id. The manual provider has no external session and
    /// returns the avatar id as a pseudo-session id.
    /// </summary>
    Task<OASISResult<string>> CreateSessionAsync(Guid avatarId, IReadOnlyList<KycDocumentModel> documents, CancellationToken ct = default);

    /// <summary>Polls the current status of a provider session.</summary>
    Task<OASISResult<KycStatus>> GetSessionStatusAsync(string providerSessionId, CancellationToken ct = default);

    /// <summary>Processes an inbound provider webhook payload.</summary>
    Task<OASISResult<KycStatus>> HandleWebhookAsync(string payload, CancellationToken ct = default);

    /// <summary>
    /// Validates the supplied documents (MIME allow-list, size cap, required
    /// fields) before any submission row is written.
    /// </summary>
    Task<OASISResult<bool>> ValidateDocumentsAsync(IReadOnlyList<SubmitKycDocumentModel> documents, CancellationToken ct = default);
}
