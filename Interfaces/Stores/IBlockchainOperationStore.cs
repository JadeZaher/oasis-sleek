using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Interfaces.Stores;

/// <summary>Persistence boundary for <see cref="IBlockchainOperation"/> aggregates.</summary>
public interface IBlockchainOperationStore
{
    /// <summary>Loads a single blockchain operation by id.</summary>
    Task<OASISResult<IBlockchainOperation>> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Loads all blockchain operations for an avatar.</summary>
    Task<OASISResult<IEnumerable<IBlockchainOperation>>> GetByAvatarAsync(Guid avatarId, CancellationToken ct = default);

    /// <summary>Inserts or updates a blockchain operation.</summary>
    Task<OASISResult<IBlockchainOperation>> UpsertAsync(IBlockchainOperation operation, CancellationToken ct = default);

    /// <summary>Deletes a blockchain operation by id.</summary>
    Task<OASISResult<bool>> DeleteAsync(Guid id, CancellationToken ct = default);
}
