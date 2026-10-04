/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/VersionedAtomicSettingsStore.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns IVersionedSettingsStore, SettingsExportManifest, VersionedAtomicSettingsStore. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;

namespace Haven.Application;

/// <summary>
/// Defines the versioned settings store contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IVersionedSettingsStore
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class;
    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class;
    Task RemoveAsync(string key, CancellationToken cancellationToken);
    Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken);
    Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken);
}

/// <summary>
/// Represents settings export manifest and keeps its related state and behavior together.
/// </summary>
public sealed class SettingsExportManifest
{
    public SettingsStoreIdentity? StoreIdentity { get; init; }
    /// <summary>Format version; legacy manifests without this field use the compatible v1 shape.</summary>
    public int SchemaVersion { get; init; } = 1;
    /// <summary>
    /// Gets or updates version, the bindable or domain state represented by this property.
    /// </summary>
    public int Version { get; init; } = 1;
    /// <summary>
    /// Gets or updates exported at, the bindable or domain state represented by this property.
    /// </summary>
    public string ExportedAt { get; init; } = DateTimeOffset.UtcNow.ToString("O");
    /// <summary>
    /// Gets or updates settings, the bindable or domain state represented by this property.
    /// </summary>
    public Dictionary<string, string> Settings { get; init; } = new();
}

/// <summary>
/// Represents versioned atomic settings store and keeps its related state and behavior together.
/// </summary>
public sealed record SettingsStoreIdentity(int SchemaVersion, Guid StoreId, DateTimeOffset CreatedAtUtc);

public sealed class VersionedAtomicSettingsStore : IVersionedSettingsStore, IResourceStoreIdentitySource, IVersionedSettingsGuardedCompareExchange
{
    private readonly IAppPaths _paths;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, string> _settings = new(StringComparer.OrdinalIgnoreCase);
    private int _version;
    private bool _loaded;
    private bool _recoveredFromBackup;
    private SettingsStoreIdentity? _identity;
    private bool _identityNeedsPersistence;
    private bool _newlyCreated;

    public VersionedAtomicSettingsStore(IAppPaths paths) => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public async ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var diskLease = await AcquireStoreLeaseAsync(cancellationToken).ConfigureAwait(false);
            await RefreshLoadedAsync(cancellationToken).ConfigureAwait(false);
            if (_identityNeedsPersistence) await CommitAsync(new(_settings, StringComparer.OrdinalIgnoreCase), cancellationToken).ConfigureAwait(false);
            return new(_identity!.SchemaVersion, _identity.StoreId, _identity.CreatedAtUtc, _newlyCreated);
        }
        finally { _lock.Release(); }
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var diskLease = await AcquireStoreLeaseAsync(cancellationToken).ConfigureAwait(false);
            await RefreshLoadedAsync(cancellationToken).ConfigureAwait(false);
            if (!_settings.TryGetValue(key, out var json)) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(json)
                    ?? throw new InvalidDataException($"Setting '{key}' contains null instead of an object; saved state was preserved.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException($"Setting '{key}' is incompatible or corrupt; saved state was preserved.", exception);
            }
        }
        finally { _lock.Release(); }
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        var json = JsonSerializer.Serialize(value);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var diskLease = await AcquireStoreLeaseAsync(cancellationToken).ConfigureAwait(false);
            await RefreshLoadedAsync(cancellationToken).ConfigureAwait(false);
            var next = new Dictionary<string, string>(_settings, StringComparer.OrdinalIgnoreCase) { [key] = json };
            await CommitAsync(next, cancellationToken).ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var diskLease = await AcquireStoreLeaseAsync(cancellationToken).ConfigureAwait(false);
            await RefreshLoadedAsync(cancellationToken).ConfigureAwait(false);
            var next = new Dictionary<string, string>(_settings, StringComparer.OrdinalIgnoreCase);
            if (next.Remove(key)) await CommitAsync(next, cancellationToken).ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    public async Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var diskLease = await AcquireStoreLeaseAsync(cancellationToken).ConfigureAwait(false);
            await RefreshLoadedAsync(cancellationToken).ConfigureAwait(false);
            return new SettingsExportManifest { Version = _version, Settings = new(_settings), StoreIdentity = _identityNeedsPersistence ? null : _identity };
        }
        finally { _lock.Release(); }
    }

    public async Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ValidateManifest(manifest);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var diskLease = await AcquireStoreLeaseAsync(cancellationToken).ConfigureAwait(false);
            await RefreshLoadedAsync(cancellationToken).ConfigureAwait(false);
            var next = new Dictionary<string, string>(_settings, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in manifest.Settings) next[key] = value;
            await CommitAsync(next, cancellationToken).ConfigureAwait(false);
            return new SettingsImportResult(true, new Dictionary<string, string>(_settings), $"Imported {manifest.Settings.Count} settings");
        }
        finally { _lock.Release(); }
    }

    public async Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expectedJson,
        string? replacementJson, CancellationToken cancellationToken)
    {
        var result = await CompareExchangeGuardedAsync(key, expectedJson, replacementJson,
            new Dictionary<string, string?>(), cancellationToken).ConfigureAwait(false);
        return new(result.Exchanged, result.CurrentJson, result.StoreVersion);
    }

    public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expectedJson,
        string? replacementJson, IReadOnlyDictionary<string, string?> expectedGuards, CancellationToken cancellationToken) =>
        CompareExchangeGuardedCoreAsync(key, expectedJson, replacementJson, expectedGuards, null, cancellationToken);

    public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expectedJson,
        string? replacementJson, IReadOnlyDictionary<string, string?> expectedGuards, ISettingsCommitAdmission admission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(admission);
        return CompareExchangeGuardedCoreAsync(key, expectedJson, replacementJson, expectedGuards, admission, cancellationToken);
    }

    private async Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedCoreAsync(string key, string? expectedJson,
        string? replacementJson, IReadOnlyDictionary<string, string?> expectedGuards, ISettingsCommitAdmission? admission, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(expectedGuards);
        if (expectedGuards.Count > 128) throw new ArgumentException("At most 128 exact settings guards are supported.", nameof(expectedGuards));
        var guards = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var guard in expectedGuards)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(guard.Key);
            if (!guards.TryAdd(guard.Key, guard.Value))
                throw new ArgumentException("Settings guard keys must be unique ignoring case.", nameof(expectedGuards));
        }
        if (replacementJson is not null)
        {
            try { using var value = JsonDocument.Parse(replacementJson); }
            catch (JsonException error) { throw new ArgumentException("Replacement settings must be valid serialized JSON.", nameof(replacementJson), error); }
        }
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var diskLease = await AcquireStoreLeaseAsync(cancellationToken).ConfigureAwait(false);
            await RefreshLoadedAsync(cancellationToken).ConfigureAwait(false);
            _settings.TryGetValue(key, out var current);
            foreach (var guard in guards)
            {
                _settings.TryGetValue(guard.Key, out var guardedCurrent);
                if (!StringComparer.Ordinal.Equals(guardedCurrent, guard.Value))
                    return new(false, current, _version, guard.Key);
            }
            if (!StringComparer.Ordinal.Equals(current, expectedJson)) return new(false, current, _version);
            if (admission is not null && !await admission.CheckAsync(new(_identity!, _version, SettingsCommitPhase.Admission), cancellationToken).ConfigureAwait(false))
                return new(false, current, _version) { AdmissionRejected = true };
            cancellationToken.ThrowIfCancellationRequested();
            if (StringComparer.Ordinal.Equals(current, replacementJson)) return new(true, current, _version);
            var next = new Dictionary<string, string>(_settings, StringComparer.OrdinalIgnoreCase);
            if (replacementJson is null) next.Remove(key); else next[key] = replacementJson;
            if (!await CommitAsync(next, cancellationToken, admission).ConfigureAwait(false))
                return new(false, current, _version) { AdmissionRejected = true };
            return new(true, replacementJson, _version);
        }
        finally { _lock.Release(); }
    }

    private async Task<FileStream> AcquireStoreLeaseAsync(CancellationToken token)
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        var path = GetSettingsPath() + ".lock";
        var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path, options); }
            catch (IOException) when (attempt < 200 && File.Exists(path))
            { await Task.Delay(25, token).ConfigureAwait(false); }
        }
    }

    private async Task RefreshLoadedAsync(CancellationToken token)
    {
        var previous = _identityNeedsPersistence ? null : _identity;
        var wasNew = _newlyCreated;
        _loaded = false;
        _settings = new(StringComparer.OrdinalIgnoreCase);
        _version = 0;
        _recoveredFromBackup = false;
        await EnsureLoadedAsync(token).ConfigureAwait(false);
        if (previous is not null)
        {
            if (_identity!.StoreId != previous.StoreId)
                throw new InvalidOperationException("The durable settings root changed; preserve it and review ownership before reopening.");
            _newlyCreated = wasNew;
        }
    }

    // Caller holds the shared gate. Empty-but-loaded is different from not loaded.
    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded) return;
        var path = GetSettingsPath();
        if (Directory.Exists(path)) throw new IOException("The settings file target is a directory; explicit recovery is required.");
        if (!File.Exists(path))
        {
            _identity = new(1, Guid.NewGuid(), DateTimeOffset.UtcNow);
            _identityNeedsPersistence = true;
            _newlyCreated = true;
            _loaded = true;
            return;
        }
        SettingsExportManifest loaded;
        try { loaded = await ReadManifestAsync(path, cancellationToken).ConfigureAwait(false); }
        catch (InvalidDataException primaryFailure)
        {
            var backupPath = path + ".bak";
            if (!File.Exists(backupPath)) throw;
            try { loaded = await ReadManifestAsync(backupPath, cancellationToken).ConfigureAwait(false); }
            catch (InvalidDataException backupFailure)
            { throw new InvalidDataException("Settings and their backup are unreadable; both files were preserved.", new AggregateException(primaryFailure, backupFailure)); }
            _recoveredFromBackup = true;
        }
        _settings = new(loaded.Settings, StringComparer.OrdinalIgnoreCase);
        _version = loaded.Version;
        _identity = loaded.StoreIdentity ?? new(1, Guid.NewGuid(), DateTimeOffset.UtcNow);
        _identityNeedsPersistence = loaded.StoreIdentity is null;
        _newlyCreated = false;
        _loaded = true;
    }

    private static async Task<SettingsExportManifest> ReadManifestAsync(string path, CancellationToken token)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
            var loaded = JsonSerializer.Deserialize<SettingsExportManifest>(json)
                ?? throw new InvalidDataException("Settings document is null.");
            ValidateManifest(loaded);
            return loaded;
        }
        catch (JsonException exception) { throw new InvalidDataException("Settings document contains invalid JSON.", exception); }
    }

    private static void ValidateManifest(SettingsExportManifest manifest)
    {
        if (manifest.SchemaVersion != 1)
            throw new NotSupportedException($"Unsupported settings schema {manifest.SchemaVersion}; saved state was preserved.");
        if (manifest.Version < 0 || manifest.Settings is null)
            throw new InvalidDataException("Settings version or entries are invalid.");
        if (manifest.StoreIdentity is { } identity)
        {
            if (identity.SchemaVersion != 1) throw new NotSupportedException("Unsupported settings store identity schema; saved state was preserved.");
            if (identity.StoreId == Guid.Empty || identity.CreatedAtUtc == default)
                throw new InvalidDataException("Corrupt settings store identity; saved state was preserved.");
        }
        foreach (var (key, value) in manifest.Settings)
        {
            if (string.IsNullOrWhiteSpace(key) || value is null)
                throw new InvalidDataException("Settings entries require a key and encoded value.");
            try { using var parsed = JsonDocument.Parse(value); }
            catch (JsonException exception) { throw new InvalidDataException($"Setting '{key}' contains invalid JSON.", exception); }
        }
    }

    private async Task<bool> CommitAsync(Dictionary<string, string> next, CancellationToken token, ISettingsCommitAdmission? admission = null)
    {
        var nextVersion = checked(_version + 1);
        var path = GetSettingsPath();
        Directory.CreateDirectory(_paths.DataDirectory);
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var manifest = new SettingsExportManifest { Version = nextVersion, Settings = next, StoreIdentity = _identity };
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 4096, Options = FileOptions.WriteThrough };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(tempPath, options))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, cancellationToken: token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                if (!_recoveredFromBackup)
                {
                    var current = await ReadManifestAsync(path, token).ConfigureAwait(false);
                    if (current.StoreIdentity is { } currentIdentity && currentIdentity.StoreId != _identity!.StoreId)
                        throw new InvalidOperationException("The settings store identity changed; reopen and review the current profile before writing.");
                }
                if (_recoveredFromBackup) File.Copy(path, path + ".corrupt." + Guid.NewGuid().ToString("N"));
                else File.Copy(path, path + ".bak", overwrite: true);
            }
            if (admission is not null && !await admission.CheckAsync(new(_identity!, _version, SettingsCommitPhase.Publication), token).ConfigureAwait(false))
                return false;
            token.ThrowIfCancellationRequested();
            File.Move(tempPath, path, overwrite: !(_newlyCreated && _version == 0));
            // Publish only after the atomic replacement has succeeded.
            _settings = next;
            _version = nextVersion;
            _recoveredFromBackup = false;
            _identityNeedsPersistence = false;
            return true;
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private string GetSettingsPath() => Path.Combine(_paths.DataDirectory, "settings.json");
}
