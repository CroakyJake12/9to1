using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;

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

/// <summary>Preview returned by an explicit name-only section icon suggestion request.</summary>
public sealed record BoardsGeneratedSectionIconResult(
    IReadOnlyList<HavenRichSectionGeneratedIcon> Candidates,
    string ProviderId,
    string ModelId);

/// <summary>Optional Home-owned model route used only after the user requests an icon suggestion.</summary>
public interface IBoardsSectionIconGenerationHost
{
    bool CanGenerate { get; }
    Task<BoardsGeneratedSectionIconResult> GenerateAsync(string sectionTitle,
        CancellationToken cancellationToken = default);
}

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
    private readonly CuiThemePreferenceContext? _themeContext;
    private readonly BoardsEmbeddedArtifactSurface _embeddedArtifacts;
    private readonly byte[] _initialDocumentBytes;
    private BoardsPresentationRenderQueue? _renderQueue;
    private BoardsEmbeddedSurfaceLifecycle? _surfaceLifecycle;
    private readonly Guid _documentId;
    private readonly string? _snapshotBoardId;
    private string _sourceRevision;
    private bool _disposed;
    private bool _localDocument;

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
        _themeContext = embeddedArtifactHost?.ThemeContext;
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
    public async Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        var preparation = await PrepareToCloseAsync(cancellationToken).ConfigureAwait(false);
        if (preparation is null) return false;
        await preparation.AbortAsync().ConfigureAwait(false);
        return true;
    }
    public async Task<IBoardsPresentationClosePreparation?> PrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return CompletedBoardsPresentationClosePreparation.Instance;
        var databases = await ViewModel.PrepareDatabaseCloseAsync(cancellationToken).ConfigureAwait(false);
        if (databases is null) return null;
        try
        {
            var document = await _embeddedArtifacts.PrepareToCloseAsync(cancellationToken).ConfigureAwait(false);
            if (document is null) { await databases.AbortAsync(); return null; }
            return new CombinedBoardsClosePreparation(document, databases);
        }
        catch { await databases.AbortAsync(); throw; }
    }
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
        IBoardsHomeBoardCreationHost? homeBoardCreationHost = null,
        IBoardsSectionIconGenerationHost? sectionIconGenerationHost = null)
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

        host = await CreatePresentationAsync(parsed, sourceId, expectedRevision, session, ct,
            embeddedArtifactHost, standaloneChrome, homeBoardCreationHost, sectionIconGenerationHost).ConfigureAwait(false);
        return host;
    }

    private static async Task<BoardsWritablePresentation> CreatePresentationAsync(HavenBoardDocument parsed,
        string sourceId, string expectedRevision, ContractSessionAdapter session, CancellationToken ct,
        IBoardsEmbeddedArtifactHost? embeddedArtifactHost, bool standaloneChrome,
        IBoardsHomeBoardCreationHost? homeBoardCreationHost, IBoardsSectionIconGenerationHost? sectionIconGenerationHost)
    {
        BoardsWritablePresentation? host = null;
        CuiControlLoader? loader = null;
        try
        {
            return await Dispatcher.UIThread.InvokeAsync<BoardsWritablePresentation>(() =>
            {
                ct.ThrowIfCancellationRequested();
                var themeContext = embeddedArtifactHost?.ThemeContext;
                var viewModel = new BoardsViewModel(session, embeddedArtifactHost, homeBoardCreationHost,
                    sectionIconGenerationHost);
                viewModel.SetLanding(false);
                if (!standaloneChrome) viewModel.SetEmbeddedPresentation();
                viewModel.Set("StatusText", "Ready to edit");
                viewModel.Set("SaveStateText", "Saved source revision");
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

    /// <summary>
    /// Presents the Boards splash with NO board behind it. Until now the generic Boards link always
    /// resolved a document first (BoardsLocalDocuments.LatestOrCreateAsync), so reloading that link
    /// reopened an unrelated recent board - or silently created a brand-new empty one - and left its
    /// name and page list on screen beside the splash (owner reports, 28 October). The splash gets a
    /// read-only, store-less placeholder document instead: nothing is opened, nothing is created,
    /// nothing is recorded as recent, and nothing can be saved by accident, while the same board
    /// actions (open a card, open a recent page, New board, Open from file) hand back a real document
    /// identity when the user actually chooses one.
    /// </summary>
    public static async Task<BoardsWritablePresentation> OpenLandingAsync(BoardsLocalDocuments documents,
        Func<Guid, CancellationToken, Task> open, CancellationToken token = default,
        IBoardsEmbeddedArtifactHost? embeddedArtifactHost = null, CancellationToken lifetimeToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(open);
        var placeholder = HavenBoardDocumentCodec.CreateNew("Boards");
        var session = await ContractSessionAdapter.OpenSplashPlaceholderAsync(placeholder).ConfigureAwait(false);
        var parsed = await session.GetDetachedContractDocumentAsync(token).ConfigureAwait(false);
        var host = await CreatePresentationAsync(parsed, "landing:boards-home",
            parsed.RichNotes!.Version.ToString(), session, token, embeddedArtifactHost, false, null, null)
            .ConfigureAwait(false);
        var runtimeToken = lifetimeToken.CanBeCanceled ? lifetimeToken : token;
        try
        {
            await Dispatcher.UIThread.InvokeAsync<Task>(async () =>
            {
                var vm = host.ViewModel;
                vm.SetNoBoardOpen();
                vm.SetLanding(true);
                vm.Set("BoardTitle", string.Empty);
                vm.Set("SaveStateText", string.Empty);
                vm.Set("StatusText", "Choose a board, or create one.");
                vm.CreateBoardAsync = async ct =>
                {
                    using var action = CancellationTokenSource.CreateLinkedTokenSource(ct, runtimeToken);
                    await open(await documents.CreateAsync(token: action.Token), action.Token);
                };
                vm.OpenBoardPathAsync = async (path, _) => await open(documents.IdFor(path), runtimeToken);
                vm.OpenBoardAsync = async () =>
                {
                    var top = TopLevel.GetTopLevel(host.Content) ?? throw new InvalidOperationException("The board window is unavailable.");
                    var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    { Title = "Open board", AllowMultiple = false,
                      FileTypeFilter = [new FilePickerFileType("9to1 Boards") { Patterns = ["*.9to1board"] }] });
                    if (files.Count == 0) return;
                    var path = files[0].TryGetLocalPath() ?? throw new NotSupportedException("This location does not provide a readable local file.");
                    await open(await documents.ImportAsync(path, runtimeToken), runtimeToken);
                };
                vm.SetLandingData(await documents.Recent.LoadCardsAsync(documents.Store, token),
                    await documents.Recent.LoadRecentPagesAsync(documents.Store, token));
                await documents.ResolveWorkspaceDirectoryAsync(runtimeToken);
                vm.Set("BoardStorageNotice", StorageNotice(documents));
            }).GetTask().Unwrap().ConfigureAwait(false);
            return host;
        }
        catch { await host.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static string StorageNotice(BoardsLocalDocuments documents) => StorageNoticeFor(documents);

    /// <summary>Names the folder a board is actually in. "Saved locally" without a location sent the
    /// owner looking at an apparently empty Boards folder, so every case states a real path: the live
    /// file this board is saved to, the workspace folder new boards go into, or - when the workspace
    /// could not be used - the draft folder plus the reason. The drafts-only wording survives only for
    /// a host with no workspace folder at all.</summary>
    public static string StorageNoticeFor(BoardsLocalDocuments documents, Guid? boardId = null)
    {
        var destination = boardId is { } id && id != Guid.Empty ? documents.DestinationFor(id) : null;
        if (destination is not null) return "Saved in " + destination;
        if (documents.WorkspaceResolutionFailure is { } unresolved)
            return "Boards could not reach the 9to1 folder, so this board is a local draft in " +
                documents.DraftDirectory + " - " + unresolved;
        if (documents.PlacementFailure is { } failure)
            return "Saved as a local draft in " + documents.DraftDirectory +
                " - the 9to1 folder could not be used: " + failure;
        if (documents.DocumentsDirectory is { } workspace)
            return "Boards are saved in " + workspace +
                " - use Save As to put one somewhere else.";
        return "Boards are kept in " + documents.DraftDirectory +
            " — use Save As to write a .9to1board copy somewhere else.";
    }

    public static async Task<BoardsWritablePresentation> OpenLocalDocumentAsync(BoardsLocalDocuments documents,
        Guid documentId, Func<Guid, CancellationToken, Task> open, bool landing = false,
        CancellationToken token = default, IBoardsEmbeddedArtifactHost? embeddedArtifactHost = null,
        CancellationToken lifetimeToken = default)
    {
        var session = await documents.OpenAsync(documentId, token).ConfigureAwait(false);
        var parsed = await session.GetDetachedContractDocumentAsync(token).ConfigureAwait(false);
        var host = await CreatePresentationAsync(parsed, "local:" + documentId.ToString("D"),
            parsed.RichNotes!.Version.ToString(), session, token, embeddedArtifactHost, false, null, null).ConfigureAwait(false);
        host._localDocument = true;
        var runtimeToken = lifetimeToken.CanBeCanceled ? lifetimeToken : token;
        try
        {
        await Dispatcher.UIThread.InvokeAsync<Task>(async () =>
        {
            var vm = host.ViewModel;
            vm.SetLanding(landing);
            vm.Set("StatusText", session.Status);
            vm.Set("SaveStateText", BoardsViewModel.MapSaveState(session.Status));
            vm.CreateBoardAsync = async ct =>
            {
                using var action = CancellationTokenSource.CreateLinkedTokenSource(ct, runtimeToken);
                await open(await documents.CreateAsync(token: action.Token), action.Token);
            };
            vm.OpenBoardPathAsync = async (path, _) => await open(documents.IdFor(path), runtimeToken);
            vm.OpenBoardAsync = async () =>
            {
                var top = TopLevel.GetTopLevel(host.Content) ?? throw new InvalidOperationException("The board window is unavailable.");
                var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                { Title = "Open board", AllowMultiple = false,
                  FileTypeFilter = [new FilePickerFileType("9to1 Boards") { Patterns = ["*.9to1board"] }] });
                if (files.Count == 0) return;
                var path = files[0].TryGetLocalPath() ?? throw new NotSupportedException("This location does not provide a readable local file.");
                await open(await documents.ImportAsync(path, runtimeToken), runtimeToken);
            };
            vm.SaveAsBoardAsync = async ct =>
            {
                var path = await PickSavePathAsync(host.Content, vm.Get("BoardTitle")?.ToString());
                if (path is null) return; // Keep the same draft, caret and history.
                using var action = CancellationTokenSource.CreateLinkedTokenSource(ct, runtimeToken);
                await session.SaveAsAsync(path, action.Token);
            };
            vm.SetLandingData(await documents.Recent.LoadCardsAsync(documents.Store, token),
                await documents.Recent.LoadRecentPagesAsync(documents.Store, token));
            await documents.ResolveWorkspaceDirectoryAsync(runtimeToken);
            vm.Set("BoardStorageNotice", StorageNotice(documents));
        }).GetTask().Unwrap().ConfigureAwait(false);
        return host;
        }
        catch { await host.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public static async Task<string?> PickSavePathAsync(Control owner, string? title)
    {
        var top = TopLevel.GetTopLevel(owner) ?? throw new InvalidOperationException("The board window is unavailable.");
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        { Title = "Save board as", SuggestedFileName = string.IsNullOrWhiteSpace(title) ? "Untitled board" : title,
          DefaultExtension = "9to1board", ShowOverwritePrompt = true,
          FileTypeChoices = [new FilePickerFileType("9to1 Boards") { Patterns = ["*.9to1board"] }] });
        if (file is null) return null;
        return file.TryGetLocalPath() ?? throw new NotSupportedException("This location does not provide a writable local file.");
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
            if (_localDocument || LastSaveReceipt is null && !_session.HasUnsavedChanges) return;
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
        // The authored page has mutually exclusive native and embedded content
        // hosts. BlocksHost is absent while Write/Canvas is selected, so it
        // cannot guard rendering of the embedded owner surface.
        if (BoardsCuiControlFinder.FindByAutomationId<StackPanel>(Content, "BlocksHost") is { } blocks)
            await BlockRenderer.RebuildAsync(blocks, ViewModel);
        if (BoardsCuiControlFinder.FindByAutomationId<StackPanel>(Content, "NavHost") is { } nav)
            NavBuilder.Rebuild(nav, ViewModel);
        if (BoardsCuiControlFinder.FindByAutomationId<StackPanel>(Content, "ToolbarHost") is { } toolbar)
            ToolbarBuilder.Rebuild(toolbar, ViewModel);
        if (BoardsCuiControlFinder.FindByAutomationId<CuiContainerControl>(Content, "EmbeddedArtifactHost") is { } embedded)
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
        if (_themeContext is not null) _themeContext.Changed -= OnOwnerThemeChanged;
        ViewModel.CancelSectionIconSuggestionForClose();
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
        await ViewModel.DisposeDatabasesAsync().ConfigureAwait(false);
        await _embeddedArtifacts.DisposeAsync().ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ViewModel.Dispose();
            _loader.Dispose();
        });
        await _session.DisposeAsync().ConfigureAwait(false);
    }
}
