namespace OASIS.WebAPI.Models.Quest;

/// <summary>
/// A single task/step within a quest DAG.
/// Wraps a call to an existing OASIS manager method.
/// </summary>
public class QuestNode
{
    public Guid Id { get; set; }
    public Guid QuestId { get; set; }
    public Guid? NodeTemplateId { get; set; }
    public QuestNodeType NodeType { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Node-specific config serialized as JSON.
    /// Deserialized to the matching request model at execution time.
    /// </summary>
    public string Config { get; set; } = "{}";

    /// <summary>
    /// Per-node runtime state. Moved to <see cref="QuestNodeExecution.State"/>
    /// by the quest-temporal-fork-model track (per-(run, node) keyed). Kept
    /// here only until QuestManager (B2's task 10–13) writes to QuestNodeExecution
    /// instead. See ADR §2.2.
    /// </summary>
    [Obsolete("Moved to QuestNodeExecution.State by quest-temporal-fork-model — see ADR §2.2")]
    public QuestNodeState State { get; set; } = QuestNodeState.Pending;

    /// <summary>
    /// Serialized OASISResult&lt;T&gt; from the manager call.
    /// Moved to <see cref="QuestNodeExecution.Output"/> by the
    /// quest-temporal-fork-model track. See ADR §2.2.
    /// </summary>
    [Obsolete("Moved to QuestNodeExecution.Output by quest-temporal-fork-model — see ADR §2.2")]
    public string? Output { get; set; }

    /// <summary>
    /// Per-node failure message. Moved to <see cref="QuestNodeExecution.Error"/>
    /// by the quest-temporal-fork-model track. See ADR §2.2.
    /// </summary>
    [Obsolete("Moved to QuestNodeExecution.Error by quest-temporal-fork-model — see ADR §2.2")]
    public string? Error { get; set; }

    /// <summary>
    /// Entry point node (no incoming control edges).
    /// </summary>
    public bool IsEntry { get; set; }

    /// <summary>
    /// Terminal node (no outgoing control edges).
    /// </summary>
    public bool IsTerminal { get; set; }

    /// <summary>
    /// Topological position (computed during validation).
    /// </summary>
    public int ExecutionOrder { get; set; }
}
