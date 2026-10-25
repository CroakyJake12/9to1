using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;
using Haven.Application;

namespace CakeOS.Apps.Boards.App;

/// <summary>Owns the active external page control while preserving the source owner's identity and close gate.</summary>
internal sealed class BoardsEmbeddedArtifactSurface(IBoardsEmbeddedArtifactHost? host, BoardsViewModel viewModel, Control root)
    : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Control _root = root ?? throw new ArgumentNullException(nameof(root));
    private IBoardsEmbeddedArtifactSession? _session;
    private string? _pageId;
    private string? _sectionId;
    private string? _pageType;
    private BoardEmbeddedArtifactReference? _reference;
    private BoardEmbeddedArtifactMode _mode;
    private CuiContainerControl? _surface;
    private SurfaceClosePreparation? _pendingPreparation;
    private FailedSelection? _failedSelection;
    private bool _disposed;

    private sealed record FailedSelection(string PageId, string? SectionId, string PageType,
        BoardEmbeddedArtifactReference? Reference, BoardEmbeddedArtifactMode Mode)
    {
        public bool Matches(string? pageId, string? sectionId, string pageType,
            BoardEmbeddedArtifactReference? reference, BoardEmbeddedArtifactMode mode) =>
            string.Equals(PageId, pageId, StringComparison.Ordinal) &&
            string.Equals(SectionId, sectionId, StringComparison.Ordinal) &&
            string.Equals(PageType, pageType, StringComparison.Ordinal) &&
            Equals(Reference, reference) && Mode == mode;
    }

    public Task RenderAsync(CuiContainerControl surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (Dispatcher.UIThread.CheckAccess()) return RenderOnUiThreadAsync(surface);
        return DispatchAsync(() => RenderOnUiThreadAsync(surface));
    }

    private async Task RenderOnUiThreadAsync(CuiContainerControl surface)
    {
        Dispatcher.UIThread.VerifyAccess();
        await _gate.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pendingPreparation is not null) return;
            _surface = surface;
            var pageId = viewModel.SelectedPageId;
            var sectionId = viewModel.SelectedSectionId;
            var pageType = viewModel.SelectedPageType;
            var reference = viewModel.SelectedEmbeddedArtifact;
            var mode = viewModel.IsEditMode && viewModel.CanEdit
                ? BoardEmbeddedArtifactMode.Edit
                : BoardEmbeddedArtifactMode.View;
            if (_failedSelection is { } failed)
            {
                if (failed.Matches(pageId, sectionId, pageType, reference, mode)) return;
                _failedSelection = null;
            }
            if (!IsEmbeddedArtifactPage(pageType))
            {
                if (!await RetireCurrentAsync(restoreSelection: true).ConfigureAwait(true)) return;
                viewModel.SetEmbeddedPageStatus(false, string.Empty);
                return;
            }

            viewModel.SetEmbeddedPageStatus(false, string.Empty);
            if (_session is not null && _pageId == pageId && Equals(_reference, reference) && _mode == mode)
            {
                try
                {
                    if (await _session.RevalidateAsync() && _session.IsPublicationCurrent)
                    {
                        var currentSurface = BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(_root, "EmbeddedArtifactHost") ?? surface;
                        if (!currentSurface.AuthoredChildren.Contains(_session.Content))
                            currentSurface.AddChild(_session.Content);
                        return;
                    }
                }
                catch (Exception error)
                {
                    var currentSurface = BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(_root, "EmbeddedArtifactHost") ?? surface;
                    currentSurface.RemoveChild(_session.Content);
                    _failedSelection = new FailedSelection(pageId!, sectionId, pageType, reference, mode);
                    viewModel.SetEmbeddedPageStatus(true,
                        "The linked artifact could not be revalidated: " + FirstLine(error.Message));
                    return;
                }
                var unavailableSurface = BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(_root, "EmbeddedArtifactHost") ?? surface;
                unavailableSurface.RemoveChild(_session.Content);
                _failedSelection = new FailedSelection(pageId!, sectionId, pageType, reference, mode);
                viewModel.SetEmbeddedPageStatus(true, "The linked artifact is unavailable at its pinned revision or current permissions. Reopen it from its owner to refresh access.");
                return;
            }

            if (!await RetireCurrentAsync(restoreSelection: true).ConfigureAwait(true)) return;
            if (host is null)
            {
                viewModel.SetEmbeddedPageStatus(true, "Open this Board in Home to access its linked artifact.");
                return;
            }
            if (reference is null)
            {
                viewModel.SetEmbeddedPageStatus(true, "This embedded page has no owner reference. Reattach the artifact from its owning app.");
                return;
            }

            try
            {
                reference.Validate();
                if (!string.Equals(reference.ApplicationId, ApplicationIdFor(pageType), StringComparison.Ordinal))
                    throw new InvalidDataException("The Board page type does not match its artifact owner.");
                var opened = await host.OpenAsync(reference, mode);
                if (!IsCurrentSelection(pageId, sectionId, pageType, reference, mode))
                {
                    await CloseUnpublishedAsync(opened);
                    return;
                }
                if (!Equals(opened.CurrentReference, reference))
                {
                    await CloseUnpublishedAsync(opened);
                    throw new InvalidDataException("The artifact owner did not open the pinned revision.");
                }
                if (!await opened.RevalidateAsync() || !opened.IsPublicationCurrent)
                {
                    await CloseUnpublishedAsync(opened);
                    throw new UnauthorizedAccessException("The artifact revision or access changed before the embedded page could be shown.");
                }
                if (!IsCurrentSelection(pageId, sectionId, pageType, reference, mode))
                {
                    await CloseUnpublishedAsync(opened);
                    return;
                }
                // Resolve the stable CUI container wrapper, then publish through
                // its authored-child API so later binding reconciliation retains
                // this externally owned control as a real CUI child.
                if (!IsCurrentSelection(pageId, sectionId, pageType, reference, mode))
                {
                    await CloseUnpublishedAsync(opened);
                    return;
                }
                var currentSurface = BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(_root, "EmbeddedArtifactHost");
                if (currentSurface is null)
                {
                    await CloseUnpublishedAsync(opened);
                    viewModel.SetEmbeddedPageStatus(true,
                        "The linked page host is not available in the current Board view. Select the page again to reopen it.");
                    return;
                }
                try { currentSurface.AddChild(opened.Content); }
                catch
                {
                    await CloseUnpublishedAsync(opened);
                    throw;
                }
                _session = opened;
                _pageId = pageId;
                _sectionId = sectionId;
                _pageType = pageType;
                _reference = reference;
                _mode = mode;
            }
            catch (Exception error)
            {
                _failedSelection = new FailedSelection(pageId!, sectionId, pageType, reference, mode);
                viewModel.SetEmbeddedPageStatus(true,
                    "The linked artifact could not be opened: " + FirstLine(error.Message));
            }
        }
        finally { _gate.Release(); }
    }

    public Task<IBoardsPresentationClosePreparation?> PrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        if (Dispatcher.UIThread.CheckAccess()) return PrepareToCloseOnUiThreadAsync(cancellationToken);
        return DispatchAsync(() => PrepareToCloseOnUiThreadAsync(cancellationToken));
    }

    public async Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        var preparation = await PrepareToCloseAsync(cancellationToken);
        if (preparation is null) return false;
        try
        {
            if (!preparation.IsCurrent) return false;
            await preparation.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await preparation.AbortAsync();
            throw;
        }
        finally { await preparation.DisposeAsync(); }
    }

    private async Task<IBoardsPresentationClosePreparation?> PrepareToCloseOnUiThreadAsync(CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return null;
        await _gate.WaitAsync(cancellationToken);
        try { return await PrepareToCloseCoreAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    private async Task<IBoardsPresentationClosePreparation?> PrepareToCloseCoreAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return null;
        if (_pendingPreparation is not null) return _pendingPreparation;
        if (_session is null)
        {
            _pendingPreparation = new SurfaceClosePreparation(this, null, null);
            return _pendingPreparation;
        }

        var session = _session;
        var pageId = _pageId;
        var sectionId = _sectionId;
        var pageType = _pageType;
        var reference = _reference;
        var mode = _mode;
        if (pageId is null || pageType is null || reference is null)
            return null;
        IBoardsEmbeddedArtifactClosePreparation? ownerPreparation = null;
        try
        {
            ownerPreparation = await session.PrepareToCloseAsync(cancellationToken);
            if (ownerPreparation is null || !ownerPreparation.IsCurrent ||
                !IsRetainedSessionCurrent(session, pageId, sectionId, pageType, reference, mode))
            {
                if (ownerPreparation is not null)
                {
                    await ownerPreparation.AbortAsync();
                    await ownerPreparation.DisposeAsync();
                }
                viewModel.Set("StatusText", "The embedded artifact owner refused to prepare this close; its page remains open.");
                return null;
            }
            _pendingPreparation = new SurfaceClosePreparation(this, session, ownerPreparation,
                pageId, sectionId, pageType, reference, mode);
            return _pendingPreparation;
        }
        catch
        {
            if (ownerPreparation is not null)
            {
                await ownerPreparation.AbortAsync();
                await ownerPreparation.DisposeAsync();
            }
            throw;
        }
    }

    private async Task<bool> RetireCurrentAsync(bool restoreSelection,
        CancellationToken cancellationToken = default)
    {
        var oldPageId = _pageId;
        var oldSectionId = _sectionId;
        IBoardsPresentationClosePreparation? preparation = null;
        try
        {
            preparation = await PrepareToCloseCoreAsync(cancellationToken);
            if (preparation is null)
            {
                RestoreSelectionIfNeeded(restoreSelection, oldSectionId, oldPageId);
                return false;
            }
            await CommitPreparedCloseCoreAsync((SurfaceClosePreparation)preparation, restoreSelection, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (preparation is not null) await AbortPreparedCloseCoreAsync((SurfaceClosePreparation)preparation);
            RestoreSelectionIfNeeded(restoreSelection, oldSectionId, oldPageId);
            throw;
        }
        catch (Exception error)
        {
            if (preparation is not null)
                await AbortPreparedCloseCoreAsync((SurfaceClosePreparation)preparation);
            RestoreSelectionIfNeeded(restoreSelection, oldSectionId, oldPageId);
            viewModel.Set("StatusText", "The embedded page remains open: " + FirstLine(error.Message));
            return false;
        }
    }

    private void RestoreSelectionIfNeeded(bool restoreSelection, string? sectionId, string? pageId)
    {
        if (restoreSelection && sectionId is not null && pageId is not null &&
            viewModel.SelectedPageId != pageId)
            viewModel.SelectPage(sectionId, pageId);
    }

    private async Task CommitPreparedCloseAsync(SurfaceClosePreparation preparation, bool restoreSelection,
        CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        await _gate.WaitAsync(cancellationToken);
        try { await CommitPreparedCloseCoreAsync(preparation, restoreSelection, cancellationToken); }
        finally { _gate.Release(); }
    }

    private async Task CommitPreparedCloseCoreAsync(SurfaceClosePreparation preparation, bool restoreSelection,
        CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(_pendingPreparation, preparation) || _disposed)
            throw new InvalidOperationException("The embedded close preparation is no longer current.");
        var session = preparation.Session;
        var ownerPreparation = preparation.OwnerPreparation;
        if (session is null)
        {
            _pendingPreparation = null;
            preparation.MarkCommitted();
            return;
        }
        var pageId = preparation.PageId;
        var sectionId = preparation.SectionId;
        try
        {
            if (ownerPreparation is null || !ownerPreparation.IsCurrent)
                throw new InvalidOperationException("The artifact owner close preparation is stale.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            if (restoreSelection && sectionId is not null && pageId is not null &&
                viewModel.SelectedPageId != pageId)
                viewModel.SelectPage(sectionId, pageId);
            viewModel.Set("StatusText", "The embedded page could not be closed: " + FirstLine(error.Message));
            throw;
        }

        var currentReference = session.CurrentReference;
        if (_reference is not null && !Equals(_reference, currentReference))
        {
            if (viewModel.CanEdit && viewModel.IsEditMode && _pageId is not null &&
                viewModel.Session is ContractSessionAdapter adapter)
            {
                await adapter.SetEmbeddedArtifactAsync(_pageId!, preparation.PageType,
                    currentReference, cancellationToken);
            }
            else
            {
                viewModel.Set("StatusText", "The linked source revision changed while this Board was read-only; reopen it from Home to refresh the link.");
                throw new InvalidOperationException("The read-only Board cannot advance the linked artifact revision.");
            }
        }

        await ownerPreparation.BeginCloseAsync(cancellationToken);
        (BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(_root, "EmbeddedArtifactHost") ?? _surface)
            ?.RemoveChild(session.Content);
        try { await session.CloseAndDrainAsync(); }
        finally
        {
            try { await ownerPreparation.DisposeAsync(); }
            finally { await session.DisposeAsync(); }
        }
        _session = null;
        _pageId = null;
        _sectionId = null;
        _pageType = null;
        _reference = null;
        _pendingPreparation = null;
        preparation.MarkCommitted();
    }

    private async Task AbortPreparedCloseAsync(SurfaceClosePreparation preparation)
    {
        if (Dispatcher.UIThread.CheckAccess())
            await AbortPreparedCloseOnUiThreadAsync(preparation);
        else
            await DispatchAsync(() => AbortPreparedCloseOnUiThreadAsync(preparation));
    }

    private async Task AbortPreparedCloseOnUiThreadAsync(SurfaceClosePreparation preparation)
    {
        Dispatcher.UIThread.VerifyAccess();
        await _gate.WaitAsync();
        try { await AbortPreparedCloseCoreAsync(preparation); }
        finally { _gate.Release(); }
    }

    private async Task AbortPreparedCloseCoreAsync(SurfaceClosePreparation preparation)
    {
        if (!ReferenceEquals(_pendingPreparation, preparation))
        {
            preparation.MarkAborted();
            return;
        }
        if (preparation.OwnerPreparation is { } ownerPreparation)
        {
            await ownerPreparation.AbortAsync();
            await ownerPreparation.DisposeAsync();
        }
        _pendingPreparation = null;
        preparation.MarkAborted();
    }

    private bool IsPreparationCurrent(SurfaceClosePreparation preparation) =>
        !_disposed && ReferenceEquals(_pendingPreparation, preparation) &&
        (preparation.Session is null || ReferenceEquals(_session, preparation.Session)) &&
        (preparation.Session is null || preparation.Reference is { } reference &&
            IsRetainedSessionCurrent(preparation.Session, preparation.PageId, preparation.SectionId,
                preparation.PageType, reference, preparation.Mode)) &&
        (preparation.OwnerPreparation?.IsCurrent ?? true);

    private bool IsRetainedSessionCurrent(IBoardsEmbeddedArtifactSession session, string pageId,
        string? sectionId, string pageType, BoardEmbeddedArtifactReference reference, BoardEmbeddedArtifactMode mode) =>
        ReferenceEquals(_session, session) && _pageId == pageId && _sectionId == sectionId &&
        _pageType == pageType && Equals(_reference, reference) && _mode == mode;

    private sealed class SurfaceClosePreparation(BoardsEmbeddedArtifactSurface owner,
        IBoardsEmbeddedArtifactSession? session, IBoardsEmbeddedArtifactClosePreparation? ownerPreparation,
        string pageId = "", string? sectionId = null, string pageType = "",
        BoardEmbeddedArtifactReference? reference = null, BoardEmbeddedArtifactMode mode = BoardEmbeddedArtifactMode.View)
        : IBoardsPresentationClosePreparation
    {
        private int _finished;
        public IBoardsEmbeddedArtifactSession? Session { get; } = session;
        public IBoardsEmbeddedArtifactClosePreparation? OwnerPreparation { get; } = ownerPreparation;
        public string PageId { get; } = pageId;
        public string? SectionId { get; } = sectionId;
        public string PageType { get; } = pageType;
        public BoardEmbeddedArtifactReference? Reference { get; } = reference;
        public BoardEmbeddedArtifactMode Mode { get; } = mode;
        public bool IsCurrent => Volatile.Read(ref _finished) == 0 && owner.IsPreparationCurrent(this);

        public void MarkCommitted() => Interlocked.Exchange(ref _finished, 1);
        public void MarkAborted() => Interlocked.Exchange(ref _finished, 1);

        public async Task CommitAsync(CancellationToken ct = default)
        {
            if (Interlocked.CompareExchange(ref _finished, 1, 0) != 0)
                throw new InvalidOperationException("The embedded close preparation has already finished.");
            try
            {
                if (Dispatcher.UIThread.CheckAccess())
                    await owner.CommitPreparedCloseAsync(this, restoreSelection: false, ct);
                else
                    await DispatchAsync(() => owner.CommitPreparedCloseAsync(this, restoreSelection: false, ct));
            }
            catch
            {
                Volatile.Write(ref _finished, 0);
                throw;
            }
        }

        public async Task AbortAsync()
        {
            if (Interlocked.CompareExchange(ref _finished, 1, 0) != 0) return;
            try { await owner.AbortPreparedCloseAsync(this); }
            catch { Volatile.Write(ref _finished, 0); throw; }
        }

        public async ValueTask DisposeAsync() => await AbortAsync();
    }

    public ValueTask DisposeAsync()
    {
        if (Dispatcher.UIThread.CheckAccess()) return new ValueTask(DisposeOnUiThreadAsync());
        return new ValueTask(DispatchAsync(DisposeOnUiThreadAsync));
    }

    private async Task DisposeOnUiThreadAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return;
        await _gate.WaitAsync();
        var disposeGate = false;
        try
        {
            if (_disposed) return;
            if (_session is not null && !await RetireCurrentAsync(restoreSelection: false))
                throw new InvalidOperationException("The embedded artifact page refused to close; its draft remains open.");
            _disposed = true;
            disposeGate = true;
        }
        finally
        {
            _gate.Release();
            if (disposeGate) _gate.Dispose();
        }
    }

    private bool IsCurrentSelection(string pageId, string? sectionId, string pageType,
        BoardEmbeddedArtifactReference reference, BoardEmbeddedArtifactMode mode) =>
        string.Equals(viewModel.SelectedPageId, pageId, StringComparison.Ordinal) &&
        string.Equals(viewModel.SelectedSectionId, sectionId, StringComparison.Ordinal) &&
        string.Equals(viewModel.SelectedPageType, pageType, StringComparison.Ordinal) &&
        Equals(viewModel.SelectedEmbeddedArtifact, reference) &&
        (viewModel.IsEditMode && viewModel.CanEdit ? BoardEmbeddedArtifactMode.Edit : BoardEmbeddedArtifactMode.View) == mode;

    private static bool IsEmbeddedArtifactPage(string pageType) =>
        pageType is HavenRichPageTypes.Write or HavenRichPageTypes.Canvas;

    private static string ApplicationIdFor(string pageType) => pageType switch
    {
        HavenRichPageTypes.Write => "write",
        HavenRichPageTypes.Canvas => "canvas",
        _ => throw new InvalidDataException("The Board page type has no embedded application owner.")
    };

    private static async Task DispatchAsync(Func<Task> work)
    {
        await Dispatcher.UIThread.InvokeAsync(work);
    }

    private static async Task<T> DispatchAsync<T>(Func<Task<T>> work)
    {
        return await Dispatcher.UIThread.InvokeAsync(work);
    }

    private static string FirstLine(string value)
    {
        var line = value.Split('\n')[0].Trim();
        return line.Length > 180 ? line[..180] : line;
    }

    private static async Task CloseUnpublishedAsync(IBoardsEmbeddedArtifactSession session)
    {
        IBoardsEmbeddedArtifactClosePreparation? preparation = null;
        try
        {
            preparation = await session.PrepareToCloseAsync().ConfigureAwait(true);
            if (preparation is null || !preparation.IsCurrent) return;
            await preparation.BeginCloseAsync().ConfigureAwait(true);
            await session.CloseAndDrainAsync().ConfigureAwait(true);
        }
        finally
        {
            try
            {
                if (preparation is not null) await preparation.DisposeAsync().ConfigureAwait(true);
            }
            finally { await session.DisposeAsync().ConfigureAwait(true); }
        }
    }
}
