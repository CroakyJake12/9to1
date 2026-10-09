using Xunit;
using CloseOriginal = HavenOS.Apps.Assistants.Canonical.DenAssistantOriginalDevelopmentOwner.CompatibleResumePinCloseOriginal;

namespace HavenOS.Apps.Assistants.Canonical.Tests;

// Finite child-close custody controls only. They issue no project, checkpoint,
// Home receipt or revision pin; the genuine full Den/SQL journey tests cover those.
public sealed class AssistantCompatibleResumePinCloseOriginalTests
{
    [Fact]
    public async Task Parent_original_scope_can_dispose_child_and_joins_same_raw_close()
    {
        var parent = new AssistantPresentationOriginals();
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var child = new CloseOriginal(() => { started.TrySetResult(); return raw.Task; }, parent.Retain);
        Task? sameClose = null;
        var command = parent.Admit(async () =>
        {
            sameClose = parent.Source(child.CloseAndDrainAsync);
            await sameClose; return true;
        });
        try
        {
            await DemandGateBeforeOriginalTerminalAsync(started.Task, command);
            Assert.False(command.IsCompleted); Assert.False(sameClose!.IsCompleted);
            Assert.Same(sameClose, child.OriginalClose);
            raw.SetResult(); await command;
            Assert.Same(sameClose, child.CloseAndDrainAsync());
            await parent.CloseAndDrainAsync();
        }
        finally { raw.TrySetResult(); await command; await parent.CloseAndDrainAsync(); }
    }

    [Fact]
    public async Task Child_close_callback_cannot_restore_neutral_context_and_join_own_pending_close()
    {
        var neutral = ExecutionContext.Capture()!;
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CloseOriginal? child = null;
        child = new CloseOriginal(() => raw.Task, sameRaw =>
        {
            Assert.Same(raw.Task, sameRaw);
            ExecutionContext.Run(neutral.CreateCopy(), _ =>
            {
                Assert.Throws<InvalidOperationException>(() => { _ = child!.CloseAndDrainAsync(); });
            }, null);
            observed.TrySetResult();
        });
        var sameClose = child.CloseAndDrainAsync();
        try
        {
            await DemandGateBeforeOriginalTerminalAsync(observed.Task, sameClose);
            Assert.False(sameClose.IsCompleted); Assert.Same(sameClose, child.OriginalClose);
            raw.SetResult(); await sameClose;
            Assert.Same(sameClose, child.CloseAndDrainAsync());
        }
        finally { raw.TrySetResult(); await sameClose; }
    }

    [Fact]
    public async Task Retainer_failure_still_joins_actual_child_and_keeps_faulted_OCE_siblings()
    {
        var callback = new IOException("Actual child retainer refused after raw capture.");
        var canceledCause = new OperationCanceledException("Faulted source cause without a canceled Task.");
        var sibling = new IOException("Actual raw child sibling.");
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var child = new CloseOriginal(() => raw.Task, _ => { captured.TrySetResult(); throw callback; });
        var sameClose = child.CloseAndDrainAsync();
        try
        {
            await DemandGateBeforeOriginalTerminalAsync(captured.Task, sameClose);
            Assert.False(sameClose.IsCompleted);
            raw.SetException([canceledCause, sibling]);
            var failure = await Assert.ThrowsAsync<AggregateException>(() => sameClose);
            Assert.Contains(failure.InnerExceptions, cause => ReferenceEquals(cause, callback));
            Assert.Contains(failure.InnerExceptions, cause => ReferenceEquals(cause, canceledCause));
            Assert.Contains(failure.InnerExceptions, cause => ReferenceEquals(cause, sibling));
            Assert.True(raw.Task.IsFaulted); Assert.True(sameClose.IsFaulted);
            Assert.Same(sameClose, child.CloseAndDrainAsync());
        }
        finally
        {
            raw.TrySetException([canceledCause, sibling]);
            try { await sameClose; } catch (AggregateException) { }
        }
    }
    private static async Task DemandGateBeforeOriginalTerminalAsync(Task actualGate, Task sameOriginal)
    {
        var first = await Task.WhenAny(actualGate, sameOriginal).WaitAsync(TestContext.Current.CancellationToken);
        if (ReferenceEquals(first, sameOriginal)) await sameOriginal; // Surface actual early source failure before asserting a gate.
        Assert.Same(actualGate, first);
        await actualGate;
    }

}
