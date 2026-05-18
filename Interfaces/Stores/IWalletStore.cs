using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Interfaces.Stores;

/// <summary>Persistence boundary for <see cref="IWallet"/> aggregates.</summary>
public interface IWalletStore
{
    /// <summary>Loads a single wallet by id.</summary>
    Task<OASISResult<IWallet>> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Loads every wallet.</summary>
    Task<OASISResult<IEnumerable<IWallet>>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Loads all wallets owned by an avatar.</summary>
    Task<OASISResult<IEnumerable<IWallet>>> GetByAvatarAsync(Guid avatarId, CancellationToken ct = default);

    /// <summary>Inserts or updates a wallet.</summary>
    Task<OASISResult<IWallet>> UpsertAsync(IWallet wallet, CancellationToken ct = default);

    /// <summary>Deletes a wallet by id.</summary>
    Task<OASISResult<bool>> DeleteAsync(Guid id, CancellationToken ct = default);
}
