using System.Text.Json;
using NineToOne.Cui.AI;
using Xunit;

namespace NineToOne.Cui.AI.Tests;

public sealed class AppAiOwnerApprovalTests
{
    private static readonly JsonElement Arguments = JsonSerializer.SerializeToElement(new { name = "Draft" });
    private static readonly AppAiActionDescriptor Descriptor = new("layout.edit", "Edit layout", "Exact owning operation",
        AppAiActionRisk.ReversibleChange, true, "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}},\"required\":[\"name\"],\"additionalProperties\":false}")
        { ApprovalFlow = AppAiApprovalFlow.OwningResourceBroker };
    private static AppAiActionRequest Request => new("launcher", "layout.edit", Arguments, "copied-generic-token", "request", AppAiAccessMode.Write, "1");

    [Fact]
    public async Task Explicit_owner_delegation_preserves_review_metadata_strips_token_and_waits_for_owner_result()
    {
        var owner = new Owner(); var graph = new Graph(); var approvals = new ForbiddenGenericApproval();
        var coordinator = new AppAiCoordinator(new Context(), owner, approvals, new UnusedDulche(), approvals, actionGraph: graph);
        var pending = await coordinator.ExecuteAsync(Request);
        Assert.Equal("approval-pending", pending.ErrorCode); Assert.False(pending.Succeeded);
        Assert.Null(owner.Last!.ApprovalToken); Assert.True(owner.Actions.Single().RequiresReview);
        Assert.True(owner.Actions.Single().RequiresPermission);
        Assert.Contains(graph.Events, item => item.Status == AppAiActionGraphStatus.WaitingForApproval);
        Assert.DoesNotContain(graph.Events, item => item.Status == AppAiActionGraphStatus.Completed);
        owner.Approved = true;
        Assert.True((await coordinator.ExecuteAsync(Request)).Succeeded);
        Assert.Equal(2, owner.Calls);
        Assert.Equal(0, approvals.Calls);
        Assert.Equal(AppAiActionGraphStatus.Completed, graph.Events.Last().Status);
    }

    [Fact]
    public async Task Descriptor_flag_cannot_enable_owner_delegation_without_real_adapter_or_truthful_review_flags()
    {
        var approvals = new ForbiddenGenericApproval(); var graph = new Graph();
        var plain = new PlainActions();
        var coordinator = new AppAiCoordinator(new Context(), plain, approvals, new UnusedDulche(), approvals, actionGraph: graph);
        Assert.Equal("owner-approval-unavailable", (await coordinator.ExecuteAsync(Request)).ErrorCode);
        var owner = new Owner { Definition = Descriptor with { RequiresReview = false } };
        coordinator = new(new Context(), owner, approvals, new UnusedDulche(), approvals, actionGraph: graph);
        Assert.Equal("owner-approval-unavailable", (await coordinator.ExecuteAsync(Request)).ErrorCode);
        Assert.Equal(0, owner.Calls); Assert.Equal(0, approvals.Calls);
    }

    [Fact]
    public async Task Owner_path_still_rejects_readonly_stale_and_changed_context_before_dispatch()
    {
        var context = new Context(); var owner = new Owner(); var approvals = new ForbiddenGenericApproval();
        var graph = new Graph { BeforePublish = () => context.Revision = "2" };
        var coordinator = new AppAiCoordinator(context, owner, approvals, new UnusedDulche(), approvals, actionGraph: graph);
        Assert.Equal("read-only-mode", (await coordinator.ExecuteAsync(Request with { AccessMode = AppAiAccessMode.ReadOnly })).ErrorCode);
        Assert.Equal("stale-context", (await coordinator.ExecuteAsync(Request with { ExpectedRevision = "other" })).ErrorCode);
        Assert.Equal("stale-context", (await coordinator.ExecuteAsync(Request)).ErrorCode);
        Assert.Equal(0, owner.Calls); Assert.Equal(0, approvals.Calls);
    }

    [Fact]
    public async Task Shared_bar_retries_exact_host_action_without_model_regeneration_and_honours_current_readonly_mode()
    {
        var owner = new Owner(); var approvals = new ForbiddenGenericApproval();
        using var bar = new FloatingAiBarState(new(new Context(), owner, approvals, new UnusedDulche(), approvals, actionGraph: new Graph()));
        Assert.Equal("read-only-mode", (await bar.ExecuteActionAsync(Request)).ErrorCode);
        bar.SetWriteMode();
        Assert.Equal("approval-pending", (await bar.ExecuteActionAsync(Request)).ErrorCode);
        Assert.Equal(AppAiRequestState.WaitingForApproval, bar.RequestState); Assert.Equal(FloatingAiBarMode.Review, bar.Mode);
        bar.SetReadOnly(); owner.Approved = true;
        Assert.Equal("read-only-mode", (await bar.ExecuteActionAsync(Request)).ErrorCode); Assert.Equal(1, owner.Calls);
        bar.SetWriteMode(); Assert.True((await bar.ExecuteActionAsync(Request)).Succeeded);
        Assert.Equal(2, owner.Calls); Assert.Null(owner.Last!.ApprovalToken);
        Assert.Equal(Arguments.GetRawText(), owner.Last.Arguments.GetRawText());
        Assert.Equal(AppAiRequestState.Completed, bar.RequestState); Assert.Equal(FloatingAiBarMode.Ready, bar.Mode);
    }

    private sealed class Context : IAppAiContext
    {
        public string Revision = "1";
        public ValueTask<AppAiContextSnapshot> CaptureAsync(CancellationToken ct) => ValueTask.FromResult(new AppAiContextSnapshot(
            "launcher", "home", "layout", "Current authorized context", null, new Dictionary<string, JsonElement>(),
            AppAiDataSensitivity.UserContent, DateTimeOffset.UtcNow, Revision));
    }
    private sealed class Owner : IAppAiResourceBrokerActions
    {
        public AppAiActionDescriptor Definition = Descriptor; public bool Approved; public int Calls; public AppAiActionRequest? Last;
        public IReadOnlyList<AppAiActionDescriptor> Actions => [Definition];
        public ValueTask<AppAiActionResult> ExecuteAsync(AppAiActionRequest request, CancellationToken ct) => throw new InvalidOperationException("Must use explicit owning approval path.");
        public ValueTask<AppAiActionResult> ExecuteWithOwnedApprovalAsync(AppAiActionRequest request, CancellationToken ct)
        {
            Calls++; Last = request;
            return ValueTask.FromResult(Approved ? AppAiActionResult.Success("Owning result") :
                AppAiActionResult.Rejected("Owning Home review is pending.", "approval-pending", true));
        }
    }
    private sealed class PlainActions : IAppAiActions
    {
        public IReadOnlyList<AppAiActionDescriptor> Actions => [Descriptor];
        public ValueTask<AppAiActionResult> ExecuteAsync(AppAiActionRequest request, CancellationToken ct) => throw new InvalidOperationException("Descriptor is not an adapter.");
    }
    private sealed class ForbiddenGenericApproval : IAppAiApprovalVerifier, IAppAiApprovalRequester
    {
        public int Calls;
        public ValueTask<bool> VerifyAsync(string app, string action, string token, CancellationToken ct) { Calls++; throw new InvalidOperationException("Duplicate approval."); }
        public ValueTask<AppAiApprovalDecision> RequestAsync(AppAiApprovalRequest request, CancellationToken ct) { Calls++; throw new InvalidOperationException("Duplicate approval."); }
        public ValueTask CompleteAsync(AppAiActionRequest request, AppAiActionResult result, CancellationToken ct) { Calls++; throw new InvalidOperationException("Duplicate audit."); }
    }
    private sealed class Graph : IAppAiActionGraph
    {
        public List<AppAiActionGraphEvent> Events = []; public Action? BeforePublish;
        public ValueTask PublishAsync(AppAiActionGraphEvent value, CancellationToken ct) { BeforePublish?.Invoke(); Events.Add(value); return ValueTask.CompletedTask; }
    }
    private sealed class UnusedDulche : IDulcheAppClient
    {
        public IAsyncEnumerable<AppAiResponseChunk> StreamAsync(AppAiPrompt prompt, CancellationToken ct) => throw new NotSupportedException();
    }
}
