using System.Runtime.ExceptionServices;
using Xunit;

namespace HavenOS.Apps.Assistants.Canonical.Tests;

// Custody-only controls for the actual bridge capture helper. No definition,
// catalogue, Task, Home actor, resource or execution authority is fabricated.
public sealed class AssistantConfigurationCapabilitySourceCustodyTests
{
    [Fact]
    public async Task Post_callback_refusal_retains_and_joins_same_accepted_raw_source_before_reporting_both_causes()
    {
        var scopeFailure = new IOException("Actual post-publication scope refusal.");
        var sourceFailure = new IOException("Actual source failed after its acceptance.");
        var raw = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retained = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = DenAssistantCanonicalBridge.CaptureOriginalConfigurationCapabilitySourceAsync(
            () => raw.Task, body => { body(); throw scopeFailure; }, same => retained.SetResult(same));
        var unexpected = new List<Exception>();
        try
        {
            Assert.Same(raw.Task, await retained.Task.WaitAsync(TestContext.Current.CancellationToken));
            Assert.False(original.IsCompleted); raw.SetException(sourceFailure);
            var observed = await Record.ExceptionAsync(() => original); Assert.NotNull(observed); Assert.True(original.IsFaulted);
            var aggregate = Assert.IsType<AggregateException>(observed);
            Assert.Contains(scopeFailure, aggregate.InnerExceptions); Assert.Contains(sourceFailure, aggregate.InnerExceptions);
            Assert.Equal(2, aggregate.InnerExceptions.Count);
        }
        catch (Exception cause) { unexpected.Add(cause); }
        finally
        {
            raw.TrySetException(sourceFailure);
            try { await original; }
            catch (AggregateException actual) when (actual.InnerExceptions.Count == 2 &&
                actual.InnerExceptions.Any(cause => ReferenceEquals(cause, scopeFailure)) &&
                actual.InnerExceptions.Any(cause => ReferenceEquals(cause, sourceFailure))) { }
            catch (Exception cause) { unexpected.Add(cause); }
        }
        if (unexpected.Count == 1) ExceptionDispatchInfo.Capture(unexpected[0]).Throw();
        if (unexpected.Count != 0) throw new AggregateException("Original fixture assertion/source settlement failed.", unexpected);
    }

    [Fact]
    public async Task Retainer_refusal_cannot_replace_foreign_opaque_raw_group_or_promote_faulted_OCE_to_cancellation()
    {
        var retainer = new OperationCanceledException("A synchronous retainer refusal is not canceled raw work.");
        var foreign = new AggregateException("Actual foreign opaque empty group.");
        var raw = Task.FromException<int>(foreign); Task? retained = null;
        var original = DenAssistantCanonicalBridge.CaptureOriginalConfigurationCapabilitySourceAsync(
            () => raw, body => body(), same => { retained = same; throw retainer; });
        var observed = await Record.ExceptionAsync(() => original); Assert.NotNull(observed);
        Assert.Same(raw, retained); Assert.True(original.IsFaulted); Assert.False(original.IsCanceled);
        var aggregate = Assert.IsType<AggregateException>(observed);
        Assert.Contains(retainer, aggregate.InnerExceptions); Assert.Contains(foreign, aggregate.InnerExceptions);
        Assert.Equal(2, aggregate.InnerExceptions.Count); Assert.Empty(foreign.InnerExceptions);
    }
}
