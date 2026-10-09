namespace HavenOS.Apps.Motion;

public sealed partial class MotionCuiWorkspace
{
    private readonly object _originalGate = new();
    private readonly List<OriginalCommand> _originalCommands = [];
    private readonly List<Task> _originalSources = [];
    private readonly List<Exception> _originalFailures = [];
    private static readonly AsyncLocal<OriginalCommand?> LogicalOriginal = new();
    [ThreadStatic] private static PhysicalScope? _physicalOriginal;
    private volatile bool _retiring;
    private Task? _originalClose;
    private OriginalCommand? _activeCommand;
    public Task? OriginalClose { get { lock (_originalGate) return _originalClose; } }
    public bool IsOriginalClosePrepared { get { lock (_originalGate) return !_retiring && !_busy && !HasUnsavedChanges && _originalFailures.Count == 0 && _originalCommands.All(c => c.Task.IsCompletedSuccessfully) && _originalSources.All(s => s.IsCompletedSuccessfully); } }
    public Task<bool> PrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(IsOriginalClosePrepared);
    }
    private sealed class OriginalCommand(MotionCuiWorkspace owner, OriginalCommand? parent)
    {
        internal MotionCuiWorkspace Owner { get; } = owner;
        internal OriginalCommand? Parent { get; } = parent;
        internal required Task Task { get; init; }
        internal CancellationTokenSource? Cancellation;
    }
    private Task AdmitOriginalCommand(string command, object? parameter, CancellationToken caller)
    {
        lock (_originalGate)
        {
            if (_retiring) throw new ObjectDisposedException(nameof(MotionCuiWorkspace));
            if (!ValidateMarkerParameter(command, parameter) || IsActionAvailable(command) != true) throw new InvalidOperationException("Motion action is unavailable.");
            if (_retiring) throw new ObjectDisposedException(nameof(MotionCuiWorkspace));
            if (command == "9to1.Motion.Cancel") { CancelOriginalOperation(); return Task.CompletedTask; }
            _originalCommands.RemoveAll(completed => completed.Task.IsCompletedSuccessfully);
            _originalSources.RemoveAll(source => source.IsCompletedSuccessfully);
            var parent = LogicalOriginal.Value ?? _physicalOriginal?.Original;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            OriginalCommand? original = null;
            var task = DriveAsync();
            original = new(this, parent) { Task = task };
            _originalCommands.Add(original); // Retain before Changed, source acquisition, or caller callbacks.
            _busy = true;
            start.SetResult();
            return task;
            async Task DriveAsync()
            {
                await start.Task;
                var prior = LogicalOriginal.Value; LogicalOriginal.Value = original;
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(caller);
                original!.Cancellation = cancellation;
                lock (_originalGate) { _activeCommand = original; _operation = cancellation; }
                try
                {
                    using (EnterPhysicalOriginal()) Changed();
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (_retiring) return;
                    await DispatchCoreAsync(command, parameter, cancellation.Token);
                }
                catch (Exception cause)
                {
                    lock (_originalGate)
                    {
                        // Unknown/faulted/canceled source outcomes retain failure custody; no token/type waiver.
                        MotionOriginalFailures.Add(_originalFailures, cause);
                    }
                    throw;
                }
                finally
                {
                    lock (_originalGate) { _operation = null; _activeCommand = null; _busy = false; }
                    try { if (!_retiring) using (EnterPhysicalOriginal()) Changed(); }
                    catch (Exception cause) { lock (_originalGate) MotionOriginalFailures.Add(_originalFailures, cause); throw; }
                    finally { LogicalOriginal.Value = prior; }
                }
            }
        }
    }
    private IDisposable EnterPhysicalOriginal()
    {
        var scope = new PhysicalScope(this, LogicalOriginal.Value ?? _activeCommand, _physicalOriginal);
        _physicalOriginal = scope;
        return scope;
    }
    private sealed class PhysicalScope(MotionCuiWorkspace owner, OriginalCommand? original, PhysicalScope? previous) : IDisposable
    {
        internal MotionCuiWorkspace Owner { get; } = owner;
        internal OriginalCommand? Original { get; } = original;
        internal PhysicalScope? Previous { get; } = previous;
        public void Dispose() => _physicalOriginal = Previous;
    }
    private MotionOriginalSourceObserver OriginalSourceObserver => new(
        callback => { using (EnterPhysicalOriginal()) callback(); },
        source => { lock (_originalGate) _originalSources.Add(source); });
    private Task<T> SourceAsync<T>(Func<Task<T>> factory) => OriginalSourceObserver.AwaitAsync(factory);
    private Task SourceAsync(Func<Task> factory) => OriginalSourceObserver.AwaitAsync(factory);
    private void CancelOriginalOperation()
    {
        lock (_originalGate)
        {
            if (_activeCommand is not { } active) return;
            using (EnterPhysicalOriginal()) active.Cancellation?.Cancel();
        }
    }
    public void RequestRetirement()
    {
        lock (_originalGate) _retiring = true;
        // Seal publication/admission immediately. Accepted commands retain their source lifetime.
    }
    public void DemandExternalOriginalRetirementJoin()
    {
        static bool Includes(OriginalCommand? original, MotionCuiWorkspace owner)
        {
            for (var current = original; current is not null; current = current.Parent)
                if (ReferenceEquals(current.Owner, owner) && !current.Task.IsCompleted) return true;
            return false;
        }
        for (var physical = _physicalOriginal; physical is not null; physical = physical.Previous)
            if (ReferenceEquals(physical.Owner, this) || Includes(physical.Original, this))
                throw new InvalidOperationException("Motion original work cannot join its own retirement.");
        if (Includes(LogicalOriginal.Value, this))
            throw new InvalidOperationException("Motion original work cannot join its own retirement.");
    }
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        lock (_originalGate)
        {
            if (_originalClose is not null) return _originalClose;
            if (!_busy && HasUnsavedChanges) throw new InvalidOperationException("Apply or explicitly discard pending Motion fields before closing.");
            _retiring = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalClose = CloseAsync(start.Task);
            start.SetResult();
            return _originalClose;
        }
    }
    private async Task CloseAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var failures = new List<Exception>();
        OriginalCommand[] commands;
        lock (_originalGate) commands = _originalCommands.ToArray();
        foreach (var original in commands)
        {
            try { await original.Task.ConfigureAwait(false); }
            catch (Exception cause)
            {
                MotionOriginalFailures.AddTask(failures, original.Task, cause);
            }
        }
        if (HasUnsavedChanges) MotionOriginalFailures.Add(failures, new InvalidOperationException("Pending Motion fields remain unacknowledged after accepted work settled."));
        // Every productive command is terminal before playback cleanup is acquired.
        if (_playback is { } playback)
        {
            var close = SourceAsync(playback.CloseAndDrainAsync);
            try { await close.ConfigureAwait(false); }
            catch (Exception cause) { MotionOriginalFailures.AddTask(failures, close, cause); }
        }
        Task[] sources;
        lock (_originalGate) { sources = _originalSources.ToArray(); foreach (var cause in _originalFailures) MotionOriginalFailures.Add(failures, cause); }
        foreach (var source in sources)
        {
            try { await source.ConfigureAwait(false); }
            catch (Exception cause)
            {
                MotionOriginalFailures.AddTask(failures, source, cause);
            }
        }
        MotionOriginalFailures.Throw(failures);
        _playback = null; _disposed = true;
        PropertyChanged = null;
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
