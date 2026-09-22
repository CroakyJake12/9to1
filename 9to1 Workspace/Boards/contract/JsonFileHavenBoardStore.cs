using System.Text.Json;

namespace CakeOS.Apps.Boards.Contract;

/// <summary>
/// The stable, user-owned envelope stored in every <c>.9to1board</c> file.
/// Runtime snapshot types deliberately remain below this compatibility boundary.
/// </summary>
public sealed record HavenBoardDocument(
    string Format,
    int SchemaVersion,
    Guid DocumentId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ModifiedUtc,
    HavenBoardSnapshot Snapshot)
{
    public const string FormatIdentity = "9to1.board";
    public const int CurrentSchemaVersion = 1;
}

public enum HavenBoardLoadDisposition
{
    Normal,
    RecoveredFromBackup,
    MigratedLegacyJson
}

/// <summary>Raised instead of opening an unknown future document as an empty board.</summary>
public sealed class UnsupportedHavenBoardDocumentVersionException(int actualVersion)
    : IOException($"This .9to1board uses unsupported schema version {actualVersion}. Update 9-1 to open it safely.")
{
    public int ActualVersion { get; } = actualVersion;
}

/// <summary>
/// Local-first physical document store. Every board is a versioned <c>.9to1board</c> file,
/// written through a same-directory validated temporary file and retained previous-version backup.
/// </summary>
public sealed class JsonFileHavenBoardStore : IHavenBoardStore, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _rootDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public JsonFileHavenBoardStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("A board storage directory is required.", nameof(rootDirectory));

        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    /// <summary>The outcome of the most recent successful load for status UI and diagnostics.</summary>
    public HavenBoardLoadDisposition LastLoadDisposition { get; private set; } = HavenBoardLoadDisposition.Normal;

    /// <summary>Returns the visible physical path for a board. IDs are path-safe, titles are document metadata.</summary>
    public string GetDocumentPath(string boardId)
    {
        ValidateBoardId(boardId);
        return PrimaryPath(boardId);
    }

    public async Task<HavenBoardSnapshot?> LoadAsync(
        string boardId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateBoardId(boardId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LastLoadDisposition = HavenBoardLoadDisposition.Normal;
            var primary = PrimaryPath(boardId);
            var primaryResult = await TryReadDocumentAsync(primary, boardId, cancellationToken).ConfigureAwait(false);
            if (primaryResult.Snapshot is not null)
                return primaryResult.Snapshot;
            if (primaryResult.UnsupportedVersion is not null)
                throw primaryResult.UnsupportedVersion;

            var backupResult = await TryReadDocumentAsync(BackupPath(boardId), boardId, cancellationToken).ConfigureAwait(false);
            if (backupResult.Snapshot is not null)
            {
                LastLoadDisposition = HavenBoardLoadDisposition.RecoveredFromBackup;
                return backupResult.Snapshot;
            }
            if (backupResult.UnsupportedVersion is not null)
                throw backupResult.UnsupportedVersion;

            // A file that exists but cannot be parsed or validated is never treated as a missing board.
            // Doing so would let a session create an empty replacement over real user data.
            if (primaryResult.InvalidDocument is not null)
                throw primaryResult.InvalidDocument;
            if (backupResult.InvalidDocument is not null)
                throw backupResult.InvalidDocument;

            // Older pre-RC prototype documents are imported once, while their original .json remains untouched.
            var legacyResult = await TryReadLegacySnapshotAsync(LegacyPath(boardId), boardId, cancellationToken).ConfigureAwait(false);
            if (legacyResult.InvalidDocument is not null)
                throw legacyResult.InvalidDocument;
            if (legacyResult.Snapshot is null)
                return null;

            await SaveCoreAsync(legacyResult.Snapshot, existing: null, primaryWasCorrupt: false, cancellationToken).ConfigureAwait(false);
            LastLoadDisposition = HavenBoardLoadDisposition.MigratedLegacyJson;
            return legacyResult.Snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        HavenBoardSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateBoardId(snapshot.Id);
        HavenBoardReducer.Validate(snapshot);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var primaryResult = await TryReadDocumentAsync(PrimaryPath(snapshot.Id), snapshot.Id, cancellationToken)
                .ConfigureAwait(false);
            if (primaryResult.UnsupportedVersion is not null)
                throw primaryResult.UnsupportedVersion;

            // Preserve stable document identity from the newest valid copy so a corrupt primary
            // can never cause a rename/re-identity on the next explicit save.
            HavenBoardDocument? existing = primaryResult.Document;
            if (existing is null)
            {
                var backupResult = await TryReadDocumentAsync(BackupPath(snapshot.Id), snapshot.Id, cancellationToken)
                    .ConfigureAwait(false);
                if (backupResult.UnsupportedVersion is not null)
                    throw backupResult.UnsupportedVersion;
                existing = backupResult.Document;
            }

            await SaveCoreAsync(snapshot, existing, primaryResult.InvalidDocument is not null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveCoreAsync(
        HavenBoardSnapshot snapshot,
        HavenBoardDocument? existing,
        bool primaryWasCorrupt,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_rootDirectory);
        var primary = PrimaryPath(snapshot.Id);
        var backup = BackupPath(snapshot.Id);
        var temp = primary + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var now = DateTimeOffset.UtcNow;
        var document = new HavenBoardDocument(
            HavenBoardDocument.FormatIdentity,
            HavenBoardDocument.CurrentSchemaVersion,
            existing?.DocumentId ?? Guid.NewGuid(),
            existing?.CreatedUtc ?? now,
            now,
            snapshot);

        try
        {
            await WriteDocumentAsync(temp, document, cancellationToken).ConfigureAwait(false);

            // Read the exact bytes written before the live document is ever touched.
            var validatedTemp = await TryReadDocumentAsync(temp, snapshot.Id, CancellationToken.None).ConfigureAwait(false);
            if (validatedTemp.Snapshot is null || validatedTemp.UnsupportedVersion is not null)
                throw new InvalidDataException("The temporary .9to1board could not be validated before replacement.");

            // From here cancellation must not leave replacement half-complete. When the primary
            // is known-good, File.Replace atomically promotes temp and preserves it as .bak.
            // When the primary is corrupt, the good .bak must not be clobbered: quarantine the
            // corrupt primary instead and keep the last known-good backup untouched.
            if (File.Exists(primary))
            {
                if (primaryWasCorrupt && File.Exists(backup))
                {
                    var quarantine = primary + ".corrupt-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ".bak";
                    File.Move(primary, quarantine);
                    File.Move(temp, primary);
                }
                else
                {
                    try
                    {
                        File.Replace(temp, primary, backup, ignoreMetadataErrors: true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        await CreateValidatedBackupAsync(primary, backup).ConfigureAwait(false);
                        File.Move(temp, primary, overwrite: true);
                    }
                    catch (IOException) when (!OperatingSystem.IsWindows())
                    {
                        await CreateValidatedBackupAsync(primary, backup).ConfigureAwait(false);
                        File.Move(temp, primary, overwrite: true);
                    }
                }
            }
            else
            {
                File.Move(temp, primary);
            }

            RestrictUnixPermissions(primary);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static async Task WriteDocumentAsync(string path, HavenBoardDocument document, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            options: FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, document, Json, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
        RestrictUnixPermissions(path);
    }

    private static async Task CreateValidatedBackupAsync(string primary, string backup)
    {
        var staging = backup + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var source = new FileStream(primary, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var target = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.WriteThrough))
            {
                await source.CopyToAsync(target).ConfigureAwait(false);
                await target.FlushAsync().ConfigureAwait(false);
                target.Flush(flushToDisk: true);
            }

            File.Move(staging, backup, overwrite: true);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    private async Task<DocumentReadResult> TryReadDocumentAsync(
        string path,
        string expectedBoardId,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return default;

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = json.RootElement;
            if (!root.TryGetProperty("format", out var format) ||
                !string.Equals(format.GetString(), HavenBoardDocument.FormatIdentity, StringComparison.Ordinal) ||
                !root.TryGetProperty("schemaVersion", out var versionElement) ||
                !versionElement.TryGetInt32(out var version))
                return new DocumentReadResult(null, null, null, new InvalidDataException(
                    "The .9to1board is missing its required format identity or schema version."));

            if (version > HavenBoardDocument.CurrentSchemaVersion)
                return new DocumentReadResult(null, null, new UnsupportedHavenBoardDocumentVersionException(version), null);
            if (version < 0)
                return new DocumentReadResult(null, null, null, new InvalidDataException("The .9to1board schema version is invalid."));

            var document = version switch
            {
                HavenBoardDocument.CurrentSchemaVersion => root.Deserialize<HavenBoardDocument>(Json),
                0 => MigrateSchemaZero(root),
                _ => null
            };

            if (document?.Snapshot is null ||
                !string.Equals(document.Snapshot.Id, expectedBoardId, StringComparison.Ordinal) ||
                document.DocumentId == Guid.Empty ||
                document.CreatedUtc == default ||
                document.ModifiedUtc == default)
                return new DocumentReadResult(null, null, null, new InvalidDataException(
                    "The .9to1board is incomplete, has a mismatched board identity, or fails validation."));

            HavenBoardReducer.Validate(document.Snapshot);
            return new DocumentReadResult(document.Snapshot, document, null, null);
        }
        catch (JsonException error)
        {
            return new DocumentReadResult(null, null, null, new InvalidDataException("The .9to1board contains invalid JSON.", error));
        }
        catch (InvalidOperationException error)
        {
            return new DocumentReadResult(null, null, null, error);
        }
        catch (NotSupportedException error)
        {
            return new DocumentReadResult(null, null, null, new InvalidDataException("The .9to1board contains unsupported data.", error));
        }
    }

    private static HavenBoardDocument? MigrateSchemaZero(JsonElement root)
    {
        if (!root.TryGetProperty("snapshot", out var snapshotElement))
            return null;

        var snapshot = snapshotElement.Deserialize<HavenBoardSnapshot>(Json);
        if (snapshot is null)
            return null;

        var now = DateTimeOffset.UtcNow;
        return new HavenBoardDocument(
            HavenBoardDocument.FormatIdentity,
            HavenBoardDocument.CurrentSchemaVersion,
            Guid.NewGuid(),
            now,
            now,
            snapshot);
    }

    private static async Task<LegacyReadResult> TryReadLegacySnapshotAsync(
        string path,
        string expectedBoardId,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return default;

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var snapshot = await JsonSerializer.DeserializeAsync<HavenBoardSnapshot>(stream, Json, cancellationToken)
                .ConfigureAwait(false);
            if (snapshot is null || !string.Equals(snapshot.Id, expectedBoardId, StringComparison.Ordinal))
                return new LegacyReadResult(null, new InvalidDataException("The legacy board has no valid matching identity."));
            HavenBoardReducer.Validate(snapshot);
            return new LegacyReadResult(snapshot, null);
        }
        catch (JsonException error)
        {
            return new LegacyReadResult(null, new InvalidDataException("The legacy board contains invalid JSON.", error));
        }
        catch (InvalidOperationException error)
        {
            return new LegacyReadResult(null, error);
        }
        catch (NotSupportedException error)
        {
            return new LegacyReadResult(null, new InvalidDataException("The legacy board contains unsupported data.", error));
        }
    }

    private string PrimaryPath(string boardId) => Path.Combine(_rootDirectory, boardId + ".9to1board");
    private string BackupPath(string boardId) => PrimaryPath(boardId) + ".bak";
    private string LegacyPath(string boardId) => Path.Combine(_rootDirectory, boardId + ".json");

    private static void ValidateBoardId(string boardId)
    {
        if (string.IsNullOrWhiteSpace(boardId) || boardId.Length > 128)
            throw new ArgumentException("Board ID must contain 1 to 128 safe characters.", nameof(boardId));

        if (boardId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException("Board ID may contain only ASCII letters, digits, '-' and '_'.", nameof(boardId));
    }

    private static void RestrictUnixPermissions(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }

    private readonly record struct DocumentReadResult(
        HavenBoardSnapshot? Snapshot,
        HavenBoardDocument? Document,
        UnsupportedHavenBoardDocumentVersionException? UnsupportedVersion,
        Exception? InvalidDocument);

    private readonly record struct LegacyReadResult(HavenBoardSnapshot? Snapshot, Exception? InvalidDocument);
}
