using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
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

    private static (SpacesHomeController Controller, SpacesHomeCuiBindings Bindings, CuiControlLoader Loader, Control Root) LoadInWindow(SpaceRegistry? originalRegistry = null, Action<string, object?>? observe = null)
    {
        var registry = originalRegistry ?? new SpaceRegistry(new MemorySettingsStore());
        var controller = new SpacesHomeController(registry);
        var bindings = new SpacesHomeCuiBindings(
            () => controller.State,
            async (command, parameter, cancellationToken) =>
            {
                observe?.Invoke(command, parameter);
                var id = parameter is Guid g ? g : controller.State.Selection?.Id ?? Guid.Empty;
                switch (command)
                {
                    case "spaces.create": await controller.CreateCustomSpaceAsync(cancellationToken); break;
                    case "spaces.open": await controller.OpenSpaceAsync(id, cancellationToken); break;
                    case "spaces.select": await controller.SelectSpaceAsync(id, cancellationToken); break;
                    case "spaces.set-rename-draft": controller.SetRenameDraft(parameter?.ToString()); break;
                    case "spaces.rename": await controller.StartRenameAsync(id, cancellationToken); break;
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
        window.UpdateLayout(); button.BringIntoView(); window.UpdateLayout();
        Assert.True(button.IsEnabled); Assert.True(button.IsEffectivelyVisible);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
        Assert.NotNull(point);
        Assert.True(new Rect(window.ClientSize).Contains(point.Value), "Pointer target is outside the window.");
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point.Value));
        Assert.True(ReferenceEquals(hit, button) || hit.GetVisualAncestors().Contains(button),
            "The requested button is clipped or covered at the pointer target.");
        window.MouseMove(point.Value);
        window.MouseDown(point.Value, MouseButton.Left);
        window.MouseUp(point.Value, MouseButton.Left);
        await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
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
    public async Task Pointer_row_actions_select_and_rename_the_actual_row_identity_not_the_previous_selection()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        await session.Dispatch<bool>(async () =>
        {
            var registry = new SpaceRegistry(new MemorySettingsStore());
            var alpha = await registry.CreateAsync("Alpha");
            var beta = await registry.CreateAsync("Beta");
            var calls = new List<(string Command, object? Parameter)>();
            var (controller, _, loader, root) = LoadInWindow(registry, (command, value) => calls.Add((command, value)));
            await controller.LoadAsync();
            var window = new Window { Width = 1280, Height = 1100, Content = root };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                await ClickAsync(window, loader, Row("open-custom-space", "Open Alpha"));
                Assert.Equal(alpha.Id, controller.State.Selection!.Id);
                Assert.Equal(alpha.Id, await registry.GetCurrentSpaceIdAsync());
                await ClickAsync(window, loader, Row("open-custom-space", "Open Beta"));
                Assert.Equal(beta.Id, controller.State.Selection!.Id);
                Assert.Equal(beta.Id, Assert.IsType<Guid>(calls.Last(item => item.Command == "spaces.select").Parameter));
                Assert.Equal(beta.Id, await registry.GetCurrentSpaceIdAsync());

                // The row being renamed deliberately differs from the current selection.
                await ClickAsync(window, loader, Row("rename-custom-space", "Rename Alpha"));
                Assert.Equal(alpha.Id, Assert.IsType<Guid>(calls.Last(item => item.Command == "spaces.rename").Parameter));
                Assert.Equal(alpha.Id, controller.State.PendingRenameSpaceId);
                Assert.Equal(alpha.Id, controller.State.Selection!.Id);
                Assert.True(controller.State.Selection.IsRenaming);
                Assert.Equal(alpha.Id, await registry.GetCurrentSpaceIdAsync());
                await ClickAsync(window, loader, FindButton(root, "cancel-rename"));
                Assert.Null(controller.State.PendingRenameSpaceId);
                Assert.False(controller.State.Selection.IsRenaming);
                Assert.DoesNotContain(root.GetVisualDescendants().OfType<TextBox>(), item => item.Name == "rename-input");
                Assert.Equal("Alpha", (await registry.GetAsync(alpha.Id))!.Name);
                await ClickAsync(window, loader, Row("rename-custom-space", "Rename Alpha"));
                var rename = FindTextBox(root, "rename-input");
                rename.Focus(); rename.SelectAll(); window.KeyTextInput("Alpha renamed");
                await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
                await ClickAsync(window, loader, FindButton(root, "confirm-rename"));
                Assert.Equal(alpha.Id, Assert.IsType<Guid>(calls.Last(item => item.Command == "spaces.confirm-rename").Parameter));
                Assert.Equal("Alpha renamed", (await registry.GetAsync(alpha.Id))!.Name);
                Assert.Equal("Beta", (await registry.GetAsync(beta.Id))!.Name);
                Assert.Null(controller.State.PendingRenameSpaceId);
                Assert.DoesNotContain(root.GetVisualDescendants().OfType<TextBox>(), item => item.Name == "rename-input");
                Assert.Equal("Open Alpha renamed", AutomationProperties.GetName(Row("open-custom-space", "Open Alpha renamed")));

                await ClickAsync(window, loader, Row("archive-custom-space", "Archive Beta"));
                Assert.Equal(beta.Id, Assert.IsType<Guid>(calls.Last(item => item.Command == "spaces.archive").Parameter));
                Assert.True((await registry.GetAsync(beta.Id))!.IsArchived);
                Assert.False((await registry.GetAsync(alpha.Id))!.IsArchived);
                await ClickAsync(window, loader, Row("first-party-space-open", "Open Chat"));
                Assert.Equal(SpaceRegistry.ChatSpaceId, controller.State.Selection!.Id);
                Assert.Equal(SpaceRegistry.ChatSpaceId, await registry.GetCurrentSpaceIdAsync());
            }
            finally { loader.Dispose(); window.Close(); }
            return true;

            Button Row(string id, string label) => root.GetVisualDescendants().OfType<Button>()
                .Single(item => item.Name == id && AutomationProperties.GetName(item) == label);
        }, lifetime.Token);
    }

    [Theory]
    [InlineData(1440, 920)]
    [InlineData(1280, 420)]
    public async Task Bounded_panes_scroll_independently_and_expose_custom_rows_to_real_pointer_input(int width, int height)
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        await session.Dispatch<bool>(async () =>
        {
            var registry = new SpaceRegistry(new MemorySettingsStore());
            var spaces = new List<SpaceDefinition>();
            for (var index = 1; index <= 8; index++)
                spaces.Add(await registry.CreateAsync($"Viewport Space {index}"));
            await registry.SetCurrentSpaceIdAsync(spaces[0].Id);
            var target = spaces[^1];
            var (controller, _, loader, root) = LoadInWindow(registry);
            await controller.LoadAsync();
            var window = new Window { Width = width, Height = height, Content = root };
            try
            {
                window.Show(); Layout();
                var sidebar = Pane("spaces-sidebar");
                var details = Pane("space-details");
                var sidebarScroll = Assert.IsType<ScrollViewer>(sidebar.Child);
                var detailsScroll = Assert.IsType<ScrollViewer>(details.Child);
                var sideBounds = BoundsInWindow(sidebar);
                var detailBounds = BoundsInWindow(details);
                Assert.InRange(sideBounds.Height, height - 40, height);
                Assert.InRange(detailBounds.Height, height - 40, height);
                Assert.True(sideBounds.Right <= detailBounds.Left);
                Assert.True(detailBounds.Right <= width + .5 && detailBounds.Bottom <= height + .5);
                Assert.True(sidebarScroll.Extent.Height > sidebarScroll.Viewport.Height);
                Assert.True(sidebarScroll.Extent.Width <= sidebarScroll.Viewport.Width + .5);
                Assert.True(detailsScroll.Extent.Width <= detailsScroll.Viewport.Width + .5);
                var row = root.GetVisualDescendants().OfType<Button>().Single(item =>
                    item.Name == "open-custom-space" && AutomationProperties.GetName(item) == $"Open {target.Name}");
                Assert.True(BoundsInWindow(row).Top >= sideBounds.Bottom, "Fixture must start with the target row offscreen.");
                var detailOffset = detailsScroll.Offset;
                var sideWheelPoint = sideBounds.Center;
                var wheelTrace = new List<string>();
                for (var attempt = 0; attempt < 80 && !sideBounds.Contains(BoundsInWindow(row)); attempt++)
                {
                    if (attempt < 12) wheelTrace.Add($"{sidebarScroll.Offset.Y}:{window.InputHitTest(sideWheelPoint)?.GetType().Name}:{(window.InputHitTest(sideWheelPoint) as Control)?.Name}");
                    window.MouseWheel(sideWheelPoint, new Vector(0, -3));
                    Layout();
                }
                Assert.True(sidebarScroll.Offset.Y > 0);
                Assert.True(sideBounds.Contains(BoundsInWindow(row)), $"Wheel scrolling must expose the complete target row. Pane={sideBounds}; row={BoundsInWindow(row)}; offset={sidebarScroll.Offset}; viewport={sidebarScroll.Viewport}; extent={sidebarScroll.Extent}; trace={string.Join("|", wheelTrace)}.");
                Assert.Equal(detailOffset, detailsScroll.Offset);
                Assert.Equal(detailBounds, BoundsInWindow(details));
                if (height <= 520)
                {
                    Assert.True(detailsScroll.Extent.Height > detailsScroll.Viewport.Height);
                    var sideOffset = sidebarScroll.Offset;
                    window.MouseWheel(detailBounds.Center, new Vector(0, -20)); Layout();
                    Assert.True(detailsScroll.Offset.Y > 0);
                    Assert.Equal(sideOffset, sidebarScroll.Offset);
                }
                // Click only after real wheel input made the row visible; ClickAsync also
                // checks the window hit test, so an offscreen accessibility action cannot pass.
                await ClickAsync(window, loader, row);
                Assert.Equal(target.Id, controller.State.Selection!.Id);
                Assert.Equal(target.Id, await registry.GetCurrentSpaceIdAsync());
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                var captures = Environment.GetEnvironmentVariable("HAVEN_SPACES_HOME_CAPTURES");
                if (!string.IsNullOrEmpty(captures))
                {
                    Directory.CreateDirectory(captures);
                    frame.Save(Path.Combine(captures, $"spaces-scrolled-{width}x{height}.png"));
                }
            }
            finally { loader.Dispose(); window.Close(); }
            return true;

            CuiContainerControl Pane(string id) => root.GetVisualDescendants().OfType<CuiContainerControl>()
                .Single(item => item.Name == id);
            Rect BoundsInWindow(Control control) => new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
            void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
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
