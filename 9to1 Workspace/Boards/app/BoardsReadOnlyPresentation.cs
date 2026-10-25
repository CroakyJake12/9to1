using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;

namespace CakeOS.Apps.Boards.App;

/// <summary>
/// Embeddable presentation over one validated document revision. Its session is detached from
/// Files and has no writable store; disposal releases UI actions and that detached session.
/// </summary>
public sealed class BoardsReadOnlyPresentation : IAsyncDisposable
{
    private static readonly JsonSerializerOptions DocumentJson = new(JsonSerializerDefaults.Web);
    private readonly ContractSessionAdapter _session;
    private readonly CuiControlLoader _loader;
    private readonly BoardsEmbeddedArtifactSurface _embeddedArtifacts;
    private BoardsPresentationRenderQueue? _renderQueue;
    private BoardsEmbeddedSurfaceLifecycle? _surfaceLifecycle;
    private bool _disposed;

    private BoardsReadOnlyPresentation(HavenBoardDocument document, ContractSessionAdapter session,
        BoardsViewModel viewModel, CuiControlLoader loader, Control content,
        IBoardsEmbeddedArtifactHost? embeddedArtifactHost)
    {
        _documentBytes = JsonSerializer.SerializeToUtf8Bytes(document, DocumentJson);
        _session = session;
        ViewModel = viewModel;
        _loader = loader;
        Content = content;
        _embeddedArtifacts = new BoardsEmbeddedArtifactSurface(embeddedArtifactHost, viewModel);
    }

    private readonly byte[] _documentBytes;

    /// <summary>Returns the detached document supplied when this presentation opened.</summary>
    public HavenBoardDocument Document => HavenBoardDocumentCodec.Deserialize(_documentBytes);
    public Task<HavenBoardDocument> GetCurrentDocumentAsync(CancellationToken cancellationToken = default) =>
        _session.GetDetachedContractDocumentAsync(cancellationToken);
    public BoardsViewModel ViewModel { get; }
    public Control Content { get; }
    public Task WaitUntilReadyAsync(CancellationToken cancellationToken = default) =>
        (_surfaceLifecycle ?? throw new InvalidOperationException("The Boards surface has not been initialized.")).WaitUntilReadyAsync(cancellationToken);
    public Task DrainRenderingAsync() => _renderQueue?.DrainAsync() ?? Task.CompletedTask;

    public static async Task<BoardsReadOnlyPresentation> OpenReadOnlyDocumentAsync(
        ReadOnlyMemory<byte> verifiedDocument, CancellationToken ct = default,
        IBoardsEmbeddedArtifactHost? embeddedArtifactHost = null)
    {
        ct.ThrowIfCancellationRequested();
        var parsed = HavenBoardDocumentCodec.Deserialize(verifiedDocument);
        var session = await ContractSessionAdapter.OpenReadOnlyAsync(parsed).ConfigureAwait(false);
        CuiControlLoader? loader = null;
        try
        {
            return await Dispatcher.UIThread.InvokeAsync<BoardsReadOnlyPresentation>(() =>
            {
                ct.ThrowIfCancellationRequested();
                var viewModel = new BoardsViewModel(session, embeddedArtifactHost);
                viewModel.SetLanding(false);
                viewModel.SetEmbeddedPresentation();
                viewModel.Set("StatusText", "Read-only source");
                viewModel.Set("SaveStateText", "Read-only source");
                loader = new CuiControlLoader(BoardsCuiDocuments.CreateControlRegistry());
                loader.SetBindingContext(viewModel);
                loader.SetActionDispatcher(viewModel);
                var (root, diagnostics) = loader.TryLoad(BoardsCuiDocuments.Load(BoardsCuiDocuments.MainSourceName));
                if (root is null)
                    throw new InvalidOperationException("The compiled Boards CUI could not be loaded: " +
                        string.Join("; ", diagnostics.Select(item => item.ToString())));

                loader.WireBindings(root);
                viewModel.Attach(root);
                ThemeApplier.ApplyChrome(root);
                embeddedArtifactHost?.BindOwningControl(root);
                var presentation = new BoardsReadOnlyPresentation(parsed, session, viewModel, loader, root,
                    embeddedArtifactHost);
                presentation.WireSurface(root);
                presentation.QueueRender();
                return presentation;
            });
        }
        catch
        {
            loader?.Dispose();
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void WireSurface(Control root)
    {
        _renderQueue = new BoardsPresentationRenderQueue(RenderAsync, error =>
        {
            if (!_disposed) ViewModel.Set("StatusText", "Boards view could not be refreshed: " + error.Message.Split('\n')[0]);
        });
        ViewModel.RebuildRequested += QueueRender;
        ViewModel.NavRebuildRequested += QueueRender;
        ViewModel.SelectionChanged += QueueRender;
        _surfaceLifecycle = new BoardsEmbeddedSurfaceLifecycle(root, ViewModel, QueueRender);
    }

    private void QueueRender()
    {
        if (_disposed) return;
        _renderQueue?.Queue();
    }

    private async Task RenderAsync()
    {
        if (_disposed || _surfaceLifecycle?.IsReady != true) return;
        var blocks = BoardsCuiControlFinder.FindByAutomationId<StackPanel>(Content, "BlocksHost");
        if (blocks is null) return;
        await BlockRenderer.RebuildAsync(blocks, ViewModel);
        if (BoardsCuiControlFinder.FindByAutomationId<StackPanel>(Content, "NavHost") is { } nav)
            NavBuilder.Rebuild(nav, ViewModel);
        if (BoardsCuiControlFinder.FindByAutomationId<StackPanel>(Content, "ToolbarHost") is { } toolbar)
            ToolbarBuilder.Rebuild(toolbar, ViewModel);
        if (BoardsCuiControlFinder.FindByAutomationId<Panel>(Content, "EmbeddedArtifactHost") is { } embedded)
            await _embeddedArtifacts.RenderAsync(embedded);
        ViewModel.RefreshCuiContext();
        if (BoardsCuiControlFinder.FindByAutomationId<Panel>(Content, "InkOverlayLayer") is { } ink)
            await (_surfaceLifecycle!.InkInteraction?.RenderAsync() ?? BoardInkOverlayRenderer.RenderReadOnlyAsync(ink, ViewModel));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (!await _embeddedArtifacts.TryPrepareToCloseAsync().ConfigureAwait(false))
            throw new InvalidOperationException("The embedded Write page refused to close; its draft remains open.");
        _disposed = true;
        ViewModel.RebuildRequested -= QueueRender;
        ViewModel.NavRebuildRequested -= QueueRender;
        ViewModel.SelectionChanged -= QueueRender;
        _renderQueue?.Dispose();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _surfaceLifecycle?.Dispose();
        });
        await (_surfaceLifecycle?.DrainAsync() ?? Task.CompletedTask).ConfigureAwait(false);
        await (_renderQueue?.DrainAsync() ?? Task.CompletedTask).ConfigureAwait(false);
        await _embeddedArtifacts.DisposeAsync().ConfigureAwait(false);
        await _loader.WhenActionsIdleAsync().ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(_loader.Dispose);
        await _session.DisposeAsync().ConfigureAwait(false);
    }
}
