using Microsoft.EntityFrameworkCore;
using OASIS.WebAPI.Data;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Interfaces.Stores;
using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Providers.Stores;

/// <summary>
/// EF-backed <see cref="IWalletStore"/>. Bodies lifted verbatim from
/// <c>EfStorageProvider</c> (Load/Save/Delete/LoadWalletsByAvatar/LoadAllWallets,
/// ~65-109).
/// </summary>
public sealed class EfWalletStore : IWalletStore
{
    private readonly OASISDbContext _db;

    public EfWalletStore(OASISDbContext db)
    {
        _db = db;
    }

    public async Task<OASISResult<IWallet>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var wallet = await _db.Wallets.FindAsync(new object[] { id }, ct);
        return new OASISResult<IWallet>
        {
            IsError = wallet == null,
            Message = wallet == null ? "Wallet not found." : "Success",
            Result = wallet
        };
    }

    public async Task<OASISResult<IEnumerable<IWallet>>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _db.Wallets.AsNoTracking().ToListAsync(ct);
        return new OASISResult<IEnumerable<IWallet>> { Result = list, Message = "Success" };
    }

    public async Task<OASISResult<IEnumerable<IWallet>>> GetByAvatarAsync(Guid avatarId, CancellationToken ct = default)
    {
        var list = await _db.Wallets.AsNoTracking().Where(w => w.AvatarId == avatarId).ToListAsync(ct);
        return new OASISResult<IEnumerable<IWallet>> { Result = list, Message = "Success" };
    }

    public async Task<OASISResult<IWallet>> UpsertAsync(IWallet wallet, CancellationToken ct = default)
    {
        var existing = await _db.Wallets.FindAsync(new object[] { wallet.Id }, ct);
        if (existing == null)
            _db.Wallets.Add((Wallet)wallet);
        else
            _db.Entry(existing).CurrentValues.SetValues((Wallet)wallet);

        await _db.SaveChangesAsync(ct);
        return new OASISResult<IWallet> { Result = wallet, Message = "Saved." };
    }

    public async Task<OASISResult<bool>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await _db.Wallets.FindAsync(new object[] { id }, ct);
        if (existing == null)
            return new OASISResult<bool> { IsError = true, Message = "Wallet not found.", Result = false };

        _db.Wallets.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return new OASISResult<bool> { Result = true, Message = "Deleted." };
    }
}
