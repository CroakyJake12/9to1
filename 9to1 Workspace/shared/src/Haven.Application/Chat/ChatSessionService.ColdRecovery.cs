using System.Runtime.CompilerServices;
using Haven.Core;

namespace Haven.Application;

public sealed partial class ChatSessionService : ITaskRunOriginalColdCaptureSource
{
    private readonly object _coldCaptureMarker = new();
    private readonly ConditionalWeakTable<HostedInitialTaskSend, ColdInitialCapture> _coldInitialCaptures = new();

    private sealed class ColdInitialCapture(TaskRunColdChatInput input)
    {
        internal readonly TaskRunColdChatInput Input = input;
        internal TaskRunOriginalColdCapture? Capture;
        internal readonly AgentRuntimeOriginalCustody Custody = new();
        internal Task? Driver;
    }

    // Called while the initial producer's actual publication gate remains closed. This only
    // publishes metadata/a gated original; no storage callback executes under that gate.
    internal void RegisterOriginalColdInitialCapture(HostedInitialTaskSend original, Task start)
    {
        if (taskCoordinator?.OriginalColdRecoveryJournal is not { } journal) return;
        if (!_coldInitialCaptures.TryGetValue(original, out var capture))
            throw new InvalidOperationException("No exact initial input was captured for the configured cold journal.");
        capture.Custody.OriginalProcessOwner = original.Coordinator;
        capture.Driver = capture.Custody.Start(async operation =>
        {
            await start.ConfigureAwait(false);
            // The SAME hosted producer remains owned/enrolled by its initial stage.
            // Observe its real terminal without converting a known provider fault into
            // healthy success. Only the exact closed-boundary validators below can seal.
            Exception? producerFailure = null;
            try { await original.Producer!.ConfigureAwait(false); }
            catch (Exception failure) { producerFailure = failure; }
            var invocation = original.Invocation ?? throw new InvalidOperationException("No original hosted invocation exists.");
            var terminal = invocation.OriginalTerminalObservation;
            if (terminal is null || terminal.State != TaskExecutionLifecycle.Suspended) return false;
            if (!invocation.OwnedCleanupTerminal
                || invocation.OriginalConversationWrite is not { IsCompletedSuccessfully: true }
                || invocation.OriginalUserMessageWrite is not { IsCompletedSuccessfully: true }
                || invocation.OriginalPersistedConversation is not { } conversation
                || invocation.OriginalUserMessage is not { } user || !invocation.OriginalUserMessagePublished)
                throw new InvalidOperationException("The genuine hosted input/body/cleanup acknowledgment is not terminal.");
            TaskRunColdCapsule capsule;
            if (terminal.Attempts.Count == 0 && terminal.Plan.Count == 0)
            {
                if (producerFailure is not null || !original.ProducerCustody.Healthy || !original.Source!.HasHealthyClosedOriginal
                    || invocation.Causes.Count != 0 && !invocation.CanReturnPublishedPermissionRefusal(terminal))
                    throw new InvalidOperationException("The never-started source has unresolved business originals.");
                capsule = new(1, Guid.NewGuid(), TaskRunColdBoundaryKind.NeverStartedAcceptedInput,
                    terminal, conversation, user, capture.Input, DateTimeOffset.UtcNow);
                try { TaskRunColdRecoveryBoundary.DemandNeverStarted(capsule, terminal); }
                catch (InvalidOperationException) { return false; }
            }
            else
            {
                if (producerFailure is null || invocation.OriginalToolCheckpoint is null) return false;
                var material = await operation.AwaitAsync(() => original.Coordinator.CaptureOriginalClosedColdToolBoundaryAsync(
                    this, invocation, terminal, new TaskRunColdOriginalSourceScope(operation), CancellationToken.None), owningCleanup: true).ConfigureAwait(false);
                capsule = new(2, Guid.NewGuid(), TaskRunColdBoundaryKind.SettledUnfinishedToolResponse,
                    terminal, conversation, user, capture.Input, DateTimeOffset.UtcNow) { OriginalToolCheckpoint = material };
                TaskRunColdRecoveryBoundary.DemandRestorableBoundary(capsule, terminal);
            }
            var issued = new TaskRunOriginalColdCapture(this, _coldCaptureMarker, original, capsule);
            capture.Capture = issued;
            await operation.AwaitAsync(() => journal.PublishOriginalAsync(issued,
                new TaskRunColdOriginalSourceScope(operation), CancellationToken.None), owningCleanup: true).ConfigureAwait(false);
            return true;
        });
        original.Stage!.RetainSource(capture.Driver);
    }

    private void CaptureOriginalColdInitialInput(HostedInitialTaskSend original, InitialTaskInput input)
    {
        if (taskCoordinator?.OriginalColdRecoveryJournal is null) return;
        _coldInitialCaptures.Add(original, new ColdInitialCapture(new(input.Conversation, input.Prompt,
            input.Model, input.Effort, Array.AsReadOnly(input.Capabilities.ToArray()), input.AgentName,
            input.AgentInstructions, input.DuoMode, input.WorkspaceRoot, input.ProjectContext,
            input.ProjectInstructions, input.Images, input.Prompts is null ? null : Array.AsReadOnly(input.Prompts.ToArray()),
            input.RegisteredContext, input.GenerationOptions, input.FilePermission, input.CommandPermission,
            input.BrowserPermission, input.ExplicitCapabilities is null ? null : Array.AsReadOnly(input.ExplicitCapabilities.ToArray()),
            input.AvailableCapabilities is null ? null : Array.AsReadOnly(input.AvailableCapabilities.ToArray()), input.ComputerUseRequest)));
    }

    public bool IsIssuedOriginalColdCapture(TaskRunOriginalColdCapture sameCapture) =>
        sameCapture is not null && ReferenceEquals(sameCapture.Issuer, this)
        && ReferenceEquals(sameCapture.Marker, _coldCaptureMarker)
        && _coldInitialCaptures.TryGetValue(sameCapture.Original, out var owned)
        && ReferenceEquals(owned.Capture, sameCapture);

    public async Task ValidateOriginalColdCaptureAsync(TaskRunOriginalColdCapture sameCapture, CancellationToken token)
    {
        if (!IsIssuedOriginalColdCapture(sameCapture)) throw new UnauthorizedAccessException("No same source-issued cold capture exists.");
        var invocation = sameCapture.Original.Invocation!;
        if (!invocation.OwnedCleanupTerminal
            || !ReferenceEquals(invocation.OriginalTerminalObservation, sameCapture.Capsule.AcknowledgedTask))
            throw new InvalidOperationException("The actual capture's business originals have no owning terminal acknowledgment.");
        if (sameCapture.Capsule.Boundary == TaskRunColdBoundaryKind.NeverStartedAcceptedInput
            && (!sameCapture.Original.ProducerCustody.Healthy || !sameCapture.Original.Source!.HasHealthyClosedOriginal
                || invocation.Causes.Count != 0 && !invocation.CanReturnPublishedPermissionRefusal(sameCapture.Capsule.AcknowledgedTask)))
            throw new InvalidOperationException("The original never-started body has unresolved faults.");
        var operation = _coldInitialCaptures.GetValue(sameCapture.Original,
            _ => throw new UnauthorizedAccessException("No actual capture custody exists.")).Custody;
        // This writer was admitted before business started; process seal cannot omit its
        // finite cleanup/input validation. It never acquires another provider or tool.
        var conversation = await operation.AwaitAsync(() => conversations.GetAsync(sameCapture.Capsule.AcceptedConversation.Id, token), owningCleanup: true).ConfigureAwait(false);
        var history = await operation.AwaitAsync(() => conversations.GetMessagesAsync(sameCapture.Capsule.AcceptedConversation.Id, token), owningCleanup: true).ConfigureAwait(false);
        if (conversation != sameCapture.Capsule.AcceptedConversation || history.Count != 1
            || history[0] != sameCapture.Capsule.AcceptedUserMessage)
            throw new InvalidOperationException("The source-acknowledged actual Conversation/User changed before capture publication.");
        var current = await operation.AwaitAsync(() => taskCoordinator!.GetAsync(sameCapture.Capsule.AcknowledgedTask.TaskId, token), owningCleanup: true).ConfigureAwait(false);
        if (current is null || System.Text.Json.JsonSerializer.Serialize(current)
            != System.Text.Json.JsonSerializer.Serialize(sameCapture.Capsule.AcknowledgedTask))
            throw new InvalidOperationException("The original safe input checkpoint changed before journal publication.");
        if (sameCapture.Capsule.Boundary == TaskRunColdBoundaryKind.SettledUnfinishedToolResponse)
        {
            var reobserved = await operation.AwaitAsync(() => sameCapture.Original.Coordinator.CaptureOriginalClosedColdToolBoundaryAsync(
                this, invocation, sameCapture.Capsule.AcknowledgedTask, new TaskRunColdOriginalSourceScope(operation), token), owningCleanup: true).ConfigureAwait(false);
            if (System.Text.Json.JsonSerializer.Serialize(reobserved) != System.Text.Json.JsonSerializer.Serialize(sameCapture.Capsule.OriginalToolCheckpoint))
                throw new InvalidOperationException("The genuine settled transcript/action outcomes changed before publication.");
        }
        TaskRunColdRecoveryBoundary.DemandRestorableBoundary(sameCapture.Capsule, current);
    }

    internal IAsyncEnumerable<ChatStreamEvent> CreateOriginalColdContinuation(
        TaskRunColdContinuationBinding binding, CancellationToken token)
    {
        var input = binding.Entry.Capsule.OriginalInput;
        var custody = binding.Invocation;
        custody.OriginalColdContinuation = binding;
        custody.OriginalUserMessage = binding.Entry.Capsule.AcceptedUserMessage;
        custody.OriginalUserMessagePublished = true;
        custody.OriginalPersistedConversation = binding.Entry.Capsule.AcceptedConversation;
        return CreateOriginalSend(input.Conversation, input.Prompt, input.Model, input.Effort,
            input.Capabilities, input.AgentName, input.AgentInstructions, input.DuoMode, input.WorkspaceRoot,
            input.ProjectContext, input.ProjectInstructions, input.Images, token, input.Prompts,
            input.RegisteredContext, input.GenerationOptions, input.FilePermission, input.CommandPermission,
            input.BrowserPermission, input.ExplicitCapabilities, input.AvailableCapabilities,
            input.ComputerUseRequest, new(binding.Acknowledgment.AcknowledgedTask.TaskId,
                binding.Acknowledgment.AcknowledgedTask.ContextId, binding.Acknowledgment.AcknowledgedTask.ExecutionId,
                null, binding.Acknowledgment.AcknowledgedTask.PersistenceRevision),
            TaskRunExecutionIntent.CanonicalAgenticTask, custody);
    }
}

public sealed partial class ChatSessionService
{
    internal ITaskRunColdToolCheckpointSelectionSource RequireOriginalColdToolSelectionSource() =>
        ollama as ITaskRunColdToolCheckpointSelectionSource
        ?? throw new InvalidOperationException("The actual configured router has no fresh source-issued cold local selection.");

    internal IAsyncEnumerable<ChatStreamEvent> CreateOriginalColdToolContinuation(
        TaskRunColdContinuationBinding binding, CancellationToken token)
    {
        var coordinator = taskCoordinator ?? throw new InvalidOperationException("The actual canonical owner is unavailable.");
        coordinator.DemandOriginalColdToolFactory(binding, this);
        var input = binding.Entry.Capsule.OriginalInput;
        var custody = binding.Invocation;
        custody.OriginalColdContinuation = binding;
        custody.OriginalChatOwner = this;
        custody.OriginalConversation = binding.Entry.Capsule.AcceptedConversation;
        custody.OriginalPersistedConversation = binding.Entry.Capsule.AcceptedConversation;
        custody.OriginalUserMessage = binding.Entry.Capsule.AcceptedUserMessage;
        custody.OriginalUserMessagePublished = true;
        custody.OriginalInputCurrentness = cancellation => coordinator.ValidateOriginalColdInputAsync(binding, custody, cancellation);
        // These are durable acknowledged values, not recreated old write/Move/lease Tasks.
        // The SAME existing loop starts at the unfinished model request and never Send/Begin.
        IAsyncEnumerable<ChatStreamEvent> CreateBody(CancellationToken businessToken) => ObserveOriginalSendAsync(
            SendOriginalToolContinuationBodyAsync(new OriginalToolContinuationState(binding),
                binding.Entry.Capsule.AcceptedConversation, input.AgentName, input.WorkspaceRoot,
                input.FilePermission, input.CommandPermission, input.BrowserPermission, businessToken), custody, businessToken);
        return coordinator.RegisterOriginalCanonicalChatProducer(custody, CreateBody, token);
    }
}
