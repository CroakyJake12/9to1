namespace HavenOS.Apps.Stacks.NativeUI;

public sealed partial class StackCuiWorkspace
{
    private readonly object _originalGate = new();
    private readonly Dictionary<Task, Task<bool>> _originalSources = new(ReferenceEqualityComparer.Instance);
    private Task? _originalCommand, _originalClose;
    public Task? OriginalCommand { get { lock (_originalGate) return _originalCommand; } }
    public Task? OriginalClose { get { lock (_originalGate) return _originalClose; } }
    private const int MaximumOriginalSources = 128;
    private sealed class Invocation(StackCuiWorkspace owner, Invocation? parent)
    { internal StackCuiWorkspace Owner { get; } = owner; internal Invocation? Parent { get; } = parent; internal bool Active = true; }
    private static readonly AsyncLocal<Invocation?> Logical = new();
    [ThreadStatic] private static Invocation? Physical;
    private sealed class Scope(Invocation invocation, Invocation? prior, bool physical) : IDisposable
    {
        public void Dispose() { invocation.Active = false; if (physical) Physical = prior; else Logical.Value = prior; }
    }
    private IDisposable EnterDriver()
    { var prior = Logical.Value; var current = new Invocation(this, prior); Logical.Value = current; return new Scope(current, prior, false); }
    private IDisposable EnterPhysical()
    { var prior = Physical; var current = new Invocation(this, prior); Physical = current; return new Scope(current, prior, true); }
    private void DemandExternalJoin()
    {
        for (var current = Logical.Value; current is not null; current = current.Parent)
            if (current.Active && ReferenceEquals(current.Owner, this)) throw new InvalidOperationException("A Stacks operation cannot join its own owner close.");
        for (var current = Physical; current is not null; current = current.Parent)
            if (current.Active && ReferenceEquals(current.Owner, this)) throw new InvalidOperationException("A Stacks callback cannot join its own owner close.");
    }
    private void CheckSourceCapacity()
    {
        foreach (var pair in _originalSources.Where(p => p.Value.IsCompletedSuccessfully && p.Value.Result).ToArray()) _originalSources.Remove(pair.Key);
        if (_originalSources.Count >= MaximumOriginalSources) throw new InvalidOperationException("Retained Stacks operations require recovery before further work.");
    }
    private void RetainOriginal(Task original)
    {
        lock (_originalGate)
        {
            if (_originalSources.ContainsKey(original)) return;
            CheckSourceCapacity();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalSources.Add(original, ObserveOriginalAsync(original, gate.Task)); gate.SetResult();
        }
    }
    private static async Task<bool> ObserveOriginalAsync(Task original, Task gate)
    { await gate.ConfigureAwait(false); try { await original.ConfigureAwait(false); return true; } catch (Exception) { return false; } }
    private Task<T> SourceAsync<T>(Func<Task<T>> acquire)
    {
        lock (_originalGate) CheckSourceCapacity();
        using var physical = EnterPhysical();
        var original = acquire() ?? throw new InvalidOperationException("Stacks did not return its actual operation.");
        RetainOriginal(original); return original;
    }
    private Task SourceAsync(Func<Task> acquire)
    {
        lock (_originalGate) CheckSourceCapacity();
        using var physical = EnterPhysical();
        var original = acquire() ?? throw new InvalidOperationException("Stacks did not return its actual operation.");
        RetainOriginal(original); return original;
    }
    private Task AdmitOriginalCommand(string command, object? parameter, CancellationToken token)
    {
        lock (_originalGate)
        {
            if (IsActionAvailable(command) != true) return Task.FromException(new InvalidOperationException("This Stacks action is currently unavailable."));
            if (command == "9to1.Stacks.OpenProject") _ = DemandRow<ProjectTarget>(parameter);
            else if (command == "9to1.Stacks.SelectDomain") _ = DemandRow<DomainTarget>(parameter);
            else if (command == "9to1.Stacks.SelectFile") _ = DemandRow<FileTarget>(parameter);
            else if (parameter is not null) return Task.FromException(new InvalidOperationException("This Stacks action does not accept a row target."));
            CheckSourceCapacity();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _busy = true;
            var original = _originalCommand = RunPublishedCommandAsync(gate.Task, command, parameter, token);
            RetainOriginal(original); gate.SetResult(); return original;
        }
    }
    private async Task RunPublishedCommandAsync(Task gate, string command, object? parameter, CancellationToken token)
    {
        using var invocation = EnterDriver(); await gate;
        var failures = new List<Exception>();
        try { Changed(); token.ThrowIfCancellationRequested(); await RunCommandAsync(command, parameter, token); }
        catch (Exception failure) { _failed = true; failures.Add(failure); _status = failure.Message; }
        finally
        {
            _busy = false;
            try { Changed(); } catch (Exception failure) { _failed = true; failures.Add(failure); }
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Stacks retains operation and presentation failures.", failures);
    }
    public Task CloseOriginalAsync()
    {
        DemandExternalJoin();
        lock (_originalGate)
        {
            if (_originalClose is not null) return _originalClose;
            if (HasDraft && !_failed) throw new InvalidOperationException("Apply or discard pending text before closing this Stacks workspace.");
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _retiring = true; _originalClose = ClosePublishedAsync(gate.Task); gate.SetResult(); return _originalClose;
        }
    }
    private async Task ClosePublishedAsync(Task gate)
    {
        using var invocation = EnterDriver(); await gate.ConfigureAwait(false);
        var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance); var failures = new List<Exception>();
        while (true)
        {
            Task[] originals;
            lock (_originalGate) originals = _originalSources.Keys.Append(OriginalInitialization)
                .Concat(_originalCommand is null ? [] : new[] { _originalCommand }).Where(t => !joined.Contains(t)).ToArray();
            if (originals.Length == 0) break;
            foreach (var original in originals)
            {
                joined.Add(original);
                try { await original.ConfigureAwait(false); }
                catch (Exception failure) { failures.Add(original.Exception ?? failure); }
            }
        }
        Task<bool>[] observations; lock (_originalGate) observations = _originalSources.Values.ToArray();
        foreach (var observation in observations)
            try { await observation.ConfigureAwait(false); } catch (Exception failure) { failures.Add(observation.Exception ?? failure); }
        if (failures.Count != 0) throw new AggregateException("Stacks retains its original failed operations.", failures);
        // Engines, Home grants and stores are borrowed. Their actual provider owner closes them separately.
    }
    public ValueTask DisposeAsync() => new(CloseOriginalAsync());
}
