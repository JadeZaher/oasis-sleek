using System.Text.Json;
using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Interfaces.QuestExecution;
using OASIS.WebAPI.Models.Quest;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Services.Quest.Handlers;

/// <summary>Handles <see cref="QuestNodeType.HolonGetPeers"/> — relocated verbatim from QuestManager.</summary>
public sealed class HolonGetPeersNodeHandler : IQuestNodeHandler
{
    private readonly IHolonManager _holonManager;

    public HolonGetPeersNodeHandler(IHolonManager holonManager) => _holonManager = holonManager;

    public QuestNodeType NodeType => QuestNodeType.HolonGetPeers;

    public async Task<OASISResult<QuestNode>> HandleAsync(Models.Quest.Quest quest, QuestNode node, CancellationToken ct = default)
    {
        var cfg = JsonSerializer.Deserialize<IdConfig>(node.Config, QuestNodeJson.Options)!;
        var r = await _holonManager.GetPeersAsync(cfg.Id);
        var outputJson = JsonSerializer.Serialize(r, QuestNodeJson.Options);
        if (r.IsError) return QuestNodeResults.Fail(node, r.Message);
        return QuestNodeResults.Ok(node, null, outputJson);
    }
}
