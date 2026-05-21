namespace OASIS.WebAPI.Models.Quest;

/// <summary>
/// Lifecycle status of a single <see cref="QuestRun"/> (one execution attempt).
/// Kept distinct from <see cref="QuestStatus"/> (which carries the definition's
/// Draft/Active/Archived semantics) so run-level and definition-level lifecycle
/// never collide. See <c>conductor/tracks/quest-temporal-fork-model/ADR.md</c>.
/// </summary>
/// <remarks>
/// Terminal states: <see cref="Succeeded"/>, <see cref="Failed"/>,
/// <see cref="Forked"/>, <see cref="Cancelled"/>. No further transitions occur
/// from any terminal state. <see cref="Forked"/> is a run-only concept and
/// MUST NOT appear in <see cref="QuestNodeState"/>.
/// </remarks>
public enum QuestRunStatus
{
    /// <summary>Run created; no node has been claimed yet.</summary>
    Pending,

    /// <summary>At least one node has transitioned out of Pending.</summary>
    Running,

    /// <summary>All terminal nodes reported Succeeded.</summary>
    Succeeded,

    /// <summary>Any node reported Failed, or supervisor marked the run failed.</summary>
    Failed,

    /// <summary>
    /// Parent run that was forked. New work continues on the child run referenced
    /// by another <see cref="QuestRun"/> whose <see cref="QuestRun.ParentRunId"/>
    /// equals this run's id. Terminal.
    /// </summary>
    Forked,

    /// <summary>Run was explicitly cancelled before completion.</summary>
    Cancelled
}
