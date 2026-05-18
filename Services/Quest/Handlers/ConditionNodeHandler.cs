using OASIS.WebAPI.Interfaces.QuestExecution;
using OASIS.WebAPI.Models.Quest;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Services.Quest.Handlers;

/// <summary>
/// Handles <see cref="QuestNodeType.Condition"/> — relocated verbatim from QuestManager.
/// Condition nodes evaluate to a pass-through; the edge conditions on outgoing
/// edges handle the actual branching. No manager dependency.
/// </summary>
public sealed class ConditionNodeHandler : IQuestNodeHandler
{
    public QuestNodeType NodeType => QuestNodeType.Condition;

    public Task<OASISResult<QuestNode>> HandleAsync(Models.Quest.Quest quest, QuestNode node, CancellationToken ct = default)
    {
        // Condition nodes evaluate to a pass-through; the edge conditions
        // on outgoing edges handle the actual branching.
        var outputJson = node.Config;
        return Task.FromResult(QuestNodeResults.Ok(node, null, outputJson));
    }
}
