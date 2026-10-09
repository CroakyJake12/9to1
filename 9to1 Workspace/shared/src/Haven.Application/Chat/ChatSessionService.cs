/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ChatSessionService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns ChatSessionService, ChatStreamEvent, ChatStreamEventKind. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents chat session service and keeps its related state and behavior together.
/// </summary>
public sealed partial class ChatSessionService(
    IConversationRepository conversations,
    IOllamaClient ollama,
    CapabilityPreflightService preflight,
    IConversationSafetyService safety,
    WorkspaceToolRuntime workspaceTools,
    ComputerToolRuntime computerTools,
    BrowserToolRuntime? browserTools = null,
    AutomationToolRuntime? automationTools = null,
    ToolAvailabilityPlanner? toolAvailability = null,
    ChatModelInventoryCache? modelInventory = null,
    McpToolRuntime? mcpTools = null,
    CalendarConnectionToolRuntime? calendarTools = null,
    PluginToolRuntime? pluginTools = null,
    IExecutionEventSink? executionEvents = null,
    AutonomousRecoveryService? recovery = null,
    RemediationCoordinator? remediations = null,
    ModelPersonalityService? personalities = null,
    ModelPermissionEvaluator? modelPermissions = null,
    IDefaultProviderStore? defaultProviders = null,
    CheckpointService? checkpoints = null,
    IProjectInstructionSource? projectInstructionFiles = null,
    IMemoryQuerySource? memorySource = null,
    TaskExecutionCoordinator? taskCoordinator = null,
    ITaskRunToolActionOwner? taskToolOwner = null,
    ITaskRunProviderContextCapture? taskProviderContextCapture = null,
    TaskRunCloudPermissionRemediationOwner? taskCloudPermissionRemediation = null,
    IChatOriginalPersistentMemorySource? originalPersistentMemorySource = null)
{
    private IChatOriginalPersistentMemorySource? _originalPersistentMemorySource = originalPersistentMemorySource;

    // Preserve the exact existing 26-argument CLR constructor while opting into
    // the SAME configured scoped memory source in the new constructor.
    public ChatSessionService(
    IConversationRepository conversations,
    IOllamaClient ollama,
    CapabilityPreflightService preflight,
    IConversationSafetyService safety,
    WorkspaceToolRuntime workspaceTools,
    ComputerToolRuntime computerTools,
    BrowserToolRuntime? browserTools,
    AutomationToolRuntime? automationTools,
    ToolAvailabilityPlanner? toolAvailability,
    ChatModelInventoryCache? modelInventory,
    McpToolRuntime? mcpTools,
    CalendarConnectionToolRuntime? calendarTools,
    PluginToolRuntime? pluginTools,
    IExecutionEventSink? executionEvents,
    AutonomousRecoveryService? recovery,
    RemediationCoordinator? remediations,
    ModelPersonalityService? personalities,
    ModelPermissionEvaluator? modelPermissions,
    IDefaultProviderStore? defaultProviders,
    CheckpointService? checkpoints,
    IProjectInstructionSource? projectInstructionFiles,
    IMemoryQuerySource? memorySource,
    TaskExecutionCoordinator? taskCoordinator,
    ITaskRunToolActionOwner? taskToolOwner,
    ITaskRunProviderContextCapture? taskProviderContextCapture,
    TaskRunCloudPermissionRemediationOwner? taskCloudPermissionRemediation)
        : this(conversations, ollama, preflight, safety, workspaceTools, computerTools, browserTools, automationTools, toolAvailability, modelInventory, mcpTools, calendarTools, pluginTools, executionEvents, recovery, remediations, personalities, modelPermissions, defaultProviders, checkpoints, projectInstructionFiles, memorySource, taskCoordinator, taskToolOwner, taskProviderContextCapture, taskCloudPermissionRemediation, originalPersistentMemorySource: null)
    { }


    // Preserve the selected 25-argument CLR constructor; adding the Ask owner is opt-in.
    public ChatSessionService(
        IConversationRepository conversations,
        IOllamaClient ollama,
        CapabilityPreflightService preflight,
        IConversationSafetyService safety,
        WorkspaceToolRuntime workspaceTools,
        ComputerToolRuntime computerTools,
        BrowserToolRuntime? browserTools,
        AutomationToolRuntime? automationTools,
        ToolAvailabilityPlanner? toolAvailability,
        ChatModelInventoryCache? modelInventory,
        McpToolRuntime? mcpTools,
        CalendarConnectionToolRuntime? calendarTools,
        PluginToolRuntime? pluginTools,
        IExecutionEventSink? executionEvents,
        AutonomousRecoveryService? recovery,
        RemediationCoordinator? remediations,
        ModelPersonalityService? personalities,
        ModelPermissionEvaluator? modelPermissions,
        IDefaultProviderStore? defaultProviders,
        CheckpointService? checkpoints,
        IProjectInstructionSource? projectInstructionFiles,
        IMemoryQuerySource? memorySource,
        TaskExecutionCoordinator? taskCoordinator,
        ITaskRunToolActionOwner? taskToolOwner,
        ITaskRunProviderContextCapture? taskProviderContextCapture)
        : this(conversations, ollama, preflight, safety, workspaceTools, computerTools, browserTools, automationTools, toolAvailability, modelInventory, mcpTools, calendarTools, pluginTools, executionEvents, recovery, remediations, personalities, modelPermissions, defaultProviders, checkpoints, projectInstructionFiles, memorySource, taskCoordinator, taskToolOwner, taskProviderContextCapture, taskCloudPermissionRemediation: null)
    { }

    // Preserve the already compiled Source09 constructor while adding the context capture port.
    public ChatSessionService(
        IConversationRepository conversations,
        IOllamaClient ollama,
        CapabilityPreflightService preflight,
        IConversationSafetyService safety,
        WorkspaceToolRuntime workspaceTools,
        ComputerToolRuntime computerTools,
        BrowserToolRuntime? browserTools,
        AutomationToolRuntime? automationTools,
        ToolAvailabilityPlanner? toolAvailability,
        ChatModelInventoryCache? modelInventory,
        McpToolRuntime? mcpTools,
        CalendarConnectionToolRuntime? calendarTools,
        PluginToolRuntime? pluginTools,
        IExecutionEventSink? executionEvents,
        AutonomousRecoveryService? recovery,
        RemediationCoordinator? remediations,
        ModelPersonalityService? personalities,
        ModelPermissionEvaluator? modelPermissions,
        IDefaultProviderStore? defaultProviders,
        CheckpointService? checkpoints,
        IProjectInstructionSource? projectInstructionFiles,
        IMemoryQuerySource? memorySource,
        TaskExecutionCoordinator? taskCoordinator,
        ITaskRunToolActionOwner? taskToolOwner)
        : this(conversations, ollama, preflight, safety, workspaceTools, computerTools, browserTools, automationTools, toolAvailability, modelInventory, mcpTools, calendarTools, pluginTools, executionEvents, recovery, remediations, personalities, modelPermissions, defaultProviders, checkpoints, projectInstructionFiles, memorySource, taskCoordinator, taskToolOwner, taskProviderContextCapture: null)
    {
    }

    // Retain the exact pre-agentic CLR constructor for already compiled consumers.
    public ChatSessionService(
        IConversationRepository conversations,
        IOllamaClient ollama,
        CapabilityPreflightService preflight,
        IConversationSafetyService safety,
        WorkspaceToolRuntime workspaceTools,
        ComputerToolRuntime computerTools,
        BrowserToolRuntime? browserTools,
        AutomationToolRuntime? automationTools,
        ToolAvailabilityPlanner? toolAvailability,
        ChatModelInventoryCache? modelInventory,
        McpToolRuntime? mcpTools,
        CalendarConnectionToolRuntime? calendarTools,
        PluginToolRuntime? pluginTools,
        IExecutionEventSink? executionEvents,
        AutonomousRecoveryService? recovery,
        RemediationCoordinator? remediations,
        ModelPersonalityService? personalities,
        ModelPermissionEvaluator? modelPermissions,
        IDefaultProviderStore? defaultProviders,
        CheckpointService? checkpoints,
        IProjectInstructionSource? projectInstructionFiles,
        IMemoryQuerySource? memorySource)
        : this(conversations, ollama, preflight, safety, workspaceTools, computerTools, browserTools, automationTools, toolAvailability, modelInventory, mcpTools, calendarTools, pluginTools, executionEvents, recovery, remediations, personalities, modelPermissions, defaultProviders, checkpoints, projectInstructionFiles, memorySource, taskCoordinator: null, taskToolOwner: null)
    {
    }

    private readonly ChatModelInventoryCache _modelInventory =
        modelInventory ?? new ChatModelInventoryCache(ollama);

    public event Action<ChatExecutionSnapshot>? ExecutionChanged;

    public ChatExecutionSnapshot? CurrentExecution { get; private set; }
    public TaskExecutionSnapshot? CurrentCanonicalTask { get; private set; }

    /// <summary>
    /// Retrieves tool availability for the current operation.
    /// </summary>
    public ToolAvailabilityPlan GetToolAvailability(
        HavenMode mode,
        string? workspaceRoot,
        IReadOnlyCollection<ActiveCapability> capabilities,
        PermissionMode filePermission,
        PermissionMode commandPermission,
        PermissionMode browserPermission)
    {
        using var computerPass = computerTools.CreatePass();
        return CreateAvailabilityPlan(mode, workspaceRoot, capabilities, filePermission, commandPermission, browserPermission,
            computerPass.Definitions);
    }

    public bool CanUseCapability(
        ActiveCapability capability,
        HavenMode mode,
        string? workspaceRoot,
        PermissionMode filePermission,
        PermissionMode commandPermission,
        PermissionMode browserPermission) =>
        GetToolAvailability(mode, workspaceRoot, [capability],
            Approvable(filePermission), Approvable(commandPermission), Approvable(browserPermission))
        .IsCapabilityAvailable(capability.Key);

    /// <summary>Classic-only compatibility boundary pending deletion of its picker.</summary>
    

    /// <summary>
    /// Performs send asynchronously so I/O does not block the caller's thread.
    /// </summary>
    // Preserve the original CLR method signature. Legacy calls do not opt into canonical tasks.
    public IAsyncEnumerable<ChatStreamEvent> SendAsync(
        Conversation conversation,
        string prompt,
        ModelDescriptor model,
        EffortLevel effort,
        IReadOnlyCollection<ActiveCapability> capabilities,
        string agentName,
        string agentInstructions,
        DuoMode duoMode,
        string? workspaceRoot,
        string? projectContext,
        string? projectInstructions,
        IReadOnlyList<string>? images,
        CancellationToken cancellationToken,
        IReadOnlyCollection<ActivePrompt>? prompts,
        string? registeredContext,
        GenerationOptions? generationOptions,
        PermissionMode filePermission,
        PermissionMode commandPermission,
        PermissionMode browserPermission,
        IReadOnlyCollection<ToolCapability>? explicitCapabilities,
        IReadOnlyCollection<ActiveCapability>? availableCapabilities,
        ComputerUseRequest? computerUseRequest) =>
        SendAsync(conversation, prompt, model, effort, capabilities, agentName, agentInstructions, duoMode, workspaceRoot, projectContext, projectInstructions, images, cancellationToken, prompts, registeredContext, generationOptions, filePermission, commandPermission, browserPermission, explicitCapabilities, availableCapabilities, computerUseRequest,
            executionContext: null, taskExecutionIntent: TaskRunExecutionIntent.OrdinaryConversation);

    public IAsyncEnumerable<ChatStreamEvent> SendAsync(
        Conversation conversation,
        string prompt,
        ModelDescriptor model,
        EffortLevel effort,
        IReadOnlyCollection<ActiveCapability> capabilities,
        string agentName,
        string agentInstructions,
        DuoMode duoMode,
        string? workspaceRoot,
        string? projectContext,
        string? projectInstructions,
        IReadOnlyList<string>? images,
        CancellationToken cancellationToken,
        IReadOnlyCollection<ActivePrompt>? prompts = null,
        string? registeredContext = null,
        GenerationOptions? generationOptions = null,
        PermissionMode filePermission = PermissionMode.FullAccess,
        PermissionMode commandPermission = PermissionMode.FullAccess,
        PermissionMode browserPermission = PermissionMode.FullAccess,
        IReadOnlyCollection<ToolCapability>? explicitCapabilities = null,
        IReadOnlyCollection<ActiveCapability>? availableCapabilities = null,
        ComputerUseRequest? computerUseRequest = null,
        ProviderExecutionContext? executionContext = null,
        TaskRunExecutionIntent taskExecutionIntent = TaskRunExecutionIntent.OrdinaryConversation)
    {
        var originalCustody = taskExecutionIntent == TaskRunExecutionIntent.CanonicalAgenticTask
            ? taskCoordinator?.CreateOriginalInvocationCustody() : null;
        return CreateOriginalSend(conversation, prompt, model, effort, capabilities, agentName,
            agentInstructions, duoMode, workspaceRoot, projectContext, projectInstructions, images,
            cancellationToken, prompts, registeredContext, generationOptions, filePermission,
            commandPermission, browserPermission, explicitCapabilities, availableCapabilities,
            computerUseRequest, executionContext, taskExecutionIntent, originalCustody);
    }

    private IAsyncEnumerable<ChatStreamEvent> CreateOriginalSend(
        Conversation conversation, string prompt, ModelDescriptor model, EffortLevel effort,
        IReadOnlyCollection<ActiveCapability> capabilities, string agentName, string agentInstructions,
        DuoMode duoMode, string? workspaceRoot, string? projectContext, string? projectInstructions,
        IReadOnlyList<string>? images, CancellationToken cancellationToken,
        IReadOnlyCollection<ActivePrompt>? prompts, string? registeredContext,
        GenerationOptions? generationOptions, PermissionMode filePermission,
        PermissionMode commandPermission, PermissionMode browserPermission,
        IReadOnlyCollection<ToolCapability>? explicitCapabilities,
        IReadOnlyCollection<ActiveCapability>? availableCapabilities,
        ComputerUseRequest? computerUseRequest, ProviderExecutionContext? executionContext,
        TaskRunExecutionIntent taskExecutionIntent, TaskRunInvocationCustody? originalCustody,
        ChatOrdinaryOriginalInvocation? ordinaryOriginal = null)
    {
        if (originalCustody is not null)
        {
            // One detached immutable snapshot is used by BOTH actual initial input and
            // its private continuation. Ordinary conversation behavior is unchanged.
            capabilities = Array.AsReadOnly(capabilities.ToArray());
            prompts = prompts is null ? null : Array.AsReadOnly(prompts.ToArray());
            images = images is null ? null : Array.AsReadOnly(images.ToArray());
            explicitCapabilities = explicitCapabilities is null ? null : Array.AsReadOnly(explicitCapabilities.ToArray());
            availableCapabilities = availableCapabilities is null ? null : Array.AsReadOnly(availableCapabilities.ToArray());
            model = model with { Capabilities = model.Capabilities.ToFrozenSet() };
            computerUseRequest = computerUseRequest is null ? null : computerUseRequest with
            { Invocations = Array.AsReadOnly(computerUseRequest.Invocations.ToArray()) };
            originalCustody.OriginalChatOwner = this;
            originalCustody.OriginalConversation = conversation;
            originalCustody.OriginalInputCurrentness = token => ValidateOriginalInputAsync(originalCustody, token);
            originalCustody.OriginalContinuationFactory = (nextCustody, context, token) => CreateOriginal(nextCustody, context, token);
            originalCustody.OriginalToolContinuationFactory = (binding, token) =>
                CreateOriginalToolCheckpointContinuation(binding, conversation, agentName, workspaceRoot,
                    filePermission, commandPermission, browserPermission, token);
        }
        IAsyncEnumerable<ChatStreamEvent> CreateOriginal(TaskRunInvocationCustody? custody, ProviderExecutionContext? context, CancellationToken token)
        {
            IAsyncEnumerable<ChatStreamEvent> CreateBody(CancellationToken originalToken)
            {
                var original = SendOriginalAsync(conversation, prompt, model, effort, capabilities, agentName, agentInstructions,
                    duoMode, workspaceRoot, projectContext, projectInstructions, images, originalToken, prompts, registeredContext,
                    generationOptions, filePermission, commandPermission, browserPermission, explicitCapabilities,
                    availableCapabilities, computerUseRequest, context, taskExecutionIntent, custody, ordinaryOriginal);
                return custody is null ? original : ObserveOriginalSendAsync(original, custody, originalToken);
            }
            return custody is null ? CreateBody(token)
                : taskCoordinator!.RegisterOriginalCanonicalChatProducer(custody, CreateBody, token);
        }
        return CreateOriginal(originalCustody, executionContext, cancellationToken);
    }

    /// <summary>Explicit continuation of the SAME source-approved, live, never-started run.
    /// It is unavailable for unknown work, invoked attempts or reconstructed historical inputs.</summary>
    public IAsyncEnumerable<ChatStreamEvent> ContinueUnstartedOriginalAsync(
        Guid taskId, Guid expectedExecutionId, CancellationToken cancellationToken)
    {
        var coordinator = taskCoordinator ?? throw new InvalidOperationException("The canonical Task owner is unavailable.");
        var original = new CanonicalContinuationProcessCustody(coordinator);
        return coordinator.RegisterOriginalContinuationProcessProducer(original,
            token => ContinueUnstartedOriginalBodyAsync(taskId, expectedExecutionId, original, token), cancellationToken);
    }

    private async IAsyncEnumerable<ChatStreamEvent> ContinueUnstartedOriginalBodyAsync(
        Guid taskId, Guid expectedExecutionId, CanonicalContinuationProcessCustody original,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var coordinator = taskCoordinator ?? throw new InvalidOperationException("The canonical Task owner is unavailable.");
        var inspection = await original.Await("process.continuation-inspection", () =>
            coordinator.InspectOriginalRecoveryAsync(taskId, expectedExecutionId, cancellationToken)).ConfigureAwait(false);
        if (inspection.OriginalCustody?.OriginalDelegatedChildLink is not null)
            throw new InvalidOperationException("The original saved child Agent must continue through its retained Retry producer; public IDs cannot replace it.");
        var prepared = await original.Await("process.continuation-preparation", () =>
            coordinator.PrepareOriginalUnstartedContinuationAsync(inspection, this, cancellationToken)).ConfigureAwait(false);
        original.OriginalPreparation = prepared;
        var child = original.Invoke(() => coordinator.ClaimOriginalUnstartedContinuation(prepared, this, cancellationToken));
        original.OriginalChild = child;
        var iterator = original.Invoke(() => child.GetAsyncEnumerator(cancellationToken));
        try
        {
            while (await original.Await("process.continuation-child-move", () => iterator.MoveNextAsync().AsTask()).ConfigureAwait(false))
                yield return original.Invoke(() => iterator.Current);
        }
        finally { await original.DisposeChildAsync(iterator).ConfigureAwait(false); }
    }

    private async Task ValidateOriginalInputAsync(TaskRunInvocationCustody original, CancellationToken token)
    {
        if (!ReferenceEquals(original.OriginalChatOwner, this) || original.OriginalConversation is not { } conversation
            || original.OriginalUserMessage is not { } message || !original.OriginalUserMessagePublished)
            throw new InvalidOperationException("The actual original accepted user input is unavailable.");
        if (original.OriginalColdContinuation is { } cold)
        {
            await taskCoordinator!.ValidateOriginalColdInputAsync(cold, original, token).ConfigureAwait(false);
            return;
        }
        if (conversation.IsTemporary)
        {
            // This is the retained genuine transient object/input, never an invented repository row.
            if (original.OriginalConversationWrite is not null || original.OriginalUserMessageWrite is not null)
                throw new InvalidOperationException("Transient input has unexpected persisted-write custody.");
            return;
        }
        if (original.OriginalConversationWrite is not { IsCompletedSuccessfully: true }
            || original.OriginalUserMessageWrite is not { IsCompletedSuccessfully: true }
            || original.OriginalPersistedConversation is not { } acceptedConversation)
            throw new InvalidOperationException("Unknown original input writes cannot be retried or skipped.");
        var actualConversationRead = (taskCoordinator ?? throw new InvalidOperationException("The original input's canonical owner is unavailable."))
            .InvokeOriginalProcessInputSource(() => conversations.GetAsync(conversation.Id, token))
            ?? throw new InvalidOperationException("No original conversation read Task was returned.");
        original.RetainAdditionalOriginal("continuation.input-conversation", actualConversationRead);
        var current = await AwaitOriginalInputReadAsync(actualConversationRead).ConfigureAwait(false);
        if (current != acceptedConversation) throw new InvalidOperationException("The accepted conversation identity/state changed.");
        var actualHistoryRead = (taskCoordinator ?? throw new InvalidOperationException("The original input's canonical owner is unavailable."))
            .InvokeOriginalProcessInputSource(() => conversations.GetMessagesAsync(conversation.Id, token))
            ?? throw new InvalidOperationException("No original history read Task was returned.");
        original.RetainAdditionalOriginal("continuation.input-history", actualHistoryRead);
        var history = await AwaitOriginalInputReadAsync(actualHistoryRead).ConfigureAwait(false);
        if (history.Count(item => item.Id == message.Id) != 1 || history.Single(item => item.Id == message.Id) != message)
            throw new InvalidOperationException("The exact accepted user message is missing or was replaced.");
    }

    private static async Task<T> AwaitOriginalInputReadAsync<T>(Task<T> actual)
    {
        try { return await actual.ConfigureAwait(false); }
        catch (Exception) when (actual.IsFaulted && actual.Exception is { })
        {
            // Preserve ALL direct actual read siblings and faulted-OCE status before the
            // preparation boundary. This observation is not permission or read retry.
            throw actual.Exception;
        }
    }

    private async IAsyncEnumerable<ChatStreamEvent> SendOriginalAsync(
        Conversation conversation,
        string prompt,
        ModelDescriptor model,
        EffortLevel effort,
        IReadOnlyCollection<ActiveCapability> capabilities,
        string agentName,
        string agentInstructions,
        DuoMode duoMode,
        string? workspaceRoot,
        string? projectContext,
        string? projectInstructions,
        IReadOnlyList<string>? images,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken,
        IReadOnlyCollection<ActivePrompt>? prompts = null,
        string? registeredContext = null,
        GenerationOptions? generationOptions = null,
        PermissionMode filePermission = PermissionMode.FullAccess,
        PermissionMode commandPermission = PermissionMode.FullAccess,
        PermissionMode browserPermission = PermissionMode.FullAccess,
        IReadOnlyCollection<ToolCapability>? explicitCapabilities = null,
        IReadOnlyCollection<ActiveCapability>? availableCapabilities = null,
        ComputerUseRequest? computerUseRequest = null,
        ProviderExecutionContext? executionContext = null,
        TaskRunExecutionIntent taskExecutionIntent = TaskRunExecutionIntent.OrdinaryConversation,
        TaskRunInvocationCustody? originalCustody = null,
        ChatOrdinaryOriginalInvocation? ordinaryOriginal = null)
    {
        if (!Enum.IsDefined(taskExecutionIntent)) throw new ArgumentOutOfRangeException(nameof(taskExecutionIntent));
        var canonicalIntent = taskExecutionIntent == TaskRunExecutionIntent.CanonicalAgenticTask;
        if (executionContext is not null && !canonicalIntent)
            throw new InvalidOperationException("A canonical continuation requires explicit agentic-task intent.");
        if (canonicalIntent && (taskCoordinator is null || !taskCoordinator.HasAttemptAuthority || taskToolOwner is null || taskProviderContextCapture is null))
            throw new InvalidOperationException("The actual canonical task authority, typed tool owner and request context owner are unavailable.");
        DemandOriginalPersistentMemoryRequest(generationOptions, ordinaryOriginal, originalCustody);
        DemandOriginalAttachmentInput(generationOptions, ordinaryOriginal, originalCustody);
        CurrentCanonicalTask = null;
        await (ordinaryOriginal is null ? safety.EnsureMayActAsync(conversation.Id, "chat.send", cancellationToken)
                    : ordinaryOriginal.Read(() => safety.EnsureMayActAsync(conversation.Id, "chat.send", cancellationToken), cancellationToken)).ConfigureAwait(false);
        ModelDescriptor etaModel = model;

        async Task<string?> EstimateEtaAsync(
            ChatEtaRequest request,
            CancellationToken token)
        {
            var activity = request.RecentActivity.Count == 0
                ? "No completed steps yet."
                : string.Join("; ", request.RecentActivity);
            var etaPrompt =
                $"Estimate the remaining time for this task. Current stage: {request.CurrentStatus}. " +
                $"Elapsed: {Math.Max(1, (int)request.Elapsed.TotalMinutes)} minutes. " +
                $"Recent activity: {activity}. " +
                "Return exactly one clear duration such as '8 minutes', '45 minutes', or '2 hours'. " +
                "Do not return a range, explanation, uncertainty, or refusal.";

            var etaRequest = new OllamaChatRequest(etaModel.Name, [new OllamaMessage("user", etaPrompt)], effort,
                "Return one concrete remaining-time duration and nothing else.", Options: generationOptions);
            return await (ordinaryOriginal is null ? ollama.CompleteAsync(etaRequest, token)
                : ordinaryOriginal.Read(() => ollama.CompleteAsync(etaRequest, token), token, eta: true)).ConfigureAwait(false);
        }

        var execution = new ChatExecutionTracker(
            ChatExecutionStage.Preparing,
            // Canonical tasks have no issuer-enrolled estimator yet. Keep their ETA unknown
            // instead of starting an unowned provider call outside the actual attempt.
            canonicalIntent ? null : EstimateEtaAsync,
            executionContext?.ExecutionId);
        await using IAsyncDisposable? ordinaryExecution = canonicalIntent ? null
            : ordinaryOriginal is null ? execution : ordinaryOriginal.OwnTracker(execution);
        if (originalCustody is not null) originalCustody.OriginalTracker = execution;

        TaskExecutionSnapshot? canonicalTask = null;
        if (canonicalIntent)
        {
            if (executionContext is not null)
            {
                canonicalTask = await taskCoordinator!.BindOriginalContinuationAsync(
                    originalCustody ?? throw new InvalidOperationException("The actual invocation custody is unavailable."),
                    executionContext, conversation.Id, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                canonicalTask = await taskCoordinator!.BeginOriginalInvocationAsync(
                    originalCustody ?? throw new InvalidOperationException("The actual invocation custody is unavailable."),
                    conversation.Id, execution.OperationId, SummarizePrompt(prompt),
                    TaskExecutionDurability.PersistedPlan, [], cancellationToken).ConfigureAwait(false);
            }
            CurrentCanonicalTask = canonicalTask;
        }
        else if (executionContext is not null)
            throw new InvalidOperationException("The canonical task owner required by this continuation is unavailable.");

        async Task<ProviderExecutionContext?> ProviderContextAsync(Guid? actionId)
        {
            if (taskCoordinator is null || canonicalTask is null) return null;
            var current = (originalCustody?.OriginalProcessProducer is not null
                ? await new OriginalMemorySourceScope(null, originalCustody).Read(
                    () => taskCoordinator.GetAsync(canonicalTask.TaskId, cancellationToken)).ConfigureAwait(false)
                : await taskCoordinator.GetAsync(canonicalTask.TaskId, cancellationToken).ConfigureAwait(false))
                ?? throw new InvalidOperationException("The original canonical task was lost before dispatch.");
            if (current.ContextId != conversation.Id || current.ExecutionId != execution.OperationId)
                throw new InvalidOperationException("A provider request cannot change the canonical task or run.");
            canonicalTask = current;
            CurrentCanonicalTask = current;
            return new ProviderExecutionContext(current.TaskId, current.ContextId, current.ExecutionId,
                current.Attempts.LastOrDefault()?.Id, current.PersistenceRevision, actionId)
            {
                RequestedCandidate = executionContext?.RequestedCandidate,
                SelectedCandidate = current.Attempts.LastOrDefault()?.Candidate ?? executionContext?.SelectedCandidate
            };
        }

        void PublishBoundEvent(ExecutionEvent value)
        {
            var bound = canonicalTask is null ? value : value with { ExecutionId = canonicalTask.ExecutionId, TaskId = canonicalTask.TaskId };
            if (originalCustody?.OriginalProcessProducer is { } producer)
                producer.InvokeOriginalCallback(() => { executionEvents?.TryPublish(bound); });
            else if (ordinaryOriginal is not null) ordinaryOriginal.InvokeOriginal(() => { executionEvents?.TryPublish(bound); });
            else executionEvents?.TryPublish(bound);
        }

        var originalAttachmentRequest = CreateOriginalAttachmentRequest(generationOptions, conversation, prompt, ordinaryOriginal, originalCustody);
        var originalAttachmentContext = UsesOriginalAttachmentInput(generationOptions)
            ? await ReadOriginalAttachmentInputAsync(generationOptions, originalAttachmentRequest,
                await ProviderContextAsync(null).ConfigureAwait(false), ordinaryOriginal, originalCustody, cancellationToken).ConfigureAwait(false)
            : null;
        var originalWirePrompt = OriginalAttachmentUserPrompt(prompt, originalAttachmentContext);

        var promptActionId = Guid.NewGuid();
        PublishBoundEvent(new ExecutionEvent(
            Guid.NewGuid(), execution.OperationId, promptActionId, null, ExecutionOrigin.Haven,
            ExecutionActionType.UserPrompt, ExecutionActionStatus.Completed,
            SummarizePrompt(prompt), null, null, "chat", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, TabId: null,
            SafeMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["conversationId"] = conversation.Id.ToString(),
                ["mode"] = conversation.Mode.ToString()
            }));

        var publishedLogCount = 0;
        Guid parentActionId = promptActionId;
        Guid? activeActionId = null;
        ChatExecutionLogEntry? activeEntry = null;
        DateTimeOffset? activeStartedAt = null;

        void PublishExecution(ChatExecutionSnapshot snapshot)
        {
            if (canonicalIntent && snapshot.Stage == ChatExecutionStage.Completed && originalCustody is not null
                && originalCustody.OriginalCompletion is not { IsCompletedSuccessfully: true })
            {
                originalCustody.DeferredCompletionPublication = () => PublishExecution(snapshot);
                return;
            }
            try
            {
            CurrentExecution = snapshot;
            if (originalCustody?.OriginalProcessProducer is { } producer)
                producer.InvokeOriginalCallback(() => ExecutionChanged?.Invoke(snapshot));
            else if (ordinaryOriginal is not null) ordinaryOriginal.InvokeOriginal(() => ExecutionChanged?.Invoke(snapshot));
            else ExecutionChanged?.Invoke(snapshot);
            if (executionEvents is null) return;
            for (; publishedLogCount < snapshot.Log.Count; publishedLogCount++)
            {
                var entry = snapshot.Log[publishedLogCount];
                if (activeActionId is { } previousAction && activeEntry is { } previousEntry && activeStartedAt is { } previousStart)
                {
                    PublishBoundEvent(new ExecutionEvent(
                        Guid.NewGuid(), snapshot.OperationId, previousAction, parentActionId, ExecutionOrigin.Haven,
                        MapActionType(previousEntry.Stage), ExecutionActionStatus.Completed, previousEntry.Summary,
                        SafeReasoningFor(previousEntry.Stage), previousEntry.Detail, "chat", entry.Timestamp,
                        previousStart, entry.Timestamp));
                    parentActionId = previousAction;
                    activeActionId = null;
                    activeEntry = null;
                    activeStartedAt = null;
                }
                var actionId = Guid.NewGuid();
                var terminal = IsTerminal(entry.Stage);
                PublishBoundEvent(new ExecutionEvent(
                    Guid.NewGuid(), snapshot.OperationId, actionId, parentActionId, ExecutionOrigin.Haven,
                    MapActionType(entry.Stage), terminal || !entry.Succeeded ? MapActionStatus(entry) : ExecutionActionStatus.Running, entry.Summary,
                    SafeReasoningFor(entry.Stage), entry.Detail, "chat", entry.Timestamp,
                    entry.Timestamp, terminal ? entry.Timestamp : null));
                if (terminal) parentActionId = actionId;
                else
                {
                    activeActionId = actionId;
                    activeEntry = entry;
                    activeStartedAt = entry.Timestamp;
                }
            }
            }
            catch (OperationCanceledException observerFailure) when (canonicalIntent)
            {
                // This synchronous observer is not canceled provider/iterator evidence.
                throw new AggregateException("An original canonical progress observer failed.", observerFailure);
            }
        }

        execution.Changed += PublishExecution;

        var now = DateTimeOffset.UtcNow;
        var resumedInput = originalCustody?.OriginalColdContinuation is { } coldContinuation
            ? coldContinuation.Entry.Capsule.AcceptedUserMessage
            : originalCustody?.OriginalUnstartedContinuation is { } continuation
            ? continuation.Original.OriginalUserMessage
                ?? throw new InvalidOperationException("The actual accepted continuation input is unavailable.")
            : null;
        var userMessage = resumedInput ?? new ChatMessage(
            Guid.NewGuid(),
            conversation.Id,
            MessageRole.User,
            prompt,
            null,
            null,
            null,
            now);

        // Yield before model discovery, context loading, or network preflight so the
        // user's message is visible immediately.
        if (resumedInput is null)
        {
            if (originalCustody is not null) originalCustody.OriginalUserMessage = userMessage;
            yield return ChatStreamEvent.User(userMessage);
            if (originalCustody is not null) originalCustody.OriginalUserMessagePublished = true;
            if (!conversation.IsTemporary)
            {
                var acceptedConversation = conversation with { UpdatedAt = now };
                var actualConversationWrite = ordinaryOriginal is null
                    ? conversations.UpsertConversationAsync(acceptedConversation, cancellationToken)
                    : ordinaryOriginal.Write(() => conversations.UpsertConversationAsync(acceptedConversation, cancellationToken));
                if (originalCustody is not null)
                {
                    originalCustody.OriginalPersistedConversation = acceptedConversation;
                    originalCustody.OriginalConversationWrite = actualConversationWrite;
                    originalCustody.RetainAdditionalOriginal("input.conversation-write", actualConversationWrite);
                }
                try { await actualConversationWrite.ConfigureAwait(false); }
                catch (Exception failure) when (originalCustody is not null)
                { originalCustody.Retain(failure, actualConversationWrite); throw; }
                var actualMessageWrite = ordinaryOriginal is null
                    ? conversations.AddMessageAsync(userMessage, cancellationToken)
                    : ordinaryOriginal.Write(() => conversations.AddMessageAsync(userMessage, cancellationToken));
                if (originalCustody is not null)
                {
                    originalCustody.OriginalUserMessageWrite = actualMessageWrite;
                    originalCustody.RetainAdditionalOriginal("input.user-message-write", actualMessageWrite);
                }
                try { await actualMessageWrite.ConfigureAwait(false); }
                catch (Exception failure) when (originalCustody is not null)
                { originalCustody.Retain(failure, actualMessageWrite); throw; }
                RecordOriginalAttachmentAcceptance(originalAttachmentRequest, userMessage, actualConversationWrite, actualMessageWrite);
            }
        }

        // Haven owns the Generative UI registry, so a capability-status answer
        // must come from deterministic host state rather than a model's stale
        // self-description. The trusted directive is live evidence, not a mock.
        if (!canonicalIntent && GenUiChatDirectiveParser.TryCreateAvailabilityResponse(prompt, out var availabilityResponse))
        {
            var availabilityId = Guid.NewGuid();
            execution.Update(ChatExecutionStage.Generating, "Opening Generative UI");
            yield return ChatStreamEvent.AssistantStarted(availabilityId, model.Name, agentName);
            yield return ChatStreamEvent.AssistantDelta(availabilityId, availabilityResponse);
            var availabilityMessage = new ChatMessage(
                availabilityId,
                conversation.Id,
                MessageRole.Assistant,
                availabilityResponse,
                agentName,
                model.Name,
                null,
                DateTimeOffset.UtcNow);
            if (!conversation.IsTemporary)
                await (ordinaryOriginal is null ? conversations.AddMessageAsync(availabilityMessage, cancellationToken)
                    : ordinaryOriginal.Write(() => conversations.AddMessageAsync(availabilityMessage, cancellationToken))).ConfigureAwait(false);
            execution.Complete();
            execution.Changed -= PublishExecution;
            yield return ChatStreamEvent.AssistantCompleted(availabilityMessage);
            yield break;
        }

        execution.Update(ChatExecutionStage.LoadingModel, "Loading Model");
        await (ordinaryOriginal is null ? safety.EnsureMayActAsync(conversation.Id, "chat.model-discovery", cancellationToken)
                    : ordinaryOriginal.Read(() => safety.EnsureMayActAsync(conversation.Id, "chat.model-discovery", cancellationToken), cancellationToken)).ConfigureAwait(false);
        // A constrained original request uses its SAME selected descriptor here. Global
        // inventory and compatibility substitution would run outside its restrictions.
        // The existing restricted provider router owns actual catalogue/dispatch/fallback
        // admission; this descriptor is not an availability or authorization grant.
        var constrainedSelection = generationOptions?.RequestedRoutingConstraints is not null;
        IReadOnlyList<ModelDescriptor> installed = constrainedSelection
            ? [model]
            : await (ordinaryOriginal is null ? _modelInventory.GetAsync(forceRefresh: false, cancellationToken: cancellationToken)
                : ordinaryOriginal.Read(() => _modelInventory.GetAsync(forceRefresh: false, cancellationToken: cancellationToken), cancellationToken)).ConfigureAwait(false);

        execution.Update(
            ChatExecutionStage.SelectingCapabilities,
            "Selecting Capabilities");

        var selectedCapabilities = explicitCapabilities is { Count: > 0 }
            ? explicitCapabilities.ToHashSet()
            : ToolCapabilitiesFromRegisteredCapabilities(capabilities);
        var capabilitySelection = ChatCapabilitySelection.Create(
            prompt,
            selectedCapabilities);
        var requiredCapabilities = capabilitySelection.Required.ToHashSet();
        if (computerUseRequest?.HasExplicitEligibleTarget != true)
            requiredCapabilities.Remove(ToolCapability.ComputerUse);

        if (images is { Count: > 0 })
        {
            requiredCapabilities.Add(ToolCapability.Vision);
        }

        var needsTools = NeedsToolRuntime(requiredCapabilities);
        if (needsTools)
        {
            requiredCapabilities.Add(ToolCapability.Tools);
            requiredCapabilities.Remove(ToolCapability.Streaming);
        }

        var turnModel = constrainedSelection
            ? model
            : ChatModelFallbackSelector.Select(model, installed, requiredCapabilities) ?? model;
        etaModel = turnModel;
        ProviderModelDescriptor? effectivePermissionModel = null;

        string? personalityDirective = null;
        var memoryReferences = PersonalityLevel.Moderate;
        if (personalities is not null)
        {
            var effectivePersonality = await (ordinaryOriginal is null ? personalities.ResolveEffectiveAsync(turnModel.Name, cancellationToken)
                : ordinaryOriginal.Read(() => personalities.ResolveEffectiveAsync(turnModel.Name, cancellationToken), cancellationToken)).ConfigureAwait(false);
            personalityDirective = ModelPersonalityPrompt.Describe(effectivePersonality);
            memoryReferences = effectivePersonality.MemoryReferences;
        }

        IReadOnlyList<KnowledgeRecord> selectedPersistentMemory = [];
        if (generationOptions?.RequestedContextConstraints?.AllowPersistentMemoryRead != false &&
            (UsesOriginalPersistentMemory(generationOptions) || memorySource is not null))
        {
            var remembered = UsesOriginalPersistentMemory(generationOptions)
                ? await ReadOriginalPersistentMemoryAsync(generationOptions!, conversation,
                    await ProviderContextAsync(null).ConfigureAwait(false), ordinaryOriginal, originalCustody, cancellationToken).ConfigureAwait(false)
                : await (ordinaryOriginal is null ? memorySource!.GetActiveLearnMeAsync(MemoryInjection.MaximumRecords, cancellationToken)
                    : ordinaryOriginal.Read(() => memorySource!.GetActiveLearnMeAsync(MemoryInjection.MaximumRecords, cancellationToken), cancellationToken)).ConfigureAwait(false);
            var selectedMemories = MemoryInjection.SelectForLevel(memoryReferences, remembered);
            if (MemoryInjection.ShouldInclude(memoryReferences, selectedMemories.Count))
            {
                var memoryDirective = MemoryInjection.BuildDirective(selectedMemories);
                if (!string.IsNullOrWhiteSpace(memoryDirective))
                {
                    personalityDirective = string.IsNullOrWhiteSpace(personalityDirective)
                        ? memoryDirective
                        : personalityDirective + "\n\n" + memoryDirective;
                    selectedPersistentMemory = Array.AsReadOnly(selectedMemories.ToArray());
                }
            }
        }

        string? providerDefaultsDirective = null;
        if (defaultProviders is not null)
        {
            var assignments = await (ordinaryOriginal is null ? defaultProviders.GetAllAsync(cancellationToken)
                : ordinaryOriginal.Read(() => defaultProviders.GetAllAsync(cancellationToken), cancellationToken)).ConfigureAwait(false);
            providerDefaultsDirective = DefaultProviderDirectives.Describe(assignments);
        }

        // Project agent-instruction files are discovered by the runtime — never left to model memory.
        string? discoveredAgentInstructions = null;
        if (projectInstructionFiles is not null && !string.IsNullOrWhiteSpace(workspaceRoot))
        {
            discoveredAgentInstructions = await ProjectAgentInstructions.LoadAsync(
                projectInstructionFiles, workspaceRoot, null, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(discoveredAgentInstructions))
            {
                var instructionsActionId = Guid.NewGuid();
                PublishBoundEvent(new ExecutionEvent(
                    Guid.NewGuid(), execution.OperationId, instructionsActionId, promptActionId, ExecutionOrigin.Haven,
                    ExecutionActionType.InstructionsLoaded, ExecutionActionStatus.Completed,
                    "Project agent instructions loaded", null,
                    "Discovered agent.md/AGENTS.md rules are applied as execution constraints.", "agent-instructions",
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            }
        }
        var effectiveProjectInstructions = string.Join("\n\n",
            new[] { projectInstructions, discoveredAgentInstructions }.Where(item => !string.IsNullOrWhiteSpace(item)));

        var computerPassCandidate = ordinaryOriginal is null ? computerTools.CreatePass(computerUseRequest)
            : ordinaryOriginal.CreateResource(() => computerTools.CreatePass(computerUseRequest));
        using IDisposable? ordinaryComputerPass = canonicalIntent ? null
            : ordinaryOriginal is null ? computerPassCandidate : ordinaryOriginal.OwnResource(computerPassCandidate);
        if (originalCustody is not null) originalCustody.OriginalResources.Add(computerPassCandidate);
        var selectedRegisteredCapabilities = FilterCapabilitiesForTurn(
                availableCapabilities ?? capabilities,
                requiredCapabilities)
            .Concat(capabilities)
            .DistinctBy(capability => capability.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var mcpDefinitions = mcpTools is null
            ? []
            : await (ordinaryOriginal is null ? mcpTools.GetDefinitionsAsync(selectedRegisteredCapabilities, cancellationToken)
                : ordinaryOriginal.Read(() => mcpTools.GetDefinitionsAsync(selectedRegisteredCapabilities, cancellationToken), cancellationToken)).ConfigureAwait(false);
        Action<Action>? originalCloudflareCallerCallback = null;
        if (canonicalIntent && taskToolOwner is CanonicalWorkspaceCloudflareToolActionOwner originalCloudflareCatalogueOwner)
        {
            // Observe only the SAME actual selected capabilities. Definitions grant no
            // execution; Prepare/Register/Execute still require private owning originals.
            var originalCloudflareProducer = originalCustody?.OriginalProcessProducer
                ?? throw new InvalidOperationException("The original canonical Chat producer is unavailable before Cloudflare catalogue acquisition.");
            originalCloudflareCallerCallback = originalCloudflareProducer.InvokeOriginalCallback;
            Task<IReadOnlyList<OllamaToolDefinition>>? originalCloudflareDefinitions = null;
            try
            {
                originalCloudflareCallerCallback(() =>
                {
                    originalCloudflareDefinitions = originalCloudflareCatalogueOwner.GetCloudflareDefinitionsAsync(selectedRegisteredCapabilities, originalCloudflareCallerCallback, cancellationToken);
                    originalCustody!.RetainAdditionalOriginal("tools.cloudflare-definitions", originalCloudflareDefinitions);
                });
                var actualDefinitions = originalCloudflareDefinitions
                    ?? throw new InvalidOperationException("The original Cloudflare catalogue callback returned no actual Task.");
                var declared = await actualDefinitions.ConfigureAwait(false);
                if (declared.Any(candidate => mcpDefinitions.Any(existing => existing.Name.Equals(candidate.Name, StringComparison.Ordinal))))
                    throw new InvalidOperationException("A fixed canonical Cloudflare definition collides with an existing MCP definition.");
                mcpDefinitions = Array.AsReadOnly(mcpDefinitions.Concat(declared).ToArray());
            }
            catch (Exception cause)
            {
                if (originalCloudflareDefinitions is not null)
                    originalCustody!.RetainAdditionalOriginal("tools.cloudflare-definitions", originalCloudflareDefinitions);
                originalCustody!.Retain(cause, originalCloudflareDefinitions);
                if (originalCloudflareDefinitions?.IsFaulted == true)
                    throw new AggregateException(originalCloudflareDefinitions.Exception!.InnerExceptions);
                if (originalCloudflareDefinitions is null && cause is OperationCanceledException)
                    throw new AggregateException("Synchronous canonical Cloudflare definition callback fault.", cause);
                throw;
            }
        }
        var pluginBindings = ordinaryOriginal is null ? pluginTools?.GetBindings(selectedRegisteredCapabilities) ?? []
            : ordinaryOriginal.InvokeOriginal(() => pluginTools?.GetBindings(selectedRegisteredCapabilities) ?? []);
        var calendarDefinitions = calendarTools is null
            ? []
            : await (ordinaryOriginal is null ? calendarTools.GetDefinitionsAsync(selectedRegisteredCapabilities, cancellationToken)
                : ordinaryOriginal.Read(() => calendarTools.GetDefinitionsAsync(selectedRegisteredCapabilities, cancellationToken), cancellationToken)).ConfigureAwait(false);
        var availabilityPlan = CreateAvailabilityPlan(
            conversation.Mode,
            workspaceRoot,
            selectedRegisteredCapabilities,
            filePermission,
            commandPermission,
            browserPermission,
            computerPassCandidate.Definitions,
            mcpDefinitions,
            calendarDefinitions,
            pluginBindings);

        var modelPlan = availabilityPlan.RestrictToModel(turnModel);
        var modelCapabilities = FilterCapabilitiesForTurn(
            modelPlan.FilterCapabilities(selectedRegisteredCapabilities),
            requiredCapabilities);

        var check = preflight.Evaluate(
            turnModel,
            modelCapabilities,
            images is { Count: > 0 },
            installed);

        if (!check.IsCompatible)
        {
            execution.Fail("Capability check failed", string.Join("; ", check.Missing.Select(item => item.Reason)));
            execution.Changed -= PublishExecution;
            yield return ChatStreamEvent.Preflight(check);
            yield break;
        }

        var toolDefinitions = RestrictOriginalRequestToolDefinitions(needsTools
            ? modelPlan.Definitions
                .Where(definition => IsToolSelectedForTurn(
                    modelPlan,
                    definition.Name,
                    requiredCapabilities))
                .Where(definition => !canonicalIntent || modelPlan.TryGetRuntime(definition.Name, out var runtime)
                    && taskToolOwner!.SupportsCanonicalInvocation(runtime, definition.Name))
                .ToArray()
            : [], generationOptions);

        var computerPass = toolDefinitions.Any(definition =>
                modelPlan.TryGetRuntime(definition.Name, out var runtime) &&
                runtime == ToolRuntimeKind.Computer)
            ? computerPassCandidate
            : null;
        var canUseTools = toolDefinitions.Length > 0;
        if (ordinaryOriginal is not null && canUseTools)
            throw new InvalidOperationException("This plain ordinary source does not issue canonical tool authority.");

        execution.Update(ChatExecutionStage.LoadingContext, "Loading Context");
        var history = await (ordinaryOriginal is null ? conversations.GetContextMessagesAsync(conversation.Id, cancellationToken)
            : ordinaryOriginal.Read(() => conversations.GetContextMessagesAsync(conversation.Id, cancellationToken), cancellationToken)).ConfigureAwait(false);

        var contextBudget = (int)Math.Clamp(
            (long)(generationOptions?.ContextLimit ?? 32768) * 4L,
            8_000L,
            131_072L);
        var contextMessages = ChatContextWindow.Build(history, contextBudget);
        var requestMessages = contextMessages
            .Where(message => message.Role is MessageRole.User or MessageRole.Assistant)
            .Select(message => new OllamaMessage(
                message.Role == MessageRole.User ? "user" : "assistant",
                message.Content))
            .ToList();

        if (requestMessages.Count == 0 ||
            requestMessages[^1].Role != "user" ||
            requestMessages[^1].Content != prompt)
        {
            requestMessages.Add(new OllamaMessage("user", originalWirePrompt, images));
        }
        else if (images is { Count: > 0 } || originalAttachmentContext is not null)
        {
            requestMessages[^1] = new OllamaMessage("user", originalWirePrompt, images);
        }

        var system = BuildSystemPrompt(
            conversation, modelCapabilities, prompts ?? [], agentName, agentInstructions, duoMode,
            modelPlan.HasRuntime(ToolRuntimeKind.Workspace) ? workspaceRoot : null,
            projectContext, effectiveProjectInstructions, registeredContext, computerPass is not null, personalityDirective,
            providerDefaultsDirective);
        var contributingHistoryIds = contextMessages.Select(message => message.Id).ToHashSet();
        var originalSelectedHistory = Array.AsReadOnly(history.Where(message => contributingHistoryIds.Contains(message.Id)).ToArray());

        async ValueTask<TaskRunContextInventory> CaptureInventoryAsync(CancellationToken token)
        {
            // Persisted inputs are read after their actual Upsert; the incoming UpdatedAt may be old.
            // The context owner holds original transient requests privately, without inventing a row.
            var originalConversation = conversation.IsTemporary ? conversation
                : await conversations.GetAsync(conversation.Id, token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The actual persisted task conversation is unavailable.");
            if (originalConversation.Id != conversation.Id)
                throw new InvalidOperationException("The context source returned another conversation.");
            return new TaskRunContextInventory(originalConversation, originalSelectedHistory, selectedPersistentMemory,
                [], projectContext, effectiveProjectInstructions, registeredContext, images, workspaceRoot)
            { OriginalAttachmentLineage = generationOptions?.RequestedOriginalAttachmentLineage,
              OriginalAttachmentInvocation = CaptureOriginalAttachmentInvocation(originalAttachmentRequest) };
        }
        async ValueTask CaptureOriginalChatAsync(OllamaChatRequest request, TaskRunContextInventory? inventory, CancellationToken token)
        {
            await ValidateOriginalPersistentMemoryAsync(generationOptions, conversation, request.ExecutionContext,
                ordinaryOriginal, originalCustody, token).ConfigureAwait(false);
            await ValidateOriginalAttachmentInputAsync(generationOptions, originalAttachmentRequest, request.ExecutionContext,
                ordinaryOriginal, originalCustody, token).ConfigureAwait(false);
            if (!canonicalIntent) return;
            var context = request.ExecutionContext ?? throw new InvalidOperationException("The original canonical request has no task observation.");
            var current = canonicalTask ?? throw new InvalidOperationException("The canonical task disappeared before context capture.");
            if (current.TaskId != context.TaskId || current.ExecutionId != context.ExecutionId || current.PersistenceRevision != context.PersistenceRevision)
                throw new InvalidOperationException("The original request does not bind the producer's actual task observation.");
            Task? originalCapture = null;
            try
            {
                originalCapture = taskProviderContextCapture!.CaptureOriginalAsync(current, request,
                    inventory ?? throw new InvalidOperationException("The actual context selection is unavailable."), token).AsTask();
                await originalCapture.ConfigureAwait(false);
                await ValidateOriginalPersistentMemoryAsync(generationOptions, conversation, request.ExecutionContext,
                    ordinaryOriginal, originalCustody, token).ConfigureAwait(false);
                await ValidateOriginalAttachmentInputAsync(generationOptions, originalAttachmentRequest, request.ExecutionContext,
                    ordinaryOriginal, originalCustody, token).ConfigureAwait(false);
            }
            catch (Exception captureFailure)
            {
                originalCustody!.Retain(captureFailure, originalCapture);
                throw;
            }
        }
        async ValueTask CaptureOriginalToolsAsync(OllamaToolRequest request, TaskRunContextInventory? inventory, CancellationToken token)
        {
            await ValidateOriginalPersistentMemoryAsync(generationOptions, conversation, request.ExecutionContext,
                ordinaryOriginal, originalCustody, token).ConfigureAwait(false);
            await ValidateOriginalAttachmentInputAsync(generationOptions, originalAttachmentRequest, request.ExecutionContext,
                ordinaryOriginal, originalCustody, token).ConfigureAwait(false);
            if (!canonicalIntent) return;
            var context = request.ExecutionContext ?? throw new InvalidOperationException("The original canonical request has no task observation.");
            var current = canonicalTask ?? throw new InvalidOperationException("The canonical task disappeared before context capture.");
            if (current.TaskId != context.TaskId || current.ExecutionId != context.ExecutionId || current.PersistenceRevision != context.PersistenceRevision)
                throw new InvalidOperationException("The original request does not bind the producer's actual task observation.");
            Task? originalCapture = null;
            try
            {
                originalCapture = taskProviderContextCapture!.CaptureOriginalAsync(current, request,
                    inventory ?? throw new InvalidOperationException("The actual context selection is unavailable."), token).AsTask();
                await originalCapture.ConfigureAwait(false);
                await ValidateOriginalPersistentMemoryAsync(generationOptions, conversation, request.ExecutionContext,
                    ordinaryOriginal, originalCustody, token).ConfigureAwait(false);
                await ValidateOriginalAttachmentInputAsync(generationOptions, originalAttachmentRequest, request.ExecutionContext,
                    ordinaryOriginal, originalCustody, token).ConfigureAwait(false);
            }
            catch (Exception captureFailure)
            {
                originalCustody!.Retain(captureFailure, originalCapture);
                throw;
            }
        }

        async Task<RemediationRequest> PublishOriginalCloudPermissionAsync(TaskRunCloudPermissionRequiredException actualAsk)
        {
            if (!canonicalIntent || taskCloudPermissionRemediation is null || originalCustody is null)
                throw actualAsk;
            originalCustody.Retain(actualAsk);
            Task<RemediationRequest>? actualPublication = null;
            try
            {
                actualPublication = taskCloudPermissionRemediation.RequestOriginalAsync(actualAsk, cancellationToken);
                originalCustody.RetainAdditionalOriginal("permission.request", actualPublication);
                var metadata = await actualPublication.ConfigureAwait(false);
                originalCustody.BindPublishedOriginalPermission(actualAsk, actualPublication, taskCloudPermissionRemediation);
                return metadata; // Request metadata is never a grant, effect or resume receipt.
            }
            catch (Exception publicationFailure)
            {
                originalCustody.Retain(publicationFailure, actualPublication);
                throw;
            }
        }

        var assistantId = Guid.NewGuid();
        var buffer = new StringBuilder();
        var toolActivities = new List<ToolActivity>();
        execution.Update(ChatExecutionStage.Thinking, "Thinking");
        yield return ChatStreamEvent.AssistantStarted(assistantId, turnModel.Name, agentName);

        if (canUseTools)
        {
            var turns = requestMessages.Select(message => new OllamaToolTurn(message.Role, message.Content, Images: message.Images)).ToList();
            var toolCallLimit = Math.Clamp(generationOptions?.ActionLimit ?? 24, 1, 100);
            var callsUsed = 0;
            var bridgeAttempted = false;
            WorkspaceToolResult? lastToolResult = null;
            OllamaToolCall? lastToolCall = null;

            async Task<WorkspaceToolResult> ExecuteRuntimeAsync(
                OllamaToolCall call,
                ToolRuntimeKind runtime,
                PermissionMode permission,
                CancellationToken token,
                ITaskRunToolActionPreparation? originalPreparation = null)
            {
                if (runtime == ToolRuntimeKind.Computer && computerPass is not null)
                    return await computerPass.ExecuteAsync(call, token).ConfigureAwait(false);
                if (runtime == ToolRuntimeKind.Browser && browserTools is not null)
                    return await browserTools.ExecuteAsync(call, token).ConfigureAwait(false);
                if (runtime == ToolRuntimeKind.Automation && automationTools is not null)
                    return await automationTools.ExecuteAsync(call, conversation.Mode, conversation.Id, conversation.ContainerId, token).ConfigureAwait(false);
                if (canonicalIntent && runtime == ToolRuntimeKind.Mcp && originalPreparation is not null
                    && taskToolOwner is CanonicalWorkspaceCloudflareToolActionOwner originalCloudflareRuntimeOwner)
                {
                    Task<WorkspaceToolResult>? originalCloudflareRuntime = null;
                    try
                    {
                        var originalRuntimeCallback = originalCloudflareCallerCallback
                            ?? throw new InvalidOperationException("The original canonical Cloudflare caller scope is unavailable before runtime dispatch.");
                        originalRuntimeCallback(() =>
                        {
                            originalCloudflareRuntime = originalCloudflareRuntimeOwner.ExecuteOriginalCloudflareRuntimeAsync(call, originalPreparation, originalRuntimeCallback, token);
                            originalCustody!.RetainAdditionalOriginal("tools.cloudflare-runtime", originalCloudflareRuntime);
                        });
                        return await (originalCloudflareRuntime
                            ?? throw new InvalidOperationException("The original Cloudflare runtime callback returned no actual Task.")).ConfigureAwait(false);
                    }
                    catch (Exception cause)
                    {
                        if (originalCloudflareRuntime is not null)
                            originalCustody!.RetainAdditionalOriginal("tools.cloudflare-runtime", originalCloudflareRuntime);
                        originalCustody!.Retain(cause, originalCloudflareRuntime);
                        if (originalCloudflareRuntime?.IsFaulted == true)
                            throw new AggregateException(originalCloudflareRuntime.Exception!.InnerExceptions);
                        if (originalCloudflareRuntime is null && cause is OperationCanceledException)
                            throw new AggregateException("Synchronous canonical Cloudflare runtime callback fault.", cause);
                        throw;
                    }
                }
                if (runtime == ToolRuntimeKind.Mcp && mcpTools is not null)
                    return await mcpTools.ExecuteAsync(call, selectedRegisteredCapabilities, permission, token).ConfigureAwait(false);
                if (runtime == ToolRuntimeKind.Plugin && pluginTools is not null)
                {
                    var binding = pluginBindings.FirstOrDefault(item => item.Definition.Name.Equals(call.Name, StringComparison.Ordinal));
                    if (binding is null)
                        return new WorkspaceToolResult(
                            new ToolActivity(Guid.NewGuid(), call.Name.Replace('_', ' '), "Plugin binding was not found for this pass.", false, TimeSpan.Zero, DateTimeOffset.UtcNow),
                            "Tool error: plugin binding was not found for this pass.");
                    return await pluginTools.ExecuteAsync(binding, call, execution.OperationId, activeActionId ?? parentActionId, token).ConfigureAwait(false);
                }
                if (runtime == ToolRuntimeKind.Calendar && calendarTools is not null)
                    return await calendarTools.ExecuteAsync(call, selectedRegisteredCapabilities, permission, token).ConfigureAwait(false);
                if (runtime == ToolRuntimeKind.Workspace && workspaceRoot is not null)
                {
                    // A checkpoint is recorded before the first applicable mutation of this execution.
                    if (checkpoints is not null &&
                        ModelToolPermissionMap.Map(call.Name) == RestrictedModelCapability.EditFiles)
                    {
                        var checkpoint = await checkpoints.EnsureBeforeMutationAsync(
                            execution.OperationId, conversation.Id, conversation.ContainerId,
                            workspaceRoot, checkpoints.Mode, cancellationToken).ConfigureAwait(false);
                        if (checkpoint is not null && taskCoordinator is not null && canonicalTask is not null)
                            canonicalTask = await taskCoordinator.RecordCheckpointAsync(canonicalTask.TaskId, canonicalTask.ExecutionId,
                                checkpoint.Id, cancellationToken).ConfigureAwait(false);
                    }
                    return originalPreparation is null
                        ? await workspaceTools.ExecuteAsync(workspaceRoot, call, token, conversation.Id, conversation.ContainerId).ConfigureAwait(false)
                        : await workspaceTools.ExecuteOriginalAsync(workspaceRoot, call, originalPreparation, token,
                            conversation.Id, conversation.ContainerId).ConfigureAwait(false);
                }
                return new WorkspaceToolResult(
                    new ToolActivity(Guid.NewGuid(), call.Name.Replace('_', ' '), "Registered runtime is unavailable.", false, TimeSpan.Zero, DateTimeOffset.UtcNow),
                    "Tool error: registered runtime is unavailable.");
            }

            async Task<WorkspaceToolResult> ExecuteToolAsync(OllamaToolCall call)
            {
                // A constrained request offers only its fresh maintained intersection.
                // A provider-returned name cannot bypass that selection through the
                // broader runtime plan, including bridges/bootstrap/recovery paths.
                if (generationOptions?.RequestedToolSelectionConstraints is { } requestedTools &&
                    (!requestedTools.Allows(call.Name) || !toolDefinitions.Any(definition =>
                        definition.Name.Equals(call.Name, StringComparison.Ordinal))))
                {
                    var deniedAt = DateTimeOffset.UtcNow;
                    const string detail = "The original request did not select this currently offered tool.";
                    return new WorkspaceToolResult(new ToolActivity(Guid.NewGuid(), call.Name.Replace('_', ' '),
                        detail, false, TimeSpan.Zero, deniedAt)
                        { InvocationEvidence = Array.AsReadOnly(new[] { new ToolInvocationEvidence(Guid.NewGuid(),
                            call.Name, null, ToolInvocationObservationStatus.DeniedBeforeDispatch, null, deniedAt, deniedAt,
                            ReportedFailureCode: "REQUEST_TOOL_NOT_SELECTED") }) }, "Tool error: " + detail);
                }
                await (ordinaryOriginal is null ? safety.EnsureMayActAsync(conversation.Id, $"chat.tool.{call.Name}", cancellationToken)
                    : ordinaryOriginal.Read(() => safety.EnsureMayActAsync(conversation.Id, $"chat.tool.{call.Name}", cancellationToken), cancellationToken)).ConfigureAwait(false);
                var toolActionId = Guid.NewGuid();
                var toolStartedAt = DateTimeOffset.UtcNow;
                var actionParentId = activeActionId ?? parentActionId;
                var invocationEvidence = new System.Collections.Concurrent.ConcurrentQueue<ToolInvocationEvidence>();

                async Task<WorkspaceToolResult> ObserveRuntimeAsync(ToolRuntimeKind selectedRuntime, PermissionMode permission,
                    CancellationToken token, Guid invocationId, Guid? retryOf = null)
                {
                    var available = selectedRuntime switch
                    {
                        ToolRuntimeKind.Computer => computerPass is not null,
                        ToolRuntimeKind.Browser => browserTools is not null,
                        ToolRuntimeKind.Automation => automationTools is not null,
                        ToolRuntimeKind.Mcp => mcpTools is not null,
                        ToolRuntimeKind.Plugin => pluginTools is not null && pluginBindings.Any(item => item.Definition.Name.Equals(call.Name, StringComparison.Ordinal)),
                        ToolRuntimeKind.Calendar => calendarTools is not null,
                        ToolRuntimeKind.Workspace => workspaceRoot is not null,
                        _ => false
                    };
                    if (canonicalIntent && !taskToolOwner!.SupportsCanonicalInvocation(selectedRuntime, call.Name))
                        throw new InvalidOperationException("The selected tool has no actual typed canonical owner before dispatch.");
                    var startedAt = DateTimeOffset.UtcNow;
                    WorkspaceToolResult observed;
                    var currentProviderContext = await ProviderContextAsync(invocationId).ConfigureAwait(false);
                    if (available && canonicalTask?.OwnerBinding is not null)
                    {
                        var owner = taskToolOwner ?? throw new InvalidOperationException("The actual canonical tool-action owner is unavailable before dispatch.");
                        var coordinator = taskCoordinator ?? throw new InvalidOperationException("The canonical task owner is unavailable before dispatch.");
                        if (currentProviderContext?.AttemptId is not { } currentAttemptId)
                            throw new InvalidOperationException("A tool cannot dispatch without the actual admitted provider attempt.");
                        var originalAttempt = await coordinator.GetIssuedAttemptAsync(canonicalTask.TaskId,
                            canonicalTask.ExecutionId, currentAttemptId, token).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("The original issuer-owned attempt is unavailable before tool dispatch.");
                        var toolPermissionIntent = ModelToolPermissionMap.Map(call.Name) switch
                        {
                            RestrictedModelCapability.EditFiles => filePermission,
                            RestrictedModelCapability.RunCommands => commandPermission,
                            _ => permission
                        };
                        var preparation = owner is CanonicalWorkspaceCloudflareToolActionOwner originalScopedCloudflareOwner
                            ? await originalScopedCloudflareOwner.PrepareCallerScopedOriginalAsync(originalAttempt, canonicalTask,
                                invocationId, call, selectedRuntime, toolPermissionIntent, workspaceRoot, originalCloudflareCallerCallback
                                    ?? throw new InvalidOperationException("The original canonical Cloudflare preparation scope is unavailable."), token).ConfigureAwait(false)
                            : await owner.PrepareOriginalAsync(originalAttempt, canonicalTask,
                                invocationId, call, selectedRuntime, toolPermissionIntent, workspaceRoot, token).ConfigureAwait(false);
                        if (!ReferenceEquals(preparation.OriginalAttempt, originalAttempt) || preparation.ActionId != invocationId)
                            throw new InvalidOperationException("The tool owner did not retain the exact original attempt and action.");
                        canonicalTask = await coordinator.RegisterOriginalToolActionAsync(preparation,
                            retryOf ?? actionParentId, StatusForTool(call.Name), token).ConfigureAwait(false);
                        var ownedResult = await owner.ExecuteOriginalAsync(preparation,
                            originalToken => ExecuteRuntimeAsync(call, selectedRuntime, toolPermissionIntent,
                                originalToken, preparation), token).ConfigureAwait(false);
                        await owner.ValidateOriginalResultAsync(preparation, ownedResult, token).ConfigureAwait(false);
                        observed = ownedResult.OriginalResult;
                        canonicalTask = await coordinator.RecordOriginalToolActionOutcomeAsync(
                            preparation, ownedResult, token).ConfigureAwait(false);
                        CurrentCanonicalTask = canonicalTask;
                        await coordinator.RetireAcknowledgedToolOriginalAsync(preparation, canonicalTask).ConfigureAwait(false);
                        originalCustody!.RetainOriginalToolOutcome(coordinator.CaptureOriginalToolOutcomeCustody(
                            preparation, ownedResult, canonicalTask));
                    }
                    else
                    {
                        observed = await ExecuteRuntimeAsync(call, selectedRuntime, permission, token).ConfigureAwait(false);
                    }
                    invocationEvidence.Enqueue(new(invocationId, call.Name, selectedRuntime.ToString(),
                        available ? ToolInvocationObservationStatus.RuntimeReturned : ToolInvocationObservationStatus.UnavailableBeforeDispatch,
                        available ? observed.Activity.Succeeded : null, startedAt, DateTimeOffset.UtcNow, retryOf, observed.Failure?.Code));
                    return observed;
                }

                if (modelPermissions is not null && ModelToolPermissionMap.Map(call.Name) is { } restrictedCapability)
                {
                    var permissionDecision = await modelPermissions.EvaluateAsync(
                        effectivePermissionModel ?? DescriptorForPermission(turnModel), restrictedCapability, acrossMesh: false, cancellationToken).ConfigureAwait(false);
                    if (!permissionDecision.Allowed)
                    {
                        PublishBoundEvent(new ExecutionEvent(
                            Guid.NewGuid(), execution.OperationId, toolActionId, actionParentId, ExecutionOrigin.Haven,
                            ExecutionActionType.PermissionDenied, ExecutionActionStatus.Blocked,
                            $"Model {turnModel.Name} is not permitted to run this capability", null,
                            permissionDecision.Reason, call.Name, toolStartedAt, toolStartedAt, toolStartedAt));
                        return new WorkspaceToolResult(
                            new ToolActivity(Guid.NewGuid(), call.Name.Replace('_', ' '),
                                $"Model {turnModel.Name} is restricted from this action by model permissions.", false, TimeSpan.Zero, DateTimeOffset.UtcNow)
                            { InvocationEvidence = Array.AsReadOnly(new[] { new ToolInvocationEvidence(toolActionId, call.Name, null,
                                ToolInvocationObservationStatus.DeniedBeforeDispatch, null, toolStartedAt, DateTimeOffset.UtcNow,
                                ReportedFailureCode: "MODEL_PERMISSION_DENIED") }) },
                            "Tool error: the selected model's permission policy denies this capability. Switch models in the model picker or adjust model permissions in Settings.",
                            new ToolFailureDescriptor(
                                "MODEL_PERMISSION_DENIED", ToolFailureKind.PermissionRequired,
                                $"Model '{turnModel.Name}' is denied this capability by the model permission policy.",
                                "model-permissions", "Model permissions",
                                new RecoveryRiskAssessment(false, false, false, false, true, false, false, 1.0),
                                Retryable: false));
                    }
                }

                PublishBoundEvent(new ExecutionEvent(
                    Guid.NewGuid(), execution.OperationId, toolActionId, actionParentId, ExecutionOrigin.Haven,
                    ExecutionActionType.ToolCall, ExecutionActionStatus.Running, StatusForTool(call.Name),
                    "The registered tool matched the requested operation and current permissions.", DescribeTool(call),
                    call.Name, toolStartedAt, toolStartedAt));

                WorkspaceToolResult originalResult;
                ToolRuntimeKind? runtimeKind = null;
                if (modelPlan.TryGetRuntime(call.Name, out var runtime))
                {
                    runtimeKind = runtime;
                    execution.Update(StageForTool(call.Name, runtime), StatusForTool(call.Name), DescribeTool(call));
                    originalResult = await ObserveRuntimeAsync(runtime, commandPermission, cancellationToken, toolActionId).ConfigureAwait(false);
                }
                else
                {
                    var detail = modelPlan.GetUnavailableReason(call.Name);
                    invocationEvidence.Enqueue(new(toolActionId, call.Name, null, ToolInvocationObservationStatus.UnavailableBeforeDispatch,
                        null, toolStartedAt, DateTimeOffset.UtcNow));
                    originalResult = new WorkspaceToolResult(
                        new ToolActivity(Guid.NewGuid(), call.Name.Replace('_', ' '), detail, false, TimeSpan.Zero, DateTimeOffset.UtcNow),
                        "Tool error: " + detail);
                }

                var firstEndedAt = DateTimeOffset.UtcNow;
                var result = originalResult;
                var descriptor = originalResult.Failure;
                RecoveryAttempt? plannedRecovery = null;
                RemediationRequest? remediationRequest = null;
                Func<RemediationResolution, CancellationToken, Task<RemediationContinuationResult>>? continuation = null;
                Guid? remediationId = null;
                WorkspaceToolResult? automaticRetryResult = null;
                Guid? automaticRetryActionId = null;
                DateTimeOffset? automaticRetryStartedAt = null;
                DateTimeOffset? automaticRetryEndedAt = null;

                if (!originalResult.Activity.Succeeded && descriptor is not null && recovery is not null &&
                    (descriptor.Retryable || descriptor.SuggestedRemediation is not null))
                {
                    plannedRecovery = recovery.Plan(
                        execution.OperationId, toolActionId, $"{descriptor.Code}:{descriptor.ComponentId}",
                        descriptor.Risk, descriptor.SafeMessage);

                    if (descriptor.Retryable && runtimeKind is { } retryRuntime && plannedRecovery.Stage == RecoveryStage.SafeAutomaticRetry)
                    {
                        automaticRetryActionId = Guid.NewGuid();
                        automaticRetryStartedAt = DateTimeOffset.UtcNow;
                        await (ordinaryOriginal is null ? safety.EnsureMayActAsync(conversation.Id, $"chat.tool.retry.{call.Name}", cancellationToken)
                    : ordinaryOriginal.Read(() => safety.EnsureMayActAsync(conversation.Id, $"chat.tool.retry.{call.Name}", cancellationToken), cancellationToken)).ConfigureAwait(false);
                        automaticRetryResult = await ObserveRuntimeAsync(retryRuntime, commandPermission, cancellationToken, automaticRetryActionId.Value, toolActionId).ConfigureAwait(false);
                        automaticRetryEndedAt = DateTimeOffset.UtcNow;
                        result = automaticRetryResult;
                    }
                    else if (descriptor.SuggestedRemediation is { } remediationType && remediations is not null)
                    {
                        remediationId = Guid.NewGuid();
                        var canResume = descriptor.Retryable && runtimeKind is ToolRuntimeKind.Mcp or ToolRuntimeKind.Calendar;
                        var now = DateTimeOffset.UtcNow;
                        var requiredInputs = descriptor.RequiredInputs ?? [];
                        var sensitivity = requiredInputs.Any(input => input.Sensitivity == RemediationSensitivity.Secret)
                            ? RemediationSensitivity.Secret
                            : RemediationSensitivity.Normal;
                        IReadOnlyList<string> allowedActions = remediationType switch
                        {
                            RemediationType.SecretInput => ["Save Securely & Retry", "Cancel"],
                            RemediationType.PermissionRequest => ["Approve & Retry", "Cancel"],
                            RemediationType.OAuthReconnect => ["Reconnect Account", "Retry", "Cancel"],
                            RemediationType.ResourceSelection => ["Select Resource", "Cancel"],
                            _ => descriptor.Retryable ? ["Retry", "Cancel"] : ["Open Settings", "Cancel"]
                        };
                        remediationRequest = new RemediationRequest(
                            remediationId.Value, execution.OperationId, toolActionId, remediationType,
                            descriptor.ComponentName + " needs attention", descriptor.SafeMessage,
                            descriptor.ComponentId, descriptor.ComponentName, descriptor.ProviderName, requiredInputs, allowedActions,
                            sensitivity, descriptor.Retryable, canResume, RecoveryPolicyDefaults.InitialUserInteractionTimeout,
                            RecoveryPolicyDefaults.MaximumInteractiveWait, RemediationState.Waiting, now, now,
                            SecretProviderId: descriptor.SecretProviderId, SecretName: descriptor.SecretName);

                        if (canResume && runtimeKind is { } resumableRuntime)
                        {
                            continuation = async (resolution, token) =>
                            {
                                await (ordinaryOriginal is null ? safety.EnsureMayActAsync(conversation.Id, $"chat.tool.resume.{call.Name}", token)
                    : ordinaryOriginal.Read(() => safety.EnsureMayActAsync(conversation.Id, $"chat.tool.resume.{call.Name}", token), token)).ConfigureAwait(false);
                                var retryPermission = resolution.Approved ? PermissionMode.FullAccess : commandPermission;
                                var retryResult = await ObserveRuntimeAsync(resumableRuntime, retryPermission, token, Guid.NewGuid(), toolActionId).ConfigureAwait(false);
                                return new RemediationContinuationResult(
                                    retryResult.Activity.Succeeded,
                                    retryResult.Activity.Succeeded ? retryResult.Activity.Detail : "The blocked action was retried but did not complete.",
                                    retryResult.Activity.Succeeded ? null : retryResult.Activity.Detail);
                            };
                        }
                    }
                }

                var originalStatus = originalResult.Activity.Succeeded ? ExecutionActionStatus.Completed : ExecutionActionStatus.Failed;
                var unavailable = runtimeKind is null;
                var recovered = automaticRetryResult?.Activity.Succeeded == true;
                var failure = originalResult.Activity.Succeeded ? null : new ExecutionFailure(
                    descriptor?.Code ?? (unavailable ? "TOOL_UNAVAILABLE" : "TOOL_EXECUTION_FAILED"),
                    descriptor?.Kind switch
                    {
                        ToolFailureKind.PermissionRequired => "Permission required",
                        ToolFailureKind.CredentialRequired => "Connection requires attention",
                        ToolFailureKind.InvalidInput => "Tool input is invalid",
                        ToolFailureKind.ResourceUnavailable => "Required resource unavailable",
                        _ => unavailable ? "Tool unavailable" : "Tool execution failed"
                    },
                    descriptor?.SafeMessage ?? originalResult.Activity.Detail,
                    Attempt: plannedRecovery?.Attempt ?? 1, AffectedComponent: descriptor?.ComponentId ?? call.Name, Recovered: recovered);
                PublishBoundEvent(new ExecutionEvent(
                    Guid.NewGuid(), execution.OperationId, toolActionId, actionParentId, ExecutionOrigin.Haven,
                    ExecutionActionType.ToolCall, originalStatus, StatusForTool(call.Name), null, originalResult.Activity.Detail,
                    descriptor?.ComponentId ?? call.Name, firstEndedAt, toolStartedAt, firstEndedAt, RemediationId: remediationId, Failure: failure));
                var resultActionId = Guid.NewGuid();
                PublishBoundEvent(new ExecutionEvent(
                    Guid.NewGuid(), execution.OperationId, resultActionId, toolActionId, ExecutionOrigin.Haven,
                    ExecutionActionType.ToolResult, originalStatus, originalResult.Activity.Title + " result", null,
                    SensitiveTextRedactor.Redact(originalResult.Output, 8_000), descriptor?.ComponentId ?? call.Name, firstEndedAt, toolStartedAt, firstEndedAt,
                    RemediationId: remediationId, Failure: failure));
                parentActionId = resultActionId;

                if (automaticRetryResult is not null && automaticRetryActionId is { } retryId &&
                    automaticRetryStartedAt is { } retryStart && automaticRetryEndedAt is { } retryEnd)
                {
                    var retryFailure = automaticRetryResult.Activity.Succeeded ? null : new ExecutionFailure(
                        automaticRetryResult.Failure?.Code ?? "TOOL_RETRY_FAILED", "Automatic retry failed",
                        automaticRetryResult.Failure?.SafeMessage ?? automaticRetryResult.Activity.Detail, Attempt: (plannedRecovery?.Attempt ?? 1) + 1,
                        AffectedComponent: automaticRetryResult.Failure?.ComponentId ?? call.Name);
                    PublishBoundEvent(new ExecutionEvent(
                        Guid.NewGuid(), execution.OperationId, retryId, resultActionId, ExecutionOrigin.Haven,
                        ExecutionActionType.AutomaticRepair,
                        automaticRetryResult.Activity.Succeeded ? ExecutionActionStatus.Completed : ExecutionActionStatus.Failed,
                        automaticRetryResult.Activity.Succeeded ? "Safe automatic retry completed" : "Safe automatic retry failed",
                        "A bounded retry was permitted because the failure was classified as reversible, in-scope, and low risk.",
                        automaticRetryResult.Activity.Detail, automaticRetryResult.Failure?.ComponentId ?? call.Name, retryEnd, retryStart, retryEnd,
                        RetryOfActionId: toolActionId, RecoveryOfActionId: toolActionId, Failure: retryFailure));
                    parentActionId = retryId;
                }

                if (remediationRequest is not null && remediations is not null)
                    await remediations.RequestAsync(remediationRequest, continuation, cancellationToken).ConfigureAwait(false);

                return result with { Activity = result.Activity with
                {
                    InvocationEvidence = Array.AsReadOnly(invocationEvidence.ToArray()),
                    HasDeferredInvocations = continuation is not null
                } };
            }

            var bootstrapCall = computerPass?.TryCreateBootstrapCall(prompt);
            if (bootstrapCall is not null)
            {
                callsUsed++;
                lastToolCall = bootstrapCall;
                lastToolResult = await ExecuteToolAsync(bootstrapCall).ConfigureAwait(false);
                toolActivities.Add(lastToolResult.Activity);
                yield return ChatStreamEvent.Activity(assistantId, lastToolResult.Activity);
                var directResult = lastToolResult.Activity.Succeeded
                    ? CompletedActionMessage(bootstrapCall)
                    : $"The tool action could not complete: {lastToolResult.Activity.Detail}";
                buffer.Append(directResult);
                yield return ChatStreamEvent.AssistantDelta(assistantId, directResult);
            }

            while (bootstrapCall is null && callsUsed < toolCallLimit)
            {
                cancellationToken.ThrowIfCancellationRequested();
                OllamaToolResponse? response = null;
                var unsupportedToolSchema = false;
                TaskRunCloudPermissionRequiredException? originalPermissionRequired = null;
                Task<OllamaToolResponse>? originalToolTurn = null;
                ChatOriginalToolCheckpointBoundary? originalCheckpoint = null;
                TaskRunOriginalResponseOperation? originalResponse = null;
                try
                {
                    await (ordinaryOriginal is null ? safety.EnsureMayActAsync(conversation.Id, "chat.model-tool-turn", cancellationToken)
                    : ordinaryOriginal.Read(() => safety.EnsureMayActAsync(conversation.Id, "chat.model-tool-turn", cancellationToken), cancellationToken)).ConfigureAwait(false);
                    var actualInventory = canonicalIntent ? await CaptureInventoryAsync(cancellationToken).ConfigureAwait(false) : null;
                    originalResponse = canonicalIntent ? ReserveOriginalChatResponse(originalCustody!, parentActionId) : null;
                    var originalRequest = new OllamaToolRequest(
                        turnModel.Name, turns, toolDefinitions, effort, system, generationOptions)
                    { ExecutionContext = await ProviderContextAsync(originalResponse?.ActionId ?? parentActionId).ConfigureAwait(false) };
                    if (canonicalIntent)
                    {
                        originalCheckpoint = CaptureOriginalToolCheckpoint(originalCustody!, originalRequest,
                            actualInventory ?? throw new InvalidOperationException("The actual tool context inventory is unavailable."),
                            assistantId, buffer.ToString(), toolActivities, callsUsed, toolCallLimit, lastToolCall, lastToolResult,
                            toolDefinitions.ToDictionary(definition => definition.Name,
                                definition => modelPlan.TryGetRuntime(definition.Name, out var runtime) ? runtime
                                    : throw new InvalidOperationException("The original offered tool has no actual runtime binding."), StringComparer.Ordinal),
                            canonicalTask ?? throw new InvalidOperationException("The actual provider task basis is unavailable."));
                        originalRequest = originalCheckpoint.OriginalRequest;
                    }
                    if (canonicalIntent) taskCoordinator!.CaptureOriginalResponseRequest(originalResponse!, originalRequest);
                    await CaptureOriginalToolsAsync(originalRequest, actualInventory, cancellationToken).ConfigureAwait(false);
                    if (originalCustody?.OriginalColdContinuation is { } coldToolInput)
                        await taskCoordinator!.ValidateOriginalColdInputAsync(coldToolInput, originalCustody, cancellationToken).ConfigureAwait(false);
                    originalToolTurn = canonicalIntent
                        ? InvokeOriginalToolCheckpointCall(originalCheckpoint!, () => ollama.ChatWithToolsAsync(originalRequest, cancellationToken))
                        : ollama.ChatWithToolsAsync(originalRequest, cancellationToken);
                    response = await originalToolTurn.ConfigureAwait(false);
                    if (canonicalIntent && response is null)
                        throw new InvalidOperationException("The actual canonical provider returned no response observation.");
                    if (canonicalIntent)
                    {
                        originalCheckpoint!.RecordActualResponse(response);
                        var responseAck = await ObserveOriginalChatResponseTerminalAsync(originalResponse, originalToolTurn).ConfigureAwait(false);
                        if (responseAck is not null) canonicalTask = responseAck;
                    }
                    if (canonicalIntent && response.ToolCalls.Count > 0 && response.EffectiveModel is null)
                        throw new InvalidOperationException("The actual effective model is unknown; canonical tools were refused before dispatch.");
                    if (response.EffectiveModel is { } actualModel)
                    {
                        effectivePermissionModel = actualModel;
                        turnModel = actualModel.Model with
                        {
                            Name = actualModel.ProviderId.Equals("ollama", StringComparison.OrdinalIgnoreCase) ? actualModel.Name : actualModel.Key
                        };
                        etaModel = turnModel;
                        modelPlan = availabilityPlan.RestrictToModel(turnModel);
                    }
                }
                catch (TaskRunCloudPermissionRequiredException actualAsk) when (canonicalIntent && taskCloudPermissionRemediation is not null)
                {
                    originalCheckpoint?.RecordActualFailure(actualAsk, originalToolTurn);
                    originalCustody!.Retain(actualAsk, originalToolTurn);
                    await ObserveOriginalChatResponseTerminalAsync(originalResponse, originalToolTurn, actualAsk).ConfigureAwait(false);
                    originalPermissionRequired = actualAsk;
                }
                catch (HttpRequestException ex) when (IsUnsupportedToolSchema(ex))
                {
                    originalCheckpoint?.RecordActualFailure(ex, originalToolTurn);
                    if (canonicalIntent) originalCustody!.Retain(ex, originalToolTurn);
                    if (canonicalIntent) await ObserveOriginalChatResponseTerminalAsync(originalResponse, originalToolTurn, ex).ConfigureAwait(false);
                    unsupportedToolSchema = true;
                }
                catch (Exception originalFailure) when (canonicalIntent)
                {
                    originalCheckpoint?.RecordActualFailure(originalFailure, originalToolTurn);
                    originalCustody!.Retain(originalFailure, originalToolTurn);
                    await ObserveOriginalChatResponseTerminalAsync(originalResponse, originalToolTurn, originalFailure).ConfigureAwait(false);
                    throw;
                }

                if (originalPermissionRequired is not null)
                {
                    var metadata = await PublishOriginalCloudPermissionAsync(originalPermissionRequired).ConfigureAwait(false);
                    yield return ChatStreamEvent.PermissionRequired(assistantId, metadata);
                    yield break;
                }

                if (unsupportedToolSchema)
                {
                    if (canonicalIntent)
                        throw new InvalidOperationException("The canonical model lacks an actual typed tool response; compatibility text cannot authorize dispatch.");
                    bridgeAttempted = true;
                    var bridged = LooksLikeToolRequest(prompt)
                        ? await TryBridgeToolCallAsync(ollama, turnModel, effort, prompt, toolDefinitions, generationOptions, cancellationToken,
                            await ProviderContextAsync(parentActionId).ConfigureAwait(false)).ConfigureAwait(false)
                        : null;
                    if (bridged is not null)
                    {
                        callsUsed++;
                        lastToolCall = bridged;
                        lastToolResult = await ExecuteToolAsync(bridged).ConfigureAwait(false);
                        toolActivities.Add(lastToolResult.Activity);
                        yield return ChatStreamEvent.Activity(assistantId, lastToolResult.Activity);
                        var bridgedResult = lastToolResult.Activity.Succeeded
                            ? CompletedActionMessage(bridged)
                            : $"The tool action could not complete: {lastToolResult.Activity.Detail}";
                        buffer.Append(bridgedResult);
                        yield return ChatStreamEvent.AssistantDelta(assistantId, bridgedResult);
                    }
                    else
                    {
                        const string unsupported = "This local model cannot emit tool calls for that request. Choose a tool-capable model, or use a directly supported Computer Use launch request.";
                        buffer.Append(unsupported);
                        yield return ChatStreamEvent.AssistantDelta(assistantId, unsupported);
                    }
                    break;
                }

                if (response is null)
                    throw new InvalidOperationException("Ollama returned no tool response.");
                if (response.ToolCalls.Count == 0)
                {
                    if (!canonicalIntent && !bridgeAttempted && callsUsed == 0 && LooksLikeToolRequest(prompt))
                    {
                        bridgeAttempted = true;
                        var bridged = await TryBridgeToolCallAsync(ollama, turnModel, effort, prompt, toolDefinitions, generationOptions, cancellationToken,
                            await ProviderContextAsync(parentActionId).ConfigureAwait(false)).ConfigureAwait(false);
                        if (bridged is not null)
                        {
                            callsUsed++;
                            lastToolCall = bridged;
                            lastToolResult = await ExecuteToolAsync(bridged).ConfigureAwait(false);
                            toolActivities.Add(lastToolResult.Activity);
                            yield return ChatStreamEvent.Activity(assistantId, lastToolResult.Activity);
                            turns.Add(new OllamaToolTurn("assistant", string.Empty, [bridged]));
                            turns.Add(new OllamaToolTurn("tool", lastToolResult.Output, ToolName: bridged.Name));
                            continue;
                        }
                    }
                    var content = string.IsNullOrWhiteSpace(response.Content)
                        ? "The tool pass completed without a final model response. Review the activity above."
                        : response.Content;
                    if (lastToolResult?.Activity.Succeeded == true && lastToolCall is not null && ResponseContradictsCompletedAction(content))
                        content = CompletedActionMessage(lastToolCall);
                    buffer.Append(content);
                    yield return ChatStreamEvent.AssistantDelta(assistantId, content);
                    break;
                }

                turns.Add(new OllamaToolTurn("assistant", response.Content, response.ToolCalls));
                if (!string.IsNullOrWhiteSpace(response.Content))
                {
                    buffer.Append(response.Content);
                    yield return ChatStreamEvent.AssistantDelta(assistantId, response.Content);
                }
                foreach (var call in response.ToolCalls)
                {
                    if (callsUsed >= toolCallLimit) break;
                    callsUsed++;
                    lastToolCall = call;
                    var result = await ExecuteToolAsync(call).ConfigureAwait(false);
                    lastToolResult = result;
                    toolActivities.Add(result.Activity);
                    yield return ChatStreamEvent.Activity(assistantId, result.Activity);
                    turns.Add(new OllamaToolTurn("tool", result.Output, ToolName: call.Name));
                }
            }

            if (buffer.Length == 0)
            {
                var limitMessage = $"Stopped after reaching the tool-call safety limit of {toolCallLimit}. Review the activity before continuing.";
                buffer.Append(limitMessage);
                yield return ChatStreamEvent.AssistantDelta(assistantId, limitMessage);
            }
        }
        else
        {
            var firstChunk = true;
            var thinkingBuffer = new StringBuilder();
            await (ordinaryOriginal is null ? safety.EnsureMayActAsync(conversation.Id, "chat.model-stream", cancellationToken)
                    : ordinaryOriginal.Read(() => safety.EnsureMayActAsync(conversation.Id, "chat.model-stream", cancellationToken), cancellationToken)).ConfigureAwait(false);
            var actualInventory = canonicalIntent ? await CaptureInventoryAsync(cancellationToken).ConfigureAwait(false) : null;
            var originalResponse = canonicalIntent ? ReserveOriginalChatResponse(originalCustody!, parentActionId) : null;
            var originalRequest = new OllamaChatRequest(turnModel.Name, requestMessages, effort, system, Options: generationOptions)
            { ExecutionContext = await ProviderContextAsync(originalResponse?.ActionId ?? parentActionId).ConfigureAwait(false) };
            if (!canonicalIntent)
            {
                await CaptureOriginalChatAsync(originalRequest, actualInventory, cancellationToken).ConfigureAwait(false);
                var providerStream = ordinaryOriginal is null ? ollama.StreamChatAsync(originalRequest, cancellationToken)
                    : ordinaryOriginal.ProviderStream(() => ollama.StreamChatAsync(originalRequest, cancellationToken));
                await foreach (var chunk in providerStream.ConfigureAwait(false))
                {
                    if (firstChunk)
                    {
                        execution.Update(ChatExecutionStage.Generating, "Writing Response");
                        firstChunk = false;
                    }

                    // Detect thinking tokens (prefixed with \x00T:)
                    await (ordinaryOriginal is null ? safety.EnsureMayActAsync(conversation.Id, "chat.model-stream-chunk", cancellationToken)
                    : ordinaryOriginal.Read(() => safety.EnsureMayActAsync(conversation.Id, "chat.model-stream-chunk", cancellationToken), cancellationToken)).ConfigureAwait(false);
                    if (chunk.StartsWith("\x00T:"))
                    {
                        var thinkingContent = chunk[3..];
                        thinkingBuffer.Append(thinkingContent);
                        yield return ChatStreamEvent.ThinkingDelta(assistantId, thinkingContent);
                    }
                    else
                    {
                        buffer.Append(chunk);
                        yield return ChatStreamEvent.AssistantDelta(assistantId, chunk);
                    }
                }
            }
            else
            {
                IAsyncEnumerator<string>? actualStream = null;
                TaskRunCloudPermissionRequiredException? originalPermissionRequired = null;
                try
                {
                    await CaptureOriginalChatAsync(originalRequest, actualInventory, cancellationToken).ConfigureAwait(false);
                    if (originalCustody?.OriginalColdContinuation is { } coldStreamInput)
                        await taskCoordinator!.ValidateOriginalColdInputAsync(coldStreamInput, originalCustody, cancellationToken).ConfigureAwait(false);
                    taskCoordinator!.CaptureOriginalResponseRequest(originalResponse!, originalRequest);
                    InvokeOriginalResponseCallback(originalResponse!, () =>
                    {
                        originalCustody!.OriginalProviderInvocationInvoked = true;
                        actualStream = ollama.StreamChatAsync(originalRequest, cancellationToken).GetAsyncEnumerator(cancellationToken);
                    });
                }
                catch (TaskRunCloudPermissionRequiredException actualAsk) when (taskCloudPermissionRemediation is not null)
                {
                    originalCustody!.Retain(actualAsk); originalPermissionRequired = actualAsk;
                    await ObserveOriginalChatResponseTerminalAsync(originalResponse, failure: actualAsk).ConfigureAwait(false);
                }
                catch (Exception actualFailure)
                {
                    originalCustody!.Retain(actualFailure);
                    await ObserveOriginalChatResponseTerminalAsync(originalResponse, failure: actualFailure).ConfigureAwait(false);
                    throw;
                }
                if (originalPermissionRequired is not null)
                {
                    var metadata = await PublishOriginalCloudPermissionAsync(originalPermissionRequired).ConfigureAwait(false);
                    yield return ChatStreamEvent.PermissionRequired(assistantId, metadata);
                    yield break;
                }
                if (actualStream is null) throw new InvalidOperationException("The actual canonical stream iterator is unavailable.");
                try
                {
                    while (true)
                    {
                        Task<bool>? actualMove = null;
                        bool hasChunk;
                        try
                        {
                            InvokeOriginalResponseCallback(originalResponse!, () =>
                            {
                                actualMove = actualStream.MoveNextAsync().AsTask();
                                originalCustody!.RetainAdditionalOriginal("provider.stream.move", actualMove);
                                taskCoordinator!.CaptureOriginalResponseMove(originalResponse!, actualMove);
                            });
                            hasChunk = await actualMove!.ConfigureAwait(false);
                        }
                        catch (TaskRunCloudPermissionRequiredException actualAsk) when (taskCloudPermissionRemediation is not null)
                        {
                            originalCustody!.Retain(actualAsk, actualMove);
                            originalPermissionRequired = actualAsk;
                            await ObserveOriginalChatResponseTerminalAsync(originalResponse, failure: actualAsk).ConfigureAwait(false);
                            break;
                        }
                        catch (Exception originalFailure)
                        {
                            originalCustody!.Retain(originalFailure, actualMove);
                            await ObserveOriginalChatResponseTerminalAsync(originalResponse, failure: originalFailure).ConfigureAwait(false);
                            throw;
                        }
                        if (!hasChunk) { originalResponse!.StreamReachedEnd = true; break; }
                        string chunk = "";
                        InvokeOriginalResponseCallback(originalResponse!, () => chunk = actualStream.Current, cleanup: true);
                        if (firstChunk)
                        {
                            execution.Update(ChatExecutionStage.Generating, "Writing Response");
                            firstChunk = false;
                        }

                        // Detect thinking tokens (prefixed with \x00T:)
                        await (ordinaryOriginal is null ? safety.EnsureMayActAsync(conversation.Id, "chat.model-stream-chunk", cancellationToken)
                    : ordinaryOriginal.Read(() => safety.EnsureMayActAsync(conversation.Id, "chat.model-stream-chunk", cancellationToken), cancellationToken)).ConfigureAwait(false);
                        if (chunk.StartsWith("\x00T:"))
                        {
                            var thinkingContent = chunk[3..];
                            thinkingBuffer.Append(thinkingContent);
                            yield return ChatStreamEvent.ThinkingDelta(assistantId, thinkingContent);
                        }
                        else
                        {
                            buffer.Append(chunk);
                            yield return ChatStreamEvent.AssistantDelta(assistantId, chunk);
                        }
                    }
                }
                finally
                {
                    Task? actualDispose = null;
                    try
                    {
                        InvokeOriginalResponseCallback(originalResponse!, () =>
                        {
                            actualDispose = actualStream.DisposeAsync().AsTask();
                            originalCustody!.RetainAdditionalOriginal("provider.stream.dispose", actualDispose);
                            taskCoordinator!.CaptureOriginalResponseDispose(originalResponse!, actualDispose);
                        }, cleanup: true);
                        await actualDispose!.ConfigureAwait(false);
                    }
                    catch (Exception originalCleanup)
                    {
                        originalCustody!.Retain(originalCleanup, actualDispose);
                        await ObserveOriginalChatResponseTerminalAsync(originalResponse, failure: originalCleanup).ConfigureAwait(false);
                        throw;
                    }
                    finally
                    {
                        var responseAck = await ObserveOriginalChatResponseTerminalAsync(originalResponse).ConfigureAwait(false);
                        if (responseAck is not null) canonicalTask = responseAck;
                    }
                }
                if (originalPermissionRequired is not null)
                {
                    var metadata = await PublishOriginalCloudPermissionAsync(originalPermissionRequired).ConfigureAwait(false);
                    yield return ChatStreamEvent.PermissionRequired(assistantId, metadata);
                    yield break;
                }
            }
        }

        await (ordinaryOriginal is null ? safety.EnsureMayActAsync(conversation.Id, "chat.complete", cancellationToken)
                    : ordinaryOriginal.Read(() => safety.EnsureMayActAsync(conversation.Id, "chat.complete", cancellationToken), cancellationToken)).ConfigureAwait(false);
        var assistantMetadata = toolActivities.Count == 0 ? null : JsonSerializer.Serialize(new { toolActivities });
        var assistant = new ChatMessage(assistantId, conversation.Id, MessageRole.Assistant, buffer.ToString(), agentName, turnModel.Name, assistantMetadata, DateTimeOffset.UtcNow);
        if (!conversation.IsTemporary)
            await (ordinaryOriginal is null ? conversations.AddMessageAsync(assistant, cancellationToken)
                    : ordinaryOriginal.Write(() => conversations.AddMessageAsync(assistant, cancellationToken))).ConfigureAwait(false);
        if (taskCoordinator is not null && canonicalTask?.OwnerBinding is not null)
        {
            var current = await taskCoordinator.GetAsync(canonicalTask.TaskId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The canonical task was lost before original execution completion.");
            _ = current.Attempts.LastOrDefault()
                ?? throw new InvalidOperationException("No actual provider attempt can be completed.");
            // This is only the observed basis. The outer owner completes after the actual
            // body, iterator Dispose, tracker timer and computer resource cleanup.
            (originalCustody ?? throw new InvalidOperationException("No actual original completion custody exists.")).CompletionBasis = current;
        }
        execution.Complete();
        execution.Changed -= PublishExecution;
        yield return ChatStreamEvent.AssistantCompleted(assistant);
    }

    private async IAsyncEnumerable<ChatStreamEvent> ObserveOriginalSendAsync(
        IAsyncEnumerable<ChatStreamEvent> originalBody, TaskRunInvocationCustody originalCustody,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var original = originalBody.GetAsyncEnumerator(cancellationToken);
        TaskExecutionSnapshot? acknowledgedOriginalTerminal = null;
        try
        {
            while (true)
            {
                Task<bool>? originalMove = null;
                bool hasItem;
                ChatStreamEvent item;
                try
                {
                    originalMove = original.MoveNextAsync().AsTask();
                    originalCustody.RetainOriginalMove(originalMove);
                    hasItem = await originalMove.ConfigureAwait(false);
                    if (!hasItem) { originalCustody.ReachedEnd = true; break; }
                    item = original.Current;
                }
                catch (Exception bodyFailure)
                {
                    originalCustody.Retain(bodyFailure, originalMove);
                    break;
                }
                if (item.Kind == ChatStreamEventKind.PermissionRequired)
                {
                    if (originalCustody.DeferredPermissionRequiredMessage is not null)
                        throw new InvalidOperationException("The actual original produced more than one permission request.");
                    originalCustody.DeferredPermissionRequiredMessage = item;
                    continue; // Publish only after actual cleanup and the acknowledged suspension.
                }
                if (item.Kind == ChatStreamEventKind.AssistantCompleted)
                {
                    if (originalCustody.DeferredCompletedMessage is not null)
                        throw new InvalidOperationException("The actual original produced more than one final response.");
                    originalCustody.DeferredCompletedMessage = item;
                    continue;
                }
                yield return item;
            }
        }
        finally
        {
            // Invoke and retain the SAME Dispose once, including synchronous call failures.
            originalCustody.DisposeInvoked = true;
            try
            {
                originalCustody.OriginalDispose = original.DisposeAsync().AsTask();
                await originalCustody.OriginalDispose.ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                if (originalCustody.OriginalDispose is null) originalCustody.DisposeDirectFailure = cleanupFailure;
                originalCustody.Retain(cleanupFailure, originalCustody.OriginalDispose);
            }
            if (originalCustody.OriginalTracker is { } tracker)
            {
                originalCustody.TrackerDisposeInvoked = true;
                try
                {
                    originalCustody.OriginalTrackerDispose = tracker.DisposeAsync().AsTask();
                    await originalCustody.OriginalTrackerDispose.ConfigureAwait(false);
                }
                catch (Exception trackerFailure)
                {
                    if (originalCustody.OriginalTrackerDispose is null) originalCustody.TrackerDisposeDirectFailure = trackerFailure;
                    originalCustody.Retain(trackerFailure, originalCustody.OriginalTrackerDispose);
                }
            }
            foreach (var resource in originalCustody.OriginalResources)
                try { resource.Dispose(); } catch (Exception resourceFailure) { originalCustody.Retain(resourceFailure); }
            originalCustody.ResourcesDisposed = true;
            originalCustody.OwnedCleanupTerminal = true;
            Task? actualResponseCleanup = null;
            try
            {
                originalCustody.OriginalProcessProducer!.InvokeOriginalCallback(() =>
                {
                    actualResponseCleanup = taskCoordinator!.FinishOriginalResponseOwnerCleanupAsync(originalCustody);
                    originalCustody.RetainAdditionalOriginal("response.owner-cleanup", actualResponseCleanup);
                });
                await actualResponseCleanup!.ConfigureAwait(false);
            }
            catch (Exception responseCleanup) { originalCustody.Retain(responseCleanup, actualResponseCleanup); }
            if (originalCustody.ReachedEnd && originalCustody.Causes.Count == 0 && originalCustody.DeferredCompletedMessage is not null)
            {
                try
                {
                    CurrentCanonicalTask = await taskCoordinator!.CompleteOriginalInvocationAsync(originalCustody).ConfigureAwait(false);
                }
                catch (Exception completionFailure) { originalCustody.Retain(completionFailure, originalCustody.OriginalCompletion); }
            }
            try
            {
                var observed = await taskCoordinator!.ObserveOriginalInvocationTerminalAsync(originalCustody).ConfigureAwait(false);
                if (observed is not null)
                {
                    acknowledgedOriginalTerminal = observed;
                    originalCustody.OriginalTerminalObservation = observed;
                    CurrentCanonicalTask = observed;
                }
            }
            catch (Exception observationFailure) { taskCoordinator!.RetainOriginalInvocationObservationFailure(originalCustody, observationFailure); }
            // A sole source-issued expected Ask may return its waiting metadata only after
            // actual cleanup and the SAME run's suspension CAS are acknowledged. All siblings
            // and uncertain observation/write outcomes retain their original thrown failures.
            if (!originalCustody.CanReturnPublishedPermissionRefusal(acknowledgedOriginalTerminal))
                originalCustody.ThrowRetained();
        }
        if (originalCustody.CanReturnPublishedPermissionRefusal(acknowledgedOriginalTerminal)
            && originalCustody.DeferredPermissionRequiredMessage is { } required)
        {
            var acknowledged = acknowledgedOriginalTerminal
                ?? throw new InvalidOperationException("No actual acknowledged suspended task observation exists.");
            yield return required with
            {
                CanonicalTaskContext = new ProviderExecutionContext(acknowledged.TaskId, acknowledged.ContextId,
                    acknowledged.ExecutionId, acknowledged.Attempts.LastOrDefault()?.Id, acknowledged.PersistenceRevision)
            };
            yield break;
        }
        if (originalCustody.OriginalCompletion is { IsCompletedSuccessfully: true }
            && originalCustody.DeferredCompletedMessage is { } completed)
        {
            try { originalCustody.DeferredCompletionPublication?.Invoke(); }
            catch (Exception observerFailure) { taskCoordinator!.ReportAcknowledgedCompletionObservationFailure(originalCustody, observerFailure); }
            yield return completed;
        }
    }

    private static string SummarizePrompt(string prompt)
    {
        var firstLine = prompt.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "Request";
        return SensitiveTextRedactor.Redact(firstLine, 240);
    }

    private static ExecutionActionType MapActionType(ChatExecutionStage stage) => stage switch
    {
        ChatExecutionStage.Preparing or ChatExecutionStage.LoadingModel or ChatExecutionStage.LoadingContext => ExecutionActionType.Planning,
        ChatExecutionStage.SelectingCapabilities or ChatExecutionStage.Thinking => ExecutionActionType.ReasoningSummary,
        ChatExecutionStage.InspectingCode => ExecutionActionType.ProjectAction,
        ChatExecutionStage.Searching => ExecutionActionType.Search,
        ChatExecutionStage.Browsing => ExecutionActionType.ToolCall,
        ChatExecutionStage.RunningTool or ChatExecutionStage.RunningCommand => ExecutionActionType.ToolCall,
        ChatExecutionStage.EditingFiles => ExecutionActionType.FileAction,
        ChatExecutionStage.Testing => ExecutionActionType.ProjectAction,
        ChatExecutionStage.Generating or ChatExecutionStage.Speaking => ExecutionActionType.ModelExecution,
        ChatExecutionStage.WaitingForApproval => ExecutionActionType.UserActionRequired,
        ChatExecutionStage.Recovering => ExecutionActionType.AutomaticRepair,
        ChatExecutionStage.Completed => ExecutionActionType.FinalResponse,
        ChatExecutionStage.Failed => ExecutionActionType.Error,
        ChatExecutionStage.Cancelled => ExecutionActionType.Warning,
        _ => ExecutionActionType.ModelExecution
    };

    private static ExecutionActionStatus MapActionStatus(ChatExecutionLogEntry entry) => entry.Stage switch
    {
        ChatExecutionStage.Completed => ExecutionActionStatus.Completed,
        ChatExecutionStage.Failed => ExecutionActionStatus.Failed,
        ChatExecutionStage.Cancelled => ExecutionActionStatus.Cancelled,
        ChatExecutionStage.WaitingForApproval => ExecutionActionStatus.UserActionRequired,
        _ when !entry.Succeeded => ExecutionActionStatus.Failed,
        _ => ExecutionActionStatus.Running
    };

    private static bool IsTerminal(ChatExecutionStage stage) => stage is
        ChatExecutionStage.Completed or ChatExecutionStage.Failed or ChatExecutionStage.Cancelled;

    private static string? SafeReasoningFor(ChatExecutionStage stage) => stage switch
    {
        ChatExecutionStage.LoadingModel => "Selecting an available model that supports the requested work.",
        ChatExecutionStage.LoadingContext => "Loading only the authorised context needed for this response.",
        ChatExecutionStage.SelectingCapabilities => "Selecting registered capabilities relevant to the request.",
        ChatExecutionStage.Recovering => "A bounded recovery path is being attempted within the existing permissions.",
        _ => null
    };

    /// <summary>
    /// Creates availability plan with the invariants required by its callers.
    /// </summary>
    private static HashSet<ToolCapability> ToolCapabilitiesFromRegisteredCapabilities(
        IReadOnlyCollection<ActiveCapability> capabilities)
    {
        var result = new HashSet<ToolCapability>();
        foreach (var capability in capabilities)
        {
            if (ExternalConnectionNaming.IsConnectionCapability(capability.Key))
            {
                result.Add(ToolCapability.Tools);
                continue;
            }
            switch (capability.Key)
            {
                case "web-search":
                    result.Add(ToolCapability.WebSearch);
                    result.Add(ToolCapability.Browser);
                    break;
                case "browser-use":
                    result.Add(ToolCapability.Browser);
                    break;
                case "computer-device-use":
                    result.Add(ToolCapability.ComputerUse);
                    break;
                case "create-automation":
                case "run-task":
                case "edit-task":
                case "run-command":
                case "run-script":
                case "powershell":
                case "read-file":
                case "write-file":
                case "run-tests":
                    result.Add(ToolCapability.Tools);
                    break;
            }
        }

        return result;
    }

    private static bool NeedsToolRuntime(
        IReadOnlySet<ToolCapability> capabilities) =>
        capabilities.Contains(ToolCapability.Tools) ||
        capabilities.Contains(ToolCapability.Browser) ||
        capabilities.Contains(ToolCapability.WebSearch) ||
        capabilities.Contains(ToolCapability.ComputerUse);

    private static IReadOnlyCollection<ActiveCapability> FilterCapabilitiesForTurn(
        IReadOnlyCollection<ActiveCapability> registeredCapabilities,
        IReadOnlySet<ToolCapability> required)
    {
        if (!NeedsToolRuntime(required)) return [];

        return registeredCapabilities
            .Where(capability => ExternalConnectionNaming.IsConnectionCapability(capability.Key)
                ? required.Contains(ToolCapability.Tools)
                : capability.Key switch
            {
                "web-search" => required.Contains(ToolCapability.WebSearch),
                "browser-use" => required.Contains(ToolCapability.Browser)
                                 && !required.Contains(ToolCapability.WebSearch),
                "computer-device-use" or "open-control-app" => required.Contains(ToolCapability.ComputerUse),
                "create-automation" or "run-task" or "edit-task" or
                "run-command" or "run-script" or "powershell" or
                "read-file" or "write-file" or "run-tests" => required.Contains(ToolCapability.Tools),
                _ => false
            })
            .ToArray();
    }

    private static bool IsToolSelectedForTurn(
        ToolAvailabilityPlan plan,
        string toolName,
        IReadOnlySet<ToolCapability> capabilities)
    {
        if (!plan.TryGetRuntime(toolName, out var runtime))
        {
            return false;
        }

        return runtime switch
        {
            ToolRuntimeKind.Browser =>
                capabilities.Contains(ToolCapability.Browser) ||
                capabilities.Contains(ToolCapability.WebSearch),
            ToolRuntimeKind.Computer =>
                capabilities.Contains(ToolCapability.ComputerUse),
            ToolRuntimeKind.Workspace or ToolRuntimeKind.Automation or ToolRuntimeKind.Mcp =>
                capabilities.Contains(ToolCapability.Tools),
            _ => false
        };
    }

    private static ChatExecutionStage StageForTool(
        string toolName,
        ToolRuntimeKind runtime)
    {
        if (toolName.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            return ChatExecutionStage.Testing;
        }

        if (toolName.Contains("command", StringComparison.OrdinalIgnoreCase))
        {
            return ChatExecutionStage.RunningCommand;
        }

        if (toolName.Contains("write", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("replace", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("change_set", StringComparison.OrdinalIgnoreCase))
        {
            return ChatExecutionStage.EditingFiles;
        }

        return runtime switch
        {
            ToolRuntimeKind.Browser => ChatExecutionStage.Browsing,
            ToolRuntimeKind.Workspace => ChatExecutionStage.InspectingCode,
            _ => ChatExecutionStage.RunningTool
        };
    }

    private static string StatusForTool(string toolName)
    {
        if (toolName.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            return "Testing";
        }

        if (toolName.Contains("command", StringComparison.OrdinalIgnoreCase))
        {
            return "Running Command";
        }

        if (toolName.Contains("write", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("replace", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("change_set", StringComparison.OrdinalIgnoreCase))
        {
            return "Editing Files";
        }

        if (toolName.StartsWith("browser_", StringComparison.Ordinal))
        {
            return "Using Browser";
        }

        if (toolName.StartsWith("workspace_", StringComparison.Ordinal) ||
            toolName is "read_file" or "list_files" or "search_files")
        {
            return "Inspecting Code";
        }

        return "Using Tool";
    }

    private static string DescribeTool(OllamaToolCall call)
    {
        if (call.Name.Contains("command", StringComparison.OrdinalIgnoreCase) &&
            call.Arguments.TryGetValue("command", out var command) &&
            command.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(command.GetString()))
        {
            var value = command.GetString()!
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            return "Running command: " +
                (value.Length <= 180 ? value : value[..180] + "…");
        }

        return "Started " + call.Name.Replace('_', ' ') + ".";
    }

    private ToolAvailabilityPlan CreateAvailabilityPlan(
        HavenMode mode,
        string? workspaceRoot,
        IReadOnlyCollection<ActiveCapability> capabilities,
        PermissionMode filePermission,
        PermissionMode commandPermission,
        PermissionMode browserPermission,
        IReadOnlyList<OllamaToolDefinition> computerDefinitions,
        IReadOnlyList<OllamaToolDefinition>? mcpDefinitions = null,
        IReadOnlyList<OllamaToolDefinition>? calendarDefinitions = null,
        IReadOnlyList<PluginToolBinding>? pluginBindings = null) =>
        (toolAvailability ?? ToolAvailabilityPlanner.Default).Create(
            new ToolAvailabilityContext(
                mode,
                workspaceRoot,
                capabilities,
                filePermission,
                commandPermission,
                browserPermission,
                computerTools.IsSupported,
                browserTools is not null,
                browserTools?.IsInteractiveAvailable == true,
                automationTools is not null),
            new ToolDefinitionSources(
                workspaceTools.Definitions,
                computerDefinitions,
                browserTools?.BackgroundDefinitions ?? [],
                browserTools?.InteractiveDefinitions ?? [],
                automationTools?.GetDefinitions(true, false) ?? [],
                automationTools?.GetDefinitions(false, true) ?? [],
                mcpDefinitions ?? [],
                calendarDefinitions ?? [],
                pluginBindings ?? []));

    /// <summary>
    /// Performs the approvable step owned by this component.
    /// </summary>
    private static PermissionMode Approvable(PermissionMode permission) =>
        permission == PermissionMode.Ask ? PermissionMode.FullAccess : permission;

    /// <summary>
    /// Performs the looks like tool request step owned by this component.
    /// </summary>
    private static bool LooksLikeToolRequest(string prompt)
    {
        var value = prompt.TrimStart();
        return new[] { "open ", "launch ", "start ", "click ", "type ", "press ", "focus ", "close ", "run " }
            .Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reports whether unsupported tool schema applies to the current state.
    /// </summary>
    private static bool IsUnsupportedToolSchema(HttpRequestException exception) =>
        exception.Message.Contains("does not support tools", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("tool support", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Performs the response contradicts completed action step owned by this component.
    /// </summary>
    private static bool ResponseContradictsCompletedAction(string response)
    {
        var value = response.ToLowerInvariant();
        return value.Contains("i can't directly", StringComparison.Ordinal) ||
               value.Contains("i cannot directly", StringComparison.Ordinal) ||
               value.Contains("i can guide you", StringComparison.Ordinal) ||
               value.Contains("how to open", StringComparison.Ordinal) ||
               value.Contains("press the windows key", StringComparison.Ordinal) ||
               value.Contains("unable to control", StringComparison.Ordinal) ||
               value.Contains("cannot open applications", StringComparison.Ordinal);
    }

    /// <summary>
    /// Performs the completed action message step owned by this component.
    /// </summary>
    private static string CompletedActionMessage(OllamaToolCall call) => call.Name switch
    {
        "computer_launch_app" => $"Done — opened {ArgumentText(call, "name", "the application")}.",
        "computer_focus_window" => $"Done — focused {ArgumentText(call, "title", "the requested window")}.",
        "computer_close_window" => $"Done — requested that {ArgumentText(call, "title", "the requested window")} close.",
        "computer_invoke" or "computer_click" => "Done — used the requested desktop control.",
        "computer_type" => "Done — typed into the requested window.",
        "computer_press" => $"Done — pressed {ArgumentText(call, "keys", "the requested keys")}.",
        _ => "Done — the requested tool action completed."
    };

    /// <summary>
    /// Performs the argument text step owned by this component.
    /// </summary>
    private static string ArgumentText(OllamaToolCall call, string name, string fallback) =>
        call.Arguments.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : fallback;

    /// <summary>
    /// Attempts to bridge tool call async and reports the result without using failure for normal control flow.
    /// </summary>
    private static async Task<OllamaToolCall?> TryBridgeToolCallAsync(
        IOllamaClient ollama,
        ModelDescriptor model,
        EffortLevel effort,
        string prompt,
        IReadOnlyList<OllamaToolDefinition> definitions,
        GenerationOptions? generationOptions,
        CancellationToken cancellationToken,
        ProviderExecutionContext? executionContext = null)
    {
        try
        {
            var allowed = definitions.Select(definition => definition.Name).ToHashSet(StringComparer.Ordinal);
            var system = "You are Haven's compatibility tool router. Choose exactly one appropriate tool for the user's action request. " +
                         "Return only one JSON object in the form {\"name\":\"tool_name\",\"arguments\":{}}. " +
                         "Use an empty name when no tool is appropriate. Available tools: " + JsonSerializer.Serialize(definitions);
            var response = await ollama.CompleteAsync(new OllamaChatRequest(
                model.Name, [new OllamaMessage("user", prompt)], effort, system, Options: generationOptions)
            { ExecutionContext = executionContext }, cancellationToken).ConfigureAwait(false);
            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            using var document = JsonDocument.Parse(response[start..(end + 1)]);
            var root = document.RootElement;
            var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) && root.TryGetProperty("tool", out var toolElement)) name = toolElement.GetString();
            if (string.IsNullOrWhiteSpace(name) || !allowed.Contains(name)) return null;
            var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (root.TryGetProperty("arguments", out var argumentElement) && argumentElement.ValueKind == JsonValueKind.Object)
                foreach (var property in argumentElement.EnumerateObject()) arguments[property.Name] = property.Value.Clone();
            return new OllamaToolCall(name, arguments);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the provider descriptor used for model-permission evaluation of the active turn model.
    /// Provider-qualified keys ("openai:gpt-4o") are honoured so cloud rules apply to cloud models.
    /// </summary>
    private static ProviderModelDescriptor DescriptorForPermission(ModelDescriptor model)
    {
        var separator = model.Name.IndexOf(':');
        if (separator > 0 && model.Name[..separator] is not ("ollama"))
            return new ProviderModelDescriptor(model.Name[..separator], false, model);
        return new ProviderModelDescriptor("ollama", true, model);
    }

    /// <summary>
    /// Builds system prompt from the currently available inputs.
    /// </summary>
    private static string BuildSystemPrompt(
        Conversation conversation,
        IReadOnlyCollection<ActiveCapability> capabilities,
        IReadOnlyCollection<ActivePrompt> prompts,
        string agentName,
        string agentInstructions,
        DuoMode duoMode,
        string? workspaceRoot,
        string? projectContext,
        string? projectInstructions,
        string? registeredContext,
        bool computerUseEnabled,
        string? personalityDirective = null,
        string? providerDefaultsDirective = null)
    {
        var mode = conversation.Mode switch
        {
            HavenMode.Chat => "You are Haven Chat, a local private assistant.",
            HavenMode.Study => "You are Haven Study. Explain clearly, check factual teaching claims with enabled research tools, and adapt to the learner.",
            HavenMode.Tasks => "You are Haven Tasks. Complete tasks safely, request approval for risky or irreversible actions, and keep an audit trail.",
            HavenMode.Studio => "You are Haven Studio. Inspect, edit, test, observe failures, repair, explain, and validate local software projects before finishing.",
            _ => "You are Haven."
        };
        var builder = new StringBuilder(mode);
        builder.Append(" Active agent: ").Append(agentName).Append('.');
        if (!string.IsNullOrWhiteSpace(personalityDirective))
            builder.Append('\n').Append(personalityDirective.Trim());
        if (!string.IsNullOrWhiteSpace(agentInstructions))
            builder.Append("\nAgent instructions:\n").Append(agentInstructions.Trim());
        if (duoMode == DuoMode.PingPong)
            builder.Append("\nDuo mode is Ping Pong. Take one clear turn, state what changed or what the other participant should do next, then hand over instead of silently completing both sides.");
        else if (duoMode == DuoMode.Collaborate)
            builder.Append("\nDuo mode is Collaborate. Treat the user as a live collaborator, make shared workspace changes explicit, call out assumptions, and leave concise review points for the next human turn.");
        else if (duoMode == DuoMode.Supervise)
            builder.Append("\nDuo mode is Supervise. The user does most of the work. Watch for mistakes, risky assumptions, spaghetti-code trends, and repeated tedious work; offer concise, timely suggestions and propose automation without taking over unless asked.");
        if (!string.IsNullOrWhiteSpace(projectContext))
            builder.Append("\nShared project context:\n").Append(projectContext.Trim());
        if (!string.IsNullOrWhiteSpace(projectInstructions))
            builder.Append("\nProject instructions and golden rules:\n").Append(projectInstructions.Trim());
        if (!string.IsNullOrWhiteSpace(registeredContext))
            builder.Append("\nRegistered conversation context and prior compact summaries:\n").Append(registeredContext.Trim());
        foreach (var capability in capabilities.Where(capability => !string.IsNullOrWhiteSpace(capability.Instructions)))
            builder.Append("\nCapability ").Append(capability.Name).Append(":\n").Append(capability.Instructions.Trim());
        foreach (var prompt in prompts.Where(prompt => !string.IsNullOrWhiteSpace(prompt.Instructions)))
            builder.Append("\nPrompt >").Append(prompt.Name).Append(":\n").Append(prompt.Instructions.Trim());
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            builder.Append("\nYou are connected to real Haven workspace tools rooted at: ").Append(workspaceRoot)
                .Append("\nUse the tools instead of pretending to inspect or modify files. Inspect first, make the minimum necessary changes, examine each result, run relevant validation, and continue until complete or genuinely blocked. Never claim an action succeeded unless a tool result confirms it. Do not access paths outside the selected workspace.");
            if (conversation.Mode is HavenMode.Tasks or HavenMode.Studio)
                builder.Append("\nBefore a material edit, give a concise impact estimate (scope, risk, affected surfaces). During edits, keep steps and change counts explicit. After every edit, provide a short changelog. Explain errors in plain English and point to their likely cause. Gather only relevant logs and recent actions. Critical correctness or security faults may be fixed immediately; ask before unrelated cleanup, style-only rewrites, dependency modernisation, or scope expansion. Keep deliverables easy to find, with a desktop executable at the requested top level when packaging permits it.");
            if (conversation.Mode == HavenMode.Studio)
                builder.Append("\nUse project decisions as constraints and warn before reversing one. Convert rough requests into requirements, constraints, and acceptance checks before broad changes. Generate targeted tests from those checks. Before release or publish, assess changed files, dependencies, past failures, and test coverage, then run the highest-risk tests first. Recommend smart initial settings and existing features when they materially help, explain why once without nagging, and wait for approval before changing settings.");
        }
        if (computerUseEnabled)
        {
            builder.Append("\nComputer Use is active and controls the real Windows desktop. Complete multi-step desktop requests with tools rather than treating the whole sentence as an application name. Use computer_launch_app with only the exact app name. Every mutation tool includes a post-action inspection in its result, so use that verification to choose the next step; call computer_snapshot or computer_list_windows separately only when more state is needed or a verification failed. Bind every input action to an exact visible target window and stop if verification fails.");
        }
        if (!string.IsNullOrWhiteSpace(providerDefaultsDirective))
            builder.Append('\n').Append(providerDefaultsDirective.Trim());
        builder.Append("\nWhen a short multiple-choice clarification is genuinely required, end with exactly one tag in this form: <haven-question>{\"question\":\"...\",\"options\":[\"First\",\"Second\"]}</haven-question>. Provide two or three mutually exclusive options and do not invent an Other option.");
        builder.Append("\nNever claim a tool or browser action happened unless a tool result confirms it.");
        return builder.ToString();
    }
}

/// <summary>
/// Represents chat stream event and keeps its related state and behavior together.
/// </summary>
public sealed record ChatStreamEvent(
    ChatStreamEventKind Kind,
    ChatMessage? Message = null,
    Guid? MessageId = null,
    string? Delta = null,
    string? Thinking = null,
    string? Model = null,
    string? Agent = null,
    CapabilityPreflightResult? PreflightResult = null,
    ToolActivity? ToolActivity = null)
{
    /// <summary>Source-issued waiting request metadata only; never permission or resume authority.</summary>
    public RemediationRequest? PermissionRequest { get; init; }
    /// <summary>Detached IDs/revision from the SAME acknowledged suspension; observation only, never a grant.</summary>
    public ProviderExecutionContext? CanonicalTaskContext { get; init; }
    public static ChatStreamEvent PermissionRequired(Guid messageId, RemediationRequest actualRequest) =>
        new(ChatStreamEventKind.PermissionRequired, MessageId: messageId) { PermissionRequest = actualRequest };

    /// <summary>
    /// Performs the user step owned by this component.
    /// </summary>
    public static ChatStreamEvent User(ChatMessage message) => new(ChatStreamEventKind.UserMessage, Message: message);
    /// <summary>
    /// Performs the assistant started step owned by this component.
    /// </summary>
    public static ChatStreamEvent AssistantStarted(Guid id, string model, string agent) => new(ChatStreamEventKind.AssistantStarted, MessageId: id, Model: model, Agent: agent);
    /// <summary>
    /// Performs the assistant delta step owned by this component.
    /// </summary>
    public static ChatStreamEvent AssistantDelta(Guid id, string delta) => new(ChatStreamEventKind.AssistantDelta, MessageId: id, Delta: delta);
    /// <summary>
    /// Performs the thinking step owned by this component.
    /// </summary>
    public static ChatStreamEvent ThinkingDelta(Guid id, string thinking) => new(ChatStreamEventKind.ThinkingDelta, MessageId: id, Thinking: thinking);
    /// <summary>
    /// Performs the assistant completed step owned by this component.
    /// </summary>
    public static ChatStreamEvent AssistantCompleted(ChatMessage message) => new(ChatStreamEventKind.AssistantCompleted, Message: message);
    /// <summary>
    /// Performs the preflight step owned by this component.
    /// </summary>
    public static ChatStreamEvent Preflight(CapabilityPreflightResult result) => new(ChatStreamEventKind.PreflightFailed, PreflightResult: result);
    /// <summary>
    /// Performs the activity step owned by this component.
    /// </summary>
    public static ChatStreamEvent Activity(Guid messageId, ToolActivity activity) => new(ChatStreamEventKind.ToolActivity, MessageId: messageId, ToolActivity: activity);
}

/// <summary>
/// Lists the supported chat stream event kind values used to make state explicit and type-safe.
/// </summary>
public enum ChatStreamEventKind { UserMessage, AssistantStarted, AssistantDelta, AssistantCompleted, ThinkingDelta, ToolActivity, PreflightFailed, PermissionRequired }
