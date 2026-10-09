using System.Reflection;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

// Maintained protected-store fixture. These are source-authentication/custody controls,
// not a live model, synthetic old receipt, or successful invoked-restart acceptance.
public sealed partial class ChatCloudPermissionCallerTests
{
    [Fact]
    public async Task Cold_invoked_public_claim_is_refused_before_any_caller_or_store_factory()
    {
        if (!OperatingSystem.IsLinux()) return;
        await using var fixture = await ColdPersonalFixture.CreateAsync(TestContext.Current.CancellationToken);
        var lease = await fixture.StartAsync(TestContext.Current.CancellationToken);
        await InitialHostedProducer(lease); await fixture.CaptureDriver(lease); await lease.DetachAndDrainAsync();
        var before = (await fixture.Rows.GetByContextAsync(fixture.Conversation.Id, TestContext.Current.CancellationToken))!;
        var capsule = (await fixture.Tasks.ObserveOriginalColdInputAsync(before.TaskId,
            before.ExecutionId, TestContext.Current.CancellationToken))!;
        var callbacks = 0; var originals = new List<Task>();
        var copied = new PublicColdClaim(new PublicColdEntry(capsule), before);
        Task? actual = null;
        var refusal = Assert.Throws<UnauthorizedAccessException>((Action)(() =>
        { actual = fixture.Journal.ValidateOriginalAcceptedBoundaryWithinSourceAsync(copied, before,
            callback => { callbacks++; callback(); }, originals.Add, TestContext.Current.CancellationToken); }));
        Assert.NotNull(refusal); Assert.Null(actual); Assert.Equal(0, callbacks); Assert.Empty(originals);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await fixture.Rows.GetAsync(before.TaskId,
            TestContext.Current.CancellationToken)));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT claim_state FROM task_run_recovery_journal;", TestContext.Current.CancellationToken));
        Assert.Equal(0, fixture.Client.Dispatches);
    }

    [Fact]
    public async Task Cold_public_schema_upgrade_does_not_authenticate_an_invoked_boundary_or_replay_accepted_work()
    {
        if (!OperatingSystem.IsLinux()) return;
        await using var fixture = await ColdPersonalFixture.CreateAsync(TestContext.Current.CancellationToken);
        var lease = await fixture.StartAsync(TestContext.Current.CancellationToken);
        await InitialHostedProducer(lease); await fixture.CaptureDriver(lease); await lease.DetachAndDrainAsync();
        var before = (await fixture.Rows.GetByContextAsync(fixture.Conversation.Id, TestContext.Current.CancellationToken))!;
        await fixture.ExecuteAsync("UPDATE task_run_recovery_journal SET capsule_json=replace(replace(capsule_json,'\"schemaVersion\":1','\"schemaVersion\":2'),'\"SchemaVersion\":1','\"SchemaVersion\":2');",
            TestContext.Current.CancellationToken);
        var fresh = fixture.NewObservationCoordinator();
        Exception? primary = null; var expected = new List<Exception>();
        try
        {
            var read = fresh.ObserveOriginalColdInputAsync(before.TaskId, before.ExecutionId, TestContext.Current.CancellationToken);
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => read);
            var refused = Assert.Single(Leaves(failure), value => value is UnauthorizedAccessException
                && value.Message == "The original capsule provenance authentication failed.");
            expected.Add(refused);
            Assert.True(read.IsFaulted); Assert.Equal(0, fixture.Client.Dispatches);
            Assert.Equal(before.TaskId, (await fixture.Rows.GetAsync(before.TaskId, TestContext.Current.CancellationToken))!.TaskId);
            Assert.Equal(before.ExecutionId, (await fixture.Rows.GetAsync(before.TaskId, TestContext.Current.CancellationToken))!.ExecutionId);
            Assert.Equal(0L, await fixture.ScalarAsync("SELECT claim_state FROM task_run_recovery_journal;", TestContext.Current.CancellationToken));
        }
        catch (Exception cause) { primary = cause; throw; }
        finally
        {
            var close = fresh.CloseAndSuspendOriginalProducersAsync();
            await JoinColdBoundaryControlSources(primary, [close], expected);
        }
    }

    [Fact]
    public async Task Cold_actual_source_scope_retains_both_swallowed_one_use_refusals_and_same_first_raw_Task()
    {
        if (!OperatingSystem.IsLinux()) return;
        await using var fixture = await ColdPersonalFixture.CreateAsync(TestContext.Current.CancellationToken);
        var lease = await fixture.StartAsync(TestContext.Current.CancellationToken);
        await InitialHostedProducer(lease); await fixture.CaptureDriver(lease); await lease.DetachAndDrainAsync();
        var source = ColdCaptureSourceScope(fixture, lease);
        var actualRaw = Task.FromResult(37); var retained = new List<Task>();
        Exception? first = null, second = null;
        var scoped = source.WithinOriginalCaller(callback =>
        {
            callback();
            try { callback(); } catch (Exception cause) { first = cause; }
            try { callback(); } catch (Exception cause) { second = cause; }
        }, retained.Add);
        var actual = Assert.Throws<AggregateException>((Action)(() => { _ = scoped.Invoke(() => actualRaw); }));
        Assert.NotNull(first); Assert.NotNull(second); Assert.NotSame(first, second);
        Assert.Contains(Leaves(actual), cause => ReferenceEquals(cause, first));
        Assert.Contains(Leaves(actual), cause => ReferenceEquals(cause, second));
        Assert.Same(actualRaw, Assert.Single(retained)); Assert.Equal(37, await actualRaw);
        Assert.Equal(0, fixture.Client.Dispatches);
    }

    [Fact]
    public async Task Cold_finite_post_scope_failure_joins_actual_held_raw_and_preserves_all_direct_faults()
    {
        if (!OperatingSystem.IsLinux()) return;
        await using var fixture = await ColdPersonalFixture.CreateAsync(TestContext.Current.CancellationToken);
        var lease = await fixture.StartAsync(TestContext.Current.CancellationToken);
        await InitialHostedProducer(lease); await fixture.CaptureDriver(lease); await lease.DetachAndDrainAsync();
        var source = ColdCaptureSourceScope(fixture, lease);
        var held = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scopeFault = new IOException("exact finite cold caller failure");
        var first = new IOException("exact held cold raw first"); var second = new OperationCanceledException("faulted OCE sibling");
        var enrolled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scoped = source.WithinOriginalCaller(callback => { callback(); throw scopeFault; }, actual =>
        { if (ReferenceEquals(actual, held.Task)) enrolled.TrySetResult(); });
        var helper = typeof(TaskExecutionCoordinator).Assembly.GetType("Haven.Application.TaskRunColdSourceIo")!;
        var method = helper.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(value => value.Name == "Read" && value.IsGenericMethod).MakeGenericMethod(typeof(int));
        var actual = (Task<int>)method.Invoke(null, [scoped, (Func<Task<int>>)(() => held.Task)])!;
        Exception? primary = null;
        try
        {
            await enrolled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(actual.IsCompleted);
            held.SetException([first, second]);
            var fault = await Assert.ThrowsAnyAsync<AggregateException>(() => actual);
            Assert.True(actual.IsFaulted); Assert.True(held.Task.IsFaulted); Assert.False(actual.IsCanceled);
            Assert.Contains(Leaves(fault), cause => ReferenceEquals(cause, scopeFault));
            Assert.Contains(Leaves(fault), cause => ReferenceEquals(cause, first));
            Assert.Contains(Leaves(fault), cause => ReferenceEquals(cause, second));
            Assert.Equal(0, fixture.Client.Dispatches);
        }
        catch (Exception cause) { primary = cause; throw; }
        finally
        {
            held.TrySetException([first, second]);
            await JoinColdBoundaryControlSources(primary, [held.Task, actual], [scopeFault, first, second]);
        }
    }

    private static TaskRunColdOriginalSourceScope ColdCaptureSourceScope(ColdPersonalFixture fixture,
        TaskRunOriginalInitialChatObservationLease lease)
    {
        var original = lease.GetType().GetField("Original", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lease)!;
        var table = typeof(ChatSessionService).GetField("_coldInitialCaptures", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Service)!;
        object?[] args = [original, null];
        Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args)!);
        var custody = args[1]!.GetType().GetField("Custody", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(args[1])!;
        return (TaskRunColdOriginalSourceScope)typeof(TaskRunColdOriginalSourceScope)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [custody.GetType()], null)!.Invoke([custody]);
    }

    private static async Task JoinColdBoundaryControlSources(Exception? primary, IReadOnlyList<Task> actuals,
        IReadOnlyList<Exception> expected)
    {
        var unknown = new List<Exception>();
        foreach (var actual in actuals.Distinct<Task>(ReferenceEqualityComparer.Instance))
        {
            try { await actual.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); }
            catch (Exception cause)
            {
                if (!actual.IsCompleted) unknown.Add(cause);
                else foreach (var leaf in Leaves((Exception?)actual.Exception ?? cause))
                    if (!expected.Any(value => ReferenceEquals(value, leaf))) unknown.Add(leaf);
            }
        }
        if (unknown.Count != 0) throw new AggregateException("Cold control primary and independently joined unknown cleanup.",
            (primary is null ? Enumerable.Empty<Exception>() : new[] { primary }).Concat(unknown));
    }
    private sealed record PublicColdEntry(TaskRunColdCapsule Capsule) : ITaskRunColdJournalEntry;
    private sealed class PublicColdClaim(ITaskRunColdJournalEntry entry, TaskExecutionSnapshot expected) : ITaskRunColdJournalClaim
    {
        public ITaskRunColdJournalEntry OriginalEntry => entry;
        public TaskExecutionSnapshot OriginalExpected => expected;
        public Guid ClaimId { get; } = Guid.NewGuid();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
