using Haven.Application;
using System.Text.Json;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesNativeBrowserService
{
    private readonly object _dataSourceGate = new();
    private readonly List<DataRead> _dataSources = [];
    private readonly List<Exception> _dataSourceErrors = [];
    private readonly List<Task> _dataSourceLateRaw = [];
    private bool _dataSourceRetiring;
    private Task? _dataSourceClose;

    private sealed class DataRead(FilesNativeBrowserService owner, Action<Action> parentScope, Action<Task> parentRetain)
    {
        internal Task Driver = null!, Observer = null!;
        internal readonly List<Task> Raw = [];
        internal readonly List<Exception> Errors = [];
        internal bool Joined;
        private bool _finished;
        internal void Finish() { lock (Raw) _finished = true; }
        internal void Capture(Exception error)
        {
            lock (Errors) if (!Errors.Contains(error, ReferenceEqualityComparer.Instance)) Errors.Add(error);
            owner.CaptureDataSourceError(error);
        }
        internal void Capture(Task raw, Exception error)
        {
            var causes = raw.Exception?.InnerExceptions;
            if (causes is null || causes.Count == 0) Capture(error);
            else foreach (var direct in causes) Capture(direct);
        }
        internal void Invoke(Action body) => InvokeWithinSource(body, true);
        internal void Cleanup(Action body) => InvokeWithinSource(body, false);
        private void InvokeWithinSource(Action body, bool productive)
        {
            if (productive) lock (Raw) if (_finished) RefuseRetiredSource();
            if (productive) owner.DemandHealthyDataSources();
            var caller = Environment.CurrentManagedThreadId; var open = 1; var calls = 0;
            var failures = new List<Exception>(); var failureGate = new object();
            void Remember(Exception error)
            {
                Capture(error);
                lock (failureGate) if (!failures.Contains(error, ReferenceEqualityComparer.Instance)) failures.Add(error);
            }
            void Actual()
            {
                if (Interlocked.Increment(ref calls) != 1 || Volatile.Read(ref open) == 0 || Environment.CurrentManagedThreadId != caller)
                {
                    var refusal = new InvalidOperationException("The original Data selection callback is finite, once-only and same-thread.");
                    Remember(refusal); throw refusal;
                }
                lock (Raw)
                {
                    if (productive && _finished) RefuseRetiredSource();
                    if (productive) owner.DemandHealthyDataSources();
                    CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () =>
                    {
                        try { body(); } catch (Exception error) { Remember(error); throw; }
                        return true;
                    });
                }
            }
            try
            {
                try
                {
                    using var own = CloudflareOriginalExecutionGuard.EnterOriginal(owner);
                    parentScope(Actual);
                    if (Volatile.Read(ref calls) != 1)
                        Remember(new InvalidOperationException("The original Data selection scope omitted its callback."));
                }
                catch (Exception error) { Remember(error); }
                try { if (productive) owner.DemandHealthyDataSources(); }
                catch (Exception error) { lock (failureGate) failures.Add(error); }
            }
            finally { Volatile.Write(ref open, 0); }
            Exception[] actualFailures; lock (failureGate) actualFailures = failures.ToArray();
            ThrowDataSources(actualFailures);
        }
        internal void Retain(Task actual)
        {
            ArgumentNullException.ThrowIfNull(actual);
            lock (Raw)
            {
                if (_finished)
                {
                    lock (owner._dataSourceGate)
                        if (!owner._dataSourceLateRaw.Contains(actual, ReferenceEqualityComparer.Instance)) owner._dataSourceLateRaw.Add(actual);
                    if (!Raw.Contains(actual, ReferenceEqualityComparer.Instance)) Raw.Add(actual);
                    RefuseRetiredSource();
                }
                if (!Raw.Contains(actual, ReferenceEqualityComparer.Instance)) Raw.Add(actual);
            }
            // A returned raw is retained before a retainer or capacity postguard
            // can reject it. Accepted work is always independently joined.
            try
            {
                CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () => { parentRetain(actual); return true; });
                lock (Raw) if (Raw.Count > 2048) throw new InvalidOperationException("Settle the original Data selection child cohort.");
            }
            catch (Exception error) { Capture(error); throw; }
        }
        private void RefuseRetiredSource()
        {
            var cause = new InvalidOperationException("The original Data selection source has retired.");
            Capture(cause); throw cause;
        }
        internal async Task Join()
        {
            var visited = new HashSet<Task>(ReferenceEqualityComparer.Instance);
            while (true)
            {
                Task[] next; lock (Raw) next = Raw.Where(visited.Add).ToArray();
                if (next.Length == 0) break;
                foreach (var raw in next)
                    try { await raw.ConfigureAwait(false); } catch (Exception error) { Capture(raw, error); }
            }
        }
    }
    private void CaptureDataSourceError(Exception error)
    { lock (_dataSourceGate) if (!_dataSourceErrors.Contains(error, ReferenceEqualityComparer.Instance)) _dataSourceErrors.Add(error); }
    private void DemandHealthyDataSources()
    { Exception[] errors; lock (_dataSourceGate) errors = _dataSourceErrors.ToArray(); ThrowDataSources(errors); }
    private static void ThrowDataSources(IEnumerable<Exception> errors)
    {
        var actual = errors.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        if (actual.Length != 0) throw new AggregateException("The original Data file selection needs recovery.", actual);
    }
    private Task<T> AdmitDataSource<T>(Action<Action> scope, Action<Task> retain, Func<FilesOriginalReadSourceScope, Task<T>> body)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        DataRead record; TaskCompletionSource start; Task<T> actual;
        lock (_dataSourceGate)
        {
            ObjectDisposedException.ThrowIf(_dataSourceRetiring, this); DemandHealthyDataSources();
            foreach (var previous in _dataSources.Where(value => value.Driver.IsCompletedSuccessfully && value.Observer.IsCompletedSuccessfully).ToArray())
            {
                previous.Driver.GetAwaiter().GetResult(); previous.Observer.GetAwaiter().GetResult();
                if (previous.Joined) _dataSources.Remove(previous);
            }
            if (_dataSources.Count >= 128) throw new InvalidOperationException("Settle the existing Data selections before another read.");
            record = new(this, scope, retain); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = DriveDataSource(start.Task, record, body); record.Driver = actual;
            record.Observer = ObserveDataSource(record); _dataSources.Add(record);
        }
        start.SetResult(); return actual;
    }
    private static async Task ObserveDataSource(DataRead record)
    { await record.Driver.ConfigureAwait(false); record.Joined = true; }
    private async Task<T> DriveDataSource<T>(Task start, DataRead record, Func<FilesOriginalReadSourceScope, Task<T>> body)
    {
        await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var source = new FilesOriginalReadSourceScope(record.Invoke, record.Retain); T value = default!;
        try { value = await body(source).ConfigureAwait(false); } catch (Exception error) { record.Capture(error); }
        try { await record.Join().ConfigureAwait(false); DemandHealthyDataSources(); return value; }
        finally { record.Finish(); }
    }
    public Task? OriginalDataSelectionsClose { get { lock (_dataSourceGate) return _dataSourceClose; } }
    public void DemandExternalOriginalDataSelectionJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    public Task CloseOriginalDataSelectionsAndDrainAsync()
    {
        DemandExternalOriginalDataSelectionJoin(); TaskCompletionSource? start = null; Task actual;
        lock (_dataSourceGate)
        {
            _dataSourceRetiring = true;
            if (_dataSourceClose?.IsCompletedSuccessfully == true)
            { _dataSourceClose.GetAwaiter().GetResult(); DemandHealthyDataSources(); }
            if (_dataSourceClose is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _dataSourceClose = DrainDataSources(start.Task); }
            actual = _dataSourceClose;
        }
        start?.SetResult(); return actual;
    }
    private async Task DrainDataSources(Task start)
    {
        await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        DataRead[] records; lock (_dataSourceGate) records = _dataSources.ToArray(); var errors = new List<Exception>();
        foreach (var record in records)
        {
            try { await record.Driver.ConfigureAwait(false); } catch (Exception error) { record.Capture(record.Driver, error); }
            try { await record.Observer.ConfigureAwait(false); } catch (Exception error) { record.Capture(record.Observer, error); }
            await record.Join().ConfigureAwait(false);
            lock (record.Errors) errors.AddRange(record.Errors);
        }
        Task[] late; lock (_dataSourceGate) late = _dataSourceLateRaw.ToArray();
        foreach (var actual in late)
            try { await actual.ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(actual.Exception ?? cause); }
        lock (_dataSourceGate) errors.AddRange(_dataSourceErrors);
        // Each accepted existing-state read independently closed its real file and
        // sidecar before its raw task settled. Shared provider/store owners remain
        // borrowed and available to their other consumers; this closes only Data.
        ThrowDataSources(errors);
    }
}
