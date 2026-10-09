using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCloudPermissionCallerTests
{
    // Actual protected SQLite/HMAC capture/observation controls. The selected values
    // are durable observations only; neither test issues a Home/memory read grant or
    // fabricates a fresh process authentication activation.
    [Fact]
    public async Task Actual_protected_journal_preserves_disabled_memory_lineage_and_same_Task_Run_User_without_live_marker()
    {
        if (!OperatingSystem.IsLinux()) return;
        await using var fixture = await ColdPersonalFixture.CreateAsync(TestContext.Current.CancellationToken);
        var lineage = MemoryLineageFor(fixture.Conversation.Id);
        var initial = await fixture.StartAsync(TestContext.Current.CancellationToken, new GenerationOptions
        {
            RequestedContextConstraints = new(false) { RequireOriginalPersistentMemoryInput = true },
            RequestedPersistentMemoryLineage = lineage
        });
        await InitialHostedProducer(initial); await fixture.CaptureDriver(initial); await initial.DetachAndDrainAsync();
        var task = Assert.IsType<TaskExecutionSnapshot>(await fixture.Rows.GetByContextAsync(
            fixture.Conversation.Id, TestContext.Current.CancellationToken));
        var user = Assert.Single(await fixture.Conversations.GetMessagesAsync(fixture.Conversation.Id,
            TestContext.Current.CancellationToken));
        var fresh = fixture.NewObservationCoordinator();
        try
        {
            var capsule = Assert.IsType<TaskRunColdCapsule>(await fresh.ObserveOriginalColdInputAsync(
                task.TaskId, task.ExecutionId, TestContext.Current.CancellationToken));
            var options = Assert.IsType<GenerationOptions>(capsule.OriginalInput.GenerationOptions);
            Assert.Equal(lineage, options.RequestedPersistentMemoryLineage);
            Assert.False(options.RequestedContextConstraints!.AllowPersistentMemoryRead);
            Assert.True(options.RequestedContextConstraints.RequireOriginalPersistentMemoryInput);
            Assert.Null(options.OriginalPersistentMemoryInput);
            Assert.Equal(task.TaskId, capsule.AcknowledgedTask.TaskId);
            Assert.Equal(task.ExecutionId, capsule.AcknowledgedTask.ExecutionId);
            Assert.Equal(task.PersistenceRevision, capsule.AcknowledgedTask.PersistenceRevision);
            Assert.Equal(task.OwnerBinding, capsule.AcknowledgedTask.OwnerBinding);
            Assert.Equal(user, capsule.AcceptedUserMessage);
            Assert.Equal(0, fixture.Client.Dispatches); Assert.Equal(0, fixture.Provider.Starts);
            Assert.Equal(0L, await fixture.ScalarAsync("SELECT claim_state FROM task_run_recovery_journal;",
                TestContext.Current.CancellationToken));
        }
        finally { await fresh.CloseAndSuspendOriginalProducersAsync(); }
    }

    [Fact]
    public async Task Actual_protected_journal_refuses_replaced_memory_lineage_before_claim_or_model_and_keeps_original_Task()
    {
        if (!OperatingSystem.IsLinux()) return;
        await using var fixture = await ColdPersonalFixture.CreateAsync(TestContext.Current.CancellationToken);
        var lineage = MemoryLineageFor(fixture.Conversation.Id);
        var initial = await fixture.StartAsync(TestContext.Current.CancellationToken, new GenerationOptions
        {
            RequestedContextConstraints = new(false) { RequireOriginalPersistentMemoryInput = true },
            RequestedPersistentMemoryLineage = lineage
        });
        await InitialHostedProducer(initial); await fixture.CaptureDriver(initial); await initial.DetachAndDrainAsync();
        var before = Assert.IsType<TaskExecutionSnapshot>(await fixture.Rows.GetByContextAsync(
            fixture.Conversation.Id, TestContext.Current.CancellationToken));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM task_run_recovery_journal WHERE instr(capsule_json,'" +
            lineage.DefinitionId + "')>0;", TestContext.Current.CancellationToken));
        await fixture.ExecuteAsync("UPDATE task_run_recovery_journal SET capsule_json=replace(capsule_json,'" +
            lineage.DefinitionId + "','replacement-memory-definition');", TestContext.Current.CancellationToken);
        var fresh = fixture.NewObservationCoordinator();
        try
        {
            var actual = fresh.ObserveOriginalColdInputAsync(before.TaskId, before.ExecutionId,
                TestContext.Current.CancellationToken);
            var failure = await Record.ExceptionAsync(() => actual);
            Assert.NotNull(failure); Assert.True(actual.IsFaulted);
            Assert.Equal(0L, await fixture.ScalarAsync("SELECT claim_state FROM task_run_recovery_journal;",
                TestContext.Current.CancellationToken));
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(
                await fixture.Rows.GetAsync(before.TaskId, TestContext.Current.CancellationToken)));
            Assert.Single(await fixture.Conversations.GetMessagesAsync(fixture.Conversation.Id,
                TestContext.Current.CancellationToken));
            Assert.Equal(0, fixture.Client.Dispatches); Assert.Equal(0, fixture.Provider.Starts);
        }
        finally { _ = await Record.ExceptionAsync(fresh.CloseAndSuspendOriginalProducersAsync); }
    }

    private static ChatOriginalPersistentMemoryLineage MemoryLineageFor(Guid actualConversationId) =>
        new(1, "untrusted-observation-den", "personal", "memory-lineage-" + Guid.NewGuid().ToString("N"),
            7, "assistant-session-" + actualConversationId.ToString("D"), 3,
            actualConversationId, Guid.NewGuid(), new string('A', 64));
}
