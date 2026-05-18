using Microsoft.EntityFrameworkCore;
using OASIS.WebAPI.Data;
using OASIS.WebAPI.Interfaces.Stores;
using OASIS.WebAPI.Models.Quest;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Providers.Stores;

/// <summary>
/// EF-backed <see cref="IQuestStore"/>. Bodies lifted verbatim from
/// <c>EfStorageProvider</c>'s Quest region (~393-551): Quest + QuestTemplate +
/// QuestNodeTemplate. <see cref="UpsertQuestAsync"/> reproduces the
/// <c>SaveQuestAsync</c> graph child-collection sync (~393-421) EXACTLY
/// (RemoveRange Nodes/Edges/Dependencies, then re-add with QuestId stamped)
/// — a naive <c>Update(quest)</c> would orphan/duplicate child rows.
/// <see cref="GetQuestsByDappSeriesAsync"/> mirrors
/// <c>QuestRepository.GetByDappSeriesIdAsync</c> semantics (Include
/// Nodes/Edges, Where DappSeriesId).
/// </summary>
public sealed class EfQuestStore : IQuestStore
{
    private readonly OASISDbContext _db;

    public EfQuestStore(OASISDbContext db)
    {
        _db = db;
    }

    // Quest (lifted from ~393-465)
    public async Task<OASISResult<Quest>> GetQuestAsync(Guid id, CancellationToken ct = default)
    {
        var quest = await _db.Quests
            .Include(q => q.Nodes)
            .Include(q => q.Edges)
            .Include(q => q.Dependencies)
            .FirstOrDefaultAsync(q => q.Id == id, ct);

        return new OASISResult<Quest>
        {
            IsError = quest == null,
            Message = quest == null ? "Quest not found." : "Success",
            Result = quest
        };
    }

    public async Task<OASISResult<IEnumerable<Quest>>> GetQuestsByAvatarAsync(Guid avatarId, CancellationToken ct = default)
    {
        var list = await _db.Quests
            .Include(q => q.Nodes)
            .Include(q => q.Edges)
            .Include(q => q.Dependencies)
            .Where(q => q.AvatarId == avatarId)
            .ToListAsync(ct);

        return new OASISResult<IEnumerable<Quest>> { Result = list, Message = "Success" };
    }

    // Mirrors QuestRepository.GetByDappSeriesIdAsync (Include Nodes/Edges,
    // Where DappSeriesId) — not present in EfStorageProvider; wrapped in the
    // store's standard OASISResult success envelope.
    public async Task<OASISResult<IEnumerable<Quest>>> GetQuestsByDappSeriesAsync(Guid dappSeriesId, CancellationToken ct = default)
    {
        var list = await _db.Quests
            .Include(q => q.Nodes)
            .Include(q => q.Edges)
            .Where(q => q.DappSeriesId == dappSeriesId)
            .ToListAsync(ct);

        return new OASISResult<IEnumerable<Quest>> { Result = list, Message = "Success" };
    }

    public async Task<OASISResult<Quest>> UpsertQuestAsync(Quest quest, CancellationToken ct = default)
    {
        var existing = await _db.Quests
            .Include(q => q.Nodes)
            .Include(q => q.Edges)
            .Include(q => q.Dependencies)
            .FirstOrDefaultAsync(q => q.Id == quest.Id, ct);

        if (existing == null)
        {
            _db.Quests.Add(quest);
        }
        else
        {
            _db.Entry(existing).CurrentValues.SetValues(quest);

            // Sync child collections
            _db.QuestNodes.RemoveRange(existing.Nodes);
            _db.QuestEdges.RemoveRange(existing.Edges);
            _db.QuestDependencies.RemoveRange(existing.Dependencies);

            foreach (var node in quest.Nodes) { node.QuestId = quest.Id; _db.QuestNodes.Add(node); }
            foreach (var edge in quest.Edges) { edge.QuestId = quest.Id; _db.QuestEdges.Add(edge); }
            foreach (var dep in quest.Dependencies) { dep.QuestId = quest.Id; _db.QuestDependencies.Add(dep); }
        }

        await _db.SaveChangesAsync(ct);
        return new OASISResult<Quest> { Result = quest, Message = "Saved." };
    }

    public async Task<OASISResult<bool>> DeleteQuestAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await _db.Quests
            .Include(q => q.Nodes)
            .Include(q => q.Edges)
            .Include(q => q.Dependencies)
            .FirstOrDefaultAsync(q => q.Id == id, ct);

        if (existing == null)
            return new OASISResult<bool> { IsError = true, Message = "Quest not found.", Result = false };

        _db.Quests.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return new OASISResult<bool> { Result = true, Message = "Deleted." };
    }

    // Quest Template (lifted from ~467-532)
    public async Task<OASISResult<QuestTemplate>> GetQuestTemplateAsync(Guid id, CancellationToken ct = default)
    {
        var template = await _db.QuestTemplates
            .Include(t => t.Nodes)
            .Include(t => t.Edges)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        return new OASISResult<QuestTemplate>
        {
            IsError = template == null,
            Message = template == null ? "Quest template not found." : "Success",
            Result = template
        };
    }

    public async Task<OASISResult<IEnumerable<QuestTemplate>>> GetAllQuestTemplatesAsync(CancellationToken ct = default)
    {
        var list = await _db.QuestTemplates
            .Include(t => t.Nodes)
            .Include(t => t.Edges)
            .AsNoTracking()
            .ToListAsync(ct);

        return new OASISResult<IEnumerable<QuestTemplate>> { Result = list, Message = "Success" };
    }

    public async Task<OASISResult<QuestTemplate>> UpsertQuestTemplateAsync(QuestTemplate template, CancellationToken ct = default)
    {
        var existing = await _db.QuestTemplates
            .Include(t => t.Nodes)
            .Include(t => t.Edges)
            .FirstOrDefaultAsync(t => t.Id == template.Id, ct);

        if (existing == null)
        {
            _db.QuestTemplates.Add(template);
        }
        else
        {
            _db.Entry(existing).CurrentValues.SetValues(template);
            _db.QuestTemplateNodes.RemoveRange(existing.Nodes);
            _db.QuestTemplateEdges.RemoveRange(existing.Edges);

            foreach (var node in template.Nodes) { node.TemplateId = template.Id; _db.QuestTemplateNodes.Add(node); }
            foreach (var edge in template.Edges) { edge.TemplateId = template.Id; _db.QuestTemplateEdges.Add(edge); }
        }

        await _db.SaveChangesAsync(ct);
        return new OASISResult<QuestTemplate> { Result = template, Message = "Saved." };
    }

    public async Task<OASISResult<bool>> DeleteQuestTemplateAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await _db.QuestTemplates
            .Include(t => t.Nodes)
            .Include(t => t.Edges)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (existing == null)
            return new OASISResult<bool> { IsError = true, Message = "Quest template not found.", Result = false };

        _db.QuestTemplates.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return new OASISResult<bool> { Result = true, Message = "Deleted." };
    }

    // Quest Node Template (lifted from ~534-551)
    public async Task<OASISResult<QuestNodeTemplate>> UpsertQuestNodeTemplateAsync(QuestNodeTemplate template, CancellationToken ct = default)
    {
        var existing = await _db.QuestNodeTemplates.FindAsync(new object[] { template.Id }, ct);
        if (existing == null)
            _db.QuestNodeTemplates.Add(template);
        else
            _db.Entry(existing).CurrentValues.SetValues(template);

        await _db.SaveChangesAsync(ct);
        return new OASISResult<QuestNodeTemplate> { Result = template, Message = "Saved." };
    }

    public async Task<OASISResult<IEnumerable<QuestNodeTemplate>>> GetAllQuestNodeTemplatesAsync(CancellationToken ct = default)
    {
        var list = await _db.QuestNodeTemplates.AsNoTracking().ToListAsync(ct);
        return new OASISResult<IEnumerable<QuestNodeTemplate>> { Result = list, Message = "Success" };
    }
}
