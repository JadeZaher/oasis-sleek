// SPDX-License-Identifier: UNLICENSED

using System.Security.Cryptography;
using System.Text;

namespace AZOA.WebAPI.Core.Webhooks;

/// <summary>
/// Replay-resistant HMAC signer for outbound consent webhooks
/// (tenant-consent-delegation §4, AC7 — H5).
///
/// <para><b>The signed payload includes the timestamp.</b> The signature is
/// <c>HMAC-SHA256(secret, "{timestampIso}.{body}")</c> — the delivery timestamp is part
/// of the signed material, NOT a separate unauthenticated header. A captured event
/// therefore cannot be replayed later to desync ArdaNova's view (e.g. resurrect a
/// revoked grant): the attacker can replay the exact <c>(body, timestamp, signature)</c>
/// tuple, but the receiver rejects a stale timestamp, and the attacker cannot forge a
/// fresh timestamp because doing so would invalidate the signature (they lack the
/// per-tenant secret).</para>
///
/// <para><b>Delivery headers.</b> Each POST carries:
/// <list type="bullet">
///   <item><c>X-Azoa-Signature</c> — the value returned by <see cref="Sign"/>
///         (lowercase hex of the HMAC).</item>
///   <item><c>X-Azoa-Timestamp</c> — the EXACT <c>timestampIso</c> string that was
///         signed. The receiver MUST feed this same string back into its own
///         <c>HMAC(secret, "{X-Azoa-Timestamp}.{rawBody}")</c> to verify — it is part
///         of the signed material, so any tampering breaks the signature.</item>
///   <item><c>X-Azoa-Idempotency-Id</c> — the stable per-event dedup id (separate from
///         the signature; lets the receiver discard a redelivery it already applied).</item>
/// </list></para>
///
/// <para><b>Receiver freshness-window contract.</b> The receiver MUST: (1) recompute the
/// HMAC over <c>"{X-Azoa-Timestamp}.{rawBody}"</c> with the shared per-tenant secret and
/// constant-time-compare it to <c>X-Azoa-Signature</c>; (2) parse <c>X-Azoa-Timestamp</c>
/// and REJECT the request if it is outside an acceptable freshness window (e.g. ±5
/// minutes of the receiver's own clock) — this is what defeats a delayed replay. AZOA
/// signs with a fresh UTC timestamp on EVERY (re)delivery attempt, so a legitimately
/// retried event is always within the receiver's window; only a captured-and-held replay
/// falls outside it.</para>
/// </summary>
public sealed class WebhookHmacSigner
{
    /// <summary>
    /// Computes the replay-resistant signature for a webhook delivery.
    /// <paramref name="timestampIso"/> is the ISO-8601 UTC delivery timestamp (the value
    /// also sent as <c>X-Azoa-Timestamp</c>); <paramref name="body"/> is the exact raw
    /// JSON body bytes-as-string that will be POSTed; <paramref name="secret"/> is the
    /// tenant's per-tenant HMAC secret. Returns the lowercase-hex HMAC-SHA256 over
    /// <c>"{timestampIso}.{body}"</c> (UTF-8). Deterministic for a given triple.
    /// </summary>
    public string Sign(string body, string timestampIso, string secret)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(timestampIso);
        if (string.IsNullOrEmpty(secret))
            throw new ArgumentException("A per-tenant HMAC secret is required.", nameof(secret));

        // The timestamp is PREPENDED into the signed material (timestamp '.' body) so it
        // is authenticated, not a side-channel header. This binds the signature to a
        // specific delivery time and is what makes a delayed replay detectable.
        var signedMaterial = $"{timestampIso}.{body}";

        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var msgBytes = Encoding.UTF8.GetBytes(signedMaterial);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(msgBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
