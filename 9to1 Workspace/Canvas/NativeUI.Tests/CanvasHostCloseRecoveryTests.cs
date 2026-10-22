using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Xunit;

namespace HavenOS.Apps.Canvas.NativeUI.Tests;

/// <summary>Actual headless native window/CUI/Files/Home interactions for an empty session.
/// These controls do not attest a loaded/dirty document, Rnote availability, installed apps or ARM.</summary>
[Collection("Canvas native UI")]
public sealed class CanvasHostCloseRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Known_empty_session_read_rejection_normally_closes_and_retains_its_failed_original(bool unsupportedPayload)
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        await native.Dispatch<bool>(async () =>
        {
            await using var fixture = await CanvasHostLaunchTests.Fixture.CreateAsync();
            if (unsupportedPayload) await fixture.SeedCatalogueArtifactAsync("Revision without donor payload");
            var window = await fixture.ShowAsync();
            if (unsupportedPayload) CanvasHostLaunchTests.Click(Assert.Single(CanvasHostLaunchTests.Cards(window)));
            else RejectSearch(window);
            var cause = await fixture.ExpectSingleCommandFailureAsync();
            if (unsupportedPayload) Assert.IsType<NotSupportedException>(cause);
            else Assert.IsType<ArgumentException>(cause);
            var closed = 0; window.Closed += (_, _) => closed++;
            window.Close();
            await CanvasHostLaunchTests.UntilAsync(() => closed == 1 && !window.IsVisible);
            var originalClose = Assert.IsAssignableFrom<Task>(window.OriginalCloseTask);
            Assert.True(originalClose.IsFaulted);
            Assert.Same(originalClose, window.CloseAndDrainAsync());
            Assert.Same(cause, window.RecoveredNoEffectCloseFailure);
            Assert.Same(cause, await Record.ExceptionAsync(() => originalClose));
            Assert.True(window.CanCompleteKnownNoEffectClose(originalClose, cause));
            // Same text/cause objects do not turn foreign tasks or new wrappers into receipts.
            var foreign = Task.FromException(cause);
            Assert.False(window.CanCompleteKnownNoEffectClose(foreign, cause));
            Assert.Same(cause, await Record.ExceptionAsync(() => foreign));
            Assert.False(window.CanCompleteKnownNoEffectClose(originalClose, new Exception(cause.Message)));
            Assert.False(window.CanCompleteKnownNoEffectClose(originalClose, new AggregateException(cause)));
            Assert.False(window.CanCompleteKnownNoEffectClose(Task.CompletedTask, cause));
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Unknown_Home_read_failure_after_known_rejection_keeps_normal_close_refused()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        await native.Dispatch<bool>(async () =>
        {
            await using var fixture = await CanvasHostLaunchTests.Fixture.CreateAsync();
            var window = await fixture.ShowAsync();
            RejectSearch(window);
            var first = await fixture.ExpectSingleCommandFailureAsync();
            Assert.IsType<ArgumentException>(first);
            var savedHome = await File.ReadAllBytesAsync(fixture.HomeStatePath);
            Exception unknown;
            try
            {
                // Deliberately corrupt only the fixture's real persisted Home state. The
                // actual next search must refuse it; no mocked readiness or private flags.
                await File.WriteAllTextAsync(fixture.HomeStatePath, "{invalid-home-state");
                Assert.IsType<TextBox>(CanvasHostLaunchTests.Find(window, "canvas-search-query")).Text = "revision";
                CanvasHostLaunchTests.Click(Assert.IsType<Button>(CanvasHostLaunchTests.Find(window, "canvas-search")));
                unknown = await fixture.ExpectSingleCommandFailureAsync();
                Assert.IsType<InvalidDataException>(unknown);
            }
            finally { await File.WriteAllBytesAsync(fixture.HomeStatePath, savedHome); }
            var closed = false; window.Closed += (_, _) => closed = true;
            window.Close();
            await CanvasHostLaunchTests.UntilAsync(() => window.OriginalCloseTask?.IsCompleted == true);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var actual = window.OriginalCloseTask!;
            var failure = await Record.ExceptionAsync(() => actual);
            Assert.NotNull(failure); Assert.True(actual.IsFaulted);
            Assert.True(window.IsVisible); Assert.False(closed);
            Assert.Null(window.RecoveredNoEffectCloseFailure);
            Assert.False(window.CanCompleteKnownNoEffectClose(actual, failure!));
            Assert.Contains(actual.Exception!.Flatten().InnerExceptions, error => ReferenceEquals(error, first));
            Assert.Contains(actual.Exception.Flatten().InnerExceptions, error => ReferenceEquals(error, unknown));
            fixture.ExpectNormalCloseRefusal();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Actual_independent_native_detach_failure_is_not_waived_by_a_known_read_rejection()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        await native.Dispatch<bool>(async () =>
        {
            await using var fixture = await CanvasHostLaunchTests.Fixture.CreateAsync();
            var window = await fixture.ShowAsync();
            RejectSearch(window);
            var originalFailure = await fixture.ExpectSingleCommandFailureAsync();
            var cleanupFailure = new IOException("Actual independent native detach failed.");
            fixture.ExpectExactFailure(cleanupFailure);
            var threw = false;
            CanvasHostLaunchTests.Find(window, "canvas-launch").DetachedFromVisualTree += (_, _) =>
            {
                if (threw) return;
                threw = true; throw cleanupFailure;
            };
            var closed = false; window.Closed += (_, _) => closed = true;
            window.Close();
            await CanvasHostLaunchTests.UntilAsync(() => window.OriginalCloseTask?.IsCompleted == true);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var actual = window.OriginalCloseTask!;
            var failure = await Record.ExceptionAsync(() => actual);
            Assert.True(threw); Assert.NotNull(failure); Assert.True(actual.IsFaulted);
            Assert.Contains(actual.Exception!.Flatten().InnerExceptions, error => ReferenceEquals(error, originalFailure));
            Assert.Contains(actual.Exception.Flatten().InnerExceptions, error => ReferenceEquals(error, cleanupFailure));
            Assert.True(window.IsVisible); Assert.False(closed);
            Assert.Null(window.RecoveredNoEffectCloseFailure);
            Assert.False(window.CanCompleteKnownNoEffectClose(actual, failure!));
            fixture.ExpectNormalCloseRefusal();
            return true;
        }, CancellationToken.None);
    }

    private static void RejectSearch(CanvasHostWindow window)
    {
        Assert.IsType<TextBox>(CanvasHostLaunchTests.Find(window, "canvas-search-query")).Text = new string('x', 257);
        CanvasHostLaunchTests.Click(Assert.IsType<Button>(CanvasHostLaunchTests.Find(window, "canvas-search")));
    }
}
