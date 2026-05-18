using System.Text.Json;
using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Interfaces.QuestExecution;
using OASIS.WebAPI.Models.Quest;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Services.Quest.Handlers;

/// <summary>Handles <see cref="QuestNodeType.WalletGetPortfolio"/> — relocated verbatim from QuestManager.</summary>
public sealed class WalletGetPortfolioNodeHandler : IQuestNodeHandler
{
    private readonly IWalletManager _walletManager;

    public WalletGetPortfolioNodeHandler(IWalletManager walletManager) => _walletManager = walletManager;

    public QuestNodeType NodeType => QuestNodeType.WalletGetPortfolio;

    public async Task<OASISResult<QuestNode>> HandleAsync(Models.Quest.Quest quest, QuestNode node, CancellationToken ct = default)
    {
        var cfg = JsonSerializer.Deserialize<IdConfig>(node.Config, QuestNodeJson.Options)!;
        var r = await _walletManager.GetPortfolioAsync(cfg.Id);
        var outputJson = JsonSerializer.Serialize(r, QuestNodeJson.Options);
        if (r.IsError) return QuestNodeResults.Fail(node, r.Message);
        return QuestNodeResults.Ok(node, null, outputJson);
    }
}
