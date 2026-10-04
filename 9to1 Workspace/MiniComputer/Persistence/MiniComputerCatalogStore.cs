using System.Collections.Immutable;
using System.Text.Json;

namespace HavenOS.Apps.MiniComputer;

public sealed record MiniComputerCatalogSnapshot(
    int SchemaVersion,
    ProviderId? DefaultProviderID,
    ImmutableArray<VirtualMachine> VirtualMachines,
    ImmutableArray<Snapshot> Snapshots,
    ImmutableArray<VirtualDisk> Disks,
    ImmutableArray<InstallationMedia> Media);

public interface IMiniComputerCatalogStore
{
    ValueTask<MiniComputerCatalogSnapshot> ReadAsync(CancellationToken cancellationToken);
    ValueTask WriteAsync(MiniComputerCatalogSnapshot snapshot, CancellationToken cancellationToken);
}

/// <summary>Versioned metadata store. Hypervisor disk blocks and secrets are never stored here.</summary>
public sealed class JsonMiniComputerCatalogStore : IMiniComputerCatalogStore
{
    public const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    public JsonMiniComputerCatalogStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async ValueTask<MiniComputerCatalogSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path)) return Empty();
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 16_384, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var snapshot = await JsonSerializer.DeserializeAsync<MiniComputerCatalogSnapshot>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("Mini Computer catalog is empty or invalid.");
            if (snapshot.SchemaVersion != CurrentSchemaVersion)
                throw new InvalidDataException($"Mini Computer catalog schema {snapshot.SchemaVersion} is not supported. Expected {CurrentSchemaVersion}; data was not reinterpreted.");
            return snapshot;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask WriteAsync(MiniComputerCatalogSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Only catalog schema {CurrentSchemaVersion} can be written.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? tempPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Catalog path must have a parent directory.");
            Directory.CreateDirectory(directory);
            tempPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16_384, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, _path, overwrite: true);
            tempPath = null;
        }
        finally
        {
            if (tempPath is not null && File.Exists(tempPath)) File.Delete(tempPath);
            _gate.Release();
        }
    }

    public static MiniComputerCatalogSnapshot Empty() => new(
        CurrentSchemaVersion, null, [], [], [], []);
}
