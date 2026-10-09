using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NineToOne.Dulche.Den;

public sealed partial class DenStore
{
    private readonly List<OwnershipObservationInvocation> _ownershipObservations = [];
    private readonly AsyncLocal<OwnershipObservationInvocation?> _ownershipExecuting = new();

    private sealed class OwnershipObservationInvocation(AuthorityReadSources sources)
    {
        internal readonly AuthorityReadSources Sources = sources;
        internal Task<DenOwnershipObservation> Driver = null!;
        internal Task? ProcessGateWait;
        internal bool ProcessGateEntered;
        internal bool ProcessGateReleased;
        internal bool GateReleaseAttempted;
        internal Task? OriginalGateRelease;
        internal FileStream? WriterLock;
        internal OwnershipResource? WriterResource;
        internal readonly List<OwnershipResource> Resources = [];
        internal bool IsHealthy => Driver.IsCompletedSuccessfully && Sources.IsHealthy &&
            (!ProcessGateEntered || ProcessGateReleased) && Resources.All(value => value.IsHealthy);
    }

    // Actual resource identity survives a failed encompassing async driver. Cleanup is
    // attempted once; its original receipt remains inspectable even after a scope fails.
    private sealed class OwnershipResource(object original)
    {
        internal readonly object Original = original;
        internal bool CleanupAttempted;
        internal Task? OriginalClose;
        internal bool CloseJoined;
        internal bool IsHealthy => CloseJoined && OriginalClose?.IsCompletedSuccessfully == true;
    }

    /// <summary>SAME canonical writer lease, refreshed manifest and bounded content
    /// fingerprint as ObserveOwnershipAsync. This is evidence, never an access grant.
    /// Every actual raw stage and resource is captured before borrowed post-checks.</summary>
    public Task<DenOwnershipObservation> ObserveOwnershipWithinOriginalSourceAsync(
        Action<Action> scope, Action<Task> retain, Action<Action> cleanupScope,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(retain);
        ArgumentNullException.ThrowIfNull(cleanupScope);
        OwnershipObservationInvocation invocation; TaskCompletionSource start;
        lock (_authorityReadGate)
        {
            ObjectDisposedException.ThrowIf(_disposed || _originalAuthorityDispose is not null, this);
            _ownershipObservations.RemoveAll(value => value.IsHealthy);
            if (_ownershipObservations.Count >= 128)
                throw new InvalidOperationException("Settle retained original Den ownership observations before another acquisition.");
            invocation = new(new AuthorityReadSources(body => InvokeAuthorityPhysical(() => scope(body)), retain));
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            invocation.Driver = ObserveOwnershipOriginalAfterStart(invocation, start.Task, cleanupScope, token);
            _ownershipObservations.Add(invocation);
        }
        try { InvokeAuthorityPhysical(() => retain(invocation.Driver)); }
        catch (Exception cause) { invocation.Sources.Add(cause); }
        finally { start.SetResult(); }
        return invocation.Driver;
    }

    private async Task<DenOwnershipObservation> ObserveOwnershipOriginalAfterStart(
        OwnershipObservationInvocation invocation, Task start, Action<Action> cleanupScope, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        var prior = _ownershipExecuting.Value; _ownershipExecuting.Value = invocation;
        try { return await ObserveOwnershipOriginalBody(invocation, cleanupScope, token).ConfigureAwait(false); }
        finally { _ownershipExecuting.Value = prior; }
    }

    private async Task<DenOwnershipObservation> ObserveOwnershipOriginalBody(
        OwnershipObservationInvocation invocation, Action<Action> cleanupScope, CancellationToken token)
    {
        var originals = invocation.Sources;
        DenOwnershipObservation? observation = null; Exception? primary = null;
        try
        {
            originals.Invoke(() =>
            {
                // Admission already sealed retirement under the store gate. An accepted
                // observer may finish its original lease after store retirement starts.
                if (_readOnly || CompareVersion(Manifest.MinimumWriterVersion, CurrentVersion) > 0)
                    throw new DenException(DenErrorCode.UnsupportedSchema,
                        "This Den is newer than this reader and is open read-only.", recoverable: true);
                return true;
            });
            await EnterOriginalOwnershipLease(invocation, token).ConfigureAwait(false);
            await RefreshOriginalOwnershipManifest(invocation, cleanupScope, token).ConfigureAwait(false);
            OwnershipResource? directoryEnumerator = null;
            try
            {
                var entries = originals.Invoke(() =>
                {
                    var actual = Directory.EnumerateDirectories(Path.Combine(_root, "transactions")).GetEnumerator();
                    directoryEnumerator = CaptureOwnershipResource(invocation, actual);
                    return actual;
                });
                if (originals.Invoke(entries.MoveNext))
                    throw new DenException(DenErrorCode.StorageFailure,
                        "Reopen interrupted transactions before observing ownership.", recoverable: true);
            }
            catch (Exception cause) { originals.Add(cause); throw; }
            finally
            {
                if (directoryEnumerator is not null)
                    await CloseOwnershipResource(invocation, directoryEnumerator, cleanupScope).ConfigureAwait(false);
            }
            OwnershipResource? hashResource = null;
            try
            {
                var hash = originals.Invoke(() =>
                {
                    var actual = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    hashResource = CaptureOwnershipResource(invocation, actual);
                    return actual;
                });
                var pending = new Stack<string>(); pending.Push(_root);
                var files = new List<string>(); var observedEntries = 0;
                while (pending.TryPop(out var directory))
                {
                    var ordered = originals.Invoke(() =>
                    {
                        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                            throw new DenException(DenErrorCode.InvalidManifest,
                                "Den ownership evidence cannot follow linked storage.");
                        // Materialize the SAME ordinal enumeration within the actual
                        // physical source; no lazy filesystem iterator escapes custody.
                        return Directory.GetFileSystemEntries(directory).Order(StringComparer.Ordinal).ToArray();
                    });
                    foreach (var entry in ordered)
                        originals.Invoke(() =>
                        {
                            var attributes = File.GetAttributes(entry);
                            if ((attributes & FileAttributes.ReparsePoint) != 0)
                                throw new DenException(DenErrorCode.InvalidManifest,
                                    "Den ownership evidence cannot follow linked storage.");
                            if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                            else if (entry != _lockPath) files.Add(entry);
                            if (++observedEntries > 100000)
                                throw new DenException(DenErrorCode.CapabilityUnavailable,
                                    "The Den exceeds the supported ownership observation entry limit.");
                            return true;
                        });
                }
                long total = 0;
                foreach (var path in files.Order(StringComparer.Ordinal))
                {
                    OwnershipResource? streamResource = null;
                    try
                    {
                        var stream = originals.Invoke(() =>
                        {
                            token.ThrowIfCancellationRequested();
                            var actual = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                            streamResource = CaptureOwnershipResource(invocation, actual);
                            total = checked(total + actual.Length);
                            if (total > 1024L * 1024 * 1024)
                                throw new DenException(DenErrorCode.CapabilityUnavailable,
                                    "The Den exceeds the supported ownership observation byte limit.");
                            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(_root, path)
                                .Replace(Path.DirectorySeparatorChar, '/') + "\0" +
                                actual.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\0"));
                            return actual;
                        });
                        var digest = await originals.Read(() => SHA256.HashDataAsync(stream, token).AsTask()).ConfigureAwait(false);
                        originals.Invoke(() => { hash.AppendData(digest); return true; });
                    }
                    catch (Exception cause) { originals.Add(cause); throw; }
                    finally
                    {
                        if (streamResource is not null)
                            await CloseOwnershipResource(invocation, streamResource, cleanupScope).ConfigureAwait(false);
                    }
                }
                observation = originals.Invoke(() => new DenOwnershipObservation(Manifest.DenId,
                    Convert.ToHexString(hash.GetHashAndReset()), files.Count == 1 && files[0] == _manifestPath));
            }
            catch (Exception cause) { originals.Add(cause); throw; }
            finally
            {
                if (hashResource is not null)
                    await CloseOwnershipResource(invocation, hashResource, cleanupScope).ConfigureAwait(false);
            }
        }
        catch (Exception cause) { primary = cause; originals.Add(cause); }
        finally
        {
            // Join every acquired resource independently; an earlier failed close never
            // suppresses another cleanup. Already attempted closes are never replayed.
            foreach (var resource in invocation.Resources.ToArray())
            {
                if (ReferenceEquals(resource, invocation.WriterResource)) continue;
                try { await CloseOwnershipResource(invocation, resource, cleanupScope).ConfigureAwait(false); }
                catch (Exception cause) { originals.Add(cause); }
            }
            if (invocation.WriterResource is { } writer)
                try { await CloseOwnershipResource(invocation, writer, cleanupScope).ConfigureAwait(false); }
                catch (Exception cause) { originals.Add(cause); }
            if (invocation.ProcessGateEntered && !invocation.ProcessGateReleased &&
                (invocation.WriterResource is null || invocation.WriterResource.IsHealthy))
            {
                try
                {
                    invocation.GateReleaseAttempted = true;
                    originals.InvokeCleanup(body => InvokeAuthorityPhysical(() => cleanupScope(body)), () =>
                    {
                        var receipt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        invocation.OriginalGateRelease = receipt.Task;
                        Exception? publication = null;
                        try { originals.Retain(receipt.Task); } catch (Exception cause) { publication = cause; originals.Add(cause); }
                        try
                        {
                            _heldFileLock = null;
                            _processGate.Release(); invocation.ProcessGateReleased = true; receipt.SetResult();
                        }
                        catch (Exception cause) { receipt.SetException(cause); throw; }
                        if (publication is not null) ExceptionDispatchInfo.Capture(publication).Throw();
                    });
                }
                catch (Exception cause) { originals.Add(cause); }
            }
            if (invocation.OriginalGateRelease is { } release)
                try { await originals.Join(release).ConfigureAwait(false); }
                catch (Exception cause) { originals.Add(cause); }
            if (invocation.ProcessGateEntered && !invocation.ProcessGateReleased)
                originals.Add(new InvalidOperationException("The actual Den ownership writer lease has no acknowledged release."));
        }
        await originals.Settle(primary).ConfigureAwait(false);
        return observation ?? throw new InvalidOperationException("No actual Den ownership observation was returned.");
    }

    private async Task EnterOriginalOwnershipLease(OwnershipObservationInvocation invocation, CancellationToken token)
    {
        var originals = invocation.Sources; Exception? invocationFailure = null;
        try
        {
            originals.Invoke(() =>
            {
                invocation.ProcessGateWait = _processGate.WaitAsync(token);
                originals.Retain(invocation.ProcessGateWait); return true;
            });
        }
        catch (Exception cause) { invocationFailure = cause; }
        if (invocation.ProcessGateWait is { } wait)
        {
            try { await originals.Join(wait).ConfigureAwait(false); }
            finally { if (wait.IsCompletedSuccessfully) invocation.ProcessGateEntered = true; }
        }
        if (invocationFailure is not null) ExceptionDispatchInfo.Capture(invocationFailure).Throw();
        if (!invocation.ProcessGateEntered) throw new InvalidOperationException("No actual Den writer gate was acquired.");
        var until = originals.Invoke(() => DateTime.UtcNow + LockTimeout);
        while (true)
        {
            var acquired = originals.Invoke(() =>
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var actual = new FileStream(_lockPath,
                        PrivateFileOptions(FileMode.OpenOrCreate, FileAccess.ReadWrite, FileOptions.None));
                    invocation.WriterLock = _heldFileLock = actual;
                    invocation.WriterResource = CaptureOwnershipResource(invocation, actual);
                    return true;
                }
                catch (IOException) when (DateTime.UtcNow < until) { return false; }
                catch (IOException)
                {
                    throw new DenException(DenErrorCode.StorageFailure,
                        "Timed out waiting for the Den writer lock.", recoverable: true, retryable: true);
                }
            });
            if (acquired) return;
            await ReadOwnershipVoidStage(originals, () => Task.Delay(40, token)).ConfigureAwait(false);
        }
    }

    private static async Task ReadOwnershipVoidStage(AuthorityReadSources originals, Func<Task> factory)
    {
        Task? raw = null; Exception? invoked = null;
        try { originals.Invoke(() => { raw = factory(); originals.Retain(raw); return true; }); }
        catch (Exception cause) { invoked = cause; originals.Add(cause); }
        if (raw is not null)
            try { await originals.Join(raw).ConfigureAwait(false); }
            catch (Exception cause) { originals.Add(cause); }
        if (invoked is not null) ExceptionDispatchInfo.Capture(invoked).Throw();
        if (raw is null) throw new InvalidOperationException("No actual Den ownership raw stage was acquired.");
        if (!raw.IsCompletedSuccessfully)
        {
            // Await the SAME raw task again only to preserve its real status/cause;
            // no source factory or business stage is replayed.
            await originals.Join(raw).ConfigureAwait(false);
        }
    }

    private async Task RefreshOriginalOwnershipManifest(OwnershipObservationInvocation invocation,
        Action<Action> cleanupScope, CancellationToken token)
    {
        var originals = invocation.Sources; OwnershipResource? resource = null;
        try
        {
            var stream = originals.Invoke(() =>
            {
                var actual = new FileStream(_manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                resource = CaptureOwnershipResource(invocation, actual);
                if (actual.Length > 2 * 1024 * 1024)
                    throw new DenException(DenErrorCode.InvalidManifest, "The Den manifest exceeds the supported size.");
                return actual;
            });
            DenManifest current;
            try
            {
                current = await originals.Read(() => JsonSerializer.DeserializeAsync<DenManifest>(stream, DenJson.Options, token).AsTask()).ConfigureAwait(false)
                    ?? throw new DenException(DenErrorCode.InvalidManifest, "The Den manifest is empty.");
            }
            catch (JsonException cause)
            {
                throw new DenException(DenErrorCode.InvalidManifest, "The Den manifest is invalid: " + cause.Message);
            }
            originals.Invoke(() =>
            {
                ValidateManifest(current, _root);
                if (current.DenId != Manifest.DenId || current.SchemaVersion != Manifest.SchemaVersion ||
                    CompareVersion(current.MinimumReaderVersion, CurrentVersion) > 0 ||
                    CompareVersion(current.MinimumWriterVersion, CurrentVersion) > 0)
                    throw new DenException(DenErrorCode.Conflict,
                        "The canonical Den identity or schema changed; reopen for recovery.", recoverable: true);
                Manifest = current; return true;
            });
        }
        catch (Exception cause) { originals.Add(cause); throw; }
        finally
        {
            if (resource is not null)
                await CloseOwnershipResource(invocation, resource, cleanupScope).ConfigureAwait(false);
        }
    }

    private static OwnershipResource CaptureOwnershipResource(OwnershipObservationInvocation invocation, object original)
    {
        var resource = new OwnershipResource(original); invocation.Resources.Add(resource); return resource;
    }

    private async Task CloseOwnershipResource(OwnershipObservationInvocation invocation,
        OwnershipResource resource, Action<Action> cleanupScope)
    {
        var originals = invocation.Sources;
        if (!resource.CleanupAttempted)
        {
            resource.CleanupAttempted = true;
            try
            {
                originals.InvokeCleanup(body => InvokeAuthorityPhysical(() => cleanupScope(body)), () =>
                {
                    if (!ReferenceEquals(resource, invocation.WriterResource) && resource.Original is IAsyncDisposable asynchronous)
                    {
                        resource.OriginalClose = asynchronous.DisposeAsync().AsTask();
                        originals.Retain(resource.OriginalClose);
                    }
                    else if (resource.Original is IDisposable synchronous)
                    {
                        var receipt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        resource.OriginalClose = receipt.Task;
                        Exception? publication = null;
                        try { originals.Retain(receipt.Task); } catch (Exception cause) { publication = cause; originals.Add(cause); }
                        try { synchronous.Dispose(); receipt.SetResult(); }
                        catch (Exception cause) { receipt.SetException(cause); throw; }
                        if (publication is not null) ExceptionDispatchInfo.Capture(publication).Throw();
                    }
                    else throw new InvalidOperationException("The actual Den ownership resource has no original cleanup source.");
                });
            }
            catch (Exception cause) { originals.Add(cause); }
        }
        if (resource.OriginalClose is { } close)
        {
            try { await originals.Join(close).ConfigureAwait(false); }
            finally { resource.CloseJoined = true; }
        }
        else throw new InvalidOperationException("The actual Den ownership resource has no acknowledged original close.");
    }

    private static async Task JoinOriginalOwnershipObservations(
        OwnershipObservationInvocation[] observations, List<Exception> failures)
    {
        foreach (var invocation in observations)
        {
            try { await invocation.Driver.ConfigureAwait(false); }
            catch (Exception cause) { failures.Add(invocation.Driver.Exception ?? cause); }
            foreach (var resource in invocation.Resources)
            {
                if (resource.OriginalClose is { } close)
                    try { await close.ConfigureAwait(false); }
                    catch (Exception cause) { failures.Add(close.Exception ?? cause); }
                else failures.Add(new InvalidOperationException("An actual retained Den ownership resource has no original cleanup receipt."));
            }
            if (invocation.Driver.IsCompletedSuccessfully && !invocation.IsHealthy)
                failures.Add(new InvalidOperationException("The actual Den ownership observation has no full healthy source/lease proof."));
        }
    }
}
