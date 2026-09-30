using System.Runtime.CompilerServices;
using System.Text.Json;
using NineToOne.Cui.AI;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class CanvasAiSurfaceStateTests
{
    [Fact]
    public async Task Switching_tools_hides_surface_without_cancelling_actual_shared_stream_and_restores_same_controller()
    {
        var client = new ControlledClient();
        using var bar = new FloatingAiBarState(new(new Context(), new NoMutationActions(), new DenyApproval(), client));
        using var surface = new CanvasAiSurfaceState(bar);
        surface.SelectAiTool();
        bar.Prompt = "Describe the authorized drawing";
        var request = bar.SubmitAsync();
        await client.Started.Task;
        surface.SelectOtherTool();
        Assert.Equal(CanvasAiSurfaceVisibility.Hidden, surface.Snapshot.Visibility);
        Assert.Equal(CanvasAiSessionIndicator.Active, surface.Snapshot.Indicator);
        Assert.Equal(AppAiRequestState.Generating, bar.RequestState);
        Assert.False(client.WasCancelled);
        surface.SelectAiTool();
        Assert.Same(bar, surface.SharedSession);
        Assert.Equal(CanvasAiSurfaceVisibility.Expanded, surface.Snapshot.Visibility);
        Assert.Equal(1, client.Requests);
        surface.SelectOtherTool();
        client.Complete.SetResult();
        await request;
        Assert.Equal(CanvasAiSurfaceVisibility.Hidden, surface.Snapshot.Visibility);
        Assert.Equal(CanvasAiSessionIndicator.CompletedAttention, surface.Snapshot.Indicator);
        Assert.Equal("Authorized description", bar.Response);
        surface.DismissAttention();
        bar.Prompt = "Next request draft";
        Assert.Equal(CanvasAiSessionIndicator.None, surface.Snapshot.Indicator);
        surface.SelectAiTool();
        Assert.Equal(CanvasAiSessionIndicator.None, surface.Snapshot.Indicator);
        Assert.Equal(1, client.Requests);
    }

    [Fact]
    public async Task Surface_disposal_unsubscribes_without_cancelling_shared_request_and_explicit_cancel_still_stops_work()
    {
        var client = new ControlledClient();
        using var bar = new FloatingAiBarState(new(new Context(), new NoMutationActions(), new DenyApproval(), client));
        var surface = new CanvasAiSurfaceState(bar);
        surface.SelectAiTool();
        bar.Prompt = "Inspect";
        var request = bar.SubmitAsync();
        await client.Started.Task;
        surface.Dispose();
        Assert.Equal(AppAiRequestState.Generating, bar.RequestState);
        Assert.False(client.WasCancelled);
        using var replacement = new CanvasAiSurfaceState(bar);
        replacement.SelectAiTool();
        Assert.Same(surface.SharedSession, replacement.SharedSession);
        replacement.CancelSession();
        await request;
        Assert.True(client.WasCancelled);
        Assert.Equal(AppAiRequestState.Cancelled, bar.RequestState);
    }

    private sealed class ControlledClient : IDulcheAppClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WasCancelled { get; private set; }
        public int Requests { get; private set; }
        public async IAsyncEnumerable<AppAiResponseChunk> StreamAsync(AppAiPrompt prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Requests++;
            Started.TrySetResult();
            try { await Complete.Task.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { WasCancelled = true; throw; }
            yield return new("Authorized description", true);
        }
    }
    private sealed class Context : IAppAiContext
    {
        public ValueTask<AppAiContextSnapshot> CaptureAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new AppAiContextSnapshot(
            "canvas", "Canvas.Workspace", Guid.NewGuid().ToString(), "Authorized fixture", null,
            new Dictionary<string, JsonElement>(), AppAiDataSensitivity.UserContent, DateTimeOffset.UtcNow));
    }
    private sealed class NoMutationActions : IAppAiActions
    {
        public IReadOnlyList<AppAiActionDescriptor> Actions => [];
        public ValueTask<AppAiActionResult> ExecuteAsync(AppAiActionRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException("Read-only fixture must not mutate");
    }
    private sealed class DenyApproval : IAppAiApprovalVerifier
    {
        public ValueTask<bool> VerifyAsync(string appId, string actionId, string approvalToken, CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }
}
