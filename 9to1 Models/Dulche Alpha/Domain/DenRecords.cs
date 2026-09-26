using System.Text.Json;
using System.Text.Json.Serialization;

namespace NineToOne.Dulche.Den;

public static class DenJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new PortableMetricConverter());
        return options;
    }
}

public sealed class PortableMetricConverter : JsonConverter<PortableMetric>
{
    public override PortableMetric Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return PortableMetric.FromWireValue(reader.GetString());
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var value)) return PortableMetric.Measured(value);
        throw new JsonException("A portable metric must be an invariant numeric value, empty string, or Na.");
    }

    public override void Write(Utf8JsonWriter writer, PortableMetric value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToWireValue());
}

public enum DenErrorCode
{
    InvalidManifest, UnsupportedSchema, InvalidRecord, NotFound, Forbidden,
    Conflict, IdempotencyMismatch, InvalidArchive, SecretMaterialRejected,
    TemporaryChatMemoryWriteBlocked, RetentionBlocked, PurgeConfirmationRequired,
    CapabilityUnavailable, StorageFailure
}

public sealed class DenException(DenErrorCode code, string message, bool recoverable = false, bool retryable = false)
    : Exception(message)
{
    public DenErrorCode Code { get; } = code;
    public bool Recoverable { get; } = recoverable;
    public bool Retryable { get; } = retryable;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$recordType")]
[JsonDerivedType(typeof(ModelRecord), "model")]
[JsonDerivedType(typeof(MemoryEntry), "memory")]
[JsonDerivedType(typeof(SessionRecord), "session")]
[JsonDerivedType(typeof(ContextRecord), "context")]
[JsonDerivedType(typeof(CheckpointRecord), "checkpoint")]
[JsonDerivedType(typeof(RequestRecord), "request")]
[JsonDerivedType(typeof(ToolActionRecord), "toolAction")]
[JsonDerivedType(typeof(ApprovalRecord), "approval")]
[JsonDerivedType(typeof(ConflictRecord), "conflict")]
[JsonDerivedType(typeof(AgentRunRecord), "agentRun")]
[JsonDerivedType(typeof(SubagentRunRecord), "subagentRun")]
[JsonDerivedType(typeof(QueueItemRecord), "queueItem")]
[JsonDerivedType(typeof(BlockerRecord), "blocker")]
[JsonDerivedType(typeof(RunEventRecord), "runEvent")]
[JsonDerivedType(typeof(BlobReferenceRecord), "blobReference")]
[JsonDerivedType(typeof(StorageQuotaRecord), "storageQuota")]
[JsonDerivedType(typeof(AgentDefinitionRecord), "agentDefinition")]
[JsonDerivedType(typeof(WorkflowDefinitionRecord), "workflowDefinition")]
[JsonDerivedType(typeof(MessageRevisionRecord), "messageRevision")]
[JsonDerivedType(typeof(RequestResultRecord), "requestResult")]
public abstract record DenRecord
{
    public required string Id { get; init; }
    public required string NamespaceId { get; init; }
    public long Revision { get; init; } = 1;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? OriginDeviceId { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public enum MetricAvailability { Measured, Empty, ProviderUnavailable }

public sealed record PortableMetric
{
    public MetricAvailability Availability { get; init; }
    public decimal? Value { get; init; }
    public static PortableMetric Measured(decimal value) => new() { Availability = MetricAvailability.Measured, Value = value };
    public static PortableMetric Empty() => new() { Availability = MetricAvailability.Empty };
    public static PortableMetric Unavailable() => new() { Availability = MetricAvailability.ProviderUnavailable };
    public string ToWireValue() => Availability switch
    {
        MetricAvailability.Empty when Value is null => "",
        MetricAvailability.ProviderUnavailable when Value is null => "Na",
        MetricAvailability.Measured when Value.HasValue => Value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new DenException(DenErrorCode.InvalidRecord, "Metric availability and value do not agree.")
    };
    public static PortableMetric FromWireValue(string? value)
    {
        if (value is null || value.Length == 0) return Empty();
        if (string.Equals(value, "Na", StringComparison.Ordinal)) return Unavailable();
        if (decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) return Measured(parsed);
        throw new DenException(DenErrorCode.InvalidRecord, "Metric must be empty, Na, or an invariant decimal value.");
    }
}

public sealed record ModelRecord : DenRecord
{
    public required string DisplayName { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public string? Provider { get; init; }
    public string? Source { get; init; }
    public required string ArtifactRevision { get; init; }
    public string? ArtifactSha256 { get; init; }
    public string? Family { get; init; }
    public string? Format { get; init; }
    public string? Quantisation { get; init; }
    public IReadOnlyList<string> Modalities { get; init; } = [];
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public long? ContextLimit { get; init; }
    public long? OutputLimit { get; init; }
    public IReadOnlyList<string> ReasoningLevels { get; init; } = [];
    public JsonElement? ConfigurationOverrides { get; init; }
    public IReadOnlyList<string> InstallationReferences { get; init; } = [];
    public IReadOnlyList<ModelReplacement> ReplacementHistory { get; init; } = [];
    public PortableMetric Parameters { get; init; } = PortableMetric.Empty();
    public PortableMetric ActiveMoeParameters { get; init; } = PortableMetric.Empty();
    public PortableMetric StorageBytes { get; init; } = PortableMetric.Empty();
    public PortableMetric HardwareMemoryBytes { get; init; } = PortableMetric.Empty();
}

public sealed record ModelReplacement(string PreviousArtifactRevision, string NewArtifactRevision,
    DateTimeOffset ReplacedAtUtc, string? RetainedSettingsJson, IReadOnlyList<string> ResetSettings);

public enum MemoryScopeKind { User, Project, Agent, AppSurface, Team, Organisation, Custom }
public enum MemoryProvenanceKind { ExplicitUserStatement, Imported, Inference, UserEdited }
public enum MemoryRetentionState { Active, SoftDeleted, Expired, Purged }
public enum MemoryFrequency { Never, Sometimes, Often, Always }

public sealed record MemoryEntry : DenRecord
{
    public required string Content { get; init; }
    public JsonElement? StructuredValue { get; init; }
    public string? SubjectEntity { get; init; }
    public required string Category { get; init; }
    public required MemoryScopeKind ScopeKind { get; init; }
    public string? ScopeId { get; init; }
    public required MemoryProvenanceKind Provenance { get; init; }
    public string? SourceId { get; init; }
    public double? Confidence { get; init; }
    public string? SupersedesId { get; init; }
    public IReadOnlyList<string> SupersededByIds { get; init; } = [];
    public MemoryRetentionState Retention { get; init; } = MemoryRetentionState.Active;
    public DateTimeOffset? RetainUntilUtc { get; init; }
    public bool UserEdited { get; init; }
    public bool Locked { get; init; }
    public bool IsTombstone { get; init; }
}

public sealed record SessionRecord : DenRecord
{
    public required string ConversationId { get; init; }
    public IReadOnlyList<string> MessageRevisionIds { get; init; } = [];
    public IReadOnlyList<string> ContextRecordIds { get; init; } = [];
    public IReadOnlyList<string> RequestIds { get; init; } = [];
    public IReadOnlyList<string> ToolActionIds { get; init; } = [];
    public IReadOnlyList<string> ApprovalIds { get; init; } = [];
    public string? ActiveBranchId { get; init; }
}

public sealed record ContextRecord : DenRecord
{
    public required string ContentReference { get; init; }
    public required string OwningApp { get; init; }
    public DateTimeOffset SourceTimestampUtc { get; init; }
    public required MemoryProvenanceKind Provenance { get; init; }
    public required string AccessScope { get; init; }
    public DateTimeOffset? RetainUntilUtc { get; init; }
    public IReadOnlyList<string> ReferencedSecretIds { get; init; } = [];
    public IReadOnlyList<string> AttachmentIds { get; init; } = [];
}

public sealed record CheckpointRecord : DenRecord
{
    public required string SessionId { get; init; }
    public required string BackendId { get; init; }
    public required string ModelId { get; init; }
    public required string CompatibilityVersion { get; init; }
    public required string ContinuationKind { get; init; }
    public IReadOnlyList<string> CompletedActionIds { get; init; } = [];
    public string? PortableStateJson { get; init; }
}

public sealed record RequestRecord : DenRecord
{
    public required string SessionId { get; init; }
    public required string ProviderPolicy { get; init; }
    public IReadOnlyList<string> ContextSnapshotIds { get; init; } = [];
    public IReadOnlyList<string> CompletedActionIds { get; init; } = [];
    public string? RemainingBudgetJson { get; init; }
    public string Status { get; init; } = "created";
}

public sealed record ToolActionRecord : DenRecord
{
    public required string RequestId { get; init; }
    public required string ActionName { get; init; }
    public required string Status { get; init; }
    public string? ResultJson { get; init; }
    public bool Consequential { get; init; }
}

public sealed record ApprovalRecord : DenRecord
{
    public required string RequestId { get; init; }
    public required string PrincipalId { get; init; }
    public required string ActionScope { get; init; }
    public required string Decision { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}

public sealed record AgentDefinitionRecord : DenRecord
{
    public required string DisplayName { get; init; }
    public required string Version { get; init; }
    public string? Instructions { get; init; }
    public IReadOnlyList<string> ToolIds { get; init; } = [];
    public IReadOnlyList<string> SkillIds { get; init; } = [];
    public IReadOnlyList<string> PluginIds { get; init; } = [];
    public IReadOnlyList<string> McpCapabilityIds { get; init; } = [];
    public IReadOnlyList<string> AllowedPermissions { get; init; } = [];
    public string? ModelPolicyJson { get; init; }
    public string? BudgetJson { get; init; }
    public bool Enabled { get; init; }
}

public sealed record WorkflowDefinitionRecord : DenRecord
{
    public required string DisplayName { get; init; }
    public required string Version { get; init; }
    public required string DefinitionJson { get; init; }
    public string? PermissionManifestJson { get; init; }
}

public sealed record MessageRevisionRecord : DenRecord
{
    public required string ConversationId { get; init; }
    public required string MessageId { get; init; }
    public required string BranchId { get; init; }
    public string? PreviousRevisionId { get; init; }
    public required string AuthorKind { get; init; }
    public required string Content { get; init; }
    public IReadOnlyList<string> ContextRecordIds { get; init; } = [];
    public IReadOnlyList<string> AttachmentReferenceIds { get; init; } = [];
}

public sealed record RequestResultRecord : DenRecord
{
    public required string RequestId { get; init; }
    public required string Status { get; init; }
    public string? ResultJson { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool Retryable { get; init; }
    public IReadOnlyList<string> CompletedActionIds { get; init; } = [];
}

public sealed record ConflictRecord : DenRecord
{
    public required string ObjectId { get; init; }
    public required long BaseRevision { get; init; }
    public required string LocalVersionJson { get; init; }
    public required string RemoteVersionJson { get; init; }
    public string Status { get; init; } = "unresolved";
}

public sealed record AgentRunRecord : DenRecord
{
    public required string AgentDefinitionId { get; init; }
    public required string SessionId { get; init; }
    public required string Status { get; init; }
    public string? ParentRunId { get; init; }
    public IReadOnlyList<string> SubagentRunIds { get; init; } = [];
    public IReadOnlyList<string> QueueItemIds { get; init; } = [];
    public IReadOnlyList<string> CheckpointIds { get; init; } = [];
    public IReadOnlyList<string> BlockerIds { get; init; } = [];
    public IReadOnlyList<string> ToolActionIds { get; init; } = [];
    public IReadOnlyList<string> ApprovalIds { get; init; } = [];
    public string? RemainingBudgetJson { get; init; }
}

public sealed record SubagentRunRecord : DenRecord
{
    public required string AgentRunId { get; init; }
    public required string AgentDefinitionId { get; init; }
    public required string Status { get; init; }
    public string? ParentSubagentRunId { get; init; }
    public IReadOnlyList<string> CheckpointIds { get; init; } = [];
    public IReadOnlyList<string> ToolActionIds { get; init; } = [];
}

public sealed record QueueItemRecord : DenRecord
{
    public required string AgentRunId { get; init; }
    public required string RequestId { get; init; }
    public required long Sequence { get; init; }
    public required string Status { get; init; }
    public string? ReplacesQueueItemId { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
}

public sealed record BlockerRecord : DenRecord
{
    public required string AgentRunId { get; init; }
    public required string Code { get; init; }
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public string? RecoveryAction { get; init; }
    public bool UserActionRequired { get; init; }
}

public sealed record RunEventRecord : DenRecord
{
    public required string AgentRunId { get; init; }
    public required long Sequence { get; init; }
    public required string EventType { get; init; }
    public required string PayloadJson { get; init; }
}

public sealed record BlobReferenceRecord : DenRecord
{
    public required string Sha256 { get; init; }
    public required string MediaType { get; init; }
    public required long Length { get; init; }
    public required string OwnerId { get; init; }
    public required string OwnerKind { get; init; }
    public bool Deleted { get; init; }
}

public sealed record StorageQuotaRecord : DenRecord
{
    public long MetadataBytes { get; init; } = 64L * 1024 * 1024;
    public long JournalBytes { get; init; } = 32L * 1024 * 1024;
    public long CacheBytes { get; init; } = 256L * 1024 * 1024;
    public long AttachmentBytes { get; init; } = 128L * 1024 * 1024;
    public long ModelWeightBytes { get; init; } = 8L * 1024 * 1024 * 1024;
}

public sealed record DenPrincipal(string Id, IReadOnlySet<string> ReadNamespaces,
    IReadOnlySet<string> WriteNamespaces, IReadOnlySet<string> AdminNamespaces);

public enum DenPermission { Read, Write, Execute, Administer }

public sealed record DenAccessRule(string PrincipalId, string NamespaceId, DenPermission Permission,
    IReadOnlySet<string>? ObjectIds = null);

public interface IDenAccessPolicy
{
    ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId,
        DenPermission permission, CancellationToken cancellationToken = default);
}

public sealed class NamespaceAccessPolicy(IEnumerable<DenAccessRule> rules) : IDenAccessPolicy
{
    private readonly DenAccessRule[] _rules = rules.ToArray();
    public ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId,
        DenPermission permission, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var allowed = _rules.Any(rule => rule.PrincipalId == principalId && rule.NamespaceId == namespaceId &&
            (rule.Permission == permission || rule.Permission == DenPermission.Administer) &&
            (rule.ObjectIds is null || rule.ObjectIds.Contains("*") || rule.ObjectIds.Contains(objectId)));
        return ValueTask.FromResult(allowed);
    }
}

public sealed record DenNamespace(string Id, string Kind, bool Shared = false);

public sealed record DenManifest
{
    public required string DenId { get; init; }
    public string DeviceId { get; init; } = Guid.NewGuid().ToString("D");
    public long Revision { get; init; } = 1;
    public int FormatVersion { get; init; } = 1;
    public int SchemaVersion { get; init; } = 1;
    public string MinimumReaderVersion { get; init; } = "1.0";
    public string MinimumWriterVersion { get; init; } = "1.0";
    public IReadOnlyList<DenNamespace> Namespaces { get; init; } = [];
    public IReadOnlyDictionary<string, string> StorageLocations { get; init; } = new Dictionary<string, string>();
    public IReadOnlySet<string> RequiredCapabilities { get; init; } = new HashSet<string>();
    public IReadOnlyDictionary<string, string> OperationReceipts { get; init; } = new Dictionary<string, string>();
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record MemoryPolicyRecord : DenRecord
{
    public MemoryFrequency Frequency { get; init; } = MemoryFrequency.Sometimes;
    public bool BackgroundLearningEnabled { get; init; }
    public IReadOnlyDictionary<string, int?> RetentionDaysByCategory { get; init; } = new Dictionary<string, int?> { ["default"] = 365 };
}

public sealed record DenSearchQuery(string Query, string NamespaceId, MemoryScopeKind? ScopeKind = null,
    string? ScopeId = null, string? Category = null, int Limit = 50);

public sealed record DenOperationResult<T>(bool Success, T? Value, DenErrorCode? ErrorCode = null,
    string? Message = null, bool Recoverable = false, bool Retryable = false)
{
    public static DenOperationResult<T> Ok(T value) => new(true, value);
    public static DenOperationResult<T> Fail(DenException ex) => new(false, default, ex.Code, ex.Message, ex.Recoverable, ex.Retryable);
}

public sealed record DenImportItemResult(string NamespaceId, string RecordId, string Status,
    DenRecord? Record = null, DenErrorCode? ErrorCode = null, string? Message = null,
    bool Recoverable = false, bool Retryable = false);
public sealed record DenImportResult(IReadOnlyList<DenImportItemResult> Items)
{
    public bool Success => Items.All(item => item.ErrorCode is null);
    public IReadOnlyList<DenImportItemResult> Succeeded => Items.Where(item => item.ErrorCode is null).ToArray();
    public IReadOnlyList<DenImportItemResult> Failed => Items.Where(item => item.ErrorCode is not null).ToArray();
}
public sealed record DenStorageUsage(long MetadataBytes, long HistoryBytes, long JournalBytes,
    long AttachmentBytes, long CacheBytes, long ModelWeightBytes,
    StorageQuotaRecord Limits);
