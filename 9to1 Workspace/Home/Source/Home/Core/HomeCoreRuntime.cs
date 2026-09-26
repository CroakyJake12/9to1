namespace HavenOS.Home.Core;

/// <summary>Canonical Home-owned service identities and independent service contract versions.</summary>
public static class HomeCoreServiceCatalog
{
    public static readonly HomeContractVersion CurrentContractVersion = new(1, 0, 0);

    public static IReadOnlyList<HomeServiceDescriptor> CreateUnavailableDefaults() =>
    [
        Unavailable("home.core", "Home Core lifecycle and registry"),
        Unavailable("dulche.runtime", "Shared Dulche broker/runtime"),
        Unavailable("permissions.trust", "Permission and trust broker"),
        Unavailable("productivity.engine", "Shared Productivity Engine"),
        Unavailable("packages", "App/package management"),
        Unavailable("notifications", "Notifications"),
        Unavailable("search.index", "Shared search/index"),
        Unavailable("mesh", "Mesh coordination"),
        Unavailable("account.session", "Account/session capability"),
        Unavailable("settings", "Ecosystem settings"),
        Unavailable("action-graph.renderer", "Action Graph renderer"),
    ];

    private static HomeServiceDescriptor Unavailable(string id, string label) => new(
        id,
        CurrentContractVersion,
        HomeServiceLifecycleState.Unavailable,
        false,
        $"The {label} provider is not registered.");
}

/// <summary>
/// The single in-process Home Core lifecycle owner. It starts registered services in dependency
/// order, degrades only unavailable services and publishes revisioned signals for attached clients.
/// </summary>
public sealed class HomeCoreRuntime : IAsyncDisposable
{
    private readonly Dictionary<string, IHomeCoreService> _implementations;
    private readonly Dictionary<string, HomeServiceDescriptor> _services;
    private readonly IHomeCoreAuthorization _authorization;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _sync = new();
    private readonly Dictionary<long, Action<HomeCoreDependencySignal>> _subscribers = [];
    private long _revision;
    private long _nextSubscriber;
    private int _dependentLeases;
    private bool _started;
    private bool _stopped;

    public HomeCoreRuntime(
        IEnumerable<IHomeCoreService>? services = null,
        IHomeCoreAuthorization? authorization = null)
    {
        var registered = (services ?? []).ToArray();
        _implementations = new Dictionary<string, IHomeCoreService>(StringComparer.Ordinal);
        foreach (var service in registered)
        {
            ArgumentNullException.ThrowIfNull(service);
            ValidateServiceId(service.Descriptor.ServiceId);
            if (!_implementations.TryAdd(service.Descriptor.ServiceId, service))
                throw new ArgumentException($"Duplicate Home service ID '{service.Descriptor.ServiceId}'.", nameof(services));
        }

        _services = HomeCoreServiceCatalog.CreateUnavailableDefaults()
            .ToDictionary(service => service.ServiceId, StringComparer.Ordinal);
        foreach (var service in _implementations.Values)
        {
            if (!_services.ContainsKey(service.Descriptor.ServiceId))
                _services.Add(service.Descriptor.ServiceId, service.Descriptor with
                {
                    State = HomeServiceLifecycleState.Stopped,
                    IsAvailable = false,
                    Diagnostic = "Service has not started.",
                });
        }

        _authorization = authorization ?? new DenyAllHomeCoreAuthorization();
        ValidateDependencyGraph();
    }

    public HomeCoreStateSnapshot Current => Snapshot();

    public async Task<HomeCoreStateSnapshot> StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_started && !_stopped)
                return Snapshot();
            if (_stopped)
                throw new InvalidOperationException("A stopped Home Core runtime cannot be restarted; create a new runtime instance.");

            foreach (var serviceId in GetStartOrder())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var service = _implementations[serviceId];
                var descriptor = service.Descriptor;
                var blockedDependency = service.Dependencies.FirstOrDefault(dependency =>
                    !_services.TryGetValue(dependency, out var state) || !state.IsAvailable);
                if (blockedDependency is not null)
                {
                    UpdateService(descriptor with
                    {
                        State = HomeServiceLifecycleState.Unavailable,
                        IsAvailable = false,
                        Diagnostic = $"Required Home service '{blockedDependency}' is unavailable.",
                    });
                    continue;
                }

                UpdateService(descriptor with
                {
                    State = HomeServiceLifecycleState.Starting,
                    IsAvailable = false,
                    Diagnostic = null,
                });
                try
                {
                    await service.StartAsync(cancellationToken).ConfigureAwait(false);
                    UpdateService(descriptor with
                    {
                        State = HomeServiceLifecycleState.Ready,
                        IsAvailable = true,
                        Diagnostic = null,
                    });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    UpdateService(descriptor with
                    {
                        State = HomeServiceLifecycleState.Stopped,
                        IsAvailable = false,
                        Diagnostic = "Service startup was cancelled.",
                    });
                    throw;
                }
                catch (Exception exception)
                {
                    UpdateService(descriptor with
                    {
                        State = HomeServiceLifecycleState.Degraded,
                        IsAvailable = false,
                        Diagnostic = $"HomeServiceStartFailed: {exception.GetType().Name}: {exception.Message}",
                    });
                }
            }

            _started = true;
            return Snapshot();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<HomeCompatibilityResult> GetCompatibilityAsync(
        HomeCallerIdentity caller,
        HomeCompatibilityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(request);
        ValidateCompatibilityRequest(request);

        var snapshot = Snapshot();
        if (string.IsNullOrWhiteSpace(caller.StableId) ||
            string.IsNullOrWhiteSpace(caller.Origin) ||
            string.IsNullOrWhiteSpace(caller.VerificationMethod))
        {
            return Denied(request.AppId, snapshot, HomeCoreErrorCode.CallerIdentityUnverified,
                "The caller identity was not established by a trusted Home platform authenticator.");
        }

        bool allowed;
        try
        {
            allowed = await _authorization.IsAllowedAsync(
                caller,
                "9to1.Home.GetCompatibility",
                new HashSet<string>(StringComparer.Ordinal) { "home.services.read" },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            allowed = false;
        }

        if (!allowed)
            return Denied(request.AppId, snapshot, HomeCoreErrorCode.PermissionDenied,
                "Home did not authorize this caller to inspect service compatibility.");

        var accepted = new List<HomeServiceDescriptor>();
        var failures = new List<HomeCoreFailure>();
        foreach (var requirement in request.RequiredServices)
        {
            var target = $"{request.AppId}:{requirement.ServiceId}";
            if (!snapshot.Services.FirstOrDefault(item => item.ServiceId == requirement.ServiceId) is { } service)
            {
                if (requirement.Required)
                    failures.Add(new HomeCoreFailure(HomeCoreErrorCode.HomeServiceUnavailable,
                        $"Required Home service '{requirement.ServiceId}' is not registered.", target, true, "Repair or update Home."));
                continue;
            }

            if (!requirement.Accepts(service.ContractVersion))
            {
                if (requirement.Required)
                    failures.Add(new HomeCoreFailure(HomeCoreErrorCode.HomeServiceIncompatible,
                        $"Required service '{requirement.ServiceId}' has contract {service.ContractVersion}; the app requires " +
                        $"major {requirement.MajorVersion}, minor >= {requirement.MinimumMinorVersion}" +
                        (requirement.MaximumMinorVersionExclusive is { } maximum ? $" and < {maximum}." : "."),
                        target, false, "Update the app or repair/update Home."));
                continue;
            }

            if (!service.IsAvailable)
            {
                if (requirement.Required)
                    failures.Add(new HomeCoreFailure(HomeCoreErrorCode.HomeServiceUnavailable,
                        service.Diagnostic ?? $"Required Home service '{requirement.ServiceId}' is unavailable.",
                        target, true, "Repair Home or retry after the service recovers."));
                continue;
            }

            accepted.Add(service);
        }

        return new HomeCompatibilityResult(
            failures.Count == 0 ? HomeCompatibilityState.Compatible : HomeCompatibilityState.RequiresHomeRepair,
            request.AppId,
            snapshot.Revision,
            accepted,
            failures);
    }

    public void ReportServiceState(string serviceId, HomeServiceLifecycleState state, bool isAvailable, string? diagnostic = null)
    {
        if (!_services.TryGetValue(serviceId, out var current))
            throw new KeyNotFoundException($"Home service '{serviceId}' is not in the service registry.");
        if (isAvailable && state is not (HomeServiceLifecycleState.Ready or HomeServiceLifecycleState.Degraded))
            throw new ArgumentException("An available service must be Ready or Degraded.", nameof(isAvailable));
        UpdateService(current with { State = state, IsAvailable = isAvailable, Diagnostic = diagnostic });

        if (!isAvailable)
        {
            foreach (var dependent in GetDependents(serviceId))
            {
                if (_services.TryGetValue(dependent, out var descriptor) && descriptor.IsAvailable)
                    UpdateService(descriptor with
                    {
                        State = HomeServiceLifecycleState.Degraded,
                        IsAvailable = false,
                        Diagnostic = $"HomeDependencyUnavailable: '{serviceId}' became unavailable.",
                    });
            }
        }
    }

    /// <summary>Holds Home Core alive while a dependent operation or client session is active.</summary>
    public IDisposable AttachDependentClient(string stableClientId)
    {
        if (string.IsNullOrWhiteSpace(stableClientId))
            throw new ArgumentException("A stable dependent client ID is required.", nameof(stableClientId));
        lock (_sync) _dependentLeases++;
        return new DependentLease(this);
    }

    public IDisposable Subscribe(Action<HomeCoreDependencySignal> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        long id;
        HomeCoreDependencySignal initial;
        lock (_sync)
        {
            id = ++_nextSubscriber;
            _subscribers.Add(id, observer);
            initial = CreateSignal(requiresReconnect: false);
        }
        observer(initial);
        return new Subscription(this, id);
    }

    public async Task<HomeCoreFailure?> StopAsync(bool explicitlyRequested, CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stopped) return null;
            lock (_sync)
            {
                if (_dependentLeases > 0 && !explicitlyRequested)
                    return new HomeCoreFailure(HomeCoreErrorCode.HomeShutdownBlocked,
                        $"Home Core has {_dependentLeases} active dependent client or operation lease.",
                        "9to1.Home.Shutdown", true, "Close dependent apps or explicitly request a full Home service shutdown.");
            }

            var order = GetStartOrder().Reverse().ToArray();
            foreach (var serviceId in order)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var service = _implementations[serviceId];
                var descriptor = _services[serviceId];
                if (!descriptor.IsAvailable && descriptor.State != HomeServiceLifecycleState.Degraded)
                    continue;
                UpdateService(descriptor with { State = HomeServiceLifecycleState.Stopped, IsAvailable = false });
                try
                {
                    await service.StopAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    UpdateService(_services[serviceId] with
                    {
                        State = HomeServiceLifecycleState.Degraded,
                        IsAvailable = false,
                        Diagnostic = $"HomeServiceStopFailed: {exception.GetType().Name}: {exception.Message}",
                    });
                }
            }

            _stopped = true;
            return null;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(explicitlyRequested: true).ConfigureAwait(false);
        _lifecycleGate.Dispose();
    }

    private HomeCoreStateSnapshot Snapshot()
    {
        lock (_sync)
            return new HomeCoreStateSnapshot(_revision,
                _services.Values.OrderBy(item => item.ServiceId, StringComparer.Ordinal).ToArray(),
                DateTimeOffset.UtcNow);
    }

    private void UpdateService(HomeServiceDescriptor descriptor)
    {
        Action<HomeCoreDependencySignal>[] observers;
        HomeCoreDependencySignal signal;
        lock (_sync)
        {
            var next = descriptor with { Revision = ++_revision };
            _services[next.ServiceId] = next;
            signal = CreateSignal(requiresReconnect: true);
            observers = _subscribers.Values.ToArray();
        }
        foreach (var observer in observers)
        {
            try { observer(signal); }
            catch { /* A dependent observer cannot interrupt a service transition. */ }
        }
    }

    private HomeCoreDependencySignal CreateSignal(bool requiresReconnect) =>
        new(_revision,
            _services.Values.OrderBy(item => item.ServiceId, StringComparer.Ordinal).ToArray(),
            requiresReconnect,
            DateTimeOffset.UtcNow);

    private string[] GetStartOrder()
    {
        var order = new List<string>();
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) throw new InvalidOperationException($"Home service dependency cycle includes '{id}'.");
            foreach (var dependency in _implementations[id].Dependencies)
            {
                if (!_services.ContainsKey(dependency))
                    throw new InvalidOperationException($"Home service '{id}' depends on unknown service '{dependency}'.");
                if (_implementations.ContainsKey(dependency)) Visit(dependency);
            }
            visiting.Remove(id);
            visited.Add(id);
            order.Add(id);
        }

        foreach (var id in _implementations.Keys.OrderBy(value => value, StringComparer.Ordinal)) Visit(id);
        return order.ToArray();
    }

    private void ValidateDependencyGraph() => _ = GetStartOrder();

    private string[] GetDependents(string serviceId) => _implementations.Values
        .Where(service => service.Dependencies.Contains(serviceId, StringComparer.Ordinal))
        .Select(service => service.Descriptor.ServiceId)
        .ToArray();

    private static void ValidateServiceId(string serviceId)
    {
        if (string.IsNullOrWhiteSpace(serviceId) || serviceId.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
            throw new ArgumentException("Service IDs must be stable ASCII identifiers.", nameof(serviceId));
    }

    private static void ValidateCompatibilityRequest(HomeCompatibilityRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AppId) || string.IsNullOrWhiteSpace(request.AppVersion))
            throw new ArgumentException("App ID and app version are required.", nameof(request));
        ArgumentNullException.ThrowIfNull(request.RequiredServices);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var requirement in request.RequiredServices)
        {
            ValidateServiceId(requirement.ServiceId);
            if (!seen.Add(requirement.ServiceId))
                throw new ArgumentException($"Duplicate service requirement '{requirement.ServiceId}'.", nameof(request));
            if (requirement.MajorVersion < 0 || requirement.MinimumMinorVersion < 0 ||
                requirement.MaximumMinorVersionExclusive is < 0 ||
                requirement.MaximumMinorVersionExclusive <= requirement.MinimumMinorVersion)
                throw new ArgumentException($"Invalid version range for service '{requirement.ServiceId}'.", nameof(request));
        }
    }

    private static HomeCompatibilityResult Denied(
        string appId,
        HomeCoreStateSnapshot snapshot,
        HomeCoreErrorCode code,
        string message) => new(
        HomeCompatibilityState.PermissionDenied,
        appId,
        snapshot.Revision,
        [],
        [new HomeCoreFailure(code, message, "9to1.Home.GetCompatibility", false)]);

    private void ReleaseDependent()
    {
        lock (_sync)
        {
            if (_dependentLeases > 0) _dependentLeases--;
        }
    }

    private void RemoveSubscription(long id)
    {
        lock (_sync) _subscribers.Remove(id);
    }

    private sealed class DenyAllHomeCoreAuthorization : IHomeCoreAuthorization
    {
        public ValueTask<bool> IsAllowedAsync(HomeCallerIdentity caller, string target, IReadOnlySet<string> scopes,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }

    private sealed class DependentLease(HomeCoreRuntime owner) : IDisposable
    {
        private HomeCoreRuntime? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseDependent();
    }

    private sealed class Subscription(HomeCoreRuntime owner, long id) : IDisposable
    {
        private HomeCoreRuntime? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.RemoveSubscription(id);
    }
}

