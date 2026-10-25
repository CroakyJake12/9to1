using Avalonia.Controls;
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

    public async Task RenderAsync(Panel surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        await _gate.WaitAsync().ConfigureAwait(true);
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
                var opened = await host.OpenAsync(reference, mode).ConfigureAwait(true);
                if (!Equals(opened.CurrentReference, reference))
                {
                    await CloseUnpublishedAsync(opened).ConfigureAwait(true);
                    throw new InvalidDataException("The Write owner did not open the pinned artifact revision.");
                }
                if (!opened.IsPublicationCurrent)
                {
                    await CloseUnpublishedAsync(opened).ConfigureAwait(true);
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

    public async Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return true;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed) return true;
            return await RetireCurrentAsync(restoreSelection: false, cancellationToken).ConfigureAwait(false);
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
            prepared = await session.TryPrepareToCloseAsync(cancellationToken).ConfigureAwait(true);
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
                    currentReference, cancellationToken).ConfigureAwait(true);
            }
            else
            {
                viewModel.Set("StatusText", "The Write source revision changed while this Board was read-only; reopen it from Home to refresh the link.");
                return false;
            }
        }

        _surface?.Children.Remove(session.Content);
        await session.CloseAndDrainAsync().ConfigureAwait(true);
        await session.DisposeAsync().ConfigureAwait(true);
        _session = null;
        _pageId = null;
        _sectionId = null;
        _reference = null;
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        var disposeGate = false;
        try
        {
            if (_disposed) return;
            if (_session is not null && !await RetireCurrentAsync(restoreSelection: false).ConfigureAwait(false))
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
