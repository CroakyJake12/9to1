using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Dulche.Runtime;
using Haven.Application;
using Xunit;

namespace Dulche.Runtime.Tests;

/// <summary>Real runtime startup/publication controls only. The recording adapter grants no native
/// installation, Task/Run, tool/context, GPU, model execution or readiness acceptance.</summary>
public sealed class ManagedInferenceModelStartupTests
{
    [Fact]
    public async Task ExplicitModelIsCapturedBeforeOriginalAdapterStart()
    {
        var adapter=new RecordingOriginalAdapter();var runtime=new DulcheRuntime([adapter]);
        var model=new ModelIdentity(adapter.ProviderId,"exact-model","artifact-revision");
        DulcheEndpointStartupOriginal? original=null;Task<OperationResult<DulcheEndpoint>>? startup=null,stop=null;
        var errors=new List<Exception>();
        try
        {
            original=runtime.PrepareManagedProviderOriginal(adapter.ProviderId,model,CancellationToken.None);
            startup=original.OriginalStartup;
            Assert.Same(model,original.OriginalAcquisition.Model);
            Assert.False(original.OriginalStartup.IsCompleted);Assert.Equal(0,adapter.Starts);
            original.StartOriginal();
            var result=await startup.WaitAsync(TimeSpan.FromSeconds(5),CancellationToken.None);
            Assert.True(result.Succeeded);Assert.Same(model,adapter.Observed!.Model);Assert.Equal(1,adapter.Starts);
            stop=runtime.JoinEndpointStopAsync(original.OriginalAcquisition.EndpointId);
            var stopped=await stop;
            Assert.True(stopped.Succeeded);Assert.Equal(1,adapter.Stops);
        }
        catch(Exception cause){Add(errors,cause);}
        finally {await Drain(runtime,original,startup,stop,errors);}
        Throw(errors);
    }
    [Fact]
    public async Task ForeignProviderModelRefusesBeforeEndpointOrCallbackAcquisition()
    {
        var adapter=new RecordingOriginalAdapter();var runtime=new DulcheRuntime([adapter]);
        DulcheEndpointStartupOriginal? unexpected=null;Task<OperationResult<DulcheEndpoint>>? startup=null;
        var errors=new List<Exception>();
        try
        {
            Assert.Throws<ArgumentException>((Action)(()=> {
                unexpected=runtime.PrepareManagedProviderOriginal(adapter.ProviderId,new ModelIdentity("foreign","model"),CancellationToken.None);
                startup=unexpected.OriginalStartup;
            }));
            Assert.Equal(0,adapter.Starts);Assert.Equal(0,adapter.Stops);
        }
        catch(Exception cause){Add(errors,cause);}
        finally {await Drain(runtime,unexpected,startup,null,errors);}
        Throw(errors);
    }
    [Fact]
    public async Task ExistingNullModelOriginalStartupRetainsItsOriginalBehavior()
    {
        var adapter=new RecordingOriginalAdapter();var runtime=new DulcheRuntime([adapter]);
        DulcheEndpointStartupOriginal? original=null;Task<OperationResult<DulcheEndpoint>>? startup=null,stop=null;
        var errors=new List<Exception>();
        try
        {
            original=runtime.PrepareManagedProviderOriginal(adapter.ProviderId,CancellationToken.None);startup=original.OriginalStartup;
            Assert.Null(original.OriginalAcquisition.Model);Assert.Equal(0,adapter.Starts);
            original.StartOriginal();var result=await startup.WaitAsync(TimeSpan.FromSeconds(5),CancellationToken.None);
            Assert.True(result.Succeeded);Assert.Null(adapter.Observed!.Model);Assert.Equal(1,adapter.Starts);
            stop=runtime.JoinEndpointStopAsync(original.OriginalAcquisition.EndpointId);
            Assert.True((await stop).Succeeded);
        }
        catch(Exception cause){Add(errors,cause);}
        finally {await Drain(runtime,original,startup,stop,errors);}
        Throw(errors);
    }
    [Fact]
    public async Task MissingProtectedStrataSourcesRefuseBeforeAnyOriginalFactory()
    {
        var scope=new RefusingScope();var model=new ModelIdentity("local","model");
        // These central dependencies are unreachable: the tested setup gate precedes all acquisitions.
        var factory=new StrataManagedInferenceEngineFactory("local",new Uri("http://127.0.0.1/"),
            null!,null,null,null!,null!,null!,null,null,null);
        var actual=factory.CreateOriginalAsync(model,scope,CancellationToken.None);
        var cause=await Assert.ThrowsAsync<InferenceEngineException>(()=>actual);
        Assert.True(actual.IsFaulted);Assert.Equal(DulcheErrorCode.ProviderUnavailable,cause.Error.Code);
        Assert.Equal(model.StableKey,cause.Error.Target);Assert.Equal(0,scope.Invocations);
    }
    private static async Task Drain(DulcheRuntime runtime,DulcheEndpointStartupOriginal? original,
        Task<OperationResult<DulcheEndpoint>>? startup,Task<OperationResult<DulcheEndpoint>>? stop,List<Exception> errors)
    {
        if(original is null)return;
        // Real retirement releases an unopened startup publication gate. Acquire stop before joins.
        try {stop??=runtime.JoinEndpointStopAsync(original.OriginalAcquisition.EndpointId);}catch(Exception cause){Add(errors,cause);}
        if(startup is not null)await Observe(startup,errors);
        if(stop is not null)await Observe(stop,errors);
    }
    private static async Task Observe(Task<OperationResult<DulcheEndpoint>> actual,List<Exception> errors)
    {
        try
        {
            var result=await actual.WaitAsync(TimeSpan.FromSeconds(5),CancellationToken.None);
            if(!result.Succeeded)Add(errors,new UnexpectedStartupResult(result.Error!));
        }
        catch(Exception cause)
        {
            Add(errors,cause);
            if(actual.Exception is { } group)foreach(var error in group.InnerExceptions)Add(errors,error);
        }
    }
    private static void Add(List<Exception> errors,Exception cause)
    {if(!errors.Any(original=>ReferenceEquals(original,cause)))errors.Add(cause);}
    private static void Throw(List<Exception> errors)
    {if(errors.Count==1)ExceptionDispatchInfo.Capture(errors[0]).Throw();if(errors.Count>1)throw new AggregateException(errors);}
    private sealed class UnexpectedStartupResult(DulcheError original):Exception(original.Message)
    {public DulcheError OriginalError {get;}=original;}
    private sealed class RefusingScope:IInferenceEngineOriginalSourceScope
    {
        public int Invocations;
        public T InvokeOriginalFactory<T>(Func<T> callback) { Invocations++;throw new InvalidOperationException("No original factory is expected."); }
        public T InvokeOriginalCleanup<T>(Func<T> callback) { Invocations++;throw new InvalidOperationException("No cleanup product was acquired."); }
        public void RetainOriginalTask(Task task) { Invocations++;throw new InvalidOperationException("No raw Task was acquired."); }
    }
    private sealed class RecordingOriginalAdapter:IDulcheOriginalProviderAdapter
    {
        public int Starts,Stops;public DulcheEndpoint? Observed;
        public string ProviderId=>"local";public string RuntimeVersion=>"recording-startup-only";public bool IsLocal=>true;
        public Uri OriginalConfiguredTarget=>new("http://127.0.0.1/");
        public IReadOnlySet<string> Capabilities=>new HashSet<string>();
        public ValueTask<OperationResult<Unit>> StartAsync(DulcheEndpoint endpoint,CancellationToken token)
        { Starts++;Observed=endpoint;return ValueTask.FromResult(OperationResult<Unit>.Success(Unit.Value)); }
        public Task BindOriginalRequestAsync(DulcheEndpoint endpoint,DulcheRequest request,RuntimeRequestHandle handle,
            TaskRunAttemptAdmission admission,Guid action,CancellationToken token)=>throw new InvalidOperationException("No request authority is issued by this startup control.");
        public void CaptureOriginalDispatchRequest(RuntimeRequestHandle handle,DulcheRequest frozen,DulcheRequest dispatched)
            =>throw new InvalidOperationException("No request dispatch is owned by this startup control.");
        public async IAsyncEnumerable<AdapterDelta> GenerateAsync(DulcheEndpoint endpoint,DulcheRequest request,string requestId,
            [EnumeratorCancellation]CancellationToken token) { await Task.CompletedTask;yield break; }
        public async IAsyncEnumerable<AdapterDelta> ContinueWithToolResultAsync(DulcheEndpoint endpoint,DulcheRequest request,string requestId,
            ToolInvocationResult result,[EnumeratorCancellation]CancellationToken token) { await Task.CompletedTask;yield break; }
        public ValueTask<OperationResult<Unit>> CancelAsync(DulcheEndpoint endpoint,string requestId,CancellationToken token)
            =>ValueTask.FromResult(OperationResult<Unit>.Success(Unit.Value));
        public ValueTask<OperationResult<Unit>> PauseAsync(DulcheEndpoint endpoint,string requestId,CancellationToken token)
            =>ValueTask.FromResult(OperationResult<Unit>.Success(Unit.Value));
        public ValueTask<OperationResult<Unit>> ResumeAsync(DulcheEndpoint endpoint,string requestId,CancellationToken token)
            =>ValueTask.FromResult(OperationResult<Unit>.Success(Unit.Value));
        public ValueTask<OperationResult<Unit>> StopAsync(DulcheEndpoint endpoint,CancellationToken token)
        { Stops++;return ValueTask.FromResult(OperationResult<Unit>.Success(Unit.Value)); }
        public ValueTask<RuntimeHealth> HealthAsync(DulcheEndpoint endpoint,CancellationToken token)
            =>throw new InvalidOperationException("No health/model proof is issued by this startup control.");
    }
}
