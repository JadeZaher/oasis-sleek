using Microsoft.EntityFrameworkCore;
using OASIS.WebAPI.Data;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Interfaces.Stores;
using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Providers.Stores;

/// <summary>
/// EF-backed <see cref="ISTARStore"/>. Bodies lifted verbatim from
/// <c>EfStorageProvider</c> (Load/Save/Delete/LoadAllSTARODKs, ~214-252).
/// </summary>
public sealed class EfStarStore : ISTARStore
{
    private readonly OASISDbContext _db;

    public EfStarStore(OASISDbContext db)
    {
        _db = db;
    }

    public async Task<OASISResult<ISTARODK>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var odk = await _db.STARODKs.FindAsync(new object[] { id }, ct);
        return new OASISResult<ISTARODK>
        {
            IsError = odk == null,
            Message = odk == null ? "STAR ODK not found." : "Success",
            Result = odk
        };
    }

    public async Task<OASISResult<IEnumerable<ISTARODK>>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _db.STARODKs.AsNoTracking().ToListAsync(ct);
        return new OASISResult<IEnumerable<ISTARODK>> { Result = list, Message = "Success" };
    }

    public async Task<OASISResult<ISTARODK>> UpsertAsync(ISTARODK odk, CancellationToken ct = default)
    {
        var existing = await _db.STARODKs.FindAsync(new object[] { odk.Id }, ct);
        if (existing == null)
            _db.STARODKs.Add((STARODK)odk);
        else
            _db.Entry(existing).CurrentValues.SetValues((STARODK)odk);

        await _db.SaveChangesAsync(ct);
        return new OASISResult<ISTARODK> { Result = odk, Message = "Saved." };
    }

    public async Task<OASISResult<bool>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await _db.STARODKs.FindAsync(new object[] { id }, ct);
        if (existing == null)
            return new OASISResult<bool> { IsError = true, Message = "STAR ODK not found.", Result = false };

        _db.STARODKs.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return new OASISResult<bool> { Result = true, Message = "Deleted." };
    }
}
