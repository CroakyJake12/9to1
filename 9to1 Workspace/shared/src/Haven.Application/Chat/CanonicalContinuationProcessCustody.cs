using System.Runtime.ExceptionServices;

namespace Haven.Application;

/// <summary>Outer enumerable custody only; implementing this internal port issues no canonical authority.</summary>
internal interface ICanonicalProcessEnumerableCustody
{
    bool HasOwnedTerminalObservation { get; }
    void RetainFailure(Exception cause);
    void RetainSource(string stage, Task actual);
    void MarkRetirementRequested();
    void DemandExternalJoin();
}

internal sealed class TaskRunInvocationProcessCustody(TaskRunInvocationCustody original) : ICanonicalProcessEnumerableCustody
{
    public bool HasOwnedTerminalObservation => original.OwnedCleanupTerminal
        && (original.OriginalBinding is null || original.OriginalTerminalObservation is not null);
    public void RetainFailure(Exception cause) => original.Retain(cause);
    public void RetainSource(string stage, Task actual) => original.RetainAdditionalOriginal(stage, actual);
    public void MarkRetirementRequested() => original.OriginalProcessRetirementRequested = true;
    public void DemandExternalJoin() => original.OriginalTracker?.DemandExternalOriginalProcessJoin();
}

/// <summary>Actual preparatory iterator sources and same privately claimed child. No fake Begin/Task or public-ID witness.</summary>
internal sealed class CanonicalContinuationProcessCustody(TaskExecutionCoordinator owner) : ICanonicalProcessEnumerableCustody
{
    private readonly object _gate = new();
    private readonly List<(string Stage, Task Actual)> _sources = [];
    private readonly List<Exception> _causes = [];
    private bool _fault;
    internal TaskRunUnstartedContinuationBinding? OriginalPreparation;
    internal TaskRunColdContinuationBinding? OriginalColdPreparation;
    internal IAsyncEnumerable<ChatStreamEvent>? OriginalChild;
    internal Task? OriginalChildDispose;
    internal bool IteratorClosed;
    internal bool RetirementRequested;
    public bool HasOwnedTerminalObservation => IteratorClosed && OriginalColdPreparation is { BodyBound: true, JournalTerminalAcknowledged: true } cold
        && ReferenceEquals(OriginalChild, cold.Invocation.OriginalProcessProducer)
        && OriginalChildDispose is { IsCompletedSuccessfully: true }
        && cold.Invocation.OriginalProcessProducer is { HasHealthyClosedOriginal: true }
        || IteratorClosed && OriginalPreparation is { Claimed: true, Bound: true } binding
        && ReferenceEquals(OriginalChild, binding.Next.OriginalProcessProducer)
        && OriginalChildDispose is { IsCompletedSuccessfully: true }
        && binding.Next.OriginalProcessProducer is { HasHealthyClosedOriginal: true };
    public void RetainFailure(Exception cause) { lock (_gate) if (!_causes.Any(prior => ReferenceEquals(prior, cause))) _causes.Add(cause); }
    public void RetainSource(string stage, Task actual) { lock (_gate) _sources.Add((stage, actual)); }
    public void MarkRetirementRequested() => RetirementRequested = true;
    public void DemandExternalJoin() => TaskRunProcessProducerContext.DemandExternalJoin(owner);
    internal T Invoke<T>(Func<T> source, bool owningCleanup = false)
    {
        try
        {
            if (!owningCleanup && owner.HasSealedOriginalProcessProducerAdmission)
                throw new InvalidOperationException("Direct continuation source admission is sealed.");
            return TaskRunProcessProducerContext.Invoke(owner, source);
        }
        catch (Exception cause) { lock (_gate) _fault = true; RetainFailure(cause); ThrowRetained(); throw; }
    }
    internal async Task<T> Await<T>(string stage, Func<Task<T>> source)
    {
        Task<T>? actual = null;
        try
        {
            actual = Invoke(source) ?? throw new InvalidOperationException("No actual direct continuation source Task was returned.");
            RetainSource(stage, actual);
            return await actual.ConfigureAwait(false);
        }
        catch (Exception cause) { Capture(cause, actual); ThrowRetained(); throw; }
    }
    internal async Task Await(string stage, Func<Task> source)
    {
        Task? actual = null;
        try
        {
            actual = Invoke(source) ?? throw new InvalidOperationException("No actual direct continuation source Task was returned.");
            RetainSource(stage, actual);
            await actual.ConfigureAwait(false);
        }
        catch (Exception cause) { Capture(cause, actual); ThrowRetained(); throw; }
    }
    internal async Task DisposeChildAsync(IAsyncEnumerator<ChatStreamEvent> iterator)
    {
        Task? actual = null;
        try
        {
            actual = Invoke(() => iterator.DisposeAsync().AsTask(), owningCleanup: true);
            OriginalChildDispose = actual;
            RetainSource("process.continuation-child-dispose", actual);
            await actual.ConfigureAwait(false);
        }
        catch (Exception cause) { Capture(cause, actual); }
        finally { IteratorClosed = true; }
        ThrowRetained();
    }
    private void Capture(Exception cause, Task? actual)
    {
        lock (_gate)
        {
            if (actual is null || actual.IsFaulted) _fault = true;
            IEnumerable<Exception> errors = actual?.Exception is { } fault ? fault.InnerExceptions : new[] { cause };
            foreach (var error in errors) if (!_causes.Any(prior => ReferenceEquals(prior, error))) _causes.Add(error);
        }
    }
    private void ThrowRetained()
    {
        Exception[] causes; bool fault;
        lock (_gate) { causes = _causes.ToArray(); fault = _fault; }
        if (causes.Length == 0) return;
        if (causes.Length == 1 && (causes[0] is not OperationCanceledException || !fault))
            ExceptionDispatchInfo.Capture(causes[0]).Throw();
        throw new AggregateException("Actual direct continuation source or child cleanup failed.", causes);
    }
}
