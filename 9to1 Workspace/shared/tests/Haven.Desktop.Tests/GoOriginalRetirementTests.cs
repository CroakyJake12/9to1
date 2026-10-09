using Avalonia.Headless.XUnit;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Go;

namespace Haven.Desktop.Tests;

// Actual Go callbacks/scene root and shared original owner. Headless custody
// controls do not certify native frame presentation, Home authority or Shell DI.
public sealed class GoOriginalRetirementTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);
    [AvaloniaFact]
    public async Task Owning_submit_publication_returns_before_scene_cleanup_and_disposed_delivery()
    {
        var page = new GoPage(new HavenEventBus()); var root = page.SceneHost.Root;
        var returned = false; var disposed = false; Task? actualClose = null;
        page.Disposed += (_, _) => { Assert.True(returned); disposed = true; };
        page.SubmitRequested += (_, instruction) =>
        {
            Assert.Equal("actual admitted instruction", instruction);
            page.RequestRetirement(); actualClose = page.OriginalClose;
            Assert.NotNull(actualClose); Assert.False(actualClose.IsCompleted);
            Assert.Same(root, page.SceneHost.Root); Assert.False(disposed); returned = true;
        };
        try
        {
            await page.SubmitOverlayInstructionAsync("actual admitted instruction"); returned = true;
            await page.CloseAndDrainAsync().WaitAsync(Bound);
            Assert.Same(actualClose, page.CloseAndDrainAsync()); Assert.Null(page.SceneHost.Root); Assert.True(disposed);
        }
        finally { returned = true; await page.CloseAndDrainAsync().WaitAsync(Bound); }
    }
    [AvaloniaFact]
    public async Task Restored_context_actual_submit_callback_refuses_its_encompassing_join()
    {
        var page = new GoPage(new HavenEventBus()); var external = ExecutionContext.Capture()!; var refused = false;
        page.SubmitRequested += (_, _) => ExecutionContext.Run(external, _ =>
        {
            Assert.Throws<InvalidOperationException>(() => { page.CloseAndDrainAsync().GetAwaiter().GetResult(); });
            refused = true;
        }, null);
        try { await page.SubmitOverlayInstructionAsync("actual"); Assert.True(refused); await page.CloseAndDrainAsync().WaitAsync(Bound); }
        finally { await page.CloseAndDrainAsync().WaitAsync(Bound); }
    }
    [AvaloniaFact]
    public async Task Scene_remains_owned_until_same_actual_parent_original_settles()
    {
        var page = new GoPage(new HavenEventBus()); var root = page.SceneHost.Root;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parent = new DesktopOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        // Genuine shared owner original. This is a controlled parent producer,
        // not a fake Shell DI acceptance or canonical-business completion receipt.
        var actualParent = parent.RunAsync(original =>
        {
            page.RetainOriginalParentBorrower(original.Task, parent.DemandExternalClose);
            return original.AwaitAsync(release.Task);
        });
        Task? actualClose = null;
        try
        {
            page.RequestRetirement(); actualClose = page.CloseAndDrainAsync();
            Assert.False(actualClose.IsCompleted); Assert.Same(root, page.SceneHost.Root);
            Assert.False(actualParent.IsCanceled); release.TrySetResult(); await actualParent.WaitAsync(Bound);
            await actualClose.WaitAsync(Bound); Assert.Null(page.SceneHost.Root);
        }
        finally
        {
            release.TrySetResult();
            var failures = new List<Exception>();
            foreach (var original in new[] { actualParent, actualClose ?? page.CloseAndDrainAsync(), parent.CloseAndDrainAsync() })
                try { await original.WaitAsync(Bound); } catch (Exception error) { failures.Add(error); }
            if (failures.Count > 0) throw new AggregateException("Independent Go and actual parent teardown failed.", failures);
        }
    }
    [AvaloniaFact]
    public async Task Actual_cleanup_callback_restoring_context_cannot_join_its_own_close()
    {
        var page = new GoPage(new HavenEventBus()); var external = ExecutionContext.Capture()!; var refused = false;
        page.Disposed += (_, _) => ExecutionContext.Run(external, _ =>
        {
            Assert.Throws<InvalidOperationException>(() => { page.CloseAndDrainAsync().GetAwaiter().GetResult(); });
            refused = true;
        }, null);
        try { await page.CloseAndDrainAsync().WaitAsync(Bound); Assert.True(refused); Assert.Null(page.SceneHost.Root); }
        finally { await page.CloseAndDrainAsync().WaitAsync(Bound); }
    }
    [AvaloniaFact]
    public async Task Direct_publication_cancellation_is_faulted_and_retains_original_reference()
    {
        var page = new GoPage(new HavenEventBus());
        var cause = new OperationCanceledException("Actual synchronous subscriber has no canceled Task.");
        page.SubmitRequested += (_, _) => throw cause;
        Task? actualClose = null; var observed = false; var failures = new List<Exception>();
        try
        {
            var direct = Assert.Throws<AggregateException>(() => { _ = page.SubmitOverlayInstructionAsync("actual"); });
            Assert.Contains(Causes(direct), original => ReferenceEquals(original, cause));
            actualClose = page.CloseAndDrainAsync();
            var error = await Assert.ThrowsAnyAsync<Exception>(() => actualClose);
            Assert.Contains(Causes(error), original => ReferenceEquals(original, cause));
            Assert.True(actualClose.IsFaulted); Assert.False(actualClose.IsCanceled); observed = true;
        }
        catch (Exception error) { failures.Add(error); }
        finally { await JoinExpectedFault(page, actualClose, observed, failures); }
        if (failures.Count > 0) throw new AggregateException("Go cancellation control and original cleanup failed.", failures);
    }
    [AvaloniaFact]
    public async Task Complete_original_subscriber_fault_group_remains_at_external_close()
    {
        var page = new GoPage(new HavenEventBus()); var first = new IOException("Actual subscriber one.");
        var second = new InvalidOperationException("Actual subscriber two."); var group = new AggregateException(first, second);
        page.SubmitRequested += (_, _) => throw group;
        Task? actualClose = null; var observed = false; var failures = new List<Exception>();
        try
        {
            Assert.Same(group, Assert.Throws<AggregateException>(() => { _ = page.SubmitOverlayInstructionAsync("actual"); }));
            actualClose = page.CloseAndDrainAsync(); var error = await Assert.ThrowsAnyAsync<Exception>(() => actualClose);
            Assert.Contains(Causes(error), original => ReferenceEquals(original, first));
            Assert.Contains(Causes(error), original => ReferenceEquals(original, second)); observed = true;
        }
        catch (Exception error) { failures.Add(error); }
        finally { await JoinExpectedFault(page, actualClose, observed, failures); }
        if (failures.Count > 0) throw new AggregateException("Go compound control and original cleanup failed.", failures);
    }
    [AvaloniaFact]
    public async Task Refused_wrapper_admission_still_joins_the_same_raw_parent_original_before_scene_release()
    {
        var page = new GoPage(new HavenEventBus()); var root = page.SceneHost.Root;
        var raw = Enumerable.Range(0, 129).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        Task? close = null; var observed = false; var failures = new List<Exception>();
        try
        {
            for (var index = 0; index < 128; index++) page.RetainOriginalParentBorrower(raw[index].Task, () => { });
            var refusal = Assert.Throws<InvalidOperationException>(() => page.RetainOriginalParentBorrower(raw[128].Task, () => { }));
            close = page.CloseAndDrainAsync();
            foreach (var source in raw.Take(128)) source.TrySetResult();
            // Allow the genuine owner continuation to reach raw-parent cleanup.
            await Task.WhenAll(raw.Take(128).Select(source => source.Task));
            Assert.False(close.IsCompleted); Assert.Same(root, page.SceneHost.Root);
            raw[128].TrySetResult();
            var error = await Assert.ThrowsAnyAsync<Exception>(() => close.WaitAsync(Bound));
            Assert.Contains(Causes(error), cause => ReferenceEquals(cause, refusal));
            Assert.True(close.IsFaulted); observed = true;
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            foreach (var actual in raw) actual.TrySetResult();
            await Task.WhenAll(raw.Select(source => source.Task));
            await JoinExpectedFault(page, close, observed, failures);
        }
        if (failures.Count > 0) throw new AggregateException("Raw-parent admission control and owning cleanup failed.", failures);
    }

    private static async Task JoinExpectedFault(GoPage page, Task? issued, bool observed, List<Exception> failures)
    {
        Task? actual = null;
        try { actual = issued ?? page.CloseAndDrainAsync(); await actual.WaitAsync(Bound); }
        catch (Exception error)
        {
            if (!observed || actual is not { IsFaulted: true })
            {
                if (actual?.Exception is { InnerExceptions.Count: > 0 } group) failures.AddRange(group.InnerExceptions);
                else failures.Add(error);
            }
        }
    }
    private static IEnumerable<Exception> Causes(Exception original)
    {
        yield return original;
        if (original is AggregateException group)
            foreach (var member in group.InnerExceptions) foreach (var cause in Causes(member)) yield return cause;
    }
}
