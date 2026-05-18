using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Interfaces.QuestExecution;
using OASIS.WebAPI.Interfaces.Stores;
using OASIS.WebAPI.Models.Quest;
using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;
using OASIS.WebAPI.Services.Quest;

namespace OASIS.WebAPI.Managers;

public class QuestManager : IQuestManager
{
    private readonly IQuestStore _questStore;
    private readonly IQuestDagValidator _dagValidator;
    private readonly IQuestNodeHandlerRegistry _registry;

    public QuestManager(
        IQuestStore questStore,
        IQuestDagValidator dagValidator,
        IQuestNodeHandlerRegistry registry)
    {
        _questStore = questStore;
        _dagValidator = dagValidator;
        _registry = registry;
    }

    // ═══════════════════════════════════════════════════════════════════
    // QUEST CRUD
    // ═══════════════════════════════════════════════════════════════════

    public async Task<OASISResult<Quest>> CreateAsync(QuestCreateModel model, Guid avatarId, OASISRequest? request = null)
    {
        var quest = new Quest
        {
            Id = Guid.NewGuid(),
            Name = model.Name,
            Description = model.Description,
            AvatarId = avatarId,
            Status = QuestStatus.Draft,
            CreatedDate = DateTime.UtcNow
        };

        // Create nodes with new Ids
        var nodeIds = new List<Guid>();
        foreach (var nodeModel in model.Nodes)
        {
            var node = new QuestNode
            {
                Id = Guid.NewGuid(),
                QuestId = quest.Id,
                Name = nodeModel.Name,
                NodeType = nodeModel.NodeType,
                Config = nodeModel.Config,
                IsEntry = nodeModel.IsEntry,
                IsTerminal = nodeModel.IsTerminal,
                NodeTemplateId = nodeModel.NodeTemplateId,
                State = QuestNodeState.Pending
            };
            quest.Nodes.Add(node);
            nodeIds.Add(node.Id);
        }

        // Map edge indices to node Ids
        foreach (var edgeModel in model.Edges)
        {
            if (edgeModel.SourceNodeId < 0 || edgeModel.SourceNodeId >= nodeIds.Count ||
                edgeModel.TargetNodeId < 0 || edgeModel.TargetNodeId >= nodeIds.Count)
            {
                return new OASISResult<Quest> { IsError = true, Message = $"Edge index out of range. Source={edgeModel.SourceNodeId}, Target={edgeModel.TargetNodeId}, NodeCount={nodeIds.Count}." };
            }

            var edge = new QuestEdge
            {
                Id = Guid.NewGuid(),
                QuestId = quest.Id,
                SourceNodeId = nodeIds[edgeModel.SourceNodeId],
                TargetNodeId = nodeIds[edgeModel.TargetNodeId],
                Condition = edgeModel.Condition,
                EdgeType = edgeModel.EdgeType
            };
            quest.Edges.Add(edge);
        }

        return await _questStore.UpsertQuestAsync(quest);
    }

    public async Task<OASISResult<Quest>> GetAsync(Guid id, OASISRequest? request = null)
    {
        return await _questStore.GetQuestAsync(id);
    }

    public async Task<OASISResult<IEnumerable<Quest>>> GetByAvatarAsync(Guid avatarId, OASISRequest? request = null)
    {
        return await _questStore.GetQuestsByAvatarAsync(avatarId);
    }

    public async Task<OASISResult<Quest>> UpdateAsync(Guid id, QuestUpdateModel model, OASISRequest? request = null)
    {
        var existing = await _questStore.GetQuestAsync(id);
        if (existing.IsError || existing.Result == null) return existing;

        var quest = existing.Result;
        if (model.Name != null) quest.Name = model.Name;
        if (model.Description != null) quest.Description = model.Description;
        if (model.Status.HasValue)
        {
            quest.Status = model.Status.Value;
            if (model.Status.Value == QuestStatus.Completed)
                quest.CompletedDate = DateTime.UtcNow;
        }

        return await _questStore.UpsertQuestAsync(quest);
    }

    public async Task<OASISResult<bool>> DeleteAsync(Guid id, OASISRequest? request = null)
    {
        return await _questStore.DeleteQuestAsync(id);
    }

    // ═══════════════════════════════════════════════════════════════════
    // DAG VALIDATION
    // ═══════════════════════════════════════════════════════════════════

    public async Task<OASISResult<bool>> ValidateDAGAsync(Guid questId, OASISRequest? request = null)
    {
        var questResult = await _questStore.GetQuestAsync(questId);
        if (questResult.IsError || questResult.Result == null)
            return new OASISResult<bool> { IsError = true, Message = questResult.Message };

        var quest = questResult.Result;

        // QuestDagValidator is the single ExecutionOrder authority — Validate
        // mutates node.ExecutionOrder in-place on the quest graph.
        var validation = _dagValidator.Validate(quest);

        if (!validation.IsValid)
        {
            return new OASISResult<bool>
            {
                IsError = true,
                Result = false,
                Message = $"DAG validation failed: {string.Join("; ", validation.Errors)}"
            };
        }

        await _questStore.UpsertQuestAsync(quest);

        return new OASISResult<bool> { Result = true, Message = "DAG is valid." };
    }

    // ═══════════════════════════════════════════════════════════════════
    // EXECUTION
    // ═══════════════════════════════════════════════════════════════════

    public async Task<OASISResult<Quest>> ExecuteAsync(Guid questId, OASISRequest? request = null)
    {
        // Validate DAG first
        var validationResult = await ValidateDAGAsync(questId, request);
        if (validationResult.IsError)
            return new OASISResult<Quest> { IsError = true, Message = validationResult.Message };

        var questResult = await _questStore.GetQuestAsync(questId);
        if (questResult.IsError || questResult.Result == null)
            return new OASISResult<Quest> { IsError = true, Message = questResult.Message };

        var quest = questResult.Result;
        quest.Status = QuestStatus.Active;

        // Execute nodes in topological order
        var sortedNodes = quest.Nodes.OrderBy(n => n.ExecutionOrder).ToList();

        foreach (var node in sortedNodes)
        {
            // Check conditional edges — skip node if any incoming conditional edge evaluates to false
            var incomingEdges = quest.Edges.Where(e => e.TargetNodeId == node.Id).ToList();
            var shouldSkip = false;

            foreach (var edge in incomingEdges)
            {
                if (edge.EdgeType == QuestEdgeType.Conditional && !string.IsNullOrEmpty(edge.Condition))
                {
                    var sourceNode = quest.Nodes.FirstOrDefault(n => n.Id == edge.SourceNodeId);
                    if (sourceNode?.State == QuestNodeState.Failed || sourceNode?.State == QuestNodeState.Skipped)
                    {
                        shouldSkip = true;
                        break;
                    }
                }

                // If source node failed on a control edge, skip this node
                if (edge.EdgeType == QuestEdgeType.Control)
                {
                    var sourceNode = quest.Nodes.FirstOrDefault(n => n.Id == edge.SourceNodeId);
                    if (sourceNode?.State == QuestNodeState.Failed)
                    {
                        shouldSkip = true;
                        break;
                    }
                }
            }

            if (shouldSkip)
            {
                node.State = QuestNodeState.Skipped;
                continue;
            }

            try
            {
                var nodeResult = await ExecuteNodeInternalAsync(quest, node);
                if (nodeResult.IsError)
                {
                    node.State = QuestNodeState.Failed;
                    node.Error = nodeResult.Message;
                }
                else
                {
                    node.State = QuestNodeState.Succeeded;
                    node.Output = nodeResult.Result?.Output;
                }
            }
            catch (Exception ex)
            {
                node.State = QuestNodeState.Failed;
                node.Error = ex.Message;
            }
        }

        // Determine overall quest status
        if (quest.Nodes.Any(n => n.State == QuestNodeState.Failed))
            quest.Status = QuestStatus.Failed;
        else
        {
            quest.Status = QuestStatus.Completed;
            quest.CompletedDate = DateTime.UtcNow;
        }

        await _questStore.UpsertQuestAsync(quest);
        return new OASISResult<Quest> { Result = quest, Message = $"Quest execution {quest.Status}." };
    }

    public async Task<OASISResult<QuestNode>> ExecuteNodeAsync(Guid questId, Guid nodeId, OASISRequest? request = null)
    {
        var questResult = await _questStore.GetQuestAsync(questId);
        if (questResult.IsError || questResult.Result == null)
            return new OASISResult<QuestNode> { IsError = true, Message = questResult.Message };

        var quest = questResult.Result;
        var node = quest.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node == null)
            return new OASISResult<QuestNode> { IsError = true, Message = "Node not found." };

        var result = await ExecuteNodeInternalAsync(quest, node);

        await _questStore.UpsertQuestAsync(quest);
        return result;
    }

    private async Task<OASISResult<QuestNode>> ExecuteNodeInternalAsync(Quest quest, QuestNode node, CancellationToken ct = default)
    {
        node.State = QuestNodeState.Running;

        if (!_registry.TryGet(node.NodeType, out var handler))
            return QuestNodeResults.Fail(node, $"Unsupported node type: {node.NodeType}");

        // One thin try/catch wrapper — mirrors the former QuestManager
        // catch (~:627-630), in one place instead of per node type.
        try
        {
            return await handler.HandleAsync(quest, node, ct);
        }
        catch (Exception ex)
        {
            return QuestNodeResults.Fail(node, ex.Message);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // TEMPLATES
    // ═══════════════════════════════════════════════════════════════════

    public async Task<OASISResult<QuestTemplate>> CreateTemplateAsync(QuestTemplateCreateModel model, Guid avatarId, OASISRequest? request = null)
    {
        var template = new QuestTemplate
        {
            Id = Guid.NewGuid(),
            Name = model.Name,
            Description = model.Description,
            AuthorAvatarId = avatarId,
            Parameters = model.Parameters,
            Version = model.Version,
            IsPublic = model.IsPublic,
            Tags = model.Tags
        };

        // Build template nodes from the create model
        var slotIds = new List<string>();
        for (int i = 0; i < model.Nodes.Count; i++)
        {
            var nodeModel = model.Nodes[i];
            var slotId = $"slot_{i}";
            slotIds.Add(slotId);

            template.Nodes.Add(new QuestTemplateNode
            {
                Id = Guid.NewGuid(),
                TemplateId = template.Id,
                SlotId = slotId,
                NodeTemplateId = nodeModel.NodeTemplateId ?? Guid.Empty,
                ParamOverrides = nodeModel.Config,
                IsEntry = nodeModel.IsEntry,
                IsTerminal = nodeModel.IsTerminal
            });
        }

        foreach (var edgeModel in model.Edges)
        {
            if (edgeModel.SourceNodeId < 0 || edgeModel.SourceNodeId >= slotIds.Count ||
                edgeModel.TargetNodeId < 0 || edgeModel.TargetNodeId >= slotIds.Count)
            {
                return new OASISResult<QuestTemplate> { IsError = true, Message = "Edge index out of range." };
            }

            template.Edges.Add(new QuestTemplateEdge
            {
                Id = Guid.NewGuid(),
                TemplateId = template.Id,
                SourceSlotId = slotIds[edgeModel.SourceNodeId],
                TargetSlotId = slotIds[edgeModel.TargetNodeId],
                EdgeType = edgeModel.EdgeType
            });
        }

        return await _questStore.UpsertQuestTemplateAsync(template);
    }

    public async Task<OASISResult<QuestTemplate>> GetTemplateAsync(Guid id, OASISRequest? request = null)
    {
        return await _questStore.GetQuestTemplateAsync(id);
    }

    public async Task<OASISResult<IEnumerable<QuestTemplate>>> ListTemplatesAsync(OASISRequest? request = null)
    {
        return await _questStore.GetAllQuestTemplatesAsync();
    }

    public async Task<OASISResult<Quest>> InstantiateTemplateAsync(Guid templateId, Guid avatarId, Dictionary<string, string>? parameters = null, OASISRequest? request = null)
    {
        var templateResult = await _questStore.GetQuestTemplateAsync(templateId);
        if (templateResult.IsError || templateResult.Result == null)
            return new OASISResult<Quest> { IsError = true, Message = templateResult.Message };

        var template = templateResult.Result;

        // Load node templates referenced by this quest template
        var nodeTemplatesResult = await _questStore.GetAllQuestNodeTemplatesAsync();
        var nodeTemplates = (nodeTemplatesResult.Result ?? Enumerable.Empty<QuestNodeTemplate>())
            .ToDictionary(nt => nt.Id);

        var quest = new Quest
        {
            Id = Guid.NewGuid(),
            Name = template.Name,
            Description = template.Description,
            AvatarId = avatarId,
            TemplateId = templateId,
            Status = QuestStatus.Draft,
            CreatedDate = DateTime.UtcNow
        };

        // Map slotId -> new node Id
        var slotToNodeId = new Dictionary<string, Guid>();

        foreach (var templateNode in template.Nodes)
        {
            var nodeId = Guid.NewGuid();
            slotToNodeId[templateNode.SlotId] = nodeId;

            // Resolve config from node template with param overrides
            var config = templateNode.ParamOverrides;
            if (nodeTemplates.TryGetValue(templateNode.NodeTemplateId, out var nodeTemplate))
            {
                config = string.IsNullOrEmpty(templateNode.ParamOverrides) || templateNode.ParamOverrides == "{}"
                    ? nodeTemplate.DefaultConfig
                    : templateNode.ParamOverrides;
            }

            // Apply parameter substitutions
            if (parameters != null)
            {
                foreach (var (key, value) in parameters)
                    config = config.Replace($"{{{{{key}}}}}", value);
            }

            var nodeType = nodeTemplate?.NodeType ?? QuestNodeType.HolonGet;

            quest.Nodes.Add(new QuestNode
            {
                Id = nodeId,
                QuestId = quest.Id,
                NodeTemplateId = templateNode.NodeTemplateId,
                NodeType = nodeType,
                Name = nodeTemplate?.Name ?? templateNode.SlotId,
                Config = config,
                IsEntry = templateNode.IsEntry,
                IsTerminal = templateNode.IsTerminal,
                State = QuestNodeState.Pending
            });
        }

        foreach (var templateEdge in template.Edges)
        {
            if (!slotToNodeId.TryGetValue(templateEdge.SourceSlotId, out var sourceId) ||
                !slotToNodeId.TryGetValue(templateEdge.TargetSlotId, out var targetId))
                continue;

            quest.Edges.Add(new QuestEdge
            {
                Id = Guid.NewGuid(),
                QuestId = quest.Id,
                SourceNodeId = sourceId,
                TargetNodeId = targetId,
                EdgeType = templateEdge.EdgeType
            });
        }

        return await _questStore.UpsertQuestAsync(quest);
    }

    // ═══════════════════════════════════════════════════════════════════
    // NODE TEMPLATES
    // ═══════════════════════════════════════════════════════════════════

    public async Task<OASISResult<QuestNodeTemplate>> CreateNodeTemplateAsync(QuestNodeTemplateCreateModel model, Guid avatarId, OASISRequest? request = null)
    {
        var template = new QuestNodeTemplate
        {
            Id = Guid.NewGuid(),
            Name = model.Name,
            NodeType = model.NodeType,
            Description = model.Description,
            DefaultConfig = model.DefaultConfig,
            ConfigSchema = model.ConfigSchema,
            InputSchema = model.InputSchema,
            OutputSchema = model.OutputSchema,
            Version = model.Version,
            AuthorAvatarId = avatarId,
            IsPublic = model.IsPublic,
            Tags = model.Tags
        };

        return await _questStore.UpsertQuestNodeTemplateAsync(template);
    }

    public async Task<OASISResult<IEnumerable<QuestNodeTemplate>>> ListNodeTemplatesAsync(OASISRequest? request = null)
    {
        return await _questStore.GetAllQuestNodeTemplatesAsync();
    }
}
