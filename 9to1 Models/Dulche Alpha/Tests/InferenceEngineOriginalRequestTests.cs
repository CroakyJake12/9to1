using System.Runtime.ExceptionServices;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Dulche.Tests;

/// <summary>Actual dispatcher task/stream custody with a synthetic transport. Null admissions
/// are negative transport data only; no model/action/context/permission authority is constructed.</summary>
public sealed class InferenceEngineOriginalRequestTests
{
    [Fact]
    public async Task Original_chat_and_tool_wires_remain_same_and_active_switch_refuses_held_raw_call()
    {
        var rig=new Rig();Task? actual=null;Exception? primary=null;
        try {
            await rig.Start();var selected=rig.Dispatcher.GetOriginalRequestSource(rig.Endpoint.EndpointId);
            var retained=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var scope=new CallerScope { OnRetain=task=> { if(ReferenceEquals(task,rig.Source.Completion.Task)) retained.TrySetResult(); } };
            var chat=new OllamaChatRequest("wire-alias",[new("user","same bytes",["same image"])],EffortLevel.High,"same system",true,new(0.4,4096,9));
            var response=selected.CompleteOriginalAsync(chat,null!,Guid.Empty,scope,CancellationToken.None);actual=response;
            await retained.Task.WaitAsync(TimeSpan.FromSeconds(5),CancellationToken.None);
            Assert.Same(chat,rig.Source.Chat);Assert.Same(rig.Source.Completion.Task,Assert.Single(scope.Tasks));
            Assert.False(response.IsCompleted);var refused=await rig.Dispatcher.SwitchInferenceEngineAsync(rig.Endpoint.EndpointId,InferenceEngine.LlamaCpp);
            Assert.False(refused.Succeeded);Assert.Equal(DulcheErrorCode.Conflict,refused.Error!.Code);Assert.Equal(1,rig.Factory.Created);
            rig.Source.Completion.TrySetResult("same output");Assert.Equal("same output",await response);
            var tools=new OllamaToolRequest("wire-alias",[new("tool","history",ToolName:"original_tool",Images:["original image"])],[],EffortLevel.Medium,"system",new());
            var result=await selected.ToolsOriginalAsync(tools,null!,Guid.Empty,new CallerScope(),CancellationToken.None);
            Assert.Same(tools,rig.Source.Tools);Assert.Same(rig.Source.ToolResult,result);
            Assert.Equal(1,rig.Source.CompleteCalls);Assert.Equal(1,rig.Source.ToolCalls);
            Assert.Equal(rig.Model,rig.Dispatcher.GetInferenceEngine(rig.Endpoint.EndpointId).Model);
        } catch(Exception error) { primary=error; }
        finally { rig.Release();await Finish(rig,[actual],[],primary); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Failed_repeated_or_deferred_caller_scope_keeps_acquired_raw_group_and_refuses_late_factory(int mode)
    {
        var rig=new Rig();var caller=new CallerScope(mode);Task? actual=null,close=null;Exception? primary=null;
        var oce=new OperationCanceledException("raw faulted OCE");var io=new IOException("raw sibling");var expected=new List<Exception>();
        try {
            await rig.Start();var selected=rig.Dispatcher.GetOriginalRequestSource(rig.Endpoint.EndpointId);
            actual=selected.CompleteOriginalAsync(new("wire",[],EffortLevel.Medium),null!,Guid.Empty,caller,CancellationToken.None);
            if(mode!=2) {
                await rig.Source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5),CancellationToken.None);
                Assert.False(actual.IsCompleted);Assert.Equal(1,rig.Source.CompleteCalls);
                close=rig.Dispatcher.DisposeAsync().AsTask();Assert.False(close.IsCompleted);
                rig.Source.Completion.TrySetException([oce,io]);
            }
            var outward=await Capture(actual);Assert.NotNull(outward);Assert.True(actual.IsFaulted);
            if(mode==2) {
                Assert.Equal(0,rig.Source.CompleteCalls);
                expected.AddRange(Graph(actual.Exception!).Where(cause=>cause is not AggregateException));
                Assert.NotNull(caller.Deferred);
                Assert.Throws<InvalidOperationException>(()=>caller.Deferred!());Assert.Equal(0,rig.Source.CompleteCalls);
                close=rig.Dispatcher.DisposeAsync().AsTask();
            } else {
                Assert.Contains(Graph(actual.Exception!),cause=>ReferenceEquals(cause,oce));
                Assert.Contains(Graph(actual.Exception!),cause=>ReferenceEquals(cause,io));
                Assert.Contains(Graph(actual.Exception!),cause=>ReferenceEquals(cause,caller.Failure));
                expected.Add(oce);expected.Add(io);expected.Add(caller.Failure!);
                Assert.True(rig.Source.Completion.Task.IsFaulted);Assert.False(rig.Source.Completion.Task.IsCanceled);
            }
            Assert.NotNull(await Capture(close!));Assert.True(close!.IsFaulted);
            if(mode!=2) {
                Assert.Contains(Graph(close.Exception!),cause=>ReferenceEquals(cause,oce));
                Assert.Contains(Graph(close.Exception!),cause=>ReferenceEquals(cause,io));
            }
        } catch(Exception error) { primary=error; }
        finally { rig.Release();await Finish(rig,[actual,close],expected,primary); }
    }

    [Fact]
    public async Task Stream_retirement_joins_same_raw_Move_and_Dispose_and_preserves_full_dispose_group()
    {
        var rig=new Rig();Task? move=null,dispose=null,close=null;Exception? primary=null;
        var e1=new OperationCanceledException("faulted dispose OCE");var e2=new IOException("dispose sibling");
        IAsyncEnumerator<string>? iterator=null;
        try {
            await rig.Start();var request=new OllamaChatRequest("same",[new("user","unaltered")],EffortLevel.Medium);
            var source=rig.Dispatcher.GetOriginalRequestSource(rig.Endpoint.EndpointId);
            var retainedMove=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var caller=new CallerScope { OnRetain=task=> { if(ReferenceEquals(task,rig.Source.Stream.Move.Task)) retainedMove.TrySetResult(); } };
            iterator=source.StreamOriginalAsync(request,null!,Guid.Empty,caller,CancellationToken.None).GetAsyncEnumerator();
            move=iterator.MoveNextAsync().AsTask();
            await retainedMove.Task.WaitAsync(TimeSpan.FromSeconds(5),CancellationToken.None);
            Assert.Same(request,rig.Source.Chat);Assert.Contains(rig.Source.Stream.Move.Task,caller.Tasks);Assert.False(move.IsCompleted);
            close=rig.Dispatcher.DisposeAsync().AsTask();Assert.False(close.IsCompleted);
            rig.Source.Stream.Dispose.TrySetException([e1,e2]);rig.Source.Stream.Move.TrySetResult(false);
            Assert.NotNull(await Capture(move));Assert.True(move.IsFaulted);
            dispose=iterator.DisposeAsync().AsTask();await Capture(dispose);
            Assert.NotNull(await Capture(close));Assert.True(close.IsFaulted);
            Assert.Equal(1,rig.Source.Stream.DisposeCalls);Assert.Contains(rig.Source.Stream.Dispose.Task,caller.Tasks);
            Assert.Contains(Graph(close.Exception!),cause=>ReferenceEquals(cause,e1));
            Assert.Contains(Graph(close.Exception!),cause=>ReferenceEquals(cause,e2));
        } catch(Exception error) { primary=error; }
        finally {
            rig.Release();if(iterator is not null&&dispose is null) try { dispose=iterator.DisposeAsync().AsTask(); } catch(Exception error) { primary=primary is null?error:new AggregateException(primary,error); }
            await Finish(rig,[move,dispose,close],[e1,e2],primary);
        }
    }

    private static async Task<Exception?> Capture(Task task)
    { try { await task;return null; } catch(Exception cause) { return cause; } }
    private static IEnumerable<Exception> Graph(Exception cause)
    { yield return cause;if(cause is AggregateException group) foreach(var child in group.InnerExceptions) foreach(var item in Graph(child)) yield return item;else if(cause.InnerException is { } inner) foreach(var item in Graph(inner)) yield return item; }
    private static async Task Finish(Rig rig,IEnumerable<Task?> originals,IEnumerable<Exception> expected,Exception? primary)
    {
        var errors=new List<Exception>();
        foreach(var original in originals.Where(task=>task is not null).Cast<Task>().Distinct()) {
            var cause=await Capture(original);if(cause is not null) errors.Add(cause);if(original.Exception is { } group) errors.AddRange(group.InnerExceptions);
        }
        Task? close=null;try { close=rig.Dispatcher.DisposeAsync().AsTask(); } catch(Exception cause) { errors.Add(cause); }
        if(close is not null) { var cause=await Capture(close);if(cause is not null) errors.Add(cause);if(close.Exception is { } group) errors.AddRange(group.InnerExceptions); }
        var allowed=expected.ToHashSet<Exception>(ReferenceEqualityComparer.Instance);
        bool Known(Exception cause)=>allowed.Contains(cause)||cause is AggregateException group&&group.InnerExceptions.All(Known);
        errors=errors.Where(cause=>!Known(cause)).Distinct<Exception>(ReferenceEqualityComparer.Instance).ToList();
        if(primary is not null) errors.Insert(0,primary);
        if(errors.Count==1) ExceptionDispatchInfo.Capture(errors[0]).Throw();if(errors.Count>1) throw new AggregateException(errors);
    }
    private sealed class CallerScope(int mode=-1):IInferenceEngineOriginalSourceScope
    {
        public List<Task> Tasks {get;}=[];public Action? Deferred;public Exception? Failure;public Action<Task>? OnRetain;
        public T InvokeOriginalFactory<T>(Func<T> factory) {
            if(mode==2) { Deferred=()=>{ _=factory(); };return default!; }
            var value=factory();
            if(mode==0) { Failure=new InvalidOperationException("actual post-callback scope failure");throw Failure; }
            if(mode==1) try { _=factory(); } catch(Exception cause) { Failure=cause;throw; }
            return value;
        }
        public T InvokeOriginalCleanup<T>(Func<T> cleanup)=>cleanup();
        public void RetainOriginalTask(Task task) { if(!Tasks.Contains(task)) Tasks.Add(task);OnRetain?.Invoke(task); }
    }
    private sealed class Rig
    {
        public ModelIdentity Model {get;}=new("source","same-model","same-artifact");
        public DulcheEndpoint Endpoint {get;}
        public RawSource Source {get;}=new();public Factory Factory {get;}
        public InferenceEngineDispatcher Dispatcher {get;}
        public Rig() {
            Endpoint=new("same-endpoint","source","http://127.0.0.1:12345",12345,EndpointState.Starting,Model,new HashSet<string>{"chat","streaming"},false,DateTimeOffset.UtcNow);
            Factory=new(Source);var requirements=new InferenceModelRequirements(Model,"artifact","architecture","family","GGUF","Q4",new HashSet<string>{"streaming"},0,0,2048);
            var hardware=new InferenceHardware("synthetic-hardware","Linux","x64",4096,[],new HashSet<string>());
            var support=new InferenceEngineSupport(InferenceEngine.LlamaCpp,"synthetic-source",true,null,new HashSet<string>{"architecture"},new HashSet<string>{"family"},new HashSet<string>{"GGUF"},new HashSet<string>{"Q4"},new HashSet<string>{"streaming"},new HashSet<string>{"Linux"},new HashSet<string>{"x64"},new HashSet<string>());
            Dispatcher=new("source",new Uri(Endpoint.Target),[new(InferenceEngine.LlamaCpp,Factory)],new Observations(new(requirements,hardware,[support])));
        }
        public async Task Start() { Assert.True((await Dispatcher.StartAsync(Endpoint,CancellationToken.None)).Succeeded); }
        public void Release() { Source.Completion.TrySetResult("released");Source.Stream.Move.TrySetResult(false);Source.Stream.Dispose.TrySetResult(); }
    }
    private sealed class Observations(InferenceRuntimeObservation observation):IInferenceRuntimeObservationSource
    { public Task<InferenceRuntimeObservation> ObserveOriginalAsync(ModelIdentity model,IInferenceEngineOriginalSourceScope scope,CancellationToken token)=>Task.FromResult(observation); }
    private sealed class Factory(RawSource source):IInferenceEngineAdapterFactory
    {
        public int Created;
        public Task<OriginalInferenceEngineLease> CreateOriginalAsync(ModelIdentity model,IInferenceEngineOriginalSourceScope scope,CancellationToken token)
        { Created++;return Task.FromResult<OriginalInferenceEngineLease>(new Lease(source)); }
    }
    private sealed class Lease(RawSource source):OriginalInferenceEngineLease,IOriginalInferenceEngineRequestLease
    {
        public override IDulcheOriginalProviderAdapter Adapter {get;}=new Adapter();
        public IOriginalInferenceEngineRequestSource OriginalRequestSource=>source;
        public override void DemandExternalOriginalJoin() { }
        public override Task CloseOriginalAsync()=>Task.CompletedTask;
    }
    private sealed class RawSource:IOriginalInferenceEngineRequestSource
    {
        public TaskCompletionSource<string> Completion {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StreamEntered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public OllamaChatRequest? Chat;public OllamaToolRequest? Tools;public int CompleteCalls,ToolCalls;
        public OllamaToolResponse ToolResult {get;}=new("existing tool owner response",[]);public RawStream Stream {get;}=new();
        public Task<string> CompleteOriginalAsync(OllamaChatRequest request,TaskRunAttemptAdmission admission,Guid action,IInferenceEngineOriginalSourceScope scope,CancellationToken token)
        { CompleteCalls++;Chat=request;Entered.TrySetResult();return Completion.Task; }
        public Task<OllamaToolResponse> ToolsOriginalAsync(OllamaToolRequest request,TaskRunAttemptAdmission admission,Guid action,IInferenceEngineOriginalSourceScope scope,CancellationToken token)
        { ToolCalls++;Tools=request;return Task.FromResult(ToolResult); }
        public IAsyncEnumerable<string> StreamOriginalAsync(OllamaChatRequest request,TaskRunAttemptAdmission admission,Guid action,IInferenceEngineOriginalSourceScope scope,CancellationToken token)
        { Chat=request;StreamEntered.TrySetResult();return Stream; }
    }
    private sealed class RawStream:IAsyncEnumerable<string>,IAsyncEnumerator<string>
    {
        public TaskCompletionSource<bool> Move {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Dispose {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public int DisposeCalls;
        public string Current=>"same stream text";
        public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken token=default)=>this;
        public ValueTask<bool> MoveNextAsync()=>new(Move.Task);
        public ValueTask DisposeAsync() { DisposeCalls++;return new(Dispose.Task); }
    }
    private sealed class Adapter:IDulcheOriginalProviderAdapter
    {
        public string ProviderId=>"source";public string RuntimeVersion=>"synthetic";public bool IsLocal=>true;
        public Uri OriginalConfiguredTarget=>new("http://127.0.0.1:12345");public IReadOnlySet<string> Capabilities {get;}=new HashSet<string>{"chat","streaming"};
        public Task BindOriginalRequestAsync(DulcheEndpoint endpoint,DulcheRequest request,RuntimeRequestHandle handle,TaskRunAttemptAdmission admission,Guid action,CancellationToken token)=>throw new NotSupportedException();
        public void CaptureOriginalDispatchRequest(RuntimeRequestHandle handle,DulcheRequest frozen,DulcheRequest dispatch)=>throw new NotSupportedException();
        public ValueTask<OperationResult<Unit>> StartAsync(DulcheEndpoint endpoint,CancellationToken token)=>ValueTask.FromResult(OperationResult<Unit>.Success(Unit.Value));
        public ValueTask<OperationResult<Unit>> LoadModelAsync(DulcheEndpoint endpoint,ModelIdentity model,CancellationToken token)=>ValueTask.FromResult(OperationResult<Unit>.Success(Unit.Value));
        public IAsyncEnumerable<AdapterDelta> GenerateAsync(DulcheEndpoint endpoint,DulcheRequest request,string id,CancellationToken token)=>throw new NotSupportedException();
        public IAsyncEnumerable<AdapterDelta> ContinueWithToolResultAsync(DulcheEndpoint endpoint,DulcheRequest request,string id,ToolInvocationResult result,CancellationToken token)=>throw new NotSupportedException();
        public ValueTask<OperationResult<Unit>> CancelAsync(DulcheEndpoint endpoint,string id,CancellationToken token)=>throw new NotSupportedException();
        public ValueTask<OperationResult<Unit>> PauseAsync(DulcheEndpoint endpoint,string id,CancellationToken token)=>throw new NotSupportedException();
        public ValueTask<OperationResult<Unit>> ResumeAsync(DulcheEndpoint endpoint,string id,CancellationToken token)=>throw new NotSupportedException();
        public ValueTask<OperationResult<Unit>> StopAsync(DulcheEndpoint endpoint,CancellationToken token)=>ValueTask.FromResult(OperationResult<Unit>.Success(Unit.Value));
        public ValueTask<RuntimeHealth> HealthAsync(DulcheEndpoint endpoint,CancellationToken token)=>throw new NotSupportedException();
    }
}
