using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;

namespace Haven.Desktop.Views.Pages.Present;

public sealed partial class PresentPage : UserControl, IDisposable
{
    private readonly HavenEventBus _bus;
    private readonly IPresentRepository _repository;
    private readonly IPresentExportService _exporter;
    private readonly PresentHavenScene _route;
    private readonly DispatcherTimer _autosaveTimer;
    private IReadOnlyList<PresentDocumentSummary> _documents = [];
    private int _deckIndex;
    private int _slideIndex;
    private int _saveRunning;
    private long _editGeneration;
    private bool _closePreparing;
    private static readonly JsonSerializerOptions DocumentJson = new(JsonSerializerDefaults.Web);
    private bool _initialized;
    private bool _busy;
    private bool _dirty;
    private bool _disposed;

    public PresentPage(HavenEventBus bus, IPresentRepository repository, IPresentExportService exporter, IPresentImportService? importer = null)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        InitializeComponent();
        _route = new PresentHavenScene();
        Scene.Root = _route.Root;
        _route.PreviousDeckRequested += OnPreviousDeckRequested; _route.NextDeckRequested += OnNextDeckRequested; _route.NewDeckRequested += OnNewDeckRequested;
        _route.SaveRequested += OnSaveRequested; _route.ExportRequested += OnExportRequested;
        _route.PreviousSlideRequested += OnPreviousSlideRequested; _route.NextSlideRequested += OnNextSlideRequested; _route.AddSlideRequested += OnAddSlideRequested; _route.DeleteSlideRequested += OnDeleteSlideRequested;
        _route.DeckTitleChanged += OnDeckTitleChanged; _route.SlideTitleChanged += OnSlideTitleChanged; _route.BodyChanged += OnBodyChanged; _route.NotesChanged += OnNotesChanged;
        InitializePhase2(importer);
        InitializeWorkspace();
        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _autosaveTimer.Tick += OnAutosaveTick;
        Loaded += OnLoaded; DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    public PresentDocument? Document { get; private set; }
    public bool IsDirty => _dirty;
    public PresentCompatibilityReport? LastCompatibilityReport { get; private set; }
    internal PresentHavenScene Route => _route;
    internal HavenSceneControl SceneHost => Scene;
    internal Haven.UI.Components.Page SceneRoot => _route.Root;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized || _disposed || _closePreparing) return;
        _initialized = true; SetBusy(true);
        try
        {
            await RefreshDocumentsAsync(cancellationToken);
            if (_documents.Count == 0) await CreateDeckAsync(cancellationToken);
            else await OpenDeckAtAsync(0, cancellationToken, false);
            _autosaveTimer.Start(); _bus.Fire("Present.Opened");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { _initialized = false; throw; }
        catch (Exception ex) { _initialized = false; _route.SetStatus("Couldn’t open local presentations: " + ex.Message); }
        finally { SetBusy(false); }
    }

    public async Task<bool> SaveAsync(string reason = "Manual save", CancellationToken cancellationToken = default)
    {
        if (_disposed) return false;
        if (Document is null || !_dirty) return true;
        if (Interlocked.Exchange(ref _saveRunning, 1) != 0) return false;
        try
        {
            var document = Document;
            if (string.IsNullOrWhiteSpace(document.Title)) document.Title = "Untitled presentation";
            var generation = _editGeneration;
            var snapshot = JsonSerializer.Deserialize<PresentDocument>(
                JsonSerializer.Serialize(document, DocumentJson), DocumentJson)
                ?? throw new InvalidDataException("The presentation could not be snapshotted.");
            var result = await _repository.SaveAsync(snapshot, reason, cancellationToken);
            if (Document is { } current && current.Id == document.Id)
            {
                current.Version = result.Version;
                current.Recovery = snapshot.Recovery;
                if (snapshot.Metadata.TryGetValue("lastSaveReason", out var savedReason))
                    current.Metadata["lastSaveReason"] = savedReason;
                if (generation == _editGeneration) current.UpdatedAt = snapshot.UpdatedAt;
                _dirty = generation != _editGeneration;
            }
            await RefreshDocumentsAsync(cancellationToken);
            if (Document?.Id == document.Id)
            {
                _deckIndex = IndexOfDocument(document.Id); RenderCurrent();
                _route.SetStatus(_dirty ? "Newer changes remain unsaved. Save them before closing."
                    : $"Saved locally at {result.SavedAt.LocalDateTime:t} · v{result.Version}");
            }
            _bus.Fire("Present.Saved"); return Document?.Id == document.Id && !_dirty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _route.SetStatus("Couldn’t save this presentation: " + ex.Message); return false; }
        finally { Interlocked.Exchange(ref _saveRunning, 0); }
    }

    /// <summary>Refuses close over active owner work or edits newer than the saved snapshot.</summary>
    public async Task<bool> PrepareToCloseAsync(
        string reason = "Autosave before closing Present", CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed) return false;
        if (_busy || _closePreparing || Volatile.Read(ref _saveRunning) != 0)
        {
            _route.SetStatus("Finish the current Present operation before closing.");
            return false;
        }
        _closePreparing = true; SetBusy(true);
        try
        {
            if (!await SaveAsync(reason, cancellationToken)) return false;
            if (_dirty)
            {
                _route.SetStatus("Newer changes remain unsaved. Save them before closing.");
                return false;
            }
            return true;
        }
        finally { _closePreparing = false; SetBusy(false); }
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e) { await InitializeAsync(); if (!_disposed) _autosaveTimer.Start(); }
    private async void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) { _autosaveTimer.Stop(); if (_dirty && Document is not null) await SaveAsync("Autosave on leaving Present"); }
    private async void OnAutosaveTick(object? sender, EventArgs e) { if (!_disposed && _dirty && !_busy && Document is not null) await SaveAsync("Autosave"); }
    private async void OnPreviousDeckRequested(object? sender, EventArgs e) => await RunBusyAsync(() => MoveDeckAsync(-1), "open the previous presentation");
    private async void OnNextDeckRequested(object? sender, EventArgs e) => await RunBusyAsync(() => MoveDeckAsync(1), "open the next presentation");
    private async void OnNewDeckRequested(object? sender, EventArgs e) => await RunBusyAsync(() => CreateDeckAsync(CancellationToken.None), "create a presentation");
    private async void OnSaveRequested(object? sender, EventArgs e) => await SaveAsync();
    private async void OnExportRequested(object? sender, EventArgs e) => await RunBusyAsync(PickExportAsync, "export this presentation");
    private void OnPreviousSlideRequested(object? sender, EventArgs e) => MoveSlide(-1);
    private void OnNextSlideRequested(object? sender, EventArgs e) => MoveSlide(1);
    private void OnAddSlideRequested(object? sender, EventArgs e) => AddSlide();
    private void OnDeleteSlideRequested(object? sender, EventArgs e) => DeleteSlide();

    private PresentSlide? CurrentSlide => Document is null || Document.Slides.Count == 0 ? null : Document.Slides[Math.Clamp(_slideIndex, 0, Document.Slides.Count - 1)];
    private void OnDeckTitleChanged(string value) => EditDeckTitle(value);
    private void OnSlideTitleChanged(string value) => EditSlideTitle(value);
    private void OnBodyChanged(string value) => EditBody(value);
    private void OnNotesChanged(string value) => EditNotes(value);

    private void MarkDirty()
    {
        if (Document is null) return; Document.UpdatedAt = DateTimeOffset.UtcNow; ++_editGeneration; _dirty = true; _route.SetStatus("Unsaved changes · autosave is on");
    }

    private void MoveSlide(int offset)
    {
        MoveSlideSelection(offset);
    }

    private void AddSlide()
    {
        AddSlideWithEditor();
    }

    private void DeleteSlide()
    {
        DeleteSlideWithEditor();
    }

    private async Task MoveDeckAsync(int offset)
    {
        if (_documents.Count <= 1) return;
        if (_dirty && !await SaveAsync("Autosave before switching presentation")) return;
        var next = (_deckIndex + offset + _documents.Count) % _documents.Count; await OpenDeckAtAsync(next, CancellationToken.None, false);
    }

    private async Task CreateDeckAsync(CancellationToken cancellationToken)
    {
        if (Document is not null && _dirty && !await SaveAsync("Autosave before creating presentation", cancellationToken)) return;
        var document = PresentDocument.Create("Untitled presentation");
        var result = await _repository.SaveAsync(document, "Presentation created", cancellationToken); document.Version = result.Version;
        await RefreshDocumentsAsync(cancellationToken); Document = document; _deckIndex = IndexOfDocument(document.Id); _slideIndex = 0; _dirty = false;
        RenderCurrent(); _route.SetStatus("Created a new local presentation."); _bus.Fire("Present.Document.Created");
    }

    private async Task OpenDeckAtAsync(int index, CancellationToken cancellationToken, bool saveBeforeSwitch)
    {
        if (_documents.Count == 0) return;
        if (saveBeforeSwitch && Document is not null && _dirty && !await SaveAsync("Autosave before switching presentation", cancellationToken)) return;
        index = Math.Clamp(index, 0, _documents.Count - 1);
        var loaded = await _repository.LoadAsync(_documents[index].Id, cancellationToken);
        if (loaded is null) { await RefreshDocumentsAsync(cancellationToken); _route.SetStatus("That local presentation no longer exists."); return; }
        loaded.Normalize(); Document = loaded; _deckIndex = index; _slideIndex = 0; _dirty = false; RenderCurrent();
        _route.SetStatus(loaded.Recovery.RecoveredFromBackup ? loaded.Recovery.Message : "Saved locally · autosave is on"); _bus.Fire("Present.Document.Opened");
    }

    internal async Task<bool> ExportToPathAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath); if (Document is null) return false;
        if (_dirty && !await SaveAsync("Save before export", cancellationToken)) return false;
        try
        {
            string path;
            if (_exporter is IPresentReportExportService reportExporter)
            {
                LastCompatibilityReport = reportExporter.PreviewExport(Document, destinationPath);
                if (LastCompatibilityReport.HasBlockingIssues)
                {
                    _route.SetStatus("Export blocked by compatibility errors. Remove the unsupported content or select another format.");
                    return false;
                }
                var result = await reportExporter.ExportWithReportAsync(Document, destinationPath, cancellationToken);
                path = result.Path;
                LastCompatibilityReport = result.CompatibilityReport;
                if (LastCompatibilityReport.Issues.Count > 0)
                {
                    var firstIssue = LastCompatibilityReport.Issues[0];
                    _route.SetStatus($"Exported {Path.GetFileName(path)} · {LastCompatibilityReport.Issues.Count} compatibility issue(s): {firstIssue.Description}");
                }
                else
                {
                    _route.SetStatus("Exported " + Path.GetFileName(path));
                }
            }
            else
            {
                LastCompatibilityReport = null;
                path = await _exporter.ExportAsync(Document, destinationPath, cancellationToken);
                _route.SetStatus("Exported " + Path.GetFileName(path));
            }
            _bus.Fire("Present.Document.Exported");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _route.SetStatus("Couldn’t export this presentation: " + ex.Message); return false; }
    }

    private async Task PickExportAsync()
    {
        if (Document is null) return; var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null) { _route.SetStatus("Export isn’t available from this platform surface."); return; }
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export presentation", SuggestedFileName = SanitizeFileName(Document.Title) + ".9to1p", DefaultExtension = "9to1p",
            FileTypeChoices =
            [
                new FilePickerFileType("9to1 presentation") { Patterns = ["*.9to1p"] },
                new FilePickerFileType("PowerPoint presentation") { Patterns = ["*.pptx"] }
            ],
            ShowOverwritePrompt = true
        });
        if (file is null) return; var localPath = file.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath)) { await ExportToPathAsync(localPath); return; }
        var temporaryExtension = Path.GetExtension(file.Name);
        if (!temporaryExtension.Equals(".pptx", StringComparison.OrdinalIgnoreCase) &&
            !temporaryExtension.Equals(".9to1p", StringComparison.OrdinalIgnoreCase))
            temporaryExtension = ".9to1p";
        var temporary = Path.Combine(Path.GetTempPath(), $"haven-present-export-{Guid.NewGuid():N}{temporaryExtension}");
        try
        {
            if (!await ExportToPathAsync(temporary)) return;
            await using var source = File.OpenRead(temporary); await using var destination = await file.OpenWriteAsync(); destination.SetLength(0);
            await source.CopyToAsync(destination); await destination.FlushAsync(); _route.SetStatus("Exported " + file.Name);
        }
        finally { TryDeleteTemporary(temporary); }
    }

    private void RenderCurrent()
    {
        if (Document is null) return; Document.Normalize(); _slideIndex = Math.Clamp(_slideIndex, 0, Document.Slides.Count - 1);
        if (_editor is null || !ReferenceEquals(_editor.Document, Document)) AttachEditor(Document);
        _route.SetDocument(Document, _deckIndex, _documents.Count, _slideIndex); RenderPhase2();
        _route.SetWorkspaceDocument(Document, _slideIndex);
        _route.SetWorkspaceSelection(Document, _slideIndex, _editor!.Selection.ElementIds);
    }
    private async Task RefreshDocumentsAsync(CancellationToken cancellationToken) => _documents = await _repository.ListAsync(cancellationToken);
    private int IndexOfDocument(Guid id) { for (var index = 0; index < _documents.Count; index++) if (_documents[index].Id == id) return index; return 0; }
    private async Task RunBusyAsync(Func<Task> action, string description)
    {
        if (_busy || _disposed || _closePreparing) return; SetBusy(true);
        try { await action(); } catch (Exception ex) { _route.SetStatus($"Couldn’t {description}: {ex.Message}"); } finally { SetBusy(false); }
    }
    private void SetBusy(bool busy) { _busy = busy; _route.SetBusy(busy); _route.SetPhase2Busy(busy); }
    private static string SanitizeFileName(string title)
    {
        var value = string.IsNullOrWhiteSpace(title) ? "Untitled presentation" : title.Trim(); foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_'); return value;
    }
    private void TryDeleteTemporary(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _route.SetStatus(_route.StatusText.Content + " Temporary-file cleanup failed: " + ex.Message); }
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _autosaveTimer.Stop(); _autosaveTimer.Tick -= OnAutosaveTick; Loaded -= OnLoaded; DetachedFromVisualTree -= OnDetachedFromVisualTree;
        _route.PreviousDeckRequested -= OnPreviousDeckRequested; _route.NextDeckRequested -= OnNextDeckRequested; _route.NewDeckRequested -= OnNewDeckRequested; _route.SaveRequested -= OnSaveRequested; _route.ExportRequested -= OnExportRequested;
        _route.PreviousSlideRequested -= OnPreviousSlideRequested; _route.NextSlideRequested -= OnNextSlideRequested; _route.AddSlideRequested -= OnAddSlideRequested; _route.DeleteSlideRequested -= OnDeleteSlideRequested;
        _route.DeckTitleChanged -= OnDeckTitleChanged; _route.SlideTitleChanged -= OnSlideTitleChanged; _route.BodyChanged -= OnBodyChanged; _route.NotesChanged -= OnNotesChanged; DisposeWorkspace(); DisposePhase2(); _route.Dispose();
    }
}
