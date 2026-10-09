using System.Diagnostics;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Dev;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using NineToOne.Dulche.Den;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class CanonicalCompatibleTaskContextCompositionTests
{
    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Declined_Den_checkpoint_closes_original_controller_and_resumes_same_allocated_pair_after_repeated_decline() =>
        RunWithOriginalDen(async rig =>
        {
            var studioBefore = await rig.ReadStudioRowsAndHistoryBytesAsync();
            var owner = CreateCompatibleCheckpointOwner(rig);
            var first = rig.CreateOriginalAssistantsController(owner);
            await rig.Keep(first.InitializeAsync(rig.Token));
            await rig.Keep(first.CreateAsync(new AssistantConfiguration { Name = "actual checkpoint Assistant" }, Guid.NewGuid(), rig.Token));
            var definition = Assert.IsType<AssistantDefinitionSnapshot>(first.Snapshot.SelectedAssistant);
            var choice = await ReadAuthorizedCheckpointChoiceAsync(rig, first);
            var conversationId = Guid.NewGuid(); var creationOp = Guid.NewGuid(); var commandOp = Guid.NewGuid();
            var initial = rig.Keep(first.CreateCompatibleConversationOriginalAsync(choice, conversationId,
                "same durable Tasks conversation", creationOp, commandOp, rig.Token));
            var declined = await DecideCheckpointCommandAsync(rig, initial, HomeApprovalChoice.Decline);
            var checkpoint = Assert.IsType<AssistantCompatibleConversationCheckpoint>(declined.Checkpoint);
            Assert.Equal(AssistantCompatibleConversationCreationState.DeclinedPending, declined.State);
            Assert.Null(declined.Binding); Assert.Null(first.Snapshot.ConversationBinding);
            Assert.Same(declined, first.Snapshot.LastCompatibleConversationOutcome);
            Assert.Same(checkpoint, Assert.Single(first.Snapshot.DeclinedPendingConversations));
            Assert.Equal(conversationId, checkpoint.PlannedConversationId); Assert.Equal(creationOp, checkpoint.OriginalCreationOperationId);
            Assert.Equal(commandOp, checkpoint.LastCommandOperationId); Assert.Equal(definition.Identity, checkpoint.Identity);
            Assert.Empty((await rig.ReadTasksPairsAsync()).Conversations);
            await AssertStoredCheckpointAsync(rig, checkpoint, "declined-pending");
            var closeFirst = rig.Keep(first.CloseAndDrainAsync()); await closeFirst;
            Assert.True(closeFirst.IsCompletedSuccessfully); Assert.Same(closeFirst, first.OriginalClose);
            Assert.Same(closeFirst, first.CloseAndDrainAsync());

            // A fresh presentation reads the durable ACK through the SAME actual process
            // issuer. No previous view's admissions, approval or live READ is reused.
            var reopened = rig.CreateOriginalAssistantsController(owner);
            await rig.Keep(reopened.InitializeAsync(rig.Token));
            await rig.Keep(reopened.OpenAssistantAsync(definition.Identity, rig.Token));
            var saved = Assert.Single(await rig.Keep(reopened.ReadOriginalDeclinedPendingAsync(32, rig.Token)));
            Assert.Equal(checkpoint.OriginalTaskConversation, saved.OriginalTaskConversation);
            Assert.Equal(checkpoint.OriginalTaskContainer, saved.OriginalTaskContainer);
            Assert.Equal(checkpoint.MembershipRevision, saved.MembershipRevision);
            var secondChoice = await ReadAuthorizedCheckpointChoiceAsync(rig, reopened);
            var secondOp = Guid.NewGuid();
            var repeated = await DecideCheckpointCommandAsync(rig,
                rig.Keep(reopened.ResumeCompatibleConversationOriginalAsync(saved, secondOp, secondChoice, rig.Token)), HomeApprovalChoice.Decline);
            var repeatedCheckpoint = Assert.IsType<AssistantCompatibleConversationCheckpoint>(repeated.Checkpoint);
            Assert.Null(repeated.Binding); Assert.Equal(AssistantCompatibleConversationCreationState.DeclinedPending, repeated.State);
            Assert.Equal(saved.OriginalTaskConversation, repeatedCheckpoint.OriginalTaskConversation);
            Assert.Equal(saved.OriginalTaskContainer, repeatedCheckpoint.OriginalTaskContainer);
            Assert.Equal(creationOp, repeatedCheckpoint.OriginalCreationOperationId);
            Assert.Equal(secondOp, repeatedCheckpoint.LastCommandOperationId);
            Assert.True(repeatedCheckpoint.MembershipRevision > saved.MembershipRevision);
            Assert.Empty((await rig.ReadTasksPairsAsync()).Conversations);
            await AssertStoredCheckpointAsync(rig, repeatedCheckpoint, "declined-pending");

            var finalChoice = await ReadAuthorizedCheckpointChoiceAsync(rig, reopened);
            var finalOp = Guid.NewGuid();
            var ready = await DecideCheckpointCommandAsync(rig,
                rig.Keep(reopened.ResumeCompatibleConversationOriginalAsync(repeatedCheckpoint, finalOp, finalChoice, rig.Token)), HomeApprovalChoice.Accept);
            var binding = Assert.IsType<AssistantConversationBinding>(ready.Binding);
            Assert.Null(ready.Checkpoint); Assert.Equal(AssistantCompatibleConversationCreationState.Ready, ready.State);
            Assert.Equal(checkpoint.OriginalTaskConversation, binding.Conversation);
            Assert.Same(binding, reopened.Snapshot.ConversationBinding);
            Assert.Empty(reopened.Snapshot.DeclinedPendingConversations);
            var pairs = await rig.ReadTasksPairsAsync();
            Assert.Equal(checkpoint.OriginalTaskConversation, Assert.Single(pairs.Conversations));
            Assert.Equal(checkpoint.OriginalTaskContainer, Assert.Single(pairs.Containers));
            Assert.Equal(studioBefore, await rig.ReadStudioRowsAndHistoryBytesAsync());
            Assert.Equal(3, rig.ApprovalRequestIds.Count);
            Assert.Equal(3, rig.ApprovalRequestIds.Distinct(StringComparer.Ordinal).Count());
            var closeReopened = rig.Keep(reopened.CloseAndDrainAsync()); await closeReopened;
            Assert.True(closeReopened.IsCompletedSuccessfully);
            var closeOwner = rig.Keep(owner.CloseAndDrainAsync()); await closeOwner;
            Assert.True(closeOwner.IsCompletedSuccessfully); Assert.Same(closeOwner, owner.CloseAndDrainAsync());
            // These explicit whole-cohort closes prove deep raw declined Home/SQL
            // occurrences were settled, not only a successful public stage envelope.
        });

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Changed_definition_refuses_old_pending_checkpoint_before_new_WRITE_and_preserves_durable_tuple() =>
        RunWithOriginalDen(async rig =>
        {
            var owner = CreateCompatibleCheckpointOwner(rig); var controller = rig.CreateOriginalAssistantsController(owner);
            await rig.Keep(controller.InitializeAsync(rig.Token));
            await rig.Keep(controller.CreateAsync(new AssistantConfiguration { Name = "revision guarded pending" }, Guid.NewGuid(), rig.Token));
            var definition = Assert.IsType<AssistantDefinitionSnapshot>(controller.Snapshot.SelectedAssistant);
            var choice = await ReadAuthorizedCheckpointChoiceAsync(rig, controller);
            var declined = await DecideCheckpointCommandAsync(rig, rig.Keep(controller.CreateCompatibleConversationOriginalAsync(
                choice, Guid.NewGuid(), "reserved pending title", Guid.NewGuid(), Guid.NewGuid(), rig.Token)), HomeApprovalChoice.Decline);
            var checkpoint = Assert.IsType<AssistantCompatibleConversationCheckpoint>(declined.Checkpoint);
            var before = await ReadActualCheckpointRecordAsync(rig, checkpoint);
            await rig.Keep(controller.ConfigureAsync(definition.Identity, definition.Revision,
                definition.Configuration with { Description = "actual new definition revision" }, Guid.NewGuid(), rig.Token));
            var commandsBefore = rig.ApprovalRequestIds.Count;
            var refusalTask = rig.Keep(controller.ResumeCompatibleConversationOriginalAsync(checkpoint, Guid.NewGuid(), choice, rig.Token));
            var refused = await Record.ExceptionAsync(() => refusalTask); Assert.NotNull(refused);
            Assert.True(controller.IsAcknowledgedOriginalCommandRefusal(refusalTask)); rig.KeepExpected(refusalTask, refused);
            Assert.Equal(commandsBefore, rig.ApprovalRequestIds.Count);
            Assert.Equal(before, await ReadActualCheckpointRecordAsync(rig, checkpoint));
            Assert.Empty((await rig.ReadTasksPairsAsync()).Conversations); Assert.Null(controller.Snapshot.ConversationBinding);
            var closeController = rig.Keep(controller.CloseAndDrainAsync()); await closeController;
            var closeOwner = rig.Keep(owner.CloseAndDrainAsync()); await closeOwner;
            Assert.True(closeController.IsCompletedSuccessfully); Assert.True(closeOwner.IsCompletedSuccessfully);
        });

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Changed_title_for_same_creation_operation_refuses_replay_without_adopting_or_creating_SQL_rows() =>
        RunWithOriginalDen(async rig =>
        {
            var owner = CreateCompatibleCheckpointOwner(rig); var controller = rig.CreateOriginalAssistantsController(owner);
            await rig.Keep(controller.InitializeAsync(rig.Token));
            await rig.Keep(controller.CreateAsync(new AssistantConfiguration { Name = "exact replay title" }, Guid.NewGuid(), rig.Token));
            var choice = await ReadAuthorizedCheckpointChoiceAsync(rig, controller); var conversation = Guid.NewGuid();
            var creation = Guid.NewGuid(); var command = Guid.NewGuid();
            var initial = await DecideCheckpointCommandAsync(rig, rig.Keep(controller.CreateCompatibleConversationOriginalAsync(
                choice, conversation, "original reserved title", creation, command, rig.Token)), HomeApprovalChoice.Decline);
            var checkpoint = Assert.IsType<AssistantCompatibleConversationCheckpoint>(initial.Checkpoint);
            var before = await ReadActualCheckpointRecordAsync(rig, checkpoint); var writesBefore = rig.ApprovalRequestIds.Count;
            var refusalTask = rig.Keep(controller.CreateCompatibleConversationOriginalAsync(choice, conversation,
                "changed title under same operation", creation, command, rig.Token));
            var refused = await Record.ExceptionAsync(() => refusalTask); Assert.NotNull(refused);
            Assert.True(controller.IsAcknowledgedOriginalCommandRefusal(refusalTask)); rig.KeepExpected(refusalTask, refused);
            Assert.Equal(before, await ReadActualCheckpointRecordAsync(rig, checkpoint));
            Assert.Equal(writesBefore, rig.ApprovalRequestIds.Count); Assert.Empty((await rig.ReadTasksPairsAsync()).Conversations);
            var closeController = rig.Keep(controller.CloseAndDrainAsync()); await closeController;
            var closeOwner = rig.Keep(owner.CloseAndDrainAsync()); await closeOwner;
            Assert.True(closeController.IsCompletedSuccessfully); Assert.True(closeOwner.IsCompletedSuccessfully);
        });

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Existing_source_authorized_Tasks_choice_returns_ready_in_same_container_without_another_Studio_pair_WRITE() =>
        RunWithOriginalDen(async rig =>
        {
            var studioBefore = await rig.ReadStudioRowsAndHistoryBytesAsync();
            var owner = CreateCompatibleCheckpointOwner(rig); var controller = rig.CreateOriginalAssistantsController(owner);
            await rig.Keep(controller.InitializeAsync(rig.Token));
            await rig.Keep(controller.CreateAsync(new AssistantConfiguration { Name = "existing Tasks route" }, Guid.NewGuid(), rig.Token));
            var studioChoice = await ReadAuthorizedCheckpointChoiceAsync(rig, controller);
            var firstReady = await DecideCheckpointCommandAsync(rig, rig.Keep(controller.CreateCompatibleConversationOriginalAsync(
                studioChoice, Guid.NewGuid(), "first real compatible Tasks conversation", Guid.NewGuid(), Guid.NewGuid(), rig.Token)), HomeApprovalChoice.Accept);
            var firstBinding = Assert.IsType<AssistantConversationBinding>(firstReady.Binding);
            Assert.Equal(AssistantCompatibleConversationCreationState.Ready, firstReady.State); Assert.Null(firstReady.Checkpoint);
            var firstPair = await rig.ReadTasksPairsAsync();
            var sameTaskContainer = Assert.Single(firstPair.Containers);
            Assert.Equal(firstBinding.Conversation, Assert.Single(firstPair.Conversations));
            Assert.True(firstBinding.Conversation.UpdatedAt > rig.OriginalStudio.UpdatedAt);

            // The protected source's persisted recent ordering puts the newly created
            // Tasks conversation first. That order selects a real candidate; the UI/test
            // never constructs a mode/container/root grant. The producer independently
            // revalidates and chooses its owning path from the private actual choice.
            var catalogue = await rig.Keep(controller.ReadOriginalProjectCandidatesAsync(32, rig.Token));
            Assert.Equal(2, catalogue.Candidates.Count);
            var taskChoiceTask = rig.Keep(controller.AuthorizeOriginalProjectChoiceAsync(catalogue.Candidates[0], rig.Token));
            await rig.DecideNextAsync(taskChoiceTask, HomeColdProjectReadReconciliation.ReadAction, HomeApprovalChoice.Accept);
            var taskChoice = await taskChoiceTask;
            var writesBefore = rig.ApprovalRequestIds.Count; var conversationId = Guid.NewGuid(); var creationOp = Guid.NewGuid();
            var ready = await AwaitCheckpointReadsWithoutWriteAsync(rig, rig.Keep(controller.CreateCompatibleConversationOriginalAsync(
                taskChoice, conversationId, "second conversation in actual Tasks container", creationOp, Guid.NewGuid(), rig.Token)));
            var binding = Assert.IsType<AssistantConversationBinding>(ready.Binding);
            Assert.Equal(AssistantCompatibleConversationCreationState.Ready, ready.State); Assert.Null(ready.Checkpoint);
            Assert.Equal(conversationId, binding.Conversation.Id); Assert.Equal(sameTaskContainer.Id, binding.Conversation.ContainerId);
            Assert.Equal(HavenMode.Tasks, binding.Conversation.Mode); Assert.Same(binding, controller.Snapshot.ConversationBinding);
            var pairs = await rig.ReadTasksPairsAsync(); Assert.Equal(2, pairs.Conversations.Count);
            Assert.Equal(sameTaskContainer, Assert.Single(pairs.Containers)); Assert.Contains(firstBinding.Conversation, pairs.Conversations);
            Assert.Contains(binding.Conversation, pairs.Conversations); Assert.Equal(writesBefore, rig.ApprovalRequestIds.Count);
            Assert.Equal(studioBefore, await rig.ReadStudioRowsAndHistoryBytesAsync());

            // The acknowledged second create changes the original catalogue from
            // two rows to three. A retry needs a freshly issued selection and its own
            // manual READ; the earlier full-window observation correctly stays stale.
            var replayCatalogue = await rig.Keep(controller.ReadOriginalProjectCandidatesAsync(32, rig.Token));
            Assert.Equal(3, replayCatalogue.Candidates.Count);
            Assert.True(binding.Conversation.UpdatedAt > firstBinding.Conversation.UpdatedAt);
            var replayChoiceTask = rig.Keep(controller.AuthorizeOriginalProjectChoiceAsync(replayCatalogue.Candidates[0], rig.Token));
            await rig.DecideNextAsync(replayChoiceTask, HomeColdProjectReadReconciliation.ReadAction, HomeApprovalChoice.Accept);
            var replayChoice = await replayChoiceTask;
            Assert.Equal(taskChoice.Project.Reference, replayChoice.Project.Reference);
            Assert.Equal(writesBefore, rig.ApprovalRequestIds.Count);
            var replay = await AwaitCheckpointReadsWithoutWriteAsync(rig, rig.Keep(controller.CreateCompatibleConversationOriginalAsync(
                replayChoice, conversationId, binding.Conversation.Title, creationOp, Guid.NewGuid(), rig.Token)));
            Assert.Equal(binding.Conversation, replay.Binding!.Conversation); Assert.Null(replay.Checkpoint);
            Assert.Equal(2, (await rig.ReadTasksPairsAsync()).Conversations.Count);
            Assert.Equal(sameTaskContainer.Id, replay.Binding!.Conversation.ContainerId);
            var changedTitle = rig.Keep(controller.CreateCompatibleConversationOriginalAsync(replayChoice, conversationId,
                "changed existing Tasks replay title", creationOp, Guid.NewGuid(), rig.Token));
            var refusal = await Record.ExceptionAsync(() => changedTitle); Assert.NotNull(refusal);
            Assert.True(controller.IsAcknowledgedOriginalCommandRefusal(changedTitle)); rig.KeepExpected(changedTitle, refusal);
            Assert.Equal(2, (await rig.ReadTasksPairsAsync()).Conversations.Count); Assert.Equal(writesBefore, rig.ApprovalRequestIds.Count);
            var controllerClose = rig.Keep(controller.CloseAndDrainAsync()); await controllerClose;
            var ownerClose = rig.Keep(owner.CloseAndDrainAsync()); await ownerClose;
            Assert.True(controllerClose.IsCompletedSuccessfully); Assert.True(ownerClose.IsCompletedSuccessfully);
        });

    private static async Task<AssistantCompatibleConversationCreationOutcome> AwaitCheckpointReadsWithoutWriteAsync(Rig rig,
        Task<AssistantCompatibleConversationCreationOutcome> sameOriginal)
    {
        var elapsed = Stopwatch.StartNew();
        while (!sameOriginal.IsCompleted)
        {
            if (elapsed.Elapsed >= TimeSpan.FromSeconds(20))
                throw new TimeoutException("The actual existing Tasks command remains retained without its next manual READ or terminal outcome.");
            var state = await rig.Keep(rig.Home.Permissions.GetSnapshotAsync(cancellationToken: rig.Token));
            Assert.DoesNotContain(state.PendingRequests, request => request.Scope.ActionName == HomeCompatibleTaskContextWriteSource.WriteAction);
            foreach (var request in state.PendingRequests.Where(request => request.Scope.ActionName == HomeColdProjectReadReconciliation.ReadAction))
            {
                Assert.Equal(HomePermissionRequestState.PendingApproval, request.State); Assert.True(request.Policy.RequiresPerActionApproval);
                Assert.True((await rig.Keep(rig.Home.Permissions.DecideAsync(request.RequestId,
                    HomeApprovalChoice.Accept, cancellationToken: rig.Token))).Succeeded);
            }
            if (!sameOriginal.IsCompleted) await Task.Delay(TimeSpan.FromMilliseconds(10), rig.Token);
        }
        return await sameOriginal;
    }

    private static DenAssistantOriginalDevelopmentOwner CreateCompatibleCheckpointOwner(Rig rig)
    {
        // Retain the SAME configured Task actor and conversation wrapper used by the
        // production App and the actual controller. Home profile identity stays separate.
        var conversations = rig.Provider.GetRequiredService<IConversationRepository>();
        var containers = rig.Provider.GetRequiredService<IContainerRepository>();
        var tasks = rig.Provider.GetRequiredService<TaskExecutionCoordinator>();
        var developer = rig.Provider.GetRequiredService<DeveloperTaskWorkspaceService>();
        var taskActors = rig.Provider.GetRequiredService<HostLocalTaskActorSource>();
        var journal = rig.Provider.GetRequiredService<ITaskRunColdRecoveryJournal>();
        var coldContext = rig.Provider.GetRequiredService<ITaskRunColdContextAuthority>();
        Assert.Same(rig.Home.Profiles, rig.Provider.GetRequiredService<IAuthenticatedResourceActorSource>());
        Assert.NotSame(rig.Home.Profiles, taskActors);
        Assert.Same(rig.Containers, containers);
        Assert.True(developer.IsBoundToOriginalCanonicalOwner(tasks));
        Assert.True(rig.ProjectReads.HasOriginalColdProjectComposition(journal, taskActors));
        Assert.True(tasks.HasOriginalColdRecoveryComposition(journal, coldContext));
        var owner = new DenAssistantOriginalDevelopmentOwner(rig.DenFactory!, conversations, containers,
            tasks, developer, taskActors, rig.ProjectReads, journal, coldContext, rig.Contexts, rig.Creator);
        Assert.Same(conversations, owner.OriginalConversationOwner);
        Assert.Same(taskActors, owner.OriginalTaskActorOwner);
        rig.Creator.BindOriginalResumeSelectionSource(owner); return owner;
    }
    private static async Task<AssistantOriginalProjectChoice> ReadAuthorizedCheckpointChoiceAsync(Rig rig, AssistantsWorkspaceController controller)
    {
        var catalogue = await rig.Keep(controller.ReadOriginalProjectCandidatesAsync(32, rig.Token));
        var candidate = Assert.Single(catalogue.Candidates, value => value.Title == rig.OriginalStudioContainer.Name);
        var actual = rig.Keep(controller.AuthorizeOriginalProjectChoiceAsync(candidate, rig.Token));
        await rig.DecideNextAsync(actual, HomeColdProjectReadReconciliation.ReadAction, HomeApprovalChoice.Accept);
        return await actual;
    }
    private static async Task<AssistantCompatibleConversationCreationOutcome> DecideCheckpointCommandAsync(Rig rig,
        Task<AssistantCompatibleConversationCreationOutcome> actual, HomeApprovalChoice writeChoice)
    {
        var elapsed = Stopwatch.StartNew(); var writes = new List<string>();
        while (!actual.IsCompleted)
        {
            if (elapsed.Elapsed >= TimeSpan.FromSeconds(20))
                throw new TimeoutException("The actual checkpoint command remains retained without its next manual READ/WRITE or terminal outcome.");
            var state = await rig.Keep(rig.Home.Permissions.GetSnapshotAsync(cancellationToken: rig.Token));
            foreach (var request in state.PendingRequests)
            {
                if (request.Scope.ActionName is not (HomeColdProjectReadReconciliation.ReadAction or HomeCompatibleTaskContextWriteSource.WriteAction)) continue;
                Assert.Equal(HomePermissionRequestState.PendingApproval, request.State); Assert.True(request.Policy.RequiresPerActionApproval);
                var isWrite = request.Scope.ActionName == HomeCompatibleTaskContextWriteSource.WriteAction;
                if (isWrite) { Assert.Empty(writes); writes.Add(request.RequestId); rig.ApprovalRequestIds.Add(request.RequestId); }
                var decided = await rig.Keep(rig.Home.Permissions.DecideAsync(request.RequestId,
                    isWrite ? writeChoice : HomeApprovalChoice.Accept, cancellationToken: rig.Token));
                Assert.True(decided.Succeeded);
            }
            if (!actual.IsCompleted) await Task.Delay(TimeSpan.FromMilliseconds(10), rig.Token);
        }
        var outcome = await actual; Assert.Single(writes); return outcome;
    }
    private static async Task<byte[]> ReadActualCheckpointRecordAsync(Rig rig, AssistantCompatibleConversationCheckpoint checkpoint)
    {
        var actual = await rig.Keep(rig.OriginalOpenedDen!.Den.GetAsync<SessionRecord>(checkpoint.Identity.NamespaceId,
            checkpoint.DenSessionId, rig.Token));
        Assert.NotNull(actual); return JsonSerializer.SerializeToUtf8Bytes<DenRecord>(actual, DenJson.Options);
    }
    private static async Task AssertStoredCheckpointAsync(Rig rig, AssistantCompatibleConversationCheckpoint checkpoint, string expectedPublication)
    {
        var bytes = await ReadActualCheckpointRecordAsync(rig, checkpoint);
        var actual = Assert.IsType<SessionRecord>(JsonSerializer.Deserialize<DenRecord>(bytes, DenJson.Options));
        Assert.Equal(checkpoint.MembershipRevision, actual.Revision);
        Assert.Equal(checkpoint.PlannedConversationId.ToString("D"), actual.ConversationId);
        Assert.NotNull(actual.ExtensionData); Assert.Equal(expectedPublication,
            actual.ExtensionData["assistants.membership.v1"].GetProperty("publication").GetString());
    }
}
