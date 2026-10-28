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
    private readonly Dictionary<Guid, Destination> _destinations;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> Gates = new();
    private readonly object _gate;
    public JsonFileHavenBoardStore Store { get; }
    public RecentBoardCatalog Recent { get; }

    public BoardsLocalDocuments(string profileId, string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
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
        return document.DocumentId;
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
