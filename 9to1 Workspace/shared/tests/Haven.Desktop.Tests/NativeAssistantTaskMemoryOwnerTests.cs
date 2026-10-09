#if !ANDROID
using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Contracts;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public async Task Native_Task_memory_requires_separate_actual_Home_permission_and_exact_current_Task_context()
    {
        var taskActors = new HostLocalTaskActorSource();
        TaskProgressGraph? graph = null;
        var rig = new Rig(configuredTaskFactory: actual => (graph = new(actual, taskActors)).Tasks,
            closeConfiguredTasks: () => graph?.CloseAsync() ?? Task.CompletedTask);
        var failures = new List<Exception>();
        Task? expectedReadFailure = null;
        try
        {
            await rig.InitializeAsync(true);
            var homeActor = (await rig.Profiles.GetCurrentAsync(Token))!;
            var taskActor = (await taskActors.GetCurrentAsync(Token))!;
            Assert.NotNull(taskActor); Assert.NotEqual(homeActor.ActorId, taskActor.ActorId);
            // First create a real local record through separate per-action Home WRITE.
            // The Task identity never substitutes for that import or write approval.
            var chat = await rig.CreateAsync(new() { Name = "Fictional native memory helper", Memory = new(true) });
            var preparedChat = await rig.PrepareAsync(chat); Assert.True(preparedChat.IsPrepared, preparedChat.Reason);
            var intentTask = rig.Memory.PrepareOriginalWriteWithinSourceAsync(preparedChat.Input!, chat.Conversation,
                "Preferred examples", "Use short fictional examples", Guid.NewGuid(), Scope, rig.Retain, Token);
            rig.Retain(intentTask); var intent = await intentTask;
            var write = rig.Memory.CommitOriginalWriteWithinSourceAsync(intent, Scope, rig.Retain, Token); rig.Retain(write);
            await DecideFirstUseMemory(rig, write); var saved = await write;
            Assert.Same(intent, saved.OriginalIntent);
            var binding = await rig.Bridge.CreateConversationAsync(chat.Definition.Identity, chat.Definition.Revision,
                Guid.NewGuid(), "Fictional work with a permitted preference", Guid.NewGuid(), Token, AssistantConversationKind.Task);
            var begin = graph!.Tasks.BeginAuthorizedAsync(binding.Conversation.Id, Guid.NewGuid(),
                "Review a fictional local draft", TaskExecutionDurability.PersistedPlan, [], Token);
            rig.Retain(begin); var begun = await begin;
            Assert.Equal(taskActor.ActorId, begun.OwnerBinding!.ActorId);
            Assert.NotEqual(homeActor.ActorId, begun.OwnerBinding.ActorId);
            var prepared = await rig.PrepareAsync(binding); Assert.True(prepared.IsPrepared, prepared.Reason);
            var context = new ProviderExecutionContext(begun.TaskId, begun.ContextId, begun.ExecutionId,
                begun.Attempts.LastOrDefault()?.Id, begun.PersistenceRevision);
            var before = await rig.CaptureMemoryAsync();
            var read = rig.Memory.ReadOriginalWithinSourceAsync(prepared.Input!, binding.Conversation,
                context, Scope, rig.Retain, Token); rig.Retain(read);
            var record = Assert.Single(await read);
            Assert.Equal(saved.Record.Id, record.Id); Assert.Equal(saved.Record.Scope, record.Scope);
            Assert.Equal("Use short fictional examples", record.Summary);
            Assert.Equal(before, await rig.CaptureMemoryAsync());
            // A changed expected revision is observation data, never a substitute
            // for the actual Task source. No record or ownership is rewritten.
            var stale = rig.Memory.ReadOriginalWithinSourceAsync(prepared.Input!, binding.Conversation,
                context with { PersistenceRevision = checked(context.PersistenceRevision + 1) }, Scope, rig.Retain, Token);
            expectedReadFailure = stale; rig.Retain(stale);
            var expectedCause = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => stale);
            Assert.Equal("The canonical Task or its actual current owner changed before memory access.", expectedCause.Message);
            Assert.Null(expectedCause.InnerException);
            Assert.True(stale.IsFaulted); Assert.False(stale.IsCanceled);
            // Inspect only the SAME Task's aggregate envelope. Any additional or
            // foreign group/cause must fail this negative control, not be flattened.
            Assert.Same(expectedCause, Assert.Single(stale.Exception!.InnerExceptions));
            var close = rig.Memory.CloseAndDrainAsync(); rig.Retain(close);
            var closeCause = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => close);
            Assert.Same(expectedCause, closeCause);
            Assert.True(close.IsFaulted); Assert.Same(close, rig.Memory.CloseAndDrainAsync());
            Assert.Same(expectedCause, Assert.Single(close.Exception!.InnerExceptions));
            // Only after checking the entire exact close envelope can this fixture
            // retain the expected failed custody without counting it as a new fault.
            rig.ExpectedMemoryClose = close;
            Assert.Equal(before, await rig.CaptureMemoryAsync());
            var finalTask = graph.Tasks.GetAsync(begun.TaskId, Token); rig.Retain(finalTask);
            var actual = (await finalTask)!;
            Assert.Equal(begun.OwnerBinding, actual.OwnerBinding); Assert.Empty(actual.Attempts);
        }
        catch (Exception cause) { failures.Add(cause); }
        Task? processClose = null;
        try { processClose = rig.CloseAsync(); rig.Retain(processClose); await processClose; }
        catch (Exception cause) { failures.Add(processClose?.Exception ?? cause); }
        // Preserve the actual intentionally failed source and every original close,
        // even after the assertions prove the negative outcome.
        if (failures.Count != 0 || expectedReadFailure is not null)
            lock (FailedTaskProgressOwners) FailedTaskProgressOwners.Add([rig, graph!, taskActors, expectedReadFailure!, failures]);
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("Actual Task memory owner fixture retained its sources.", failures);
    }
}
#endif
