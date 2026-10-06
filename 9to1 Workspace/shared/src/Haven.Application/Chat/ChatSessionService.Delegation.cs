using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>One detached original saved-Agent input. Neither this digest nor its fields grant child dispatch.</summary>
internal sealed class TaskRunDelegatedAgentInput
{
    internal TaskRunDelegatedAgentInput(ChatSessionService owner, string prompt, ModelDescriptor model,
        IReadOnlyCollection<ActiveCapability> capabilities, string agentName, string instructions)
    {
        Owner = owner; Prompt = prompt; AgentName = agentName; Instructions = instructions;
        Model = model with { Capabilities = model.Capabilities.ToFrozenSet() };
        Capabilities = Array.AsReadOnly(capabilities.ToArray());
        Digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            Prompt, AgentName, Instructions, Model.Name, Model.SizeBytes, Model.Family,
            Model.ParameterSize, Model.Quantization, Model.ModifiedAt,
            ModelCapabilities = Model.Capabilities.OrderBy(value => (int)value).ToArray(), Capabilities
        })))).ToLowerInvariant();
    }
    internal ChatSessionService Owner { get; }
    internal string Prompt { get; }
    internal string AgentName { get; }
    internal string Instructions { get; }
    internal ModelDescriptor Model { get; }
    internal IReadOnlyList<ActiveCapability> Capabilities { get; }
    internal string Digest { get; }
}

public sealed partial class ChatSessionService
{
    internal TaskExecutionCoordinator OriginalDelegationTaskOwner => taskCoordinator
        ?? throw new InvalidOperationException("The actual canonical Task owner is unavailable for delegation.");

    internal TaskRunDelegatedAgentInput CaptureOriginalDelegatedAgentInput(string prompt, ModelDescriptor model,
        IReadOnlyCollection<ActiveCapability> capabilities, string agentName, string instructions) =>
        new(this, prompt, model, capabilities, agentName, instructions);

    /// <summary>Reuses the actual existing Chat body only after the private creation/link owners acknowledged this fixed child.</summary>
    internal ChatOriginalAgentInvocation CreateOriginalDelegatedAgentInvocation(
        TaskRunDelegatedChildLinkAcknowledgment actualLink, TaskRunDelegatedAgentInput actualInput, CancellationToken token)
    {
        if (!ReferenceEquals(actualInput.Owner, this))
            throw new InvalidOperationException("The exact detached child input belongs to another Chat producer.");
        return OriginalDelegationTaskOwner.CreateOriginalDelegatedChatProducer(actualLink, actualInput, () =>
        {
            var child = actualLink.Child;
            var conversation = new Conversation(child.ContextId, HavenMode.Tasks, ConversationKind.Chat,
                "Agent · " + actualInput.AgentName, null, null, false, true, child.CreatedAt, child.CreatedAt);
            var custody = OriginalDelegationTaskOwner.CreateOriginalInvocationCustody();
            custody.OriginalDelegatedChildLink = actualLink;
            var observation = new ProviderExecutionContext(child.TaskId, child.ContextId, child.ExecutionId,
                child.Attempts.LastOrDefault()?.Id, child.PersistenceRevision);
            var stream = CreateOriginalSend(conversation, actualInput.Prompt, actualInput.Model, EffortLevel.Medium,
                actualInput.Capabilities, actualInput.AgentName, actualInput.Instructions, DuoMode.Solo,
                null, null, null, null, token, null, null, new GenerationOptions(ActionLimit: 24),
                PermissionMode.Ask, PermissionMode.Ask, PermissionMode.Ask, null, actualInput.Capabilities,
                null, observation, TaskRunExecutionIntent.CanonicalAgenticTask, custody);
            return new(this, conversation, custody, stream);
        });
    }
}
