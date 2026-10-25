using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    private bool _disposed;
    private bool _renderQueued;

    private BoardsReadOnlyPresentation(HavenBoardDocument document, ContractSessionAdapter session,
        BoardsViewModel viewModel, CuiControlLoader loader, Control content)
    {
        _documentBytes = JsonSerializer.SerializeToUtf8Bytes(document, DocumentJson);
        _session = session;
        ViewModel = viewModel;
        _loader = loader;
        Content = content;
    }

    private readonly byte[] _documentBytes;

    /// <summary>Returns a fresh detached document copy on each read.</summary>
    public HavenBoardDocument Document => HavenBoardDocumentCodec.Deserialize(_documentBytes);
    public BoardsViewModel ViewModel { get; }
    public Control Content { get; }

    public static async Task<BoardsReadOnlyPresentation> OpenReadOnlyDocumentAsync(
        ReadOnlyMemory<byte> verifiedDocument, CancellationToken ct = default)
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
                var viewModel = new BoardsViewModel(session);
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
                var host = new BoardsReadOnlyPresentation(parsed, session, viewModel, loader, root);
                host.WireSurface(root);
                host.QueueRender();
                return host;
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
        if (FindByAutomationId<TextBox>(root, "NavSearchBox") is { } search)
            search.TextChanged += (_, _) => ViewModel.SearchText = search.Text ?? string.Empty;
        if (FindByAutomationId<Canvas>(root, "InkOverlayLayer") is { } ink)
            ink.SizeChanged += (_, _) => QueueRender();
        ViewModel.RebuildRequested += QueueRender;
        ViewModel.NavRebuildRequested += QueueRender;
        ViewModel.SelectionChanged += QueueRender;
    }

    private void QueueRender()
    {
        if (_disposed || _renderQueued) return;
        _renderQueued = true;
        Dispatcher.UIThread.Post(async () =>
        {
            try { await RenderAsync(); }
            catch (Exception error)
            {
                if (!_disposed) ViewModel.Set("StatusText", "Boards view could not be refreshed: " + error.Message.Split('\n')[0]);
            }
        });
    }

    private async Task RenderAsync()
    {
        _renderQueued = false;
        if (_disposed) return;
        var blocks = FindByAutomationId<StackPanel>(Content, "BlocksHost");
        if (blocks is not null) await BlockRenderer.RebuildAsync(blocks, ViewModel);
        if (FindByAutomationId<StackPanel>(Content, "NavHost") is { } nav)
            NavBuilder.Rebuild(nav, ViewModel);
        if (FindByAutomationId<StackPanel>(Content, "ToolbarHost") is { } toolbar)
            ToolbarBuilder.Rebuild(toolbar, ViewModel);
        ViewModel.RefreshCuiContext();
        if (FindByAutomationId<Canvas>(Content, "InkOverlayLayer") is { } ink)
            await BoardInkOverlayRenderer.RenderReadOnlyAsync(ink, ViewModel);
    }

    private static T? FindByAutomationId<T>(Control root, string id) where T : Control
    {
        if (root is T candidate && AutomationProperties.GetAutomationId(candidate) == id) return candidate;
        foreach (var child in root.GetVisualDescendants().OfType<Control>())
            if (child is T control && AutomationProperties.GetAutomationId(control) == id) return control;
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        ViewModel.RebuildRequested -= QueueRender;
        ViewModel.NavRebuildRequested -= QueueRender;
        ViewModel.SelectionChanged -= QueueRender;
        await _loader.WhenActionsIdleAsync().ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(_loader.Dispose);
        await _session.DisposeAsync().ConfigureAwait(false);
    }
}
