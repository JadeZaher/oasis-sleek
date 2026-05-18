using System.Text.Json;
using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Interfaces.QuestExecution;
using OASIS.WebAPI.Models.Quest;
using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Services.Quest.Handlers;

/// <summary>Handles <see cref="QuestNodeType.WalletQuery"/> — relocated verbatim from QuestManager.</summary>
public sealed class WalletQueryNodeHandler : IQuestNodeHandler
{
    private readonly IWalletManager _walletManager;

    public WalletQueryNodeHandler(IWalletManager walletManager) => _walletManager = walletManager;

    public QuestNodeType NodeType => QuestNodeType.WalletQuery;

    public async Task<OASISResult<QuestNode>> HandleAsync(Models.Quest.Quest quest, QuestNode node, CancellationToken ct = default)
    {
        var query = JsonSerializer.Deserialize<WalletQueryRequest>(node.Config, QuestNodeJson.Options)!;
        var r = await _walletManager.QueryAsync(query);
        var outputJson = JsonSerializer.Serialize(r, QuestNodeJson.Options);
        if (r.IsError) return QuestNodeResults.Fail(node, r.Message);
        return QuestNodeResults.Ok(node, null, outputJson);
    }
}
