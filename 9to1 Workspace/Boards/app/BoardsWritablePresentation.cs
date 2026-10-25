using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;

namespace CakeOS.Apps.Boards.App;

/// <summary>The acknowledged result of one external Home/Files revision save.</summary>
public sealed record BoardsExternalSaveReceipt(
    bool Committed,
    string? NewRevision,
    string? Code = null,
    string? Message = null,
    bool Conflict = false);

/// <summary>Home/Files-owned save boundary. The document bytes are bounded, validated,
/// and detached from the presentation before the callback receives them.</summary>
public delegate ValueTask<BoardsExternalSaveReceipt> BoardsExternalSaveCallback(
    string sourceId,
    string expectedRevision,
    ReadOnlyMemory<byte> validatedDocument,
    CancellationToken cancellationToken);

/// <summary>
/// Editable detached presentation for one Home-authorized source revision. It reuses
/// the standalone Boards domain, editor and compiled CUI while granting persistence only
/// through the explicit, externally owned revision callback.
/// </summary>
public sealed class BoardsWritablePresentation : IAsyncDisposable
{
    private static readonly JsonSerializerOptions DocumentJson = new(JsonSerializerDefaults.Web);
    private readonly ContractSessionAdapter _session;
    private readonly CuiControlLoader _loader;
    private readonly byte[] _initialDocumentBytes;
    private BoardsPresentationRenderQueue? _renderQueue;
    private BoardInkInteractionController? _inkInteraction;
    private BoardsTextAndKeyboardOwner? _inputOwner;
    private readonly Guid _documentId;
    private readonly string? _snapshotBoardId;
    private string _sourceRevision;
    private bool _disposed;

    private BoardsWritablePresentation(
        HavenBoardDocument document,
        string sourceId,
        string sourceRevision,
        ContractSessionAdapter session,
        BoardsViewModel viewModel,
        CuiControlLoader loader,
        Control content)
    {
        _initialDocumentBytes = JsonSerializer.SerializeToUtf8Bytes(document, DocumentJson);
        _documentId = document.DocumentId;
        _snapshotBoardId = document.Snapshot?.Id;
        SourceId = sourceId;
        _sourceRevision = sourceRevision;
        _session = session;
        ViewModel = viewModel;
        _loader = loader;
        Content = content;
    }

    public string SourceId { get; }
    public string SourceRevision => _sourceRevision;
    public bool HasUnsavedChanges => _session.HasUnsavedChanges;
    public BoardsExternalSaveReceipt? LastSaveReceipt { get; private set; }
    public BoardsViewModel ViewModel { get; }
    public Control Content { get; }
    /// <summary>Returns the detached document supplied when this presentation opened.</summary>
    public HavenBoardDocument Document => HavenBoardDocumentCodec.Deserialize(_initialDocumentBytes);
    public Task<HavenBoardDocument> GetCurrentDocumentAsync(CancellationToken cancellationToken = default) =>
        _session.GetDetachedContractDocumentAsync(cancellationToken);

    public static async Task<BoardsWritablePresentation> OpenWritableDocumentAsync(
        ReadOnlyMemory<byte> verifiedDocument,
        string sourceId,
        string sourceRevision,
        BoardsExternalSaveCallback save,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRevision);
        ArgumentNullException.ThrowIfNull(save);
        ct.ThrowIfCancellationRequested();
        var parsed = HavenBoardDocumentCodec.Deserialize(verifiedDocument);
        var documentBytes = JsonSerializer.SerializeToUtf8Bytes(parsed, DocumentJson);
        if (documentBytes.Length > HavenBoardDocumentCodec.MaximumDocumentBytes)
            throw new InvalidDataException("The board exceeds the maximum document size for this source.");

        BoardsWritablePresentation? host = null;
        var expectedRevision = sourceRevision;
        var session = await ContractSessionAdapter.OpenExternallyWritableAsync(parsed, async (bytes, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (bytes.Length > HavenBoardDocumentCodec.MaximumDocumentBytes)
                throw new InvalidDataException("The board exceeds the maximum document size for this source.");
            var candidate = HavenBoardDocumentCodec.Deserialize(bytes);
            if (candidate.DocumentId != parsed.DocumentId ||
                !string.Equals(candidate.Snapshot?.Id, parsed.Snapshot?.Id, StringComparison.Ordinal))
                throw new InvalidDataException("The board document identity cannot change while this source is open.");

            // Copy before crossing the application boundary. The Files owner validates the
            // exact source and expected revision again before publishing new immutable bytes.
            var isolatedBytes = bytes.ToArray();
            var receipt = await save(sourceId, expectedRevision, isolatedBytes, token).ConfigureAwait(false);
            if (receipt is null)
                throw new InvalidDataException("The external save returned no receipt.");
            if (!receipt.Committed || receipt.Conflict || string.IsNullOrWhiteSpace(receipt.NewRevision) ||
                string.Equals(receipt.NewRevision, expectedRevision, StringComparison.Ordinal))
            {
                host!.LastSaveReceipt = receipt;
                var failure = !string.IsNullOrWhiteSpace(receipt.Message)
                    ? receipt.Message
                    : receipt.Conflict
                        ? "The source changed before this board revision could be saved."
                        : "The external source did not confirm a new board revision.";
                throw new InvalidOperationException(failure);
            }

            expectedRevision = receipt.NewRevision;
            host!._sourceRevision = receipt.NewRevision;
            host!.LastSaveReceipt = receipt;
            return true;
        }).ConfigureAwait(false);

        CuiControlLoader? loader = null;
        try
        {
            return await Dispatcher.UIThread.InvokeAsync<BoardsWritablePresentation>(() =>
            {
                ct.ThrowIfCancellationRequested();
                var viewModel = new BoardsViewModel(session);
                viewModel.SetLanding(false);
                viewModel.SetEmbeddedPresentation();
                viewModel.Set("StatusText", "Ready to edit");
                viewModel.Set("SaveStateText", "Saved source revision");
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
                host = new BoardsWritablePresentation(parsed, sourceId, expectedRevision, session,
                    viewModel, loader, root);
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

    /// <summary>Runs the normal Boards save command. Success is reported only after the
    /// external callback returns a committed receipt containing its new revision.</summary>
    public async ValueTask SaveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        LastSaveReceipt = null;
        await _session.SaveAsync(cancellationToken).ConfigureAwait(false);
        if (LastSaveReceipt is null && !_session.HasUnsavedChanges) return;
        var receipt = LastSaveReceipt;
        if (receipt is null || !receipt.Committed || string.IsNullOrWhiteSpace(receipt.NewRevision))
            throw new InvalidOperationException("The external source did not confirm this board revision.");
        _sourceRevision = receipt.NewRevision;
    }

    private void WireSurface(Control root)
    {
        _inputOwner = new BoardsTextAndKeyboardOwner(root, ViewModel);
        if (FindByAutomationId<Canvas>(root, "InkOverlayLayer") is { } ink)
            _inkInteraction = new BoardInkInteractionController(ink, ViewModel);
        _renderQueue = new BoardsPresentationRenderQueue(RenderAsync, error =>
        {
            if (!_disposed) ViewModel.Set("StatusText", "Boards view could not be refreshed: " + error.Message.Split('\n')[0]);
        });
        ViewModel.RebuildRequested += QueueRender;
        ViewModel.NavRebuildRequested += QueueRender;
        ViewModel.SelectionChanged += QueueRender;
    }

    private void QueueRender()
    {
        if (_disposed) return;
        _renderQueue?.Queue();
    }

    private async Task RenderAsync()
    {
        if (_disposed) return;
        if (FindByAutomationId<StackPanel>(Content, "BlocksHost") is { } blocks)
            await BlockRenderer.RebuildAsync(blocks, ViewModel);
        if (FindByAutomationId<StackPanel>(Content, "NavHost") is { } nav)
            NavBuilder.Rebuild(nav, ViewModel);
        if (FindByAutomationId<StackPanel>(Content, "ToolbarHost") is { } toolbar)
            ToolbarBuilder.Rebuild(toolbar, ViewModel);
        ViewModel.RefreshCuiContext();
        if (FindByAutomationId<Canvas>(Content, "InkOverlayLayer") is { } ink)
            await (_inkInteraction?.RenderAsync() ?? BoardInkOverlayRenderer.RenderReadOnlyAsync(ink, ViewModel));
    }

    private static T? FindByAutomationId<T>(Control root, string id) where T : Control
    {
        if (root is T candidate && AutomationProperties.GetAutomationId(candidate) == id) return candidate;
        foreach (var child in root.GetVisualDescendants().OfType<Control>())
            if (child is T control && AutomationProperties.GetAutomationId(control) == id) return control;
        return null;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        ViewModel.RebuildRequested -= QueueRender;
        ViewModel.NavRebuildRequested -= QueueRender;
        ViewModel.SelectionChanged -= QueueRender;
        _renderQueue?.Dispose();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _inputOwner?.Dispose();
            _inkInteraction?.Dispose();
        });
        await (_inputOwner?.DrainAsync() ?? Task.CompletedTask).ConfigureAwait(false);
        await (_inkInteraction?.DrainAsync() ?? Task.CompletedTask).ConfigureAwait(false);
        await _loader.WhenActionsIdleAsync().ConfigureAwait(false);
        await (_renderQueue?.DrainAsync() ?? Task.CompletedTask).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(_loader.Dispose);
        await _session.DisposeAsync().ConfigureAwait(false);
    }
}
