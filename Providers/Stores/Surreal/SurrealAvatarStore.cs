using System.Text.Json.Serialization;
using Oasis.SurrealDb.Client;
using Oasis.SurrealDb.Client.Query;
using OASIS.WebAPI.Core;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Interfaces.Stores;
using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Providers.Stores.Surreal;

/// <summary>
/// SurrealDB-backed <see cref="IAvatarStore"/>. Maps between the legacy
/// <see cref="Avatar"/> domain model and an inline POCO (no source-gen this round)
/// via private ToPoco / FromPoco helpers.
///
/// ID encoding: <c>Guid.ToString("N").ToLowerInvariant()</c> (32-char hex,
/// no dashes). Dates are stored as DateTimeOffset with UTC kind applied.
/// </summary>
public sealed class SurrealAvatarStore : IAvatarStore
{
    private const string AvatarTable = "avatar";

    private readonly ISurrealExecutor _executor;

    public SurrealAvatarStore(ISurrealExecutor executor)
    {
        _executor = executor;
    }

    // ── IAvatarStore ──────────────────────────────────────────────────────────

    public async Task<OASISResult<IAvatar>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var q = SurrealQuery
                .Of("SELECT * FROM type::record($_t, $_id)")
                .WithParam("_t",  AvatarTable)
                .WithParam("_id", ToSurrealId(id));
            var row = await _executor.QuerySingleAsync<SurrealAvatar>(q, ct);
            return new OASISResult<IAvatar>
            {
                IsError = row == null,
                Message = row == null
                    ? $"Avatar not found (id: {id}). The avatar may have been deleted; if your session token references it, sign out and re-authenticate."
                    : "Success",
                Result  = row == null ? null : FromPoco(row)
            };
        }
        catch (Exception ex)
        {
            return new OASISResult<IAvatar>().CaptureException(ex, $"SurrealAvatarStore.GetByIdAsync failed: {ex.Message}");
        }
    }

    public async Task<OASISResult<IEnumerable<IAvatar>>> GetAllAsync(CancellationToken ct = default)
    {
        try
        {
            var q = SurrealQuery.SelectAll(AvatarTable);
            var rows = await _executor.QueryAsync<SurrealAvatar>(q, ct);
            return new OASISResult<IEnumerable<IAvatar>>
            {
                Result  = rows.Select(FromPoco).ToList(),
                Message = "Success"
            };
        }
        catch (Exception ex)
        {
            return new OASISResult<IEnumerable<IAvatar>>().CaptureException(ex, $"SurrealAvatarStore.GetAllAsync failed: {ex.Message}");
        }
    }

    public async Task<OASISResult<IAvatar>> UpsertAsync(IAvatar avatar, CancellationToken ct = default)
    {
        try
        {
            if (avatar.Id == Guid.Empty)
                avatar.Id = Guid.NewGuid();

            var poco   = ToPoco(avatar);
            var surrId = poco.Id;

            // UPSERT type::record($_t, $_id) CONTENT $_body RETURN AFTER
            // SurrealDB upsert: creates the record if it does not exist; replaces
            // it if it does. Same pattern as SurrealWalletStore.
            var q = SurrealQuery
                .Of("UPSERT type::record($_t, $_id) CONTENT $_body RETURN AFTER")
                .WithParam("_t",    AvatarTable)
                .WithParam("_id",   surrId)
                .WithParam("_body", poco);

            var resp = await _executor.ExecuteAsync(q, ct);
            resp.EnsureAllOk();

            var saved = resp.GetValues<SurrealAvatar>(0).FirstOrDefault();
            var result = saved is not null ? FromPoco(saved) : avatar;

            return new OASISResult<IAvatar> { Result = result, Message = "Saved." };
        }
        catch (Exception ex)
        {
            return new OASISResult<IAvatar>().CaptureException(ex, $"SurrealAvatarStore.UpsertAsync failed: {ex.Message}");
        }
    }

    public async Task<OASISResult<bool>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            // Check existence first (matches the prior EF read-before-update contract).
            var checkQ = SurrealQuery
                .Of("SELECT * FROM type::record($_t, $_id)")
                .WithParam("_t",  AvatarTable)
                .WithParam("_id", ToSurrealId(id));
            var existing = await _executor.QuerySingleAsync<SurrealAvatar>(checkQ, ct);
            if (existing == null)
                return new OASISResult<bool> { IsError = true, Message = "Avatar not found.", Result = false };

            var q = SurrealQuery
                .Of("DELETE type::record($_t, $_id)")
                .WithParam("_t",  AvatarTable)
                .WithParam("_id", ToSurrealId(id));
            await _executor.ExecuteAsync(q, ct);

            return new OASISResult<bool> { Result = true, Message = "Deleted." };
        }
        catch (Exception ex)
        {
            return new OASISResult<bool>().CaptureException(ex, $"SurrealAvatarStore.DeleteAsync failed: {ex.Message}");
        }
    }

    public async Task<OASISResult<IEnumerable<IAvatar>>> ListByOwnerTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            // Owner-scoped: only rows whose owner_tenant_id links to this tenant.
            // owner_tenant_id is a record<avatar> link, matched the same way
            // SurrealApiKeyStore.ListByAvatarAsync matches avatar_id.
            var q = SurrealQuery
                .Of("SELECT * FROM avatar WHERE owner_tenant_id = $_tenant ORDER BY created_date DESC")
                .WithParam("_tenant", SurrealLink.ToLink(AvatarTable, ToSurrealId(tenantId)));
            var rows = await _executor.QueryAsync<SurrealAvatar>(q, ct);
            return new OASISResult<IEnumerable<IAvatar>>
            {
                Result  = rows.Select(FromPoco).ToList(),
                Message = "Success"
            };
        }
        catch (Exception ex)
        {
            return new OASISResult<IEnumerable<IAvatar>>().CaptureException(ex, $"SurrealAvatarStore.ListByOwnerTenantAsync failed: {ex.Message}");
        }
    }

    public async Task<OASISResult<IAvatar>> GetByTenantAndExternalUserAsync(Guid tenantId, string externalUserId, CancellationToken ct = default)
    {
        try
        {
            // Owner-scoped resolve. A miss returns Result == null with NO error so
            // the manager treats it as "create new" (idempotency), mirroring
            // ISTARStore.GetByNameAndAvatarAsync.
            var q = SurrealQuery
                .Of("SELECT * FROM avatar WHERE owner_tenant_id = $_tenant AND external_user_id = $_ext LIMIT 1")
                .WithParam("_tenant", SurrealLink.ToLink(AvatarTable, ToSurrealId(tenantId)))
                .WithParam("_ext", externalUserId);
            var row = await _executor.QuerySingleAsync<SurrealAvatar>(q, ct);
            return new OASISResult<IAvatar>
            {
                Result  = row == null ? null : FromPoco(row),
                Message = "Success"
            };
        }
        catch (Exception ex)
        {
            return new OASISResult<IAvatar>().CaptureException(ex, $"SurrealAvatarStore.GetByTenantAndExternalUserAsync failed: {ex.Message}");
        }
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static string ToSurrealId(Guid id)
        => id.ToString("N").ToLowerInvariant();

    private static Guid FromSurrealId(string id)
        => Guid.ParseExact(id, "N");

    private static SurrealAvatar ToPoco(IAvatar a) => new()
    {
        Id               = ToSurrealId(a.Id),
        Username         = a.Username,
        Email            = a.Email,
        PasswordHash     = a.PasswordHash,
        Title            = a.Title,
        FirstName        = a.FirstName,
        LastName         = a.LastName,
        CreatedDate      = new DateTimeOffset(
                               DateTime.SpecifyKind(a.CreatedDate, DateTimeKind.Utc)),
        LastBeamedInDate = a.LastBeamedInDate.HasValue
                               ? new DateTimeOffset(
                                     DateTime.SpecifyKind(a.LastBeamedInDate.Value, DateTimeKind.Utc))
                               : null,
        IsActive         = a.IsActive,
        IsVerified       = a.IsVerified,
        // owner_tenant_id is a record<avatar> link; encode the same way the
        // ApiKey store encodes avatar_id. null tenant => null link column.
        OwnerTenantId    = a.OwnerTenantId.HasValue
                               ? SurrealLink.ToLink(AvatarTable, ToSurrealId(a.OwnerTenantId.Value))
                               : null,
        ExternalUserId   = a.ExternalUserId,
        ExternalRef      = a.ExternalRef
    };

    private static Avatar FromPoco(SurrealAvatar p) => new()
    {
        Id               = FromSurrealId(p.Id),
        Username         = p.Username,
        Email            = p.Email,
        PasswordHash     = p.PasswordHash,
        Title            = p.Title,
        FirstName        = p.FirstName,
        LastName         = p.LastName,
        CreatedDate      = p.CreatedDate.UtcDateTime,
        LastBeamedInDate = p.LastBeamedInDate?.UtcDateTime,
        IsActive         = p.IsActive,
        IsVerified       = p.IsVerified,
        OwnerTenantId    = string.IsNullOrEmpty(p.OwnerTenantId)
                               ? null
                               : Guid.ParseExact(SurrealLink.FromLink(p.OwnerTenantId)!, "N"),
        ExternalUserId   = p.ExternalUserId,
        ExternalRef      = p.ExternalRef
    };

    // ── Inline POCO ───────────────────────────────────────────────────────────

    private sealed class SurrealAvatar : Oasis.SurrealDb.Client.ISurrealRecord
    {
        public string SchemaName => AvatarTable;

        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("password_hash")]
        public string PasswordHash { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("created_date")]
        public DateTimeOffset CreatedDate { get; set; }

        [JsonPropertyName("last_beamed_in_date")]
        public DateTimeOffset? LastBeamedInDate { get; set; }

        [JsonPropertyName("is_active")]
        public bool IsActive { get; set; } = true;

        [JsonPropertyName("is_verified")]
        public bool IsVerified { get; set; }

        [JsonPropertyName("owner_tenant_id")]
        public string? OwnerTenantId { get; set; }

        [JsonPropertyName("external_user_id")]
        public string? ExternalUserId { get; set; }

        [JsonPropertyName("external_ref")]
        public string? ExternalRef { get; set; }
    }
}
