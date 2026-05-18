using OASIS.WebAPI.Models.Quest;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Services.Quest;

/// <summary>
/// Builds the success / failure <see cref="OASISResult{QuestNode}"/> for a
/// quest node handler. Reproduces the former <c>QuestManager</c> success block
/// (~:623-625) and <c>QuestManager.Fail</c> (~:633-638) verbatim so handler
/// dispatch stays behaviour-identical — one place, not 34.
/// </summary>
public static class QuestNodeResults
{
    /// <summary>
    /// Marks <paramref name="node"/> succeeded with <paramref name="outputJson"/>
    /// and returns the success result. <paramref name="message"/> defaults to the
    /// original "Node executed successfully." text.
    /// </summary>
    public static OASISResult<QuestNode> Ok(QuestNode node, string? message, string? outputJson)
    {
        node.State = QuestNodeState.Succeeded;
        node.Output = outputJson;
        return new OASISResult<QuestNode> { Result = node, Message = message ?? "Node executed successfully." };
    }

    /// <summary>
    /// Marks <paramref name="node"/> failed with <paramref name="message"/> and
    /// returns the error result (verbatim from the former <c>QuestManager.Fail</c>).
    /// </summary>
    public static OASISResult<QuestNode> Fail(QuestNode node, string message)
    {
        node.State = QuestNodeState.Failed;
        node.Error = message;
        return new OASISResult<QuestNode> { IsError = true, Result = node, Message = message };
    }
}
