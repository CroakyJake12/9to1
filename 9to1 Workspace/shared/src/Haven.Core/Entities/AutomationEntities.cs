namespace Haven.Core;

/// <summary>
/// Represents an automation definition.
/// </summary>
public sealed record AutomationDefinition(
    Guid Id,
    string Name,
    HavenMode Mode,
    string Instruction,
    AutomationScheduleKind ScheduleKind,
    string ScheduleJson,
    DateTimeOffset? NextRunAt,
    Guid? ContainerId,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    // These persisted descriptors are evidence to revalidate, never an execution grant.
    public long Revision { get; init; }
    public AutomationOwnerBinding? OwnerBinding { get; init; }
    public AutomationDefinitionGraphBinding? GraphBinding { get; init; }
    public AutomationDefinitionMetadata? Metadata { get; init; }
    public AutomationOperationalState OperationalState { get; init; } = AutomationOperationalState.NeedsAttention;
    public DateTimeOffset? ArchivedAt { get; init; }
    public AutomationGraphPublicationJournal? PublicationJournal { get; init; }
    public AutomationOwnerCommitReceipt? LastOwnerCommit { get; init; }
}


/// <summary>
/// Represents an automation run.
/// </summary>
public sealed record AutomationRun(
    Guid Id,
    Guid AutomationId,
    AutomationRunStatus Status,
    DateTimeOffset ScheduledFor,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? Result,
    string? Error,
    string? LeaseToken)
{
    public long Revision { get; init; }
    public long? DefinitionRevision { get; init; }
    public AutomationGraphBinding? PinnedGraph { get; init; }
    public string? AdmissionSnapshotJson { get; init; }
    public string? ContinuationDescriptorJson { get; init; }
    public AutomationRunDetails? Details { get; init; }
}

/// <summary>The owning repository issues this binding only after observing its actual durable store and current actor.</summary>
public sealed record AutomationOwnerBinding(Guid StoreId, string ProfileId, string ActorId, string AuthenticationRevision,
    Guid? AccountId, Guid? OrganisationId);
public sealed record AutomationDefinitionGraphBinding(Guid GraphId, long DraftRevision, long? ActiveRevision);
public sealed record AutomationGraphBinding(Guid GraphId, long Revision);

/// <summary>Recovery coordinates publication in separate SQL and Home stores; this record contains no live capability.</summary>
public sealed record AutomationGraphPublicationJournal(Guid OperationId, long ExpectedDefinitionRevision,
    Guid GraphId, long? PublishedDraftRevision, long? PublishedActiveRevision, AutomationGraphPublicationPhase Phase,
    string OriginalPayloadSha256, string? RecoveryReason);


/// <summary>Detached definition data. Lists/configuration are cloned by the owning admission, not treated as capabilities.</summary>
public sealed record AutomationDefinitionMetadata(string? Description, string? Icon, string? InputSchemaJson,
    string? OutputSchemaJson, IReadOnlyList<AutomationTriggerDefinition> Triggers,
    IReadOnlyList<string> RequiredCapabilities, AutomationExecutionPolicy ExecutionPolicy);
public sealed record AutomationTriggerDefinition(Guid TriggerId, AutomationTriggerKind Kind, string ConfigurationJson, bool Enabled);
public sealed record AutomationExecutionPolicy(int MaximumNodeExecutions, int MaximumDurationSeconds,
    int MaximumActionRetries, int MaximumConcurrentRuns, int MaximumCausalDepth, long? TokenBudget,
    bool ContinueOnNodeError, string RunAsPolicy);

/// <summary>Historical typed state retains exact trigger identity, data availability and owning audit references.</summary>
public sealed record AutomationRunDetails(Guid? TriggerId, string? TriggerEventId, string TypedInputsJson,
    string? TypedOutputsJson, string NodeExecutionStatesJson, string? ErrorDescriptorJson,
    string CausalAncestryJson, string AuditReferencesJson, Guid? ActionGraphId);

/// <summary>Actual same-transaction definition receipt. Supersession is unconfirmed outcome, never replay permission.</summary>
public sealed record AutomationOwnerCommitReceipt(int SchemaVersion, Guid StoreId, Guid EntityId,
    AutomationDefinitionEntityKind EntityKind, Guid OperationId, string PayloadSha256,
    long ExpectedRevision, long CommittedRevision, DateTimeOffset CommittedAt,
    AutomationOwnerBinding ObservedOwner);
