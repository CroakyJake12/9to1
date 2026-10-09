using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Services;
using Haven.UI.Components;

namespace Haven.Desktop.Views.Pages.Write;

/// <summary>
/// Thin Avalonia backend host for the Haven.UI Write scene and the recovered local document services.
/// </summary>
public sealed partial class WritePage : UserControl, IDisposable, IAsyncDisposable,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly HavenEventBus _bus;
    private readonly INotesRepository _repository;
    private readonly INotesImportExportService _formats;
    private readonly IWriteNativeDocumentPackageStore? _nativePackageStore;
    private readonly WordWriteHavenScene _route;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly Guid? _initialDocumentId;
    private readonly INotesAiService? _ai;
    private readonly IOllamaClient? _aiModels;
    private readonly NotesReadAloudController? _readAloud;
    private CancellationTokenSource _readAloudSessionCts = new();
    private IReadOnlyList<NotesDocumentSummary> _documents = [];
    private int _documentIndex;
    private int _saveRunning;
    private long _editGeneration;
    private bool _closePreparing;
    private static readonly JsonSerializerOptions DocumentJson = new(JsonSerializerDefaults.Web);
    private bool _initialized;
    private Exception? _originalInitializationFailure;
    private bool _busy;
    private bool _dirty;
    private bool _disposed;

    public WritePage(
        HavenEventBus bus,
        INotesRepository repository,
        INotesImportExportService formats,
        INotesAttachmentStore? attachments = null,
        Guid? initialDocumentId = null,
        INotesAiService? ai = null,
        IOllamaClient? aiModels = null,
        NotesReadAloudController? readAloud = null,
        IWriteNativeDocumentPackageStore? nativePackageStore = null,
        Action<WritePage>? captureOriginalOwner = null)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _formats = formats ?? throw new ArgumentNullException(nameof(formats));
        _nativePackageStore = nativePackageStore;
        _wordAttachments = attachments;
        _initialDocumentId = initialDocumentId;
        _ai = ai;
        _aiModels = aiModels;
        _readAloud = readAloud;
        _work = new(StopOriginalPresentationAsync, CleanupOriginalPresentationAsync);

        var constructorSources = _physicalSources ??= [];
        constructorSources.Add(this);
        try
        {
        InvokePhysicalSource(() => { captureOriginalOwner?.Invoke(this); return true; });
        _work.DemandAdmission();
        InitializeComponent();
        _route = new WordWriteHavenScene(OwnOriginalSceneCallback, ScheduleOriginalStats);
        Scene.Root = _route.Root;
        _route.LibraryRequested += OnLibraryRequested;
        _route.DocumentOpenRequested += OnDocumentOpenRequested;
        _route.AiProposalRequested += OnAiProposalRequested;
        _route.AiApplyRequested += OnAiApplyRequested;
        _route.AiRejectRequested += OnAiRejectRequested;
        _route.NewRequested += OnNewRequested;
        _route.ImportRequested += OnImportRequested;
        _route.ExportRequested += OnExportRequested;
        _route.SaveRequested += OnSaveRequested;
        _route.PreviousRequested += OnPreviousRequested;
        _route.NextRequested += OnNextRequested;
        _route.DocumentChanged += OnWordDocumentChanged;
        _route.ImageRequested += OnWordImageRequested;
        _route.ReadAloudRequested += OnReadAloudRequested;
        _route.ReadAloudStopRequested += OnReadAloudStopRequested;
        _route.ReadAloudSkipBackRequested += OnReadAloudSkipBackRequested;
        _route.ReadAloudSkipForwardRequested += OnReadAloudSkipForwardRequested;
        _route.ReadAloudPauseResumeRequested += OnReadAloudPauseResumeRequested;
        if (_readAloud is not null)
        {
            _readAloud.StatusChanged += OnReadAloudStatusChanged;
            _readAloud.ProgressChanged += OnReadAloudProgressChanged;
            _readAloud.IsReadingChanged += OnReadAloudIsReadingChanged;
        }

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _autosaveTimer.Tick += OnAutosaveTick;
        Loaded += OnLoaded;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        _work.DemandAdmission();
        }
        catch
        {
            _work.RequestRetirement();
            throw;
        }
        finally
        {
            _constructionSettled.TrySetResult();
            constructorSources.RemoveAt(constructorSources.Count - 1);
        }
    }

    public NotesDocument? Document { get; private set; }
    public bool IsDirty => _dirty;

    internal WordWriteHavenScene Route => _route;
    internal HavenSceneControl SceneHost => Scene;
    internal Haven.UI.Components.Page SceneRoot => _route.Root;

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        RunOriginalAsync(() => InitializeCoreAsync(cancellationToken));

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        if (_initialized || _disposed || _closePreparing)
            return;

        _initialized = true;
        _originalInitializationFailure = null;
        SetBusy(true);
        try
        {
            await RefreshDocumentsAsync(cancellationToken);
            if (!CanPublishOriginal) return;
            await RefreshAiModelsAsync(cancellationToken);
            if (!CanPublishOriginal) return;
            if (_initialDocumentId is { } initialDocumentId)
            {
                if (!await OpenDocumentByIdAsync(initialDocumentId, cancellationToken, saveBeforeSwitch: false))
                    ShowLibrary();
            }
            else
            {
                ShowLibrary();
            }

            Publish(_autosaveTimer.Start);
            Publish(() => _bus.Fire("Write.Opened"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _initialized = false;
            throw;
        }
        catch (Exception ex)
        {
            _work.Executing?.Retain(ex);
            _initialized = false;
            _originalInitializationFailure = ex;
            _work.Executing!.Retain(ex);
            Publish(() => _route.SetStatus("Couldnâ€™t open local documents: " + ex.Message));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshAiModelsAsync(CancellationToken cancellationToken)
    {
        if (_aiModels is null) { Publish(() => _route.SetAiModels([])); return; }
        try { var models = await ObserveOriginalAsync(() => _aiModels.GetModelsAsync(cancellationToken)); Publish(() => _route.SetAiModels(models.Select(model => model.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray())); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { Publish(() => _route.SetAiModels([])); }
    }

    public Task<bool> SaveAsync(
        string reason = "Manual save",
        CancellationToken cancellationToken = default) =>
        RunOriginalAsync(() => SaveCoreAsync(reason, cancellationToken));

    private async Task<bool> SaveCoreAsync(string reason, CancellationToken cancellationToken)
    {
        if (_disposed) return false;
        if (Document is null || (!_dirty && !Document.Recovery.HasUnsavedRecovery))
            return true;

        if (Interlocked.Exchange(ref _saveRunning, 1) != 0)
            return false;

        try
        {
            var document = Document;
            if (string.IsNullOrWhiteSpace(document.Title))
            {
                document.Title = "Untitled document";
                Publish(() => _route.SetTitleFromModel(document.Title));
            }

            // The repository owns this submitted snapshot. Later retained edits
            // keep their own content and remain dirty against the acknowledged revision.
            var generation = _editGeneration;
            var snapshot = JsonSerializer.Deserialize<NotesDocument>(
                JsonSerializer.Serialize(document, DocumentJson), DocumentJson)
                ?? throw new InvalidDataException("The Write document could not be snapshotted.");
            var result = await ObserveOriginalAsync(() => _repository.SaveAsync(snapshot, reason, cancellationToken));
            if (Document is { } current && current.Id == document.Id)
            {
                current.Version = result.Version;
                current.Recovery = snapshot.Recovery;
                if (generation == _editGeneration) current.UpdatedAt = snapshot.UpdatedAt;
                _dirty = generation != _editGeneration;
            }

            if (CanPublishOriginal) await RefreshDocumentsAsync(cancellationToken);
            if (Document?.Id == document.Id)
            {
                _documentIndex = IndexOfDocument(document.Id);
                UpdatePosition();
                Publish(() => _route.SetStatus(_dirty ? "Newer changes remain unsaved. Save them before closing."
                    : $"Saved locally at {result.SavedAt.LocalDateTime:t} Â· v{result.Version}"));
            }
            Publish(() => _bus.Fire("Write.Saved"));
            return Document is { } remaining && remaining.Id == document.Id && !_dirty
                && !remaining.Recovery.HasUnsavedRecovery;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _work.Executing?.Retain(ex);
            Publish(() => _route.SetStatus("Couldn't save this document: " + ex.Message));
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _saveRunning, 0);
        }
    }

    /// <summary>Refuses close over active owner work or a draft not acknowledged by storage.</summary>
    public Task<bool> PrepareToCloseAsync(
        string reason = "Autosave before closing Write", CancellationToken cancellationToken = default) =>
        RunOriginalAsync(() => PrepareToCloseCoreAsync(reason, cancellationToken));

    private async Task<bool> PrepareToCloseCoreAsync(string reason, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed) return false;
        if (!_initialized || _originalInitializationFailure is not null || (_initialDocumentId is not null && Document is null))
        {
            Publish(() => _route.SetStatus("Finish opening this Write workspace before closing."));
            return false;
        }
        if (_busy || _closePreparing || Volatile.Read(ref _saveRunning) != 0)
        {
            Publish(() => _route.SetStatus("Finish the current Write operation before closing."));
            return false;
        }

        _closePreparing = true;
        SetBusy(true);
        try
        {
            await StopReadAloudForContextChangeAsync();
            if (!CanPublishOriginal) return false;
            if (_readAloudStopUnacknowledged || _readAloud?.IsActive == true) return false;
            if (!await SaveAsync(reason, cancellationToken)) return false;
            if (_dirty || Document?.Recovery.HasUnsavedRecovery == true)
            {
                Publish(() => _route.SetStatus("Newer changes remain unsaved. Save them before closing."));
                return false;
            }
            return true;
        }
        finally
        {
            _closePreparing = false;
            SetBusy(false);
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => RunOriginalEvent(() => OnLoadedCoreAsync(sender, e));

    private async Task OnLoadedCoreAsync(object? sender, RoutedEventArgs e)
    {
        await InitializeAsync();
        Publish(_autosaveTimer.Start);
    }

    private void OnDetachedFromVisualTree(
        object? sender,
        Avalonia.VisualTreeAttachmentEventArgs e) => RunOriginalEvent(() => OnDetachedFromVisualTreeCoreAsync(sender, e));

    private async Task OnDetachedFromVisualTreeCoreAsync(
        object? sender,
        Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _autosaveTimer.Stop();
        if (_readAloud is not null && _readAloud.IsActive)
            await StopReadAloudForContextChangeAsync();
        if (_dirty && Document is not null)
            await SaveAsync("Autosave on leaving Write");
    }

    private void OnAutosaveTick(object? sender, EventArgs e) => RunOriginalEvent(() => OnAutosaveTickCoreAsync(sender, e));

    private async Task OnAutosaveTickCoreAsync(object? sender, EventArgs e)
    {
        if (_disposed || !_dirty || _busy || Document is null)
            return;

        await SaveAsync("Autosave");
    }

    private void OnLibraryRequested(object? sender, EventArgs e) => RunOriginalEvent(() => OnLibraryRequestedCoreAsync(sender, e));

    private async Task OnLibraryRequestedCoreAsync(object? sender, EventArgs e) =>
        await RunBusyAsync(
            () => ShowLibraryAsync(saveBeforeSwitch: true, CancellationToken.None),
            "open the document library");

    private void OnDocumentOpenRequested(Guid documentId) => RunOriginalEvent(() => OnDocumentOpenRequestedCoreAsync(documentId));

    private async Task OnDocumentOpenRequestedCoreAsync(Guid documentId) =>
        await RunBusyAsync(
            async () => { await OpenDocumentByIdAsync(documentId, CancellationToken.None, saveBeforeSwitch: true); },
            "open this document");

    private void OnAiProposalRequested(string instruction, bool allowDocumentContext, string modelName) => RunOriginalEvent(() => OnAiProposalRequestedCoreAsync(instruction, allowDocumentContext, modelName));

    private async Task OnAiProposalRequestedCoreAsync(string instruction, bool allowDocumentContext, string modelName) =>
        await RunBusyAsync(() => ProposeAiAsync(instruction, allowDocumentContext, modelName, CancellationToken.None), "create an AI proposal");

    private async Task ProposeAiAsync(string instruction, bool allowDocumentContext, string modelName, CancellationToken cancellationToken)
    {
        if (Document is null) return;
        if (_ai is null) { Publish(() => _route.SetStatus("AI proposals are unavailable because the Notes AI service is not registered.")); return; }
        var selectedText = _route.SelectedText;
        if (!allowDocumentContext && string.IsNullOrWhiteSpace(selectedText)) { Publish(() => _route.SetStatus("Select text, or explicitly allow document context, before requesting an AI edit.")); return; }
        var context = allowDocumentContext ? string.Join("\n", NotesTextStatistics.EnumerateText(Document)) : string.Empty;
        var result = await ObserveOriginalAsync(() => _ai.ProposeAsync(new NotesAiProposalRequest(Document.Id, _route.SelectedBlockId, instruction, selectedText, context, modelName, allowDocumentContext, Document.Citations), cancellationToken));
        if (!CanPublishOriginal) return;
        var change = new NotesAiChange { BlockId = _route.SelectedBlockId, Instruction = instruction.Trim(), OriginalContent = selectedText, ProposedContent = result.ProposedContent, Explanation = result.Explanation, CitationIds = result.CitationIds.ToList(), ProviderId = result.ProviderId, ModelName = result.ModelName, Status = NotesAiChangeStatus.Proposed, UserConsentRecorded = allowDocumentContext || !string.IsNullOrWhiteSpace(selectedText), SentDocumentContext = allowDocumentContext };
        Document.AiChanges.Add(change); MarkDirty(); Publish(() => _route.SetPendingAiChange(change)); Publish(() => _route.SetStatus("AI proposal ready for review. Nothing has been applied.")); Publish(() => _bus.Fire("Write.Ai.Proposed"));
    }

    private void OnAiApplyRequested(object? sender, EventArgs e) => RunOriginalEvent(() => OnAiApplyRequestedCoreAsync(sender, e));

    private async Task OnAiApplyRequestedCoreAsync(object? sender, EventArgs e)
    {
        if (await SaveAsync("Applied reviewed AI proposal")) { Publish(() => _route.SetStatus("Reviewed AI proposal applied and saved.")); Publish(() => _bus.Fire("Write.Ai.Applied")); }
    }

    private void OnAiRejectRequested(object? sender, EventArgs e) => RunOriginalEvent(() => OnAiRejectRequestedCoreAsync(sender, e));

    private async Task OnAiRejectRequestedCoreAsync(object? sender, EventArgs e)
    {
        if (await SaveAsync("Rejected AI proposal")) { Publish(() => _route.SetStatus("AI proposal rejected. Document content was unchanged.")); Publish(() => _bus.Fire("Write.Ai.Rejected")); }
    }

    private void OnReadAloudRequested(object? sender, EventArgs e) => RunOriginalEvent(() => OnReadAloudRequestedCoreAsync(sender, e));

    private async Task OnReadAloudRequestedCoreAsync(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (_readAloud is null)
        {
            Publish(() => _route.SetStatus("Read aloud is unavailable because the local speech service is not registered."));
            return;
        }

        if (Document is null)
        {
            Publish(() => _route.SetStatus("Open a document before reading it aloud."));
            return;
        }

        var plainText = string.Join("\n", NotesTextStatistics.EnumerateText(Document));
        if (string.IsNullOrWhiteSpace(plainText))
        {
            Publish(() => _route.SetStatus("This document has no readable text yet."));
            return;
        }

        try
        {
            if (_readAloud.IsActive)
                await StopReadAloudOriginalAsync();
            await ObserveOriginalAsync(() => _readAloudSessionCts.CancelAsync());
            if (!CanPublishOriginal) return;
            _readAloudSessionCts.Dispose();
            _readAloudSessionCts = new CancellationTokenSource();
            await ObserveOriginalAsync(() => _readAloud.SpeakOriginalLongFormAsync(plainText, language: null,
                _readAloudSessionCts.Token, ObserveOriginalAsync, QualifyOriginalSpeechSource));
        }
        catch (ArgumentException ex)
        {
            _work.Executing?.Retain(ex);
            Publish(() => _route.SetStatus(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _work.Executing?.Retain(ex);
            Publish(() => _route.SetStatus(ex.Message));
        }
        catch (OperationCanceledException)
        {
            // Page change cancelled this local reading session.
        }
    }

    private void OnReadAloudStopRequested(object? sender, EventArgs e) => RunOriginalEvent(() => OnReadAloudStopRequestedCoreAsync(sender, e));

    private async Task OnReadAloudStopRequestedCoreAsync(object? sender, EventArgs e)
    {
        if (_disposed || _readAloud is null || !_readAloud.IsActive) return;
        try
        {
            await StopReadAloudOriginalAsync();
        }
        catch (Exception ex)
        {
            _work.Executing?.Retain(ex);
            Publish(() => _route.SetStatus("Couldn't stop read aloud: " + ex.Message));
        }
    }

    private void OnReadAloudSkipBackRequested(object? sender, EventArgs e) => RunOriginalEvent(() => OnReadAloudSkipBackRequestedCoreAsync(sender, e));

    private async Task OnReadAloudSkipBackRequestedCoreAsync(object? sender, EventArgs e)
    {
        if (_disposed || _readAloud is null || !_readAloud.IsReading) return;
        try
        {
            await ObserveOriginalAsync(() => _readAloud.SkipBackwardAsync(CancellationToken.None));
        }
        catch (InvalidOperationException ex)
        {
            _work.Executing?.Retain(ex);
            Publish(() => _route.SetStatus(ex.Message));
        }
    }

    private void OnReadAloudSkipForwardRequested(object? sender, EventArgs e) => RunOriginalEvent(() => OnReadAloudSkipForwardRequestedCoreAsync(sender, e));

    private async Task OnReadAloudSkipForwardRequestedCoreAsync(object? sender, EventArgs e)
    {
        if (_disposed || _readAloud is null || !_readAloud.IsReading) return;
        try
        {
            await ObserveOriginalAsync(() => _readAloud.SkipForwardAsync(CancellationToken.None));
        }
        catch (InvalidOperationException ex)
        {
            _work.Executing?.Retain(ex);
            Publish(() => _route.SetStatus(ex.Message));
        }
    }

    private void OnReadAloudPauseResumeRequested(object? sender, EventArgs e) => RunOriginalEvent(() => OnReadAloudPauseResumeRequestedCoreAsync(sender, e));

    private async Task OnReadAloudPauseResumeRequestedCoreAsync(object? sender, EventArgs e)
    {
        if (_disposed || _readAloud is null || !_readAloud.IsReading) return;
        try
        {
            if (_readAloud.IsPaused)
                await ObserveOriginalAsync(() => _readAloud.ResumeAsync(CancellationToken.None));
            else
                await ObserveOriginalAsync(() => _readAloud.PauseAsync(CancellationToken.None));
        }
        catch (InvalidOperationException ex)
        {
            _work.Executing?.Retain(ex);
            Publish(() => _route.SetStatus(ex.Message));
        }
    }

    private void OnReadAloudStatusChanged(object? sender, NotesReadAloudStatus status) =>
        QueueOriginalPublication(() =>
        {
            Publish(() => _route.SetStatus(status.Message));
            RefreshReadAloudRouteState();
        });

    private void OnReadAloudProgressChanged(double progress) =>
        QueueOriginalPublication(RefreshReadAloudRouteState);

    private void OnReadAloudIsReadingChanged(bool isReading) =>
        QueueOriginalPublication(RefreshReadAloudRouteState);

    private void RefreshReadAloudRouteState()
    {
        if (_readAloud is null) return;
        var count = Math.Max(_readAloud.ChunkCount, _readAloud.IsReading ? 1 : 0);
        var index = Math.Clamp(_readAloud.CurrentChunkIndex, 0, Math.Max(0, count - 1));
        var label = !_readAloud.IsReading
            ? string.Empty
            : _readAloud.IsPaused
                ? $"Paused · section {index + 1} of {count}"
                : $"Reading locally · section {index + 1} of {count}";
        Publish(() => _route.SetReadAloudState(_readAloud.IsReading, _readAloud.IsPaused, label));
    }

    private async Task StopReadAloudForContextChangeAsync()
    {
        if (_readAloud is null || !_readAloud.IsActive) return;
        try
        {
            await ObserveOriginalAsync(() => _readAloudSessionCts.CancelAsync());
            await StopReadAloudOriginalAsync();
        }
        catch (Exception ex)
        {
            _work.Executing?.Retain(ex);
            Publish(() => _route.SetStatus("Couldn't stop read aloud during the switch: " + ex.Message));
        }
    }

    private void OnNewRequested(object? sender, EventArgs e) => RunOriginalEvent(() => OnNewRequestedCoreAsync(sender, e));

    private async Task OnNewRequestedCoreAsync(object? sender, EventArgs e) =>
        await RunBusyAsync(
            () => CreateDocumentAsync(CancellationToken.None),
            "create a document");

    private void OnSaveRequested(object? sender, EventArgs e) => RunOriginalEvent(() => OnSaveRequestedCoreAsync(sender, e));

    private async Task OnSaveRequestedCoreAsync(object? sender, EventArgs e) => await SaveAsync();

    private void OnPreviousRequested(object? sender, EventArgs e) => RunOriginalEvent(() => OnPreviousRequestedCoreAsync(sender, e));

    private async Task OnPreviousRequestedCoreAsync(object? sender, EventArgs e) =>
        await RunBusyAsync(() => MoveAsync(-1), "open the previous document");

    private void OnNextRequested(object? sender, EventArgs e) => RunOriginalEvent(() => OnNextRequestedCoreAsync(sender, e));

    private async Task OnNextRequestedCoreAsync(object? sender, EventArgs e) =>
        await RunBusyAsync(() => MoveAsync(1), "open the next document");

    private void OnImportRequested(object? sender, EventArgs e) => RunOriginalEvent(() => OnImportRequestedCoreAsync(sender, e));

    private async Task OnImportRequestedCoreAsync(object? sender, EventArgs e) =>
        await RunBusyAsync(PickImportAsync, "import a document");

    private void OnExportRequested(object? sender, EventArgs e) => RunOriginalEvent(() => OnExportRequestedCoreAsync(sender, e));

    private async Task OnExportRequestedCoreAsync(object? sender, EventArgs e) =>
        await RunBusyAsync(PickExportAsync, "export this document");

    internal Task<bool> ImportFromPathAsync(
        string sourcePath,
        CancellationToken cancellationToken = default) =>
        RunOriginalAsync(() => ImportFromPathCoreAsync(sourcePath, cancellationToken));

    private async Task<bool> ImportFromPathCoreAsync(string sourcePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (Document is not null && _dirty
            && !await SaveAsync("Autosave before import", cancellationToken))
        {
            return false;
        }

        try
        {
            NotesDocument imported;
            if (Path.GetExtension(sourcePath).Equals(WriteNativeDocumentPackageMetadata.Extension, StringComparison.OrdinalIgnoreCase))
            {
                if (_nativePackageStore is null)
                {
                    Publish(() => _route.SetStatus("Native .9to1w import is unavailable because its package service is not registered."));
                    return false;
                }
                var package = await ObserveOriginalAsync(() => _nativePackageStore.OpenAsync(sourcePath, cancellationToken));
                if (!package.IsSuccess)
                {
                    var error = package.Error;
                    if (error is null)
                    {
                        Publish(() => _route.SetStatus("Couldn’t import this document because the package service returned no document."));
                        return false;
                    }
                    Publish(() => _route.SetStatus($"Couldn’t import this document ({error.Code}): {error.Message}"));
                    return false;
                }
                imported = package.Value ?? throw new InvalidDataException("The native package service returned no document.");
            }
            else
            {
                imported = await ObserveOriginalAsync(() => _formats.ImportAsync(sourcePath, cancellationToken));
            }
            var save = await ObserveOriginalAsync(() => _repository.SaveAsync(
                imported,
                "Imported " + Path.GetFileName(sourcePath),
                cancellationToken));
            imported.Version = save.Version;
            await RefreshDocumentsAsync(cancellationToken);
            if (!CanPublishOriginal) return false;
            Document = imported;
            _documentIndex = IndexOfDocument(imported.Id);
            _dirty = false;
            Publish(() => _route.SetDocument(imported, _documentIndex, _documents.Count));
            Publish(() => _route.SetStatus("Imported " + Path.GetFileName(sourcePath)));
            Publish(() => _bus.Fire("Write.Document.Imported"));
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _work.Executing?.Retain(ex);
            Publish(() => _route.SetStatus("Couldnâ€™t import this document: " + ex.Message));
            return false;
        }
    }

    internal Task<bool> ExportToPathAsync(
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        RunOriginalAsync(() => ExportToPathCoreAsync(destinationPath, cancellationToken));

    private async Task<bool> ExportToPathCoreAsync(string destinationPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (Document is null)
            return false;

        if (_dirty && !await SaveAsync("Save before export", cancellationToken))
            return false;

        try
        {
            if (Path.GetExtension(destinationPath).Equals(WriteNativeDocumentPackageMetadata.Extension, StringComparison.OrdinalIgnoreCase))
            {
                if (_nativePackageStore is null)
                {
                    Publish(() => _route.SetStatus("Native .9to1w export is unavailable because its package service is not registered."));
                    return false;
                }
                var package = await ObserveOriginalAsync(() => _nativePackageStore.SaveAsync(Document, destinationPath, cancellationToken));
                if (!package.IsSuccess)
                {
                    var error = package.Error;
                    if (error is null)
                    {
                        Publish(() => _route.SetStatus("Couldn’t export this document because the package service returned no saved path."));
                        return false;
                    }
                    Publish(() => _route.SetStatus($"Couldn’t export this document ({error.Code}): {error.Message}"));
                    return false;
                }
            }
            else
            {
                await ObserveOriginalAsync(() => _formats.ExportAsync(Document, destinationPath, cancellationToken));
            }
            Publish(() => _route.SetStatus(BuildExportStatus(destinationPath)));
            Publish(() => _bus.Fire("Write.Document.Exported"));
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _work.Executing?.Retain(ex);
            Publish(() => _route.SetStatus("Couldnâ€™t export this document: " + ex.Message));
            return false;
        }
    }

    private async Task PickImportAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
        {
            Publish(() => _route.SetStatus("Import isnâ€™t available from this platform surface."));
            return;
        }

        var files = await ObserveOriginalAsync(() => top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a Write document",
            AllowMultiple = false,
            FileTypeFilter = BuildFileTypes(GetImportExtensions())
        }));
        var file = files.FirstOrDefault();
        if (file is null || !CanPublishOriginal)
            return;

        var localPath = file.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            await ImportFromPathAsync(localPath);
            return;
        }

        var extension = Path.GetExtension(file.Name);
        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"haven-write-import-{Guid.NewGuid():N}{extension}");
        try
        {
            {
                var source = await ObserveOriginalAsync(() => file.OpenReadAsync());
                await using var sourceDisposal = OwnOriginalStreamDisposal(source);
                var destination = File.Create(temporaryPath);
                await using var destinationDisposal = OwnOriginalStreamDisposal(destination);
                await ObserveOriginalAsync(() => source.CopyToAsync(destination));
            }

            await ImportFromPathAsync(temporaryPath);
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    private async Task PickExportAsync()
    {
        if (Document is null)
            return;

        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
        {
            Publish(() => _route.SetStatus("Export isnâ€™t available from this platform surface."));
            return;
        }

        var exportExtensions = GetExportExtensions();
        var defaultExtension = (_nativePackageStore is not null ? WriteNativeDocumentPackageMetadata.Extension : null)
            ?? exportExtensions
            .FirstOrDefault(extension => extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
            ?? exportExtensions.FirstOrDefault()
            ?? ".haven-notes.json";
        var file = await ObserveOriginalAsync(() => top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Write document",
            SuggestedFileName = SanitizeFileName(Document.Title) + defaultExtension,
            DefaultExtension = defaultExtension.TrimStart('.'),
            FileTypeChoices = BuildFileTypes(exportExtensions),
            ShowOverwritePrompt = true
        }));
        if (file is null || !CanPublishOriginal)
            return;

        var localPath = file.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            await ExportToPathAsync(localPath);
            return;
        }

        var selectedExtension = Path.GetExtension(file.Name);
        if (string.IsNullOrWhiteSpace(selectedExtension))
            selectedExtension = defaultExtension;
        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"haven-write-export-{Guid.NewGuid():N}{selectedExtension}");
        try
        {
            if (!await ExportToPathAsync(temporaryPath))
                return;

            var source = File.OpenRead(temporaryPath);
            await using var sourceDisposal = OwnOriginalStreamDisposal(source);
            var destination = await ObserveOriginalAsync(() => file.OpenWriteAsync());
            await using var destinationDisposal = OwnOriginalStreamDisposal(destination);
            destination.SetLength(0);
            await ObserveOriginalAsync(() => source.CopyToAsync(destination));
            await ObserveOriginalAsync(() => destination.FlushAsync());
            Publish(() => _route.SetStatus(BuildExportStatus(file.Name)));
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    private static string BuildExportStatus(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(path);
        var native = extension.Equals(WriteNativeDocumentPackageMetadata.Extension, StringComparison.OrdinalIgnoreCase)
                     || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                     || path.EndsWith(".haven-notes.json", StringComparison.OrdinalIgnoreCase);
        return native
            ? "Exported " + name
            : "Exported " + name + " · This format may not preserve every Haven-only object or formatting detail.";
    }

    private static IReadOnlyList<FilePickerFileType> BuildFileTypes(
        IReadOnlyCollection<string> extensions) =>
        extensions
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(extension => new FilePickerFileType(
                extension.TrimStart('.').ToUpperInvariant() + " document")
            {
                Patterns = ["*" + extension]
            })
            .ToArray();

    private IReadOnlyCollection<string> GetImportExtensions() =>
        _nativePackageStore is null
            ? _formats.ImportExtensions
            : _formats.ImportExtensions.Append(WriteNativeDocumentPackageMetadata.Extension)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private IReadOnlyCollection<string> GetExportExtensions() =>
        _nativePackageStore is null
            ? _formats.ExportExtensions
            : _formats.ExportExtensions.Append(WriteNativeDocumentPackageMetadata.Extension)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string SanitizeFileName(string title)
    {
        var value = string.IsNullOrWhiteSpace(title) ? "Untitled document" : title.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value;
    }

    private void DeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _work.Executing?.Retain(ex);
            Publish(() => _route.SetStatus(_route.StatusText.Content + " Temporary-file cleanup failed: " + ex.Message));
        }
    }

    private void OnTitleChanged(string title)
    {
        if (Document is null || Document.Title == title)
            return;

        Document.Title = title;
        MarkDirty();
    }

    private void OnBlockTextChanged(WriteBlockTextChangedEventArgs e)
    {
        if (Document is null)
            return;

        var block = Document.Sections
            .SelectMany(section => section.Pages)
            .SelectMany(page => page.Blocks)
            .FirstOrDefault(candidate => candidate.Id == e.BlockId);
        if (block is null)
            return;

        if (!e.IsList)
        {
            if (EditableText(block) == e.Text)
                return;

            ReplaceTextPreservingRuns(block, e.Text);
        }
        else if (block.List is not null)
        {
            var lines = e.Text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');

            for (var index = 0; index < lines.Length; index++)
            {
                if (index < block.List.Items.Count)
                    block.List.Items[index].Text = lines[index];
                else
                    block.List.Items.Add(new NotesListItem { Text = lines[index], Level = 0 });
            }

            while (block.List.Items.Count > lines.Length)
                block.List.Items.RemoveAt(block.List.Items.Count - 1);
        }

        MarkDirty();
    }

    private static string EditableText(NotesBlock block) =>
        block.Runs.Count > 0
            ? string.Concat(block.Runs.Select(run => run.Text))
            : block.PlainText;

    private static void ReplaceTextPreservingRuns(NotesBlock block, string newText)
    {
        newText ??= string.Empty;
        if (block.Runs.Count == 0)
        {
            block.PlainText = newText;
            return;
        }

        var oldText = string.Concat(block.Runs.Select(run => run.Text));
        if (string.Equals(oldText, newText, StringComparison.Ordinal))
        {
            block.PlainText = newText;
            return;
        }

        var prefixLength = CommonPrefixLength(oldText, newText);
        var suffixLength = CommonSuffixLength(oldText, newText, prefixLength);
        var oldReplacementEnd = oldText.Length - suffixLength;
        var insertedText = newText.Substring(
            prefixLength,
            newText.Length - prefixLength - suffixLength);

        var starts = new int[block.Runs.Count];
        var lengths = new int[block.Runs.Count];
        var offset = 0;
        for (var index = 0; index < block.Runs.Count; index++)
        {
            starts[index] = offset;
            lengths[index] = block.Runs[index].Text.Length;
            offset += lengths[index];
        }

        var targetIndex = block.Runs.Count - 1;
        for (var index = 0; index < block.Runs.Count; index++)
        {
            var runEnd = starts[index] + lengths[index];
            if (prefixLength <= runEnd)
            {
                targetIndex = index;
                break;
            }
        }

        var targetInsertion = Math.Clamp(
            prefixLength - starts[targetIndex],
            0,
            lengths[targetIndex]);

        for (var index = 0; index < block.Runs.Count; index++)
        {
            var runStart = starts[index];
            var runEnd = runStart + lengths[index];
            var deleteStart = Math.Max(prefixLength, runStart);
            var deleteEnd = Math.Min(oldReplacementEnd, runEnd);
            if (deleteEnd <= deleteStart)
                continue;

            var localStart = deleteStart - runStart;
            block.Runs[index].Text = block.Runs[index].Text.Remove(
                localStart,
                deleteEnd - deleteStart);
        }

        if (insertedText.Length > 0)
        {
            var target = block.Runs[targetIndex];
            target.Text = target.Text.Insert(
                Math.Min(targetInsertion, target.Text.Length),
                insertedText);
        }

        block.PlainText = string.Concat(block.Runs.Select(run => run.Text));
    }

    private static int CommonPrefixLength(string left, string right)
    {
        var limit = Math.Min(left.Length, right.Length);
        var length = 0;
        while (length < limit && left[length] == right[length])
            length++;
        return length;
    }

    private static int CommonSuffixLength(string left, string right, int prefixLength)
    {
        var limit = Math.Min(left.Length, right.Length) - prefixLength;
        var length = 0;
        while (length < limit
               && left[left.Length - 1 - length] == right[right.Length - 1 - length])
        {
            length++;
        }

        return length;
    }

    private void MarkDirty()
    {
        if (!CanPublishOriginal || Document is null)
            return;

        Document.UpdatedAt = DateTimeOffset.UtcNow;
        ++_editGeneration;
        _dirty = true;
        Publish(() => _route.SetStatus("Unsaved changes Â· autosave is on"));
    }


    private async Task<bool> OpenDocumentByIdAsync(Guid documentId, CancellationToken cancellationToken, bool saveBeforeSwitch)
    {
        var index = -1;
        for (var i = 0; i < _documents.Count; i++) if (_documents[i].Id == documentId) { index = i; break; }
        if (index < 0) { Publish(() => _route.SetStatus("That local document no longer exists.")); return false; }
        await OpenDocumentAtAsync(index, cancellationToken, saveBeforeSwitch);
        return Document?.Id == documentId;
    }

    private async Task ShowLibraryAsync(bool saveBeforeSwitch, CancellationToken cancellationToken)
    {
        await StopReadAloudForContextChangeAsync();
        if (!CanPublishOriginal) return;
        if (saveBeforeSwitch && Document is not null && _dirty && !await SaveAsync("Autosave before opening document library", cancellationToken)) return;
        await RefreshDocumentsAsync(cancellationToken);
        ShowLibrary();
    }

    private void ShowLibrary()
    {
        if (!CanPublishOriginal) return;
        Document = null; _dirty = false; _documentIndex = 0;
        Publish(() => _route.SetLibrary(_documents));
        Publish(() => _route.SetStatus(_documents.Count == 0 ? "No local documents yet. Create one or import a supported file." : "Choose a local document to open."));
        Publish(() => _bus.Fire("Write.Library.Opened"));
    }

    private async Task MoveAsync(int offset)
    {
        if (_documents.Count <= 1)
            return;

        var next = (_documentIndex + offset + _documents.Count) % _documents.Count;
        await OpenDocumentAtAsync(next, CancellationToken.None, saveBeforeSwitch: true);
    }

    public Task<bool> OpenDocumentAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        RunOriginalAsync(() => OpenDocumentCoreAsync(documentId, cancellationToken));

    private async Task<bool> OpenDocumentCoreAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (_disposed || _closePreparing) return false;
        await InitializeAsync(cancellationToken);
        if (!CanPublishOriginal) return false;
        await RefreshDocumentsAsync(cancellationToken);
        if (!CanPublishOriginal) return false;
        var index = -1;
        for (var candidate = 0; candidate < _documents.Count; candidate++)
        {
            if (_documents[candidate].Id != documentId) continue;
            index = candidate;
            break;
        }

        if (index < 0)
        {
            Publish(() => _route.SetStatus("That local document no longer exists."));
            return false;
        }

        await OpenDocumentAtAsync(index, cancellationToken, saveBeforeSwitch: true);
        return Document?.Id == documentId;
    }

    private async Task CreateDocumentAsync(CancellationToken cancellationToken)
    {
        await StopReadAloudForContextChangeAsync();
        if (!CanPublishOriginal) return;
        if (Document is not null && _dirty
            && !await SaveAsync("Autosave before creating document", cancellationToken))
        {
            return;
        }

        var document = NotesDocument.Create("Untitled document");
        var result = await ObserveOriginalAsync(() => _repository.SaveAsync(document, "Write document created", cancellationToken));
        document.Version = result.Version;

        await RefreshDocumentsAsync(cancellationToken);
        if (!CanPublishOriginal) return;
        Document = document;
        _documentIndex = IndexOfDocument(document.Id);
        _dirty = false;
        Publish(() => _route.SetDocument(document, _documentIndex, _documents.Count));
        Publish(() => _route.SetStatus("Created a new local Write document."));
        Publish(() => _bus.Fire("Write.Document.Created"));
    }

    private async Task OpenDocumentAtAsync(
        int index,
        CancellationToken cancellationToken,
        bool saveBeforeSwitch)
    {
        await StopReadAloudForContextChangeAsync();
        if (!CanPublishOriginal) return;
        if (_documents.Count == 0)
            return;

        if (saveBeforeSwitch
            && Document is not null
            && _dirty
            && !await SaveAsync("Autosave before switching document", cancellationToken))
        {
            return;
        }

        index = Math.Clamp(index, 0, _documents.Count - 1);
        var loaded = await ObserveOriginalAsync(() => _repository.LoadAsync(_documents[index].Id, cancellationToken));
        if (!CanPublishOriginal) return;
        if (loaded is null)
        {
            await RefreshDocumentsAsync(cancellationToken);
            Publish(() => _route.SetStatus("That local document no longer exists."));
            return;
        }

        Document = loaded;
        _documentIndex = index;
        _dirty = false;
        Publish(() => _route.SetDocument(loaded, index, _documents.Count));
        Publish(() => _route.SetStatus(
            loaded.Recovery.HasUnsavedRecovery
                ? "Recovered the last valid local version. Review it, then save to confirm recovery."
                : "Saved locally Â· autosave is on"));
        Publish(() => _bus.Fire("Write.Document.Opened"));
    }

    private async Task RefreshDocumentsAsync(CancellationToken cancellationToken)
    {
        var documents = await ObserveOriginalAsync(() => _repository.ListAsync(cancellationToken));
        if (CanPublishOriginal) _documents = documents;
    }

    private int IndexOfDocument(Guid id)
    {
        for (var index = 0; index < _documents.Count; index++)
        {
            if (_documents[index].Id == id)
                return index;
        }

        return 0;
    }

    private void UpdatePosition()
    {
        if (Document is null)
            return;

        Publish(() => _route.DocumentPositionText.Content = _documents.Count == 0
            ? "Local document"
            : $"{_documentIndex + 1} of {_documents.Count} Â· v{Document.Version}");
    }

    private Task RunBusyAsync(Func<Task> action, string description) =>
        RunOriginalAsync(() => RunBusyCoreAsync(action, description));

    private async Task RunBusyCoreAsync(Func<Task> action, string description)
    {
        if (_busy || _disposed || _closePreparing)
            return;

        SetBusy(true);
        try
        {
            await ObserveOriginalAsync(action);
        }
        catch (Exception ex)
        {
            _work.Executing?.Retain(ex);
            Publish(() => _route.SetStatus($"Couldnâ€™t {description}: {ex.Message}"));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Publish(() => _route.SetBusy(busy));
    }

    private void DetachOriginalCallbacks()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_autosaveTimer is not null)
        {
            _autosaveTimer.Stop();
            _autosaveTimer.Tick -= OnAutosaveTick;
        }
        Loaded -= OnLoaded;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        if (_route is null) return;
        _route.LibraryRequested -= OnLibraryRequested;
        _route.DocumentOpenRequested -= OnDocumentOpenRequested;
        _route.AiProposalRequested -= OnAiProposalRequested;
        _route.AiApplyRequested -= OnAiApplyRequested;
        _route.AiRejectRequested -= OnAiRejectRequested;
        _route.NewRequested -= OnNewRequested;
        _route.ImportRequested -= OnImportRequested;
        _route.ExportRequested -= OnExportRequested;
        _route.SaveRequested -= OnSaveRequested;
        _route.PreviousRequested -= OnPreviousRequested;
        _route.NextRequested -= OnNextRequested;
        _route.DocumentChanged -= OnWordDocumentChanged;
        _route.ImageRequested -= OnWordImageRequested;
        _route.ReadAloudRequested -= OnReadAloudRequested;
        _route.ReadAloudStopRequested -= OnReadAloudStopRequested;
        _route.ReadAloudSkipBackRequested -= OnReadAloudSkipBackRequested;
        _route.ReadAloudSkipForwardRequested -= OnReadAloudSkipForwardRequested;
        _route.ReadAloudPauseResumeRequested -= OnReadAloudPauseResumeRequested;
        if (_readAloud is not null)
        {
            _readAloud.StatusChanged -= OnReadAloudStatusChanged;
            _readAloud.ProgressChanged -= OnReadAloudProgressChanged;
            _readAloud.IsReadingChanged -= OnReadAloudIsReadingChanged;
        }
        _route.TitleChanged -= OnTitleChanged;
        _route.BlockTextChanged -= OnBlockTextChanged;
    }
}
