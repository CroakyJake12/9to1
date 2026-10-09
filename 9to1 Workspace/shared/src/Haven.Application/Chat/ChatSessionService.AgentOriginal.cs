using Haven.Core;

namespace Haven.Application;

/// <summary>The exact existing Chat producer and its private original custody. Never deserialized or issued from a public DTO.</summary>
internal sealed class ChatOriginalAgentInvocation(
    ChatSessionService sameChat, Conversation sameConversation,
    TaskRunInvocationCustody sameCustody, IAsyncEnumerable<ChatStreamEvent> sameStream)
{
    internal readonly ChatSessionService Owner = sameChat;
    internal readonly Conversation OriginalConversation = sameConversation;
    internal readonly TaskRunInvocationCustody Original = sameCustody;
    private readonly IAsyncEnumerable<ChatStreamEvent> _stream = sameStream;
    private int _consumed;
    private readonly object _preparationGate = new();
    private readonly List<AgentRuntimeOriginalCustody> _preparationOriginals = [];
    internal Task<T> StartOriginalPreparation<T>(Func<AgentRuntimeOriginalCustody, Task<T>> body)
    {
        AgentRuntimeOriginalCustody actual;
        lock (_preparationGate)
        {
            _preparationOriginals.RemoveAll(static prior => prior.Healthy);
            if (_preparationOriginals.Count >= 128)
                throw new InvalidOperationException("Retained original Agent preparation capacity requires owning inspection.");
            actual = new();
            _preparationOriginals.Add(actual);
        }
        return actual.Start(body);
    }

    internal IAsyncEnumerable<ChatStreamEvent> ConsumeOriginal()
    {
        if (Interlocked.Exchange(ref _consumed, 1) != 0)
            throw new InvalidOperationException("The actual original Agent stream is already consumed.");
        return _stream;
    }

    internal TaskExecutionSnapshot? AcknowledgedObservation =>
        Original.OriginalTerminalObservation ?? Original.OriginalBinding;

    internal bool HasAcknowledgedCompletion =>
        Original.OwnedCleanupTerminal && Original.ReachedEnd && Original.Causes.Count == 0
        && Original.OriginalDispose is { IsCompletedSuccessfully: true }
        && Original.OriginalTrackerDispose is { IsCompletedSuccessfully: true }
        && Original.ResourcesDisposed
        && Original.OriginalCompletion is { IsCompletedSuccessfully: true }
        && Original.OriginalOwningCompletion is { IsCompletedSuccessfully: true }
        && Original.OriginalSettlement is { IsCompletedSuccessfully: true }
        && AcknowledgedObservation is { State: TaskExecutionLifecycle.Completed };

    internal AgentRunCanonicalBinding? BindingObservation()
    {
        var value = AcknowledgedObservation;
        if (value is null) return null;
        if (value.ContextId != OriginalConversation.Id)
            throw new InvalidOperationException("The actual Agent producer changed its original context.");
        return new(value.TaskId, value.ContextId, value.ExecutionId, value.PersistenceRevision,
            value.State, value.Attempts.LastOrDefault()?.Id);
    }
}

public sealed partial class ChatSessionService
{
    /// <summary>Saved Agents use the SAME existing Chat/tool body and strict per-action permissions.</summary>
    internal ChatOriginalAgentInvocation CreateOriginalAgentInvocation(
        Conversation conversation, string prompt, ModelDescriptor model,
        IReadOnlyCollection<ActiveCapability> capabilities, string agentName,
        string instructions, CancellationToken token)
    {
        var custody = (taskCoordinator ?? throw new InvalidOperationException("The canonical Task owner is unavailable."))
            .CreateOriginalInvocationCustody();
        var stream = CreateOriginalSend(conversation, prompt, model, EffortLevel.Medium,
            capabilities, agentName, instructions, DuoMode.Solo, null, null, null, null, token,
            null, null, new GenerationOptions(ActionLimit: 24), PermissionMode.Ask,
            PermissionMode.Ask, PermissionMode.Ask, null, capabilities, null, null,
            TaskRunExecutionIntent.CanonicalAgenticTask, custody);
        return new(this, conversation, custody, stream);
    }

    internal Task<ChatOriginalAgentInvocation> ContinueOriginalAgentInvocationAsync(
        ChatOriginalAgentInvocation sameOriginal, CancellationToken token) =>
        sameOriginal.StartOriginalPreparation(operation => ContinueOriginalAgentBodyAsync(operation, sameOriginal, token));

    private async Task<ChatOriginalAgentInvocation> ContinueOriginalAgentBodyAsync(
        AgentRuntimeOriginalCustody operation, ChatOriginalAgentInvocation sameOriginal, CancellationToken token)
    {
        if (!ReferenceEquals(sameOriginal.Owner, this)
            || !ReferenceEquals(sameOriginal.Original.OriginalChatOwner, this))
            throw new InvalidOperationException("No actual original Agent input producer is retained.");
        var basis = sameOriginal.AcknowledgedObservation
            ?? throw new InvalidOperationException("No acknowledged original canonical binding is retained.");
        var coordinator = taskCoordinator ?? throw new InvalidOperationException("The canonical Task owner is unavailable.");
        var inspection = await operation.AwaitAsync(() => coordinator.InspectOriginalRecoveryAsync(basis.TaskId, basis.ExecutionId, token)).ConfigureAwait(false);
        if (!ReferenceEquals(inspection.OriginalCustody, sameOriginal.Original))
            throw new InvalidOperationException("Another original invocation owns this task's recovery custody.");
        var prepared = await operation.AwaitAsync(() => coordinator.PrepareOriginalUnstartedContinuationAsync(inspection, this, token)).ConfigureAwait(false);
        var actual = coordinator.ClaimOriginalUnstartedContinuation(prepared, this, token);
        var next = new ChatOriginalAgentInvocation(this, sameOriginal.OriginalConversation, prepared.Next, actual);
        return operation.Invoke(() => coordinator.BindOriginalDelegatedAgentSuccessor(sameOriginal, prepared, next));
    }
}
