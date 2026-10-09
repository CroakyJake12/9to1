using System.Collections.Frozen;
using System.Runtime.ExceptionServices;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>SAME canonical Home identities supplied synchronously to a trusted Home-domain
/// evidence factory. This is a Home-process composition seam, never an app IPC response.</summary>
public sealed record HomeNativeWindowsIdentityComponents(
    FileHomeCoreStateStore StateStore, HomeLocalProfileIdentity Profiles,
    HomePermissionTrustService Permissions);

/// <summary>The existing Home ownership registry/receipt authority must precede the actual
/// canonical resource resolver; no app provider receives these raw Home objects over IPC.</summary>
public sealed record HomeNativeWindowsOwnershipComponents(
    HomeNativeWindowsIdentityComponents Identity, HomeLocalStoreOwnership LocalStoreOwnership,
    HomeResourceStoreOwnershipAuthority Ownership);

/// <summary>Home-only registrations captured immediately, before original startup begins.
/// Returned evidence/providers confer no ownership or permission by registration alone.</summary>
public sealed record HomeNativeWindowsStoreRegistrations(
    IReadOnlyList<IHomeLocalStoreEvidenceProvider> Evidence,
    IReadOnlyDictionary<Type, object> Services);
public sealed record HomeNativeWindowsResolverRegistrations(
    IReadOnlyList<ICanonicalResourceAccessResolver> Resolvers,
    IReadOnlyDictionary<Type, object> Services);

/// <summary>Original Home components available to the trusted native host's owning-domain
/// registration callback. No request, compatibility response or model supplies this callback.</summary>
public sealed record HomeNativeWindowsOwnerComponents(
    FileHomeCoreStateStore StateStore,
    HomeLocalProfileIdentity Profiles,
    HomePermissionTrustService Permissions,
    ResourceAuthorizationService Resources,
    HomeResourceStoreOwnershipAuthority Ownership,
    HomeResourceOperationBroker Broker);

/// <summary>Concrete Windows Home producer over the canonical local state/profile, manual
/// permission service and private session issuer. Its immutable provider belongs to this
/// Home process. Native apps own separate providers and require issued IPC domain ports;
/// compatibility/readiness never transfers raw Home objects, store bindings or receipts.</summary>
public sealed partial class HomeNativeWindowsComposition : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _process = new();
    private readonly IAppPaths _paths;
    private readonly HomeNativeWindowsEndpoint _endpoint;
    private Task? _start;
    private Task? _close;
    private bool _closing;
    private HomeNativeWindowsBootstrap? _bootstrap;
    private Task? _originalBootstrapStart;
    private Task? _originalBootstrapClose;
    private Task? _originalIssuerClose;
    private Task? _originalRuntimeClose;

    public FileHomeCoreStateStore StateStore { get; }
    public HomeLocalProfileIdentity Profiles { get; }
    public HomePermissionTrustService Permissions { get; }
    public ResourceAuthorizationService Resources { get; }
    public HomeLocalStoreOwnership LocalStoreOwnership { get; }
    public HomeResourceStoreOwnershipAuthority Ownership { get; }
    public HomeResourceOperationBroker Broker { get; }
    public HomeNativeCoreApiSessions Sessions { get; }
    public HomeProductivityEngineService Productivity { get; }
    public HomeCoreRuntime Runtime { get; }
    public HomeCoreApi Api { get; }
    public IServiceProvider Services { get; }
    public bool InstalledPeerAdmissionConfigured { get; }
    public Task? OriginalStartTask { get { lock (_sync) return _start; } }
    public Task? OriginalCloseTask { get { lock (_sync) return _close; } }

    public HomeNativeWindowsComposition(FileHomeCoreStateStore originalStateStore,
        ITrustedHostPrincipalSource originalPrincipalSource, IAppPaths trustedHomePaths,
        HomeNativeWindowsEndpoint configuredEndpoint,
        IHomeNativeInstalledPeerVerifier? protectedInstalledPeerVerifier = null,
        IEnumerable<IHomeLocalStoreEvidenceProvider>? originalStoreEvidence = null,
        IEnumerable<ICanonicalResourceAccessResolver>? originalResourceResolvers = null,
        IEnumerable<IHomeActionPolicySource>? originalActionPolicies = null,
        Func<HomeNativeWindowsOwnerComponents, IReadOnlyDictionary<Type, object>>? configureOriginalOwners = null,
        Func<HomeNativeWindowsIdentityComponents, HomeNativeWindowsStoreRegistrations>? configureOriginalStores = null,
        Func<HomeNativeWindowsOwnershipComponents, HomeNativeWindowsResolverRegistrations>? configureOriginalResolvers = null)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The Windows Home producer requires Windows.");
        StateStore = originalStateStore ?? throw new ArgumentNullException(nameof(originalStateStore));
        ArgumentNullException.ThrowIfNull(originalPrincipalSource);
        _paths = CapturedPaths.Capture(trustedHomePaths);
        _endpoint = (configuredEndpoint ?? throw new ArgumentNullException(nameof(configuredEndpoint))).Capture();
        var evidence = Capture(originalStoreEvidence);
        var resolvers = Capture(originalResourceResolvers);
        var policies = Capture(originalActionPolicies);
        var coreReadPolicy = new HomeCoreServiceReadActionPolicies();
        Profiles = new(StateStore, originalPrincipalSource);
        Permissions = new(StateStore, ResolvePolicy);
        var identity = new HomeNativeWindowsIdentityComponents(StateStore, Profiles, Permissions);
        var stageServices = new Dictionary<Type, object>();
        if (configureOriginalStores is not null)
        {
            var stores = configureOriginalStores(identity)
                ?? throw new InvalidOperationException("The original Home evidence factory returned no registration.");
            evidence = Capture(evidence.Concat(Capture(stores.Evidence)));
            AddRegistrations(stageServices, stores.Services);
        }
        var evidenceRegistry = new HomeLocalStoreEvidenceRegistry(evidence);
        LocalStoreOwnership = new(StateStore, Profiles, evidenceRegistry, Permissions);
        Ownership = new(LocalStoreOwnership, Profiles);
        if (configureOriginalResolvers is not null)
        {
            var registered = configureOriginalResolvers(new(identity, LocalStoreOwnership, Ownership))
                ?? throw new InvalidOperationException("The original Home resolver factory returned no registration.");
            resolvers = Capture(resolvers.Concat(Capture(registered.Resolvers)));
            AddRegistrations(stageServices, registered.Services);
        }
        Resources = new(Profiles, resolvers);
        Broker = new(Resources, Permissions);
        var installed = protectedInstalledPeerVerifier ?? new UnavailableHomeNativeInstalledPeerVerifier();
        InstalledPeerAdmissionConfigured = installed is IHomeNativeInstalledPeerOriginalActorVerifier;
        HomeCoreApi? api = null;
        Sessions = new(Permissions, Profiles, installed,
            () => api ?? throw new InvalidOperationException("The original Home API has not been constructed."));
        Productivity = new();
        var permissionsService = new HomePermissionsCoreService(Permissions, Profiles);
        Runtime = new([new HomeCoreStateService(StateStore), permissionsService, Productivity], Sessions);
        Api = api = new(Runtime, Sessions, Profiles);
        var components = new Dictionary<Type, object>
        {
            [typeof(FileHomeCoreStateStore)] = StateStore,
            [typeof(IHomeCoreStateStore)] = StateStore,
            [typeof(ITrustedHostPrincipalSource)] = originalPrincipalSource,
            [typeof(HomeLocalProfileIdentity)] = Profiles,
            [typeof(IAuthenticatedResourceActorSource)] = Profiles,
            [typeof(HomePermissionTrustService)] = Permissions,
            [typeof(ResourceAuthorizationService)] = Resources,
            [typeof(HomeLocalStoreOwnership)] = LocalStoreOwnership,
            [typeof(HomeResourceStoreOwnershipAuthority)] = Ownership,
            [typeof(IResourceStoreOwnershipReceiptAuthority)] = Ownership,
            [typeof(IHomeLocalStoreEvidenceSource)] = evidenceRegistry,
            [typeof(HomeResourceOperationBroker)] = Broker,
            [typeof(HomeNativeCoreApiSessions)] = Sessions,
            [typeof(IHomeCoreAuthorization)] = Sessions,
            [typeof(IHomeNativeInstalledPeerVerifier)] = installed,
            [typeof(HomeProductivityEngineService)] = Productivity,
            [typeof(IHomeProductivityEngine)] = Productivity.Engine,
            [typeof(HomePermissionsCoreService)] = permissionsService,
            [typeof(HomeCoreRuntime)] = Runtime,
            [typeof(HomeCoreApi)] = Api,
            [typeof(IHomeCoreApi)] = Api,
            [typeof(IAppPaths)] = _paths
        };
        AddRegistrations(components, stageServices);
        if (configureOriginalOwners is not null)
            AddRegistrations(components,
                configureOriginalOwners(new(StateStore, Profiles, Permissions, Resources, Ownership, Broker))
                    ?? throw new InvalidOperationException("The original Home-domain registration returned no components."));
        Services = new OriginalProvider(components.ToFrozenDictionary());

        HomePermissionActionPolicy? ResolvePolicy(string appId, string actionId)
        {
            var core = coreReadPolicy.TryGet(appId, actionId);
            HomePermissionActionPolicy? registered = null;
            foreach (var policy in policies)
            {
                var current = policy.TryGet(appId, actionId);
                if (current is null) continue;
                if (core is not null || registered is not null) return null;
                registered = current;
            }
            return core ?? registered;
        }
    }

    /// <summary>Authoring candidate routing only. Actual OS-local state and principal are used;
    /// protected installed-peer admission remains unavailable until the host supplies its verifier.</summary>
    public static HomeNativeWindowsComposition CreateCandidate() =>
        new(FileHomeCoreStateStore.CreateDefault(), new OperatingSystemPrincipalSource(),
            CapturedPaths.CreateCandidate(), new("9to1.home.candidate.v1"));

    public Task StartOriginalAsync()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing || _originalProcessRetiring, this);
            if (_start is not null) return _start;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _start = StartCoreAsync(gate.Task);
            gate.SetResult();
            return _start;
        }
    }

    private async Task StartCoreAsync(Task gate)
    {
        await gate.ConfigureAwait(false);
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var actor = await Profiles.GetCurrentAsync(RunOriginalHomeProcessSource, RetainOriginalHomeProcessSource, _process.Token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original OS-local Home profile is unavailable.");
        HomeNativeWindowsBootstrap bootstrap;
        Task bootstrapStart;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            _process.Token.ThrowIfCancellationRequested();
            bootstrap = _bootstrap = HomeNativeWindowsBootstrap.Start(
                new(Services, Runtime, Profiles), _paths, _endpoint, _process.Token);
            bootstrapStart = _originalBootstrapStart = bootstrap.OriginalStartTask;
        }
        await bootstrapStart.ConfigureAwait(false);
        if (actor != await Profiles.GetCurrentAsync(RunOriginalHomeProcessSource, RetainOriginalHomeProcessSource, _process.Token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original Home profile retired during startup.");
        _process.Token.ThrowIfCancellationRequested();
        lock (_sync) ObjectDisposedException.ThrowIf(_closing, this);
    }

    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalProcessJoin();
        lock (_sync)
        {
            if (_close is not null) return _close;
            _closing = true;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseCoreAsync(gate.Task);
            gate.SetResult();
            return _close;
        }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private async Task CloseCoreAsync(Task gate)
    {
        await gate.ConfigureAwait(false);
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        List<Exception> failures = [];
        // All accepted finite Root listener reads are terminal before the SAME
        // bootstrap/listener/lease they borrowed can be disposed.
        await JoinOriginalRootListeningAsync(failures).ConfigureAwait(false);
        lock (_sync)
            try { if (_bootstrap is not null) _originalBootstrapClose = _bootstrap.CloseAndDrainAsync(); }
            catch (Exception error) { Add(error); }
        // With an acquired bootstrap, its SAME close first seals/cancels its owned listener.
        // Canceling the original process token sooner would turn healthy close into caller cancellation.
        if (_originalBootstrapClose is null)
            try { _process.Cancel(); } catch (Exception error) { Add(error); }
        try { if (_start is not null) await _start.ConfigureAwait(false); } catch (Exception error) { Add(error); }
        try { if (_originalBootstrapStart is not null) await _originalBootstrapStart.ConfigureAwait(false); }
        catch (Exception error) { Add(error); }
        try { if (_originalBootstrapClose is not null) await _originalBootstrapClose.ConfigureAwait(false); }
        catch (Exception error) { Add(error); }
        try { _process.Cancel(); } catch (Exception error) { Add(error); }
        try { _originalIssuerClose = Sessions.DisposeAsync().AsTask(); } catch (Exception error) { Add(error); }
        try { if (_originalIssuerClose is not null) await _originalIssuerClose.ConfigureAwait(false); }
        catch (Exception error) { Add(error); }
        try { _originalRuntimeClose = Runtime.DisposeAsync().AsTask(); } catch (Exception error) { Add(error); }
        try { if (_originalRuntimeClose is not null) await _originalRuntimeClose.ConfigureAwait(false); }
        catch (Exception error) { Add(error); }
        await JoinOriginalHomeProcessSourcesAsync(failures).ConfigureAwait(false);
        try { _process.Dispose(); } catch (Exception error) { Add(error); }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Original Windows Home startup and shutdown failed.", failures);
        void Add(Exception error)
        {
            if (!failures.Any(original => ReferenceEquals(original, error))) failures.Add(error);
        }
    }

    private static T[] Capture<T>(IEnumerable<T>? originals) where T : class
    {
        if (originals is null) return [];
        List<T> rows = [];
        foreach (var original in originals)
        {
            if (original is null || rows.Count == 128)
                throw new ArgumentException("Original component registrations are missing or exceed capacity.");
            rows.Add(original);
        }
        return rows.ToArray();
    }

    private static void AddRegistrations(Dictionary<Type, object> originals,
        IReadOnlyDictionary<Type, object> registered)
    {
        ArgumentNullException.ThrowIfNull(registered);
        if (registered.Count > 128)
            throw new InvalidOperationException("The original Home-domain registration exceeds capacity.");
        foreach (var row in registered)
            if (row.Key is null || row.Value is null || !row.Key.IsInstanceOfType(row.Value) ||
                originals.Count >= 256 || !originals.TryAdd(row.Key, row.Value))
                throw new InvalidOperationException("A Home-domain registration is invalid or replaces an original component.");
    }

    private sealed class OriginalProvider(FrozenDictionary<Type, object> components) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            return components.GetValueOrDefault(serviceType);
        }
    }

    private sealed record CapturedPaths(string DataDirectory, string DatabasePath,
        string BrowserProfileDirectory, string AttachmentsDirectory, string LogsDirectory,
        string LegacyStatePath) : IAppPaths
    {
        internal static CapturedPaths Capture(IAppPaths original)
        {
            ArgumentNullException.ThrowIfNull(original);
            return new(Absolute(original.DataDirectory), Absolute(original.DatabasePath),
                Absolute(original.BrowserProfileDirectory), Absolute(original.AttachmentsDirectory),
                Absolute(original.LogsDirectory), Absolute(original.LegacyStatePath));
        }
        internal static CapturedPaths CreateCandidate()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
                throw new InvalidOperationException("Windows did not provide a local application data directory.");
            var data = Path.Combine(root, "9to1");
            return new(data, Path.Combine(data, "haven.db"), Path.Combine(data, "BrowserProfile"),
                Path.Combine(data, "Attachments"), Path.Combine(data, "Logs"), Path.Combine(data, "state.json"));
        }
        private static string Absolute(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
                throw new ArgumentException("The original Home host paths must be absolute.");
            return Path.GetFullPath(value);
        }
    }
}
