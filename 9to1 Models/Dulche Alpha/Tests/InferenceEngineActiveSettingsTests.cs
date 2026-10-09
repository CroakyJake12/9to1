using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Dulche.Runtime;
using Haven.Application;
using Xunit;

namespace Dulche.Tests;

/// <summary>Actual dispatcher/runtime controls with deliberately synthetic engine observations.
/// These do not issue model/TaskRun authority or prove CUDA, native loading or measured performance.</summary>
public sealed class InferenceEngineActiveSettingsTests
{
    [Fact]
    public async Task Pending_preference_does_not_relabel_the_active_mode_and_active_switch_joins_old_lease()
    {
        var rig=new Rig();Task? actual=null;Exception? primary=null;
        try {
            Assert.True((await rig.Dispatcher.StartAsync(rig.Endpoint,CancellationToken.None)).Succeeded);
            Assert.Equal("Automatic",rig.Report.SelectionMode);
            Assert.True(rig.Dispatcher.SetInferenceEngine(InferenceEngine.Strata).Succeeded);
            Assert.Equal(InferenceEngine.LlamaCpp,rig.Report.Effective);
            Assert.Equal(InferenceEngine.Automatic,rig.Report.ActiveSelectionPreference);
            Assert.Equal(InferenceEngine.Strata,rig.Report.PendingPreference);
            Assert.True(rig.Report.SelectionPending);
            var old=rig.Llama.LastLease;rig.Llama.HoldClose=true;
            var change=rig.Dispatcher.SwitchInferenceEngineAsync(rig.Endpoint.EndpointId,InferenceEngine.Strata);actual=change;
            await rig.Llama.CloseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5),CancellationToken.None);
            Assert.False(change.IsCompleted);Assert.Equal(0,rig.Strata.Created);
            Assert.Null(rig.Report.Effective);Assert.True(rig.Report.SelectionPending);
            rig.Llama.CloseRelease.TrySetResult();Assert.True((await change).Succeeded);
            Assert.True(old!.CloseOriginalAsync().IsCompletedSuccessfully);
            Assert.Same(rig.Model,rig.Strata.LastModel);Assert.Equal(InferenceEngine.Strata,rig.Report.Effective);
            Assert.Equal("Manual",rig.Report.SelectionMode);Assert.False(rig.Report.SelectionPending);
            Assert.True((await rig.Dispatcher.SwitchInferenceEngineAsync(rig.Endpoint.EndpointId,InferenceEngine.Automatic)).Succeeded);
            Assert.Equal(InferenceEngine.LlamaCpp,rig.Report.Effective);Assert.Equal("Automatic",rig.Report.SelectionMode);
            Assert.Same(rig.Model,rig.Llama.LastModel);
        } catch(Exception error) { primary=error; }
        finally { rig.Release();await Finish(rig.Dispatcher,[actual],[],primary); }
    }

    [Fact]
    public async Task Incompatible_manual_choice_reports_unmet_requirements_and_explicit_Automatic_recovers_same_model()
    {
        var rig=new Rig("GGUF");Exception? primary=null;
        try {
            Assert.True((await rig.Dispatcher.StartAsync(rig.Endpoint,CancellationToken.None)).Succeeded);
            var refused=await rig.Dispatcher.SwitchInferenceEngineAsync(rig.Endpoint.EndpointId,InferenceEngine.Strata);
            Assert.False(refused.Succeeded);Assert.Equal(DulcheErrorCode.UnsupportedCapability,refused.Error!.Code);
            Assert.Equal(0,rig.Strata.Created);Assert.Equal(1,rig.Llama.Created);
            Assert.Null(rig.Report.Effective);Assert.Equal("Unselected",rig.Report.SelectionMode);
            Assert.Equal(InferenceEngine.Strata,rig.Report.PendingPreference);Assert.Same(rig.Model,rig.Report.Model);
            Assert.NotEmpty(Assert.Single(rig.Report.Compatibility,row=>row.Engine==InferenceEngine.Strata).Unmet);
            Assert.True((await rig.Dispatcher.SwitchInferenceEngineAsync(rig.Endpoint.EndpointId,InferenceEngine.Automatic)).Succeeded);
            Assert.Equal(2,rig.Llama.Created);Assert.Same(rig.Model,rig.Llama.LastModel);
            Assert.Equal(InferenceEngine.LlamaCpp,rig.Report.Effective);Assert.Equal("Automatic",rig.Report.SelectionMode);
        } catch(Exception error) { primary=error; }
        finally { rig.Release();await Finish(rig.Dispatcher,[],[],primary); }
    }

    [Fact]
    public async Task Same_manual_retry_consumes_only_the_actual_failure_permission_for_the_original_model()
    {
        var rig=new Rig();rig.Strata.FailStart=true;Exception? primary=null;
        try {
            Assert.True((await rig.Dispatcher.StartAsync(rig.Endpoint,CancellationToken.None)).Succeeded);
            var failed=await rig.Dispatcher.SwitchInferenceEngineAsync(rig.Endpoint.EndpointId,InferenceEngine.Strata);
            Assert.False(failed.Succeeded);Assert.Equal(1,rig.Llama.Created);
            Assert.False(rig.Dispatcher.PermitManualInitializationFallback(rig.Endpoint.EndpointId,failed.Error! with { }).Succeeded);
            Assert.True(rig.Dispatcher.PermitManualInitializationFallback(rig.Endpoint.EndpointId,failed.Error!).Succeeded);
            Assert.True((await rig.Dispatcher.SwitchInferenceEngineAsync(rig.Endpoint.EndpointId,InferenceEngine.Strata)).Succeeded);
            Assert.Equal(2,rig.Strata.Created);Assert.Equal(2,rig.Llama.Created);
            Assert.Equal(InferenceEngine.Strata,rig.Report.ActiveSelectionPreference);
            Assert.Equal(InferenceEngine.LlamaCpp,rig.Report.Effective);Assert.Equal("Manual",rig.Report.SelectionMode);
            Assert.Same(rig.Model,rig.Llama.LastModel);Assert.Single(rig.Report.InitializationFailures);
        } catch(Exception error) { primary=error; }
        finally { rig.Release();await Finish(rig.Dispatcher,[],[],primary); }
    }

    [Fact]
    public async Task Typed_runtime_settings_awaits_actual_switch_and_preserves_model_session_and_settings()
    {
        var rig=new Rig();var runtime=new DulcheRuntime([rig.Dispatcher]);Task? actual=null,stop=null;Exception? primary=null;
        try {
            var started=await runtime.StartLocalAsync("local-engine",12345,rig.Model);Assert.True(started.Succeeded);
            var endpoint=started.Value!;var settings=new InferenceEngineSettings(runtime,endpoint.EndpointId);
            var session=runtime.CreateSession(endpoint.EndpointId);Assert.True(session.Succeeded);
            var generation=new GenerationSettings(Temperature:0.4,Seed:73);
            Assert.True(runtime.SetFutureSettings(endpoint.EndpointId,generation).Succeeded);
            rig.Llama.HoldClose=true;var change=settings.ApplyAsync(InferenceEngine.Strata);actual=change;
            await rig.Llama.CloseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5),CancellationToken.None);
            Assert.False(change.IsCompleted);Assert.Equal(0,rig.Strata.Created);
            Assert.True(settings.Read().SelectionPending);Assert.Null(settings.Read().Effective);
            rig.Llama.CloseRelease.TrySetResult();Assert.True((await change).Succeeded);
            Assert.Equal(InferenceEngine.Strata,settings.Read().Effective);
            Assert.Equal(rig.Model,runtime.InspectEndpoint(endpoint.EndpointId).Value!.Model);
            Assert.Equal(session.Value!.SessionId,runtime.GetSession(session.Value.SessionId).Value!.SessionId);
            var retainedSettings=(IDictionary<string,GenerationSettings>)typeof(DulcheRuntime)
                .GetField("_endpointSettings",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(runtime)!;
            Assert.Equal(generation,retainedSettings[endpoint.EndpointId]); // Actual existing settings; reflection grants no authority.
            Assert.Empty(runtime.GetQueueSnapshot(endpoint.EndpointId).Value!.Queued);
            Assert.True((await settings.AutomaticAsync()).Succeeded);Assert.Equal("Automatic",settings.Read().SelectionMode);
            stop=runtime.JoinEndpointStopAsync(endpoint.EndpointId);await stop;
        } catch(Exception error) { primary=error; }
        finally { rig.Release();await Finish(rig.Dispatcher,[actual,stop],[],primary); }
    }

    [Fact]
    public async Task Restored_context_engine_callback_cannot_admit_runtime_stop_or_nested_switch()
    {
        var prior=ExecutionContext.Capture()!;var rig=new Rig();var runtime=new DulcheRuntime([rig.Dispatcher]);
        Task? actual=null,stop=null,forbiddenStop=null,forbiddenSwitch=null;Exception? primary=null;Exception? stopRefusal=null,switchRefusal=null;
        try {
            var endpoint=(await runtime.StartLocalAsync("local-engine",12345,rig.Model)).Value!;
            var settings=new InferenceEngineSettings(runtime,endpoint.EndpointId);
            rig.Strata.OnLoad=()=>ExecutionContext.Run(prior,unusedState=> {
                try { forbiddenStop=runtime.JoinEndpointStopAsync(endpoint.EndpointId); } catch(Exception error) { stopRefusal=error; }
                try { forbiddenSwitch=settings.ApplyAsync(InferenceEngine.LlamaCpp); } catch(Exception error) { switchRefusal=error; }
            },null);
            var change=settings.ApplyAsync(InferenceEngine.Strata);actual=change;Assert.True((await change).Succeeded);
            Assert.IsType<InvalidOperationException>(stopRefusal);Assert.IsType<InvalidOperationException>(switchRefusal);
            Assert.Equal(InferenceEngine.Strata,settings.Read().Effective);
            Assert.Equal(EndpointState.Ready,runtime.InspectEndpoint(endpoint.EndpointId).Value!.State);
            Assert.Equal(1,rig.Strata.LoadCalls);Assert.Equal(1,rig.Llama.Created);
            stop=runtime.JoinEndpointStopAsync(endpoint.EndpointId);Assert.True((await (Task<OperationResult<DulcheEndpoint>>)stop).Succeeded);
        } catch(Exception error) { primary=error; }
        finally { rig.Release();await Finish(rig.Dispatcher,[actual,stop,forbiddenStop,forbiddenSwitch],[],primary); }
    }

    [Fact]
    public async Task Runtime_model_replacement_during_held_switch_refuses_success_disclosure_and_retains_actual_cause()
    {
        var rig=new Rig();var runtime=new DulcheRuntime([rig.Dispatcher]);Task? actual=null,stop=null;Exception? primary=null;
        try {
            var endpoint=(await runtime.StartLocalAsync("local-engine",12345,rig.Model)).Value!;
            rig.Llama.HoldClose=true;actual=runtime.setInferenceEngine(InferenceEngine.Strata,endpoint.EndpointId);
            await rig.Llama.CloseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5),CancellationToken.None);
            var replacement=new ModelIdentity(rig.Model.ProviderId,"different-model","different-artifact");
            Assert.True(runtime.SetModelForFutureRequests(endpoint.EndpointId,replacement).Succeeded);
            rig.Llama.CloseRelease.TrySetResult();var expected=await Capture(actual);
            Assert.IsType<InvalidOperationException>(expected);Assert.True(actual.IsFaulted);
            Assert.Equal(replacement,runtime.InspectEndpoint(endpoint.EndpointId).Value!.Model);
            Assert.Equal(rig.Model,rig.Dispatcher.GetInferenceEngine(endpoint.EndpointId).Model);
            Assert.False((await runtime.setInferenceEngine(InferenceEngine.Automatic,endpoint.EndpointId)).Succeeded);
            stop=runtime.JoinEndpointStopAsync(endpoint.EndpointId);var closeCause=await Capture(stop);
            Assert.NotNull(closeCause);Assert.Contains(Graph(closeCause!),cause=>ReferenceEquals(cause,expected));
        } catch(Exception error) { primary=error; }
        finally { rig.Release();await Finish(rig.Dispatcher,[actual,stop],[actual],primary); }
    }

    [Fact]
    public async Task Binding_during_failed_change_waits_same_driver_and_never_binds_previous_or_failed_engine()
    {
        var rig=new Rig();rig.Strata.FailStart=true;Task? change=null,binding=null;Exception? primary=null;
        try {
            Assert.True((await rig.Dispatcher.StartAsync(rig.Endpoint,CancellationToken.None)).Succeeded);
            var old=rig.Llama.LastLease!;rig.Llama.HoldClose=true;
            var actual=rig.Dispatcher.SwitchInferenceEngineAsync(rig.Endpoint.EndpointId,InferenceEngine.Strata);change=actual;
            await rig.Llama.CloseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5),CancellationToken.None);
            var request=new DulcheRequest("same original",SessionId:"same-session",Model:rig.Model);
            var handle=new RuntimeRequestHandle("same-request","same-session",rig.Endpoint.EndpointId,
                _=>Task.FromException<DulcheResult>(new NotSupportedException()),(_,_)=>EmptyEvents());
            // Synthetic negative adapter only: no opaque admission or authority is constructed.
            binding=rig.Dispatcher.BindOriginalRequestAsync(rig.Endpoint,request,handle,null!,Guid.Empty,CancellationToken.None);
            Assert.False(binding.IsCompleted);Assert.Equal(0,old.Actual.BindCalls);
            rig.Llama.CloseRelease.TrySetResult();var failed=await actual;Assert.False(failed.Succeeded);
            var cause=await Capture(binding);var projected=Assert.IsType<InferenceEngineException>(cause);
            Assert.Same(failed.Error,projected.Error);Assert.True(binding.IsFaulted);
            Assert.Equal(0,old.Actual.BindCalls);Assert.Equal(0,rig.Strata.LastLease!.Actual.BindCalls);
            Assert.Null(rig.Report.Effective);Assert.Same(rig.Model,rig.Report.Model);
            var close=rig.Dispatcher.DisposeAsync().AsTask();var closeCause=await Capture(close);
            Assert.NotNull(closeCause);Assert.Contains(Graph(closeCause!),member=>ReferenceEquals(member,projected));
        } catch(Exception error) { primary=error; }
        finally { rig.Release();await Finish(rig.Dispatcher,[change,binding],[binding],primary); }
    }

    private static async Task<Exception?> Capture(Task task)
    { try { await task;return null; } catch(Exception error) { return error; } }
    private static IEnumerable<Exception> Graph(Exception error)
    {
        yield return error;
        if(error is AggregateException group) foreach(var child in group.InnerExceptions) foreach(var item in Graph(child)) yield return item;
        else if(error.InnerException is { } inner) foreach(var item in Graph(inner)) yield return item;
    }
    private static async Task Finish(InferenceEngineDispatcher dispatcher,IEnumerable<Task?> originals,IEnumerable<Task?> expected,Exception? primary)
    {
        var causes=new List<Exception>();var originalsArray=originals.Where(task=>task is not null).Cast<Task>().Distinct().ToArray();
        foreach(var original in originalsArray) { var cause=await Capture(original);if(cause is not null) causes.Add(cause);if(original.Exception is { } group) causes.AddRange(group.InnerExceptions); }
        Task? close=null;
        try { close=dispatcher.DisposeAsync().AsTask(); } catch(Exception cause) { causes.Add(cause); }
        if(close is not null) {
            var closeCause=await Capture(close);if(closeCause is not null) causes.Add(closeCause);
            if(close.Exception is { } closeGroup) causes.AddRange(closeGroup.InnerExceptions);
        }
        var allowed=expected.Where(task=>task?.Exception is not null).SelectMany(task=>Graph(task!.Exception!)).ToHashSet(ReferenceEqualityComparer.Instance);
        bool Known(Exception cause)=>allowed.Contains(cause)||cause is AggregateException group&&group.InnerExceptions.All(Known);
        var unexpected=causes.Where(cause=>!Known(cause)).Distinct<Exception>(ReferenceEqualityComparer.Instance).ToList();
        if(primary is not null) unexpected.Insert(0,primary);
        if(unexpected.Count==1) ExceptionDispatchInfo.Capture(unexpected[0]).Throw();
        if(unexpected.Count>1) throw new AggregateException("Actual active-engine control and independent cleanup failed.",unexpected);
    }
    private static async IAsyncEnumerable<RuntimeEvent> EmptyEvents([EnumeratorCancellation] CancellationToken token=default)
    { token.ThrowIfCancellationRequested();await Task.CompletedTask;yield break; }

    private sealed class Rig
    {
        public ModelIdentity Model {get;}=new("local-engine","same-model","same-artifact");
        public DulcheEndpoint Endpoint {get;}
        public Factory Llama {get;}=new();public Factory Strata {get;}=new();
        public InferenceEngineDispatcher Dispatcher {get;}
        public InferenceEngineDiagnostic Report=>Dispatcher.GetInferenceEngine(Endpoint.EndpointId);
        public Rig(string format="Safetensors") {
            Endpoint=new("same-endpoint","local-engine","http://127.0.0.1:12345",12345,EndpointState.Starting,Model,new HashSet<string>{"chat","streaming"},false,DateTimeOffset.UtcNow);
            var requirements=new InferenceModelRequirements(Model,"same-artifact","fixture-architecture","fixture-family",format,"Q4",new HashSet<string>{"streaming"},1,1,2048,NativeRegistration:"fixture-native");
            var hardware=new InferenceHardware("fixture-hardware","Linux","x64",8192,[new("fixture-gpu",8,6,4096)],new HashSet<string>());
            InferenceEngineSupport Support(InferenceEngine engine)=>new(engine,"synthetic-build",true,null,new HashSet<string>{"fixture-architecture"},new HashSet<string>{"fixture-family"},
                engine==InferenceEngine.Strata?new HashSet<string>{"Safetensors"}:new HashSet<string>{"Safetensors","GGUF"},new HashSet<string>{"Q4"},new HashSet<string>{"streaming"},
                new HashSet<string>{"Linux"},new HashSet<string>{"x64"},new HashSet<string>(),NativeRegistrations:new HashSet<string>{"fixture-native"});
            Dispatcher=new("local-engine",new Uri(Endpoint.Target),[new(InferenceEngine.LlamaCpp,Llama),new(InferenceEngine.Strata,Strata)],new Observations(new(requirements,hardware,[Support(InferenceEngine.LlamaCpp),Support(InferenceEngine.Strata)])));
        }
        public void Release() { Llama.CloseRelease.TrySetResult();Strata.CloseRelease.TrySetResult(); }
    }
    private sealed class Observations(InferenceRuntimeObservation value):IInferenceRuntimeObservationSource
    { public Task<InferenceRuntimeObservation> ObserveOriginalAsync(ModelIdentity model,IInferenceEngineOriginalSourceScope scope,CancellationToken token)=>Task.FromResult(value); }
    private sealed class Factory:IInferenceEngineAdapterFactory
    {
        public int Created,LoadCalls;public ModelIdentity? LastModel;public Lease? LastLease;public bool HoldClose,FailStart;public Action? OnLoad;
        public TaskCompletionSource CloseEntered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CloseRelease {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<OriginalInferenceEngineLease> CreateOriginalAsync(ModelIdentity model,IInferenceEngineOriginalSourceScope scope,CancellationToken token)
        { Created++;LastModel=model;LastLease=new(this);return Task.FromResult<OriginalInferenceEngineLease>(LastLease); }
    }
    private sealed class Lease(Factory owner):OriginalInferenceEngineLease
    {
        private Task? _close;public Adapter Actual {get;}=new(owner);public override IDulcheOriginalProviderAdapter Adapter=>Actual;
        public override void DemandExternalOriginalJoin() { }
        public override Task CloseOriginalAsync()=>_close??=Close();
        private async Task Close() { owner.CloseEntered.TrySetResult();if(owner.HoldClose) await owner.CloseRelease.Task; }
    }
    private sealed class Adapter(Factory owner):IDulcheOriginalProviderAdapter
    {
        public string ProviderId=>"local-engine";public string RuntimeVersion=>"synthetic-control";public bool IsLocal=>true;
        public Uri OriginalConfiguredTarget=>new("http://127.0.0.1:12345");public IReadOnlySet<string> Capabilities {get;}=new HashSet<string>{"chat","streaming"};public int BindCalls;
        public Task BindOriginalRequestAsync(DulcheEndpoint endpoint,DulcheRequest request,RuntimeRequestHandle handle,TaskRunAttemptAdmission admission,Guid action,CancellationToken token)
        { BindCalls++;return Task.CompletedTask; }
        public void CaptureOriginalDispatchRequest(RuntimeRequestHandle handle,DulcheRequest frozen,DulcheRequest dispatch) { }
        public ValueTask<OperationResult<Unit>> StartAsync(DulcheEndpoint endpoint,CancellationToken token)=>ValueTask.FromResult(owner.FailStart
            ?OperationResult<Unit>.Failure(new(DulcheErrorCode.ModelLoadFailed,"actual controlled start failed","local-engine",true)):OperationResult<Unit>.Success(Unit.Value));
        public ValueTask<OperationResult<Unit>> LoadModelAsync(DulcheEndpoint endpoint,ModelIdentity model,CancellationToken token)
        { owner.LoadCalls++;owner.OnLoad?.Invoke();return ValueTask.FromResult(OperationResult<Unit>.Success(Unit.Value)); }
        public async IAsyncEnumerable<AdapterDelta> GenerateAsync(DulcheEndpoint endpoint,DulcheRequest request,string id,[EnumeratorCancellation] CancellationToken token)
        { token.ThrowIfCancellationRequested();await Task.CompletedTask;yield break; }
        public IAsyncEnumerable<AdapterDelta> ContinueWithToolResultAsync(DulcheEndpoint endpoint,DulcheRequest request,string id,ToolInvocationResult result,CancellationToken token)=>GenerateAsync(endpoint,request,id,token);
        public ValueTask<OperationResult<Unit>> CancelAsync(DulcheEndpoint endpoint,string id,CancellationToken token)=>ValueTask.FromResult(OperationResult<Unit>.Success(Unit.Value));
        public ValueTask<OperationResult<Unit>> PauseAsync(DulcheEndpoint endpoint,string id,CancellationToken token)=>CancelAsync(endpoint,id,token);
        public ValueTask<OperationResult<Unit>> ResumeAsync(DulcheEndpoint endpoint,string id,CancellationToken token)=>CancelAsync(endpoint,id,token);
        public ValueTask<OperationResult<Unit>> StopAsync(DulcheEndpoint endpoint,CancellationToken token)=>ValueTask.FromResult(OperationResult<Unit>.Success(Unit.Value));
        public ValueTask<RuntimeHealth> HealthAsync(DulcheEndpoint endpoint,CancellationToken token)=>ValueTask.FromResult(new RuntimeHealth(endpoint.EndpointId,EndpointState.Ready,DateTimeOffset.UtcNow,
            "Synthetic initialized engine only",Metric<double>.Na,Metric<double>.Na,Metric<double>.Na));
    }
}
