using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Apps.MiniComputer;

namespace HavenOS.Apps.Assistants.MiniComputer;

/// <summary>Admitted presentation/source command custody, sharing the actual Mini
/// engine callback helper. It has no VM lifecycle or resource permission behavior.</summary>
internal sealed class AssistantMiniComputerOriginals
{
    private readonly object _gate = new();
    private readonly Dictionary<Task, MiniComputerOriginalInvocation> _commands = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Task, HomeOriginalLocalStoreImportSession> _homeImports = new(ReferenceEqualityComparer.Instance);
    internal void RetainOriginalHomeImportSource(Task actual, HomeOriginalLocalStoreImportSession owner)
    {
        lock (_gate)
        {
            if (_homeImports.TryGetValue(actual, out var prior) && !ReferenceEquals(prior, owner))
                throw new InvalidOperationException("The original import source has a different Home issuer.");
            _homeImports[actual] = owner;
        }
    }
    private bool _retiring; private Task? _close;
    internal Task<T> Run<T>(Action<Action> scope, Action<Task> retain,
        Func<MiniComputerOriginalInvocation, Task<T>> body)
    {
        TaskCompletionSource start; Task<T> actual;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            foreach (var completed in _commands.Keys.Where(task => task.IsCompletedSuccessfully).ToArray()) _commands.Remove(completed);
            foreach (var pair in _homeImports.Where(pair => pair.Key.IsCompletedSuccessfully || pair.Value.IsAcknowledgedOriginalSource(pair.Key)).ToArray())
                _homeImports.Remove(pair.Key);
            if (_commands.Count >= 128) throw new InvalidOperationException("Original Mini Computer commands require inspection.");
            var source = new MiniComputerOriginalInvocation(this, scope, retain);
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = Drive(start.Task, source, body); _commands.Add(actual, source);
        }
        start.SetResult(); return actual;
    }
    private async Task<T> Drive<T>(Task start, MiniComputerOriginalInvocation source,
        Func<MiniComputerOriginalInvocation, Task<T>> body)
    {
        await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        T value = default!;
        try { value = await body(source).ConfigureAwait(false); }
        catch (Exception cause) { source.Remember(cause); }
        await source.CloseAsync().ConfigureAwait(false); return value;
    }
    internal Task Cleanup(Task sameParent, Action<Action> scope, Action<Task> retain,
        Func<MiniComputerOriginalInvocation, Task> body)
    {
        TaskCompletionSource start; Task actual;
        lock (_gate)
        {
            if (_close?.IsCompleted == true || sameParent.IsCompleted || !_commands.ContainsKey(sameParent))
                throw new InvalidOperationException("Original Mini Computer cleanup requires its SAME still-live admitted parent.");
            var source = new MiniComputerOriginalInvocation(this, scope, retain);
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = Drive(start.Task, source, async current => { await body(current).ConfigureAwait(false); return true; });
            _commands.Add(actual, source);
        }
        start.SetResult(); return actual;
    }
    internal Task? OriginalClose { get { lock (_gate) return _close; } }
    internal void DemandExternalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    internal void RequestRetirement() { lock (_gate) _retiring = true; }
    internal Task CloseAndDrain()
    {
        DemandExternalJoin(); Task actual; TaskCompletionSource? start = null;
        lock (_gate)
        {
            _retiring = true;
            if (_close is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Drain(start.Task); }
            actual = _close;
        }
        start?.SetResult(); return actual;
    }
    private async Task Drain(Task start)
    {
        await start.ConfigureAwait(false);
        var errors = new List<Exception>();
        var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            Task[] all; lock (_gate) all = _commands.Keys.Where(joined.Add).ToArray();
            if (all.Length == 0) break;
            foreach (var actual in all) try { await actual.ConfigureAwait(false); }
                catch (Exception cause) { errors.Add(actual.Exception ?? cause); }
        }
        KeyValuePair<Task, HomeOriginalLocalStoreImportSession>[] imports;
        lock (_gate) imports = _homeImports.ToArray();
        foreach (var pair in imports) try { await pair.Key.ConfigureAwait(false); }
            catch (Exception cause) { if (!pair.Value.IsAcknowledgedOriginalSource(pair.Key)) errors.Add(pair.Key.Exception ?? cause); }
        MiniComputerOriginalInvocation.Throw(errors);
    }
}
