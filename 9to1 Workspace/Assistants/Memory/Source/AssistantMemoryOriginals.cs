using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Assistants.Memory;

/// <summary>Owns admitted product source calls; raw source tasks are also retained by the
/// actual caller before awaiting. No fault, canceled wrapper or cleanup is waived by type.</summary>
internal sealed class AssistantMemoryOriginals
{
    private readonly object _gate = new();
    private readonly List<Task> _commands = [];
    // Escaped/repeated callbacks and independent caller protocol failures stay
    // with this owner even after their successful original command was joined.
    private readonly List<Exception> _unexpectedCallbacks = [];
    private void RetainUnexpectedCallback(Exception cause)
    {
        lock (_gate) if (!_unexpectedCallbacks.Any(value => ReferenceEquals(value, cause))) _unexpectedCallbacks.Add(cause);
    }
    private void DemandNoUnexpectedCallbacks()
    { List<Exception> failures; lock (_gate) failures = _unexpectedCallbacks.ToList(); Throw(failures); }
    private void AddUnexpectedCallbacks(List<Exception> failures)
    { lock (_gate) foreach (var cause in _unexpectedCallbacks)
        if (!failures.Any(value => ReferenceEquals(value, cause))) failures.Add(cause); }
    private readonly Dictionary<Task, HomeOriginalLocalStoreImportSession> _homeImportSources = new(ReferenceEqualityComparer.Instance);
    private readonly AsyncLocal<int> _logical = new();
    [ThreadStatic] private static AssistantMemoryOriginals? _physical;
    private bool _retiring;
    private Task? _close;
    private readonly ConditionalWeakTable<Task, OriginalRefusalProof> _refusals = new();
    private sealed record OriginalRefusalProof(Func<Task, bool> Check);
    internal void RegisterOriginalRefusalProof(Task actual, Func<Task, bool> proof) => _refusals.Add(actual, new(proof));
    internal Task<T> Admit<T>(Func<Task<T>> body)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            Throw(_unexpectedCallbacks.ToList());
            _commands.RemoveAll(command => command.IsCompletedSuccessfully);
            foreach (var actual in _homeImportSources.Where(pair => pair.Key.IsCompletedSuccessfully ||
                pair.Value.IsAcknowledgedOriginalSource(pair.Key)).Select(pair => pair.Key).ToArray())
                _homeImportSources.Remove(actual);
            if (_commands.Count >= 128) throw new InvalidOperationException("Assistant memory original custody requires inspection.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var command = Drive(start.Task, body); _commands.Add(command); start.SetResult(); return command;
        }
    }
    private void RetainHomeImportSource(Task actual, HomeOriginalLocalStoreImportSession sameOwner)
    {
        lock (_gate)
        {
            if (_homeImportSources.TryGetValue(actual, out var prior) && !ReferenceEquals(prior, sameOwner))
                throw new InvalidOperationException("An original import source belongs to a different Home issuer.");
            _homeImportSources[actual] = sameOwner;
        }
    }
    internal Task AdmitOriginalCleanup(Task sameLiveParent, Func<Task> body)
    {
        ArgumentNullException.ThrowIfNull(sameLiveParent); ArgumentNullException.ThrowIfNull(body);
        lock (_gate)
        {
            if (_close?.IsCompleted == true || sameLiveParent.IsCompleted ||
                !_commands.Any(command => ReferenceEquals(command, sameLiveParent)))
                throw new InvalidOperationException("New original cleanup requires its SAME still-live admitted memory parent.");
            // Only the private per-invocation cached wait/release use this leaf.
            // Retirement still accepts their descendants, but no first callback may
            // start after the real parent or owner close has already settled.
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var command = Drive(start.Task, async () => { await body().ConfigureAwait(false); return true; }, cleanupOnly: true);
            _commands.Add(command); start.SetResult(); return command;
        }
    }
    private async Task<T> Drive<T>(Task start, Func<Task<T>> body, bool cleanupOnly = false)
    {
        await start.ConfigureAwait(false); _logical.Value++;
        T result = default!; Task<T>? raw = null; var failures = new List<Exception>();
        try
        {
            if (!cleanupOnly) DemandNoUnexpectedCallbacks();
            raw = body(); result = await raw.ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            foreach (var cause in raw?.Exception?.InnerExceptions.ToArray() ?? [failure])
                if (!failures.Any(value => ReferenceEquals(value, cause))) failures.Add(cause);
        }
        finally { _logical.Value--; }
        // Cleanup children still release their already-owned resources. The enclosing
        // productive command and global drain retain the owner's unexpected cause.
        if (!cleanupOnly) AddUnexpectedCallbacks(failures);
        Throw(failures); return result;
    }
    internal Scope CreateScope(Action<Action> caller, Action<Task> retain) => new(this, caller, retain);
    internal Scope CreateCleanupScope(Action<Action> caller, Action<Task> retain) => new(this, caller, retain, cleanupOnly: true);
    internal void RequestRetirement() { lock (_gate) _retiring = true; }
    internal Task? OriginalClose { get { lock (_gate) return _close; } }
    internal void DemandExternalOriginalRetirementJoin()
    {
        if (_logical.Value != 0 || ReferenceEquals(_physical, this))
            throw new InvalidOperationException("A memory source cannot synchronously join its own original close.");
    }
    internal Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        lock (_gate)
        {
            _retiring = true;
            if (_close is not null)
            {
                if (_close.IsCompletedSuccessfully)
                {
                    _close.GetAwaiter().GetResult();
                    Throw(_unexpectedCallbacks.ToList());
                }
                return _close;
            }
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = Drain(start.Task); start.SetResult(); return _close;
        }
    }
    private async Task Drain(Task start)
    {
        await start.ConfigureAwait(false); var failures = new List<Exception>();
        var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            Task[] commands; lock (_gate) commands = _commands.Where(joined.Add).ToArray();
            if (commands.Length == 0) break;
            foreach (var command in commands)
                try { await command.ConfigureAwait(false); }
                catch (Exception failure)
                {
                    if (_refusals.TryGetValue(command, out var proof) && proof.Check(command)) continue;
                    foreach (var cause in command.Exception?.InnerExceptions.ToArray() ?? [failure])
                        if (!failures.Any(prior => ReferenceEquals(prior, cause))) failures.Add(cause);
                }
        }
        // A truthful AuditPending command may already be terminal. Its original
        // raw failure remains owned here until SAME Home audit recovery is proved.
        KeyValuePair<Task, HomeOriginalLocalStoreImportSession>[] imports;
        lock (_gate) imports = _homeImportSources.ToArray();
        foreach (var pair in imports)
            try { await pair.Key.ConfigureAwait(false); }
            catch (Exception failure)
            {
                if (pair.Value.IsAcknowledgedOriginalSource(pair.Key)) continue;
                foreach (var cause in pair.Key.Exception?.InnerExceptions.ToArray() ?? [failure])
                    if (!failures.Any(prior => ReferenceEquals(prior, cause))) failures.Add(cause);
            }
        lock (_gate) foreach (var cause in _unexpectedCallbacks)
            if (!failures.Any(value => ReferenceEquals(value, cause))) failures.Add(cause);
        Throw(failures);
    }
    internal static void Throw(List<Exception> failures)
    {
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Actual Assistant memory sources or cleanup did not settle.", failures);
    }
    internal sealed class Scope(AssistantMemoryOriginals owner, Action<Action> caller, Action<Task> retainer, bool cleanupOnly = false)
    {
        private readonly object _gate = new();
        private readonly List<Task> _raw = [];
        private readonly Dictionary<Task, HomeOriginalLocalStoreImportSession> _homeImports = new(ReferenceEqualityComparer.Instance);
        private readonly List<Exception> _sync = [];
        private bool _cleanupOnly = cleanupOnly;
        // Irreversible private transition after productive dispatch. It never
        // acknowledges owner faults and cannot admit a new productive command.
        internal void BeginOriginalCleanup() => _cleanupOnly = true;
        internal void Run(Action body) => RunCore(body, owningCleanup: false);
        private void RunCore(Action body, bool owningCleanup)
        {
            var cleanup = owningCleanup || _cleanupOnly;
            if (!cleanup) owner.DemandNoUnexpectedCallbacks();
            var active = 1; var used = 0; var thread = Environment.CurrentManagedThreadId;
            var prior = _physical; _physical = owner; var invocationFailures = new List<Exception>();
            void Remember(Exception failure, bool unexpected = false)
            {
                lock (_gate)
                {
                    if (!_sync.Any(value => ReferenceEquals(value, failure))) _sync.Add(failure);
                    if (!invocationFailures.Any(value => ReferenceEquals(value, failure))) invocationFailures.Add(failure);
                }
                if (unexpected) owner.RetainUnexpectedCallback(failure);
            }
            try
            {
                try
                {
                    caller(() =>
                    {
                        if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread ||
                            Interlocked.Exchange(ref used, 1) != 0)
                        {
                            var failure = new InvalidOperationException("The original memory callback expired, repeated or moved threads.");
                            Remember(failure, unexpected: true); throw failure;
                        }
                        try { if (!cleanup) owner.DemandNoUnexpectedCallbacks(); body(); }
                        catch (Exception failure) { Remember(failure); throw; }
                    });
                }
                catch (Exception failure)
                {
                    bool knownBody; lock (_gate) knownBody = invocationFailures.Any(value => ReferenceEquals(value, failure));
                    Remember(failure, unexpected: !knownBody);
                }
                if (Volatile.Read(ref used) == 0)
                    Remember(new InvalidOperationException("The actual memory source callback was not invoked."), unexpected: true);
            }
            finally { Volatile.Write(ref active, 0); _physical = prior; }
            // A borrowed callback may swallow a body/protocol fault or replace it with
            // another failure. Retain both actual causes; never infer success from return.
            Exception[] captured; lock (_gate) captured = invocationFailures.ToArray();
            var failures = captured.ToList();
            if (!cleanup) owner.AddUnexpectedCallbacks(failures);
            Throw(failures);
        }
        internal void Retain(Task actual)
        {
            lock (_gate) if (!_raw.Any(prior => ReferenceEquals(prior, actual))) _raw.Add(actual);
            retainer(actual); // Private capture precedes caller publication.
        }
        internal void RetainOriginalHomeImportSource(Task actual, HomeOriginalLocalStoreImportSession sameOwner)
        {
            owner.RetainHomeImportSource(actual, sameOwner);
            lock (_gate)
            {
                if (_homeImports.TryGetValue(actual, out var prior) && !ReferenceEquals(prior, sameOwner))
                    throw new InvalidOperationException("The SAME actual Home import owner is required.");
                _homeImports[actual] = sameOwner;
            }
            Retain(actual);
        }
        internal bool AcknowledgeOriginalRefusal(Task actual, Func<Task, bool> sourceProof)
        {
            if (!actual.IsFaulted || !sourceProof(actual)) return false;
            lock (_gate)
            {
                if (!_raw.Any(value => ReferenceEquals(value, actual))) return false;
                _raw.RemoveAll(value => ReferenceEquals(value, actual)); return true;
            }
        }
        internal void AcknowledgeOriginalRefusalOccurrences(Func<Task, bool> sourceProof)
        {
            Task[] originals; lock (_gate) originals = _raw.ToArray();
            foreach (var actual in originals) if (actual.IsFaulted) AcknowledgeOriginalRefusal(actual, sourceProof);
        }
        internal async Task<T> Read<T>(Func<Task<T>> factory, Action<T>? capture = null)
        {
            Task<T>? actual = null; T result = default!; var failures = new List<Exception>();
            try { Run(() => { actual = factory(); Retain(actual); }); } catch (Exception failure) { failures.Add(failure); }
            if (actual is not null)
                try { result = await actual.ConfigureAwait(false); capture?.Invoke(result); } catch (Exception failure) { failures.Add(failure); }
            if (!_cleanupOnly) owner.AddUnexpectedCallbacks(failures);
            Throw(failures); return actual is null ? throw new InvalidOperationException("No actual memory source Task was captured.") : result;
        }
        internal Task Read(Func<Task> factory) => ReadCore(factory, owningCleanup: false);
        internal Task ReadCleanup(Func<Task> originalClose) => ReadCore(originalClose, owningCleanup: true);
        private async Task ReadCore(Func<Task> factory, bool owningCleanup)
        {
            Task? actual = null; var failures = new List<Exception>();
            try { RunCore(() => { actual = factory(); Retain(actual); }, owningCleanup); } catch (Exception failure) { failures.Add(failure); }
            if (actual is not null) try { await actual.ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
            if (!owningCleanup && !_cleanupOnly) owner.AddUnexpectedCallbacks(failures);
            Throw(failures); if (actual is null) throw new InvalidOperationException("No actual memory source Task was captured.");
        }
        internal async Task JoinAsync(HomeOriginalLocalStoreImportSession? pendingOwner = null,
            HomeOriginalLocalStoreImportSession.Snapshot? pendingSnapshot = null)
        {
            var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance); var failures = new List<Exception>();
            while (true)
            {
                Task[] originals; lock (_gate) originals = _raw.Where(joined.Add).ToArray();
                if (originals.Length == 0) break;
                foreach (var original in originals)
                    try { await original.ConfigureAwait(false); }
                    catch (Exception observed)
                    {
                        HomeOriginalLocalStoreImportSession? home;
                        lock (_gate) _homeImports.TryGetValue(original, out home);
                        if (home is not null && (home.IsAcknowledgedOriginalSource(original) ||
                            ReferenceEquals(home, pendingOwner) && pendingSnapshot is not null &&
                            home.IsOwnedOriginalPendingSource(original, pendingSnapshot))) continue;
                        foreach (var failure in original.Exception?.InnerExceptions.ToArray() ?? [observed])
                            if (!failures.Any(prior => ReferenceEquals(prior, failure))) failures.Add(failure);
                    }
            }
            lock (_gate) foreach (var failure in _sync) if (!failures.Any(prior => ReferenceEquals(prior, failure))) failures.Add(failure);
            if (!_cleanupOnly) owner.AddUnexpectedCallbacks(failures);
            Throw(failures);
        }
    }
}
