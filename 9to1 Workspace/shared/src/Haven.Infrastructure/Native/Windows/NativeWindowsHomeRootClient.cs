using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;

namespace Haven.Infrastructure.Native.Windows;

/// <summary>The actual limited Home process connects to its installed SCM Root.
/// Kernel Root identity, signed catalogue, explicit publisher enrollment and complete
/// protected activation are observed independently of any running Home session.
/// Wire strings/rows remain observations. Constructors perform no IO or enrollment.</summary>
public sealed partial class NativeWindowsHomeRootClient : IInstalledApplicationOriginalScopedObservationProvider, IAsyncDisposable
{
    public const string OriginalProviderId = "9to1.home.original-root-installed";
    public string ProviderId => OriginalProviderId;
    private readonly object _gate = new();
    private readonly string _machineFile;
    private readonly FileHomeCoreStateStore _machineStore;
    private readonly HomeLocalProfileIdentity _machineProfiles;
    private readonly HomePackageDatabase _machinePackages;
    private readonly NativeWindowsHomeInstallerBootstrapAdmission _publisher;
    private readonly SemaphoreSlim _transportGate = new(1, 1);
    private readonly CancellationToken _appLifetime;
    private NamedPipeClientStream? _pipe;
    private NativeWindowsHomeInstalledRootAdmission? _admission;
    private FileHomeCoreStateStore? _homeStore;
    private HomeLocalProfileIdentity? _homeProfiles;
    private HomeInstalledApplicationRegistry? _registry;
    private readonly List<Original> _originals = [];
    private readonly CloudflareOriginalTaskLedger _closing = new();
    private readonly ConditionalWeakTable<PackageObservation, Original> _packages = new();
    private Task? _connect, _close, _pipeClose;
    private bool _retiring;
    private sealed class Original(CloudflareOriginalTaskLedger source)
    {
        internal readonly CloudflareOriginalTaskLedger Source = source;
        internal Task Driver = null!;
        internal readonly List<RemoteRead> Reads = [];
        internal bool Healthy
        {
            get
            {
                var sameDriver = Driver;
                if (!sameDriver.IsCompletedSuccessfully) return false;
                // Status only bounds this finite observation. Independently join the
                // SAME privately published driver before successful custody retirement.
                sameDriver.GetAwaiter().GetResult();
                return Source.OriginalErrors.Count == 0 && Source.OriginalTasks.All(raw => raw.IsCompletedSuccessfully) &&
                    Reads.All(read => read.Healthy);
            }
        }
    }
    private sealed class RemoteRead(Guid id, object owner)
    {
        internal readonly Guid Id = id;
        internal readonly CloudflareOriginalTaskLedger Cleanup = CreateSource(owner);
        internal Task? Close;
        internal bool Healthy => Close?.IsCompletedSuccessfully == true && Cleanup.OriginalErrors.Count == 0 &&
            Cleanup.OriginalTasks.All(raw => raw.IsCompletedSuccessfully);
        private static CloudflareOriginalTaskLedger CreateSource(object owner)
        { var source = new CloudflareOriginalTaskLedger(); source.BindOriginalOwner(owner); return source; }
    }
    public sealed class PackageObservation
    {
        internal readonly NativeWindowsHomeRootClient Owner;
        internal readonly NativeWindowsHomeInstalledRootAdmission.Observation Root;
        internal readonly HomePackageOriginalArtifactObservation Artifact;
        internal readonly HomePackageDatabaseEntry Package;
        internal readonly HomePackageOriginalInstalledActivationRecord Activation;
        internal readonly string Fingerprint;
        public AuthenticatedResourceActor Actor { get; }
        public string OsPrincipal { get; }
        public string AppId => Activation.AppId;
        public string PackageId => Package.PackageId;
        public string Entrypoint => Path.Combine(Activation.InstallationRoot, Activation.EntrypointRelativePath);
        public string Version => Package.InstalledVersion!;
        internal PackageObservation(NativeWindowsHomeRootClient owner, NativeWindowsHomeInstalledRootAdmission.Observation root,
            HomePackageOriginalArtifactObservation artifact, HomePackageDatabaseEntry package,
            HomePackageOriginalInstalledActivationRecord activation, AuthenticatedResourceActor actor, string principal)
        {
            Owner = owner; Root = root; Artifact = artifact; Package = package; Activation = activation; Actor = actor; OsPrincipal = principal;
            Fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { package, activation })));
        }
    }
    public NativeWindowsHomeRootClient(string actualMachineStateFile, CancellationToken actualAppLifetime)
    {
        if (!actualAppLifetime.CanBeCanceled) throw new ArgumentException("The actual application lifetime is required.", nameof(actualAppLifetime));
        _machineFile = Path.GetFullPath(actualMachineStateFile); _appLifetime = actualAppLifetime;
        _machineStore = new(_machineFile); _machineProfiles = new(_machineStore, new OperatingSystemPrincipalSource());
        _machinePackages = new(_machineStore); _publisher = new(_machineProfiles);
        _closing.BindOriginalOwner(this);
    }
    /// <summary>Pure one-time binding before startup. This exact index is the maintained
    /// Home registry over the SAME local Home state/current actor and this real inventory.
    /// A registry row still needs the fresh native Root package observation below.</summary>
    public void BindOriginalHomeInventory(FileHomeCoreStateStore sameHomeStore, HomeLocalProfileIdentity sameHomeProfiles,
        HomeInstalledApplicationRegistry sameRegistry)
    {
        // The public owning registry query checks the SAME Home profile's
        // internal store binding as well as every exact registry/provider reference.
        if (!sameRegistry.HasOriginalComposition(sameHomeStore, sameHomeProfiles, [this]))
            throw new UnauthorizedAccessException("The SAME actual Home/index/actor/inventory tuple is required.");
        lock (_gate)
        {
            if (_connect is not null || _retiring || _homeStore is not null)
                throw new InvalidOperationException("Bind the actual Home inventory once before any cold Root source begins.");
            _homeStore = sameHomeStore; _homeProfiles = sameHomeProfiles; _registry = sameRegistry;
        }
    }
    public bool HasOriginalHomeInventory(FileHomeCoreStateStore sameStore, HomeLocalProfileIdentity sameProfiles,
        HomeInstalledApplicationRegistry sameRegistry) => ReferenceEquals(_homeStore, sameStore) && ReferenceEquals(_homeProfiles, sameProfiles) && ReferenceEquals(_registry, sameRegistry);
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public Task ConnectOriginalWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        return Admit(scope, retain, async source =>
        {
            source.Invoke(() =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Installed Windows Root required.");
                if (_homeProfiles is null || _registry is null) throw new InvalidOperationException("Bind the actual Home inventory before Root connection.");
                _pipe = new(".", NativeWindowsHomeRootServiceRuntime.OriginalControlPipeName,
                    PipeDirection.InOut, PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.Identification);
                _admission = new(_publisher, _machinePackages, _machineStore, _machineProfiles, _machineFile, actualConnectedRoot: _pipe);
                return true;
            });
            await Take(source, () => _pipe!.ConnectAsync(15000, token)).ConfigureAwait(false);
            var root = await Take(source, () => _admission!.InspectOriginalRuntimeWithinSourceAsync(
                body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); retain(raw); }, token)).ConfigureAwait(false);
            if (root is null || !_admission!.IsIssuedOriginalAdmission(root))
            {
                var reason = root is null ? "AuthenticatedSignedRootEnrollmentAndActivationRequired" : "UnknownRootAdmission";
                throw new UnauthorizedAccessException(reason);
            }
            var response = await ExchangeOwned(source, new(1, Guid.NewGuid(), "observe-current")).ConfigureAwait(false);
            if (!response.Accepted) throw new UnauthorizedAccessException(response.Reason ?? "ActualRootControlledHomeRequired");
            return true;
        }, publish: raw => _connect = raw, existing: () => _connect as Task<bool>);
    }
    public ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken token)
        => ObserveWithinOriginalSourceAsync(body => body(), _ => { }, token);
    public ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveWithinOriginalSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token)
        => new(Admit(scope, retain, async source =>
        {
            if (_connect?.IsCompletedSuccessfully != true) return (IReadOnlyList<InstalledApplicationProfileObservation>)Array.Empty<InstalledApplicationProfileObservation>();
            var profiles = _homeProfiles ?? throw new UnauthorizedAccessException("The SAME actual Home profile source is required.");
            var actor = await Take(source, () => profiles.GetCurrentWithinOriginalSourceAsync(
                body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); retain(raw); }, token)).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The actual current Home actor is unavailable.");
            var root = await Take(source, () => _admission!.InspectOriginalRuntimeWithinSourceAsync(
                body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); retain(raw); }, token)).ConfigureAwait(false);
            if (root is null || !_admission!.IsIssuedOriginalAdmission(root))
                return Array.Empty<InstalledApplicationProfileObservation>();
            var applications = new List<InstalledApplicationObservation>();
            foreach (var package in root.Enrollment.SelectedPackageIds)
            {
                var observed = await ObservePackageOwned(source, root, actor, package, token).ConfigureAwait(false);
                if (observed is null) continue;
                applications.Add(new("9to1.package:" + observed.PackageId, observed.Entrypoint,
                    observed.Package.Name, observed.Version, true) { StableLaunchIdentity = observed.AppId });
            }
            if (actor != await Take(source, () => profiles.GetCurrentWithinOriginalSourceAsync(
                body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); retain(raw); }, token)).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The actual Home actor changed during installed package inventory.");
            return Array.AsReadOnly(new[] { new InstalledApplicationProfileObservation("windows-sid:" + root.Enrollment.OriginalOsPrincipal,
                "Current Windows principal", false, true, Array.AsReadOnly(applications.ToArray())) });
        }, originalLaunchParent: OriginalLaunchReadParent()));
    public Task<PackageObservation?> ObserveOriginalPackageWithinSourceAsync(AuthenticatedResourceActor sameActor, string canonicalAppId,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => Admit(scope, retain, async source =>
        {
            if (_connect?.IsCompletedSuccessfully != true) return null;
            var actor = await Take(source, () => _homeProfiles!.GetCurrentWithinOriginalSourceAsync(
                body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); retain(raw); }, token)).ConfigureAwait(false);
            if (actor != sameActor) throw new UnauthorizedAccessException("The actual Home actor changed before the selected package read.");
            var root = await Take(source, () => _admission!.InspectOriginalRuntimeWithinSourceAsync(
                body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); retain(raw); }, token)).ConfigureAwait(false);
            if (root is null || !_admission!.IsIssuedOriginalAdmission(root)) return null;
            var matching = root.Publisher.SignedProcess.Descriptors.Values.Where(descriptor => descriptor.AppId == canonicalAppId &&
                root.Enrollment.SelectedPackageIds.Contains(descriptor.PackageId, StringComparer.Ordinal)).ToArray();
            if (matching.Length == 0) return null;
            if (matching.Length != 1) throw new UnauthorizedAccessException("The protected signed catalogue contains ambiguous installed app choices.");
            var result = await ObservePackageOwned(source, root, sameActor, matching[0].PackageId, token).ConfigureAwait(false);
            if (sameActor != await Take(source, () => _homeProfiles!.GetCurrentWithinOriginalSourceAsync(
                body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); retain(raw); }, token)).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The actual Home actor changed before package publication.");
            return result;
        }, originalLaunchParent: OriginalLaunchReadParent(sameActor, canonicalAppId));
    public bool IsIssuedOriginalPackageObservation(PackageObservation same) => same is not null && ReferenceEquals(same.Owner, this) &&
        _packages.TryGetValue(same, out var source) && source.Healthy && _admission!.IsIssuedOriginalAdmission(same.Root);
    public Task DemandOriginalPackageCurrentWithinSourceAsync(PackageObservation same, Action<Action> scope, Action<Task> retain, CancellationToken token)
        => Admit(scope, retain, async source =>
        {
            if (!IsIssuedOriginalPackageObservation(same)) throw new UnauthorizedAccessException("The SAME source-issued protected package observation is required.");
            var fresh = await Take(source, () => ObserveOriginalPackageWithinSourceAsync(same.Actor, same.AppId,
                body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); retain(raw); }, token)).ConfigureAwait(false);
            if (fresh is null || !IsIssuedOriginalPackageObservation(fresh) || fresh.Fingerprint != same.Fingerprint || fresh.OsPrincipal != same.OsPrincipal ||
                !fresh.Artifact.SignedDescriptorBytes.Span.SequenceEqual(same.Artifact.SignedDescriptorBytes.Span))
                throw new UnauthorizedAccessException("The actual signed/enrolled installed package changed or retired.");
            return true;
        }, originalLaunchParent: OriginalLaunchReadParent(same.Actor, same.AppId));
    private async Task<PackageObservation?> ObservePackageOwned(CloudflareOriginalTaskLedger sources,
        NativeWindowsHomeInstalledRootAdmission.Observation root, AuthenticatedResourceActor actor, string packageId, CancellationToken token)
    {
        if (!root.Publisher.SignedProcess.Artifacts.TryGetValue(packageId, out var artifact))
            throw new UnauthorizedAccessException("The actual enrolled publisher did not issue this selected artifact.");
        var request = new NativeWindowsHomeRootWire.Request(1, Guid.NewGuid(), "open-activation") { PackageId = packageId };
        RemoteRead? read = null; Original invocation;
        lock (_gate) invocation = _originals.Single(original => ReferenceEquals(original.Source, sources));
        PackageObservation? result = null; Exception? primary = null;
        try
        {
            var response = await ExchangeOwned(sources, request, actual =>
            {
                if (actual.SchemaVersion == 1 && actual.CorrelationId == request.CorrelationId && actual.Accepted && actual.ReadId != Guid.Empty)
                {
                    // Assignment-only custody runs inside the actual decode before
                    // any caller's postcheck. It creates no request or authority.
                    lock (_gate) { read = new(actual.ReadId, this); invocation.Reads.Add(read); }
                }
            }).ConfigureAwait(false);
            if (!response.Accepted) return null;
            if (read is null) throw new UnauthorizedAccessException("The actual Root omitted its original read receipt.");
            if (response.PackageId != packageId || response.OriginalPackage is null || response.Activation is null ||
                response.SignedDescriptor is null || response.DescriptorPayload is null || response.CatalogueRevision != artifact.CatalogueRevision ||
                !response.SignedDescriptor.AsSpan().SequenceEqual(artifact.SignedDescriptorBytes.Span) ||
                !response.DescriptorPayload.AsSpan().SequenceEqual(artifact.DescriptorPayloadBytes.Span) ||
                response.OriginalPackage.PackageId != packageId || response.Activation.PackageId != packageId ||
                response.OriginalPackage.InstallationState != HomePackageInstallState.Installed ||
                response.Activation.OriginalRootOperationId != response.ActivationOperationId)
                throw new UnauthorizedAccessException("The authenticated Root returned a different original selected signed activation/current principal.");
            var current = await ExchangeOwned(sources, new(1, Guid.NewGuid(), "validate-activation") { ReadId = read.Id }).ConfigureAwait(false);
            if (!current.Accepted || current.ReadId != read.Id || current.PackageId != packageId || current.ActivationOperationId != response.ActivationOperationId)
                throw new UnauthorizedAccessException("The actual native Root read failed original currentness.");
            result = new(this, root, artifact, response.OriginalPackage, response.Activation, actor, "windows-sid:" + root.Enrollment.OriginalOsPrincipal);
        }
        catch (Exception cause) { primary = cause; sources.Retain(cause); }
        finally
        {
            if (read is not null)
            {
                var close = CloseRemoteRead(read); _ = sources.Track(close);
                try { await sources.AwaitAsync(close).ConfigureAwait(false); }
                catch (Exception cause) { sources.Capture(close, cause); primary ??= cause; }
            }
        }
        if (primary is not null || sources.OriginalErrors.Count != 0)
            throw new AggregateException("Original Root package acquisition/publication and independent read cleanup failed.", sources.OriginalErrors);
        // A private row is published only after independently healthy Root/native close.
        _packages.Add(result!, invocation); return result;
    }
    private Task CloseRemoteRead(RemoteRead same)
    {
        TaskCompletionSource? begin = null; Task actual;
        lock (_gate)
        {
            if (same.Close is null) { begin = new(TaskCreationOptions.RunContinuationsAsynchronously); same.Close = Close(begin.Task); }
            actual = same.Close;
        }
        begin?.SetResult(); return actual;
        async Task Close(Task start)
        {
            await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var source = same.Cleanup; // Never reuse a completed productive caller scope.
            try
            {
                var response = await ExchangeOwned(source, new(1, Guid.NewGuid(), "close-activation") { ReadId = same.Id }).ConfigureAwait(false);
                if (!response.Accepted || response.ReadId != same.Id) throw new InvalidOperationException("The actual Root activation read close remains unconfirmed.");
            }
            catch (Exception cause) { source.Retain(cause); }
            await source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (source.OriginalErrors.Count != 0)
                throw new AggregateException("The SAME original Root read cleanup remains failed and retained.", source.OriginalErrors);
        }
    }
    private async Task<NativeWindowsHomeRootWire.Response> ExchangeOwned(CloudflareOriginalTaskLedger source,
        NativeWindowsHomeRootWire.Request original, Action<NativeWindowsHomeRootWire.Response>? retainActualResponse = null)
    {
        Task? gate = null; bool entered = false; Exception? acquisitionFailure = null;
        try
        {
            try { source.Invoke(() => { gate = _transportGate.WaitAsync(CancellationToken.None); _ = source.Track(gate); return true; }); }
            catch (Exception cause) { source.Retain(cause); acquisitionFailure = cause; }
            if (gate is not null) { await source.AwaitAsync(gate).ConfigureAwait(false); entered = true; }
            if (acquisitionFailure is not null) throw acquisitionFailure;
            if (!entered) throw new InvalidOperationException("The actual Root transport admission returned no retained gate task.");
            await NativeWindowsHomeRootWire.WriteAsync(_pipe!, original, source, CancellationToken.None).ConfigureAwait(false);
            var response = await NativeWindowsHomeRootWire.ReadAsync<NativeWindowsHomeRootWire.Response>(_pipe!, source, CancellationToken.None,
                retainActualResponse).ConfigureAwait(false);
            if (response is null || response.SchemaVersion != 1 || response.CorrelationId != original.CorrelationId)
                throw new UnauthorizedAccessException("The original Root channel returned no matching authenticated response.");
            return response;
        }
        finally { if (entered) _transportGate.Release(); }
    }
    private Task<T> Admit<T>(Action<Action> scope, Action<Task> retain, Func<CloudflareOriginalTaskLedger, Task<T>> body,
        Action<Task>? publish = null, Func<Task<T>?>? existing = null, LaunchInvocation? originalLaunchParent = null, bool originalLaunchAdmission = false)
    {
        var source = new CloudflareOriginalTaskLedger(); source.BindOriginalOwner(this);
        source.BindOriginalCallerCallback(action => CloudflareOriginalExecutionGuard.InvokeOriginal(this,
            () => { RunRootCallback(source, scope, action); return true; }));
        var work = new Original(source); var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task<T> actual;
        lock (_gate)
        {
            if (existing?.Invoke() is { } known) return known;
            ObjectDisposedException.ThrowIf(_retiring && !IsLiveOriginalLaunchReadParent(originalLaunchParent), this);
            ObjectDisposedException.ThrowIf(_launchWithdrawalRequested && originalLaunchAdmission, this); _appLifetime.ThrowIfCancellationRequested();
            _originals.RemoveAll(original => original.Healthy);
            if (_originals.Count >= 128) throw new InvalidOperationException("Unconfirmed original Root client sources remain retained.");
            actual = Drive(begin.Task); work.Driver = actual; _originals.Add(work); publish?.Invoke(actual);
        }
        try { source.Invoke(() => { retain(actual); return true; }); } catch (Exception cause) { source.Retain(cause); }
        finally { begin.SetResult(); } return actual;
        async Task<T> Drive(Task start)
        {
            await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            T result = default!;
            try { result = await body(source).ConfigureAwait(false); } catch (Exception cause) { source.Retain(cause); }
            await source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (source.OriginalErrors.Count != 0) throw new AggregateException("Original Root client/caller/raw protocol failed; all evidence remains retained.", source.OriginalErrors);
            return result;
        }
    }
    private static async Task<T> Take<T>(CloudflareOriginalTaskLedger source, Func<Task<T>> factory)
    {
        Task<T>? raw = null;
        try { source.Invoke(() => { raw = factory(); _ = source.Track(raw); return true; }); } catch (Exception cause) { source.Retain(cause); }
        T result = default!;
        if (raw is not null) try { result = await source.AwaitAsync(raw).ConfigureAwait(false); } catch (Exception cause) { source.Capture(raw, cause); }
        if (source.OriginalErrors.Count != 0) throw new AggregateException("Original Root child/caller source failed.", source.OriginalErrors);
        return raw is null ? throw new InvalidOperationException("The original Root source returned no raw Task.") : result;
    }
    private static async Task Take(CloudflareOriginalTaskLedger source, Func<Task> factory)
    {
        Task? raw = null;
        try { source.Invoke(() => { raw = factory(); _ = source.Track(raw); return true; }); }
        catch (Exception cause) { source.Retain(cause); }
        if (raw is not null)
            try { await source.AwaitAsync(raw).ConfigureAwait(false); } catch (Exception cause) { source.Capture(raw, cause); }
        if (source.OriginalErrors.Count != 0) throw new AggregateException("Original Root child/caller source failed.", source.OriginalErrors);
        if (raw is null) throw new InvalidOperationException("The original Root source returned no raw Task.");
    }
    private static void RunRootCallback(CloudflareOriginalTaskLedger source, Action<Action> caller, Action body)
    {
        var active = 1; var used = 0; var thread = Environment.CurrentManagedThreadId; var errors = new List<Exception>();
        void Keep(Exception cause) { lock (errors) errors.Add(cause); source.Retain(cause); }
        try
        {
            try
            {
                caller(() =>
                {
                    if (Volatile.Read(ref active) != 1 || thread != Environment.CurrentManagedThreadId || Interlocked.Exchange(ref used, 1) != 0)
                    { var failure = new InvalidOperationException("Original Root client callback is expired, repeated or foreign-thread."); Keep(failure); throw failure; }
                    try { body(); } catch (Exception cause) { Keep(cause); throw; }
                });
                if (used != 1) Keep(new InvalidOperationException("The original Root callback was omitted."));
            }
            catch (Exception cause) { Keep(cause); }
        }
        finally { Volatile.Write(ref active, 0); }
        Exception[] all; lock (errors) all = errors.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        if (all.Length != 0) throw new AggregateException("Original Root caller/body/protocol failed.", all);
    }
    public void DemandExternalOriginalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    public void RequestOriginalRetirement()
    { DemandExternalOriginalJoin(); RequestOriginalLaunchReviewWithdrawals(); lock (_gate) _retiring = true; }
    public Task CloseAndDrainOriginalAsync()
    {
        DemandExternalOriginalJoin(); RequestOriginalLaunchReviewWithdrawals(); TaskCompletionSource? begin = null; Task actual;
        lock (_gate)
        {
            _retiring = true;
            if (_close is null) { begin = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = ClosePublished(begin.Task); }
            actual = _close;
        }
        begin?.SetResult(); return actual;
        async Task ClosePublished(Task start)
        {
            await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var launchClose = DrainOriginalApplicationLaunchesOwned();
            try { await launchClose.ConfigureAwait(false); } catch (Exception cause) { _closing.Capture(launchClose, cause); }
            var errors = new List<Exception>(); Original[] all;
            lock (_gate) all = _originals.ToArray();
            foreach (var original in all)
            {
                try { await original.Driver.ConfigureAwait(false); } catch (Exception cause) { errors.Add(original.Driver.Exception ?? cause); }
                foreach (var read in original.Reads)
                {
                    try { var close = CloseRemoteRead(read); _ = _closing.Track(close); await close.ConfigureAwait(false); }
                    catch (Exception cause) { errors.Add(read.Close?.Exception ?? cause); }
                }
                await original.Source.ObserveAllOriginalTasksAsync().ConfigureAwait(false); errors.AddRange(original.Source.OriginalErrors);
            }
            if (_closing.OriginalErrors.Count != 0)
                throw new AggregateException("Unsettled actual launch custody retains the original Root transport and downstream owners.", _closing.OriginalErrors);
            if (_pipe is not null)
                try { _closing.Invoke(() => { _pipeClose ??= _pipe.DisposeAsync().AsTask(); _ = _closing.Track(_pipeClose); return true; }); await _pipeClose!.ConfigureAwait(false); }
                catch (Exception cause) { errors.Add(_pipeClose?.Exception ?? cause); }
            foreach (var close in new Func<Task>[] { () => _admission?.CloseAndDrainAsync() ?? Task.CompletedTask, _publisher.CloseAndDrainAsync })
            { Task? raw = null; try { _closing.Invoke(() => { raw = close(); _ = _closing.Track(raw); return true; }); } catch (Exception cause) { errors.Add(cause); }
              if (raw is not null) try { await raw.ConfigureAwait(false); } catch (Exception cause) { errors.Add(raw.Exception ?? cause); } }
            await _closing.ObserveAllOriginalTasksAsync().ConfigureAwait(false); errors.AddRange(_closing.OriginalErrors);
            if (errors.Count != 0) throw new AggregateException("Original Home Root client/read/protocol/native cleanup remains failed.", errors);
            _transportGate.Dispose();
        }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
}
