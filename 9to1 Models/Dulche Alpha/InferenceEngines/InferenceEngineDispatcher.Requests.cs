using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Haven.Application;

namespace Dulche.Runtime;

public sealed partial class InferenceEngineDispatcher
{
    /// <summary>Observation of the actual selected endpoint lease. The returned source is bound
    /// to that exact lease; it never selects an endpoint by an untrusted wire model alias.</summary>
    public IOriginalInferenceEngineRequestSource GetOriginalRequestSource(string originalEndpointId)
    {
        Endpoint owner;OriginalInferenceEngineLease lease;ModelIdentity model;
        lock(_gate) {
            owner=Required(originalEndpointId);
            if(_sealed||owner.Sealed||owner.Initializing||owner.CurrentLease is null||owner.Selected is null||owner.Diagnostic.Model is null)
                throw new InvalidOperationException("No current original selected engine transport is available.");
            lease=owner.CurrentLease;model=owner.Diagnostic.Model;
        }
        if(lease is not IOriginalInferenceEngineRequestLease requestLease)
            throw new NotSupportedException("The actual selected engine has no original raw request transport.");
        var source=Physical(()=>requestLease.OriginalRequestSource)
            ??throw new InvalidOperationException("The actual engine returned no original request source.");
        DemandCurrentRequestBinding(owner,lease,model);
        return new BoundOriginalRequests(this,owner,lease,model,source);
    }

    private void DemandCurrentRequestBinding(Endpoint owner,OriginalInferenceEngineLease lease,ModelIdentity model)
    {
        lock(_gate) if(_sealed||owner.Sealed||owner.Initializing||!ReferenceEquals(owner.CurrentLease,lease)
            ||owner.Selected is null||owner.Diagnostic.Model!=model)
            throw new InvalidOperationException("The same original selected engine/model binding is no longer current.");
    }

    private sealed class BoundOriginalRequests(InferenceEngineDispatcher parent,Endpoint owner,
        OriginalInferenceEngineLease lease,ModelIdentity model,IOriginalInferenceEngineRequestSource source):IOriginalInferenceEngineRequestSource
    {
        public Task<string> CompleteOriginalAsync(OllamaChatRequest request,TaskRunAttemptAdmission admission,Guid action,
            IInferenceEngineOriginalSourceScope caller,CancellationToken token)
            =>parent.Publish(owner,actual=>Call(actual,caller,token,(scope,linked)=>source.CompleteOriginalAsync(request,admission,action,scope,linked)));
        public Task<OllamaToolResponse> ToolsOriginalAsync(OllamaToolRequest request,TaskRunAttemptAdmission admission,Guid action,
            IInferenceEngineOriginalSourceScope caller,CancellationToken token)
            =>parent.Publish(owner,actual=>Call(actual,caller,token,(scope,linked)=>source.ToolsOriginalAsync(request,admission,action,scope,linked)));

        private async Task<T> Call<T>(Endpoint actual,IInferenceEngineOriginalSourceScope caller,CancellationToken token,
            Func<RequestOriginalScope,CancellationToken,Task<T>> factory)
        {
            var errors=new List<Exception>();var onlyCanceledOriginals=true;var joined=new HashSet<Task>(ReferenceEqualityComparer.Instance);CancellationTokenSource? linked=null;Task<T>? raw=null;
            var own=new SourceScope(parent,actual,parent._executing.Value??throw new InvalidOperationException("No original raw request driver."));
            var scope=new RequestOriginalScope(parent,actual,lease,model,own,caller,token);
            try {
                parent.DemandCurrentRequestBinding(actual,lease,model);token.ThrowIfCancellationRequested();
                linked=CancellationTokenSource.CreateLinkedTokenSource(token,actual.Retirement.Token);
                try { scope.InvokeOriginalFactory(()=> {
                    raw=factory(scope,linked.Token)??throw new InvalidOperationException("The original raw provider returned no task.");
                    scope.RetainOriginalTask(raw);return raw;
                }); }
                catch(Exception error) { onlyCanceledOriginals=false;Add(errors,error); }
                if(raw is not null) onlyCanceledOriginals&=await JoinOnce(raw,joined,errors).ConfigureAwait(false);
                else if(errors.Count==0) { onlyCanceledOriginals=false;Add(errors,new InvalidOperationException("No original raw request was acquired.")); }
            }
            catch(Exception error) { onlyCanceledOriginals=false;Add(errors,error); }
            finally {
                // Each actual child is retained before scope exit, including a task returned before
                // a later caller-scope fault. Join independently after the encompassing raw call.
                foreach(var task in scope.SnapshotOriginals()) onlyCanceledOriginals&=await JoinOnce(task,joined,errors,scope.IsOriginalCleanupTask(task)).ConfigureAwait(false);
                if(linked is not null) try { linked.Dispose(); } catch(Exception error) { onlyCanceledOriginals=false;Add(errors,error); }
            }
            ThrowRequestOutcomes(errors,onlyCanceledOriginals);return raw!.Result;
        }

        public IAsyncEnumerable<string> StreamOriginalAsync(OllamaChatRequest request,TaskRunAttemptAdmission admission,Guid action,
            IInferenceEngineOriginalSourceScope caller,CancellationToken token)=>Stream(request,admission,action,caller,token);
        private async IAsyncEnumerable<string> Stream(OllamaChatRequest request,TaskRunAttemptAdmission admission,Guid action,
            IInferenceEngineOriginalSourceScope caller,[EnumeratorCancellation] CancellationToken token)
        {
            var output=Channel.CreateBounded<string>(new BoundedChannelOptions(8) { SingleWriter=true,SingleReader=true,FullMode=BoundedChannelFullMode.Wait });
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,owner.Retirement.Token);
            // A real source producer is published before any external enumerable/Move factory.
            // The existing caller owns its outer Move/Dispose; this source owns the actual inner
            // stream, producer, raw Move/Dispose and every finite acquisition task.
            var producer=parent.Publish(owner,actual=>Produce(actual,request,admission,action,caller,linked.Token,output));
            try { await foreach(var text in output.Reader.ReadAllAsync(token).ConfigureAwait(false)) yield return text; }
            finally {
                var errors=new List<Exception>();
                try { parent.Physical(()=> { linked.Cancel();return Unit.Value; }); } catch(Exception error) { Add(errors,error); }
                await Join(producer,errors).ConfigureAwait(false);Throw(errors);
            }
        }

        private async Task<Unit> Produce(Endpoint actual,OllamaChatRequest request,TaskRunAttemptAdmission admission,Guid action,
            IInferenceEngineOriginalSourceScope caller,CancellationToken token,Channel<string> output)
        {
            var errors=new List<Exception>();var onlyCanceledOriginals=true;var joined=new HashSet<Task>(ReferenceEqualityComparer.Instance);IAsyncEnumerator<string>? iterator=null;
            var own=new SourceScope(parent,actual,parent._executing.Value??throw new InvalidOperationException("No original stream driver."));
            var scope=new RequestOriginalScope(parent,actual,lease,model,own,caller,token);
            try {
                parent.DemandCurrentRequestBinding(actual,lease,model);token.ThrowIfCancellationRequested();
                scope.InvokeOriginalFactory(()=> {
                    iterator=source.StreamOriginalAsync(request,admission,action,scope,token).GetAsyncEnumerator(token);
                    return iterator;
                });
                while(true) {
                    Task<bool>? move=null;Exception? direct=null;
                    try { scope.InvokeOriginalFactory(()=> { move=iterator!.MoveNextAsync().AsTask();scope.RetainOriginalTask(move);return move; }); }
                    catch(Exception error) { direct=error;onlyCanceledOriginals=false;Add(errors,error); }
                    if(move is not null) onlyCanceledOriginals&=await JoinOnce(move,joined,errors).ConfigureAwait(false);
                    if(direct is not null||errors.Count!=0) break;
                    if(move is null) throw new InvalidOperationException("No original raw stream Move was acquired.");
                    if(!move.Result) break;
                    var text=scope.InvokeOriginalFactory(()=>iterator!.Current);
                    await output.Writer.WriteAsync(text,token).ConfigureAwait(false);
                }
            }
            catch(Exception error) { onlyCanceledOriginals=false;Add(errors,error); }
            finally {
                if(iterator is not null) {
                    Task? dispose=null;
                    try { scope.InvokeOriginalCleanup(()=> { dispose=iterator.DisposeAsync().AsTask();scope.RetainOriginalTask(dispose);return dispose; }); }
                    catch(Exception error) { onlyCanceledOriginals=false;Add(errors,error); }
                    if(dispose is not null) onlyCanceledOriginals&=await JoinOnce(dispose,joined,errors,cleanup:true).ConfigureAwait(false);
                }
                foreach(var task in scope.SnapshotOriginals()) onlyCanceledOriginals&=await JoinOnce(task,joined,errors,scope.IsOriginalCleanupTask(task)).ConfigureAwait(false);
                output.Writer.TryComplete(errors.Count==0?null:errors.Count==1?errors[0]:new AggregateException(errors));
            }
            ThrowRequestOutcomes(errors,onlyCanceledOriginals);return Unit.Value;
        }
    }

    private static async Task<bool> JoinOnce(Task task,HashSet<Task> joined,List<Exception> errors,bool cleanup=false)
    {
        if(joined.Add(task)) await Join(task,errors).ConfigureAwait(false);
        // Productive cancellation requires this SAME observed canceled original. Cleanup must
        // succeed, including a nested cleanup child already joined through a productive driver.
        return task.IsCompletedSuccessfully||!cleanup&&task.IsCanceled;
    }
    private static void ThrowRequestOutcomes(List<Exception> errors,bool onlyCanceledOriginals)
    {
        if(errors.Count>1&&onlyCanceledOriginals)
            throw new OperationCanceledException("Every failed original request task was actually canceled.",
                new AggregateException("All original cancellation causes remain retained.",errors));
        if(!onlyCanceledOriginals&&errors.Count==1&&errors[0] is OperationCanceledException)
            throw new AggregateException("An original request fault or unsuccessful cleanup remains a fault.",errors);
        Throw(errors);
    }

    /// <summary>Caller and selected-engine scopes are both required. Each external invocation is
    /// synchronous, original-thread and one-use; a delayed/repeated callback cannot acquire work.</summary>
    private sealed class RequestOriginalScope(InferenceEngineDispatcher parent,Endpoint owner,
        OriginalInferenceEngineLease lease,ModelIdentity model,SourceScope own,IInferenceEngineOriginalSourceScope caller,CancellationToken cancellationToken):IInferenceEngineOriginalSourceScope
    {
        private readonly object _tasksGate=new();private readonly List<Task> _tasks=[];
        private readonly HashSet<Task> _cleanupTasks=new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<int,int> _cleanupDepth=[];
        public T InvokeOriginalFactory<T>(Func<T> factory)=>Invoke(factory,false);
        public T InvokeOriginalCleanup<T>(Func<T> factory)=>Invoke(factory,true);
        private T Invoke<T>(Func<T> factory,bool cleanup)
        {
            var thread=Environment.CurrentManagedThreadId;var active=1;var once=0;T captured=default!;
            T Actual() {
                if(Volatile.Read(ref active)!=1||Environment.CurrentManagedThreadId!=thread||Interlocked.Exchange(ref once,1)!=0)
                    throw new InvalidOperationException("Original request callbacks require the same live synchronous thread and one use.");
                T Own() {
                    if(!cleanup) { parent.DemandCurrentRequestBinding(owner,lease,model);cancellationToken.ThrowIfCancellationRequested(); }
                    lock(_tasksGate) if(!cleanup&&_tasks.Count>=131072)
                        throw new InvalidOperationException("Original request child custody is full before factory admission.");
                    if(cleanup) lock(_tasksGate) _cleanupDepth[thread]=_cleanupDepth.GetValueOrDefault(thread)+1;
                    try { var value=factory();if(value is Task task) RetainOriginalTask(task);return value; }
                    finally {
                        if(cleanup) lock(_tasksGate) {
                            var remaining=_cleanupDepth[thread]-1;
                            if(remaining==0) _cleanupDepth.Remove(thread);else _cleanupDepth[thread]=remaining;
                        }
                    }
                }
                captured=cleanup?own.InvokeOriginalCleanup(Own):own.InvokeOriginalFactory(Own);return captured;
            }
            try {
                _=parent.Physical(()=>cleanup?caller.InvokeOriginalCleanup(Actual):caller.InvokeOriginalFactory(Actual));
                if(Volatile.Read(ref once)!=1) throw new InvalidOperationException("The actual original request callback was not invoked.");
                return captured;
            }
            catch(OperationCanceledException cause) { throw new AggregateException("The original raw request callback faulted synchronously.",cause); }
            finally { Volatile.Write(ref active,0); }
        }
        public void RetainOriginalTask(Task task)
        {
            ArgumentNullException.ThrowIfNull(task);
            own.RetainOriginalTask(task);
            lock(_tasksGate) {
                if(!_tasks.Contains(task,ReferenceEqualityComparer.Instance)) _tasks.Add(task);
                if(_cleanupDepth.GetValueOrDefault(Environment.CurrentManagedThreadId)>0) _cleanupTasks.Add(task);
                if(_tasks.Count>139264) // Already acquired children stay retained even on refusal.
                    throw new InvalidOperationException("Original request child cleanup custody is full.");
            }
            caller.RetainOriginalTask(task);
        }
        public bool IsOriginalCleanupTask(Task task) { lock(_tasksGate) return _cleanupTasks.Contains(task); }
        public Task[] SnapshotOriginals() { lock(_tasksGate) return _tasks.ToArray(); }
    }
}
