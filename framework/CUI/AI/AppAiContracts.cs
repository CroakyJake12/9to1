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
    DateTimeOffset CapturedAt);

public sealed record AppAiActionDescriptor(
    string Id,
    string DisplayName,
    string Description,
    AppAiActionRisk Risk,
    bool RequiresReview,
    string InputSchemaJson);

public sealed record AppAiActionRequest(
    string AppId,
    string ActionId,
    JsonElement Arguments,
    string? ApprovalToken,
    string CorrelationId);

public sealed record AppAiActionResult(
    bool Succeeded,
    string Summary,
    JsonElement? Value,
    string? ErrorCode = null)
{
    public static AppAiActionResult Rejected(string summary, string errorCode) =>
        new(false, summary, null, errorCode);
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

public sealed record AppAiPrompt(
    string Prompt,
    AppAiContextSnapshot Context,
    string CorrelationId);

public sealed record AppAiResponseChunk(string Text, bool IsFinal = false);

public interface IDulcheAppClient
{
    IAsyncEnumerable<AppAiResponseChunk> StreamAsync(
        AppAiPrompt prompt,
        CancellationToken cancellationToken);
}
