using Microsoft.EntityFrameworkCore;
using OASIS.WebAPI.Data;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Interfaces.Stores;
using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Providers.Stores;

/// <summary>
/// EF-backed <see cref="IBlockchainOperationStore"/>. Bodies lifted verbatim
/// from <c>EfStorageProvider</c> (Load/Save/Delete/LoadBlockchainOperationsByAvatar,
/// ~173-211).
/// </summary>
public sealed class EfBlockchainOperationStore : IBlockchainOperationStore
{
    private readonly OASISDbContext _db;

    public EfBlockchainOperationStore(OASISDbContext db)
    {
        _db = db;
    }

    public async Task<OASISResult<IBlockchainOperation>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var op = await _db.BlockchainOperations.FindAsync(new object[] { id }, ct);
        return new OASISResult<IBlockchainOperation>
        {
            IsError = op == null,
            Message = op == null ? "Operation not found." : "Success",
            Result = op
        };
    }

    public async Task<OASISResult<IEnumerable<IBlockchainOperation>>> GetByAvatarAsync(Guid avatarId, CancellationToken ct = default)
    {
        var list = await _db.BlockchainOperations.AsNoTracking().Where(o => o.AvatarId == avatarId).ToListAsync(ct);
        return new OASISResult<IEnumerable<IBlockchainOperation>> { Result = list, Message = "Success" };
    }

    public async Task<OASISResult<IBlockchainOperation>> UpsertAsync(IBlockchainOperation operation, CancellationToken ct = default)
    {
        var existing = await _db.BlockchainOperations.FindAsync(new object[] { operation.Id }, ct);
        if (existing == null)
            _db.BlockchainOperations.Add((BlockchainOperation)operation);
        else
            _db.Entry(existing).CurrentValues.SetValues((BlockchainOperation)operation);

        await _db.SaveChangesAsync(ct);
        return new OASISResult<IBlockchainOperation> { Result = operation, Message = "Saved." };
    }

    public async Task<OASISResult<bool>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await _db.BlockchainOperations.FindAsync(new object[] { id }, ct);
        if (existing == null)
            return new OASISResult<bool> { IsError = true, Message = "Operation not found.", Result = false };

        _db.BlockchainOperations.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return new OASISResult<bool> { Result = true, Message = "Deleted." };
    }
}
