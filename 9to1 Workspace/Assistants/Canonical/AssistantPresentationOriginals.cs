using NineToOne.Dulche.Den;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Canonical;

/// <summary>Admission and retained originals for one presentation, never for borrowed business owners.</summary>
internal sealed class AssistantPresentationOriginals
{
    private readonly object _gate = new();
    private readonly List<Task> _commands = [];
    private readonly List<Task> _sources = [];
    private readonly List<(Func<Task> Close, Func<Task?>? Peek)> _observationCloses = [];
    private readonly AsyncLocal<Invocation?> _current = new();
    [ThreadStatic] private static AssistantPresentationOriginals? _physicalSource;
    private bool _retired;
    private Task? _close;
    private const int MaximumCommands = 64;
    private const int MaximumSources = 256;
    private const int MaximumObservations = 32;
    private int _pendingSources;
    private sealed class Invocation { internal bool Active = true; }

    internal Task<T> Admit<T>(Func<Task<T>> body)
    {
        lock (_gate)
        {
            if (_retired) throw new ObjectDisposedException("Assistant presentation");
            PruneJoined(_commands);
            PruneJoined(_sources);
            if (_commands.Count >= MaximumCommands || _sources.Count + _pendingSources >= MaximumSources)
                throw new AssistantCommandRefusedException("Retained Assistant originals require settlement or inspection before further admission.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actual = DriveAsync(start.Task, body);
            _commands.Add(actual);
            start.SetResult();
            return actual;
        }
    }

    private async Task<T> DriveAsync<T>(Task start, Func<Task<T>> body)
    {
        await start.ConfigureAwait(false);
        var invocation = new Invocation();
        _current.Value = invocation;
        try { return await body().ConfigureAwait(false); }
        finally { invocation.Active = false; _current.Value = null; }
    }

    internal T Invoke<T>(Func<T> source)
    {
        if (_current.Value is not { Active: true }) throw new InvalidOperationException("No admitted Assistant source owns this invocation.");
        var previous = _physicalSource;
        _physicalSource = this;
        try { return source(); }
        finally { _physicalSource = previous; }
    }

    internal Task<T> Source<T>(Func<Task<T>> source)
    {
        ReserveSource();
        try { var actual = Invoke(source) ?? throw new InvalidOperationException("The actual source returned no Task."); Retain(actual); return actual; }
        finally { lock (_gate) _pendingSources--; }
    }
    internal Task Source(Func<Task> source)
    {
        ReserveSource();
        try { var actual = Invoke(source) ?? throw new InvalidOperationException("The actual source returned no Task."); Retain(actual); return actual; }
        finally { lock (_gate) _pendingSources--; }
    }
    internal void Retain(Task actual) { lock (_gate) _sources.Add(actual); }
    internal bool AcknowledgeOriginalExternalPreEffectRefusal(Task sameActual, Func<Task, bool> sameConfiguredIssuerProof)
    {
        ArgumentNullException.ThrowIfNull(sameActual);
        ArgumentNullException.ThrowIfNull(sameConfiguredIssuerProof);
        lock (_gate)
            if (!_sources.Any(source => ReferenceEquals(source, sameActual))) return false;
        if (!sameActual.IsFaulted || sameActual.Exception is not { InnerExceptions.Count: 1 } payload ||
            payload.InnerExceptions[0] is AggregateException) return false;
        // Independently consume this terminal SAME raw Task. It cannot block or replace
        // another source. Scope/issuer exceptions propagate as their own command fault.
        try { sameActual.GetAwaiter().GetResult(); } catch { }
        if (!Invoke(() => sameConfiguredIssuerProof(sameActual))) return false;
        return AssistantOriginalExternalRefusalReceipts.Publish(sameActual, payload.InnerExceptions[0]);
    }

    private void ReserveSource()
    {
        lock (_gate)
        {
            PruneJoined(_sources);
            if (_sources.Count + _pendingSources >= MaximumSources)
                throw new AssistantCommandRefusedException("Actual Assistant sources require settlement or inspection before another source factory.");
            _pendingSources++;
        }
    }
    internal void DemandObservationCapacity()
    {
        lock (_gate)
        {
            for (var index = _observationCloses.Count - 1; index >= 0; index--)
                if (_observationCloses[index].Peek?.Invoke() is { IsCompletedSuccessfully: true } originalClose)
                { originalClose.GetAwaiter().GetResult(); _observationCloses.RemoveAt(index); }
            if (_observationCloses.Count >= MaximumObservations)
                throw new AssistantCommandRefusedException("Close existing observations before admitting more work in this presentation.");
        }
    }
    internal void Observe(Func<Task> originalClose, Func<Task?>? peekOriginalClose = null)
    { lock (_gate) _observationCloses.Add((originalClose, peekOriginalClose)); }
    internal void RequestRetirement() { lock (_gate) _retired = true; }
    internal void DemandExternalJoin()
    {
        if (ReferenceEquals(_physicalSource, this) || _current.Value is { Active: true })
            throw new InvalidOperationException("An Assistant source cannot join its own presentation retirement.");
    }
    internal Task CloseAndDrainAsync()
    {
        DemandExternalJoin();
        lock (_gate)
        {
            _retired = true;
            if (_close is not null) return _close;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = DrainAsync(start.Task, _commands.ToArray());
            start.SetResult();
            return _close;
        }
    }
    private async Task DrainAsync(Task start, Task[] commands)
    {
        await start.ConfigureAwait(false);
        var failures = new List<Exception>();
        foreach (var command in commands) await JoinAsync(command, failures).ConfigureAwait(false);
        (Func<Task> Close, Func<Task?>? Peek)[] closes;
        Task[] sources;
        lock (_gate) { closes = _observationCloses.ToArray(); sources = _sources.ToArray(); }
        foreach (var close in closes)
        {
            try { await JoinAsync(close.Close(), failures).ConfigureAwait(false); }
            catch (Exception cause) { failures.Add(cause); }
        }
        foreach (var source in sources) await JoinAsync(source, failures).ConfigureAwait(false);
        if (failures.Count != 0) throw new AggregateException("Assistant presentation originals did not drain cleanly.", failures.Distinct<Exception>(ReferenceEqualityComparer.Instance));
    }
    private static async Task JoinAsync(Task actual, List<Exception> failures)
    {
        try { await actual.ConfigureAwait(false); }
        catch (Exception cause)
        {
            if (AssistantOriginalExternalRefusalReceipts.IsAcknowledgedOriginal(actual)) return;
            IEnumerable<Exception> causes = actual.Exception is { } aggregate ? aggregate.InnerExceptions : new[] { cause };
            foreach (var failure in causes)
                if (failure is not AssistantCommandRefusedException && !IsAcknowledgedDenRefusal(failure)) failures.Add(failure);
        }
    }
    private static bool IsAcknowledgedDenRefusal(Exception cause) => cause is DenException den && den.Code is
        DenErrorCode.Conflict or DenErrorCode.Forbidden or DenErrorCode.NotFound or DenErrorCode.InvalidRecord
        or DenErrorCode.IdempotencyMismatch or DenErrorCode.SecretMaterialRejected;
    private static void PruneJoined(List<Task> originals)
    {
        for (var index = originals.Count - 1; index >= 0; index--)
        {
            var original = originals[index];
            if (!original.IsCompleted) continue;
            try { original.GetAwaiter().GetResult(); originals.RemoveAt(index); }
            catch (Exception cause)
            {
                if (AssistantOriginalExternalRefusalReceipts.IsAcknowledgedOriginal(original))
                { originals.RemoveAt(index); continue; }
                IEnumerable<Exception> causes = original.Exception is { } aggregate ? aggregate.InnerExceptions : new[] { cause };
                if (causes.All(error => error is AssistantCommandRefusedException || IsAcknowledgedDenRefusal(error))) originals.RemoveAt(index);
            }
        }
    }
}

