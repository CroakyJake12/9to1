using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class OriginalTabTransientPresentationTests
{
    [AvaloniaFact]
    public async Task Selection_uses_stable_origin_container_and_cancellation_restores_original()
    {
        var host = new OriginalTabTransientPresentation();
        var original = new TextBlock { Text = "Original Board" };
        var picker = new TextBlock { Text = "Canonical destination" };
        var detached = 0; original.DetachedFromVisualTree += (_, _) => detached++;
        Assert.Same(host, host.BindOriginal(original));
        var window = new Window { Content = host }; window.Show();
        try
        {
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = host.PresentAsync(picker, async token =>
        {
            admitted.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return true;
        }, CancellationToken.None);
        await admitted.Task;
        Assert.Same(picker, host.VisibleContent);
        Assert.NotNull(original.Parent);
        Assert.Same(host, host.BindOriginal(original));
        host.CancelPending();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Same(original, host.VisibleContent);
        Assert.Null(picker.Parent);
        Assert.Equal(0, detached);
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Another_tab_container_is_unchanged_and_duplicate_admission_is_refused()
    {
        var first = new OriginalTabTransientPresentation(); var second = new OriginalTabTransientPresentation();
        var firstView = new TextBlock(); var secondView = new TextBlock();
        first.BindOriginal(firstView); second.BindOriginal(secondView);
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = first.PresentAsync(new TextBlock(), _ => decision.Task, CancellationToken.None);
        Assert.Same(secondView, second.VisibleContent);
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.PresentAsync(new TextBlock(), _ => Task.FromResult(true), CancellationToken.None));
        decision.SetResult(true); Assert.True(await active);
        Assert.Same(firstView, first.VisibleContent); Assert.Same(secondView, second.VisibleContent);
    }
}
