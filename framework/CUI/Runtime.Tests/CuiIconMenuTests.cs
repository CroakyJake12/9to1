using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiIconMenuTests
{
    [Fact]
    public async Task Closing_context_menu_restores_typing_to_owner_without_stealing_new_focus()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <Input Type="Text" Value="before">
                    <Container Type="Context Menu"><Button Type="Menu Item" Content="Copy" /></Container>
                  </Input>
                  <Input Type="Text" Value="other" />
                </Container>
                """);
            Assert.Empty(diagnostics);
            var window = new Window { Width = 500, Height = 400, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var inputs = root!.GetVisualDescendants().OfType<TextBox>().ToArray();
                var owner = inputs[0]; var other = inputs[1];
                owner.Focus(); owner.CaretIndex = owner.Text!.Length;
                var menu = owner.ContextMenu!; menu.Open(owner); Dispatcher.UIThread.RunJobs();
                Assert.IsType<CuiMenuItem>(Assert.Single(menu.Items)).Focus();
                menu.Close(); Dispatcher.UIThread.RunJobs();
                Assert.True(owner.IsFocused);
                window.KeyTextInput(" after");
                Assert.Equal("before after", owner.Text);
                menu.Open(owner); Dispatcher.UIThread.RunJobs();
                menu.Close(); other.Focus(); Dispatcher.UIThread.RunJobs();
                Assert.True(other.IsFocused);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Gallery_uses_named_image_choices_and_grid_keyboard_navigation()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var registry = new CuiControlRegistry();
            var picture = new DrawingImage(new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0,0,100,100)), Pen = new Pen(Brushes.Black, 4) });
            registry.RegisterImageSourceResolver((_, _) => picture);
            using var loader = new CuiControlLoader(registry);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Menu">
                  <Button Type="Menu Item" Content="Shapes" MenuColumns="3">
                    <Button Type="Menu Item" Content="Rectangle" IconSource="shape:rect" IconOnly="true" ToggleType="CheckBox" Checked="True" Width="56" Height="56" />
                    <Button Type="Menu Item" Content="Unavailable" IconSource="shape:rect" IconOnly="true" Interactive="false" Width="56" Height="56" />
                    <Button Type="Menu Item" Content="Ellipse" IconSource="shape:ellipse" IconOnly="true" Width="56" Height="56" />
                    <Button Type="Menu Item" Content="Triangle" IconSource="shape:triangle" IconOnly="true" Width="56" Height="56" />
                    <Button Type="Menu Item" Content="Polygon" IconSource="shape:triangle" IconOnly="true" Width="56" Height="56" />
                  </Button>
                </Container>
                """);
            Assert.Empty(diagnostics);
            var window = new Window { Width = 500, Height = 400, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var menu = Assert.Single(root!.GetVisualDescendants().OfType<CuiMenuItem>());
                menu.IsSubMenuOpen = true; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var choices = menu.Items.OfType<CuiMenuItem>().ToArray();
                Assert.Equal(3, menu.MenuColumns);
                Assert.Equal(choices[0].Bounds.Y, choices[2].Bounds.Y);
                Assert.True(choices[3].Bounds.Y > choices[0].Bounds.Y);
                Assert.Equal("Rectangle", ControlAutomationPeer.CreatePeerForElement(choices[0])!.GetName());
                Assert.True(Assert.Single(choices[0].GetVisualDescendants().OfType<Image>()).IsVisible);
                Assert.Contains(choices[0].GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "✓" && text.IsVisible);
                // A short final row and disabled cells must not strand keyboard
                // navigation when moving vertically through the gallery.
                CuiLifecycleProperties.SetLiteral(choices[4], "Interactive", "false", CuiPropertyLevel.Instance, "disabled gallery choice");
                choices[2].Focus();
                window.KeyPress(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                window.KeyRelease(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                Assert.True(choices[3].IsFocused);
                choices[0].Focus();
                window.KeyPress(Avalonia.Input.Key.Right, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null); window.KeyRelease(Avalonia.Input.Key.Right, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                Assert.True(choices[2].IsFocused);
                window.KeyPress(Avalonia.Input.Key.Home, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null); window.KeyRelease(Avalonia.Input.Key.Home, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                window.KeyPress(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null); window.KeyRelease(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                Assert.True(choices[3].IsFocused);
                // If the currently focused command becomes unavailable, focus
                // transfers to the nearest live gallery choice rather than
                // remaining on a disabled control.
                choices[2].Focus();
                CuiLifecycleProperties.SetLiteral(choices[2], "Interactive", "false", CuiPropertyLevel.Instance, "focused choice became unavailable");
                Dispatcher.UIThread.RunJobs();
                Assert.True(choices[3].IsFocused);
                CuiLifecycleProperties.SetLiteral(choices[2], "Interactive", "true", CuiPropertyLevel.Instance, "restore gallery choice");
                choices[2].Focus();
                CuiLifecycleProperties.SetLiteral(choices[2], "Interactive", "false", CuiPropertyLevel.Instance, "focused choice became unavailable");
                choices[0].Focus();
                Dispatcher.UIThread.RunJobs();
                Assert.True(choices[0].IsFocused);
                window.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null); window.KeyRelease(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                Assert.False(menu.IsSubMenuOpen);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Gallery_keyboard_navigation_preserves_non_menu_item_grid_slots()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Menu">
                  <Button Type="Menu Item" Content="Shapes" MenuColumns="2">
                    <Button Type="Menu Item" Content="Rectangle" />
                    <Button Type="Menu Item" Content="Ellipse" />
                    <Button Type="Menu Item" Content="Triangle" />
                  </Button>
                </Container>
                """);
            Assert.Empty(diagnostics);
            var window = new Window { Width = 400, Height = 240, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var owner = Assert.Single(root!.GetVisualDescendants().OfType<CuiMenuItem>());
                owner.IsSubMenuOpen = true;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                // The renderer may insert its registered CUI-owned separator as a real menu item slot.
                owner.Items.Insert(1, new Separator());
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal(4, owner.Items.Count);
                var commands = owner.Items.OfType<CuiMenuItem>().ToArray();
                Assert.True(commands[1].Bounds.Y > commands[0].Bounds.Y);
                Assert.True(commands[0].Focus());
                window.KeyPress(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                window.KeyRelease(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                Assert.True(commands[1].IsFocused); // Item index 2 is directly below index 0.
                commands[0].Focus();
                window.KeyPress(Avalonia.Input.Key.Right, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                window.KeyRelease(Avalonia.Input.Key.Right, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                Assert.True(commands[1].IsFocused); // Horizontal navigation skips the divider slot.
                window.KeyPress(Avalonia.Input.Key.End, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                window.KeyRelease(Avalonia.Input.Key.End, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                Assert.True(commands[2].IsFocused);
                window.KeyPress(Avalonia.Input.Key.Home, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                window.KeyRelease(Avalonia.Input.Key.Home, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                Assert.True(commands[0].IsFocused);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Gallery_keyboard_navigation_skips_enabled_but_nonfocusable_choices()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Menu">
                  <Button Type="Menu Item" Content="Shapes" MenuColumns="2">
                    <Button Type="Menu Item" Content="Decorative" Focusable="False" />
                    <Button Type="Menu Item" Content="Rectangle" />
                    <Button Type="Menu Item" Content="Ellipse" />
                  </Button>
                </Container>
                """);
            Assert.Empty(diagnostics);
            var window = new Window { Width = 400, Height = 240, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var owner = Assert.Single(root!.GetVisualDescendants().OfType<CuiMenuItem>());
                owner.IsSubMenuOpen = true;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var choices = owner.Items.OfType<CuiMenuItem>().ToArray();
                Assert.False(choices[0].Focusable);
                Assert.True(choices[2].Focus());
                window.KeyPress(Avalonia.Input.Key.Up, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                window.KeyRelease(Avalonia.Input.Key.Up, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                Assert.True(choices[1].IsFocused);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Gallery_vertical_navigation_skips_an_entire_disabled_row_without_wrapping()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Menu">
                  <Button Type="Menu Item" Content="Shapes" MenuColumns="2">
                    <Button Type="Menu Item" Content="One" />
                    <Button Type="Menu Item" Content="Two" />
                    <Button Type="Menu Item" Content="Unavailable one" Interactive="False" />
                    <Button Type="Menu Item" Content="Unavailable two" Interactive="False" />
                    <Button Type="Menu Item" Content="Five" />
                    <Button Type="Menu Item" Content="Six" />
                  </Button>
                </Container>
                """);
            Assert.Empty(diagnostics);
            var window = new Window { Width = 400, Height = 240, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var owner = Assert.Single(root!.GetVisualDescendants().OfType<CuiMenuItem>());
                owner.IsSubMenuOpen = true;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var items = owner.Items.OfType<CuiMenuItem>().ToArray();
                Assert.True(items[0].Focus());
                window.KeyPress(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                window.KeyRelease(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                Assert.True(items[4].IsFocused); // Same column, skipping the wholly unavailable row.

                window.KeyPress(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                window.KeyRelease(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                Assert.True(items[4].IsFocused); // The lower edge does not wrap to the first row.
                window.KeyPress(Avalonia.Input.Key.Up, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                window.KeyRelease(Avalonia.Input.Key.Up, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                Assert.True(items[0].IsFocused);
                Assert.False(items[5].IsFocused);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Icon_header_keeps_native_menu_name_popup_and_disabled_gate()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var registry = new CuiControlRegistry();
            var picture = new DrawingImage(new GeometryDrawing { Geometry = new EllipseGeometry(new Rect(4, 4, 16, 16)), Pen = new Pen(Brushes.Black, 2) });
            registry.RegisterImageSourceResolver((name, _) => name == "icon:more" ? picture : null);
            using var loader = new CuiControlLoader(registry);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Menu">
                  <Button Type="Menu Item" Content="More" AccessibleName="More" IconSource="icon:more" Width="36" Height="36" Padding="8">
                    <Button Type="Menu Item" Content="Duplicate slide" />
                  </Button>
                </Container>
                """);
            Assert.Empty(diagnostics);
            var window = new Window { Width = 400, Height = 200, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var menu = Assert.Single(root!.GetVisualDescendants().OfType<CuiMenuItem>());
                Assert.Equal("More", menu.Header);
                Assert.Same(picture, menu.Icon);
                var image = Assert.Single(menu.GetVisualDescendants().OfType<Image>());
                Assert.True(image.IsVisible);
                Assert.Same(picture, image.Source);
                var peer = ControlAutomationPeer.CreatePeerForElement(menu)!;
                Assert.Equal("More", peer.GetName());
                var provider = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetProvider<IExpandCollapseProvider>());
                provider.Expand(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.True(menu.IsSubMenuOpen);
                var submenuCommand = Assert.IsType<CuiMenuItem>(Assert.Single(menu.Items));
                Assert.Equal("Duplicate slide", submenuCommand.Header);
                Assert.True(submenuCommand.IsFocused);
                provider.Collapse(); Dispatcher.UIThread.RunJobs();
                Assert.False(menu.IsSubMenuOpen);
                Assert.True(menu.IsFocused);
                CuiLifecycleProperties.SetLiteral(menu, "Interactive", "false", CuiPropertyLevel.Instance, "test owner gate");
                Assert.False(menu.IsEffectivelyEnabled);
                provider.Expand();
                Assert.False(menu.IsSubMenuOpen);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Checkable_menu_items_show_cui_owned_checked_and_radio_state()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            var dispatcher = new RecordingMenuActionDispatcher();
            loader.SetActionDispatcher(dispatcher);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Menu">
                  <Button Type="Menu Item" Content="View">
                    <Button Type="Menu Item" Content="Grid" ToggleType="CheckBox" Checked="True" StaysOpenOnClick="True" Action="ToggleGrid" />
                    <Button Type="Menu Item" Content="Guides" ToggleType="Radio" Checked="True" StaysOpenOnClick="True" />
                    <Button Type="Menu Item" Content="Locked grid" ToggleType="CheckBox" Checked="True" Interactive="False" />
                  </Button>
                </Container>
                """);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Width = 400, Height = 240, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var owner = Assert.Single(root!.GetVisualDescendants().OfType<CuiMenuItem>());
                owner.IsSubMenuOpen = true;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var items = owner.Items.OfType<CuiMenuItem>().ToArray();
                var checkItem = items[0];
                var radioItem = items[1];
                var lockedItem = items[2];
                Assert.Equal(MenuItemToggleType.CheckBox, checkItem.ToggleType);
                Assert.Equal(MenuItemToggleType.Radio, radioItem.ToggleType);
                Assert.True(checkItem.IsChecked);
                Assert.True(radioItem.IsChecked);
                Assert.True(checkItem.StaysOpenOnClick);
                Assert.True(radioItem.StaysOpenOnClick);
                var checkMark = Assert.Single(checkItem.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "✓");
                var radioMark = Assert.Single(radioItem.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "●");
                var lockedMark = Assert.Single(lockedItem.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "✓");
                Assert.True(checkMark.IsVisible);
                Assert.True(radioMark.IsVisible);
                Assert.True(lockedMark.IsVisible);
                Assert.False(lockedItem.IsEffectivelyEnabled);

                checkItem.IsChecked = false;
                Assert.Equal(string.Empty, checkMark.Text);
                checkItem.IsChecked = true;
                Assert.Equal("✓", checkMark.Text);
                radioItem.IsChecked = false;
                Assert.Equal(string.Empty, radioMark.Text);
                radioItem.IsChecked = true;
                Assert.Equal("●", radioMark.Text);
                radioItem.ToggleType = MenuItemToggleType.None;
                Assert.False(radioMark.IsVisible);
                radioItem.ToggleType = MenuItemToggleType.Radio;
                Assert.True(radioMark.IsVisible);
                Assert.Equal("●", radioMark.Text);
                var checkToggle = Assert.IsAssignableFrom<IToggleProvider>(
                    ControlAutomationPeer.CreatePeerForElement(checkItem)!.GetProvider<IToggleProvider>());
                var toggleStateChanges = 0;
                ControlAutomationPeer.CreatePeerForElement(checkItem)!.PropertyChanged += (_, change) =>
                {
                    if (change.Property == TogglePatternIdentifiers.ToggleStateProperty) toggleStateChanges++;
                };
                Assert.Equal(ToggleState.On, checkToggle.ToggleState);
                checkToggle.Toggle();
                await loader.WhenActionsIdleAsync();
                Assert.Equal(ToggleState.Off, checkToggle.ToggleState);
                Assert.Equal(1, toggleStateChanges);
                Assert.Equal(new[] { "ToggleGrid" }, dispatcher.Commands);
                Assert.Equal(string.Empty, checkMark.Text);
                Assert.True(owner.IsSubMenuOpen); // StaysOpenOnClick retains a checked-settings menu.
                var radioToggle = Assert.IsAssignableFrom<IToggleProvider>(
                    ControlAutomationPeer.CreatePeerForElement(radioItem)!.GetProvider<IToggleProvider>());
                Assert.Equal(ToggleState.On, radioToggle.ToggleState);
                radioItem.IsChecked = false;
                radioToggle.Toggle();
                Assert.True(radioItem.IsChecked);
                await loader.WhenActionsIdleAsync();
                Assert.Equal(new[] { "ToggleGrid" }, dispatcher.Commands); // A radio selection dispatches once.
                radioToggle.Toggle();
                Assert.True(radioItem.IsChecked); // A selected radio item cannot toggle itself off.
                Assert.Null(ControlAutomationPeer.CreatePeerForElement(owner)!.GetProvider<IToggleProvider>());
                var lockedToggle = Assert.IsAssignableFrom<IToggleProvider>(
                    ControlAutomationPeer.CreatePeerForElement(lockedItem)!.GetProvider<IToggleProvider>());
                Assert.Throws<ElementNotEnabledException>(lockedToggle.Toggle);

                checkItem.Focus();
                window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                window.KeyRelease(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                await loader.WhenActionsIdleAsync();
                Assert.True(checkItem.IsChecked);
                Assert.Equal("✓", checkMark.Text);
                Assert.Equal(new[] { "ToggleGrid", "ToggleGrid" }, dispatcher.Commands);
                Assert.True(owner.IsSubMenuOpen);
                owner.IsSubMenuOpen = true;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.True(checkItem.Focus());
                window.KeyPress(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                window.KeyRelease(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                await loader.WhenActionsIdleAsync();
                Assert.False(checkItem.IsChecked);
                Assert.Equal(string.Empty, checkMark.Text);
                Assert.Equal(new[] { "ToggleGrid", "ToggleGrid", "ToggleGrid" }, dispatcher.Commands);
                Assert.True(owner.IsSubMenuOpen);
                ControlAutomationPeer.CreatePeerForElement(checkItem)!.GetProvider<IInvokeProvider>()!.Invoke();
                Dispatcher.UIThread.RunJobs();
                await loader.WhenActionsIdleAsync();
                Assert.True(checkItem.IsChecked);
                Assert.Equal("✓", checkMark.Text);
                Assert.Equal(new[] { "ToggleGrid", "ToggleGrid", "ToggleGrid", "ToggleGrid" }, dispatcher.Commands);
                Assert.True(owner.IsSubMenuOpen);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Disabling_focused_top_level_menu_item_moves_focus_to_live_sibling()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Menu">
                  <Button Type="Menu Item" Content="First" />
                  <Button Type="Menu Item" Content="Second" />
                </Container>
                """);
            Assert.Empty(diagnostics);
            var window = new Window { Width = 400, Height = 200, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var items = root!.GetVisualDescendants().OfType<CuiMenuItem>().ToArray();
                Assert.Equal(2, items.Length);
                Assert.True(items[0].Focus());
                CuiLifecycleProperties.SetLiteral(items[0], "Interactive", "false", CuiPropertyLevel.Instance, "focused menu command became unavailable");
                Dispatcher.UIThread.RunJobs();
                Assert.True(items[1].IsFocused);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Space_activates_top_level_CUI_menu_command_through_typed_action()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            var dispatcher = new RecordingMenuActionDispatcher();
            loader.SetActionDispatcher(dispatcher);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Menu">
                  <Button Type="Menu Item" Content="Refresh" Action="RefreshWorkspace" />
                </Container>
                """);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Width = 400, Height = 200, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var item = Assert.Single(root!.GetVisualDescendants().OfType<CuiMenuItem>());
                Assert.True(item.Focus());
                window.KeyPress(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                window.KeyRelease(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                await loader.WhenActionsIdleAsync();
                Assert.Equal(new[] { "RefreshWorkspace" }, dispatcher.Commands);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Menu_item_shortcut_invokes_the_same_authored_click_action_without_opening_menu()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            var dispatcher = new RecordingMenuActionDispatcher();
            loader.SetActionDispatcher(dispatcher);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <Container Type="Menu">
                    <Button Type="Menu Item" Content="Refresh" Shortcut="Ctrl+R" Action="RefreshWorkspace" />
                  </Container>
                  <Input Type="Text" Value="draft" />
                </Container>
                """);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Width = 400, Height = 240, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var editor = Assert.Single(root!.GetVisualDescendants().OfType<TextBox>());
                Assert.True(editor.Focus());
                window.KeyPress(Avalonia.Input.Key.R, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
                window.KeyRelease(Avalonia.Input.Key.R, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
                await loader.WhenActionsIdleAsync();
                Assert.Single(dispatcher.Commands);
                Assert.Equal("RefreshWorkspace", dispatcher.Commands[0]);
                Assert.True(editor.IsFocused);
                var menu = Assert.Single(root.GetVisualDescendants().OfType<CuiMenu>());
                Assert.False(menu.IsOpen);
                var menuItem = Assert.Single(menu.Items.OfType<CuiMenuItem>());
                CuiLifecycleProperties.SetLiteral(menuItem, "Interactive", "false", CuiPropertyLevel.Instance, "shortcut command disabled");
                window.KeyPress(Avalonia.Input.Key.R, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
                window.KeyRelease(Avalonia.Input.Key.R, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
                await loader.WhenActionsIdleAsync();
                Assert.Single(dispatcher.Commands);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Context_menu_shortcut_registers_on_popup_root_only_while_context_is_open()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            var dispatcher = new RecordingMenuActionDispatcher();
            loader.SetActionDispatcher(dispatcher);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <Input Type="Text" Value="draft">
                    <Container Type="Context Menu">
                      <Button Type="Menu Item" Content="Mark" Shortcut="Ctrl+M" Action="MarkDocument" />
                    </Container>
                  </Input>
                </Container>
                """);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Width = 400, Height = 240, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var editor = Assert.Single(root!.GetVisualDescendants().OfType<TextBox>());
                Assert.True(editor.Focus());
                window.KeyPress(Avalonia.Input.Key.M, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
                window.KeyRelease(Avalonia.Input.Key.M, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
                await loader.WhenActionsIdleAsync();
                Assert.Empty(dispatcher.Commands); // Context-only shortcuts are inactive while the menu is closed.

                var menu = editor.ContextMenu!;
                menu.Open(editor);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var item = Assert.IsType<CuiMenuItem>(Assert.Single(menu.Items));
                Assert.True(item.Focus());
                var popupRoot = TopLevel.GetTopLevel(item)!;
                popupRoot.KeyPress(Avalonia.Input.Key.M, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
                popupRoot.KeyRelease(Avalonia.Input.Key.M, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
                await loader.WhenActionsIdleAsync();
                Assert.Equal(new[] { "MarkDocument" }, dispatcher.Commands);
                Assert.False(menu.IsOpen);
                Dispatcher.UIThread.RunJobs();
                Assert.True(editor.IsFocused);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Context_submenu_shortcut_requires_both_context_and_nested_submenu_open()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            var dispatcher = new RecordingMenuActionDispatcher();
            loader.SetActionDispatcher(dispatcher);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <Input Type="Text" Value="draft">
                    <Container Type="Context Menu">
                      <Button Type="Menu Item" Content="Export options">
                        <Button Type="Menu Item" Content="Export" Shortcut="Ctrl+E" Action="ExportDocument" />
                      </Button>
                    </Container>
                  </Input>
                </Container>
                """);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Width = 400, Height = 240, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var editor = Assert.Single(root!.GetVisualDescendants().OfType<TextBox>());
                Assert.True(editor.Focus());
                var contextMenu = editor.ContextMenu!;
                contextMenu.Open(editor);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var parent = Assert.IsType<CuiMenuItem>(Assert.Single(contextMenu.Items));
                Assert.True(parent.Focus());
                Assert.False(parent.IsSubMenuOpen);
                var contextPopup = TopLevel.GetTopLevel(parent)!;
                contextPopup.KeyPress(Avalonia.Input.Key.E, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
                contextPopup.KeyRelease(Avalonia.Input.Key.E, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
                await loader.WhenActionsIdleAsync();
                Assert.Empty(dispatcher.Commands); // The context is open, but its nested command is still hidden.

                parent.IsSubMenuOpen = true;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var item = Assert.IsType<CuiMenuItem>(Assert.Single(parent.Items));
                Assert.True(item.Focus());
                var submenuPopup = TopLevel.GetTopLevel(item)!;
                submenuPopup.KeyPress(Avalonia.Input.Key.E, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
                submenuPopup.KeyRelease(Avalonia.Input.Key.E, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
                await loader.WhenActionsIdleAsync();
                Assert.Equal(new[] { "ExportDocument" }, dispatcher.Commands);
                Assert.False(contextMenu.IsOpen);
                Dispatcher.UIThread.RunJobs();
                Assert.True(editor.IsFocused);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Main_menu_shortcut_remains_active_when_its_parent_submenu_is_closed()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            var dispatcher = new RecordingMenuActionDispatcher();
            loader.SetActionDispatcher(dispatcher);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <Container Type="Menu">
                    <Button Type="Menu Item" Content="File">
                      <Button Type="Menu Item" Content="Export" Shortcut="Ctrl+E" Action="ExportDocument" />
                    </Button>
                  </Container>
                  <Input Type="Text" Value="draft" />
                </Container>
                """);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Width = 400, Height = 240, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var editor = Assert.Single(root!.GetVisualDescendants().OfType<TextBox>());
                Assert.True(editor.Focus());
                var fileMenu = Assert.Single(root.GetVisualDescendants().OfType<CuiMenuItem>());
                Assert.False(fileMenu.IsSubMenuOpen);
                PressMenuShortcut(window, Avalonia.Input.Key.E);
                await loader.WhenActionsIdleAsync();
                Assert.Equal(new[] { "ExportDocument" }, dispatcher.Commands);

                var menu = Assert.Single(root.GetVisualDescendants().OfType<CuiMenuItem>());
                menu.IsSubMenuOpen = true;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var item = Assert.IsType<CuiMenuItem>(Assert.Single(menu.Items));
                Assert.True(item.Focus());
                var popupRoot = TopLevel.GetTopLevel(item)!;
                popupRoot.KeyPress(Avalonia.Input.Key.E, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
                popupRoot.KeyRelease(Avalonia.Input.Key.E, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
                await loader.WhenActionsIdleAsync();
                Assert.Equal(new[] { "ExportDocument", "ExportDocument" }, dispatcher.Commands);

                Dispatcher.UIThread.RunJobs();
                menu.IsSubMenuOpen = false;
                Dispatcher.UIThread.RunJobs();
                Assert.True(editor.Focus());
                PressMenuShortcut(window, Avalonia.Input.Key.E);
                await loader.WhenActionsIdleAsync();
                Assert.Equal(new[] { "ExportDocument", "ExportDocument", "ExportDocument" }, dispatcher.Commands);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Retired_menu_shortcut_is_unregistered_and_recreated_with_its_CUI_branch()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var state = new CuiViewModel();
            state.Set("ShowAction", true);
            var dispatcher = new RecordingMenuActionDispatcher();
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(state);
            loader.SetActionDispatcher(dispatcher);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Cui>
                  <If condition="{Binding ShowAction}">
                    <Container Type="Menu">
                      <Button Type="Menu Item" Content="Refresh" Shortcut="Ctrl+R" Action="RefreshWorkspace" />
                    </Container>
                  </If>
                  <Input Type="Text" Value="draft" />
                </Cui>
                """);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Width = 400, Height = 240, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var editor = Assert.Single(root!.GetVisualDescendants().OfType<TextBox>());
                Assert.True(editor.Focus());
                PressMenuShortcut(window);
                await loader.WhenActionsIdleAsync();
                Assert.Single(dispatcher.Commands);

                state.Set("ShowAction", false);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                PressMenuShortcut(window);
                await loader.WhenActionsIdleAsync();
                Assert.Single(dispatcher.Commands);

                state.Set("ShowAction", true);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                PressMenuShortcut(window);
                await loader.WhenActionsIdleAsync();
                Assert.Equal(new[] { "RefreshWorkspace", "RefreshWorkspace" }, dispatcher.Commands);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    private static void PressMenuShortcut(Window window, Avalonia.Input.Key key = Avalonia.Input.Key.R)
    {
        window.KeyPress(key, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
        window.KeyRelease(key, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.None, null);
    }

    [Fact]
    public async Task Space_opens_top_level_submenu_and_focuses_first_enabled_child()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Menu">
                  <Button Type="Menu Item" Content="File">
                    <Button Type="Menu Item" Content="Decorative" Focusable="False" />
                    <Button Type="Menu Item" Content="Open" />
                  </Button>
                </Container>
                """);
            Assert.Empty(diagnostics);
            var window = new Window { Width = 400, Height = 200, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var owner = Assert.Single(root!.GetVisualDescendants().OfType<CuiMenuItem>());
                Assert.True(owner.Focus());
                window.KeyPress(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                window.KeyRelease(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.True(owner.IsSubMenuOpen);
                var children = owner.Items.OfType<CuiMenuItem>().ToArray();
                Assert.True(children[0].IsEffectivelyEnabled);
                Assert.False(children[0].Focusable);
                Assert.True(children[1].IsFocused);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Space_opened_submenu_does_not_steal_later_focus()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <Container Type="Menu">
                    <Button Type="Menu Item" Content="File">
                      <Button Type="Menu Item" Content="Open" />
                    </Button>
                  </Container>
                  <Input Type="Text" Value="draft" />
                </Container>
                """);
            Assert.Empty(diagnostics);
            var window = new Window { Width = 400, Height = 200, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var item = Assert.Single(root!.GetVisualDescendants().OfType<CuiMenuItem>());
                var editor = Assert.Single(root.GetVisualDescendants().OfType<TextBox>());
                Assert.True(item.Focus());
                window.KeyPress(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                window.KeyRelease(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                Assert.True(item.IsSubMenuOpen);
                Assert.True(editor.Focus());
                Dispatcher.UIThread.RunJobs();
                Assert.True(editor.IsFocused);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Framework_focus_clear_after_disable_restores_gallery_sibling()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var fixture = new GalleryFocusFixture();
            Assert.True(fixture.Items[0].Focus());
            var popupRoot = TopLevel.GetTopLevel(fixture.Items[0])!;
            CuiLifecycleProperties.SetLiteral(fixture.Items[0], "Interactive", "false", CuiPropertyLevel.Instance, "focused choice disabled");
            Assert.Null(popupRoot.FocusManager!.GetFocusedElement());
            Dispatcher.UIThread.RunJobs();
            Assert.True(fixture.Items[1].IsFocused);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Focus_move_elsewhere_before_disable_recovery_is_not_stolen()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var fixture = new GalleryFocusFixture();
            Assert.True(fixture.Items[0].Focus());
            CuiLifecycleProperties.SetLiteral(fixture.Items[0], "Interactive", "false", CuiPropertyLevel.Instance, "focused choice disabled");
            Assert.True(fixture.Items[2].Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.True(fixture.Items[2].IsFocused);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Disable_reenable_disable_invalidates_stale_focus_recovery()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var fixture = new GalleryFocusFixture();
            Assert.True(fixture.Items[0].Focus());
            CuiLifecycleProperties.SetLiteral(fixture.Items[0], "Interactive", "false", CuiPropertyLevel.Instance, "first disable");
            CuiLifecycleProperties.SetLiteral(fixture.Items[0], "Interactive", "true", CuiPropertyLevel.Instance, "re-enable");
            Assert.True(fixture.Items[0].Focus());
            CuiLifecycleProperties.SetLiteral(fixture.Items[0], "Interactive", "false", CuiPropertyLevel.Instance, "second disable");
            Assert.True(fixture.Items[2].Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.True(fixture.Items[2].IsFocused);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Closing_and_reopening_submenu_invalidates_pending_focus_recovery()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var fixture = new GalleryFocusFixture();
            Assert.True(fixture.Items[0].Focus());
            CuiLifecycleProperties.SetLiteral(fixture.Items[0], "Interactive", "false", CuiPropertyLevel.Instance, "focused choice disabled");
            fixture.Owner.IsSubMenuOpen = false;
            fixture.Owner.IsSubMenuOpen = true;
            Assert.True(fixture.Items[2].Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.True(fixture.Items[2].IsFocused);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Disabling_last_enabled_choice_returns_focus_to_open_menu_owner()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var fixture = new GalleryFocusFixture();
            CuiLifecycleProperties.SetLiteral(fixture.Items[1], "Interactive", "false", CuiPropertyLevel.Instance, "disabled choice");
            CuiLifecycleProperties.SetLiteral(fixture.Items[2], "Interactive", "false", CuiPropertyLevel.Instance, "disabled choice");
            Assert.True(fixture.Items[0].Focus());
            CuiLifecycleProperties.SetLiteral(fixture.Items[0], "Interactive", "false", CuiPropertyLevel.Instance, "last choice disabled");
            Dispatcher.UIThread.RunJobs();
            Assert.True(fixture.Owner.IsFocused);
            Assert.DoesNotContain(fixture.Items, item => item.IsFocused);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Context_popup_focus_clear_recovers_within_its_actual_popup_root()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <Input Type="Text" Value="document">
                    <Container Type="Context Menu">
                      <Button Type="Menu Item" Content="First" />
                      <Button Type="Menu Item" Content="Second" />
                    </Container>
                  </Input>
                </Container>
                """);
            Assert.Empty(diagnostics);
            var window = new Window { Width = 400, Height = 240, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var editor = Assert.Single(root!.GetVisualDescendants().OfType<TextBox>());
                var contextMenu = editor.ContextMenu!;
                contextMenu.Open(editor);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var items = contextMenu.Items.OfType<CuiMenuItem>().ToArray();
                Assert.Equal(2, items.Length);
                Assert.True(items[0].Focus());
                var popupRoot = TopLevel.GetTopLevel(items[0])!;
                CuiLifecycleProperties.SetLiteral(items[0], "Interactive", "false", CuiPropertyLevel.Instance, "focused context command disabled");
                var focusAfterDisable = popupRoot.FocusManager!.GetFocusedElement();
                Assert.NotSame(items[0], focusAfterDisable);
                Dispatcher.UIThread.RunJobs();
                var focusAfterRecovery = popupRoot.FocusManager.GetFocusedElement();
                if (focusAfterDisable is null) Assert.Same(items[1], focusAfterRecovery);
                else Assert.Same(focusAfterDisable, focusAfterRecovery);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Context_popup_focus_recovery_does_not_steal_editor_move_then_explicit_clear()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <Input Type="Text" Value="draft">
                    <Container Type="Context Menu">
                      <Button Type="Menu Item" Content="First" />
                      <Button Type="Menu Item" Content="Second" />
                    </Container>
                  </Input>
                </Container>
                """);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Width = 400, Height = 240, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var editor = Assert.Single(root!.GetVisualDescendants().OfType<TextBox>());
                var menu = editor.ContextMenu!;
                menu.Open(editor);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var items = menu.Items.OfType<CuiMenuItem>().ToArray();
                Assert.Equal(2, items.Length);
                Assert.True(items[0].Focus());
                var popupRoot = TopLevel.GetTopLevel(items[0])!;
                CuiLifecycleProperties.SetLiteral(items[0], "Interactive", "false", CuiPropertyLevel.Instance, "focused context command disabled");
                Assert.Null(popupRoot.FocusManager!.GetFocusedElement());

                Assert.True(editor.Focus());
                var windowFocus = window.FocusManager!;
                Assert.Same(editor, windowFocus.GetFocusedElement());
                windowFocus.Focus(null);
                Assert.Null(windowFocus.GetFocusedElement());

                Dispatcher.UIThread.RunJobs();
                Assert.Null(windowFocus.GetFocusedElement());
                Assert.DoesNotContain(items, item => item.IsFocused);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Keyboard_context_menu_key_opens_authored_menu_and_escape_returns_to_editor()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <Input Type="Text" Value="document">
                    <Container Type="Context Menu">
                      <Button Type="Menu Item" Content="Copy" />
                      <Button Type="Menu Item" Content="Paste" />
                    </Container>
                  </Input>
                </Container>
                """);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Width = 400, Height = 240, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var editor = Assert.Single(root!.GetVisualDescendants().OfType<TextBox>());
                editor.Focus();
                editor.CaretIndex = editor.Text!.Length;
                var menu = editor.ContextMenu!;
                window.KeyPress(Avalonia.Input.Key.Apps, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.True(menu.IsOpen);
                var item = Assert.IsType<CuiMenuItem>(menu.Items[1]);
                Assert.True(item.Focus());
                window.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                await loader.WhenActionsIdleAsync();
                Dispatcher.UIThread.RunJobs();
                Assert.False(menu.IsOpen);
                Assert.True(editor.IsFocused);
                Assert.Equal(editor.Text!.Length, editor.CaretIndex);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Space_activates_context_menu_item_through_canonical_click_action()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            var dispatcher = new RecordingMenuActionDispatcher();
            loader.SetActionDispatcher(dispatcher);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <Input Type="Text" Value="document">
                    <Container Type="Context Menu">
                      <Button Type="Menu Item" Content="Mark" ToggleType="CheckBox" Checked="True" Action="MarkDocument" />
                    </Container>
                  </Input>
                </Container>
                """);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Width = 400, Height = 240, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var editor = Assert.Single(root!.GetVisualDescendants().OfType<TextBox>());
                var menu = editor.ContextMenu!;
                menu.Open(editor);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var item = Assert.IsType<CuiMenuItem>(Assert.Single(menu.Items));
                var mark = Assert.Single(item.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "✓");
                Assert.True(item.Focus());
                var popupRoot = TopLevel.GetTopLevel(item)!;
                popupRoot.KeyPress(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                popupRoot.KeyRelease(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                await loader.WhenActionsIdleAsync();
                Assert.False(menu.IsOpen);
                Dispatcher.UIThread.RunJobs();
                Assert.True(editor.IsFocused);
                Assert.False(item.IsChecked);
                Assert.Equal(string.Empty, mark.Text);
                Assert.Equal(new[] { "MarkDocument" }, dispatcher.Commands);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Space_opens_nested_menu_item_and_skips_disabled_first_choice()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Menu">
                  <Button Type="Menu Item" Content="File">
                    <Button Type="Menu Item" Content="Tools">
                      <Button Type="Menu Item" Content="Unavailable" Interactive="False" />
                      <Button Type="Menu Item" Content="Settings" />
                    </Button>
                  </Button>
                </Container>
                """);
            Assert.Empty(diagnostics);
            var window = new Window { Width = 400, Height = 200, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var file = Assert.Single(root!.GetVisualDescendants().OfType<CuiMenuItem>());
                file.IsSubMenuOpen = true;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var tools = Assert.IsType<CuiMenuItem>(Assert.Single(file.Items));
                Assert.True(tools.Focus());
                window.KeyPress(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                window.KeyRelease(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                Dispatcher.UIThread.RunJobs();
                Assert.True(tools.IsSubMenuOpen);
                var children = tools.Items.OfType<CuiMenuItem>().ToArray();
                Assert.False(children[0].IsEffectivelyEnabled);
                Assert.True(children[1].IsFocused);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private sealed class GalleryFocusFixture : IDisposable
    {
        public CuiControlLoader Loader { get; } = new();
        public Window Window { get; }
        public CuiMenuItem Owner { get; }
        public CuiMenuItem[] Items { get; }

        public GalleryFocusFixture()
        {
            var (root, diagnostics) = Loader.LoadMarkup("""
                <Container Type="Menu">
                  <Button Type="Menu Item" Content="Gallery" MenuColumns="3">
                    <Button Type="Menu Item" Content="One" />
                    <Button Type="Menu Item" Content="Two" />
                    <Button Type="Menu Item" Content="Three" />
                  </Button>
                </Container>
                """);
            Assert.Empty(diagnostics);
            Window = new Window { Width = 400, Height = 240, Content = root };
            Window.Show();
            Window.UpdateLayout();
            Owner = Assert.Single(root!.GetVisualDescendants().OfType<CuiMenuItem>());
            Owner.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Items = Owner.Items.OfType<CuiMenuItem>().ToArray();
        }

        public void Dispose()
        {
            Window.Close();
            Loader.Dispose();
        }
    }

    private sealed class RecordingMenuActionDispatcher : ICuiActionDispatcher
    {
        public List<string> Commands { get; } = new();
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return ValueTask.CompletedTask;
        }
    }
}
