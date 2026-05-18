using Microsoft.EntityFrameworkCore;
using OASIS.WebAPI.Data;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Interfaces.Stores;
using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Providers.Stores;

/// <summary>
/// EF-backed <see cref="IAvatarStore"/>. Bodies lifted verbatim from
/// <c>EfStorageProvider</c> (Load/Save/Delete/LoadAllAvatarsAsync, ~24-62).
/// </summary>
public sealed class EfAvatarStore : IAvatarStore
{
    private readonly OASISDbContext _db;

    public EfAvatarStore(OASISDbContext db)
    {
        _db = db;
    }

    public async Task<OASISResult<IAvatar>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var avatar = await _db.Avatars.FindAsync(new object[] { id }, ct);
        return new OASISResult<IAvatar>
        {
            IsError = avatar == null,
            Message = avatar == null ? "Avatar not found." : "Success",
            Result = avatar
        };
    }

    public async Task<OASISResult<IEnumerable<IAvatar>>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _db.Avatars.AsNoTracking().ToListAsync(ct);
        return new OASISResult<IEnumerable<IAvatar>> { Result = list, Message = "Success" };
    }

    public async Task<OASISResult<IAvatar>> UpsertAsync(IAvatar avatar, CancellationToken ct = default)
    {
        var existing = await _db.Avatars.FindAsync(new object[] { avatar.Id }, ct);
        if (existing == null)
            _db.Avatars.Add((Avatar)avatar);
        else
            _db.Entry(existing).CurrentValues.SetValues((Avatar)avatar);

        await _db.SaveChangesAsync(ct);
        return new OASISResult<IAvatar> { Result = avatar, Message = "Saved." };
    }

    public async Task<OASISResult<bool>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await _db.Avatars.FindAsync(new object[] { id }, ct);
        if (existing == null)
            return new OASISResult<bool> { IsError = true, Message = "Avatar not found.", Result = false };

        _db.Avatars.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return new OASISResult<bool> { Result = true, Message = "Deleted." };
    }
}
