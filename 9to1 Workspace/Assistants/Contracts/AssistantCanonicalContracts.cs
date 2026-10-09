using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Dev;

namespace HavenOS.Apps.Assistants.Contracts;

/// <summary>A known command refusal before dispatch; it does not acknowledge unknown effects.</summary>
public sealed class AssistantCommandRefusedException(string message) : InvalidOperationException(message);

/// <summary>Existing authoritative Den definition identity. These IDs are observations, never grants.</summary>
public sealed record AssistantIdentity(string DenId, string NamespaceId, string DefinitionId);
public enum ConfiguredIdentityKind { Assistant, Specialist }
public enum AssistantConversationKind { Chat, Task }
public enum AssistantSupportState { Available, NotConfigured, RequiresAuthorization, Unsupported, RequiresInspection }
public sealed record AssistantCapabilityObservation(string Feature, AssistantSupportState State, string Reason);

public sealed record AssistantModelPreferences(string? ProviderId = null, string? ModelId = null,
    EffortLevel Effort = EffortLevel.Medium, bool AllowCloud = false, bool AllowFallback = false);
public sealed record AssistantUsageLimits(long? Tokens = null, TimeSpan? Time = null, long? Steps = null,
    long? ToolCalls = null, decimal? Cost = null);
public sealed record AssistantMemoryPreferences(bool Enabled = false, bool IncludeProjectContext = false);
public sealed record AssistantProactivePreferences(bool Enabled = false, bool AllowCheckIns = false,
    bool AllowUnsolicitedConversations = false, IReadOnlyList<string>? EventKinds = null,
    IReadOnlyList<string>? NotificationChannels = null, IReadOnlyList<string>? AutomationIds = null);

/// <summary>Editable preferences. Saved resource/tool IDs do not establish effective access.</summary>
public sealed record AssistantConfiguration
{
    public required string Name { get; init; }
    public string? IconResourceId { get; init; }
    public string Description { get; init; } = "";
    public string Purpose { get; init; } = "";
    public string Role { get; init; } = "";
    public string Instructions { get; init; } = "";
    public AssistantModelPreferences Model { get; init; } = new();
    public IReadOnlyList<string> ToolIds { get; init; } = [];
    public IReadOnlyList<string> ConnectedAppIds { get; init; } = [];
    public IReadOnlyList<string> KnowledgeResourceIds { get; init; } = [];
    public IReadOnlyList<DeveloperProjectReference> ProjectReferences { get; init; } = [];
    public AssistantMemoryPreferences Memory { get; init; } = new();
    public bool ComputerUseRequested { get; init; }
    public string? MiniComputerId { get; init; }
    public AssistantProactivePreferences Proactive { get; init; } = new();
    public string? VoiceId { get; init; }
    public IReadOnlyList<string> Modalities { get; init; } = [];
    public AssistantUsageLimits Limits { get; init; } = new();
    public bool Enabled { get; init; } = true;
    public bool Archived { get; init; }
}

public sealed record AssistantDefinitionSnapshot(AssistantIdentity Identity, long Revision,
    ConfiguredIdentityKind Kind, AssistantConfiguration Configuration,
    IReadOnlyList<AssistantCapabilityObservation> Capabilities);
public sealed record AssistantCatalogueObservation(IReadOnlyList<AssistantDefinitionSnapshot> Definitions,
    IReadOnlyList<AssistantCapabilityObservation> HostCapabilities);
public sealed record AssistantLegacyMigrationCandidate(AssistantIdentity LegacyIdentity, long Revision,
    string Name, string Reason, IReadOnlyList<ConfiguredIdentityKind> CompatibleKinds);

public sealed record AssistantConversationSummary(Guid ConversationId, string Title,
    DateTimeOffset UpdatedAt, bool IsArchived, ProviderExecutionContext? CanonicalTask);
public sealed record AssistantConversationData(Conversation Conversation, IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<MessageAttachment> Attachments, IReadOnlyList<ConversationBranch> Branches,
    ConversationDraft? Draft, TaskExecutionSnapshot? CanonicalTask);
public sealed record AssistantWorkObservation(TaskExecutionSnapshot? CanonicalTask,
    TaskRunOriginalRunControlAvailability? Controls, IReadOnlyList<AssistantCapabilityObservation> Capabilities)
{
    /// <summary>Current READ observation only; the persisted owner's session needs
    /// canonical cold renewal before any live control. This establishes no grant.</summary>
    public bool RequiresOwnerRenewal { get; init; }
}

/// <summary>Privately issued membership binding over an actual persisted canonical conversation.
/// The bridge revalidates current membership, definition revision and actor before every operation.</summary>
public sealed class AssistantConversationBinding
{
    internal AssistantConversationBinding(object issuer, AssistantDefinitionSnapshot definition,
        string denSessionId, long denSessionRevision, Conversation conversation)
    { Issuer = issuer; Definition = definition; DenSessionId = denSessionId;
        DenSessionRevision = denSessionRevision; Conversation = conversation; }
    internal object Issuer { get; }
    internal string DenSessionId { get; }
    internal long DenSessionRevision { get; }
    public AssistantDefinitionSnapshot Definition { get; }
    public Conversation Conversation { get; }
}

public sealed record AssistantTaskInput(string Prompt, ModelDescriptor Model,
    EffortLevel Effort = EffortLevel.Medium, IReadOnlyList<Guid>? AttachmentIds = null,
    string? ProviderId = null);
public sealed record AssistantModelChoice(ModelDescriptor Model, string ProviderId);

/// <summary>Host-owned current model selection, intersecting configuration with canonical governance.</summary>
public interface IAssistantOriginalModelSelectionOwner
{
    Task<IReadOnlyList<AssistantModelChoice>> ReadAvailableOriginalAsync(
        AssistantDefinitionSnapshot definition, Conversation conversation, CancellationToken token);
}

/// <summary>Actual native/browser selected-file owner. A public path alone grants no file access.</summary>
public interface IAssistantOriginalAttachmentOwner
{
    Task<MessageAttachment> ImportOriginalAsync(AssistantConversationBinding binding,
        string selectedPath, Guid? branchId, CancellationToken token);
    Task RemoveOriginalAsync(AssistantConversationBinding binding, Guid attachmentId, CancellationToken token);
}

public sealed record AssistantConversationSendResult(Guid ConversationId, bool ObservationDetached);

/// <summary>Bridge-hosted observation of the SAME ordinary Chat producer, with independently retained close.</summary>
public sealed class AssistantOriginalConversationObservation
{
    private readonly Func<CancellationToken, IAsyncEnumerable<ChatStreamEvent>> _events;
    private readonly Func<CancellationToken, Task<AssistantConversationSendResult>> _wait;
    private readonly Action _requestRetirement;
    private readonly Action _demandExternalJoin;
    private readonly Func<Task> _close;
    private readonly object _closeGate = new();
    private Task? _originalClose;
    internal AssistantOriginalConversationObservation(AssistantConversationBinding binding,
        Func<CancellationToken, IAsyncEnumerable<ChatStreamEvent>> events,
        Func<CancellationToken, Task<AssistantConversationSendResult>> wait, Action requestRetirement,
        Action demandExternalJoin, Func<Task> close)
    { Binding = binding; _events = events; _wait = wait; _requestRetirement = requestRetirement;
        _demandExternalJoin = demandExternalJoin; _close = close; }
    public AssistantConversationBinding Binding { get; }
    public IAsyncEnumerable<ChatStreamEvent> ObserveOriginalEventsAsync(CancellationToken token = default) => _events(token);
    public Task<AssistantConversationSendResult> WaitAsync(CancellationToken token = default) => _wait(token);
    public void RequestRetirement() => _requestRetirement();
    public void DemandExternalOriginalRetirementJoin() => _demandExternalJoin();
    public Task? OriginalClose { get { lock (_closeGate) return _originalClose; } }
    public Task CloseAndDrainAsync()
    {
        _demandExternalJoin();
        lock (_closeGate) return _originalClose ??= _close();
    }
}

/// <summary>Retains the SAME canonical producer observation. Detach joins observers, never cancels business.</summary>
public sealed class AssistantOriginalSendObservation
{
    private readonly object _closeGate = new();
    private Task? _originalClose;
    internal AssistantOriginalSendObservation(AssistantConversationBinding binding,
        TaskRunOriginalInitialChatObservationLease actual)
    { Binding = binding; Original = actual; }
    public AssistantConversationBinding Binding { get; }
    public TaskRunOriginalInitialChatObservationLease Original { get; }
    public IAsyncEnumerable<ChatStreamEvent> ObserveOriginalEventsAsync(CancellationToken token = default) =>
        Original.ObserveOriginalEventsAsync(token);
    public Task<TaskRunInitialChatObservationResult> WaitAsync(CancellationToken token = default) =>
        Original.WaitForOriginalObservationAsync(token);
    public void RequestRetirement() => Original.RequestOriginalObservationRetirement();
    public void DemandExternalOriginalRetirementJoin() => Original.DemandExternalOriginalObservationJoin();
    public Task? OriginalClose { get { lock (_closeGate) return _originalClose; } }
    public Task CloseAndDrainAsync()
    {
        Original.DemandExternalOriginalObservationJoin();
        lock (_closeGate) return _originalClose ??= Original.DetachAndDrainAsync();
    }
}

/// <summary>Owner-issued actual Dev binding, not a project path copied from configuration.</summary>
public sealed class AssistantDevelopmentBinding
{
    internal AssistantDevelopmentBinding(object issuer, AssistantConversationBinding conversation,
        DeveloperResolvedProject project, ProviderExecutionContext canonicalTask, object original)
    { Issuer = issuer; Conversation = conversation; Project = project;
        CanonicalTask = canonicalTask; Original = original; }
    internal object Issuer { get; }
    internal object Original { get; }
    public AssistantConversationBinding Conversation { get; }
    public DeveloperResolvedProject Project { get; }
    public ProviderExecutionContext CanonicalTask { get; }
}

/// <summary>A source-owned native borrower transferred independently of its originating presentation.
/// The issuer reopens current Home/Den membership; this scope never disposes canonical Dev/Task owners.</summary>
public interface IAssistantOriginalDevelopmentCustody
{
    void RequestRetirement();
    void DemandExternalOriginalRetirementJoin();
    Task CloseAndDrainAsync();
}
public sealed record AssistantDevelopmentCurrentObservation(DeveloperResolvedProject Project,
    TaskExecutionSnapshot CanonicalTask, AuthenticatedResourceActor Actor)
{
    // Observations only: the privately issued binding and current source reads own
    // admission. Keep positional Actor as the existing Task-actor projection.
    public AuthenticatedResourceActor TaskActor => Actor;
    public AuthenticatedResourceActor? HomeActor { get; init; }
}

/// <summary>Trusted host adapter over the SAME Dev/resource owners. Implementations privately issue
/// bindings; public IDs and interface implementations cannot mint project or effect authorization.</summary>
public interface IAssistantOriginalDevelopmentOwner
{
    Task<AssistantDevelopmentBinding> OpenOriginalAsync(AssistantConversationBinding conversation,
        DeveloperProjectReference reference, ProviderExecutionContext expectedTask, CancellationToken token);
    bool IsIssuedOriginalBinding(AssistantDevelopmentBinding binding);
    Task<IAssistantOriginalDevelopmentCustody> TransferOriginalWithinSourceAsync(AssistantDevelopmentBinding binding,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalCustody(AssistantDevelopmentBinding binding, IAssistantOriginalDevelopmentCustody custody);
    Task<AssistantDevelopmentCurrentObservation> ValidateOriginalBindingWithinSourceAsync(AssistantDevelopmentBinding binding,
        IAssistantOriginalDevelopmentCustody custody, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    Task<TaskExecutionSnapshot> ValidateOriginalBindingWithinSourceAsync(AssistantDevelopmentBinding binding,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    Task<DeveloperOperationResult<DeveloperActionObservation>> ReadOriginalAsync(AssistantDevelopmentBinding binding,
        DeveloperCanonicalActionContext action, DeveloperCodeDocument document, CancellationToken token);
    Task<DeveloperOperationResult<DeveloperActionObservation>> ApplyOriginalAsync(AssistantDevelopmentBinding binding,
        DeveloperCanonicalActionContext action, DeveloperReviewedTextEdit edit, CancellationToken token);
    Task<DeveloperOperationResult<DeveloperActionObservation>> RunTestsOriginalAsync(AssistantDevelopmentBinding binding,
        DeveloperCanonicalActionContext action, string command, int timeoutSeconds, CancellationToken token);
}

/// <summary>Assistant/Specialist product API over canonical Den and Chat/Task owners.
/// It creates no Space, model router, tool engine, Task database or permission broker.</summary>
public interface IAssistantCanonicalBridge
{
    Task<AssistantCatalogueObservation> ListAsync(CancellationToken token = default);
    Task<AssistantDefinitionSnapshot> GetAsync(AssistantIdentity identity, CancellationToken token = default);
    Task<AssistantDefinitionSnapshot> CreateAsync(ConfiguredIdentityKind kind, AssistantConfiguration configuration,
        Guid operationId, CancellationToken token = default);
    Task<AssistantDefinitionSnapshot> UpdateAsync(AssistantIdentity identity, long expectedRevision,
        AssistantConfiguration configuration, Guid operationId, CancellationToken token = default);
    Task<IReadOnlyList<AssistantLegacyMigrationCandidate>> ReadLegacyMigrationCandidatesAsync(CancellationToken token = default);
    Task<AssistantDefinitionSnapshot> ResolveLegacyDefinitionAsync(AssistantIdentity legacyIdentity,
        long expectedRevision, ConfiguredIdentityKind confirmedKind, AssistantConfiguration configuration,
        Guid operationId, CancellationToken token = default);
    Task<IReadOnlyList<AssistantConversationSummary>> ReadConversationsAsync(AssistantIdentity identity,
        int maximum = 100, CancellationToken token = default);
    Task<AssistantConversationBinding> CreateConversationAsync(AssistantIdentity identity, long expectedDefinitionRevision,
        Guid conversationId, string title, Guid operationId, CancellationToken token = default,
        AssistantConversationKind kind = AssistantConversationKind.Chat);
    Task<AssistantOriginalProjectCatalogue> ReadOriginalProjectCandidatesAsync(int maximum, CancellationToken token = default) =>
        Task.FromException<AssistantOriginalProjectCatalogue>(new AssistantCommandRefusedException("The actual canonical project catalogue owner is not composed."));
    Task<AssistantOriginalProjectChoice> AuthorizeOriginalProjectChoiceAsync(AssistantOriginalProjectCandidate candidate,
        CancellationToken token = default) => Task.FromException<AssistantOriginalProjectChoice>(
            new AssistantCommandRefusedException("The actual Home project READ selection owner is not composed."));
    Task<AssistantConversationBinding> CreateProjectConversationOriginalAsync(AssistantIdentity identity,
        long expectedDefinitionRevision, Guid conversationId, string title, Guid operationId,
        AssistantOriginalProjectChoice actualChoice, CancellationToken token = default) =>
        Task.FromException<AssistantConversationBinding>(new AssistantCommandRefusedException("The canonical authorized project conversation owner is not composed."));
    Task<AssistantConversationBinding> OpenConversationAsync(AssistantIdentity identity, Guid conversationId,
        CancellationToken token = default);
    bool IsIssuedOriginalBinding(AssistantConversationBinding binding);
    Task<AssistantConversationData> ReadConversationAsync(AssistantConversationBinding binding, CancellationToken token = default);
    Task<IReadOnlyList<AssistantModelChoice>> ListAvailableModelsAsync(AssistantConversationBinding binding,
        CancellationToken token = default);
    Task SaveConversationDraftAsync(AssistantConversationBinding binding, Guid? branchId,
        string content, IReadOnlyList<Guid> attachmentIds, CancellationToken token = default);
    Task<MessageAttachment> ImportAttachmentOriginalAsync(AssistantConversationBinding binding,
        string selectedPath, Guid? branchId, CancellationToken token = default);
    Task RemoveAttachmentOriginalAsync(AssistantConversationBinding binding, Guid attachmentId,
        CancellationToken token = default);
    Task<ConversationBranch> CreateBranchAsync(AssistantConversationBinding binding, Guid messageId,
        string? name, CancellationToken token = default);
    Task SwitchBranchAsync(AssistantConversationBinding binding, Guid branchId, CancellationToken token = default);
    Task<AssistantOriginalConversationObservation> SendConversationOriginalAsync(AssistantConversationBinding binding,
        AssistantTaskInput input, CancellationToken token = default);
    Task<AssistantWorkObservation> ReadWorkAsync(AssistantConversationBinding binding, CancellationToken token = default);
    Task<AssistantOriginalSendObservation> StartOriginalTaskAsync(AssistantConversationBinding binding,
        AssistantTaskInput input, CancellationToken token = default);
    Task<FollowUpDecision> SubmitFollowUpAsync(AssistantConversationBinding binding, ProviderExecutionContext expectedTask,
        string instruction, TaskFollowUpMode mode, CancellationToken token = default);
    Task<TaskRunOriginalRunControlResult> ControlOriginalRunAsync(AssistantConversationBinding binding,
        ProviderExecutionContext expectedTask, TaskRunOriginalRunControlKind kind, CancellationToken token = default);
    Task<TaskRunOriginalResumeObservationLease> StartObservedOriginalResumeAsync(AssistantConversationBinding binding,
        ProviderExecutionContext expectedTask, CancellationToken token = default);
    Task<AssistantDevelopmentBinding> OpenDevelopmentOriginalAsync(AssistantConversationBinding binding,
        DeveloperProjectReference reference, ProviderExecutionContext expectedTask, CancellationToken token = default);
    void RequestRetirement();
    void DemandExternalOriginalRetirementJoin();
    Task CloseAndDrainAsync();
}
