using OASIS.WebAPI.Models.Quest;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Interfaces.Stores;

/// <summary>
/// Persistence boundary for <see cref="QuestNodeExecution"/> — the per-(run,
/// node) runtime record introduced by the quest-temporal-fork-model track.
/// Replaces in-place mutation of <see cref="QuestNode"/>.State/Output/Error.
/// See <c>conductor/tracks/quest-temporal-fork-model/ADR.md</c>.
/// </summary>
/// <remarks>
/// The natural key is <c>(RunId, NodeId)</c>. <see cref="TryClaimPendingAsync"/>
/// is the api-safety-hardening G2 conditional-update primitive (maps to the
/// SurrealDB <c>UPDATE … WHERE state = 'Pending' RETURN AFTER</c> pattern).
/// </remarks>
public interface IQuestNodeExecutionStore
{
    /// <summary>Inserts a new per-(run, node) execution row.</summary>
    Task<OASISResult<QuestNodeExecution>> CreateAsync(QuestNodeExecution execution, CancellationToken ct = default);

    /// <summary>Loads an execution by its own id.</summary>
    Task<OASISResult<QuestNodeExecution>> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Updates an existing execution (state transition, output/error capture, ended_at).</summary>
    Task<OASISResult<QuestNodeExecution>> UpdateAsync(QuestNodeExecution execution, CancellationToken ct = default);

    /// <summary>All executions for a single run, ordered by <see cref="QuestNodeExecution.StartedAt"/>.</summary>
    Task<OASISResult<IEnumerable<QuestNodeExecution>>> GetByRunIdAsync(Guid runId, CancellationToken ct = default);

    /// <summary>
    /// Exact-match lookup by the natural key <c>(runId, nodeId)</c>.
    /// <c>IsError</c> when no row exists.
    /// </summary>
    Task<OASISResult<QuestNodeExecution>> GetByRunAndNodeAsync(Guid runId, Guid nodeId, CancellationToken ct = default);

    /// <summary>
    /// G2 claim primitive: conditional update that only succeeds when current
    /// <see cref="QuestNodeExecution.State"/> equals
    /// <see cref="QuestNodeState.Pending"/>. Transitions to
    /// <see cref="QuestNodeState.Running"/> and stamps a fresh
    /// <see cref="QuestNodeExecution.StartedAt"/>.
    /// </summary>
    /// <returns>
    /// The claimed execution row on success. <c>Result == null</c> with
    /// <c>IsError == false</c> when the row exists but is not Pending (lost
    /// race — another worker already claimed it). <c>IsError == true</c>
    /// when the row does not exist at all.
    /// </returns>
    Task<OASISResult<QuestNodeExecution?>> TryClaimPendingAsync(Guid runId, Guid nodeId, CancellationToken ct = default);
}
