using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
namespace Haven.Infrastructure.Tests;

public sealed class AutomationDefinitionReviewCallerTests
{
    [Fact]
    public async Task Actual_Home_review_keeps_SQL_unchanged_until_individual_approval_then_finishes_same_disabled_definition_once()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-automation-caller-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var services = new ServiceCollection().AddHavenInfrastructure().AddHavenAutomationDefinitionOwnership();
            services.AddSingleton<IAppPaths>(new Paths(root));
            services.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            await using var graph = services.BuildServiceProvider();
            var database = graph.GetRequiredService<SqliteDatabase>();
            await database.InitializeAsync(token);
            var actor = (await graph.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(token))!;
            var identity = await database.GetStoreIdentityAsync(token);
            await graph.GetRequiredService<HomeLocalStoreOwnership>().BindNewEmptyAsync(actor,
                "automations", identity.StoreId.ToString("D"), token);
            var caller = graph.GetRequiredService<IAutomationDefinitionReviewCaller>();
            Assert.Same(graph.GetRequiredService<AutomationDefinitionReviewCaller>(), caller);
            var selection = await caller.CaptureAsync(actor, token);
            var now = DateTimeOffset.UtcNow;
            var proposal = new AutomationDefinition(Guid.NewGuid(), "Actual reviewed draft", default, "Original instruction",
                AutomationScheduleKind.Daily, "{}", null, null, false, now, now);
            var review = await caller.ReviewAsync(selection, proposal, 0, AutomationDefinitionChangeKind.Create, token);
            var repository = graph.GetRequiredService<IAutomationOwnerRepository>();
            Assert.Null(await repository.GetOwnedAsync(proposal.Id, token));
            Assert.Null((await review.FinishAsync(token)).Committed);
            Assert.Null(await repository.GetOwnedAsync(proposal.Id, token));
            var permissions = graph.GetRequiredService<HomePermissionTrustService>();
            Assert.Equal(review.RequestId, Assert.Single((await permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests).RequestId);
            Assert.True((await permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
            Assert.True((await review.FinishAsync(token)).Committed);
            var committed = Assert.IsType<AutomationOwnerRead<AutomationDefinition>>(await repository.GetOwnedAsync(proposal.Id, token));
            Assert.False(committed.Value.IsEnabled);
            Assert.Null(committed.Value.NextRunAt);
            Assert.Equal(AutomationOperationalState.NeedsAttention, committed.Value.OperationalState);
            Assert.Equal(1, committed.Value.Revision);
            Assert.True((await review.FinishAsync(token)).Committed);
            Assert.Equal(committed.Value.LastOwnerCommit, (await repository.GetOwnedAsync(proposal.Id, token))!.Value.LastOwnerCommit);
            // Cancellation before operation-gate admission aborts this actual unclaimed capability,
            // retaining its negative audit handle instead of inferring an ambiguous SQL outcome or replay.
            var cancelledProposal = proposal with { Id = Guid.NewGuid(), Name = "Cancelled before claim" };
            var cancelledIntent = AutomationDefinitionChange.Capture(identity.StoreId, actor, cancelledProposal, 0, AutomationDefinitionChangeKind.Create);
            var broker = graph.GetRequiredService<HomeResourceOperationBroker>();
            var cancelReview = await broker.AuthorizeForActorAsync(actor, AutomationDefinitionChange.TargetAppID,
                cancelledIntent.ActionID, cancelledIntent.Scopes, cancelledIntent.Arguments, "Actual cancelled owner operation", null,
                "automation-caller-cancel", token);
            Assert.True((await permissions.DecideAsync(cancelReview.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
            var cancelCapability = Assert.IsType<HomeResourceExecutionCapability>(await broker.BeginExecutionCapabilityAsync(cancelReview.RequestId, cancelledIntent.Arguments, token));
            var ownerOperation = graph.GetRequiredService<AutomationHomeDefinitionOperation>();
            var cancelObservation = ownerOperation.PrepareObservation(cancelledIntent, cancelCapability);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var cancelledResult = await ownerOperation.ExecuteAsync(cancelledIntent, cancelCapability, cancelled.Token);
            Assert.False(cancelledResult.Committed);
            Assert.Null(await repository.GetOwnedAsync(cancelledProposal.Id, token));
            var terminal = (await broker.GetExecutionDecisionAsync(cancelCapability, token))!;
            Assert.Equal(HomePermissionRequestState.Failed, terminal.State);
            Assert.Equal("HOME_RESOURCE_EXECUTION_ABORTED", terminal.Code);
            Assert.False((await cancelObservation.ObserveAsync(token)).Committed);
            Assert.False((await ownerOperation.ExecuteAsync(cancelledIntent, cancelCapability, token)).Committed);
            Assert.Null(await repository.GetOwnedAsync(cancelledProposal.Id, token));
            // A public cleanup call cannot abort/overwrite an already claimed original operation.
            var heldProposal = proposal with { Id = Guid.NewGuid(), Name = "Held actual owning transaction" };
            var heldIntent = AutomationDefinitionChange.Capture(identity.StoreId, actor, heldProposal, 0, AutomationDefinitionChangeKind.Create);
            var heldRequest = await broker.AuthorizeForActorAsync(actor, AutomationDefinitionChange.TargetAppID,
                heldIntent.ActionID, heldIntent.Scopes, heldIntent.Arguments, "Actual held owner operation", null, "automation-held", token);
            Assert.True((await permissions.DecideAsync(heldRequest.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
            var heldCapability = Assert.IsType<HomeResourceExecutionCapability>(await broker.BeginExecutionCapabilityAsync(heldRequest.RequestId, heldIntent.Arguments, token));
            var holding = new HoldingRepository(repository);
            var heldOperation = new AutomationHomeDefinitionOperation(holding, graph.GetRequiredService<IReusableTaskOwnerRepository>(),
                graph.GetRequiredService<AutomationLocalStoreAuthority>(), broker);
            var executing = heldOperation.ExecuteAsync(heldIntent, heldCapability, token);
            try
            {
                await holding.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                Assert.Null((await heldOperation.AbortUnstartedAsync(heldIntent, heldCapability)).Committed);
                Assert.Equal(HomePermissionRequestState.Executing, (await broker.GetExecutionDecisionAsync(heldCapability, token))!.State);
            }
            finally { holding.Release.TrySetResult(); }
            Assert.True((await executing.WaitAsync(TimeSpan.FromSeconds(10), token)).Committed);
            var heldRow = (await repository.GetOwnedAsync(heldProposal.Id, token))!.Value;
            Assert.True((await heldOperation.AbortUnstartedAsync(heldIntent, heldCapability)).Committed);
            Assert.Equal(heldRow.LastOwnerCommit, (await repository.GetOwnedAsync(heldProposal.Id, token))!.Value.LastOwnerCommit);
            // Actual tool entrypoint: old scoped gate is additional policy, never individual Home approval.
            var legacyGate = new PermissionDecisionEngine(); legacyGate.Grant("tasks.reusable.create");
            var arguments = new Dictionary<string, JsonElement>
            {
                ["name"] = JsonSerializer.SerializeToElement("Original tool draft"),
                ["instruction"] = JsonSerializer.SerializeToElement("Original before safety await")
            };
            var runtime = new AutomationToolRuntime(graph.GetRequiredService<IAutomationRepository>(),
                graph.GetRequiredService<IWorkspaceStateRepository>(), legacyGate,
                new MutatingSafety(graph.GetRequiredService<IConversationSafetyService>(),
                    () => arguments["instruction"] = JsonSerializer.SerializeToElement("Substituted after owner capture")),
                graph.GetRequiredService<IAuthenticatedResourceActorSource>(), caller);
            var toolCall = new OllamaToolCall("task_create", arguments, "actual-retained-tool-call");
            var conversationId = Guid.NewGuid();
            var unbound = await runtime.ExecuteAsync(toolCall, default, conversationId, null, token);
            Assert.False(unbound.Activity.Succeeded);
            Assert.Contains("original issuer-owned selection", unbound.Output, StringComparison.OrdinalIgnoreCase);
            var forged = await runtime.ExecuteAsync(toolCall, default, conversationId, null, new ForgedSelection(selection.StoreId, actor), token);
            Assert.False(forged.Activity.Succeeded);
            var toolPending = await runtime.ExecuteAsync(toolCall, default, conversationId, null, selection, token);
            Assert.False(toolPending.Activity.Succeeded);
            Assert.Contains("Review pending", toolPending.Output, StringComparison.Ordinal);
            var pending = Assert.Single((await permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
            var tasks = graph.GetRequiredService<IReusableTaskOwnerRepository>();
            Assert.Empty((await tasks.ListOwnedTasksAsync(new(), token)).Items);
            var operationId = Guid.Parse(toolPending.Output.Split("Operation: ", StringSplitOptions.None)[1].Split(';')[0]);
            var beforeApprovalFinish = await runtime.ExecuteAsync(new OllamaToolCall("automation_finish_review",
                new Dictionary<string, JsonElement> { ["operation_id"] = JsonSerializer.SerializeToElement(operationId.ToString("D")) },
                "finish-before-home-approval"), default, conversationId, null, selection, token);
            Assert.False(beforeApprovalFinish.Activity.Succeeded);
            using (var awaiting = JsonDocument.Parse(beforeApprovalFinish.Output))
                Assert.Equal(JsonValueKind.Null, awaiting.RootElement.GetProperty("Committed").ValueKind);
            Assert.Empty((await tasks.ListOwnedTasksAsync(new(), token)).Items);
            Assert.True((await permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
            var finishCall = new OllamaToolCall("automation_finish_review", new Dictionary<string, JsonElement>
            { ["operation_id"] = JsonSerializer.SerializeToElement(operationId.ToString("D")) }, "finish-original-tool-review");
            var finishedTool = await runtime.ExecuteAsync(finishCall, default, conversationId, null, selection, token);
            Assert.True(finishedTool.Activity.Succeeded);
            using (var outcome = JsonDocument.Parse(finishedTool.Output))
            { Assert.True(outcome.RootElement.GetProperty("Committed").GetBoolean()); Assert.Equal("DefinitionCommitted", outcome.RootElement.GetProperty("Code").GetString()); }
            var actualTask = Assert.Single((await tasks.ListOwnedTasksAsync(new(), token)).Items).Value;
            Assert.Equal("Original before safety await", actualTask.Instruction);
            Assert.False(actualTask.IsEnabled);
            var repeatedFinish = await runtime.ExecuteAsync(finishCall, default, conversationId, null, selection, token);
            Assert.True(repeatedFinish.Activity.Succeeded);
            var substitutedReplay = await runtime.ExecuteAsync(toolCall, default, conversationId, null, selection, token);
            Assert.False(substitutedReplay.Activity.Succeeded);
            Assert.Contains("original tool invocation context changed", substitutedReplay.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(actualTask.Revision, Assert.Single((await tasks.ListOwnedTasksAsync(new(), token)).Items).Value.Revision);
            // Caller metadata and a copied store/actor pair cannot manufacture the owner private selection.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => caller.ReviewAsync(
                new ForgedSelection(selection.StoreId, actor), proposal with { Name = "Forged replacement" },
                1, AutomationDefinitionChangeKind.Update, token));
            Assert.Equal("Actual reviewed draft", (await repository.GetOwnedAsync(proposal.Id, token))!.Value.Name);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => caller.CaptureAsync(
                actor with { AuthenticationRevision = "not-the-original-authentication-revision" }, token));
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
    private sealed class HoldingRepository(IAutomationOwnerRepository actual) : IAutomationOwnerRepository
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken ct) => actual.GetStoreIdentityAsync(ct);
        public Task<AutomationOwnerRead<AutomationDefinition>?> GetOwnedAsync(Guid id, CancellationToken ct) => actual.GetOwnedAsync(id, ct);
        public Task<AutomationLibraryPage<AutomationDefinition>> ListOwnedAsync(AutomationLibraryQuery query, CancellationToken ct) => actual.ListOwnedAsync(query, ct);
        public Task<AutomationCommitReceiptRead> ObserveCommitAsync(Guid id, Guid operation, string hash, long revision, CancellationToken ct) => actual.ObserveCommitAsync(id, operation, hash, revision, ct);
        public async Task<AutomationDefinitionCommitResult> CompareExchangeOwnedAsync(AutomationDefinitionChange change, IAutomationDefinitionCommitAdmission admission, CancellationToken ct)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); return await actual.CompareExchangeOwnedAsync(change, admission, ct); }
    }
    private sealed class MutatingSafety(IConversationSafetyService actual, Action mutation) : IConversationSafetyService
    {
        public Task<ConversationSafetySnapshot> GetSnapshotAsync(Guid id, CancellationToken ct) => actual.GetSnapshotAsync(id, ct);
        public Task<ConversationSafetyFlagResult> RecordConfirmedFlagAsync(Guid id, ConfirmedSafetyFlag flag, CancellationToken ct) => actual.RecordConfirmedFlagAsync(id, flag, ct);
        public async Task EnsureMayActAsync(Guid id, string operation, CancellationToken ct)
        { await actual.EnsureMayActAsync(id, operation, ct); mutation(); }
    }
    private sealed record ForgedSelection(Guid StoreId, AuthenticatedResourceActor Actor) : IAutomationDefinitionCallerSelection;
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "app.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
