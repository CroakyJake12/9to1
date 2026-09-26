using System.Runtime.CompilerServices;
using System.Text.Json;
using CakeOS.Cui.Language;
using NineToOne.Cui.AI;
using Xunit;

namespace NineToOne.Cui.AI.Tests;

public sealed class AppAiCoordinatorTests
{
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

    [Fact]
    public void SharedBarKeepsPersistentModeControlAndSemanticModelPickerInCui()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "AI", "UI", "FloatingAiBar.cui"));
        var document = new CuiRichParser().ParseFile(path);
        var component = Assert.Single(document.Components, child => child.Type == "Component");
        var source = File.ReadAllText(path);
        Assert.Equal("Component", component.Type);
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
        public List<AppAiActionGraphEvent> Events { get; } = [];
        public ValueTask PublishAsync(AppAiActionGraphEvent value, CancellationToken cancellationToken)
        {
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
