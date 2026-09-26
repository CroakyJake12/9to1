using System.Text.Json;

namespace NineToOne.Cui.AI;

public enum AppAiDataSensitivity
{
    Public,
    UserContent,
    Private,
    Restricted,
}

public enum AppAiActionRisk
{
    ReadOnly,
    ReversibleChange,
    DestructiveChange,
    ExternalSideEffect,
}

/// <summary>AI-bar capability mode, independent of the host application's editing state.</summary>
public enum AppAiAccessMode
{
    ReadOnly = 0,
    Write = 1,
}

public enum AppAiApprovalOutcome
{
    Approved = 0,
    Pending = 1,
    Denied = 2,
    Unavailable = 3,
}

public enum AppAiActionGraphStatus
{
    Started = 0,
    WaitingForApproval = 1,
    Completed = 2,
    Blocked = 3,
    Failed = 4,
}

public enum AppAiRequestState
{
    Idle,
    CapturingContext,
    Generating,
    WaitingForApproval,
    ExecutingAction,
    Completed,
    Cancelled,
    Failed,
}

public sealed record AppAiSelection(
    string Kind,
    string? DisplayName,
    JsonElement Value);

public sealed record AppAiContextSnapshot(
    string AppId,
    string SurfaceId,
    string? DocumentId,
    string Summary,
    AppAiSelection? Selection,
    IReadOnlyDictionary<string, JsonElement> SemanticState,
    AppAiDataSensitivity Sensitivity,
    DateTimeOffset CapturedAt,
    string? Revision = null,
    IReadOnlyList<string>? SelectionIds = null,
    string? HostState = null,
    bool IsLiveDatabase = false);

public sealed record AppAiActionDescriptor(
    string Id,
    string DisplayName,
    string Description,
    AppAiActionRisk Risk,
    bool RequiresReview,
    string InputSchemaJson,
    bool RequiresPermission = true,
    bool IsMutation = true,
    bool IsReversible = true,
    bool HasExternalSideEffects = false,
    IReadOnlyList<string>? AffectedObjectIds = null,
    bool ImpactUnknown = true,
    string? ImpactSummary = null);

public sealed record AppAiActionRequest(
    string AppId,
    string ActionId,
    JsonElement Arguments,
    string? ApprovalToken,
    string CorrelationId,
    AppAiAccessMode AccessMode = AppAiAccessMode.ReadOnly,
    string? ExpectedRevision = null);

public sealed record AppAiActionResult(
    bool Succeeded,
    string Summary,
    JsonElement? Value,
    string? ErrorCode = null,
    bool CanRetry = false)
{
    public static AppAiActionResult Success(string summary, JsonElement? value = null) =>
        new(true, summary, value);

    public static AppAiActionResult Rejected(string summary, string errorCode, bool canRetry = false) =>
        new(false, summary, null, errorCode, canRetry);
}

public interface IAppAiContext
{
    ValueTask<AppAiContextSnapshot> CaptureAsync(CancellationToken cancellationToken);
}

public interface IAppAiActions
{
    IReadOnlyList<AppAiActionDescriptor> Actions { get; }

    ValueTask<AppAiActionResult> ExecuteAsync(
        AppAiActionRequest request,
        CancellationToken cancellationToken);
}

public interface IAppAiApprovalVerifier
{
    ValueTask<bool> VerifyAsync(
        string appId,
        string actionId,
        string approvalToken,
        CancellationToken cancellationToken);
}

/// <summary>Home-backed approval request port. Missing or unavailable approval fails closed.</summary>
public interface IAppAiApprovalRequester
{
    ValueTask<AppAiApprovalDecision> RequestAsync(
        AppAiApprovalRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Data's host implementation prepares and verifies live-database changes.</summary>
public interface IAppAiDatabaseMutationGuard
{
    ValueTask<AppAiDatabasePreparation> PrepareAsync(
        AppAiContextSnapshot context,
        AppAiActionDescriptor action,
        JsonElement arguments,
        CancellationToken cancellationToken);

    ValueTask<bool> VerifyAsync(
        AppAiContextSnapshot context,
        AppAiActionDescriptor action,
        JsonElement arguments,
        AppAiActionResult result,
        string backupId,
        CancellationToken cancellationToken);
}

/// <summary>Home adapter to the common Action Graph; payloads exclude prompt and artifact content.</summary>
public interface IAppAiActionGraph
{
    ValueTask PublishAsync(AppAiActionGraphEvent value, CancellationToken cancellationToken);
}

/// <summary>Adapter to the shared Universal Model Picker and its routing state.</summary>
public interface IAppAiModelPicker
{
    ValueTask<IReadOnlyList<AppAiModelOption>> GetModelsAsync(CancellationToken cancellationToken);
    ValueTask<AppAiModelSelection?> GetSelectionAsync(CancellationToken cancellationToken);
    ValueTask<bool> SelectAsync(string modelId, CancellationToken cancellationToken);
}

public sealed record AppAiPrompt(
    string Prompt,
    AppAiContextSnapshot Context,
    string CorrelationId,
    AppAiAccessMode AccessMode = AppAiAccessMode.ReadOnly,
    IReadOnlyList<AppAiActionDescriptor>? AvailableActions = null,
    AppAiModelSelection? ModelSelection = null)
{
    public string SystemInstructions => AccessMode == AppAiAccessMode.ReadOnly
        ? "Inspect only the supplied authorised semantic context. Do not request or perform app actions. You may describe proposed changes in your response. Do not infer private or off-scope information."
        : "Use only the supplied authorised semantic context and listed typed app actions. Request mutations only through those actions and stable target IDs. The host app, Home permissions and its current edit/review state remain authoritative; Write mode does not bypass them. Do not use UI simulation or invent entities.";
}

/// <summary>A model-selected typed action. Approval tokens are never model-authored.</summary>
public sealed record AppAiRequestedAction(string ActionId, JsonElement Arguments);

public sealed record AppAiResponseChunk(
    string Text,
    bool IsFinal = false,
    AppAiRequestedAction? RequestedAction = null);

public sealed record AppAiApprovalRequest(
    string CallerId,
    AppAiContextSnapshot Context,
    AppAiActionDescriptor Action,
    bool ImpactUnknown,
    bool ForcePerActionApproval,
    string? ChangePreview,
    string? BackupId,
    string CorrelationId);

public sealed record AppAiApprovalDecision(
    AppAiApprovalOutcome Outcome,
    string? ApprovalToken,
    string Code,
    string Message);

public sealed record AppAiDatabasePreparation(
    bool IsValid,
    string Preview,
    string? BackupId,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record AppAiActionGraphEvent(
    string AppId,
    string SurfaceId,
    string? ArtifactId,
    string ActionId,
    string CorrelationId,
    AppAiActionGraphStatus Status,
    string Summary,
    DateTimeOffset Timestamp);

public sealed record AppAiModelOption(string Id, string DisplayName, string ProviderId, bool IsLocal, bool IsAvailable);

public sealed record AppAiModelSelection(string ModelId, string Effort);

public interface IDulcheAppClient
{
    IAsyncEnumerable<AppAiResponseChunk> StreamAsync(
        AppAiPrompt prompt,
        CancellationToken cancellationToken);
}
