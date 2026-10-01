using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace NineToOne.Launcher;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "operation")]
[JsonDerivedType(typeof(LauncherCreatePageCommand), "createPage")]
[JsonDerivedType(typeof(LauncherRemovePageCommand), "removePage")]
[JsonDerivedType(typeof(LauncherConfigureFolderCommand), "configureFolder")]
[JsonDerivedType(typeof(LauncherPresentationCommand), "presentation")]
[JsonDerivedType(typeof(LauncherCreatePageWithApplicationsCommand), "createPageWithApplications")]
[JsonDerivedType(typeof(LauncherGroupPlacementsCommand), "groupPlacements")]
[JsonDerivedType(typeof(LauncherRenamePageCommand), "renamePage")]
[JsonDerivedType(typeof(LauncherSelectPageCommand), "selectPage")]
[JsonDerivedType(typeof(LauncherGridCommand), "grid")]
[JsonDerivedType(typeof(LauncherAddApplicationCommand), "addApplication")]
[JsonDerivedType(typeof(LauncherMovePlacementCommand), "movePlacement")]
[JsonDerivedType(typeof(LauncherMoveToContainerCommand), "moveToContainer")]
[JsonDerivedType(typeof(LauncherCreateFolderCommand), "createFolder")]
[JsonDerivedType(typeof(LauncherDockCommand), "dock")]
[JsonDerivedType(typeof(LauncherHideApplicationCommand), "hideApplication")]
[JsonDerivedType(typeof(LauncherRemovePlacementCommand), "removePlacement")]
public abstract record LauncherSemanticCommand;
public sealed record LauncherCreatePageCommand(string Name) : LauncherSemanticCommand;
public sealed record LauncherRemovePageCommand(Guid PageId) : LauncherSemanticCommand;
public sealed record LauncherConfigureFolderCommand(Guid FolderId, string Name, int Columns) : LauncherSemanticCommand;
public sealed record LauncherPresentationCommand(int IconSizeDp, int LabelSizeSp, int HorizontalSpacingDp,
    int VerticalSpacingDp, bool ShowLabels, bool ShowPackages) : LauncherSemanticCommand;
public sealed record LauncherCreatePageWithApplicationsCommand(string Name, IReadOnlyList<Guid> ApplicationIds) : LauncherSemanticCommand;
public sealed record LauncherGroupPlacementsCommand(Guid PageId, string Name, IReadOnlyList<Guid> PlacementIds) : LauncherSemanticCommand;
public sealed record LauncherRenamePageCommand(Guid PageId, string Name) : LauncherSemanticCommand;
public sealed record LauncherSelectPageCommand(Guid PageId) : LauncherSemanticCommand;
public sealed record LauncherGridCommand(int Rows, int Columns) : LauncherSemanticCommand;
public sealed record LauncherAddApplicationCommand(Guid PageId, Guid ApplicationId) : LauncherSemanticCommand;
public sealed record LauncherMovePlacementCommand(Guid PlacementId, Guid ContainerId, int Column, int Row) : LauncherSemanticCommand;
public sealed record LauncherMoveToContainerCommand(Guid PlacementId, Guid ContainerId) : LauncherSemanticCommand;
public sealed record LauncherCreateFolderCommand(Guid PageId, string Name) : LauncherSemanticCommand;
public sealed record LauncherDockCommand(int Rows, int Columns) : LauncherSemanticCommand;
public sealed record LauncherHideApplicationCommand(Guid ApplicationId, bool Hidden) : LauncherSemanticCommand;
public sealed record LauncherRemovePlacementCommand(Guid PlacementId) : LauncherSemanticCommand;

/// <summary>Detached, reviewable proposal only. Neither this data nor a request ID grants a write.</summary>
public sealed record LauncherSemanticPlan(string AuthorityId, long ExpectedRevision, LauncherLayout Proposed);
public sealed record LauncherSemanticReceipt(LauncherStoredLayout? Saved = null, string? PendingApprovalRequestId = null);
public sealed class LauncherSemanticActionPolicies : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == LauncherSemanticFeatureProvider.AppId && actionId == LauncherSemanticFeatureProvider.EditAction
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.Routine, true, false, true) : null;
}

/// <summary>Owning semantic edit port. The shared Home broker authorizes exact arguments; the same
/// actor-pinned owner transaction used by the native host enforces the actual revision and commit lease.</summary>
public sealed class LauncherSemanticFeatureProvider(HomeLauncherSession sessions, HomeResourceOperationBroker operations)
{
    public const string AppId = "9to1.launcher";
    public const string EditAction = "launcher.layout.edit";
    public async Task<LauncherStoredLayout?> ReadAsync(CancellationToken ct = default)
        => (await sessions.ReadAsync(ct).ConfigureAwait(false))?.Layout;

    public async Task<LauncherSemanticPlan> PrepareAsync(string authorityId, long expectedRevision,
        IReadOnlyList<LauncherSemanticCommand> commands, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count is < 1 or > 64) throw new ArgumentException("A launcher edit must contain one to 64 typed commands.");
        var frozen = commands.Select(command => command switch
        {
            LauncherCreatePageWithApplicationsCommand c => c with { ApplicationIds = FreezeIds(c.ApplicationIds) },
            LauncherGroupPlacementsCommand c => c with { PlacementIds = FreezeIds(c.PlacementIds) },
            _ => command
        }).ToArray();
        var session = await sessions.ReadAsync(ct).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Open the current Home launcher profile first.");
        if (session.Layout.AuthorityId != authorityId || session.Layout.Revision != expectedRevision)
            throw new IOException("Launcher changed. Read the current layout before preparing an edit.");
        var candidate = LauncherLayoutEdits.Clone(session.Layout.Current);
        foreach (var command in frozen) candidate = command switch
        {
            LauncherCreatePageCommand c => LauncherLayoutEdits.AddPage(candidate, c.Name),
            LauncherRemovePageCommand c => LauncherLayoutEdits.RemovePage(candidate, c.PageId),
            LauncherConfigureFolderCommand c => LauncherLayoutEdits.ConfigureFolder(candidate, c.FolderId, c.Name, c.Columns),
            LauncherPresentationCommand c => LauncherLayoutEdits.SetPresentation(candidate,
                new(c.IconSizeDp, c.LabelSizeSp, c.HorizontalSpacingDp, c.VerticalSpacingDp, c.ShowLabels, c.ShowPackages)),
            LauncherCreatePageWithApplicationsCommand c => LauncherLayoutEdits.AddPageWithApplications(candidate, c.Name, c.ApplicationIds),
            LauncherGroupPlacementsCommand c => LauncherLayoutEdits.GroupPlacements(candidate, c.PageId, c.Name, c.PlacementIds),
            LauncherRenamePageCommand c => LauncherLayoutEdits.RenamePage(candidate, c.PageId, c.Name),
            LauncherSelectPageCommand c => LauncherLayoutEdits.SelectPage(candidate, c.PageId),
            LauncherGridCommand c => LauncherLayoutEdits.Reflow(candidate, c.Rows, c.Columns),
            LauncherAddApplicationCommand c => LauncherLayoutEdits.AddApplication(candidate, c.PageId, c.ApplicationId),
            LauncherMovePlacementCommand c => LauncherLayoutEdits.MovePlacement(candidate, c.PlacementId, c.ContainerId, c.Column, c.Row),
            LauncherMoveToContainerCommand c => LauncherLayoutEdits.MoveToContainer(candidate, c.PlacementId, c.ContainerId),
            LauncherCreateFolderCommand c => LauncherLayoutEdits.CreateFolder(candidate, c.PageId, c.Name),
            LauncherDockCommand c => LauncherLayoutEdits.ConfigureDock(candidate, c.Rows, c.Columns),
            LauncherHideApplicationCommand c => LauncherLayoutEdits.SetHidden(candidate, c.ApplicationId, c.Hidden),
            LauncherRemovePlacementCommand c => LauncherLayoutEdits.RemovePlacement(candidate, c.PlacementId),
            _ => throw new ArgumentException("Unsupported launcher semantic command.")
        };
        if (!await sessions.IsCurrentAsync(session, ct).ConfigureAwait(false)) throw new UnauthorizedAccessException("Home changed while preparing the edit.");
        return new(authorityId, expectedRevision, LauncherLayoutEdits.Clone(candidate));
    }

    private static Guid[] FreezeIds(IReadOnlyList<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count is < 1 or > 64) throw new ArgumentException("Choose one to 64 existing identities.");
        var copy = ids.ToArray();
        if (copy.Any(id => id == Guid.Empty) || copy.Distinct().Count() != copy.Length) throw new ArgumentException("Choose distinct existing identities.");
        return copy;
    }

    public async Task<HomeCoreOperationResult<LauncherSemanticReceipt>> ApplyAsync(LauncherSemanticPlan plan,
        string? approvalRequestId = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var frozen = plan with { Proposed = LauncherLayoutEdits.Clone(plan.Proposed) }; frozen.Proposed.Validate();
        var session = await sessions.ReadAsync(ct).ConfigureAwait(false);
        if (session is null || session.Layout.AuthorityId != frozen.AuthorityId || session.Layout.Revision != frozen.ExpectedRevision)
            return new(false, "Conflict", "Read the current profile/layout before requesting this edit.");
        var scopes = new[] { new ResourceScope(HomeLauncherLayoutStore.RecordType, frozen.AuthorityId,
            frozen.ExpectedRevision.ToString(CultureInfo.InvariantCulture), ResourceAccess.Write) };
        var arguments = JsonSerializer.SerializeToElement(frozen);
        var previewOptions = new JsonSerializerOptions { WriteIndented = true }; previewOptions.Converters.Add(new JsonStringEnumConverter());
        var preview = "Launcher layout change. Before:\n" + JsonSerializer.Serialize(session.Layout.Current, previewOptions) + "\nAfter:\n" + JsonSerializer.Serialize(frozen.Proposed, previewOptions);
        if (preview.Length > 65536) return new(false, "PreviewTooLarge", "Use a smaller edit so Home can show the complete change.");
        if (string.IsNullOrWhiteSpace(approvalRequestId))
        {
            var approval = await operations.AuthorizeAsync(AppId, EditAction, scopes, arguments, preview, null,
                session.Actor.AuthenticationRevision, ct).ConfigureAwait(false);
            return new(false, "ApprovalRequired", "Review this exact launcher change in Home, then retry the unchanged plan.", new(PendingApprovalRequestId: approval.RequestId));
        }
        var capability = await operations.BeginExecutionCapabilityAsync(approvalRequestId, arguments, ct).ConfigureAwait(false);
        if (capability is null || await operations.ClaimExecutionAsync(capability, AppId, EditAction, scopes, arguments, ct).ConfigureAwait(false) != session.Actor)
            return new(false, "PermissionDenied", "The exact current launcher change has not been approved.");
        LauncherStoredLayout saved;
        try { saved = await sessions.EditAsync(session, _ => frozen.Proposed, ct).ConfigureAwait(false); }
        catch
        {
            await RecordAsync(capability, new(HomePermissionRequestState.PartiallyCompleted, "LAUNCHER_OUTCOME_UNCONFIRMED",
                "The owner did not return a confirmed layout outcome. Reload canonical state before another edit.", [])).ConfigureAwait(false);
            throw;
        }
        var audited = await RecordAsync(capability, new(HomePermissionRequestState.Succeeded, "LAUNCHER_LAYOUT_COMMITTED",
            "The canonical launcher layout was saved.", [new(HomeLauncherLayoutStore.RecordType, saved.AuthorityId)])).ConfigureAwait(false);
        return new(true, audited ? "Saved" : "SavedAuditPending", audited ? "Launcher layout saved." : "Launcher saved; Home audit is pending. Do not repeat the edit.", new(saved), Revision: saved.Revision);
    }
    private async Task<bool> RecordAsync(HomeResourceExecutionCapability capability, HomeExecutionOutcome outcome)
    {
        try { return (await operations.CompleteExecutionAsync(capability, outcome, CancellationToken.None).ConfigureAwait(false)).Succeeded; }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException) { return false; }
    }
}
