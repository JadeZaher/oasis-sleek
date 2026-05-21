using OASIS.WebAPI.Interfaces.Stores;
using OASIS.WebAPI.Models.Quest;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Providers.Stores;

/// <summary>
/// EF-backed <see cref="IQuestRunStore"/> stub. Intentionally NOT implemented:
/// the SurrealDB migration (<c>conductor/tracks/surrealdb-migration</c>) deletes
/// the EF/Postgres backplane in wave 3, including this adapter. The class is
/// kept as a typed placeholder so the DI seam compiles symmetrically with the
/// other per-aggregate stores; spending design time on Postgres mappings here
/// would be deleted before it ever ran.
/// </summary>
[Obsolete("removed by surrealdb-migration — see conductor/tracks/quest-temporal-fork-model/plan.md task 8")]
public sealed class EfQuestRunStore : IQuestRunStore
{
    public Task<OASISResult<QuestRun>> CreateAsync(QuestRun run, CancellationToken ct = default)
        => throw new NotImplementedException("EfQuestRunStore is a stub; use InMemoryQuestRunStore until surrealdb-migration ships the SurrealDB adapter.");

    public Task<OASISResult<QuestRun>> GetByIdAsync(Guid id, CancellationToken ct = default)
        => throw new NotImplementedException("EfQuestRunStore is a stub; use InMemoryQuestRunStore until surrealdb-migration ships the SurrealDB adapter.");

    public Task<OASISResult<QuestRun>> UpdateAsync(QuestRun run, CancellationToken ct = default)
        => throw new NotImplementedException("EfQuestRunStore is a stub; use InMemoryQuestRunStore until surrealdb-migration ships the SurrealDB adapter.");

    public Task<OASISResult<IEnumerable<QuestRun>>> GetByQuestIdAsync(Guid questId, CancellationToken ct = default)
        => throw new NotImplementedException("EfQuestRunStore is a stub; use InMemoryQuestRunStore until surrealdb-migration ships the SurrealDB adapter.");

    public Task<OASISResult<IEnumerable<QuestRun>>> GetByAvatarIdAsync(Guid avatarId, CancellationToken ct = default)
        => throw new NotImplementedException("EfQuestRunStore is a stub; use InMemoryQuestRunStore until surrealdb-migration ships the SurrealDB adapter.");

    public Task<OASISResult<IEnumerable<QuestRun>>> GetByStatusAsync(QuestRunStatus status, CancellationToken ct = default)
        => throw new NotImplementedException("EfQuestRunStore is a stub; use InMemoryQuestRunStore until surrealdb-migration ships the SurrealDB adapter.");

    public Task<OASISResult<IEnumerable<QuestRun>>> GetLineageAsync(Guid runId, CancellationToken ct = default)
        => throw new NotImplementedException("EfQuestRunStore is a stub; use InMemoryQuestRunStore until surrealdb-migration ships the SurrealDB adapter.");
}
