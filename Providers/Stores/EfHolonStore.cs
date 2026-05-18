using Microsoft.EntityFrameworkCore;
using OASIS.WebAPI.Data;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Interfaces.Stores;
using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Providers.Stores;

/// <summary>
/// EF-backed <see cref="IHolonStore"/>. Bodies lifted verbatim from
/// <c>EfStorageProvider</c> (Load/Save/Delete/LoadAllHolonsAsync, ~112-170).
/// <see cref="QueryAsync"/> reproduces the full <c>LoadAllHolonsAsync</c>
/// filter logic; a null query is the unfiltered all-holons path (identical to
/// the source's <c>query == null</c> branch).
/// </summary>
public sealed class EfHolonStore : IHolonStore
{
    private readonly OASISDbContext _db;

    public EfHolonStore(OASISDbContext db)
    {
        _db = db;
    }

    public async Task<OASISResult<IHolon>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var holon = await _db.Holons.FindAsync(new object[] { id }, ct);
        return new OASISResult<IHolon>
        {
            IsError = holon == null,
            Message = holon == null ? "Holon not found." : "Success",
            Result = holon
        };
    }

    public async Task<OASISResult<IEnumerable<IHolon>>> QueryAsync(HolonQueryRequest? query = null, CancellationToken ct = default)
    {
        var q = _db.Holons.AsNoTracking().AsQueryable();

        if (query != null)
        {
            if (!string.IsNullOrEmpty(query.Name))
                q = q.Where(h => h.Name.Contains(query.Name));
            if (query.AvatarId.HasValue)
                q = q.Where(h => h.AvatarId == query.AvatarId);
            if (!string.IsNullOrEmpty(query.ProviderName))
                q = q.Where(h => h.ProviderName == query.ProviderName);
            if (!string.IsNullOrEmpty(query.ChainId))
                q = q.Where(h => h.ChainId == query.ChainId);
            if (!string.IsNullOrEmpty(query.AssetType))
                q = q.Where(h => h.AssetType == query.AssetType);
            if (query.IsActive.HasValue)
                q = q.Where(h => h.IsActive == query.IsActive.Value);
            if (query.ParentHolonId.HasValue)
                q = q.Where(h => h.ParentHolonId == query.ParentHolonId.Value);
        }

        var list = await q.ToListAsync(ct);
        return new OASISResult<IEnumerable<IHolon>> { Result = list, Message = "Success" };
    }

    public async Task<OASISResult<IHolon>> UpsertAsync(IHolon holon, CancellationToken ct = default)
    {
        var existing = await _db.Holons.FindAsync(new object[] { holon.Id }, ct);
        if (existing == null)
            _db.Holons.Add((Holon)holon);
        else
            _db.Entry(existing).CurrentValues.SetValues((Holon)holon);

        await _db.SaveChangesAsync(ct);
        return new OASISResult<IHolon> { Result = holon, Message = "Saved." };
    }

    public async Task<OASISResult<bool>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await _db.Holons.FindAsync(new object[] { id }, ct);
        if (existing == null)
            return new OASISResult<bool> { IsError = true, Message = "Holon not found.", Result = false };

        _db.Holons.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return new OASISResult<bool> { Result = true, Message = "Deleted." };
    }
}
