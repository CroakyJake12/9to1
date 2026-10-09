using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

public sealed class AssistantsRichMarkdownTests
{
    [Fact]
    public async Task Shared_renderer_follows_its_current_local_theme_instead_of_application_global_brushes()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        Assert.True(await native.Dispatch<bool>(async () =>
        {
            var first = new SolidColorBrush(Colors.Red); // Deliberately distinct fixture resources.
            var second = new SolidColorBrush(Colors.Blue);
            var view = new CuiMarkdownView { Text = "```text\nFixture code\n```" };
            var window = new Window { Width = 800, Height = 600, Content = view };
            window.Resources["HavenPanel3Brush"] = first;
            try
            {
                window.Show(); window.UpdateLayout();
                var surface = Assert.Single(view.GetVisualDescendants().OfType<Border>(), value =>
                    value.Child is StackPanel stack && stack.Children.OfType<TextBox>().Any(text => text.Text == "Fixture code"));
                Assert.Same(first, surface.Background);
                window.Resources["HavenPanel3Brush"] = second;
                await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout).GetTask();
                Assert.Same(second, surface.Background);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Only_this_render_generation_issues_code_requests_and_old_buttons_cannot_replay_them()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        Assert.True(await native.Dispatch<bool>(() =>
        {
            var view = new CuiMarkdownView { Text = "```text\nFirst fixture\n```" };
            var other = new CuiMarkdownView { Text = view.Text };
            var requests = new List<CuiMarkdownCodeActionRequest>(); view.CodeActionRequested += requests.Add;
            var window = new Window { Width = 800, Height = 600, Content = view };
            try
            {
                window.Show(); window.UpdateLayout();
                var old = Assert.Single(view.GetVisualDescendants().OfType<Button>(), value => Equals(value.Content, "Ask to run"));
                old.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var request = Assert.Single(requests);
                Assert.True(view.IsCurrentOriginalCodeAction(request)); Assert.False(other.IsCurrentOriginalCodeAction(request));
                Assert.Equal("First fixture", request.Code);
                view.Text = "```text\nSecond fixture\n```"; window.UpdateLayout();
                Assert.False(view.IsCurrentOriginalCodeAction(request));
                old.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Single(requests);
                var current = Assert.Single(view.GetVisualDescendants().OfType<Button>(), value => Equals(value.Content, "Ask to apply"));
                current.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(2, requests.Count); Assert.Equal("Second fixture", requests[1].Code);
                Assert.True(view.IsCurrentOriginalCodeAction(requests[1]));
            }
            finally { window.Close(); }
            return Task.FromResult(true);
        }, CancellationToken.None));
    }
}
