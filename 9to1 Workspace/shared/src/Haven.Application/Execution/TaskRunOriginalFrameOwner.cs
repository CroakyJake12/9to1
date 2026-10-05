using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Haven.Core;

namespace Haven.Application;

/// <summary>A live observation of one exact terminal raw provider failure. It is not persisted authority.</summary>
public sealed class TaskRunOriginalFailureObservation
{
    internal TaskRunOriginalFailureObservation(TaskRunAttemptAdmission admission, Task frame, Exception cause)
    {
        TaskId = admission.Snapshot.TaskId;
        ExecutionId = admission.Snapshot.ExecutionId;
        AttemptId = admission.AttemptId;
        OriginalFrame = frame;
        OriginalCause = cause;
    }
    public Guid TaskId { get; }
    public Guid ExecutionId { get; }
    public Guid AttemptId { get; }
    public Task OriginalFrame { get; }
    public Exception OriginalCause { get; }
}

public interface ITaskRunProviderFailureSettlement : ITaskRunRuntimeSettlement
{
    bool ValidateProviderFailureObservation(TaskRunOriginalFailureObservation observation, TaskRunAttemptAdmission originalAdmission);
    void AcknowledgeProviderFailure(TaskRunOriginalFailureObservation observation, TaskRunAttemptAdmission originalAdmission,
        TaskRunFailurePersistenceAcknowledgment acknowledgedFailure);
}

/// <summary>Process-local custody of finite originals. The issuer alone grants access; this service only joins work.</summary>
public interface ITaskRunOriginalFrameOwner : ITaskRunProviderFailureSettlement, ITaskRunOriginalAttemptRetirement, IAsyncDisposable
{
    Task RegisterOriginalAttemptAsync(TaskRunAttemptAdmission originalAdmission, CancellationToken cancellationToken);
    Task<T> StartOriginalFrameAsync<T>(TaskRunAttemptAdmission originalAdmission,
        Func<CancellationToken, Task<T>> rawProviderBody, CancellationToken cancellationToken);
    Task<T> StartOriginalResourceFrameAsync<T, TResource>(TaskRunAttemptAdmission originalAdmission,
        Func<CancellationToken, Task<TResource>> acquireOriginalResource,
        Func<TResource, CancellationToken, Task> revalidateOriginalResource,
        Func<CancellationToken, Task<T>> rawProviderBody, CancellationToken cancellationToken) where TResource : class, IAsyncDisposable;
    Task<T> StartOriginalResourceFrameAsync<T, TResource>(TaskRunAttemptAdmission originalAdmission,
        Func<CancellationToken, Task<TResource>> acquireOriginalResource,
        Func<TResource, CancellationToken, Task> revalidateOriginalResource,
        Func<TResource, CancellationToken, Task<T>> rawProviderBody, CancellationToken cancellationToken) where TResource : class, IAsyncDisposable;
    Task<T> StartOriginalToolFrameAsync<T>(TaskRunAttemptAdmission originalAdmission,
        Func<CancellationToken, Task<T>> originalToolBody, CancellationToken cancellationToken);
    IAsyncEnumerable<T> StreamOriginalFrame<T>(TaskRunAttemptAdmission originalAdmission,
        Func<CancellationToken, IAsyncEnumerable<T>> rawProviderStream, CancellationToken cancellationToken);
    TaskRunOriginalFailureObservation CreateProviderFailureObservation(TaskRunAttemptAdmission originalAdmission,
        Task originalFrame, Exception originalCause);
    TaskRunOriginalFailureObservation? TryObserveProviderFailure(TaskRunAttemptAdmission originalAdmission, Task originalFrame);
    Task CloseAndDrainAsync();
}

/// <summary>
/// Holds one issuer lease per attempt across finite model/tool frames. Register uses the canonical
/// coordinator's actual GetIssuedAttemptAsync, supplied by the composition owner. IDs, copied records,
/// provider output, and acknowledgments cannot mint a lease. Never call settlement from a frame it joins.
/// </summary>
public sealed class TaskRunOriginalFrameOwner : ITaskRunOriginalFrameOwner
{
    private const int Capacity = 128;
    private readonly object _sync = new();
    private readonly Func<Guid, Guid, Guid, CancellationToken, Task<TaskRunAttemptAdmission?>> _getIssued;
    private readonly Dictionary<(Guid Task, Guid Run, Guid Attempt), Attempt> _attempts = new();
    private readonly ConditionalWeakTable<TaskRunOriginalFailureObservation, Observation> _observations = new();
    private readonly AsyncLocal<Attempt?> _executing = new();
    private Task? _close;
    private bool _closing;
    private Exception? _capacityRefusal;

    public TaskRunOriginalFrameOwner(Func<Guid, Guid, Guid, CancellationToken, Task<TaskRunAttemptAdmission?>> getOriginalIssuedAdmission)
        => _getIssued = getOriginalIssuedAdmission ?? throw new ArgumentNullException(nameof(getOriginalIssuedAdmission));

    public Task RegisterOriginalAttemptAsync(TaskRunAttemptAdmission originalAdmission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalAdmission);
        cancellationToken.ThrowIfCancellationRequested();
        TaskCompletionSource gate;
        Attempt attempt;
        lock (_sync)
        {
            RequireOpen();
            var key = Key(originalAdmission);
            if (_attempts.TryGetValue(key, out attempt!))
            {
                RequireSame(attempt, originalAdmission);
                return attempt.Registration!;
            }
            if (_capacityRefusal is not null || _attempts.Count >= Capacity)
                throw _capacityRefusal ??= new InvalidOperationException("Original attempt custody is full; a new owner is required.");
            attempt = new(originalAdmission);
            gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            attempt.Registration = RegisterCoreAsync(attempt, gate.Task);
            _attempts.Add(key, attempt);
        }
        gate.SetResult(); // The SAME returned original is already registered before any lookup callback.
        return attempt.Registration;
    }

    private async Task RegisterCoreAsync(Attempt attempt, Task start)
    {
        await start.ConfigureAwait(false);
        var errors = new List<Exception>();
        try
        {
            attempt.OriginalLookup = _getIssued(attempt.Admission.Snapshot.TaskId, attempt.Admission.Snapshot.ExecutionId,
                attempt.Admission.AttemptId, CancellationToken.None) ?? throw new InvalidOperationException("The issuer returned no original lookup task.");
            var issued = await attempt.OriginalLookup.ConfigureAwait(false);
            if (!ReferenceEquals(issued, attempt.Admission))
                throw new UnauthorizedAccessException("This is not the canonical coordinator's original issued attempt.");
            lock (_sync) attempt.OwnedLease = issued.Lease; // Capture the actual acquired owner before any subsequent metadata callback.
            var owner = issued.Lease.Owner;
            if (owner.TaskId != issued.Snapshot.TaskId || owner.ContextId != issued.Snapshot.ContextId
                || owner.ExecutionId != issued.Snapshot.ExecutionId || issued.Lease.AttemptId != issued.AttemptId)
                throw new UnauthorizedAccessException("The issuer lease does not bind this original task, context and run.");
        }
        catch (Exception error) { AddTask(errors, error, attempt.OriginalLookup); }
        if (errors.Count != 0)
        {
            lock (_sync) foreach (var error in errors) Add(attempt.Errors, error);
            Throw(errors);
        }
    }

    public Task<T> StartOriginalFrameAsync<T>(TaskRunAttemptAdmission originalAdmission,
        Func<CancellationToken, Task<T>> rawProviderBody, CancellationToken cancellationToken)
        => Start(originalAdmission, rawProviderBody, cancellationToken, provider: true);

    public Task<T> StartOriginalResourceFrameAsync<T, TResource>(TaskRunAttemptAdmission originalAdmission,
        Func<CancellationToken, Task<TResource>> acquireOriginalResource,
        Func<TResource, CancellationToken, Task> revalidateOriginalResource,
        Func<CancellationToken, Task<T>> rawProviderBody, CancellationToken cancellationToken) where TResource : class, IAsyncDisposable
    {
        ArgumentNullException.ThrowIfNull(acquireOriginalResource);
        ArgumentNullException.ThrowIfNull(revalidateOriginalResource);
        ArgumentNullException.ThrowIfNull(rawProviderBody);
        return StartOriginalResourceFrameAsync<T, TResource>(originalAdmission, acquireOriginalResource,
            revalidateOriginalResource, (_, token) => rawProviderBody(token), cancellationToken);
    }

    public Task<T> StartOriginalResourceFrameAsync<T, TResource>(TaskRunAttemptAdmission originalAdmission,
        Func<CancellationToken, Task<TResource>> acquireOriginalResource,
        Func<TResource, CancellationToken, Task> revalidateOriginalResource,
        Func<TResource, CancellationToken, Task<T>> rawProviderBody, CancellationToken cancellationToken) where TResource : class, IAsyncDisposable
    {
        ArgumentNullException.ThrowIfNull(acquireOriginalResource);
        ArgumentNullException.ThrowIfNull(revalidateOriginalResource);
        ArgumentNullException.ThrowIfNull(rawProviderBody);
        return StartWithFrame(originalAdmission, (frame, token) => rawProviderBody((TResource)frame.OriginalResource!, token), cancellationToken,
            provider: true, token => acquireOriginalResource(token),
            original => ((Task<TResource>)original).Result,
            (resource, token) => revalidateOriginalResource((TResource)resource, token));
    }

    public Task<T> StartOriginalToolFrameAsync<T>(TaskRunAttemptAdmission originalAdmission,
        Func<CancellationToken, Task<T>> originalToolBody, CancellationToken cancellationToken)
        => Start(originalAdmission, originalToolBody, cancellationToken, provider: false);

    private Task<T> Start<T>(TaskRunAttemptAdmission admission, Func<CancellationToken, Task<T>> body,
        CancellationToken cancellationToken, bool provider)
        => StartWithFrame(admission, (_, token) => body(token), cancellationToken, provider);

    private Task<T> StartWithFrame<T>(TaskRunAttemptAdmission admission, Func<Frame, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken, bool provider,
        Func<CancellationToken, Task>? acquireOriginalResource = null,
        Func<Task, IAsyncDisposable>? originalResourceResult = null,
        Func<IAsyncDisposable, CancellationToken, Task>? revalidateOriginalResource = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        cancellationToken.ThrowIfCancellationRequested();
        TaskCompletionSource gate;
        Frame frame;
        lock (_sync)
        {
            RequireOpen();
            var attempt = RequireRegistered(admission);
            if (attempt.Sealed) throw new InvalidOperationException("The original attempt is sealed.");
            attempt.Frames.RemoveAll(item => item.Original!.IsCompletedSuccessfully && item.Errors.Count == 0 && item.BodyErrors.Count == 0 && item.ResultObservationErrors.Count == 0);
            if (attempt.CapacityRefusal is not null || attempt.Frames.Count >= Capacity)
            {
                var refusal = attempt.CapacityRefusal ??= new InvalidOperationException("Original frame custody is full; no further frame is admitted.");
                Add(attempt.Errors, refusal);
                throw refusal;
            }
            gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            frame = new(attempt, provider, cancellationToken, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, attempt.Lifetime.Token))
            { AcquireOriginalResource = acquireOriginalResource, OriginalResourceResult = originalResourceResult, RevalidateOriginalResource = revalidateOriginalResource };
            frame.Original = RunFrameAsync(frame, gate.Task, body);
            attempt.Frames.Add(frame);
        }
        gate.SetResult();
        return (Task<T>)frame.Original!;
    }

    private async Task<T> RunFrameAsync<T>(Frame frame, Task start, Func<Frame, CancellationToken, Task<T>> body)
    {
        await start.ConfigureAwait(false);
        var old = _executing.Value;
        _executing.Value = frame.Attempt;
        T result = default!;
        try
        {
            await frame.Attempt.Registration!.ConfigureAwait(false);
            lock (_sync) RequireBodyAdmission(frame);
            frame.Revalidation = frame.Attempt.OwnedLease!.RevalidateAsync(frame.Cancellation.Token).AsTask();
            try { await frame.Revalidation.ConfigureAwait(false); }
            catch (Exception error) { Capture(frame, error, frame.Revalidation, providerBody: false); throw; }
            if (frame.AcquireOriginalResource is not null)
            {
                try
                {
                    lock (_sync)
                    {
                        RequireBodyAdmission(frame);
                        frame.OriginalResourceAcquire = frame.AcquireOriginalResource(frame.Cancellation.Token)
                            ?? throw new InvalidOperationException("The resource owner returned no original acquisition task.");
                    }
                    await frame.OriginalResourceAcquire.ConfigureAwait(false);
                    frame.OriginalResource = frame.OriginalResourceResult!(frame.OriginalResourceAcquire)
                        ?? throw new InvalidOperationException("The original resource acquisition returned no resource.");
                    lock (_sync)
                    {
                        RequireBodyAdmission(frame);
                        frame.OriginalResourceRevalidation = frame.RevalidateOriginalResource!(frame.OriginalResource, frame.Cancellation.Token)
                            ?? throw new InvalidOperationException("The resource owner returned no original revalidation task.");
                    }
                    await frame.OriginalResourceRevalidation.ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    Capture(frame, error, frame.OriginalResourceRevalidation ?? frame.OriginalResourceAcquire, providerBody: false);
                    throw;
                }
            }
            try
            {
                Task<T> originalBody;
                lock (_sync)
                {
                    RequireBodyAdmission(frame);
                    originalBody = body(frame, frame.Cancellation.Token) ?? throw new InvalidOperationException("The body returned no original task.");
                    frame.Body = originalBody;
                }
                result = await originalBody.ConfigureAwait(false);
                if (!frame.Provider && result is TaskRunToolActionResult { OriginalResult.OriginalRuntimeError: { } originalRuntimeError })
                    lock (_sync) Add(frame.ResultObservationErrors, originalRuntimeError); // Keep the SAME successful known-effect result while independently retaining its original observation failure for settlement.
            }
            catch (Exception error)
            {
                if (!frame.StreamOrigin)
                    Capture(frame, error, frame.Body, providerBody: frame.Provider && frame.Body is not null);
            }
        }
        catch (Exception error)
        {
            lock (_sync)
                if (!frame.BodyErrors.Any(item => ReferenceEquals(item, error)) && !frame.Errors.Any(item => ReferenceEquals(item, error)))
                    Add(frame.Errors, error);
        }
        finally
        {
            if (frame.OriginalResource is not null)
            {
                try { frame.OriginalResourceDispose = frame.OriginalResource.DisposeAsync().AsTask(); }
                catch (Exception error) { Capture(frame, error, null, providerBody: false); }
                if (frame.OriginalResourceDispose is not null)
                    try { await frame.OriginalResourceDispose.ConfigureAwait(false); }
                    catch (Exception error) { Capture(frame, error, frame.OriginalResourceDispose, providerBody: false); }
            }
            try { frame.Cancellation.Dispose(); }
            catch (Exception error) { lock (_sync) Add(frame.Errors, error); }
            _executing.Value = old;
        }
        List<Exception> failures;
        lock (_sync)
        {
            failures = new(frame.BodyErrors);
            foreach (var error in frame.Errors) Add(failures, error);
        }
        Throw(failures);
        return result;
    }

    // The actual enumerator/finally stays inside the original frame; no idle/response task stands in for it.
    public async IAsyncEnumerable<T> StreamOriginalFrame<T>(TaskRunAttemptAdmission admission,
        Func<CancellationToken, IAsyncEnumerable<T>> rawProviderStream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rawProviderStream);
        using var consumer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(8)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        Task<bool>? original = null;
        original = StartWithFrame(admission, async (frame, token) =>
        {
            frame.StreamOrigin = true;
            IAsyncEnumerator<T>? enumerator = null;
            var failures = new List<Exception>();
            Task<bool>? move = null;
            Task? dispose = null;
            var providerStage = true;
            try
            {
                enumerator = rawProviderStream(token).GetAsyncEnumerator(token);
                while (true)
                {
                    providerStage = true;
                    move = enumerator.MoveNextAsync().AsTask();
                    if (!await move.ConfigureAwait(false)) break;
                    var value = enumerator.Current;
                    providerStage = false;
                    await channel.Writer.WriteAsync(value, token).ConfigureAwait(false);
                }
            }
            catch (Exception error)
            {
                AddTask(failures, error, providerStage ? move : null);
                Capture(frame, error, providerStage ? move : null, providerBody: providerStage);
            }
            finally
            {
                if (enumerator is not null)
                {
                    try { dispose = enumerator.DisposeAsync().AsTask(); }
                    catch (Exception error) { Add(failures, error); Capture(frame, error, null, providerBody: false); }
                    if (dispose is not null)
                        try { await dispose.ConfigureAwait(false); }
                        catch (Exception error)
                        {
                            AddTask(failures, error, dispose);
                            Capture(frame, error, dispose, providerBody: false);
                        }
                }
                channel.Writer.TryComplete(failures.Count == 0 ? null : failures.Count == 1 ? failures[0] : new AggregateException(failures));
            }
            // Compound stream failures are deliberately ineligible for a single-cause provider acknowledgment.
            Throw(failures);
            return true;
        }, consumer.Token, provider: true);
        var actualTerminalObserver = CompleteStreamWriterAsync(original, channel.Writer);
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) yield return item;
        }
        finally
        {
            Exception? cancelFailure = null;
            try { consumer.Cancel(); } catch (Exception error) { cancelFailure = error; }
            var errors = new List<Exception>();
            if (cancelFailure is not null) Add(errors, cancelFailure);
            try { await original.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, error, original); }
            try { await actualTerminalObserver.ConfigureAwait(false); }
            catch (Exception error) { AddTask(errors, error, actualTerminalObserver); }
            Throw(errors);
        }
    }

    private static async Task CompleteStreamWriterAsync<T>(Task originalProducer, ChannelWriter<T> writer)
    {
        Exception? originalFailure = null;
        try { await originalProducer.ConfigureAwait(false); }
        catch (Exception error) { originalFailure = originalProducer.Exception ?? error; }
        finally { writer.TryComplete(originalFailure); }
    }

    public TaskRunOriginalFailureObservation? TryObserveProviderFailure(TaskRunAttemptAdmission originalAdmission, Task originalFrame)
    {
        lock (_sync)
        {
            var attempt = RequireRegistered(originalAdmission);
            var frame = attempt.Frames.SingleOrDefault(item => ReferenceEquals(item.Original, originalFrame));
            if (frame is null || frame.BodyErrors.Count != 1 || !EligibleProviderCause(frame, frame.BodyErrors[0])) return null;
            return CreateProviderFailureObservation(originalAdmission, originalFrame, frame.BodyErrors[0]);
        }
    }

    public TaskRunOriginalFailureObservation CreateProviderFailureObservation(TaskRunAttemptAdmission originalAdmission,
        Task originalFrame, Exception originalCause)
    {
        ArgumentNullException.ThrowIfNull(originalFrame);
        ArgumentNullException.ThrowIfNull(originalCause);
        lock (_sync)
        {
            var attempt = RequireRegistered(originalAdmission);
            var frame = attempt.Frames.SingleOrDefault(item => ReferenceEquals(item.Original, originalFrame));
            if (frame is null || !EligibleProviderCause(frame, originalCause))
                throw new InvalidOperationException("This is not an exact observed terminal raw provider failure.");
            if (frame.Observations.TryGetValue(originalCause, out var prior)) return prior;
            var observation = new TaskRunOriginalFailureObservation(originalAdmission, originalFrame, originalCause);
            _observations.Add(observation, new(attempt, frame, originalCause));
            frame.Observations.Add(originalCause, observation);
            return observation;
        }
    }

    public bool ValidateProviderFailureObservation(TaskRunOriginalFailureObservation observation, TaskRunAttemptAdmission originalAdmission)
    {
        lock (_sync) return IsOriginalObservation(observation, originalAdmission, out _);
    }

    public void AcknowledgeProviderFailure(TaskRunOriginalFailureObservation observation, TaskRunAttemptAdmission originalAdmission,
        TaskRunFailurePersistenceAcknowledgment acknowledgedFailure)
    {
        ArgumentNullException.ThrowIfNull(acknowledgedFailure);
        lock (_sync)
        {
            if (!IsOriginalObservation(observation, originalAdmission, out var original)
                || !ReferenceEquals(acknowledgedFailure.OriginalObservation, observation)
                || !ReferenceEquals(acknowledgedFailure.OriginalAdmission, originalAdmission)
                || acknowledgedFailure.TaskId != observation.TaskId || acknowledgedFailure.ExecutionId != observation.ExecutionId
                || acknowledgedFailure.AttemptId != observation.AttemptId || acknowledgedFailure.AcknowledgedRevision <= originalAdmission.Snapshot.PersistenceRevision
                || acknowledgedFailure.AcknowledgedSnapshot.PersistenceRevision != acknowledgedFailure.AcknowledgedRevision
                || acknowledgedFailure.AcknowledgedSnapshot.TaskId != observation.TaskId
                || acknowledgedFailure.AcknowledgedSnapshot.ExecutionId != observation.ExecutionId
                || acknowledgedFailure.AcknowledgedSnapshot.Attempts.LastOrDefault() is not { State: TaskRunAttemptState.Failed } last
                || last.Id != observation.AttemptId)
                throw new InvalidOperationException("No original matched failure CAS acknowledges this exact frame and cause.");
            if (original!.AcknowledgedReceipt is not null && !ReferenceEquals(original.AcknowledgedReceipt, acknowledgedFailure))
                throw new InvalidOperationException("This original cause was already acknowledged by a different failure CAS.");
            original.AcknowledgedReceipt = acknowledgedFailure;
            Add(original.Frame.AcknowledgedBodyErrors, original.Cause);
        }
    }

    private bool IsOriginalObservation(TaskRunOriginalFailureObservation observation, TaskRunAttemptAdmission admission, out Observation? original)
    {
        original = null;
        return observation is not null && admission is not null && _observations.TryGetValue(observation, out original)
            && ReferenceEquals(original.Attempt.Admission, admission) && EligibleProviderCause(original.Frame, observation.OriginalCause)
            && original.Frame.BodyErrors.Any(error => ReferenceEquals(error, observation.OriginalCause))
            && ReferenceEquals(original.Frame.Original, observation.OriginalFrame) && ReferenceEquals(original.Cause, observation.OriginalCause)
            && observation.TaskId == admission.Snapshot.TaskId && observation.ExecutionId == admission.Snapshot.ExecutionId
            && observation.AttemptId == admission.AttemptId;
    }

    public Task AwaitSettlementAsync(Guid taskId, Guid executionId, Guid attemptId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task result;
        lock (_sync)
        {
            if (!_attempts.TryGetValue((taskId, executionId, attemptId), out var attempt))
                throw new InvalidOperationException("No original attempt is registered for settlement.");
            if (ReferenceEquals(_executing.Value, attempt)) throw new InvalidOperationException("An original frame cannot join its own attempt settlement.");
            result = StartSettlement(attempt);
        }
        return cancellationToken.CanBeCanceled ? result.WaitAsync(cancellationToken) : result;
    }

    private Task StartSettlement(Attempt attempt)
    {
        if (attempt.Settlement is not null) return attempt.Settlement;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        attempt.Settlement = SettleCoreAsync(attempt, gate.Task);
        attempt.Sealed = true;
        gate.SetResult();
        return attempt.Settlement;
    }

    private async Task SettleCoreAsync(Attempt attempt, Task start)
    {
        await start.ConfigureAwait(false);
        var failures = new List<Exception>();
        try { attempt.Lifetime.Cancel(); } catch (Exception error) { Add(failures, error); }
        try { await attempt.Registration!.ConfigureAwait(false); } catch (Exception error) { AddTask(failures, error, attempt.Registration); }
        Frame[] frames;
        lock (_sync) frames = attempt.Frames.ToArray();
        foreach (var frame in frames)
        {
            try { await frame.Original!.ConfigureAwait(false); }
            catch (Exception) { } // Classify the retained exact source causes below; nothing is discarded.
            lock (_sync)
            {
                foreach (var error in frame.Errors)
                    if (!ReferenceEquals(error, frame.OwnerPreBodyRefusal)) Add(failures, error);
                foreach (var error in frame.BodyErrors)
                    if (!frame.AcknowledgedBodyErrors.Any(known => ReferenceEquals(known, error))) Add(failures, error);
                foreach (var error in frame.ResultObservationErrors) Add(failures, error);
            }
        }
        if (attempt.OwnedLease is not null)
        {
            try { attempt.OriginalLeaseClose = attempt.OwnedLease.DisposeAsync().AsTask(); }
            catch (Exception error) { Add(failures, error); }
            if (attempt.OriginalLeaseClose is not null)
                try { await attempt.OriginalLeaseClose.ConfigureAwait(false); }
                catch (Exception error) { AddTask(failures, error, attempt.OriginalLeaseClose); }
        }
        try { attempt.Lifetime.Dispose(); } catch (Exception error) { Add(failures, error); }
        lock (_sync)
        {
            foreach (var error in attempt.Errors) Add(failures, error);
            foreach (var error in failures) Add(attempt.Errors, error);
        }
        Throw(failures);
    }

    public ValueTask RetireAcknowledgedOriginalAttemptAsync(
        TaskRunOriginalRetirementAcknowledgment acknowledgedRetirement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(acknowledgedRetirement);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!acknowledgedRetirement.IsOriginalAcknowledgment)
                throw new UnauthorizedAccessException("A copied retirement acknowledgment is not the original coordinator receipt.");
            var admission = acknowledgedRetirement.OriginalAdmission;
            var attempt = RequireRegistered(admission);
            var snapshot = acknowledgedRetirement.AcknowledgedSnapshot;
            var previous = snapshot.Attempts.FirstOrDefault(item => item.Id == admission.AttemptId);
            var current = snapshot.Attempts.LastOrDefault();
            var terminal = current?.Id == admission.AttemptId && current.State == TaskRunAttemptState.Completed
                && snapshot.State == TaskExecutionLifecycle.Completed;
            var successor = current is not null && current.Id != admission.AttemptId
                && current.RetryOfAttemptId == admission.AttemptId
                && current.State is TaskRunAttemptState.Admitted or TaskRunAttemptState.Running
                && previous?.State is TaskRunAttemptState.Failed or TaskRunAttemptState.Suspended;
            if (snapshot.TaskId != admission.Snapshot.TaskId || snapshot.ContextId != admission.Snapshot.ContextId
                || snapshot.ExecutionId != admission.Snapshot.ExecutionId || snapshot.OwnerBinding != attempt.OwnedLease?.Owner
                || snapshot.CreatedAt != admission.Snapshot.CreatedAt
                || acknowledgedRetirement.AcknowledgedRevision != snapshot.PersistenceRevision
                || snapshot.PersistenceRevision <= admission.Snapshot.PersistenceRevision
                || previous is null || !terminal && !successor)
                throw new InvalidOperationException("No original matched terminal/successor CAS retires this exact attempt.");
            if (attempt.RetirementReceipt is not null)
                throw new InvalidOperationException("This original attempt's retirement receipt has already been consumed.");
            if (!attempt.Sealed || attempt.Registration is not { IsCompletedSuccessfully: true }
                || attempt.Settlement is not { IsCompletedSuccessfully: true }
                || attempt.OwnedLease is null || attempt.OriginalLeaseClose is not { IsCompletedSuccessfully: true }
                || attempt.Errors.Count != 0 || attempt.CapacityRefusal is not null
                || attempt.Frames.Any(frame => frame.Original is not { IsCompleted: true }
                    || frame.ResultObservationErrors.Count != 0
                    || frame.Errors.Any(error => !ReferenceEquals(error, frame.OwnerPreBodyRefusal))
                    || frame.BodyErrors.Any(error => !frame.AcknowledgedBodyErrors.Any(known => ReferenceEquals(known, error)))))
                throw new InvalidOperationException("The actual original work, lease and cleanup have not settled without unresolved causes.");
            attempt.RetirementReceipt = acknowledgedRetirement;
            // Successful settlement may acknowledge a provider fault without erasing its diagnostic originals.
            // Only fully healthy originals release capacity; returned tasks remain their real historical witnesses.
            if (attempt.Frames.All(frame => frame.BodyErrors.Count == 0 && frame.Errors.Count == 0
                && frame.ResultObservationErrors.Count == 0))
                _attempts.Remove(Key(admission));
        }
        return ValueTask.CompletedTask;
    }

    public Task CloseAndDrainAsync()
    {
        lock (_sync)
        {
            if (_executing.Value is not null) throw new InvalidOperationException("A frame cannot join the owner that contains it.");
            if (_close is not null) return _close;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseCoreAsync(gate.Task);
            _closing = true;
            foreach (var attempt in _attempts.Values) attempt.Sealed = true;
            gate.SetResult();
            return _close;
        }
    }

    private async Task CloseCoreAsync(Task start)
    {
        await start.ConfigureAwait(false);
        Task[] settlements;
        lock (_sync) settlements = _attempts.Values.Select(StartSettlement).ToArray();
        var errors = new List<Exception>();
        foreach (var original in settlements)
            try { await original.ConfigureAwait(false); }
            catch (Exception error) { AddTask(errors, error, original); }
        if (_capacityRefusal is not null) Add(errors, _capacityRefusal);
        Throw(errors);
    }

    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private void RequireOpen() { if (_closing) throw new ObjectDisposedException(nameof(TaskRunOriginalFrameOwner)); }
    private Attempt RequireRegistered(TaskRunAttemptAdmission admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        if (!_attempts.TryGetValue(Key(admission), out var attempt)) throw new InvalidOperationException("The actual attempt has not been registered.");
        RequireSame(attempt, admission);
        return attempt;
    }
    private static void RequireSame(Attempt attempt, TaskRunAttemptAdmission admission)
    {
        if (!ReferenceEquals(attempt.Admission, admission)) throw new UnauthorizedAccessException("A copied admission is not the original issued attempt.");
    }
    private static (Guid, Guid, Guid) Key(TaskRunAttemptAdmission admission)
    {
        if (admission.Snapshot.TaskId == Guid.Empty || admission.Snapshot.ExecutionId == Guid.Empty || admission.AttemptId == Guid.Empty)
            throw new ArgumentException("Original task, execution and attempt IDs are required.", nameof(admission));
        return (admission.Snapshot.TaskId, admission.Snapshot.ExecutionId, admission.AttemptId);
    }
    private void Capture(Frame frame, Exception error, Task? original, bool providerBody)
    {
        lock (_sync)
        {
            AddTask(providerBody ? frame.BodyErrors : frame.Errors, error, original);
            if (providerBody && error is TaskCanceledException { InnerException: TimeoutException }
                && !frame.CallerCancellation.IsCancellationRequested && !frame.Attempt.Lifetime.IsCancellationRequested
                && !frame.Cancellation.IsCancellationRequested && original is { IsCompleted: true })
                frame.OriginalTimeoutCause = error;
        }
    }
    private static bool EligibleProviderCause(Frame frame, Exception cause)
    {
        if (!frame.Provider || frame.Original is not { IsCompleted: true } original || frame.Errors.Count != 0
            || cause is AggregateException || !frame.BodyErrors.Any(error => ReferenceEquals(error, cause))) return false;
        if (original.IsFaulted) return true;
        return original.IsCanceled && frame.BodyErrors.Count == 1 && ReferenceEquals(frame.OriginalTimeoutCause, cause)
            && !frame.CallerCancellation.IsCancellationRequested;
    }
    private void RequireBodyAdmission(Frame frame)
    {
        if (_closing || frame.Attempt.Sealed || frame.Attempt.Lifetime.IsCancellationRequested)
        {
            var original = new OperationCanceledException("The original attempt retired before this body was admitted.", frame.Cancellation.Token);
            frame.OwnerPreBodyRefusal = original;
            throw original;
        }
        frame.Cancellation.Token.ThrowIfCancellationRequested();
    }
    private static void Add(List<Exception> errors, Exception error)
    {
        if (!errors.Any(item => ReferenceEquals(item, error))) errors.Add(error);
    }
    private static void AddTask(List<Exception> errors, Exception caught, Task? original)
    {
        Add(errors, caught);
        if (original?.Exception is { } compound) foreach (var error in compound.InnerExceptions) Add(errors, error);
    }
    private static void Throw(List<Exception> errors)
    {
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException(errors);
    }
    private sealed class Attempt(TaskRunAttemptAdmission admission)
    {
        public readonly TaskRunAttemptAdmission Admission = admission;
        public readonly CancellationTokenSource Lifetime = new();
        public readonly List<Frame> Frames = new();
        public readonly List<Exception> Errors = new();
        public ITaskRunAdmissionLease? OwnedLease;
        public Task? Registration, Settlement, OriginalLeaseClose;
        public Task<TaskRunAttemptAdmission?>? OriginalLookup;
        public bool Sealed;
        public Exception? CapacityRefusal;
        public TaskRunOriginalRetirementAcknowledgment? RetirementReceipt;
    }
    private sealed class Frame(Attempt attempt, bool provider, CancellationToken callerCancellation, CancellationTokenSource cancellation)
    {
        public readonly Attempt Attempt = attempt;
        public readonly bool Provider = provider;
        public readonly CancellationToken CallerCancellation = callerCancellation;
        public readonly CancellationTokenSource Cancellation = cancellation;
        public readonly List<Exception> BodyErrors = new(), Errors = new(), AcknowledgedBodyErrors = new(), ResultObservationErrors = new();
        public readonly Dictionary<Exception, TaskRunOriginalFailureObservation> Observations = new(ReferenceEqualityComparer.Instance);
        public Task? Original, Body, Revalidation;
        public Func<CancellationToken, Task>? AcquireOriginalResource;
        public Func<Task, IAsyncDisposable>? OriginalResourceResult;
        public Func<IAsyncDisposable, CancellationToken, Task>? RevalidateOriginalResource;
        public Task? OriginalResourceAcquire;
        public IAsyncDisposable? OriginalResource;
        public Task? OriginalResourceRevalidation, OriginalResourceDispose;
        public Exception? OwnerPreBodyRefusal;
        public Exception? OriginalTimeoutCause;
        public bool StreamOrigin;
    }
    private sealed class Observation(Attempt attempt, Frame frame, Exception cause)
    {
        public readonly Attempt Attempt = attempt;
        public readonly Frame Frame = frame;
        public readonly Exception Cause = cause;
        public TaskRunFailurePersistenceAcknowledgment? AcknowledgedReceipt;
    }
}
