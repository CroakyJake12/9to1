using System.Collections.Frozen;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Infrastructure.Native.Windows;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;

namespace Haven.Infrastructure.Native.Linux;

/// <summary>Home-side client for its actual registered Linux Root and controlled
/// child channel. Root independently enforces publisher/activation/launch policy;
/// metadata, UID, socket presence and this constructor confer no installed role.</summary>
public sealed class LinuxHomeRootClient : IHomeNativeInstalledPeerOriginalActorVerifier,
    IHomeNativeSessionHostVerifier, IInstalledApplicationOriginalScopedObservationProvider, IAsyncDisposable
{
    public const string OriginalProviderId = "9to1.home.original-linux-root-installed";
    public string ProviderId => OriginalProviderId;
    private readonly string _endpoint;
    private readonly CancellationTokenSource _lifetime;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _wire = new(1, 1);
    private readonly Dictionary<Task, CanonicalSqliteOriginalSourceScope> _operations = new(ReferenceEqualityComparer.Instance);
    private readonly List<Exception> _unexpected = [];
    private Socket? _socket;
    private NetworkStream? _stream;
    private HomeNativePeerObservation.OriginalUnixProcess? _rootProcess;
    private HomeNativeObservedPeer? _rootPeer;
    private FileHomeCoreStateStore? _homeStore;
    private HomeLocalProfileIdentity? _profiles;
    private HomeInstalledApplicationRegistry? _registry;
    private HomeNativeWindowsComposition? _home;
    private HomeNativeWindowsComposition.OriginalRootListeningObservation? _actualListening;
    private InstalledApplicationReference? _homeRow;
    private NativeWindowsHomeRootWire.Listening? _listening;
    private HomePackageOriginalInstalledActivationRecord? _homeActivation;
    private string[] _packageIds = [];
    private string? _homePackageId, _catalogueRevision;
    private Task? _connect, _close;
    private bool _retiring, _channelFailed;

    public LinuxHomeRootClient(string originalControlEndpoint, CancellationToken actualProcessLifetime)
    {
        if (!OperatingSystem.IsLinux() || !actualProcessLifetime.CanBeCanceled || !Path.IsPathFullyQualified(originalControlEndpoint) ||
            System.Text.Encoding.UTF8.GetByteCount(originalControlEndpoint) > 100)
            throw new ArgumentException("The actual Linux child process lifetime and explicit bounded original Root endpoint are required.");
        _endpoint = Path.GetFullPath(originalControlEndpoint); _lifetime = CancellationTokenSource.CreateLinkedTokenSource(actualProcessLifetime);
    }
    public void BindOriginalHomeInventory(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles, HomeInstalledApplicationRegistry registry)
    {
        lock (_gate)
        {
            DemandActive();
            if (!registry.HasOriginalComposition(store, profiles, [this]) || _homeStore is not null)
                throw new UnauthorizedAccessException("Bind this SAME Home/store/profile/index tuple exactly once.");
            _homeStore = store; _profiles = profiles; _registry = registry;
        }
    }
    public bool HasOriginalHomeInventory(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles, HomeInstalledApplicationRegistry registry)
        => ReferenceEquals(_homeStore, store) && ReferenceEquals(_profiles, profiles) && ReferenceEquals(_registry, registry);

    public Task ConnectOriginalWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        lock (_gate) return _connect ??= Admit(scope, retain, async (source, lifetime) => {
            if (_profiles is null || _registry is null) throw new UnauthorizedAccessException("The SAME original Home inventory must be bound before connecting.");
            source.Run(() => _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified));
            await source.Read(() => _socket!.ConnectAsync(new UnixDomainSocketEndPoint(_endpoint), lifetime).AsTask()).ConfigureAwait(false);
            source.Run(() => {
                _rootProcess = HomeNativePeerObservation.RetainOriginalUnixServiceLifetime(_socket!)
                    ?? throw new UnauthorizedAccessException("The actual connected Root process lifetime is unavailable.");
                _rootPeer = _rootProcess.ObserveCurrent(); DemandRoot();
                _stream = new NetworkStream(_socket!, ownsSocket: false);
            });
            var response = await Exchange(source, new(1, Guid.NewGuid(), "hello-home"), lifetime).ConfigureAwait(false);
            var root = response.Activation;
            var ids = response.PackageIds?.Take(129).ToArray();
            if (root is not { SchemaVersion: 1, AppId: "root", Platform: "linux" } || string.IsNullOrWhiteSpace(response.PackageId) ||
                ids is not { Length: >= 2 and <= 128 } || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length ||
                !ids.Contains(root.PackageId) || !ids.Contains(response.PackageId) || response.CatalogueRevision != root.CatalogueRevision)
                throw new UnauthorizedAccessException("The actual registered Root did not issue its current canonical bootstrap identities.");
            source.Run(() => { _packageIds = ids; _homePackageId = response.PackageId; _catalogueRevision = response.CatalogueRevision; });
            var home = await ReadPackage(source, _homePackageId!, lifetime).ConfigureAwait(false);
            if (home.AppId != "home") throw new UnauthorizedAccessException("The actual Root must identify this controlled Home package.");
            DemandActualHomeImage(home);
            source.Run(() => _homeActivation = home);
            return true;
        }, token);
    }
    public ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken token)
        => ObserveWithinOriginalSourceAsync(body => body(), _ => { }, token);
    public ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveWithinOriginalSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token)
        => new(Admit(scope, retain, async (source, lifetime) => {
            if (_connect?.IsCompletedSuccessfully != true) return (IReadOnlyList<InstalledApplicationProfileObservation>)Array.Empty<InstalledApplicationProfileObservation>();
            var actor = await Actor(source, lifetime).ConfigureAwait(false);
            var principal = await source.Read(() => new OperatingSystemPrincipalSource().GetPrincipalAsync(lifetime).AsTask()).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(principal)) throw new UnauthorizedAccessException("The actual local OS principal is unavailable.");
            var applications = new List<InstalledApplicationObservation>();
            foreach (var id in _packageIds)
            {
                var package = await ReadPackage(source, id, lifetime).ConfigureAwait(false);
                applications.Add(new("9to1.package:" + package.PackageId, Path.Combine(package.InstallationRoot, package.EntrypointRelativePath),
                    package.AppId, package.Version, true) { StableLaunchIdentity = package.AppId });
            }
            if (actor != await Actor(source, lifetime).ConfigureAwait(false)) throw new UnauthorizedAccessException("The actual Home actor changed during Root inventory observation.");
            return Array.AsReadOnly(new[] { new InstalledApplicationProfileObservation(principal, "Current local Linux principal", false, true,
                Array.AsReadOnly(applications.ToArray())) });
        }, token));

    public Task PublishOriginalHomeListeningWithinSourceAsync(HomeNativeWindowsComposition home, HomeInstalledApplicationRegistry registry,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => Admit(scope, retain, async (source, lifetime) => {
            if (!HasOriginalHomeInventory(home.StateStore, home.Profiles, registry) || _connect?.IsCompletedSuccessfully != true)
                throw new UnauthorizedAccessException("The SAME connected Home/store/profile/index composition is required.");
            var actual = await source.Read(() => home.ObserveOriginalRootListeningWithinSourceAsync(source.Run, source.Retain, lifetime)).ConfigureAwait(false);
            if (!home.IsIssuedOriginalRootListeningObservation(actual)) throw new UnauthorizedAccessException("The actual original Linux listener must already be owned and listening.");
            var package = await ReadPackage(source, _homePackageId!, lifetime).ConfigureAwait(false); DemandActualHomeImage(package);
            var rows = await source.Read(() => registry.RefreshForActorWithinOriginalSourceAsync(actual.Actor, source.Run, source.Retain, lifetime).AsTask()).ConfigureAwait(false);
            var principal = await source.Read(() => new OperatingSystemPrincipalSource().GetPrincipalAsync(lifetime).AsTask()).ConfigureAwait(false);
            var row = rows.SingleOrDefault(value => value.ProviderId == ProviderId && value.HomeProfileId == actual.Actor.ProfileId && value.PlatformProfileId == principal &&
                value.OsApplicationId == "9to1.package:" + package.PackageId && value.StableLaunchIdentity == "home" &&
                value.Entrypoint == Path.Combine(package.InstallationRoot, package.EntrypointRelativePath) && value.Enabled && value.ProfileAccessible)
                ?? throw new UnauthorizedAccessException("The SAME maintained Home registry must issue this actual Home identity.");
            var listening = new NativeWindowsHomeRootWire.Listening(actual.Actor, actual.ActualLease.ProfileId, actual.ActualLease.LeaseIdentity,
                actual.ActualLease.PipeName, row.ApplicationId, Fingerprint(package), actual.ActualLease.LeaseIdentity.ToString("D"));
            var acknowledged = await Exchange(source, new(1, Guid.NewGuid(), "publish-listening") { Listening = listening }, lifetime).ConfigureAwait(false);
            if (acknowledged.Listening != listening || !home.IsCurrentOriginalRootListeningObservation(actual))
                throw new UnauthorizedAccessException("The actual Root/Home listening acknowledgement is unavailable or retired.");
            source.Run(() => { _home = home; _actualListening = actual; _homeRow = row; _homeActivation = package; _listening = listening; });
            return true;
        }, token);

    public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer observed, CancellationToken token)
        => new(Admit(body => body(), _ => { }, async (source, lifetime) => {
            if (_connect?.IsCompletedSuccessfully != true) return null;
            return await Verify(source, observed, await Actor(source, lifetime).ConfigureAwait(false), lifetime).ConfigureAwait(false);
        }, token));
    public ValueTask<HomeNativeInstalledPeer?> VerifyForActorAsync(HomeNativeObservedPeer observed, AuthenticatedResourceActor expectedActor, CancellationToken token)
        => new(Admit(body => body(), _ => { }, (source, lifetime) => Verify(source, observed, expectedActor, lifetime), token));
    public ValueTask<HomeNativeInstalledPeer?> VerifyHostAsync(HomeNativeObservedPeer observed, HomeNativeSessionHostRequirement requirement, CancellationToken token)
        => new(Admit(body => body(), _ => { }, async (source, lifetime) => {
            if (requirement.AppId != "home" || requirement.OperatingSystemApplicationId != _homeRow?.OsApplicationId) return null;
            return await Verify(source, observed, await Actor(source, lifetime).ConfigureAwait(false), lifetime).ConfigureAwait(false);
        }, token));
    private async Task<HomeNativeInstalledPeer?> Verify(CanonicalSqliteOriginalSourceScope source, HomeNativeObservedPeer observed,
        AuthenticatedResourceActor expected, CancellationToken token)
    {
        // Until Root owns an actual selected-app launch, every other process is denied.
        if (_listening is null || _homeActivation is null || _homeRow is null || observed.ProcessId != Environment.ProcessId ||
            _home is null || _actualListening is null || !_home.IsCurrentOriginalRootListeningObservation(_actualListening)) return null;
        if (expected != await Actor(source, token).ConfigureAwait(false)) return null;
        var principal = await source.Read(() => new OperatingSystemPrincipalSource().GetPrincipalAsync(token).AsTask()).ConfigureAwait(false);
        if (observed.OperatingSystemPrincipalId != principal) return null;
        var current = await Exchange(source, new(1, Guid.NewGuid(), "check-home"), token).ConfigureAwait(false);
        if (current.Listening != _listening) return null;
        var row = await source.Read(() => _registry!.ResolveLaunchForActorWithinOriginalSourceAsync(_homeRow.ApplicationId, _homeRow.Revision,
            expected, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
        if (row is null || row.ProviderId != ProviderId || row.StableLaunchIdentity != "home") return null;
        DemandActualHomeImage(_homeActivation);
        if (expected != await Actor(source, token).ConfigureAwait(false) || !_home.IsCurrentOriginalRootListeningObservation(_actualListening)) return null;
        var descriptor = ParseDescriptor(_homeActivation);
        var executable = _homeActivation.ActivatedFiles.Single(value => value.RelativePath == _homeActivation.EntrypointRelativePath);
        return new("home", row.ApplicationId, Fingerprint(_homeActivation), "sha256:" + executable.Sha256,
            descriptor.RequiredServiceIds.ToFrozenSet(StringComparer.Ordinal))
            { Roles = new[] { HomeNativeSessionHostRequirement.RequiredRole }.ToFrozenSet(StringComparer.Ordinal) };
    }
    private async Task<AuthenticatedResourceActor> Actor(CanonicalSqliteOriginalSourceScope source, CancellationToken token)
        => await source.Read(() => (_profiles ?? throw new UnauthorizedAccessException("The SAME original Home profiles are not bound."))
            .GetCurrentWithinOriginalSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The actual Home actor is unavailable.");

    private async Task<HomePackageOriginalInstalledActivationRecord> ReadPackage(CanonicalSqliteOriginalSourceScope source, string id, CancellationToken token)
    {
        if (!_packageIds.Contains(id, StringComparer.Ordinal)) throw new UnauthorizedAccessException("Only the current Root's original selected package identities may be observed.");
        var response = await Exchange(source, new(1, Guid.NewGuid(), "inspect-package") { PackageId = id }, token).ConfigureAwait(false);
        var package = response.Activation ?? throw new UnauthorizedAccessException("The actual Root package observation is unavailable.");
        if (package.SchemaVersion != 1 || package.PackageId != id || package.Platform != "linux" || package.CatalogueRevision != _catalogueRevision ||
            !Path.IsPathFullyQualified(package.InstallationRoot) || !HomePackageOriginalInstalledActivationRecord.SafeRelative(package.EntrypointRelativePath))
            throw new UnauthorizedAccessException("The actual current Root package identity/layout is inconsistent.");
        var descriptor = ParseDescriptor(package);
        if (descriptor.AppId != package.AppId || descriptor.Platform != package.Platform || descriptor.Abi != package.Abi ||
            package.ActivatedFiles is not { Count: > 0 and <= 4096 } ||
            !package.ActivatedFiles.Any(value => value.RelativePath == package.EntrypointRelativePath))
            throw new UnauthorizedAccessException("The actual Root activation must match its exact signed descriptor and runtime cohort.");
        return package;
    }
    private static HomePackageArtifactDescriptor ParseDescriptor(HomePackageOriginalInstalledActivationRecord package)
        => HomePackageOriginalArtifactDescriptorParser.Parse(Convert.FromBase64String(package.SignedDescriptorBase64), Convert.FromBase64String(package.DescriptorPayloadBase64),
            package.CatalogueRevision, new(package.PackageId, HomePackageAction.Install, "linux.root.observation", package.Version, null, package.CatalogueRevision)).Descriptor;
    private static string Fingerprint(HomePackageOriginalInstalledActivationRecord package) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(package)));
    private static void DemandActualHomeImage(HomePackageOriginalInstalledActivationRecord package)
    {
        using var self = File.OpenHandle("/proc/self/exe", FileMode.Open, FileAccess.Read, FileShare.Read);
        using var directory = LinuxOriginalDownloadPaths.OpenAbsoluteDirectory(package.InstallationRoot, false);
        using var file = LinuxOriginalDownloadPaths.OpenAt(directory, package.EntrypointRelativePath, 0x80000UL | 0x800UL);
        var actual = LinuxOriginalDownloadPaths.ReadIdentity(file);
        if (actual.Uid != 0 || (actual.Mode & 0x1ff) != 0x1ed || !actual.SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(self)))
            throw new UnauthorizedAccessException("This process must execute the SAME protected Root-controlled Home entrypoint.");
    }
    private void DemandRoot()
    {
        var actual = _rootProcess?.ObserveCurrent();
        if (actual is null || actual != _rootPeer || actual.OperatingSystemPrincipalId != "unix-euid:0" ||
            !LinuxHomeRegisteredRootServiceContext.IsOriginalRegisteredProcess(actual.ProcessId))
            throw new UnauthorizedAccessException("The SAME live peer-bound registered Linux Root service is required.");
    }
    private async Task<LinuxHomeRootControlWire.Response> Exchange(CanonicalSqliteOriginalSourceScope source, LinuxHomeRootControlWire.Request request, CancellationToken token)
    {
        await source.Read(() => _wire.WaitAsync(token)).ConfigureAwait(false);
        try
        {
            source.Run(DemandRoot);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(10));
            await source.Read(() => LinuxHomeRootServiceRuntime.WriteFrame(_stream!, request, deadline.Token)).ConfigureAwait(false);
            var response = await source.Read(() => LinuxHomeRootServiceRuntime.ReadFrame<LinuxHomeRootControlWire.Response>(_stream!, deadline.Token)).ConfigureAwait(false);
            source.Run(DemandRoot);
            return response is { SchemaVersion: 1, Accepted: true } && response.CorrelationId == request.CorrelationId ? response
                : throw new UnauthorizedAccessException("The actual Root refused or did not correlate the original control command: " + response?.Reason);
        }
        catch { lock (_gate) _channelFailed = true; _socket?.Dispose(); throw; }
        finally { _wire.Release(); }
    }
    private Task<T> Admit<T>(Action<Action> scope, Action<Task> retain,
        Func<CanonicalSqliteOriginalSourceScope, CancellationToken, Task<T>> body, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var source = new CanonicalSqliteOriginalSourceScope(this, scope, retain, error => { lock (_gate) _unexpected.Add(error); }, DemandActive);
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<T> actual;
        lock (_gate)
        {
            DemandActive();
            foreach (var success in _operations.Where(value => value.Key.IsCompletedSuccessfully && value.Value.IsHealthySettled).Select(value => value.Key).ToArray()) _operations.Remove(success);
            if (_operations.Count >= 128) throw new InvalidOperationException("Unresolved original Linux Root operations remain retained.");
            actual = Drive(); _operations.Add(actual, source);
        }
        try { source.Run(() => retain(actual)); }
        catch (Exception cause) { lock (_gate) _unexpected.Add(cause); throw; }
        finally { begin.SetResult(); }
        return actual;
        async Task<T> Drive()
        {
            await begin.Task.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, token);
            T result = default!; Exception? primary = null;
            try { DemandActive(); result = await body(source, lifetime.Token).ConfigureAwait(false); } catch (Exception cause) { primary = cause; }
            var errors = new List<Exception>();
            if (primary is not null) errors.Add(primary);
            try { await source.CloseResourcesAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            if (errors.Count != 0) throw new AggregateException("The actual Linux Root source did not settle.", errors);
            return result;
        }
    }
    private void DemandActive()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_channelFailed || _unexpected.Count != 0) throw new UnauthorizedAccessException("The original Linux Root channel/callback lifetime failed and is retained.");
            _lifetime.Token.ThrowIfCancellationRequested();
        }
    }
    public Task CloseAndDrainAsync()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        if (CanonicalSqliteOriginalSourceScope.IsPhysicalSource(this)) throw new InvalidOperationException("An original Root callback cannot join its own client lifetime.");
        lock (_gate) { _retiring = true; return _close ??= Close(); }
        async Task Close()
        {
            var failures = new List<Exception>();
            try { _lifetime.Cancel(); } catch (Exception cause) { failures.Add(cause); }
            try { _socket?.Dispose(); } catch (Exception cause) { failures.Add(cause); }
            KeyValuePair<Task, CanonicalSqliteOriginalSourceScope>[] original; lock (_gate) original = _operations.ToArray();
            foreach (var pair in original)
                try { await pair.Key.ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            foreach (var pair in original)
                try { await pair.Value.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            try { _stream?.Dispose(); } catch (Exception cause) { failures.Add(cause); }
            try { _rootProcess?.Dispose(); } catch (Exception cause) { failures.Add(cause); }
            try { _lifetime.Dispose(); } catch (Exception cause) { failures.Add(cause); }
            try { _wire.Dispose(); } catch (Exception cause) { failures.Add(cause); }
            lock (_gate) failures.AddRange(_unexpected);
            if (failures.Count != 0) throw new AggregateException("Original Linux Root client retirement retained actual failures.", failures);
        }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
