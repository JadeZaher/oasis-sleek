using System.Text.Json;
using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Interfaces.QuestExecution;
using OASIS.WebAPI.Models.Quest;

namespace OASIS.WebAPI.Services.Quest.Handlers;

/// <summary>
/// Handles <see cref="QuestNodeType.Transfer"/> — Tier-2 chain action (move-to-actor).
/// Mechanism only: wraps <see cref="INftManager.TransferAsync"/> with the actor
/// avatar taken from the run context (the config body carries no avatar). Distinct
/// from the legacy <see cref="QuestNodeType.NftTransfer"/> handler: this is the
/// capability-gated Tier-2 node. Requires a chain capability.
/// </summary>
public sealed class TransferNodeHandler : IQuestNodeHandler
{
    private readonly INftManager _nftManager;

    public TransferNodeHandler(INftManager nftManager) => _nftManager = nftManager;

    public QuestNodeType NodeType => QuestNodeType.Transfer;

    public bool RequiresChainCapability => true;

    public async Task<QuestNodeHandlerResult> HandleAsync(QuestNodeExecutionContext context, CancellationToken ct = default)
    {
        var cfg = JsonSerializer.Deserialize<TransferNodeConfig>(context.Node.Config, QuestNodeJson.Options)!;
        var r = await _nftManager.TransferAsync(cfg.NftId, cfg.Request, context.Quest.AvatarId);
        var outputJson = JsonSerializer.Serialize(r, QuestNodeJson.Options);
        if (r.IsError) return QuestNodeResults.Fail(r.Message);
        return QuestNodeResults.Ok(outputJson);
    }
}
