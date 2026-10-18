using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Spaces.Builder;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class SpaceDetailsCuiTests
{
    [Fact]
    public async Task Authored_inputs_and_buttons_edit_save_and_discard_the_actual_canonical_details()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var space = await files.OpenRegistry().CreateAsync("Original", cancellationToken: token);
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        await session.Dispatch<bool>(async () =>
        {
            var controller = new SpaceDetailsController(files.OpenRegistry(), space.Id);
            Assert.True(await controller.LoadAsync(token));
            var actions = new List<string>();
            var bindings = new SpaceDetailsCuiBindings(() => controller.State,
                (field, value) => Assert.True(controller.SetField(field, value)), async (command, _, cancellation) =>
                {
                    actions.Add(command);
                    var succeeded = command switch
                    {
                        "space-details.save" => await controller.SaveAsync(cancellation),
                        "space-details.discard" => await controller.DiscardChangesAsync(cancellation),
                        "space-details.reload" => await controller.LoadAsync(cancellation),
                        _ => throw new InvalidOperationException("Unexpected test dispatch")
                    };
                    Assert.True(succeeded);
                });
            void Refresh(SpaceDetailsState _)
            {
                if (Dispatcher.UIThread.CheckAccess()) bindings.Refresh();
                else Dispatcher.UIThread.Post(bindings.Refresh);
            }
            controller.StateChanged += Refresh;
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(bindings);
            loader.SetActionDispatcher(bindings);
            Window? window = null;
            try
            {
                var (tree, diagnostics) = loader.TryLoad(SpaceDetailsCuiDocument.Load());
                Assert.Empty(diagnostics);
                var root = Assert.IsAssignableFrom<Control>(tree);
                loader.WireBindings(root);
                window = new Window { Content = root, Width = 900, Height = 800 };
                window.Show();
                Settle(window);
                T Find<T>(string id) where T : Control => root.GetVisualDescendants().OfType<T>()
                    .Single(control => control.Name == id || AutomationProperties.GetAutomationId(control) == id);
                var name = Find<TextBox>("space-details-name");
                var description = Find<TextBox>("space-details-description");
                var icon = Find<TextBox>("space-details-icon");
                var instructions = Find<TextBox>("space-details-instructions");
                var save = Find<Button>("space-details-save");
                Assert.Equal("Original", name.Text);
                Assert.False(save.IsEnabled);
                name.Text = "Rendered name";
                description.Text = "Rendered purpose";
                icon.Text = "book";
                instructions.Text = "First line\nSecond line";
                Settle(window);
                Assert.Equal("Rendered name", controller.State.Draft.Name);
                Assert.Equal("Rendered purpose", controller.State.Draft.Description);
                Assert.Equal("book", controller.State.Draft.IconKey);
                Assert.Equal("First line\nSecond line", controller.State.Draft.Instructions);
                Assert.True(instructions.AcceptsReturn);
                Assert.True(save.IsEnabled);
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync();
                Settle(window);
                var saved = (await files.OpenRegistry().GetAsync(space.Id, token))!;
                Assert.Equal("Rendered name", saved.Name);
                Assert.Equal("Rendered purpose", saved.Description);
                Assert.Equal("book", saved.IconKey);
                Assert.Equal("First line\nSecond line", saved.Instructions);
                Assert.False(controller.State.HasChanges);
                Assert.False(save.IsEnabled);
                name.Text = "Unsaved title";
                Settle(window);
                var discard = Find<Button>("space-details-discard");
                Assert.True(discard.IsEnabled);
                discard.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync();
                Settle(window);
                Assert.Equal("Rendered name", name.Text);
                Assert.False(controller.State.HasChanges);
                Assert.Equal(["space-details.save", "space-details.discard"], actions);
                Assert.False(bindings.TrySetValue("details.permission", "grant"));
                await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync("space-details.delete", null, token).AsTask());
            }
            finally
            {
                controller.StateChanged -= Refresh;
                window?.Close();
                loader.Dispose();
                await loader.WhenActionsIdleAsync();
                Dispatcher.UIThread.RunJobs();
            }
            return true;
        }, token);
    }

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
        }
    }
}
