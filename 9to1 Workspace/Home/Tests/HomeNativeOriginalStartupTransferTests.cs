using System.Collections.Frozen;
using System.IO.Pipes;
using System.Reflection;
using Flags = System.Reflection.BindingFlags;
using System.Runtime.ExceptionServices;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

// Actual product resource-close/callback protocol controls. Private constructor
// fixtures use disconnected real pipes/CTS and deliberately invalid peer DTOs.
// They exercise no verification, connected host, enrollment or Ready authority.
public sealed class HomeNativeOriginalStartupTransferTests
{
    [Fact]
    public Task Exact_parent_resource_close_does_not_join_held_attachment_and_public_close_keeps_its_guard() => WithRig(async rig =>
    {
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attachment = rig.BindClient(accepted.Task); var source = Source(attachment);
        Task? resourceClose = null; Task aggregateClose;
        try
        {
            var prior = ExecutionContext.Capture()!;
            source.Run(() => ExecutionContext.Run(prior, state => Assert.Throws<InvalidOperationException>(
                () => { _ = rig.Client.DisposeAsync(); }), null));
            await source.Cleanup(() => PrivateTask(rig.Client, "CloseFailedOriginalAttachment", attachment), same => resourceClose = same);
            Assert.NotNull(resourceClose); await resourceClose;
            Assert.False(accepted.Task.IsCompleted);
            Assert.Same(resourceClose, PrivateTask(rig.Client, "CloseFailedOriginalAttachment", attachment));
            aggregateClose = rig.Client.DisposeAsync().AsTask();
            Assert.Same(aggregateClose, rig.Client.DisposeAsync().AsTask());
            Assert.False(aggregateClose.IsCompleted);
            accepted.SetResult(); await aggregateClose;
            await source.JoinAll(); source.Throw();
        }
        finally { accepted.TrySetResult(); }
    });

    [Fact]
    public Task Pruned_check_callback_and_transferred_session_client_sources_remain_in_aggregate_close() => WithRig(async rig =>
    {
        Action? saved = null;
        var attachment = rig.BindClient(Task.CompletedTask);
        var session = rig.MakeSession();
        var sessionAttachment = rig.BindSession(session, Task.CompletedTask, callback => callback());
        var oldCheck = rig.BindCompletedCheck(session, callback => { saved ??= callback; callback(); });
        Source(oldCheck).Run(() => { }); Assert.NotNull(saved);
        var actualCheck = session.CheckWithinOriginalSourceAsync(callback => callback(), _ => { });
        var absent = await Assert.ThrowsAsync<AggregateException>(() => actualCheck);
        var absentLeaves = Leaves(absent).ToArray(); Assert.NotEmpty(absentLeaves);
        Assert.All(absentLeaves, same => Assert.IsType<UnauthorizedAccessException>(same));
        foreach (var same in absentLeaves) rig.Expected.Add(same);
        Assert.DoesNotContain(oldCheck, ((System.Collections.IList)Field(session, "_originalChecks")!).Cast<object>());
        var late = Assert.Throws<InvalidOperationException>(saved!); rig.Expected.Add(late);
        var clientCause = new IOException("Exact transferred client source occurrence.");
        var sessionCause = new IOException("Exact transferred session source occurrence.");
        Source(attachment).Keep(clientCause); rig.Expected.Add(clientCause);
        Source(sessionAttachment).Keep(sessionCause); rig.Expected.Add(sessionCause);
        var refused = Assert.Throws<AggregateException>(() => { _ = session.CheckWithinOriginalSourceAsync(callback => callback(), _ => { }); });
        Assert.Contains(Leaves(refused), same => ReferenceEquals(same, late));
        var close = session.CloseAndDrainAsync(); Assert.Same(close, session.CloseAndDrainAsync());
        var observed = await Assert.ThrowsAsync<AggregateException>(() => close);
        foreach (var same in new[] { late, clientCause, sessionCause }) Assert.Contains(Leaves(observed), leaf => ReferenceEquals(leaf, same));
        Assert.All(Leaves(observed), same => Assert.Contains(same, rig.Expected));
    });

    [Fact]
    public Task Failed_startup_close_retains_borrowed_pipe_lifetime_and_exact_transferred_connection_source() => WithRig(async rig =>
    {
        var clientAttachment = rig.BindClient(Task.CompletedTask);
        var session = rig.MakeSession(); rig.BindSession(session, Task.CompletedTask, callback => callback());
        var connection = rig.MakeConnection(session); var attempt = rig.BindConnection(connection, session);
        var startupCause = new IOException("Exact unresolved original client source.");
        var attemptCause = new IOException("Exact transferred connection source.");
        Source(clientAttachment).Keep(startupCause); rig.Expected.Add(startupCause);
        Source(attempt).Keep(attemptCause); rig.Expected.Add(attemptCause);
        var close = connection.CloseAndDrainAsync(); Assert.Same(close, connection.CloseAndDrainAsync());
        var observed = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Contains(Leaves(observed), same => ReferenceEquals(same, startupCause));
        Assert.Contains(Leaves(observed), same => ReferenceEquals(same, attemptCause));
        Assert.All(Leaves(observed), same => Assert.Contains(same, rig.Expected));
        Assert.Null(Field(attempt, "PipeClose")); Assert.Null(Field(attempt, "LifetimeClose"));
        Assert.True(rig.Lifetime.IsCancellationRequested);
        _ = rig.Lifetime.Token; // The actual parent CTS has not been disposed.
        Assert.True(rig.Pipe.CanRead);
    });

    private static async Task WithRig(Func<ResourceRig, Task> body)
    {
        var rig = new ResourceRig(); Exception? failure = null;
        try { await body(rig); } catch (Exception cause) { failure = cause; }
        finally { await rig.Join(failure); }
    }

    private sealed class ResourceRig
    {
        internal readonly NamedPipeClientStream Pipe = new(".", "home-unconnected-resource-control", PipeDirection.InOut, PipeOptions.Asynchronous);
        internal readonly CancellationTokenSource Lifetime = new();
        internal readonly HomeWindowsCoreClient Client;
        internal readonly HashSet<Exception> Expected = new(ReferenceEqualityComparer.Instance);
        private readonly List<Task> _raw = [];
        private readonly List<object> _owners = [];
        // Expected unresolved closes retain their successor resources.
        private static readonly List<ResourceRig> Unresolved = [];
        internal ResourceRig()
        {
            Client = Construct<HomeWindowsCoreClient>(Pipe, new NoHostVerifier(),
                new HomeNativeSessionHostRequirement("unverified-control", "unverified-control"),
                new HomeNativeObservedPeer(0, "unverified-control"),
                new HomeNativeInstalledPeer("unverified-control", Guid.Empty, "unverified-control", "unverified-control", FrozenSet<string>.Empty),
                Lifetime.Token);
            Set(Client, "_originalScoped", true); _owners.Add(Client);
        }
        private void Retain(Task same) { lock (_raw) if (!_raw.Contains(same)) _raw.Add(same); }
        internal object BindClient(Task driver)
        {
            var work = Nested(typeof(HomeWindowsCoreClient), "ScopedClientAttachment", (Action<Action>)(callback => callback()), (Action<Task>)Retain);
            Set(work, "Client", Client); Set(work, "Driver", driver); Set(Client, "_originalAttachment", work); Retain(driver); return work;
        }
        internal HomeNativeWindowsStartupSession MakeSession()
        {
            var same = Construct<HomeNativeWindowsStartupSession>(Client,
                new HomeCompatibilityRequest("unverified-control", "0.0.0", [new HomeServiceRequirement("home.core", 1)]), Lifetime.Token);
            Set(same, "_originalScoped", true); _owners.Add(same); return same;
        }
        internal object BindSession(HomeNativeWindowsStartupSession session, Task driver, Action<Action> scope)
        {
            var work = Nested(typeof(HomeNativeWindowsStartupSession), "ScopedStartupAttachment", scope, (Action<Task>)Retain);
            Set(work, "Client", Client); Set(work, "Session", session); Set(work, "Driver", driver);
            Set(session, "_originalAttachment", work); Retain(driver); return work;
        }
        internal object BindCompletedCheck(HomeNativeWindowsStartupSession session, Action<Action> scope)
        {
            var work = Nested(typeof(HomeNativeWindowsStartupSession), "ScopedStartupCheck", session, scope, (Action<Task>)Retain);
            Set(work, "Driver", Task.CompletedTask);
            ((System.Collections.IList)Field(session, "_originalChecks")!).Add(work); return work;
        }
        internal HomeNativeWindowsAppConnection MakeConnection(HomeNativeWindowsStartupSession session)
        {
            var same = Construct<HomeNativeWindowsAppConnection>(Pipe, Lifetime, session);
            Set(same, "_originalScoped", true); _owners.Add(same); return same;
        }
        internal object BindConnection(HomeNativeWindowsAppConnection connection, HomeNativeWindowsStartupSession session)
        {
            var work = Nested(typeof(HomeNativeWindowsAppConnection), "ScopedConnectionAttempt", (Action<Action>)(callback => callback()), (Action<Task>)Retain);
            Set(work, "Pipe", Pipe); Set(work, "Lifetime", Lifetime); Set(work, "Startup", session); Set(work, "Result", connection);
            Set(work, "Driver", Task.CompletedTask); Set(connection, "_originalAttempt", work); return work;
        }
        internal async Task Join(Exception? body)
        {
            List<Exception> errors = []; if (body is not null) errors.Add(body);
            // Independently join cached closes; faults bar successor teardown.
            List<Task> closeTasks = [];
            foreach (var owner in _owners.AsEnumerable().Reverse())
            {
                try
                {
                    var same = owner switch
                    {
                        HomeNativeWindowsAppConnection connection => connection.CloseAndDrainAsync(),
                        HomeNativeWindowsStartupSession session => session.CloseAndDrainAsync(),
                        HomeWindowsCoreClient client => client.DisposeAsync().AsTask(),
                        _ => throw new InvalidOperationException("Unknown actual resource owner.")
                    };
                    closeTasks.Add(same);
                }
                catch (Exception cause) { AddUnknown(errors, cause); }
            }
            Task[] all; lock (_raw) all = _raw.Concat(closeTasks).Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
            foreach (var same in all)
                try { await same; } catch (Exception cause) { AddUnknown(errors, same.Exception ?? cause); }
            if (errors.Count == 0 && Expected.Count == 0)
            {
                try { await Pipe.DisposeAsync(); } catch (Exception cause) { errors.Add(cause); }
                try { Lifetime.Dispose(); } catch (Exception cause) { errors.Add(cause); }
            }
            else lock (Unresolved) Unresolved.Add(this);
            if (errors.Count != 0) throw new AggregateException("Actual transfer control/body/unknown cleanup failed.", errors);
        }
        private void AddUnknown(List<Exception> errors, Exception cause)
        {
            var leaves = Leaves(cause).ToArray();
            if (leaves.Length == 0 || leaves.Any(same => !Expected.Contains(same))) errors.Add(cause);
        }
    }
    private sealed class NoHostVerifier : IHomeNativeSessionHostVerifier
    {
        public ValueTask<HomeNativeInstalledPeer?> VerifyHostAsync(HomeNativeObservedPeer peer, HomeNativeSessionHostRequirement host, CancellationToken token) =>
            throw new InvalidOperationException("Resource-only controls must never perform host verification.");
    }
    private const Flags PrivateInstance = Flags.Instance | Flags.NonPublic;
    private static HomeNativeOriginalStartupScope Source(object owner) => (HomeNativeOriginalStartupScope)Field(owner, "Source")!;
    private static object? Field(object owner, string name) => owner.GetType().GetField(name, PrivateInstance)!.GetValue(owner);
    private static void Set(object owner, string name, object? value) => owner.GetType().GetField(name, PrivateInstance)!.SetValue(owner, value);
    private static T Construct<T>(params object[] args) => (T)Activator.CreateInstance(typeof(T), PrivateInstance, null, args, null)!;
    private static object Nested(Type owner, string name, params object[] args) => Activator.CreateInstance(owner.GetNestedType(name, Flags.NonPublic)!, PrivateInstance, null, args, null)!;
    private static Task PrivateTask(object owner, string method, object same)
    {
        try { return (Task)owner.GetType().GetMethod(method, PrivateInstance)!.Invoke(owner, [same])!; }
        catch (TargetInvocationException cause) when (cause.InnerException is not null)
        { ExceptionDispatchInfo.Capture(cause.InnerException).Throw(); throw; }
    }
    private static IEnumerable<Exception> Leaves(Exception cause)
    {
        if (cause is AggregateException group && group.InnerExceptions.Count != 0)
            foreach (var child in group.InnerExceptions) foreach (var leaf in Leaves(child)) yield return leaf;
        else yield return cause;
    }
}
