using System.Text.Json;
using Haven.Application;

namespace Haven.Core.Tests;

public sealed class GenUiOriginalMutationTests
{
    [Fact]
    public void Exact_original_cleanup_refuses_identical_registration_replacement_and_removes_only_current_occurrence()
    {
        var store = new GenUiInstanceStore(); var document = Document(); store.Register(document);
        var old = store.ObserveOriginalRegisteredDocument(document);
        store.Register(document); // SAME public document/ID, a different actual private state.
        Assert.False(store.RemoveOriginalCurrentObservation(old));
        Assert.Same(document, store.TryGet(document.Origin.InstanceId));
        var current = store.ObserveOriginalRegisteredDocument(document);
        Assert.True(store.RemoveOriginalCurrentObservation(current));
        Assert.Null(store.TryGet(document.Origin.InstanceId));
        Assert.False(store.RemoveOriginalCurrentObservation(current));
    }

    [Fact]
    public async Task Actual_router_issues_exact_mutation_and_registration_replacement_invalidates_it()
    {
        var store = new GenUiInstanceStore(); var local = new GenUiLocalActionRegistry();
        var original = Document(); store.Register(original);
        var root = store.ObserveOriginalRegisteredDocument(original);
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), store);
        local.Register("original.set", (ev, _) => Task.FromResult(Result(ev, "routed")));
        var result = await router.RouteAsync(Event(original), Binding(original), CancellationToken.None);
        Assert.True(store.TryObserveOriginalMutation(result, out var receipt));
        var actual = Assert.IsType<GenUiOriginalMutationReceipt>(receipt);
        Assert.True(actual.OriginalApplyTask.IsCompletedSuccessfully);
        Assert.True(store.IsOriginalContinuation(root, actual));
        Assert.False(store.TryObserveOriginalMutation(result with { }, out _));
        var after = Assert.IsType<GenUiOriginalInstanceObservation>(actual.OriginalSuccessor);
        Assert.Equal("routed", after.Document.State["value"].GetString());
        Assert.Same(after.Document, store.TryGet(original.Origin.InstanceId));
        Assert.Throws<InvalidOperationException>(() => store.ObserveOriginalRegisteredDocument(after.Document));
        store.Register(after.Document);
        Assert.False(store.IsCurrentOriginalObservation(after));
        Assert.False(store.IsOriginalContinuation(root, actual));
        Assert.Same(after.Document, store.TryGet(original.Origin.InstanceId));
    }

    [Fact]
    public async Task Held_original_action_refuses_a_changed_predecessor_before_its_patches()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var store = new GenUiInstanceStore(); var local = new GenUiLocalActionRegistry();
        var original = Document(); store.Register(original);
        _ = store.ObserveOriginalRegisteredDocument(original);
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        GenUiActionResult? actualResult = null;
        local.Register("original.set", async (ev, _) =>
        {
            captured.SetResult(); await release.Task;
            return actualResult = Result(ev, "must refuse");
        });
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), store);
        var command = router.RouteAsync(Event(original), Binding(original), CancellationToken.None);
        Exception? bodyFailure = null;
        try
        {
            var first = await Task.WhenAny(captured.Task, command).WaitAsync(deadline.Token);
            if (ReferenceEquals(first, command)) await command;
            await captured.Task.WaitAsync(deadline.Token);
            Assert.False(command.IsCompleted);
            store.ApplyPatch(Patch(original, "concurrent"));
        }
        catch (Exception cause) { bodyFailure = cause; }
        finally { release.TrySetResult(); }
        var commandFailure = await Record.ExceptionAsync(() => command.WaitAsync(deadline.Token));
        if (bodyFailure is not null)
            throw new AggregateException(commandFailure is null ? [bodyFailure] : new[] { bodyFailure, commandFailure });
        var refusal = Assert.IsType<InvalidOperationException>(commandFailure);
        Assert.True(command.IsFaulted);
        Assert.Equal("concurrent", store.TryGet(original.Origin.InstanceId)!.State["value"].GetString());
        var result = Assert.IsType<GenUiActionResult>(actualResult);
        Assert.True(store.TryObserveOriginalMutation(result, out var receipt));
        var apply = Assert.IsType<GenUiOriginalMutationReceipt>(receipt).OriginalApplyTask;
        Assert.True(apply.IsFaulted);
        Assert.Same(refusal, Assert.Single(apply.Exception!.InnerExceptions));
        Assert.Same(refusal, Assert.Single(command.Exception!.InnerExceptions));
    }

    [Fact]
    public async Task Mutable_alias_and_unobserved_public_mutation_never_continue_original_registration()
    {
        var store = new GenUiInstanceStore(); var local = new GenUiLocalActionRegistry();
        var original = Document(); store.Register(original);
        var root = store.ObserveOriginalRegisteredDocument(original);
        ((Dictionary<string, JsonElement>)original.State)["value"] = JsonSerializer.SerializeToElement("alias");
        Assert.False(store.IsCurrentOriginalObservation(root));
        Assert.Throws<InvalidOperationException>(() => store.ObserveOriginalRegisteredDocument(original));
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), store);
        local.Register("original.set", (ev, _) => Task.FromResult(Result(ev, "routed")));
        var result = await router.RouteAsync(Event(original), Binding(original), CancellationToken.None);
        Assert.True(store.TryObserveOriginalMutation(result, out var receipt));
        Assert.False(store.IsOriginalContinuation(root, Assert.IsType<GenUiOriginalMutationReceipt>(receipt)));
        Assert.Equal("routed", store.TryGet(original.Origin.InstanceId)!.State["value"].GetString());
    }

    [Fact]
    public async Task Faulted_publication_preserves_exact_apply_cause_and_blocks_later_provenance()
    {
        var store = new GenUiInstanceStore(); var local = new GenUiLocalActionRegistry();
        var original = Document(); store.Register(original);
        var root = store.ObserveOriginalRegisteredDocument(original);
        var cancellation = new OperationCanceledException("original callback");
        var io = new IOException("original callback sibling");
        var group = new AggregateException(cancellation, io);
        EventHandler<GenUiDocument> callback = (_, _) => throw group;
        store.DocumentChanged += callback;
        GenUiActionResult? actualResult = null;
        local.Register("original.set", (ev, _) => Task.FromResult(actualResult = Result(ev, "applied")));
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), store);
        var command = router.RouteAsync(Event(original), Binding(original), CancellationToken.None);
        Assert.Same(group, await Assert.ThrowsAsync<AggregateException>(() => command));
        var result = Assert.IsType<GenUiActionResult>(actualResult);
        Assert.True(store.TryObserveOriginalMutation(result, out var receipt));
        var failed = Assert.IsType<GenUiOriginalMutationReceipt>(receipt);
        Assert.True(failed.OriginalApplyTask.IsFaulted); Assert.False(failed.OriginalApplyTask.IsCanceled);
        Assert.Same(group, Assert.Single(failed.OriginalApplyTask.Exception!.InnerExceptions));
        Assert.Same(cancellation, group.InnerExceptions[0]); Assert.Same(io, group.InnerExceptions[1]);
        Assert.False(store.IsOriginalContinuation(root, failed));
        store.DocumentChanged -= callback;
        var after = store.TryGet(original.Origin.InstanceId)!;
        var next = await router.RouteAsync(Event(after), Binding(after), CancellationToken.None);
        Assert.True(store.TryObserveOriginalMutation(next, out var nextReceipt));
        Assert.False(store.IsOriginalContinuation(root, Assert.IsType<GenUiOriginalMutationReceipt>(nextReceipt)));
        Assert.Equal("applied", store.TryGet(original.Origin.InstanceId)!.State["value"].GetString());
    }

    [Fact]
    public async Task Concurrent_registration_reserves_the_actual_original_commit_state_and_invalidates_its_receipt()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var release = new ManualResetEventSlim();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new HeldOriginalState(held, release, deadline.Token);
        var store = new GenUiInstanceStore(); var local = new GenUiLocalActionRegistry();
        var original = Document() with { State = state }; store.Register(original);
        var registration = store.ObserveOriginalRegisteredDocument(original);
        local.Register("original.set", (ev, _) => { state.Arm(); return Task.FromResult(Result(ev, "committed predecessor")); });
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), store);
        var action = Task.Run(() => router.RouteAsync(Event(original), Binding(original), CancellationToken.None));
        Task? replacement = null; Exception? bodyFailure = null;
        var latest = original with { State = new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement("replacement") } };
        try
        {
            var first = await Task.WhenAny(held.Task, action).WaitAsync(deadline.Token);
            if (ReferenceEquals(first, action)) await action;
            await held.Task.WaitAsync(deadline.Token);
            var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            replacement = Task.Factory.StartNew(() => { attempted.SetResult(); store.Register(latest); },
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            await attempted.Task.WaitAsync(deadline.Token);
            var raced = await Task.WhenAny(replacement, Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token));
            Assert.NotSame(replacement, raced); Assert.False(action.IsCompleted);
        }
        catch (Exception cause) { bodyFailure = cause; }
        finally { release.Set(); }
        var failures = new List<Exception>();
        if (bodyFailure is not null) failures.Add(bodyFailure);
        GenUiActionResult? result = null;
        try { result = await action.WaitAsync(deadline.Token); } catch (Exception cause) { failures.Add(action.Exception ?? cause); }
        if (replacement is not null) try { await replacement.WaitAsync(deadline.Token); } catch (Exception cause) { failures.Add(replacement.Exception ?? cause); }
        if (failures.Count != 0) throw new AggregateException("Actual concurrent original and registration were independently joined.", failures);
        Assert.Same(latest, store.TryGet(original.Origin.InstanceId));
        Assert.True(store.TryObserveOriginalMutation(Assert.IsType<GenUiActionResult>(result), out var receipt));
        var actual = Assert.IsType<GenUiOriginalMutationReceipt>(receipt);
        Assert.True(actual.OriginalApplyTask.IsCompletedSuccessfully);
        Assert.False(store.IsCurrentOriginalObservation(Assert.IsType<GenUiOriginalInstanceObservation>(actual.OriginalSuccessor)));
        Assert.False(store.IsOriginalContinuation(registration, actual));
    }

    private sealed class HeldOriginalState(TaskCompletionSource held, ManualResetEventSlim release, CancellationToken token)
        : IReadOnlyDictionary<string, JsonElement>
    {
        private readonly Dictionary<string, JsonElement> _values = new() { ["value"] = JsonSerializer.SerializeToElement("initial") };
        private int _armed;
        internal void Arm() => Volatile.Write(ref _armed, 1);
        public int Count => _values.Count;
        public IEnumerable<string> Keys => _values.Keys;
        public IEnumerable<JsonElement> Values => _values.Values;
        public JsonElement this[string key] => _values[key];
        public bool ContainsKey(string key) => _values.ContainsKey(key);
        public bool TryGetValue(string key, out JsonElement value) => _values.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<string, JsonElement>> GetEnumerator()
        {
            if (Interlocked.Exchange(ref _armed, 0) != 0) { held.SetResult(); release.Wait(token); }
            return _values.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static GenUiDocument Document() => new(Guid.NewGuid(), 1,
        new(Guid.NewGuid(), "assistants", null, Guid.NewGuid()), "Runtime source witness", "accent",
        new("button", "HavenButton", new Dictionary<string, JsonElement> { ["label"] = JsonSerializer.SerializeToElement("Set") },
            [new("set", GenUiRouteKind.Local, "original.set", CapabilityRiskClass.Low, false)], []),
        new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement("initial") }, DateTimeOffset.UtcNow);
    private static GenUiActionBinding Binding(GenUiDocument document) => document.Root.Actions[0];
    private static GenUiEvent Event(GenUiDocument document) => new(Guid.NewGuid(), GenUiEventType.ActionInvoked,
        DateTimeOffset.UtcNow, document.Origin, "button", "set", null, null, null,
        JsonSerializer.SerializeToElement(new { }), GenUiEventSource.User, "Actual maintained local router action.");
    private static GenUiStatePatch Patch(GenUiDocument document, string value) => new(Guid.NewGuid(), document.Origin.InstanceId,
        GenUiPatchOperation.Replace, "state", "value", JsonSerializer.SerializeToElement(value), DateTimeOffset.UtcNow);
    private static GenUiActionResult Result(GenUiEvent ev, string value) => GenerativeUiEventRouter.Result(ev,
        GenUiActionStatus.Completed, "Set local state", patches: [new(Guid.NewGuid(), ev.Origin.InstanceId,
            GenUiPatchOperation.Replace, "state", "value", JsonSerializer.SerializeToElement(value), DateTimeOffset.UtcNow)]);
}
