using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;
using NineToOne.Web.Services;

namespace NineToOne.Web.Spaces.Storage;

/// <summary>One browser composition owns this transport and its SAME JS module.
/// Page/action producers must return before an external owner joins this encompassing close.</summary>
[SupportedOSPlatform("browser")]
public sealed partial class BrowserTaskExecutionTransport : ITaskExecutionBrowserTransport, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly string _moduleOwner;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Task> _originals = [];
    private readonly Dictionary<Task, AuthenticatedOriginal> _authenticatedOriginals = [];
    private readonly Func<string, string, string, string, bool, Task<string>> _invoke = Invoke;
    private readonly Action<string, string> _cancel = Cancel;
    private readonly Func<string, Task> _disposeModule = DisposeModule;
    private bool _retiring;
    private Task? _close;
    private InvalidOperationException? _capacityRefusal;

    public BrowserTaskExecutionTransport()
    {
        // The actual JS host issues one private lifetime before any IDB/factory callback.
        // This is correlation ownership only, never signed actor/profile or permission.
        _moduleOwner = InvokePhysicalSource(OpenOwner);
        if (string.IsNullOrWhiteSpace(_moduleOwner)) throw new InvalidOperationException("No actual private Task module owner was issued.");
    }

    // Source-linked owning controls enter the SAME driver with held original Tasks.
    // Production construction uses only the real imports above; no route/provider/grant is added.
    internal BrowserTaskExecutionTransport(string moduleOwner,
        Func<string, string, string, string, bool, Task<string>> invoke,
        Action<string, string> cancel, Func<string, Task> disposeModule)
    { _moduleOwner = moduleOwner; _invoke = invoke; _cancel = cancel; _disposeModule = disposeModule; }

    public IndexedDbExecutionEventRepository CreateExecutionEventRepository(BrowserTaskActorSource actualActors) =>
        new(new ProfileEventTransport(this, actualActors));
    private sealed class ProfileEventTransport(BrowserTaskExecutionTransport transport, BrowserTaskActorSource actors) : IExecutionEventBrowserTransport
    {
        public async Task<(AuthenticatedResourceActor Actor, JsonElement Reply)> InvokeAsync(string action, JsonElement args, CancellationToken token)
        { var actual = await transport.InvokeAuthenticatedAsync(actors, action, args, token); return (actual.Actor, actual.Reply); }
    }

    /// <summary>Synchronous generation fence only. No cancellation, source callback or save.
    /// The same encompassing external CloseAndDrainAsync later joins all issued originals.</summary>
    public void RevokePrivateContext()
    { lock (_gate) _retiring = true; }

    public void DemandPrivateContextCurrent()
    { lock (_gate) if (_retiring) throw new ObjectDisposedException(nameof(BrowserTaskExecutionTransport)); }

    private readonly AsyncLocal<AuthenticatedOriginal?> _authenticatedExecuting = new();
    [ThreadStatic] private static List<BrowserTaskExecutionTransport>? _physicalSources;

    private sealed class AuthenticatedOriginal(BrowserTaskExecutionTransport owner, AuthenticatedOriginal? parent)
    {
        internal readonly BrowserTaskExecutionTransport Owner = owner;
        internal readonly AuthenticatedOriginal? Parent = parent;
        internal Task? Task;
        internal readonly List<Task> ActorSources = [];
        internal bool StorageIssued;
        internal Exception? NullActorRefusal;
        internal ExpectedRefusalKind? ExpectedRefusal;
        internal Exception? ExpectedCause;
    }
    public enum ExpectedRefusalKind { CancelledBeforeStorage, NoCurrentActorBeforeStorage }
    public sealed record ExpectedRefusalObservation(Task OriginalTask, ExpectedRefusalKind Kind, Exception OriginalCause,
        IReadOnlyList<Task> OriginalActorSources);
    public IReadOnlyList<ExpectedRefusalObservation> ExpectedPreStorageRefusals
    {
        get { lock (_gate) return _authenticatedOriginals.Values.Where(original => original.Task is { IsCompleted: true } &&
            original.ExpectedRefusal.HasValue).Select(original => new ExpectedRefusalObservation(original.Task!,
                original.ExpectedRefusal!.Value, original.ExpectedCause!, Array.AsReadOnly(original.ActorSources.ToArray()))).ToArray(); }
    }

    /// <summary>The SAME storage owner publishes the complete actor→storage→actor driver.
    /// Actor/profile observations scope device-local data; they do not admit a Task or provider.</summary>
    public Task<BrowserAuthenticatedStorageReply> InvokeAuthenticatedAsync(
        BrowserTaskActorSource actualActors, string action, JsonElement arguments, CancellationToken caller)
        => StartAuthenticatedOriginal(actualActors, action, arguments, caller);
    internal Task<BrowserAuthenticatedStorageReply> InvokeAuthenticatedControlledAsync(
        IAuthenticatedResourceActorSource actualActors, string action, JsonElement arguments, CancellationToken caller)
        => StartAuthenticatedOriginal(actualActors, action, arguments, caller);
    private Task<BrowserAuthenticatedStorageReply> StartAuthenticatedOriginal(
        IAuthenticatedResourceActorSource actualActors, string action, JsonElement arguments, CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(actualActors);
        var start = new TaskCompletionSource();
        var original = new AuthenticatedOriginal(this, _authenticatedExecuting.Value);
        Task<BrowserAuthenticatedStorageReply> actual;
        lock (_gate)
        {
            if (_retiring) throw new ObjectDisposedException(nameof(BrowserTaskExecutionTransport));
            PruneSuccessfulOriginals();
            if (_capacityRefusal is not null) throw _capacityRefusal;
            if (_originals.Count >= 256)
                throw _capacityRefusal = new InvalidOperationException("The browser task transport original-work capacity requires external retirement.");
            actual = InvokeAuthenticatedOriginalAsync(start.Task, original, actualActors, action, arguments.Clone(), caller);
            original.Task = actual;
            _originals.Add(actual); // Whole prefix exists before any actor/source callback.
            _authenticatedOriginals.Add(actual, original);
        }
        start.SetResult();
        return actual;
    }

    private async Task<BrowserAuthenticatedStorageReply> InvokeAuthenticatedOriginalAsync(Task start,
        AuthenticatedOriginal original, IAuthenticatedResourceActorSource actors, string action, JsonElement args, CancellationToken caller)
    {
        await start;
        var previous = _authenticatedExecuting.Value;
        _authenticatedExecuting.Value = original;
        var causes = new List<Exception>();
        CancellationTokenSource? scope = null;
        AuthenticatedResourceActor? actor = null;
        JsonElement? reply = null;
        CancellationToken admittedToken = default;
        try
        {
            scope = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, caller);
            var token = scope.Token;
            admittedToken = token;
            token.ThrowIfCancellationRequested();
            DemandPrivateContextCurrent();
            var actorTask = InvokePhysicalSource(() => actors.GetCurrentAsync(token).AsTask());
            original.ActorSources.Add(actorTask);
            actor = await AwaitAuthenticatedSourceAsync(actorTask);
            DemandPrivateContextCurrent();
            token.ThrowIfCancellationRequested();
            if (actor is null)
                throw original.NullActorRefusal = new BrowserTaskContextUnavailableException("No current verified browser Task account/profile is attached.");
            if (!actor.ProfileId.StartsWith("cake-account-profile:", StringComparison.Ordinal) ||
                !actor.ActorId.StartsWith("cake-task:", StringComparison.Ordinal) || actor.AccountId is null ||
                string.IsNullOrWhiteSpace(actor.AuthenticationRevision))
                throw new BrowserTaskContextUnavailableException("No current verified browser Task account/profile is attached.");
            var supplied = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(args.GetRawText())
                ?? throw new ArgumentException("A canonical storage argument object is required.");
            supplied["profileId"] = JsonSerializer.SerializeToElement(actor.ProfileId);
            if (action == "ExecutionEvents.Append")
            {
                var capturedRows = supplied["rows"].EnumerateArray().Select(candidate =>
                {
                    var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(candidate.GetRawText())
                        ?? throw new ArgumentException("A complete canonical event row is required.");
                    fields["profileId"] = JsonSerializer.SerializeToElement(actor.ProfileId);
                    return fields;
                }).ToArray();
                supplied["rows"] = JsonSerializer.SerializeToElement(capturedRows);
            }
            if (supplied.TryGetValue("row", out var candidate))
            {
                var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(candidate.GetRawText())
                    ?? throw new ArgumentException("A canonical storage row object is required.");
                // Conversation rows carry a storage partition; the canonical domain JSON has no new actor fields.
                if (action.StartsWith("Conversation.", StringComparison.Ordinal))
                    fields["profileId"] = JsonSerializer.SerializeToElement(actor.ProfileId);
                if (action == "Upsert")
                {
                    var snapshot = JsonSerializer.Deserialize<TaskExecutionSnapshot>(fields["json"].GetString()!,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("No canonical task payload.");
                    if (snapshot.OwnerBinding is { } binding &&
                        (binding.ActorId != actor.ActorId || binding.ProfileId != actor.ProfileId ||
                         binding.AccountId != actor.AccountId || binding.OrganisationId != actor.OrganisationId ||
                         binding.AuthenticationRevision != actor.AuthenticationRevision))
                        throw new BrowserTaskContextUnavailableException("The current signed activation differs from the canonical Task owner observation.");
                }
                supplied["row"] = JsonSerializer.SerializeToElement(fields);
            }
            token.ThrowIfCancellationRequested();
            DemandPrivateContextCurrent();
            original.StorageIssued = true; // Fence the exact acquisition attempt, including a synchronous source fault.
            reply = await AwaitAuthenticatedSourceAsync(InvokePhysicalSource(() => InvokeAsync(action,
                JsonSerializer.SerializeToElement(supplied), token)));
            DemandPrivateContextCurrent();
            token.ThrowIfCancellationRequested();
            var currentTask = InvokePhysicalSource(() => actors.GetCurrentAsync(token).AsTask());
            original.ActorSources.Add(currentTask);
            var current = await AwaitAuthenticatedSourceAsync(currentTask);
            DemandPrivateContextCurrent();
            token.ThrowIfCancellationRequested();
            if (current != actor)
                throw new BrowserTaskContextUnavailableException("The signed session/profile changed during this original storage operation; inspect the original partition before retrying.");
        }
        catch (Exception error) { Add(causes, error); }
        finally
        {
            try { scope?.Dispose(); } catch (Exception error) { Add(causes, error); }
            _authenticatedExecuting.Value = previous;
        }
        if (causes.Count != 0)
        {
            // Only a complete lawful pre-storage original can be classified. A faulted
            // OCE/sibling group, unknown/late mutation, changed actor or any cleanup error
            // cannot enter this private disposition and is still retained by encompassing close.
            if (!original.StorageIssued && causes.Count == 1)
            {
                var cause = causes[0];
                if (cause is OperationCanceledException &&
                    (admittedToken.IsCancellationRequested || original.ActorSources.Any(source => source.IsCanceled)) &&
                    original.ActorSources.All(source => source.IsCompletedSuccessfully || source.IsCanceled))
                {
                    original.ExpectedRefusal = ExpectedRefusalKind.CancelledBeforeStorage; original.ExpectedCause = cause;
                    ExceptionDispatchInfo.Capture(cause).Throw(); // SAME driver is genuinely Cancelled, not a faulted OCE aggregate.
                }
                if (ReferenceEquals(cause, original.NullActorRefusal) && original.ActorSources.Count == 1 &&
                    original.ActorSources[0].IsCompletedSuccessfully)
                {
                    original.ExpectedRefusal = ExpectedRefusalKind.NoCurrentActorBeforeStorage; original.ExpectedCause = cause;
                    ExceptionDispatchInfo.Capture(cause).Throw();
                }
            }
            var failure = new AggregateException("Original authenticated browser storage or cleanup failed.", causes);
            if (reply is { } actualReply) throw new TaskExecutionTransportReplyFailureException(actualReply, failure);
            throw failure;
        }
        return new(actor!, reply ?? throw new IOException("No actual browser storage reply was received."));
    }

    private T InvokePhysicalSource<T>(Func<T> source)
    {
        var owners = _physicalSources ??= []; owners.Add(this);
        try { return source(); }
        catch (OperationCanceledException fault) { throw new AggregateException("An actual browser storage source faulted synchronously.", fault); }
        finally { owners.RemoveAt(owners.Count - 1); }
    }
    private static async Task<T> AwaitAuthenticatedSourceAsync<T>(Task<T> actual)
    {
        try { return await actual; }
        catch (Exception error)
        {
            if (actual.Exception is { } group) throw group; // Complete direct group, including OCE-first faults.
            if (actual.IsCanceled) ExceptionDispatchInfo.Capture(error).Throw();
            throw new AggregateException("The actual browser storage source faulted without a terminal fault group.", error);
        }
    }
    public void DemandExternalOriginalRetirementJoin()
    {
        if (_physicalSources?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An actual browser storage source cannot join its encompassing close.");
        for (var original = _authenticatedExecuting.Value; original is not null; original = original.Parent)
            if (ReferenceEquals(original.Owner, this) && original.Task is { IsCompleted: false })
                throw new InvalidOperationException("A live authenticated browser storage original must return before external close.");
    }

    public Task<JsonElement> InvokeAsync(string action, JsonElement arguments, CancellationToken cancellationToken)
    {
        Task<JsonElement> original;
        var start = new TaskCompletionSource();
        lock (_gate)
        {
            if (_retiring) throw new ObjectDisposedException(nameof(BrowserTaskExecutionTransport));
            PruneSuccessfulOriginals();
            if (_capacityRefusal is not null) throw _capacityRefusal;
            if (_originals.Count >= 256)
                throw _capacityRefusal = new InvalidOperationException("The browser task transport original-work capacity requires external retirement.");
            original = InvokeOriginalAsync(start.Task, action, arguments.GetRawText(), cancellationToken);
            _originals.Add(original);
        }
        start.SetResult();
        return original;
    }

    private async Task<JsonElement> InvokeOriginalAsync(Task start, string action, string arguments, CancellationToken caller)
    {
        await start;
        var errors = new List<Exception>();
        var errorGate = new object();
        CancellationTokenSource? scope = null;
        CancellationTokenRegistration registration = default;
        Task<string>? actual = null;
        JsonElement? reply = null;
        var invokeAttempted = false;
        var id = Guid.NewGuid().ToString("N");
        try
        {
            scope = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, caller);
            var token = scope.Token;
            token.ThrowIfCancellationRequested();
            registration = token.Register(RequestCancel);
            token.ThrowIfCancellationRequested();
            DemandPrivateContextCurrent();
            invokeAttempted = true;
            // Capture the SAME raw request Task before any cancellation callback can
            // terminate this managed owner. The raw original is independently joined below.
            actual = InvokePhysicalSource(() => _invoke(_moduleOwner, id, action, arguments, token.IsCancellationRequested));
            if (token.IsCancellationRequested) RequestCancel();
        }
        catch (Exception error) { Retain(error); }
        if (actual is not null)
        {
            try
            {
                var text = await actual;
                using var document = JsonDocument.Parse(text);
                var received = document.RootElement.Clone();
                if (received.ValueKind != JsonValueKind.Object)
                    throw new IOException("A task storage reply was not an object.");
                reply = received; // Original reply stays separate from cancel/cleanup failures.
            }
            catch (Exception error)
            {
                var failures = new List<Exception>();
                Capture(failures, actual, error); // Preserve every actual direct fault sibling.
                foreach (var failure in failures) Retain(failure);
            }
        }
        // A received commit stays separately retained even if this SAME context was retired.
        try { DemandPrivateContextCurrent(); } catch (Exception error) { Retain(error); }
        // Unregister joins any already-running callback before the final cause snapshot.
        try { registration.Dispose(); } catch (Exception error) { Retain(error); }
        if (scope is not null)
        {
            if (!(reply is { } received && received.TryGetProperty("committed", out var committed) &&
                committed.ValueKind == JsonValueKind.True))
                try { scope.Token.ThrowIfCancellationRequested(); } catch (Exception error) { Retain(error); }
            try { scope.Dispose(); } catch (Exception error) { Retain(error); }
        }
        Exception[] causes;
        lock (errorGate) causes = errors.ToArray();
        if (causes.Length != 0)
        {
            var retained = new AggregateException("Original task storage request and cancellation/cleanup failed.", causes);
            if (reply is { } received)
                throw new TaskExecutionTransportReplyFailureException(received, retained);
            if (IsMutation(action) && invokeAttempted)
                throw new TaskExecutionCommitOutcomeUnknownException(
                    "The original mutation reply was lost; inspect durable state before retrying.", retained);
            throw retained;
        }
        return reply ?? throw new IOException("The actual task storage request supplied no reply.");

        void RequestCancel()
        {
            try { _cancel(_moduleOwner, id); }
            catch (Exception error) { Retain(error); } // Stop failure cannot abandon the pending raw request.
        }
        void Retain(Exception error) { lock (errorGate) Add(errors, error); }
    }

    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        Task close;
        var start = new TaskCompletionSource();
        lock (_gate)
        {
            if (_close is not null) return _close;
            _retiring = true;
            close = CloseOriginalAsync(start.Task, _originals.ToArray(), new(_authenticatedOriginals), _capacityRefusal);
            _close = close;
        }
        start.SetResult();
        return close;
    }

    private async Task CloseOriginalAsync(Task start, Task[] originals, Dictionary<Task, AuthenticatedOriginal> authenticated, Exception? originalCapacityRefusal)
    {
        await start;
        var failures = new List<Exception>();
        if (originalCapacityRefusal is not null) Add(failures, originalCapacityRefusal);
        try { InvokePhysicalSource(() => { _lifetime.Cancel(); return true; }); } catch (Exception error) { Add(failures, error); }
        Task? actualStop = null;
        try { actualStop = InvokePhysicalSource(() => _disposeModule(_moduleOwner)); } catch (Exception error) { Add(failures, error); }
        foreach (var original in originals)
        {
            try { await original; }
            catch (Exception error)
            {
                var expected = authenticated.TryGetValue(original, out var owner) && !owner.StorageIssued &&
                    (owner.ExpectedRefusal == ExpectedRefusalKind.CancelledBeforeStorage && original.IsCanceled ||
                     owner.ExpectedRefusal == ExpectedRefusalKind.NoCurrentActorBeforeStorage && original.IsFaulted &&
                     original.Exception is { InnerExceptions.Count: 1 } group && ReferenceEquals(group.InnerExceptions[0], owner.ExpectedCause));
                if (!expected) Capture(failures, original, error);
            }
        }
        if (actualStop is not null)
        {
            try { await actualStop; }
            catch (Exception error) { Capture(failures, actualStop, error); }
        }
        try { _lifetime.Dispose(); } catch (Exception error) { Add(failures, error); }
        if (failures.Count != 0) throw new AggregateException("Actual browser task storage originals or cleanup failed.", failures);
    }

    private static bool IsMutation(string action) => action is "ExecutionEvents.Append" or "Upsert" or "Conversation.Upsert" or "Conversation.Delete" or
        "Conversation.DetachSpace" or "Conversation.PutMessage" or "Conversation.DeleteMessage" or
        "Conversation.CompactMessages" or "Conversation.PutContext" or "Conversation.DeleteContext";

    private void PruneSuccessfulOriginals()
    {
        _originals.RemoveWhere(static task => task.IsCompletedSuccessfully);
        foreach (var task in _authenticatedOriginals.Keys.Where(static task => task.IsCompletedSuccessfully).ToArray())
            _authenticatedOriginals.Remove(task);
    }

    private static void Capture(List<Exception> failures, Task original, Exception caught)
    {
        if (original.Exception is { InnerExceptions.Count: > 0 } group)
            foreach (var cause in group.InnerExceptions) Add(failures, cause);
        else Add(failures, caught);
    }
    private static void Add(List<Exception> failures, Exception cause)
    {
        if (!failures.Any(prior => ReferenceEquals(prior, cause))) failures.Add(cause);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    [JSImport("openOwner", "nineToOneTaskExecution")]
    private static partial string OpenOwner();
    [JSImport("invoke", "nineToOneTaskExecution")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> Invoke(string moduleOwner, string requestId, string action, string json, bool cancelledAtAdmission);
    [JSImport("cancel", "nineToOneTaskExecution")]
    private static partial void Cancel(string moduleOwner, string requestId);
    [JSImport("disposeOwner", "nineToOneTaskExecution")]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    private static partial Task DisposeModule(string moduleOwner);
}

/// <summary>The raw transport received this complete reply, but an independent cancellation or
/// cleanup failed. This unvalidated reply is observation only; the canonical repository must
/// validate all IDs/revision/hash before interpreting its committed flag.</summary>
public sealed class TaskExecutionTransportReplyFailureException(JsonElement originalReply, Exception cause)
    : IOException("The original task storage reply was received with independent cleanup failure.", cause)
{
    public JsonElement OriginalReply { get; } = originalReply.Clone();
}

/// <summary>Verified current-profile observation only, never a Task/provider admission.</summary>
public sealed record BrowserAuthenticatedStorageReply(AuthenticatedResourceActor Actor, JsonElement Reply);
public sealed class BrowserTaskContextUnavailableException(string message) : InvalidOperationException(message);
