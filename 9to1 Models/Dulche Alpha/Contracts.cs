using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
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
    VoiceModelUnavailable, VoiceContextUnavailable, TranscriptRetentionDenied, InvalidPersistedState
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
public sealed record DulcheMessage(string Role, string? Text = null, IReadOnlyList<AuthorizedInput>? Inputs = null);
public sealed record AuthorizedInput(string Kind, string Reference, string? PermissionScope);
public sealed record DulcheRequest(string? Input, IReadOnlyList<DulcheMessage>? Messages = null, string? SessionId = null, ModelIdentity? Model = null, GenerationSettings? Settings = null, IReadOnlySet<string>? PermittedTools = null, string? OutputSchema = null, bool Stream = false, int? ContextLimit = null, int? QueueTimeoutSeconds = null, string? CallerId = null, bool AllowCloudContext = false)
{
    public string? EffectivePrompt => Input ?? (Messages is { Count: > 0 } ? string.Join("\n", Messages.Select(message => message.Text).Where(text => text is not null)) : null);
}

public sealed record DulcheSession(string SessionId, string EndpointId, long Revision, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, IReadOnlyList<DulcheMessage> Messages);
public sealed record DulcheEndpoint(string EndpointId, string ProviderId, string Target, int? Port, EndpointState State, ModelIdentity? Model, IReadOnlySet<string> Capabilities, bool IsRemote, DateTimeOffset UpdatedAt, DulcheError? LastError = null);
public sealed record TokenMetrics(Metric<long> Input, Metric<long> Output, Metric<long> Total, Metric<long> Response, Metric<long> Agentic, Metric<long> Thinking, Metric<long> Cached);
public sealed record ModelInvocation(ModelIdentity Model, string Role, int CallCount, string EndpointId, Metric<long> Parameters, Metric<long> ActiveParameters, TokenMetrics Tokens);
public sealed record DulcheResult(string RequestId, string SessionId, string EndpointId, RequestState Status, FinishReason? FinishReason, int Revision, string AttemptId, string? Text, Metric<string> Thinking, IReadOnlyList<string>? Tools, IReadOnlyList<string>? Sources, TokenMetrics Tokens, Metric<double> TimeElapsedSeconds, Metric<double> TimeToFirstOutputSeconds, Metric<double> TokensPerSecond, IReadOnlyList<ModelInvocation> Models, IReadOnlyList<DulcheError> Errors, IReadOnlyList<RuntimeEvent> FullLog, DulcheError? Error = null);
public sealed record RuntimeEvent(string EventId, DateTimeOffset At, string RequestId, int Revision, string AttemptId, long Sequence, string Type, string? Detail = null, string? ParentEventId = null);
public sealed record RuntimeCapabilities(string RuntimeVersion, string ApiVersion, string SchemaVersion, IReadOnlySet<string> Capabilities, int MaximumQueueDepth, bool SupportsExactPause, bool SupportsExactResume, bool SupportsStreaming, bool SupportsCancellation);
public sealed record ProviderPolicy(bool AllowLocal = true, bool AllowRemote = true, bool AllowCloud = false, bool AllowFallback = true, bool AllowPrivateContextToCloud = false, IReadOnlySet<string>? AllowedProviders = null, IReadOnlySet<string>? RequiredCapabilities = null);
public sealed record ModelRoute(string RouteId, int Version, IReadOnlyList<ModelIdentity> Candidates, ProviderPolicy Policy);
public sealed record RouteSelection(ModelIdentity Model, string Reason, IReadOnlyList<string> Skipped);
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
    ValueTask<OperationResult<Unit>> CancelAsync(DulcheEndpoint endpoint, string requestId, CancellationToken cancellationToken);
    ValueTask<OperationResult<Unit>> PauseAsync(DulcheEndpoint endpoint, string requestId, CancellationToken cancellationToken);
    ValueTask<OperationResult<Unit>> ResumeAsync(DulcheEndpoint endpoint, string requestId, CancellationToken cancellationToken);
    ValueTask<OperationResult<Unit>> StopAsync(DulcheEndpoint endpoint, CancellationToken cancellationToken);
    ValueTask<RuntimeHealth> HealthAsync(DulcheEndpoint endpoint, CancellationToken cancellationToken);
}
public sealed record Unit { public static Unit Value { get; } = new(); private Unit() { } }
public sealed record AdapterDelta(string? Text = null, string? Thinking = null, string? Type = null, string? Detail = null, long? InputTokens = null, long? OutputTokens = null, string? FinishReason = null);

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
