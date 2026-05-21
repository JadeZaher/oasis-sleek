using OASIS.WebAPI.Models.Quest;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Interfaces.Stores;

/// <summary>
/// Persistence boundary for <see cref="QuestRun"/> — the per-attempt runtime
/// counterpart to the immutable <see cref="Quest"/> definition. Introduced by
/// the quest-temporal-fork-model track; see
/// <c>conductor/tracks/quest-temporal-fork-model/ADR.md</c>.
/// </summary>
/// <remarks>
/// Lineage forms a tree via <see cref="QuestRun.ParentRunId"/>; there is no
/// merge-of-forks (deliberate non-goal). <see cref="GetLineageAsync"/> walks
/// the chain in child-to-root order.
/// </remarks>
public interface IQuestRunStore
{
    /// <summary>Inserts a new run row.</summary>
    Task<OASISResult<QuestRun>> CreateAsync(QuestRun run, CancellationToken ct = default);

    /// <summary>Loads a single run by id; <c>IsError</c> when not found.</summary>
    Task<OASISResult<QuestRun>> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Updates an existing run (status transition, ended_at, fork fields, etc.).</summary>
    Task<OASISResult<QuestRun>> UpdateAsync(QuestRun run, CancellationToken ct = default);

    /// <summary>All runs for a single <see cref="Quest"/> definition.</summary>
    Task<OASISResult<IEnumerable<QuestRun>>> GetByQuestIdAsync(Guid questId, CancellationToken ct = default);

    /// <summary>All runs initiated by an avatar across any quest.</summary>
    Task<OASISResult<IEnumerable<QuestRun>>> GetByAvatarIdAsync(Guid avatarId, CancellationToken ct = default);

    /// <summary>All runs currently in a given status (e.g. <see cref="QuestRunStatus.Running"/>).</summary>
    Task<OASISResult<IEnumerable<QuestRun>>> GetByStatusAsync(QuestRunStatus status, CancellationToken ct = default);

    /// <summary>
    /// Returns the ancestor chain of a run, starting with the run itself and
    /// walking <see cref="QuestRun.ParentRunId"/> until null. Child-to-root
    /// order. <c>IsError</c> if <paramref name="runId"/> does not exist.
    /// </summary>
    Task<OASISResult<IEnumerable<QuestRun>>> GetLineageAsync(Guid runId, CancellationToken ct = default);
}
