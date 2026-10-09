namespace HavenOS.Files;

public sealed partial class VersionedJsonStateStore<TState> where TState : class
{
    private readonly object _originalReadsGate = new();
    private readonly List<OriginalUpdate> _originalReads = [];
    private bool _originalReadsRetiring;
    private Task? _originalReadsClose;

    /// <summary>Reads the existing envelope using the maintained gate, parser and
    /// raw-source custody. Both state and process sidecar must already exist.</summary>
    public Task<TState> ReadExistingWithinOriginalSourceAsync(Action<Action> scope,
        Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        ThrowIfOriginalUpdateJoinWouldCycle(); OriginalUpdate actual; TaskCompletionSource start;
        lock (_originalReadsGate)
        {
            ObjectDisposedException.ThrowIf(_originalReadsRetiring, this);
            _originalReads.RemoveAll(value => value.Driver.IsCompletedSuccessfully &&
                value.Observation.IsCompletedSuccessfully && value.Source.IsHealthy);
            if (_originalReads.Count >= 64) throw new InvalidOperationException("Settle the retained Files reads before another read.");
            actual = new(this, scope, retain); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual.Driver = DriveOriginalRead(start.Task, actual, token);
            actual.Observation = ObserveOriginalUpdateAsync(actual.Driver); _originalReads.Add(actual);
        }
        try { actual.Source.PublishDriver(actual.Driver); }
        catch (Exception error) { actual.Source.Remember(error); }
        finally { start.SetResult(); }
        return actual.Driver;
    }
    public Task? OriginalReadsClose { get { lock (_originalReadsGate) return _originalReadsClose; } }
    public void ThrowIfOriginalReadJoinWouldCycle() => ThrowIfOriginalUpdateJoinWouldCycle();
    public Task CloseOriginalReadsAndDrainAsync()
    {
        ThrowIfOriginalReadJoinWouldCycle(); TaskCompletionSource? start = null; Task actual;
        lock (_originalReadsGate)
        {
            _originalReadsRetiring = true;
            if (_originalReadsClose is null)
            { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _originalReadsClose = DrainOriginalReads(start.Task); }
            actual = _originalReadsClose;
        }
        start?.SetResult(); return actual;
    }
    private async Task DrainOriginalReads(Task start)
    {
        await start.ConfigureAwait(false); OriginalUpdate[] reads;
        lock (_originalReadsGate) reads = _originalReads.ToArray();
        var errors = new List<Exception>();
        foreach (var actual in reads)
        {
            try { await actual.Driver.ConfigureAwait(false); } catch (Exception error) { errors.Add(actual.Driver.Exception ?? error); }
            try { await actual.Observation.ConfigureAwait(false); } catch (Exception error) { errors.Add(actual.Observation.Exception ?? error); }
        }
        if (errors.Count != 0) throw new AggregateException("Original Files reads or their resource closes failed.", errors);
    }
    private async Task<TState> DriveOriginalRead(Task start, OriginalUpdate original, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        var previous = OriginalUpdateLogical.Value; var invocation = new UpdateInvocation(_gate, previous);
        OriginalUpdateLogical.Value = invocation;
        var source = original.Source; TState result = null!; Exception? failure = null;
        try
        {
            source.ThrowRemembered();
            await source.Read(() => _gate.WaitAsync(token), () => original.GateHeld = true).ConfigureAwait(false);
            source.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                // Existing-only, including the maintained cross-process lock. No
                // creation factory, directory creation or sidecar initialization.
                original.ProcessLease = new FileStream(_path + ".lock", FileMode.Open,
                    FileAccess.Read, FileShare.None, 1, FileOptions.Asynchronous);
                source.Capture(original.ProcessLease);
            });
            result = await ReadOriginalEnvelopeAsync(source, token).ConfigureAwait(false);
        }
        catch (Exception error) { failure = error; }
        finally
        {
            try { await source.CloseResources().ConfigureAwait(false); }
            catch (Exception error) { source.Remember(error); }
            if (original.GateHeld && (original.ProcessLease is null || source.IsResourceHealthyClosed(original.ProcessLease)))
                try { source.OwningCleanup(() => { _gate.Release(); original.GateHeld = false; }); }
                catch (Exception error) { source.Remember(error); }
            try { await source.JoinRaw().ConfigureAwait(false); } catch (Exception error) { source.Remember(error); }
            invocation.Active = false; OriginalUpdateLogical.Value = previous;
        }
        source.ThrowRemembered(failure); return result;
    }
}
