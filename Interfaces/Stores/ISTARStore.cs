using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Interfaces.Stores;

/// <summary>Persistence boundary for <see cref="ISTARODK"/> aggregates.</summary>
public interface ISTARStore
{
    /// <summary>Loads a single STAR ODK by id.</summary>
    Task<OASISResult<ISTARODK>> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Loads the STAR ODK matching <paramref name="name"/> (case-insensitive)
    /// AND owned by <paramref name="avatarId"/>. Returns <c>Result == null</c>
    /// (no error) when no match exists — the manager interprets that as
    /// "create new". This is the IDOR-safe lookup the manager uses on POST.
    /// </summary>
    Task<OASISResult<ISTARODK>> GetByNameAndAvatarAsync(string name, Guid avatarId, CancellationToken ct = default);

    /// <summary>Loads every STAR ODK.</summary>
    Task<OASISResult<IEnumerable<ISTARODK>>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Inserts or updates a STAR ODK.</summary>
    Task<OASISResult<ISTARODK>> UpsertAsync(ISTARODK odk, CancellationToken ct = default);

    /// <summary>Deletes a STAR ODK by id.</summary>
    Task<OASISResult<bool>> DeleteAsync(Guid id, CancellationToken ct = default);
}
