namespace OASIS.WebAPI.Models.Quest;

/// <summary>
/// A single executable DAG representing a workflow unit.
/// Each Quest is a directed acyclic graph with entry/terminal nodes,
/// optional dependencies on completed quests, and reusable node templates.
/// </summary>
public class Quest
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid AvatarId { get; set; }

    /// <summary>
    /// Definition-level Status. Moved to <see cref="QuestRun.Status"/> by the
    /// quest-temporal-fork-model track — runtime lifecycle belongs to the run,
    /// not the definition. Kept here only until QuestManager (B2's task 10–13)
    /// is rewritten against the (runId, nodeId) shape; will be physically
    /// removed in that same window together with the project-level
    /// <c>NoWarn CS0618</c> scope. See ADR §2.2.
    /// </summary>
    [Obsolete("Moved to QuestRun.Status by quest-temporal-fork-model — see ADR §2.2")]
    public QuestStatus Status { get; set; }

    public List<QuestNode> Nodes { get; set; } = new();
    public List<QuestEdge> Edges { get; set; } = new();
    public List<QuestDependency> Dependencies { get; set; } = new();

    public Guid? TemplateId { get; set; }
    public Guid? DappSeriesId { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>Definition birthdate. STAYS on the definition (not a runtime artifact).</summary>
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Moved to <see cref="QuestRun.EndedAt"/> by the quest-temporal-fork-model
    /// track. See ADR §2.2.
    /// </summary>
    [Obsolete("Moved to QuestRun.EndedAt by quest-temporal-fork-model — see ADR §2.2")]
    public DateTime? CompletedDate { get; set; }
}
