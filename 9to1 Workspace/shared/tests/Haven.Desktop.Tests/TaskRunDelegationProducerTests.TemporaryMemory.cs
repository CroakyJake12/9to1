using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCloudPermissionCallerTests
{
    [Fact]
    public async Task Canonical_delegated_child_preserves_governed_memory_without_an_ordinary_temporary_choice()
    {
        var memory = new CanonicalChildMemorySource();
        await using var rig = new ChildRig(memorySource: memory);
        var parent = await rig.ParentAsync();
        // The real child Chat factory creates Tasks/Chat with IsTemporary=true for
        // source storage; that flag does not turn a canonical Agent into ordinary Chat.
        var run = await rig.Agents.RunDelegatedAsync(parent, rig.Definition.Id, "canonical-memory",
            "Use the existing governed child context", [], TestContext.Current.CancellationToken);
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Equal(1, memory.Reads);
        var request = Assert.Single(rig.Client.Requests);
        Assert.NotNull(request.ExecutionContext);
        Assert.Contains(CanonicalChildMemorySource.Sentinel, request.SystemPrompt ?? "", StringComparison.Ordinal);
        Assert.Null(request.Options?.TemporaryMemoryReadChoice);
        Assert.Null(request.Options?.RequestedContextConstraints);
        var child = (await rig.Tasks.GetAsync(run.CanonicalTask!.TaskId, TestContext.Current.CancellationToken))!;
        Assert.NotNull(child.ParentDelegation);
        Assert.Equal(TaskExecutionLifecycle.Completed, child.State);
        var currentParent = (await rig.Tasks.GetAsync(parent.Snapshot.TaskId, TestContext.Current.CancellationToken))!;
        var complete = await rig.Tasks.CompleteAttemptAsync(currentParent.TaskId, currentParent.ExecutionId,
            parent.AttemptId, TestContext.Current.CancellationToken);
        Assert.Equal(TaskExecutionLifecycle.Completed, complete.State);
    }

    private sealed class CanonicalChildMemorySource : IMemoryQuerySource
    {
        internal const string Sentinel = "existing-canonical-child-memory";
        internal int Reads;
        public Task<IReadOnlyList<KnowledgeRecord>> GetActiveLearnMeAsync(int limit, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Reads++;
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult<IReadOnlyList<KnowledgeRecord>>([new(Guid.NewGuid(), KnowledgeCategory.LearnMe,
                "controlled canonical source", Sentinel, Sentinel, KnowledgePrivacyClass.Normal, 1, true,
                now, now, null, "controlled original source", [])]);
        }
    }
}
