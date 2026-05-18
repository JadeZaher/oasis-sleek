using System.Text.Json;
using OASIS.WebAPI.Interfaces.QuestExecution;
using OASIS.WebAPI.Models.Quest;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Services.Quest.Handlers;

/// <summary>
/// Handles <see cref="QuestNodeType.ComposeOutputs"/> — relocated verbatim from
/// QuestManager. Gathers outputs from all upstream nodes. No manager dependency.
/// </summary>
public sealed class ComposeOutputsNodeHandler : IQuestNodeHandler
{
    public QuestNodeType NodeType => QuestNodeType.ComposeOutputs;

    public Task<OASISResult<QuestNode>> HandleAsync(Models.Quest.Quest quest, QuestNode node, CancellationToken ct = default)
    {
        // Gather outputs from all upstream nodes
        var incomingNodeIds = quest.Edges
            .Where(e => e.TargetNodeId == node.Id)
            .Select(e => e.SourceNodeId)
            .ToHashSet();
        var upstreamOutputs = quest.Nodes
            .Where(n => incomingNodeIds.Contains(n.Id) && n.Output != null)
            .ToDictionary(n => n.Name, n => n.Output!);
        var outputJson = JsonSerializer.Serialize(upstreamOutputs, QuestNodeJson.Options);
        return Task.FromResult(QuestNodeResults.Ok(node, null, outputJson));
    }
}
