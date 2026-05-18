using System.Text.Json;
using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Interfaces.QuestExecution;
using OASIS.WebAPI.Models.Quest;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Services.Quest.Handlers;

/// <summary>Handles <see cref="QuestNodeType.AvatarNFTGetComposite"/> — relocated verbatim from QuestManager.</summary>
public sealed class AvatarNFTGetCompositeNodeHandler : IQuestNodeHandler
{
    private readonly IAvatarNFTService _avatarNFTService;

    public AvatarNFTGetCompositeNodeHandler(IAvatarNFTService avatarNFTService) => _avatarNFTService = avatarNFTService;

    public QuestNodeType NodeType => QuestNodeType.AvatarNFTGetComposite;

    public async Task<OASISResult<QuestNode>> HandleAsync(Models.Quest.Quest quest, QuestNode node, CancellationToken ct = default)
    {
        var cfg = JsonSerializer.Deserialize<IdConfig>(node.Config, QuestNodeJson.Options)!;
        var r = await _avatarNFTService.GetAvatarNFTCompositeAsync(cfg.Id);
        var outputJson = JsonSerializer.Serialize(r, QuestNodeJson.Options);
        if (r.IsError) return QuestNodeResults.Fail(node, r.Message);
        return QuestNodeResults.Ok(node, null, outputJson);
    }
}
