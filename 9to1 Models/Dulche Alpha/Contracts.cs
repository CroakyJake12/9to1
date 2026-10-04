using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dulche.Runtime;

public enum Availability { Value, Empty, Unavailable }
public readonly record struct Metric<T>(Availability Availability, T? Value = default)
{
    public static Metric<T> Measured(T value) => new(Availability.Value, value);
    public static Metric<T> Empty => new(Availability.Empty);
    public static Metric<T> Na => new(Availability.Unavailable);
    public string AvailabilityLabel => Availability switch { Availability.Empty => "null", Availability.Unavailable => "Na", _ => "value" };
}

public sealed class MetricJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Metric<>);
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(MetricJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
    private sealed class MetricJsonConverter<TValue> : JsonConverter<Metric<TValue>>
    {
        public override Metric<TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return Metric<TValue>.Empty;
            if (reader.TokenType == JsonTokenType.String && reader.GetString() == "Na") return Metric<TValue>.Na;
            return Metric<TValue>.Measured(JsonSerializer.Deserialize<TValue>(ref reader, options)!);
        }
        public override void Write(Utf8JsonWriter writer, Metric<TValue> value, JsonSerializerOptions options)
        {
            if (value.Availability == Availability.Empty) writer.WriteNullValue();
            else if (value.Availability == Availability.Unavailable) writer.WriteStringValue("Na");
            else JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}

public enum EndpointState { Starting, Loading, Ready, Busy, Pausing, Paused, Stopping, Stopped, Failed }
public enum ModelState { Downloading, Installed, Verifying, Verified, Failed }
public enum RequestState { Queued, Running, Pausing, Paused, Stopping, Completed, Cancelled, Replaced, Blocked, Failed }
public enum FinishReason { Completed, Cancelled, Replaced, Blocked, Error, OutputLimit, Refused }
public enum DulcheErrorCode
{
    ModelNotFound, MissingPrompt, EndpointNotFound, PortInUse, InvalidArgument, OutOfMemory, ContextExceeded,
    ProviderUnavailable, AuthenticationFailed, PermissionDenied, UnsupportedCapability, ToolFailed, RequestNotFound,
    InvalidState, QueueFull, Conflict, ModelLoadFailed,
    LanguageUnsupported, LanguageAmbiguous, LocaleUnsupported, TranslationModelUnavailable,
    TranslationCapabilityUnavailable, GlossaryUnavailable, SourceUnavailable, SourceRevisionConflict,
    SegmentAlignmentUnavailable, VariantNotFound, TranslationSetNotFound, TranslationJobNotFound,
    TranslationJobNotResumable, StructuredFormatUnsupported, LayoutPreservationUnavailable,
    PartialTranslationFailure, PrivacyPolicyDenied, VoiceSessionNotFound, VoiceSessionAlreadyActive,
    VoiceSessionNotResumable, VoiceInputUnavailable, VoiceOutputUnavailable, VoiceDeviceUnavailable,
    VoiceModelUnavailable, VoiceContextUnavailable, TranscriptRetentionDenied, InvalidPersistedState, AuditUnavailable
}

public sealed record DulcheError(DulcheErrorCode Code, string Message, string Target, bool Retryable, string? RetryAfter = null, IReadOnlyDictionary<string, string>? Details = null);
public sealed record OperationResult<T>(T? Value, DulcheError? Error)
{
    public bool Succeeded => Error is null;
    public static OperationResult<T> Success(T value) => new(value, null);
    public static OperationResult<T> Failure(DulcheError error) => new(default, error);
}

public sealed record ModelIdentity(string ProviderId, string ModelId, string? ArtifactRevision = null)
{
    public string StableKey => $"{ProviderId}:{ModelId}:{ArtifactRevision ?? "current"}";
}
public sealed record GenerationSettings(double? Temperature = null, int? MaximumOutputTokens = null, double? TopP = null, int? TopK = null, int? Seed = null, IReadOnlyList<string>? StopSequences = null, IReadOnlyDictionary<string, double>? Penalties = null, string? ReasoningLevel = null, string? OutputSchema = null);
public sealed record GenerativeContainerCapability(IReadOnlySet<string> Schemas, IReadOnlySet<string> Components, IReadOnlySet<string> ActionIds, int MaximumComponents = 100, int MaximumPayloadBytes = 65536);
public sealed record GeneratedUiActionBinding(string ActionId, System.Text.Json.JsonElement Arguments);
public sealed record GeneratedUiComponent(string ComponentId, string ComponentType, System.Text.Json.JsonElement Properties, IReadOnlyList<GeneratedUiActionBinding>? Actions = null);
public sealed record GeneratedUiDocument(int SchemaVersion, string Schema, IReadOnlyList<GeneratedUiComponent> Components);
public sealed record GeneratedUiPayload(GeneratedUiDocument Document, bool Renderable, IReadOnlyList<string> Warnings);
public sealed record DulcheMessage(string Role, string? Text = null, IReadOnlyList<AuthorizedInput>? Inputs = null);
public sealed record AuthorizedInput(string Kind, string Reference, string? PermissionScope);
public enum ContextSensitivity { Public, Private, Unknown }
public sealed record DulcheRequest(string? Input, IReadOnlyList<DulcheMessage>? Messages = null, string? SessionId = null, ModelIdentity? Model = null, GenerationSettings? Settings = null, IReadOnlySet<string>? PermittedTools = null, string? OutputSchema = null, bool Stream = false, int? ContextLimit = null, int? QueueTimeoutSeconds = null, string? CallerId = null, bool AllowCloudContext = false, ToolCallPolicy? ToolPolicy = null, ExecutionBudget? Budget = null, string? IdempotencyKey = null, GenerativeContainerCapability? GenerativeContainer = null, bool IsContinuation = false, ContextSensitivity ContextSensitivity = ContextSensitivity.Unknown)
{
    public string? EffectivePrompt => Input ?? (Messages is { Count: > 0 } ? string.Join("\n", Messages.Select(message => message.Text).Where(text => text is not null)) : null);
}
public enum ToolCallMode { None, ReadOnly, Selected, AllAuthorized }
public sealed record ToolCallPolicy(ToolCallMode Mode, IReadOnlySet<string> AllowedTools, string CallerId, string ScopeId, bool RequireApprovalForConsequential = true);
public sealed record ToolProposal(string InvocationId, string Name, System.Text.Json.JsonElement Arguments, bool IsConsequential = false, string? Version = null);
public enum ToolInvocationStatus { Proposed, ApprovalRequired, Approved, Denied, Executed, Failed, Cancelled }
public sealed record ToolInvocationResult(string InvocationId, ToolInvocationStatus Status, System.Text.Json.JsonElement? Result, DulcheError? Error, bool SideEffectCompleted, bool Retryable);
public sealed record ToolExecutionContext(string RequestId, int Revision, string AttemptId, string SessionId, string EndpointId, ModelIdentity? Model, string CallerId, IReadOnlyList<DulcheMessage> SessionContext, ToolCallPolicy Policy, CancellationToken CancellationToken);
public sealed record ExecutionBudget(int MaximumSteps = 32, int MaximumDurationSeconds = 300, long? MaximumOutputTokens = null, decimal? MaximumCost = null, string? Currency = null);
public interface IDulcheToolCoordinator
{
    Task<OperationResult<ToolInvocationResult>> ExecuteAsync(ToolProposal proposal, ToolExecutionContext context, CancellationToken cancellationToken);
}
public interface IRuntimeRequestAuthorizer
{
    Task<OperationResult<Unit>> AuthorizeRemoteContextAsync(DulcheRequest request, DulcheEndpoint endpoint, CancellationToken cancellationToken);
    Task<OperationResult<Unit>> RecheckBeforeDispatchAsync(DulcheRequest request, DulcheEndpoint endpoint, CancellationToken cancellationToken);
    Task<OperationResult<Unit>> AuthorizeToolAsync(ToolProposal proposal, ToolExecutionContext context, CancellationToken cancellationToken);
}

public sealed record DulcheSession(string SessionId, string EndpointId, long Revision, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, IReadOnlyList<DulcheMessage> Messages);
public sealed record DulcheEndpoint(string EndpointId, string ProviderId, string Target, int? Port, EndpointState State, ModelIdentity? Model, IReadOnlySet<string> Capabilities, bool IsRemote, DateTimeOffset UpdatedAt, DulcheError? LastError = null, RemoteEndpointTarget? RemoteTarget = null);
public sealed record TokenMetrics(Metric<long> Input, Metric<long> Output, Metric<long> Total, Metric<long> Response, Metric<long> Agentic, Metric<long> Thinking, Metric<long> Cached);
public sealed record ModelInvocation(ModelIdentity Model, string Role, int CallCount, string EndpointId, Metric<long> Parameters, Metric<long> ActiveParameters, TokenMetrics Tokens);
public sealed record DulcheResult(string RequestId, string SessionId, string EndpointId, RequestState Status, FinishReason? FinishReason, int Revision, string AttemptId, string? Text, Metric<string> Thinking, IReadOnlyList<string>? Tools, IReadOnlyList<string>? Sources, TokenMetrics Tokens, Metric<double> TimeElapsedSeconds, Metric<double> TimeQueuedSeconds, Metric<double> TimePausedSeconds, Metric<double> TimeExecutingSeconds, Metric<double> TimeToFirstOutputSeconds, Metric<double> TokensPerSecond, IReadOnlyList<ModelInvocation> Models, IReadOnlyList<DulcheError> Errors, IReadOnlyList<RuntimeEvent> FullLog, DulcheError? Error = null, GeneratedUiPayload? GeneratedUI = null);
public sealed record DulcheQueueItem(string RequestId, string SessionId, RequestState State, int Position, DateTimeOffset AcceptedAt, int? TimeoutSeconds);
public sealed record DulcheQueueSnapshot(string EndpointId, string? RunningRequestId, IReadOnlyList<DulcheQueueItem> Queued, int Capacity, DateTimeOffset CapturedAt);
public sealed record RemoteEndpointTarget(string ProviderId, Uri BaseUri, ModelIdentity Model, string? CredentialReference, IReadOnlyDictionary<string, string> TransportSettings, IReadOnlySet<string> AdvertisedCapabilities);
public sealed record RuntimeEvent(string EventId, DateTimeOffset At, string RequestId, int Revision, string AttemptId, long Sequence, string Type, string? Detail = null, string? ParentEventId = null, System.Text.Json.JsonElement? Payload = null);
public sealed record RuntimeCapabilities(string RuntimeVersion, string ApiVersion, string SchemaVersion, IReadOnlySet<string> Capabilities, int MaximumQueueDepth, bool SupportsExactPause, bool SupportsExactResume, bool SupportsStreaming, bool SupportsCancellation);
public sealed record ProviderPolicy(bool AllowLocal = true, bool AllowRemote = true, bool AllowCloud = false, bool AllowFallback = true, bool AllowPrivateContextToCloud = false, IReadOnlySet<string>? AllowedProviders = null, IReadOnlySet<string>? RequiredCapabilities = null);
public sealed record ModelRoute(string RouteId, int Version, IReadOnlyList<ModelIdentity> Candidates, ProviderPolicy Policy);
public sealed record RouteSelection(ModelIdentity Model, string Reason, IReadOnlyList<string> Skipped);
public enum ModelRouteScope { User, App, Agent, Task }
public enum ModelCapabilityCategory { Active, Background, Chat, Image, Voice, Audio, Video }
public sealed record ModelRouteCandidate(ModelIdentity Model, bool Enabled = true, int Order = 0);
public sealed record ConfiguredModelRoute(string RouteId, long Revision, ModelRouteScope Scope, string ScopeId, ModelCapabilityCategory Category, IReadOnlyList<ModelRouteCandidate> Candidates, ProviderPolicy Policy);
public sealed record ModelCatalogueEntry(ModelIdentity Identity, string DisplayName, string ProviderName, bool IsLocal, IReadOnlySet<string> Capabilities, int? ContextWindow, ModelState? State, Metric<long> StorageBytes, string? PrivacyResidency = null, string? Alias = null);
public sealed record ModelRouteResolutionPreview(ConfiguredModelRoute Route, RouteSelection? Selection, IReadOnlyList<string> Trace, DulcheError? Error);
public sealed record RouteUpdateResult(ConfiguredModelRoute? Route, DulcheError? Error);
public interface IVersionedModelRouteRepository
{
    Task<ConfiguredModelRoute?> GetAsync(string routeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ConfiguredModelRoute>> ListAsync(CancellationToken cancellationToken);
    Task<bool> TrySaveAsync(ConfiguredModelRoute route, long expectedRevision, CancellationToken cancellationToken);
}
public sealed record RuntimeHealth(string EndpointId, EndpointState State, DateTimeOffset CheckedAt, string? Message, Metric<double> MemoryBytes, Metric<double> StorageBytes, Metric<double> RateLimitRemaining);

public interface IDulcheAdapter
{
    string ProviderId { get; }
    string RuntimeVersion { get; }
    bool IsLocal { get; }
    IReadOnlySet<string> Capabilities { get; }
    bool SupportsExactPause => Capabilities.Contains("response.pause.exact");
    bool SupportsExactResume => Capabilities.Contains("response.resume.exact");
    ValueTask<OperationResult<Unit>> StartAsync(DulcheEndpoint endpoint, CancellationToken cancellationToken);
    ValueTask<OperationResult<Unit>> LoadModelAsync(DulcheEndpoint endpoint, ModelIdentity model, CancellationToken cancellationToken) => ValueTask.FromResult(OperationResult<Unit>.Failure(new(DulcheErrorCode.UnsupportedCapability, "Adapter does not expose model loading.", model.StableKey, false)));
    IAsyncEnumerable<AdapterDelta> GenerateAsync(DulcheEndpoint endpoint, DulcheRequest request, string requestId, CancellationToken cancellationToken);
    IAsyncEnumerable<AdapterDelta> ContinueWithToolResultAsync(DulcheEndpoint endpoint, DulcheRequest request, string requestId, ToolInvocationResult result, CancellationToken cancellationToken);
    ValueTask<OperationResult<Unit>> CancelAsync(DulcheEndpoint endpoint, string requestId, CancellationToken cancellationToken);
    ValueTask<OperationResult<Unit>> PauseAsync(DulcheEndpoint endpoint, string requestId, CancellationToken cancellationToken);
    ValueTask<OperationResult<Unit>> ResumeAsync(DulcheEndpoint endpoint, string requestId, CancellationToken cancellationToken);
    ValueTask<OperationResult<Unit>> StopAsync(DulcheEndpoint endpoint, CancellationToken cancellationToken);
    ValueTask<RuntimeHealth> HealthAsync(DulcheEndpoint endpoint, CancellationToken cancellationToken);
}
public sealed record Unit { public static Unit Value { get; } = new(); private Unit() { } }
public sealed record AdapterDelta(string? Text = null, string? Thinking = null, string? Type = null, string? Detail = null, long? InputTokens = null, long? OutputTokens = null, string? FinishReason = null, ToolProposal? ToolProposal = null, GeneratedUiDocument? GeneratedUI = null);
public sealed record ModelArtifact(ModelIdentity Identity, string Source, string Format, long? DeclaredSizeBytes, string? License, string? SourceRevision, string? ArtifactHash, ModelState State, string? PersistentLocation = null, string? OwnerScopeId = null);
public sealed record AcquisitionProgress(string OperationId, ModelIdentity Model, ModelState State, double? ProgressPercent, long? DownloadedBytes, long? TotalBytes, string? RequiredStoragePath, string? Message, DulcheError? Error);
public sealed record ModelReplaceResult(ModelArtifact Previous, ModelArtifact Current, string PreservationPolicy, IReadOnlyList<string> PreservedSettings, IReadOnlyList<string> ResetSettings, string RollbackId);
public interface IDulcheAcquisitionAdapter
{
    string ProviderId { get; }
    Task<IReadOnlyList<ModelArtifact>> ListModelsAsync(CancellationToken cancellationToken);
    Task<OperationResult<ModelArtifact>> FindModelAsync(string query, CancellationToken cancellationToken);
    Task<OperationResult<AcquisitionProgress>> PullAsync(ModelArtifact model, string ownerScopeId, IProgress<AcquisitionProgress>? progress, CancellationToken cancellationToken);
    Task<OperationResult<ModelArtifact>> InstallAsync(ModelArtifact model, string installLocation, CancellationToken cancellationToken);
    Task<OperationResult<ModelReplaceResult>> ReplaceAsync(ModelArtifact oldModel, ModelArtifact replacement, string policy, CancellationToken cancellationToken);
    Task<OperationResult<ModelArtifact>> RollbackAsync(string rollbackId, CancellationToken cancellationToken);
    Task ReleaseTemporaryOwnershipAsync(ModelIdentity model, string ownerScopeId, CancellationToken cancellationToken);
}

public static class ReasoningLevelMapper
{
    public static OperationResult<string> FromPercentage(double percentage, IReadOnlyList<string> supportedLevels)
    {
        if (double.IsNaN(percentage) || double.IsInfinity(percentage) || percentage is < 0 or > 100)
            return OperationResult<string>.Failure(new(DulcheErrorCode.InvalidArgument, "Reasoning percentage must be between 0 and 100.", "reasoningPercentage", false));
        if (supportedLevels is null || supportedLevels.Count == 0)
            return OperationResult<string>.Failure(new(DulcheErrorCode.UnsupportedCapability, "Model does not advertise reasoning levels.", "reasoningLevels", false));
        var index = Math.Min(supportedLevels.Count - 1, (int)Math.Floor(percentage * supportedLevels.Count / 100d));
        return OperationResult<string>.Success(supportedLevels[index]);
    }

    public static OperationResult<string> ResolveNamed(string name, IReadOnlyList<string> supportedLevels) =>
        supportedLevels is not null && supportedLevels.FirstOrDefault(level => StringComparer.OrdinalIgnoreCase.Equals(level, name)) is { } actual
            ? OperationResult<string>.Success(actual)
            : OperationResult<string>.Failure(new(DulcheErrorCode.UnsupportedCapability, "Requested reasoning level is not advertised by this model.", name, false));
}

public interface IRuntimeEventStream
{
    IAsyncEnumerable<RuntimeEvent> ReadAsync(long afterSequence = 0, CancellationToken cancellationToken = default);
}

public sealed class RuntimeEventHub
{
    private readonly ConcurrentDictionary<string, Channel<RuntimeEvent>> _channels = new(StringComparer.Ordinal);
    public void Publish(string requestId, RuntimeEvent value)
    {
        var channel = _channels.GetOrAdd(requestId, _ => Channel.CreateBounded<RuntimeEvent>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.Wait, SingleReader = false, SingleWriter = false }));
        if (!channel.Writer.TryWrite(value))
        {
            while (channel.Reader.TryRead(out _)) { }
            channel.Writer.TryWrite(value with { Type = "SnapshotRequired", Detail = "Event buffer overflow; fetch current request snapshot before continuing." });
        }
    }
    public async IAsyncEnumerable<RuntimeEvent> ReadAsync(string requestId, long afterSequence = 0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = _channels.GetOrAdd(requestId, _ => Channel.CreateBounded<RuntimeEvent>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.Wait, SingleReader = false, SingleWriter = false }));
        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            if (item.Sequence > afterSequence) yield return item;
    }
}
