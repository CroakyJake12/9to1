using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CakeOS.Cui.Runtime;
using Xunit;

namespace HavenOS.Images.Tests;

// Local observation of the SAME loader which owns the actual rendered CUI.
// This helper grants no product readiness, dispatches no substitute action,
// and never treats a scheduled callback as synchronous app command admission.
internal static class PictureCuiActionFixture
{
    internal static async Task ClickAsync(MainWindow window, Button button)
    {
        Assert.True(button.IsEnabled);
        var loader = OriginalLoader(window);
        var observation = Assert.IsType<CuiControlDiagnostics>(loader.Inspect(button));
        Assert.True(observation.DispatcherConnected);
        Assert.True(observation.ActionsWired);
        var previous = window.OriginalCommand;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        // Capture after the real click: admission has already retained its
        // gated pipeline, even when the UI continuation has yet to run.
        var originalPipeline = loader.WhenActionsIdleAsync();
        await originalPipeline.ObserveOriginalAsync(window, "Picture CUI pipeline: " + button.Name);
        Assert.True(originalPipeline.IsCompletedSuccessfully);
        Assert.Same(loader, OriginalLoader(window));
        Assert.NotNull(window.OriginalCommand);
        var originalCommand = window.OriginalCommand!;
        Assert.NotSame(previous, originalCommand);
        await originalCommand.ObserveOriginalAsync(window, "Picture admitted command: " + button.Name);
        Assert.True(originalCommand.IsCompletedSuccessfully);
        Assert.Same(originalCommand, window.OriginalCommand);
    }

    private static CuiControlLoader OriginalLoader(MainWindow window) =>
        Assert.IsType<CuiControlLoader>(typeof(CuiSceneHost)
            .GetField("_contentLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window.SceneHost));
}
