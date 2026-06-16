using System.Security.Claims;

namespace OASIS.WebAPI.Core;

/// <summary>
/// The single, reusable consuming side of the scope claims emitted by
/// <c>ApiKeyAuthenticationHandler</c> (<c>ApiKeyAuthenticationHandler.cs:81-87</c>
/// splits the <c>ApiKey.Scopes</c> CSV into one <c>scope</c> claim per entry).
/// Used by the <c>TenantScope</c> authorization policy and by the
/// credential-issuer that decides which scopes may be delegated onto a child
/// credential — one helper, no scattered <c>User.FindAll("scope")</c> drift.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// True when the principal carries a <c>scope</c> claim whose value exactly
    /// (case-sensitive) equals <paramref name="scope"/>. Scope tokens are
    /// lowercase colon-delimited (e.g. <c>tenant:provision</c>); matching is
    /// ordinal to avoid culture surprises.
    /// </summary>
    public static bool HasScope(this ClaimsPrincipal principal, string scope)
    {
        if (principal is null || string.IsNullOrEmpty(scope))
            return false;

        foreach (var claim in principal.FindAll("scope"))
        {
            if (string.Equals(claim.Value, scope, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns every <c>scope</c> claim value the principal carries, de-duplicated
    /// and trimmed. Used by the credential-issuer to compute the
    /// intersection-with-requested set (no privilege escalation through
    /// delegation).
    /// </summary>
    public static IReadOnlyCollection<string> GetScopes(this ClaimsPrincipal principal)
    {
        if (principal is null)
            return Array.Empty<string>();

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var claim in principal.FindAll("scope"))
        {
            var value = claim.Value?.Trim();
            if (!string.IsNullOrEmpty(value))
                set.Add(value);
        }

        return set;
    }
}
