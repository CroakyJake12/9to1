using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using CakeOS.Cui.Runtime;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(HavenOS.Apps.Browse.Tests.BrowseWindowsTestAppBuilder))]
namespace HavenOS.Apps.Browse.Tests;

public static class BrowseWindowsTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<Application>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }));
}

// Actual setup/CUI retirement controls, with explicit test-owned raw drivers.
// These do not certify installed Home or the Windows WebView backend.
public sealed class BrowseStandaloneSetupTests
{
    private static readonly List<(BrowseStartupWindow Window, Task? Source, Exception? Failure)> RetainedOwners = [];
    [AvaloniaFact]
    public async Task Direct_launch_reports_missing_Home_in_actual_CUI_and_retires_original_scene()
    {
        var window = new BrowseStartupWindow(null); window.Show();
        try
        {
            var original = window.InitializeAsync(); Assert.Same(original, window.InitializeAsync());
            var availability = await Observe(original, window);
            Assert.Equal(CuiSceneAvailabilityState.Unavailable, availability.State);
            Assert.Equal("HomeStartupAttachmentUnavailable", availability.Code);
            Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text?.Contains("Open Browse from Home", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Button>(), button => button.Name == "BrowseGoButton");
            window.Close(); var close = window.OriginalClose; Assert.NotNull(close);
            Assert.True(await Observe(close!, window)); Assert.Same(close, window.OriginalClose);
            Assert.False(window.IsVisible);
        }
        catch (Exception failure) { RetainedOwners.Add((window, window.OriginalClose, failure)); throw; }
    }
    [AvaloniaFact]
    public async Task Actual_setup_close_joins_same_admitted_driver_and_reuses_failed_close()
    {
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sourceOwner = new object(); var window = new BrowseStartupWindow(null);
        window.BindOriginalTransition(raw.Task, () => OriginalNativeInvocation.DemandExternalJoin(sourceOwner));
        window.Show(); await Observe(window.InitializeAsync(), window);
        window.Close(); var close = window.OriginalClose; Assert.NotNull(close);
        Assert.False(close!.IsCompleted); Assert.True(window.IsVisible);
        var failure = new InvalidOperationException("Actual original startup fixture driver failed.");
        raw.SetException(failure);
        var terminal = await Assert.ThrowsAsync<AggregateException>(() => Observe(close, window));
        Assert.Contains(failure, terminal.Flatten().InnerExceptions);
        window.Close(); Assert.Same(close, window.OriginalClose); Assert.True(window.IsVisible);
        RetainedOwners.Add((window, raw.Task, terminal));
    }
    private static async Task<T> Observe<T>(Task<T> source, BrowseStartupWindow window)
    {
        var completed = await Task.WhenAny(source, Task.Delay(TimeSpan.FromSeconds(20)));
        if (!ReferenceEquals(completed, source))
        {
            var failure = new TimeoutException("The SAME standalone Browse source remains pending; original window/source retained.");
            RetainedOwners.Add((window, source, failure)); throw failure;
        }
        return await source;
    }
}
