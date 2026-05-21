using System.Collections.Concurrent;
using OASIS.WebAPI.Interfaces.Stores;
using OASIS.WebAPI.Models.Quest;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Providers.Stores;

/// <summary>
/// Thread-safe in-memory <see cref="IQuestNodeExecutionStore"/>.
/// Singleton-scoped via <c>Program.cs</c>.
/// </summary>
/// <remarks>
/// The natural key <c>(RunId, NodeId)</c> is indexed via a secondary
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> for O(1) lookup;
/// <see cref="TryClaimPendingAsync"/> uses
/// <see cref="ConcurrentDictionary{TKey,TValue}.TryUpdate"/> to provide the
/// G2 conditional-update semantic (succeeds only when current state is
/// <see cref="QuestNodeState.Pending"/>).
/// </remarks>
public sealed class InMemoryQuestNodeExecutionStore : IQuestNodeExecutionStore
{
    // Keyed by execution row Id
    private readonly ConcurrentDictionary<Guid, QuestNodeExecution> _byId = new();

    // Secondary index: (RunId, NodeId) -> execution Id
    private readonly ConcurrentDictionary<(Guid RunId, Guid NodeId), Guid> _byNaturalKey = new();

    public Task<OASISResult<QuestNodeExecution>> CreateAsync(QuestNodeExecution execution, CancellationToken ct = default)
    {
        if (!_byId.TryAdd(execution.Id, execution))
        {
            return Task.FromResult(new OASISResult<QuestNodeExecution>
            {
                IsError = true,
                Message = $"QuestNodeExecution {execution.Id} already exists.",
                Result = null
            });
        }

        if (!_byNaturalKey.TryAdd((execution.RunId, execution.NodeId), execution.Id))
        {
            // Roll back the primary insert to keep both indexes consistent.
            _byId.TryRemove(execution.Id, out _);
            return Task.FromResult(new OASISResult<QuestNodeExecution>
            {
                IsError = true,
                Message = $"QuestNodeExecution already exists for (run={execution.RunId}, node={execution.NodeId}).",
                Result = null
            });
        }

        return Task.FromResult(new OASISResult<QuestNodeExecution> { Result = execution, Message = "Created." });
    }

    public Task<OASISResult<QuestNodeExecution>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (_byId.TryGetValue(id, out var exec))
            return Task.FromResult(new OASISResult<QuestNodeExecution> { Result = exec, Message = "Success" });

        return Task.FromResult(new OASISResult<QuestNodeExecution>
        {
            IsError = true,
            Message = $"QuestNodeExecution {id} not found.",
            Result = null
        });
    }

    public Task<OASISResult<QuestNodeExecution>> UpdateAsync(QuestNodeExecution execution, CancellationToken ct = default)
    {
        if (!_byId.ContainsKey(execution.Id))
        {
            return Task.FromResult(new OASISResult<QuestNodeExecution>
            {
                IsError = true,
                Message = $"QuestNodeExecution {execution.Id} not found.",
                Result = null
            });
        }
        _byId[execution.Id] = execution;
        return Task.FromResult(new OASISResult<QuestNodeExecution> { Result = execution, Message = "Updated." });
    }

    public Task<OASISResult<IEnumerable<QuestNodeExecution>>> GetByRunIdAsync(Guid runId, CancellationToken ct = default)
    {
        IEnumerable<QuestNodeExecution> matches = _byId.Values
            .Where(e => e.RunId == runId)
            .OrderBy(e => e.StartedAt)
            .ToList();
        return Task.FromResult(new OASISResult<IEnumerable<QuestNodeExecution>> { Result = matches, Message = "Success" });
    }

    public Task<OASISResult<QuestNodeExecution>> GetByRunAndNodeAsync(Guid runId, Guid nodeId, CancellationToken ct = default)
    {
        if (_byNaturalKey.TryGetValue((runId, nodeId), out var execId) &&
            _byId.TryGetValue(execId, out var exec))
        {
            return Task.FromResult(new OASISResult<QuestNodeExecution> { Result = exec, Message = "Success" });
        }

        return Task.FromResult(new OASISResult<QuestNodeExecution>
        {
            IsError = true,
            Message = $"No QuestNodeExecution for (run={runId}, node={nodeId}).",
            Result = null
        });
    }

    public Task<OASISResult<QuestNodeExecution?>> TryClaimPendingAsync(Guid runId, Guid nodeId, CancellationToken ct = default)
    {
        if (!_byNaturalKey.TryGetValue((runId, nodeId), out var execId) ||
            !_byId.TryGetValue(execId, out var current))
        {
            return Task.FromResult(new OASISResult<QuestNodeExecution?>
            {
                IsError = true,
                Message = $"No QuestNodeExecution for (run={runId}, node={nodeId}).",
                Result = null
            });
        }

        if (current.State != QuestNodeState.Pending)
        {
            // Row exists but not Pending — caller lost the race. Not an error.
            return Task.FromResult(new OASISResult<QuestNodeExecution?>
            {
                Result = null,
                Message = $"QuestNodeExecution (run={runId}, node={nodeId}) is not Pending (current: {current.State})."
            });
        }

        var claimed = new QuestNodeExecution
        {
            Id = current.Id,
            RunId = current.RunId,
            NodeId = current.NodeId,
            State = QuestNodeState.Running,
            Output = current.Output,
            Error = current.Error,
            StartedAt = DateTime.UtcNow,
            EndedAt = current.EndedAt
        };

        // Conditional CAS: only swap if the row we read is still the current row.
        // Mirrors the SurrealDB UPDATE … WHERE state='Pending' RETURN AFTER semantic.
        if (_byId.TryUpdate(current.Id, claimed, current))
        {
            return Task.FromResult(new OASISResult<QuestNodeExecution?>
            {
                Result = claimed,
                Message = "Claimed."
            });
        }

        // Lost the race — another caller updated this row between our read and our CAS.
        return Task.FromResult(new OASISResult<QuestNodeExecution?>
        {
            Result = null,
            Message = $"QuestNodeExecution (run={runId}, node={nodeId}) was concurrently modified."
        });
    }
}
