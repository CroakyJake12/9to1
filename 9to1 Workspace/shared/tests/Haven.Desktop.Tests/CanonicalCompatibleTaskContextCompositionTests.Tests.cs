using System.Runtime.CompilerServices;
using Haven.Core;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace Haven.Desktop.Tests;

// PRIVATE, UNCOMPILED/UNRUN. The sibling owning Rig constructs the actual protected
// SQLite, current OS/Home actor, imported store, Files project and individually
// accepted live READ/WRITE. These controls do not certify an installed product.
public sealed partial class CanonicalCompatibleTaskContextCompositionTests
{
    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Individually_accepted_write_creates_a_distinct_Tasks_pair_and_preserves_full_Studio_history()
        => Run(async rig =>
        {
            var before = await rig.ReadStudioRowsAndHistoryBytesAsync();
            var taskId = Guid.NewGuid(); var operationId = Guid.NewGuid();
            var intent = await rig.PrepareStudioIntentAsync(taskId, "Work with the existing project", operationId);
            Assert.True(rig.Creator.IsIssuedOriginalCreationIntent(intent));
            Assert.Same(rig.LiveRead, intent.OriginalProjectRead);
            Assert.Equal(rig.OriginalStudio, intent.OriginalStudioConversation);
            Assert.Equal(rig.OriginalStudioContainer, intent.OriginalStudioContainer);
            Assert.Equal(HavenMode.Studio, intent.OriginalStudioConversation.Mode);
            Assert.Equal(ConversationKind.StudioChat, intent.OriginalStudioConversation.Kind);
            Assert.Equal(HavenMode.Tasks, intent.TaskConversation.Mode);
            Assert.Equal(ConversationKind.Task, intent.TaskConversation.Kind);
            Assert.NotEqual(intent.OriginalStudioContainer.Id, intent.TaskContainer.Id);
            Assert.Equal(intent.TaskContainer.Id, intent.TaskConversation.ContainerId);
            Assert.Equal(rig.LiveRead.OriginalDescriptor.RegisteredProjectRoot, intent.TaskContainer.RootPath);
            Assert.Equal(rig.LiveRead.OriginalDescriptor.ExactProjectReferenceJson, intent.TaskContainer.Context);
            Assert.Equal(rig.OriginalStudioContainer.Instructions, intent.TaskContainer.Instructions);
            Assert.Empty(rig.ApprovalRequestIds);

            var creation = await rig.CommitWithManualWriteAsync(intent);
            Assert.True(rig.Creator.IsIssuedOriginalCreation(creation));
            Assert.Same(intent, creation.OriginalIntent);
            Assert.Equal(intent.Actor, creation.Actor);
            Assert.Equal(operationId, creation.OperationId);
            Assert.Equal(intent.TaskConversation, creation.TaskConversation);
            Assert.Equal(intent.TaskContainer, creation.TaskContainer);
            await rig.Creator.RevalidateOriginalCreationWithinSourceAsync(creation, rig.Scope, rig.Retain, rig.Token);
            Assert.Single(rig.ApprovalRequestIds);
            Assert.Equal(before, await rig.ReadStudioRowsAndHistoryBytesAsync());
            var stored = await rig.ReadTasksPairsAsync();
            Assert.Equal(creation.TaskConversation, Assert.Single(stored.Conversations));
            Assert.Equal(creation.TaskContainer, Assert.Single(stored.Containers));
            Assert.Empty((await rig.Home.Permissions.GetSnapshotAsync(cancellationToken: rig.Token)).Grants);
        });

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Reopened_original_operation_receipt_observes_the_same_pair_without_second_SQL_effects()
        => Run(async rig =>
        {
            var before = await rig.ReadStudioRowsAndHistoryBytesAsync();
            var taskId = Guid.NewGuid(); var operationId = Guid.NewGuid(); const string title = "Recover the original work context";
            var intent = await rig.PrepareStudioIntentAsync(taskId, title, operationId);
            var first = await rig.CommitWithManualWriteAsync(intent);
            var pairBefore = await rig.ReadTasksPairsAsync();
            var originalCreator = rig.Creator;

            // Reopen constructs a new actual creator/WRITE source over the same configured
            // physical store and real Home/read tuple. It reads the durable operation row.
            var reopened = await rig.ReopenIntentAsync(taskId, title, operationId);
            Assert.NotSame(originalCreator, rig.Creator);
            Assert.NotSame(intent, reopened);
            Assert.Equal(first.TaskConversation, reopened.TaskConversation);
            Assert.Equal(first.TaskContainer, reopened.TaskContainer);
            Assert.True(rig.Creator.IsIssuedOriginalCreationIntent(reopened));
            Assert.False(rig.Creator.IsIssuedOriginalCreationIntent(intent));
            var recovered = await rig.CommitWithManualWriteAsync(reopened);
            Assert.True(rig.Creator.IsIssuedOriginalCreation(recovered));
            Assert.Equal(first.TaskConversation, recovered.TaskConversation);
            Assert.Equal(first.TaskContainer, recovered.TaskContainer);
            Assert.Equal(operationId, recovered.OperationId);
            await rig.Creator.RevalidateOriginalCreationWithinSourceAsync(recovered, rig.Scope, rig.Retain, rig.Token);
            var pairAfter = await rig.ReadTasksPairsAsync();
            Assert.Equal(pairBefore.Conversations.ToArray(), pairAfter.Conversations.ToArray());
            Assert.Equal(pairBefore.Containers.ToArray(), pairAfter.Containers.ToArray());
            Assert.Single(pairAfter.Conversations); Assert.Single(pairAfter.Containers);
            Assert.Equal(before, await rig.ReadStudioRowsAndHistoryBytesAsync());
            Assert.Equal(2, rig.ApprovalRequestIds.Count);
            Assert.Equal(2, rig.ApprovalRequestIds.Distinct(StringComparer.Ordinal).Count());
        });

    [LinuxCompatibleStoreTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Collision_or_changed_source_refuses_atomically_without_overwrite_or_orphan_Tasks_container(bool changedStudio)
        => Run(async rig =>
        {
            var taskId = Guid.NewGuid(); var operationId = Guid.NewGuid();
            var intent = await rig.PrepareStudioIntentAsync(taskId, "Preserve the existing source", operationId);
            Conversation? collision = null;
            if (changedStudio) await rig.ChangeOriginalStudioTitleAsync("A later actual Studio edit");
            else collision = await rig.SeedCollidingConversationAsync(taskId);
            var before = await rig.ReadStudioRowsAndHistoryBytesAsync();
            var pairsBefore = await rig.ReadTasksPairsAsync();
            var original = rig.StartOriginalCommit(intent);
            var failure = await Assert.ThrowsAnyAsync<Exception>(async () =>
            { await rig.AcceptNextWriteOrJoinAsync(original); await original; });
            rig.KeepExpected(original, failure);
            Assert.True(original.IsFaulted); Assert.False(original.IsCanceled);
            Assert.False(rig.Creator.IsAcknowledgedOriginalCreationRefusal(original));
            Assert.Equal(before, await rig.ReadStudioRowsAndHistoryBytesAsync());
            var pairsAfter = await rig.ReadTasksPairsAsync();
            Assert.Equal(pairsBefore.Conversations.ToArray(), pairsAfter.Conversations.ToArray());
            Assert.Equal(pairsBefore.Containers.ToArray(), pairsAfter.Containers.ToArray());
            Assert.DoesNotContain(pairsAfter.Containers, container => container.Id == intent.TaskContainer.Id);
            if (collision is not null)
            {
                Assert.Equal(collision, await rig.ReadStoredConversationAsync(collision.Id));
                Assert.Single(rig.ApprovalRequestIds);
            }
            else Assert.Empty(rig.ApprovalRequestIds);
        });

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Creator_retirement_joins_the_same_accepted_write_before_finishing_and_seals_new_creation()
        => Run(async rig =>
        {
            var intent = await rig.PrepareStudioIntentAsync(Guid.NewGuid(), "Join the actual pending WRITE", Guid.NewGuid());
            var actual = rig.StartOriginalCommit(intent);
            var close = rig.Keep(rig.Creator.CloseAndDrainOriginalAsync());
            try
            {
                Assert.Same(close, rig.Creator.CloseAndDrainOriginalAsync());
                Assert.False(close.IsCompleted);
                Assert.Throws<ObjectDisposedException>(() =>
                {
                    _ = rig.Creator.PrepareOriginalCreationIntentWithinSourceAsync(
                        intent.OriginalStoreObservation, rig.LiveRead, Guid.NewGuid(), "Refused later work", Guid.NewGuid(),
                        rig.Scope, rig.Retain, rig.Token);
                });
            }
            finally
            {
                // Release the real broker review even if an early assertion fails. The
                // admitted driver must settle before any Home/store/native owner closes.
                await rig.AcceptNextWriteOrJoinAsync(actual);
            }
            var acknowledgment = await actual;
            await close;
            Assert.True(close.IsCompletedSuccessfully);
            Assert.True(rig.Creator.IsIssuedOriginalCreation(acknowledgment));
            Assert.Single(rig.ApprovalRequestIds);
        });

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Actual_creator_callback_with_restored_execution_context_cannot_join_its_own_originals()
        => Run(async rig =>
        {
            var intent = await rig.PrepareStudioIntentAsync(Guid.NewGuid(), "Inspect the actual callback boundary", Guid.NewGuid());
            var clean = ExecutionContext.Capture()!; var calls = 0;
            void Scope(Action body)
            {
                body();
                ExecutionContext.Run(clean.CreateCopy(), _ =>
                {
                    Assert.Throws<InvalidOperationException>(() => rig.Creator.DemandExternalOriginalJoin());
                    Assert.Throws<InvalidOperationException>(() => { _ = rig.Creator.CloseAndDrainOriginalAsync(); });
                    Interlocked.Increment(ref calls);
                }, null);
            }
            await rig.Keep(rig.Creator.ValidateOriginalCreationIntentWithinSourceAsync(intent, Scope, rig.Retain, rig.Token));
            Assert.True(calls > 0);
            Assert.Null(rig.Creator.OriginalClose);
            rig.Creator.DemandExternalOriginalJoin();
            await rig.Keep(rig.Creator.CloseAndDrainOriginalAsync());
            Assert.True(rig.Creator.OriginalClose!.IsCompletedSuccessfully);
        });

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Exact_individual_WRITE_decline_has_no_SQL_effect_and_does_not_poison_a_later_approved_command_or_close()
        => Run(async rig =>
        {
            var before = await rig.ReadStudioRowsAndHistoryBytesAsync();
            var intent = await rig.PrepareStudioIntentAsync(Guid.NewGuid(), "Decline this specific creation", Guid.NewGuid());
            var actual = rig.StartOriginalCommit(intent);
            await rig.DecideNextAsync(actual, HomeCompatibleTaskContextWriteSource.WriteAction, HomeApprovalChoice.Decline);
            var cause = await Assert.ThrowsAnyAsync<Exception>(() => actual);
            rig.KeepExpected(actual, cause);
            Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
            Assert.True(rig.Creator.IsAcknowledgedOriginalCreationRefusal(actual));
            var homeAcquisition = Assert.Single(rig.ObserveRetainedOriginalSources(),
                rig.IsAcknowledgedOriginalHomeWriteRefusal);
            Assert.NotSame(actual, homeAcquisition);
            Assert.True(homeAcquisition.IsFaulted);
            Assert.True(rig.Creator.IsAcknowledgedOriginalCreationSourceRefusal(homeAcquisition));
            var homeAlias = rig.Keep(Task.FromException(homeAcquisition.Exception!.InnerExceptions[0]));
            rig.KeepExpected(homeAlias, homeAcquisition.Exception.InnerExceptions[0]);
            Assert.False(rig.IsAcknowledgedOriginalHomeWriteRefusal(homeAlias));
            Assert.False(rig.Creator.IsAcknowledgedOriginalCreationSourceRefusal(homeAlias));
            var alias = rig.Keep(Task.FromException<ICanonicalProjectTaskContextCreation>(actual.Exception!.InnerExceptions[0]));
            rig.KeepExpected(alias, actual.Exception.InnerExceptions[0]);
            Assert.False(rig.Creator.IsAcknowledgedOriginalCreationRefusal(alias));
            Assert.Equal(before, await rig.ReadStudioRowsAndHistoryBytesAsync());
            var afterDecline = await rig.ReadTasksPairsAsync();
            Assert.Empty(afterDecline.Conversations); Assert.Empty(afterDecline.Containers);

            // An explicit new operation receives its own genuine individual approval.
            // The declined original remains faulted/cached and is never silently replayed.
            Assert.Same(actual, rig.Creator.CommitOriginalCreationWithinSourceAsync(intent, rig.LiveRead, rig.Scope, rig.Retain, rig.Token));
            var next = await rig.PrepareStudioIntentAsync(Guid.NewGuid(), "Approve a new specific creation", Guid.NewGuid());
            var created = await rig.CommitWithManualWriteAsync(next);
            Assert.True(rig.Creator.IsIssuedOriginalCreation(created));
            Assert.Equal(before, await rig.ReadStudioRowsAndHistoryBytesAsync());
            await rig.Keep(rig.Creator.CloseAndDrainOriginalAsync());
            Assert.True(rig.Creator.OriginalClose!.IsCompletedSuccessfully);
            Assert.True(rig.Creator.IsAcknowledgedOriginalCreationRefusal(actual));
            Assert.Equal(2, rig.ApprovalRequestIds.Count);
        });

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task A_retired_creator_does_not_admit_another_intents_validation_from_an_unrelated_actual_prepare_callback()
        => Run(async rig =>
        {
            var other = await rig.PrepareStudioIntentAsync(Guid.NewGuid(), "Another original issued intent", Guid.NewGuid());
            var invoked = 0;
            void Scope(Action body)
            {
                body();
                if (Interlocked.Exchange(ref invoked, 1) != 0) return;
                rig.Creator.RequestOriginalRetirement();
                Assert.Throws<ObjectDisposedException>(() =>
                {
                    _ = rig.Creator.ValidateOriginalCreationIntentWithinSourceAsync(other, rig.Scope, rig.Retain, rig.Token);
                });
            }
            var admitted = rig.Keep(rig.Creator.PrepareOriginalCreationIntentWithinSourceAsync(other.OriginalStoreObservation,
                rig.LiveRead, Guid.NewGuid(), "The already admitted finite prepare", Guid.NewGuid(), Scope, rig.Retain, rig.Token));
            var returned = await admitted;
            Assert.Equal(1, invoked);
            Assert.True(rig.Creator.IsIssuedOriginalCreationIntent(returned));
            await rig.Keep(rig.Creator.CloseAndDrainOriginalAsync());
            Assert.True(rig.Creator.OriginalClose!.IsCompletedSuccessfully);
            Assert.Empty(rig.ApprovalRequestIds);
            var stored = await rig.ReadTasksPairsAsync();
            Assert.Empty(stored.Conversations); Assert.Empty(stored.Containers);
        });

    public sealed class LinuxCompatibleStoreTheoryAttribute : TheoryAttribute
    {
        public LinuxCompatibleStoreTheoryAttribute([CallerFilePath] string? sourceFilePath = null,
            [CallerLineNumber] int sourceLineNumber = 0) : base(sourceFilePath, sourceLineNumber)
        { if (!OperatingSystem.IsLinux()) Skip = "Actual private Linux kernel store/project controls; Windows is separately unvalidated."; }
    }
}
