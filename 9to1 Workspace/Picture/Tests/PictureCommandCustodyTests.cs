using Avalonia.Headless.XUnit;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureCommandCustodyTests
{
    // These are actual failed owners. Tests keep them and their raw tasks alive;
    // a fixture must not manufacture successful retirement to tidy a failure.
    private static readonly List<MainWindow> RetainedFailedOwners = [];

    [AvaloniaFact]
    public async Task A_failed_direct_command_remains_owned_after_a_later_success_and_failed_close()
    {
        var window = new MainWindow(new PictureFixtureReadiness());
        RetainedFailedOwners.Add(window);
        await window.InitializeAsync(TestContext.Current.CancellationToken);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        var failed = window.DispatchAsync("9to1.Picture.Unknown", null).AsTask();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => failed);
        Assert.Same(failed, window.OriginalCommand);
        await window.DispatchAsync("9to1.Picture.MetadataPreserve", null);
        Assert.True(window.OriginalCommand!.IsCompletedSuccessfully);
        Assert.Contains(window.OriginalCommands, source => ReferenceEquals(source, failed));
        window.Close();
        Assert.NotNull(window.OriginalClose);
        var originalClose = window.OriginalClose!;
        var closeFailure = await Assert.ThrowsAsync<AggregateException>(async () => { await originalClose; });
        window.Close();
        Assert.Same(originalClose, window.OriginalClose);
        Assert.Contains(closeFailure.InnerExceptions, error => ReferenceEquals(error, failure));
        Assert.Contains(window.OriginalCommands, source => ReferenceEquals(source, failed));
        Assert.False(closed);
    }

    [AvaloniaFact]
    public async Task An_admitted_canceled_command_has_no_unproved_clean_close_waiver()
    {
        var window = new MainWindow(new PictureFixtureReadiness());
        RetainedFailedOwners.Add(window);
        await window.InitializeAsync(TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var canceled = window.DispatchAsync("9to1.Picture.Fit", null, cancellation.Token).AsTask();
        cancellation.Cancel(); // Actual admission has occurred; its gated UI driver has not resumed.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.True(canceled.IsCanceled);
        await window.DispatchAsync("9to1.Picture.MetadataPreserve", null);
        Assert.Contains(window.OriginalCommands, source => ReferenceEquals(source, canceled));
        window.Close();
        Assert.NotNull(window.OriginalClose);
        var closeFailure = await Assert.ThrowsAsync<AggregateException>(async () => { await window.OriginalClose!; });
        Assert.Contains(closeFailure.InnerExceptions, error => error is OperationCanceledException);
        Assert.Contains(window.OriginalCommands, source => ReferenceEquals(source, canceled));
    }

    [AvaloniaFact]
    public async Task Admission_prunes_only_successful_sources_and_refuses_to_drop_failures_at_its_bound()
    {
        var window = new MainWindow(new PictureFixtureReadiness());
        RetainedFailedOwners.Add(window);
        await window.InitializeAsync(TestContext.Current.CancellationToken);
        await window.DispatchAsync("9to1.Picture.MetadataPreserve", null);
        var successful = window.OriginalCommand!;
        var sources = new List<Task>();
        for (var index = 0; index < 128; index++)
        {
            var source = window.DispatchAsync("9to1.Picture.Unknown", null).AsTask();
            await Assert.ThrowsAsync<InvalidOperationException>(() => source);
            sources.Add(source);
        }
        Assert.DoesNotContain(window.OriginalCommands, source => ReferenceEquals(source, successful));
        Assert.Equal(128, window.OriginalCommands.Count);
        var last = window.OriginalCommand;
        Assert.Throws<InvalidOperationException>(() => { _ = window.DispatchAsync("9to1.Picture.MetadataPreserve", null); });
        Assert.Same(last, window.OriginalCommand);
        Assert.Equal(128, window.OriginalCommands.Count);
        Assert.All(sources, source => Assert.Contains(window.OriginalCommands, retained => ReferenceEquals(source, retained)));
        window.Close();
        Assert.NotNull(window.OriginalClose);
        var closeFailure = await Assert.ThrowsAsync<AggregateException>(async () => { await window.OriginalClose!; });
        Assert.Equal(128, closeFailure.InnerExceptions.Count);
    }
}
