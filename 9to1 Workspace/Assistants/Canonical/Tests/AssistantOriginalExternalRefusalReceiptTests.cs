using Xunit;

namespace HavenOS.Apps.Assistants.Canonical.Tests;

public sealed class AssistantOriginalExternalRefusalReceiptTests
{
    [Fact]
    public async Task Exact_settled_owner_refusal_propagates_to_both_raw_task_borrowers()
    {
        var cause = new UnauthorizedAccessException("Actual issuer declined before an effect.");
        var actual = Task.FromException(cause);
        var child = new AssistantPresentationOriginals(); var parent = new AssistantPresentationOriginals();
        await child.Admit(() =>
        {
            child.Retain(actual); parent.Retain(actual);
            Assert.True(child.AcknowledgeOriginalExternalPreEffectRefusal(actual, same => ReferenceEquals(same, actual)));
            return Task.FromResult(true);
        });
        await child.CloseAndDrainAsync(); await parent.CloseAndDrainAsync();
        Assert.True(AssistantOriginalExternalRefusalReceipts.IsAcknowledgedOriginal(actual));
    }

    [Fact]
    public async Task Foreign_same_exception_task_remains_failed_after_original_refusal_acknowledgment()
    {
        var cause = new UnauthorizedAccessException("SAME cause, separate original occurrence.");
        var actual = Task.FromException(cause); var alias = Task.FromException(cause);
        var child = new AssistantPresentationOriginals(); var parent = new AssistantPresentationOriginals();
        await child.Admit(() =>
        {
            child.Retain(actual); parent.Retain(actual); parent.Retain(alias);
            Assert.True(child.AcknowledgeOriginalExternalPreEffectRefusal(actual, same => ReferenceEquals(same, actual)));
            Assert.False(AssistantOriginalExternalRefusalReceipts.IsAcknowledgedOriginal(alias));
            return Task.FromResult(true);
        });
        await child.CloseAndDrainAsync();
        var failure = await Assert.ThrowsAsync<AggregateException>(() => parent.CloseAndDrainAsync());
        Assert.Same(cause, Assert.Single(failure.InnerExceptions));
    }

    [Fact]
    public async Task Unjoined_or_refused_issuer_and_mixed_payload_cannot_publish_receipt()
    {
        var cause = new UnauthorizedAccessException("Not yet acknowledged."); var sibling = new IOException("Unknown durable sibling.");
        var actual = Task.FromException(cause); var pending = new TaskCompletionSource();
        var mixed = new TaskCompletionSource(); mixed.SetException([cause, sibling]);
        var scope = new AssistantPresentationOriginals();
        await scope.Admit(() =>
        {
            scope.Retain(actual); scope.Retain(pending.Task); scope.Retain(mixed.Task);
            Assert.False(scope.AcknowledgeOriginalExternalPreEffectRefusal(actual, _ => false));
            Assert.False(scope.AcknowledgeOriginalExternalPreEffectRefusal(pending.Task, _ => true));
            Assert.False(scope.AcknowledgeOriginalExternalPreEffectRefusal(mixed.Task, _ => true));
            pending.SetResult(); return Task.FromResult(true);
        });
        var failure = await Assert.ThrowsAsync<AggregateException>(() => scope.CloseAndDrainAsync());
        Assert.Contains(failure.InnerExceptions, value => ReferenceEquals(value, cause));
        Assert.Contains(failure.InnerExceptions, value => ReferenceEquals(value, sibling));
    }

    [Fact]
    public async Task Foreign_empty_group_is_opaque_and_never_acknowledged()
    {
        var foreign = new AggregateException("Foreign empty group."); var actual = Task.FromException(foreign);
        var scope = new AssistantPresentationOriginals();
        await scope.Admit(() =>
        {
            scope.Retain(actual);
            Assert.False(scope.AcknowledgeOriginalExternalPreEffectRefusal(actual, _ => true));
            return Task.FromResult(true);
        });
        var failure = await Assert.ThrowsAsync<AggregateException>(() => scope.CloseAndDrainAsync());
        Assert.Same(foreign, Assert.Single(failure.InnerExceptions));
    }

    [Fact]
    public async Task Actual_issuer_query_failure_remains_own_fault_and_cannot_self_join_scope()
    {
        var cause = new UnauthorizedAccessException("Actual declined source."); var query = new IOException("Issuer observation failed.");
        var actual = Task.FromException(cause); var scope = new AssistantPresentationOriginals();
        var command = scope.Admit<bool>(() =>
        {
            scope.Retain(actual);
            scope.AcknowledgeOriginalExternalPreEffectRefusal(actual, _ =>
            {
                Assert.Throws<InvalidOperationException>(() => { _ = scope.CloseAndDrainAsync(); });
                throw query;
            });
            return Task.FromResult(true);
        });
        Assert.Same(query, await Record.ExceptionAsync(() => command));
        Assert.False(AssistantOriginalExternalRefusalReceipts.IsAcknowledgedOriginal(actual));
        var failure = await Assert.ThrowsAsync<AggregateException>(() => scope.CloseAndDrainAsync());
        Assert.Contains(failure.InnerExceptions, value => ReferenceEquals(value, cause));
        Assert.Contains(failure.InnerExceptions, value => ReferenceEquals(value, query));
    }
}
