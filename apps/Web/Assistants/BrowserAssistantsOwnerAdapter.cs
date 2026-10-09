using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;

namespace NineToOne.Web.Assistants;

/// <summary>One browser private-context acquisition. Retains actual factory/controller originals;
/// it supplies no Den, actor, resource permission or execution port itself.</summary>
public sealed class BrowserAssistantsOwnerAdapter : IBrowserPrivateContextParticipant
{
    private readonly IBrowserAssistantsCanonicalOwner _owner;
    private readonly object _gate = new();
    private readonly AsyncLocal<Invocation?> _current = new();
    [ThreadStatic] private static Dictionary<BrowserAssistantsOwnerAdapter, int>? _physicalSources;
    private readonly List<Exception> _revocationFailures = [];
    private Task<IAssistantCanonicalBridge>? _factory;
    private Task<bool>? _initialization;
    private AssistantsWorkspaceController? _controller;
    private Task? _close;
    private bool _revoked;
    private bool _authorityRevocationRequested;
    private sealed class Invocation { internal bool Live = true; }

    public BrowserAssistantsOwnerAdapter(IBrowserAssistantsCanonicalOwner owner) =>
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public bool IsCurrent { get { lock (_gate) return !_revoked; } }
    public Task<bool>? OriginalInitialization { get { lock (_gate) return _initialization; } }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public AssistantsWorkspaceController DemandCurrentController()
    {
        lock (_gate)
        {
            if (_revoked) throw new UnauthorizedAccessException("The Assistants context has been revoked.");
            if (_initialization?.IsCompletedSuccessfully != true || !_initialization.Result)
                throw new InvalidOperationException("The original Assistants acquisition has not initialized.");
            return _controller ?? throw new InvalidOperationException("The original controller is unavailable.");
        }
    }

    public Task<bool> InitializeOriginalAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource start; Task<bool> actual;
        lock (_gate)
        {
            if (_revoked) throw new ObjectDisposedException(nameof(BrowserAssistantsOwnerAdapter));
            if (_initialization is not null) return _initialization;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = InitializeCoreAsync(start.Task, cancellationToken); _initialization = actual;
        }
        start.TrySetResult(); return actual;
    }

    private async Task<bool> InitializeCoreAsync(Task start, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        var invocation = new Invocation(); var previous = _current.Value; _current.Value = invocation;
        try
        {
            Task<IAssistantCanonicalBridge> factory;
            using (EnterPhysicalSource()) factory = _owner.OpenOriginalBridgeAsync(token)
                ?? throw new InvalidOperationException("The actual browser Assistants factory returned no Task.");
            lock (_gate) _factory = factory;
            IAssistantCanonicalBridge bridge;
            try { bridge = await factory.ConfigureAwait(false); }
            catch when (factory.Exception is { InnerExceptions.Count: > 1 }) { throw factory.Exception!; }
            if (bridge is null) throw new InvalidOperationException("The actual browser Assistants factory returned no bridge.");
            var controller = new AssistantsWorkspaceController(bridge);
            bool revoked;
            lock (_gate) { _controller = controller; revoked = _revoked; }
            // Capture the SAME partially acquired owner before initialization/publication.
            if (revoked) { controller.RequestRetirement(); return false; }
            Task<AssistantsWorkspaceSnapshot> initialization;
            using (EnterPhysicalSource()) initialization = controller.InitializeAsync(token);
            await initialization.ConfigureAwait(false);
            lock (_gate) return !_revoked;
        }
        finally { invocation.Live = false; _current.Value = previous; }
    }

    public void RevokePrivateContext()
    {
        AssistantsWorkspaceController? controller;
        lock (_gate)
        {
            if (_authorityRevocationRequested) return;
            _authorityRevocationRequested = true; _revoked = true; controller = _controller;
        }
        // Fence our getters first, then revoke actual authority before controller callbacks.
        try { using (EnterPhysicalSource()) _owner.RevokePrivateContext(); }
        catch (Exception error) { lock (_gate) _revocationFailures.Add(error); }
        try { using (EnterPhysicalSource()) controller?.RequestRetirement(); }
        catch (Exception error) { lock (_gate) _revocationFailures.Add(error); }
        Exception[] failures; lock (_gate) failures = _revocationFailures.ToArray();
        if (failures.Length != 0) throw new AggregateException("Assistants authority revocation failed.", failures);
    }

    public void DemandExternalOriginalRetirementJoin()
    {
        if (_physicalSources?.ContainsKey(this) == true || _current.Value is { Live: true })
            throw new InvalidOperationException("An Assistants acquisition source cannot join its own browser owner.");
        _owner.DemandExternalOriginalRetirementJoin();
        AssistantsWorkspaceController? controller; lock (_gate) controller = _controller;
        controller?.DemandExternalOriginalRetirementJoin();
    }

    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        TaskCompletionSource start; Task actual;
        lock (_gate)
        {
            if (_close is not null) return _close;
            _revoked = true;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = DrainAsync(start.Task); _close = actual;
        }
        try { RevokePrivateContext(); } catch { /* SAME errors are retained and reported by the close original. */ }
        start.TrySetResult(); return actual;
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private async Task DrainAsync(Task start)
    {
        await start.ConfigureAwait(false); var errors = new List<Exception>();
        try { RevokePrivateContext(); } catch (Exception error) { Add(errors, error); }
        Task<bool>? initialization; lock (_gate) initialization = _initialization;
        if (initialization is not null) await Join(initialization, errors).ConfigureAwait(false);
        Task<IAssistantCanonicalBridge>? factory; AssistantsWorkspaceController? controller;
        lock (_gate) { factory = _factory; controller = _controller; }
        if (factory is not null) await Join(factory, errors).ConfigureAwait(false);
        Task? controllerClose = null, ownerClose = null;
        try { if (controller is not null) using (EnterPhysicalSource()) controllerClose = controller.CloseAndDrainAsync(); }
        catch (Exception error) { Add(errors, error); }
        if (controllerClose is not null) await Join(controllerClose, errors).ConfigureAwait(false);
        try { using (EnterPhysicalSource()) ownerClose = _owner.CloseAndDrainAsync(); }
        catch (Exception error) { Add(errors, error); }
        if (ownerClose is null) Add(errors, new InvalidOperationException("The actual browser Assistants owner returned no close Task."));
        else await Join(ownerClose, errors).ConfigureAwait(false);
        lock (_gate) foreach (var error in _revocationFailures) Add(errors, error);
        if (errors.Count != 0) throw new AggregateException("Browser Assistants originals did not drain cleanly.", errors);
    }

    private IDisposable EnterPhysicalSource()
    {
        var sources = _physicalSources ??= [];
        sources[this] = sources.GetValueOrDefault(this) + 1;
        return new PhysicalSourceScope(this);
    }
    private sealed class PhysicalSourceScope(BrowserAssistantsOwnerAdapter owner) : IDisposable
    {
        public void Dispose()
        {
            var sources = _physicalSources!;
            if (sources[owner] == 1) sources.Remove(owner); else sources[owner]--;
        }
    }
    private static async Task Join(Task actual, List<Exception> errors)
    {
        try { await actual.ConfigureAwait(false); }
        catch (Exception error)
        { foreach (var cause in actual.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable()) Add(errors, cause); }
    }
    private static void Add(List<Exception> errors, Exception error)
    {
        if (!errors.Any(known => ReferenceEquals(known, error))) errors.Add(error);
        if (error is AggregateException group && group.InnerExceptions.Count != 0)
            foreach (var cause in group.InnerExceptions) Add(errors, cause);
    }
}
