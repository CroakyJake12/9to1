using Haven.Application;
using HavenOS.Home.Apps;

namespace Haven.Infrastructure.Native.Windows;

public sealed partial class NativeWindowsHomeInstalledRootAdmission
{
    private readonly Dictionary<OriginalActivationRead, Invocation> _activationReads = new(ReferenceEqualityComparer.Instance);
    /// <summary>Actual source-issued protected read. Its descriptor/rows are
    /// observations; the retained original native cohort and current Root admission
    /// establish this read's authority. It performs no installer/package mutation.</summary>
    public sealed class OriginalActivationRead
    {
        internal readonly NativeWindowsHomeInstalledRootAdmission Owner;
        internal readonly Invocation Source;
        internal readonly Observation Admission;
        internal readonly InstalledPackage Package;
        internal Task? Close;
        internal readonly List<Invocation> Currentness = [];
        internal OriginalActivationRead(NativeWindowsHomeInstalledRootAdmission owner, Invocation source,
            Observation admission, InstalledPackage package)
        { Owner = owner; Source = source; Admission = admission; Package = package; }
        public HomePackageArtifactDescriptor Descriptor => Package.Descriptor;
        public HomePackageOriginalArtifactObservation OriginalArtifact => Package.SignedArtifact;
        public HomePackageOriginalInstalledActivationRecord Activation => Package.Activation;
        public HomePackageDatabaseEntry OriginalPackage => Package.Entry;
        public Task? OriginalClose { get { lock (Owner._gate) return Close; } }
        public void DemandExternalOriginalJoin()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(Owner);
            CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        }
        public Task CloseAndDrainOriginalAsync() => Owner.CloseOriginalActivationReadAsync(this);
    }
    public Task<OriginalActivationRead> AcquireOriginalActivationReadWithinSourceAsync(Observation sameAdmission, string packageId,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var sources = new CloudflareOriginalTaskLedger(); sources.BindOriginalOwner(this);
        sources.BindOriginalCallerCallback(body => WithinSource(sources, scope, body));
        var work = new Invocation(sources, retain); var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            _originals.RemoveAll(old => old.Healthy);
            if (_originals.Count >= 128 || _activationReads.Count >= 128)
                throw new InvalidOperationException("Unresolved original Root activation reads remain retained.");
            work.Driver = ReadPublished(begin.Task); _originals.Add(work); _originalReceipts.Add(work.Driver, work);
        }
        try { sources.Invoke(() => { retain(work.Driver); return true; }); } catch (Exception cause) { sources.Retain(cause); }
        finally { begin.SetResult(); }
        return (Task<OriginalActivationRead>)work.Driver;
        async Task<OriginalActivationRead> ReadPublished(Task start)
        {
            await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            OriginalActivationRead? result = null;
            try
            {
                sources.Invoke(() =>
                {
                    if (!IsIssuedOriginalAdmission(sameAdmission) || _connectedRoot is not null || _serviceContext is null ||
                        string.IsNullOrWhiteSpace(packageId) || packageId.Length > 256 ||
                        !sameAdmission.Publisher.SignedProcess.Descriptors.ContainsKey(packageId))
                        throw new UnauthorizedAccessException("The actual registered Root's exact signed catalogue admission is required.");
                    return true;
                });
                var current = await Read(work, () => InspectOriginalRuntimeWithinSourceAsync(
                    body => sources.Invoke(() => { body(); return true; }), raw => Keep(work, raw), token)).ConfigureAwait(false);
                if (current is null || !IsIssuedOriginalAdmission(current) || current.Actor != sameAdmission.Actor ||
                    current.Publisher.CatalogueSha256 != sameAdmission.Publisher.CatalogueSha256 ||
                    !SameOriginalPackage(current.Root, sameAdmission.Root) || !SameOriginalPackage(current.Home, sameAdmission.Home))
                    throw new UnauthorizedAccessException("The protected original Root/Home/current user activation changed.");
                var descriptor = sameAdmission.Publisher.SignedProcess.Descriptors[packageId];
                var package = await ReadPackage(work, current.Registry, current.Publisher, packageId, descriptor.AppId, token).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("The selected exact signed package has no complete original installed activation.");
                sources.Invoke(() =>
                {
                    DemandNativeCurrent(work); result = new(this, work, current, package);
                    lock (_gate)
                    {
                        ObjectDisposedException.ThrowIf(_retiring, this);
                        _activationReads.Add(result, work);
                    }
                    return true;
                });
            }
            catch (Exception cause) { sources.Retain(cause); }
            await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (sources.OriginalErrors.Count != 0)
            {
                await CloseResources(work).ConfigureAwait(false);
                throw new AggregateException("Original activation read failed; actual native objects and causes remain retained.", sources.OriginalErrors);
            }
            return result!;
        }
    }
    public Task DemandOriginalActivationReadCurrentWithinSourceAsync(OriginalActivationRead same,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var sources = new CloudflareOriginalTaskLedger(); sources.BindOriginalOwner(same);
        sources.BindOriginalCallerCallback(body => WithinSource(sources, scope, body));
        var work = new Invocation(sources, retain);
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (!ReferenceEquals(same.Owner, this) || !_activationReads.TryGetValue(same, out var original) ||
                !ReferenceEquals(original, same.Source) || same.Close is not null || !original.Driver.IsCompletedSuccessfully)
                throw new UnauthorizedAccessException("The SAME actual unclosed native Root activation read is required.");
            if (same.Currentness.Count >= 4096) throw new InvalidOperationException("Unresolved Root read currentness originals remain retained.");
            // Publish the complete admitted validator while the read admission gate is
            // still held. Close joins these exact drivers before touching native pins.
            work.Driver = ValidatePublished(begin.Task); same.Currentness.Add(work);
        }
        try { sources.Invoke(() => { retain(work.Driver); return true; }); }
        catch (Exception cause) { sources.Retain(cause); }
        finally { begin.SetResult(); }
        return work.Driver;
        async Task ValidatePublished(Task start)
        {
            await start.ConfigureAwait(false);
            using var owner = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            using var read = CloudflareOriginalExecutionGuard.EnterOriginal(same);
            try
            {
                // This validator uses its own captured caller lineage, rather than a
                // completed acquisition command's callback. Accepted validation remains
                // admitted if close seals the read while this body is already running.
                sources.Invoke(() => { token.ThrowIfCancellationRequested(); DemandNativeCurrent(same.Source); return true; });
                await Read(work, async () =>
                {
                    await DemandOriginalCurrentWithinSourceAsync(same.Admission,
                        body => sources.Invoke(() => { body(); return true; }), raw => Keep(work, raw), token).ConfigureAwait(false);
                    return true;
                }).ConfigureAwait(false);
                sources.Invoke(() => { DemandNativeCurrent(same.Source); return true; });
            }
            catch (Exception cause) { sources.Retain(cause); }
            await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (sources.OriginalErrors.Count != 0)
                throw new AggregateException("The accepted original Root read validation failed and remains retained.", sources.OriginalErrors);
        }
    }
    private Task CloseOriginalActivationReadAsync(OriginalActivationRead same)
    {
        same.DemandExternalOriginalJoin(); return CloseOriginalActivationReadOwnedAsync(same);
    }
    private Task CloseOriginalActivationReadOwnedAsync(OriginalActivationRead same)
    {
        TaskCompletionSource? begin = null; Task actual;
        lock (_gate)
        {
            if (!ReferenceEquals(same.Owner, this) || !_activationReads.TryGetValue(same, out var source) || !ReferenceEquals(source, same.Source))
                throw new UnauthorizedAccessException("The source-issued original activation read is required.");
            if (same.Close is null) { begin = new(TaskCreationOptions.RunContinuationsAsynchronously); same.Close = ClosePublished(begin.Task); }
            actual = same.Close;
        }
        begin?.SetResult(); return actual;
        async Task ClosePublished(Task start)
        {
            await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            try { await same.Source.Driver.ConfigureAwait(false); } catch (Exception cause) { same.Source.Sources.Capture(same.Source.Driver, cause); }
            Invocation[] checks; lock (_gate) checks = same.Currentness.ToArray();
            foreach (var check in checks)
            {
                try { await check.Driver.ConfigureAwait(false); }
                catch (Exception cause) { same.Source.Sources.Capture(check.Driver, cause); }
                await check.Sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
                foreach (var cause in check.Sources.OriginalErrors) same.Source.Sources.Retain(cause);
            }
            await same.Source.Sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            await CloseResources(same.Source).ConfigureAwait(false);
            if (same.Source.Sources.OriginalErrors.Count != 0)
                throw new AggregateException("Original activation read/cleanup remains unconfirmed.", same.Source.Sources.OriginalErrors);
            lock (_gate) _activationReads.Remove(same); // Only independently healthy full close retires strong custody.
        }
    }
}
