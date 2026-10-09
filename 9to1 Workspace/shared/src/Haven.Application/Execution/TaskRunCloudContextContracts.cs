using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Actual context selected by the trusted Chat producer after its existing service reads and
/// authorization. This is an observation, never a caller-supplied grant. No source may classify
/// foreign/shared resources as personal app data merely because their text is available.
/// Persistent Memory is distinct from Background Learning; the latter retains its existing
/// separate disclosure/contributor/scope rules. Sources and contents stay in process memory.
/// </summary>
public sealed record TaskRunContextInventory(
    Conversation OriginalConversation,
    IReadOnlyList<ChatMessage> OriginalHistory,
    IReadOnlyList<KnowledgeRecord> SelectedPersistentMemory,
    IReadOnlyList<KnowledgeRecord> SelectedBackgroundLearning,
    string? ProjectContext,
    string? ProjectInstructions,
    string? RegisteredContext,
    IReadOnlyList<string>? OriginalImages,
    string? OriginalWorkspaceRoot,
    string? BackgroundAppId = null,
    string? BackgroundProjectId = null,
    IReadOnlySet<string>? OriginalBackgroundScopes = null)
{
    // Selected by the trusted original Chat source after current protected READ.
    // This durable observation cannot reconstruct the JsonIgnored live source.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ChatOriginalAttachmentLineage? OriginalAttachmentLineage { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public ChatOriginalAttachmentInvocation? OriginalAttachmentInvocation { get; init; }
}

/// <summary>
/// Called ONLY by the trusted application producer, after actual context reads and existing
/// source authorization. It binds the SAME original request/context reference to the current
/// task owner and detached wire payload. A new finite request requires a new capture. Recorded
/// IDs, hashes, privacy labels and route flags do not reconstruct that private issuance.
/// Ordinary conversations have no obligation to enter this canonical request path.
/// </summary>
public interface ITaskRunProviderContextCapture
{
    ValueTask CaptureOriginalAsync(TaskExecutionSnapshot currentSnapshot,
        OllamaChatRequest actualOriginalRequest, TaskRunContextInventory actualSelections,
        CancellationToken cancellationToken);
    ValueTask CaptureOriginalAsync(TaskExecutionSnapshot currentSnapshot,
        OllamaToolRequest actualOriginalRequest, TaskRunContextInventory actualSelections,
        CancellationToken cancellationToken);
}

/// <summary>
/// Per-request content admission, separate from the attempt's route/credential lease. The router
/// supplies both the SAME original caller request and its actual model/context clone. Only those
/// two fields may change; exact transcript, images, tool definitions, options and system payload
/// remain captured. Current router observations must name the same issued attempt/current run.
/// The frame stays held through the actual finite body/MoveNext/Dispose/finally; its release never
/// releases the encompassing issuer lease. Cleanup faults cannot be provider-failure waivers.
/// </summary>
public interface ITaskRunProviderContextAuthority
{
    ValueTask<ITaskRunProviderContextFrame> AcquireOriginalFrameAsync(
        TaskRunAttemptAdmission sameIssuedAdmission, TaskExecutionSnapshot currentSnapshot,
        OllamaChatRequest actualOriginalRequest, OllamaChatRequest actualRoutedRequest,
        CancellationToken cancellationToken);
    ValueTask<ITaskRunProviderContextFrame> AcquireOriginalFrameAsync(
        TaskRunAttemptAdmission sameIssuedAdmission, TaskExecutionSnapshot currentSnapshot,
        OllamaToolRequest actualOriginalRequest, OllamaToolRequest actualRoutedRequest,
        CancellationToken cancellationToken);
}

public interface ITaskRunProviderContextFrame : IAsyncDisposable
{
    ValueTask RevalidateAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Provider settings and credential PRESENCE observation only. Upstream validity, current quota,
/// price and monetary budget are not exposed by the maintained provider/secret interfaces. Null
/// values remain unknown and may not be advertised as a spending cap, balance or reservation.
/// An applicable separately existing paid-use policy must still be enforced by its actual owner.
/// </summary>
public sealed record TaskRunCloudConfigurationObservation(
    string ProviderId, string ModelId, bool ConfiguredCredentialPresent,
    decimal? KnownCost, string? Currency, decimal? KnownRemainingBudget,
    string BudgetQualification);

/// <summary>Optional actual frame subtype required for canonical remote invocation. After async
/// source/actor revalidation and immutable wire detachment, call once around the actual RAW Task
/// start (or the Task starting real stream iteration). It returns that SAME original value; no
/// await/network/observer work occurs under the central policy writer gate. Revocation after an
/// admitted start does not undo that effect. Caller owns full Task.Exception/enumerator/finally.
/// A fingerprint alone does not protect mutable DTO collections during external transmission.</summary>
public interface ITaskRunProviderInvocationFence
{
    T RunOriginalInvocation<T>(Func<T> originalRawStart);
}
