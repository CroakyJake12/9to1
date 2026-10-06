namespace Dulche.Runtime;

public sealed partial class DulcheRuntime
{
    /// <summary>Applicable to the configured local inference dispatcher; existing callers/APIs are unchanged.</summary>
    public OperationResult<Unit> setInferenceEngine(InferenceEngine engine)
    {
        var dispatchers = _adapters.Values.OfType<InferenceEngineDispatcher>().ToArray();
        if (dispatchers.Length != 1) return OperationResult<Unit>.Failure(new(DulcheErrorCode.InvalidState,
            "Exactly one configured inference runtime is required for an unscoped override.", "inferenceEngine", false));
        return dispatchers[0].SetInferenceEngine(engine);
    }
    public InferenceEngineDiagnostic getInferenceEngine(string? endpointId = null)
    {
        var dispatchers = _adapters.Values.OfType<InferenceEngineDispatcher>().ToArray();
        if (dispatchers.Length != 1) return new(InferenceEngine.Automatic, null,
            "No unique configured inference dispatcher exists.", null, null, [], []);
        return dispatchers[0].GetInferenceEngine(endpointId);
    }
    public OperationResult<Unit> SetInferenceEngine(InferenceEngine engine) => setInferenceEngine(engine);
    public InferenceEngineDiagnostic GetInferenceEngine(string? endpointId = null) => getInferenceEngine(endpointId);

    /// <summary>Awaitable active override for the SAME runtime endpoint/model. The existing
    /// one-argument preference API stays unchanged; UI settings use this observed control.</summary>
    public Task<OperationResult<Unit>> setInferenceEngine(InferenceEngine engine,string endpointId,CancellationToken cancellationToken=default)
    {
        if(!_endpoints.TryGetValue(endpointId,out var endpoint)||endpoint.Adapter is not InferenceEngineDispatcher dispatcher)
            return Task.FromResult(OperationResult<Unit>.Failure(new(DulcheErrorCode.InvalidState,"No configured inference endpoint exists.",endpointId,false)));
        if(IsLiveOriginalEndpointCall(endpoint)) throw new InvalidOperationException("An original endpoint cannot switch the engine containing it.");
        dispatcher.DemandExternalOriginalJoin();
        var current=dispatcher.GetInferenceEngine(endpointId);
        if(endpoint.Endpoint.Model is null||current.Model!=endpoint.Endpoint.Model)
            return Task.FromResult(OperationResult<Unit>.Failure(new(DulcheErrorCode.InvalidState,"Active switch requires the SAME runtime model identity.",endpointId,false)));
        var stage=new OriginalInferenceEngineChange(endpoint.Endpoint.Model);
        lock(_originalInferenceEngineChangesGate) {
            _originalInferenceEngineChanges.RemoveAll(old=>old.Driver?.IsCompletedSuccessfully==true&&old.Raw?.IsCompletedSuccessfully==true&&old.Raw.Result.Succeeded);
            if(_originalInferenceEngineChanges.Count>=128) throw new InvalidOperationException("Original engine-switch custody is full.");
            _originalInferenceEngineChanges.Add(stage);
        }
        try {
            stage.Driver=endpoint.StartOriginalFiniteControl(start=>ChangeOriginalInferenceEngine(endpoint,dispatcher,engine,start,stage,cancellationToken));
            return stage.Driver;
        }
        catch { lock(_originalInferenceEngineChangesGate) _originalInferenceEngineChanges.Remove(stage);throw; }
    }
    public Task<OperationResult<Unit>> SetInferenceEngineAsync(InferenceEngine engine,string endpointId,CancellationToken cancellationToken=default)
        =>setInferenceEngine(engine,endpointId,cancellationToken);

    public OperationResult<Unit> PermitInferenceEngineInitializationFallback(string endpointId,DulcheError sameObservedFailure)
    {
        if(!_endpoints.TryGetValue(endpointId,out var endpoint)||endpoint.Adapter is not InferenceEngineDispatcher dispatcher)
            return OperationResult<Unit>.Failure(new(DulcheErrorCode.InvalidState,"No configured inference endpoint exists.",endpointId,false));
        if(IsLiveOriginalEndpointCall(endpoint)) throw new InvalidOperationException("An original endpoint cannot permit fallback for its own engine.");
        dispatcher.DemandExternalOriginalJoin();
        if(endpoint.Endpoint.Model is null||dispatcher.GetInferenceEngine(endpointId).Model!=endpoint.Endpoint.Model)
            return OperationResult<Unit>.Failure(new(DulcheErrorCode.InvalidState,"Fallback permission requires the SAME runtime model identity.",endpointId,false));
        return dispatcher.PermitManualInitializationFallback(endpointId,sameObservedFailure);
    }

    private readonly object _originalInferenceEngineChangesGate=new();
    private readonly List<OriginalInferenceEngineChange> _originalInferenceEngineChanges=[];
    private sealed class OriginalInferenceEngineChange(ModelIdentity sameModel)
    { public ModelIdentity Model {get;}=sameModel;public Task<OperationResult<Unit>>? Driver; public Task<OperationResult<Unit>>? Raw; }
    private async Task<OperationResult<Unit>> ChangeOriginalInferenceEngine(EndpointSlot endpoint,InferenceEngineDispatcher dispatcher,
        InferenceEngine engine,Task start,OriginalInferenceEngineChange stage,CancellationToken cancellationToken)
    {
        await start.ConfigureAwait(false);
        var previous=_originalSubmitting.Value;var phase=new OriginalEndpointPhase(endpoint,previous);_originalSubmitting.Value=phase;
        CancellationTokenSource? linked=null;var failures=new List<Exception>();OperationResult<Unit>? denied=null;
        try {
            endpoint.RequireOriginalSubmissionOpen();
            if(endpoint.Endpoint.Model!=stage.Model||dispatcher.GetInferenceEngine(endpoint.Endpoint.EndpointId).Model!=stage.Model)
                throw new InvalidOperationException("The original runtime model changed before engine-switch admission.");
            linked=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,endpoint.Stopping.Token);
            var queue=endpoint.SnapshotQueue();
            if(queue.RunningRequestId is not null||queue.Queued.Count!=0)
                denied=OperationResult<Unit>.Failure(new(DulcheErrorCode.Conflict,"An original request still owns this endpoint.",endpoint.Endpoint.EndpointId,false));
            else try { _ = InvokePhysicalOriginalEndpoint(endpoint,()=> {
                stage.Raw=dispatcher.SwitchInferenceEngineAsync(endpoint.Endpoint.EndpointId,engine,linked.Token);
                return stage.Raw;
            }); }
            catch(OperationCanceledException cause) { AddOriginalStopCause(failures,new AggregateException("The original engine-switch factory faulted synchronously.",cause)); }
            catch(Exception cause) { AddOriginalStopCause(failures,cause); }
            if(stage.Raw is not null) {
                await JoinOriginalStopTaskAsync(stage.Raw,failures).ConfigureAwait(false);
                if(stage.Raw.Exception is { InnerExceptions.Count:1 } group&&group.InnerExceptions[0] is OperationCanceledException)
                    AddOriginalStopCause(failures,group); // Faulted(OCE) stays Faulted; a true canceled raw keeps its own classification.
                if(failures.Count==0&&stage.Raw.Result.Succeeded) {
                    if(endpoint.Endpoint.Model!=stage.Model||dispatcher.GetInferenceEngine(endpoint.Endpoint.EndpointId).Model!=stage.Model)
                        throw new InvalidOperationException("The original runtime model changed before engine-switch disclosure.");
                    endpoint.PublishOriginalManagedReady();
                }
            }
        }
        catch(Exception cause) { AddOriginalStopCause(failures,cause); }
        finally {
            if(linked is not null) try { linked.Dispose(); } catch(Exception cause) { AddOriginalStopCause(failures,cause); }
            phase.Retire();_originalSubmitting.Value=previous;
        }
        ThrowOriginalStopCauses(failures);
        return denied??(stage.Raw?.IsCompletedSuccessfully==true ? stage.Raw.Result
            :OperationResult<Unit>.Failure(new(DulcheErrorCode.ProviderUnavailable,"No actual engine-switch driver was acquired.",endpoint.Endpoint.EndpointId,false)));
    }
}
