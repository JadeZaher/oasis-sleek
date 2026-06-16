using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using OASIS.WebAPI.Core;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Interfaces.Stores;
using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Managers;

/// <summary>
/// Tenant provisioning manager (Decision D1 — Option B). A tenant is an Avatar
/// that owns a <c>tenant:provision</c> key; child avatars carry
/// <c>OwnerTenantId == tenantId</c>. Cross-tenant isolation is the security crux
/// (DEPLOY-STEP B5): every per-child op asserts ownership and reports a miss as
/// 404 (<see cref="TenantAuthorizationError.NotFound"/>), never 403, so a prober
/// cannot enumerate another tenant's avatars.
///
/// JWT issuance (Decision D2): this manager DUPLICATES the minimal symmetric
/// signing primitive from <c>AvatarManager.GenerateJwt</c> (same <c>Jwt:Key</c> /
/// HmacSha256 / issuer / audience) rather than coupling to AvatarManager, because
/// a CHILD token is structurally different — subject is the child avatar id and
/// the claims are delegated <c>scope</c> claims with a SHORTENED TTL
/// (<see cref="ChildTokenLifetime"/>), not the login token's email/username
/// claims with a 24h TTL. The shared part is only the config key + algorithm,
/// which is one line; the token shape is the genuinely different bit.
/// </summary>
public class TenantManager : ITenantManager
{
    /// <summary>Short-lived child credential TTL (D2: shorten vs. the 24h login token).</summary>
    private static readonly TimeSpan ChildTokenLifetime = TimeSpan.FromMinutes(15);

    private readonly IAvatarStore _avatarStore;
    private readonly IConfiguration _config;

    public TenantManager(IAvatarStore avatarStore, IConfiguration config)
    {
        _avatarStore = avatarStore;
        _config = config;
    }

    public async Task<OASISResult<ChildAvatarResponse>> ProvisionChildAsync(Guid tenantId, ProvisionChildModel model, CancellationToken ct = default)
    {
        var result = new OASISResult<ChildAvatarResponse>();

        if (string.IsNullOrWhiteSpace(model.ExternalUserId))
        {
            result.IsError = true;
            result.Message = "externalUserId is required.";
            return result;
        }

        var externalUserId = model.ExternalUserId.Trim();

        // Idempotency: a repeat provision for the same (tenant, externalUserId)
        // returns the existing child, not a duplicate. The store query is
        // owner-scoped, so it can only ever surface THIS tenant's row.
        var existing = await _avatarStore.GetByTenantAndExternalUserAsync(tenantId, externalUserId, ct);
        if (!existing.IsError && existing.Result is not null)
        {
            result.Result = ToResponse(existing.Result);
            result.Message = "Existing child returned (idempotent).";
            return result;
        }

        // Deterministic username/email seeds when not supplied — both are
        // unique-indexed and required on the avatar record. The tenant-id prefix
        // keeps two tenants' identical externalUserIds from colliding globally.
        var seed = $"tenant-{tenantId:N}-{externalUserId}";
        var username = string.IsNullOrWhiteSpace(model.Username) ? seed : model.Username.Trim();
        var email = string.IsNullOrWhiteSpace(model.Email) ? $"{seed}@tenant.oasis.local" : model.Email.Trim();

        var child = new Avatar
        {
            Username = username,
            Email = email,
            // No password login path for tenant-managed children; the tenant
            // acts for them via short-lived child credentials. A random hash
            // keeps the column non-empty without granting a usable password.
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")),
            IsActive = true,
            IsVerified = false,
            // IDOR rule: ownership is set from the PARAMETER, never the model.
            OwnerTenantId = tenantId,
            ExternalUserId = externalUserId,
            ExternalRef = string.IsNullOrWhiteSpace(model.ExternalRef) ? null : model.ExternalRef.Trim(),
        };

        var saved = await _avatarStore.UpsertAsync(child, ct);
        if (saved.IsError || saved.Result is null)
        {
            result.IsError = true;
            result.Message = saved.IsError ? saved.Message : "Failed to provision child avatar.";
            result.Exception = saved.Exception;
            return result;
        }

        result.Result = ToResponse(saved.Result);
        result.Message = "Child avatar provisioned.";
        return result;
    }

    public async Task<OASISResult<IEnumerable<ChildAvatarResponse>>> ListChildrenAsync(Guid tenantId, string? externalUserId, CancellationToken ct = default)
    {
        var result = new OASISResult<IEnumerable<ChildAvatarResponse>>();

        var owned = await _avatarStore.ListByOwnerTenantAsync(tenantId, ct);
        if (owned.IsError)
        {
            result.IsError = true;
            result.Message = owned.Message;
            result.Exception = owned.Exception;
            return result;
        }

        var children = owned.Result ?? Enumerable.Empty<IAvatar>();
        if (!string.IsNullOrWhiteSpace(externalUserId))
        {
            var filter = externalUserId.Trim();
            children = children.Where(c => string.Equals(c.ExternalUserId, filter, StringComparison.Ordinal));
        }

        result.Result = children.Select(ToResponse).ToList();
        result.Message = "Success";
        return result;
    }

    public async Task<OASISResult<ChildAvatarResponse>> ResolveChildAsync(Guid tenantId, string externalUserId, CancellationToken ct = default)
    {
        var result = new OASISResult<ChildAvatarResponse>();

        if (string.IsNullOrWhiteSpace(externalUserId))
        {
            // Indistinguishable-from-miss: no leak about what exists.
            result.IsError = true;
            result.Message = TenantAuthorizationError.NotFound + "No child avatar for that external user id.";
            return result;
        }

        var found = await _avatarStore.GetByTenantAndExternalUserAsync(tenantId, externalUserId.Trim(), ct);
        if (found.IsError || found.Result is null)
        {
            result.IsError = true;
            result.Message = TenantAuthorizationError.NotFound + "No child avatar for that external user id.";
            result.Exception = found.Exception;
            return result;
        }

        result.Result = ToResponse(found.Result);
        result.Message = "Success";
        return result;
    }

    public async Task<OASISResult<ChildCredentialResponse>> IssueChildCredentialAsync(
        Guid tenantId,
        Guid childId,
        IEnumerable<string> requestedScopes,
        IEnumerable<string> tenantScopes,
        CancellationToken ct = default)
    {
        var result = new OASISResult<ChildCredentialResponse>();

        // Load the child and assert ownership. A cross-tenant or unowned target
        // (OwnerTenantId == null OR != tenantId) is reported as NOT_FOUND (404),
        // never FORBIDDEN — the isolation crux (spec §3 / acceptance c).
        var loaded = await _avatarStore.GetByIdAsync(childId, ct);
        if (loaded.IsError || loaded.Result is null
            || loaded.Result.OwnerTenantId is null
            || loaded.Result.OwnerTenantId.Value != tenantId)
        {
            result.IsError = true;
            result.Message = TenantAuthorizationError.NotFound + "No such child avatar for this tenant.";
            return result;
        }

        var child = loaded.Result;

        // Delegation: the issued credential may carry ONLY scopes the tenant key
        // itself holds (no privilege escalation). Intersect requested ∩ tenant's
        // own; an empty requested set delegates the full tenant set.
        var tenantOwn = new HashSet<string>(
            (tenantScopes ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()),
            StringComparer.Ordinal);

        var requested = (requestedScopes ?? Enumerable.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();

        // tenant:provision is never delegated down to a child credential — a
        // child must not be able to provision further avatars.
        tenantOwn.Remove(OasisScopes.TenantProvision);

        var delegated = (requested.Count == 0
                ? tenantOwn
                : requested.Where(tenantOwn.Contains))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var expiresAt = DateTime.UtcNow.Add(ChildTokenLifetime);
        var token = GenerateChildJwt(child.Id, delegated, expiresAt);

        result.Result = new ChildCredentialResponse
        {
            AvatarId = child.Id,
            Token = token,
            ExpiresAt = expiresAt,
            Scopes = delegated,
        };
        result.Message = "Child credential issued.";
        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ChildAvatarResponse ToResponse(IAvatar a) => new()
    {
        AvatarId = a.Id,
        ExternalUserId = a.ExternalUserId ?? string.Empty,
        ExternalRef = a.ExternalRef,
        Username = a.Username,
        Email = a.Email,
    };

    /// <summary>
    /// Minimal symmetric child-token primitive (D2). Subject = child avatar id;
    /// one <c>scope</c> claim per delegated scope (matching the shape the API-key
    /// handler emits at <c>ApiKeyAuthenticationHandler.cs:81-87</c>, so downstream
    /// per-avatar authorization treats the child token identically). Short TTL.
    /// </summary>
    private string GenerateChildJwt(Guid childAvatarId, IEnumerable<string> scopes, DateTime expiresAt)
    {
        var key = _config.GetValue<string>("Jwt:Key") ?? throw new InvalidOperationException("JWT Key missing.");
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, childAvatarId.ToString()),
            new("AvatarId", childAvatarId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        foreach (var scope in scopes)
            claims.Add(new Claim("scope", scope));

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
