using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using Haven.Application;
using Haven.Core;

namespace NineToOne.Web.Spaces.Storage;

/// <summary>Read/reopen of the same durable Task through the actual shared coordinator.
/// Stored state supplies no admission, live Run, provider, checkpoint or permission authority.</summary>
[SupportedOSPlatform("browser")]
public sealed class BrowserStoredTaskReadOwner
{
    private const int MaximumRetainedReads = 256;
    private readonly object _gate;
    private readonly TaskExecutionCoordinator _coordinator;
    private readonly BrowserTaskExecutionTransport _transport;
    private readonly Func<Task>? _publicationSource;
    private readonly List<OriginalRead> _originals = [];
    private readonly AsyncLocal<OriginalRead?> _executing = new();
    [ThreadStatic] private static List<BrowserStoredTaskReadOwner>? _physicalSources;
    private bool _revoked;
    private Task? _close;
    private Exception? _capacityRefusal;

    private sealed class OriginalRead(BrowserStoredTaskReadOwner owner, OriginalRead? parent, CancellationToken caller)
    {
        internal readonly BrowserStoredTaskReadOwner Owner = owner;
        internal readonly OriginalRead? Parent = parent;
        internal readonly CancellationToken Caller = caller;
        internal readonly TaskCompletionSource<TaskExecutionSnapshot?> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task<TaskExecutionSnapshot?> Outer => Completion.Task;
        internal Task<TaskExecutionSnapshot?>? ManagedDriver;
        internal Task? Publisher;
        internal Task<TaskExecutionSnapshot?>? Repository;
        internal Task? PublicationSource;
        internal readonly List<Task<BrowserAuthenticatedStorageReply>> Drivers = [];
    }
    internal sealed record OriginalReadObservation(Task Outer, Task ManagedDriver, Task Publisher, Task? Repository,
        Task? PublicationSource, IReadOnlyList<Task> AuthenticatedDrivers);
    internal IReadOnlyList<OriginalReadObservation> OriginalReads
    {
        get { lock (_gate) return _originals.Select(original => new OriginalReadObservation(original.Outer,
            original.ManagedDriver!, original.Publisher!, original.Repository, original.PublicationSource,
            Array.AsReadOnly(original.Drivers.Cast<Task>().ToArray()))).ToArray(); }
    }
    // The optional source is an internal controlled publication seam only. Production
    // construction supplies none; the SAME driver, gate and retirement guards still apply.
    internal BrowserStoredTaskReadOwner(TaskExecutionCoordinator coordinator, BrowserTaskExecutionTransport sameTransport,
        object? ownerGate = null, Func<Task>? publicationSource = null)
    { _coordinator = coordinator; _transport = sameTransport; _gate = ownerGate ?? new(); _publicationSource = publicationSource; }

    public Task<TaskExecutionSnapshot?> GetAsync(Guid taskId, CancellationToken token)
    {
        if (taskId == Guid.Empty) throw new ArgumentException("Choose the same canonical TaskId.", nameof(taskId));
        return Start(() => _coordinator.GetAsync(taskId, token), token);
    }
    public Task<TaskExecutionSnapshot?> GetByContextAsync(Guid contextId, CancellationToken token)
    {
        if (contextId == Guid.Empty) throw new ArgumentException("Choose the actual canonical ContextId.", nameof(contextId));
        return Start(() => _coordinator.GetByContextAsync(contextId, token), token);
    }
    private Task<TaskExecutionSnapshot?> Start(Func<Task<TaskExecutionSnapshot?>> source, CancellationToken caller)
    {
        var start = new TaskCompletionSource();
        var original = new OriginalRead(this, _executing.Value, caller);
        lock (_gate)
        {
            if (_revoked) throw new ObjectDisposedException(nameof(BrowserStoredTaskReadOwner));
            _transport.DemandPrivateContextCurrent();
            _originals.RemoveAll(row => row.Outer.IsCompletedSuccessfully && row.ManagedDriver is { IsCompletedSuccessfully: true } &&
                row.Publisher is { IsCompletedSuccessfully: true } && row.Repository is { IsCompletedSuccessfully: true } &&
                (row.PublicationSource is null || row.PublicationSource.IsCompletedSuccessfully) &&
                row.Drivers.All(driver => driver.IsCompletedSuccessfully));
            if (_capacityRefusal is not null) throw _capacityRefusal;
            if (_originals.Count >= MaximumRetainedReads)
                throw _capacityRefusal = new InvalidOperationException("Stored Task read capacity requires external retirement.");
            original.ManagedDriver = ReadOriginalAsync(start.Task, original, source);
            original.Publisher = PublishOriginalAsync(start.Task, original);
            _originals.Add(original); // Public completion, managed driver and publisher exist before any source callback.
        }
        start.SetResult();
        return original.Outer;
    }
    private async Task<TaskExecutionSnapshot?> ReadOriginalAsync(Task start, OriginalRead original, Func<Task<TaskExecutionSnapshot?>> source)
    {
        await start;
        var previous = _executing.Value; _executing.Value = original;
        try
        {
            original.Caller.ThrowIfCancellationRequested();
            lock (_gate) if (_revoked) throw new ObjectDisposedException(nameof(BrowserStoredTaskReadOwner));
            var owners = _physicalSources ??= []; owners.Add(this);
            Task<TaskExecutionSnapshot?> actual;
            try
            {
                try { actual = source() ?? throw new InvalidOperationException("No actual coordinator repository Task was returned."); }
                catch (OperationCanceledException fault) { throw new AggregateException("The actual stored Task read source faulted synchronously.", fault); }
                lock (_gate) original.Repository = actual; // Capture before consuming the same Task.
            }
            finally { owners.RemoveAt(owners.Count - 1); }
            TaskExecutionSnapshot? result;
            try { result = await actual; }
            catch (Exception error)
            {
                if (actual.Exception is { } group) throw group; // Faulted OCE/siblings keep their complete original fault state.
                if (actual.IsCanceled) ExceptionDispatchInfo.Capture(error).Throw();
                throw;
            }
            return result; // Private original only; the SAME exposed Task is completed by the guarded publisher.
        }
        finally { _executing.Value = previous; }
    }
    private async Task PublishOriginalAsync(Task start, OriginalRead original)
    {
        await start;
        TaskExecutionSnapshot? result = null;
        Exception? failure = null, publicationFailure = null;
        var cancelled = false; var cancellationToken = default(CancellationToken);
        try { result = await original.ManagedDriver!; }
        catch (Exception error)
        {
            if (original.ManagedDriver!.Exception is { } group) failure = group;
            else if (original.ManagedDriver.IsCanceled)
            { cancelled = true; cancellationToken = error is OperationCanceledException cancelledCause ? cancelledCause.CancellationToken : default; }
            else failure = error;
        }
        // A terminal managed driver has restored its original execution context and has
        // completed all repository/decode cleanup before publication can acquire a source.
        if (failure is null && !cancelled && _publicationSource is { } source)
        {
            var previous = _executing.Value; _executing.Value = original;
            try
            {
                lock (_gate) if (_revoked) throw new ObjectDisposedException(nameof(BrowserStoredTaskReadOwner));
                var owners = _physicalSources ??= []; owners.Add(this);
                Task actual;
                try
                {
                    try { actual = source() ?? throw new InvalidOperationException("No actual publication source Task was returned."); }
                    catch (OperationCanceledException fault) { throw new AggregateException("The publication source faulted synchronously.", fault); }
                    lock (_gate) original.PublicationSource = actual;
                }
                finally { owners.RemoveAt(owners.Count - 1); }
                try { await actual; }
                catch (Exception error)
                {
                    // This stage is after the complete storage read. Even a genuinely
                    // canceled publication source is a retained fault, never prestorage refusal.
                    throw actual.Exception ?? new AggregateException("The original publication source did not complete lawfully.", error);
                }
            }
            catch (Exception error) { publicationFailure = error; failure = error; }
            finally { _executing.Value = previous; }
        }
        lock (_gate)
        {
            if (failure is null && !cancelled)
                try
                {
                    if (_revoked) throw new ObjectDisposedException(nameof(BrowserStoredTaskReadOwner));
                    _transport.DemandPrivateContextCurrent();
                }
                catch (Exception error) { publicationFailure = error; failure = error; }
            // These operations complete the ACTUAL exposed Task under the SAME parent
            // revocation gate. Asynchronous continuations cannot call a source in this lock.
            // All source/decode/context cleanup precedes this completion; no async return
            // is relied upon as the publication fence.
            if (failure is not null) original.Completion.SetException(failure); // Retain even an empty original AggregateException as a fault.
            else if (cancelled) original.Completion.SetCanceled(cancellationToken);
            else original.Completion.SetResult(result);
        }
        // Independently retained publisher failure is terminally joined by the owner.
        if (publicationFailure is not null) throw new AggregateException("Original stored Task publication failed.", publicationFailure);
    }
    // Only the same production ProfileTransport calls this after obtaining its actual
    // authenticated driver. Direct legacy storage use has no stored-read frame to associate.
    internal void RetainAuthenticatedDriver(Task<BrowserAuthenticatedStorageReply> actual)
    {
        for (var frame = _executing.Value; frame is not null; frame = frame.Parent)
            if (ReferenceEquals(frame.Owner, this))
            {
                lock (_gate) if (!frame.Drivers.Any(prior => ReferenceEquals(prior, actual))) frame.Drivers.Add(actual);
                return;
            }
    }
    internal void DemandExternalOriginalRetirementJoin()
    {
        if (_physicalSources?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An actual stored Task read source cannot join its encompassing close.");
        lock (_gate)
            for (var frame = _executing.Value; frame is not null; frame = frame.Parent)
                if (ReferenceEquals(frame.Owner, this) && (!frame.Outer.IsCompleted ||
                    frame.ManagedDriver is { IsCompleted: false } || frame.Publisher is { IsCompleted: false } ||
                    frame.Repository is { IsCompleted: false } || frame.PublicationSource is { IsCompleted: false } ||
                    frame.Drivers.Any(driver => !driver.IsCompleted)))
                    throw new InvalidOperationException("A live stored Task read original must return before its external close.");
    }
    internal void RevokePrivateContext() { lock (_gate) _revoked = true; }
    internal Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        var start = new TaskCompletionSource(); Task actual;
        lock (_gate)
        {
            if (_close is not null) return _close;
            _revoked = true;
            actual = CloseOriginalAsync(start.Task, _originals.ToArray(), _capacityRefusal); _close = actual;
        }
        start.SetResult(); return actual;
    }
    private bool IsProvenExpected(OriginalRead original)
    {
        var outer = original.Outer;
        if (original.Publisher is not { IsCompletedSuccessfully: true } || original.PublicationSource is not null) return false;
        if (original.Repository is null)
            return outer.IsCanceled && original.ManagedDriver is { IsCanceled: true } &&
                original.Caller.IsCancellationRequested && original.Drivers.Count == 0;
        if (original.Drivers.Count != 1) return false;
        var driver = original.Drivers[0];
        var observation = _transport.ExpectedPreStorageRefusals.SingleOrDefault(row => ReferenceEquals(row.OriginalTask, driver));
        if (observation is null) return false;
        if (observation.Kind == BrowserTaskExecutionTransport.ExpectedRefusalKind.CancelledBeforeStorage)
            return outer.IsCanceled && original.ManagedDriver is { IsCanceled: true } && original.Repository.IsCanceled && driver.IsCanceled;
        return observation.Kind == BrowserTaskExecutionTransport.ExpectedRefusalKind.NoCurrentActorBeforeStorage &&
            OnlySameFault(outer, observation.OriginalCause) && OnlySameFault(original.ManagedDriver!, observation.OriginalCause) &&
            OnlySameFault(original.Repository, observation.OriginalCause) &&
            OnlySameFault(driver, observation.OriginalCause);
    }
    private static bool OnlySameFault(Task actual, Exception cause) => actual.IsFaulted && actual.Exception is { } group &&
        group.Flatten().InnerExceptions.Count == 1 && ReferenceEquals(group.Flatten().InnerExceptions[0], cause);
    private async Task CloseOriginalAsync(Task start, OriginalRead[] originals, Exception? capacityRefusal)
    {
        await start;
        var errors = new List<Exception>();
        if (capacityRefusal is not null) Add(errors, capacityRefusal);
        foreach (var original in originals)
        {
            var failures = new List<(Task Original, Exception Caught)>();
            async Task Join(Task? actual)
            {
                if (actual is null) return;
                try { await actual; } catch (Exception error) { failures.Add((actual, error)); }
            }
            await Join(original.Outer); await Join(original.ManagedDriver); await Join(original.Publisher);
            await Join(original.Repository); await Join(original.PublicationSource);
            Task[] drivers; lock (_gate) drivers = original.Drivers.Cast<Task>().ToArray();
            foreach (var driver in drivers) await Join(driver);
            // Every acquired managed, publisher, source and authenticated driver is
            // independently terminal before exact prestorage disposition is considered.
            if (!IsProvenExpected(original))
                foreach (var failure in failures) Capture(errors, failure.Original, failure.Caught);
        }
        if (errors.Count != 0) throw new AggregateException("Original stored Task reads or validation failed.", errors);
    }
    private static void Capture(List<Exception> errors, Task actual, Exception caught)
    {
        if (actual.Exception is { } group) foreach (var cause in group.InnerExceptions) Add(errors, cause);
        else Add(errors, caught);
    }
    private static void Add(List<Exception> errors, Exception cause)
    { if (!errors.Any(prior => ReferenceEquals(prior, cause))) errors.Add(cause); }
}
