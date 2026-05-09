using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Interfaces;

public interface IOASISStorageProviderNFTExtensions
{
    // Avatar NFT Management
    Task<OASISResult<IAvatarNFT>> SaveAvatarNFTAsync(IAvatarNFT avatarNFT, CancellationToken ct = default);
    Task<OASISResult<IAvatarNFT>> LoadAvatarNFTAsync(Guid id, CancellationToken ct = default);
    Task<OASISResult<IAvatarNFT>> LoadAvatarNFTByTokenIdAsync(string chainType, string nftContractAddress, string tokenId, CancellationToken ct = default);
    Task<OASISResult<IEnumerable<IAvatarNFT>>> LoadAvatarNFTsByAvatarAsync(Guid avatarId, CancellationToken ct = default);
    Task<OASISResult<bool>> DeleteAvatarNFTAsync(Guid id, CancellationToken ct = default);
    
    // Holon NFT Binding Management
    Task<OASISResult<IHolonNFTBinding>> SaveHolonNFTBindingAsync(IHolonNFTBinding binding, CancellationToken ct = default);
    Task<OASISResult<IHolonNFTBinding>> LoadHolonNFTBindingAsync(Guid id, CancellationToken ct = default);
    Task<OASISResult<IEnumerable<IHolonNFTBinding>>> LoadHolonNFTBindingsByAvatarNFTAsync(Guid avatarNFTId, CancellationToken ct = default);
    Task<OASISResult<bool>> DeleteHolonNFTBindingAsync(Guid id, CancellationToken ct = default);
    
    // Wallet NFT Binding Management
    Task<OASISResult<IWalletNFTBinding>> SaveWalletNFTBindingAsync(IWalletNFTBinding binding, CancellationToken ct = default);
    Task<OASISResult<IWalletNFTBinding>> LoadWalletNFTBindingAsync(Guid id, CancellationToken ct = default);
    Task<OASISResult<IEnumerable<IWalletNFTBinding>>> LoadWalletNFTBindingsByAvatarNFTAsync(Guid avatarNFTId, CancellationToken ct = default);
    Task<OASISResult<bool>> DeleteWalletNFTBindingAsync(Guid id, CancellationToken ct = default);
}