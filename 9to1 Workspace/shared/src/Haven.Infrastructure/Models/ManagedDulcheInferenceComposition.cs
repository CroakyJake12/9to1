using System.Runtime.ExceptionServices;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Trusted optional bootstrap, not a model or context issuer. Receives the SAME service's
/// borrowed authority owners; no extra coordinator, registry, router or fallback loop is constructed.</summary>
public interface IManagedDulcheInferenceCompositionSource
{
    Task<OriginalInferenceEngineLease> CreateAfterPublicationAsync(Task originalStart,string providerId,
        ModelIdentity sameModel,TaskRunAttemptAdmission sameModelUseAdmission,IModelProviderRegistry sameRegistry,IProviderConfigurationStore sameConfigurations,
        TaskExecutionCoordinator sameCoordinator,ITaskRunOriginalFrameOwner sameFrames,
        IOriginalDulcheProviderToolSource? sameTools,IOriginalDulcheProviderContextSource? sameContexts,
        ITaskRunProviderContextAuthority? sameContextAuthority,IDulcheOriginalFactoryCallbackScope originalScope,
        CancellationToken cancellationToken);
}

/// <summary>One real multi-engine bootstrap. Installation/hardware/model observation sources must
/// be provided by their actual owners. Construction performs no I/O, native launch or probing.</summary>
public sealed class ManagedDulcheInferenceComposition : IManagedDulcheInferenceCompositionSource
{
    private readonly object _gate=new();
    private readonly IInferenceRuntimeObservationSource _observations;
    private readonly IOriginalStrataModelSource? _strataModels;
    private readonly IOriginalStrataWorkerSource? _strataBinaries;
    private readonly List<OriginalComposition> _originals=[];
    public ManagedDulcheInferenceComposition(IInferenceRuntimeObservationSource observations,
        IOriginalStrataModelSource? strataModels=null,IOriginalStrataWorkerSource? strataBinaries=null)
    {
        _observations=observations??throw new ArgumentNullException(nameof(observations));
        if((strataModels is null)!=(strataBinaries is null)) throw new ArgumentException("The actual Strata binary and model sources must be composed together.");
        _strataModels=strataModels;_strataBinaries=strataBinaries;
    }
    public Task<OriginalInferenceEngineLease> CreateAfterPublicationAsync(Task originalStart,string providerId,
        ModelIdentity sameModel,TaskRunAttemptAdmission sameModelUseAdmission,IModelProviderRegistry sameRegistry,IProviderConfigurationStore sameConfigurations,
        TaskExecutionCoordinator sameCoordinator,ITaskRunOriginalFrameOwner sameFrames,
        IOriginalDulcheProviderToolSource? sameTools,IOriginalDulcheProviderContextSource? sameContexts,
        ITaskRunProviderContextAuthority? sameContextAuthority,IDulcheOriginalFactoryCallbackScope originalScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalStart);
        lock(_gate)
        {
            if(_originals.Count>=128) throw new InvalidOperationException("Original engine composition custody is full.");
            var original=new OriginalComposition();
            // Caller owns this gated encompassing Task before any external original acquisition.
            original.Driver=Create(originalStart,original);_originals.Add(original);return original.Driver!;
        }
        async Task<OriginalInferenceEngineLease> Create(Task start,OriginalComposition original)
        {
            await start.ConfigureAwait(false);InferenceEngineDispatcher? dispatcher=null;
            try
            {
                if(sameModel.ProviderId!=providerId)throw new UnauthorizedAccessException("The canonical model belongs to another configured provider.");
                original.Configuration=originalScope.RunOriginalFactoryInvocation(()=> {
                    var actual=sameConfigurations.GetAsync(providerId,cancellationToken)
                        ??throw new InvalidOperationException("No actual configuration Task was returned.");
                    original.Configuration=actual;return actual;
                });
                var configuration=await original.Configuration.ConfigureAwait(false);
                var raw=originalScope.RunOriginalFactoryInvocation(()=>sameRegistry.GetRequired(providerId));
                if(raw is not LlamaCppModelProvider||raw.Id!=providerId||configuration is null||!configuration.IsEnabled
                    ||configuration.Id!=providerId||configuration.Kind!=raw.Kind||!configuration.IsLocal
                    ||!Uri.TryCreate(configuration.Endpoint,UriKind.Absolute,out var target)
                    ||target.Scheme is not ("http" or "https")||!string.IsNullOrEmpty(target.UserInfo)
                    ||!string.IsNullOrEmpty(target.Fragment)||!string.IsNullOrEmpty(target.Query)
                    ||target.Scheme!=Uri.UriSchemeHttps&&!target.IsLoopback)
                    throw new UnauthorizedAccessException("No SAME configured concrete local engine route is available.");
                original.Observation=originalScope.RunOriginalFactoryInvocation(()=>
                    _observations is IStrataOriginalRequestObservationSource perRequest
                        ? perRequest.BindOriginalRequest(sameModelUseAdmission)
                            ??throw new InvalidOperationException("No SAME request-bound inference observer was returned.")
                        : _observations);
                dispatcher=originalScope.RunOriginalFactoryInvocation(()=> {
                    var registrations=new List<InferenceEngineRegistration>{new(InferenceEngine.LlamaCpp,
                        new ConfiguredManagedInferenceEngineFactory(providerId,sameRegistry,sameConfigurations,sameCoordinator,sameFrames,
                            sameTools,sameContexts,sameContextAuthority,raw))};
                    registrations.Add(new(InferenceEngine.Strata,new StrataManagedInferenceEngineFactory(providerId,target,sameModelUseAdmission,
                        _strataBinaries,_strataModels,sameConfigurations,sameCoordinator,sameFrames,sameTools,sameContexts,sameContextAuthority)));
                    return new InferenceEngineDispatcher(providerId,target,registrations,original.Observation!);
                });
                return originalScope.RunOriginalFactoryInvocation<OriginalInferenceEngineLease>(()=>new DispatcherLease(dispatcher));
            }
            catch(Exception cause)
            {
                var errors=new List<Exception>();Add(errors,cause);
                if(original.Configuration?.Exception is { } group)foreach(var error in group.InnerExceptions)Add(errors,error);
                if(dispatcher is not null)
                {
                    // Late successful products remain owned even if the service sealed during acquisition.
                    try { original.Cleanup=originalScope.RunOriginalFactoryInvocation(()=>dispatcher.DisposeAsync().AsTask()); }
                    catch(Exception error){Add(errors,error);}
                    if(original.Cleanup is { } actualCleanup)
                    {
                        try { await actualCleanup.ConfigureAwait(false); }catch(Exception error){Add(errors,error);}
                        if(actualCleanup.Exception is { } cleanup)foreach(var error in cleanup.InnerExceptions)Add(errors,error);
                    }
                }
                if(errors.Count==1&&errors[0] is not OperationCanceledException)ExceptionDispatchInfo.Capture(errors[0]).Throw();
                throw new AggregateException("Original engine composition/cleanup causes.",errors);
            }
        }
    }
    private static void Add(List<Exception> errors,Exception error) { if(!errors.Any(x=>ReferenceEquals(x,error)))errors.Add(error); }
    private sealed class OriginalComposition { public Task<OriginalInferenceEngineLease>? Driver;public Task<ProviderConfiguration?>? Configuration;public Task? Cleanup;public IInferenceRuntimeObservationSource? Observation; }
    private sealed class DispatcherLease(InferenceEngineDispatcher dispatcher):OriginalInferenceEngineLease
    {
        private readonly object _gate=new();private Task? _close;private Task? _originalDispatcherClose;
        public override IDulcheOriginalProviderAdapter Adapter=>dispatcher;
        public override void DemandExternalOriginalJoin()=>dispatcher.DemandExternalOriginalJoin();
        public override Task CloseOriginalAsync()
        {
            DemandExternalOriginalJoin();
            TaskCompletionSource? start=null;Task actual;
            lock(_gate) {if(_close is null){start=new(TaskCreationOptions.RunContinuationsAsynchronously);_close=Close(start.Task);}actual=_close;}
            start?.SetResult();return actual;
        }
        private async Task Close(Task start)
        {
            await start.ConfigureAwait(false);
            try
            {
                _originalDispatcherClose=dispatcher.DisposeAsync().AsTask();
                await _originalDispatcherClose.ConfigureAwait(false);
            }
            catch(Exception cause)
            {
                var errors=new List<Exception>();Add(errors,cause);
                if(_originalDispatcherClose?.Exception is { } group)foreach(var error in group.InnerExceptions)Add(errors,error);
                if(errors.Count==1&&errors[0] is not OperationCanceledException)ExceptionDispatchInfo.Capture(errors[0]).Throw();
                throw new AggregateException("Original dispatcher close causes.",errors);
            }
        }
    }
}
