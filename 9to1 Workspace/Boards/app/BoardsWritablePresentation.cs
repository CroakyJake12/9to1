using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
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
    private readonly BoardsEmbeddedArtifactSurface _embeddedArtifacts;
    private readonly byte[] _initialDocumentBytes;
    private BoardsPresentationRenderQueue? _renderQueue;
    private BoardsEmbeddedSurfaceLifecycle? _surfaceLifecycle;
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
        Control content,
        IBoardsEmbeddedArtifactHost? embeddedArtifactHost)
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
        _embeddedArtifacts = new BoardsEmbeddedArtifactSurface(embeddedArtifactHost, viewModel, content);
    }

    public string SourceId { get; }
    public string SourceRevision => _sourceRevision;
    public bool HasUnsavedChanges => _session.HasUnsavedChanges;
    public BoardsExternalSaveReceipt? LastSaveReceipt { get; private set; }
    public BoardsViewModel ViewModel { get; }
    public Control Content { get; }
    public Task WaitUntilReadyAsync(CancellationToken cancellationToken = default) =>
        (_surfaceLifecycle ?? throw new InvalidOperationException("The Boards surface has not been initialized.")).WaitUntilReadyAsync(cancellationToken);
    public Task DrainRenderingAsync() => _renderQueue?.DrainAsync() ?? Task.CompletedTask;
    public Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default) =>
        _disposed ? Task.FromResult(true) : _embeddedArtifacts.TryPrepareToCloseAsync(cancellationToken);
    public Task<IBoardsPresentationClosePreparation?> PrepareToCloseAsync(CancellationToken cancellationToken = default) =>
        _disposed ? Task.FromResult<IBoardsPresentationClosePreparation?>(CompletedBoardsPresentationClosePreparation.Instance) : _embeddedArtifacts.PrepareToCloseAsync(cancellationToken);
    /// <summary>Returns the detached document supplied when this presentation opened.</summary>
    public HavenBoardDocument Document => HavenBoardDocumentCodec.Deserialize(_initialDocumentBytes);
    public Task<HavenBoardDocument> GetCurrentDocumentAsync(CancellationToken cancellationToken = default) =>
        _session.GetDetachedContractDocumentAsync(cancellationToken);

    public static async Task<BoardsWritablePresentation> OpenWritableDocumentAsync(
        ReadOnlyMemory<byte> verifiedDocument,
        string sourceId,
        string sourceRevision,
        BoardsExternalSaveCallback save,
        CancellationToken ct = default,
        IBoardsEmbeddedArtifactHost? embeddedArtifactHost = null,
        bool standaloneChrome = false,
        IBoardsHomeBoardCreationHost? homeBoardCreationHost = null)
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
            host!.LastSaveReceipt = null;
            BoardsExternalSaveReceipt? receipt;
            try
            {
                receipt = await save(sourceId, expectedRevision, isolatedBytes, token).ConfigureAwait(false);
            }
            finally
            {
                // Home/Files clones the transfer synchronously before its first await.
                // Clear only this facade-owned transfer buffer; the session draft stays intact.
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(isolatedBytes);
            }
            if (receipt is null)
                throw new InvalidDataException("The external save returned no receipt.");
            var newRevision = receipt.NewRevision;
            var invalidRevision = string.IsNullOrWhiteSpace(newRevision) ||
                string.Equals(newRevision, expectedRevision, StringComparison.Ordinal);
            if (!receipt.Committed || receipt.Conflict || invalidRevision)
            {
                host!.LastSaveReceipt = receipt.Committed
                    ? receipt with
                    {
                        Committed = false,
                        Conflict = receipt.Conflict || invalidRevision,
                        Code = receipt.Code ?? (invalidRevision ? "invalid-revision" : "conflict"),
                        Message = receipt.Message ?? (invalidRevision
                            ? "The external source did not confirm a new board revision."
                            : "The source changed before this board revision could be saved.")
                    }
                    : receipt;
                var failure = !string.IsNullOrWhiteSpace(receipt.Message)
                    ? receipt.Message
                    : receipt.Conflict
                        ? "The source changed before this board revision could be saved."
                        : "The external source did not confirm a new board revision.";
                throw new InvalidOperationException(failure);
            }

            if (newRevision is null)
                throw new InvalidDataException("The external source did not confirm a new board revision.");
            expectedRevision = newRevision;
            host!._sourceRevision = newRevision;
            host!.LastSaveReceipt = receipt;
            return true;
        }).ConfigureAwait(false);

        CuiControlLoader? loader = null;
        try
        {
            return await Dispatcher.UIThread.InvokeAsync<BoardsWritablePresentation>(() =>
            {
                ct.ThrowIfCancellationRequested();
        var viewModel = new BoardsViewModel(session, embeddedArtifactHost, homeBoardCreationHost);
                viewModel.SetLanding(false);
                if (!standaloneChrome) viewModel.SetEmbeddedPresentation();
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
                embeddedArtifactHost?.BindOwningControl(root);
                host = new BoardsWritablePresentation(parsed, sourceId, expectedRevision, session,
                    viewModel, loader, root, embeddedArtifactHost);
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
        if (!await TryPrepareToCloseAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The embedded Write page refused to close; its draft remains open.");
        try
        {
            await _session.SaveAsync(cancellationToken).ConfigureAwait(false);
            if (LastSaveReceipt is null && !_session.HasUnsavedChanges) return;
            var receipt = LastSaveReceipt;
            if (receipt is null || !receipt.Committed || string.IsNullOrWhiteSpace(receipt.NewRevision))
                throw new InvalidOperationException("The external source did not confirm this board revision.");
            _sourceRevision = receipt.NewRevision;
        }
        finally
        {
            // The nested Write owner is reopened at the current pinned revision while
            // this Board presentation remains active.
            QueueRender();
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
        // The authored page has mutually exclusive native and embedded content
        // hosts. BlocksHost is absent while Write/Canvas is selected, so it
        // cannot guard rendering of the embedded owner surface.
        if (BoardsCuiControlFinder.FindByAutomationId<StackPanel>(Content, "BlocksHost") is { } blocks)
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

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (!await TryPrepareToCloseAsync().ConfigureAwait(false))
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
        await _loader.WhenActionsIdleAsync().ConfigureAwait(false);
        await (_renderQueue?.DrainAsync() ?? Task.CompletedTask).ConfigureAwait(false);
        await _embeddedArtifacts.DisposeAsync().ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(_loader.Dispose);
        await _session.DisposeAsync().ConfigureAwait(false);
    }
}
