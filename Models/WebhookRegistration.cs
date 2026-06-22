// SPDX-License-Identifier: UNLICENSED

namespace AZOA.WebAPI.Models;

/// <summary>
/// A tenant's outbound consent-webhook configuration (tenant-consent-delegation §4,
/// AC7). Holds the receiver URL + the tenant's per-tenant HMAC secret. ONE active
/// registration per tenant for v1 (enforced by a unique index on <c>tenant_id</c>).
///
/// <para><b>Strict per-tenant isolation (H5).</b> Registration is scoped to the
/// authenticated API-key principal: a tenant only ever reads/writes its OWN
/// registration, and the delivery worker signs each tenant's events with ONLY that
/// tenant's <see cref="Secret"/> — there is no shared secret.</para>
///
/// <para><b>SSRF (H5).</b> <see cref="Url"/> is validated by
/// <c>WebhookSsrfGuard</c> before any POST — https-only, public-IP allowlist, with
/// link-local / RFC1918 / cloud-metadata ranges blocked — so a registered callback can
/// never reach an AZOA-internal service.</para>
/// </summary>
public sealed class WebhookRegistration
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning tenant principal. The registration's isolation key — one
    /// active row per tenant (unique index).</summary>
    public Guid TenantId { get; set; }

    /// <summary>The receiver endpoint. MUST be https and MUST resolve to a public IP —
    /// re-validated by <c>WebhookSsrfGuard</c> at delivery time (not just registration),
    /// so a DNS rebind to a private address between register and deliver is still
    /// caught.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// The tenant's per-tenant HMAC secret, used to sign every event delivered to this
    /// tenant (<c>X-Azoa-Signature</c>). Rotatable via
    /// <c>IWebhookRegistrationStore.RotateSecretAsync</c> (a rotation re-keys all
    /// SUBSEQUENT deliveries; in-flight retries pick up the new secret on their next
    /// attempt).
    ///
    /// <para><b>FOLLOW-UP (v1 limitation):</b> stored as-is (plaintext at rest) for v1.
    /// This SHOULD be encrypted-at-rest — mirror the custody-key pattern where
    /// <c>Wallet.EncryptedPrivateKey</c> is held encrypted + JsonIgnored. A
    /// per-tenant-secret-at-rest-encryption pass is a deliberate follow-up; flagged here
    /// so it is not forgotten.</para>
    /// </summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>When the secret was last rotated (UTC); null if never rotated since
    /// creation. Surfaced so a tenant can confirm a rotation took effect.</summary>
    public DateTime? SecretRotatedAt { get; set; }

    /// <summary>Whether deliveries are active. An inactive registration causes the
    /// worker to skip (dead-letter) its events rather than POST.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When the registration was created (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
