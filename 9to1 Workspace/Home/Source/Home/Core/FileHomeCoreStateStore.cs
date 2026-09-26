using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;

namespace HavenOS.Home.Core;

/// <summary>
/// Crash-safe, revision-checked local Home state storage. A corrupt or newer file is reported
/// without replacing it, so permission/device/package state can be repaired instead of erased.
/// </summary>
public sealed class FileHomeCoreStateStore : IHomeCoreStateStore
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate;

    public FileHomeCoreStateStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A state file path is required.", nameof(path));
        _path = Path.GetFullPath(path);
        _gate = PathLocks.GetOrAdd(_path, static _ => new SemaphoreSlim(1, 1));
    }

    public static FileHomeCoreStateStore CreateDefault()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("The operating system did not provide a local application data directory.");
        return new FileHomeCoreStateStore(Path.Combine(root, "9to1", "Home", "home-core-state.json"));
    }

    public async Task<HomeStateReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var processLock = await AcquireProcessLockAsync(cancellationToken).ConfigureAwait(false);
            return await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HomeStateWriteResult> WriteAsync(
        HomeCoreStateRecord record,
        long expectedRecordRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (expectedRecordRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedRecordRevision));
        var invalid = ValidateRecord(record);
        if (invalid is not null) return HomeStateWriteResult.Failed(invalid);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var processLock = await AcquireProcessLockAsync(cancellationToken).ConfigureAwait(false);
            var read = await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            if (!read.IsSuccess)
                return HomeStateWriteResult.Failed(read.Failure!);

            var current = read.State!;
            var existing = current.Records.FirstOrDefault(item => item.RecordId == record.RecordId);
            var actualRevision = existing?.Revision ?? 0;
            if (actualRevision != expectedRecordRevision)
                return HomeStateWriteResult.Failed(new HomeCoreFailure(
                    HomeCoreErrorCode.HomeStateConflict,
                    $"State record revision conflict: expected {expectedRecordRevision}, observed {actualRevision}.",
                    record.RecordId,
                    true,
                    "Read the latest record and retry with its revision."));

            var updated = record with { Revision = checked(actualRevision + 1) };
            var records = current.Records.Where(item => item.RecordId != record.RecordId).Append(updated)
                .OrderBy(item => item.RecordId, StringComparer.Ordinal).ToArray();
            var next = new HomeCoreStoredState(CurrentSchemaVersion, checked(current.Revision + 1), records);
            var writeFailure = await WriteAtomicallyAsync(next, cancellationToken).ConfigureAwait(false);
            return writeFailure is null
                ? HomeStateWriteResult.Success(next)
                : HomeStateWriteResult.Failed(writeFailure);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HomeStateReadResult> ReadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return HomeStateReadResult.Success(new HomeCoreStoredState(CurrentSchemaVersion, 0, []));

        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var state = await JsonSerializer.DeserializeAsync<HomeCoreStoredState>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (state is null)
                return Corrupt("The Home state file contained no state document.");
            if (state.SchemaVersion != CurrentSchemaVersion)
                return HomeStateReadResult.Failed(new HomeCoreFailure(
                    HomeCoreErrorCode.HomeStateIncompatible,
                    $"Home state schema {state.SchemaVersion} is not supported by schema {CurrentSchemaVersion}.",
                    "9to1.Home.State",
                    false,
                    "Use the matching Home version or an explicit state migration."));
            if (state.Revision < 0 || state.Records is null)
                return Corrupt("The Home state document has invalid revision or records.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in state.Records)
            {
                if (record is null || ValidateRecord(record) is not null || !ids.Add(record.RecordId))
                    return Corrupt("The Home state document contains an invalid or duplicate record.");
            }
            return HomeStateReadResult.Success(state);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            return Corrupt($"The Home state file could not be decoded ({exception.GetType().Name}).");
        }
        catch (IOException exception)
        {
            return HomeStateReadResult.Failed(new HomeCoreFailure(
                HomeCoreErrorCode.HomeServiceUnavailable,
                $"The Home state file could not be read ({exception.GetType().Name}).",
                "9to1.Home.State",
                true,
                "Check local storage access and retry."));
        }
        catch (UnauthorizedAccessException)
        {
            return HomeStateReadResult.Failed(new HomeCoreFailure(
                HomeCoreErrorCode.PermissionDenied,
                "Home does not have permission to read its local state file.",
                "9to1.Home.State",
                false,
                "Restore the current user's local application data access."));
        }
    }

    private async Task<FileStream> AcquireProcessLockAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var lockPath = _path + ".lock";
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    1, FileOptions.Asynchronous);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(40), cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                throw new IOException("Home state is currently locked by another Home Core process.", exception);
            }
        }
    }

    private async Task<HomeCoreFailure?> WriteAtomicallyAsync(HomeCoreStoredState state, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)!;
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            // The temporary file is on the same volume. Replacing the destination leaves the old
            // committed document intact if the process stops before this operation.
            File.Move(temporaryPath, _path, overwrite: true);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return new HomeCoreFailure(HomeCoreErrorCode.PermissionDenied,
                "Home does not have permission to persist local state.", "9to1.Home.State", false,
                "Restore the current user's local application data access.");
        }
        catch (IOException exception)
        {
            return new HomeCoreFailure(HomeCoreErrorCode.HomeServiceUnavailable,
                $"Home state could not be committed ({exception.GetType().Name}).", "9to1.Home.State", true,
                "Check local storage availability and retry; the previous committed state remains authoritative.");
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { /* A stale temporary file is non-authoritative and never read on startup. */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static HomeCoreFailure? ValidateRecord(HomeCoreStateRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.RecordId) || string.IsNullOrWhiteSpace(record.RecordType) ||
            record.SchemaVersion <= 0 || record.Revision < 0 || record.Payload.ValueKind == JsonValueKind.Undefined)
            return new HomeCoreFailure(HomeCoreErrorCode.HomeStateCorrupt,
                "A Home state record has an invalid identity, schema, revision or payload.",
                "9to1.Home.State", false, "Repair the affected Home state record from its owning service.");

        if (record.Scope == HomeDataScope.DeviceLocal && record.Authority == HomeRecordAuthority.RemoteCanonicalReplica)
            return new HomeCoreFailure(HomeCoreErrorCode.HomeStateScopeUnsupported,
                "Device-local state cannot claim remote canonical authority.", record.RecordId, false);
        if (record.Scope != HomeDataScope.DeviceLocal && record.Authority == HomeRecordAuthority.LocalCanonical)
            return new HomeCoreFailure(HomeCoreErrorCode.HomeStateScopeUnsupported,
                "Account, workspace and organisation state require a remote-canonical replica or rebuildable cache authority.",
                record.RecordId, false, "Use the authenticated synchronization owner for non-device state.");
        if (ContainsSensitiveProperty(record.Payload))
            return new HomeCoreFailure(HomeCoreErrorCode.HomeSecretPersistenceBlocked,
                "Credential values cannot be stored in Home state; store an operating-system credential reference instead.",
                record.RecordId, false, "Replace secret material with a secure credential reference.");
        return null;
    }

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PathLocks = new(StringComparer.OrdinalIgnoreCase);

    private static bool ContainsSensitiveProperty(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                var key = property.Name.Replace("_", string.Empty, StringComparison.Ordinal)
                    .Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
                if (key is "password" or "secret" or "token" or "accesstoken" or "refreshtoken" or
                    "apikey" or "clientsecret" or "privatekey" or "authorization" or "cookie" or "credential")
                    return true;
                if (ContainsSensitiveProperty(property.Value)) return true;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                if (ContainsSensitiveProperty(item)) return true;
        }
        return false;
    }

    private static HomeStateReadResult Corrupt(string message) => HomeStateReadResult.Failed(new HomeCoreFailure(
        HomeCoreErrorCode.HomeStateCorrupt,
        message,
        "9to1.Home.State",
        false,
        "Preserve the original file and use Home's explicit repair or restore flow."));
}
