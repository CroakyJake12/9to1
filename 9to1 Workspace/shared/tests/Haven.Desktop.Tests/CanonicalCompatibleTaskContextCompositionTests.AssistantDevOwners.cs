using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class CanonicalCompatibleTaskContextCompositionTests
{
    private static readonly List<object> FailedAssistantDevTaskOwners = [];

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public async Task Separate_actual_Home_and_Host_Task_owners_open_transfer_and_reopen_same_Assistant_Dev_project()
    {
        var rig = new Rig(includeOriginalDen: true);
        TaskExecutionCoordinator? tasks = null;
        var custodies = new List<IAssistantOriginalDevelopmentCustody>();
        var failures = new List<Exception>();
        var readApprovals = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            await rig.InitializeAsync();
            var owner = CreateCompatibleCheckpointOwner(rig);
            var first = rig.CreateOriginalAssistantsController(owner);
            await rig.Keep(first.InitializeAsync(rig.Token));
            await rig.Keep(first.CreateAsync(new AssistantConfiguration { Name = "Fictional project helper" }, Guid.NewGuid(), rig.Token));
            var definition = Assert.IsType<AssistantDefinitionSnapshot>(first.Snapshot.SelectedAssistant);
            var homeActor = (await rig.Keep(rig.Home.Profiles.GetCurrentWithinOriginalSourceAsync(rig.Scope, rig.Retain, rig.Token)))!;
            var taskActors = rig.Provider.GetRequiredService<HostLocalTaskActorSource>();
            tasks = rig.Provider.GetRequiredService<TaskExecutionCoordinator>();
            var taskObservation = await rig.Keep(tasks.ObserveOriginalTaskActorWithinSourceAsync(rig.Scope, rig.Retain, rig.Token));
            Assert.True(tasks.IsIssuedOriginalTaskActorObservation(taskObservation, taskActors));
            Assert.False(tasks.IsIssuedOriginalTaskActorObservation(taskObservation, rig.Home.Profiles));
            var taskActor = Assert.IsType<AuthenticatedResourceActor>(taskObservation.Actor);
            Assert.NotEqual(homeActor.ActorId, taskActor.ActorId);
            var originalStudio = await rig.ReadStudioRowsAndHistoryBytesAsync();

            // Real original project READ and a distinct explicit Home WRITE create
            // the canonical Tasks conversation. No Task/provider is fabricated.
            var choice = await ReadAuthorizedCheckpointChoiceAsync(rig, first);
            var create = rig.Keep(first.CreateCompatibleConversationOriginalAsync(choice, Guid.NewGuid(),
                "Fictional source review", Guid.NewGuid(), Guid.NewGuid(), rig.Token));
            var ready = await DecideCheckpointCommandAsync(rig, create, HomeApprovalChoice.Accept);
            var binding = Assert.IsType<AssistantConversationBinding>(ready.Binding);
            var begun = await rig.Keep(tasks.BeginAuthorizedAsync(binding.Conversation.Id, Guid.NewGuid(),
                "Inspect the existing fictional project", TaskExecutionDurability.PersistedPlan, [], rig.Token));
            var begunOwner = Assert.IsType<TaskExecutionOwnerBinding>(begun.OwnerBinding);
            Assert.Equal(taskActor.ActorId, begunOwner.ActorId);
            Assert.NotEqual(homeActor.ActorId, begunOwner.ActorId);
            Assert.Empty(begun.Attempts);
            var context = new ProviderExecutionContext(begun.TaskId, begun.ContextId, begun.ExecutionId,
                begun.Attempts.LastOrDefault()?.Id, begun.PersistenceRevision);
            var work = (await rig.Keep(first.RefreshWorkAsync(rig.Token))).Work!;
            Assert.Equal(begun.TaskId, work.CanonicalTask!.TaskId);
            Assert.False(work.RequiresOwnerRenewal);

            var opened = await AwaitActualDevReadsAsync(rig, rig.Keep(first.OpenDevelopmentOriginalAsync(
                choice.Project.Reference, context, rig.Token)), readApprovals);
            Assert.True(owner.IsIssuedOriginalBinding(opened));
            Assert.Same(binding, opened.Conversation); Assert.Equal(context, opened.CanonicalTask);
            var transfer = rig.Keep(owner.TransferOriginalWithinSourceAsync(opened, rig.Scope, rig.Retain, rig.Token));
            var custody = await AwaitActualDevReadsAsync(rig, transfer, readApprovals); custodies.Add(custody);
            Assert.True(owner.IsIssuedOriginalCustody(opened, custody));
            Assert.Equal(2, readApprovals.Count);
            var current = await rig.Keep(owner.ValidateOriginalBindingWithinSourceAsync(
                opened, custody, rig.Scope, rig.Retain, rig.Token));
            Assert.Equal(homeActor, current.HomeActor); Assert.Equal(taskActor, current.TaskActor);
            Assert.Equal(current.Actor, current.TaskActor);
            Assert.Equal(choice.Project.Reference, current.Project.Reference);
            Assert.Equal(begun.OwnerBinding, current.CanonicalTask.OwnerBinding);
            Assert.Equal(begun.PersistenceRevision, current.CanonicalTask.PersistenceRevision);

            // The transferred Dev borrower owns its actual READ independently of
            // the first presentation. Closing that presentation grants no new access.
            var firstClose = rig.Keep(first.CloseAndDrainAsync()); await firstClose;
            var stillCurrent = await rig.Keep(owner.ValidateOriginalBindingWithinSourceAsync(
                opened, custody, rig.Scope, rig.Retain, rig.Token));
            Assert.Equal(current.HomeActor, stillCurrent.HomeActor);
            Assert.Equal(current.TaskActor, stillCurrent.TaskActor);
            Assert.Equal(current.Project.Reference, stillCurrent.Project.Reference);
            var custodyClose = rig.Keep(custody.CloseAndDrainAsync()); await custodyClose;
            Assert.Same(custodyClose, custody.CloseAndDrainAsync());

            // A fresh presentation keeps the same persisted identity/context/Task.
            // Opening and transferring again require new manual source READs.
            var reopened = rig.CreateOriginalAssistantsController(owner);
            await rig.Keep(reopened.InitializeAsync(rig.Token));
            await rig.Keep(reopened.OpenAssistantAsync(definition.Identity, rig.Token));
            await rig.Keep(reopened.OpenConversationAsync(binding.Conversation.Id, rig.Token));
            var nextWork = (await rig.Keep(reopened.RefreshWorkAsync(rig.Token))).Work!;
            var reopenedTask = Assert.IsType<TaskExecutionSnapshot>(nextWork.CanonicalTask);
            Assert.Equal(begun.TaskId, reopenedTask.TaskId);
            Assert.Equal(begun.ExecutionId, reopenedTask.ExecutionId);
            var next = await AwaitActualDevReadsAsync(rig, rig.Keep(reopened.OpenDevelopmentOriginalAsync(
                choice.Project.Reference, context, rig.Token)), readApprovals);
            var nextCustody = await AwaitActualDevReadsAsync(rig, rig.Keep(owner.TransferOriginalWithinSourceAsync(
                next, rig.Scope, rig.Retain, rig.Token)), readApprovals); custodies.Add(nextCustody);
            var final = await rig.Keep(owner.ValidateOriginalBindingWithinSourceAsync(next, nextCustody,
                rig.Scope, rig.Retain, rig.Token));
            Assert.Equal(4, readApprovals.Count);
            Assert.Equal(definition.Identity, next.Conversation.Definition.Identity);
            Assert.Equal(binding.Conversation.Id, next.Conversation.Conversation.Id);
            Assert.Equal(opened.Project.Reference, final.Project.Reference);
            Assert.Equal(homeActor, final.HomeActor); Assert.Equal(taskActor, final.TaskActor);
            Assert.Equal(begun.OwnerBinding, final.CanonicalTask.OwnerBinding);
            Assert.Empty(final.CanonicalTask.Attempts);
            Assert.Equal(originalStudio, await rig.ReadStudioRowsAndHistoryBytesAsync());
            Assert.Single(rig.ApprovalRequestIds); // Only the separate context-creation WRITE.
        }
        catch (Exception cause) { failures.Add(cause); }
        foreach (var custody in custodies)
        {
            Task? close = null;
            try { custody.RequestRetirement(); close = rig.Keep(custody.CloseAndDrainAsync()); await close; }
            catch (Exception cause) { failures.Add(close?.Exception ?? cause); }
        }
        Task? taskClose = null;
        try { if (tasks is not null) { taskClose = rig.Keep(tasks.CloseAndSuspendOriginalProducersAsync()); await taskClose; } }
        catch (Exception cause) { failures.Add(taskClose?.Exception ?? cause); }
        await rig.JoinOriginalsAsync(failures);
        if (failures.Count != 0)
        {
            lock (FailedAssistantDevTaskOwners) FailedAssistantDevTaskOwners.Add(new object?[] { rig, tasks, custodies, taskClose, failures });
            if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException("Actual Assistant Dev sources remain retained after a failed original join.", failures);
        }
    }

    private static async Task<T> AwaitActualDevReadsAsync<T>(Rig rig, Task<T> actual, HashSet<string> issuedRequests)
    {
        var elapsed = Stopwatch.StartNew(); var start = issuedRequests.Count;
        while (!actual.IsCompleted)
        {
            if (elapsed.Elapsed >= TimeSpan.FromSeconds(20))
                throw new TimeoutException("The actual Dev source has not settled its separate manual project READ.");
            var snapshot = await rig.Keep(rig.Home.Permissions.GetSnapshotAsync(cancellationToken: rig.Token));
            Assert.DoesNotContain(snapshot.PendingRequests, request => request.Scope.ActionName == HomeCompatibleTaskContextWriteSource.WriteAction);
            foreach (var request in snapshot.PendingRequests.Where(request => request.Scope.ActionName == HomeColdProjectReadReconciliation.ReadAction))
            {
                Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
                Assert.True(request.Policy.RequiresPerActionApproval); Assert.True(issuedRequests.Add(request.RequestId));
                Assert.True((await rig.Keep(rig.Home.Permissions.DecideAsync(request.RequestId,
                    HomeApprovalChoice.Accept, cancellationToken: rig.Token))).Succeeded);
            }
            if (!actual.IsCompleted) await Task.Delay(TimeSpan.FromMilliseconds(10), rig.Token);
        }
        var result = await actual;
        Assert.Equal(start + 1, issuedRequests.Count);
        return result;
    }
}
