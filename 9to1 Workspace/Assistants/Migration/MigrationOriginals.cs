using System.Runtime.ExceptionServices;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Assistants.Migration;

/// <summary>Retains actual source/command tasks and their independent faults. Only a locally
/// issued pre-effect refusal with successful sources or their exact propagation of that local
/// refusal after cleanup is acknowledged. Foreign faults and mixed failures remain retained.</summary>
internal sealed class MigrationOriginals
{
    private sealed class Invocation
    {
        internal Task Command = null!;
        internal readonly List<Task> Sources = [];
        internal readonly Dictionary<Task, HomeOriginalLocalStoreImportSession> ImportOwners = new(ReferenceEqualityComparer.Instance);
        internal LegacyAgentMigrationRefusedException? Refusal;
        internal bool EffectStarted;
        internal bool Active;
    }
    private readonly object _gate = new();
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly List<Invocation> _originals = [];
    private readonly AsyncLocal<Invocation?> _current = new();
    [ThreadStatic] private static MigrationOriginals? _physicalSource;
    private bool _retired;
    private Task? _close;

    internal Task? OriginalClose { get { lock (_gate) return _close; } }
    internal Task<T> Admit<T>(Func<Task<T>> body)
    {
        lock (_gate)
        {
            if (_retired) throw new ObjectDisposedException("Saved Agent migration presentation");
            _originals.RemoveAll(item => item.Command.IsCompletedSuccessfully && item.Sources.All(source => source.IsCompletedSuccessfully || IsRecoveredImportSource(item, source)) || Acknowledged(item));
            if (_originals.Count >= 64) throw new InvalidOperationException("Migration originals require inspection before more work.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var invocation = new Invocation();
            invocation.Command = DriveAsync(start.Task, invocation, body);
            _originals.Add(invocation); start.SetResult();
            return (Task<T>)invocation.Command;
        }
    }
    private async Task<T> DriveAsync<T>(Task start, Invocation invocation, Func<Task<T>> body)
    {
        await start.ConfigureAwait(false);
        await _serial.WaitAsync().ConfigureAwait(false);
        _current.Value = invocation; invocation.Active = true;
        try { return await body().ConfigureAwait(false); }
        finally { invocation.Active = false; _current.Value = null; _serial.Release(); }
    }
    internal void Run(Action source)
    {
        var invocation = Current();
        var prior = _physicalSource; _physicalSource = this;
        try { source(); }
        catch (Exception failure)
        {
            lock (_gate) invocation.Sources.Add(Task.FromException(failure));
            throw;
        }
        finally { _physicalSource = prior; }
    }
    internal void Retain(Task actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        var invocation = Current();
        lock (_gate)
            if (!invocation.Sources.Any(prior => ReferenceEquals(prior, actual))) invocation.Sources.Add(actual);
    }
    internal void RetainImportSource(Task actual, HomeOriginalLocalStoreImportSession sameOwner)
    {
        Retain(actual); var invocation = Current();
        lock (_gate)
        {
            if (invocation.ImportOwners.TryGetValue(actual, out var prior) && !ReferenceEquals(prior, sameOwner))
                throw new InvalidOperationException("The retained import source belongs to another original Home session.");
            invocation.ImportOwners[actual] = sameOwner;
        }
    }
    private static bool IsRecoveredImportSource(Invocation item, Task actual) =>
        item.ImportOwners.TryGetValue(actual, out var owner) && owner.IsAcknowledgedOriginalSource(actual);
    internal Task<T> Source<T>(Func<Task<T>> source)
    {
        Task<T>? actual = null;
        Run(() => { actual = source() ?? throw new InvalidOperationException("The migration source returned no actual Task."); Retain(actual); });
        return actual!;
    }
    internal void EffectStarting() => Current().EffectStarted = true;
    internal LegacyAgentMigrationRefusedException Refuse(string code, string message)
    {
        var invocation = Current();
        var refusal = new LegacyAgentMigrationRefusedException(code, message);
        if (!invocation.EffectStarted) invocation.Refusal = refusal;
        return refusal;
    }
    private Invocation Current() => _current.Value is { Active: true } current
        ? current : throw new InvalidOperationException("An admitted migration operation is required.");
    private static bool Acknowledged(Invocation item) => !item.Active && !item.EffectStarted &&
        item.Refusal is { } refusal && item.Command.IsFaulted &&
        item.Command.Exception!.InnerExceptions.Count == 1 &&
        ReferenceEquals(item.Command.Exception.InnerException, refusal) && item.Sources.All(source =>
            source.IsCompletedSuccessfully || IsRecoveredImportSource(item, source) || source.IsFaulted && source.Exception!.InnerExceptions.Count == 1 &&
            ReferenceEquals(source.Exception.InnerException, refusal));
    internal bool IsAcknowledged(Task actual)
    { lock (_gate) return _originals.Any(item => ReferenceEquals(item.Command, actual) && Acknowledged(item)); }
    internal void RequestRetirement() { lock (_gate) _retired = true; }
    internal void DemandExternalJoin()
    {
        if (ReferenceEquals(_physicalSource, this) || _current.Value is { Active: true })
            throw new InvalidOperationException("A migration callback cannot join its own presentation.");
    }
    internal Task CloseAndDrainAsync()
    {
        DemandExternalJoin();
        lock (_gate)
        {
            _retired = true;
            if (_close is not null) return _close;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = DrainAsync(start.Task, _originals.ToArray()); start.SetResult(); return _close;
        }
    }
    private static async Task DrainAsync(Task start, Invocation[] originals)
    {
        await start.ConfigureAwait(false);
        var failures = new List<Exception>();
        foreach (var item in originals)
        {
            try { await item.Command.ConfigureAwait(false); }
            catch (Exception cause) { if (!Acknowledged(item)) Add(failures, item.Command, cause); }
            foreach (var source in item.Sources)
                try { await source.ConfigureAwait(false); }
                catch (Exception cause) { if (!Acknowledged(item) && !IsRecoveredImportSource(item, source)) Add(failures, source, cause); }
        }
        if (failures.Count > 0) throw new AggregateException("Migration originals did not drain cleanly; retain their source owners.", failures);
    }
    private static void Add(List<Exception> failures, Task actual, Exception observed)
    {
        foreach (var cause in actual.Exception?.InnerExceptions.ToArray() ?? [observed])
            if (!failures.Any(prior => ReferenceEquals(prior, cause))) failures.Add(cause);
    }
    internal static void ThrowCombined(List<Exception> errors)
    {
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Migration operation and independent cleanup failed.", errors);
    }
}
