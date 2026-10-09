using System.Runtime.Versioning;
using Haven.Application;
using NineToOne.Web.Services;

namespace NineToOne.Web.Spaces.Storage;

/// <summary>The actual private registry owns this device-local storage lifetime. Signed account
/// observations partition records; this does not grant Home/OS access, admit a Task/Run, construct
/// a provider or report that unavailable Dev actions are implemented.</summary>
[SupportedOSPlatform("browser")]
public sealed class BrowserPrivateTaskStorage : IBrowserPrivateContextParticipant
{
    private readonly object _gate = new();
    private readonly BrowserTaskActorSource _actors = new();
    private BrowserTaskExecutionTransport? _transport;
    private IndexedDbConversationRepository? _conversations;
    private IndexedDbProfileTaskExecutionRepository? _tasks;
    private BrowserTaskContextIssuer? _contexts;
    private IndexedDbExecutionEventRepository? _eventRepository;
    private ExecutionEventHub? _eventHub;
    private TaskExecutionCoordinator? _coordinator;
    private BrowserStoredTaskReadOwner? _storedReads;
    private bool _revoked;
    private bool _initializing;
    private Exception? _initializationFailure;
    private Task? _close;

    // Construction enrolls no JS, actor, IDB or provider source. Register this exact lifetime
    // first; only an explicit actual repository use acquires its owned platform module.
    public BrowserPrivateTaskStorage() { }

    private void EnsureInitialized()
    {
        lock (_gate)
        {
            if (_revoked) throw new ObjectDisposedException(nameof(BrowserPrivateTaskStorage));
            if (_initializationFailure is not null) throw new InvalidOperationException("The same private storage initialization failed.", _initializationFailure);
            if (_storedReads is not null) return;
            if (_initializing) throw new InvalidOperationException("A private storage source cannot reenter its own original initialization.");
            _initializing = true;
            try
            {
                _transport = new(); // Keep the actual acquired transport before subsequent constructors.
                _conversations = new(_transport, _actors);
                _tasks = new(_transport, _actors, actual => _storedReads?.RetainAuthenticatedDriver(actual));
                _contexts = new(_conversations);
                _eventRepository = _transport.CreateExecutionEventRepository(_actors);
                _eventHub = new(_eventRepository); // Capture the genuine collector before later composition.
                _coordinator = new(_tasks, _eventHub, timeProvider: null); // Actual preserved read-only constructor; no attempt authority.
                _storedReads = new(_coordinator, _transport, _gate); // Same private fence gate makes final read publication atomic with revocation.
            }
            catch (Exception error) { _initializationFailure = error; throw; }
            finally { _initializing = false; }
        }
    }

    // Every original read/write still performs actor→storage→actor through this SAME owner.
    // An already-acquired adapter cannot bypass its transport's synchronous retirement fence.
    public IndexedDbConversationRepository Conversations
    { get { EnsureInitialized(); return _conversations!; } }
    public IndexedDbProfileTaskExecutionRepository Tasks
    { get { EnsureInitialized(); return _tasks!; } }
    public BrowserTaskContextIssuer Contexts
    { get { EnsureInitialized(); return _contexts!; } }
    public BrowserStoredTaskReadOwner StoredTasks
    { get { EnsureInitialized(); return _storedReads!; } }
    public void DemandPrivateContextCurrent()
    {
        lock (_gate)
        {
            if (_revoked) throw new ObjectDisposedException(nameof(BrowserPrivateTaskStorage));
            if (_initializationFailure is not null) throw new InvalidOperationException("The same private storage initialization failed.", _initializationFailure);
            _transport?.DemandPrivateContextCurrent();
        }
    }
    public void RevokePrivateContext()
    {
        lock (_gate)
        {
            _revoked = true; // No callbacks, cancellation, actor read or save on this private fence.
            _storedReads?.RevokePrivateContext();
            _eventHub?.RequestClose(); // Private, nonpublishing read graph has no live event issuer.
            _transport?.RevokePrivateContext();
        }
    }
    public ValueTask DisposeAsync()
    {
        Task actual;
        var start = new TaskCompletionSource();
        lock (_gate)
        {
            if (_initializing) throw new InvalidOperationException("An actual storage initialization source cannot join its encompassing close.");
            _storedReads?.DemandExternalOriginalRetirementJoin();
            _transport?.DemandExternalOriginalRetirementJoin();
            if (_close is not null) return new(_close);
            _revoked = true;
            _storedReads?.RevokePrivateContext();
            _eventHub?.RequestClose();
            _transport?.RevokePrivateContext();
            actual = CloseOriginalAsync(start.Task, _storedReads, _eventHub, _transport, _initializationFailure);
            _close = actual; // Same actual close Task exists before any disposal source callback.
        }
        start.SetResult();
        return new(actual);
    }
    // Exact retirement driver is internal only for source-linked partial/fault controls.
    // It accepts actual owned types, never an authentication/admission/provider bypass.
    internal static async Task CloseOriginalAsync(Task start, BrowserStoredTaskReadOwner? reads, ExecutionEventHub? hub,
        BrowserTaskExecutionTransport? transport, Exception? initializationFailure)
    {
        await start;
        reads?.DemandExternalOriginalRetirementJoin();
        transport?.DemandExternalOriginalRetirementJoin();
        var causes = new List<Exception>();
        if (initializationFailure is not null) causes.Add(initializationFailure);
        Task? readClose = null, hubClose = null;
        try { readClose = reads?.CloseAndDrainAsync(); } catch (Exception error) { Capture(causes, null, error); }
        try { hubClose = hub?.DisposeAsync().AsTask(); } catch (Exception error) { Capture(causes, null, error); }
        if (hubClose is not null)
            try { await hubClose; } catch (Exception error) { Capture(causes, hubClose, error); }
        // Genuine collector is terminal before the SAME transport cancel/drain starts.
        // Continue every remaining join even when partial construction or hub close failed.
        Task? raw = null;
        try { raw = transport?.CloseAndDrainAsync(); } catch (Exception error) { causes.Add(error); }
        if (readClose is not null)
            try { await readClose; } catch (Exception error) { Capture(causes, readClose, error); }
        if (raw is not null)
            try { await raw; } catch (Exception error) { Capture(causes, raw, error); }
        if (causes.Count != 0) throw new AggregateException("Actual private Task storage initialization or drain failed.", causes);
    }
    private static void Capture(List<Exception> causes, Task? actual, Exception error)
    {
        IEnumerable<Exception> originals = actual?.Exception is { } group ? group.InnerExceptions : new[] { error };
        foreach (var cause in originals)
            if (!causes.Any(prior => ReferenceEquals(prior, cause))) causes.Add(cause);
    }
}
