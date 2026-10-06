using System.Runtime.ExceptionServices;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Exact service-owned binding; observation fields convey no issuance or permission.
/// Runtime/adapter remain private so every service-owned endpoint has an original close.</summary>
public sealed class ManagedDulcheProviderBinding
{
    internal ManagedDulcheProviderBinding(ManagedDulcheRuntimeService issuer, DulcheEndpoint endpoint)
    { Issuer = issuer; OriginalSelf = this; Endpoint = endpoint; }
    internal ManagedDulcheRuntimeService Issuer { get; }
    internal ManagedDulcheProviderBinding OriginalSelf { get; }
    public DulcheEndpoint Endpoint { get; }
}

/// <summary>One explicit selected-provider owner. Dependencies are borrowed; this owner never
/// disposes registry/configuration/coordinator/frame/tool/context sources. Construction performs
/// no provider I/O, login, model load, route change or permission acquisition.</summary>
public sealed class ManagedDulcheRuntimeService : IAsyncDisposable, IDulcheOriginalFactoryCallbackScope
{
    private const int RetainedWorkCapacity = 128;
    private readonly object _sync = new();
    private readonly IModelProviderRegistry _registry;
    private readonly IProviderConfigurationStore _configurations;
    private readonly TaskExecutionCoordinator _coordinator;
    private readonly ITaskRunOriginalFrameOwner _frames;
    private readonly IOriginalDulcheProviderToolSource? _tools;
    private readonly IOriginalDulcheProviderContextSource? _contextSource;
    private readonly ITaskRunProviderContextAuthority? _contextAuthority;
    private readonly IDulcheToolCoordinator? _toolCoordinator;
    private readonly CancellationTokenSource _ownerStop = new();
    private readonly List<Task> _originalWork = [];
    private readonly List<Exception> _originalErrors = [];
    private readonly AsyncLocal<OriginalPhase?> _executing = new();
    [ThreadStatic] private static List<ManagedDulcheRuntimeService>? _physicalCalls;
    private string? _providerId;
    private Task<OperationResult<ManagedDulcheProviderBinding>>? _originalInitialization;
    private Task<ManagedProviderDulcheAdapter>? _originalFactory;
    private ManagedProviderDulcheAdapter? _adapter;
    private DulcheRuntime? _runtime;
    private DulcheEndpointStartupOriginal? _startup;
    private ManagedDulcheProviderBinding? _binding;
    private Task? _close;
    private Task<OperationResult<DulcheEndpoint>>? _originalRuntimeStop;
    private Task? _originalAdapterClose;
    private bool _closing;
    private Exception? _capacityRefusal;

    public ManagedDulcheRuntimeService(IModelProviderRegistry registry,
        IProviderConfigurationStore configurations, TaskExecutionCoordinator coordinator,
        ITaskRunOriginalFrameOwner frames, IOriginalDulcheProviderToolSource? tools = null,
        IOriginalDulcheProviderContextSource? contextSource = null,
        ITaskRunProviderContextAuthority? contextAuthority = null, IDulcheToolCoordinator? toolCoordinator = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _configurations = configurations ?? throw new ArgumentNullException(nameof(configurations));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        _tools = tools; _contextSource = contextSource; _contextAuthority = contextAuthority;
        _toolCoordinator = toolCoordinator;
    }

    /// <summary>The same original initialization is coalesced for the actual selected provider.
    /// Caller cancellation withdraws only its wait; Close owns cancellation and real draining.</summary>
    public Task<OperationResult<ManagedDulcheProviderBinding>> StartConfiguredProviderAsync(string actualProviderId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actualProviderId);
        cancellationToken.ThrowIfCancellationRequested();
        TaskCompletionSource? gate = null;
        Task<OperationResult<ManagedDulcheProviderBinding>> actual;
        lock (_sync)
        {
            RequireOpen();
            if (_providerId is not null && !StringComparer.Ordinal.Equals(_providerId, actualProviderId))
                throw new InvalidOperationException("This original service already owns another selected provider.");
            if (_originalInitialization is null)
            {
                _providerId = actualProviderId;
                gate = Signal();
                _originalInitialization = RunOriginalAsync(gate.Task, InitializeOriginalAsync);
                _originalWork.Add(_originalInitialization);
            }
            actual = _originalInitialization;
        }
        gate?.SetResult();
        return cancellationToken.CanBeCanceled ? actual.WaitAsync(cancellationToken) : actual;
    }

    private async Task<OperationResult<ManagedDulcheProviderBinding>> InitializeOriginalAsync()
    {
        Task<ManagedProviderDulcheAdapter> actualFactory;
        lock (_sync) { RequireOpen(); _ownerStop.Token.ThrowIfCancellationRequested(); }
        var factoryStart = Signal();
        actualFactory = ManagedProviderDulcheAdapter.CreateAfterPublicationAsync(factoryStart.Task, _providerId!, _registry, _configurations,
            _coordinator, _frames, _tools, _ownerStop.Token, _contextSource, _contextAuthority, this);
        lock (_sync) _originalFactory = actualFactory;
        factoryStart.SetResult();
        ManagedProviderDulcheAdapter actualAdapter;
        try { actualAdapter = await actualFactory.ConfigureAwait(false); }
        catch (Exception error) { ThrowTask(error, actualFactory); throw; }
        lock (_sync) _adapter = actualAdapter; // Retain an acquired adapter even if close already sealed.
        var runtime = InvokePhysical(() => new DulcheRuntime([actualAdapter], toolCoordinator: _toolCoordinator));
        lock (_sync)
        {
            _runtime = runtime;
            RequireOpen(); _ownerStop.Token.ThrowIfCancellationRequested();
        }
        // This finite runtime acquisition publishes/enrolls startup before its raw callbacks.
        var startup = InvokePhysical(() => runtime.PrepareManagedProviderOriginal(actualAdapter.ProviderId, _ownerStop.Token));
        lock (_sync) _startup = startup;
        InvokePhysical(() => { startup.StartOriginal(); return true; });
        OperationResult<DulcheEndpoint> observed;
        try { observed = await startup.OriginalStartup.ConfigureAwait(false); }
        catch (Exception error) { ThrowTask(error, startup.OriginalStartup); throw; }
        if (!observed.Succeeded) return OperationResult<ManagedDulcheProviderBinding>.Failure(observed.Error!);
        lock (_sync)
        {
            RequireOpen(); _ownerStop.Token.ThrowIfCancellationRequested();
            _binding = new(this, observed.Value!);
            return OperationResult<ManagedDulcheProviderBinding>.Success(_binding);
        }
    }

    /// <summary>Publication precedes the actual runtime bind/enqueue callbacks. Config/provider
    /// identity alone grants nothing: the existing adapter validates the SAME issued coordinator
    /// admission and acknowledged action. Missing tool/remote context producer remains denied.</summary>
    public Task<OperationResult<RuntimeRequestHandle>> SubmitWithContextAsync(ManagedDulcheProviderBinding binding,
        DulcheRequest actualRequest, TaskRunAttemptAdmission originalAdmission, Guid originalActionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actualRequest); ArgumentNullException.ThrowIfNull(originalAdmission);
        cancellationToken.ThrowIfCancellationRequested();
        TaskCompletionSource gate;
        Task<OperationResult<RuntimeRequestHandle>> actual;
        lock (_sync)
        {
            RequireBinding(binding); RequireOpen();
            _originalWork.RemoveAll(task => task.IsCompletedSuccessfully && !ReferenceEquals(task, _originalInitialization));
            if (_capacityRefusal is not null || _originalWork.Count >= RetainedWorkCapacity)
            {
                var refusal = _capacityRefusal ??= new InvalidOperationException("Original service task custody is full.");
                Add(_originalErrors, refusal); throw refusal;
            }
            gate = Signal();
            actual = RunOriginalAsync(gate.Task, () => SubmitOriginalAsync(binding, actualRequest,
                originalAdmission, originalActionId, cancellationToken));
            _originalWork.Add(actual);
        }
        gate.SetResult(); return actual;
    }

    private Task<OperationResult<RuntimeRequestHandle>> SubmitOriginalAsync(ManagedDulcheProviderBinding binding,
        DulcheRequest actualRequest, TaskRunAttemptAdmission admission, Guid action, CancellationToken callerCancellation)
    {
        DulcheRuntime runtime;
        lock (_sync) { RequireOpen(); RequireBinding(binding); runtime = _runtime!; }
        return runtime.SubmitWithContextAsync(actualRequest, binding.Endpoint.EndpointId, admission, action, callerCancellation);
    }

    /// <summary>Observation of the exact real handle; response completion is not settlement.</summary>
    public bool TryObserveOriginalRequestWork(ManagedDulcheProviderBinding binding, RuntimeRequestHandle originalHandle,
        out Task? originalProcessing, out Task? originalEndpointWorker, out IReadOnlyList<Task> originalCleanup)
    {
        lock (_sync)
        {
            RequireBinding(binding);
            if (originalHandle.EndpointId != binding.Endpoint.EndpointId)
                throw new InvalidOperationException("The original handle belongs to another endpoint.");
            return _runtime!.TryObserveOriginalRequestWork(originalHandle, out originalProcessing,
                out originalEndpointWorker, out originalCleanup);
        }
    }

    public Task CloseAndDrainAsync()
    {
        TaskCompletionSource? start = null; Task actual;
        lock (_sync)
        {
            RequireIndependentClose();
            if (_close is null)
            {
                start = Signal();
                _close = CloseOriginalAsync(start.Task);
                _closing = true;
            }
            actual = _close;
        }
        start?.SetResult(); return actual;
    }
    /// <summary>Request-only stop is permitted inside an owned callback; independently join Close later.</summary>
    public void RequestStop()
    {
        TaskCompletionSource? start = null;
        lock (_sync)
        {
            if (_close is null) { start = Signal(); _close = CloseOriginalAsync(start.Task); _closing = true; }
        }
        start?.SetResult();
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private async Task CloseOriginalAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var previous = _executing.Value; var phase = new OriginalPhase(this, previous); _executing.Value = phase;
        var failures = new List<Exception>();
        try
        {
            try { InvokePhysical(() => { _ownerStop.Cancel(); return true; }); } catch (Exception error) { Add(failures, error); }
            Task? initialization; lock (_sync) initialization = _originalInitialization;
            if (initialization is not null) await Join(initialization, failures).ConfigureAwait(false);
            // Initialization may have acquired resources after the first close snapshot. Capture
            // them only after its actual original/finally is terminal, then stop independently.
            DulcheRuntime? runtime; DulcheEndpointStartupOriginal? startup; ManagedProviderDulcheAdapter? adapter;
            Task[] work;
            lock (_sync) { runtime = _runtime; startup = _startup; adapter = _adapter; work = _originalWork.ToArray(); }
            if (runtime is not null && startup is { WasAcquired: true })
            {
                try { _originalRuntimeStop = InvokePhysical(() => runtime.JoinEndpointStopAsync(startup.OriginalAcquisition.EndpointId)); }
                catch (Exception error) { Add(failures, error); }
            }
            // Runtime retirement admits its actual adapter cancel/stop before global adapter close seals
            // control admission. Even a faulted runtime stop does not skip independent adapter close.
            if (_originalRuntimeStop is not null)
            {
                await Join(_originalRuntimeStop, failures).ConfigureAwait(false);
                if (_originalRuntimeStop.IsCompletedSuccessfully && !_originalRuntimeStop.Result.Succeeded)
                    Add(failures, new ManagedDulcheRetirementException(_originalRuntimeStop.Result.Error!));
            }
            if (adapter is not null)
            {
                try { _originalAdapterClose = InvokePhysical(() => adapter.DisposeAsync().AsTask()); }
                catch (Exception error) { Add(failures, error); }
            }
            if (_originalAdapterClose is not null) await Join(_originalAdapterClose, failures).ConfigureAwait(false);
            foreach (var original in work) await Join(original, failures).ConfigureAwait(false);
            lock (_sync) foreach (var error in _originalErrors) Add(failures, error);
        }
        finally
        {
            try { _ownerStop.Dispose(); } catch (Exception error) { Add(failures, error); }
            phase.Retire(); _executing.Value = previous;
        }
        Throw(failures);
    }

    private async Task<T> RunOriginalAsync<T>(Task start, Func<Task<T>> body)
    {
        await start.ConfigureAwait(false);
        var previous = _executing.Value; var phase = new OriginalPhase(this, previous); _executing.Value = phase;
        Task<T>? actual = null;
        try
        {
            lock (_sync) RequireOpen();
            actual = InvokePhysical(body) ?? throw new InvalidOperationException("No actual original task was returned.");
            return await actual.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            var failures = new List<Exception>(); AddTask(failures, error, actual);
            lock (_sync) foreach (var cause in failures) Add(_originalErrors, cause);
            Throw(failures); throw;
        }
        finally { phase.Retire(); _executing.Value = previous; }
    }
    private void RequireBinding(ManagedDulcheProviderBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!ReferenceEquals(binding.OriginalSelf, binding) || !ReferenceEquals(binding.Issuer, this)
            || !ReferenceEquals(_binding, binding)) throw new UnauthorizedAccessException("This binding is not the actual service-issued object.");
    }
    private void RequireOpen() { if (_closing) throw new ObjectDisposedException(nameof(ManagedDulcheRuntimeService)); }
    private void RequireIndependentClose()
    {
        for (var p = _executing.Value; p is not null; p = p.Parent)
            if (p.IsLive && ReferenceEquals(p.Owner, this)) throw new InvalidOperationException("A service original cannot join its own close.");
        if (_physicalCalls?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("A service original cannot join its own close.");
        _adapter?.RequireIndependentOriginalProviderJoin();
        if (_runtime is { } runtime && _startup is { } startup)
            if (startup.WasAcquired) runtime.RequireIndependentOriginalEndpointJoin(startup.OriginalAcquisition.EndpointId);
    }
    T IDulcheOriginalFactoryCallbackScope.RunOriginalFactoryInvocation<T>(Func<T> actualCallback)
    {
        var phase = _executing.Value;
        while (phase is not null && !(phase.IsLive && ReferenceEquals(phase.Owner, this))) phase = phase.Parent;
        if (phase is null) throw new InvalidOperationException("No live original service factory owns this callback.");
        return InvokePhysical(actualCallback); // Captured/null ExecutionContext inside the callback cannot hide the physical owner.
    }
    private T InvokePhysical<T>(Func<T> body)
    {
        var calls = _physicalCalls ??= []; calls.Add(this);
        try { return body(); } finally { calls.RemoveAt(calls.Count - 1); }
    }
    private sealed class OriginalPhase(ManagedDulcheRuntimeService owner, OriginalPhase? parent)
    {
        public ManagedDulcheRuntimeService Owner { get; } = owner;
        public OriginalPhase? Parent { get; } = parent;
        private int _live = 1;
        public bool IsLive => Volatile.Read(ref _live) != 0;
        public void Retire() => Interlocked.Exchange(ref _live, 0);
    }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Add(List<Exception> causes, Exception cause)
    { if (!causes.Any(actual => ReferenceEquals(actual, cause))) causes.Add(cause); }
    private static void AddTask(List<Exception> causes, Exception error, Task? actual)
    { Add(causes, error); if (actual?.Exception is { } group) foreach (var cause in group.InnerExceptions) Add(causes, cause); }
    private static void ThrowTask(Exception error, Task actual)
    { var causes = new List<Exception>(); AddTask(causes, error, actual); Throw(causes); }
    private static async Task Join(Task actual, List<Exception> causes)
    { try { await actual.ConfigureAwait(false); } catch (Exception error) { AddTask(causes, error, actual); } }
    private static void Throw(List<Exception> causes)
    { if (causes.Count == 1) ExceptionDispatchInfo.Capture(causes[0]).Throw(); if (causes.Count > 1) throw new AggregateException(causes); }
}

/// <summary>Preserves an actual unsuccessful runtime retirement result without reclassifying it as settlement.</summary>
public sealed class ManagedDulcheRetirementException(DulcheError originalError)
    : Exception("The actual managed runtime retirement returned an unsuccessful result.")
{
    public DulcheError OriginalError { get; } = originalError;
}
