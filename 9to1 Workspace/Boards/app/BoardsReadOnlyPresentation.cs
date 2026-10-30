using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;

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
    private readonly CuiThemePreferenceContext? _themeContext;
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
        _themeContext = embeddedArtifactHost?.ThemeContext;
        Content = content;
        _embeddedArtifacts = new BoardsEmbeddedArtifactSurface(embeddedArtifactHost, viewModel, content);
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
    public Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default) =>
        _disposed ? Task.FromResult(true) : _embeddedArtifacts.TryPrepareToCloseAsync(cancellationToken);
    public Task<IBoardsPresentationClosePreparation?> PrepareToCloseAsync(CancellationToken cancellationToken = default) =>
        _disposed ? Task.FromResult<IBoardsPresentationClosePreparation?>(CompletedBoardsPresentationClosePreparation.Instance) : _embeddedArtifacts.PrepareToCloseAsync(cancellationToken);

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
                var themeContext = embeddedArtifactHost?.ThemeContext;
                var viewModel = new BoardsViewModel(session, embeddedArtifactHost);
                viewModel.SetLanding(false);
                viewModel.SetEmbeddedPresentation();
                viewModel.Set("StatusText", "Read-only source");
                viewModel.Set("SaveStateText", "Read-only source");
                loader = BoardsCuiDocuments.CreateLoader(themeContext);
                loader.SetBindingContext(viewModel);
                loader.SetActionDispatcher(viewModel);
                var (root, diagnostics) = loader.TryLoad(BoardsCuiDocuments.Load(BoardsCuiDocuments.MainSourceName));
                if (root is null)
                    throw new InvalidOperationException("The compiled Boards CUI could not be loaded: " +
                        string.Join("; ", diagnostics.Select(item => item.ToString())));

                using var themeScope = BoardsTheme.UseSurface(root, themeContext);
                loader.WireBindings(root);
                viewModel.Attach(root);
                ThemeApplier.ApplyChrome(root, applyResources: themeContext is null);
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
        ViewModel.ToolbarChanged += RefreshToolbar;
        _surfaceLifecycle = new BoardsEmbeddedSurfaceLifecycle(root, ViewModel, QueueRender);
        if (_themeContext is not null) _themeContext.Changed += OnOwnerThemeChanged;
    }

    private void OnOwnerThemeChanged(object? sender, EventArgs args)
    {
        void Refresh()
        {
            if (_disposed || _themeContext is null) return;
            using var themeScope = BoardsTheme.UseSurface(Content, _themeContext);
            ViewModel.RefreshOwnerThemeBindings();
            ThemeApplier.ApplyChrome(Content, applyResources: false);
            QueueRender();
        }
        if (Dispatcher.UIThread.CheckAccess()) Refresh(); else Dispatcher.UIThread.Invoke(Refresh);
    }

    private void QueueRender()
    {
        if (_disposed) return;
        _renderQueue?.Queue();
    }

    private async Task RenderAsync()
    {
        if (_disposed || _surfaceLifecycle?.IsReady != true) return;
        using var themeScope = BoardsTheme.UseSurface(Content, _themeContext);
        if (BoardsCuiControlFinder.FindByAutomationId<StackPanel>(Content, "BlocksHost") is { } blocks)
            await BlockRenderer.RebuildAsync(blocks, ViewModel);
        if (BoardsCuiControlFinder.FindByAutomationId<StackPanel>(Content, "NavHost") is { } nav)
            NavBuilder.Rebuild(nav, ViewModel);
        RefreshToolbar();
        if (BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(Content, "EmbeddedArtifactHost") is { } embedded)
            await _embeddedArtifacts.RenderAsync(embedded);
        ViewModel.RefreshCuiContext();
        if (BoardsCuiControlFinder.FindByAutomationId<Panel>(Content, "InkOverlayLayer") is { } ink)
            await (_surfaceLifecycle!.InkInteraction?.RenderAsync() ?? BoardInkOverlayRenderer.RenderReadOnlyAsync(ink, ViewModel));
    }

    private void RefreshToolbar()
    {
        if (_disposed) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(RefreshToolbar); return; }
        using var themeScope = BoardsTheme.UseSurface(Content, _themeContext);
        if (BoardsCuiControlFinder.FindByAutomationId<StackPanel>(Content, "ToolbarHost") is { } toolbar)
            ToolbarBuilder.Rebuild(toolbar, ViewModel);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (!await TryPrepareToCloseAsync().ConfigureAwait(false))
            throw new InvalidOperationException("The embedded Write page refused to close; its draft remains open.");
        _disposed = true;
        if (_themeContext is not null) _themeContext.Changed -= OnOwnerThemeChanged;
        ViewModel.RebuildRequested -= QueueRender;
        ViewModel.NavRebuildRequested -= QueueRender;
        ViewModel.SelectionChanged -= QueueRender;
        ViewModel.ToolbarChanged -= RefreshToolbar;
        _renderQueue?.Dispose();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _surfaceLifecycle?.Dispose();
        });
        await (_surfaceLifecycle?.DrainAsync() ?? Task.CompletedTask).ConfigureAwait(false);
        await (_renderQueue?.DrainAsync() ?? Task.CompletedTask).ConfigureAwait(false);
        await _embeddedArtifacts.DisposeAsync().ConfigureAwait(false);
        await _loader.WhenActionsIdleAsync().ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ViewModel.Dispose();
            _loader.Dispose();
        });
        await _session.DisposeAsync().ConfigureAwait(false);
    }
}
