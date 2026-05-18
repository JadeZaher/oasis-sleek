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
}
