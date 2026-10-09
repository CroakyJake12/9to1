using System.Collections.Frozen;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;

namespace Haven.Infrastructure.Native.Windows;

/// <summary>Concrete installed Home host verifier for a native app's actual connected
/// Home peer. It independently authenticates the SCM Root and its exact signed/enrolled
/// protected activation, then observes only Root's still-live controlled Home child.
/// It supplies no caller application identity, action consent or Ready boolean.</summary>
public sealed partial class NativeWindowsHomeRootHostVerifier : IHomeNativeSessionHostVerifier, IAsyncDisposable
{
    private readonly string _machineFile;
    private readonly CancellationToken _appLifetime;
    private readonly object _gate = new();
    private readonly List<Invocation> _originals = [];
    // Finite caller callbacks may escape even after their healthy invocation was joined/pruned.
    private readonly List<Exception> _originalSourceFailures = [];
    private readonly ConditionalWeakTable<OriginalHomeHostEndpoint, Invocation> _endpoints = new();
    private bool _retiring; private Task? _close;
    private sealed class Invocation
    {
        internal readonly CloudflareOriginalTaskLedger Sources = new();
        internal OriginalHostSourceScope? ScopedSource;
        internal Task Driver = null!;
        internal OriginalHomeHostEndpoint? Result;
        internal NamedPipeClientStream? Pipe;
        internal NativeWindowsHomeInstallerBootstrapAdmission? Publisher;
        internal NativeWindowsHomeInstalledRootAdmission? Admission;
        internal CancellationTokenSource? Active;
        internal Task? PipeClose, AdmissionClose, PublisherClose, ActiveClose;
        internal bool Healthy
        {
            get
            {
                var same = Driver;
                if (!same.IsCompletedSuccessfully) return false;
                same.GetAwaiter().GetResult();
                return Sources.OriginalErrors.Count == 0 && Sources.OriginalTasks.All(raw => raw.IsCompletedSuccessfully) &&
                    (Pipe is null || PipeClose?.IsCompletedSuccessfully == true) &&
                    (Admission is null || AdmissionClose?.IsCompletedSuccessfully == true) &&
                    (Publisher is null || PublisherClose?.IsCompletedSuccessfully == true) &&
                    (Active is null || ActiveClose?.IsCompletedSuccessfully == true);
            }
        }
    }
    public NativeWindowsHomeRootHostVerifier(string actualConfiguredMachineStateFile, CancellationToken actualAppLifetime)
    {
        if (!actualAppLifetime.CanBeCanceled) throw new ArgumentException("The actual owning app lifetime is required.", nameof(actualAppLifetime));
        _machineFile = Path.GetFullPath(actualConfiguredMachineStateFile); _appLifetime = actualAppLifetime;
    }
    public sealed class OriginalHomeHostEndpoint
    {
        internal readonly NativeWindowsHomeRootHostVerifier Owner;
        internal readonly HomeNativeInstalledPeer Host;
        public HomeNativeWindowsEndpoint Endpoint { get; }
        public HomeNativeSessionHostRequirement HostRequirement { get; }
        public int ControlledHomeProcessId { get; }
        internal OriginalHomeHostEndpoint(NativeWindowsHomeRootHostVerifier owner, HomeNativeInstalledPeer host,
            NativeWindowsHomeRootHostWire.Witness witness)
        {
            Owner = owner; Host = host; ControlledHomeProcessId = witness.ProcessId;
            Endpoint = new HomeNativeWindowsEndpoint(witness.Listening.PipeName);
            HostRequirement = new("home", "9to1.package:" + witness.Package.PackageId);
        }
    }
    /// <summary>Source-issued current installed Home endpoint observation. It supplies
    /// only a route/host requirement; actual connection, compatibility and READ/WRITE
    /// permission still occur through the maintained Home transport.</summary>
    public Task<OriginalHomeHostEndpoint?> ObserveOriginalEndpointAsync(CancellationToken token) =>
        Begin<OriginalHomeHostEndpoint?>(null, null, token, endpoint => endpoint);
    public bool IsIssuedOriginalEndpoint(OriginalHomeHostEndpoint same)
    {
        lock (_gate)
            return _originalSourceFailures.Count == 0 && same is not null &&
                ReferenceEquals(same.Owner, this) && _endpoints.TryGetValue(same, out var work) &&
                ReferenceEquals(work.Result, same) && work.Healthy;
    }
    private void RetainOriginalSourceFailure(Invocation work, Exception same)
    {
        work.Sources.Retain(same);
        lock (_gate)
            if (!_originalSourceFailures.Any(cause => ReferenceEquals(cause, same)))
                _originalSourceFailures.Add(same);
    }
    private void DemandOriginalSourceHealthy()
    {
        Exception[] causes; lock (_gate) causes = _originalSourceFailures.ToArray();
        if (causes.Length != 0)
            throw new AggregateException("Original host caller failures remain in verifier custody.", causes);
    }
    public ValueTask<HomeNativeInstalledPeer?> VerifyHostAsync(HomeNativeObservedPeer observed,
        HomeNativeSessionHostRequirement requirement, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(observed); ArgumentNullException.ThrowIfNull(requirement);
        return new(Begin<HomeNativeInstalledPeer?>(observed with { }, requirement with { }, token, endpoint => endpoint?.Host));
    }
    private Task<T> Begin<T>(HomeNativeObservedPeer? observed, HomeNativeSessionHostRequirement? requirement,
        CancellationToken token, Func<OriginalHomeHostEndpoint?, T> projection, Action<Action>? scope = null, Action<Task>? retain = null)
    {
        var work = new Invocation(); work.Sources.BindOriginalOwner(this);
        if (scope is not null && retain is not null)
        {
            work.ScopedSource = new(this, work, scope, retain);
            work.Sources.BindOriginalCallerCallback(work.ScopedSource.Run);
        }
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task<T> actual;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this); DemandOriginalSourceHealthy();
            _appLifetime.ThrowIfCancellationRequested(); token.ThrowIfCancellationRequested();
            _originals.RemoveAll(old => old.Healthy);
            if (_originals.Count >= 128) throw new InvalidOperationException("Unconfirmed actual Home host verification originals remain retained.");
            actual = Drive(work, begin.Task, observed, requirement, token, projection); work.Driver = actual; _originals.Add(work);
        }
        work.ScopedSource?.Publish(actual); begin.SetResult(); return actual;
    }
    private async Task<T> Drive<T>(Invocation work, Task begin, HomeNativeObservedPeer? observed,
        HomeNativeSessionHostRequirement? required, CancellationToken token, Func<OriginalHomeHostEndpoint?, T> projection)
    {
        await begin.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        OriginalHomeHostEndpoint? result = null;
        try
        {
            DemandOriginalSourceHealthy(); work.ScopedSource?.DemandHealthy();
            if (OperatingSystem.IsWindows() && (observed is null || observed.ProcessId > 0) && (required is null || required.AppId == "home"))
            {
                var source = work.Sources;
                FileHomeCoreStateStore? store = null; HomeLocalProfileIdentity? profiles = null; HomePackageDatabase? packages = null;
                source.Invoke(() =>
                {
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                    work.Active = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime, token);
                    work.Active.CancelAfter(TimeSpan.FromSeconds(15));
                    work.Pipe = new(".", NativeWindowsHomeRootServiceRuntime.OriginalHostAttestationPipeName,
                        PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
                    store = new(_machineFile); profiles = new(store, new OperatingSystemPrincipalSource()); packages = new(store);
                    work.Publisher = new(profiles);
                    work.Admission = new(work.Publisher, packages, store, profiles, _machineFile, actualConnectedRoot: work.Pipe);
                    return true;
                });
                await Take(source, () => work.Pipe!.ConnectAsync(work.Active!.Token)).ConfigureAwait(false);
                var admission = await Take(source, () => work.Admission!.InspectOriginalRuntimeWithinSourceAsync(
                    body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); }, work.Active!.Token)).ConfigureAwait(false);
                if (admission is not null && work.Admission!.IsIssuedOriginalAdmission(admission))
                {
                    var original = new NativeWindowsHomeRootWire.Request(1, Guid.NewGuid(), "observe-home-host");
                    await NativeWindowsHomeRootWire.WriteAsync(work.Pipe!, original, source, work.Active!.Token).ConfigureAwait(false);
                    var reply = await NativeWindowsHomeRootWire.ReadAsync<NativeWindowsHomeRootHostWire.Response>(work.Pipe!, source, work.Active.Token).ConfigureAwait(false);
                    if (reply is null || reply.SchemaVersion != 1 || reply.CorrelationId != original.CorrelationId)
                        throw new UnauthorizedAccessException("The authenticated Root omitted its exact original host reply.");
                    if (reply.OriginalHome is { } witness)
                    {
                        result = source.Invoke(() =>
                        {
                            var host = CaptureVerifiedHost(admission, witness, observed, required);
                            return host is null ? null : new OriginalHomeHostEndpoint(this, host, witness);
                        });
                        await Take(source, () => work.Admission.DemandOriginalCurrentWithinSourceAsync(admission,
                            body => source.Invoke(() => { body(); return true; }), raw => { _ = source.Track(raw); }, work.Active.Token)).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception cause) { work.Sources.Retain(cause); }
        finally
        {
            // All independently acquired native/transport/publisher siblings close;
            // a failed earlier close never skips another accepted original.
            if (work.Pipe is not null) await Close(() => work.Pipe.DisposeAsync().AsTask(), raw => work.PipeClose = raw).ConfigureAwait(false);
            if (work.Admission is not null) await Close(work.Admission.CloseAndDrainAsync, raw => work.AdmissionClose = raw).ConfigureAwait(false);
            if (work.Publisher is not null) await Close(work.Publisher.CloseAndDrainAsync, raw => work.PublisherClose = raw).ConfigureAwait(false);
            if (work.Active is not null)
            {
                var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); work.ActiveClose = disposed.Task; _ = work.Sources.Track(disposed.Task);
                try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { work.Active.Dispose(); return true; }); disposed.SetResult(); }
                catch (Exception cause) { disposed.SetException(cause); }
            }
        }
        try { work.ScopedSource?.ForwardRaw(); } catch (Exception cause) { work.Sources.Retain(cause); }
        await work.Sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (work.Sources.OriginalErrors.Count != 0) throw new AggregateException("Original installed Home host verification/cleanup failed and remains retained.", work.Sources.OriginalErrors);
        lock (_gate)
        {
            DemandOriginalSourceHealthy();
            if (result is not null) { work.Result = result; _endpoints.Add(result, work); }
            return projection(result);
        }
        async Task Close(Func<Task> acquire, Action<Task> capture)
        {
            Task? actual = null;
            try
            {
                if (work.ScopedSource is null)
                    work.Sources.Invoke(() => { actual = acquire(); capture(actual); _ = work.Sources.Track(actual); return true; });
                else
                {
                    // Former productive scope may be sealed. Acquire and root this
                    // actual cleanup child independently before forwarding its receipt.
                    CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
                    { actual = acquire(); capture(actual); _ = work.Sources.Track(actual); return true; });
                    work.ScopedSource.ForwardRaw();
                }
            }
            catch (Exception cause) { work.Sources.Retain(cause); }
            if (actual is not null) try { await work.Sources.AwaitAsync(actual).ConfigureAwait(false); } catch (Exception cause) { work.Sources.Capture(actual, cause); }
        }
    }
    private static HomeNativeInstalledPeer? CaptureVerifiedHost(NativeWindowsHomeInstalledRootAdmission.Observation admission,
        NativeWindowsHomeRootHostWire.Witness witness, HomeNativeObservedPeer? observed, HomeNativeSessionHostRequirement? required)
    {
        var actual = admission.Home;
        if (witness.ProcessId <= 0 || (observed is not null && (observed.ProcessId != witness.ProcessId || observed.OperatingSystemPrincipalId != witness.OsPrincipal)) ||
            witness.OsPrincipal != "windows-sid:" + admission.Enrollment.OriginalOsPrincipal ||
            (required is not null && (required.AppId != "home" || required.OperatingSystemApplicationId != "9to1.package:" + actual.Entry.PackageId)) ||
            witness.Listening.InstalledApplicationId == Guid.Empty || witness.Listening.LeaseIdentity == Guid.Empty ||
            string.IsNullOrWhiteSpace(witness.Listening.PipeName) || witness.Listening.PipeName.Length > 128 ||
            witness.Listening.PipeName.Any(value => !char.IsAsciiLetterOrDigit(value) && value is not '.' and not '-' and not '_') ||
            witness.Listening.ProfileId != witness.Listening.Actor.ProfileId || witness.Listening.Actor.OrganisationId is not null ||
            witness.Listening.InstallationRevision != NativeWindowsHomeRootHostWire.Fingerprint(actual.Entry, actual.Activation) ||
            witness.CatalogueRevision != actual.SignedArtifact.CatalogueRevision ||
            !witness.SignedDescriptor.AsSpan().SequenceEqual(actual.SignedArtifact.SignedDescriptorBytes.Span) ||
            !witness.DescriptorPayload.AsSpan().SequenceEqual(actual.SignedArtifact.DescriptorPayloadBytes.Span) ||
            JsonSerializer.Serialize(witness.Package) != JsonSerializer.Serialize(actual.Entry) ||
            JsonSerializer.Serialize(witness.Activation) != JsonSerializer.Serialize(actual.Activation) ||
            witness.EntrypointSha256 != actual.EntrypointSha256) return null;
        // This declaration admits installed service requirements structurally.
        // All native session/currentness and individual READ/WRITE consent remain
        // the actual Home API's responsibility after this real host authentication.
        return new("home", witness.Listening.InstalledApplicationId, witness.Listening.InstallationRevision,
            "sha256:" + witness.EntrypointSha256, actual.Descriptor.RequiredServiceIds.ToFrozenSet(StringComparer.Ordinal))
        { Roles = new[] { HomeNativeSessionHostRequirement.RequiredRole }.ToFrozenSet(StringComparer.Ordinal) };
    }
    private static async Task<T> Take<T>(CloudflareOriginalTaskLedger sources, Func<Task<T>> acquire)
    {
        Task<T>? raw = null;
        try { sources.Invoke(() => { raw = acquire(); _ = sources.Track(raw); return true; }); } catch (Exception cause) { sources.Retain(cause); }
        T result = default!;
        if (raw is not null) try { result = await sources.AwaitAsync(raw).ConfigureAwait(false); } catch (Exception cause) { sources.Capture(raw, cause); }
        if (sources.OriginalErrors.Count != 0) throw new AggregateException("The actual host source/callback failed.", sources.OriginalErrors);
        return raw is null ? throw new InvalidOperationException("The actual host source returned no original Task.") : result;
    }
    private static async Task Take(CloudflareOriginalTaskLedger sources, Func<Task> acquire)
    {
        Task? raw = null;
        try { sources.Invoke(() => { raw = acquire(); _ = sources.Track(raw); return true; }); } catch (Exception cause) { sources.Retain(cause); }
        if (raw is not null) try { await sources.AwaitAsync(raw).ConfigureAwait(false); } catch (Exception cause) { sources.Capture(raw, cause); }
        if (sources.OriginalErrors.Count != 0) throw new AggregateException("The actual host source/callback failed.", sources.OriginalErrors);
        if (raw is null) throw new InvalidOperationException("The actual host source returned no original Task.");
    }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public void DemandExternalOriginalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    public void RequestOriginalRetirement() { lock (_gate) _retiring = true; }
    public Task CloseAndDrainOriginalAsync()
    {
        DemandExternalOriginalJoin(); TaskCompletionSource? begin = null; Task actual;
        lock (_gate)
        {
            _retiring = true;
            if (_close is null) { begin = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Close(begin.Task); }
            actual = _close;
        }
        begin?.SetResult(); return actual;
        async Task Close(Task start)
        {
            await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            Invocation[] all; lock (_gate) all = _originals.ToArray(); var failures = new List<Exception>();
            foreach (var work in all)
            {
                try { await work.Driver.ConfigureAwait(false); } catch (Exception cause) { failures.Add(work.Driver.Exception ?? cause); }
                await work.Sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false); failures.AddRange(work.Sources.OriginalErrors);
            }
            // Independently joined invocation retirement never discards an escaped caller occurrence.
            lock (_gate) failures.AddRange(_originalSourceFailures);
            if (failures.Count != 0) throw new AggregateException("Actual Home host-verifier originals remain unconfirmed.", failures);
        }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
}
