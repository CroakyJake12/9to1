using Avalonia.Threading;
using HavenOS.Files;

namespace HavenOS.Apps.Canvas;

public sealed partial class CanvasHostSurface
{
    private CanvasEmbeddedArtifactTarget? _embeddedTarget;
    private CanvasEmbeddedArtifactOpenResult? _embeddedSelection;
    private bool _embeddedMutationsAllowed;
    private bool _embeddedChooser;
    private CanvasEmbeddedArtifactChoiceMode _embeddedChoiceMode;
    private CanvasEmbeddedArtifactChoiceWitness? _embeddedChoiceWitness;
    private readonly TaskCompletionSource<CanvasEmbeddedArtifactTarget?> _embeddedChoice =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal CanvasEmbeddedArtifactTarget CurrentEmbeddedArtifactTarget => _embeddedTarget
        ?? throw new InvalidOperationException("This Canvas surface is not an embedded artifact session.");

    internal async Task<CanvasEmbeddedArtifactOpenResult> InitializeEmbeddedArtifactAsync(
        CanvasEmbeddedArtifactTarget target, CanvasEmbeddedArtifactMode mode, CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(target);
        if (_initialization is not null || _embeddedTarget is not null || _nativeOwnerAcquired || _closed)
            throw new InvalidOperationException("Create an embedded Canvas session from a fresh canonical Canvas surface.");
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        _embeddedTarget = target;
        _embeddedMutationsAllowed = mode == CanvasEmbeddedArtifactMode.Edit;
        try
        {
            await _originalWork.ObserveOriginalAsync(InitializeAsync(cancellationToken));
            return _embeddedSelection ?? throw new InvalidOperationException("The pinned embedded Canvas source was not retained.");
        }
        catch (Exception primary)
        {
            Task? close = null;
            try { close = CloseAndDrainAsync(); await close; }
            catch (Exception cleanup) { throw new AggregateException("Embedded Canvas open and owner cleanup failed.", primary, cleanup); }
            throw;
        }
    }

    internal Task<CanvasEmbeddedArtifactTarget?> EmbeddedChoice => _embeddedChoice.Task;

    internal async Task InitializeEmbeddedChooserAsync(CanvasEmbeddedArtifactChoiceMode mode, CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_initialization is not null || _embeddedTarget is not null || _embeddedChooser || _nativeOwnerAcquired || _closed)
            throw new InvalidOperationException("Create an embedded Canvas chooser from a fresh canonical Canvas surface.");
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        _embeddedChooser = true;
        _embeddedChoiceMode = mode;
        try
        {
            await _originalWork.ObserveOriginalAsync(InitializeAsync(cancellationToken));
        }
        catch (Exception primary)
        {
            _embeddedChoice.TrySetResult(null);
            try { await CloseAndDrainAsync(); }
            catch (Exception cleanup) { throw new AggregateException("Embedded Canvas chooser initialization and cleanup failed.", primary, cleanup); }
            throw;
        }
    }

    private async Task<CanvasEmbeddedArtifactTarget> SelectEmbeddedArtifactAsync(CanvasLaunchRow selectedRow,
        CancellationToken cancellationToken)
    {
        if (!_embeddedChooser || _embeddedChoice.Task.IsCompleted || _embeddedChoiceMode == CanvasEmbeddedArtifactChoiceMode.CreateNew || _closed || _launchSurface?.IsEffectivelyVisible != true ||
            !_launchRows.Any(row => ReferenceEquals(row, selectedRow)))
            throw new InvalidOperationException("Choose a current Canvas from the embedded Canvas launcher.");

        var workspace = _workspace ?? throw new UnauthorizedAccessException("The original Files workspace is unavailable.");
        var authority = Get<NativeFilesWorkspaceAuthority>();
        var currentWorkspace = await Original(() => authority.GetCurrentAsync(cancellationToken));
        if (currentWorkspace is not { } current || current.Actor != workspace.Actor ||
            !ReferenceEquals(current.Provider, workspace.Provider) || current.Configuration.StoreId != workspace.Configuration.StoreId)
            throw new UnauthorizedAccessException("The original Files workspace changed before Canvas selection.");

        var currentRow = await Original(() => workspace.Provider.GetAsync(selectedRow.Item.Id, cancellationToken));
        if (!currentRow.IsSuccess || currentRow.Value!.Kind != HostedItemKind.Artifact ||
            currentRow.Value.ParentId != selectedRow.Item.ParentId ||
            currentRow.Value.CurrentRevisionId != selectedRow.Item.CurrentRevisionId)
            throw new InvalidOperationException("The selected Canvas row changed. Refresh the launcher and choose its current version.");
        var filesRevision = currentRow.Value.CurrentRevisionId
            ?? throw new InvalidDataException("The selected Canvas Files item has no current revision.");
        var metadata = await Original(() => _files!.ResolveEmbeddedArtifactMetadataAsync(
            selectedRow.Item.Id, workspace.Configuration.StoreId, filesRevision, cancellationToken));
        if (await Original(() => authority.GetCurrentAsync(cancellationToken)) is not { } after ||
            after.Actor != workspace.Actor || !ReferenceEquals(after.Provider, workspace.Provider) ||
            after.Configuration.StoreId != workspace.Configuration.StoreId)
            throw new UnauthorizedAccessException("The original Files workspace changed while selecting a Canvas.");
        var target = new CanvasEmbeddedArtifactTarget(metadata.ArtifactId, metadata.ArtifactRevisionId).Validate();
        _embeddedChoiceWitness = new(workspace.Configuration.StoreId, selectedRow.Item.Id,
            filesRevision, target);
        return target;
    }

    internal async Task RevalidateEmbeddedChoiceAsync(CanvasEmbeddedArtifactTarget target, CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_embeddedChooser || _closed || _embeddedChoiceWitness is not { } witness || witness.Target != target)
            throw new InvalidOperationException("This Canvas choice was not issued by the current embedded chooser.");
        var workspace = _workspace ?? throw new UnauthorizedAccessException("The original Files workspace is unavailable.");
        var authority = Get<NativeFilesWorkspaceAuthority>();
        if (await Original(() => authority.GetCurrentAsync(cancellationToken)) is not { } current ||
            current.Actor != workspace.Actor || !ReferenceEquals(current.Provider, workspace.Provider) ||
            current.Configuration.StoreId != witness.StoreId)
            throw new UnauthorizedAccessException("The original Files workspace changed after Canvas selection.");
        var item = await Original(() => workspace.Provider.GetForOriginalStoreAsync(
            witness.StoreId, witness.FileId, cancellationToken));
        if (!item.IsSuccess || item.Value!.CurrentRevisionId != witness.FilesRevisionId)
            throw new InvalidOperationException("The selected Canvas Files revision changed; choose it again.");
        var metadata = await Original(() => _files!.ResolveEmbeddedArtifactMetadataAsync(
            witness.FileId, witness.StoreId, witness.FilesRevisionId, cancellationToken));
        if (metadata.ArtifactId != target.ArtifactId || metadata.ArtifactRevisionId != target.ExpectedRevisionId)
            throw new InvalidOperationException("The selected Canvas identity or app revision is no longer current.");
        if (await Original(() => authority.GetCurrentAsync(cancellationToken)) is not { } after ||
            after.Actor != workspace.Actor || !ReferenceEquals(after.Provider, workspace.Provider) ||
            after.Configuration.StoreId != witness.StoreId)
            throw new UnauthorizedAccessException("The original Files workspace changed while revalidating Canvas selection.");
    }

    internal void CompleteEmbeddedChoice(CanvasEmbeddedArtifactTarget? target)
    {
        if (!_embeddedChooser) throw new InvalidOperationException("This Canvas surface is not an embedded chooser.");
        if (target is not null)
        {
            target.Validate();
            if (_embeddedChoiceWitness is not { } witness || witness.Target != target)
                throw new InvalidOperationException("The Canvas choice does not match this owner's current selection witness.");
        }
        _embeddedChoice.TrySetResult(target);
    }

    internal CanvasEmbeddedArtifactTarget PrepareEmbeddedCreationChoice(CanvasFilesOpenResult opened, HostedItemId fileId)
    {
        ArgumentNullException.ThrowIfNull(opened);
        if (!_embeddedChooser || _embeddedChoiceMode == CanvasEmbeddedArtifactChoiceMode.SelectExisting)
            throw new InvalidOperationException("This Canvas surface is not accepting a create result.");
        var target = new CanvasEmbeddedArtifactTarget(opened.Artifact.ArtifactId, opened.Artifact.RevisionId).Validate();
        _embeddedChoiceWitness = new(opened.StoreId, fileId, opened.CasRevisionId, target);
        return target;
    }

    internal void CancelEmbeddedChoice()
    {
        if (_embeddedChooser) _embeddedChoice.TrySetResult(null);
    }

    internal sealed record CanvasEmbeddedArtifactChoiceWitness(Guid StoreId, HostedItemId FileId,
        FilesRevisionId FilesRevisionId, CanvasEmbeddedArtifactTarget Target);

    internal Task RevalidateEmbeddedArtifactAsync(CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        return _originalWork.RunOriginalAsync(RevalidateEmbeddedArtifactOriginalAsync, cancellationToken);
    }

    private async Task RevalidateEmbeddedArtifactOriginalAsync(CancellationToken cancellationToken)
    {
        var target = _embeddedTarget ?? throw new InvalidOperationException("This Canvas surface is not an embedded artifact session.");
        var selection = _embeddedSelection ?? throw new InvalidOperationException("The embedded Canvas source was not opened.");
        var workspace = _workspace ?? throw new UnauthorizedAccessException("The original Files workspace is unavailable.");
        await RequireOriginalOwnerAsync(workspace, selection.Opened.StoreId, cancellationToken);
        var current = await Original(() => _files!.OpenAsync(selection.FileId, selection.Opened.StoreId, cancellationToken));
        if (current.StoreId != selection.Opened.StoreId || current.CasRevisionId != selection.Opened.CasRevisionId ||
            current.Artifact.ArtifactId != target.ArtifactId || current.Artifact.RevisionId != target.ExpectedRevisionId ||
            current.Artifact.RevisionId != selection.Opened.Artifact.RevisionId)
            throw new InvalidOperationException("The embedded Canvas source changed; reopen the current revision from its owner.");
        await RequireOriginalOwnerAsync(workspace, selection.Opened.StoreId, cancellationToken);
    }

    private async Task OpenEmbeddedTargetAsync(CanvasEmbeddedArtifactTarget target, CancellationToken cancellationToken)
    {
        var workspace = _workspace ?? throw new UnauthorizedAccessException("The original Files workspace is unavailable.");
        var files = _files ?? throw new UnauthorizedAccessException("The original Canvas Files owner is unavailable.");
        await RequireOriginalOwnerAsync(workspace, workspace.Configuration.StoreId, cancellationToken);
        var selection = await Original(() => files.OpenEmbeddedArtifactAsync(
            target.ArtifactId, target.ExpectedRevisionId, cancellationToken));
        if (selection.Opened.StoreId != workspace.Configuration.StoreId ||
            selection.Opened.Artifact.ArtifactId != target.ArtifactId ||
            selection.Opened.Artifact.RevisionId != target.ExpectedRevisionId)
            throw new InvalidOperationException("The embedded Canvas identity or expected revision changed before open.");
        _embeddedSelection = selection;
        await OpenAsync(selection.FileId, selection.Opened, cancellationToken);
        await RevalidateEmbeddedArtifactOriginalAsync(cancellationToken);
    }
}
