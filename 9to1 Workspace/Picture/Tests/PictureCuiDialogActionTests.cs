using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CakeOS.Cui.Runtime;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureCuiDialogActionTests
{
    private static readonly List<(object Dialog, Window Owner, Exception Failure)> RetainedFailedOwners = [];

    [AvaloniaTheory]
    [InlineData("PictureSaveConfirmation", "picture.confirm.save", "save")]
    [InlineData("PictureSaveConfirmation", "picture.confirm.discard", "discard")]
    [InlineData("PictureSaveConfirmation", "picture.confirm.cancel", "cancel")]
    [InlineData("PictureMetadata", "picture.metadata.close", "close")]
    public async Task Original_authored_dialog_buttons_are_wired_and_return_the_actual_native_modal_result(
        string document, string action, string expected)
    {
        var owner = new Window();
        owner.Show();
        var model = new CuiViewModel();
        foreach (var key in new[] { "FileFacts", "EmbeddedMetadata", "MetadataNotice" }) model.Set(key, "Local modal control fixture");
        var type = typeof(MainWindow).Assembly.GetType("HavenOS.Images.PictureCuiDialog", throwOnError: true)!;
        var dialog = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null,
            ["Original Picture modal fixture", document, new PictureFixtureReadiness(), model], null)!;
        var window = Assert.IsType<Window>(type.GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog));
        var scene = Assert.IsType<CuiSceneHost>(type.GetField("_scene", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Opened += (_, _) => entered.TrySetResult();
        var originalOpen = Assert.IsAssignableFrom<Task<string?>>(type.GetMethod("OpenAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dialog, [owner]));
        PictureOriginalSourceFixture.RetainOriginal(dialog, originalOpen, "Picture original modal driver");
        PictureOriginalSourceFixture.RetainOriginal(dialog, entered.Task, "Picture original Opened observation");
        Exception? firstFailure = null;
        try
        {
            var first = await Task.WhenAny(entered.Task, originalOpen).ObserveOriginalAsync(dialog, "Picture native modal entry");
            if (ReferenceEquals(first, originalOpen)) await originalOpen;
            await entered.Task.ObserveOriginalAsync(dialog, "Picture native modal Opened event");
            var loader = Assert.IsType<CuiControlLoader>(typeof(CuiSceneHost)
                .GetField("_contentLoader", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene));
            var buttons = window.GetLogicalDescendants().OfType<Button>().ToArray();
            Assert.NotEmpty(buttons);
            foreach (var originalButton in buttons)
            {
                var observed = Assert.IsType<CuiControlDiagnostics>(loader.Inspect(originalButton));
                Assert.True(observed.DispatcherConnected);
                Assert.True(observed.ActionsWired);
            }
            var button = buttons.Single(button => Equals(button.Tag, action));
            Assert.True(button.IsEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var originalPipeline = loader.WhenActionsIdleAsync();
            PictureOriginalSourceFixture.RetainOriginal(dialog, originalPipeline, "Picture original modal action pipeline");
            await Task.WhenAll(originalOpen, originalPipeline).ObserveOriginalAsync(dialog, "Picture modal result and original CUI action pipeline");
            Assert.Equal(expected, await originalOpen);
            Assert.True(originalOpen.IsCompletedSuccessfully);
            Assert.True(originalPipeline.IsCompletedSuccessfully);
            Assert.NotNull(scene.OriginalClose);
            Assert.True(scene.OriginalClose!.IsCompletedSuccessfully);
            Assert.False(window.IsVisible);
        }
        catch (Exception error)
        {
            firstFailure = error;
            RetainedFailedOwners.Add((dialog, owner, error));
            throw;
        }
        finally
        {
            if (firstFailure is null) { ((IDisposable)dialog).Dispose(); owner.Close(); }
        }
    }
}
