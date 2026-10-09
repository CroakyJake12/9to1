using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesNativeBrowserService
{
    public sealed partial class BrowserDownloadFilesOwner
    {
        private sealed class Command
        {
            internal Task Driver = null!;
            internal Sources Source = null!;
            internal volatile bool Live = true;
        }
        // Custody of this owner's actual finite sources, not a new permission or
        // transfer framework. Every accepted raw source is rooted before callbacks.
        private sealed class Sources(BrowserDownloadFilesOwner owner, Action<Action> scope, Action<Task> retain)
        {
            internal Command OriginalCommand = null!;
            private readonly object _gate = new();
            private readonly HashSet<Task> _raw = new(ReferenceEqualityComparer.Instance);
            private sealed class Observation { internal Task Driver = null!; }
            private readonly Dictionary<Task, Observation> _observations = new(ReferenceEqualityComparer.Instance);
            private readonly List<Exception> _errors = [];
            internal void Invoke(Action body)
            {
                var thread = Environment.CurrentManagedThreadId; int phase = 1, used = 0;
                var local = new List<Exception>();
                void Record(Exception error) { lock (local) Add(local, error); }
                void Once()
                {
                    Exception? refusal = Volatile.Read(ref phase) == 0
                        ? new InvalidOperationException("The original download Files callback ended.")
                        : Interlocked.CompareExchange(ref used, 1, 0) != 0
                            ? new InvalidOperationException("The original download Files callback cannot run twice.")
                            : Environment.CurrentManagedThreadId != thread
                                ? new InvalidOperationException("The original download Files callback changed thread.") : null;
                    if (refusal is not null) { Record(refusal); throw refusal; }
                    try { owner.Physical(OriginalCommand, body); } catch (Exception error) { Record(error); throw; }
                }
                try
                {
                    try { scope(Once); if (Volatile.Read(ref used) == 0) Record(new InvalidOperationException("The original source omitted its callback.")); }
                    catch (Exception error) { Record(error); }
                }
                finally { Volatile.Write(ref phase, 0); }
                Exception[] failures; lock (local) failures = local.ToArray();
                lock (_gate) foreach (var error in failures) Add(_errors, error);
                Throw(failures);
            }
            internal T Invoke<T>(Func<T> body) { T value = default!; Invoke(() => { value = body(); }); return value; }
            internal void Retain(Task actual)
            {
                ArgumentNullException.ThrowIfNull(actual);
                Exception? capacity = null;
                lock (_gate)
                {
                    if (!_raw.Contains(actual))
                    {
                        if (_raw.Count >= 2048)
                        {
                            capacity = new InvalidOperationException("Join the original download Files source cohort before more work.");
                            Add(_errors, capacity);
                        }
                        // A caller retainer sees an already accepted source. Capture it
                        // even on refusal; pre-factory admission below prevents new work.
                        _raw.Add(actual);
                        var observation = new Observation(); _observations.Add(actual, observation);
                        // Publication precedes observation. An already completed raw
                        // is independently awaited here, rather than queued for a
                        // status-only prune or a delayed observer-capacity race.
                        observation.Driver = ObserveAccepted(actual);
                    }
                }
                Invoke(() => { retain(actual); if (capacity is not null) ExceptionDispatchInfo.Capture(capacity).Throw(); });
                async Task ObserveAccepted(Task sameRaw)
                {
                    try
                    {
                        await sameRaw.ConfigureAwait(false);
                        lock (_gate) { _raw.Remove(sameRaw); _observations.Remove(sameRaw); }
                    }
                    catch (Exception error) { lock (_gate) Capture(_errors, sameRaw, error); }
                }
            }
            private void DemandCapacity()
            {
                lock (_gate) if (_raw.Count >= 2048)
                    throw new InvalidOperationException("Join the original download Files source cohort before more work.");
            }
            internal async Task<T> Read<T>(Func<Task<T>> factory, Action<T>? capture = null)
            {
                Task<T>? actual = null; T value = default!; var errors = new List<Exception>();
                try { Invoke(() => { DemandCapacity(); actual = factory() ?? throw new InvalidOperationException("No actual original source Task."); Retain(actual); }); }
                catch (Exception error) { Add(errors, error); }
                if (actual is not null)
                {
                    try
                    {
                        value = await actual.ConfigureAwait(false);
                        // Capture actual cleanup-bearing products before publishing any
                        // earlier callback refusal; this is private custody only.
                        if (capture is not null) owner.Physical(() => capture(value));
                        lock (_gate) _raw.Remove(actual);
                    }
                    catch (Exception error) { Capture(errors, actual, error); }
                }
                lock (_gate) foreach (var error in errors) Add(_errors, error);
                Throw(errors); return value;
            }
            internal async Task Read(Func<Task> factory, Action? captureSuccess = null)
            {
                Task? actual = null; var errors = new List<Exception>();
                try { Invoke(() => { DemandCapacity(); actual = factory() ?? throw new InvalidOperationException("No actual original source Task."); Retain(actual); }); }
                catch (Exception error) { Add(errors, error); }
                if (actual is not null)
                {
                    try { await actual.ConfigureAwait(false); if (captureSuccess is not null) owner.Physical(OriginalCommand, captureSuccess); lock (_gate) _raw.Remove(actual); }
                    catch (Exception error) { Capture(errors, actual, error); }
                }
                lock (_gate) foreach (var error in errors) Add(_errors, error);
                Throw(errors);
            }
            internal async Task Join()
            {
                Task[] actuals; Task[] observations;
                lock (_gate) { actuals = _raw.ToArray(); observations = _observations.Values.Select(record => record.Driver).ToArray(); }
                foreach (var actual in actuals)
                {
                    try { await actual.ConfigureAwait(false); lock (_gate) _raw.Remove(actual); }
                    catch (Exception error) { lock (_gate) Capture(_errors, actual, error); }
                }
                foreach (var observation in observations) await observation.ConfigureAwait(false);
                Exception[] errors; lock (_gate) errors = _errors.ToArray(); Throw(errors);
            }
            internal bool Healthy { get { lock (_gate) return _raw.Count == 0 && _observations.Count == 0 && _errors.Count == 0; } }
        }
        private readonly object _gate = new();
        private readonly SemaphoreSlim _serial = new(1, 1);
        private readonly HashSet<Command> _commands = [];
        private readonly AsyncLocal<Command?> _executing = new();
        private readonly AsyncLocal<bool> _closing = new();
        [ThreadStatic] private static Dictionary<BrowserDownloadFilesOwner, int>? _physical;
        [ThreadStatic] private static Dictionary<Command, int>? _physicalCommands;
        private bool _retiring;
        private Task? _close;
        private void Physical(Action body)
        {
            var physical = _physical ??= new(ReferenceEqualityComparer.Instance);
            physical.TryGetValue(this, out var depth); physical[this] = depth + 1;
            try { body(); } finally { if (depth == 0) physical.Remove(this); else physical[this] = depth; }
        }
        private void Physical(Command command, Action body)
        {
            var physical = _physicalCommands ??= new(ReferenceEqualityComparer.Instance);
            physical.TryGetValue(command, out var depth); physical[command] = depth + 1;
            try { Physical(body); } finally { if (depth == 0) physical.Remove(command); else physical[command] = depth; }
        }
        private Task<T> Run<T>(Action<Action> scope, Action<Task> retain, Func<Sources, Task<T>> body)
        {
            ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Command command;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_retiring, this);
                _commands.RemoveWhere(item => !item.Live && item.Driver.IsCompletedSuccessfully && item.Source.Healthy);
                if (_commands.Count >= 64) throw new InvalidOperationException("Close unresolved download Files commands before more work.");
                command = new() { Source = new(this, scope, retain) };
                command.Source.OriginalCommand = command;
                command.Driver = Driver(start.Task); _commands.Add(command);
            }
            start.SetResult(); return (Task<T>)command.Driver;
            async Task<T> Driver(Task begin)
            {
                await begin.ConfigureAwait(false); var prior = _executing.Value; _executing.Value = command;
                var entered = false;
                try
                {
                    await command.Source.Read(() => _serial.WaitAsync(), () => entered = true).ConfigureAwait(false);
                    return await command.Source.Read(() => body(command.Source)).ConfigureAwait(false);
                }
                finally
                {
                    if (entered) _serial.Release();
                    command.Live = false; _executing.Value = prior;
                }
            }
        }
        private static void Add(List<Exception> errors, Exception error)
        { if (!errors.Any(actual => ReferenceEquals(actual, error))) errors.Add(error); }
        private static void Capture(List<Exception> errors, Task actual, Exception observed)
        {
            if (actual.Exception is { } clr) foreach (var cause in clr.InnerExceptions) Add(errors, cause);
            else Add(errors, observed);
        }
        private static void Throw(IEnumerable<Exception> actual)
        {
            var errors = actual.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
            if (errors.Length == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
            if (errors.Length != 0) throw new AggregateException("Original Browser Files sources must independently settle.", errors);
        }
        public Task? OriginalClose { get { lock (_gate) return _close; } }
        public void DemandExternalOriginalRetirementJoin()
        {
            if (_closing.Value || _executing.Value is not null || _physical?.ContainsKey(this) == true)
                throw new InvalidOperationException("The actual Browser Files owner cannot join its own command or callback.");
        }
        public void RequestRetirement() { lock (_gate) _retiring = true; }
        public Task CloseAndDrainAsync()
        {
            DemandExternalOriginalRetirementJoin(); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate) { if (_close is not null) return _close; _retiring = true; _close = Close(start.Task); }
            start.SetResult(); return _close;
            async Task Close(Task begin)
            {
                await begin.ConfigureAwait(false); _closing.Value = true; Command[] commands;
                lock (_gate) commands = _commands.ToArray();
                var errors = new List<Exception>();
                foreach (var command in commands)
                {
                    try { await command.Driver.ConfigureAwait(false); } catch (Exception error) { Capture(errors, command.Driver, error); }
                    try { await command.Source.Join().ConfigureAwait(false); } catch (Exception error) { Add(errors, error); }
                }
                Registration[] registrations; DurableDriveProvider[] providers;
                lock (_gate) { registrations = _operations.Values.ToArray(); providers = _providers.ToArray(); }
                foreach (var registration in registrations)
                    try { await CloseRegistrationResources(registration).ConfigureAwait(false); } catch (Exception error) { Add(errors, error); }
                // Close only this provider's NEW original-update cohort after every
                // accepted caller/raw child has joined. Ordinary Files is borrowed.
                foreach (var provider in providers)
                {
                    Task? actual = null;
                    try { Physical(() => actual = provider.CloseOriginalBrowserDownloadsAndDrainAsync()); }
                    catch (Exception error) { Add(errors, error); }
                    if (actual is not null) try { await actual.ConfigureAwait(false); } catch (Exception error) { Capture(errors, actual, error); }
                }
                Throw(errors);
            }
        }
    }
}
