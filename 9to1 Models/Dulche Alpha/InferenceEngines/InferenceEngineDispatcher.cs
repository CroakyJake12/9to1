using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Haven.Application;

namespace Dulche.Runtime;

/// <summary>One adapter inside the existing Dulche runtime. Selection changes the engine only;
/// SAME canonical request, original admission/handle, context and permission owners are forwarded.</summary>
public sealed partial class InferenceEngineDispatcher : IDulcheOriginalProviderAdapter, IDulcheOriginalCancellationSource, IAsyncDisposable
{
    private const int Capacity = 128;
    private readonly object _gate = new();
    private readonly Dictionary<InferenceEngine, IInferenceEngineAdapterFactory> _engines;
    private readonly IInferenceRuntimeObservationSource _observations;
    private readonly InferenceCalibrationCache _calibration;
    public string? LastCalibrationDiagnostic { get; private set; }
    private readonly List<Exception> _calibrationFailures=[];
    private int _calibrationReservations;
    private readonly Dictionary<string, Endpoint> _endpoints = new(StringComparer.Ordinal);
    private readonly AsyncLocal<Phase?> _executing = new();
    [ThreadStatic] private static List<InferenceEngineDispatcher>? _physical;
    private InferenceEngine _requested;
    private bool _sealed;
    private Task? _close;

    public InferenceEngineDispatcher(string providerId, Uri originalConfiguredTarget,
        IEnumerable<InferenceEngineRegistration> engines, IInferenceRuntimeObservationSource observations)
        :this(providerId,originalConfiguredTarget,engines,observations,null) { }
    public InferenceEngineDispatcher(string providerId, Uri originalConfiguredTarget,
        IEnumerable<InferenceEngineRegistration> engines, IInferenceRuntimeObservationSource observations,
        string? configuredLocalCalibrationDiagnosticFile)
    {
        ProviderId = string.IsNullOrWhiteSpace(providerId) ? throw new ArgumentException("Provider identity is required.", nameof(providerId)) : providerId;
        OriginalConfiguredTarget = originalConfiguredTarget ?? throw new ArgumentNullException(nameof(originalConfiguredTarget));
        _observations = observations ?? throw new ArgumentNullException(nameof(observations));
        _engines = new();
        foreach (var item in engines)
        {
            if (item.Engine is not (InferenceEngine.LlamaCpp or InferenceEngine.Strata) || item.Factory is null
                || !_engines.TryAdd(item.Engine, item.Factory))
                throw new ArgumentException("Each concrete local engine must share the SAME configured provider and target.", nameof(engines));
        }
        if (_engines.Count == 0) throw new ArgumentException("At least one actual engine is required.", nameof(engines));
        _calibration=new(configuredLocalCalibrationDiagnosticFile);
    }

    public string ProviderId { get; }
    public Uri OriginalConfiguredTarget { get; }
    public string RuntimeVersion => "dulche-multi-engine-1";
    public bool IsLocal => true;
    // Catalogue availability does not grant capabilities. Runtime/model compatibility is checked before load.
    public IReadOnlySet<string> Capabilities => new HashSet<string>(StringComparer.Ordinal) { "chat", "streaming", "canonical-task-context", "inference-engine-selection" };

    public OperationResult<Unit> SetInferenceEngine(InferenceEngine requested)
    {
        DemandExternalJoin();
        if (!Enum.IsDefined(requested)) return Error<Unit>(DulcheErrorCode.InvalidArgument, "Unknown inference engine.");
        lock (_gate)
        {
            if (_sealed) return Error<Unit>(DulcheErrorCode.InvalidState, "Inference dispatch is sealed.");
            if (_endpoints.Values.Any(owner => owner.Operations.Any(task => !task.IsCompleted) || owner.Handles.Count != 0))
                return Error<Unit>(DulcheErrorCode.Conflict, "An original request or initialization is still owned.");
            _requested = requested;
            foreach(var owner in _endpoints.Values) owner.ManualFallback=null;
            return OperationResult<Unit>.Success(Unit.Value);
        }
    }

    public InferenceEngineDiagnostic GetInferenceEngine(string? endpointId = null)
    {
        lock(_gate) {
            Endpoint? owner=endpointId is not null ? _endpoints.GetValueOrDefault(endpointId)
                :_endpoints.Count==1 ? _endpoints.Values.Single() :null;
            if(owner is null) return new(_requested,null,"No unique active engine; preference applies at model initialization.",null,null,[],[]) { PendingPreference=_requested };
            var active=!_sealed&&!owner.Sealed&&!owner.Initializing&&owner.Selected is not null;
            return owner.Diagnostic with {
                Requested=_requested, PendingPreference=_requested,
                Effective=active ? owner.Diagnostic.Effective :null,
                ActiveSelectionPreference=active ? owner.ActiveSelectionPreference :null,
                SelectionPending=owner.Initializing||active&&owner.ActiveSelectionPreference!=_requested
            };
        }
    }

    /// <summary>Explicit active switch of the SAME loaded model. The returned actual driver is
    /// published/owned before any callback; initialization joins the old lease before fresh load.</summary>
    public Task<OperationResult<Unit>> SwitchInferenceEngineAsync(string endpointId,InferenceEngine requested,CancellationToken cancellationToken=default)
    {
        DemandExternalJoin();
        if(!Enum.IsDefined(requested)) return Task.FromResult(Error<Unit>(DulcheErrorCode.InvalidArgument,"Unknown inference engine."));
        lock(_gate) {
            var owner=Required(endpointId);
            if(_sealed||owner.Sealed||owner.Diagnostic.Model is not { } model)
                return Task.FromResult(Error<Unit>(DulcheErrorCode.InvalidState,"No original model is available for an engine switch."));
            if(owner.Operations.Count>=4096||_endpoints.Values.Any(value=>value.Initializing||value.Handles.Count!=0||value.Operations.Any(task=>!task.IsCompleted)))
                return Task.FromResult(Error<Unit>(DulcheErrorCode.Conflict,"An original request or initialization still owns this runtime."));
            cancellationToken.ThrowIfCancellationRequested();
            if(_requested!=requested) foreach(var value in _endpoints.Values) value.ManualFallback=null;
            _requested=requested;
            var start=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original=Drive(owner,start.Task,actual=>InitializeAsync(actual,actual.Original with { Model=model },model,cancellationToken));
            owner.Operations.Add(original);owner.EngineChange=original;start.SetResult();return original;
        }
    }

    /// <summary>Explicit post-failure caller permission; it does not authorize model/context/tool access.</summary>
    public OperationResult<Unit> PermitManualInitializationFallback(string endpointId, DulcheError sameObservedFailure)
    {
        DemandExternalJoin();
        lock (_gate)
        {
            var owner = Required(endpointId);
            if (_requested == InferenceEngine.Automatic || owner.LastFailure is null || owner.Diagnostic.Model is null
                || owner.Diagnostic.Requested!=_requested
                || !ReferenceEquals(owner.LastFailure, sameObservedFailure) || owner.Operations.Any(task => !task.IsCompleted))
                return Error<Unit>(DulcheErrorCode.InvalidState, "Permission must refer to this endpoint's actual terminal initialization failure.");
            owner.ManualFallback = new(sameObservedFailure,owner.Diagnostic.Model,_requested);
            return OperationResult<Unit>.Success(Unit.Value);
        }
    }

    public ValueTask<OperationResult<Unit>> StartAsync(DulcheEndpoint endpoint, CancellationToken cancellationToken)
    {
        Endpoint owner;
        lock (_gate)
        {
            if (_sealed || _endpoints.ContainsKey(endpoint.EndpointId) || _endpoints.Count >= Capacity)
                return ValueTask.FromResult(Error<Unit>(DulcheErrorCode.InvalidState, "Endpoint admission is sealed, duplicated or full."));
            owner = new(endpoint); _endpoints.Add(endpoint.EndpointId, owner);
        }
        // Empty endpoints cannot advertise Ready without an actual engine/model observation.
        if (endpoint.Model is null) return ValueTask.FromResult(Error<Unit>(DulcheErrorCode.InvalidArgument, "An inference endpoint requires its exact canonical model before initialization."));
        return new(Publish(owner, actual => InitializeAsync(actual, endpoint, endpoint.Model, cancellationToken)));
    }

    public ValueTask<OperationResult<Unit>> LoadModelAsync(DulcheEndpoint endpoint, ModelIdentity model, CancellationToken cancellationToken)
    {
        var owner = RequiredThreadSafe(endpoint.EndpointId);
        return new(Publish(owner, async actual => {
            bool same; lock (_gate) same=actual.Selected is not null&&actual.Diagnostic.Model==model
                &&(_requested==InferenceEngine.Automatic||_requested==actual.Diagnostic.Effective);
            if(same) {
                var health=await Original(actual,()=>Selected(actual).HealthAsync(endpoint,cancellationToken).AsTask()).ConfigureAwait(false);
                if(health.State==EndpointState.Ready) {
                    lock(_gate) {
                        if(_sealed||actual.Sealed) throw new InvalidOperationException("The same-model observation retired before selected-mode disclosure.");
                        cancellationToken.ThrowIfCancellationRequested();
                        actual.ActiveSelectionPreference=_requested;
                    }
                    return OperationResult<Unit>.Success(Unit.Value);
                }
            }
            return await InitializeAsync(actual, endpoint, model, cancellationToken).ConfigureAwait(false);
        }));
    }

    private async Task<OperationResult<Unit>> InitializeAsync(Endpoint owner, DulcheEndpoint endpoint, ModelIdentity model, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, owner.Retirement.Token);
        cancellationToken = linked.Token;
        var sourceScope=new SourceScope(this,owner,_executing.Value??throw new InvalidOperationException("No original initialization driver."));
        InferenceEngine preference;
        lock (_gate)
        {
            if (owner.Handles.Count != 0 || owner.Initializing) return Error<Unit>(DulcheErrorCode.Conflict, "The existing original model/request is still owned.");
            owner.Initializing = true; preference = _requested;
        }
        try
        {
            if (owner.CurrentLease is not null)
            {
                var prior=owner.CurrentLease;
                prior.DemandExternalOriginalJoin();
                lock (_gate) { owner.Selected = null; owner.CurrentLease = null; owner.ActiveSelectionPreference=null; }
                await Original(owner, prior.CloseOriginalAsync).ConfigureAwait(false);
            }
            var observation = InferenceCompatibilityRegistry.Detach(await Original(owner,
                () => _observations.ObserveOriginalAsync(model, sourceScope, cancellationToken)).ConfigureAwait(false));
            if (observation.Requirements.Model != model) return Error<Unit>(DulcheErrorCode.ModelLoadFailed, "The actual model observation names a different canonical artifact.");
            var reports = observation.Engines.Select(profile => InferenceCompatibilityRegistry.Inspect(observation.Requirements, observation.Hardware, profile)).ToArray();
            if (reports.Select(report => report.Engine).Distinct().Count() != reports.Length)
                return Error<Unit>(DulcheErrorCode.InvalidState, "Duplicate engine observations are ambiguous.");
            var fit = reports.Where(report => report.Compatible && _engines.ContainsKey(report.Engine)).Select(report => report.Engine).ToArray();
            var preferred = observation.Requirements.PreferredEngines ?? [];
            var ordered = fit.OrderBy(engine => preferred.Contains(engine) ? preferred.ToList().IndexOf(engine) : int.MaxValue)
                .ThenBy(engine => engine == InferenceEngine.LlamaCpp ? 0 : 1).ToArray();
            var calibrated=_calibration.TryOrder(observation,ordered,out var measuredOrder,out var calibrationReason);
            if(preference==InferenceEngine.Automatic&&calibrated) ordered=measuredOrder;
            bool manualFallback; lock (_gate) {
                var permit=owner.ManualFallback; owner.ManualFallback=null;
                manualFallback=permit is not null&&permit.Model==model&&permit.Engine==preference
                    &&ReferenceEquals(permit.Failure,owner.LastFailure);
            }
            var candidates = preference == InferenceEngine.Automatic ? ordered
                : ordered.Where(engine => engine == preference).Concat(manualFallback ? ordered.Where(engine => engine != preference) : []).ToArray();
            var failures = new List<DulcheError>();
            lock (_gate) owner.Diagnostic = new(preference, null,
                preference == InferenceEngine.Automatic ? (calibrated ? calibrationReason : "Compatible engines ordered by declared fit; llama.cpp is the uncalibrated default. No local timing measurement is claimed. "+calibrationReason)
                : "Explicit manual engine; no substitution without this failure's post-failure permission.", model, observation.Hardware.Fingerprint, reports, []);
            if (candidates.Length == 0)
            {
                var incompatible = new DulcheError(DulcheErrorCode.UnsupportedCapability, "Requested engine has unmet model/runtime/hardware requirements.", ProviderId, false,
                    Details: reports.SelectMany(report => report.Unmet.Select((gap, index) => new KeyValuePair<string, string>($"{report.Engine}.{index}.{gap.Requirement}", $"Expected {gap.Expected}; actual {gap.Actual}"))).ToDictionary());
                lock (_gate) owner.LastFailure = incompatible; return OperationResult<Unit>.Failure(incompatible);
            }
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CheckAdmission(owner);
                OriginalInferenceEngineLease? lease = null;
                OperationResult<Unit> loaded;
                try
                {
                    lease = await Original(owner, () => _engines[candidate].CreateOriginalAsync(model,sourceScope,cancellationToken)).ConfigureAwait(false);
                    if (lease is null) throw new InvalidDataException("No actual engine lease was returned.");
                    lock (_gate) owner.Leases.Add(lease); // Retain late successful products before any seal check.
                    var actual = lease.Adapter;
                    if (actual.ProviderId != ProviderId || !actual.IsLocal || actual.OriginalConfiguredTarget != OriginalConfiguredTarget)
                        throw new InvalidDataException("The actual engine lease changed the configured provider/target.");
                    CheckAdmission(owner);
                    var started = await ProductiveOriginal(owner,cancellationToken, () => actual.StartAsync(endpoint, cancellationToken).AsTask()).ConfigureAwait(false);
                    loaded = started.Succeeded ? await ProductiveOriginal(owner,cancellationToken, () => actual.LoadModelAsync(endpoint, model, cancellationToken).AsTask()).ConfigureAwait(false) : started;
                    if (loaded.Succeeded)
                    {
                        lock (_gate)
                        {
                            if(_sealed||owner.Sealed) throw new InvalidOperationException("The original initialization was retired before disclosure.");
                            cancellationToken.ThrowIfCancellationRequested();
                            owner.Selected = actual; owner.CurrentLease = lease; owner.LastFailure = null; owner.ActiveSelectionPreference=preference;
                            owner.Diagnostic = owner.Diagnostic with { Effective = candidate, InitializationFailures = failures.ToArray(),
                                Reason = failures.Count == 0 ? owner.Diagnostic.Reason : owner.Diagnostic.Reason + " Prior compatible initialization failed and its actual cleanup succeeded." };
                        }
                        return loaded;
                    }
                }
                catch (InferenceEngineException failure)
                {
                    // Only the actual typed native failure is projected. Failed cleanup envelopes never qualify.
                    loaded = OperationResult<Unit>.Failure(failure.Error);
                }
                failures.Add(loaded.Error!); lock (_gate) owner.LastFailure = loaded.Error;
                if (lease is not null)
                {
                    lease.DemandExternalOriginalJoin();
                    await Original(owner, lease.CloseOriginalAsync).ConfigureAwait(false);
                }
                // A factory failure may fall through only after its own encompassing Task has completed;
                // the factory contract requires original resources/late products to be joined before failure.

            }
            lock (_gate) owner.Diagnostic = owner.Diagnostic with { InitializationFailures = failures.ToArray() };
            return OperationResult<Unit>.Failure(owner.LastFailure!);
        }
        finally { lock (_gate) owner.Initializing = false; }
    }

    public Task BindOriginalRequestAsync(DulcheEndpoint endpoint, DulcheRequest originalFrozenRequest,
        RuntimeRequestHandle originalHandle, TaskRunAttemptAdmission originalAdmission, Guid originalActionId, CancellationToken cancellationToken)
    {
        var owner=RequiredThreadSafe(endpoint.EndpointId);
        lock(_gate) if(owner.EngineChange is { IsCompleted:false } change)
            return Publish(owner,actual=>BindAfterOriginalEngineChange(actual,change,endpoint,originalFrozenRequest,originalHandle,originalAdmission,originalActionId,cancellationToken));
        return BindSelectedOriginal(owner,endpoint,originalFrozenRequest,originalHandle,originalAdmission,originalActionId,cancellationToken);
    }
    private async Task<Unit> BindAfterOriginalEngineChange(Endpoint owner,Task<OperationResult<Unit>> sameChange,DulcheEndpoint endpoint,
        DulcheRequest request,RuntimeRequestHandle handle,TaskRunAttemptAdmission admission,Guid action,CancellationToken cancellationToken)
    {
        var result=await Original(owner,()=>sameChange.WaitAsync(cancellationToken)).ConfigureAwait(false);
        if(!result.Succeeded) throw new InferenceEngineException(result.Error!);
        CheckAdmission(owner);cancellationToken.ThrowIfCancellationRequested();
        await Original(owner,()=>BindSelectedOriginal(owner,endpoint,request,handle,admission,action,cancellationToken)).ConfigureAwait(false);
        return Unit.Value;
    }
    private Task BindSelectedOriginal(Endpoint owner,DulcheEndpoint endpoint,DulcheRequest request,RuntimeRequestHandle handle,
        TaskRunAttemptAdmission admission,Guid action,CancellationToken cancellationToken)
    {
        lock(_gate) { if(owner.Initializing) throw new InvalidOperationException("The actual engine initialization has no completed selected binding.");owner.Handles.Add(handle); }
        try { return Forward(owner,()=>Selected(owner).BindOriginalRequestAsync(endpoint,request,handle,admission,action,cancellationToken)); }
        catch { lock(_gate) owner.Handles.Remove(handle);throw; }
    }

    public void CaptureOriginalDispatchRequest(RuntimeRequestHandle originalHandle, DulcheRequest originalFrozenRequest, DulcheRequest actualDispatchRequest)
    {
        Endpoint owner;
        lock (_gate) owner = _endpoints.Values.Single(value => value.Handles.Contains(originalHandle));
        Physical(() => { Selected(owner).CaptureOriginalDispatchRequest(originalHandle, originalFrozenRequest, actualDispatchRequest); return Unit.Value; });
    }

    public IAsyncEnumerable<AdapterDelta> GenerateAsync(DulcheEndpoint endpoint, DulcheRequest request, string requestId, CancellationToken cancellationToken)
        => GenerateOwnedAsync(RequiredThreadSafe(endpoint.EndpointId), endpoint, request, requestId, cancellationToken);

    private async IAsyncEnumerable<AdapterDelta> GenerateOwnedAsync(Endpoint owner, DulcheEndpoint endpoint, DulcheRequest request,
        string requestId, [EnumeratorCancellation] CancellationToken cancellationToken, ToolInvocationResult? continuation = null)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, owner.Retirement.Token);
        var output = Channel.CreateBounded<AdapterDelta>(new BoundedChannelOptions(8) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        var producer = Publish(owner, async actual =>
        {
            var errors = new List<Exception>(); IAsyncEnumerator<AdapterDelta>? iterator = null;
            try
            {
                iterator = Physical(() => (continuation is null ? Selected(actual).GenerateAsync(endpoint, request, requestId, linked.Token)
                    : Selected(actual).ContinueWithToolResultAsync(endpoint, request, requestId, continuation, linked.Token)).GetAsyncEnumerator(linked.Token));
                while (await Original(actual, () => iterator.MoveNextAsync().AsTask()).ConfigureAwait(false))
                    await output.Writer.WriteAsync(iterator.Current, linked.Token).ConfigureAwait(false);
            }
            catch (Exception error) { Add(errors, error); }
            finally
            {
                if (iterator is not null)
                    try { await Original(actual, () => iterator.DisposeAsync().AsTask(), owningCleanup:true).ConfigureAwait(false); }
                    catch (Exception error) { Add(errors, error); }
                if(errors.Count==0&&request.Model is { } model) {
                    try {
                        OriginalInferenceEngineLease? lease;
                        lock(_gate) {
                            lease=_sealed||actual.Sealed||_calibrationReservations>=4096 ? null : actual.CurrentLease;
                            if(lease is IOriginalInferenceCalibrationLease) _calibrationReservations++;
                        }
                        if(lease is IOriginalInferenceCalibrationLease measuredLease) {
                            // The actual generation/dispose driver already owns this finite observation.
                            // Source getters/callbacks run outside the metadata gate on its physical scope.
                            Physical(()=> {
                                var source=measuredLease.CalibrationSource;
                                if(source is not null&&source.TryObserveOriginalCalibration(model,out var reading)&&reading is not null)
                                    _calibration.RecordOriginal(source,reading);
                                return Unit.Value;
                            });
                        }
                    }
                    catch(Exception calibrationFailure) {
                        lock(_gate) {
                            Add(_calibrationFailures,calibrationFailure);
                            LastCalibrationDiagnostic="Actual inference completed; optional calibration observation unavailable: "+calibrationFailure.Message;
                        }
                    }
                }
                lock (_gate) actual.Handles.RemoveWhere(handle => handle.RequestId == requestId);
                output.Writer.TryComplete(errors.Count == 0 ? null : errors.Count == 1 ? errors[0] : new AggregateException(errors));
            }
            Throw(errors); return Unit.Value;
        });
        try { await foreach (var delta in output.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) yield return delta; }
        finally
        {
            var errors = new List<Exception>();
            try { linked.Cancel(); } catch (Exception error) { Add(errors, error); }
            await Join(producer, errors).ConfigureAwait(false); Throw(errors);
        }
    }

    public IAsyncEnumerable<AdapterDelta> ContinueWithToolResultAsync(DulcheEndpoint endpoint, DulcheRequest request, string requestId,
        ToolInvocationResult toolResult, CancellationToken cancellationToken)
        => GenerateOwnedAsync(RequiredThreadSafe(endpoint.EndpointId), endpoint, request, requestId, cancellationToken, toolResult);
    public ValueTask<OperationResult<Unit>> CancelAsync(DulcheEndpoint endpoint, string requestId, CancellationToken cancellationToken)
        => new(Forward(RequiredThreadSafe(endpoint.EndpointId), () => Selected(RequiredThreadSafe(endpoint.EndpointId)).CancelAsync(endpoint, requestId, cancellationToken).AsTask()));
    public ValueTask<OperationResult<Unit>> PauseAsync(DulcheEndpoint endpoint, string requestId, CancellationToken cancellationToken)
        => new(Forward(RequiredThreadSafe(endpoint.EndpointId), () => Selected(RequiredThreadSafe(endpoint.EndpointId)).PauseAsync(endpoint, requestId, cancellationToken).AsTask()));
    public ValueTask<OperationResult<Unit>> ResumeAsync(DulcheEndpoint endpoint, string requestId, CancellationToken cancellationToken)
        => new(Forward(RequiredThreadSafe(endpoint.EndpointId), () => Selected(RequiredThreadSafe(endpoint.EndpointId)).ResumeAsync(endpoint, requestId, cancellationToken).AsTask()));
    public ValueTask<RuntimeHealth> HealthAsync(DulcheEndpoint endpoint, CancellationToken cancellationToken)
        => new(Forward(RequiredThreadSafe(endpoint.EndpointId), () => Selected(RequiredThreadSafe(endpoint.EndpointId)).HealthAsync(endpoint, cancellationToken).AsTask()));

    public bool TryObserveOriginalCancellation(RuntimeRequestHandle originalHandle, Exception originalOutwardFailure,
        CancellationToken originalOwnerCancellation, out DulcheOriginalCancellationObservation? observation)
    {
        IDulcheOriginalProviderAdapter? selected;
        lock (_gate) selected = _endpoints.GetValueOrDefault(originalHandle.EndpointId)?.Selected;
        if (selected is IDulcheOriginalCancellationSource source)
            return source.TryObserveOriginalCancellation(originalHandle, originalOutwardFailure, originalOwnerCancellation, out observation);
        observation = null; return false;
    }

    public ValueTask<OperationResult<Unit>> StopAsync(DulcheEndpoint endpoint, CancellationToken cancellationToken)
    {
        DemandExternalJoin(); var owner = RequiredThreadSafe(endpoint.EndpointId);
        lock (_gate)
        {
            if (owner.Stop is not null) return new(owner.Stop);
            owner.Sealed = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.Stop = StopBody(owner, endpoint, start.Task); start.SetResult(); return new(owner.Stop);
        }
    }
    private async Task<OperationResult<Unit>> StopBody(Endpoint owner, DulcheEndpoint endpoint, Task start)
    {
        await start.ConfigureAwait(false); var failures = new List<Exception>();
        try { Physical(()=> { owner.Retirement.Cancel();return Unit.Value; }); } catch (Exception error) { Add(failures, error); }
        var closes = new Dictionary<OriginalInferenceEngineLease,Task>(ReferenceEqualityComparer.Instance);
        void StartCloses()
        {
            OriginalInferenceEngineLease[] leases; lock (_gate) leases=owner.Leases.ToArray();
            foreach(var lease in leases)
                if(!closes.ContainsKey(lease))
                    try { closes.Add(lease,Physical(lease.CloseOriginalAsync)); } catch(Exception error) { Add(failures,error); }
        }
        StartCloses(); // Release current engine work before joining admitted public drivers.
        ForwardReservation[] forwards; lock(_gate) forwards=owner.Forwards.ToArray();
        foreach(var reservation in forwards) {
            await reservation.Published.Task.ConfigureAwait(false);
            if(reservation.Original is not null) await Join(reservation.Original,failures).ConfigureAwait(false);
            if(reservation.Failure is not null) Add(failures,reservation.Failure);
        }
        Task[] originals; lock (_gate) originals=owner.Operations.Concat(owner.Raw).Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
        foreach(var actual in originals) await Join(actual,failures).ConfigureAwait(false);
        // Encompassing producers may publish raw cleanup/Move originals after the first snapshot.
        Task[] finalRaw; lock(_gate) finalRaw=owner.Raw.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
        foreach(var actual in finalRaw) await Join(actual,failures).ConfigureAwait(false);
        StartCloses(); // Include every late successful factory product before endpoint cleanup can return.
        foreach(var close in closes.Values) await Join(close,failures).ConfigureAwait(false);
        Throw(failures); return OperationResult<Unit>.Success(Unit.Value);
    }

    public ValueTask DisposeAsync()
    {
        DemandExternalJoin();
        lock (_gate)
        {
            if (_close is not null) return new(_close);
            _sealed = true; var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseBody(start.Task); start.SetResult(); return new(_close);
        }
    }
    private async Task CloseBody(Task start)
    {
        await start.ConfigureAwait(false); var failures = new List<Exception>(); var stops = new List<Task<OperationResult<Unit>>>();
        Endpoint[] owners; lock (_gate) owners = _endpoints.Values.ToArray();
        foreach (var owner in owners)
            try { stops.Add(StopAsync(owner.Original, CancellationToken.None).AsTask()); } catch (Exception error) { Add(failures, error); }
        foreach (var stop in stops)
        {
            await Join(stop, failures).ConfigureAwait(false);
            if (stop.IsCompletedSuccessfully && !stop.Result.Succeeded) Add(failures, new InvalidOperationException(stop.Result.Error!.Message));
        }
        foreach (var owner in owners) try { owner.Retirement.Dispose(); } catch (Exception error) { Add(failures, error); }
        Task? calibrationClose=null;
        try { calibrationClose=_calibration.DisposeAsync().AsTask(); } catch(Exception error) { Add(failures,error); }
        if(calibrationClose is not null) await Join(calibrationClose,failures).ConfigureAwait(false);
        lock(_gate) foreach(var failure in _calibrationFailures) Add(failures,failure);
        Throw(failures);
    }

    // Return the SAME actual finite inner Task. Its inherited live phase is tied to that Task,
    // so an asynchronous callback cannot join its own encompassing dispatcher after restoring EC.
    private Task Forward(Endpoint owner, Func<Task> factory)
    {
        var reservation=ReserveForward(owner); var previous=_executing.Value; var phase=new Phase(previous); _executing.Value=phase;
        try { CheckAdmission(owner); var task=Physical(factory) ?? throw new InvalidOperationException("No actual engine task."); phase.Actual=task; reservation.Original=task;
            lock(_gate) { owner.Raw.Add(task); owner.Operations.Add(task); } return task; }
        catch(Exception error) { phase.Live=false; reservation.Failure=error; throw; }
        finally { reservation.Published.TrySetResult(); _executing.Value=previous; }
    }
    private Task<T> Forward<T>(Endpoint owner, Func<Task<T>> factory)
    {
        var reservation=ReserveForward(owner); var previous=_executing.Value; var phase=new Phase(previous); _executing.Value=phase;
        try { CheckAdmission(owner); var task=Physical(factory) ?? throw new InvalidOperationException("No actual engine task."); phase.Actual=task; reservation.Original=task;
            lock(_gate) { owner.Raw.Add(task); owner.Operations.Add(task); } return task; }
        catch(Exception error) { phase.Live=false; reservation.Failure=error; throw; }
        finally { reservation.Published.TrySetResult(); _executing.Value=previous; }
    }
    private ForwardReservation ReserveForward(Endpoint owner)
    {
        lock(_gate) {
            if(_sealed||owner.Sealed||owner.Operations.Count+owner.Forwards.Count>=4096)
                throw new InvalidOperationException("Actual forwarded source admission is sealed or full.");
            var reservation=new ForwardReservation(); owner.Forwards.Add(reservation); return reservation;
        }
    }
    private void CheckAdmission(Endpoint owner)
    { lock(_gate) if(_sealed||owner.Sealed) throw new InvalidOperationException("Inference initialization/factory admission is sealed."); }

    private Task<T> Publish<T>(Endpoint owner, Func<Endpoint, Task<T>> body)
    {
        lock (_gate)
        {
            if (_sealed || owner.Sealed || owner.Operations.Count >= 4096) throw new InvalidOperationException("Actual engine operation custody is sealed or full.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = Drive(owner, start.Task, body); owner.Operations.Add(task); start.SetResult(); return task;
        }
    }
    private async Task<T> Drive<T>(Endpoint owner, Task start, Func<Endpoint, Task<T>> body)
    {
        await start.ConfigureAwait(false); var previous = _executing.Value; var phase = new Phase(previous); _executing.Value = phase;
        try { return await body(owner).ConfigureAwait(false); }
        finally { phase.Live = false; _executing.Value = previous; }
    }
    private async Task<T> Original<T>(Endpoint owner, Func<Task<T>> factory, bool owningCleanup=false)
    {
        ReserveOriginal(owner,owningCleanup);
        var task = Physical(factory) ?? throw new InvalidOperationException("Engine returned no original task.");
        lock (_gate) owner.Raw.Add(task);
        try { return await task.ConfigureAwait(false); }
        catch (Exception error) { var failures = new List<Exception>(); Add(failures, error); if (task.Exception is { } group) { foreach (var cause in group.InnerExceptions) Add(failures, cause); if (group.InnerExceptions.Count == 1 && group.InnerExceptions[0] is OperationCanceledException) Add(failures, group); } Throw(failures); throw; }
    }
    private Task<T> ProductiveOriginal<T>(Endpoint owner,CancellationToken cancellationToken,Func<Task<T>> factory)
    {
        lock(_gate) {
            if(_sealed||owner.Sealed) throw new InvalidOperationException("Original model factory admission is sealed.");
            cancellationToken.ThrowIfCancellationRequested();
            // This productive callback is admitted on the already-published initialization driver.
            // Any subsequent seal joins that driver and the SAME acquired raw Task independently.
        }
        return Original(owner,factory);
    }
    private async Task Original(Endpoint owner, Func<Task> factory, bool owningCleanup=false)
    {
        ReserveOriginal(owner,owningCleanup);
        var task = Physical(factory) ?? throw new InvalidOperationException("Engine returned no original task.");
        lock (_gate) owner.Raw.Add(task);
        var errors = new List<Exception>(); await Join(task, errors).ConfigureAwait(false); Throw(errors);
    }
    private void ReserveOriginal(Endpoint owner,bool cleanup)
    { lock(_gate) if(cleanup ? ++owner.CleanupReservations>8192 : ++owner.RawReservations>131072)
        throw new InvalidOperationException("Bounded original inference source custody is full."); }
    private T Physical<T>(Func<T> factory)
    {
        var owners = _physical ??= []; owners.Add(this);
        try { return factory(); }
        catch (OperationCanceledException cause) { throw new AggregateException("The original engine callback faulted synchronously.", cause); }
        finally { owners.RemoveAt(owners.Count - 1); }
    }
    public void DemandExternalOriginalJoin()=>DemandExternalJoin();
    private void DemandExternalJoin()
    {
        _calibration.DemandExternalOriginalJoin();
        OriginalInferenceEngineLease[] leases; lock(_gate) leases=_endpoints.Values.SelectMany(owner=>owner.Leases).ToArray();
        foreach(var lease in leases) lease.DemandExternalOriginalJoin();
        for (var phase = _executing.Value; phase is not null; phase = phase.Parent)
            if (phase.Live && phase.Actual?.IsCompleted != true) throw new InvalidOperationException("An engine original cannot join its own dispatch owner.");
        if (_physical?.Contains(this) == true) throw new InvalidOperationException("A physical engine callback cannot join its own dispatch owner.");
    }
    private Endpoint RequiredThreadSafe(string id) { lock (_gate) return Required(id); }
    private Endpoint Required(string id) => _endpoints.GetValueOrDefault(id) ?? throw new InvalidOperationException("No original inference endpoint.");
    private IDulcheOriginalProviderAdapter Selected(Endpoint owner) { lock (_gate) return owner.Selected ?? throw new InvalidOperationException("No compatible initialized inference engine."); }
    private OperationResult<T> Error<T>(DulcheErrorCode code, string message) => OperationResult<T>.Failure(new(code, message, ProviderId, false));
    private static void Add(List<Exception> errors, Exception error) { if (!errors.Any(value => ReferenceEquals(value, error))) errors.Add(error); }
    private static async Task Join(Task task, List<Exception> errors)
    {
        try { await task.ConfigureAwait(false); } catch (Exception error) { Add(errors, error); }
        if (task.Exception is { } group) { foreach (var error in group.InnerExceptions) Add(errors, error); if (group.InnerExceptions.Count == 1 && group.InnerExceptions[0] is OperationCanceledException) Add(errors, group); }
    }
    private static void Throw(List<Exception> errors)
    { if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw(); if (errors.Count > 1) throw new AggregateException(errors); }
    private sealed class Phase(Phase? parent) { public Phase? Parent { get; } = parent; public volatile bool Live = true; public Task? Actual; }
    private sealed class SourceScope(InferenceEngineDispatcher parent,Endpoint endpoint,Phase phase):IInferenceEngineOriginalSourceScope
    {
        public T InvokeOriginalFactory<T>(Func<T> factory)=>Invoke(factory,false);
        public T InvokeOriginalCleanup<T>(Func<T> cleanup)=>Invoke(cleanup,true);
        private T Invoke<T>(Func<T> factory,bool cleanup)
        {
            lock(parent._gate)
            {
                if(!phase.Live||!cleanup&&(parent._sealed||endpoint.Sealed))
                    throw new InvalidOperationException("The original initialization callback is no longer admitted.");
            }
            parent.ReserveOriginal(endpoint,cleanup);
            return parent.Physical(()=> { var value=factory(); if(value is Task task) RetainOriginalTask(task); return value; });
        }
        public void RetainOriginalTask(Task task)
        {
            ArgumentNullException.ThrowIfNull(task);
            lock(parent._gate)
            {
                if(!phase.Live||_physical?.Contains(parent)!=true) throw new InvalidOperationException("Retain inside the same finite original initialization callback.");
                if(!endpoint.Raw.Contains(task,ReferenceEqualityComparer.Instance)) endpoint.Raw.Add(task);
            }
        }
    }
    private sealed class ForwardReservation {
        public TaskCompletionSource Published { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task? Original; public Exception? Failure;
    }
    private sealed record ManualFallbackPermit(DulcheError Failure,ModelIdentity Model,InferenceEngine Engine);
    private sealed class Endpoint(DulcheEndpoint original)
    {
        public DulcheEndpoint Original { get; } = original;
        public IDulcheOriginalProviderAdapter? Selected; public OriginalInferenceEngineLease? CurrentLease; public bool Sealed; public bool Initializing; public ManualFallbackPermit? ManualFallback;
        public DulcheError? LastFailure; public Task<OperationResult<Unit>>? Stop; public Task<OperationResult<Unit>>? EngineChange;
        public InferenceEngine? ActiveSelectionPreference;
        public int RawReservations; public int CleanupReservations;
        public CancellationTokenSource Retirement { get; } = new();
        public List<Task> Operations { get; } = []; public List<Task> Raw { get; } = [];
        public List<OriginalInferenceEngineLease> Leases { get; } = []; public List<ForwardReservation> Forwards { get; } = [];
        public HashSet<RuntimeRequestHandle> Handles { get; } = new(ReferenceEqualityComparer.Instance);
        public InferenceEngineDiagnostic Diagnostic = new(InferenceEngine.Automatic, null, "No model initialized.", null, null, [], []);
    }
}
