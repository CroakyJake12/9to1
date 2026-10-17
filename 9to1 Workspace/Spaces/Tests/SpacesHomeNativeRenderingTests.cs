using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Spaces.Home;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

/// <summary>
/// Full-window render and interaction tests for <c>SpacesHome.cui</c>. These run the real
/// primitive CUI theme and use pointer/keyboard input against the actual controller so the tests
/// exercise the product surface, not just the visual tree.
/// </summary>
public sealed class SpacesHomeNativeRenderingTests
{
    private static string ReadAuthoredSource()
    {
        var assembly = typeof(SpacesHomeCuiBindings).Assembly;
        using var stream = assembly.GetManifestResourceStream(SpacesHomeCuiDocument.ResourceName)
            ?? throw new InvalidOperationException($"'{SpacesHomeCuiDocument.ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static (SpacesHomeController Controller, SpacesHomeCuiBindings Bindings, CuiControlLoader Loader, Control Root) LoadInWindow()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var controller = new SpacesHomeController(registry);
        var bindings = new SpacesHomeCuiBindings(
            () => controller.State,
            async (command, parameter, cancellationToken) =>
            {
                var id = parameter is Guid g ? g : controller.State.Selection?.Id ?? Guid.Empty;
                switch (command)
                {
                    case "spaces.create": await controller.CreateCustomSpaceAsync(cancellationToken); break;
                    case "spaces.open": await controller.OpenSpaceAsync(id, cancellationToken); break;
                    case "spaces.rename": controller.StartRename(id); break;
                    case "spaces.move": await controller.MoveSpaceAsync(id, cancellationToken); break;
                    case "spaces.fork": await controller.ForkSpaceAsync(id, cancellationToken); break;
                    case "spaces.archive": await controller.ArchiveSpaceAsync(id, cancellationToken); break;
                    case "spaces.add-context": await controller.AddContextAsync(id, null, cancellationToken); break;
                    case "spaces.clear-search": controller.SetQuery(string.Empty); break;
                    case "spaces.set-query": controller.SetQuery(parameter?.ToString()); break;
                    case "spaces.confirm-rename": await controller.ConfirmRenameAsync(cancellationToken); break;
                    case "spaces.cancel-rename": controller.CancelRename(); break;
                    case "spaces.select-move-parent": controller.SelectMoveParentIndex((int)parameter!); break;
                    default: throw new InvalidOperationException($"Unknown command {command}");
                }
            });

        controller.StateChanged += _ => bindings.Refresh();

        var loader = new CuiControlLoader();
        loader.SetBindingContext(bindings);
        loader.SetActionDispatcher(bindings);
        var (tree, diagnostics) = loader.LoadMarkup(ReadAuthoredSource());
        Assert.Empty(diagnostics);
        var root = Assert.IsAssignableFrom<Control>(tree);
        loader.WireBindings(root);
        return (controller, bindings, loader, root);
    }

    private static Button FindButton(Control root, string name) =>
        root.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);

    private static TextBox FindTextBox(Control root, string name) =>
        root.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == name);

    private static async Task ClickAsync(Window window, CuiControlLoader loader, Button button)
    {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
        Assert.NotNull(point);
        window.MouseMove(point.Value);
        window.MouseDown(point.Value, MouseButton.Left);
        window.MouseUp(point.Value, MouseButton.Left);
        await loader.WhenActionsIdleAsync();
        Dispatcher.UIThread.RunJobs();
    }

    [Theory]
    [InlineData(390, 844, CuiAppearance.SuperBright)]
    [InlineData(390, 844, CuiAppearance.Bright)]
    [InlineData(390, 844, CuiAppearance.Dark)]
    [InlineData(390, 844, CuiAppearance.SuperDark)]
    [InlineData(1280, 900, CuiAppearance.SuperBright)]
    [InlineData(1280, 900, CuiAppearance.Bright)]
    [InlineData(1280, 900, CuiAppearance.Dark)]
    [InlineData(1280, 900, CuiAppearance.SuperDark)]
    public async Task Authored_spaces_home_renders_at_narrow_and_desktop_sizes(
        int width, int height, CuiAppearance appearance)
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        await session.Dispatch<bool>(async () =>
        {
            CuiNativeHost.InitialisePrimitiveTheme(Application.Current!, "Spaces", appearance);

            var (controller, _, loader, root) = LoadInWindow();
            await controller.LoadAsync();
            Dispatcher.UIThread.RunJobs();

            var window = new Window { Width = width, Height = height, Content = root };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal(width, frame.PixelSize.Width);
                Assert.Equal(height, frame.PixelSize.Height);

                var captures = Environment.GetEnvironmentVariable("HAVEN_SPACES_HOME_CAPTURES");
                if (!string.IsNullOrEmpty(captures))
                {
                    Directory.CreateDirectory(captures);
                    frame.Save(Path.Combine(captures, $"spaces-home-{width}x{height}-{appearance}.png"));
                }

                var texts = root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
                var rendered = string.Join(" | ", texts);
                Assert.Contains("Spaces", rendered);
                Assert.Contains("Chat", rendered);
                Assert.Contains("Your Spaces", rendered);

                var createButton = FindButton(root, "create-space");
                Assert.True(createButton.Bounds.Width >= 44 && createButton.Bounds.Height >= 24,
                    "Create Space button must remain a usable touch target.");
            }
            finally
            {
                loader.Dispose();
                window.Close();
            }
            return true;
        }, lifetime.Token);
    }

    [Fact]
    public async Task Clicking_create_space_button_creates_a_custom_space()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        await session.Dispatch<bool>(async () =>
        {
            var (controller, _, loader, root) = LoadInWindow();
            await controller.LoadAsync();
            Dispatcher.UIThread.RunJobs();

            var window = new Window { Width = 1280, Height = 900, Content = root };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var createButton = FindButton(root, "create-space");
                await ClickAsync(window, loader, createButton);

                Assert.Single(controller.State.CustomSpaces);
                Assert.Equal("New Space", controller.State.CustomSpaces[0].Name);
                Assert.True(controller.State.Selection?.IsRenaming);
            }
            finally
            {
                loader.Dispose();
                window.Close();
            }
            return true;
        }, lifetime.Token);
    }

    [Fact]
    public async Task Typing_in_search_box_filters_the_space_list()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        await session.Dispatch<bool>(async () =>
        {
            var registry = new SpaceRegistry(new MemorySettingsStore());
            var alpha = await registry.CreateAsync("Alpha recipes");
            var beta = await registry.CreateAsync("Beta notes");
            var controller = new SpacesHomeController(registry);
            var bindings = new SpacesHomeCuiBindings(
                () => controller.State,
                (command, parameter, _) =>
                {
                    if (command == "spaces.set-query")
                        controller.SetQuery(parameter?.ToString());
                    return ValueTask.CompletedTask;
                });
            controller.StateChanged += _ => bindings.Refresh();

            await controller.LoadAsync();
            Dispatcher.UIThread.RunJobs();

            using var loader = new CuiControlLoader();
            loader.SetBindingContext(bindings);
            loader.SetActionDispatcher(bindings);
            var (tree, diagnostics) = loader.LoadMarkup(ReadAuthoredSource());
            Assert.Empty(diagnostics);
            var root = Assert.IsAssignableFrom<Control>(tree);
            loader.WireBindings(root);

            var window = new Window { Width = 1280, Height = 900, Content = root };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var search = FindTextBox(root, "spaces-search");
                search.Focus();
                search.SelectAll();
                window.KeyTextInput("beta");
                await loader.WhenActionsIdleAsync();
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("beta", controller.State.Query);
                Assert.Single(controller.State.CustomSpaces);
                Assert.Equal(beta.Id, controller.State.CustomSpaces[0].Id);
            }
            finally
            {
                loader.Dispose();
                window.Close();
            }
            return true;
        }, lifetime.Token);
    }

    [Fact]
    public async Task Built_in_extension_editor_button_is_disabled_without_host()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        await session.Dispatch<bool>(async () =>
        {
            var (controller, _, loader, root) = LoadInWindow();
            await controller.LoadAsync();
            await controller.SelectSpaceAsync(SpaceRegistry.StudySpaceId);
            Dispatcher.UIThread.RunJobs();

            var window = new Window { Width = 1280, Height = 900, Content = root };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var editButton = FindButton(root, "edit-extensions");
                Assert.False(editButton.IsEnabled);
            }
            finally
            {
                loader.Dispose();
                window.Close();
            }
            return true;
        }, lifetime.Token);
    }
}
