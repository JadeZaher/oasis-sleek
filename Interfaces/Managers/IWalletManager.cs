using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Interfaces.Managers;

public interface IWalletManager
{
    Task<OASISResult<IWallet>> GetAsync(Guid id, OASISRequest? request = null);
    Task<OASISResult<IEnumerable<IWallet>>> QueryAsync(WalletQueryRequest query, OASISRequest? request = null);
    Task<OASISResult<IWallet>> CreateAsync(WalletCreateModel model, Guid avatarId, OASISRequest? request = null);
    Task<OASISResult<IWallet>> UpdateAsync(Guid id, WalletUpdateModel model, OASISRequest? request = null);
    Task<OASISResult<bool>> DeleteAsync(Guid id, OASISRequest? request = null);
    Task<OASISResult<bool>> SetDefaultAsync(Guid avatarId, Guid walletId, OASISRequest? request = null);
    Task<OASISResult<PortfolioResult>> GetPortfolioAsync(Guid walletId, OASISRequest? request = null);

    /// <summary>
    /// Generate a new wallet on the platform for a chain (creates keypair, stores encrypted).
    /// </summary>
    Task<OASISResult<IWallet>> GenerateWalletAsync(WalletGenerateRequest model, Guid avatarId, OASISRequest? request = null);

    /// <summary>
    /// Connect an external wallet (e.g., MetaMask) by verifying signed message ownership.
    /// </summary>
    Task<OASISResult<IWallet>> ConnectWalletAsync(WalletConnectRequest model, Guid avatarId, OASISRequest? request = null);

    /// <summary>
    /// Export a platform-generated wallet's private key and seed phrase.
    /// Requires verification of avatar ownership.
    /// </summary>
    Task<OASISResult<WalletExportResult>> ExportWalletAsync(Guid walletId, Guid avatarId, OASISRequest? request = null);
}
