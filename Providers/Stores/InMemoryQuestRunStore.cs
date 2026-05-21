using System.Collections.Concurrent;
using OASIS.WebAPI.Interfaces.Stores;
using OASIS.WebAPI.Models.Quest;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Providers.Stores;

/// <summary>
/// Thread-safe in-memory <see cref="IQuestRunStore"/>. Singleton-scoped via
/// <c>Program.cs</c>. Used as the default during the
/// quest-temporal-fork-model + surrealdb-migration transition window;
/// SurrealDB-backed implementation arrives with surrealdb-migration tasks 9–10.
/// </summary>
public sealed class InMemoryQuestRunStore : IQuestRunStore
{
    private readonly ConcurrentDictionary<Guid, QuestRun> _runs = new();

    public Task<OASISResult<QuestRun>> CreateAsync(QuestRun run, CancellationToken ct = default)
    {
        if (!_runs.TryAdd(run.Id, run))
        {
            return Task.FromResult(new OASISResult<QuestRun>
            {
                IsError = true,
                Message = $"QuestRun {run.Id} already exists.",
                Result = null
            });
        }
        return Task.FromResult(new OASISResult<QuestRun> { Result = run, Message = "Created." });
    }

    public Task<OASISResult<QuestRun>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (_runs.TryGetValue(id, out var run))
            return Task.FromResult(new OASISResult<QuestRun> { Result = run, Message = "Success" });

        return Task.FromResult(new OASISResult<QuestRun>
        {
            IsError = true,
            Message = $"QuestRun {id} not found.",
            Result = null
        });
    }

    public Task<OASISResult<QuestRun>> UpdateAsync(QuestRun run, CancellationToken ct = default)
    {
        if (!_runs.ContainsKey(run.Id))
        {
            return Task.FromResult(new OASISResult<QuestRun>
            {
                IsError = true,
                Message = $"QuestRun {run.Id} not found.",
                Result = null
            });
        }
        _runs[run.Id] = run;
        return Task.FromResult(new OASISResult<QuestRun> { Result = run, Message = "Updated." });
    }

    public Task<OASISResult<IEnumerable<QuestRun>>> GetByQuestIdAsync(Guid questId, CancellationToken ct = default)
    {
        IEnumerable<QuestRun> matches = _runs.Values.Where(r => r.QuestId == questId).ToList();
        return Task.FromResult(new OASISResult<IEnumerable<QuestRun>> { Result = matches, Message = "Success" });
    }

    public Task<OASISResult<IEnumerable<QuestRun>>> GetByAvatarIdAsync(Guid avatarId, CancellationToken ct = default)
    {
        IEnumerable<QuestRun> matches = _runs.Values.Where(r => r.AvatarId == avatarId).ToList();
        return Task.FromResult(new OASISResult<IEnumerable<QuestRun>> { Result = matches, Message = "Success" });
    }

    public Task<OASISResult<IEnumerable<QuestRun>>> GetByStatusAsync(QuestRunStatus status, CancellationToken ct = default)
    {
        IEnumerable<QuestRun> matches = _runs.Values.Where(r => r.Status == status).ToList();
        return Task.FromResult(new OASISResult<IEnumerable<QuestRun>> { Result = matches, Message = "Success" });
    }

    public Task<OASISResult<IEnumerable<QuestRun>>> GetLineageAsync(Guid runId, CancellationToken ct = default)
    {
        if (!_runs.TryGetValue(runId, out var current))
        {
            return Task.FromResult(new OASISResult<IEnumerable<QuestRun>>
            {
                IsError = true,
                Message = $"QuestRun {runId} not found.",
                Result = Array.Empty<QuestRun>()
            });
        }

        var chain = new List<QuestRun>();
        var visited = new HashSet<Guid>(); // structural safety against malformed data
        var cursor = (QuestRun?)current;
        while (cursor is not null && visited.Add(cursor.Id))
        {
            chain.Add(cursor);
            if (cursor.ParentRunId is null) break;
            _runs.TryGetValue(cursor.ParentRunId.Value, out cursor);
        }

        return Task.FromResult(new OASISResult<IEnumerable<QuestRun>>
        {
            Result = chain,
            Message = "Success"
        });
    }
}
