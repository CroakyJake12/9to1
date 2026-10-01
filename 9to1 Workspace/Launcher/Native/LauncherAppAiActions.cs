using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NineToOne.Cui.AI;
using Haven.Application;
using Haven.Application.Go;

namespace NineToOne.Launcher;

/// <summary>Current permission-filtered owning layout, not a second application index.</summary>
public sealed class LauncherAppAiContext(HomeLauncherSession sessions, GoService? discovery = null, IInstalledApplicationRegistry? applications = null) : IAppAiContext
{
    public async ValueTask<AppAiContextSnapshot> CaptureAsync(CancellationToken ct)
    {
        var session = await sessions.ReadAsync(ct).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The current Home launcher layout is unavailable.");
        var layout = session.Layout;
        var state = new Dictionary<string, JsonElement> { ["layout"] = JsonSerializer.SerializeToElement(layout.Current) };
        var labels = new List<object>(); var partial = discovery is null || applications is null;
        if (discovery is not null && applications is not null)
        {
            await foreach (var update in discovery.QueryAsync(new("", "Apps", 256,
                new(Owners: new HashSet<string>(StringComparer.Ordinal) { "Home" }, Kinds: new HashSet<string>(StringComparer.Ordinal) { "os.installed-application" })), ct))
            {
                if (update.Failure is not null) partial = true;
                if (update.Result is not { } result || !Guid.TryParse(result.Reference.Id, out var id) ||
                    !long.TryParse(result.Reference.Revision, NumberStyles.None, CultureInfo.InvariantCulture, out var revision)) continue;
                var app = await applications.ResolveLaunchAsync(id, revision, ct).ConfigureAwait(false);
                if (app is null || app.HomeProfileId != session.Actor.ProfileId) continue;
                labels.Add(new { app.ApplicationId, app.Label, app.PlatformProfileId, app.IsManaged, app.Revision });
                if (labels.Count == 256) { partial = true; break; }
            }
        }
        if (!await sessions.IsCurrentAsync(session, ct).ConfigureAwait(false)) throw new UnauthorizedAccessException("Home changed while capturing launcher context.");
        state["applications"] = JsonSerializer.SerializeToElement(labels);
        state["applicationDiscoveryPartial"] = JsonSerializer.SerializeToElement(partial);
        if (state.Values.Sum(value => value.GetRawText().Length) > 262144) throw new InvalidOperationException("This layout is too large for the current AI context. Use the owning manual page controls.");
        return new(LauncherSemanticFeatureProvider.AppId, "launcher.home", layout.AuthorityId,
            $"Launcher: {layout.Current.Pages.Count} pages; active page {layout.Current.ActivePage.Name}", null,
            state,
            AppAiDataSensitivity.UserContent, DateTimeOffset.UtcNow, Revision(layout));
    }
    internal static string Revision(LauncherStoredLayout layout) => layout.AuthorityId + ":" + layout.Revision.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Bounded host intent cache only. The exact prepared plan survives same-action retry;
/// neither this cache nor a generic AppAi approval token can grant the owning transaction.</summary>
public sealed class LauncherAppAiActions(HomeLauncherSession sessions, LauncherSemanticFeatureProvider owner) : IAppAiResourceBrokerActions, IDisposable
{
    private sealed record Intent(LauncherSessionSnapshot Session, LauncherSemanticPlan Plan, string? PendingRequestId);
    private sealed record CommandInput(IReadOnlyList<LauncherSemanticCommand> Commands);
    private readonly Dictionary<string, Intent> _intents = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _disposed;
    private readonly CancellationTokenSource _lifetime = new();
    public string? PendingReviewRequestId { get; private set; }
    private AppAiActionRequest? _pendingAction;
    public AppAiActionRequest? PendingActionRequest => _pendingAction is { } value ? value with { Arguments = value.Arguments.Clone(), ApprovalToken = null } : null;
    public IReadOnlyList<AppAiActionDescriptor> Actions { get; } = [new("launcher.layout.edit", "Edit launcher layout",
        "Prepare typed changes to canonical pages, app placements, folders, dock and visibility. Home reviews the complete exact owner plan before any write.",
        AppAiActionRisk.ReversibleChange, RequiresReview: true,
        InputSchemaJson: CommandSchema(),
        // True review metadata is preserved. Production activation requires W1's explicit
        // owner-broker delegation contract, not false flags or a generic token conversion.
        RequiresPermission: true, IsMutation: true, IsReversible: true, HasExternalSideEffects: false,
        ImpactSummary: "The current Home-profile launcher layout changes only after its exact owner plan is approved.") { ApprovalFlow = AppAiApprovalFlow.OwningResourceBroker }];

    private static string CommandSchema()
    {
        // The shared validator intentionally supports a bounded subset without oneOf/const/format.
        // Strict polymorphic deserialization below enforces each operation's exact constructor fields.
        static object Id() => new { type = "string", pattern = "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$" };
        static object Number(int min, int max) => new { type = "integer", minimum = min, maximum = max };
        var properties = new Dictionary<string, object>
        {
            ["operation"] = new { type = "string", @enum = new[] { "createPage", "renamePage", "selectPage", "grid", "addApplication", "movePlacement", "moveToContainer", "createFolder", "dock", "hideApplication", "removePlacement" } },
            ["Name"] = new { type = "string", minLength = 1, maxLength = 4096 },
            ["PageId"] = Id(), ["ApplicationId"] = Id(), ["PlacementId"] = Id(), ["ContainerId"] = Id(),
            ["Rows"] = Number(1, 8), ["Columns"] = Number(3, 7), ["Column"] = Number(0, 6), ["Row"] = Number(0, 1023),
            ["Hidden"] = new { type = "boolean" }
        };
        return JsonSerializer.Serialize(new { type = "object", properties = new { Commands = new { type = "array", minItems = 1, maxItems = 64,
            items = new { type = "object", properties, required = new[] { "operation" }, additionalProperties = false,
                description = "Exact required fields: createPage(Name); renamePage(PageId,Name); selectPage(PageId); grid(Rows,Columns); addApplication(PageId,ApplicationId); movePlacement(PlacementId,ContainerId,Column,Row); moveToContainer(PlacementId,ContainerId); createFolder(PageId,Name); dock(Rows,Columns); hideApplication(ApplicationId,Hidden); removePlacement(PlacementId). No unrelated fields." }
        } }, required = new[] { "Commands" }, additionalProperties = false });
    }

    public ValueTask<AppAiActionResult> ExecuteAsync(AppAiActionRequest request, CancellationToken ct)
        => ExecuteWithOwnedApprovalAsync(request, ct);
    public async ValueTask<AppAiActionResult> ExecuteWithOwnedApprovalAsync(AppAiActionRequest request, CancellationToken ct)
    {
        if (_disposed || request.AppId != LauncherSemanticFeatureProvider.AppId || request.ActionId != LauncherSemanticFeatureProvider.EditAction ||
            request.AccessMode != AppAiAccessMode.Write)
            return AppAiActionResult.Rejected("Launcher write mode and the owning action are required.", "launcher-action-unavailable");
        if (request.Arguments.ValueKind != JsonValueKind.Object) return AppAiActionResult.Rejected("Supply typed launcher command arguments.", "invalid-launcher-command");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        ct = lifetime.Token;
        if (request.Arguments.GetRawText().Length > 65536)
            return AppAiActionResult.Rejected("Use a smaller typed launcher edit.", "launcher-edit-too-large");
        var arguments = request.Arguments.Clone();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed) return AppAiActionResult.Rejected("This launcher session is closed.", "launcher-session-closed");
            var session = await sessions.ReadAsync(ct).ConfigureAwait(false);
            if (session is null || request.ExpectedRevision != LauncherAppAiContext.Revision(session.Layout))
                return AppAiActionResult.Rejected("Read the current Home layout before editing it.", "stale-context", true);
            foreach (var pair in _intents.ToArray())
                if (!await sessions.IsCurrentAsync(pair.Value.Session, ct).ConfigureAwait(false)) _intents.Remove(pair.Key);
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(LauncherAppAiContext.Revision(session.Layout) + "\n" + arguments.GetRawText())));
            if (!_intents.TryGetValue(key, out var intent))
            {
                if (_intents.Count >= 8) return AppAiActionResult.Rejected("Close old launcher drafts before preparing another edit.", "launcher-draft-limit");
                CommandInput input;
                try { input = arguments.Deserialize<CommandInput>(new JsonSerializerOptions { RespectRequiredConstructorParameters = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow }) ?? throw new JsonException(); }
                catch (JsonException) { return AppAiActionResult.Rejected("Supply supported typed launcher commands.", "invalid-launcher-command"); }
                var plan = await owner.PrepareAsync(session.Layout.AuthorityId, session.Layout.Revision, input.Commands, ct).ConfigureAwait(false);
                if (!await sessions.IsCurrentAsync(session, ct).ConfigureAwait(false)) return AppAiActionResult.Rejected("Home changed while preparing the edit.", "stale-context", true);
                intent = new(session, plan, null); _intents.Add(key, intent);
            }
            // ApprovalToken is intentionally ignored. Only the broker's issuer-sealed capability
            // inside the owning ApplyAsync can authorize persistence of this exact cached plan.
            var result = await owner.ApplyAsync(intent.Plan, intent.PendingRequestId, ct).ConfigureAwait(false);
            if (result.Code == "ApprovalRequired" && result.Value?.PendingApprovalRequestId is { } pending)
            {
                _intents[key] = intent with { PendingRequestId = pending }; PendingReviewRequestId = pending;
                _pendingAction = request with { Arguments = arguments.Clone(), ApprovalToken = null };
                return new(false, result.Message, JsonSerializer.SerializeToElement(new { PendingApprovalRequestId = pending }), "approval-pending", true);
            }
            if (result.Succeeded) { _intents.Remove(key); PendingReviewRequestId = null; _pendingAction = null; }
            return new(result.Succeeded, result.Message, JsonSerializer.SerializeToElement(result.Value), result.Succeeded ? null : result.Code, !result.Succeeded);
        }
        finally { if (_disposed) { _intents.Clear(); PendingReviewRequestId = null; _pendingAction = null; } _gate.Release(); }
    }
    public void Dispose()
    {
        _disposed = true; _lifetime.Cancel();
        // Do not dispose a semaphore while an in-flight owner operation is unwinding.
        if (_gate.Wait(0)) { try { _intents.Clear(); PendingReviewRequestId = null; _pendingAction = null; } finally { _gate.Release(); } }
    }
}
