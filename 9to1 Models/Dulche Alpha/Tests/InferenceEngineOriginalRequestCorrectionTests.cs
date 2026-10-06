using System.Runtime.ExceptionServices;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Dulche.Tests;

/// <summary>Actual original-task status/value/cancellation controls with synthetic transport;
/// no model, TaskRun, acknowledged response action or native capability is issued here.</summary>
public sealed class InferenceEngineOriginalRequestCorrectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Single_original_join_preserves_true_canceled_and_faulted_OCE_distinctly(bool faulted)
    {
        var cause=new OperationCanceledException("actual raw OCE");
        var raw=faulted?Task.FromException<string>(cause):Task.FromCanceled<string>(new CancellationToken(true));
        var rig=new Rig(raw);var caller=new CallerScope();Task? actual=null,close=null;Exception? primary=null;
        var expected=new List<Exception>();
        try {
            await rig.Start();actual=rig.Dispatcher.GetOriginalRequestSource(rig.Endpoint.EndpointId)
                .CompleteOriginalAsync(new("same",[],EffortLevel.Medium),null!,Guid.Empty,caller,CancellationToken.None);
            var outward=await Capture(actual);Assert.NotNull(outward);expected.Add(outward!);
            Assert.Contains(raw,caller.Tasks);Assert.Equal(faulted,raw.IsFaulted);Assert.Equal(!faulted,raw.IsCanceled);
            Assert.Equal(faulted,actual.IsFaulted);Assert.Equal(!faulted,actual.IsCanceled);
            if(faulted) { Assert.Contains(Graph(actual.Exception!),item=>ReferenceEquals(item,cause));expected.Add(cause); }
            close=rig.Dispatcher.DisposeAsync().AsTask();Assert.NotNull(await Capture(close));Assert.True(close.IsFaulted);
            if(faulted) Assert.Contains(Graph(close.Exception!),item=>ReferenceEquals(item,cause));
            else Assert.Contains(Graph(close.Exception!),item=>item is TaskCanceledException canceled&&ReferenceEquals(canceled.Task,raw));
        } catch(Exception error) { primary=error; }
        finally { rig.Release();await Finish(rig,[actual,close],expected,raw.IsCanceled?raw:null,primary); }
    }

    [Fact]
    public async Task Caller_return_substitution_never_replaces_actual_nested_result_or_stream_Current()
    {
        var rig=new Rig(Task.FromResult("same response"));var caller=new CallerScope { Substitute=true };
        Task? move=null,dispose=null;IAsyncEnumerator<string>? iterator=null;Exception? primary=null;
        try {
            await rig.Start();var source=rig.Dispatcher.GetOriginalRequestSource(rig.Endpoint.EndpointId);
            Assert.Equal("same response",await source.CompleteOriginalAsync(new("same",[],EffortLevel.Medium),null!,Guid.Empty,caller,CancellationToken.None));
            Assert.Equal("actual nested result",rig.Source.Nested);
            iterator=source.StreamOriginalAsync(new("same",[],EffortLevel.Medium),null!,Guid.Empty,caller,CancellationToken.None).GetAsyncEnumerator();
            var first=iterator.MoveNextAsync().AsTask();move=first;Assert.True(await first);
            Assert.Equal("actual unicode € delta",iterator.Current);Assert.NotEqual("caller substitute",iterator.Current);
            var final=iterator.MoveNextAsync().AsTask();move=final;Assert.False(await final);
            dispose=iterator.DisposeAsync().AsTask();await dispose;
            Assert.True(caller.Substitutions>=2);Assert.Equal(1,rig.Source.DisposeCalls);
        } catch(Exception error) { primary=error; }
        finally {
            rig.Release();if(iterator is not null&&dispose is null) try { dispose=iterator.DisposeAsync().AsTask(); } catch(Exception error) { primary=primary is null?error:new AggregateException(primary,error); }
            await Finish(rig,[move,dispose],[],null,primary);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restored_context_raw_token_callback_cannot_retrieve_or_admit_same_dispatcher_close(bool consumerDispose)
    {
        var prior=ExecutionContext.Capture()!;var rig=new Rig(Task.FromResult("same"));rig.Source.Mode=consumerDispose?2:1;
        var retained=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackDone=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? move=null,dispose=null,close=null,callbackClose=null;Exception? primary=null,refusal=null;IAsyncEnumerator<string>? iterator=null;
        rig.Source.OnCancellation=()=>ExecutionContext.Run(prior,unusedState=> {
            try { callbackClose=rig.Dispatcher.DisposeAsync().AsTask(); } catch(Exception error) { refusal=error; }
            finally { callbackDone.TrySetResult(); }
        },null);
        try {
            await rig.Start();var caller=new CallerScope { OnRetain=task=> { if(ReferenceEquals(task,rig.Source.HeldMove.Task)) retained.TrySetResult(); } };
            iterator=rig.Dispatcher.GetOriginalRequestSource(rig.Endpoint.EndpointId)
                .StreamOriginalAsync(new("same",[],EffortLevel.Medium),null!,Guid.Empty,caller,CancellationToken.None).GetAsyncEnumerator();
            var first=iterator.MoveNextAsync().AsTask();move=first;
            if(consumerDispose) Assert.True(await first);
            await retained.Task.WaitAsync(TimeSpan.FromSeconds(5),CancellationToken.None);
            if(consumerDispose) dispose=iterator.DisposeAsync().AsTask();else close=rig.Dispatcher.DisposeAsync().AsTask();
            await callbackDone.Task.WaitAsync(TimeSpan.FromSeconds(5),CancellationToken.None);
            Assert.IsType<InvalidOperationException>(refusal);Assert.Null(callbackClose);
            Assert.False((consumerDispose?dispose:close)!.IsCompleted);Assert.Equal(1,rig.Source.CancellationCallbacks);
            rig.Source.HeldMove.TrySetResult(false);
            if(consumerDispose) await dispose!;else { Assert.False(await first);await close!; }
            Assert.Equal(1,rig.Source.DisposeCalls);
        } catch(Exception error) { primary=error; }
        finally {
            rig.Release();if(iterator is not null&&dispose is null) try { dispose=iterator.DisposeAsync().AsTask(); } catch(Exception error) { primary=primary is null?error:new AggregateException(primary,error); }
            await Finish(rig,[move,dispose,close,callbackClose],[],null,primary);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Entire_caller_scope_post_callback_keeps_actual_dispatcher_physical_owner(bool cleanup)
    {
        var prior=ExecutionContext.Capture()!;var raw=Task.FromResult("same response");var rig=new Rig(raw);
        var attempted=new List<Task>();var refusals=new List<Exception>();Task? actual=null,move=null,dispose=null;
        IAsyncEnumerator<string>? iterator=null;Exception? primary=null;
        var caller=new PostCallbackScope(cleanup,()=>ExecutionContext.Run(prior,unusedState=> {
            try { attempted.Add(rig.Dispatcher.DisposeAsync().AsTask()); } catch(Exception error) { refusals.Add(error); }
        },null));
        try {
            await rig.Start();var source=rig.Dispatcher.GetOriginalRequestSource(rig.Endpoint.EndpointId);
            if(!cleanup) {
                actual=source.CompleteOriginalAsync(new("same",[],EffortLevel.Medium),null!,Guid.Empty,caller,CancellationToken.None);
                Assert.Equal("same response",await (Task<string>)actual);Assert.Contains(raw,caller.Tasks);
            } else {
                iterator=source.StreamOriginalAsync(new("same",[],EffortLevel.Medium),null!,Guid.Empty,caller,CancellationToken.None).GetAsyncEnumerator();
                var first=iterator.MoveNextAsync().AsTask();move=first;Assert.True(await first);
                var last=iterator.MoveNextAsync().AsTask();move=last;Assert.False(await last);
                dispose=iterator.DisposeAsync().AsTask();await dispose;Assert.Equal(1,rig.Source.DisposeCalls);
            }
            Assert.True(refusals.Count>0);foreach(var refusal in refusals) Assert.IsType<InvalidOperationException>(refusal);
            Assert.Empty(attempted);
        } catch(Exception error) { primary=error; }
        finally {
            rig.Release();if(iterator is not null&&dispose is null) try { dispose=iterator.DisposeAsync().AsTask(); }
            catch(Exception error) { primary=primary is null?error:new AggregateException(primary,error); }
            Task?[] originals=[actual,move,dispose];await Finish(rig,originals.Concat<Task?>(attempted),[],null,primary);
        }
    }
    private sealed class PostCallbackScope(bool inspectCleanup,Action afterActual):IInferenceEngineOriginalSourceScope
    {
        public List<Task> Tasks {get;}=[];
        public T InvokeOriginalFactory<T>(Func<T> factory) { var actual=factory();if(!inspectCleanup) afterActual();return actual; }
        public T InvokeOriginalCleanup<T>(Func<T> cleanup) { var actual=cleanup();if(inspectCleanup) afterActual();return actual; }
        public void RetainOriginalTask(Task task) { if(!Tasks.Contains(task)) Tasks.Add(task); }
    }

    private static async Task<Exception?> Capture(Task task)
    { try { await task;return null; } catch(Exception cause) { return cause; } }
    private static IEnumerable<Exception> Graph(Exception cause)
    { yield return cause;if(cause is AggregateException group) foreach(var child in group.InnerExceptions) foreach(var item in Graph(child)) yield return item;else if(cause.InnerException is { } inner) foreach(var item in Graph(inner)) yield return item; }
    private static async Task Finish(Rig rig,IEnumerable<Task?> originals,IEnumerable<Exception> expected,Task? exactCanceledRaw,Exception? primary)
    {
        var errors=new List<Exception>();
        foreach(var task in originals.Where(task=>task is not null).Cast<Task>().Distinct()) {
            var cause=await Capture(task);if(cause is not null) errors.Add(cause);if(task.Exception is { } group) errors.AddRange(group.InnerExceptions);
        }
        Task? close=null;try { close=rig.Dispatcher.DisposeAsync().AsTask(); } catch(Exception cause) { errors.Add(cause); }
        if(close is not null) { var cause=await Capture(close);if(cause is not null) errors.Add(cause);if(close.Exception is { } group) errors.AddRange(group.InnerExceptions); }
        var allowed=expected.SelectMany(Graph).ToHashSet<Exception>(ReferenceEqualityComparer.Instance);
        bool Known(Exception cause)=>allowed.Contains(cause)
            ||exactCanceledRaw?.IsCanceled==true&&cause is TaskCanceledException canceled&&ReferenceEquals(canceled.Task,exactCanceledRaw)
            ||cause is AggregateException group&&group.InnerExceptions.All(Known);
        errors=errors.Where(cause=>!Known(cause)).Distinct<Exception>(ReferenceEqualityComparer.Instance).ToList();
        if(primary is not null) errors.Insert(0,primary);
        if(errors.Count==1) ExceptionDispatchInfo.Capture(errors[0]).Throw();if(errors.Count>1) throw new AggregateException(errors);
    }
    private sealed class CallerScope:IInferenceEngineOriginalSourceScope
    {
        public bool Substitute;public int Substitutions;public List<Task> Tasks {get;}=[];public Action<Task>? OnRetain;
        public T InvokeOriginalFactory<T>(Func<T> factory) { var actual=factory();if(Substitute&&actual is string) { Substitutions++;return (T)(object)"caller substitute"; }return actual; }
        public T InvokeOriginalCleanup<T>(Func<T> cleanup)=>cleanup();
        public void RetainOriginalTask(Task task) { if(!Tasks.Contains(task)) Tasks.Add(task);OnRetain?.Invoke(task); }
    }
    private sealed class Rig
    {
        public ModelIdentity Model {get;}=new("source","same","same-artifact");public DulcheEndpoint Endpoint {get;}
        public RawSource Source {get;}public InferenceEngineDispatcher Dispatcher {get;}
        public Rig(Task<string> actual) {
            Source=new(actual);Endpoint=new("same-endpoint","source","http://127.0.0.1:12345",12345,EndpointState.Starting,Model,new HashSet<string>{"chat","streaming"},false,DateTimeOffset.UtcNow);
            var model=new InferenceModelRequirements(Model,"artifact","architecture","family","GGUF","Q4",new HashSet<string>(),0,0,2048);
            var hardware=new InferenceHardware("synthetic","Linux","x64",4096,[],new HashSet<string>());
            var support=new InferenceEngineSupport(InferenceEngine.LlamaCpp,"synthetic",true,null,new HashSet<string>{"architecture"},new HashSet<string>{"family"},new HashSet<string>{"GGUF"},new HashSet<string>{"Q4"},new HashSet<string>(),new HashSet<string>{"Linux"},new HashSet<string>{"x64"},new HashSet<string>());
            Dispatcher=new("source",new Uri(Endpoint.Target),[new(InferenceEngine.LlamaCpp,new Factory(Source))],new Observations(new(model,hardware,[support])));
        }
        public async Task Start() { Assert.True((await Dispatcher.StartAsync(Endpoint,CancellationToken.None)).Succeeded); }
        public void Release()=>Source.HeldMove.TrySetResult(false);
    }
    private sealed class Observations(InferenceRuntimeObservation value):IInferenceRuntimeObservationSource
    { public Task<InferenceRuntimeObservation> ObserveOriginalAsync(ModelIdentity model,IInferenceEngineOriginalSourceScope scope,CancellationToken token)=>Task.FromResult(value); }
    private sealed class Factory(RawSource source):IInferenceEngineAdapterFactory
    { public Task<OriginalInferenceEngineLease> CreateOriginalAsync(ModelIdentity model,IInferenceEngineOriginalSourceScope scope,CancellationToken token)=>Task.FromResult<OriginalInferenceEngineLease>(new Lease(source)); }
    private sealed class Lease(RawSource source):OriginalInferenceEngineLease,IOriginalInferenceEngineRequestLease
    {
        // Existing controlled adapter is reused through reflection only as a synthetic transport;
        // no actual model/admission/authority or private native witness is constructed.
        public override IDulcheOriginalProviderAdapter Adapter {get;}=(IDulcheOriginalProviderAdapter)Activator.CreateInstance(
            typeof(InferenceEngineOriginalRequestTests).GetNestedType("Adapter",System.Reflection.BindingFlags.NonPublic)!,true)!;
        public IOriginalInferenceEngineRequestSource OriginalRequestSource=>source;
        public override void DemandExternalOriginalJoin() { }
        public override Task CloseOriginalAsync()=>Task.CompletedTask;
    }
    private sealed class RawSource(Task<string> actual):IOriginalInferenceEngineRequestSource,IAsyncEnumerable<string>,IAsyncEnumerator<string>
    {
        private CancellationTokenRegistration _registration;private int _moves;
        public TaskCompletionSource<bool> HeldMove {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Mode,DisposeCalls,CancellationCallbacks;public Action? OnCancellation;public string? Nested;
        public Task<string> CompleteOriginalAsync(OllamaChatRequest request,TaskRunAttemptAdmission admission,Guid action,IInferenceEngineOriginalSourceScope scope,CancellationToken token)
        { Nested=scope.InvokeOriginalFactory(()=>"actual nested result");return actual; }
        public Task<OllamaToolResponse> ToolsOriginalAsync(OllamaToolRequest request,TaskRunAttemptAdmission admission,Guid action,IInferenceEngineOriginalSourceScope scope,CancellationToken token)=>throw new NotSupportedException();
        public IAsyncEnumerable<string> StreamOriginalAsync(OllamaChatRequest request,TaskRunAttemptAdmission admission,Guid action,IInferenceEngineOriginalSourceScope scope,CancellationToken token)
        { _registration=token.Register(()=> { CancellationCallbacks++;OnCancellation?.Invoke(); });return this; }
        public string Current=>"actual unicode € delta";
        public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken token=default)=>this;
        public ValueTask<bool> MoveNextAsync() { var index=_moves++;return Mode==1||Mode==2&&index>0?new(HeldMove.Task):ValueTask.FromResult(index==0); }
        public ValueTask DisposeAsync() { DisposeCalls++;_registration.Dispose();return ValueTask.CompletedTask; }
    }
}
