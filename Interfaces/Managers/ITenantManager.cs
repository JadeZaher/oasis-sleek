using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Interfaces.Managers;

/// <summary>
/// Discriminator the controller uses to translate a manager auth failure into
/// the right HTTP status without raw string-matching. Carried via the
/// <see cref="OASISResult{T}.Message"/> prefix — mirrors
/// <see cref="STARODKAuthorizationError"/>.
///
/// SECURITY NOTE: a cross-tenant or unowned target is reported as
/// <see cref="NotFound"/> (→ 404), NEVER <see cref="Forbidden"/>, so a prober
/// cannot distinguish "no such avatar" from "exists but belongs to another
/// tenant" (the §3 isolation crux). <see cref="Forbidden"/> is reserved for the
/// surface-level "missing tenant:provision scope" case, which the
/// authorization policy already rejects at 403 before the manager runs.
/// </summary>
public static class TenantAuthorizationError
{
    /// <summary>Caller lacks tenant authority — surfaces as 403.</summary>
    public const string Forbidden = "TENANT_FORBIDDEN: ";

    /// <summary>
    /// Target child does not exist OR is not owned by this tenant — surfaces as
    /// 404 (deliberately indistinguishable, per the isolation threat model).
    /// </summary>
    public const string NotFound = "TENANT_NOT_FOUND: ";
}

/// <summary>
/// Tenant provisioning surface. A "tenant" is an Avatar that owns an API key
/// carrying the <c>tenant:provision</c> scope (Decision D1 — Option B: scope +
/// <c>OwnerTenantId</c> self-FK, no parallel Tenant entity). Every method takes
/// the tenant id as an explicit parameter the controller fills from the
/// authenticated key's claim — NEVER from a request body (IDOR rule, mirrors
/// <c>AvatarManager.UpdateAsync(id, model, avatarId)</c>).
/// </summary>
public interface ITenantManager
{
    /// <summary>
    /// Provisions a new child avatar under <paramref name="tenantId"/>. Sets
    /// <c>OwnerTenantId = tenantId</c> from the PARAMETER, never the model.
    /// Idempotent on <c>(tenantId, externalUserId)</c>: a repeat provision for
    /// the same external user returns the existing child, not a duplicate.
    /// </summary>
    Task<OASISResult<ChildAvatarResponse>> ProvisionChildAsync(Guid tenantId, ProvisionChildModel model, CancellationToken ct = default);

    /// <summary>
    /// Lists the tenant's child avatars, optionally filtered to a single
    /// external user id. Scoped to the tenant — never returns another tenant's
    /// children.
    /// </summary>
    Task<OASISResult<IEnumerable<ChildAvatarResponse>>> ListChildrenAsync(Guid tenantId, string? externalUserId, CancellationToken ct = default);

    /// <summary>
    /// Resolves one child by the tenant's own external user id (the primary
    /// tenant lookup path). Cross-tenant / no match → <see cref="TenantAuthorizationError.NotFound"/>.
    /// </summary>
    Task<OASISResult<ChildAvatarResponse>> ResolveChildAsync(Guid tenantId, string externalUserId, CancellationToken ct = default);

    /// <summary>
    /// Issues a short-lived, child-scoped JWT (subject = child avatar id) the
    /// tenant can use to act AS that child for wallet/NFT operations. Loads the
    /// child and asserts <c>child.OwnerTenantId == tenantId</c>; on mismatch or
    /// <c>null</c> ownership returns <see cref="TenantAuthorizationError.NotFound"/>
    /// (404, never 403). The delegated scopes are the INTERSECTION of
    /// <paramref name="requestedScopes"/> with <paramref name="tenantScopes"/>
    /// (the tenant key's own scopes) — no privilege escalation through delegation.
    /// </summary>
    Task<OASISResult<ChildCredentialResponse>> IssueChildCredentialAsync(
        Guid tenantId,
        Guid childId,
        IEnumerable<string> requestedScopes,
        IEnumerable<string> tenantScopes,
        CancellationToken ct = default);
}
