using FluentAssertions;
using Moq;
using OASIS.WebAPI.Interfaces.QuestExecution;
using OASIS.WebAPI.Interfaces.Stores;
using OASIS.WebAPI.Managers;
using OASIS.WebAPI.Models.Quest;
using OASIS.WebAPI.Models.Responses;
using OASIS.WebAPI.Services;
using OASIS.WebAPI.Services.Quest;
using Xunit;
using QuestEntity = OASIS.WebAPI.Models.Quest.Quest;

namespace OASIS.WebAPI.Tests.Quest.Handlers;

/// <summary>
/// Guards the ExecutionOrder dedupe: QuestManager deleted its own
/// topological-order reassignment; QuestDagValidator is the single authority
/// and mutates node.ExecutionOrder in-place. ValidateDAGAsync must still
/// persist a graph whose nodes carry the validator-assigned order.
/// </summary>
public class QuestManagerExecutionOrderTests
{
    [Fact]
    public async Task ValidateDAGAsync_SetsExecutionOrderViaValidator_AndPersists()
    {
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();
        var cId = Guid.NewGuid();
        var questId = Guid.NewGuid();

        var quest = new QuestEntity
        {
            Id = questId,
            Name = "Linear",
            AvatarId = Guid.NewGuid(),
            Nodes = new List<QuestNode>
            {
                new() { Id = aId, Name = "A", IsEntry = true, IsTerminal = false },
                new() { Id = bId, Name = "B", IsEntry = false, IsTerminal = false },
                new() { Id = cId, Name = "C", IsEntry = false, IsTerminal = true }
            },
            Edges = new List<QuestEdge>
            {
                new() { Id = Guid.NewGuid(), SourceNodeId = aId, TargetNodeId = bId },
                new() { Id = Guid.NewGuid(), SourceNodeId = bId, TargetNodeId = cId }
            }
        };

        QuestEntity? persisted = null;
        var store = new Mock<IQuestStore>();
        store.Setup(s => s.GetQuestAsync(questId, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new OASISResult<QuestEntity> { Result = quest });
        store.Setup(s => s.UpsertQuestAsync(It.IsAny<QuestEntity>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((QuestEntity q, CancellationToken _) => { persisted = q; return new OASISResult<QuestEntity> { Result = q }; });

        // Real validator — the single ExecutionOrder authority.
        var manager = new QuestManager(
            store.Object,
            new QuestDagValidator(),
            new QuestNodeHandlerRegistry(Array.Empty<IQuestNodeHandler>()));

        var result = await manager.ValidateDAGAsync(questId);

        result.IsError.Should().BeFalse();
        result.Result.Should().BeTrue();

        var nodeA = quest.Nodes.First(n => n.Id == aId);
        var nodeB = quest.Nodes.First(n => n.Id == bId);
        var nodeC = quest.Nodes.First(n => n.Id == cId);
        nodeA.ExecutionOrder.Should().BeLessThan(nodeB.ExecutionOrder);
        nodeB.ExecutionOrder.Should().BeLessThan(nodeC.ExecutionOrder);

        // The persisted graph carries the validator-assigned order.
        persisted.Should().NotBeNull();
        persisted!.Nodes.Select(n => n.ExecutionOrder).Distinct().Should().HaveCount(3);
        store.Verify(s => s.UpsertQuestAsync(It.IsAny<QuestEntity>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
