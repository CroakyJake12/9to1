using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Apps.Boards.Contract;
using Haven.Application;

namespace CakeOS.Apps.Boards.App;

/// <summary>Owns the active external page control while preserving the source owner's identity and close gate.</summary>
internal sealed class BoardsEmbeddedArtifactSurface(IBoardsEmbeddedArtifactHost? host, BoardsViewModel viewModel)
    : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IBoardsEmbeddedArtifactSession? _session;
    private string? _pageId;
    private string? _sectionId;
    private BoardEmbeddedArtifactReference? _reference;
    private BoardEmbeddedArtifactMode _mode;
    private Panel? _surface;
    private bool _disposed;

    public Task RenderAsync(Panel surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (Dispatcher.UIThread.CheckAccess()) return RenderOnUiThreadAsync(surface);
        return DispatchAsync(() => RenderOnUiThreadAsync(surface));
    }

    private async Task RenderOnUiThreadAsync(Panel surface)
    {
        Dispatcher.UIThread.VerifyAccess();
        await _gate.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _surface = surface;
            var pageId = viewModel.SelectedPageId;
            var sectionId = viewModel.SelectedSectionId;
            var pageType = viewModel.SelectedPageType;
            var reference = viewModel.SelectedEmbeddedArtifact;
            if (!string.Equals(pageType, HavenRichPageTypes.Write, StringComparison.Ordinal))
            {
                if (!await RetireCurrentAsync(restoreSelection: true).ConfigureAwait(true)) return;
                surface.Children.Clear();
                viewModel.SetEmbeddedPageStatus(false, string.Empty);
                return;
            }

            viewModel.SetEmbeddedPageStatus(false, string.Empty);
            var mode = viewModel.IsEditMode && viewModel.CanEdit
                ? BoardEmbeddedArtifactMode.Edit
                : BoardEmbeddedArtifactMode.View;
            if (_session is not null && _pageId == pageId && Equals(_reference, reference) && _mode == mode)
            {
                if (_session.IsPublicationCurrent)
                {
                    if (!surface.Children.Contains(_session.Content))
                    {
                        surface.Children.Clear();
                        surface.Children.Add(_session.Content);
                    }
                    return;
                }
                surface.Children.Clear();
                viewModel.SetEmbeddedPageStatus(true, "The linked Write document is no longer available under the current source permissions.");
                return;
            }

            if (!await RetireCurrentAsync(restoreSelection: true).ConfigureAwait(true)) return;
            surface.Children.Clear();
            if (host is null)
            {
                viewModel.SetEmbeddedPageStatus(true, "Open this Board in Home to access its linked Write document.");
                return;
            }
            if (reference is null)
            {
                viewModel.SetEmbeddedPageStatus(true, "This Write page has no owner reference. Attach a Write document from the Home Library.");
                return;
            }

            try
            {
                reference.Validate();
                if (!string.Equals(reference.ApplicationId, "write", StringComparison.Ordinal))
                    throw new InvalidDataException("The Board page does not identify a Write artifact.");
                var opened = await host.OpenAsync(reference, mode);
                if (!IsCurrentSelection(pageId, sectionId, reference, mode))
                {
                    await CloseUnpublishedAsync(opened);
                    return;
                }
                if (!Equals(opened.CurrentReference, reference))
                {
                    await CloseUnpublishedAsync(opened);
                    throw new InvalidDataException("The Write owner did not open the pinned artifact revision.");
                }
                if (!opened.IsPublicationCurrent)
                {
                    await CloseUnpublishedAsync(opened);
                    throw new UnauthorizedAccessException("Write access changed before the embedded page could be shown.");
                }
                _session = opened;
                _pageId = pageId;
                _sectionId = sectionId;
                _reference = reference;
                _mode = mode;
                surface.Children.Add(opened.Content);
                viewModel.SetEmbeddedPageStatus(false, string.Empty);
            }
            catch (Exception error)
            {
                surface.Children.Clear();
                viewModel.SetEmbeddedPageStatus(true,
                    "The linked Write document could not be opened: " + FirstLine(error.Message));
            }
        }
        finally { _gate.Release(); }
    }

    public Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        if (Dispatcher.UIThread.CheckAccess()) return TryPrepareToCloseOnUiThreadAsync(cancellationToken);
        return DispatchAsync(() => TryPrepareToCloseOnUiThreadAsync(cancellationToken));
    }

    private async Task<bool> TryPrepareToCloseOnUiThreadAsync(CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return true;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_disposed) return true;
            return await RetireCurrentAsync(restoreSelection: false, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> RetireCurrentAsync(bool restoreSelection,
        CancellationToken cancellationToken = default)
    {
        if (_session is not { } session) return true;
        var pageId = _pageId;
        var sectionId = _sectionId;
        bool prepared;
        try
        {
            prepared = await session.TryPrepareToCloseAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            prepared = false;
            viewModel.Set("StatusText", "The Write page could not be closed: " + FirstLine(error.Message));
        }
        if (!prepared)
        {
            if (restoreSelection && sectionId is not null && pageId is not null &&
                viewModel.SelectedPageId != pageId)
                viewModel.SelectPage(sectionId, pageId);
            viewModel.Set("StatusText", "The Write page has unsaved or unavailable changes; it remains open.");
            return false;
        }

        var currentReference = session.CurrentReference;
        if (_reference is not null && !Equals(_reference, currentReference))
        {
            if (viewModel.CanEdit && viewModel.IsEditMode && _pageId is not null &&
                viewModel.Session is ContractSessionAdapter adapter)
            {
                await adapter.SetEmbeddedArtifactAsync(_pageId, HavenRichPageTypes.Write,
                    currentReference, cancellationToken);
            }
            else
            {
                viewModel.Set("StatusText", "The Write source revision changed while this Board was read-only; reopen it from Home to refresh the link.");
                return false;
            }
        }

        _surface?.Children.Remove(session.Content);
        await session.CloseAndDrainAsync();
        await session.DisposeAsync();
        _session = null;
        _pageId = null;
        _sectionId = null;
        _reference = null;
        return true;
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
                throw new InvalidOperationException("The embedded Write page refused to close; its draft remains open.");
            _disposed = true;
            disposeGate = true;
        }
        finally
        {
            _gate.Release();
            if (disposeGate) _gate.Dispose();
        }
    }

    private bool IsCurrentSelection(string pageId, string? sectionId,
        BoardEmbeddedArtifactReference reference, BoardEmbeddedArtifactMode mode) =>
        string.Equals(viewModel.SelectedPageId, pageId, StringComparison.Ordinal) &&
        string.Equals(viewModel.SelectedSectionId, sectionId, StringComparison.Ordinal) &&
        string.Equals(viewModel.SelectedPageType, HavenRichPageTypes.Write, StringComparison.Ordinal) &&
        Equals(viewModel.SelectedEmbeddedArtifact, reference) &&
        (viewModel.IsEditMode && viewModel.CanEdit ? BoardEmbeddedArtifactMode.Edit : BoardEmbeddedArtifactMode.View) == mode;

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
        try { await session.CloseAndDrainAsync().ConfigureAwait(true); }
        finally { await session.DisposeAsync().ConfigureAwait(true); }
    }
}
