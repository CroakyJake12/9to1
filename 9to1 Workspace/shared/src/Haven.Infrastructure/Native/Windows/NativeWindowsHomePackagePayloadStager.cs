using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using HavenOS.Home.Apps;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Windows;

/// <summary>
/// Owns only a temporary exact-payload stage. It is not a publisher, installer, receipt
/// issuer, package registry writer or activation authority. Its caller must already own
/// the original platform decision and the held source/destination handles.
/// </summary>
internal sealed class NativeWindowsHomePackagePayloadStage : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly WindowsOriginalRoot _root;
    private readonly SafeFileHandle _parent;
    private readonly string _parentPath, _stagePath;
    private readonly string _parentSecurity, _stageSecurity;
    private readonly WindowsOriginalFileIdentity _parentIdentity, _createdIdentity;
    private readonly SafeFileHandle _writeHandle;
    private WindowsOriginalFileIdentity? _completedIdentity;
    private string? _payloadSha256;
    private long? _payloadBytes;
    private Task? _close;

    internal SafeFileHandle WriteHandle => _writeHandle;

    internal NativeWindowsHomePackagePayloadStage(WindowsOriginalRoot root, SafeFileHandle parent,
        string parentPath, string stagePath, string parentSecurity, string stageSecurity,
        WindowsOriginalFileIdentity parentIdentity, WindowsOriginalFileIdentity createdIdentity,
        SafeFileHandle writeHandle)
    {
        _root = root; _parent = parent; _parentPath = parentPath; _stagePath = stagePath;
        _parentSecurity = parentSecurity; _stageSecurity = stageSecurity;
        _parentIdentity = parentIdentity; _createdIdentity = createdIdentity; _writeHandle = writeHandle;
    }

    internal WindowsOriginalFileIdentity StageIdentity =>
        _completedIdentity ?? throw new InvalidOperationException("The exact package payload stage has not completed.");
    internal long PayloadBytes => _payloadBytes ?? throw new InvalidOperationException("The exact package payload stage has not completed.");
    internal string PayloadSha256 => _payloadSha256 ?? throw new InvalidOperationException("The exact package payload stage has not completed.");
    internal SafeFileHandle OriginalReadHandle
    {
        get
        {
            lock (_gate)
            {
                if (_close is not null || _completedIdentity is null || _writeHandle.IsClosed || _writeHandle.IsInvalid)
                    throw new ObjectDisposedException(nameof(NativeWindowsHomePackagePayloadStage));
                return _writeHandle;
            }
        }
    }

    internal void Complete(WindowsOriginalFileIdentity finalIdentity, long payloadBytes, string payloadSha256)
    {
        lock (_gate)
        {
            if (_close is not null || _completedIdentity is not null)
                throw new InvalidOperationException("The package payload stage can be completed exactly once.");
            _completedIdentity = finalIdentity;
            _payloadBytes = payloadBytes; _payloadSha256 = payloadSha256;
        }
    }

    internal Task AbortAndDrainAsync()
    {
        lock (_gate) return _close ??= AbortAfterStartAsync();
    }

    private async Task AbortAfterStartAsync()
    {
        var failures = new List<Exception>();
        try
        {
            var current = WindowsOriginalFileCustody.ReadIdentity(_writeHandle);
            if (!current.IsRegular || current.Links != 1 || current.Volume != _createdIdentity.Volume ||
                !current.SameFile(_createdIdentity) ||
                _completedIdentity is { } completed && !current.SameReadVersion(completed) ||
                WindowsOriginalFileCustody.ReadSecurity(_writeHandle).Fingerprint != _stageSecurity)
                throw new UnauthorizedAccessException("The stage is no longer the exact owned package-payload file.");
            WindowsOriginalFileCustody.DeleteOwnedStage(_writeHandle, _stagePath, current);
        }
        catch (Exception error) { Add(failures, error); }
        try { _writeHandle.Dispose(); } catch (Exception error) { Add(failures, error); }
        try
        {
            var parent = WindowsOriginalFileCustody.ReadIdentity(_parent);
            if (!parent.IsDirectory || !parent.SameFile(_parentIdentity) ||
                WindowsOriginalFileCustody.ReadSecurity(_parent).Fingerprint != _parentSecurity)
                throw new UnauthorizedAccessException("The original package-payload destination parent changed before stage cleanup.");
            _root.DemandCurrent();
            WindowsOriginalFileCustody.DemandPath(_parent, _parentPath, true);
            WindowsOriginalFileCustody.Flush(_parent);
        }
        catch (Exception error) { Add(failures, error); }
        Throw(failures);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => new(AbortAndDrainAsync());

    private static void Add(List<Exception> failures, Exception error)
    { if (!failures.Any(prior => ReferenceEquals(prior, error))) failures.Add(error); }

    private static void Throw(List<Exception> failures)
    {
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1)
            throw new AggregateException("Package payload stage abort/close failures retained.", failures);
    }
}

/// <summary>
/// Bounded copy/hash/flush primitive over a caller-authorized original payload handle
/// and a caller-authorized held destination directory. The descriptor supplies only
/// expected bytes/hash; it never authorizes a path, download, install or publication.
/// </summary>
internal static class NativeWindowsHomePackagePayloadStager
{
    private const int BufferBytes = 64 * 1024;
    private const long MaximumPayloadBytes = 8L * 1024 * 1024 * 1024;

    /// <summary>
    /// Creates a unique temporary stage under the already held destination parent. The
    /// destination path is only used to verify that parent handle; it is not used to open
    /// the source or to name a final/cache destination. The source handle must already be
    /// opened through the same retained native root by the owning platform callback.
    /// </summary>
    internal static async Task<NativeWindowsHomePackagePayloadStage> StageOriginalPayloadAsync(
        WindowsOriginalRoot sameOriginalRoot, SafeFileHandle sameOriginalPayloadSource,
        string alreadyAuthorizedOriginalPayloadPath, SafeFileHandle sameAuthorizedDestinationParent,
        string authorizedDestinationParentPath,
        HomePackageArtifactDescriptor originalSignedArtifact,
        Func<CancellationToken, Task> demandOriginalCurrent, Action<Task> retainOriginal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sameOriginalRoot);
        ArgumentNullException.ThrowIfNull(sameOriginalPayloadSource);
        ArgumentNullException.ThrowIfNull(sameAuthorizedDestinationParent);
        ArgumentNullException.ThrowIfNull(originalSignedArtifact);
        ArgumentNullException.ThrowIfNull(demandOriginalCurrent);
        ArgumentNullException.ThrowIfNull(retainOriginal);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Original package payload staging requires actual Windows custody.");
        WindowsOriginalFileCustody.RequireCapabilities();
        if (sameOriginalPayloadSource.IsInvalid || sameOriginalPayloadSource.IsClosed ||
            sameAuthorizedDestinationParent.IsInvalid || sameAuthorizedDestinationParent.IsClosed)
            throw new UnauthorizedAccessException("Already-open original payload and destination handles are required.");
        var expectedBytes = originalSignedArtifact.PayloadBytes;
        var expectedSha256 = originalSignedArtifact.PayloadSha256;
        if (expectedBytes is < 1 or > MaximumPayloadBytes || !IsSha256(expectedSha256))
            throw new InvalidDataException("The signed package payload length/hash is invalid or exceeds 8 GiB.");
        var parentPath = WindowsOriginalFileCustody.NormalizeLocalPath(authorizedDestinationParentPath);
        var sourcePath = WindowsOriginalFileCustody.NormalizeLocalPath(alreadyAuthorizedOriginalPayloadPath);

        WindowsOriginalFileIdentity sourceIdentity = default, parentIdentity = default;
        string parentSecurity = string.Empty;
        SafeFileHandle? stageWrite = null;
        NativeWindowsHomePackagePayloadStage? stageOwner = null;
        string? stagePath = null;
        try
        {
            await DemandCurrentAsync(sameOriginalRoot, demandOriginalCurrent, retainOriginal, cancellationToken).ConfigureAwait(false);
            sourceIdentity = WindowsOriginalFileCustody.ReadIdentity(sameOriginalPayloadSource);
            WindowsOriginalFileCustody.DemandPath(sameOriginalPayloadSource, sourcePath, false);
            using (var sameRootSource = sameOriginalRoot.OpenRead(sourcePath))
                if (!WindowsOriginalFileCustody.ReadIdentity(sameRootSource).SameReadVersion(sourceIdentity))
                    throw new UnauthorizedAccessException("The held original payload source is not the exact current file reached through its retained root.");
            parentIdentity = WindowsOriginalFileCustody.ReadIdentity(sameAuthorizedDestinationParent);
            WindowsOriginalFileCustody.DemandPath(sameAuthorizedDestinationParent, parentPath, true);
            if (!sourceIdentity.IsRegular || sourceIdentity.Links != 1 || sourceIdentity.Size != (ulong)expectedBytes ||
                !parentIdentity.IsDirectory || sourceIdentity.Volume != parentIdentity.Volume)
                throw new UnauthorizedAccessException("The actual source/held destination are not exact unaliased same-volume package files.");
            parentSecurity = WindowsOriginalFileCustody.ReadSecurity(sameAuthorizedDestinationParent).Fingerprint;
            using (var sameParent = sameOriginalRoot.OpenDirectory(parentPath, mutable: true))
                if (!WindowsOriginalFileCustody.ReadIdentity(sameParent).SameFile(parentIdentity))
                    throw new UnauthorizedAccessException("The authorized destination path differs from its held parent handle.");
            await DemandStateAsync(sameOriginalRoot, sameOriginalPayloadSource, sourcePath, sourceIdentity,
                sameAuthorizedDestinationParent, parentPath, parentIdentity, parentSecurity,
                stage: null, stagePath: null, stageIdentity: null, expectedStageBytes: 0, stageSecurity: null,
                demandOriginalCurrent, retainOriginal, cancellationToken).ConfigureAwait(false);

            var stageLeaf = ".9to1-home-payload-stage-" + Guid.NewGuid().ToString("N") + ".tmp";
            if (!WindowsOriginalFileCustody.IsSafeLeaf(stageLeaf))
                throw new InvalidDataException("The generated owned stage leaf is unsupported.");
            stagePath = Path.Combine(parentPath, stageLeaf);
            stageWrite = WindowsOriginalFileCustody.OpenRelative(sameAuthorizedDestinationParent, stageLeaf,
                WindowsOriginalFileCustody.ExclusiveStage, 3, WindowsOriginalCreateDisposition.CreateNew,
                WindowsOriginalFileKind.File);
            var createdIdentity = WindowsOriginalFileCustody.ReadIdentity(stageWrite);
            if (!createdIdentity.IsRegular || createdIdentity.Links != 1 || createdIdentity.Size != 0 ||
                createdIdentity.Volume != parentIdentity.Volume)
                throw new UnauthorizedAccessException("The actual unique package payload stage is not a fresh same-volume regular file.");
            WindowsOriginalFileCustody.DemandPath(stageWrite, stagePath, false);
            var stageSecurity = WindowsOriginalFileCustody.ReadSecurity(stageWrite).Fingerprint;
            stageOwner = new(sameOriginalRoot, sameAuthorizedDestinationParent, parentPath, stagePath,
                parentSecurity, stageSecurity, parentIdentity, createdIdentity, stageWrite);
            stageWrite = null; // The one-use stage owner now owns this exact handle.

            using var sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[BufferBytes];
            long copied = 0;
            while (copied < expectedBytes)
            {
                await DemandStateAsync(sameOriginalRoot, sameOriginalPayloadSource, sourcePath, sourceIdentity,
                    sameAuthorizedDestinationParent, parentPath, parentIdentity, parentSecurity,
                    stageOwner.WriteHandle, stagePath, createdIdentity, copied, stageSecurity,
                    demandOriginalCurrent, retainOriginal, cancellationToken).ConfigureAwait(false);
                var length = (int)Math.Min(buffer.Length, expectedBytes - copied);
                var read = await ReadRetainedAsync(sameOriginalPayloadSource, buffer.AsMemory(0, length), copied,
                    retainOriginal, cancellationToken).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("The original payload ended before its signed length.");
                await DemandStateAsync(sameOriginalRoot, sameOriginalPayloadSource, sourcePath, sourceIdentity,
                    sameAuthorizedDestinationParent, parentPath, parentIdentity, parentSecurity,
                    stageOwner.WriteHandle, stagePath, createdIdentity, copied, stageSecurity,
                    demandOriginalCurrent, retainOriginal, cancellationToken).ConfigureAwait(false);
                sourceHash.AppendData(buffer, 0, read);
                await DemandCurrentAsync(sameOriginalRoot, demandOriginalCurrent, retainOriginal, cancellationToken).ConfigureAwait(false);
                await WriteRetainedAsync(stageOwner.WriteHandle, buffer.AsMemory(0, read), copied,
                    retainOriginal, cancellationToken).ConfigureAwait(false);
                copied += read;
                await DemandStateAsync(sameOriginalRoot, sameOriginalPayloadSource, sourcePath, sourceIdentity,
                    sameAuthorizedDestinationParent, parentPath, parentIdentity, parentSecurity,
                    stageOwner.WriteHandle, stagePath, createdIdentity, copied, stageSecurity,
                    demandOriginalCurrent, retainOriginal, cancellationToken).ConfigureAwait(false);
            }

            var extra = new byte[1];
            await DemandStateAsync(sameOriginalRoot, sameOriginalPayloadSource, sourcePath, sourceIdentity,
                sameAuthorizedDestinationParent, parentPath, parentIdentity, parentSecurity,
                stageOwner.WriteHandle, stagePath, createdIdentity, copied, stageSecurity,
                demandOriginalCurrent, retainOriginal, cancellationToken).ConfigureAwait(false);
            if (await ReadRetainedAsync(sameOriginalPayloadSource, extra, expectedBytes,
                retainOriginal, cancellationToken).ConfigureAwait(false) != 0)
                throw new IOException("The original payload exceeds its signed length.");
            var sourceDigest = Convert.ToHexString(sourceHash.GetHashAndReset()).ToLowerInvariant();
            if (copied != expectedBytes || !StringComparer.OrdinalIgnoreCase.Equals(sourceDigest, expectedSha256))
                throw new InvalidDataException("The complete original payload does not match its signed SHA256/length.");

            await DemandCurrentAsync(sameOriginalRoot, demandOriginalCurrent, retainOriginal, cancellationToken).ConfigureAwait(false);
            WindowsOriginalFileCustody.Flush(stageOwner.WriteHandle);
            var finalStageIdentity = WindowsOriginalFileCustody.ReadIdentity(stageOwner.WriteHandle);
            if (!finalStageIdentity.IsRegular || finalStageIdentity.Links != 1 ||
                finalStageIdentity.Size != (ulong)expectedBytes || !finalStageIdentity.SameFile(createdIdentity) ||
                finalStageIdentity.Volume != parentIdentity.Volume)
                throw new IOException("The flushed original payload stage does not have its exact expected file identity/length.");
            WindowsOriginalFileCustody.Flush(sameAuthorizedDestinationParent);

            var readIdentity = WindowsOriginalFileCustody.ReadIdentity(stageOwner.WriteHandle);
            if (!readIdentity.SameReadVersion(finalStageIdentity))
                throw new UnauthorizedAccessException("The staged package payload changed before readback verification.");
            var stagedDigest = await HashStageAsync(sameOriginalRoot, stageOwner.WriteHandle, stagePath, finalStageIdentity,
                sameAuthorizedDestinationParent, parentPath, parentIdentity, parentSecurity,
                demandOriginalCurrent, retainOriginal, cancellationToken).ConfigureAwait(false);
            if (!StringComparer.OrdinalIgnoreCase.Equals(stagedDigest, expectedSha256))
                throw new InvalidDataException("The flushed package stage does not match the signed payload SHA256.");
            await DemandCurrentAsync(sameOriginalRoot, demandOriginalCurrent, retainOriginal, cancellationToken).ConfigureAwait(false);
            stageOwner.Complete(finalStageIdentity, expectedBytes, stagedDigest);
            return stageOwner;
        }
        catch (Exception primary)
        {
            var cleanup = new List<Exception>();
            if (stageOwner is not null)
                try { await stageOwner.AbortAndDrainAsync().ConfigureAwait(false); } catch (Exception error) { Add(cleanup, error); }
            else if (stageWrite is not null)
                try { await DeletePartialStageAsync(sameAuthorizedDestinationParent, stagePath!, stageWrite).ConfigureAwait(false); }
                catch (Exception error) { Add(cleanup, error); }
            Throw(primary, cleanup);
            throw;
        }
    }

    private static async Task DemandStateAsync(WindowsOriginalRoot root, SafeFileHandle source, string sourcePath,
        WindowsOriginalFileIdentity sourceIdentity, SafeFileHandle parent, string parentPath,
        WindowsOriginalFileIdentity parentIdentity, string parentSecurity, SafeFileHandle? stage,
        string? stagePath, WindowsOriginalFileIdentity? stageIdentity, long expectedStageBytes, string? stageSecurity,
        Func<CancellationToken, Task> demandOriginalCurrent, Action<Task> retain, CancellationToken token)
    {
        await DemandCurrentAsync(root, demandOriginalCurrent, retain, token).ConfigureAwait(false);
        var sourceNow = WindowsOriginalFileCustody.ReadIdentity(source);
        var parentNow = WindowsOriginalFileCustody.ReadIdentity(parent);
        if (!sourceIdentity.SameReadVersion(sourceNow) || !sourceNow.IsRegular || sourceNow.Links != 1 ||
            !parentNow.IsDirectory || !parentNow.SameFile(parentIdentity) || parentNow.Volume != sourceNow.Volume ||
            WindowsOriginalFileCustody.ReadSecurity(parent).Fingerprint != parentSecurity)
            throw new UnauthorizedAccessException("The held package payload source/destination identity or ACL changed during staging.");
        WindowsOriginalFileCustody.DemandPath(source, sourcePath, false);
        using (var sameRootSource = root.OpenRead(sourcePath))
            if (!WindowsOriginalFileCustody.ReadIdentity(sameRootSource).SameReadVersion(sourceNow))
                throw new UnauthorizedAccessException("The original payload path no longer resolves through the same held root to the same file version.");
        WindowsOriginalFileCustody.DemandPath(parent, parentPath, true);
        if (stage is not null)
        {
            var stageNow = WindowsOriginalFileCustody.ReadIdentity(stage);
            if (stageIdentity is not { } original || !stageNow.SameFile(original) || !stageNow.IsRegular ||
                stageNow.Links != 1 || stageNow.Volume != parentIdentity.Volume || stageNow.Size != (ulong)expectedStageBytes ||
                WindowsOriginalFileCustody.ReadSecurity(stage).Fingerprint != stageSecurity)
                throw new UnauthorizedAccessException("The exact owned package payload stage changed during copy.");
            WindowsOriginalFileCustody.DemandPath(stage, stagePath!, false);
        }
    }

    private static async Task DemandCurrentAsync(WindowsOriginalRoot root,
        Func<CancellationToken, Task> demandOriginalCurrent, Action<Task> retain, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        root.DemandCurrent();
        await AwaitRetainedAsync(demandOriginalCurrent(token), retain).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        root.DemandCurrent();
    }

    private static async Task<string> HashStageAsync(WindowsOriginalRoot root, SafeFileHandle stage,
        string stagePath, WindowsOriginalFileIdentity expectedStage, SafeFileHandle parent,
        string parentPath, WindowsOriginalFileIdentity parentIdentity, string parentSecurity,
        Func<CancellationToken, Task> demandOriginalCurrent, Action<Task> retain, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferBytes]; long offset = 0;
        while (offset < (long)expectedStage.Size)
        {
            await DemandCurrentAsync(root, demandOriginalCurrent, retain, token).ConfigureAwait(false);
            DemandReadbackIdentities(stage, stagePath, expectedStage, parent, parentPath, parentIdentity, parentSecurity);
            var count = (int)Math.Min(buffer.Length, (long)expectedStage.Size - offset);
            var read = await ReadRetainedAsync(stage, buffer.AsMemory(0, count), offset, retain, token).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("The staged payload ended during exact readback.");
            hash.AppendData(buffer, 0, read); offset += read;
        }
        var extra = new byte[1];
        if (await ReadRetainedAsync(stage, extra, offset, retain, token).ConfigureAwait(false) != 0)
            throw new IOException("The staged payload grew during readback.");
        await DemandCurrentAsync(root, demandOriginalCurrent, retain, token).ConfigureAwait(false);
        DemandReadbackIdentities(stage, stagePath, expectedStage, parent, parentPath, parentIdentity, parentSecurity);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void DemandReadbackIdentities(SafeFileHandle stage, string stagePath,
        WindowsOriginalFileIdentity expectedStage, SafeFileHandle parent, string parentPath,
        WindowsOriginalFileIdentity parentIdentity, string parentSecurity)
    {
        var stageNow = WindowsOriginalFileCustody.ReadIdentity(stage);
        var parentNow = WindowsOriginalFileCustody.ReadIdentity(parent);
        if (!stageNow.SameReadVersion(expectedStage) || !stageNow.IsRegular || stageNow.Links != 1 ||
            !parentNow.IsDirectory || !parentNow.SameFile(parentIdentity) ||
            WindowsOriginalFileCustody.ReadSecurity(parent).Fingerprint != parentSecurity)
            throw new UnauthorizedAccessException("The staged payload or destination identity changed during readback.");
        WindowsOriginalFileCustody.DemandPath(stage, stagePath, false);
        WindowsOriginalFileCustody.DemandPath(parent, parentPath, true);
    }

    private static async Task<int> ReadRetainedAsync(SafeFileHandle handle, Memory<byte> buffer,
        long offset, Action<Task> retain, CancellationToken token) =>
        await AwaitRetainedAsync(RandomAccess.ReadAsync(handle, buffer, offset, token).AsTask(), retain).ConfigureAwait(false);

    private static Task WriteRetainedAsync(SafeFileHandle handle, ReadOnlyMemory<byte> buffer,
        long offset, Action<Task> retain, CancellationToken token) =>
        AwaitRetainedAsync(RandomAccess.WriteAsync(handle, buffer, offset, token).AsTask(), retain);

    private static async Task<T> AwaitRetainedAsync<T>(Task<T> original, Action<Task> retain)
    {
        var failures = new List<Exception>();
        try { retain(original); } catch (Exception error) { Add(failures, error); }
        T result = default!;
        try { result = await original.ConfigureAwait(false); } catch (Exception error) { Add(failures, error); }
        Throw(failures);
        return result;
    }

    private static async Task AwaitRetainedAsync(Task original, Action<Task> retain)
    {
        var failures = new List<Exception>();
        try { retain(original); } catch (Exception error) { Add(failures, error); }
        try { await original.ConfigureAwait(false); } catch (Exception error) { Add(failures, error); }
        Throw(failures);
    }

    private static async Task DeletePartialStageAsync(SafeFileHandle parent, string path, SafeFileHandle stage)
    {
        var errors = new List<Exception>();
        try
        {
            var identity = WindowsOriginalFileCustody.ReadIdentity(stage);
            if (!identity.IsRegular || identity.Links != 1)
                throw new UnauthorizedAccessException("Refusing to clean a substituted package stage.");
            WindowsOriginalFileCustody.DeleteOwnedStage(stage, path, identity);
        }
        catch (Exception error) { Add(errors, error); }
        try { stage.Dispose(); } catch (Exception error) { Add(errors, error); }
        try { WindowsOriginalFileCustody.Flush(parent); } catch (Exception error) { Add(errors, error); }
        Throw(errors);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static void Add(List<Exception> failures, Exception error)
    { if (!failures.Any(prior => ReferenceEquals(prior, error))) failures.Add(error); }
    private static void Throw(List<Exception> failures)
    {
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Original package payload staging failures retained.", failures);
    }
    private static void Throw(Exception primary, List<Exception> cleanup)
    {
        if (cleanup.Count == 0) ExceptionDispatchInfo.Capture(primary).Throw();
        throw new AggregateException("Original payload staging and independent cleanup failures retained.",
            new[] { primary }.Concat(cleanup));
    }
}
