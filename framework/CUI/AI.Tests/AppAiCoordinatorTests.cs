using System.Runtime.CompilerServices;
using System.Text.Json;
using CakeOS.Cui.Language;
using NineToOne.Cui.AI;
using Xunit;

namespace NineToOne.Cui.AI.Tests;

public sealed class AppAiCoordinatorTests
{
    [Fact]
    public async Task Compact_selection_is_bar_scoped_never_persists_and_rechecks_availability_before_dispatch()
    {
        var picker = new SelectionPicker();
        var dulche = new FakeDulche();
        var first = new AppAiCoordinator(new FakeContext(), new FakeActions(), new FakeApprovals(), dulche, modelPicker: picker);
        var second = new AppAiCoordinator(new FakeContext(), new FakeActions(), new FakeApprovals(), new FakeDulche(), modelPicker: picker);
        Assert.True(await first.SelectModelAsync("local:chosen"));
        Assert.Equal("local:chosen", (await first.GetModelSelectionAsync())!.ModelId);
        Assert.Equal("local:default", (await second.GetModelSelectionAsync())!.ModelId);
        Assert.Equal(0, picker.PersistentWrites);
        await DrainAsync(first.StreamAsync("Summarise", "scoped-selection"));
        Assert.Equal("local:chosen", dulche.LastPrompt!.ModelSelection!.ModelId);
        picker.Available = false;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await DrainAsync(first.StreamAsync("Summarise", "revoked-selection")));
        Assert.Equal(0, picker.PersistentWrites);
    }

    [Fact]
    public async Task Confirmed_owner_result_retains_audit_only_finish_without_reexecuting()
    {
        var actions = new FakeActions();
        var approvals = new PendingAuditApprovals();
        var coordinator = new AppAiCoordinator(new FakeContext(), actions, approvals, new FakeDulche(),
            new FakeApprovalRequester(), actionGraph: new FakeActionGraph());
        using var bar = new FloatingAiBarState(coordinator);
        bar.SetWriteMode();
        var result = await bar.ExecuteActionAsync(new("write", "insert-paragraph", Json("{}"), null, "audit-case", AppAiAccessMode.Write));
        Assert.True(result.Succeeded); Assert.NotNull(result.AuditRecovery); Assert.True(bar.HasPendingActionAudit);
        Assert.DoesNotContain("AuditRecovery", JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
        bar.SetReadOnly();
        Assert.True((await bar.FinishActionAuditAsync()).AuditRecorded);
        Assert.False(bar.HasPendingActionAudit); Assert.Equal(1, actions.ExecutionCount);
        Assert.Equal(1, approvals.Verifications); Assert.Equal(1, approvals.Finishes);
    }
    [Fact]
    public async Task Model_requested_action_retains_only_coordinator_issued_audit_observation()
    {
        var actions = new FakeActions(); var approvals = new PendingAuditApprovals();
        var model = new FakeDulche { Chunks = [new AppAiResponseChunk("", RequestedAction: new("insert-paragraph", Json("{}")))] };
        using var bar = new FloatingAiBarState(new AppAiCoordinator(new FakeContext(), actions, approvals, model,
            new FakeApprovalRequester(), actionGraph: new FakeActionGraph()));
        bar.SetWriteMode(); bar.Prompt = "Controlled model protocol action";
        await bar.SubmitAsync();
        Assert.True(bar.HasPendingActionAudit);
        Assert.True((await bar.FinishActionAuditAsync()).AuditRecorded);
        Assert.Equal(1, actions.ExecutionCount); Assert.Equal(1, approvals.Verifications);
    }

    [Fact]
    public async Task Legacy_audit_transport_failure_preserves_owner_success_without_inventing_handle()
    {
        var actions = new FakeActions();
        using var bar = new FloatingAiBarState(new AppAiCoordinator(new FakeContext(), actions,
            new ThrowingCompletionApprovals(), new FakeDulche(), new FakeApprovalRequester(), actionGraph: new FakeActionGraph()));
        bar.SetWriteMode();
        var result = await bar.ExecuteActionAsync(new("write", "insert-paragraph", Json("{}"), null, "legacy-audit", AppAiAccessMode.Write));
        Assert.True(result.Succeeded); Assert.True(result.CompletionAuditPending); Assert.Null(result.AuditRecovery);
        Assert.True(bar.HasUnconfirmedActionAudit); Assert.False(bar.HasPendingActionAudit);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await bar.FinishActionAuditAsync());
        Assert.Equal(1, actions.ExecutionCount);
    }
    [Fact]
    public async Task Post_owner_graph_failure_preserves_observed_success_and_audit_handle()
    {
        var actions = new FakeActions(); var approvals = new PendingAuditApprovals();
        var coordinator = new AppAiCoordinator(new FakeContext(), actions, approvals, new FakeDulche(),
            new FakeApprovalRequester(), actionGraph: new FakeActionGraph { FailCompleted = true });
        var result = await coordinator.ExecuteAsync(new("write", "insert-paragraph", Json("{}"), null, "graph-audit", AppAiAccessMode.Write));
        Assert.True(result.Succeeded); Assert.True(result.ActionGraphPending); Assert.True(result.CompletionAuditPending);
        Assert.NotNull(result.AuditRecovery); Assert.True((await result.AuditRecovery!.FinishAsync()).AuditRecorded);
        Assert.Equal(1, actions.ExecutionCount); Assert.Equal(1, approvals.Verifications);
    }
    [Fact]
    public async Task Boolean_only_legacy_verifier_never_claims_durable_completion()
    {
        var actions = new FakeActions();
        using var bar = new FloatingAiBarState(new AppAiCoordinator(new FakeContext(), actions,
            new FakeApprovals(), new FakeDulche(), new FakeApprovalRequester(), actionGraph: new FakeActionGraph()));
        bar.SetWriteMode();
        var result = await bar.ExecuteActionAsync(new("write", "insert-paragraph", Json("{}"), null, "boolean-audit", AppAiAccessMode.Write));
        Assert.True(result.Succeeded); Assert.True(result.CompletionAuditPending); Assert.Null(result.AuditRecovery);
        Assert.True(bar.HasUnconfirmedActionAudit); Assert.False(bar.HasPendingActionAudit);
        IAppAiApprovalVerifier legacy = new FakeApprovals();
        Assert.False((await legacy.CompleteRejectedVerificationAsync(
            new("write", "insert-paragraph", Json("{}"), "approved", "boolean-rejected", AppAiAccessMode.Write), default)).AuditRecorded);
        Assert.Equal(1, actions.ExecutionCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delayed_finish_acknowledges_audit_without_overwriting_newer_or_disposed_view(bool dispose)
    {
        var actions = new FakeActions(); var approvals = new DelayedAuditApprovals();
        using var bar = new FloatingAiBarState(new AppAiCoordinator(new FakeContext(), actions, approvals,
            new FakeDulche(), new FakeApprovalRequester(), actionGraph: new FakeActionGraph()));
        bar.SetWriteMode();
        await bar.ExecuteActionAsync(new("write", "insert-paragraph", Json("{}"), null, "delayed-audit", AppAiAccessMode.Write));
        var finish = bar.FinishActionAuditAsync().AsTask();
        await approvals.Entered.Task;
        if (dispose) bar.Dispose();
        else { bar.SetReadOnly(); bar.Prompt = "A newer controlled request"; await bar.SubmitAsync(); }
        var response = bar.Response; var error = bar.Error; var requestState = bar.RequestState;
        var changed = 0; bar.Changed += (_, _) => changed++;
        approvals.Completion.SetResult(new AppAiCompletionObservation(true));
        Assert.True((await finish).AuditRecorded); Assert.False(bar.HasPendingActionAudit);
        Assert.Equal(response, bar.Response); Assert.Equal(error, bar.Error); Assert.Equal(requestState, bar.RequestState);
        Assert.Equal(0, changed); Assert.Equal(1, actions.ExecutionCount);
    }

    [Fact]
    public async Task Finish_of_older_audit_never_replaces_an_already_newer_response()
    {
        var actions = new FakeActions(); var approvals = new PendingAuditApprovals();
        using var bar = new FloatingAiBarState(new AppAiCoordinator(new FakeContext(), actions, approvals,
            new FakeDulche(), new FakeApprovalRequester(), actionGraph: new FakeActionGraph()));
        bar.SetWriteMode();
        await bar.ExecuteActionAsync(new("write", "insert-paragraph", Json("{}"), null, "older-audit", AppAiAccessMode.Write));
        bar.SetReadOnly(); bar.Prompt = "A newer controlled request"; await bar.SubmitAsync();
        var response = bar.Response; var error = bar.Error;
        Assert.True((await bar.FinishActionAuditAsync()).AuditRecorded);
        Assert.Equal(response, bar.Response); Assert.Equal(error, bar.Error);
        Assert.False(bar.HasPendingActionAudit); Assert.Equal(1, actions.ExecutionCount);
    }

    private sealed class DelayedAuditApprovals : IAppAiApprovalVerifier, IAppAiAuditRecovery
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<AppAiCompletionObservation> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<bool> VerifyAsync(string appId, string actionId, string approvalToken, CancellationToken ct) => ValueTask.FromResult(true);
        public ValueTask<AppAiCompletionObservation> CompleteWithRecoveryAsync(AppAiActionRequest request, AppAiActionResult result, CancellationToken ct)
            => ValueTask.FromResult(new AppAiCompletionObservation(false, this));
        public async ValueTask<AppAiCompletionObservation> FinishAsync(CancellationToken ct = default)
        { Entered.TrySetResult(true); return await Completion.Task.WaitAsync(ct); }
    }

    private sealed class ThrowingCompletionApprovals : IAppAiApprovalVerifier
    {
        public ValueTask<bool> VerifyAsync(string appId, string actionId, string approvalToken, CancellationToken ct) => ValueTask.FromResult(true);
        public ValueTask CompleteAsync(AppAiActionRequest request, AppAiActionResult result, CancellationToken ct) =>
            ValueTask.FromException(new IOException("Controlled legacy audit transport failure"));
    }

    private sealed class PendingAuditApprovals : IAppAiApprovalVerifier, IAppAiAuditRecovery
    {
        public int Verifications; public int Finishes;
        public ValueTask<bool> VerifyAsync(string appId, string actionId, string approvalToken, CancellationToken ct)
        { Verifications++; return ValueTask.FromResult(true); }
        public ValueTask<AppAiCompletionObservation> CompleteWithRecoveryAsync(AppAiActionRequest request, AppAiActionResult result, CancellationToken ct)
            => ValueTask.FromResult(new AppAiCompletionObservation(false, this));
        public ValueTask<AppAiCompletionObservation> FinishAsync(CancellationToken ct = default)
        { Finishes++; return ValueTask.FromResult(new AppAiCompletionObservation(true)); }
    }

    private sealed class SelectionPicker : IAppAiModelPicker
    {
        public bool Available = true;
        public int PersistentWrites;
        public ValueTask<IReadOnlyList<AppAiModelOption>> GetModelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<AppAiModelOption>>([new("local:chosen", "Chosen", "local", true, Available)]);
        public ValueTask<AppAiModelSelection?> GetSelectionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<AppAiModelSelection?>(new("local:default", "Medium"));
        public ValueTask<bool> SelectAsync(string modelId, CancellationToken cancellationToken)
        { PersistentWrites++; return ValueTask.FromResult(true); }
    }

    [Fact]
    public async Task ReadOnlyModeIsDefaultAndCannotInvokeTypedMutations()
    {
        var actions = new FakeActions();
        var dulche = new FakeDulche
        {
            Chunks = [new AppAiResponseChunk(string.Empty, RequestedAction: new("insert-paragraph", Json("{\"name\":\"Draft\"}")))]
        };
        var coordinator = new AppAiCoordinator(new FakeContext(), actions, new FakeApprovals(), dulche);
        using var state = new FloatingAiBarState(coordinator);

        Assert.Equal(AppAiAccessMode.ReadOnly, state.AccessMode);
        Assert.True(state.IsReadOnly);
        Assert.False(state.IsWriteMode);

        var chunks = await DrainAsync(coordinator.StreamAsync("Add a paragraph", "request-1"));

        Assert.Equal(0, actions.ExecutionCount);
        Assert.Empty(dulche.LastPrompt!.AvailableActions!);
        Assert.Contains("do not request or perform app actions", dulche.LastPrompt.SystemInstructions);
        Assert.Contains(chunks, chunk => chunk.Text.Contains("Action not run: Read-only mode", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReviewedMutationUsesHomeApprovalTokenAndActionGraph()
    {
        var actions = new FakeActions();
        var approvals = new FakeApprovalRequester();
        var graph = new FakeActionGraph();
        var coordinator = new AppAiCoordinator(
            new FakeContext(), actions, new FakeApprovals(), new FakeDulche(), approvals,
            actionGraph: graph);
        using var arguments = JsonDocument.Parse("{\"name\":\"Draft\"}");

        var rejected = await coordinator.ExecuteAsync(new(
            "write", "insert-paragraph", arguments.RootElement.Clone(), null, "one"));
        var accepted = await coordinator.ExecuteAsync(new(
            "write", "insert-paragraph", arguments.RootElement.Clone(), "model-cannot-author-this", "two",
            AppAiAccessMode.Write, "rev-1"));

        Assert.Equal("read-only-mode", rejected.ErrorCode);
        Assert.True(accepted.Succeeded);
        Assert.Equal(1, approvals.RequestCount);
        Assert.Equal("approved", actions.LastRequest?.ApprovalToken);
        Assert.Equal(1, actions.ExecutionCount);
        Assert.Contains(graph.Events, item => item.Status == AppAiActionGraphStatus.WaitingForApproval);
        Assert.Contains(graph.Events, item => item.Status == AppAiActionGraphStatus.Completed);
        Assert.All(graph.Events, item => Assert.DoesNotContain("Draft", item.Summary, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingHomeApprovalFailsClosed()
    {
        var actions = new FakeActions();
        var graph = new FakeActionGraph();
        var coordinator = new AppAiCoordinator(
            new FakeContext(), actions, new FakeApprovals(), new FakeDulche(), actionGraph: graph);
        var result = await coordinator.ExecuteAsync(new(
            "write", "insert-paragraph", Json("{\"name\":\"Draft\"}"), null, "no-home",
            AppAiAccessMode.Write));

        Assert.Equal("approval-unavailable", result.ErrorCode);
        Assert.Equal(0, actions.ExecutionCount);
    }

    [Fact]
    public async Task ActionCannotCrossApplicationBoundary()
    {
        var actions = new FakeActions();
        var coordinator = new AppAiCoordinator(new FakeContext(), actions, new FakeApprovals(), new FakeDulche());
        var result = await coordinator.ExecuteAsync(new(
            "boards", "insert-paragraph", Json("{}"), null, "one", AppAiAccessMode.Write));

        Assert.Equal("app-mismatch", result.ErrorCode);
        Assert.Equal(0, actions.ExecutionCount);
    }

    [Fact]
    public async Task StaleRevisionAfterApprovalDoesNotRunAction()
    {
        var actions = new FakeActions();
        var context = new FakeContext { Revisions = ["rev-1", "rev-2"] };
        var coordinator = new AppAiCoordinator(context, actions, new FakeApprovals(), new FakeDulche(),
            new FakeApprovalRequester(), actionGraph: new FakeActionGraph());
        var result = await coordinator.ExecuteAsync(new(
            "write", "insert-paragraph", Json("{\"name\":\"Draft\"}"), null, "stale",
            AppAiAccessMode.Write, "rev-1"));

        Assert.Equal("stale-context", result.ErrorCode);
        Assert.Equal(0, actions.ExecutionCount);
    }

    [Fact]
    public async Task DataMutationAlwaysRequiresPerActionApprovalAndBackupForLiveDatabase()
    {
        var approvals = new FakeApprovalRequester();
        var order = new List<string>();
        var guard = new FakeDatabaseGuard { Events = order };
        var actions = new FakeActions { AppId = "data", RequireReview = false, RequirePermission = false, Events = order };
        var context = new FakeContext { AppId = "data", IsLiveDatabase = true };
        var coordinator = new AppAiCoordinator(
            context, actions, new FakeApprovals(), new FakeDulche(), approvals, guard,
            new FakeActionGraph());

        var result = await coordinator.ExecuteAsync(new(
            "data", "insert-paragraph", Json("{\"name\":\"Draft\"}"), null, "data-1",
            AppAiAccessMode.Write, "rev-1"));

        Assert.True(result.Succeeded);
        Assert.Equal(1, guard.PrepareCount);
        Assert.Equal(1, guard.VerifyCount);
        Assert.True(approvals.LastRequest!.ForcePerActionApproval);
        Assert.Equal("preview diff", approvals.LastRequest.ChangePreview);
        Assert.Equal("backup-1", approvals.LastRequest.BackupId);
        Assert.True(guard.Events.IndexOf("prepare") < guard.Events.IndexOf("execute"));
        Assert.True(guard.Events.IndexOf("execute") < guard.Events.IndexOf("verify"));
    }

    [Fact]
    public async Task LiveDatabaseMutationDoesNotRunWithoutRecoverableBackup()
    {
        var actions = new FakeActions { AppId = "data" };
        var guard = new FakeDatabaseGuard
        {
            Preparation = new AppAiDatabasePreparation(false, string.Empty, null, "backup-failed", "Backup unavailable")
        };
        var coordinator = new AppAiCoordinator(
            new FakeContext { AppId = "data", IsLiveDatabase = true }, actions,
            new FakeApprovals(), new FakeDulche(), new FakeApprovalRequester(), guard,
            new FakeActionGraph());

        var result = await coordinator.ExecuteAsync(new(
            "data", "insert-paragraph", Json("{\"name\":\"Draft\"}"), null, "data-2",
            AppAiAccessMode.Write, "rev-1"));

        Assert.Equal("backup-failed", result.ErrorCode);
        Assert.Equal(0, actions.ExecutionCount);
    }

    [Fact]
    public async Task FloatingBarStreamsAndCanBeReused()
    {
        var dulche = new FakeDulche();
        var coordinator = new AppAiCoordinator(new FakeContext(), new FakeActions(), new FakeApprovals(), dulche);
        using var state = new FloatingAiBarState(coordinator) { Prompt = "Summarise" };

        state.SetWriteMode();
        Assert.Equal(AppAiAccessMode.Write, state.AccessMode);
        Assert.True(state.IsWriteMode);
        state.SetReadOnly();
        Assert.True(state.IsReadOnly);
        state.Expand();
        await state.SubmitAsync();

        Assert.Equal(FloatingAiBarMode.Ready, state.Mode);
        Assert.Equal(AppAiRequestState.Completed, state.RequestState);
        Assert.Equal("Done", state.Response);
        Assert.Equal(AppAiAccessMode.ReadOnly, dulche.LastPrompt!.AccessMode);
        Assert.Equal("Use current model", state.SelectedModelLabel);
    }

    private static string SharedBarAuthoredSourcePath(
        [CallerFilePath] string sourceFile = "")
    {
        var projectDirectory = Path.GetDirectoryName(sourceFile)
            ?? throw new InvalidOperationException("The actual test source directory is unavailable.");
        if (!File.Exists(Path.Combine(projectDirectory, "NineToOne.Cui.AI.Tests.csproj")))
            throw new InvalidOperationException("The actual owning AI test project is unavailable.");
        return Path.GetFullPath(Path.Combine(projectDirectory,
            "..", "AI", "UI", "FloatingAiBar.cui"));
    }

    [Fact]
    public void SharedBarKeepsPersistentModeControlAndSemanticModelPickerInCui()
    {
        var path = SharedBarAuthoredSourcePath();
        var document = new CuiRichParser().ParseFile(path);
        var component = Assert.Single(document.Components);
        var source = File.ReadAllText(path);
        Assert.Equal("Border", component.Type);
        Assert.Equal("floating-ai-surface", component.Name);
        Assert.Contains("id=\"ReadOnlyMode\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"WriteMode\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"AiModel\"", source, StringComparison.Ordinal);
    }

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private static async Task<List<AppAiResponseChunk>> DrainAsync(IAsyncEnumerable<AppAiResponseChunk> stream)
    {
        var chunks = new List<AppAiResponseChunk>();
        await foreach (var chunk in stream) chunks.Add(chunk);
        return chunks;
    }

    private sealed class FakeContext : IAppAiContext
    {
        private int _captureCount;
        public string AppId { get; init; } = "write";
        public bool IsLiveDatabase { get; init; }
        public IReadOnlyList<string>? Revisions { get; init; }

        public ValueTask<AppAiContextSnapshot> CaptureAsync(CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _captureCount) - 1;
            var revision = Revisions is { Count: > 0 } ? Revisions[Math.Min(index, Revisions.Count - 1)] : "rev-1";
            return ValueTask.FromResult(new AppAiContextSnapshot(
                AppId, "document", "doc-1", "Current document", null,
                new Dictionary<string, JsonElement>(), AppAiDataSensitivity.UserContent,
                DateTimeOffset.UtcNow, revision, [], "Edit", IsLiveDatabase));
        }
    }

    private sealed class FakeActions : IAppAiActions
    {
        public int ExecutionCount { get; private set; }
        public string AppId { get; init; } = "write";
        public bool RequireReview { get; init; } = true;
        public bool RequirePermission { get; init; } = true;
        public AppAiActionRequest? LastRequest { get; private set; }
        public AppAiActionResult? LastResult { get; private set; }
        public List<string>? Events { get; init; }

        public IReadOnlyList<AppAiActionDescriptor> Actions =>
        [
            new("insert-paragraph", "Insert paragraph", "Adds reviewed text.",
                AppAiActionRisk.ReversibleChange, RequireReview, "{\"type\":\"object\"}",
                RequiresPermission: RequirePermission, IsMutation: true,
                AffectedObjectIds: ["section-1"], ImpactUnknown: false, ImpactSummary: "Updates one section"),
        ];

        public ValueTask<AppAiActionResult> ExecuteAsync(AppAiActionRequest request, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            LastRequest = request;
            Events?.Add("execute");
            LastResult = AppAiActionResult.Success("Inserted");
            return ValueTask.FromResult(LastResult);
        }
    }

    private sealed class FakeApprovals : IAppAiApprovalVerifier
    {
        public ValueTask<bool> VerifyAsync(
            string appId,
            string actionId,
            string approvalToken,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(approvalToken == "approved");
    }

    private sealed class FakeApprovalRequester : IAppAiApprovalRequester
    {
        public int RequestCount { get; private set; }
        public AppAiApprovalRequest? LastRequest { get; private set; }
        public AppAiApprovalOutcome Outcome { get; init; } = AppAiApprovalOutcome.Approved;

        public ValueTask<AppAiApprovalDecision> RequestAsync(
            AppAiApprovalRequest request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequest = request;
            return ValueTask.FromResult(new AppAiApprovalDecision(Outcome,
                Outcome == AppAiApprovalOutcome.Approved ? "approved" : null,
                Outcome.ToString().ToLowerInvariant(), Outcome == AppAiApprovalOutcome.Approved ? "Approved" : "Not approved"));
        }
    }

    private sealed class FakeActionGraph : IAppAiActionGraph
    {
        public bool FailCompleted;
        public List<AppAiActionGraphEvent> Events { get; } = [];
        public ValueTask PublishAsync(AppAiActionGraphEvent value, CancellationToken cancellationToken)
        {
            if (FailCompleted && value.Status == AppAiActionGraphStatus.Completed) throw new IOException("Controlled graph acknowledgment failure");
            Events.Add(value);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeDatabaseGuard : IAppAiDatabaseMutationGuard
    {
        public int PrepareCount { get; private set; }
        public int VerifyCount { get; private set; }
        public List<string> Events { get; init; } = [];
        public AppAiDatabasePreparation Preparation { get; init; } = new(true, "preview diff", "backup-1");

        public ValueTask<AppAiDatabasePreparation> PrepareAsync(
            AppAiContextSnapshot context,
            AppAiActionDescriptor action,
            JsonElement arguments,
            CancellationToken cancellationToken)
        {
            PrepareCount++;
            Events.Add("prepare");
            return ValueTask.FromResult(Preparation);
        }

        public ValueTask<bool> VerifyAsync(
            AppAiContextSnapshot context,
            AppAiActionDescriptor action,
            JsonElement arguments,
            AppAiActionResult result,
            string backupId,
            CancellationToken cancellationToken)
        {
            VerifyCount++;
            Events.Add("verify");
            return ValueTask.FromResult(backupId == "backup-1");
        }
    }

    private sealed class FakeDulche : IDulcheAppClient
    {
        public IReadOnlyList<AppAiResponseChunk> Chunks { get; init; } =
        [new AppAiResponseChunk("Do"), new AppAiResponseChunk("ne", true)];
        public AppAiPrompt? LastPrompt { get; private set; }

        public async IAsyncEnumerable<AppAiResponseChunk> StreamAsync(
            AppAiPrompt prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastPrompt = prompt;
            await Task.Yield();
            foreach (var chunk in Chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunk;
            }
        }
    }
}
