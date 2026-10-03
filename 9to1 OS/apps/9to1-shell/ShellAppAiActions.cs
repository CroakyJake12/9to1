using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NineToOne.Cui.AI;

namespace NineToOne.Os.Shell;

public sealed class ShellAppAiContext(ShellConfigurationService configuration, ShellStoredConfiguration? expectedSession = null) : IAppAiContext
{
    private ShellStoredConfiguration? _session = expectedSession;
    public async ValueTask<AppAiContextSnapshot> CaptureAsync(CancellationToken ct)
    {
        if (_session is { } existing && !await configuration.IsCurrentSessionAsync(existing, ct)) throw new UnauthorizedAccessException("The original shell AI session changed.");
        var snapshot = await configuration.GetAsync(ct);
        var original = Interlocked.CompareExchange(ref _session, snapshot.Stored, null) ?? snapshot.Stored;
        if (original.SessionActor != snapshot.Stored.SessionActor || !await configuration.IsCurrentSessionAsync(original, ct)) throw new UnauthorizedAccessException("The original shell AI session changed.");
        var state = new Dictionary<string, JsonElement>
        {
            ["savedConfiguration"] = JsonSerializer.SerializeToElement(snapshot.Stored.Current),
            ["hasPreview"] = JsonSerializer.SerializeToElement(snapshot.Preview is not null),
            ["previewRequiresExplicitKeep"] = JsonSerializer.SerializeToElement(true)
        };
        if (state.Values.Sum(v => v.GetRawText().Length) > 262144) throw new InvalidOperationException("The current shell is too large for AI context; use the native controls.");
        return new(ShellSemanticFeatureProvider.AppId, "os.shell", snapshot.Stored.AuthorityId,
            "Desktop Space: " + snapshot.Stored.Current.ActiveSpace.Name, null, state,
            AppAiDataSensitivity.UserContent, DateTimeOffset.UtcNow, Revision(snapshot));
    }
    internal static string Revision(ShellConfigurationSnapshot snapshot) => snapshot.Stored.AuthorityId + ":" +
        snapshot.Stored.Revision.ToString(CultureInfo.InvariantCulture) + ":" + snapshot.IntentGeneration.ToString(CultureInfo.InvariantCulture) + ":" +
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot.Stored.SessionActor)));
}

/// <summary>One host's bounded immutable preview intents; all authority remains in the Home broker.</summary>
public sealed class ShellAppAiActions(ShellConfigurationService configuration, ShellSemanticFeatureProvider owner) : IAppAiResourceBrokerActions, IDisposable
{
    private sealed record Input(IReadOnlyList<ShellSemanticCommand> Commands);
    private sealed record Intent(ShellSemanticPlan Plan, string? RequestId);
    private readonly Dictionary<string, Intent> _intents = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private volatile bool _disposed;
    private AppAiActionRequest? _pendingAction;
    public string? PendingReviewRequestId { get; private set; }
    public string? PendingAuditReceiptId { get; private set; }
    public AppAiActionRequest? PendingActionRequest => _pendingAction is { } request ? request with { Arguments = request.Arguments.Clone(), ApprovalToken = null } : null;
    public IReadOnlyList<AppAiActionDescriptor> Actions { get; } = [new(ShellSemanticFeatureProvider.PreviewAction, "Preview shell changes",
        "Prepare typed changes to desktop pages, grid, Desktop Spaces and taskbar layers. Home reviews the complete exact proposal; explicit native Keep is required to save the 30-second preview.",
        AppAiActionRisk.ReversibleChange, RequiresReview: true, InputSchemaJson: Schema(), RequiresPermission: true, IsMutation: true,
        IsReversible: true, HasExternalSideEffects: false, ImpactSummary: "Stages a volatile shell preview only. No durable configuration is saved.")
        { ApprovalFlow = AppAiApprovalFlow.OwningResourceBroker }];
    private static string Schema()
    {
        static object Integer(int min, int max) => new { type = "integer", minimum = min, maximum = max };
        var properties = new Dictionary<string, object>
        {
            ["operation"] = new { type = "string", @enum = new[] { "addPage", "renamePage", "grid", "addLayer", "renameLayer", "layerPresentation", "duplicateSpace", "renameSpace" } },
            ["Name"] = new { type = "string", minLength = 1, maxLength = 256 },
            ["Columns"] = Integer(1, 32), ["Rows"] = Integer(1, 32), ["Thickness"] = Integer(24, 256),
            ["Spacing"] = Integer(0, 64), ["Padding"] = Integer(0, 64), ["CornerRadius"] = Integer(0, 128),
            ["Opacity"] = new { type = "number", minimum = .2, maximum = 1 }
        };
        return JsonSerializer.Serialize(new { type = "object", properties = new { Commands = new { type = "array", minItems = 1, maxItems = 64,
            items = new { type = "object", properties, required = new[] { "operation" }, additionalProperties = false,
                description = "Exact fields: addPage(Name), renamePage(Name), grid(Columns,Rows), addLayer(Name), renameLayer(Name), layerPresentation(Thickness,Spacing,Padding,CornerRadius,Opacity), duplicateSpace(Name), renameSpace(Name). Changes target the current active space/page/layer; newly created targets become active for following commands." }
        } }, required = new[] { "Commands" }, additionalProperties = false });
    }
    public ValueTask<AppAiActionResult> ExecuteAsync(AppAiActionRequest request, CancellationToken ct) => ExecuteWithOwnedApprovalAsync(request, ct);
    public async ValueTask<AppAiActionResult> ExecuteWithOwnedApprovalAsync(AppAiActionRequest request, CancellationToken ct)
    {
        if (_disposed || request.AppId != ShellSemanticFeatureProvider.AppId || request.ActionId != ShellSemanticFeatureProvider.PreviewAction || request.AccessMode != AppAiAccessMode.Write)
            return AppAiActionResult.Rejected("Use shell Write mode for this owning preview action.", "shell-action-unavailable");
        if (request.Arguments.ValueKind != JsonValueKind.Object || request.Arguments.GetRawText().Length > 65536)
            return AppAiActionResult.Rejected("Supply a bounded typed shell proposal.", "invalid-shell-command");
        var arguments = request.Arguments.Clone();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token); ct = linked.Token;
        await _gate.WaitAsync(ct);
        try
        {
            if (_disposed) return AppAiActionResult.Rejected("This shell AI session is closed.", "shell-session-closed");
            var snapshot = await configuration.GetAsync(ct);
            if (snapshot.Stored.SessionActor is null || request.ExpectedRevision != ShellAppAiContext.Revision(snapshot))
                return AppAiActionResult.Rejected("Read the current shell before preparing a preview.", "stale-context", true);
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.ExpectedRevision + "\n" + arguments.GetRawText())));
            if (!_intents.TryGetValue(key, out var intent))
            {
                if (_intents.Count >= 8) return AppAiActionResult.Rejected("Close this session to discard old pending drafts before adding more.", "shell-draft-limit");
                Input input;
                try { input = arguments.Deserialize<Input>(new JsonSerializerOptions { RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw new JsonException(); }
                catch (JsonException) { return AppAiActionResult.Rejected("Supply supported typed shell commands with their exact fields.", "invalid-shell-command"); }
                intent = new(await owner.PrepareAsync(snapshot, input.Commands, ct), null); _intents.Add(key, intent);
            }
            var result = await owner.ApplyAsync(intent.Plan, intent.RequestId, ct);
            if (result.Code == "ApprovalRequired" && result.Value?.PendingApprovalRequestId is { } pending)
            {
                _intents[key] = intent with { RequestId = pending }; PendingReviewRequestId = pending;
                _pendingAction = request with { Arguments = arguments.Clone(), ApprovalToken = null };
                return new(false, result.Message, JsonSerializer.SerializeToElement(new { PendingApprovalRequestId = pending }), "approval-pending", true);
            }
            if (result.Value?.AuditReceiptId is { } audit) PendingAuditReceiptId = audit;
            if (result.Succeeded || result.Value?.ExecutionConsumed == true) { _intents.Remove(key); PendingReviewRequestId = null; _pendingAction = null; }
            return new(result.Succeeded, result.Message, JsonSerializer.SerializeToElement(result.Value), result.Succeeded ? null : result.Code, !result.Succeeded);
        }
        finally { if (_disposed) Clear(); _gate.Release(); }
    }
    public async Task<bool> RetryPendingAuditAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_disposed || PendingAuditReceiptId is not { } receipt) return false;
            if (!await owner.RetryAuditAsync(receipt, ct)) return false;
            PendingAuditReceiptId = null; return true;
        }
        finally { _gate.Release(); }
    }
    private void Clear() { _intents.Clear(); PendingReviewRequestId = null; _pendingAction = null; }
    public void Dispose()
    {
        _disposed = true; _lifetime.Cancel();
        if (_gate.Wait(0)) { try { Clear(); } finally { _gate.Release(); } }
    }
}
