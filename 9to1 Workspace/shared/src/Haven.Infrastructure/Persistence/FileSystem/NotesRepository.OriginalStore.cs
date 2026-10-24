using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

/// <summary>Original Notes store metadata under the SAME repository semaphore and physical
/// .repository.lock used by ordinary Notes transactions. No content authority is minted here.
/// The first physical implementation is Linux; unsupported systems fail explicitly.</summary>
public sealed partial class NotesRepository : INotesOriginalScopedStoreSource, IHomeOriginalScopedResourceStoreIdentitySource
{
    private const string OriginalIdentityLeaf = ".store-identity.json";
    private sealed record OriginalIdentityDocument(int SchemaVersion, Guid StoreId, DateTimeOffset CreatedAtUtc);
    internal sealed record PhysicalVersion(ulong Inode, uint Major, uint Minor, uint User, ushort Mode,
        uint Links, ulong Size, long ChangedSeconds, uint ChangedNanoseconds, long WrittenSeconds, uint WrittenNanoseconds);
    private sealed record OriginalObservation(NotesRepository Issuer, ResourceStoreIdentity StoreIdentity,
        string Revision, bool IsEmpty, PhysicalVersion DataRoot, PhysicalVersion NotesRoot,
        PhysicalVersion File, PhysicalVersion CanonicalLock) : INotesOriginalStoreObservation;
    private sealed class OriginalSaveIntent : INotesOriginalSaveIntent
    {
        internal OriginalSaveIntent(NotesRepository issuer, NotesDocument candidate, string reason,
            OriginalObservation observation, byte[] canonicalBytes)
        {
            Issuer = issuer; Candidate = candidate; Reason = reason; StoreObservation = observation;
            ContentSha256 = Convert.ToHexString(SHA256.HashData(canonicalBytes));
            OperationId = Guid.NewGuid(); DocumentId = candidate.Id; ExpectedVersion = candidate.Version;
            Arguments = JsonSerializer.SerializeToElement(new { operationId = OperationId,
                storeId = observation.StoreIdentity.StoreId, storeRevision = observation.Revision,
                documentId = DocumentId, expectedVersion = ExpectedVersion,
                contentSha256 = ContentSha256, reason = Reason });
        }
        internal NotesRepository Issuer { get; }
        // The caller never receives this detached canonical candidate. A pending Home request
        // therefore cannot be repointed by later edits to the original UI document or reason.
        internal NotesDocument Candidate { get; }
        internal int ExecutionStarted;
        internal OriginalSaveOutcome? Outcome;
        public Guid OperationId { get; }
        public Guid DocumentId { get; }
        public long ExpectedVersion { get; }
        public string ContentSha256 { get; }
        public string Reason { get; }
        public INotesOriginalStoreObservation StoreObservation { get; }
        public JsonElement Arguments { get; }
    }

    public async ValueTask<INotesOriginalSaveIntent> PrepareOriginalSaveAsync(NotesDocument document,
        string reason, CancellationToken cancellationToken = default)
    {
        ValidateSaveCandidate(document);
        // Serialize BEFORE the first await, then deserialize into private ownership. The
        // ordinary editor may continue changing its own instance during Home review.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        var detached = JsonSerializer.Deserialize<NotesDocument>(bytes, JsonOptions)
            ?? throw new InvalidDataException("The Notes candidate could not be detached.");
        ValidateSaveCandidate(detached);
        var capturedReason = NormalizeReason(reason);
        var observed = await ObserveOriginalStoreCoreAsync(initialise: false, cancellationToken).ConfigureAwait(false);
        return new OriginalSaveIntent(this, detached, capturedReason, observed, bytes);
    }

    public bool IsIssuedOriginalSaveIntent(INotesOriginalSaveIntent intent) =>
        intent is OriginalSaveIntent actual && ReferenceEquals(actual.Issuer, this) &&
        IsIssuedOriginalStoreObservation(actual.StoreObservation);

    private sealed class OriginalLibraryReadRequest(NotesRepository issuer, OriginalObservation observation,
        Guid? documentId, NotesOriginalReadKind kind = NotesOriginalReadKind.LibrarySnapshot,
        string? versionId = null) : INotesOriginalLibraryReadRequest
    {
        internal NotesRepository Issuer { get; } = issuer;
        public Guid OperationId { get; } = Guid.NewGuid();
        public Guid? DocumentId { get; } = documentId;
        public NotesOriginalReadKind Kind { get; } = kind;
        public string? VersionId { get; } = versionId;
        public INotesOriginalStoreObservation StoreObservation { get; } = observation;
        public JsonElement Arguments => Kind == NotesOriginalReadKind.LibrarySnapshot
            ? JsonSerializer.SerializeToElement(new { operationId = OperationId,
                storeId = observation.StoreIdentity.StoreId, storeRevision = observation.Revision, documentId = DocumentId })
            : JsonSerializer.SerializeToElement(new { operationId = OperationId,
                storeId = observation.StoreIdentity.StoreId, storeRevision = observation.Revision, documentId = DocumentId,
                kind = Kind == NotesOriginalReadKind.VersionHistory ? "history" : "saved-version", versionId = VersionId });
        internal int ExecutionStarted;
        internal OriginalLibraryReadOutcome? Outcome;
        internal NotesOriginalLibrarySnapshot? ReturnedSnapshot;
        internal IReadOnlyList<NotesOriginalDocumentMetadata>? ObservedMetadata;
        internal OriginalLibraryReadWitness? ReadWitness;
        internal HomeNotesLibraryReadAdmission? ReadAdmission;
    }

    public async ValueTask<INotesOriginalLibraryReadRequest> PrepareOriginalLibraryReadAsync(Guid? documentId,
        CancellationToken cancellationToken = default)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("The requested document ID cannot be empty.", nameof(documentId));
        var observed = await ObserveOriginalStoreCoreAsync(initialise: false, cancellationToken).ConfigureAwait(false);
        return new OriginalLibraryReadRequest(this, observed, documentId);
    }

    /// <summary>Prepare one exact history-list or saved-version read. This observes metadata
    /// only; the same original Home review, one-use claim and Notes read-only transaction are required.</summary>
    public async ValueTask<INotesOriginalLibraryReadRequest> PrepareOriginalVersionReadAsync(Guid documentId,
        string? versionId = null, CancellationToken cancellationToken = default)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("A canonical Notes document ID is required.", nameof(documentId));
        if (versionId is not null && !NotesOriginalVersionIdentity.IsVersionId(versionId))
            throw new ArgumentException("An exact canonical Notes saved-version ID is required.", nameof(versionId));
        var observed = await ObserveOriginalStoreCoreAsync(initialise: false, cancellationToken).ConfigureAwait(false);
        return new OriginalLibraryReadRequest(this, observed, documentId,
            versionId is null ? NotesOriginalReadKind.VersionHistory : NotesOriginalReadKind.Version, versionId);
    }

    public bool IsIssuedOriginalLibraryReadRequest(INotesOriginalLibraryReadRequest request) =>
        request is OriginalLibraryReadRequest original && ReferenceEquals(original.Issuer, this) &&
        IsIssuedOriginalStoreObservation(original.StoreObservation);

    private OriginalObservation? _originalStoreBaseline;
    private Guid? _identityCreatedInThisOwner;
    private bool _identityCreatedOnEmptyRoot;

    public async ValueTask<ResourceStoreIdentity> InitialiseOriginalStoreIdentityAsync(CancellationToken cancellationToken = default)
        => (await ObserveOriginalStoreCoreAsync(initialise: true, cancellationToken).ConfigureAwait(false)).StoreIdentity;

    /// <summary>Read only. Missing identity is a setup requirement, never an implicit new store.</summary>
    public async ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken cancellationToken)
        => (await ObserveOriginalStoreCoreAsync(initialise: false, cancellationToken).ConfigureAwait(false)).StoreIdentity;

    public async ValueTask<INotesOriginalStoreObservation> ObserveOriginalStoreAsync(CancellationToken cancellationToken = default)
        => await ObserveOriginalStoreCoreAsync(initialise: false, cancellationToken).ConfigureAwait(false);

    public bool IsIssuedOriginalStoreObservation(INotesOriginalStoreObservation observation)
        => observation is OriginalObservation original && ReferenceEquals(original.Issuer, this);

    public async ValueTask<bool> IsOriginalStoreObservationCurrentAsync(INotesOriginalStoreObservation observation,
        CancellationToken cancellationToken = default)
    {
        if (observation is not OriginalObservation original || !ReferenceEquals(original.Issuer, this)) return false;
        var current = await ObserveOriginalStoreCoreAsync(initialise: false, cancellationToken).ConfigureAwait(false);
        return current.StoreIdentity.StoreId == original.StoreIdentity.StoreId && current.Revision == original.Revision
            && current.IsEmpty == original.IsEmpty && SameNode(current.CanonicalLock, original.CanonicalLock)
            && SameNode(current.DataRoot, original.DataRoot) && SameNode(current.NotesRoot, original.NotesRoot)
            && current.File == original.File;
    }

    private sealed record OriginalSetupInspection(NotesRepository Issuer, NotesOriginalStoreSetupState State,
        INotesOriginalStoreObservation? Observation, string Code, string Message) : INotesOriginalStoreSetupInspection;

    public bool IsIssuedOriginalSetupInspection(INotesOriginalStoreSetupInspection inspection) =>
        inspection is OriginalSetupInspection actual && ReferenceEquals(actual.Issuer, this);

    public ValueTask<INotesOriginalStoreSetupInspection> InspectOriginalStoreSetupWithinOriginalSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken = default)
    {
        var source = new OriginalMetadataSource(this, scope, retain);
        return new(source.RunAsync<INotesOriginalStoreSetupInspection>(async () =>
        {
            // The probe reads no document and creates no directory, lock or identity.
            // Expected physical refusals are classified INSIDE the finite source callback;
            // a caller callback failure remains an original fault, never a setup result.
            var probe = source.Invoke(() => ProbeOriginalStoreSetup(cancellationToken));
            if (probe.State != NotesOriginalStoreSetupState.Available) return probe;
            var observed = await ObserveOriginalStoreCoreAsync(false, cancellationToken, source).ConfigureAwait(false);
            return new OriginalSetupInspection(this, NotesOriginalStoreSetupState.Available, observed,
                "NotesIdentityAvailable", "The original local library identity is available.");
        }));
    }

    private OriginalSetupInspection ProbeOriginalStoreSetup(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        OriginalSetupInspection Result(NotesOriginalStoreSetupState state, string code, string message) =>
            new(this, state, null, code, message);
        if (!OperatingSystem.IsLinux() || !BitConverter.IsLittleEndian ||
            RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            return Result(NotesOriginalStoreSetupState.Unsupported, "NotesPhysicalBoundaryUnsupported",
                "This platform does not yet provide the required local library storage boundary.");
        try
        {
            var dataRoot = Path.GetDirectoryName(Path.GetDirectoryName(_root)!)!;
            using var root = OriginalNotesRoot.Open(dataRoot, false);
            using var identity = root.OpenIdentity(false, out _);
            root.DemandCurrent();
            return Result(NotesOriginalStoreSetupState.Available, "NotesIdentityPresent", "Inspect the original local library.");
        }
        catch (IOException error) when (_originalStoreBaseline is null &&
            (error is DirectoryNotFoundException or FileNotFoundException || error.InnerException is Win32Exception { NativeErrorCode: 2 }))
        {
            return Result(NotesOriginalStoreSetupState.MissingIdentity, "NotesIdentitySetupRequired",
                "Set up this device's library or review an existing library. Nothing has been imported.");
        }
        catch (UnauthorizedAccessException)
        {
            return Result(NotesOriginalStoreSetupState.Inaccessible, "NotesOriginalStoreInaccessible",
                "The original local library is not accessible to this operating-system user. Preserve it and repair access.");
        }
        catch (IOException)
        {
            return Result(NotesOriginalStoreSetupState.RecoveryRequired, "NotesOriginalStoreRecoveryRequired",
                "The original local library needs recovery. Its files and ownership have been preserved.");
        }
    }

    public ValueTask<ResourceStoreIdentity> GetStoreIdentityWithinOriginalSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        new(ReadOriginalMetadataAsync(scope, retain, async source =>
            (await ObserveOriginalStoreCoreAsync(false, token, source).ConfigureAwait(false)).StoreIdentity));

    public ValueTask<ResourceStoreIdentity> InitialiseOriginalStoreIdentityWithinOriginalSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken = default) =>
        new(ReadOriginalMetadataAsync(scope, retain, async source =>
            (await ObserveOriginalStoreCoreAsync(true, cancellationToken, source).ConfigureAwait(false)).StoreIdentity));

    public ValueTask<INotesOriginalStoreObservation> ObserveOriginalStoreWithinOriginalSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken = default) =>
        new(ReadOriginalMetadataAsync<INotesOriginalStoreObservation>(scope, retain, async source =>
            await ObserveOriginalStoreCoreAsync(false, cancellationToken, source).ConfigureAwait(false)));

    public ValueTask<bool> IsOriginalStoreObservationCurrentWithinOriginalSourceAsync(INotesOriginalStoreObservation observation,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken = default) =>
        new(ReadOriginalMetadataAsync(scope, retain, async source =>
        {
            if (observation is not OriginalObservation original || !ReferenceEquals(original.Issuer, this)) return false;
            var current = await ObserveOriginalStoreCoreAsync(false, cancellationToken, source).ConfigureAwait(false);
            return current.StoreIdentity.StoreId == original.StoreIdentity.StoreId && current.Revision == original.Revision &&
                current.IsEmpty == original.IsEmpty && SameNode(current.CanonicalLock, original.CanonicalLock) &&
                SameNode(current.DataRoot, original.DataRoot) && SameNode(current.NotesRoot, original.NotesRoot) && current.File == original.File;
        }));

    private Task<T> ReadOriginalMetadataAsync<T>(Action<Action> scope, Action<Task> retain,
        Func<OriginalMetadataSource, Task<T>> body)
    {
        var source = new OriginalMetadataSource(this, scope, retain);
        return source.RunAsync(() => body(source));
    }

    // Finite source/task custody shared by metadata inspection and explicit identity
    // creation. This is a producer boundary, not a second application state machine.
    internal sealed class OriginalMetadataSource
    {
        private readonly CloudflareOriginalTaskLedger _originals = new();
        private readonly Action<Task> _retain;
        internal OriginalMetadataSource(NotesRepository owner, Action<Action> scope, Action<Task> retain)
        {
            ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
            _retain = retain; _originals.BindOriginalOwner(owner);
            _originals.BindOriginalCallerCallback(body =>
            {
                int active = 1, used = 0, thread = Environment.CurrentManagedThreadId;
                Exception? originalFailure = null;
                try
                {
                    scope(() =>
                    {
                        if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread ||
                            Interlocked.Exchange(ref used, 1) != 0)
                        {
                            var refusal = new InvalidOperationException("The original Notes metadata callback is inactive, foreign-thread or consumed.");
                            _originals.Retain(refusal); throw refusal;
                        }
                        try { body(); }
                        catch (Exception cause) { originalFailure = cause; _originals.Retain(cause); throw; }
                    });
                    if (Volatile.Read(ref used) == 0) throw new InvalidOperationException("The original Notes metadata callback was not invoked.");
                    if (originalFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(originalFailure).Throw();
                }
                finally { Interlocked.Exchange(ref active, 0); }
            });
        }
        internal T Invoke<T>(Func<T> body) => _originals.Invoke(body);
        internal void Retain(Exception error) => _originals.Retain(error);
        internal void Run(Action body) => Invoke(() => { body(); return true; });
        internal Task<T> RunAsync<T>(Func<Task<T>> body) => _originals.RunToOriginalSettlementAsync(body);
        internal async Task<T> CaptureAsync<T>(Func<Task<T>> acquire, Action<T> capture)
        {
            Task<T>? actual = null; T value = default!;
            try { _ = _originals.Invoke(() => { actual = acquire(); _originals.Track(actual); _retain(actual); return actual; }); }
            catch (Exception error) { _originals.Retain(error); }
            if (actual is not null)
                try { value = await _originals.AwaitAsync(actual).ConfigureAwait(false); capture(value); }
                catch (Exception error) { _originals.Capture(actual, error); }
            Throw();
            return actual is null ? throw new InvalidOperationException("No original Notes acquisition task was captured.") : value;
        }
        internal async Task WaitAsync(Func<Task> acquire, Action? acquired = null)
        {
            Task? actual = null;
            try { _ = _originals.Invoke(() => { actual = acquire(); _originals.Track(actual); _retain(actual); return actual; }); }
            catch (Exception error) { _originals.Retain(error); }
            if (actual is not null)
                try { await _originals.AwaitAsync(actual).ConfigureAwait(false); acquired?.Invoke(); }
                catch (Exception error) { _originals.Capture(actual, error); }
            Throw();
            if (actual is null) throw new InvalidOperationException("No original Notes source task was captured.");
        }
        private void Throw()
        {
            if (_originals.OriginalErrors.Count != 0)
                throw new AggregateException("The original Notes metadata source or finite callback failed.", _originals.OriginalErrors);
        }
    }

    private async Task<OriginalObservation> ObserveOriginalStoreCoreAsync(bool initialise, CancellationToken token,
        OriginalMetadataSource? source = null)
    {
        if (!OperatingSystem.IsLinux() || !BitConverter.IsLittleEndian ||
            RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("The original Notes physical identity boundary requires the supported Linux backend.");
        T Read<T>(Func<T> body) => source is null ? body() : source.Invoke(body);
        void Run(Action body) { if (source is null) body(); else source.Run(body); }
        bool entered = false; OriginalNotesRoot? root = null; FileStream? lease = null, file = null;
        try
        {
            if (source is null) { await _gate.WaitAsync(token).ConfigureAwait(false); entered = true; }
            else await source.WaitAsync(() => _gate.WaitAsync(token), () => entered = true).ConfigureAwait(false);
            Run(token.ThrowIfCancellationRequested);
            var originalDataRoot = Path.GetDirectoryName(Path.GetDirectoryName(_root)!)!;
            if (source is null) root = OriginalNotesRoot.Open(originalDataRoot, initialise);
            else await source.CaptureAsync(() => Task.FromResult(OriginalNotesRoot.Open(originalDataRoot, initialise)),
                value => root = value).ConfigureAwait(false);
            var actualRoot = root!;
            if (source is null) lease = await actualRoot.AcquireCanonicalRepositoryLockAsync(token).ConfigureAwait(false);
            else await source.CaptureAsync(() => actualRoot.AcquireCanonicalRepositoryLockAsync(token, source),
                value => lease = value).ConfigureAwait(false);
            Run(actualRoot.DemandCurrent);
            if (_originalStoreBaseline is { } baseline &&
                (!SameNode(actualRoot.DataIdentity, baseline.DataRoot) || !SameNode(actualRoot.NotesIdentity, baseline.NotesRoot) ||
                 !SameNode(actualRoot.CanonicalLockIdentity!, baseline.CanonicalLock)))
                throw new IOException("The original Notes directory identity changed; preserve the store for recovery.");
            var empty = Read(actualRoot.IsContentEmpty);
            bool created = false;
            if (source is null) file = actualRoot.OpenIdentity(initialise && _originalStoreBaseline is null, out created);
            else await source.CaptureAsync(() =>
            {
                var opened = actualRoot.OpenIdentity(initialise && _originalStoreBaseline is null, out var wasCreated);
                return Task.FromResult((File: opened, Created: wasCreated));
            }, value => { file = value.File; created = value.Created; }).ConfigureAwait(false);
            var actualFile = file!;
            if (created)
            {
                var identity = new OriginalIdentityDocument(1, Guid.NewGuid(), DateTimeOffset.UtcNow);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(identity, JsonOptions);
                if (source is null) await actualFile.WriteAsync(bytes, token).ConfigureAwait(false);
                else await source.WaitAsync(() => actualFile.WriteAsync(bytes, token).AsTask()).ConfigureAwait(false);
                Run(() => { actualFile.Flush(flushToDisk: true); actualRoot.Flush(); });
                _identityCreatedInThisOwner = identity.StoreId; _identityCreatedOnEmptyRoot = empty;
            }
            var before = Read(() => OriginalNotesRoot.Stat(actualFile.SafeFileHandle));
            Run(() => OriginalNotesRoot.DemandPrivateRegular(before));
            if (before.Size is 0 or > 4096) throw new InvalidDataException("The Notes store identity has an invalid size.");
            Run(() => actualFile.Position = 0);
            var buffer = new byte[checked((int)before.Size)];
            if (source is null) await actualFile.ReadExactlyAsync(buffer, token).ConfigureAwait(false);
            else await source.WaitAsync(() => actualFile.ReadExactlyAsync(buffer, token).AsTask()).ConfigureAwait(false);
            var after = Read(() => OriginalNotesRoot.Stat(actualFile.SafeFileHandle));
            if (before != after || Read(actualRoot.ReadIdentityVersion) != after)
                throw new IOException("The original Notes identity changed while it was read.");
            Run(actualRoot.DemandCurrent);
            OriginalIdentityDocument stored;
            try
            {
                using var json = JsonDocument.Parse(buffer);
                if (json.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("The store identity must be an object.");
                var names = json.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
                if (names.Length != 3 || names.Distinct(StringComparer.Ordinal).Count() != 3 ||
                    !names.Order(StringComparer.Ordinal).SequenceEqual(new[] { "createdAtUtc", "schemaVersion", "storeId" }))
                    throw new JsonException("Unknown or repeated store identity field.");
                stored = json.RootElement.Deserialize<OriginalIdentityDocument>(JsonOptions) ?? throw new JsonException("No store identity.");
                if (stored.SchemaVersion != 1 || stored.StoreId == Guid.Empty || stored.CreatedAtUtc.Offset != TimeSpan.Zero)
                    throw new JsonException("Invalid store identity.");
            }
            catch (JsonException error) { throw new InvalidDataException("The Notes store identity is corrupt; it was preserved.", error); }
            var fingerprint = Convert.ToHexString(SHA256.HashData(buffer));
            if (_originalStoreBaseline is { } captured &&
                (captured.File != after || captured.Revision != fingerprint || captured.StoreIdentity.StoreId != stored.StoreId))
                throw new IOException("The original Notes identity was replaced; it was not rebound.");
            var observation = new OriginalObservation(this,
                new(stored.SchemaVersion, stored.StoreId, stored.CreatedAtUtc,
                    _identityCreatedInThisOwner == stored.StoreId && _identityCreatedOnEmptyRoot),
                fingerprint, empty, actualRoot.DataIdentity, actualRoot.NotesIdentity, after, actualRoot.CanonicalLockIdentity!);
            _originalStoreBaseline ??= observation;
            return observation;
        }
        catch (Exception error) { source?.Retain(error); throw; }
        finally
        {
            // Physical resources acquired before a post-callback fault are retained above
            // and released here, independently of whether the caller scope is still live.
            var cleanup = new List<Exception>();
            void Release(Action body)
            { try { body(); } catch (Exception error) { cleanup.Add(error); source?.Retain(error); } }
            if (file is not null) Release(file.Dispose);
            if (lease is not null) Release(lease.Dispose);
            if (root is not null) Release(root.Dispose);
            if (entered) Release(() => _gate.Release());
            if (cleanup.Count != 0) throw new AggregateException("Original Notes metadata cleanup failed.", cleanup);
        }
    }

    private void DemandOriginalObservationWithinTransaction(OriginalNotesRoot root, OriginalObservation observed)
    {
        if (!ReferenceEquals(observed.Issuer, this)) throw new UnauthorizedAccessException("Foreign Notes observation.");
        root.DemandCurrent();
        if (!SameNode(root.DataIdentity, observed.DataRoot) || !SameNode(root.NotesIdentity, observed.NotesRoot) ||
            !SameNode(root.CanonicalLockIdentity!, observed.CanonicalLock) || root.ReadIdentityVersion() != observed.File ||
            root.IsContentEmpty() != observed.IsEmpty)
            throw new IOException("The original Notes store changed before its protected transaction.");
    }

    private static bool SameNode(PhysicalVersion left, PhysicalVersion right) =>
        left.Inode == right.Inode && left.Major == right.Major && left.Minor == right.Minor &&
        left.User == right.User && left.Mode == right.Mode;

    internal sealed class OriginalNotesRoot : IDisposable
    {
        private const int ReadOnly = 0, ReadWrite = 2, Create = 64, Exclusive = 128,
            NonBlock = 2048, CloseOnExec = 524288;
        // Linux ARM64 and x64 publish different fcntl bits for these two flags.
        // Applying the x64 bits on ARM64 enables unrelated flags and loses no-follow.
        private static int DirectoryFlag => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 16384 : 65536;
        private static int NoFollow => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 32768 : 131072;
        private readonly string _dataPath;
        private readonly SafeFileHandle _data, _notes;
        internal PhysicalVersion DataIdentity { get; }
        internal PhysicalVersion NotesIdentity { get; }
        internal PhysicalVersion? CanonicalLockIdentity { get; private set; }
        private OriginalNotesRoot(string dataPath, SafeFileHandle data, SafeFileHandle notes)
        {
            _dataPath = dataPath; _data = data; _notes = notes;
            DataIdentity = Stat(data); NotesIdentity = Stat(notes);
            DemandDirectory(DataIdentity); DemandDirectory(NotesIdentity);
            if ((DataIdentity.Mode & 63) != 0)
                throw new UnauthorizedAccessException("The original Notes application root must remain private to its OS owner.");
        }
        internal static OriginalNotesRoot Open(string dataPath, bool initialise)
        {
            var canonical = Path.GetFullPath(dataPath);
            // No traversal through a reparse point, including an earlier ancestor.
            for (var directory = new DirectoryInfo(canonical); directory is not null; directory = directory.Parent)
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("Original Notes storage cannot traverse a symbolic link.");
            var data = OpenHandle(canonical, ReadOnly | DirectoryFlag | NoFollow | CloseOnExec);
            SafeFileHandle? notes = null;
            try
            {
                DemandDirectory(Stat(data));
                if (initialise && NativeMkdirAt(Fd(data), "Notes", 448) != 0 && Marshal.GetLastPInvokeError() != 17)
                    throw NativeFailure("Create original Notes directory");
                notes = OpenRelative(data, "Notes", ReadOnly | DirectoryFlag | NoFollow | CloseOnExec);
                return new(canonical, data, notes);
            }
            catch { notes?.Dispose(); data.Dispose(); throw; }
        }
        internal void DemandCurrent()
        {
            using var currentData = OpenHandle(_dataPath, ReadOnly | DirectoryFlag | NoFollow | CloseOnExec);
            using var currentNotes = OpenRelative(currentData, "Notes", ReadOnly | DirectoryFlag | NoFollow | CloseOnExec);
            if (!SameNode(Stat(currentData), DataIdentity) || !SameNode(Stat(currentNotes), NotesIdentity))
                throw new IOException("The original Notes directory was replaced.");
            if (CanonicalLockIdentity is { } originalLock)
            {
                using var namedLock = OpenRelative(currentNotes, ".repository.lock", ReadWrite | NonBlock | NoFollow | CloseOnExec);
                var currentLock = Stat(namedLock);
                if (!SameNode(currentLock, originalLock) || currentLock.Links != 1)
                    throw new IOException("The original Notes repository lock was replaced.");
            }
        }
        internal bool IsContentEmpty()
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(Alias(_notes)))
            {
                var leaf = Path.GetFileName(entry);
                if (leaf is OriginalIdentityLeaf or ".repository.lock") continue;
                if (leaf is not ("Documents" or "Trash")) return false;
                using var directory = OpenRelative(_notes, leaf, ReadOnly | DirectoryFlag | NoFollow | CloseOnExec);
                DemandDirectory(Stat(directory));
                if (Directory.EnumerateFileSystemEntries(Alias(directory)).Any()) return false;
            }
            return true;
        }
        internal FileStream OpenIdentity(bool create, out bool created)
        {
            created = false;
            SafeFileHandle handle;
            if (create)
            {
                var fd = NativeOpenAt(Fd(_notes), OriginalIdentityLeaf,
                    ReadWrite | Create | Exclusive | NonBlock | NoFollow | CloseOnExec, 384);
                if (fd >= 0) { handle = new((IntPtr)fd, ownsHandle: true); created = true; }
                else if (Marshal.GetLastPInvokeError() == 17) handle = OpenRelative(_notes, OriginalIdentityLeaf, ReadOnly | NonBlock | NoFollow | CloseOnExec);
                else throw NativeFailure("Create original Notes identity");
            }
            else handle = OpenRelative(_notes, OriginalIdentityLeaf, ReadOnly | NonBlock | NoFollow | CloseOnExec);
            try
            {
                DemandPrivateRegular(Stat(handle));
                return new FileStream(handle, created ? FileAccess.ReadWrite : FileAccess.Read, 4096, isAsync: false);
            }
            catch { handle.Dispose(); throw; }
        }
        internal PhysicalVersion ReadIdentityVersion()
        {
            using var actual = OpenRelative(_notes, OriginalIdentityLeaf, ReadOnly | NonBlock | NoFollow | CloseOnExec);
            var identity = Stat(actual); DemandPrivateRegular(identity); return identity;
        }
        internal Task<FileStream> AcquireCanonicalRepositoryLockAsync(CancellationToken token) =>
            AcquireCanonicalRepositoryLockAsync(token, null);
        internal async Task<FileStream> AcquireCanonicalRepositoryLockAsync(CancellationToken token, OriginalMetadataSource? source)
        {
            // flock is the same physical lock used by FileShare.None in the existing Unix
            // Notes transaction. No second lock file or independent storage authority.
            var timer = Stopwatch.StartNew();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                FileStream? acquired = null;
                FileStream? Attempt()
                {
                    var handle = OpenRelative(_notes, ".repository.lock", ReadWrite | Create | NonBlock | NoFollow | CloseOnExec, 384);
                    try
                    {
                        var identity = Stat(handle);
                        if ((identity.Mode & 0xf000) != 0x8000 || identity.User != NativeGetEffectiveUser() || identity.Links != 1)
                            throw new UnauthorizedAccessException("The original Notes repository lock is not an owned regular file.");
                        if (NativeFlock(Fd(handle), 2 | 4) == 0)
                        {
                            using var probe = OpenRelative(_notes, ".repository.lock", ReadWrite | NonBlock | NoFollow | CloseOnExec);
                            if (!SameNode(Stat(probe), identity))
                                throw new IOException("The original Notes repository lock was replaced while being acquired.");
                            if (NativeFlock(Fd(probe), 2 | 4) == 0)
                                throw new IOException("Original Notes storage did not enforce its canonical process lock.");
                            if (Marshal.GetLastPInvokeError() != 11) throw NativeFailure("Verify original Notes lock");
                            CanonicalLockIdentity = identity; DemandCurrent();
                            return new FileStream(handle, FileAccess.ReadWrite, 1, isAsync: false);
                        }
                        if (Marshal.GetLastPInvokeError() != 11) throw NativeFailure("Acquire original Notes lock");
                    }
                    catch { handle.Dispose(); throw; }
                    handle.Dispose(); return null;
                }
                try
                {
                    if (source is null) acquired = Attempt();
                    else await source.CaptureAsync(() => Task.FromResult(Attempt()), value => acquired = value).ConfigureAwait(false);
                    if (acquired is not null) return acquired;
                }
                catch { acquired?.Dispose(); throw; }
                if (timer.Elapsed >= RepositoryLockTimeout) throw new IOException("Timed out acquiring the original Notes repository lock.");
                if (source is null) await Task.Delay(25, token).ConfigureAwait(false);
                else await source.WaitAsync(() => Task.Delay(25, token)).ConfigureAwait(false);
            }
        }
        internal OriginalDocumentCustody OpenDocumentCustody(Guid id, bool createDirectories = true)
            => new(this, _notes, id, DirectoryFlag, NoFollow, createDirectories);
        internal OriginalLibraryDirectory OpenLibraryCustody()
            => new(this, _notes, DirectoryFlag, NoFollow);
        internal void Flush() { if (NativeSync(Fd(_notes)) != 0) throw NativeFailure("Flush original Notes identity directory"); }
        internal static PhysicalVersion Stat(SafeFileHandle handle)
        {
            var bytes = new byte[256];
            if (NativeStatAt(Fd(handle), "", 0x1000, 0x7ff, bytes) != 0) throw NativeFailure("Observe original Notes physical identity");
            if ((BitConverter.ToUInt32(bytes, 0) & 0x7ff) != 0x7ff)
                throw new PlatformNotSupportedException("The filesystem did not provide the required original Notes identity fields.");
            return new(BitConverter.ToUInt64(bytes, 32), BitConverter.ToUInt32(bytes, 136), BitConverter.ToUInt32(bytes, 140),
                BitConverter.ToUInt32(bytes, 20), BitConverter.ToUInt16(bytes, 28), BitConverter.ToUInt32(bytes, 16),
                BitConverter.ToUInt64(bytes, 40), BitConverter.ToInt64(bytes, 96), BitConverter.ToUInt32(bytes, 104),
                BitConverter.ToInt64(bytes, 112), BitConverter.ToUInt32(bytes, 120));
        }
        internal static void DemandPrivateRegular(PhysicalVersion identity)
        {
            if ((identity.Mode & 0xf000) != 0x8000 || identity.User != NativeGetEffectiveUser() || identity.Links != 1 || (identity.Mode & 63) != 0)
                throw new UnauthorizedAccessException("The original Notes identity must be a private owned regular file.");
        }
        internal static void DemandDirectory(PhysicalVersion identity)
        {
            if ((identity.Mode & 0xf000) != 0x4000 || identity.User != NativeGetEffectiveUser() || (identity.Mode & 18) != 0)
                throw new UnauthorizedAccessException("Original Notes directories require the current OS owner and no foreign write access.");
        }
        internal static string Alias(SafeFileHandle handle) => "/proc/self/fd/" + Fd(handle);
        internal static int Fd(SafeFileHandle handle) => checked((int)handle.DangerousGetHandle());
        private static SafeFileHandle OpenHandle(string path, int flags)
        {
            var fd = NativeOpen(path, flags, 0);
            return fd >= 0 ? new((IntPtr)fd, ownsHandle: true) : throw NativeFailure("Open original Notes directory");
        }
        internal static SafeFileHandle OpenRelative(SafeFileHandle parent, string leaf, int flags, uint mode = 0)
        {
            var fd = NativeOpenAt(Fd(parent), leaf, flags, mode);
            return fd >= 0 ? new((IntPtr)fd, ownsHandle: true) : throw NativeFailure("Open original Notes store object");
        }
        private static IOException NativeFailure(string operation) => new(operation + " failed.", new Win32Exception(Marshal.GetLastPInvokeError()));
        public void Dispose() { _notes.Dispose(); _data.Dispose(); }
        [DllImport("libc", EntryPoint = "open", SetLastError = true, CharSet = CharSet.Ansi)] private static extern int NativeOpen(string path, int flags, uint mode);
        [DllImport("libc", EntryPoint = "openat", SetLastError = true, CharSet = CharSet.Ansi)] private static extern int NativeOpenAt(int parent, string path, int flags, uint mode);
        [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true, CharSet = CharSet.Ansi)] private static extern int NativeMkdirAt(int parent, string path, uint mode);
        [DllImport("libc", EntryPoint = "flock", SetLastError = true)] private static extern int NativeFlock(int fd, int operation);
        [DllImport("libc", EntryPoint = "fsync", SetLastError = true)] private static extern int NativeSync(int fd);
        [DllImport("libc", EntryPoint = "geteuid")] private static extern uint NativeGetEffectiveUser();
        [DllImport("libc", EntryPoint = "statx", SetLastError = true, CharSet = CharSet.Ansi)] private static extern int NativeStatAt(int fd, string path, int flags, uint mask, [Out] byte[] buffer);
    }
}
