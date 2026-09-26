using System.Text.Json;

namespace HavenOS.Apps.MiniComputer;

public interface IProviderIdentityMapStore
{
    ValueTask<VirtualMachineId> GetOrCreateAsync(ProviderId providerID, string providerMachineID, CancellationToken cancellationToken);
    ValueTask RemoveAsync(ProviderId providerID, string providerMachineID, CancellationToken cancellationToken);
}

public sealed class JsonProviderIdentityMapStore : IProviderIdentityMapStore
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonProviderIdentityMapStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async ValueTask<VirtualMachineId> GetOrCreateAsync(ProviderId providerID, string providerMachineID, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMachineID);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
            var key = Key(providerID, providerMachineID);
            if (state.Mappings.TryGetValue(key, out var id)) return new VirtualMachineId(id);
            id = Guid.NewGuid();
            state.Mappings.Add(key, id);
            await WriteStateAsync(state, cancellationToken).ConfigureAwait(false);
            return new VirtualMachineId(id);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask RemoveAsync(ProviderId providerID, string providerMachineID, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
            if (!state.Mappings.Remove(Key(providerID, providerMachineID))) return;
            await WriteStateAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async ValueTask<IdentityMapState> ReadStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new(SchemaVersion, new(StringComparer.Ordinal));
        await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var state = await JsonSerializer.DeserializeAsync<IdentityMapState>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Provider identity map is invalid.");
        if (state.SchemaVersion != SchemaVersion) throw new InvalidDataException($"Provider identity map schema {state.SchemaVersion} is unsupported; expected {SchemaVersion}.");
        return state;
    }

    private async ValueTask WriteStateAsync(IdentityMapState state, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Identity map path needs a directory.");
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, _path, overwrite: true);
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }

    private static string Key(ProviderId providerID, string providerMachineID) => $"{providerID}:{providerMachineID.Trim().ToLowerInvariant()}";
    private sealed record IdentityMapState(int SchemaVersion, Dictionary<string, Guid> Mappings);
}
