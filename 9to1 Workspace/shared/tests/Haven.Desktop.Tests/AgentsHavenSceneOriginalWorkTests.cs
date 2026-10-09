using System.Runtime.CompilerServices;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Catalog;
using Haven.UI;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Actual scene/runtime metadata owners. No private canonical Agent source,
/// actor, approval response or provider execution is fabricated by these controls.</summary>
public sealed class AgentsHavenSceneOriginalWorkTests
{
    [Fact]
    public async Task Historical_suspended_binding_displays_same_ids_and_never_enables_or_dispatches_retry()
    {
        var run = Suspended();
        var repository = new ControlledRuns(run);
        var viewModel = ViewModel();
        var runtime = MetadataRuntime(repository);
        var scene = new AgentsHavenScene(viewModel, runtime);
        try
        {
            await scene.RefreshRunsAsync();
            Assert.Contains("Suspended", scene.ExecutionStatusText.Content, StringComparison.Ordinal);
            Assert.Contains(run.CanonicalTask!.TaskId.ToString("N"), scene.ExecutionStatusText.Content, StringComparison.Ordinal);
            Assert.Contains(run.CanonicalTask.ExecutionId.ToString("N"), scene.ExecutionStatusText.Content, StringComparison.Ordinal);
            Assert.Contains("saved revision 7", scene.ExecutionStatusText.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("Completed.", scene.ExecutionStatusText.Content, StringComparison.Ordinal);
            Assert.False(scene.RetryLatestButton.GetValue(HavenProperties.Enabled));
            Assert.False(runtime.HasOriginalUnstartedRetrySource(run.Id));
            Assert.Null(await scene.RetryLatestAsync());
            Assert.Contains("Retry unavailable", scene.ExecutionStatusText.Content, StringComparison.Ordinal);
            Assert.Equal(0, repository.GetCalls); // No actual service RetryAsync was dispatched.
            Assert.Equal(0, repository.Upserts);
            Assert.Equal(run, Assert.Single(repository.Values));
        }
        finally { await scene.CloseAndDrainAsync(); }
    }

    [Fact]
    public async Task Actual_held_history_reads_survive_scene_retirement_and_close_waits_before_card_cleanup()
    {
        var repository = new ControlledRuns(Suspended()) { Hold = true };
        var viewModel = ViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        var scene = new AgentsHavenScene(viewModel, MetadataRuntime(repository));
        var actual = scene.RefreshRunsAsync();
        try
        {
            Assert.Equal(2, repository.ReadCalls); // Constructor original plus explicit original.
            Assert.Single(scene.AgentCards.Items);
            var before = scene.ExecutionStatusText.Content;
            var close = scene.CloseAndDrainAsync();
            Assert.Same(close, scene.CloseAndDrainAsync());
            Assert.False(close.IsCompleted);
            Assert.False(actual.IsCompleted);
            Assert.Single(scene.AgentCards.Items);
            Assert.All(repository.ReadTokens, token => Assert.False(token.CanBeCanceled));
            Assert.False(repository.Read.Task.IsCanceled);
            repository.Read.SetResult(repository.Values);
            Assert.NotNull(await Record.ExceptionAsync(() => actual));
            Assert.True(actual.IsFaulted); // Real source withdrawal retained, not normalized to success.
            Assert.NotNull(await Record.ExceptionAsync(() => close));
            Assert.True(close.IsFaulted);
            Assert.True(repository.Read.Task.IsCompletedSuccessfully);
            Assert.Equal(before, scene.ExecutionStatusText.Content);
            Assert.Empty(scene.AgentCards.Items);
            Assert.Equal(0, repository.Upserts);
        }
        finally
        {
            repository.Read.TrySetResult(repository.Values);
            _ = await Record.ExceptionAsync(() => actual);
            _ = await Record.ExceptionAsync(() => scene.CloseAndDrainAsync());
        }
    }

    [Fact]
    public async Task Actual_raw_history_direct_siblings_remain_in_returned_scene_and_close_fault_payload()
    {
        var first = new IOException("actual history first");
        var sibling = new UnauthorizedAccessException("actual history sibling");
        var repository = new ControlledRuns(Suspended()) { Hold = true };
        var scene = new AgentsHavenScene(ViewModel(), MetadataRuntime(repository));
        var actual = scene.RefreshRunsAsync();
        try
        {
            repository.Read.SetException(new Exception[] { first, sibling });
            Assert.NotNull(await Record.ExceptionAsync(() => actual));
            Assert.Collection(repository.Read.Task.Exception!.InnerExceptions,
                cause => Assert.Same(first, cause), cause => Assert.Same(sibling, cause));
            Assert.True(actual.IsFaulted);
            Assert.True(ContainsExact(actual.Exception!, first));
            Assert.True(ContainsExact(actual.Exception!, sibling));
            var close = scene.CloseAndDrainAsync();
            Assert.NotNull(await Record.ExceptionAsync(() => close));
            Assert.True(close.IsFaulted);
            Assert.True(ContainsExact(close.Exception!, first));
            Assert.True(ContainsExact(close.Exception!, sibling));
            Assert.False(close.IsCanceled);
        }
        finally
        {
            repository.Read.TrySetException(new Exception[] { first, sibling });
            _ = await Record.ExceptionAsync(() => actual);
            _ = await Record.ExceptionAsync(() => scene.CloseAndDrainAsync());
        }
    }

    [Fact]
    public async Task Actual_text_notification_retirement_refuses_own_join_and_prevents_later_status_publication()
    {
        var scene = new AgentsHavenScene(ViewModel());
        var prior = scene.ExecutionStatusText.Content;
        var callbacks = 0;
        scene.LatestActivityText.Invalidated += (_, _) =>
        {
            if (++callbacks != 1) return;
            scene.RequestRetirement();
            Assert.NotNull(scene.OriginalClose);
            Assert.Throws<InvalidOperationException>(() => scene.DemandExternalOriginalRetirementJoin());
            Assert.Throws<InvalidOperationException>(() => { _ = scene.CloseAndDrainAsync(); });
        };
        var observed = Suspended();
        Assert.ThrowsAny<OperationCanceledException>(() => scene.ApplyRunUpdate(observed));
        var close = scene.CloseAndDrainAsync();
        Assert.NotNull(await Record.ExceptionAsync(() => close));
        Assert.True(close.IsFaulted);
        Assert.Equal(prior, scene.ExecutionStatusText.Content);
        Assert.Equal(1, callbacks);
        scene.ApplyRunUpdate(observed with { Status = AgentRunStatus.Completed });
        Assert.Equal(prior, scene.ExecutionStatusText.Content);
        Assert.Same(close, scene.CloseAndDrainAsync());
    }

    [Fact]
    public async Task Healthy_scene_history_refreshes_prune_without_permanently_sealing_presentation()
    {
        var repository = new ControlledRuns(Suspended());
        var scene = new AgentsHavenScene(ViewModel(), MetadataRuntime(repository));
        try
        {
            for (var index = 0; index < 256; ++index) await scene.RefreshRunsAsync();
            Assert.Null(scene.OriginalClose);
            Assert.Contains("Suspended", scene.ExecutionStatusText.Content, StringComparison.Ordinal);
            Assert.False(scene.RetryLatestButton.GetValue(HavenProperties.Enabled));
            Assert.Equal(257, repository.ReadCalls);
        }
        finally { await scene.CloseAndDrainAsync(); }
    }

    [AvaloniaFact]
    public async Task Actual_page_keeps_same_renderer_root_until_held_scene_read_and_child_close_settle()
    {
        var repository = new ControlledRuns(Suspended()) { Hold = true };
        var viewModel = ViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        var page = new AgentsPage(viewModel, MetadataRuntime(repository));
        var scene = page.HavenScene;
        var root = page.Scene.Root;
        var actual = scene.RefreshRunsAsync();
        try
        {
            Assert.Same(scene.Root, root);
            Assert.Single(scene.AgentCards.Items);
            page.Dispose(); // Original sync API requests, never detaches ahead of drain.
            Assert.NotNull(scene.OriginalClose);
            Assert.NotNull(page.OriginalClose);
            var close = page.CloseAndDrainAsync();
            Assert.Same(page.OriginalClose, close);
            Assert.Same(close, page.CloseAndDrainAsync());
            Assert.False(close.IsCompleted);
            Assert.Same(root, page.Scene.Root);
            Assert.False(actual.IsCompleted);
            Assert.All(repository.ReadTokens, token => Assert.False(token.CanBeCanceled));
            repository.Read.SetResult(repository.Values);
            Assert.NotNull(await Record.ExceptionAsync(() => actual));
            Assert.NotNull(await Record.ExceptionAsync(() => close));
            Assert.True(scene.OriginalClose!.IsFaulted);
            Assert.True(close.IsFaulted);
            Assert.Null(page.Scene.Root);
            Assert.Empty(scene.AgentCards.Items);
            Assert.Equal(0, repository.Upserts);
        }
        finally
        {
            repository.Read.TrySetResult(repository.Values);
            _ = await Record.ExceptionAsync(() => actual);
            _ = await Record.ExceptionAsync(() => page.CloseAndDrainAsync());
        }
    }

    [AvaloniaFact]
    public async Task Actual_child_notification_can_request_page_retirement_but_cannot_join_same_page_close()
    {
        var page = new AgentsPage(ViewModel());
        var scene = page.HavenScene;
        var root = page.Scene.Root;
        var callbacks = 0;
        scene.LatestActivityText.Invalidated += (_, _) =>
        {
            if (++callbacks != 1) return;
            page.RequestRetirement();
            Assert.NotNull(page.OriginalClose);
            Assert.NotNull(scene.OriginalClose);
            Assert.Same(root, page.Scene.Root);
            Assert.Throws<InvalidOperationException>(() => page.DemandExternalOriginalRetirementJoin());
            Assert.Throws<InvalidOperationException>(() => { _ = page.CloseAndDrainAsync(); });
        };
        try
        {
            Assert.ThrowsAny<OperationCanceledException>(() => scene.ApplyRunUpdate(Suspended()));
            var close = page.CloseAndDrainAsync();
            Assert.NotNull(await Record.ExceptionAsync(() => close));
            Assert.True(close.IsFaulted);
            Assert.True(scene.OriginalClose!.IsFaulted);
            Assert.Equal(1, callbacks);
            Assert.Null(page.Scene.Root);
            Assert.Same(close, page.CloseAndDrainAsync());
        }
        finally { _ = await Record.ExceptionAsync(() => page.CloseAndDrainAsync()); }
    }

    private static bool ContainsExact(Exception container, Exception expected) =>
        ReferenceEquals(container, expected) || container is AggregateException group &&
        group.InnerExceptions.Any(child => ContainsExact(child, expected)); // Test inspection only, no production flattening.

    private static AgentRun Suspended()
    {
        var now = DateTimeOffset.UtcNow;
        return new AgentRun(Guid.NewGuid(), Guid.NewGuid(), "Saved Agent", "Original task",
            AgentRunStatus.Suspended, "local-model", string.Empty, "Review response does not resume this Task/Run.",
            "[]", "[]", now, now, null, ProgressPercent: 35)
        { CanonicalTask = new AgentRunCanonicalBinding(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            7, TaskExecutionLifecycle.Suspended) };
    }

    private static CatalogPageViewModel ViewModel() => new(CatalogPageKind.Agents, new Catalog(), new Models(), true);

    // Same real metadata-only constructor seam as maintained AgentTaskRuntimeServiceTests.
    // Unused collaborators are never treated as actor/authority/provider readiness.
    private static AgentTaskRuntimeService MetadataRuntime(ControlledRuns repository) =>
        new(null!, repository, null!, null!, null!, null!);

    private sealed class ControlledRuns(params AgentRun[] values) : IAgentRunRepository
    {
        internal IReadOnlyList<AgentRun> Values { get; } = values;
        internal bool Hold { get; set; }
        internal int ReadCalls, GetCalls, Upserts;
        internal List<CancellationToken> ReadTokens { get; } = [];
        internal TaskCompletionSource<IReadOnlyList<AgentRun>> Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<AgentRun>> GetRecentAsync(int limit, CancellationToken token)
        { ++ReadCalls; ReadTokens.Add(token); return Hold ? Read.Task : Task.FromResult(Values); }
        public Task<AgentRun?> GetAsync(Guid id, CancellationToken token)
        { ++GetCalls; return Task.FromResult(Values.FirstOrDefault(run => run.Id == id)); }
        public Task UpsertAsync(AgentRun run, CancellationToken token)
        { ++Upserts; return Task.CompletedTask; }
        public Task<IReadOnlyList<AgentRun>> GetByAgentAsync(Guid id, int limit, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<AgentRun>>(Values.Where(run => run.AgentId == id).Take(limit).ToArray());
    }

    private sealed class Catalog : ICatalogRepository
    {
        private readonly AgentDefinition _agent = new(Guid.NewGuid(), "Saved", "Original catalogue card", "Instructions",
            "agent", "default", null, string.Empty, "{}", false, true, DateTimeOffset.UtcNow);
        public Task<IReadOnlyList<AgentDefinition>> GetAgentsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<AgentDefinition>>([_agent]);
        public Task<IReadOnlyList<AgentDefinition>> GetAllAgentsAsync(CancellationToken token) => GetAgentsAsync(token);
        public Task<IReadOnlyList<PromptDefinition>> GetPromptsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<PromptDefinition>>([]);
        public Task UpsertAgentAsync(AgentDefinition value, CancellationToken token) => Task.CompletedTask;
        public Task UpsertPromptAsync(PromptDefinition value, CancellationToken token) => Task.CompletedTask;
        public Task SetAgentEnabledAsync(Guid id, bool enabled, CancellationToken token) => Task.CompletedTask;
        public Task SetPromptEnabledAsync(Guid id, bool enabled, CancellationToken token) => Task.CompletedTask;
        public Task DeleteCustomAgentAsync(Guid id, CancellationToken token) => Task.CompletedTask;
        public Task DeleteCustomPromptAsync(Guid id, CancellationToken token) => Task.CompletedTask;
    }

    private sealed class Models : IOllamaClient
    {
        public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken token)
        { await Task.Yield(); yield break; }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => Task.FromResult("Original draft");
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => Task.FromResult(new OllamaToolResponse(string.Empty, []));
    }
}
