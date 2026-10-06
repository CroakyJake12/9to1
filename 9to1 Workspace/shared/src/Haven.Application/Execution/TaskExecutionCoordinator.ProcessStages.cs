using Haven.Core;

namespace Haven.Application;

public sealed partial class TaskExecutionCoordinator
{
    private readonly List<TaskRunProcessStageCustody> _originalProcessStages = [];
    private TaskRunProcessStageCustody[]? _sealedOriginalProcessStages;

    private Task<T> StartOriginalProcessStage<T>(string name, CancellationToken callerToken,
        Func<CancellationToken, Task<T>> body, Action<TaskRunProcessStageCustody>? publish = null)
    {
        TaskCompletionSource start;
        Task<T> actual;
        lock (_processProducerGate)
        {
            if (_processProducerAdmissionSealed) throw new InvalidOperationException("Canonical coordinator source admission is sealed.");
            _originalProcessStages.RemoveAll(static prior => prior.HealthyClosed);
            if (_originalProcessStages.Count >= OriginalInvocationCapacity)
                throw new InvalidOperationException("Finite unresolved coordinator source custody requires original inspection.");
            var original = new TaskRunProcessStageCustody(this, name, callerToken);
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = original.Publish(start.Task, body);
            _originalProcessStages.Add(original);
            publish?.Invoke(original); // Internal metadata only, never external callback/authority under the gate.
        }
        start.SetResult();
        return actual;
    }

    internal IAsyncEnumerable<ChatStreamEvent> RegisterOriginalContinuationProcessProducer(
        CanonicalContinuationProcessCustody custody, Func<CancellationToken, IAsyncEnumerable<ChatStreamEvent>> source,
        CancellationToken callerToken)
    {
        lock (_processProducerGate)
        {
            if (_processProducerAdmissionSealed) throw new InvalidOperationException("Canonical continuation source admission is sealed.");
            _processChatProducers.RemoveAll(static prior => prior.HasHealthyClosedOriginal || prior.HasSuccessfullyResolvedOriginalToolCheckpoint);
            if (_processChatProducers.Count >= OriginalInvocationCapacity)
                throw new InvalidOperationException("Finite actual continuation producer custody requires inspection.");
            var original = new CanonicalChatProcessProducer(this, custody, source, callerToken);
            _processChatProducers.Add(original);
            return original;
        }
    }

    private TaskRunProcessStageCustody RequireOriginalProcessStage() => TaskRunProcessStageCustody.CurrentFor(this)
        ?? throw new InvalidOperationException("No actual coordinator source stage was admitted.");

    private async Task<TaskExecutionSnapshot> RequireOriginalProcessSnapshotAsync(Guid taskId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var original = RequireOriginalProcessStage();
        var result = await original.Await(() => repository.GetAsync(taskId, token)).ConfigureAwait(false);
        return result ?? throw new KeyNotFoundException($"Task '{taskId}' was not found.");
    }

    private async Task ValidateOriginalProcessCommandAsync(TaskExecutionSnapshot snapshot, string command, CancellationToken token) =>
        await RequireOriginalProcessStage().Await(() => ValidateTaskCommandAsync(snapshot, command, token)).ConfigureAwait(false);
    internal T InvokeOriginalProcessInputSource<T>(Func<T> source)
    {
        var stage = TaskRunProcessStageCustody.CurrentFor(this);
        if (stage is not null)
        {
            var returned = stage.Invoke(source);
            if (returned is Task actual) stage.RetainSource(actual);
            return returned;
        }
        if (_processProducerAdmissionSealed)
            throw new InvalidOperationException("Original canonical input source admission is sealed.");
        return TaskRunProcessProducerContext.Invoke(this, source);
    }

}
