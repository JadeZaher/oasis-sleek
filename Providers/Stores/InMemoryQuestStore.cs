using System.Collections.Concurrent;
using OASIS.WebAPI.Interfaces.Stores;
using OASIS.WebAPI.Models.Quest;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Providers.Stores;

/// <summary>
/// Thread-safe in-memory <see cref="IQuestStore"/>. Singleton-scoped via
/// <c>Program.cs</c>. Used as the default during the
/// <c>quest-temporal-fork-model</c> transition window; a SurrealDB-backed
/// implementation arrives with that track's runtime store landing
/// ([`surrealdb-migration` plan.md tasks 9–11]).
///
/// <para><b>Boundary with <see cref="IQuestTemplateStore"/>.</b> The newer
/// definition-side <see cref="IQuestTemplateStore"/> (Stream C2) handles
/// READ-ONLY template lookups consumed by <c>QuestInstantiator</c>. This
/// <see cref="IQuestStore"/> is the legacy unified surface that
/// <see cref="OASIS.WebAPI.Managers.QuestManager"/> still consumes for
/// instantiated <see cref="Quest"/> CRUD plus full template / node-template
/// authoring. Once <c>quest-temporal-fork-model</c> ships its Surreal store +
/// QuestManager refactor onto the per-aggregate seams, this in-memory adapter
/// can be deleted.</para>
///
/// <para><b>Data persistence caveat.</b> State is process-lifetime only. Loss
/// across restarts is acceptable for the transition window because
/// definition-side data is small + authored from configuration, and
/// instantiated quests are not yet user-visible. DO NOT keep this past the
/// fork-model landing.</para>
/// </summary>
public sealed class InMemoryQuestStore : IQuestStore
{
    private readonly ConcurrentDictionary<Guid, Quest> _quests = new();
    private readonly ConcurrentDictionary<Guid, QuestTemplate> _templates = new();
    private readonly ConcurrentDictionary<Guid, QuestNodeTemplate> _nodeTemplates = new();

    // ── Quest CRUD ────────────────────────────────────────────────────────────

    public Task<OASISResult<Quest>> GetQuestAsync(Guid id, CancellationToken ct = default)
    {
        if (_quests.TryGetValue(id, out var quest))
            return Task.FromResult(new OASISResult<Quest> { Result = quest, Message = "Success" });

        return Task.FromResult(new OASISResult<Quest>
        {
            IsError = true,
            Message = $"Quest {id} not found.",
            Result = null,
        });
    }

    public Task<OASISResult<IEnumerable<Quest>>> GetQuestsByAvatarAsync(
        Guid avatarId, CancellationToken ct = default)
    {
        var matches = _quests.Values.Where(q => q.AvatarId == avatarId).ToList();
        return Task.FromResult(new OASISResult<IEnumerable<Quest>>
        {
            Result = matches,
            Message = "Success",
        });
    }

    public Task<OASISResult<IEnumerable<Quest>>> GetQuestsByDappSeriesAsync(
        Guid dappSeriesId, CancellationToken ct = default)
    {
        var matches = _quests.Values
            .Where(q => q.DappSeriesId.HasValue && q.DappSeriesId.Value == dappSeriesId)
            .ToList();
        return Task.FromResult(new OASISResult<IEnumerable<Quest>>
        {
            Result = matches,
            Message = "Success",
        });
    }

    public Task<OASISResult<Quest>> UpsertQuestAsync(Quest quest, CancellationToken ct = default)
    {
        _quests[quest.Id] = quest;
        return Task.FromResult(new OASISResult<Quest> { Result = quest, Message = "Upserted." });
    }

    public Task<OASISResult<bool>> DeleteQuestAsync(Guid id, CancellationToken ct = default)
    {
        var removed = _quests.TryRemove(id, out _);
        return Task.FromResult(new OASISResult<bool>
        {
            Result = removed,
            Message = removed ? "Deleted." : $"Quest {id} not found.",
            IsError = !removed,
        });
    }

    // ── QuestTemplate CRUD ────────────────────────────────────────────────────

    public Task<OASISResult<QuestTemplate>> GetQuestTemplateAsync(Guid id, CancellationToken ct = default)
    {
        if (_templates.TryGetValue(id, out var template))
            return Task.FromResult(new OASISResult<QuestTemplate> { Result = template, Message = "Success" });

        return Task.FromResult(new OASISResult<QuestTemplate>
        {
            IsError = true,
            Message = $"QuestTemplate {id} not found.",
            Result = null,
        });
    }

    public Task<OASISResult<IEnumerable<QuestTemplate>>> GetAllQuestTemplatesAsync(
        CancellationToken ct = default)
    {
        var snapshot = _templates.Values.ToList();
        return Task.FromResult(new OASISResult<IEnumerable<QuestTemplate>>
        {
            Result = snapshot,
            Message = "Success",
        });
    }

    public Task<OASISResult<QuestTemplate>> UpsertQuestTemplateAsync(
        QuestTemplate template, CancellationToken ct = default)
    {
        _templates[template.Id] = template;
        return Task.FromResult(new OASISResult<QuestTemplate> { Result = template, Message = "Upserted." });
    }

    public Task<OASISResult<bool>> DeleteQuestTemplateAsync(Guid id, CancellationToken ct = default)
    {
        var removed = _templates.TryRemove(id, out _);
        return Task.FromResult(new OASISResult<bool>
        {
            Result = removed,
            Message = removed ? "Deleted." : $"QuestTemplate {id} not found.",
            IsError = !removed,
        });
    }

    // ── QuestNodeTemplate CRUD ────────────────────────────────────────────────

    public Task<OASISResult<QuestNodeTemplate>> UpsertQuestNodeTemplateAsync(
        QuestNodeTemplate template, CancellationToken ct = default)
    {
        _nodeTemplates[template.Id] = template;
        return Task.FromResult(new OASISResult<QuestNodeTemplate>
        {
            Result = template,
            Message = "Upserted.",
        });
    }

    public Task<OASISResult<IEnumerable<QuestNodeTemplate>>> GetAllQuestNodeTemplatesAsync(
        CancellationToken ct = default)
    {
        var snapshot = _nodeTemplates.Values.ToList();
        return Task.FromResult(new OASISResult<IEnumerable<QuestNodeTemplate>>
        {
            Result = snapshot,
            Message = "Success",
        });
    }
}
