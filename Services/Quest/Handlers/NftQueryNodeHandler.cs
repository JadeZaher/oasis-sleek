using System.Text.Json;
using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Interfaces.QuestExecution;
using OASIS.WebAPI.Models.Quest;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Services.Quest.Handlers;

/// <summary>Handles <see cref="QuestNodeType.NftQuery"/> — relocated verbatim from QuestManager.</summary>
public sealed class NftQueryNodeHandler : IQuestNodeHandler
{
    private readonly INftManager _nftManager;

    public NftQueryNodeHandler(INftManager nftManager) => _nftManager = nftManager;

    public QuestNodeType NodeType => QuestNodeType.NftQuery;

    public async Task<OASISResult<QuestNode>> HandleAsync(Models.Quest.Quest quest, QuestNode node, CancellationToken ct = default)
    {
        var query = JsonSerializer.Deserialize<NftQueryRequest>(node.Config, QuestNodeJson.Options)!;
        var r = await _nftManager.QueryAsync(query);
        var outputJson = JsonSerializer.Serialize(r, QuestNodeJson.Options);
        if (r.IsError) return QuestNodeResults.Fail(node, r.Message);
        return QuestNodeResults.Ok(node, null, outputJson);
    }
}
