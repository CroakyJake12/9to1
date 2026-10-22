using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

/// <summary>Original Notes store metadata under the SAME repository semaphore and physical
/// .repository.lock used by ordinary Notes transactions. No content authority is minted here.
/// The first physical implementation is Linux; unsupported systems fail explicitly.</summary>
public sealed partial class NotesRepository : INotesOriginalStoreSource
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
        Guid? documentId) : INotesOriginalLibraryReadRequest
    {
        internal NotesRepository Issuer { get; } = issuer;
        public Guid OperationId { get; } = Guid.NewGuid();
        public Guid? DocumentId { get; } = documentId;
        public INotesOriginalStoreObservation StoreObservation { get; } = observation;
        public JsonElement Arguments => JsonSerializer.SerializeToElement(new { operationId = OperationId,
            storeId = observation.StoreIdentity.StoreId, storeRevision = observation.Revision, documentId = DocumentId });
        internal int ExecutionStarted;
        internal OriginalLibraryReadOutcome? Outcome;
    }

    public async ValueTask<INotesOriginalLibraryReadRequest> PrepareOriginalLibraryReadAsync(Guid? documentId,
        CancellationToken cancellationToken = default)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("The requested document ID cannot be empty.", nameof(documentId));
        var observed = await ObserveOriginalStoreCoreAsync(initialise: false, cancellationToken).ConfigureAwait(false);
        return new OriginalLibraryReadRequest(this, observed, documentId);
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

    private async Task<OriginalObservation> ObserveOriginalStoreCoreAsync(bool initialise, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux() || !BitConverter.IsLittleEndian ||
            RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("The original Notes physical identity boundary requires the supported Linux backend.");
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            // Bind the constructor-captured canonical Notes root, never a later mutable IAppPaths value.
            var originalDataRoot = Path.GetDirectoryName(Path.GetDirectoryName(_root)!)!;
            using var root = OriginalNotesRoot.Open(originalDataRoot, initialise);
            await using var lease = await root.AcquireCanonicalRepositoryLockAsync(token).ConfigureAwait(false);
            root.DemandCurrent();
            if (_originalStoreBaseline is { } baseline &&
                (!SameNode(root.DataIdentity, baseline.DataRoot) || !SameNode(root.NotesIdentity, baseline.NotesRoot) ||
                 !SameNode(root.CanonicalLockIdentity!, baseline.CanonicalLock)))
                throw new IOException("The original Notes directory identity changed; preserve the store for recovery.");
            var empty = root.IsContentEmpty();
            using var file = root.OpenIdentity(initialise && _originalStoreBaseline is null, out var created);
            if (created)
            {
                var identity = new OriginalIdentityDocument(1, Guid.NewGuid(), DateTimeOffset.UtcNow);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(identity, JsonOptions);
                await file.WriteAsync(bytes, token).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
                root.Flush();
                _identityCreatedInThisOwner = identity.StoreId;
                _identityCreatedOnEmptyRoot = empty;
            }
            var before = OriginalNotesRoot.Stat(file.SafeFileHandle);
            OriginalNotesRoot.DemandPrivateRegular(before);
            if (before.Size is 0 or > 4096) throw new InvalidDataException("The Notes store identity has an invalid size.");
            file.Position = 0;
            var buffer = new byte[checked((int)before.Size)];
            await file.ReadExactlyAsync(buffer, token).ConfigureAwait(false);
            var after = OriginalNotesRoot.Stat(file.SafeFileHandle);
            if (before != after || root.ReadIdentityVersion() != after)
                throw new IOException("The original Notes identity changed while it was read.");
            root.DemandCurrent();
            OriginalIdentityDocument stored;
            try
            {
                using var json = JsonDocument.Parse(buffer);
                if (json.RootElement.ValueKind != JsonValueKind.Object)
                    throw new JsonException("The store identity must be an object.");
                var names = json.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
                if (names.Length != 3 || names.Distinct(StringComparer.Ordinal).Count() != 3 ||
                    !names.Order(StringComparer.Ordinal).SequenceEqual(new[] { "createdAtUtc", "schemaVersion", "storeId" }))
                    throw new JsonException("Unknown or repeated store identity field.");
                stored = json.RootElement.Deserialize<OriginalIdentityDocument>(JsonOptions)
                    ?? throw new JsonException("No store identity.");
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
                fingerprint, empty, root.DataIdentity, root.NotesIdentity, after, root.CanonicalLockIdentity!);
            _originalStoreBaseline ??= observation;
            return observation;
        }
        finally { _gate.Release(); }
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
        internal async Task<FileStream> AcquireCanonicalRepositoryLockAsync(CancellationToken token)
        {
            // flock is the same physical lock used by FileShare.None in the existing Unix
            // Notes transaction. No second lock file or independent storage authority.
            var timer = Stopwatch.StartNew();
            while (true)
            {
                token.ThrowIfCancellationRequested();
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
                        CanonicalLockIdentity = identity;
                        DemandCurrent();
                        return new FileStream(handle, FileAccess.ReadWrite, 1, isAsync: false);
                    }
                    if (Marshal.GetLastPInvokeError() != 11) throw NativeFailure("Acquire original Notes lock");
                }
                catch { handle.Dispose(); throw; }
                handle.Dispose();
                if (timer.Elapsed >= RepositoryLockTimeout) throw new IOException("Timed out acquiring the original Notes repository lock.");
                await Task.Delay(25, token).ConfigureAwait(false);
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
