using System.Security.Cryptography;
using System.Text.Json;

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
    public const long MaxEmbeddedAttachmentBytes = 24L * 1024 * 1024;
    public const long MaxSidecarAttachmentBytes = 512L * 1024 * 1024;
    public const int MaxHistoryEntries = 60;

    private static readonly JsonSerializerOptions HistoryJson = new(JsonSerializerDefaults.Web);

    private readonly JsonFileHavenBoardStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _autosaveTimer;
    private readonly TimeSpan _autosaveDelay = TimeSpan.FromMilliseconds(750);
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
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
            PushHistory();
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

    public bool CanUndo
    {
        get
        {
            lock (_undo) return _undo.Count > 0;
        }
    }

    public bool CanRedo
    {
        get
        {
            lock (_redo) return _redo.Count > 0;
        }
    }

    /// <summary>
    /// Restores the previous document state. The restored state autosaves normally,
    /// so reopening after undo shows the undone state.
    /// </summary>
    public async Task<bool> UndoAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string previous;
            lock (_undo)
            {
                if (_undo.Count == 0) return false;
                previous = _undo.Pop();
            }
            lock (_redo)
            {
                _redo.Push(SerializeRich());
                while (_redo.Count > MaxHistoryEntries) TrimStack(_redo);
            }
            RestoreRich(previous);
            HasUnsavedChanges = true;
            UpdateStatus("Undone — editing…", hasUnsavedChanges: true);
            _autosaveTimer.Change(_autosaveDelay, Timeout.InfiniteTimeSpan);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RedoAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string next;
            lock (_redo)
            {
                if (_redo.Count == 0) return false;
                next = _redo.Pop();
            }
            lock (_undo)
            {
                _undo.Push(SerializeRich());
                while (_undo.Count > MaxHistoryEntries) TrimStack(_undo);
            }
            RestoreRich(next);
            HasUnsavedChanges = true;
            UpdateStatus("Redone — editing…", hasUnsavedChanges: true);
            _autosaveTimer.Change(_autosaveDelay, Timeout.InfiniteTimeSpan);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void PushHistory()
    {
        lock (_undo)
        {
            _undo.Push(SerializeRich());
            while (_undo.Count > MaxHistoryEntries) TrimStack(_undo);
        }
        lock (_redo) _redo.Clear();
    }

    private string SerializeRich() =>
        JsonSerializer.Serialize(Rich, HistoryJson);

    private void RestoreRich(string json)
    {
        var restored = JsonSerializer.Deserialize<HavenRichNotes>(json, HistoryJson)
            ?? throw new InvalidDataException("Undo history entry could not be restored.");
        HavenRichNotesValidator.Validate(restored);
        Document = Document with { RichNotes = restored };
    }

    private static void TrimStack(Stack<string> stack)
    {
        var kept = stack.Take(MaxHistoryEntries).Reverse().ToArray();
        stack.Clear();
        foreach (var entry in kept)
            stack.Push(entry);
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
        ThrowIfDisposed();
        var info = new FileInfo(sourcePath);
        if (!info.Exists)
            throw new FileNotFoundException("Attachment source was not found.", sourcePath);
        if (info.Length > MaxSidecarAttachmentBytes)
            throw new InvalidDataException(
                $"Attachments larger than {MaxSidecarAttachmentBytes / 1024 / 1024} MB are not supported in this build.");
        if (info.Length <= MaxEmbeddedAttachmentBytes)
        {
            var bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            return new HavenRichAttachmentRef
            {
                DisplayName = Path.GetFileName(sourcePath),
                MediaType = GuessMediaType(sourcePath),
                DataBase64 = Convert.ToBase64String(bytes),
                SizeBytes = bytes.Length
            };
        }

        // Large payloads stream into a portable sidecar directory next to the board
        // so the .9to1board file itself stays a safe size. Move/copy the board together
        // with its sibling ".files" directory; a missing sidecar reports Missing, never blank.
        var digest = await HashFileAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var sidecarDir = SidecarDirectoryFor(FilePath);
        Directory.CreateDirectory(sidecarDir);
        var blobPath = Path.Combine(sidecarDir, digest + ".blob");
        if (!File.Exists(blobPath))
        {
            var temp = blobPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var source = File.OpenRead(sourcePath))
                await using (var target = File.Create(temp))
                    await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                File.Move(temp, blobPath);
            }
            finally
            {
                TryDelete(temp);
            }
        }
        return new HavenRichAttachmentRef
        {
            DisplayName = Path.GetFileName(sourcePath),
            MediaType = GuessMediaType(sourcePath),
            LocalReference = "sidecar:" + digest,
            SizeBytes = info.Length
        };
    }

    /// <summary>
    /// Resolves attachment bytes for display: embedded payloads, board-relative
    /// sidecars, or a missing status when the sidecar did not travel with the board.
    /// </summary>
    public Task<HavenAttachmentResolution> ResolveAttachmentAsync(
        HavenRichAttachmentRef attachment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrEmpty(attachment.DataBase64))
        {
            try
            {
                return Task.FromResult(new HavenAttachmentResolution(
                    attachment.Id, HavenAttachmentStatus.Available,
                    Convert.FromBase64String(attachment.DataBase64), null,
                    $"Attachment '{attachment.DisplayName}' is available."));
            }
            catch (FormatException error)
            {
                return Task.FromResult(new HavenAttachmentResolution(
                    attachment.Id, HavenAttachmentStatus.Missing, null, null,
                    $"Attachment '{attachment.DisplayName}' is corrupt: {error.Message}."));
            }
        }
        if (!string.IsNullOrEmpty(attachment.LocalReference) &&
            attachment.LocalReference.StartsWith("sidecar:", StringComparison.Ordinal))
        {
            var digest = attachment.LocalReference["sidecar:".Length..];
            var blobPath = Path.Combine(SidecarDirectoryFor(FilePath), digest + ".blob");
            if (File.Exists(blobPath))
            {
                return Task.FromResult(new HavenAttachmentResolution(
                    attachment.Id, HavenAttachmentStatus.Sidecar, null, blobPath,
                    $"Attachment '{attachment.DisplayName}' is available as a sidecar."));
            }
            return Task.FromResult(new HavenAttachmentResolution(
                attachment.Id, HavenAttachmentStatus.Missing, null, null,
                $"Attachment '{attachment.DisplayName}' is missing: copy the board together with its '.files' directory."));
        }
        return Task.FromResult(new HavenAttachmentResolution(
            attachment.Id, HavenAttachmentStatus.Missing, null, null,
            $"Attachment '{attachment.DisplayName}' has no resolvable payload."));
    }

    public static string SidecarDirectoryFor(string? boardPath)
    {
        if (string.IsNullOrWhiteSpace(boardPath))
            throw new InvalidOperationException("Save the board first; sidecar attachments need a board location.");
        var full = Path.GetFullPath(boardPath);
        return Path.Combine(
            Path.GetDirectoryName(full) ?? throw new InvalidOperationException("Board path has no directory."),
            Path.GetFileNameWithoutExtension(full) + ".files");
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
