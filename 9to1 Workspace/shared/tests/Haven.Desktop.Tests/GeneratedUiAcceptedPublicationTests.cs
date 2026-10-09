using System.Runtime.ExceptionServices;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Haven.Desktop.Services;
using Xunit;

namespace Haven.Desktop.Tests;

// Actual original publication/dispatcher custody; no provider, installed owner or permission is supplied.
public sealed class GeneratedUiAcceptedPublicationTests
{
    [AvaloniaFact]
    public async Task Accepted_dispatcher_callback_settles_after_retirement_and_keeps_physical_self_join_guard()
    {
        var outside = ExecutionContext.Capture()!;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource<DesktopOriginalWorkLifetime.Original>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new DesktopOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        var body = owner.RunAsync(async original => { ready.SetResult(original); await original.AwaitAsync(release.Task); });
        var tasks = new List<Task> { body }; var errors = new List<Exception>(); var calls = 0;
        try
        {
            var original = await ready.Task;
            original.BindPublicationGuard(() =>
            {
                ExecutionContext.Run(outside, state =>
                {
                    Assert.Null(owner.Executing);
                    Assert.Throws<InvalidOperationException>(() => { _ = owner.CloseAndDrainAsync(); });
                }, null);
                return true;
            });
            Assert.True(original.IsAcceptedPublicationCurrent);
            original.RunAcceptedPublicationCallback(() =>
            {
                Assert.Same(original, owner.Executing);
                ExecutionContext.Run(outside, state =>
                {
                    Assert.Null(owner.Executing); // The original context was restored away.
                    Assert.Throws<InvalidOperationException>(() => { _ = owner.CloseAndDrainAsync(); });
                }, null);
                calls++;
            });
            Assert.Equal(1, calls);
            var callback = Dispatcher.UIThread.InvokeAsync(() => original.RunAcceptedPublicationCallback(() => calls++),
                DispatcherPriority.Background).GetTask(); tasks.Add(callback);
            var joinedCallback = original.AwaitAsync(callback); tasks.Add(joinedCallback);
            var close = owner.CloseAndDrainAsync(); tasks.Add(close); Assert.False(close.IsCompleted);
            Assert.False(original.IsAcceptedPublicationCurrent);
            await joinedCallback; Assert.True(callback.IsCompletedSuccessfully); Assert.Equal(1, calls);
            release.SetResult(); await body; await close;
            Assert.True(close.IsCompletedSuccessfully); Assert.Same(close, owner.CloseAndDrainAsync());
        }
        catch (Exception failure) { errors.Add(failure); }
        finally
        {
            release.TrySetResult();
            await JoinEveryOriginalAsync(owner, tasks, errors, null);
        }
        Throw(errors);
    }

    [Fact]
    public async Task Actual_publication_guard_fault_is_retained_even_when_the_admitted_body_returns_successfully()
    {
        var io = new IOException("Actual publication source");
        var cancelled = new OperationCanceledException("Actual unrelated publication source");
        var cause = new AggregateException("Opaque publication callback", io, cancelled);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource<DesktopOriginalWorkLifetime.Original>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new DesktopOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        var body = owner.RunAsync(async original => { ready.SetResult(original); await original.AwaitAsync(release.Task); });
        var tasks = new List<Task> { body }; var errors = new List<Exception>(); var invoked = false;
        try
        {
            var original = await ready.Task; original.BindPublicationGuard(() => throw cause);
            Assert.Same(cause, Assert.Throws<AggregateException>(() => original.RunAcceptedPublicationCallback(() => invoked = true)));
            Assert.False(invoked); release.SetResult(); await body; Assert.True(body.IsCompletedSuccessfully);
            var close = owner.CloseAndDrainAsync(); tasks.Add(close);
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => close);
            AssertKnown(failure, cause); AssertKnown(close.Exception!, cause);
            Assert.True(close.IsFaulted); Assert.False(close.IsCanceled); Assert.Same(close, owner.CloseAndDrainAsync());
        }
        catch (Exception failure) { errors.Add(failure); }
        finally
        {
            release.TrySetResult();
            await JoinEveryOriginalAsync(owner, tasks, errors, cause);
        }
        Throw(errors);
    }

    [Fact]
    public async Task Saved_terminal_original_cannot_reenter_publication_and_its_exact_protocol_failure_seals_close()
    {
        DesktopOriginalWorkLifetime.Original? saved = null;
        var owner = new DesktopOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        var body = owner.RunAsync(original => { saved = original; return Task.CompletedTask; });
        var tasks = new List<Task> { body }; var errors = new List<Exception>(); Exception? cause = null;
        try
        {
            await body; var original = Assert.IsType<DesktopOriginalWorkLifetime.Original>(saved); var invoked = false;
            cause = Assert.Throws<InvalidOperationException>(() => original.RunAcceptedPublicationCallback(() => invoked = true));
            Assert.False(invoked); Assert.True(owner.IsRetiring);
            var close = Assert.IsAssignableFrom<Task>(owner.OriginalClose); tasks.Add(close);
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => close);
            AssertKnown(failure, cause); AssertKnown(close.Exception!, cause);
            Assert.Same(close, owner.CloseAndDrainAsync()); Assert.True(close.IsFaulted);
        }
        catch (Exception failure) { errors.Add(failure); }
        finally { await JoinEveryOriginalAsync(owner, tasks, errors, cause); }
        Throw(errors);
    }

    private static readonly List<object[]> RetainedOwners = [];
    private static async Task JoinEveryOriginalAsync(DesktopOriginalWorkLifetime owner, List<Task> tasks,
        List<Exception> errors, Exception? expectedCloseCause)
    {
        Task? close = null;
        try { close = owner.CloseAndDrainAsync(); tasks.Add(close); }
        catch (Exception failure) { errors.Add(failure); }
        var originals = tasks.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
        // Retain all actual owners/raw tasks before inspecting any unexpected cleanup cause.
        lock (RetainedOwners) RetainedOwners.Add([owner, originals, errors, expectedCloseCause!]);
        foreach (var actual in originals)
        {
            try { await actual; }
            catch (Exception failure)
            {
                var observed = actual.Exception ?? failure;
                if (ReferenceEquals(actual, close) && expectedCloseCause is not null)
                {
                    try { AssertKnown(observed, expectedCloseCause); }
                    catch (Exception unknown) { errors.Add(observed); errors.Add(unknown); }
                }
                else errors.Add(observed);
            }
        }
    }
    private static void AssertKnown(Exception actual, Exception expected)
    {
        if (ReferenceEquals(actual, expected)) return;
        if (actual is AggregateException { InnerExceptions.Count: > 0 } wrapper)
        { foreach (var direct in wrapper.InnerExceptions) AssertKnown(direct, expected); return; }
        Assert.Fail("Unknown accepted publication cause: " + actual);
    }
    private static void Throw(List<Exception> errors)
    {
        if (errors.Count == 0) return;
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        throw new AggregateException("Actual accepted publication fixture/independent cleanup failures.", errors);
    }
}
