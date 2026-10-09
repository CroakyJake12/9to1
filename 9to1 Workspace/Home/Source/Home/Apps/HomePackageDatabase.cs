using Haven.Application;
using System.Text.Json;
using System.Text.Json.Serialization;
using HavenOS.Home.Core;

namespace HavenOS.Home.Apps;

public enum HomePackageInstallState
{
    Unknown,
    Available,
    Staged,
    Installed,
    Updating,
    Repairing,
    Removing,
    Failed,
}

public enum HomePackageCompatibility
{
    Unknown,
    Compatible,
    Incompatible,
    MissingDependency,
    HomeServiceUnavailable,
}

public enum HomePackageIntegrityState
{
    Unknown,
    Pending,
    Verified,
    Rejected,
}

public enum HomePackageJournalState
{
    Pending,
    Staged,
    Validated,
    Activating,
    Succeeded,
    Failed,
    RolledBack,
    OutcomeUnknown,
}

public sealed record HomePackageDependency(string PackageId, string? MinimumVersion = null,
    string? MaximumVersionExclusive = null);

/// <summary>Verification evidence only; signature bytes and credentials are not persisted here.</summary>
public sealed record HomePackageIntegrityEvidence(string Version, string? ArtifactSha256,
    string? SignerIdentity, HomePackageIntegrityState State, DateTimeOffset? VerifiedAtUtc);

/// <summary>
/// Canonical device-local package state. Artifact paths are deliberately excluded; runtime
/// services resolve them from PackageId through trusted platform adapters.
/// </summary>
public sealed record HomePackageDatabaseEntry(
    string PackageId,
    string? AppId,
    string Name,
    string? IconUri,
    string? InstalledVersion,
    string? AvailableVersion,
    string? UpdateChannel,
    HomePackageInstallState InstallationState,
    HomePackageCompatibility Compatibility,
    string? CompatibilityReason,
    IReadOnlyList<HomePackageDependency> Dependencies,
    IReadOnlyList<HomePackageIntegrityEvidence> IntegrityEvidence,
    string? LastKnownGoodVersion,
    IReadOnlyList<string> RetainedRollbackVersions,
    bool PreservesOwnedArtifactsOnUninstall,
    long Revision)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <summary>
/// Durable deduplication journal for package operations. It is not a replacement for Home's
/// audit/event stream; it only prevents replay of a consequential operation after interruption.
/// </summary>
public sealed record HomePackageJournalEntry(
    string IdempotencyKey,
    string OperationId,
    string PackageId,
    string Action,
    HomePackageJournalState State,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string ResultCode,
    bool Retryable,
    bool PreviousKnownGoodVersionRetained,
    IReadOnlyList<string> SucceededSteps,
    IReadOnlyList<string> FailedSteps,
    IReadOnlyList<string> SkippedSteps,
    IReadOnlyList<string> RolledBackSteps)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

public sealed record HomePackageDatabaseSnapshot(
    int SchemaVersion,
    long Revision,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<HomePackageDatabaseEntry> Packages,
    IReadOnlyList<HomePackageJournalEntry> RecentOperations)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; init; }

    public static HomePackageDatabaseSnapshot Empty { get; } = new(
        HomePackageDatabase.CurrentSchemaVersion,
        0,
        DateTimeOffset.UnixEpoch,
        Array.Empty<HomePackageDatabaseEntry>(),
        Array.Empty<HomePackageJournalEntry>());
}

public sealed record HomePackageDatabaseFailure(string Code, string Message, string Target,
    bool Retryable, bool Recoverable);

public sealed record HomePackageDatabaseReadResult(HomePackageDatabaseSnapshot? Snapshot,
    HomePackageDatabaseFailure? Failure)
{
    public bool Succeeded => Snapshot is not null && Failure is null;
}

public sealed record HomePackageDatabaseWriteResult(HomePackageDatabaseSnapshot? Snapshot,
    HomePackageDatabaseFailure? Failure)
{
    public bool Succeeded => Snapshot is not null && Failure is null;
}

/// <summary>
/// Versioned canonical Home package database backed by the Home Core state store. The store's
/// compare-and-write revision provides cross-process conflict detection and atomic record saves.
/// </summary>
public sealed partial class HomePackageDatabase(IHomeCoreStateStore store)
{
    public const int CurrentSchemaVersion = 1;
    public const string RecordId = "home.packages.registry";
    public const string RecordType = "Home.PackageRegistry";
    public const int MaximumJournalEntries = 256;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHomeCoreStateStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    // Object binding only; trusted device scope, ACL and root commit authority remain external prerequisites.
    internal bool IsBoundToStore(IHomeCoreStateStore original) => ReferenceEquals(_store, original);

    public Task<HomePackageDatabaseReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
        ReadOriginalBootstrapCoreAsync(cancellationToken);

    private async Task<HomePackageDatabaseReadResult> ReadOriginalBootstrapCoreAsync(CancellationToken cancellationToken,
        HomeOwnershipOriginalSourceCallbacks? originalSource = null)
    {
        var read = originalSource is null
            ? await _store.ReadAsync(cancellationToken).ConfigureAwait(false)
            : await originalSource.ReadAsync(() => _store.ReadAsync(cancellationToken)).ConfigureAwait(false);
        if (!read.IsSuccess)
            return ReadFailure(read.Failure!);

        var records = read.State!.Records.Where(record => StringComparer.Ordinal.Equals(record.RecordId, RecordId)).ToArray();
        if (records.Length == 0)
            return new(HomePackageDatabaseSnapshot.Empty, null);
        if (records.Length != 1)
            return FailedRead("HomePackages.DuplicateRegistry", "The package database contains duplicate canonical records.", retryable: false);

        var record = records[0];
        if (!StringComparer.Ordinal.Equals(record.RecordType, RecordType) || record.Scope != HomeDataScope.DeviceLocal ||
            record.Authority != HomeRecordAuthority.LocalCanonical)
            return FailedRead("HomePackages.InvalidRegistryRecord", "The package database record has an incompatible identity or ownership scope.", retryable: false);
        if (record.SchemaVersion != CurrentSchemaVersion)
            return FailedRead("HomePackages.UnsupportedSchema", $"Package database schema {record.SchemaVersion} is not supported.", retryable: false);

        HomePackageDatabaseSnapshot snapshot;
        try
        {
            snapshot = record.Payload.Deserialize<HomePackageDatabaseSnapshot>(JsonOptions)
                ?? throw new JsonException("The package database payload is empty.");
        }
        catch (JsonException)
        {
            return FailedRead("HomePackages.CorruptRegistry", "The package database record is invalid. Its data was preserved for recovery.", retryable: false);
        }

        if (snapshot.SchemaVersion != CurrentSchemaVersion)
            return FailedRead("HomePackages.UnsupportedSchema", $"Package database payload schema {snapshot.SchemaVersion} is not supported.", retryable: false);
        if (snapshot.Revision != record.Revision)
            return FailedRead("HomePackages.RegistryRevisionMismatch", "The package database record and payload revisions do not match.", retryable: false);

        var validation = Validate(snapshot);
        return validation is null
            ? new(snapshot, null)
            : new(null, validation);
    }

    public Task<HomePackageDatabaseWriteResult> SaveAsync(HomePackageDatabaseSnapshot snapshot,
        long expectedRevision, CancellationToken cancellationToken = default) =>
        SaveCoreAsync(snapshot, expectedRevision, null, null, cancellationToken);

    /// <summary>Same canonical write, with a genuinely supplied non-reentrant owner guard under the store writer.</summary>
    internal Task<HomePackageDatabaseWriteResult> SaveGuardedAsync(HomePackageDatabaseSnapshot snapshot,
        long expectedRevision, AuthenticatedResourceActor originalActor, IHomeStateCommitActorGuard originalGuard,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalActor);
        ArgumentNullException.ThrowIfNull(originalGuard);
        var captured = CaptureGuardedSnapshot(snapshot);
        return SaveCoreAsync(captured, expectedRevision, originalActor, originalGuard, cancellationToken);
    }

    private async Task<HomePackageDatabaseWriteResult> SaveCoreAsync(HomePackageDatabaseSnapshot snapshot,
        long expectedRevision, AuthenticatedResourceActor? originalActor, IHomeStateCommitActorGuard? originalGuard,
        CancellationToken cancellationToken, HomeOwnershipOriginalSourceCallbacks? originalSource = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (expectedRevision < 0)
            return FailedWrite("HomePackages.InvalidRevision", "The expected package database revision is invalid.", retryable: false);
        if (expectedRevision == long.MaxValue)
            return FailedWrite("HomePackages.RevisionExhausted", "The package database revision cannot be advanced.", retryable: false);

        var validation = Validate(snapshot);
        if (validation is not null)
            return new(null, validation);
        if (snapshot.Revision != expectedRevision)
            return FailedWrite("HomePackages.StaleSnapshot", "The package database snapshot revision does not match the expected revision.", retryable: true);

        await WaitOriginalBootstrapPackageGateAsync(originalSource, cancellationToken).ConfigureAwait(false);
        try
        {
            var next = snapshot with
            {
                SchemaVersion = CurrentSchemaVersion,
                Revision = expectedRevision + 1,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Packages = snapshot.Packages.OrderBy(package => package.PackageId, StringComparer.Ordinal).ToArray(),
                RecentOperations = snapshot.RecentOperations.ToArray(),
            };
            var record = new HomeCoreStateRecord(
                RecordId,
                RecordType,
                CurrentSchemaVersion,
                HomeDataScope.DeviceLocal,
                HomeRecordAuthority.LocalCanonical,
                next.Revision,
                JsonSerializer.SerializeToElement(next, JsonOptions));
            var written = originalSource is null
                ? originalGuard is null
                    ? await _store.WriteAsync(record, expectedRevision, cancellationToken).ConfigureAwait(false)
                    : await _store.WriteGuardedAsync(record, expectedRevision, originalActor!, originalGuard, cancellationToken).ConfigureAwait(false)
                : await originalSource.ReadAsync(() => _store.WriteGuardedAsync(record, expectedRevision, originalActor!, originalGuard!, cancellationToken)).ConfigureAwait(false);
            if (!written.IsSuccess)
                return WriteFailure(written.Failure!);

            var saved = written.State!.Records.SingleOrDefault(item => StringComparer.Ordinal.Equals(item.RecordId, RecordId));
            if (saved is null || saved.Revision != next.Revision || saved.SchemaVersion != CurrentSchemaVersion)
                return FailedWrite("HomePackages.InvalidStoreResult", "Home Core did not confirm the saved package database revision.", retryable: true);
            return new(next, null);
        }
        finally
        {
            if (originalSource is null) _writeGate.Release(); else originalSource.Run(() => _writeGate.Release());
        }
    }

    public async Task<HomePackageJournalEntry?> FindOperationAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        var read = await ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!read.Succeeded)
            throw new HomePackageDatabaseException(read.Failure!);
        return read.Snapshot!.RecentOperations.FirstOrDefault(operation =>
            StringComparer.Ordinal.Equals(operation.IdempotencyKey, idempotencyKey));
    }

    private static HomePackageDatabaseFailure? Validate(HomePackageDatabaseSnapshot snapshot)
    {
        if (snapshot.SchemaVersion is not 0 and not CurrentSchemaVersion)
            return new("HomePackages.UnsupportedSchema", $"Package database schema {snapshot.SchemaVersion} is not supported.", RecordId, false, false);
        if (snapshot.Revision < 0)
            return new("HomePackages.InvalidRevision", "The package database revision cannot be negative.", RecordId, false, false);
        if (snapshot.Packages is null || snapshot.RecentOperations is null)
            return new("HomePackages.InvalidSnapshot", "The package database snapshot is missing required collections.", RecordId, false, false);
        if (snapshot.RecentOperations.Count > MaximumJournalEntries)
            return new("HomePackages.OperationJournalLimit", $"The operation journal cannot exceed {MaximumJournalEntries} entries.", RecordId, false, true);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in snapshot.Packages)
        {
            if (package is null || string.IsNullOrWhiteSpace(package.PackageId) || string.IsNullOrWhiteSpace(package.Name))
                return new("HomePackages.InvalidPackage", "Every package record requires a stable PackageID and name.", RecordId, false, true);
            if (!ids.Add(package.PackageId))
                return new("HomePackages.DuplicatePackageId", $"PackageID '{package.PackageId}' appears more than once.", package.PackageId, false, true);
            if (package.Dependencies is null || package.IntegrityEvidence is null || package.RetainedRollbackVersions is null || package.Revision < 0)
                return new("HomePackages.InvalidPackage", $"Package '{package.PackageId}' has incomplete or invalid state.", package.PackageId, false, true);
            if (package.RetainedRollbackVersions.Count > 100)
                return new("HomePackages.RollbackHistoryLimit", $"Package '{package.PackageId}' exceeds the retained rollback history limit.", package.PackageId, false, true);
        }

        var operationKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in snapshot.RecentOperations)
        {
            if (operation is null || string.IsNullOrWhiteSpace(operation.IdempotencyKey) ||
                string.IsNullOrWhiteSpace(operation.OperationId) || string.IsNullOrWhiteSpace(operation.PackageId) ||
                string.IsNullOrWhiteSpace(operation.Action) || string.IsNullOrWhiteSpace(operation.ResultCode) ||
                operation.SucceededSteps is null || operation.FailedSteps is null || operation.SkippedSteps is null ||
                operation.RolledBackSteps is null)
                return new("HomePackages.InvalidOperationJournal", "The package operation journal contains an incomplete entry.", RecordId, false, true);
            if (!operationKeys.Add(operation.IdempotencyKey))
                return new("HomePackages.DuplicateIdempotencyKey", "The package operation journal contains a duplicate idempotency key.", operation.IdempotencyKey, false, true);
        }

        return null;
    }

    // New owning guarded path only. Legacy SaveAsync validation/collection behavior remains exact.
    internal static HomePackageDatabaseSnapshot CaptureGuardedSnapshot(HomePackageDatabaseSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        static T[] Capture<T>(IReadOnlyList<T>? source, int maximum)
        {
            if (source is null) throw new InvalidDataException("Original guarded package collection missing.");
            var result = new List<T>();
            foreach (var item in source)
            {
                if (result.Count == maximum) throw new InvalidDataException("Original guarded package collection exceeds its bound.");
                result.Add(item);
            }
            return result.ToArray();
        }
        static Dictionary<string, JsonElement>? Fields(Dictionary<string, JsonElement>? original)
        {
            if (original is null) return null;
            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var item in original)
            {
                if (result.Count == 64 || item.Key.Length is < 1 or > 128)
                    throw new InvalidDataException("Original package extension metadata exceeds its bound.");
                result.Add(item.Key, item.Value);
            }
            return result;
        }
        var packages = Capture(snapshot.Packages, 4096).Select(item => item is null
            ? throw new InvalidDataException("Original package missing.")
            : item with {
                Dependencies = Array.AsReadOnly(Capture(item.Dependencies, 128)),
                IntegrityEvidence = Array.AsReadOnly(Capture(item.IntegrityEvidence, 128)),
                RetainedRollbackVersions = Array.AsReadOnly(Capture(item.RetainedRollbackVersions, 100)),
                UnknownFields = Fields(item.UnknownFields)
            }).ToArray();
        var journal = Capture(snapshot.RecentOperations, MaximumJournalEntries).Select(item => item is null
            ? throw new InvalidDataException("Original package journal entry missing.")
            : item with {
                SucceededSteps = Array.AsReadOnly(Capture(item.SucceededSteps, 1000)),
                FailedSteps = Array.AsReadOnly(Capture(item.FailedSteps, 1000)),
                SkippedSteps = Array.AsReadOnly(Capture(item.SkippedSteps, 1000)),
                RolledBackSteps = Array.AsReadOnly(Capture(item.RolledBackSteps, 1000)),
                UnknownFields = Fields(item.UnknownFields)
            }).ToArray();
        var detached = snapshot with {
            Packages = Array.AsReadOnly(packages), RecentOperations = Array.AsReadOnly(journal),
            UnknownFields = Fields(snapshot.UnknownFields)
        };
        using var stream = new GuardedSnapshotCaptureStream();
        // Serialization completes synchronously before publishing the first owning async task.
        // Its running cap bounds stream allocation/write; private parse detaches ALL nested JSON.
        // Serializer temporary buffers are not a whole-process memory bound.
        JsonSerializer.Serialize(stream, detached, JsonOptions);
        return JsonSerializer.Deserialize<HomePackageDatabaseSnapshot>(stream.ToArray(), JsonOptions)
            ?? throw new InvalidDataException("Original guarded package snapshot is empty.");
    }
    private sealed class GuardedSnapshotCaptureStream : MemoryStream
    {
        private const long MaximumBytes = 8L * 1024 * 1024;
        private void Demand(int bytes)
        {
            if (bytes < 0 || Position > MaximumBytes - bytes)
                throw new InvalidDataException("Original guarded package snapshot exceeds 8 MiB.");
        }
        public override void Write(byte[] buffer, int offset, int count)
        { Demand(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer)
        { Demand(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value)
        { Demand(1); base.WriteByte(value); }
    }

    private static HomePackageDatabaseReadResult ReadFailure(HomeCoreFailure failure) => new(null,
        new($"HomeCore.{failure.Code}", failure.Message, failure.Target, failure.Retryable, failure.RecoveryAction is not null));

    private static HomePackageDatabaseWriteResult WriteFailure(HomeCoreFailure failure) => new(null,
        new($"HomeCore.{failure.Code}", failure.Message, failure.Target, failure.Retryable, failure.RecoveryAction is not null));

    private static HomePackageDatabaseReadResult FailedRead(string code, string message, bool retryable) =>
        new(null, new(code, message, RecordId, retryable, Recoverable: true));

    private static HomePackageDatabaseWriteResult FailedWrite(string code, string message, bool retryable) =>
        new(null, new(code, message, RecordId, retryable, Recoverable: true));
}

public sealed class HomePackageDatabaseException(HomePackageDatabaseFailure failure)
    : InvalidOperationException(failure?.Message ?? "The package database operation failed.")
{
    public HomePackageDatabaseFailure Failure { get; } = failure ?? throw new ArgumentNullException(nameof(failure));
}
