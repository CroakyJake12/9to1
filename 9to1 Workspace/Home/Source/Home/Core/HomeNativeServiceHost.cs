using Haven.Application;

namespace HavenOS.Home.Core;

public enum HomeNativeHostState { Ready, RequiresHomeRepair }
public sealed record HomeNativeServiceSession(IServiceProvider Services, HomeCoreRuntime Runtime,
    IAuthenticatedResourceActorSource Actors);
public enum HomeNativeOriginalFailureStage { None, Composition, RuntimeStartup, RequiredServiceStartup }
public sealed record HomeNativeHostResult(HomeNativeHostState State, string Code, string Message,
    IServiceProvider? Services, HomeCoreStateSnapshot? Snapshot)
{
    // Method-only same-process observation. No serialized exception, stack, message, authority or
    // Ready fallback is added to the maintained public repair envelope.
    private Exception? _originalFailure;
    private HomeNativeOriginalFailureStage _originalStage;
    private Task? _originalFailureTask;
    internal HomeNativeHostResult RetainOriginalFailure(HomeNativeOriginalFailureStage stage, Exception? original, Task? originalTask = null)
    {
        if (original is null) return this;
        if (_originalFailure is not null && !ReferenceEquals(_originalFailure, original))
            throw new InvalidOperationException("The first original Home startup failure cannot be replaced.");
        if (_originalFailure is null) { _originalFailure = original; _originalStage = stage; _originalFailureTask = originalTask; }
        return this;
    }
    public HomeNativeOriginalFailureStage ObserveOriginalFailureStage() => _originalStage;
    public void RethrowOriginalFailureIfPresent()
    {
        if (_originalFailure is { } original)
        {
            if (_originalFailureTask is { IsCompleted: false })
                throw new InvalidOperationException("The retained original startup task has not settled.");
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(original).Throw();
        }
    }
}

/// <summary>
/// One process-wide bundled Home host, shared by launcher and primary UI. The factory must be the platform's
/// canonical Home composition; this bridge never constructs a replacement provider, account or package database.
/// </summary>
public sealed class HomeNativeServiceHost
{
    private readonly object _sync = new();
    private Task<HomeNativeServiceSession>? _start;
    private string? _compositionId;
    public static HomeNativeServiceHost Process { get; } = new();
    private HomeNativeServiceHost() { }

    /// <summary>Only the trusted bundled host supplies composition and required service IDs. These are not app request fields.</summary>
    public ValueTask<HomeNativeHostResult> EnsureAsync(string compositionId,
        Func<CancellationToken, Task<HomeNativeServiceSession>> canonicalComposition,
        IReadOnlyList<string> requiredServices, CancellationToken cancellationToken = default) =>
        EnsureAsync(compositionId, canonicalComposition, requiredServices.Select(id => new HomeServiceRequirement(id,
            HomeCoreServiceCatalog.CurrentContractVersion.Major, HomeCoreServiceCatalog.CurrentContractVersion.Minor)).ToArray(), cancellationToken);

    public async ValueTask<HomeNativeHostResult> EnsureAsync(string compositionId,
        Func<CancellationToken, Task<HomeNativeServiceSession>> canonicalComposition,
        IReadOnlyList<HomeServiceRequirement> requiredServices, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(canonicalComposition);
        if (string.IsNullOrWhiteSpace(compositionId) || requiredServices is null || requiredServices.Count == 0 ||
            requiredServices.Count > 64 || requiredServices.Any(r => r is null || string.IsNullOrWhiteSpace(r.ServiceId) ||
                r.MajorVersion < 0 || r.MinimumMinorVersion < 0 || r.MaximumMinorVersionExclusive <= r.MinimumMinorVersion) ||
            requiredServices.Select(r => r.ServiceId).Distinct(StringComparer.Ordinal).Count() != requiredServices.Count)
            throw new ArgumentException("Canonical composition and distinct versioned Home service requirements are required.");
        Task<HomeNativeServiceSession> start;
        lock (_sync)
        {
            if (_compositionId is not null && _compositionId != compositionId)
                return Repair("HomeCompositionConflict", "This process already owns a different Home composition.");
            _compositionId = compositionId;
            // Cancellation of one activity/window must not tear down the shared process host for another client.
            _start ??= Task.Run(() => canonicalComposition(CancellationToken.None));
            start = _start;
        }
        HomeNativeServiceSession session;
        try { session = await start.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception original) { return Repair("HomeBootstrapFailed", "Home could not start safely. Open Home to repair its installation or recover its preserved state.")
            .RetainOriginalFailure(HomeNativeOriginalFailureStage.Composition, original, start); }
        if (session.Services is null || session.Runtime is null || session.Actors is null)
            return Repair("HomeCompositionInvalid", "The bundled Home composition is incomplete.");
        Task<HomeCoreStateSnapshot>? originalRuntimeStart = null;
        try
        {
            var actor = await session.Actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (actor is null) return Repair("HomeIdentityUnavailable", "Home cannot verify this operating-system profile. Open Home for recovery.");
            originalRuntimeStart = session.Runtime.StartAsync(cancellationToken);
            var snapshot = await originalRuntimeStart.ConfigureAwait(false);
            var missing = requiredServices.Where(requirement => requirement.Required && !snapshot.Services.Any(s => s.ServiceId == requirement.ServiceId && s.IsAvailable &&
                s.State == HomeServiceLifecycleState.Ready && requirement.Accepts(s.ContractVersion))).Select(r => r.ServiceId).ToArray();
            if (missing.Length != 0)
                return new HomeNativeHostResult(HomeNativeHostState.RequiresHomeRepair, "HomeServiceUnavailable", "Required Home services are unavailable or incompatible: " + string.Join(", ", missing), null, snapshot)
                    .RetainOriginalFailure(HomeNativeOriginalFailureStage.RequiredServiceStartup,
                        session.Runtime.CaptureOriginalStartFailures(missing));
            if (actor != await session.Actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false))
                return Repair("HomeProfileChanged", "The active Home profile changed during startup. Open Home and retry.");
            return new(HomeNativeHostState.Ready, "Ready", "The canonical Home host is ready.", session.Services, snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception original) { return Repair("HomeRecoveryRequired", "Home state or profile authority requires recovery. Existing data was preserved.")
            .RetainOriginalFailure(HomeNativeOriginalFailureStage.RuntimeStartup, original, originalRuntimeStart); }
    }
    private static HomeNativeHostResult Repair(string code, string message) => new(HomeNativeHostState.RequiresHomeRepair, code, message, null, null);
}

/// <summary>Reports registry readiness only after the actual trusted platform inventory and OS profile can be read.</summary>
public sealed class HomeInstalledApplicationsService(IInstalledApplicationRegistry registry,
    IEnumerable<IInstalledApplicationObservationProvider> providers) : IHomeCoreService
{
    public HomeServiceDescriptor Descriptor { get; } = new("apps.installed", HomeCoreServiceCatalog.CurrentContractVersion,
        HomeServiceLifecycleState.Stopped, false, "Installed application discovery has not started.");
    public IReadOnlyList<string> Dependencies { get; } = ["home.core", "home.state"];
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!providers.Any()) throw new InvalidOperationException("No trusted platform application inventory is registered.");
        await registry.RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
    public Task StopAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
}
