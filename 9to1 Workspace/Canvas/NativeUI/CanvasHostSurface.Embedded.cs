using Avalonia.Threading;
using HavenOS.Files;

namespace HavenOS.Apps.Canvas;

public sealed partial class CanvasHostSurface
{
    private CanvasEmbeddedArtifactTarget? _embeddedTarget;
    private CanvasEmbeddedArtifactOpenResult? _embeddedSelection;
    private bool _embeddedMutationsAllowed;
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
