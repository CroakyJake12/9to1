using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;

namespace Dulche.Runtime;

/// <summary>Borrowed trusted artifact producer. A path or requirements DTO is never an issued lease.
/// Acquisition must retain/drain undisclosed or rejected products and use the finite callback scope.
/// IsIssued is permanent private historical custody only; it grants no current use.</summary>
public interface IOriginalStrataModelSource
{
    Task<StrataOriginalModelLease> AcquireOriginalAsync(ModelIdentity sameModel, TaskRunAttemptAdmission sameModelUseAdmission,
        IInferenceEngineOriginalSourceScope originalScope, CancellationToken cancellationToken);
    bool IsIssuedOriginalModelLease(StrataOriginalModelLease sameLease, ModelIdentity sameModel, TaskRunAttemptAdmission sameModelUseAdmission);
}

/// <summary>Held protected installed worker. Expected hashes alone do not establish execution trust.
/// Its actual source must preserve the original executable binding and immutability until whole close.</summary>
public abstract class StrataOriginalWorkerLease : IAsyncDisposable
{
    public abstract StrataBundledWorker OriginalWorker { get; }
    public abstract void DemandCurrentOriginalBinding();
    public abstract ValueTask DisposeAsync();
}
/// <summary>The producer retains/drains undisclosed or rejected products. Historical IsIssued must
/// remain valid for authentic returned leases through seal/retirement, without granting current use.</summary>
public interface IOriginalStrataWorkerSource
{
    Task<StrataOriginalWorkerLease> AcquireOriginalAsync(TaskRunAttemptAdmission sameModelUseAdmission, IInferenceEngineOriginalSourceScope originalScope,
        CancellationToken cancellationToken);
    bool IsIssuedOriginalWorkerLease(StrataOriginalWorkerLease sameLease, TaskRunAttemptAdmission sameModelUseAdmission);
}

/// <summary>Observed byte count of the SAME held, verified checkpoint inventory. It is
/// model artifact metadata, not RAM/VRAM capacity, residency or permission. The actual
/// source must demand its live private model binding before disclosing the count.</summary>
public interface IStrataOriginalModelSizeBinding
{
    long OriginalModelSizeBytes { get; }
}

/// <summary>Fresh adapter around the SAME configured raw local provider. The provider lifetime is borrowed.</summary>
public sealed class ConfiguredManagedInferenceEngineFactory : IInferenceEngineAdapterFactory
{
    private readonly string _providerId;
    private readonly IModelProviderRegistry _registry;
    private readonly IProviderConfigurationStore _configurations;
    private readonly TaskExecutionCoordinator _coordinator;
    private readonly ITaskRunOriginalFrameOwner _frames;
    private readonly IOriginalDulcheProviderToolSource? _tools;
    private readonly IOriginalDulcheProviderContextSource? _contexts;
    private readonly ITaskRunProviderContextAuthority? _contextAuthority;
    private readonly IModelProvider? _expectedRawProvider;
    public ConfiguredManagedInferenceEngineFactory(string providerId, IModelProviderRegistry registry,
        IProviderConfigurationStore configurations, TaskExecutionCoordinator coordinator,
        ITaskRunOriginalFrameOwner frames, IOriginalDulcheProviderToolSource? tools,
        IOriginalDulcheProviderContextSource? contexts, ITaskRunProviderContextAuthority? contextAuthority,
        IModelProvider? sameObservedRawProvider=null)
    { _expectedRawProvider=sameObservedRawProvider; _providerId=providerId; _registry=registry; _configurations=configurations; _coordinator=coordinator;
        _frames=frames; _tools=tools; _contexts=contexts; _contextAuthority=contextAuthority; }
    public async Task<OriginalInferenceEngineLease> CreateOriginalAsync(ModelIdentity sameModel,
        IInferenceEngineOriginalSourceScope originalScope, CancellationToken cancellationToken)
    {
        if(sameModel.ProviderId!=_providerId) throw new UnauthorizedAccessException("The model belongs to another configured provider.");
        var start=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actual=ManagedEngineLease.Own(originalScope,()=>ManagedProviderDulcheAdapter.CreateAfterPublicationAsync(
            start.Task,_providerId,_registry,_configurations,_coordinator,_frames,_tools,cancellationToken,
            _contexts,_contextAuthority,new FactoryScope(originalScope)));
        start.SetResult();
        ManagedProviderDulcheAdapter? adapter=null;
        try
        {
            adapter=await actual.ConfigureAwait(false);
            return originalScope.InvokeOriginalFactory<OriginalInferenceEngineLease>(()=> {
                var raw=adapter.ObserveOriginalRawModelProvider();
                if(_expectedRawProvider is not null&&!ReferenceEquals(_expectedRawProvider,raw))
                    throw new UnauthorizedAccessException("The exact configured physical provider changed during engine acquisition.");
                return new ManagedEngineLease(adapter,null,
                    originalRequestSource:new ManagedInferenceEngineRequestSource(raw,sameModel,_coordinator));
            });
        }
        catch(Exception cause)
        {
            var errors=new List<Exception>(); ManagedEngineLease.AddTask(errors,cause,actual);
            if(adapter is not null) await ManagedEngineLease.CloseFailedAsync(adapter,null,originalScope,errors).ConfigureAwait(false);
            ManagedEngineLease.Throw(errors); throw;
        }
    }
    private sealed class FactoryScope(IInferenceEngineOriginalSourceScope scope):IDulcheOriginalFactoryCallbackScope
    { public T RunOriginalFactoryInvocation<T>(Func<T> callback)=>scope.InvokeOriginalFactory(()=> {
        var actual=callback();if(actual is Task task)scope.RetainOriginalTask(task);return actual;
    }); }
}

/// <summary>Actual native worker -> SAME managed frame/context owner. No raw registry substitution,
/// second router, tool protocol, context issuer or installation/readiness grant is introduced.</summary>
public sealed class StrataManagedInferenceEngineFactory : IInferenceEngineAdapterFactory
{
    private readonly string _providerId;
    private readonly Uri _target;
    private readonly IOriginalStrataWorkerSource? _binaries;
    private readonly TaskRunAttemptAdmission _modelUseAdmission;
    private readonly IOriginalStrataModelSource? _models;
    private readonly IProviderConfigurationStore _configurations;
    private readonly object _custodyGate=new();private int _admissions;
    private readonly List<object> _originalProducts=[];
    private readonly List<Task> _originalAcquisitions=[];
    private readonly TaskExecutionCoordinator _coordinator;
    private readonly ITaskRunOriginalFrameOwner _frames;
    private readonly IOriginalDulcheProviderToolSource? _tools;
    private readonly IOriginalDulcheProviderContextSource? _contexts;
    private readonly ITaskRunProviderContextAuthority? _contextAuthority;
    public StrataManagedInferenceEngineFactory(string providerId,Uri originalConfiguredTarget,TaskRunAttemptAdmission sameModelUseAdmission,IOriginalStrataWorkerSource? binaries,
        IOriginalStrataModelSource? models,IProviderConfigurationStore configurations,TaskExecutionCoordinator coordinator,ITaskRunOriginalFrameOwner frames,
        IOriginalDulcheProviderToolSource? tools,IOriginalDulcheProviderContextSource? contexts,
        ITaskRunProviderContextAuthority? contextAuthority)
    { _providerId=providerId; _target=originalConfiguredTarget; _modelUseAdmission=sameModelUseAdmission; _binaries=binaries; _models=models; _configurations=configurations; _coordinator=coordinator;
        _frames=frames; _tools=tools; _contexts=contexts; _contextAuthority=contextAuthority; }
    public async Task<OriginalInferenceEngineLease> CreateOriginalAsync(ModelIdentity sameModel,
        IInferenceEngineOriginalSourceScope originalScope,CancellationToken cancellationToken)
    {
        if(sameModel.ProviderId!=_providerId) throw new UnauthorizedAccessException("The model belongs to another configured provider.");
        var models=_models;var binaries=_binaries;
        if(models is null||binaries is null||_modelUseAdmission is null) throw new InferenceEngineException(new(DulcheErrorCode.ProviderUnavailable,
            "Strata setup requires actual protected installed-worker and Safetensors model owners.",sameModel.StableKey,false));
        if(_modelUseAdmission.Lease.Candidate.RequiredCapabilities.Any(value=>value is "Vision"))
            throw new InferenceEngineException(new(DulcheErrorCode.UnsupportedCapability,
                "The actual native Strata transport has no image execution protocol.",sameModel.StableKey,false));
        lock(_custodyGate) { if(_admissions>=128)throw new InvalidOperationException("Original native factory custody is full.");_admissions++; }
        Task<StrataOriginalModelLease>? acquisition=null; Task<StrataNativeWorker>? creation=null;
        Task<ProviderConfiguration?>? currentConfiguration=null;
        Task<StrataOriginalWorkerLease>? binaryAcquisition=null;StrataOriginalWorkerLease? originalBinary=null;
        bool modelOwned=false,binaryOwned=false,toolsObserved=false;
        Task<bool>? toolProbe=null; Task<TaskRunAttemptAdmission?>? toolAdmissionLookup=null;
        StrataOriginalModelLease? originalModel=null; ScopedOriginalModel? scopedModel=null;
        StrataNativeWorker? worker=null; ManagedProviderDulcheAdapter? adapter=null;
        try
        {
            binaryAcquisition=ManagedEngineLease.Own(originalScope,()=>binaries.AcquireOriginalAsync(_modelUseAdmission,originalScope,cancellationToken));
            lock(_custodyGate)_originalAcquisitions.Add(binaryAcquisition);
            originalBinary=await binaryAcquisition.ConfigureAwait(false);
            if(originalBinary is not null)lock(_custodyGate)_originalProducts.Add(originalBinary);
            // Historical private issuance observes cleanup custody, not live use. It must be
            // available after dispatcher seal so an authentic late product can still be closed.
            originalScope.InvokeOriginalCleanup(()=> {
                if(originalBinary is null||!binaries.IsIssuedOriginalWorkerLease(originalBinary,_modelUseAdmission))
                    throw new UnauthorizedAccessException("Returned worker release ownership remains unknown; its source must retain and drain it.");
                binaryOwned=true;return true;
            });
            var heldBinary=originalBinary??throw new InvalidOperationException("No actual issued worker lease was returned.");
            originalScope.InvokeOriginalFactory(()=> {heldBinary.DemandCurrentOriginalBinding();return true;});
            acquisition=ManagedEngineLease.Own(originalScope,()=>models.AcquireOriginalAsync(sameModel,_modelUseAdmission,originalScope,cancellationToken));
            lock(_custodyGate)_originalAcquisitions.Add(acquisition);
            originalModel=await acquisition.ConfigureAwait(false);
            if(originalModel is not null)lock(_custodyGate)_originalProducts.Add(originalModel);
            originalScope.InvokeOriginalCleanup(()=> {
                if(originalModel is null||!models.IsIssuedOriginalModelLease(originalModel,sameModel,_modelUseAdmission))
                    throw new UnauthorizedAccessException("Returned model release ownership remains unknown; its source must retain and drain it.");
                modelOwned=true;return true;
            });
            var heldModel=originalModel??throw new InvalidOperationException("No actual issued model lease was returned.");
            var originalModelSizeBytes=originalScope.InvokeOriginalFactory(()=> {
                heldModel.DemandCurrentOriginalBinding();
                if(heldModel is not IStrataOriginalModelSizeBinding observedSize)
                    throw new InferenceEngineException(new(DulcheErrorCode.ProviderUnavailable,
                        "Strata setup requires the genuine held checkpoint inventory byte-count source.",sameModel.StableKey,false));
                var actualBytes=observedSize.OriginalModelSizeBytes;
                heldModel.DemandCurrentOriginalBinding();
                if(actualBytes<=0)throw new InferenceEngineException(new(DulcheErrorCode.ModelLoadFailed,
                    "The actual held checkpoint inventory has no positive model artifact byte count.",sameModel.StableKey,false));
                return actualBytes;
            });
            scopedModel=originalScope.InvokeOriginalFactory(()=> {
                if(heldModel.Requirements.Model!=sameModel)throw new InvalidDataException("The authentic model lease names another model.");
                heldModel.DemandCurrentOriginalBinding();heldBinary.DemandCurrentOriginalBinding();
                return heldModel is IStrataOriginalHardwareBinding actualHardware
                    ? new ScopedOriginalHardwareModel(heldModel,heldBinary,originalScope,actualHardware)
                    : new ScopedOriginalModel(heldModel,heldBinary,originalScope);
            });
            var start=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            creation=ManagedEngineLease.Own(originalScope,()=> {
                heldBinary.DemandCurrentOriginalBinding();
                return StrataNativeWorker.CreateAfterPublicationAsync(start.Task,heldBinary.OriginalWorker,scopedModel,cancellationToken);
            });
            lock(_custodyGate)_originalAcquisitions.Add(creation);
            start.SetResult(); worker=await creation.ConfigureAwait(false);
            lock(_custodyGate)_originalProducts.Add(worker);
            if (_modelUseAdmission.Lease.Candidate.RequiredCapabilities.Contains("Tools", StringComparer.Ordinal))
            {
                // Inventory is eligibility only. Load remains the original protected owner;
                // only a real correlated proposal from this model establishes raw Tools support.
                toolAdmissionLookup=ManagedEngineLease.Own(originalScope,()=>_coordinator.GetIssuedAttemptWithinOriginalSourceAsync(
                    _modelUseAdmission,callback=>originalScope.InvokeOriginalFactory(()=> { callback();return true; }),originalScope.RetainOriginalTask,cancellationToken));
                lock(_custodyGate)_originalAcquisitions.Add(toolAdmissionLookup);
                if(!ReferenceEquals(await toolAdmissionLookup.ConfigureAwait(false),_modelUseAdmission))
                    throw new UnauthorizedAccessException("The original Task attempt changed before its native tool probe.");
                toolProbe=ManagedEngineLease.Own(originalScope,()=>worker.ProbeOriginalStructuredToolsAsync(cancellationToken));
                lock(_custodyGate)_originalAcquisitions.Add(toolProbe);
                if(!await toolProbe.ConfigureAwait(false))throw new NotSupportedException("No actual model structured-tool probe succeeded.");
                toolAdmissionLookup=ManagedEngineLease.Own(originalScope,()=>_coordinator.GetIssuedAttemptWithinOriginalSourceAsync(
                    _modelUseAdmission,callback=>originalScope.InvokeOriginalFactory(()=> { callback();return true; }),originalScope.RetainOriginalTask,cancellationToken));
                lock(_custodyGate)_originalAcquisitions.Add(toolAdmissionLookup);
                if(!ReferenceEquals(await toolAdmissionLookup.ConfigureAwait(false),_modelUseAdmission))
                    throw new UnauthorizedAccessException("The original Task attempt changed during its native tool probe.");
                toolsObserved=originalScope.InvokeOriginalFactory(()=>worker.OriginalStructuredToolsAvailable);
                if(!toolsObserved)throw new NotSupportedException("The actual original native Tools proof is no longer current.");
            }
            currentConfiguration=ManagedEngineLease.Own(originalScope,()=>_configurations.GetAsync(_providerId,cancellationToken));
            lock(_custodyGate)_originalAcquisitions.Add(currentConfiguration);
            var configuration=await currentConfiguration.ConfigureAwait(false);
            adapter=originalScope.InvokeOriginalFactory(()=> {
                if(configuration is null||!configuration.IsEnabled||configuration.Id!=_providerId
                    ||configuration.Kind!=ModelProviderKind.OpenAICompatible||!configuration.IsLocal
                    ||!Uri.TryCreate(configuration.Endpoint,UriKind.Absolute,out var target)||target!=_target)
                    throw new UnauthorizedAccessException("The original configured local engine target changed during initialization.");
                var requirements=worker.Model;
                var descriptor=new ProviderModelDescriptor(_providerId,true,new(sameModel.ModelId,originalModelSizeBytes,"Strata","","",
                    toolsObserved ? new HashSet<ToolCapability>{ToolCapability.Text,ToolCapability.Streaming,ToolCapability.Tools}
                        : new HashSet<ToolCapability>{ToolCapability.Text,ToolCapability.Streaming},DateTimeOffset.UtcNow),
                    requirements.ContextTokens,sameModel.ModelId);
                var raw=new StrataRawModelProvider(_providerId,worker,descriptor);
                return ManagedProviderDulcheAdapter.CreateOriginalStrataProvider(raw,_target,_coordinator,_frames,
                    _tools,_contexts,_contextAuthority);
            });
            var lease=originalScope.InvokeOriginalFactory(()=>new ManagedEngineLease(adapter,worker,heldBinary,
                new ManagedInferenceEngineRequestSource(adapter.ObserveOriginalRawModelProvider(),sameModel,_coordinator,worker.Model.ContextTokens)));
            scopedModel.PublishOriginal(); // No new native callback starts until dispatcher retains this returned lease.
            return lease;
        }
        catch(Exception cause)
        {
            var errors=new List<Exception>(); ManagedEngineLease.AddTask(errors,cause,(Task?)currentConfiguration??(Task?)toolAdmissionLookup??(Task?)toolProbe??(Task?)creation??(Task?)acquisition??binaryAcquisition);
            await ManagedEngineLease.CloseFailedAsync(adapter,worker,originalScope,errors).ConfigureAwait(false);
            // Worker initialization owns its model as soon as its actual driver is acquired; its failure
            // already joins model close. The wrapper coalesces that SAME cleanup if queried here too.
            Task? modelClose=null;
            if(scopedModel is not null)
                try { modelClose=ManagedEngineLease.OwnCleanup(originalScope,()=>scopedModel.DisposeAsync().AsTask()); }
                catch(Exception error) { ManagedEngineLease.Add(errors,error); }
            else if(modelOwned&&originalModel is not null)
                try { modelClose=ManagedEngineLease.OwnCleanup(originalScope,()=>originalModel.DisposeAsync().AsTask()); }
                catch(Exception error) { ManagedEngineLease.Add(errors,error); }
            if(modelClose is not null) await ManagedEngineLease.Join(modelClose,errors).ConfigureAwait(false);
            Task? binaryClose=null;
            if(binaryOwned&&originalBinary is not null)
                try { binaryClose=ManagedEngineLease.OwnCleanup(originalScope,()=>originalBinary.DisposeAsync().AsTask()); }
                catch(Exception error){ManagedEngineLease.Add(errors,error);}
            if(binaryClose is not null)await ManagedEngineLease.Join(binaryClose,errors).ConfigureAwait(false);
            ManagedEngineLease.Throw(errors); throw;
        }
    }

    /// <summary>Protects every artifact callback while its native product is still unpublished.
    /// After publication, the actual worker's finite guard and returned lease preflight own those callbacks.</summary>
    /// <summary>Expose optional hardware only when the SAME authentic held model supplies it.
    /// Missing hardware stays uncalibrated; a real getter fault remains the original source fault.</summary>
    private sealed class ScopedOriginalHardwareModel(StrataOriginalModelLease original,StrataOriginalWorkerLease binary,
        IInferenceEngineOriginalSourceScope scope,IStrataOriginalHardwareBinding sameHardware)
        :ScopedOriginalModel(original,binary,scope),IStrataOriginalHardwareBinding
    {
        public InferenceHardware OriginalHardwareObservation=>Read(()=>sameHardware.OriginalHardwareObservation);
    }
    private class ScopedOriginalModel(StrataOriginalModelLease original,StrataOriginalWorkerLease binary,IInferenceEngineOriginalSourceScope scope):StrataOriginalModelLease
    {
        private readonly object _gate=new(); private bool _published; private Task? _close; private Task? _rawClose;
        protected T Read<T>(Func<T> callback) { bool published;lock(_gate) published=_published;return published?callback():scope.InvokeOriginalFactory(callback); }
        public override InferenceModelRequirements Requirements=>Read(()=>original.Requirements);
        public override string OriginalCheckpointDirectory=>Read(()=>original.OriginalCheckpointDirectory);
        public override IReadOnlyList<int> ActualCudaDeviceIndices=>Read(()=>original.ActualCudaDeviceIndices);
        public override InferenceEngineSupport? OriginalBuildSupport=>Read(()=>original.OriginalBuildSupport);
        public override void DemandCurrentOriginalBinding()=>Read(()=> { binary.DemandCurrentOriginalBinding();original.DemandCurrentOriginalBinding();return true; });
        public void PublishOriginal() { lock(_gate) _published=true; }
        public override ValueTask DisposeAsync()
        {
            TaskCompletionSource<Task>? acquired=null;Task actual;bool published;
            lock(_gate) { published=_published;if(_close is null) {
                acquired=new(TaskCreationOptions.RunContinuationsAsynchronously);_close=Close(acquired.Task);
            }actual=_close; }
            if(acquired is not null)
            {
                // Acquire now, inside the invoking worker's finite physical cleanup scope. The
                // encompassing close was published first, but no external callback runs under _gate.
                try { _rawClose=published?original.DisposeAsync().AsTask():ManagedEngineLease.OwnCleanup(scope,()=>original.DisposeAsync().AsTask());acquired.SetResult(_rawClose); }
                catch(Exception cause) { acquired.SetException(new AggregateException("The original model close factory faulted.",cause)); }
            }
            return new(actual);
        }
        private static async Task Close(Task<Task> acquisition)
        {
            var errors=new List<Exception>();Task? actual=null;
            try {actual=await acquisition.ConfigureAwait(false);}catch(Exception cause){ManagedEngineLease.AddTask(errors,cause,acquisition);}
            if(actual is not null)await ManagedEngineLease.Join(actual,errors).ConfigureAwait(false);ManagedEngineLease.Throw(errors);
        }
    }
}

internal sealed class ManagedEngineLease(ManagedProviderDulcheAdapter adapter,StrataNativeWorker? worker,StrataOriginalWorkerLease? binary=null,
    IOriginalInferenceEngineRequestSource? originalRequestSource=null):OriginalInferenceEngineLease,IOriginalInferenceCalibrationLease,IOriginalInferenceEngineRequestLease
{
    private readonly object _gate=new();private Task? _close;private Task? _adapterClose;private Task? _workerClose;private Task? _binaryClose;
    private readonly AsyncLocal<ClosePhase?> _executing=new();
    [ThreadStatic]private static List<ManagedEngineLease>? _physical;
    public override IDulcheOriginalProviderAdapter Adapter=>adapter;
    public IOriginalInferenceCalibrationSource? CalibrationSource=>worker;
    public IOriginalInferenceEngineRequestSource OriginalRequestSource=>originalRequestSource
        ??throw new NotSupportedException("This original engine lease has no actual typed request transport.");
    public override void DemandExternalOriginalJoin()
    {
        if(_executing.Value?.Live==true||_physical?.Contains(this)==true)
            throw new InvalidOperationException("An original engine lease cleanup cannot join its containing close.");
        adapter.RequireIndependentOriginalProviderJoin();worker?.DemandExternalOriginalJoin();
    }
    public override Task CloseOriginalAsync()
    {
        DemandExternalOriginalJoin();TaskCompletionSource? start=null;Task actual;
        lock(_gate) { if(_close is null) { start=new(TaskCreationOptions.RunContinuationsAsynchronously);_close=Close(start.Task); } actual=_close; }
        start?.SetResult();return actual;
    }
    private async Task Close(Task start)
    {
        await start.ConfigureAwait(false);var errors=new List<Exception>();var phase=new ClosePhase();_executing.Value=phase;
        try
        {
            // Acquire both actual closes before joining either. Adapter failure never skips worker/model close.
            try { _adapterClose=Physical(()=>adapter.DisposeAsync().AsTask()); } catch(Exception error) { Add(errors,error); }
            try { _workerClose=Physical(()=>worker?.DisposeAsync().AsTask()); } catch(Exception error) { Add(errors,error); }
            if(_adapterClose is not null) await Join(_adapterClose,errors).ConfigureAwait(false);
            if(_workerClose is not null) await Join(_workerClose,errors).ConfigureAwait(false);
            // Preserve executable custody through the actual worker's whole process/model/pipe close.
            try { _binaryClose=Physical(()=>binary?.DisposeAsync().AsTask()); }catch(Exception error){Add(errors,error);}
            if(_binaryClose is not null)await Join(_binaryClose,errors).ConfigureAwait(false);
        }
        finally {phase.Live=false;_executing.Value=null;}
        Throw(errors);
    }
    private T Physical<T>(Func<T> callback)
    {var stack=_physical??=[];stack.Add(this);try{return callback();}finally{stack.RemoveAt(stack.Count-1);}}
    private sealed class ClosePhase {public volatile bool Live=true;}
    internal static async Task CloseFailedAsync(ManagedProviderDulcheAdapter? adapter,StrataNativeWorker? worker,
        IInferenceEngineOriginalSourceScope scope,List<Exception> errors)
    {
        Task? a=null,w=null;
        if(adapter is not null)try { a=OwnCleanup(scope,()=>adapter.DisposeAsync().AsTask()); }catch(Exception error){Add(errors,error);}
        if(worker is not null)try { w=OwnCleanup(scope,()=>worker.DisposeAsync().AsTask()); }catch(Exception error){Add(errors,error);}
        if(a is not null)await Join(a,errors).ConfigureAwait(false);if(w is not null)await Join(w,errors).ConfigureAwait(false);
    }
    internal static Task<T> Own<T>(IInferenceEngineOriginalSourceScope scope,Func<Task<T>> factory)
    {
        try {return scope.InvokeOriginalFactory(()=> {var task=factory()??throw new InvalidOperationException("No actual source Task.");scope.RetainOriginalTask(task);return task;});}
        catch(OperationCanceledException cause) {throw new AggregateException("The original source factory faulted synchronously.",cause);}
    }
    internal static Task OwnCleanup(IInferenceEngineOriginalSourceScope scope,Func<Task> factory)
    {
        try {return scope.InvokeOriginalCleanup(()=> {var task=factory()??throw new InvalidOperationException("No actual cleanup Task.");scope.RetainOriginalTask(task);return task;});}
        catch(OperationCanceledException cause) {throw new AggregateException("The original cleanup factory faulted synchronously.",cause);}
    }
    internal static void Add(List<Exception> errors,Exception error) { if(!errors.Any(x=>ReferenceEquals(x,error)))errors.Add(error); }
    internal static void AddTask(List<Exception> errors,Exception error,Task? original)
    { Add(errors,error);if(original?.Exception is { } group)foreach(var cause in group.InnerExceptions)Add(errors,cause); }
    internal static async Task Join(Task task,List<Exception> errors)
    { try { await task.ConfigureAwait(false); }catch(Exception error){AddTask(errors,error,task);} }
    internal static void Throw(List<Exception> errors)
    { if(errors.Count==1&&errors[0] is not OperationCanceledException)ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if(errors.Count!=0)throw new AggregateException("Original engine initialization/cleanup causes.",errors); }
}
