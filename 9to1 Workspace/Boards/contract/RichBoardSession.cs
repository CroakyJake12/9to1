namespace CakeOS.Apps.Boards.Contract;

/// <summary>
/// Application-session boundary for rich revision notes.
/// All mutations flow through <see cref="HavenRichNotesOps"/>, are validated,
/// and reach durable storage via debounced autosave or explicit save.
/// The UI must only report "Saved" after <see cref="LastSavedUtc"/> advances,
/// which happens exclusively after the store confirms the write.
/// </summary>
public sealed class RichBoardSession : IAsyncDisposable
{
    public const long MaxEmbeddedAttachmentBytes = 5L * 1024 * 1024;

    private readonly JsonFileHavenBoardStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _autosaveTimer;
    private readonly TimeSpan _autosaveDelay = TimeSpan.FromMilliseconds(750);
    private long _lastSavedRichVersion = -1;
    private bool _disposed;

    private RichBoardSession(JsonFileHavenBoardStore store, HavenBoardDocument document, string? filePath)
    {
        _store = store;
        Document = document;
        FilePath = filePath;
        _lastSavedRichVersion = document.RichNotes?.Version ?? -1;
        _autosaveTimer = new Timer(_ => _ = AutosaveTickAsync(), null, Timeout.Infinite, Timeout.Infinite);
        UpdateStatus(document.RichNotes is null ? "No notes yet" : "Loaded locally", hasUnsavedChanges: false);
    }

    public HavenBoardDocument Document { get; private set; }
    public HavenRichNotes Rich => Document.RichNotes ?? throw new InvalidOperationException("This board has no rich notes yet.");
    public bool HasRichNotes => Document.RichNotes is not null;
    public string? FilePath { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public bool HasUnsavedChanges { get; private set; }
    public DateTimeOffset? LastSavedUtc { get; private set; }

    public event EventHandler<string>? StatusChanged;

    public static Task<RichBoardSession> CreateNewAsync(
        JsonFileHavenBoardStore store, string title, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var now = DateTimeOffset.UtcNow;
        var document = new HavenBoardDocument(
            HavenBoardDocument.FormatIdentity,
            HavenBoardDocument.CurrentSchemaVersion,
            Guid.NewGuid(), now, now, Snapshot: null,
            RichNotes: HavenRichNotes.Create(title));
        HavenRichNotesValidator.Validate(document.RichNotes!);
        return Task.FromResult(new RichBoardSession(store, document, filePath: null));
    }

    public static async Task<RichBoardSession> OpenAsync(
        JsonFileHavenBoardStore store, string boardId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var document = await store.LoadDocumentAsync(boardId, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            var created = await CreateNewAsync(store, boardId, cancellationToken).ConfigureAwait(false);
            created.FilePath = store.GetDocumentPath(boardId);
            return created;
        }
        return new RichBoardSession(store, EnsureRich(document), store.GetDocumentPath(boardId));
    }

    public static async Task<RichBoardSession> OpenAtPathAsync(
        JsonFileHavenBoardStore store, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var document = await store.LoadDocumentAtPathAsync(path, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException("No .9to1board was found at the requested path.", path);
        return new RichBoardSession(store, EnsureRich(document), Path.GetFullPath(path));
    }

    private static HavenBoardDocument EnsureRich(HavenBoardDocument document)
    {
        if (document.RichNotes is not null)
            return document;
        if (document.Snapshot is null)
            throw new InvalidDataException("The .9to1board contains neither a board snapshot nor rich notes.");
        // v1 files predate the seed-on-read migration only if produced before it existed;
        // seed now so the session always has editable notes while the snapshot stays intact.
        return document with { RichNotes = HavenRichNotesSeeder.SeedFromSnapshot(document.Snapshot) };
    }

    /// <summary>Applies a validated mutation and schedules autosave. Throws on invalid edits.</summary>
    public async Task MutateAsync(Action<HavenRichNotes> mutation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            mutation(Rich);
            HavenRichNotesValidator.Validate(Rich);
            HasUnsavedChanges = true;
            UpdateStatus("Editing…", hasUnsavedChanges: true);
            _autosaveTimer.Change(_autosaveDelay, Timeout.InfiniteTimeSpan);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task RequestAutosaveAsync()
    {
        ThrowIfDisposed();
        _autosaveTimer.Change(_autosaveDelay, Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    /// <summary>Waits for any pending autosave to finish without forcing a new save.</summary>
    public async Task FlushAsync()
    {
        ThrowIfDisposed();
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
        // If a save is running, the gate above serialized after it; re-check dirtiness.
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (HasUnsavedChanges)
                await SaveCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Durably persists to the current path (or board path). Reports "Saved" only on success.</summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Saves the same stable document identity to a new user-chosen path.</summary>
    public async Task SaveAsAsync(string path, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FilePath = Path.GetFullPath(path);
            await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HavenRichAttachmentRef> ImportAttachmentAsync(
        string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        if (bytes.Length > MaxEmbeddedAttachmentBytes)
            throw new InvalidDataException(
                $"Attachments larger than {MaxEmbeddedAttachmentBytes / 1024 / 1024} MB must stay as sidecar references; not embedded in this RC.");
        return new HavenRichAttachmentRef
        {
            DisplayName = Path.GetFileName(sourcePath),
            MediaType = GuessMediaType(sourcePath),
            DataBase64 = Convert.ToBase64String(bytes)
        };
    }

    private async Task AutosaveTickAsync()
    {
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!HasUnsavedChanges || _disposed)
                    return;
                // Never let an older save overwrite a newer edit: saves are serialized
                // and each mutation bumps Rich.Version, so only save when dirty.
                await SaveCoreAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception error)
        {
            UpdateStatus("Autosave failed: " + FirstLine(error.Message), hasUnsavedChanges: true);
        }
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        if (FilePath is null)
            throw new InvalidOperationException("This board has no file path yet; use Save As first.");
        if (Document.RichNotes is null)
            throw new InvalidOperationException("This board has no rich notes to save.");

        var capturedVersion = Document.RichNotes.Version;
        HavenRichNotesValidator.Validate(Document.RichNotes);
        var now = DateTimeOffset.UtcNow;
        var toSave = Document with { ModifiedUtc = now };
        await _store.SaveDocumentAtPathAsync(toSave, FilePath, cancellationToken).ConfigureAwait(false);

        Document = toSave;
        _lastSavedRichVersion = capturedVersion;
        LastSavedUtc = now;
        HasUnsavedChanges = false;
        UpdateStatus("Saved " + now.ToLocalTime().ToString("HH:mm:ss"), hasUnsavedChanges: false);
    }

    private void UpdateStatus(string status, bool hasUnsavedChanges)
    {
        Status = status;
        HasUnsavedChanges = hasUnsavedChanges;
        StatusChanged?.Invoke(this, status);
    }

    private static string FirstLine(string message) =>
        message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? message;

    private static string GuessMediaType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".pdf" => "application/pdf",
            ".txt" or ".md" => "text/plain",
            _ => "application/octet-stream"
        };

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try { await _autosaveTimer.DisposeAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException) { }

        // Flush pending edits so close/switch never loses the last keystrokes.
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (HasUnsavedChanges && FilePath is not null)
            {
                try { await SaveCoreAsync(CancellationToken.None).ConfigureAwait(false); }
                catch { UpdateStatus("Save on close failed", hasUnsavedChanges: true); }
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
