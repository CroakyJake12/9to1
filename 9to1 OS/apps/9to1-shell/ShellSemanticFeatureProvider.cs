using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace NineToOne.Os.Shell;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "operation")]
[JsonDerivedType(typeof(ShellAddPageCommand), "addPage")]
[JsonDerivedType(typeof(ShellRenamePageCommand), "renamePage")]
[JsonDerivedType(typeof(ShellGridCommand), "grid")]
[JsonDerivedType(typeof(ShellAddLayerCommand), "addLayer")]
[JsonDerivedType(typeof(ShellRenameLayerCommand), "renameLayer")]
[JsonDerivedType(typeof(ShellLayerPresentationCommand), "layerPresentation")]
[JsonDerivedType(typeof(ShellDuplicateSpaceCommand), "duplicateSpace")]
[JsonDerivedType(typeof(ShellRenameSpaceCommand), "renameSpace")]
public abstract record ShellSemanticCommand;
public sealed record ShellAddPageCommand(string Name) : ShellSemanticCommand;
public sealed record ShellRenamePageCommand(string Name) : ShellSemanticCommand;
public sealed record ShellGridCommand(int Columns, int Rows) : ShellSemanticCommand;
public sealed record ShellAddLayerCommand(string Name) : ShellSemanticCommand;
public sealed record ShellRenameLayerCommand(string Name) : ShellSemanticCommand;
public sealed record ShellLayerPresentationCommand(int Thickness, int Spacing, int Padding, int CornerRadius, double Opacity) : ShellSemanticCommand;
public sealed record ShellDuplicateSpaceCommand(string Name) : ShellSemanticCommand;
public sealed record ShellRenameSpaceCommand(string Name) : ShellSemanticCommand;

/// <summary>Owner-created, detached proposal. Its original read binding remains process-local.</summary>
public sealed class ShellSemanticPlan
{
    internal ShellConfigurationSnapshot ExpectedSnapshot { get; }
    internal ShellStoredConfiguration Expected => ExpectedSnapshot.Stored;
    internal ShellConfiguration Candidate { get; }
    public Guid IntentId { get; } = Guid.NewGuid();
    public string AuthorityId => Expected.AuthorityId;
    public long ExpectedRevision => Expected.Revision;
    public ShellConfiguration Proposed => ShellSemanticFeatureProvider.Clone(Candidate);
    internal ShellSemanticPlan(ShellConfigurationSnapshot expected, ShellConfiguration candidate)
    { ExpectedSnapshot = expected; Candidate = ShellSemanticFeatureProvider.Clone(candidate); }
}
public sealed record ShellSemanticReceipt(Guid? PreviewId = null, DateTimeOffset? ExpiresAt = null, string? PendingApprovalRequestId = null, string? AuditReceiptId = null, bool ExecutionConsumed = false);
public sealed class ShellSemanticActionPolicies : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == ShellSemanticFeatureProvider.AppId && actionId == ShellSemanticFeatureProvider.PreviewAction
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.Routine, true, false, true) : null;
}

/// <summary>The Home broker authorizes a volatile preview only. Explicit native Keep remains
/// a separate original-session/CAS transaction; no approval token can bypass that boundary.</summary>
public sealed class ShellSemanticFeatureProvider(ShellConfigurationService configuration, HomeResourceOperationBroker operations)
{
    private sealed record AuditReceipt(HomeResourceExecutionCapability Capability, HomeExecutionOutcome Outcome);
    private readonly Dictionary<string, AuditReceipt> _auditReceipts = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    public const string AppId = "9to1.os.shell";
    public const string PreviewAction = "os.shell.configuration.preview";
    internal static ShellConfiguration Clone(ShellConfiguration value) =>
        JsonSerializer.Deserialize<ShellConfiguration>(JsonSerializer.Serialize(value)) ?? throw new InvalidDataException("Shell configuration is missing.");
    public async Task<ShellSemanticPlan> PrepareAsync(ShellConfigurationSnapshot expected, IReadOnlyList<ShellSemanticCommand> commands, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count is < 1 or > 64) throw new ArgumentException("Choose one to 64 typed shell changes.");
        var frozen = commands.ToArray();
        var current = await CurrentAsync(expected, ct);
        var candidate = Clone(current.Stored.Current);
        foreach (var command in frozen) candidate = command switch
        {
            ShellAddPageCommand c => DesktopPageEdits.AddPage(candidate, c.Name),
            ShellRenamePageCommand c => DesktopPageEdits.RenamePage(candidate, c.Name),
            ShellGridCommand c => DesktopPageEdits.GridSize(candidate, c.Columns, c.Rows),
            ShellAddLayerCommand c => ShellEdits.AddLayer(candidate, c.Name),
            ShellRenameLayerCommand c => ShellEdits.RenameLayer(candidate, c.Name),
            ShellLayerPresentationCommand c => ShellEdits.Presentation(candidate, c.Thickness, c.Spacing, c.Padding, c.CornerRadius, c.Opacity),
            ShellDuplicateSpaceCommand c => ShellEdits.DuplicateSpace(candidate, c.Name),
            ShellRenameSpaceCommand c => ShellEdits.RenameSpace(candidate, c.Name),
            _ => throw new ArgumentException("This shell semantic command is unavailable.")
        };
        candidate.Validate();
        return new(expected with { Stored = expected.Stored with { Current = Clone(expected.Stored.Current), Previous = expected.Stored.Previous is null ? null : Clone(expected.Stored.Previous) } }, candidate);
    }
    public async Task<HomeCoreOperationResult<ShellSemanticReceipt>> ApplyAsync(ShellSemanticPlan plan, string? approvalRequestId = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return await ApplyCoreAsync(plan, approvalRequestId, ct); }
        finally { _gate.Release(); }
    }
    private async Task<HomeCoreOperationResult<ShellSemanticReceipt>> ApplyCoreAsync(ShellSemanticPlan plan, string? approvalRequestId, CancellationToken ct)
    {
        if (_auditReceipts.Count >= 32) return new(false, "AuditRecoveryRequired", "Finish retained shell audit recovery before another preview.");
        ArgumentNullException.ThrowIfNull(plan);
        var current = await CurrentAsync(plan.ExpectedSnapshot, ct);
        var arguments = JsonSerializer.SerializeToElement(new { plan.IntentId, plan.AuthorityId, plan.ExpectedRevision, IntentGeneration = plan.ExpectedSnapshot.IntentGeneration, Proposed = plan.Candidate });
        var scopes = new[] { new ResourceScope(HomeShellConfigurationStore.RecordType, plan.AuthorityId,
            plan.ExpectedRevision.ToString(CultureInfo.InvariantCulture), ResourceAccess.Write) };
        var options = new JsonSerializerOptions { WriteIndented = true }; options.Converters.Add(new JsonStringEnumConverter());
        var preview = "Prepare a volatile 30-second shell preview. Nothing is saved. Explicit Keep is required before expiry.\nBefore:\n" +
            JsonSerializer.Serialize(current.Stored.Current, options) + "\nAfter:\n" + JsonSerializer.Serialize(plan.Candidate, options);
        if (preview.Length > 65536) return new(false, "PreviewTooLarge", "Use a smaller shell change so Home can show its complete impact.");
        if (string.IsNullOrWhiteSpace(approvalRequestId))
        {
            var approval = await operations.AuthorizeAsync(AppId, PreviewAction, scopes, arguments, preview, null,
                plan.Expected.SessionActor!.AuthenticationRevision, ct);
            return new(false, "ApprovalRequired", "Review this exact preview in Home, then retry the unchanged request.", new(PendingApprovalRequestId: approval.RequestId));
        }
        var capability = await operations.BeginExecutionCapabilityAsync(approvalRequestId, arguments, ct);
        if (capability is null) return new(false, "PermissionDenied", "The current exact shell preview has not been approved.");
        var claimedActor = await operations.ClaimExecutionAsync(capability, AppId, PreviewAction, scopes, arguments, ct);
        if (claimedActor is null) return new(false, "PermissionDenied", "The current exact shell preview has not been approved.");
        if (claimedActor != plan.Expected.SessionActor)
        {
            var failedAudit = await RecordAsync(capability, new(HomePermissionRequestState.Failed, "SHELL_ACTOR_CHANGED", "The original shell actor changed. No preview or durable write was attempted.", []));
            return new(false, "PermissionDenied", "The original shell actor changed; no preview was applied.", new(AuditReceiptId: failedAudit, ExecutionConsumed: true));
        }
        ShellConfigurationSnapshot staged;
        try { staged = await configuration.PreviewIfUnchangedAsync(plan.ExpectedSnapshot, plan.Candidate, TimeSpan.FromSeconds(30), ct); }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException or OperationCanceledException)
        {
            var failedAudit = await RecordAsync(capability, new(HomePermissionRequestState.PartiallyCompleted, "SHELL_PREVIEW_UNCONFIRMED", "No durable save was requested. Inspect the current shell preview before another request.", []));
            return new(false, "PreviewUnconfirmed", "Inspect the current preview; no durable save was requested. Do not repeat this intent.", new(AuditReceiptId: failedAudit, ExecutionConsumed: true));
        }
        var auditReceipt = await RecordAsync(capability, new(HomePermissionRequestState.Succeeded, "SHELL_PREVIEW_PREPARED", "Volatile preview prepared. No configuration was saved; explicit Keep is still required.", []));
        return new(true, auditReceipt is null ? "PreviewPrepared" : "PreviewPreparedAuditPending",
            auditReceipt is null ? "Preview prepared; use Keep within 30 seconds to save, or Revert to discard." :
                "Preview prepared; its activity record is pending. Finish recording the result without repeating the change. Keep is still required before expiry to save.",
            new(staged.Preview!.Id, staged.Preview.ExpiresAt, AuditReceiptId: auditReceipt, ExecutionConsumed: true), Revision: staged.Stored.Revision);
    }
    private async Task<ShellConfigurationSnapshot> CurrentAsync(ShellConfigurationSnapshot expectedSnapshot, CancellationToken ct)
    {
        var expected = expectedSnapshot.Stored;
        if (!await configuration.IsCurrentSessionAsync(expected, ct)) throw new UnauthorizedAccessException("The original Home session changed.");
        var current = await configuration.GetAsync(ct);
        if (expected.SessionActor is null || current.Stored.SessionActor != expected.SessionActor || current.Stored.AuthorityId != expected.AuthorityId)
            throw new UnauthorizedAccessException("The shell request belongs to an older Home session.");
        if (current.Stored.Revision != expected.Revision) throw new ShellConfigurationConflictException();
        if (current.IntentGeneration != expectedSnapshot.IntentGeneration) throw new InvalidOperationException("The original shell preview intent was replaced or expired.");
        if (current.Preview is not null) throw new InvalidOperationException("Keep or revert the existing preview before preparing another change.");
        return current;
    }
    public async Task<IReadOnlyList<string>> ReadPendingAuditIdsAsync(ShellStoredConfiguration expected, CancellationToken ct = default)
    {
        if (!await configuration.IsCurrentSessionAsync(expected, ct)) throw new UnauthorizedAccessException("The current Home shell session is unavailable.");
        await _gate.WaitAsync(ct);
        try
        {
            if (!await configuration.IsCurrentSessionAsync(expected, ct)) throw new UnauthorizedAccessException("Home changed while reading shell audit recovery.");
            return _auditReceipts.Where(pair => pair.Value.Capability.Scopes.Any(scope =>
                scope.Kind == HomeShellConfigurationStore.RecordType && scope.Id == expected.AuthorityId)).Select(pair => pair.Key).ToArray();
        }
        finally { _gate.Release(); }
    }
    public async Task<bool> RetryAuditAsync(string receiptId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!_auditReceipts.TryGetValue(receiptId, out var receipt)) return false;
            if (!await TryRecordAsync(receipt.Capability, receipt.Outcome, ct)) return false;
            _auditReceipts.Remove(receiptId); return true;
        }
        finally { _gate.Release(); }
    }
    private async Task<string?> RecordAsync(HomeResourceExecutionCapability capability, HomeExecutionOutcome outcome)
    {
        if (await TryRecordAsync(capability, outcome, CancellationToken.None)) return null;
        var id = Guid.NewGuid().ToString("N"); _auditReceipts.Add(id, new(capability, outcome)); return id;
    }
    private async Task<bool> TryRecordAsync(HomeResourceExecutionCapability capability, HomeExecutionOutcome outcome, CancellationToken ct)
    {
        try { return (await operations.CompleteExecutionAsync(capability, outcome, ct)).Succeeded; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { return false; }
    }
}
