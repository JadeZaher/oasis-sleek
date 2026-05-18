using OASIS.WebAPI.Models.Quest;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Interfaces.QuestExecution;

/// <summary>
/// Executes one <see cref="QuestNodeType"/>; exactly one handler per type.
/// <see cref="HandleAsync"/> returns the node with State/Output/Error set,
/// matching the current <c>QuestManager.ExecuteNodeInternalAsync</c> contract.
/// </summary>
public interface IQuestNodeHandler
{
    /// <summary>The single node type this handler dispatches.</summary>
    QuestNodeType NodeType { get; }

    /// <summary>Executes <paramref name="node"/> within <paramref name="quest"/>; returns the node with State/Output/Error populated.</summary>
    Task<OASISResult<QuestNode>> HandleAsync(Models.Quest.Quest quest, QuestNode node, CancellationToken ct = default);
}
