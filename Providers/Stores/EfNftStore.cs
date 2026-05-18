using Microsoft.EntityFrameworkCore;
using OASIS.WebAPI.Data;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Interfaces.Stores;
using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Providers.Stores;

/// <summary>
/// EF-backed <see cref="INftStore"/>. All 13 NFT-extension bodies lifted
/// verbatim from <c>EfStorageProvider</c>'s NFT region (~255-390): AvatarNFT
/// management plus Holon/Wallet NFT-binding management. Method names map
/// Upsert→Save and Get→Load; the EF queries / OASISResult wrapping /
/// tracking choices are unchanged.
/// </summary>
public sealed class EfNftStore : INftStore
{
    private readonly OASISDbContext _db;

    public EfNftStore(OASISDbContext db)
    {
        _db = db;
    }

    // Avatar NFT Management (lifted from ~255-308)
    public async Task<OASISResult<IAvatarNFT>> UpsertAvatarNFTAsync(IAvatarNFT avatarNFT, CancellationToken ct = default)
    {
        var existing = await _db.AvatarNFTs.FindAsync(new object[] { avatarNFT.Id }, ct);
        if (existing == null)
            _db.AvatarNFTs.Add((AvatarNFT)avatarNFT);
        else
            _db.Entry(existing).CurrentValues.SetValues((AvatarNFT)avatarNFT);

        await _db.SaveChangesAsync(ct);
        return new OASISResult<IAvatarNFT> { Result = avatarNFT, Message = "Saved." };
    }

    public async Task<OASISResult<IAvatarNFT>> GetAvatarNFTByIdAsync(Guid id, CancellationToken ct = default)
    {
        var nft = await _db.AvatarNFTs.FindAsync(new object[] { id }, ct);
        return new OASISResult<IAvatarNFT>
        {
            IsError = nft == null,
            Message = nft == null ? "NFT not found." : "Success",
            Result = nft
        };
    }

    public async Task<OASISResult<IAvatarNFT>> GetAvatarNFTByTokenIdAsync(string chainType, string nftContractAddress, string tokenId, CancellationToken ct = default)
    {
        var nft = await _db.AvatarNFTs
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.ChainType == chainType &&
                                   n.NFTContractAddress == nftContractAddress &&
                                   n.TokenId == tokenId, ct);
        return new OASISResult<IAvatarNFT>
        {
            IsError = nft == null,
            Message = nft == null ? "NFT not found." : "Success",
            Result = nft
        };
    }

    public async Task<OASISResult<IEnumerable<IAvatarNFT>>> GetAvatarNFTsByAvatarAsync(Guid avatarId, CancellationToken ct = default)
    {
        var list = await _db.AvatarNFTs.AsNoTracking().Where(n => n.AvatarId == avatarId).ToListAsync(ct);
        return new OASISResult<IEnumerable<IAvatarNFT>> { Result = list, Message = "Success" };
    }

    public async Task<OASISResult<bool>> DeleteAvatarNFTAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await _db.AvatarNFTs.FindAsync(new object[] { id }, ct);
        if (existing == null)
            return new OASISResult<bool> { IsError = true, Message = "NFT not found.", Result = false };

        _db.AvatarNFTs.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return new OASISResult<bool> { Result = true, Message = "Deleted." };
    }

    // Holon NFT Binding Management (lifted from ~310-349)
    public async Task<OASISResult<IHolonNFTBinding>> UpsertHolonNFTBindingAsync(IHolonNFTBinding binding, CancellationToken ct = default)
    {
        var existing = await _db.HolonNFTBindings.FindAsync(new object[] { binding.Id }, ct);
        if (existing == null)
            _db.HolonNFTBindings.Add((HolonNFTBinding)binding);
        else
            _db.Entry(existing).CurrentValues.SetValues((HolonNFTBinding)binding);

        await _db.SaveChangesAsync(ct);
        return new OASISResult<IHolonNFTBinding> { Result = binding, Message = "Saved." };
    }

    public async Task<OASISResult<IHolonNFTBinding>> GetHolonNFTBindingByIdAsync(Guid id, CancellationToken ct = default)
    {
        var binding = await _db.HolonNFTBindings.FindAsync(new object[] { id }, ct);
        return new OASISResult<IHolonNFTBinding>
        {
            IsError = binding == null,
            Message = binding == null ? "Binding not found." : "Success",
            Result = binding
        };
    }

    public async Task<OASISResult<IEnumerable<IHolonNFTBinding>>> GetHolonNFTBindingsByAvatarNFTAsync(Guid avatarNFTId, CancellationToken ct = default)
    {
        var list = await _db.HolonNFTBindings.AsNoTracking().Where(b => b.AvatarNFTId == avatarNFTId).ToListAsync(ct);
        return new OASISResult<IEnumerable<IHolonNFTBinding>> { Result = list, Message = "Success" };
    }

    public async Task<OASISResult<bool>> DeleteHolonNFTBindingAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await _db.HolonNFTBindings.FindAsync(new object[] { id }, ct);
        if (existing == null)
            return new OASISResult<bool> { IsError = true, Message = "Binding not found.", Result = false };

        _db.HolonNFTBindings.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return new OASISResult<bool> { Result = true, Message = "Deleted." };
    }

    // Wallet NFT Binding Management (lifted from ~351-390)
    public async Task<OASISResult<IWalletNFTBinding>> UpsertWalletNFTBindingAsync(IWalletNFTBinding binding, CancellationToken ct = default)
    {
        var existing = await _db.WalletNFTBindings.FindAsync(new object[] { binding.Id }, ct);
        if (existing == null)
            _db.WalletNFTBindings.Add((WalletNFTBinding)binding);
        else
            _db.Entry(existing).CurrentValues.SetValues((WalletNFTBinding)binding);

        await _db.SaveChangesAsync(ct);
        return new OASISResult<IWalletNFTBinding> { Result = binding, Message = "Saved." };
    }

    public async Task<OASISResult<IWalletNFTBinding>> GetWalletNFTBindingByIdAsync(Guid id, CancellationToken ct = default)
    {
        var binding = await _db.WalletNFTBindings.FindAsync(new object[] { id }, ct);
        return new OASISResult<IWalletNFTBinding>
        {
            IsError = binding == null,
            Message = binding == null ? "Binding not found." : "Success",
            Result = binding
        };
    }

    public async Task<OASISResult<IEnumerable<IWalletNFTBinding>>> GetWalletNFTBindingsByAvatarNFTAsync(Guid avatarNFTId, CancellationToken ct = default)
    {
        var list = await _db.WalletNFTBindings.AsNoTracking().Where(b => b.AvatarNFTId == avatarNFTId).ToListAsync(ct);
        return new OASISResult<IEnumerable<IWalletNFTBinding>> { Result = list, Message = "Success" };
    }

    public async Task<OASISResult<bool>> DeleteWalletNFTBindingAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await _db.WalletNFTBindings.FindAsync(new object[] { id }, ct);
        if (existing == null)
            return new OASISResult<bool> { IsError = true, Message = "Binding not found.", Result = false };

        _db.WalletNFTBindings.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return new OASISResult<bool> { Result = true, Message = "Deleted." };
    }
}
