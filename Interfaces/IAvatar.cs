using System.Text.Json.Serialization;

namespace OASIS.WebAPI.Interfaces;

public interface IAvatar
{
    Guid Id { get; set; }
    string Username { get; set; }
    string Email { get; set; }
    [JsonIgnore]
    string PasswordHash { get; set; }
    string? Title { get; set; }
    string? FirstName { get; set; }
    string? LastName { get; set; }
    DateTime CreatedDate { get; set; }
    DateTime? LastBeamedInDate { get; set; }
    bool IsActive { get; set; }
    bool IsVerified { get; set; }

    // Tenant ownership (tenant-onboarding). OwnerTenantId is the provisioning
    // tenant principal's avatar id (self-FK); null = not tenant-managed.
    Guid? OwnerTenantId { get; set; }
    string? ExternalUserId { get; set; }
    string? ExternalRef { get; set; }
}
