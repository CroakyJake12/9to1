using System.Runtime.ExceptionServices;

namespace HavenOS.Files;

public sealed partial class VersionedJsonStateStore<TState> where TState : class
{
    private readonly object _originalUpdatesGate = new();
    private readonly List<OriginalUpdate> _originalUpdates = [];
    private bool _originalUpdatesRetiring;
    private Task? _originalUpdatesClose;
    private static readonly AsyncLocal<UpdateInvocation?> OriginalUpdateLogical = new();
    [ThreadStatic] private static Dictionary<object, int>? OriginalUpdatePhysical;
    private sealed class UpdateInvocation(object gate, UpdateInvocation? parent)
    { internal readonly object Gate = gate; internal readonly UpdateInvocation? Parent = parent; internal volatile bool Active = true; }

    /// <summary>Updates an existing envelope using the SAME metadata gate and process lease.
    /// The asynchronous transform performs its own pre-effect checks. The validator
    /// runs after that transform and again after the original output has been flushed.</summary>
    public Task<TState> UpdateExistingWithinOriginalSourceAsync(
        Func<TState, CancellationToken, Task<TState>> update,
        Func<CancellationToken, ValueTask>? validateCommitAuthority,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(update); ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        ThrowIfOriginalUpdateJoinWouldCycle();
        OriginalUpdate actual; TaskCompletionSource start;
        lock (_originalUpdatesGate)
        {
            if (_originalUpdatesRetiring) throw new ObjectDisposedException("Original Files metadata updates");
            // Successful original receipts have been independently awaited by their
            // own published observer. Failed records retain their actual objects.
            _originalUpdates.RemoveAll(value => value.Driver.IsCompletedSuccessfully &&
                value.Observation.IsCompletedSuccessfully && value.Source.IsHealthy);
            if (_originalUpdates.Count >= 64)
                throw new InvalidOperationException("Settle original Files metadata updates before admitting another write.");
            actual = new(this, scope, retain); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual.Driver = DriveOriginalUpdateAsync(start.Task, actual, update, validateCommitAuthority, token);
            actual.Observation = ObserveOriginalUpdateAsync(actual.Driver);
            _originalUpdates.Add(actual);
        }
        // The encompassing driver is owned by the store record, never a child
        // of its own raw-source join. Publish it without enrolling a self-cycle.
        try { actual.Source.PublishDriver(actual.Driver); }
        catch (Exception cause) { actual.Source.Remember(cause); }
        finally { start.SetResult(); }
        return actual.Driver;
    }

    public Task? OriginalUpdatesClose { get { lock (_originalUpdatesGate) return _originalUpdatesClose; } }

    /// <summary>Preflight for the process owner before joining this additive cohort.</summary>
    public void ThrowIfOriginalUpdateJoinWouldCycle()
    {
        if (OriginalUpdatePhysical?.ContainsKey(_gate) == true)
            throw new InvalidOperationException("The actual Files metadata callback cannot join or reenter its own update.");
        for (var current = OriginalUpdateLogical.Value; current is not null; current = current.Parent)
            if (current.Active && ReferenceEquals(current.Gate, _gate))
                throw new InvalidOperationException("The actual Files metadata driver cannot join or reenter its own update.");
    }

    /// <summary>Seals only original updates. Ordinary Read/Update compatibility is unchanged.</summary>
    public Task CloseOriginalUpdatesAndDrainAsync()
    {
        ThrowIfOriginalUpdateJoinWouldCycle(); TaskCompletionSource? start = null; Task actual;
        lock (_originalUpdatesGate)
        {
            if (_originalUpdatesClose is null)
            {
                _originalUpdatesRetiring = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _originalUpdatesClose = CloseOriginalUpdatesAsync(start.Task);
            }
            actual = _originalUpdatesClose;
        }
        start?.SetResult(); return actual;
    }

    private async Task CloseOriginalUpdatesAsync(Task start)
    {
        await start.ConfigureAwait(false);
        OriginalUpdate[] originals; lock (_originalUpdatesGate) originals = _originalUpdates.ToArray();
        var errors = new List<Exception>();
        foreach (var actual in originals)
        {
            try { await actual.Driver.ConfigureAwait(false); } catch (Exception cause) { errors.Add(actual.Driver.Exception ?? cause); }
            try { await actual.Observation.ConfigureAwait(false); } catch (Exception cause) { errors.Add(actual.Observation.Exception ?? cause); }
        }
        if (errors.Count != 0) throw new AggregateException("Every original Files metadata update and cleanup was independently joined.", errors);
    }

    private static async Task ObserveOriginalUpdateAsync(Task actual) => await actual.ConfigureAwait(false);
    private sealed class OriginalUpdate(VersionedJsonStateStore<TState> owner, Action<Action> scope, Action<Task> retain)
    {
        internal readonly UpdateSources Source = new(owner, scope, retain);
        internal Task<TState> Driver = null!;
        internal Task Observation = null!;
        internal bool GateHeld;
        internal FileStream? ProcessLease;
        internal string? TemporaryPath;
    }

    private async Task<TState> DriveOriginalUpdateAsync(Task start, OriginalUpdate original,
        Func<TState, CancellationToken, Task<TState>> update,
        Func<CancellationToken, ValueTask>? validate, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        var previous = OriginalUpdateLogical.Value; var invocation = new UpdateInvocation(_gate, previous);
        OriginalUpdateLogical.Value = invocation;
        var source = original.Source; TState next = null!; Exception? bodyFailure = null;
        try
        {
            source.ThrowRemembered();
            source.Run(() => { if (!File.Exists(_path)) throw new FileNotFoundException("The existing Files state is unavailable.", _path); });
            await source.Read(() => _gate.WaitAsync(token), () => original.GateHeld = true).ConfigureAwait(false);
            await AcquireOriginalProcessLeaseAsync(original, token).ConfigureAwait(false);
            var current = await ReadOriginalEnvelopeAsync(source, token).ConfigureAwait(false);
            next = await source.Read(() => update(current, token)).ConfigureAwait(false);
            if (next is null) throw new InvalidOperationException("A Files state update cannot return null.");
            if (validate is not null) await source.Read(() => validate(token).AsTask()).ConfigureAwait(false);
            var directory = source.Invoke(() => Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Files state has no parent directory."));
            source.Run(() => original.TemporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp"));
            var stream = source.Invoke(() => new FileStream(original.TemporaryPath!, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough));
            var writer = source.Invoke(() => new System.Text.Json.Utf8JsonWriter(stream));
            source.Run(() =>
            {
                writer.WriteStartObject(); writer.WriteNumber("schemaVersion", _schemaVersion); writer.WritePropertyName("state");
                System.Text.Json.JsonSerializer.Serialize(writer, next, SerializerOptions); writer.WriteEndObject();
            });
            await source.Close(writer).ConfigureAwait(false);
            await source.Read(() => stream.FlushAsync(token)).ConfigureAwait(false);
            source.Run(() => stream.Flush(flushToDisk: true));
            await source.Close(stream).ConfigureAwait(false);
            if (validate is not null) await source.Read(() => validate(token).AsTask()).ConfigureAwait(false);
            source.Run(() => { token.ThrowIfCancellationRequested(); File.Move(original.TemporaryPath!, _path, overwrite: true); });
        }
        catch (Exception cause) { bodyFailure = cause; }
        finally
        {
            try { await source.CloseResources().ConfigureAwait(false); }
            catch (Exception cause) { source.Remember(cause); }
            // The process lease is a captured resource. Unknown cleanup must keep
            // the SAME gate/lease custody rather than release into a second writer.
            if (original.GateHeld && (original.ProcessLease is null || source.IsResourceHealthyClosed(original.ProcessLease)))
                try { source.OwningCleanup(() => { _gate.Release(); original.GateHeld = false; }); }
                catch (Exception cause) { source.Remember(cause); }
            try { await source.JoinRaw().ConfigureAwait(false); }
            catch (Exception cause) { source.Remember(cause); }
            invocation.Active = false; OriginalUpdateLogical.Value = previous;
        }
        source.ThrowRemembered(bodyFailure);
        return next;
    }

    private async Task AcquireOriginalProcessLeaseAsync(OriginalUpdate original, CancellationToken token)
    {
        var source = original.Source; var elapsed = System.Diagnostics.Stopwatch.StartNew();
        source.Run(() => { if (!File.Exists(_path)) throw new FileNotFoundException("The existing Files state is unavailable.", _path); });
        while (true)
        {
            IOException? contention = null;
            source.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite,
                    Share = FileShare.None, BufferSize = 1, Options = FileOptions.Asynchronous };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                try { original.ProcessLease = new FileStream(_path + ".lock", options); source.Capture(original.ProcessLease); }
                catch (IOException cause) { contention = cause; }
            });
            if (original.ProcessLease is not null) return;
            if (elapsed.Elapsed >= TimeSpan.FromSeconds(30))
                throw new IOException("Files metadata is locked or unavailable in another process.", contention);
            await source.Read(() => Task.Delay(25, token)).ConfigureAwait(false);
        }
    }

    private async Task<TState> ReadOriginalEnvelopeAsync(UpdateSources source, CancellationToken token)
    {
        var stream = source.Invoke(() => new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan));
        var document = await source.Read(() => System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: token)).ConfigureAwait(false);
        var current = source.Invoke(() =>
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var element) || !element.TryGetInt32(out var version))
                throw new InvalidDataException("Files state is missing its schema version.");
            if (version != _schemaVersion) throw new InvalidDataException($"Files state schema {version} is not supported; expected {_schemaVersion}.");
            if (!root.TryGetProperty("state", out var state)) throw new InvalidDataException("Files state envelope is missing its state payload.");
            return System.Text.Json.JsonSerializer.Deserialize<TState>(state, SerializerOptions)
                ?? throw new InvalidDataException("Files state payload is empty or invalid.");
        });
        await source.Close(document).ConfigureAwait(false); await source.Close(stream).ConfigureAwait(false);
        return current;
    }

    private sealed class UpdateSources(VersionedJsonStateStore<TState> owner, Action<Action> scope, Action<Task> retainer)
    {
        private readonly object _gate = new();
        private readonly List<Task> _raw = [];
        private readonly List<Exception> _errors = [];
        private readonly Dictionary<Task, Exception[]> _faults = new(ReferenceEqualityComparer.Instance);
        private readonly List<object> _resources = [];
        private readonly Dictionary<object, OriginalClose> _closes = new(ReferenceEqualityComparer.Instance);
        private sealed class OriginalClose { internal Task Driver = null!; internal Task? Raw; internal bool Entered; }
        internal bool IsHealthy { get { lock (_gate) return _raw.Count == 0 && _resources.Count == 0 && _errors.Count == 0 && _faults.Count == 0; } }
        internal void Remember(Exception cause) { lock (_gate) _errors.Add(cause); }
        internal void Run(Action body) => Invoke(() => { body(); return 0; });
        internal T Invoke<T>(Func<T> body)
        {
            var active = 1; var used = 0; var thread = Environment.CurrentManagedThreadId; T result = default!;
            var failures = new List<Exception>();
            void Record(Exception cause)
            { lock (failures) failures.Add(cause); Remember(cause); }
            T InvokeBody()
            {
                result = body(); Capture(result); return result;
            }
            void Callback()
            {
                if (Interlocked.Exchange(ref used, 1) != 0 || Volatile.Read(ref active) == 0 || thread != Environment.CurrentManagedThreadId)
                { var failure = new InvalidOperationException("The original Files state callback is inactive, foreign-thread or consumed."); Record(failure); throw failure; }
                try { InvokeBody(); }
                catch (Exception cause) { Record(cause); throw; }
            }
            try { OwningCleanup(() => scope(Callback)); }
            catch (Exception cause) { lock (failures) if (!failures.Any(value => ReferenceEquals(value, cause))) Record(cause); }
            finally { Volatile.Write(ref active, 0); }
            if (used == 0) Record(new InvalidOperationException("The original Files state callback was not invoked."));
            Exception[] failuresAtReturn; lock (failures) failuresAtReturn = failures.ToArray();
            if (failuresAtReturn.Length == 1) ExceptionDispatchInfo.Capture(failuresAtReturn[0]).Throw();
            if (failuresAtReturn.Length != 0) throw new AggregateException("Original Files callback and scope failures are independently retained.", failuresAtReturn);
            return result;
        }
        internal void OwningCleanup(Action body)
        {
            var physical = OriginalUpdatePhysical ??= new(ReferenceEqualityComparer.Instance);
            physical.TryGetValue(owner._gate, out var depth); physical[owner._gate] = depth + 1;
            try { body(); } finally { if (depth == 0) physical.Remove(owner._gate); else physical[owner._gate] = depth; }
        }
        internal void Retain(Task actual)
        {
            ArgumentNullException.ThrowIfNull(actual);
            lock (_gate) if (!_raw.Any(value => ReferenceEquals(value, actual))) _raw.Add(actual);
            Run(() => retainer(actual));
        }
        internal void PublishDriver(Task actual) => Run(() => retainer(actual));
        internal void Capture<T>(T actual)
        {
            if (actual is not IDisposable && actual is not IAsyncDisposable) return;
            lock (_gate) if (!_resources.Any(value => ReferenceEquals(value, actual))) _resources.Add(actual!);
        }
        internal async Task<T> Read<T>(Func<Task<T>> factory)
        {
            Task<T>? raw = null; T result = default!; Exception? acquisition = null;
            try { Run(() => { raw = factory() ?? throw new InvalidOperationException("No original Files Task was returned."); Retain(raw); }); }
            catch (Exception cause) { acquisition = cause; }
            Exception? terminal = null;
            if (raw is not null) try { result = await raw.ConfigureAwait(false); Capture(result); MarkJoined(raw); }
                catch (Exception cause) { terminal = CaptureFault(raw, cause); }
            ThrowAcquisition(acquisition, terminal); return result;
        }
        internal async Task Read(Func<Task> factory, Action? afterSuccessfulJoin = null)
        {
            Task? raw = null; Exception? acquisition = null;
            try { Run(() => { raw = factory() ?? throw new InvalidOperationException("No original Files Task was returned."); Retain(raw); }); }
            catch (Exception cause) { acquisition = cause; }
            Exception? terminal = null;
            if (raw is not null) try { await raw.ConfigureAwait(false); afterSuccessfulJoin?.Invoke(); MarkJoined(raw); }
                catch (Exception cause) { terminal = CaptureFault(raw, cause); }
            ThrowAcquisition(acquisition, terminal);
        }
        private static void ThrowAcquisition(Exception? acquisition, Exception? terminal)
        {
            if (acquisition is not null && terminal is not null) throw new AggregateException("Original Files acquisition and raw source both failed.", acquisition, terminal);
            if (acquisition is not null) ExceptionDispatchInfo.Capture(acquisition).Throw();
            if (terminal is not null) ExceptionDispatchInfo.Capture(terminal).Throw();
        }
        private void MarkJoined(Task raw) { if (raw.IsCompletedSuccessfully) lock (_gate) _raw.RemoveAll(value => ReferenceEquals(value, raw)); }
        private Exception CaptureFault(Task raw, Exception observed)
        {
            if (raw.IsFaulted && raw.Exception is { } clr)
            { lock (_gate) _faults[raw] = clr.InnerExceptions.ToArray(); return clr; }
            lock (_gate) _faults[raw] = [observed]; return observed;
        }
        internal Task Close(object resource)
        {
            OriginalClose record; TaskCompletionSource? start = null;
            lock (_gate)
            {
                if (_closes.TryGetValue(resource, out record!)) return record.Driver;
                if (!_resources.Any(value => ReferenceEquals(value, resource))) throw new UnauthorizedAccessException("This source did not acquire the actual Files cleanup object.");
                record = new(); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                record.Driver = DriveClose(start.Task, resource, record); _closes.Add(resource, record);
            }
            start.SetResult(); return record.Driver;
        }
        private async Task DriveClose(Task start, object resource, OriginalClose record)
        {
            await start.ConfigureAwait(false); Exception? acquisition = null, terminal = null;
            Task Acquire()
            {
                record.Entered = true;
                record.Raw = resource is IAsyncDisposable asyncResource ? asyncResource.DisposeAsync().AsTask() : DisposeSynchronous((IDisposable)resource);
                // Receipt is cached inside the productive callback, before any postguard.
                Retain(record.Raw); return record.Raw;
            }
            try { Run(() => Acquire()); } catch (Exception cause) { acquisition = cause; }
            if (!record.Entered)
                try { OwningCleanup(() => Acquire()); } catch (Exception cause) { Remember(cause); acquisition = acquisition is null ? cause : new AggregateException(acquisition, cause); }
            if (record.Raw is not null)
                try { await record.Raw.ConfigureAwait(false); MarkJoined(record.Raw); }
                catch (Exception cause) { terminal = CaptureFault(record.Raw, cause); }
            ThrowAcquisition(acquisition, terminal);
            if (record.Raw is null) throw new InvalidOperationException("The original entered Files cleanup has no returned receipt.");
            lock (_gate) _resources.RemoveAll(value => ReferenceEquals(value, resource));
        }
        private static Task DisposeSynchronous(IDisposable resource) { resource.Dispose(); return Task.CompletedTask; }
        internal bool IsResourceHealthyClosed(object resource)
        { lock (_gate) return _closes.TryGetValue(resource, out var close) && close.Driver.IsCompletedSuccessfully; }
        internal async Task CloseResources()
        {
            object[] resources; lock (_gate) resources = _resources.AsEnumerable().Reverse().ToArray();
            var errors = new List<Exception>();
            foreach (var resource in resources) try { await Close(resource).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            if (errors.Count != 0) throw new AggregateException("Every original Files cleanup object was independently joined.", errors);
        }
        internal async Task JoinRaw()
        {
            Task[] raw; lock (_gate) raw = _raw.ToArray();
            foreach (var actual in raw) try { await actual.ConfigureAwait(false); MarkJoined(actual); } catch (Exception cause) { CaptureFault(actual, cause); }
            ThrowRemembered();
        }
        internal void ThrowRemembered(Exception? primary = null)
        {
            Exception[] errors; lock (_gate) errors = _errors.Concat(_faults.Values.SelectMany(value => value)).ToArray();
            if (primary is not null && !errors.Any(value => ReferenceEquals(value, primary))) errors = [primary, .. errors];
            if (errors.Length != 0) throw new AggregateException("Original Files update/source/cleanup failures remain retained.", errors);
        }
    }
}
