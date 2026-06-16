using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Interfaces.Stores;

/// <summary>Persistence boundary for <see cref="IAvatar"/> aggregates.</summary>
public interface IAvatarStore
{
    /// <summary>Loads a single avatar by id.</summary>
    Task<OASISResult<IAvatar>> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Loads every avatar.</summary>
    Task<OASISResult<IEnumerable<IAvatar>>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Inserts or updates an avatar.</summary>
    Task<OASISResult<IAvatar>> UpsertAsync(IAvatar avatar, CancellationToken ct = default);

    /// <summary>Deletes an avatar by id.</summary>
    Task<OASISResult<bool>> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Lists every child avatar owned by the given tenant principal (matched on
    /// <c>owner_tenant_id</c>). Owner-scoped: returns ONLY that tenant's rows —
    /// never another tenant's children. Mirrors the
    /// <see cref="IApiKeyStore.ListByAvatarAsync"/> owner-scoped query.
    /// </summary>
    Task<OASISResult<IEnumerable<IAvatar>>> ListByOwnerTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Resolves a single child avatar by the tenant's own external user id,
    /// scoped to that tenant (<c>owner_tenant_id = tenant AND external_user_id =
    /// externalUserId</c>). Returns <c>Result == null</c> with no error when no
    /// match exists — the manager interprets that as "create new" (idempotency),
    /// mirroring <see cref="ISTARStore.GetByNameAndAvatarAsync"/>.
    /// </summary>
    Task<OASISResult<IAvatar>> GetByTenantAndExternalUserAsync(Guid tenantId, string externalUserId, CancellationToken ct = default);
}
