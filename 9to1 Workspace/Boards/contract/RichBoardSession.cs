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

    private readonly JsonFileHavenBoardStore? _store;
    private readonly bool _readOnly;
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<bool>>? _externalSave;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _autosaveTimer;
    private readonly TimeSpan _autosaveDelay = TimeSpan.FromMilliseconds(750);
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private long _lastSavedRichVersion = -1;
    private bool _disposed;
    private string? _recoveryPath;
    private string? _expectedFileHash;
    private string? _expectedRecoveryHash;
    private string? _conflictContentHash;
    public string? LastConflictRecoveryPath { get; private set; }
    public event Action<string, string>? LocalFileSaved;

    public static async Task<RichBoardSession> OpenRecoverableAsync(JsonFileHavenBoardStore store,
        string recoveryPath, string? destination, string? expectedFileHash, CancellationToken token = default)
    {
        using var lease = await LocalBoardWriteLease.AcquireAsync(recoveryPath, token).ConfigureAwait(false);
        return await OpenRecoverableUnderLeaseAsync(store, recoveryPath, destination, expectedFileHash, lease, token).ConfigureAwait(false);
    }

    public static async Task<RichBoardSession> OpenRecoverableUnderLeaseAsync(JsonFileHavenBoardStore store,
        string recoveryPath, string? destination, string? expectedFileHash, LocalBoardWriteLease lease,
        CancellationToken token = default)
    {
        if (!lease.IsHeldFor(recoveryPath)) throw new InvalidOperationException("The local board opening lease is not current.");
        var session = await OpenAtPathAsync(store, recoveryPath, token).ConfigureAwait(false);
        // The canonical loader supports a missing primary with a validated backup.
        // Restore that complete revision under this lease before hashing its head.
        if (!File.Exists(recoveryPath))
            await store.SaveDocumentAtPathAsync(session.Document, recoveryPath, token).ConfigureAwait(false);
        session._recoveryPath = Path.GetFullPath(recoveryPath);
        session.FilePath = destination is null ? session._recoveryPath : Path.GetFullPath(destination);
        session._expectedRecoveryHash = await HashFileAsync(recoveryPath, token).ConfigureAwait(false);
        session._expectedFileHash = expectedFileHash ?? (destination is null ? session._expectedRecoveryHash : null);
        return session;
    }


    private RichBoardSession(JsonFileHavenBoardStore? store, HavenBoardDocument document, string? filePath,
        bool readOnly = false,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<bool>>? externalSave = null)
    {
        _store = store;
        _readOnly = readOnly;
        _externalSave = externalSave;
        Document = document;
        FilePath = filePath;
        _lastSavedRichVersion = document.RichNotes?.Version ?? -1;
        _autosaveTimer = new Timer(_ => _ = AutosaveTickAsync(), null, Timeout.Infinite, Timeout.Infinite);
        UpdateStatus(document.RichNotes is null ? "No notes yet" : "Loaded locally", hasUnsavedChanges: false);
    }

    public HavenBoardDocument Document { get; private set; }
    public HavenRichNotes Rich => Document.RichNotes ?? throw new InvalidOperationException("This board has no rich notes yet.");
    public bool HasRichNotes => Document.RichNotes is not null;
    public bool IsReadOnly => _readOnly;
    public bool CanImportLocalAttachments => !_disposed && !_readOnly && _externalSave is null;
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

    /// <summary>Creates an isolated immutable session from an already validated document snapshot.</summary>
    public static RichBoardSession OpenReadOnly(HavenBoardDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var copy = HavenBoardDocumentCodec.Deserialize(JsonSerializer.SerializeToUtf8Bytes(document, HistoryJson));
        return new RichBoardSession(null, EnsureRich(copy), filePath: null, readOnly: true);
    }

    /// <summary>Creates an editable detached session whose only persistence authority is
    /// the caller's explicit save callback. It never autosaves or writes to a path/store.</summary>
    public static RichBoardSession OpenExternallyWritable(
        HavenBoardDocument document,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<bool>> save)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(save);
        var copy = HavenBoardDocumentCodec.Deserialize(JsonSerializer.SerializeToUtf8Bytes(document, HistoryJson));
        return new RichBoardSession(null, EnsureRich(copy), filePath: null, externalSave: save);
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
        if (_readOnly) throw new InvalidOperationException("This board source is open read-only.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A refused mutation must not consume or pollute an undo step and must not leave a
            // partially applied tree behind: capture the head first, and only record history
            // once the mutation and validation have both succeeded.
            var before = SerializeRich();
            try
            {
                mutation(Rich);
                HavenRichNotesValidator.Validate(Rich);
            }
            catch
            {
                // Roll the head back exactly; the refused attempt must not be observable.
                ApplyRich(before, validate: false);
                throw;
            }
            PushHistory(before);
            HasUnsavedChanges = true;
            UpdateStatus("Editing…", hasUnsavedChanges: true);
            if (_externalSave is null) _autosaveTimer.Change(_autosaveDelay, Timeout.InfiniteTimeSpan);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The same mutation owner/history, with a deny-only precondition
    /// checked under its gate BEFORE history or content changes. A refusal is not
    /// an empty undo transaction. Exceptions/cancellation retain their normal cause.</summary>
    public async Task<bool> MutateIfAsync(Func<HavenRichNotes, bool> condition,
        Action<HavenRichNotes> mutation, CancellationToken cancellationToken = default,
        bool requireReadableDocument = false)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(mutation);
        ThrowIfDisposed();
        if (_readOnly) return false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!condition(Rich)) return false;
            var before = SerializeRich();
            try
            {
                mutation(Rich);
                HavenRichNotesValidator.Validate(Rich);
                // Include persisted indentation/escaping and the longest possible save timestamp.
                // List splicing may duplicate metadata beyond an import's estimated byte budget.
                if (requireReadableDocument && JsonFileHavenBoardStore.GetDocumentByteCount(
                        Document with { ModifiedUtc = DateTimeOffset.MaxValue }) > HavenBoardDocumentCodec.MaximumDocumentBytes)
                    throw new InvalidDataException("This insertion would exceed the maximum document size. Choose another destination.");
            }
            catch
            {
                ApplyRich(before, validate: false);
                throw;
            }
            PushHistory(before);
            HasUnsavedChanges = true;
            UpdateStatus("Editing…", hasUnsavedChanges: true);
            if (_externalSave is null) _autosaveTimer.Change(_autosaveDelay, Timeout.InfiniteTimeSpan);
            return true;
        }
        finally { _gate.Release(); }
    }

    public bool CanUndo
    {
        get
        {
            if (_readOnly) return false;
            lock (_undo) return _undo.Count > 0;
        }
    }

    public bool CanRedo
    {
        get
        {
            if (_readOnly) return false;
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
        if (_readOnly) return false;
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
            if (_externalSave is null) _autosaveTimer.Change(_autosaveDelay, Timeout.InfiniteTimeSpan);
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
        if (_readOnly) return false;
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
            if (_externalSave is null) _autosaveTimer.Change(_autosaveDelay, Timeout.InfiniteTimeSpan);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void PushHistory()
    {
        PushHistory(SerializeRich());
    }

    private void PushHistory(string previousHead)
    {
        lock (_undo)
        {
            _undo.Push(previousHead);
            while (_undo.Count > MaxHistoryEntries) TrimStack(_undo);
        }
        lock (_redo) _redo.Clear();
    }

    private string SerializeRich() =>
        JsonSerializer.Serialize(Rich, HistoryJson);

    private void RestoreRich(string json) => ApplyRich(json, validate: true);

    /// <summary>Installs a serialized head. A rollback re-installs the exact previous head and
    /// therefore must not re-run validation over a document that was already current.</summary>
    private void ApplyRich(string json, bool validate)
    {
        var restored = JsonSerializer.Deserialize<HavenRichNotes>(json, HistoryJson)
            ?? throw new InvalidDataException("Undo history entry could not be restored.");
        if (validate) HavenRichNotesValidator.Validate(restored);
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
        if (_readOnly || _externalSave is not null) return Task.CompletedTask;
        _autosaveTimer.Change(_autosaveDelay, Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    /// <summary>Waits for any pending autosave to finish without forcing a new save.</summary>
    public async Task FlushAsync()
    {
        ThrowIfDisposed();
        if (_readOnly || _externalSave is not null) return;
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
        if (_readOnly) throw new InvalidOperationException("This board source is open read-only and cannot be saved.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_externalSave is not null && !HasUnsavedChanges) return;
            UpdateStatus("Saving…", hasUnsavedChanges: true);
            await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // IRichBoardSession promises a real failure state: the live edits and the last
            // durable revision both survive, and the label never claims a completed save.
            UpdateStatus("Save failed: " + FirstLine(error.Message), hasUnsavedChanges: HasUnsavedChanges);
            throw;
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
        if (_readOnly || _externalSave is not null)
            throw new InvalidOperationException("This board source cannot be saved to a different path.");
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A rejected/canceled Save As must leave normal Save and close pointed
            // at the last acknowledged file, rather than the attempted target.
            UpdateStatus("Saving…", hasUnsavedChanges: true);
            await SaveCoreAsync(cancellationToken, Path.GetFullPath(path)).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            UpdateStatus("Save failed: " + FirstLine(error.Message), hasUnsavedChanges: HasUnsavedChanges);
            throw;
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
        if (!CanImportLocalAttachments)
            throw new InvalidOperationException("This board source does not grant local attachment import access.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var initialLength = source.Length;
            if (initialLength > MaxSidecarAttachmentBytes)
                throw new InvalidDataException(
                    $"Attachments larger than {MaxSidecarAttachmentBytes / 1024 / 1024} MB are not supported in this build.");
            // The per-file threshold alone is insufficient: several embedded files can exceed
            // the canonical document read limit. Prefer sidecars before exhausting that budget.
            var embeddedBudget = initialLength <= MaxEmbeddedAttachmentBytes
                ? HavenBoardDocumentCodec.MaximumDocumentBytes - JsonFileHavenBoardStore.GetDocumentByteCount(Document) - 8192 : 0;
            var embeddedFits = initialLength <= MaxEmbeddedAttachmentBytes && 4 * ((initialLength + 2) / 3) <= embeddedBudget;
            using var memory = embeddedFits ? new MemoryStream((int)initialLength) : null;
            Stream payloadSource = source;
            if (memory is not null)
            {
                var copied = await CopyAttachmentPayloadAsync(source, memory, MaxEmbeddedAttachmentBytes, cancellationToken).ConfigureAwait(false);
                if (copied.Size != initialLength) throw new IOException("The attachment changed while being imported. Choose it again.");
                ThrowIfDisposed();
                var encoded = Convert.ToBase64String(memory.GetBuffer(), 0, (int)copied.Size);
                // '+' is escaped by the JSON encoder. Budget the actual encoded string, not only
                // the base64 character count, before claiming this payload can stay embedded.
                if (JsonSerializer.SerializeToUtf8Bytes(encoded, HistoryJson).LongLength <= embeddedBudget)
                    return new HavenRichAttachmentRef
                    {
                        DisplayName = Path.GetFileName(sourcePath), MediaType = GuessMediaType(sourcePath),
                        DataBase64 = encoded, SizeBytes = copied.Size
                    };
                // Fall back from the same prepared bytes, never reopen a potentially changed file.
                memory.Position = 0;
                payloadSource = memory;
            }

            // Hash exactly the bytes copied from this one open source stream. The old path hashed
            // the source, reopened it, then copied potentially different bytes under the first hash.
            var directory = SidecarDirectoryFor(FilePath);
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, ".import-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                (long Size, string Digest) copied;
                await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    copied = await CopyAttachmentPayloadAsync(payloadSource, target, MaxSidecarAttachmentBytes, cancellationToken).ConfigureAwait(false);
                    await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                if (copied.Size != initialLength) throw new IOException("The attachment changed while being imported. Choose it again.");
                ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();
                var blob = Path.Combine(directory, copied.Digest + ".blob");
                try { File.Move(temporary, blob); }
                catch (IOException) when (File.Exists(blob))
                {
                    // Another reference/import may already own the same immutable payload.
                    // Never overwrite a conflicting existing file or delete a shared blob.
                    if (await HashFileAsync(blob, cancellationToken).ConfigureAwait(false) != copied.Digest)
                        throw new IOException("The existing attachment payload is inconsistent; it was preserved unchanged.");
                }
                return new HavenRichAttachmentRef
                {
                    DisplayName = Path.GetFileName(sourcePath), MediaType = GuessMediaType(sourcePath),
                    LocalReference = "sidecar:" + copied.Digest, SizeBytes = copied.Size
                };
            }
            finally { TryDelete(temporary); }
        }
        finally { _gate.Release(); }
    }

    private static async Task<(long Size, string Digest)> CopyAttachmentPayloadAsync(
        Stream source, Stream target, long limit, CancellationToken token)
    {
        var buffer = new byte[65536];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long size = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            size += read;
            if (size > limit) throw new InvalidDataException("The attachment grew beyond its import size limit. Choose it again.");
            await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            hash.AppendData(buffer, 0, read);
        }
        return (size, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
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
        if (!string.IsNullOrEmpty(attachment.DataBase64) ||
            attachment is { DataBase64: "", SizeBytes: 0, LocalReference: null or "" })
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
            if (string.IsNullOrWhiteSpace(FilePath))
                return Task.FromResult(new HavenAttachmentResolution(
                    attachment.Id, HavenAttachmentStatus.Missing, null, null,
                    $"Attachment '{attachment.DisplayName}' is unavailable in this detached document view."));
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

    private async Task SaveCoreAsync(CancellationToken cancellationToken, string? destination = null)
    {
        using var lease = _recoveryPath is null ? null :
            await LocalBoardWriteLease.AcquireAsync(_recoveryPath, cancellationToken).ConfigureAwait(false);
        await SaveCoreLockedAsync(cancellationToken, destination).ConfigureAwait(false);
    }

    private async Task SaveCoreLockedAsync(CancellationToken cancellationToken, string? destination)
    {
        var targetPath = destination ?? FilePath;
        if (targetPath is null && _externalSave is null)
            throw new InvalidOperationException("This board has no file path yet; use Save As first.");
        if (Document.RichNotes is null)
            throw new InvalidOperationException("This board has no rich notes to save.");

        var capturedVersion = Document.RichNotes.Version;
        HavenRichNotesValidator.Validate(Document.RichNotes);
        var now = DateTimeOffset.UtcNow;
        var toSave = Document with { ModifiedUtc = now };
        if (_externalSave is not null)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(toSave, HistoryJson);
            if (bytes.Length > HavenBoardDocumentCodec.MaximumDocumentBytes)
                throw new InvalidDataException("The board exceeds the maximum document size for this source.");
            _ = HavenBoardDocumentCodec.Deserialize(bytes);
            cancellationToken.ThrowIfCancellationRequested();
            if (!await _externalSave(bytes, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("The external source did not confirm this board revision.");
        }
        else
        {
            var store = _store ?? throw new InvalidOperationException("This board session has no writable store.");
            if (_recoveryPath is not null)
            {
                // Both expected heads are checked while holding the same board lease,
                // before either canonical file is touched. A stale editor retains its
                // own version separately; it cannot replace the current private head.
                var recoveryCurrent = File.Exists(_recoveryPath)
                    ? await HashFileAsync(_recoveryPath, cancellationToken).ConfigureAwait(false) : null;
                var externalCurrent = FilePath is not null && !SamePath(FilePath, _recoveryPath) && File.Exists(FilePath)
                    ? await HashFileAsync(FilePath, cancellationToken).ConfigureAwait(false) : null;
                if (_expectedRecoveryHash != recoveryCurrent ||
                    FilePath is not null && !SamePath(FilePath, _recoveryPath) && _expectedFileHash != externalCurrent)
                {
                    await PreserveConflictCopyAsync(store, toSave).ConfigureAwait(false);
                    throw new IOException("This board changed in another editor. Your version was retained separately at " + LastConflictRecoveryPath);
                }
                if (!SamePath(_recoveryPath, targetPath!))
                {
                    await store.SaveDocumentAndSidecarsAtPathAsync(toSave, _recoveryPath,
                        FilePath ?? _recoveryPath, cancellationToken).ConfigureAwait(false);
                    // The private commit is already acknowledged; cancellation must not
                    // leave its expected head behind even if the destination write fails.
                    _expectedRecoveryHash = await HashFileAsync(_recoveryPath, CancellationToken.None).ConfigureAwait(false);
                }
            }
            await store.SaveDocumentAndSidecarsAtPathAsync(toSave, targetPath!,
                FilePath is { } previousPath && !SamePath(previousPath, targetPath!) ? previousPath : null,
                cancellationToken).ConfigureAwait(false);
            FilePath = targetPath;
            if (_recoveryPath is not null)
            {
                // Complete receipt/bookkeeping without caller cancellation after commit.
                _expectedFileHash = await HashFileAsync(targetPath!, CancellationToken.None).ConfigureAwait(false);
                if (SamePath(_recoveryPath, targetPath!)) _expectedRecoveryHash = _expectedFileHash;
            }

        }

        Document = toSave;
        _lastSavedRichVersion = capturedVersion;
        LastSavedUtc = now;
        HasUnsavedChanges = false;
        if (_recoveryPath is not null)
        {
            try { LocalFileSaved?.Invoke(FilePath!, _expectedFileHash!); }
            catch (Exception error)
            {
                throw new IOException("The board contents were saved, but its destination metadata could not be updated. Retry Save.", error);
            }
        }
        UpdateStatus("Saved " + now.ToLocalTime().ToString("HH:mm:ss"), hasUnsavedChanges: false);
    }

    private async Task PreserveConflictCopyAsync(JsonFileHavenBoardStore store, HavenBoardDocument document)
    {
        var contentHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Document, HistoryJson)));
        if (_conflictContentHash == contentHash && LastConflictRecoveryPath is { } existing && File.Exists(existing)) return;
        var directory = Path.Combine(Path.GetDirectoryName(_recoveryPath!)!, "conflicts");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, document.DocumentId.ToString("D") + "-" + Guid.NewGuid().ToString("N") + ".9to1board");
        await CopyOwnedSidecarsAsync(FilePath ?? _recoveryPath!, path, CancellationToken.None).ConfigureAwait(false);
        await store.SaveDocumentAtPathAsync(document, path, CancellationToken.None).ConfigureAwait(false);
        _conflictContentHash = contentHash;
        LastConflictRecoveryPath = path;
    }

    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static async Task CopyOwnedSidecarsAsync(string source, string destination, CancellationToken token)
    {
        var sourceDirectory = SidecarDirectoryFor(source);
        var destinationDirectory = SidecarDirectoryFor(destination);
        if (!Directory.Exists(sourceDirectory) || SamePath(sourceDirectory, destinationDirectory)) return;
        Directory.CreateDirectory(destinationDirectory);
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*.blob"))
        {
            token.ThrowIfCancellationRequested();
            var target = Path.Combine(destinationDirectory, Path.GetFileName(sourceFile));
            if (File.Exists(target))
            {
                if (await HashFileAsync(sourceFile, token).ConfigureAwait(false) != await HashFileAsync(target, token).ConfigureAwait(false))
                    throw new IOException("A media file at the selected destination conflicts with this board.");
                continue;
            }
            var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var input = File.OpenRead(sourceFile))
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await input.CopyToAsync(output, token).ConfigureAwait(false);
                    await output.FlushAsync(token).ConfigureAwait(false);
                }
                File.Move(temporary, target);
            }
            finally { TryDelete(temporary); }
        }
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
            if (HasUnsavedChanges && _externalSave is null && FilePath is not null)
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
