using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Dulche.Runtime.Tests;

/// <summary>Actual dispatcher seal + factory/raw close tasks, using the maintained private coordinator
/// harness. Controlled lease objects exercise only cleanup custody; no installed artifact, model-use,
/// hardware/capability authority or native process is issued or executed.</summary>
public sealed partial class ManagedProviderDulcheAdapterOriginalTests
{
    [Fact]
    public Task Late_worker_lease_after_real_dispatcher_seal_is_closed_before_original_failure()
        => Control(h => ObserveLateStrataLease(h,holdModel:false));
    [Fact]
    public Task Late_model_lease_after_real_dispatcher_seal_is_closed_before_original_failure()
        => Control(h => ObserveLateStrataLease(h,holdModel:true));

    private static async Task ObserveLateStrataLease(Harness h,bool holdModel)
    {
        var model=new ModelIdentity(h.Provider.Id,"model");
        var sources=new ControlledStrataCustodySources(model,h.Admission,holdModel);
        var factory=new StrataManagedInferenceEngineFactory(h.Provider.Id,h.Adapter.OriginalConfiguredTarget,
            h.Admission,sources,sources,new SyntheticConfigurations(h.Provider),h.Coordinator,h.ObservedFrames,
            h.Tools,h.Context,h.Context);
        var dispatcher=new InferenceEngineDispatcher(h.Provider.Id,h.Adapter.OriginalConfiguredTarget,
            [new(InferenceEngine.Strata,factory)],new ControlledStrataCustodyObservation());
        var endpoint=h.Endpoint with {EndpointId=Guid.NewGuid().ToString("N"),Model=model};
        Task<OperationResult<Unit>>? initialize=null;Task? close=null;var errors=new List<Exception>();
        try
        {
            initialize=dispatcher.StartAsync(endpoint,CancellationToken.None).AsTask();
            await sources.AcquisitionEntered.Task.WaitAsync(TimeSpan.FromSeconds(5),CancellationToken.None);
            Assert.False(initialize.IsCompleted);
            close=dispatcher.DisposeAsync().AsTask(); // The REAL dispatcher seals the supplied source scope.
            Assert.False(close.IsCompleted);
            sources.ReleaseAcquisition();
            await sources.HeldCleanupEntered.WaitAsync(TimeSpan.FromSeconds(5),CancellationToken.None);
            Assert.False(initialize.IsCompleted);Assert.False(close.IsCompleted);
            Assert.Equal(0,sources.Model.RequirementReads);
            Assert.Equal(0,sources.Model.CurrentChecks);
            Assert.Equal(0,sources.Binary.WorkerReads);
            if(!holdModel)Assert.Equal(0,sources.Binary.CurrentChecks);
            sources.ReleaseCleanup();
            var failed=await Record.ExceptionAsync(()=>initialize);
            Assert.NotNull(failed);Assert.True(initialize.IsFaulted);Assert.False(initialize.IsCanceled);
            var distinctCauses=Leaves(failed!).Distinct<Exception>(ReferenceEqualityComparer.Instance);
            var refusal=Assert.Single(distinctCauses,cause=>cause is InvalidOperationException);
            Assert.All(Leaves(failed!),cause=>Assert.Same(refusal,cause));
            h.Expect(refusal);
            var closeFailure=await Record.ExceptionAsync(()=>close);
            Assert.NotNull(closeFailure);Assert.True(close.IsFaulted);
            Assert.All(Leaves(closeFailure!),cause=>Assert.Same(refusal,cause));
            Assert.Equal(1,sources.Binary.Closes);
            Assert.Equal(holdModel?1:0,sources.Model.Closes);
            Assert.True(sources.Binary.OriginalClose.IsCompletedSuccessfully);
            if(holdModel)Assert.True(sources.Model.OriginalClose.IsCompletedSuccessfully);
        }
        catch(Exception cause){errors.Add(cause);}
        finally
        {
            sources.ReleaseAcquisition();sources.ReleaseCleanup();
            try {close??=dispatcher.DisposeAsync().AsTask();}catch(Exception cause){AddUnexpected(h,errors,cause);}
            if(initialize is not null)await JoinUnexpected(h,errors,initialize);
            if(close is not null)await JoinUnexpected(h,errors,close);
        }
        if(errors.Count==1)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if(errors.Count>1)throw new AggregateException(errors);
    }
    private sealed class ControlledStrataCustodySources : IOriginalStrataWorkerSource,IOriginalStrataModelSource
    {
        private readonly ModelIdentity _model;
        private readonly TaskRunAttemptAdmission _admission;
        private readonly bool _holdModel;
        private readonly TaskCompletionSource<StrataOriginalWorkerLease> _binary=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<StrataOriginalModelLease> _modelAcquisition=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource AcquisitionEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ControlledStrataBinaryLease Binary {get;}
        public ControlledStrataModelLease Model {get;}
        public Task HeldCleanupEntered=>_holdModel?Model.CleanupEntered.Task:Binary.CleanupEntered.Task;
        public ControlledStrataCustodySources(ModelIdentity model,TaskRunAttemptAdmission admission,bool holdModel)
        {_model=model;_admission=admission;_holdModel=holdModel;Binary=new(!holdModel);Model=new(holdModel);}
        public Task<StrataOriginalWorkerLease> AcquireOriginalAsync(TaskRunAttemptAdmission admission,
            IInferenceEngineOriginalSourceScope scope,CancellationToken token)
        {
            Assert.Same(_admission,admission);
            if(_holdModel)return Task.FromResult<StrataOriginalWorkerLease>(Binary);
            AcquisitionEntered.TrySetResult();return _binary.Task;
        }
        public Task<StrataOriginalModelLease> AcquireOriginalAsync(ModelIdentity model,TaskRunAttemptAdmission admission,
            IInferenceEngineOriginalSourceScope scope,CancellationToken token)
        {
            Assert.Same(_admission,admission);Assert.Same(_model,model);
            Assert.True(_holdModel,"The sealed worker-only control must never acquire a model.");
            AcquisitionEntered.TrySetResult();return _modelAcquisition.Task;
        }
        public bool IsIssuedOriginalWorkerLease(StrataOriginalWorkerLease lease,TaskRunAttemptAdmission admission)
            =>ReferenceEquals(lease,Binary)&&ReferenceEquals(admission,_admission);
        public bool IsIssuedOriginalModelLease(StrataOriginalModelLease lease,ModelIdentity model,TaskRunAttemptAdmission admission)
            =>ReferenceEquals(lease,Model)&&ReferenceEquals(model,_model)&&ReferenceEquals(admission,_admission);
        public void ReleaseAcquisition()
        {_binary.TrySetResult(Binary);_modelAcquisition.TrySetResult(Model);}
        public void ReleaseCleanup(){Binary.Release.TrySetResult();Model.Release.TrySetResult();}
    }
    private sealed class ControlledStrataBinaryLease(bool held):StrataOriginalWorkerLease
    {
        public int CurrentChecks,WorkerReads,Closes;
        public readonly TaskCompletionSource CleanupEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task OriginalClose=>held?Release.Task:Task.CompletedTask;
        public override StrataBundledWorker OriginalWorker {get {WorkerReads++;throw new InvalidOperationException("No native worker may be read or launched.");}}
        public override void DemandCurrentOriginalBinding(){CurrentChecks++;}
        public override ValueTask DisposeAsync(){Closes++;CleanupEntered.TrySetResult();return new(OriginalClose);}
    }
    private sealed class ControlledStrataModelLease(bool held):StrataOriginalModelLease
    {
        public int RequirementReads,CurrentChecks,Closes;
        public readonly TaskCompletionSource CleanupEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task OriginalClose=>held?Release.Task:Task.CompletedTask;
        public override InferenceModelRequirements Requirements {get {RequirementReads++;throw new InvalidOperationException("No artifact requirements may be read after seal.");}}
        public override string OriginalCheckpointDirectory=>throw new InvalidOperationException("No model path may be read.");
        public override IReadOnlyList<int> ActualCudaDeviceIndices=>throw new InvalidOperationException("No hardware may be read.");
        public override void DemandCurrentOriginalBinding(){CurrentChecks++;}
        public override ValueTask DisposeAsync(){Closes++;CleanupEntered.TrySetResult();return new(OriginalClose);}
    }
    private sealed class ControlledStrataCustodyObservation:IInferenceRuntimeObservationSource
    {
        public Task<InferenceRuntimeObservation> ObserveOriginalAsync(ModelIdentity model,
            IInferenceEngineOriginalSourceScope scope,CancellationToken token)
            =>scope.InvokeOriginalFactory(()=> {
                // Deliberately controlled compatibility metadata, never actual device/artifact evidence.
                var requirements=new InferenceModelRequirements(model,"control-no-artifact","control","control",
                    "Safetensors","control",new HashSet<string>(),0,0,1,[InferenceEngine.Strata],"control-registration");
                var hardware=new InferenceHardware("control-no-hardware","Linux","x64",0,[],new HashSet<string>());
                var support=new InferenceEngineSupport(InferenceEngine.Strata,"control-no-runtime",true,null,
                    new HashSet<string>{"control"},new HashSet<string>{"control"},new HashSet<string>{"Safetensors"},
                    new HashSet<string>{"control"},new HashSet<string>(),new HashSet<string>{"Linux"},
                    new HashSet<string>{"x64"},new HashSet<string>(),NativeRegistrations:new HashSet<string>{"control-registration"});
                var actual=Task.FromResult(new InferenceRuntimeObservation(requirements,hardware,[support]));
                scope.RetainOriginalTask(actual);return actual;
            });
    }
}
