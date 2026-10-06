using Haven.Application;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
namespace Haven.Infrastructure;

/// <summary>Explicit native host configuration using actual protected Home inputs. This owns
/// startup/Cloudflare original custody; it supplies no installed actor, account or OAuth grant.</summary>
public sealed class CloudflareNativeHostRegistration
{
    private readonly object _gate = new();
    private readonly CloudflareOriginalTaskLedger _sources = new(), _closing = new();
    private readonly HomeCloudflareResourceResolver _resolver;
    private readonly HomeCloudflareActionPolicySource _policy;
    private HomeCloudflareServiceOwner? _owner;
    private IServiceProvider? _provider;
    private TaskRunCanonicalProcessRetirementOwner? _canonical;
    private Task? _start, _close, _homeClose;
    private bool _retiring, _configured;
    public HomeNativeWindowsComposition OriginalHome { get; }
    private CloudflareNativeHostRegistration(HomeNativeWindowsComposition home,
        HomeCloudflareResourceResolver resolver, HomeCloudflareActionPolicySource policy,
        CloudflareOriginalTaskLedger sources)
    {
        OriginalHome = home; _resolver = resolver; _policy = policy;
        _sources = sources; _sources.BindOriginalOwner(this); _closing.BindOriginalOwner(this);
    }

    public static CloudflareNativeHostRegistration CreateOriginal(FileHomeCoreStateStore originalStore,
        ITrustedHostPrincipalSource originalPrincipal, IAppPaths originalPaths,
        HomeNativeWindowsEndpoint originalEndpoint, IHomeNativeInstalledPeerOriginalActorVerifier originalInstalledVerifier,
        IEnumerable<IHomeLocalStoreEvidenceProvider>? originalEvidence = null,
        IEnumerable<ICanonicalResourceAccessResolver>? originalResolvers = null,
        IEnumerable<IHomeActionPolicySource>? originalPolicies = null,
        Func<HomeNativeWindowsOwnerComponents, IReadOnlyDictionary<Type, object>>? configureOriginalOwners = null,
        Func<HomeNativeWindowsIdentityComponents, HomeNativeWindowsStoreRegistrations>? configureOriginalStores = null,
        Func<HomeNativeWindowsOwnershipComponents, HomeNativeWindowsResolverRegistrations>? configureOriginalResolvers = null)
    {
        ArgumentNullException.ThrowIfNull(originalInstalledVerifier);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The configured Home/OAuth native host requires Windows; ordinary local routes remain independently configured.");
        var sources = new CloudflareOriginalTaskLedger();
        CloudflareNativeHostRegistration? registration = null;
        var resolver = new HomeCloudflareResourceResolver(() => registration?._owner
            ?? throw new InvalidOperationException("Resolve the SAME configured Cloudflare owner before evaluating its resource policy."));
        var policy = new HomeCloudflareActionPolicySource();
        var principal = new OriginalPrincipal(originalPrincipal, sources);
        var home = new HomeNativeWindowsComposition(originalStore, principal, originalPaths, originalEndpoint,
            originalInstalledVerifier, originalEvidence, (originalResolvers ?? []).Append(resolver), (originalPolicies ?? []).Append(policy),
            configureOriginalOwners, configureOriginalStores, configureOriginalResolvers);
        if (!home.InstalledPeerAdmissionConfigured) throw new UnauthorizedAccessException("The actual protected installed Home peer verifier is required.");
        registration = new(home, resolver, policy, sources); return registration;
    }
    private sealed class OriginalPrincipal(ITrustedHostPrincipalSource actual, CloudflareOriginalTaskLedger sources) : ITrustedHostPrincipalSource
    {
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token)
        {
            Task<string?>? raw = null; Exception? failure = null;
            try
            {
                sources.Invoke(() =>
                {
                    raw = actual.GetPrincipalAsync(token).AsTask(); _ = sources.Track(raw); return raw;
                });
            }
            catch (Exception cause) { failure = cause; sources.Retain(cause); }
            return new(JoinAsync(raw, failure));
        }
        private async Task<string?> JoinAsync(Task<string?>? raw, Exception? failure)
        {
            string? value = null;
            if (raw is not null)
                try { value = await sources.AwaitAsync(raw).ConfigureAwait(false); }
                catch (Exception cause) { failure ??= cause; sources.Capture(raw, cause); }
            // The public factory’s physical registration owner remains bound in sources.
            // Currentness is still established by the real principal/profile, never this wrapper.
            if (failure is not null)
            {
                if (raw?.IsCanceled == true && failure is OperationCanceledException)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                throw new AggregateException("Actual configured Home principal source failed.", sources.OriginalErrors);
            }
            return value;
        }
    }

    public void ConfigureOriginalServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        lock (_gate)
        {
            if (_retiring || _configured) throw new InvalidOperationException("Configure this original native Home registration exactly once before startup.");
            Type[] tuple = [typeof(HomeNativeWindowsComposition), typeof(FileHomeCoreStateStore), typeof(IHomeCoreStateStore),
                typeof(HomeLocalProfileIdentity), typeof(IAuthenticatedResourceActorSource), typeof(HomePermissionTrustService),
                typeof(ResourceAuthorizationService), typeof(HomeResourceOperationBroker), typeof(IResourceStoreOwnershipAuthority)];
            if (tuple.Any(type => services.Any(row => row.ServiceType == type)))
                throw new InvalidOperationException("A different Home tuple is already configured; never replace or create a parallel store/profile graph.");
            services.AddSingleton(this); services.AddSingleton(OriginalHome);
            services.AddSingleton(OriginalHome.StateStore); services.AddSingleton<IHomeCoreStateStore>(OriginalHome.StateStore);
            services.AddSingleton(OriginalHome.Profiles); services.AddSingleton<IAuthenticatedResourceActorSource>(OriginalHome.Profiles);
            services.AddSingleton(OriginalHome.Permissions); services.AddSingleton(OriginalHome.Resources);
            services.AddSingleton(OriginalHome.Broker); services.AddSingleton(OriginalHome.Ownership);
            services.AddSingleton<IResourceStoreOwnershipAuthority>(OriginalHome.Ownership);
            services.AddSingleton<IResourceStoreOwnershipReceiptAuthority>(OriginalHome.Ownership);
            services.AddSingleton(OriginalHome.LocalStoreOwnership);
            services.AddHavenOwnedCloudflareTaskTools(this, _resolver, _policy);
            _configured = true;
        }
    }
    internal HomeNativeWindowsOwnerComponents RequireOriginalHomeComponents(IServiceProvider provider)
    {
        lock (_gate)
        {
            if (!_configured || _retiring) throw new InvalidOperationException("The original configured Home tuple is unavailable or retired.");
            if (!ReferenceEquals(provider.GetRequiredService<HomeNativeWindowsComposition>(), OriginalHome))
                throw new UnauthorizedAccessException("The SAME configured Home composition is required.");
            return new(OriginalHome.StateStore, OriginalHome.Profiles, OriginalHome.Permissions,
                OriginalHome.Resources, OriginalHome.Ownership, OriginalHome.Broker);
        }
    }
    public HomeCloudflareServiceOwner CaptureOriginalOwner(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var acquired = provider.GetRequiredService<HomeCloudflareServiceOwner>();
        lock (_gate)
        {
            // Retain the actual returned owner before any later configured-component checks.
            if (_owner is not null && !ReferenceEquals(acquired, _owner))
                throw new InvalidOperationException("The configured Cloudflare owner was replaced.");
            _owner = acquired;
            if (_provider is not null && !ReferenceEquals(provider, _provider))
                throw new InvalidOperationException("The original host provider was replaced.");
            _provider = provider;
        }
        var canonical = provider.GetRequiredService<TaskRunCanonicalProcessRetirementOwner>();
        lock (_gate)
        {
            if (_canonical is not null && !ReferenceEquals(_canonical, canonical)) throw new InvalidOperationException("The canonical process owner was replaced.");
            _canonical = canonical;
        }
        _ = RequireOriginalHomeComponents(provider);
        return acquired;
    }
    internal void RetainConstructedOriginalOwner(HomeCloudflareServiceOwner acquired)
    {
        lock (_gate)
        {
            if (_owner is not null && !ReferenceEquals(_owner, acquired)) throw new InvalidOperationException("The original Cloudflare producer was replaced.");
            _owner = acquired;
            acquired.BindOriginalHostStartup(() => OriginalHome.OriginalStartTask?.IsCompletedSuccessfully == true);
        }
    }
    public Task StartOriginalHomeAsync()
    {
        DemandExternalOriginalCloudflareJoin();
        lock (_gate)
        {
            if (_retiring) throw new ObjectDisposedException(nameof(CloudflareNativeHostRegistration));
            if (!_configured) throw new InvalidOperationException("Configure the SAME native services before starting Home.");
            if (_start is not null) return _start;
            _start = _sources.Invoke(OriginalHome.StartOriginalAsync); return _start;
        }
    }
    public void DemandExternalOriginalCloudflareJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        OriginalHome.DemandExternalOriginalProcessJoin();
        HomeCloudflareServiceOwner? owner; TaskRunCanonicalProcessRetirementOwner? canonical;
        lock (_gate) { owner = _owner; canonical = _canonical; }
        owner?.DemandExternalOriginalHostJoin(); canonical?.DemandExternalOriginalProcessJoin();
    }
    public void RequestOriginalCloudflareRetirement()
    {
        HomeCloudflareServiceOwner? owner;
        lock (_gate) { _retiring = true; owner = _owner; }
        // Both are request-only. Actual shared Home disposal stays downstream of all borrowers.
        OriginalHome.RequestOriginalProcessRetirement(); owner?.RequestOriginalHostRetirement();
    }
    public Task CloseAndDrainOriginalCloudflareAsync()
    {
        DemandExternalOriginalCloudflareJoin();
        lock (_gate)
        {
            if (_close is not null) return _close;
            _retiring = true; var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = ClosePublishedAsync(begin.Task); begin.SetResult(); return _close;
        }
    }
    private async Task ClosePublishedAsync(Task begin)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        try { _closing.Invoke(() => { RequestOriginalCloudflareRetirement(); return true; }); } catch (Exception cause) { _closing.Retain(cause); }
        TaskRunCanonicalProcessRetirementOwner? canonical; lock (_gate) canonical = _canonical;
        if (canonical is not null)
            try { await _closing.AwaitAsync(_closing.Invoke(canonical.CloseAndSuspendOriginalProducersAsync)).ConfigureAwait(false); }
            catch (Exception cause) { _closing.Retain(cause); }
        // Canonical caller/task originals settle before permission/audit custody is snapshotted.
        HomeCloudflareServiceOwner? owner; lock (_gate) owner = _owner;
        if (owner is not null)
            try { await _closing.AwaitAsync(_closing.Invoke(owner.CloseAndDrainOriginalHostAsync)).ConfigureAwait(false); }
            catch (Exception cause) { _closing.Retain(cause); }
        if (OriginalHome.OriginalProcessRetirementRequestTask is { } request)
            try { await _closing.AwaitAsync(request).ConfigureAwait(false); } catch (Exception cause) { _closing.Capture(request, cause); }
        if (_start is { } start)
            try { await _closing.AwaitAsync(start).ConfigureAwait(false); } catch (Exception cause) { _closing.Capture(start, cause); }
        await _sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        foreach (var cause in _sources.OriginalErrors) _closing.Retain(cause);
        await _closing.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (_closing.OriginalErrors.Count != 0) throw new AggregateException("Actual configured Cloudflare process originals failed.", _closing.OriginalErrors);
    }
    /// <summary>The actual App invokes this only AFTER its independent Dev/Files/CF/business
    /// joins. This method cannot certify those external joins or dispose their borrowed Home early.</summary>
    public Task CloseOriginalHomeAfterBorrowersAsync()
    {
        DemandExternalOriginalCloudflareJoin();
        lock (_gate) return _homeClose ??= OriginalHome.CloseAndDrainAsync();
    }
}
