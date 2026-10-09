using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;

namespace Haven.Infrastructure.Native.Windows;

public sealed partial class NativeWindowsHomeInstalledRootAdmission
{
    private readonly ConditionalWeakTable<Invocation, OriginalSelectedApplicationLaunchCohort> _selectedLaunchCohorts = new();
    /// <summary>Opaque native custody of the complete signed selected application payload. The
    /// actual child's original exit is bound before any resume; pins cannot be
    /// released while that accepted child remains alive or its exit is unknown.</summary>
    public sealed class OriginalSelectedApplicationLaunchCohort
    {
        private readonly NativeWindowsHomeInstalledRootAdmission _issuer;
        private readonly Invocation _source;
        internal Invocation OriginalSource => _source;
        internal bool HasOriginalIssuer(NativeWindowsHomeInstalledRootAdmission same) => ReferenceEquals(_issuer, same);
        internal readonly Observation Admission;
        internal readonly InstalledPackage Package;
        private NativeWindowsHomeOriginalControlledProcess? _child;
        private Task? _exit, _close;
        internal OriginalSelectedApplicationLaunchCohort(NativeWindowsHomeInstalledRootAdmission issuer, Invocation source,
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
                    throw new UnauthorizedAccessException("The SAME suspended native selected application child/actual exit and original user are required.");
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
                        throw new UnauthorizedAccessException("The actual admitted signed selected application cohort/child retired or failed before resume.");
                    DemandNativeCurrent(_source);
                    // No await or release between source-current native pins and
                    // the SAME child resume. A rejected callback keeps the actual
                    // child/exit/pins retained; it never reports startup success.
                    actualResume();
                }
                return true;
            });
        }
        internal void DemandOriginalPreparedCurrent()
        {
            CloudflareOriginalExecutionGuard.InvokeOriginal(_issuer, () =>
            {
                lock (_issuer._gate)
                {
                    if (_close is not null || !_source.Driver.IsCompletedSuccessfully ||
                        _source.Sources.OriginalErrors.Count != 0 || _source.Sources.OriginalTasks.Any(raw => !raw.IsCompletedSuccessfully))
                        throw new UnauthorizedAccessException("The SAME original selected runtime preparation retired or failed.");
                    _source.Driver.GetAwaiter().GetResult(); DemandNativeCurrent(_source);
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
                    throw new AggregateException("The actual selected application exit remains unknown; retain its immutable original payload pins.", _source.Sources.OriginalErrors);
                }
            }
            await _issuer.CloseResources(_source).ConfigureAwait(false);
            await _source.Sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (_source.Sources.OriginalErrors.Count != 0)
                throw new AggregateException("Original selected application native cohort cleanup failed and remains retained.", _source.Sources.OriginalErrors);
        }
    }

    /// <summary>The actual Root child creation/resume is bounded by a fresh native
    /// read-only pin over the existing protected canonical machine state. This
    /// window closes after the independently joined original effect; the accepted
    /// child keeps its separate immutable runtime cohort until its actual exit.</summary>
    internal Task<T> RunOriginalSelectedLaunchWithinSourceAsync<T>(OriginalSelectedApplicationLaunchCohort sameCohort,
        Func<Task<T>> actualEffect, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(actualEffect); ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var source = new CloudflareOriginalTaskLedger(); source.BindOriginalOwner(this);
        source.BindOriginalCallerCallback(body => WithinSource(source, scope, body));
        var work = new Invocation(source, retain); var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            _originals.RemoveAll(old => old.Healthy);
            if (_originals.Count >= 128) throw new InvalidOperationException("Unresolved original Root launch windows remain retained.");
            work.Driver = DriveWindow(begin.Task); _originals.Add(work); _originalReceipts.Add(work.Driver, work);
        }
        try { source.Invoke(() => { retain(work.Driver); return true; }); } catch (Exception cause) { source.Retain(cause); }
        finally { begin.SetResult(); }
        return (Task<T>)work.Driver;
        async Task<T> DriveWindow(Task start)
        {
            await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            T result = default!;
            try
            {
                source.Invoke(() =>
                {
                    if (!OperatingSystem.IsWindows() || _connectedRoot is not null || _serviceContext is null ||
                        !sameCohort.HasOriginalIssuer(this) ||
                        !_selectedLaunchCohorts.TryGetValue(sameCohort.OriginalSource, out var actual) || !ReferenceEquals(actual, sameCohort) ||
                        !IsIssuedOriginalAdmission(sameCohort.Admission))
                        throw new UnauthorizedAccessException("The SAME actual Root-issued selected runtime cohort is required.");
                    sameCohort.DemandOriginalPreparedCurrent(); token.ThrowIfCancellationRequested(); return true;
                });
                var actor = await Read(work, () => _profiles.GetCurrentWithinOriginalSourceAsync(
                    body => source.Invoke(() => { body(); return true; }), raw => Keep(work, raw), token)).ConfigureAwait(false);
                var principal = await Read(work, () => _serviceContext!.GetPrincipalAsync(token).AsTask()).ConfigureAwait(false);
                // FILE_SHARE_READ excludes native writes and replacement through the
                // actual effect. This is the existing machine package state, not a
                // second store or an action grant from a path or signed declaration.
                var stateRoot = OpenRoot(work, Path.GetDirectoryName(_file)!);
                var stateFile = OpenImmutableRuntimeFile(work, stateRoot, _file);
                var state = await Read(work, () =>
                {
                    work.OriginalStateRead = ReadDocument<HomeCoreStoredState>(work, stateFile, 16 * 1024 * 1024, token);
                    return work.OriginalStateRead;
                }).ConfigureAwait(false);
                work.OriginalState = state;
                source.Invoke(() =>
                {
                    DemandOriginalSelectedWindowState(sameCohort, state, actor, principal);
                    DemandNativeCurrent(work); sameCohort.DemandOriginalPreparedCurrent(); return true;
                });
                // The callback is internal and pairs the actual Root record to this
                // SAME source-issued cohort. Its raw Task is retained before any
                // caller postguard and joined even when that publication fails.
                result = await Read(work, actualEffect).ConfigureAwait(false);
                source.Invoke(() => { DemandNativeCurrent(work); return true; });
            }
            catch (Exception cause) { source.Retain(cause); }
            finally { await CloseResources(work).ConfigureAwait(false); }
            await source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (source.OriginalErrors.Count != 0)
                throw new AggregateException("Actual selected launch/current activation window did not settle; preserve its original sources.", source.OriginalErrors);
            return result;
        }
    }
    private static void DemandOriginalSelectedWindowState(OriginalSelectedApplicationLaunchCohort cohort,
        HomeCoreStoredState state, AuthenticatedResourceActor? actor, string? principal)
    {
        var original = cohort.Admission;
        if (!FileHomeCoreStateStore.IsOriginalProtectedStateValid(state) || actor != original.Actor ||
            principal != "windows-sid:" + original.Enrollment.OriginalOsPrincipal)
            throw new UnauthorizedAccessException("The actual enrolled Root actor/principal changed before selected launch.");
        var profileRecord = state.Records.SingleOrDefault(row => row.RecordId == "home.local-profile");
        if (profileRecord is null || profileRecord.RecordType != "home.local-profile" || profileRecord.SchemaVersion != 1 ||
            profileRecord.Scope != HomeDataScope.DeviceLocal || profileRecord.Authority != HomeRecordAuthority.LocalCanonical)
            throw new InvalidDataException("The pinned canonical machine profile requires recovery.");
        var profile = profileRecord.Payload.Deserialize<HomeLocalProfile>();
        if (profile is null || profile.ProfileId.ToString("D") != original.Actor.ProfileId || profile.CreatedAtUtc == default ||
            profile.PrincipalDigest != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(principal!))))
            throw new UnauthorizedAccessException("The pinned canonical machine profile differs from the actual Root actor.");
        var record = state.Records.SingleOrDefault(row => row.RecordId == HomePackageDatabase.RecordId);
        if (record is null || record.RecordType != HomePackageDatabase.RecordType || record.Scope != HomeDataScope.DeviceLocal ||
            record.Authority != HomeRecordAuthority.LocalCanonical || record.SchemaVersion != HomePackageDatabase.CurrentSchemaVersion)
            throw new InvalidDataException("The pinned canonical package record requires recovery.");
        var registry = record.Payload.Deserialize<HomePackageDatabaseSnapshot>(Json)
            ?? throw new InvalidDataException("The pinned canonical package snapshot is empty.");
        if (registry.Revision != record.Revision || HomePackageDatabase.ValidateOriginalProtectedSnapshot(registry) is not null ||
            registry.UnknownFields is null || !registry.UnknownFields.TryGetValue(HomePackageOriginalPublisherEnrollmentRecord.Field, out var enrolled))
            throw new InvalidDataException("The pinned canonical package/enrollment snapshot is invalid.");
        var enrollment = enrolled.Deserialize<HomePackageOriginalPublisherEnrollmentRecord>(Json)
            ?? throw new InvalidDataException("The pinned canonical publisher enrollment is malformed.");
        if (!JsonSerializer.SerializeToUtf8Bytes(enrollment, Json).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(original.Enrollment, Json)))
            throw new UnauthorizedAccessException("The actual explicit publisher enrollment changed before selected launch.");
        foreach (var expected in new[] { original.Root, original.Home, cohort.Package })
        {
            var entry = registry.Packages.SingleOrDefault(row => row.PackageId == expected.Entry.PackageId);
            if (entry is null || !JsonSerializer.SerializeToUtf8Bytes(entry, Json).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(expected.Entry, Json)))
                throw new UnauthorizedAccessException("The actual protected Root/Home/selected activation changed before selected launch.");
        }
    }
    internal static string OriginalSelectedPackageFingerprint(InstalledPackage package) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { package = package.Entry, activation = package.Activation })));

    public Task<OriginalSelectedApplicationLaunchCohort> AcquireOriginalSelectedApplicationLaunchCohortWithinSourceAsync(Observation sameAdmission, string packageId, string originalActivationSha256,
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
        return (Task<OriginalSelectedApplicationLaunchCohort>)work.Driver;
        async Task<OriginalSelectedApplicationLaunchCohort> Acquire(Task gate)
        {
            await gate.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            OriginalSelectedApplicationLaunchCohort? result = null;
            try
            {
                source.Invoke(() =>
                {
                    token.ThrowIfCancellationRequested();
                    if (!OperatingSystem.IsWindows() || _connectedRoot is not null || _serviceContext is null || !IsIssuedOriginalAdmission(sameAdmission) || string.IsNullOrWhiteSpace(packageId) || packageId.Length > 256 ||
                        !sameAdmission.Enrollment.SelectedPackageIds.Contains(packageId, StringComparer.Ordinal) ||
                        !sameAdmission.Publisher.SignedProcess.Descriptors.TryGetValue(packageId, out var selected) ||
                        selected.AppId is "root" or "home" || selected.ComponentClass is not ("optional-app" or "mandatory-shared-core"))
                        throw new UnauthorizedAccessException("Only the actual registered Root service's SAME healthy cold admission can launch a selected application.");
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
                    packageId, sameAdmission.Publisher.SignedProcess.Descriptors[packageId].AppId, token).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("The exact protected Home activation is unavailable.");
                if (OriginalSelectedPackageFingerprint(package) != originalActivationSha256)
                    throw new UnauthorizedAccessException("The actual selected installed package changed after its Home choice.");
                // The transient inventory read above is not launch authority. Acquire
                // each declared byte under a new immutable native pin and verify it
                // again before the source may bind an accepted child lifetime.
                var runtimeRoot = OpenRoot(work, package.Activation.InstallationRoot);
                var runtimeFiles = new Dictionary<string, Microsoft.Win32.SafeHandles.SafeFileHandle>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in package.Activation.ActivatedFiles)
                {
                    var actual = OpenImmutableRuntimeFile(work, runtimeRoot, Path.Combine(package.Activation.InstallationRoot,
                        file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                    runtimeFiles.Add(file.RelativePath, actual);
                    await DemandDigest(work, actual, file.Sha256, file.Bytes, token).ConfigureAwait(false);
                }
                if (!await HasSupportedOriginalRuntime(work, runtimeRoot, package.Activation, runtimeFiles, token).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("The selected application requires its protected self-contained unbundled .NET10 runtime.");
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
                throw new AggregateException("The original signed selected application launch cohort failed; retained evidence cannot be promoted.", source.OriginalErrors);
            }
            _selectedLaunchCohorts.Add(work, result!); return result!;
        }
    }
}
