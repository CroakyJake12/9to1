using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;

namespace Haven.Infrastructure.Native.Windows;

public sealed partial class NativeWindowsHomeInstalledRootAdmission
{
    private readonly ConditionalWeakTable<Invocation, OriginalHomeLaunchCohort> _launchCohorts = new();
    /// <summary>Opaque native custody of the complete signed Home payload. The
    /// actual child's original exit is bound before any resume; pins cannot be
    /// released while that accepted child remains alive or its exit is unknown.</summary>
    public sealed class OriginalHomeLaunchCohort
    {
        private readonly NativeWindowsHomeInstalledRootAdmission _issuer;
        private readonly Invocation _source;
        internal readonly Observation Admission;
        internal readonly InstalledPackage Package;
        private NativeWindowsHomeOriginalControlledProcess? _child;
        private Task? _exit, _close;
        internal OriginalHomeLaunchCohort(NativeWindowsHomeInstalledRootAdmission issuer, Invocation source,
            Observation admission, InstalledPackage package)
        { _issuer = issuer; _source = source; Admission = admission; Package = package; }
        public string ProtectedEntrypoint => Package.Entrypoint;
        public string ProtectedWorkingDirectory => Package.Activation.InstallationRoot;
        public Task? OriginalClose { get { lock (_issuer._gate) return _close; } }
        internal void BindOriginalChildLifetime(NativeWindowsHomeOriginalControlledProcess actual, Task sameActualExit)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows controlled child required.");
            lock (_issuer._gate)
            {
                ObjectDisposedException.ThrowIf(_issuer._retiring || _close is not null, this);
                if (_child is not null || !actual.HasOriginalProcess || actual.IsResumed || !ReferenceEquals(actual.OriginalExit, sameActualExit) ||
                    !StringComparer.OrdinalIgnoreCase.Equals(actual.OriginalExecutable, Package.Entrypoint) ||
                    actual.OriginalPrincipal != "windows-sid:" + Admission.Enrollment.OriginalOsPrincipal)
                    throw new UnauthorizedAccessException("The SAME suspended native Home child/actual exit and original user are required.");
                _child = actual; _exit = sameActualExit;
            }
        }
        internal void ResumeOriginalChild(Action actualResume)
        {
            CloudflareOriginalExecutionGuard.InvokeOriginal(_issuer, () =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows controlled child required.");
                lock (_issuer._gate)
                {
                    if (_issuer._retiring || _close is not null || _child is null || _exit is null || _exit.IsCompleted ||
                        !_source.Driver.IsCompletedSuccessfully || _source.Sources.OriginalErrors.Count != 0 ||
                        _source.Sources.OriginalTasks.Any(raw => !raw.IsCompletedSuccessfully))
                        throw new UnauthorizedAccessException("The actual admitted signed Home cohort/child retired or failed before resume.");
                    DemandNativeCurrent(_source);
                    // No await or release between source-current native pins and
                    // the SAME child resume. A rejected callback keeps the actual
                    // child/exit/pins retained; it never reports startup success.
                    actualResume();
                }
                return true;
            });
        }
        public void DemandExternalOriginalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(_issuer);
        public Task CloseAndDrainOriginalAsync()
        {
            DemandExternalOriginalJoin(); TaskCompletionSource? start = null; Task actual;
            lock (_issuer._gate)
            {
                if (_close is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Close(start.Task); }
                actual = _close;
            }
            start?.SetResult(); return actual;
        }
        private async Task Close(Task start)
        {
            await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            try { await _source.Driver.ConfigureAwait(false); } catch (Exception cause) { _source.Sources.Capture(_source.Driver, cause); }
            if (_exit is not null)
            {
                try { await _source.Sources.AwaitAsync(_exit).ConfigureAwait(false); }
                catch (Exception cause)
                {
                    _source.Sources.Capture(_exit, cause);
                    throw new AggregateException("The actual Home exit remains unknown; retain its immutable original payload pins.", _source.Sources.OriginalErrors);
                }
            }
            await _issuer.CloseResources(_source).ConfigureAwait(false);
            await _source.Sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (_source.Sources.OriginalErrors.Count != 0)
                throw new AggregateException("Original Home native cohort cleanup failed and remains retained.", _source.Sources.OriginalErrors);
        }
    }
    private static bool SameOriginalPackage(InstalledPackage current, InstalledPackage previous) =>
        current.Entrypoint == previous.Entrypoint && current.EntrypointSha256 == previous.EntrypointSha256 &&
        current.SignedArtifact.SignedDescriptorBytes.Span.SequenceEqual(previous.SignedArtifact.SignedDescriptorBytes.Span) &&
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { current.Entry, current.Activation }, Json)).AsSpan().SequenceEqual(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { previous.Entry, previous.Activation }, Json)));

    public Task<OriginalHomeLaunchCohort> AcquireOriginalHomeLaunchCohortWithinSourceAsync(Observation sameAdmission,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var source = new CloudflareOriginalTaskLedger(); source.BindOriginalOwner(this);
        source.BindOriginalCallerCallback(body => WithinSource(source, scope, body));
        var work = new Invocation(source, retain); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            _originals.RemoveAll(old => old.Healthy);
            if (_originals.Count >= 128) throw new InvalidOperationException("Unresolved Root launch/read originals remain retained.");
            work.Driver = Acquire(start.Task); _originals.Add(work); _originalReceipts.Add(work.Driver, work);
        }
        try { source.Invoke(() => { retain(work.Driver); return true; }); } catch (Exception cause) { source.Retain(cause); }
        finally { start.SetResult(); }
        return (Task<OriginalHomeLaunchCohort>)work.Driver;
        async Task<OriginalHomeLaunchCohort> Acquire(Task gate)
        {
            await gate.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            OriginalHomeLaunchCohort? result = null;
            try
            {
                source.Invoke(() =>
                {
                    token.ThrowIfCancellationRequested();
                    if (!OperatingSystem.IsWindows() || _connectedRoot is not null || _serviceContext is null || !IsIssuedOriginalAdmission(sameAdmission))
                        throw new UnauthorizedAccessException("Only the actual registered Root service's SAME healthy cold admission can launch Home.");
                    return true;
                });
                // The complete signed cohort is acquired under native read pins;
                // this does not reuse a prior transient hash as launch authority.
                var fresh = await Read(work, () => InspectOriginalRuntimeWithinSourceAsync(
                    body => source.Invoke(() => { body(); return true; }), raw => Keep(work, raw), token)).ConfigureAwait(false);
                if (fresh is null || !IsIssuedOriginalAdmission(fresh) || fresh.Actor != sameAdmission.Actor ||
                    fresh.Publisher.CatalogueSha256 != sameAdmission.Publisher.CatalogueSha256 ||
                    !SameOriginalPackage(fresh.Home, sameAdmission.Home) || !SameOriginalPackage(fresh.Root, sameAdmission.Root))
                    throw new UnauthorizedAccessException("The actual protected enrolled Root/Home activation changed before native launch.");
                var package = await ReadPackage(work, fresh.Registry, sameAdmission.Publisher,
                    sameAdmission.Publisher.HomePackageId, "home", token).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("The exact protected Home activation is unavailable.");
                if (!SameOriginalPackage(package, sameAdmission.Home)) throw new UnauthorizedAccessException("The actual Home activation changed before original launch.");
                var actor = await Read(work, () => _profiles.GetCurrentWithinOriginalSourceAsync(
                    body => source.Invoke(() => { body(); return true; }), raw => Keep(work, raw), token)).ConfigureAwait(false);
                if (actor != sameAdmission.Actor) throw new UnauthorizedAccessException("The original kernel user/profile changed before Home launch.");
                await Read(work, async () =>
                {
                    await _publisher.DemandOriginalRootPublisherCurrentWithinSourceAsync(sameAdmission.Publisher,
                        body => source.Invoke(() => { body(); return true; }), raw => Keep(work, raw), token).ConfigureAwait(false);
                    return true;
                }).ConfigureAwait(false);
                source.Invoke(() => { DemandNativeCurrent(work); result = new(this, work, sameAdmission, package); return true; });
            }
            catch (Exception cause) { source.Retain(cause); }
            await source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (source.OriginalErrors.Count != 0)
            {
                await CloseResources(work).ConfigureAwait(false);
                throw new AggregateException("The original signed Home launch cohort failed; retained evidence cannot be promoted.", source.OriginalErrors);
            }
            _launchCohorts.Add(work, result!); return result!;
        }
    }
}
