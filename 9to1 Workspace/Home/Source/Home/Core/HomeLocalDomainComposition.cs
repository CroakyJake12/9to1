using System.Collections.Frozen;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

public sealed record HomeLocalDomainIdentityComponents(FileHomeCoreStateStore StateStore,
    HomeLocalProfileIdentity Profiles, HomePermissionTrustService Permissions);
public sealed record HomeLocalDomainOwnershipComponents(HomeLocalDomainIdentityComponents Identity,
    HomeLocalStoreOwnership LocalStoreOwnership, HomeResourceStoreOwnershipAuthority Ownership);
public sealed record HomeLocalDomainStoreRegistrations(IReadOnlyList<IHomeLocalStoreEvidenceProvider> Evidence,
    IReadOnlyDictionary<Type, object> Services);
public sealed record HomeLocalDomainResolverRegistrations(IReadOnlyList<ICanonicalResourceAccessResolver> Resolvers,
    IReadOnlyDictionary<Type, object> Services);
public sealed record HomeLocalDomainOwnerComponents(FileHomeCoreStateStore StateStore,
    HomeLocalProfileIdentity Profiles, HomePermissionTrustService Permissions,
    ResourceAuthorizationService Resources, HomeResourceStoreOwnershipAuthority Ownership,
    HomeResourceOperationBroker Broker);

/// <summary>Linux Home-process domain composition over the actual configured state and
/// trusted process principal. This issues no installed-peer, app, account, CAKE or IPC session.
/// Approval remains the SAME manual Home permission workflow. All users of these borrowed
/// objects must drain their own work before the owning process retires this composition.</summary>
public sealed class HomeLocalDomainComposition : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _process = new();
    private readonly AsyncLocal<Original?> _executing = new();
    [ThreadStatic] private static HashSet<HomeLocalDomainComposition>? _physical;
    private sealed class Original(Original? parent)
    {
        internal readonly Original? Parent = parent;
        internal readonly List<Task> Sources = [];
        internal readonly List<Exception> Causes = [];
        internal bool Live = true;
        internal Task? Driver;
    }
    private Original? _startup;
    private Original? _request;
    private Original? _retirement;
    private bool _retiring;
    private AuthenticatedResourceActor? _startedActor;
    private readonly ITrustedHostPrincipalSource _principals;
    private readonly HomeLocalDomainStateLayout _layout;
    private Task? _originalLayoutClose;
    private readonly Action<Action>? _parentScope;
    private readonly Action<Task>? _parentRetain;
    public Task? OriginalLayoutCloseTask { get { lock (_gate) return _originalLayoutClose; } }

    public FileHomeCoreStateStore StateStore { get; }
    public HomeLocalProfileIdentity Profiles { get; }
    public HomePermissionTrustService Permissions { get; }
    public ResourceAuthorizationService Resources { get; }
    public HomeLocalStoreOwnership LocalStoreOwnership { get; }
    public HomeResourceStoreOwnershipAuthority Ownership { get; }
    public HomeResourceOperationBroker Broker { get; }
    public IServiceProvider Services { get; }
    public Task? OriginalStartTask { get { lock (_gate) return _startup?.Driver; } }
    public Task? OriginalProcessRetirementRequestTask { get { lock (_gate) return _request?.Driver; } }
    public Task? OriginalCloseTask { get { lock (_gate) return _retirement?.Driver; } }

    public HomeLocalDomainComposition(FileHomeCoreStateStore originalStateStore,
        ITrustedHostPrincipalSource originalPrincipalSource,
        IEnumerable<IHomeLocalStoreEvidenceProvider>? originalStoreEvidence = null,
        IEnumerable<ICanonicalResourceAccessResolver>? originalResourceResolvers = null,
        IEnumerable<IHomeActionPolicySource>? originalActionPolicies = null,
        Func<HomeLocalDomainOwnerComponents, IReadOnlyDictionary<Type, object>>? configureOriginalOwners = null,
        Func<HomeLocalDomainIdentityComponents, HomeLocalDomainStoreRegistrations>? configureOriginalStores = null,
        Func<HomeLocalDomainOwnershipComponents, HomeLocalDomainResolverRegistrations>? configureOriginalResolvers = null,
        Action<Action>? originalSynchronousScope = null, Action<Task>? retainOriginalTask = null)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("The local Home domain producer requires Linux.");
        StateStore = originalStateStore ?? throw new ArgumentNullException(nameof(originalStateStore));
        _principals = originalPrincipalSource ?? throw new ArgumentNullException(nameof(originalPrincipalSource));
        if ((originalSynchronousScope is null) != (retainOriginalTask is null))
            throw new ArgumentException("The original parent scope and raw task custody must be configured together.");
        _parentScope = originalSynchronousScope; _parentRetain = retainOriginalTask;
        try { _layout = new(StateStore); }
        catch (Exception primary)
        {
            List<Exception> errors = []; Add(errors, primary);
            try { _process.Dispose(); } catch (Exception error) { Add(errors, error); }
            throw new AggregateException("The original domain layout acquisition failed.", errors);
        }
        try
        {
        var evidence = Capture(originalStoreEvidence);
        var resolvers = Capture(originalResourceResolvers);
        var policies = Capture(originalActionPolicies);
        var coreReadPolicy = new HomeCoreServiceReadActionPolicies();
        Profiles = new(StateStore, new LayoutCheckedPrincipal(_principals, _layout));
        Permissions = new(StateStore, ResolvePolicy);
        var identity = new HomeLocalDomainIdentityComponents(StateStore, Profiles, Permissions);
        var stageServices = new Dictionary<Type, object>();
        if (configureOriginalStores is not null)
        {
            var registered = configureOriginalStores(identity)
                ?? throw new InvalidOperationException("The original domain evidence factory returned no registration.");
            evidence = Capture(evidence.Concat(Capture(registered.Evidence)));
            AddRegistrations(stageServices, registered.Services);
        }
        var evidenceRegistry = new HomeLocalStoreEvidenceRegistry(evidence);
        LocalStoreOwnership = new(StateStore, Profiles, evidenceRegistry, Permissions);
        Ownership = new(LocalStoreOwnership, Profiles);
        if (configureOriginalResolvers is not null)
        {
            var registered = configureOriginalResolvers(new(identity, LocalStoreOwnership, Ownership))
                ?? throw new InvalidOperationException("The original domain resolver factory returned no registration.");
            resolvers = Capture(resolvers.Concat(Capture(registered.Resolvers)));
            AddRegistrations(stageServices, registered.Services);
        }
        Resources = new(Profiles, resolvers);
        Broker = new(Resources, Permissions);
        var components = new Dictionary<Type, object>
        {
            [typeof(FileHomeCoreStateStore)] = StateStore,
            [typeof(IHomeCoreStateStore)] = StateStore,
            [typeof(ITrustedHostPrincipalSource)] = _principals,
            [typeof(HomeLocalProfileIdentity)] = Profiles,
            [typeof(IAuthenticatedResourceActorSource)] = Profiles,
            [typeof(HomePermissionTrustService)] = Permissions,
            [typeof(ResourceAuthorizationService)] = Resources,
            [typeof(HomeLocalStoreOwnership)] = LocalStoreOwnership,
            [typeof(HomeResourceStoreOwnershipAuthority)] = Ownership,
            [typeof(IResourceStoreOwnershipReceiptAuthority)] = Ownership,
            [typeof(IHomeLocalStoreEvidenceSource)] = evidenceRegistry,
            [typeof(HomeResourceOperationBroker)] = Broker,
        };
        AddRegistrations(components, stageServices);
        if (configureOriginalOwners is not null)
            AddRegistrations(components, configureOriginalOwners(new(StateStore, Profiles, Permissions, Resources, Ownership, Broker))
                ?? throw new InvalidOperationException("The original domain owner factory returned no registration."));
        Services = new OriginalProvider(components.ToFrozenDictionary());

        HomePermissionActionPolicy? ResolvePolicy(string appId, string actionId)
        {
            var core = coreReadPolicy.TryGet(appId, actionId);
            HomePermissionActionPolicy? registered = null;
            foreach (var source in policies)
            {
                var current = source.TryGet(appId, actionId);
                if (current is null) continue;
                if (core is not null || registered is not null) return null;
                registered = current;
            }
            return core ?? registered;
        }
        }
        catch (Exception primary)
        {
            List<Exception> causes = [];
            Add(causes, primary);
            try
            {
                _originalLayoutClose = _layout.DisposeAsync().AsTask();
                try { _originalLayoutClose.GetAwaiter().GetResult(); }
                catch (Exception error)
                {
                    if (_originalLayoutClose.IsFaulted)
                        foreach (var cause in _originalLayoutClose.Exception!.InnerExceptions) Add(causes, cause);
                    else Add(causes, error);
                }
            }
            catch (Exception error) { Add(causes, error); }
            try { _process.Dispose(); } catch (Exception error) { Add(causes, error); }
            throw new AggregateException("The actual domain construction and independent layout cleanup failed.", causes);
        }
    }

    /// <summary>Exact immutable configured tuple only. Neither this predicate nor startup
    /// supplies permission to access a project, execute a tool or impersonate an installed app.</summary>
    public bool IsBoundToOriginalComposition(FileHomeCoreStateStore stateStore, HomeLocalProfileIdentity profiles,
        HomePermissionTrustService permissions, ResourceAuthorizationService resources,
        HomeResourceStoreOwnershipAuthority ownership, HomeResourceOperationBroker broker) =>
        ReferenceEquals(StateStore, stateStore) && ReferenceEquals(Profiles, profiles) &&
        ReferenceEquals(Permissions, permissions) && ReferenceEquals(Resources, resources) &&
        ReferenceEquals(Ownership, ownership) && ReferenceEquals(Broker, broker);

    public void DemandOriginalStarted()
    {
        lock (_gate) DemandStartedUnderGate();
    }
    /// <summary>Retained startup observation; callers still require fresh profile checks and
    /// the actual action's owning permission/admission before reads or effects.</summary>
    public AuthenticatedResourceActor GetOriginalStartedActor()
    { lock (_gate) { DemandStartedUnderGate(); return _startedActor!; } }
    private void DemandStartedUnderGate()
    {
        var startup = _startup;
        if (_retiring || _startedActor is null || startup is null || startup.Driver?.IsCompletedSuccessfully != true || startup.Causes.Count != 0)
            throw new InvalidOperationException("The SAME local domain startup has not completed or has retired.");
    }

    public Task StartOriginalAsync()
    {
        DemandExternalOriginalProcessJoin();
        TaskCompletionSource start;
        Task actual;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_startup is not null) return _startup.Driver!;
            var original = _startup = new(_executing.Value);
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = original.Driver = Drive(original, start.Task, StartBody);
        }
        try { Scope(_startup!, () => Retain(_startup!, actual), checkLayout: false); }
        finally { start.SetResult(); }
        return actual;
    }
    private async Task StartBody(Original original)
    {
        var actor = await ReadProfile(original).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The actual local Home profile is unavailable.");
        if (actor != await ReadProfile(original).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The actual local Home profile changed during startup.");
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            _process.Token.ThrowIfCancellationRequested();
            if (original.Causes.Count != 0) throw new AggregateException("The original startup callbacks failed.", original.Causes);
            _startedActor = actor;
        }
    }
    private async Task<AuthenticatedResourceActor?> ReadProfile(Original original)
    {
        Task<AuthenticatedResourceActor?>? actual = null;
        List<Exception> errors = [];
        AuthenticatedResourceActor? observed = null;
        try { Scope(original, () =>
        {
            // The reviewed profile overload propagates this SAME physical guard and raw
            // custody into every principal/store factory, including factories after awaits.
            actual = Profiles.GetCurrentAsync(body => Scope(original, body),
                task => Retain(original, task), _process.Token).AsTask();
            Retain(original, actual);
        }); }
        catch (Exception error) { Add(errors, error); }
        if (actual is not null)
            try { observed = await actual.ConfigureAwait(false); }
            catch (Exception error)
            {
                if (errors.Count == 0 && actual.IsCanceled)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
                if (actual.IsFaulted)
                    foreach (var cause in actual.Exception!.InnerExceptions) Add(errors, cause);
                else Add(errors, error);
            }
        if (errors.Count != 0)
            throw new AggregateException("The actual domain profile acquisition and source failed.", errors);
        if (actual is null)
        {
            throw new InvalidOperationException("The actual domain profile task was not captured.");
        }
        return observed;
    }
    private async Task Drive(Original original, Task gate, Func<Original, Task> body)
    {
        await gate.ConfigureAwait(false);
        var previous = _executing.Value; _executing.Value = original;
        Task? actual = null;
        Exception? scopeError = null;
        try
        {
            List<Exception> errors = [];
            try { Scope(original, () => { actual = body(original); Retain(original, actual); }, checkLayout: false); }
            catch (Exception error) { scopeError = error; Add(errors, error); }
            if (actual is not null)
                try { await actual.ConfigureAwait(false); }
                catch (Exception error)
                {
                    bool noCallbacksFailed; lock (_gate) noCallbacksFailed = original.Causes.Count == 0;
                    if (scopeError is null && noCallbacksFailed && actual.IsCanceled)
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
                    if (actual.IsFaulted) foreach (var cause in actual.Exception!.InnerExceptions) Add(errors, cause);
                    else Add(errors, error);
                }
            Exception[] callbackCauses; lock (_gate) callbackCauses = original.Causes.ToArray();
            foreach (var cause in callbackCauses) Add(errors, cause);
            if (errors.Count != 0) throw new AggregateException("The actual domain scope/body failed.", errors);
        }
        finally { lock (_gate) original.Live = false; _executing.Value = previous; }
    }
    private void Retain(Original original, Task actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        lock (_gate)
        {
            if (!original.Live) throw new InvalidOperationException("The original domain source has completed.");
            if (!original.Sources.Contains(actual, ReferenceEqualityComparer.Instance)) original.Sources.Add(actual);
        }
        _parentRetain?.Invoke(actual);
    }
    private void Scope(Original original, Action callback, bool checkLayout = true)
    {
        var scopes = _physical ??= [];
        var added = scopes.Add(this);
        var thread = Environment.CurrentManagedThreadId;
        var active = 1; var invoked = 0; List<Exception> causes = [];
        void Record(Exception cause)
        {
            lock (causes) Add(causes, cause);
            lock (_gate) Add(original.Causes, cause);
        }
        void Once()
        {
            Exception? refusal = Volatile.Read(ref active) == 0
                ? new InvalidOperationException("The original domain callback phase ended.")
                : Environment.CurrentManagedThreadId != thread
                    ? new InvalidOperationException("The original domain callback changed its synchronous thread.")
                    : Interlocked.CompareExchange(ref invoked, 1, 0) != 0
                        ? new InvalidOperationException("The original domain callback cannot run twice.") : null;
            if (refusal is not null) { Record(refusal); throw refusal; }
            try { if (checkLayout) _layout.DemandCurrent(); callback(); }
            catch (Exception error) { Record(error); throw; }
        }
        try
        {
            try
            {
                if (_parentScope is null) Once(); else _parentScope(Once);
                if (Volatile.Read(ref invoked) == 0) Record(new InvalidOperationException("The original domain scope omitted its callback."));
            }
            catch (Exception error) { Record(error); }
            finally { Volatile.Write(ref active, 0); }
            Exception[] snapshot; lock (causes) snapshot = causes.ToArray();
            if (snapshot.Length != 0) throw new AggregateException("The original domain finite callback failed.", snapshot);
        }
        finally { if (added) scopes.Remove(this); }
    }
    public void DemandExternalOriginalProcessJoin()
    {
        if (_physical?.Contains(this) == true)
            throw new InvalidOperationException("An original domain callback cannot join its owning process.");
        for (var current = _executing.Value; current is not null; current = current.Parent)
            if (Volatile.Read(ref current.Live))
                throw new InvalidOperationException("An original domain driver cannot join its owning process.");
    }
    public void RequestOriginalProcessRetirement()
    {
        TaskCompletionSource start;
        lock (_gate)
        {
            _retiring = true;
            if (_request is not null) return;
            var original = _request = new(_executing.Value);
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            original.Driver = Drive(original, start.Task, request =>
            {
                Scope(request, () => _process.Cancel(), checkLayout: false);
                return Task.CompletedTask;
            });
        }
        try { Scope(_request!, () => Retain(_request!, _request!.Driver!), checkLayout: false); }
        finally { start.SetResult(); }
    }
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalProcessJoin();
        TaskCompletionSource start;
        Task actual;
        lock (_gate)
        {
            if (_retirement is not null) return _retirement.Driver!;
            _retiring = true;
            var original = _retirement = new(_executing.Value);
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = original.Driver = Drive(original, start.Task, CloseBody);
        }
        try { Scope(_retirement!, () => Retain(_retirement!, actual), checkLayout: false); }
        finally { start.SetResult(); }
        return actual;
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private async Task CloseBody(Original retirement)
    {
        List<Exception> errors = [];
        try { RequestOriginalProcessRetirement(); } catch (Exception error) { Add(errors, error); }
        Task? request; lock (_gate) request = _request?.Driver;
        if (request is not null) await Join(request, errors).ConfigureAwait(false);
        Original? startup; lock (_gate) startup = _startup;
        if (startup is not null)
        {
            await Join(startup.Driver!, errors).ConfigureAwait(false);
            Task[] raw; Exception[] callbacks;
            lock (_gate) { raw = startup.Sources.ToArray(); callbacks = startup.Causes.ToArray(); }
            foreach (var actual in raw) await Join(actual, errors).ConfigureAwait(false);
            foreach (var error in callbacks) Add(errors, error);
        }
        try
        {
            Scope(retirement, () =>
            {
                var actual = _layout.DisposeAsync().AsTask();
                lock (_gate) _originalLayoutClose = actual;
                Retain(retirement, actual);
            }, checkLayout: false);
        }
        catch (Exception error) { Add(errors, error); }
        if (_originalLayoutClose is not null) await Join(_originalLayoutClose, errors).ConfigureAwait(false);
        try { Scope(retirement, () => _process.Dispose(), checkLayout: false); } catch (Exception error) { Add(errors, error); }
        if (errors.Count != 0) throw new AggregateException("The original local Home domain retired with source causes.", errors);
    }
    private static async Task Join(Task actual, List<Exception> errors)
    {
        try { await actual.ConfigureAwait(false); }
        catch (Exception error)
        {
            if (actual.IsFaulted) foreach (var cause in actual.Exception!.InnerExceptions) Add(errors, cause);
            else Add(errors, error);
        }
    }
    private static void Add(List<Exception> errors, Exception error)
    {
        if (error is AggregateException group) { foreach (var cause in group.InnerExceptions) Add(errors, cause); return; }
        if (!errors.Any(cause => ReferenceEquals(cause, error))) errors.Add(error);
    }
    private static T[] Capture<T>(IEnumerable<T>? originals) where T : class
    {
        if (originals is null) return [];
        List<T> rows = [];
        foreach (var original in originals)
        {
            if (original is null || rows.Count == 128) throw new ArgumentException("Original domain registrations are missing or full.");
            rows.Add(original);
        }
        return rows.ToArray();
    }
    private static void AddRegistrations(Dictionary<Type, object> originals, IReadOnlyDictionary<Type, object> registered)
    {
        ArgumentNullException.ThrowIfNull(registered);
        if (registered.Count > 128) throw new InvalidOperationException("Original domain registrations exceed capacity.");
        foreach (var row in registered)
            if (row.Key is null || row.Value is null || !row.Key.IsInstanceOfType(row.Value) ||
                originals.Count >= 256 || !originals.TryAdd(row.Key, row.Value))
                throw new InvalidOperationException("A domain registration is invalid or replaces an original component.");
    }
    private sealed class OriginalProvider(FrozenDictionary<Type, object> components) : IServiceProvider
    { public object? GetService(Type serviceType) => components.GetValueOrDefault(serviceType ?? throw new ArgumentNullException(nameof(serviceType))); }
    private sealed class LayoutCheckedPrincipal(ITrustedHostPrincipalSource original, HomeLocalDomainStateLayout layout)
        : ITrustedHostPrincipalSource
    {
        public ValueTask<string?> GetPrincipalAsync(CancellationToken cancellationToken)
        { layout.DemandCurrent(); return original.GetPrincipalAsync(cancellationToken); }
    }
}
