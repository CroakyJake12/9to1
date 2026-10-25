using System.Collections.Frozen;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>Observation only. Operation IDs work only on their SAME retained native session.</summary>
public sealed record HomeNativePackageOperationSnapshot(string OperationId, HomePackageOperationState State,
    string? PermissionRequestId, bool AuditOnlyAfterAttempt, IReadOnlyList<HomePackageActionResult> KnownResults,
    string Code);

/// <summary>The registered original package owner on the existing Core session/lease.
/// Uses the original planner, device owner, resource broker and per-action review. It
/// creates no listener, profile, publisher trust or second package database.</summary>
public sealed class HomeNativePackageOperationsService : IHomeCoreService
{
    public const string ResourceKind = "home.package";
    public const string RemoveActionId = "home.packages.uninstall";
    private readonly HomePackageOriginalDeviceOwner _device;
    private readonly IHomePackageOriginalPlatformOwner _platform;
    private readonly HomeResourceOperationBroker _broker;
    private readonly Func<CancellationToken, Task> _start;
    private readonly Func<string> _home, _abi;
    private readonly object _gate = new();
    private readonly List<Operation> _originals = [];
    private readonly AsyncLocal<Operation?> _active = new();
    private readonly List<Exception> _callbackFailures = [];
    private bool _started, _closing;
    private Task? _stop;
    private sealed class Operation(HomeNativeCoreApiSessions.Session session, HomePackageOriginalOperationAdmission admission,
        HomePackageOriginalLifecycleRun run, HomePackageArtifactSelection selection)
    {
        internal readonly HomeNativeCoreApiSessions.Session Session = session;
        internal readonly HomePackageOriginalOperationAdmission Admission = admission;
        internal readonly HomePackageOriginalLifecycleRun Run = run;
        internal readonly HomePackageArtifactSelection Selection = selection;
        internal readonly List<Task> Tasks = [];
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal Exception? Failure;
    }
    public HomeNativePackageOperationsService(HomePackageOriginalDeviceOwner sameDevice, HomeNativeCoreApiSessions sessions,
        HomeResourceOperationBroker sameBroker, Func<CancellationToken, Task> startValidator,
        Func<string> originalHomePackage, Func<string> originalAbi)
    {
        _device = sameDevice ?? throw new ArgumentNullException(nameof(sameDevice)); _platform = sameDevice;
        _broker = sameBroker ?? throw new ArgumentNullException(nameof(sameBroker)); _start = startValidator;
        _home = originalHomePackage; _abi = originalAbi; sessions.BindOriginalPackageService(this);
    }
    public HomeServiceDescriptor Descriptor { get; } = new("packages", new(1, 0, 0), HomeServiceLifecycleState.Stopped, false);
    public bool IsOriginalAvailable { get { lock (_gate) return _started && !_closing && _callbackFailures.Count == 0; } }
    public bool HasOriginalCallbackFailure { get { lock (_gate) return _callbackFailures.Count != 0; } }
    public IReadOnlyList<string> Dependencies { get; } = ["home.state", "permissions.trust"];
    public async Task StartAsync(CancellationToken token = default)
    {
        await _start(token).ConfigureAwait(false);
        lock (_gate) { if (_closing) throw new ObjectDisposedException(nameof(HomeNativePackageOperationsService)); _started = true; }
    }
    internal async Task<HomeNativeCoreApiResult<HomeNativePackageOperationSnapshot>> BeginAsync(HomeNativeCoreApiSessions.Session session,
        HomePackageActionRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Action != HomePackageAction.Uninstall) throw new UnauthorizedAccessException("Only original optional-app removal is implemented.");
        await session.DemandOriginalPackageHomeAsync(token).ConfigureAwait(false);
        lock (_gate)
        {
            DemandStarted();
            if (_originals.Count >= 32 || _originals.Any(value => ReferenceEquals(value.Session, session)))
                throw new InvalidOperationException("Continue/recover the SAME original session's retained package operation.");
        }
        var read = await _platform.Database.ReadAsync(token).ConfigureAwait(false);
        var registry = read.Succeeded && read.Snapshot is { } snapshot ? snapshot : throw new InvalidDataException("The canonical package registry is unavailable.");
        var selections = new List<HomePackageArtifactSelection>();
        foreach (var row in registry.Packages)
        {
            var observationRequest = request with { PackageId = row.PackageId, RequestedVersion = null, RequestedChannel = null, ExpectedRevision = null };
            var material = await _platform.ResolveOriginalArtifactAsync(observationRequest, registry, token).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The complete original retained package catalogue is unavailable.");
            selections.Add(HomePackageArtifactSelection.Capture(_platform, material, observationRequest));
        }
        var registrations = selections.Select(selection => new HomePackageOriginalLifecycleRegistration(selection,
            selection.Descriptor.ComponentClass switch {
                "mandatory-shared-core" => HomePackageComponentClass.MandatorySharedCore,
                "optional-app" => HomePackageComponentClass.OptionalApp,
                "app-required-dependency" => HomePackageComponentClass.AppRequiredDependency,
                "optional-feature-dependency" => HomePackageComponentClass.OptionalFeature,
                _ => throw new UnauthorizedAccessException("The signed original component class is unsupported.") },
            selection.Descriptor.ComponentClass == "optional-app" ? new[] { HomePackageAction.Uninstall }.ToFrozenSet() : Array.Empty<HomePackageAction>().ToFrozenSet())).ToArray();
        var policy = new HomePackageOriginalLifecyclePolicy(_platform, _home(), "linux", _abi(), registrations);
        var planner = new HomePackageOriginalDependencyLifecycle(_platform, policy, selections);
        var plan = planner.Plan(request, registry, false);
        if (plan.Plan is null) throw new UnauthorizedAccessException(plan.Error?.Code ?? "The original removal plan was refused.");
        await session.DemandOriginalPackageHomeAsync(token).ConfigureAwait(false);
        var admission = session.OpenOriginalPackageOperations(_platform, _broker);
        var operation = new Operation(session, admission, new(admission, _device, planner, plan.Plan),
            selections.Single(value => value.Descriptor.PackageId == request.PackageId));
        lock (_gate)
        {
            // Publish custody before the first original review/effect task starts.
            _originals.Add(operation);
            DemandStarted();
            if (_originals.Count(value => ReferenceEquals(value.Session, session)) != 1)
                throw new InvalidOperationException("An original removal is already retained on this session.");
        }
        return await Execute(operation, false, token).ConfigureAwait(false);
    }
    private Operation Require(HomeNativeCoreApiSessions.Session session, string id)
    {
        lock (_gate)
        {
            DemandStarted();
            return _originals.SingleOrDefault(value => ReferenceEquals(value.Session, session) && value.Run.ObserveOriginal().OperationId == id)
                ?? throw new UnauthorizedAccessException("Only the SAME native session's privately retained operation may continue.");
        }
    }
    internal Task<HomeNativeCoreApiResult<HomeNativePackageOperationSnapshot>> ContinueAsync(HomeNativeCoreApiSessions.Session session, string id, CancellationToken token)
        => Execute(Require(session, id), false, token);
    internal Task<HomeNativeCoreApiResult<HomeNativePackageOperationSnapshot>> RecoverAsync(HomeNativeCoreApiSessions.Session session, string id, CancellationToken token)
        => Execute(Require(session, id), true, token);
    internal async Task<HomeNativeCoreApiResult<HomeNativePackageOperationSnapshot>> ObserveAsync(HomeNativeCoreApiSessions.Session session, string id, CancellationToken token)
    {
        var operation = Require(session, id); await session.DemandOriginalPackageHomeAsync(token).ConfigureAwait(false);
        return Describe(operation);
    }
    private async Task<HomeNativeCoreApiResult<HomeNativePackageOperationSnapshot>> Execute(Operation operation, bool recover, CancellationToken token)
    {
        await operation.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await operation.Session.DemandOriginalPackageHomeAsync(token).ConfigureAwait(false);
            _active.Value = operation;
            try
            {
                Task original = recover ? operation.Run.RecoverOriginalAsync(token) : operation.Run.ContinueOriginalAsync(token);
                Retain(operation, original);
                await original.ConfigureAwait(false);
            }
            catch (Exception cause)
            {
                // The exact original remains retained/drained. An uncertain effect is
                // observable; this wrapper never dispatches a replacement effect.
                operation.Failure = cause;
            }
            finally { _active.Value = null; }
            return Describe(operation);
        }
        finally { operation.Gate.Release(); }
    }
    private static HomeNativeCoreApiResult<HomeNativePackageOperationSnapshot> Describe(Operation operation)
    {
        var value = operation.Run.ObserveOriginal();
        var result = new HomeNativePackageOperationSnapshot(value.OperationId, value.State, value.OriginalPermissionRequestId,
            value.AuditOnlyAfterAttempt, value.KnownOriginalResults, operation.Failure is null ? "HomePackages.OriginalOperation" : "HomePackages.OriginalOperationInterrupted");
        return new(new(true, "HomePackages.OriginalObservation", "Original package operation observed.", result), value.OriginalPermissionRequestId);
    }
    public async Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor actor, string action,
        ResourceScope resource, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var operation = _active.Value;
        if (operation is null || actor != operation.Session.OriginalPackageActor || action != RemoveActionId ||
            resource != new ResourceScope(ResourceKind, operation.Selection.Descriptor.PackageId, operation.Selection.Descriptor.Version, ResourceAccess.Write))
            return new(false, "HOME_PACKAGE_ORIGINAL_SESSION_REQUIRED", actor.ActorId, resource.Revision, actor.OrganisationId);
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = Observe();
        var callbacks = new HomeOwnershipOriginalSourceCallbacks(scope, retain,
            failure => { lock (_gate) _callbackFailures.Add(failure); });
        // The broker invokes this only inside the SAME authenticated connection gate.
        try { Retain(operation, original); callbacks.Run(() => callbacks.Retain(original)); }
        catch (Exception cause) { begin.SetException(cause); throw; }
        begin.SetResult();
        await original.ConfigureAwait(false);
        if (callbacks.Errors.Length != 0) throw new AggregateException("Original package resource callbacks failed.", callbacks.Errors);
        return new(true, "HOME_PACKAGE_ORIGINAL_MANUAL_WRITE", actor.ActorId, resource.Revision, actor.OrganisationId);
        async Task Observe()
        { await begin.Task.ConfigureAwait(false); await operation.Session.DemandOriginalPackageBorrowedAsync(token).ConfigureAwait(false); }
    }
    private void DemandStarted()
    { if (!_started || _closing || _callbackFailures.Count != 0) throw new UnauthorizedAccessException("The actual original package service is unavailable."); }
    private void Retain(Operation operation, Task original)
    {
        lock (_gate)
        {
            operation.Tasks.RemoveAll(value => value.IsCompletedSuccessfully);
            // Publish custody before reporting capacity failure: the new original
            // must still be drained, and no later effect may use a healthy service.
            operation.Tasks.Add(original);
            if (operation.Tasks.Count > 256)
            { var failure = new InvalidOperationException("Original package task failures remain retained."); _callbackFailures.Add(failure); throw failure; }
        }
    }
    public Task StopAsync(CancellationToken token = default)
    {
        lock (_gate)
        {
            if (_stop is not null) return _stop;
            _closing = true;
            return _stop = Drain(_originals.ToArray());
        }
    }
    private async Task Drain(Operation[] all)
    {
        var failures = new List<Exception>();
        foreach (var operation in all)
        {
            await operation.Gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                foreach (var task in operation.Tasks) try { await task.ConfigureAwait(false); } catch (Exception cause) { failures.Add(task.Exception ?? cause); }
                try { await operation.Run.DisposeAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
                try { await operation.Admission.DisposeAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            }
            finally { operation.Gate.Release(); }
        }
        try { await _device.CloseAndDrainAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
        lock (_gate) failures.AddRange(_callbackFailures);
        if (failures.Count != 0) throw new AggregateException("Original package operation/owner failures retained.", failures);
    }
}

public sealed class HomeNativePackageActionPolicies : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) => appId == "home" && actionId == HomeNativePackageOperationsService.RemoveActionId
        ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.Elevated, false, false, RequiresPerActionApproval: true) : null;
}
