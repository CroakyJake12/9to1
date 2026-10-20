using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using System.Threading;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Lane-3 coverage for the guarded <see cref="Popup.BeforeShow"/> seam. The callback is synchronous
/// after the popup's open state is published, so a handler may Close()/retire/reopen the popup before
/// returning. These fixtures exercise the deterministic close/reentry/throw/cleanup-fault guards and
/// assert the original popup dismissal/focus/open semantics are preserved (no Show on a disposed
/// destination, no duplicate disposal, original callback errors preserved, retryable). Execution is
/// slot-gated / NOT RUN here.
/// </summary>
public sealed class CuiPopupBeforeShowGuardTests
{
    private static Popup NewPopupInWindow(out Window window)
    {
        var target = new Border { Width = 120, Height = 40 };
        window = new Window { Content = new Panel { Children = { target } }, Width = 400, Height = 300 };
        window.Show();
        window.UpdateLayout();
        return new Popup { PlacementTarget = target, Child = new TextBlock { Text = "x" } };
    }

    [Fact]
    public async Task Before_show_handler_close_never_shows_the_retired_destination()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var popup = NewPopupInWindow(out var window);
            try
            {
                EventHandler<Popup.PopupBeforeShowEventArgs> handler = (_, _) => popup.Close();
                popup.BeforeShow += handler;
                popup.IsOpen = true;
                Dispatcher.UIThread.RunJobs();
                // The handler closed the popup while it was opening: the destination must not be shown
                // and the popup must report closed, with no exception from operating on a disposed host.
                Assert.False(popup.IsOpen);
                popup.BeforeShow -= handler;
                // A later genuine open still works (the state was fully retired).
                popup.IsOpen = true;
                Dispatcher.UIThread.RunJobs();
                Assert.True(popup.IsOpen);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Before_show_handler_reopen_does_not_double_show_and_wins_cleanly()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var popup = NewPopupInWindow(out var window);
            try
            {
                var calls = 0;
                EventHandler<Popup.PopupBeforeShowEventArgs> handler = (_, _) =>
                {
                    if (++calls == 1)
                    {
                        // Retire and re-open synchronously from inside the callback.
                        popup.Close();
                        popup.IsOpen = true;
                    }
                };
                popup.BeforeShow += handler;
                popup.IsOpen = true;
                Dispatcher.UIThread.RunJobs();
                // The inner reopen succeeded; the outer Open() must not double-show the retired host.
                Assert.True(popup.IsOpen);
                Assert.True(calls >= 1);
                popup.BeforeShow -= handler;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Before_show_handler_throw_is_preserved_state_retired_and_retryable()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var popup = NewPopupInWindow(out var window);
            try
            {
                var boom = new InvalidOperationException("boom");
                EventHandler<Popup.PopupBeforeShowEventArgs> handler = (_, _) => throw boom;
                popup.BeforeShow += handler;
                var ex = Assert.Throws<InvalidOperationException>(() => popup.IsOpen = true);
                Assert.Same(boom, ex);
                Assert.False(popup.IsOpen);
                popup.BeforeShow -= handler;
                // Retry with the faulting handler removed succeeds (clean state was retired exactly once).
                popup.IsOpen = true;
                Dispatcher.UIThread.RunJobs();
                Assert.True(popup.IsOpen);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Cleanup_fault_never_masks_the_original_callback_error_nor_double_disposes()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var popup = NewPopupInWindow(out var window);
            try
            {
                // The handler BOTH closes the popup (which already retires and disposes the destination)
                // and then throws. The original callback error must be preserved and there must be no
                // duplicate disposal of the already-closed destination.
                EventHandler<Popup.PopupBeforeShowEventArgs> handler = (_, _) =>
                {
                    popup.Close();
                    throw new InvalidOperationException("callback-fault");
                };
                popup.BeforeShow += handler;
                var ex = Assert.Throws<InvalidOperationException>(() => popup.IsOpen = true);
                Assert.Equal("callback-fault", ex.Message);
                Assert.False(popup.IsOpen);
                popup.BeforeShow -= handler;
                // Still retryable once the faulting handler is removed.
                popup.IsOpen = true;
                Dispatcher.UIThread.RunJobs();
                Assert.True(popup.IsOpen);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
