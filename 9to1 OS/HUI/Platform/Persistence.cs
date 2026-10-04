using System.Text.Json;

namespace CakeOS.Platform;

/// <summary>Allows non-Linux adapters to provide the same one-root storage layout.</summary>
public interface IPlatformStorageLayout
{
    string DataRoot { get; }
    string SettingsFile { get; }
    string ConfigDirectory { get; }
    string CacheDirectory { get; }
    string StateDirectory { get; }
    string ModelsDirectory { get; }
}

/// <summary>
/// Owns the shared Haven data root. Model files remain under <c>models</c>; platform settings live under
/// <c>platform</c>, avoiding a second application-data root.
/// </summary>
public sealed class XdgPlatformStorageLayout : IPlatformStorageLayout
{
    public XdgPlatformStorageLayout(string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRoot) || !Path.IsPathFullyQualified(dataRoot))
            throw new ArgumentException("Platform data root must be an absolute path.", nameof(dataRoot));
        DataRoot = Path.GetFullPath(dataRoot);
        SettingsFile = Path.Combine(DataRoot, "platform", "settings.v1.json");
        ConfigDirectory = Path.Combine(DataRoot, "config");
        CacheDirectory = Path.Combine(DataRoot, "cache");
        StateDirectory = Path.Combine(DataRoot, "state");
        ModelsDirectory = Path.Combine(DataRoot, "models");
    }

    public string DataRoot { get; }
    public string SettingsFile { get; }
    public string ConfigDirectory { get; }
    public string CacheDirectory { get; }
    public string StateDirectory { get; }
    public string ModelsDirectory { get; }

    public static XdgPlatformStorageLayout FromEnvironment(Func<string, string?>? readEnvironment = null)
    {
        readEnvironment ??= Environment.GetEnvironmentVariable;
        var xdgDataHome = readEnvironment("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
            return new XdgPlatformStorageLayout(Path.Combine(xdgDataHome, "haven"));

        var home = readEnvironment("HOME");
        if (string.IsNullOrWhiteSpace(home))
            throw new InvalidOperationException("XDG_DATA_HOME or HOME is required to resolve the Haven data root.");
        return new XdgPlatformStorageLayout(Path.Combine(home, ".local", "share", "haven"));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(StateDirectory);
        Directory.CreateDirectory(ModelsDirectory);
    }
}

public interface IVersionedSettingsStore
{
    int SchemaVersion { get; }
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}

public interface IConfigStore
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}

public interface IStateStore
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}

public sealed record MigrationRecord(
    int FromVersion,
    int ToVersion,
    DateTimeOffset AppliedAtUtc,
    string Description);

/// <summary>Atomic, versioned JSON settings store rooted exclusively in <see cref="IPlatformStorageLayout.DataRoot"/>.</summary>
public sealed class VersionedSettingsStore : IVersionedSettingsStore, IConfigStore, IStateStore
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly IPlatformStorageLayout _layout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, JsonElement> _entries = new(StringComparer.Ordinal);
    private bool _loaded;

    public VersionedSettingsStore(IPlatformStorageLayout layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    }

    public int SchemaVersion => CurrentSchemaVersion;

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _entries.TryGetValue(key, out var value) ? value.Deserialize<T>(SerializerOptions) : default;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateKey(key);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            _entries[key] = JsonSerializer.SerializeToElement(value, SerializerOptions);
            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            if (_entries.Remove(key))
                await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MigrationResult> MigrateAsync(int targetVersion, Func<Dictionary<string, JsonElement>, int, Task<Dictionary<string, JsonElement>>> migrator, CancellationToken cancellationToken = default)
    {
        if (targetVersion <= 0 || targetVersion > CurrentSchemaVersion)
            throw new ArgumentOutOfRangeException(nameof(targetVersion), "Target version must be between 1 and current schema version.");
        
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var currentVersion = _entries.TryGetValue("__schema_version__", out var versionElement)
                ? versionElement.GetInt32()
                : 0;
            
            if (currentVersion >= targetVersion)
                return new MigrationResult(true, currentVersion, targetVersion, "Already at or above target version.", []);

            var migratedEntries = await migrator(_entries, currentVersion).ConfigureAwait(false);
            migratedEntries["__schema_version__"] = JsonSerializer.SerializeToElement(targetVersion, SerializerOptions);
            
            var previousEntries = new Dictionary<string, JsonElement>(_entries);
            _entries = migratedEntries;
            await PersistAsync(cancellationToken).ConfigureAwait(false);
            
            var record = new MigrationRecord(currentVersion, targetVersion, DateTimeOffset.UtcNow, $"Migrated from v{currentVersion} to v{targetVersion}");
            return new MigrationResult(true, currentVersion, targetVersion, "Migration successful.", [record]);
        }
        catch (Exception ex)
        {
            return new MigrationResult(false, 0, targetVersion, ex.Message, []);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BackupResult> CreateBackupAsync(string backupName, CancellationToken cancellationToken = default)
    {
        ValidateKey(backupName);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var backupDir = Path.Combine(_layout.DataRoot, "backups");
            Directory.CreateDirectory(backupDir);
            var backupFile = Path.Combine(backupDir, $"{backupName}.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");
            
            var document = new SettingsDocument(CurrentSchemaVersion, _entries);
            await using (var stream = new FileStream(backupFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken).ConfigureAwait(false);
            }
            
            return new BackupResult(true, backupFile, _entries.Count, "Backup created successfully.");
        }
        catch (Exception ex)
        {
            return new BackupResult(false, null, 0, ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RestoreResult> RestoreBackupAsync(string backupFile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupFile) || !Path.IsPathFullyQualified(backupFile))
            throw new ArgumentException("Backup file must be an absolute path.", nameof(backupFile));
        
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = new FileStream(backupFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            var document = await JsonSerializer.DeserializeAsync<SettingsDocument>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("Backup document is empty.");
            
            if (document.SchemaVersion != CurrentSchemaVersion)
                throw new InvalidDataException($"Backup schema version {document.SchemaVersion} is unsupported; current is {CurrentSchemaVersion}.");
            
            _entries = document.Entries is null
                ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                : new Dictionary<string, JsonElement>(document.Entries, StringComparer.Ordinal);
            _loaded = true;
            await PersistAsync(cancellationToken).ConfigureAwait(false);
            
            return new RestoreResult(true, _entries.Count, "Backup restored successfully.");
        }
        catch (Exception ex)
        {
            return new RestoreResult(false, 0, ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
            return;
        if (!File.Exists(_layout.SettingsFile))
        {
            _loaded = true;
            return;
        }

        await using var stream = new FileStream(_layout.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var document = await JsonSerializer.DeserializeAsync<SettingsDocument>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Platform settings document is empty.");
        if (document.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Platform settings schema version {document.SchemaVersion} is unsupported.");
        _entries = document.Entries is null
            ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(document.Entries, StringComparer.Ordinal);
        _loaded = true;
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_layout.SettingsFile)
            ?? throw new InvalidOperationException("Platform settings file has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryFile = _layout.SettingsFile + ".tmp";
        var document = new SettingsDocument(CurrentSchemaVersion, _entries);
        await using (var stream = new FileStream(temporaryFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        File.Move(temporaryFile, _layout.SettingsFile, overwrite: true);
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 160 ||
            key.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
        {
            throw new ArgumentException("Settings keys may contain ASCII letters, digits, dot, dash, and underscore.", nameof(key));
        }
    }

    private sealed record SettingsDocument(int SchemaVersion, Dictionary<string, JsonElement>? Entries);
}

public sealed record MigrationResult(
    bool Success,
    int FromVersion,
    int ToVersion,
    string Message,
    IReadOnlyCollection<MigrationRecord> Records);

public sealed record BackupResult(
    bool Success,
    string? BackupPath,
    int EntryCount,
    string Message);

public sealed record RestoreResult(
    bool Success,
    int EntryCount,
    string Message);