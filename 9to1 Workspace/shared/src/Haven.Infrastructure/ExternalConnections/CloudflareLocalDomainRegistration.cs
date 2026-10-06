using Haven.Application;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure;

/// <summary>Explicit Linux Home-domain/Cloudflare composition. This supplies no installed
/// peer, publisher, account, OAuth credential or permission grant. The actual user still
/// reviews each Home request. Shared Home disposal follows all actual business borrowers.</summary>
public sealed class CloudflareLocalDomainRegistration
{
    private readonly object _gate = new();
    private readonly CloudflareOriginalTaskLedger _requests = new(), _closing = new(), _homeSources = new();
    private readonly HomeCloudflareResourceResolver _resolver;
    private readonly HomeCloudflareActionPolicySource _policy = new();
    private readonly IAppPaths _paths;
    private readonly ITrustedHostPrincipalSource _principal;
    private HomeCloudflareServiceOwner? _owner;
    private HomeCloudflareServiceOwner? _constructedOwner;
    private IServiceProvider? _provider;
    private TaskRunCanonicalProcessRetirementOwner? _canonical;
    private Task? _start, _close, _homeClose;
    private int _retiring;
    private bool _configured;
    public HomeLocalDomainComposition OriginalHome { get; private set; } = null!;
    public Task? OriginalStartTask => Volatile.Read(ref _start);

    private CloudflareLocalDomainRegistration(IAppPaths paths, ITrustedHostPrincipalSource principal)
    {
        _paths = paths; _principal = principal;
        _resolver = new(() => Volatile.Read(ref _owner)
            ?? throw new InvalidOperationException("Resolve the SAME local Cloudflare owner before its resource policy."));
        _requests.BindOriginalOwner(this); _closing.BindOriginalOwner(this); _homeSources.BindOriginalOwner(this);
    }

    public static CloudflareLocalDomainRegistration CreateOriginal(FileHomeCoreStateStore originalStore,
        ITrustedHostPrincipalSource originalPrincipal, IAppPaths originalPaths,
        IEnumerable<IHomeLocalStoreEvidenceProvider>? originalEvidence = null,
        IEnumerable<ICanonicalResourceAccessResolver>? originalResolvers = null,
        IEnumerable<IHomeActionPolicySource>? originalPolicies = null,
        Func<HomeLocalDomainOwnerComponents, IReadOnlyDictionary<Type, object>>? configureOriginalOwners = null,
        Func<HomeLocalDomainIdentityComponents, HomeLocalDomainStoreRegistrations>? configureOriginalStores = null,
        Func<HomeLocalDomainOwnershipComponents, HomeLocalDomainResolverRegistrations>? configureOriginalResolvers = null)
    {
        ArgumentNullException.ThrowIfNull(originalStore);
        ArgumentNullException.ThrowIfNull(originalPrincipal);
        ArgumentNullException.ThrowIfNull(originalPaths);
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The local domain Cloudflare owner requires Linux.");
        var original = new CloudflareLocalDomainRegistration(originalPaths, originalPrincipal);
        original.OriginalHome = original.Invoke(() => new HomeLocalDomainComposition(originalStore, originalPrincipal,
            originalEvidence, (originalResolvers ?? []).Append(original._resolver), (originalPolicies ?? []).Append(original._policy),
            configureOriginalOwners, configureOriginalStores, configureOriginalResolvers,
            originalSynchronousScope: body => original.Invoke(() => { body(); return true; }),
            retainOriginalTask: original.RetainHomeOriginal));
        return original;
    }

    /// <summary>Call after the maintained AddHavenInfrastructure and before BuildServiceProvider.
    /// Only its uninstantiated default paths/Windows-secret descriptors may be replaced by
    /// these explicit supplied local owners. No existing Home or Cloudflare graph is adopted.</summary>
    public void ConfigureOriginalServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Invoke(() =>
        {
            lock (_gate)
            {
                if (_configured || Volatile.Read(ref _retiring) != 0)
                    throw new InvalidOperationException("Configure this original local registration once before admission.");
                Type[] tuple = [typeof(HomeLocalDomainComposition), typeof(HomeNativeWindowsComposition),
                    typeof(FileHomeCoreStateStore), typeof(IHomeCoreStateStore), typeof(HomeLocalProfileIdentity),
                    typeof(IAuthenticatedResourceActorSource), typeof(HomePermissionTrustService),
                    typeof(ResourceAuthorizationService), typeof(HomeResourceOperationBroker),
                    typeof(HomeResourceStoreOwnershipAuthority), typeof(IResourceStoreOwnershipAuthority),
                    typeof(HomeLocalStoreOwnership), typeof(ITrustedHostPrincipalSource),
                    typeof(IResourceStoreOwnershipReceiptAuthority), typeof(HomeCloudflareServiceOwner),
                    typeof(HomeCloudflareResourceResolver), typeof(HomeCloudflareActionPolicySource)];
                if (tuple.Any(type => services.Any(row => row.ServiceType == type)))
                    throw new InvalidOperationException("A Home/Cloudflare tuple is already configured; no parallel or substituted owner is permitted.");
                var paths = Single(services, typeof(IAppPaths));
                if (paths.Lifetime != ServiceLifetime.Singleton ||
                    !(ReferenceEquals(paths.ImplementationInstance, _paths) || paths.ImplementationType == typeof(AppPaths)))
                    throw new InvalidOperationException("Use the maintained default path descriptor or the SAME supplied paths instance.");
                var secret = Single(services, typeof(IProviderSecretStore));
                if (secret.Lifetime != ServiceLifetime.Singleton || secret.ImplementationType != typeof(WindowsProviderSecretStore) ||
                    services.Any(row => row.ServiceType == typeof(LinuxProviderSecretStore)))
                    throw new InvalidOperationException("The maintained uninstantiated default secret descriptor is required for explicit Linux configuration.");
                if (!services.Any(row => row.ServiceType == typeof(TaskRunCanonicalProcessRetirementOwner)))
                    throw new InvalidOperationException("Use the SAME maintained canonical process cohort.");
                services.Remove(paths); services.AddSingleton<IAppPaths>(_paths);
                services.Remove(secret);
                services.AddSingleton<LinuxProviderSecretStore>(provider => Invoke(() =>
                {
                    if (!ReferenceEquals(provider.GetRequiredService<IAppPaths>(), _paths))
                        throw new UnauthorizedAccessException("The protected credential owner requires SAME actual paths.");
                    return new LinuxProviderSecretStore(_paths);
                }));
                services.AddSingleton<IProviderSecretStore>(provider => provider.GetRequiredService<LinuxProviderSecretStore>());
                services.AddSingleton(this); services.AddSingleton(OriginalHome);
                services.AddSingleton(OriginalHome.StateStore); services.AddSingleton<IHomeCoreStateStore>(OriginalHome.StateStore);
                services.AddSingleton(OriginalHome.Profiles); services.AddSingleton<IAuthenticatedResourceActorSource>(OriginalHome.Profiles);
                services.AddSingleton<ITrustedHostPrincipalSource>(_principal);
                services.AddSingleton(OriginalHome.Permissions); services.AddSingleton(OriginalHome.Resources);
                services.AddSingleton(OriginalHome.Broker); services.AddSingleton(OriginalHome.Ownership);
                services.AddSingleton<IResourceStoreOwnershipAuthority>(OriginalHome.Ownership);
                services.AddSingleton<IResourceStoreOwnershipReceiptAuthority>(OriginalHome.Ownership);
                services.AddSingleton(OriginalHome.LocalStoreOwnership);
                // The legacy tuple is a reference carrier only. No Windows composition,
                // installed verifier/session or native endpoint is created on this path.
                services.AddHavenOwnedCloudflareTaskTools(RequireOriginalHomeComponents);
                ReplaceSingle(services, typeof(HomeCloudflareResourceResolver), ServiceDescriptor.Singleton(_resolver));
                ReplaceSingle(services, typeof(HomeCloudflareActionPolicySource), ServiceDescriptor.Singleton(_policy));
                var ownerDescriptor = Single(services, typeof(HomeCloudflareServiceOwner));
                var actualFactory = ownerDescriptor.ImplementationFactory
                    ?? throw new InvalidOperationException("The maintained actual Cloudflare owner factory is required.");
                ReplaceSingle(services, typeof(HomeCloudflareServiceOwner), ServiceDescriptor.Singleton<HomeCloudflareServiceOwner>(provider =>
                    Invoke(() =>
                    {
                        var acquired = actualFactory(provider) as HomeCloudflareServiceOwner
                            ?? throw new InvalidOperationException("The original Cloudflare factory returned no owner.");
                        // Capture the actual product before any later binding failure.
                        lock (_gate)
                        {
                            if (_constructedOwner is not null && !ReferenceEquals(_constructedOwner, acquired))
                                throw new InvalidOperationException("The original Cloudflare factory owner changed.");
                            _constructedOwner = acquired;
                        }
                        // No registration lock is acquired by this deny-only predicate.
                        acquired.BindOriginalHostStartup(() => Volatile.Read(ref _retiring) == 0 &&
                            Volatile.Read(ref _start)?.IsCompletedSuccessfully == true);
                        lock (_gate) _owner = acquired;
                        return acquired;
                    })));
                _configured = true;
            }
            return true;
        });
    }

    private static ServiceDescriptor Single(IServiceCollection services, Type type)
    {
        var rows = services.Where(row => row.ServiceType == type).Take(2).ToArray();
        return rows.Length == 1 ? rows[0] : throw new InvalidOperationException("Exactly one original descriptor is required for " + type.Name + ".");
    }
    private static void ReplaceSingle(IServiceCollection services, Type type, ServiceDescriptor replacement)
    { services.Remove(Single(services, type)); services.Add(replacement); }

    private HomeNativeWindowsOwnerComponents RequireOriginalHomeComponents(IServiceProvider provider) => Invoke(() =>
    {
        lock (_gate)
        {
            if (!_configured || Volatile.Read(ref _retiring) != 0)
                throw new InvalidOperationException("The original local Home tuple is unavailable or retired.");
            if (!ReferenceEquals(provider.GetRequiredService<HomeLocalDomainComposition>(), OriginalHome) ||
                !ReferenceEquals(provider.GetRequiredService<IAppPaths>(), _paths) ||
                !OriginalHome.IsBoundToOriginalComposition(provider.GetRequiredService<FileHomeCoreStateStore>(),
                    provider.GetRequiredService<HomeLocalProfileIdentity>(), provider.GetRequiredService<HomePermissionTrustService>(),
                    provider.GetRequiredService<ResourceAuthorizationService>(), provider.GetRequiredService<HomeResourceStoreOwnershipAuthority>(),
                    provider.GetRequiredService<HomeResourceOperationBroker>()))
                throw new UnauthorizedAccessException("The SAME original local Home tuple is required.");
            return new HomeNativeWindowsOwnerComponents(OriginalHome.StateStore, OriginalHome.Profiles,
                OriginalHome.Permissions, OriginalHome.Resources, OriginalHome.Ownership, OriginalHome.Broker);
        }
    });

    /// <summary>Capture before startup/admission. This binds custody, never approval.</summary>
    public HomeCloudflareServiceOwner CaptureOriginalOwner(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return Invoke(() =>
        {
            lock (_gate)
            {
                if (!_configured || Volatile.Read(ref _retiring) != 0) throw new InvalidOperationException("The configured local owner is unavailable.");
                if (_provider is not null && !ReferenceEquals(_provider, provider)) throw new InvalidOperationException("The original provider changed.");
                _provider = provider;
                var canonical = provider.GetRequiredService<TaskRunCanonicalProcessRetirementOwner>();
                if (_canonical is not null && !ReferenceEquals(_canonical, canonical)) throw new InvalidOperationException("The original canonical cohort changed.");
                _canonical = canonical;
                var acquired = provider.GetRequiredService<HomeCloudflareServiceOwner>();
                if (_owner is not null && !ReferenceEquals(_owner, acquired)) throw new InvalidOperationException("The original Cloudflare owner changed.");
                _owner = acquired;
                _ = RequireOriginalHomeComponents(provider);
                return acquired;
            }
        });
    }

    public Task StartOriginalHomeAsync()
    {
        DemandExternalOriginalCloudflareJoin();
        TaskCompletionSource begin; Task actual;
        lock (_gate)
        {
            if (!_configured || _owner is null || _canonical is null || Volatile.Read(ref _retiring) != 0)
                throw new InvalidOperationException("Capture the SAME configured local owners before actual Home startup.");
            if (_start is not null) return _start;
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = _start = StartPublishedAsync(begin.Task);
        }
        begin.SetResult(); return actual;
    }
    private async Task StartPublishedAsync(Task begin)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this);
        Task? raw = null;
        try { _ = originals.Invoke(() => { raw = OriginalHome.StartOriginalAsync(); return raw; }); }
        catch (Exception cause) { originals.Retain(cause); }
        if (raw is not null) try { await originals.AwaitAsync(raw).ConfigureAwait(false); } catch (Exception cause) { originals.Capture(raw, cause); }
        await originals.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (originals.OriginalErrors.Count != 0)
        {
            if (raw?.IsCanceled == true && originals.OriginalErrors.All(cause => cause is OperationCanceledException))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(originals.OriginalErrors[0]).Throw();
            throw new AggregateException("The actual local Home startup source failed.", originals.OriginalErrors);
        }
        if (Volatile.Read(ref _retiring) != 0) throw new ObjectDisposedException(nameof(CloudflareLocalDomainRegistration));
    }

    public void DemandExternalOriginalCloudflareJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        OriginalHome.DemandExternalOriginalProcessJoin();
        HomeCloudflareServiceOwner? owner; TaskRunCanonicalProcessRetirementOwner? canonical;
        lock (_gate) { owner = _constructedOwner; canonical = _canonical; }
        owner?.DemandExternalOriginalHostJoin(); canonical?.DemandExternalOriginalProcessJoin();
    }
    public void RequestOriginalCloudflareRetirement()
    {
        HomeCloudflareServiceOwner? owner;
        lock (_gate) { Volatile.Write(ref _retiring, 1); owner = _constructedOwner; }
        try { _requests.Invoke(() => { OriginalHome.RequestOriginalProcessRetirement(); return true; }); }
        catch (Exception cause) { _requests.Retain(cause); }
        finally { if (OriginalHome.OriginalProcessRetirementRequestTask is { } request) _ = _requests.Track(request); }
        if (owner is not null)
            try { _requests.Invoke(() => { owner.RequestOriginalHostRetirement(); return true; }); }
            catch (Exception cause) { _requests.Retain(cause); }
        if (_requests.OriginalErrors.Count != 0) throw new AggregateException("Original local retirement requests failed.", _requests.OriginalErrors);
    }
    public Task CloseAndDrainOriginalCloudflareAsync()
    {
        DemandExternalOriginalCloudflareJoin();
        TaskCompletionSource begin; Task actual;
        lock (_gate)
        {
            if (_close is not null) return _close;
            Volatile.Write(ref _retiring, 1); begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = _close = ClosePublishedAsync(begin.Task);
        }
        begin.SetResult(); return actual;
    }
    private async Task ClosePublishedAsync(Task begin)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        try { RequestOriginalCloudflareRetirement(); } catch (Exception cause) { _closing.Retain(cause); }
        TaskRunCanonicalProcessRetirementOwner? canonical; HomeCloudflareServiceOwner? owner; Task? start;
        lock (_gate) { canonical = _canonical; owner = _constructedOwner; start = _start; }
        // Business owners settle before the actual CF permission/entry/body inventory.
        if (canonical is not null)
            try { await _closing.AwaitAsync(_closing.Invoke(canonical.CloseAndSuspendOriginalProducersAsync)).ConfigureAwait(false); }
            catch (Exception cause) { _closing.Retain(cause); }
        if (owner is not null)
            try { await _closing.AwaitAsync(_closing.Invoke(owner.CloseAndDrainOriginalHostAsync)).ConfigureAwait(false); }
            catch (Exception cause) { _closing.Retain(cause); }
        await _requests.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        foreach (var cause in _requests.OriginalErrors) _closing.Retain(cause);
        if (start is not null) try { await _closing.AwaitAsync(start).ConfigureAwait(false); } catch (Exception cause) { _closing.Capture(start, cause); }
        await _homeSources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        foreach (var cause in _homeSources.OriginalErrors) _closing.Retain(cause);
        await _closing.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (_closing.OriginalErrors.Count != 0) throw new AggregateException("Actual local Cloudflare originals failed.", _closing.OriginalErrors);
    }

    /// <summary>The owning runner calls this only after independently joining all actual
    /// canonical/Dev/Files/Cloudflare borrowers. CF completion cannot certify their joins.</summary>
    public Task CloseOriginalHomeAfterBorrowersAsync()
    {
        DemandExternalOriginalCloudflareJoin();
        TaskCompletionSource begin; Task actual;
        lock (_gate)
        {
            if (_homeClose is not null) return _homeClose;
            Volatile.Write(ref _retiring, 1); begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = _homeClose = CloseHomePublishedAsync(begin.Task);
        }
        begin.SetResult(); return actual;
    }
    private async Task CloseHomePublishedAsync(Task begin)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this);
        Task? raw = null;
        try { _ = originals.Invoke(() => { raw = OriginalHome.CloseAndDrainAsync(); return raw; }); }
        catch (Exception cause) { originals.Retain(cause); }
        if (raw is not null) try { await originals.AwaitAsync(raw).ConfigureAwait(false); } catch (Exception cause) { originals.Capture(raw, cause); }
        await originals.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        await _homeSources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        foreach (var cause in _homeSources.OriginalErrors) originals.Retain(cause);
        if (originals.OriginalErrors.Count != 0) throw new AggregateException("Actual borrowed Home close failed.", originals.OriginalErrors);
    }
    private void RetainHomeOriginal(Task actual) => Invoke(() => { _ = _homeSources.Track(actual); return true; });
    private T Invoke<T>(Func<T> finite) => CloudflareOriginalExecutionGuard.InvokeOriginal(this, finite);
}
