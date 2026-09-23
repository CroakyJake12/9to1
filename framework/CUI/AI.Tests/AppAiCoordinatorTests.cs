using System.Runtime.CompilerServices;
using System.Text.Json;
using CakeOS.Cui.Language;
using NineToOne.Cui.AI;
using Xunit;

namespace NineToOne.Cui.AI.Tests;

public sealed class AppAiCoordinatorTests
{
    [Fact]
    public async Task ReviewedActionRequiresValidApproval()
    {
        var actions = new FakeActions();
        var coordinator = new AppAiCoordinator(new FakeContext(), actions, new FakeApprovals(), new FakeDulche());
        using var arguments = JsonDocument.Parse("{\"name\":\"Draft\"}");

        var rejected = await coordinator.ExecuteAsync(new(
            "write", "insert-paragraph", arguments.RootElement.Clone(), null, "one"));
        var accepted = await coordinator.ExecuteAsync(new(
            "write", "insert-paragraph", arguments.RootElement.Clone(), "approved", "two"));

        Assert.False(rejected.Succeeded);
        Assert.Equal("approval-required", rejected.ErrorCode);
        Assert.True(accepted.Succeeded);
        Assert.Equal(1, actions.ExecutionCount);
    }

    [Fact]
    public async Task ActionCannotCrossApplicationBoundary()
    {
        var actions = new FakeActions();
        var coordinator = new AppAiCoordinator(new FakeContext(), actions, new FakeApprovals(), new FakeDulche());
        using var arguments = JsonDocument.Parse("{}");

        var result = await coordinator.ExecuteAsync(new(
            "boards", "insert-paragraph", arguments.RootElement.Clone(), "approved", "one"));

        Assert.False(result.Succeeded);
        Assert.Equal("app-mismatch", result.ErrorCode);
        Assert.Equal(0, actions.ExecutionCount);
    }

    [Fact]
    public async Task FloatingBarStreamsAndCanBeReused()
    {
        var coordinator = new AppAiCoordinator(new FakeContext(), new FakeActions(), new FakeApprovals(), new FakeDulche());
        using var state = new FloatingAiBarState(coordinator) { Prompt = "Summarise" };

        state.Expand();
        await state.SubmitAsync();

        Assert.Equal(FloatingAiBarMode.Ready, state.Mode);
        Assert.Equal("Done", state.Response);
    }

    [Fact]
    public void SharedBarIsAuthoredAsCui()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "AI", "UI", "FloatingAiBar.cui"));

        var document = new CuiRichParser().ParseFile(path);

        Assert.Contains(document.Components, child => child.Type == "Component");
    }

    private sealed class FakeContext : IAppAiContext
    {
        public ValueTask<AppAiContextSnapshot> CaptureAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AppAiContextSnapshot(
                "write", "document", "doc-1", "Current document", null,
                new Dictionary<string, JsonElement>(), AppAiDataSensitivity.UserContent,
                DateTimeOffset.UtcNow));
    }

    private sealed class FakeActions : IAppAiActions
    {
        public int ExecutionCount { get; private set; }

        public IReadOnlyList<AppAiActionDescriptor> Actions { get; } =
        [
            new("insert-paragraph", "Insert paragraph", "Adds reviewed text.",
                AppAiActionRisk.ReversibleChange, true, "{\"type\":\"object\"}"),
        ];

        public ValueTask<AppAiActionResult> ExecuteAsync(AppAiActionRequest request, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return ValueTask.FromResult(new AppAiActionResult(true, "Inserted", null));
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

    private sealed class FakeDulche : IDulcheAppClient
    {
        public async IAsyncEnumerable<AppAiResponseChunk> StreamAsync(
            AppAiPrompt prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new AppAiResponseChunk("Do");
            yield return new AppAiResponseChunk("ne", true);
        }
    }
}
