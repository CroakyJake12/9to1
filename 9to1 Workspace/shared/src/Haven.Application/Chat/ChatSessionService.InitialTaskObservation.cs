using System.Collections.Frozen;
using System.Runtime.ExceptionServices;
using Haven.Core;

namespace Haven.Application;

public sealed partial class ChatSessionService
{
    private readonly object _initialTaskObservationIssuer = new();

    public Task<TaskRunOriginalInitialChatObservationLease> StartObservedOriginalTaskSendAsync(
        Conversation conversation, string prompt, ModelDescriptor model, EffortLevel effort,
        IReadOnlyCollection<ActiveCapability> capabilities, string agentName, string agentInstructions,
        DuoMode duoMode, string? workspaceRoot, string? projectContext, string? projectInstructions,
        IReadOnlyList<string>? images, CancellationToken observationCancellationToken,
        IReadOnlyCollection<ActivePrompt>? prompts = null, string? registeredContext = null,
        GenerationOptions? generationOptions = null, PermissionMode filePermission = PermissionMode.FullAccess,
        PermissionMode commandPermission = PermissionMode.FullAccess, PermissionMode browserPermission = PermissionMode.FullAccess,
        IReadOnlyCollection<ToolCapability>? explicitCapabilities = null,
        IReadOnlyCollection<ActiveCapability>? availableCapabilities = null, ComputerUseRequest? computerUseRequest = null)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        if (conversation.Id == Guid.Empty || conversation.Mode != HavenMode.Tasks)
            throw new InvalidOperationException("Initial hosted Tasks input requires its actual Tasks conversation.");
        if (observationCancellationToken.IsCancellationRequested)
            return Task.FromCanceled<TaskRunOriginalInitialChatObservationLease>(observationCancellationToken);
        var coordinator = taskCoordinator ?? throw new InvalidOperationException("The actual canonical Task owner is unavailable.");
        var original = new HostedInitialTaskSend(this, coordinator, _initialTaskObservationIssuer, conversation.Id);
        InitialTaskInput? input = null;
        original.Acquisition = coordinator.StartOriginalInitialTaskObservation(original, async token =>
        {
            await original.InputReady.Task.ConfigureAwait(false);
            if (original.InputFailure is { } failure) ExceptionDispatchInfo.Capture(failure).Throw();
            return await original.Stage!.Await(() => PrepareInitialTaskObservationAsync(original,
                input ?? throw new InvalidOperationException("No detached original initial input was captured."), token)).ConfigureAwait(false);
        });
        var acquisitionWait = new InitialChatObserverWaitOriginal<TaskRunOriginalInitialChatObservationLease>(
            original, original.Acquisition, observationCancellationToken, captureLateSource: true);
        lock (original.Gate)
        { original.AcquisitionObservationWait = acquisitionWait; original.AcquisitionWait = acquisitionWait.Driver; }
        original.Stage!.RetainSource(acquisitionWait.Driver); // Before either gate: failed/late acquisitions are also independently drained.
        // Snapshot before returning to caller, with a real prepublished acquisition driver.
        // Restored ExecutionContext cannot bypass this same source's physical join guard.
        try
        {
            input = original.Stage!.Invoke(() => new InitialTaskInput(conversation, prompt,
                model with { Capabilities = model.Capabilities.ToFrozenSet() }, effort,
                Array.AsReadOnly(capabilities.ToArray()), agentName, agentInstructions, duoMode,
                workspaceRoot, projectContext, projectInstructions,
                images is null ? null : Array.AsReadOnly(images.ToArray()),
                prompts is null ? null : Array.AsReadOnly(prompts.ToArray()), registeredContext,
                generationOptions, filePermission, commandPermission, browserPermission,
                explicitCapabilities is null ? null : Array.AsReadOnly(explicitCapabilities.ToArray()),
                availableCapabilities is null ? null : Array.AsReadOnly(availableCapabilities.ToArray()),
                computerUseRequest is null ? null : computerUseRequest with
                { Invocations = Array.AsReadOnly(computerUseRequest.Invocations.ToArray()) }));
        }
        catch (Exception cause) { original.InputFailure = cause; }
        finally { acquisitionWait.StartOriginal(); original.InputReady.TrySetResult(); }
        return acquisitionWait.Driver;
    }

    public bool IsIssuedOriginalTaskObservation(TaskRunOriginalInitialChatObservationLease sameObservation) =>
        sameObservation is not null && ReferenceEquals(sameObservation.Issuer, this)
        && ReferenceEquals(sameObservation.Original.Chat, this)
        && ReferenceEquals(sameObservation.Original.Marker, _initialTaskObservationIssuer)
        && ReferenceEquals(sameObservation.Original.Lease, sameObservation);

    /// <summary>Pure pending acquisition/command source guard; requests no process retirement.</summary>
    public void DemandExternalOriginalTaskObservationSourceJoin() =>
        (taskCoordinator ?? throw new InvalidOperationException("The actual canonical Task owner is unavailable."))
            .DemandExternalOriginalProcessJoin();

    public Task<TaskRunOriginalRunControlResult> StopObservedOriginalTaskAsync(
        TaskRunOriginalInitialChatObservationLease sameObservation, CancellationToken commandCancellationToken)
    {
        if (!IsIssuedOriginalTaskObservation(sameObservation))
            throw new UnauthorizedAccessException("Only the SAME privately issued initial Tasks observation identifies its original input.");
        var context = sameObservation.Original.ReadAcknowledgedContext()
            ?? throw new InvalidOperationException("The initial producer has not acknowledged a canonical Task/run yet.");
        var coordinator = sameObservation.Original.Coordinator;
        if (!ReferenceEquals(coordinator, taskCoordinator))
            throw new InvalidOperationException("The actual original Chat coordinator changed.");
        return coordinator.StopOriginalRunAsync(context.TaskId, context.ExecutionId, commandCancellationToken);
    }

    private async Task<TaskRunOriginalInitialChatObservationLease> PrepareInitialTaskObservationAsync(
        HostedInitialTaskSend original, InitialTaskInput input, CancellationToken token)
    {
        var stage = original.Stage ?? throw new InvalidOperationException("No actual initial source stage was published.");
        if (input.Conversation.IsTemporary || input.Conversation.Kind != ConversationKind.Task)
            throw new InvalidOperationException("Initial hosted Tasks input requires its genuine durable Task draft.");
        await stage.Await(() => original.Coordinator.ValidateOriginalInitialTaskContextAsync(original, token)).ConfigureAwait(false);
        var stored = await stage.Await(() => conversations.GetAsync(input.Conversation.Id, token)).ConfigureAwait(false);
        if (stored is not null && (stored.Mode != HavenMode.Tasks || stored.IsTemporary
            || (stored with { Title = input.Conversation.Title, UpdatedAt = input.Conversation.UpdatedAt }) != input.Conversation))
            throw new InvalidOperationException("The actual initial Tasks draft identity or scope changed.");
        var history = await stage.Await(() => conversations.GetMessagesAsync(input.Conversation.Id, token)).ConfigureAwait(false);
        if (history.Count != 0) throw new InvalidOperationException("Existing Tasks input cannot be replaced by another initial Send.");
        await stage.Await(() => original.Coordinator.ValidateOriginalInitialTaskContextAsync(original, token)).ConfigureAwait(false);
        var currentDraft = await stage.Await(() => conversations.GetAsync(input.Conversation.Id, token)).ConfigureAwait(false);
        if (currentDraft != stored)
            throw new InvalidOperationException("The actual initial draft changed during context/history admission.");
        if (stored != input.Conversation)
        {
            // Initial new work may persist its actual fresh draft or title/time change. Accepted
            // input/context is refused above; every protected stored draft field must still match.
            original.OriginalDraftWrite = stage.Invoke(() =>
            {
                var actualWrite = conversations.UpsertConversationAsync(input.Conversation, token)
                    ?? throw new InvalidOperationException("No actual initial draft write was returned.");
                stage.RetainSource(actualWrite); // Enrollment is part of this SAME acquisition, before any later seal/token check.
                return actualWrite;
            });
            await stage.Await(() => original.OriginalDraftWrite
                ?? throw new InvalidOperationException("No actual initial draft write was returned."), owningCleanup: true).ConfigureAwait(false);
        }
        stored = await stage.Await(() => conversations.GetAsync(input.Conversation.Id, token)).ConfigureAwait(false);
        if (stored is null || stored != input.Conversation)
            throw new InvalidOperationException("The actual initial conversation changed before its source factory.");
        history = await stage.Await(() => conversations.GetMessagesAsync(stored.Id, token)).ConfigureAwait(false);
        if (history.Count != 0) throw new InvalidOperationException("Original Tasks input already exists; use its actual task commands.");
        await stage.Await(() => original.Coordinator.ValidateOriginalInitialTaskContextAsync(original, token)).ConfigureAwait(false);
        try
        {
            stage.Invoke(() => { CaptureOriginalColdInitialInput(original, input); return true; });
            original.Invocation = stage.Invoke(original.Coordinator.CreateOriginalInvocationCustody);
            original.Source = stage.Invoke(() => CreateOriginalSend(input.Conversation, input.Prompt,
                input.Model, input.Effort, input.Capabilities, input.AgentName, input.AgentInstructions,
                input.DuoMode, input.WorkspaceRoot, input.ProjectContext, input.ProjectInstructions,
                input.Images, CancellationToken.None, input.Prompts, input.RegisteredContext,
                input.GenerationOptions, input.FilePermission, input.CommandPermission, input.BrowserPermission,
                input.ExplicitCapabilities, input.AvailableCapabilities, input.ComputerUseRequest,
                executionContext: null, taskExecutionIntent: TaskRunExecutionIntent.CanonicalAgenticTask, originalCustody: original.Invocation))
                as CanonicalChatProcessProducer ?? throw new InvalidOperationException("The original initial Send was not registered by its canonical owner.");
            return original.Coordinator.PublishOriginalInitialTaskObservation(original, RunInitialTaskProducerAsync).Lease!;
        }
        catch (Exception)
        {
            original.Coordinator.ReleaseOriginalUnpublishedInitialInput(original);
            throw;
        }
    }

    private async Task<TaskRunInitialChatObservationResult> RunInitialTaskProducerAsync(
        HostedInitialTaskSend original, AgentRuntimeOriginalCustody operation)
    {
        IAsyncEnumerator<ChatStreamEvent>? iterator = null;
        try
        {
            operation.Invoke(() =>
            {
                original.Projection = (_, snapshot) => original.RecordAcknowledgment(snapshot);
                original.Coordinator.AttachOriginalInitialTaskProjection(original);
                return true;
            });
            iterator = operation.Invoke(() =>
            {
                original.Coordinator.DemandOriginalInitialTaskBodyAdmission(original);
                return original.Source!.GetAsyncEnumerator(CancellationToken.None);
            });
            while (await operation.AwaitAsync(() =>
            {
                original.Coordinator.DemandOriginalInitialTaskBodyAdmission(original);
                return iterator.MoveNextAsync().AsTask();
            }).ConfigureAwait(false))
            {
                var value = operation.Invoke(() => iterator.Current);
                _ = original.ReadAcknowledgedContext();
                await operation.AwaitAsync(() => original.PublishEventAsync(value)).ConfigureAwait(false);
            }
            _ = original.ReadAcknowledgedContext();
        }
        finally
        {
            try
            {
                if (iterator is not null)
                    await operation.AwaitAsync(() => iterator.DisposeAsync().AsTask(), owningCleanup: true).ConfigureAwait(false);
            }
            finally
            {
                try { operation.Invoke(() => { original.Coordinator.DetachOriginalInitialTaskProjection(original); return true; }, owningCleanup: true); }
                finally { original.MarkProducerTerminal(); }
            }
        }
        var actual = original.Invocation ?? throw new InvalidOperationException("No privately issued initial invocation exists.");
        var terminal = actual.OriginalTerminalObservation
            ?? throw new InvalidOperationException("The actual initial producer has no acknowledged terminal observation.");
        original.RecordAcknowledgment(terminal);
        if (!actual.OwnedCleanupTerminal || !original.Source!.HasHealthyClosedOriginal)
            throw new InvalidOperationException("Actual initial body/Move/Dispose/cleanup are not all successfully terminal.");
        return new(TaskRunInitialChatObservationDisposition.ProducerTerminal, original.ReadAcknowledgedContext());
    }

    private sealed record InitialTaskInput(Conversation Conversation, string Prompt, ModelDescriptor Model,
        EffortLevel Effort, IReadOnlyCollection<ActiveCapability> Capabilities, string AgentName,
        string AgentInstructions, DuoMode DuoMode, string? WorkspaceRoot, string? ProjectContext,
        string? ProjectInstructions, IReadOnlyList<string>? Images, IReadOnlyCollection<ActivePrompt>? Prompts,
        string? RegisteredContext, GenerationOptions? GenerationOptions, PermissionMode FilePermission,
        PermissionMode CommandPermission, PermissionMode BrowserPermission,
        IReadOnlyCollection<ToolCapability>? ExplicitCapabilities,
        IReadOnlyCollection<ActiveCapability>? AvailableCapabilities, ComputerUseRequest? ComputerUseRequest);
}
