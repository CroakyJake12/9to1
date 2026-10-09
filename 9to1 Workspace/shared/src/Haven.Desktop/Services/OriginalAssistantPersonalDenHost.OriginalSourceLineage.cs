using System.Runtime.ExceptionServices;

namespace Haven.Desktop.Services;

public sealed partial class OriginalAssistantPersonalDenHost
{
    // A source lends its actual predecessor, never a child later installed into
    // AsyncLocal by Home. The finite call also owns every raw returned through
    // that lineage until independent joins finish.
    private ExternalSource CaptureOriginalSourceLineage() =>
        new OriginalSourceCall(this, _externalSource.Value).Lineage;

    private T InvokeWithinOriginalSourceLineage<T>(Func<T> callback, ExternalSource? predecessor) =>
        new OriginalSourceCall(this, predecessor).Invoke(callback);

    private void RetainWithinOriginalSourceLineage(Task actual, ExternalSource? predecessor) =>
        new OriginalSourceCall(this, predecessor).Retain(actual);

    private Task<T> ReadOriginalSourceAsync<T>(DesktopOriginalWorkLifetime.Original? original,
        Func<ExternalSource, Task<T>> factory, Action<T>? capture = null) =>
        new OriginalSourceCall(this, _externalSource.Value).ReadAsync(original, factory, capture);

    private void TrackOriginalSource(Task actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        lock (_gate)
        {
            // Status alone is never a join receipt. Each removed source was
            // actually awaited successfully by this same host first.
            foreach (var joined in _sources.Where(_successfullyJoinedSources.Contains).ToArray())
            { _sources.Remove(joined); _successfullyJoinedSources.Remove(joined); }
            if (!_sources.Any(source => ReferenceEquals(source, actual))) _sources.Add(actual);
        }
    }

    private void RememberOriginalSourceFailure(Exception cause)
    {
        // Preserve callback occurrences, including separate callbacks which
        // throw the same exception object. Foreign aggregates stay opaque.
        lock (_gate) _sourceFailures.Add(cause);
        _work.Executing?.Retain(cause);
    }

    private static void ThrowOriginalSourceFailures(IReadOnlyList<Exception> failures)
    {
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Actual personal Den callback/source causes remain retained.", failures);
    }

    private sealed class OriginalSourceCall
    {
        private readonly OriginalAssistantPersonalDenHost _owner;
        private readonly ExternalSource? _predecessor;
        private readonly object _gate = new();
        private readonly List<Exception> _failures = [];
        private readonly List<Task> _raw = [];
        private readonly HashSet<Task> _joined = new(ReferenceEqualityComparer.Instance);
        private bool _closed;
        internal OriginalSourceCall(OriginalAssistantPersonalDenHost owner, ExternalSource? predecessor)
        { _owner = owner; _predecessor = predecessor; Lineage = new(Run, Retain); }
        internal ExternalSource Lineage { get; }
        private Exception[] Failures { get { lock (_gate) return _failures.ToArray(); } }
        private void Remember(Exception cause)
        {
            lock (_gate) _failures.Add(cause);
            _owner.RememberOriginalSourceFailure(cause);
        }
        private void Track(Task actual)
        {
            // The host owns the raw task before a borrowed retainer can refuse.
            _owner.TrackOriginalSource(actual);
            lock (_gate) if (!_raw.Any(source => ReferenceEquals(source, actual))) _raw.Add(actual);
        }
        internal void Retain(Task actual)
        {
            Track(actual);
            if (_predecessor is not null)
                Invoke(() => { _predecessor.Retain(actual); return true; });
        }
        private void Run(Action body) => Invoke(() => { body(); return true; });
        internal T Invoke<T>(Func<T> body)
        {
            var active = 1; var used = 0; var thread = Environment.CurrentManagedThreadId;
            var invocationFailures = new List<Exception>();
            void Fail(Exception cause) { lock (invocationFailures) invocationFailures.Add(cause); Remember(cause); }
            T value = default!;
            var physical = _physicalSources ??= [];
            physical[_owner] = physical.GetValueOrDefault(_owner) + 1;
            try
            {
                void Enter()
                {
                    bool closed; lock (_gate) closed = _closed;
                    if (closed || Volatile.Read(ref active) == 0 || thread != Environment.CurrentManagedThreadId ||
                        Interlocked.CompareExchange(ref used, 1, 0) != 0)
                    {
                        var protocol = new InvalidOperationException("The actual personal Den callback expired, repeated or moved threads.");
                        Fail(protocol); throw protocol;
                    }
                    try
                    {
                        value = body();
                        if (value is Task actual) Track(actual);
                    }
                    catch (Exception cause) { Fail(cause); throw; }
                }
                try
                {
                    if (_predecessor is null) Enter(); else _predecessor.Scope(Enter);
                }
                catch (Exception cause)
                {
                    // A caller may swallow a body failure or replace it with a
                    // different post-callback failure; both remain rooted.
                    Fail(cause);
                }
                if (Volatile.Read(ref used) == 0)
                    Fail(new InvalidOperationException("The actual personal Den source callback was not entered."));
                // Sticky custody belongs to the enclosing finite source. A
                // later legitimate callback still settles its own raw source.
                Exception[] currentFailures; lock (invocationFailures) currentFailures = invocationFailures.ToArray();
                ThrowOriginalSourceFailures(currentFailures);
                return value;
            }
            finally
            {
                Volatile.Write(ref active, 0);
                if (physical[_owner] == 1) physical.Remove(_owner); else physical[_owner]--;
            }
        }
        internal async Task<T> ReadAsync<T>(DesktopOriginalWorkLifetime.Original? original,
            Func<ExternalSource, Task<T>> factory, Action<T>? capture)
        {
            Task<T>? actual = null; T value = default!;
            try
            {
                try
                {
                    _ = Invoke(() =>
                    {
                        actual = factory(Lineage) ?? throw new InvalidOperationException("No actual personal Den source Task was returned.");
                        Retain(actual);
                        return actual;
                    });
                }
                catch { /* Invoke already preserves each body/protocol/scope cause. */ }
                if (actual is not null)
                {
                    try
                    {
                        value = original is null ? await actual.ConfigureAwait(false) : await original.AwaitAsync(actual).ConfigureAwait(false);
                        lock (_gate) _joined.Add(actual);
                        lock (_owner._gate) _owner._successfullyJoinedSources.Add(actual);
                        // Resource custody precedes rethrow even when a caller
                        // rejected publication after acquiring this same source.
                        capture?.Invoke(value);
                    }
                    catch (Exception cause)
                    {
                        lock (_gate) _joined.Add(actual);
                        if (actual.Exception is { } group)
                            foreach (var direct in group.InnerExceptions) Remember(direct);
                        else Remember(cause);
                    }
                }
                await JoinCapturedAsync(original).ConfigureAwait(false);
                ThrowOriginalSourceFailures(Failures);
                return actual is null ? throw new InvalidOperationException("No actual personal Den source Task was captured.") : value;
            }
            finally { lock (_gate) _closed = true; }
        }
        private async Task JoinCapturedAsync(DesktopOriginalWorkLifetime.Original? original)
        {
            while (true)
            {
                Task[] raw; lock (_gate) raw = _raw.Where(source => !_joined.Contains(source)).ToArray();
                if (raw.Length == 0) return;
                foreach (var actual in raw)
                {
                    try
                    {
                        if (original is null) await actual.ConfigureAwait(false);
                        else await original.AwaitAsync(actual).ConfigureAwait(false);
                        lock (_owner._gate) _owner._successfullyJoinedSources.Add(actual);
                    }
                    catch (Exception cause)
                    {
                        if (actual.Exception is { } group)
                            foreach (var direct in group.InnerExceptions) Remember(direct);
                        else Remember(cause);
                    }
                    finally { lock (_gate) _joined.Add(actual); }
                }
            }
        }
    }
}
