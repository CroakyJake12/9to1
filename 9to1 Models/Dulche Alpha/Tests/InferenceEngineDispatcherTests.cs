using System.Runtime.CompilerServices;
using Dulche.Runtime;
using Haven.Application;
using Xunit;
using static Dulche.Runtime.InferenceEngines;

namespace Dulche.Runtime.Tests;

/// <summary>Managed selection/custody controls with explicitly synthetic engine/manifest observations.
/// They neither mint a TaskRun admission nor execute a GPU, installed model or native worker.</summary>
public sealed class InferenceEngineDispatcherTests
{
    [Fact]
    public async Task Manual_incompatible_Strata_refuses_without_calling_or_substituting_an_engine()
    {
        var rig=new Rig("GGUF"); await using var dispatcher=rig.Dispatcher;
        Assert.True(dispatcher.SetInferenceEngine(InferenceEngine.Strata).Succeeded);
        var result=await dispatcher.StartAsync(rig.Endpoint,CancellationToken.None);
        Assert.False(result.Succeeded); Assert.Equal(DulcheErrorCode.UnsupportedCapability,result.Error!.Code);
        Assert.Equal(0,rig.Llama.Created); Assert.Equal(0,rig.Strata.Created);
        var report=dispatcher.GetInferenceEngine(rig.Endpoint.EndpointId);
        Assert.Equal(InferenceEngine.Strata,report.Requested); Assert.Null(report.Effective);
        Assert.Contains(report.Compatibility.Single(value=>value.Engine==InferenceEngine.Strata).Unmet,
            gap=>gap.Requirement=="model.weight-format"||gap.Requirement=="strata.weight-format");
    }

    [Fact]
    public async Task Automatic_declared_fit_preserves_the_same_model_and_defaults_to_llama_without_calibration_claim()
    {
        var rig=new Rig(); await using var dispatcher=rig.Dispatcher;
        Assert.True((await dispatcher.StartAsync(rig.Endpoint,CancellationToken.None)).Succeeded);
        Assert.Same(rig.Model,rig.Llama.LastModel); Assert.Equal(1,rig.Llama.Created); Assert.Equal(0,rig.Strata.Created);
        var report=dispatcher.GetInferenceEngine(rig.Endpoint.EndpointId);
        Assert.Equal(InferenceEngine.LlamaCpp,report.Effective); Assert.Contains("No local timing measurement",report.Reason);
        Assert.Equal(rig.Model,report.Model); Assert.Empty(report.InitializationFailures);
    }

    [Fact]
    public async Task Automatic_failed_initialization_joins_actual_prior_close_before_same_model_fallback()
    {
        var rig=new Rig(); rig.Llama.FailStart=true; rig.Llama.HoldClose=true;
        var dispatcher=rig.Dispatcher; var actual=dispatcher.StartAsync(rig.Endpoint,CancellationToken.None).AsTask();
        try
        {
            await rig.Llama.CloseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5),CancellationToken.None);
            Assert.False(actual.IsCompleted); Assert.Equal(0,rig.Strata.Created);
            rig.Llama.CloseRelease.TrySetResult(); Assert.True((await actual).Succeeded);
            Assert.Same(rig.Model,rig.Strata.LastModel);
            Assert.Equal(InferenceEngine.Strata,dispatcher.GetInferenceEngine(rig.Endpoint.EndpointId).Effective);
            Assert.Single(dispatcher.GetInferenceEngine(rig.Endpoint.EndpointId).InitializationFailures);
        }
        finally { rig.Llama.CloseRelease.TrySetResult(); try { await actual; } finally { await dispatcher.DisposeAsync(); } }
    }

    [Fact]
    public async Task Manual_failed_initialization_needs_explicit_permission_for_that_observed_failure()
    {
        var rig=new Rig(); rig.Strata.FailStart=true; await using var dispatcher=rig.Dispatcher;
        Assert.True(dispatcher.SetInferenceEngine(InferenceEngine.Strata).Succeeded);
        var failed=await dispatcher.StartAsync(rig.Endpoint,CancellationToken.None);
        Assert.False(failed.Succeeded); Assert.Equal(0,rig.Llama.Created);
        Assert.False(dispatcher.PermitManualInitializationFallback(rig.Endpoint.EndpointId,failed.Error! with { }).Succeeded);
        Assert.True(dispatcher.PermitManualInitializationFallback(rig.Endpoint.EndpointId,failed.Error!).Succeeded);
        Assert.True((await dispatcher.LoadModelAsync(rig.Endpoint,rig.Model,CancellationToken.None)).Succeeded);
        Assert.Equal(InferenceEngine.LlamaCpp,dispatcher.GetInferenceEngine(rig.Endpoint.EndpointId).Effective);
        Assert.Equal(InferenceEngine.Strata,dispatcher.GetInferenceEngine(rig.Endpoint.EndpointId).Requested);
    }

    [Fact]
    public async Task Engine_switch_joins_the_actual_old_lease_before_loading_the_same_model_in_a_fresh_engine()
    {
        var rig=new Rig(); var dispatcher=rig.Dispatcher;
        Assert.True((await dispatcher.StartAsync(rig.Endpoint,CancellationToken.None)).Succeeded);
        var original=rig.Llama.LastLease;
        rig.Llama.HoldClose=true;
        Assert.True(dispatcher.SetInferenceEngine(InferenceEngine.Strata).Succeeded);
        var actual=dispatcher.LoadModelAsync(rig.Endpoint,rig.Model,CancellationToken.None).AsTask();
        try
        {
            await rig.Llama.CloseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5),CancellationToken.None);
            Assert.False(actual.IsCompleted); Assert.Equal(0,rig.Strata.Created);
            rig.Llama.CloseRelease.TrySetResult(); Assert.True((await actual).Succeeded);
            Assert.Same(rig.Model,rig.Strata.LastModel); Assert.Same(original,rig.Llama.LastLease);
            Assert.Equal(InferenceEngine.Strata,dispatcher.GetInferenceEngine(rig.Endpoint.EndpointId).Effective);
        }
        finally { rig.Llama.CloseRelease.TrySetResult(); try { await actual; } finally { await dispatcher.DisposeAsync(); } }
    }

    [Fact]
    public async Task Public_runtime_three_calls_reuse_the_existing_adapter_registration_and_manual_scope()
    {
        var rig=new Rig(); await using var dispatcher=rig.Dispatcher;
        var Dulche=new DulcheRuntime([dispatcher]);
        Assert.True(Dulche.setInferenceEngine(Llama.cpp).Succeeded);
        Assert.Equal(InferenceEngine.LlamaCpp,Dulche.getInferenceEngine().Requested);
        Assert.True(Dulche.setInferenceEngine(Strata).Succeeded);
        Assert.Equal(InferenceEngine.Strata,Dulche.getInferenceEngine().Requested);
        Assert.True(Dulche.setInferenceEngine(Automatic).Succeeded);
        Assert.Equal(InferenceEngine.Automatic,Dulche.getInferenceEngine().Requested);
    }

    [Fact]
    public async Task Exact_finite_binding_task_handle_request_and_cancel_are_forwarded_to_same_selected_adapter()
    {
        var rig=new Rig(); await using var dispatcher=rig.Dispatcher;
        Assert.True((await dispatcher.StartAsync(rig.Endpoint,CancellationToken.None)).Succeeded);
        var adapter=rig.Llama.LastLease!.Actual;
        var request=new DulcheRequest("original",SessionId:"same-session",Model:rig.Model);
        var handle=new RuntimeRequestHandle("same-request","same-session",rig.Endpoint.EndpointId,
            _=>Task.FromException<DulcheResult>(new NotSupportedException()),(_,_)=>EmptyEvents());
        // Null is passed only through the deliberately synthetic adapter: no opaque admission is constructed.
        var original=dispatcher.BindOriginalRequestAsync(rig.Endpoint,request,handle,null!,Guid.Empty,CancellationToken.None);
        Assert.Same(adapter.Binding,original); await original;
        Assert.Same(request,adapter.LastRequest); Assert.Same(handle,adapter.LastHandle);
        var cancel=dispatcher.CancelAsync(rig.Endpoint,handle.RequestId,CancellationToken.None).AsTask();
        Assert.Same(adapter.Cancel,cancel); await cancel;
        Assert.False(dispatcher.SetInferenceEngine(InferenceEngine.Strata).Succeeded); // Bound request still owns its engine.
    }

    [Theory]
    [InlineData("architecture")]
    [InlineData("quantization")]
    [InlineData("feature")]
    [InlineData("memory")]
    public void Compatibility_uses_independent_hard_requirements_not_dense_or_MoE_shortcuts(string missing)
    {
        var rig=new Rig(); var model=rig.Observation.Requirements;
        model=missing switch {
            "architecture"=>model with { Architecture="unregistered-moe" },
            "quantization"=>model with { Quantization="unsupported" },
            "feature"=>model with { RequiredFeatures=new HashSet<string>{"actual-images"} },
            _=>model with { MinimumRamBytes=long.MaxValue }
        };
        var report=InferenceCompatibilityRegistry.Inspect(model,rig.Observation.Hardware,rig.Observation.Engines[0]);
        Assert.False(report.Compatible); Assert.NotEmpty(report.Unmet);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stop_during_actual_Start_or_Load_joins_the_same_original_and_refuses_later_load_or_selected_disclosure(bool holdLoad)
    {
        var rig=new Rig(); rig.Llama.HoldStart=!holdLoad; rig.Llama.HoldLoad=holdLoad;
        var dispatcher=rig.Dispatcher;
        var actual=dispatcher.StartAsync(rig.Endpoint,CancellationToken.None).AsTask();
        Task<OperationResult<Unit>>? stop=null;
        try
        {
            await (holdLoad ? rig.Llama.LoadEntered.Task : rig.Llama.StartEntered.Task).WaitAsync(TimeSpan.FromSeconds(5),CancellationToken.None);
            stop=dispatcher.StopAsync(rig.Endpoint,CancellationToken.None).AsTask();
            Assert.False(stop.IsCompleted); Assert.False(actual.IsCompleted);
            if(holdLoad) rig.Llama.LoadRelease.TrySetResult(); else rig.Llama.StartRelease.TrySetResult();
            var actualFailure=await CaptureFailure(actual);
            Assert.NotNull(actualFailure); Assert.True(actual.IsFaulted);
            Assert.Equal(holdLoad ? 1 : 0,rig.Llama.LoadCalls);
            Assert.Null(dispatcher.GetInferenceEngine(rig.Endpoint.EndpointId).Effective);
            var stopFailure=await CaptureFailure(stop);
            Assert.NotNull(stopFailure); Assert.True(stop.IsFaulted);
            Assert.True(rig.Llama.LastLease!.CloseOriginalAsync().IsCompletedSuccessfully);
            Assert.Equal(0,rig.Strata.Created);
        }
        finally
        {
            rig.Llama.StartRelease.TrySetResult(); rig.Llama.LoadRelease.TrySetResult();
            await CaptureFailure(actual);
            if(stop is not null) await CaptureFailure(stop);
            await CaptureFailure(dispatcher.DisposeAsync().AsTask());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_failure_permission_cannot_substitute_a_different_model_or_changed_preference(bool changePreference)
    {
        var rig=new Rig(); rig.Strata.FailStart=true; var dispatcher=rig.Dispatcher;
        try
        {
            Assert.True(dispatcher.SetInferenceEngine(InferenceEngine.Strata).Succeeded);
            var failed=await dispatcher.StartAsync(rig.Endpoint,CancellationToken.None);
            Assert.False(failed.Succeeded);
            Assert.True(dispatcher.PermitManualInitializationFallback(rig.Endpoint.EndpointId,failed.Error!).Succeeded);
            var model=changePreference ? rig.Model : new ModelIdentity("local-engine","other-model","other-artifact");
            if(changePreference)
            {
                Assert.True(dispatcher.SetInferenceEngine(InferenceEngine.LlamaCpp).Succeeded);
                Assert.True(dispatcher.SetInferenceEngine(InferenceEngine.Strata).Succeeded);
            }
            var result=await dispatcher.LoadModelAsync(rig.Endpoint,model,CancellationToken.None);
            Assert.False(result.Succeeded); Assert.Equal(0,rig.Llama.Created);
            Assert.Equal(2,rig.Strata.Created); Assert.Same(model,rig.Strata.LastModel);
            Assert.Null(dispatcher.GetInferenceEngine(rig.Endpoint.EndpointId).Effective);
            Assert.Equal(model,dispatcher.GetInferenceEngine(rig.Endpoint.EndpointId).Model);
        }
        finally { await dispatcher.DisposeAsync(); }
    }

    private static async Task<Exception?> CaptureFailure(Task actual)
    { try { await actual; return null; } catch(Exception error) { return error; } }

    private static async IAsyncEnumerable<RuntimeEvent> EmptyEvents([EnumeratorCancellation] CancellationToken cancellationToken=default)
    { cancellationToken.ThrowIfCancellationRequested(); await Task.CompletedTask; yield break; }
    private sealed class Rig
    {
        public ModelIdentity Model { get; }=new("local-engine","same-model","same-artifact");
        public DulcheEndpoint Endpoint { get; }
        public Factory Llama { get; }=new(); public Factory Strata { get; }=new();
        public InferenceRuntimeObservation Observation { get; }
        public InferenceEngineDispatcher Dispatcher { get; }
        public Rig(string format="Safetensors")
        {
            Endpoint=new("same-endpoint","local-engine","http://127.0.0.1:12345",12345,EndpointState.Starting,Model,
                new HashSet<string>{"chat","streaming"},false,DateTimeOffset.UtcNow);
            var requirements=new InferenceModelRequirements(Model,"same-artifact","fixture-architecture","fixture-family",format,"Q4",
                new HashSet<string>{"streaming"},1,1,2048,NativeRegistration:"fixture-native");
            var hardware=new InferenceHardware("fixture-hardware","Linux","x64",8192,[new("fixture-gpu",8,6,4096)],new HashSet<string>());
            InferenceEngineSupport Support(InferenceEngine engine)=>new(engine,"synthetic-build",true,null,
                new HashSet<string>{"fixture-architecture"},new HashSet<string>{"fixture-family"},
                engine==InferenceEngine.Strata?new HashSet<string>{"Safetensors"}:new HashSet<string>{"Safetensors","GGUF"},
                new HashSet<string>{"Q4"},new HashSet<string>{"streaming"},new HashSet<string>{"Linux"},new HashSet<string>{"x64"},new HashSet<string>(),
                NativeRegistrations:new HashSet<string>{"fixture-native"});
            Observation=new(requirements,hardware,[Support(InferenceEngine.LlamaCpp),Support(InferenceEngine.Strata)]);
            Dispatcher=new("local-engine",new Uri(Endpoint.Target),[new(InferenceEngine.LlamaCpp,Llama),new(InferenceEngine.Strata,Strata)],new Observations(Observation));
        }
    }
    private sealed class Observations(InferenceRuntimeObservation value):IInferenceRuntimeObservationSource
    { public Task<InferenceRuntimeObservation> ObserveOriginalAsync(ModelIdentity model,IInferenceEngineOriginalSourceScope originalScope,CancellationToken token)=>Task.FromResult(model==value.Requirements.Model ? value : value with { Requirements=value.Requirements with { Model=model,ArtifactFingerprint=model.ArtifactRevision??model.StableKey } }); }
    private sealed class Factory:IInferenceEngineAdapterFactory
    {
        public int Created; public ModelIdentity? LastModel; public Lease? LastLease; public bool FailStart,HoldClose,HoldStart,HoldLoad; public int LoadCalls;
        public TaskCompletionSource StartEntered { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StartRelease { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource LoadEntered { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource LoadRelease { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CloseEntered { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CloseRelease { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<OriginalInferenceEngineLease> CreateOriginalAsync(ModelIdentity model,IInferenceEngineOriginalSourceScope originalScope,CancellationToken token)
        { Created++; LastModel=model; LastLease=new(this); return Task.FromResult<OriginalInferenceEngineLease>(LastLease); }
    }
    private sealed class Lease(Factory owner):OriginalInferenceEngineLease
    {
        private Task? _close; public FakeAdapter Actual { get; }=new(owner);
        public override IDulcheOriginalProviderAdapter Adapter=>Actual;
        public override void DemandExternalOriginalJoin() { }
        public override Task CloseOriginalAsync()=>_close??=Close();
        private async Task Close() { owner.CloseEntered.TrySetResult(); if(owner.HoldClose) await owner.CloseRelease.Task; }
    }
    private sealed class FakeAdapter(Factory owner):IDulcheOriginalProviderAdapter
    {
        public string ProviderId=>"local-engine"; public string RuntimeVersion=>"synthetic-control"; public bool IsLocal=>true;
        public Uri OriginalConfiguredTarget=>new("http://127.0.0.1:12345"); public IReadOnlySet<string> Capabilities { get; }=new HashSet<string>{"chat","streaming"};
        public Task Binding { get; }=Task.FromResult(1);
        public Task<OperationResult<Unit>> Cancel { get; }=Task.FromResult(OperationResult<Unit>.Success(Unit.Value));
        public DulcheRequest? LastRequest; public RuntimeRequestHandle? LastHandle;
        public Task BindOriginalRequestAsync(DulcheEndpoint endpoint,DulcheRequest request,RuntimeRequestHandle handle,TaskRunAttemptAdmission admission,Guid action,CancellationToken token)
        { LastRequest=request; LastHandle=handle; return Binding; }
        public void CaptureOriginalDispatchRequest(RuntimeRequestHandle handle,DulcheRequest frozen,DulcheRequest dispatch) { }
        public ValueTask<OperationResult<Unit>> StartAsync(DulcheEndpoint endpoint,CancellationToken token)=>new(StartOriginal());
        private async Task<OperationResult<Unit>> StartOriginal()
        {
            owner.StartEntered.TrySetResult(); if(owner.HoldStart) await owner.StartRelease.Task;
            return owner.FailStart ? OperationResult<Unit>.Failure(new(DulcheErrorCode.ModelLoadFailed,"actual controlled initialization failed","local-engine",true))
                :OperationResult<Unit>.Success(Unit.Value);
        }
        public ValueTask<OperationResult<Unit>> LoadModelAsync(DulcheEndpoint endpoint,ModelIdentity model,CancellationToken token)=>new(LoadOriginal());
        private async Task<OperationResult<Unit>> LoadOriginal()
        {
            owner.LoadCalls++; owner.LoadEntered.TrySetResult(); if(owner.HoldLoad) await owner.LoadRelease.Task;
            return OperationResult<Unit>.Success(Unit.Value);
        }
        public async IAsyncEnumerable<AdapterDelta> GenerateAsync(DulcheEndpoint endpoint,DulcheRequest request,string id,[EnumeratorCancellation] CancellationToken token)
        { token.ThrowIfCancellationRequested(); await Task.CompletedTask; yield return new(Text:"synthetic delta"); }
        public IAsyncEnumerable<AdapterDelta> ContinueWithToolResultAsync(DulcheEndpoint endpoint,DulcheRequest request,string id,ToolInvocationResult result,CancellationToken token)=>GenerateAsync(endpoint,request,id,token);
        public ValueTask<OperationResult<Unit>> CancelAsync(DulcheEndpoint endpoint,string id,CancellationToken token)=>new(Cancel);
        public ValueTask<OperationResult<Unit>> PauseAsync(DulcheEndpoint endpoint,string id,CancellationToken token)=>ValueTask.FromResult(OperationResult<Unit>.Failure(new(DulcheErrorCode.UnsupportedCapability,"no exact pause","local-engine",false)));
        public ValueTask<OperationResult<Unit>> ResumeAsync(DulcheEndpoint endpoint,string id,CancellationToken token)=>PauseAsync(endpoint,id,token);
        public ValueTask<OperationResult<Unit>> StopAsync(DulcheEndpoint endpoint,CancellationToken token)=>ValueTask.FromResult(OperationResult<Unit>.Success(Unit.Value));
        public ValueTask<RuntimeHealth> HealthAsync(DulcheEndpoint endpoint,CancellationToken token)=>ValueTask.FromException<RuntimeHealth>(new NotSupportedException("No actual native health in this control."));
    }
}
