// SPDX-License-Identifier: UNLICENSED

using System.Net;
using System.Net.Sockets;

namespace AZOA.WebAPI.Core.Webhooks;

/// <summary>
/// SSRF guard for tenant-registered webhook URLs (tenant-consent-delegation §4, AC7 —
/// H5). A tenant controls the callback URL, so without this guard a registered callback
/// could point at an AZOA-internal service (the SurrealDB admin port, a cloud metadata
/// endpoint, a sidecar), turning the delivery worker into a confused-deputy SSRF probe.
///
/// <para><b>Policy: https-only + public-IP allowlist.</b> A URL is allowed ONLY when
/// it is <c>https</c> AND <b>every</b> address its host resolves to is a public,
/// routable IP. Any non-https scheme, or any resolved address in a private / loopback /
/// link-local / ULA / metadata range, rejects the URL.</para>
///
/// <para><b>DNS-rebinding defence.</b> The host is resolved via
/// <see cref="Dns.GetHostAddresses(string)"/> and the URL is rejected if <b>ANY</b>
/// resolved address is blocked — a public name that resolves (in whole or in part) to a
/// private address cannot slip through. The worker calls this immediately before each
/// POST (not only at registration time), so a rebind between register and deliver is
/// still caught.</para>
///
/// <para>This is an injectable instance (so the DNS resolution can be faked in tests),
/// but the IP-classification core is exposed as the pure, unit-testable static helper
/// <see cref="IsBlockedIp(IPAddress)"/>.</para>
/// </summary>
public sealed class WebhookSsrfGuard
{
    /// <summary>
    /// Validates a webhook URL against the https-only + public-IP-allowlist policy.
    /// Returns <c>true</c> only when the URL is safe to POST to; otherwise <c>false</c>
    /// with a human-readable <paramref name="reason"/> the caller logs / dead-letters
    /// with. NEVER throws — a malformed URL or a DNS failure is a rejection, not an
    /// exception.
    /// </summary>
    public bool IsAllowed(string url, out string reason)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            reason = "URL is empty.";
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            reason = "URL is not a well-formed absolute URI.";
            return false;
        }

        // https-only — a captured http callback could be MITM'd and an internal-scheme
        // (file://, etc.) is never a legitimate webhook target.
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"URL scheme '{uri.Scheme}' is not https.";
            return false;
        }

        // Resolve the host to its address set. A literal-IP host short-circuits the DNS
        // lookup (GetHostAddresses returns it directly), so both "https://10.0.0.1/" and
        // "https://internal.name/" (rebinding to a private A record) are covered.
        IPAddress[] addresses;
        try
        {
            addresses = ResolveHost(uri.DnsSafeHost);
        }
        catch (Exception ex)
        {
            // A resolution failure is fail-closed: we do not POST to a host we cannot
            // classify.
            reason = $"Host '{uri.DnsSafeHost}' could not be resolved: {ex.Message}";
            return false;
        }

        if (addresses.Length == 0)
        {
            reason = $"Host '{uri.DnsSafeHost}' resolved to no addresses.";
            return false;
        }

        // Reject if ANY resolved address is blocked — defence against a public name that
        // resolves (in whole or in part) to a private/metadata address.
        foreach (var addr in addresses)
        {
            if (IsBlockedIp(addr))
            {
                reason = $"Host '{uri.DnsSafeHost}' resolves to blocked address {addr} " +
                         "(private / loopback / link-local / ULA / metadata range).";
                return false;
            }
        }

        reason = "Allowed.";
        return true;
    }

    /// <summary>
    /// DNS resolution seam. Virtual-by-delegate so tests can substitute a deterministic
    /// resolver; production resolves via <see cref="Dns.GetHostAddresses(string)"/>.
    /// </summary>
    private Func<string, IPAddress[]>? _resolver;

    /// <summary>
    /// Test seam: override how hosts resolve to addresses. Production leaves this null
    /// and uses <see cref="Dns.GetHostAddresses(string)"/>.
    /// </summary>
    public void UseResolver(Func<string, IPAddress[]> resolver) => _resolver = resolver;

    private IPAddress[] ResolveHost(string host)
        => (_resolver ?? Dns.GetHostAddresses)(host);

    /// <summary>
    /// Pure, unit-testable IP classifier. Returns <c>true</c> when <paramref name="ip"/>
    /// is in a range a webhook callback must NEVER reach:
    /// <list type="bullet">
    ///   <item>IPv4 loopback <c>127.0.0.0/8</c></item>
    ///   <item>IPv4 private <c>10.0.0.0/8</c>, <c>172.16.0.0/12</c>, <c>192.168.0.0/16</c></item>
    ///   <item>IPv4 link-local <c>169.254.0.0/16</c> (INCLUDES the cloud metadata
    ///         endpoint <c>169.254.169.254</c>)</item>
    ///   <item>IPv6 loopback <c>::1</c></item>
    ///   <item>IPv6 ULA <c>fc00::/7</c></item>
    ///   <item>IPv6 link-local <c>fe80::/10</c></item>
    ///   <item>the unspecified address (<c>0.0.0.0</c> / <c>::</c>)</item>
    /// </list>
    /// An IPv4-mapped IPv6 address (<c>::ffff:a.b.c.d</c>) is unwrapped to its IPv4 form
    /// and classified there, so a mapped private address cannot evade the v4 checks.
    /// </summary>
    public static bool IsBlockedIp(IPAddress ip)
    {
        if (ip is null) return true;

        // Unwrap an IPv4-mapped IPv6 address so ::ffff:10.0.0.1 is classified as v4.
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip)) return true; // 127.0.0.0/8 and ::1

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes(); // network order: b[0].b[1].b[2].b[3]

            // 0.0.0.0/8 — "this host" / unspecified.
            if (b[0] == 0) return true;

            // 127.0.0.0/8 — loopback (IsLoopback already covers, defensive).
            if (b[0] == 127) return true;

            // 10.0.0.0/8 — RFC1918.
            if (b[0] == 10) return true;

            // 172.16.0.0/12 — RFC1918 (172.16.0.0 – 172.31.255.255).
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;

            // 192.168.0.0/16 — RFC1918.
            if (b[0] == 192 && b[1] == 168) return true;

            // 169.254.0.0/16 — link-local, INCLUDES 169.254.169.254 cloud metadata.
            if (b[0] == 169 && b[1] == 254) return true;

            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal) return true;      // fe80::/10
            if (IPAddress.IPv6Any.Equals(ip)) return true; // ::

            var b = ip.GetAddressBytes(); // 16 bytes

            // fc00::/7 — Unique Local Addresses (first 7 bits == 1111 110).
            if ((b[0] & 0xFE) == 0xFC) return true;

            // fe80::/10 — link-local (defensive: IsIPv6LinkLocal already covers).
            if (b[0] == 0xFE && (b[1] & 0xC0) == 0x80) return true;

            return false;
        }

        // Unknown address family — fail closed.
        return true;
    }
}
