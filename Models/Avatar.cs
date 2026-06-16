using OASIS.WebAPI.Interfaces;

namespace OASIS.WebAPI.Models;

public class Avatar : IAvatar
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? LastBeamedInDate { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsVerified { get; set; }

    // ── Tenant ownership (tenant-onboarding) ──────────────────────────────────
    // OwnerTenantId is the avatar id of the tenant principal that provisioned
    // this child avatar (a self-FK). null = not a tenant-managed avatar (every
    // avatar that pre-dates multi-tenancy). ExternalUserId is the tenant's own
    // user id for this child (unique per tenant, not globally); ExternalRef is a
    // free opaque tenant string (e.g. org/realm).
    public Guid? OwnerTenantId { get; set; }
    public string? ExternalUserId { get; set; }
    public string? ExternalRef { get; set; }

    public List<Wallet> Wallets { get; set; } = new();
}
