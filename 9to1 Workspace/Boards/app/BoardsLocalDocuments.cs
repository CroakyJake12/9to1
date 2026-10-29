using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.App;

/// <summary>Private profile storage of canonical boards. Paths are locators, never BoardIDs.</summary>
public sealed class BoardsLocalDocuments : IDisposable
{
    private sealed record Destination(string Path, string Hash, string? RecoveryHash = null);
    private readonly string _directory, _destinationFile;
    private readonly Func<CancellationToken, Task<string?>>? _documentsProvider;
    private string? _documentsDirectory;
    private Task<string?>? _documentsResolving;
    private readonly Dictionary<Guid, Destination> _destinations;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> Gates = new();
    private readonly object _gate;
    public JsonFileHavenBoardStore Store { get; }
    public RecentBoardCatalog Recent { get; }

    /// <summary>The folder this profile's board drafts really live in. Exposed so the surface can
    /// name the location instead of only reporting "Saved locally".</summary>
    public string DraftDirectory => _directory;

    /// <summary>The Files-visible folder new boards are saved into, once the host has answered. Surfaced
    /// so Boards can tell the owner where a board is rather than that it is "saved locally". Null until
    /// then, which is why a surface resolves before writing its notice.</summary>
    public string? DocumentsDirectory => _documentsDirectory;

    /// <summary>Asks the host where boards belong, at most once per catalogue. A provider that fails is
    /// remembered as no folder plus its reason, so the surface can say the board stayed a draft rather
    /// than imply a save that the disk never performed.</summary>
    public async Task<string?> ResolveWorkspaceDirectoryAsync(CancellationToken token = default)
    {
        if (_documentsProvider is null) return null;
        Task<string?> pending;
        lock (_gate)
        {
            _documentsResolving ??= ResolveWorkspaceDirectoryCoreAsync(token);
            pending = _documentsResolving;
        }
        return await pending.ConfigureAwait(false);
    }

    private async Task<string?> ResolveWorkspaceDirectoryCoreAsync(CancellationToken token)
    {
        try
        {
            var supplied = await _documentsProvider!(token).ConfigureAwait(false);
            _documentsDirectory = string.IsNullOrWhiteSpace(supplied)
                ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(supplied!));
            WorkspaceResolutionFailure = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or InvalidDataException or ArgumentException)
        {
            _documentsDirectory = null;
            WorkspaceResolutionFailure = error.Message.Split('\n', 2)[0];
        }
        return _documentsDirectory;
    }

    /// <summary>Why the host could not name a workspace folder, when that happened.</summary>
    public string? WorkspaceResolutionFailure { get; private set; }

    /// <summary>Where this board's live file actually is, once it has one. Null while a board exists only
    /// as a recovery draft.</summary>
    public string? DestinationFor(Guid id)
    {
        if (id == Guid.Empty) return null;
        lock (_gate)
        {
            _destinations.Clear();
            if (File.Exists(_destinationFile))
                foreach (var pair in JsonSerializer.Deserialize<Dictionary<Guid, Destination>>(File.ReadAllText(_destinationFile))
                    ?? throw new InvalidDataException("Board destination metadata is empty.")) _destinations.Add(pair.Key, pair.Value);
            return _destinations.TryGetValue(id, out var known) ? known.Path : null;
        }
    }

    /// <summary>
    /// <paramref name="documentsDirectory"/> is the Files-visible folder boards are saved into - the
    /// board app folder inside the device 9to1 workspace the owner chose on 29 October ("everything is
    /// saved to that unless the user picks another location"). The private draft directory stays exactly
    /// as it is: it is the recovery copy, so a board is never only a draft, and a failed placement can
    /// never lose work. Passing null keeps the historic drafts-only behaviour, which is what the tests
    /// and any host without a Files workspace get.</summary>
    /// <summary><paramref name="documentsDirectoryProvider"/> resolves the Files-visible folder boards
    /// are saved into - the board app folder inside the device 9to1 workspace the owner chose on
    /// 29 October ("everything is saved to that unless the user picks another location"). It is asked
    /// lazily and asynchronously, because establishing that folder may have to create the workspace and
    /// Boards surfaces are built on the UI thread. The private draft directory is unchanged: it remains
    /// the recovery copy, so a board is never only a draft and a failed placement never loses work.
    /// Passing null keeps the historic drafts-only behaviour.</summary>
    public BoardsLocalDocuments(string profileId, string dataRoot,
        Func<CancellationToken, Task<string?>>? documentsDirectoryProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _documentsProvider = documentsDirectoryProvider;
        var profileKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profileId))).ToLowerInvariant();
        _directory = Path.Combine(Path.GetFullPath(dataRoot), "profiles", profileKey, "drafts");
        Directory.CreateDirectory(_directory);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Store = new JsonFileHavenBoardStore(_directory);
        Recent = new RecentBoardCatalog(Path.Combine(_directory, "recent.json"));
        _destinationFile = Path.Combine(_directory, "destinations.json");
        _gate = Gates.GetOrAdd(_destinationFile, _ => new object());
        _destinations = File.Exists(_destinationFile)
            ? JsonSerializer.Deserialize<Dictionary<Guid, Destination>>(File.ReadAllText(_destinationFile))
                ?? throw new InvalidDataException("Board destination metadata is empty.")
            : [];
        // Recovery does not depend on an index write having completed before interruption.
        foreach (var path in Directory.EnumerateFiles(_directory, "*.9to1board").OrderBy(File.GetLastWriteTimeUtc))
            if (!Recent.References.Any(reference => reference.Path == path)) Recent.Record(path);
    }

    public string PathFor(Guid id) => id == Guid.Empty ? throw new ArgumentException("A BoardID is required.")
        : Path.Combine(_directory, id.ToString("D") + ".9to1board");

    public bool OwnsPath(string path) => string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), _directory,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public Guid IdFor(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(fullPath), "D", out var id) ||
            !string.Equals(fullPath, PathFor(id), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new UnauthorizedAccessException("This board is not in the active profile's local catalogue.");
        return id;
    }

    public async Task<Guid> CreateAsync(ReadOnlyMemory<byte> bytes = default, CancellationToken token = default)
    {
        var document = HavenBoardDocumentCodec.Deserialize(bytes.IsEmpty ? BoardsViewModel.CreateNewBoardDraftBytes() : bytes);
        var path = PathFor(document.DocumentId);
        using var lease = await LocalBoardWriteLease.AcquireAsync(path, token).ConfigureAwait(false);
        if (File.Exists(path) || File.Exists(path + ".bak")) throw new IOException("This draft already exists.");
        await Store.SaveDocumentAtPathAsync(document, path, token).ConfigureAwait(false);
        Recent.Record(path);
        await PlaceInWorkspaceAsync(document, path, token).ConfigureAwait(false);
        return document.DocumentId;
    }

    /// <summary>Writes the new board as a normal, visible <c>.9to1board</c> file inside the workspace
    /// folder and records it as the board's live destination, so the owner finds it in Files and every
    /// later save goes to that file while the draft stays as recovery. Naming is collision-safe: an
    /// existing file is never overwritten, a second "Roadmap" becomes "Roadmap (2)".</summary>
    private async Task PlaceInWorkspaceAsync(HavenBoardDocument document, string draftPath, CancellationToken token)
    {
        var workspace = await ResolveWorkspaceDirectoryAsync(token).ConfigureAwait(false);
        if (workspace is null) return;
        try
        {
            Directory.CreateDirectory(workspace);
            var bytes = await File.ReadAllBytesAsync(draftPath, token).ConfigureAwait(false);
            var target = UniqueDestination(workspace, BoardFileName(document));
            var temporary = target + ".tmp";
            await File.WriteAllBytesAsync(temporary, bytes, token).ConfigureAwait(false);
            File.Move(temporary, target, false);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            RecordDestination(document.DocumentId, target,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            PlacementFailure = null;
            PlacedPath = target;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or InvalidDataException or ArgumentException)
        {
            // The board still exists as a recoverable draft, so creation is never blocked by a placement
            // problem - but the surface must say so and name where the draft actually is.
            PlacementFailure = error.Message.Split('\n', 2)[0];
            PlacedPath = null;
        }
    }

    /// <summary>Set when the last created board could not be placed in the workspace folder; the surface
    /// shows this instead of claiming a save the disk did not perform.</summary>
    public string? PlacementFailure { get; private set; }
    /// <summary>The visible file the last created board was written to, when placement succeeded.</summary>
    public string? PlacedPath { get; private set; }

    /// <summary>The board's own title, falling back to the untitled board wording. Control characters and
    /// path separators are removed, and a trailing dot or space is not kept because Windows treats those
    /// as unusable name parts.</summary>
    private static string BoardFileName(HavenBoardDocument document)
    {
        var title = (document.RichNotes?.Title ?? string.Empty).Trim();
        var safe = new string(title.Where(character => !char.IsControl(character) &&
            !Path.GetInvalidFileNameChars().Contains(character)).ToArray()).TrimEnd(' ', '.');
        if (safe.Length == 0) safe = "Board";
        return safe.Length > 80 ? safe[..80].TrimEnd(' ', '.') : safe;
    }

    /// <summary>"Roadmap.9to1board", then "Roadmap (2).9to1board" - never an overwrite of something that
    /// already exists, whether the owner made it or a previous board did.</summary>
    private static string UniqueDestination(string directory, string baseName)
    {
        var candidate = Path.Combine(directory, baseName + ".9to1board");
        if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        for (var ordinal = 2; ordinal < 1000; ordinal++)
        {
            candidate = Path.Combine(directory, $"{baseName} ({ordinal}).9to1board");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        return Path.Combine(directory, baseName + "-" + Guid.NewGuid().ToString("N") + ".9to1board");
    }

    public async Task<Guid> ImportAsync(string path, CancellationToken token = default)
    {
        var requestedPath = Path.GetFullPath(path);
        var sourcePath = File.Exists(requestedPath) ? requestedPath : requestedPath + ".bak";
        byte[] bytes;
        // Parse and hash the same bounded read. A later external replacement can
        // only cause a subsequent conflict, never authorize old data with its new hash.
        await using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (input.Length > HavenBoardDocumentCodec.MaximumDocumentBytes)
                throw new InvalidDataException("The selected board exceeds the supported size.");
            bytes = new byte[checked((int)input.Length)];
            await input.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        }
        var document = HavenBoardDocumentCodec.Deserialize(bytes);
        var observedHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var privatePath = PathFor(document.DocumentId);
        using var lease = await LocalBoardWriteLease.AcquireAsync(privatePath, token).ConfigureAwait(false);
        if (File.Exists(privatePath) || File.Exists(privatePath + ".bak"))
        {
            lock (_gate)
            {
                _destinations.Clear();
                if (File.Exists(_destinationFile))
                {
                    var latest = JsonSerializer.Deserialize<Dictionary<Guid, Destination>>(File.ReadAllText(_destinationFile))
                        ?? throw new InvalidDataException("Board destination metadata is empty.");
                    foreach (var pair in latest) _destinations.Add(pair.Key, pair.Value);
                }
                if (_destinations.TryGetValue(document.DocumentId, out var known) &&
                    string.Equals(known.Path, requestedPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    if (observedHash != known.Hash)
                        throw new IOException("This file changed outside Boards. Open the retained local board to preserve both revisions.");
                    return document.DocumentId;
                }
            }
            throw new IOException("A local board with this identity already exists. Open that draft to resolve both revisions.");
        }
        await RichBoardSession.CopyOwnedSidecarsAsync(requestedPath, privatePath, token).ConfigureAwait(false);
        await Store.SaveDocumentAtPathAsync(document, privatePath, token).ConfigureAwait(false);
        if (sourcePath == requestedPath)
            RecordDestination(document.DocumentId, requestedPath, observedHash);
        Recent.Record(privatePath);
        return document.DocumentId;
    }

    public async Task<ContractSessionAdapter> OpenAsync(Guid id, CancellationToken token = default)
    {
        var path = PathFor(id);
        using var lease = await LocalBoardWriteLease.AcquireAsync(path, token).ConfigureAwait(false);
        var document = await Store.LoadDocumentAtPathAsync(path, token).ConfigureAwait(false)
            ?? throw new FileNotFoundException("This local draft is unavailable.", path);
        if (document.DocumentId != id) throw new InvalidDataException("The draft identity does not match its catalogue entry.");
        if (!File.Exists(path))
            await Store.SaveDocumentAtPathAsync(document, path, token).ConfigureAwait(false);
        Destination? destination;
        lock (_gate)
        {
            // A catalogue instance may predate another tab's Save As. Resolve its
            // current destination while the same board-opening lease is held.
            _destinations.Clear();
            if (File.Exists(_destinationFile))
            {
                var latest = JsonSerializer.Deserialize<Dictionary<Guid, Destination>>(File.ReadAllText(_destinationFile))
                    ?? throw new InvalidDataException("Board destination metadata is empty.");
                foreach (var pair in latest) _destinations.Add(pair.Key, pair.Value);
            }
            _destinations.TryGetValue(id, out destination);
        }
        var privateHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, token).ConfigureAwait(false))).ToLowerInvariant();
        var destinationMatches = destination is null || destination.RecoveryHash == privateHash;
        var adapter = await ContractSessionAdapter.OpenLocalDraftAsync(Store, path,
            destinationMatches ? destination?.Path : null, destinationMatches ? destination?.Hash : null,
            (savedPath, hash) => RecordDestination(id, savedPath, hash), token, heldLease: lease).ConfigureAwait(false);
        if (!destinationMatches)
            adapter.SetLocalRecoveryNotice("Recovered locally — destination metadata differs; use Save As to choose where to continue.");
        Recent.Record(path);
        return adapter;
    }

    private void RecordDestination(Guid id, string path, string hash)
    {
        lock (_gate)
        {
            if (File.Exists(_destinationFile))
            {
                var latest = JsonSerializer.Deserialize<Dictionary<Guid, Destination>>(File.ReadAllText(_destinationFile))
                    ?? throw new InvalidDataException("Board destination metadata is empty.");
                _destinations.Clear();
                foreach (var pair in latest) _destinations.Add(pair.Key, pair.Value);
            }
            if (string.Equals(Path.GetFullPath(path), PathFor(id), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                _destinations.Remove(id);
            else _destinations[id] = new Destination(path, hash,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(PathFor(id)))).ToLowerInvariant());
            var temporary = _destinationFile + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_destinations));
            File.Move(temporary, _destinationFile, true);
        }
    }

    public string CatalogPathFor(string path)
    {
        if (OwnsPath(path)) return Path.GetFullPath(path);
        lock (_gate)
        {
            var match = _destinations.FirstOrDefault(pair => string.Equals(pair.Value.Path, Path.GetFullPath(path),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            return match.Key == Guid.Empty ? path : PathFor(match.Key);
        }
    }

    public async Task<IReadOnlyList<string>> AdoptLegacyRecentsAsync(string catalogPath, string defaultBoardPath,
        CancellationToken token = default)
    {
        var legacy = new RecentBoardCatalog(catalogPath);
        var failures = new List<string>();
        var paths = legacy.References.Select(item => item.Path).Append(defaultBoardPath).Distinct();
        foreach (var path in paths)
        {
            if (!File.Exists(path) && !File.Exists(path + ".bak")) continue;
            try { await ImportAsync(path, token).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            { failures.Add(Path.GetFileName(path) + ": " + error.Message.Split('\n')[0]); }
        }
        return failures;
    }

    public async Task<Guid> LatestOrCreateAsync(CancellationToken token = default)
    {
        foreach (var recent in Recent.References)
        {
            try
            {
                var id = IdFor(recent.Path);
                if (File.Exists(PathFor(id)) || File.Exists(PathFor(id) + ".bak")) return id;
            }
            catch (ArgumentException) { }
            catch (UnauthorizedAccessException) { }
        }
        return await CreateAsync(token: token).ConfigureAwait(false);
    }

    public void Dispose() => Store.Dispose();
}
