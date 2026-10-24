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
                    <Button Type="Menu Item" Content="Grid" ToggleType="CheckBox" Checked="True" Action="ToggleGrid" />
                    <Button Type="Menu Item" Content="Guides" ToggleType="Radio" Checked="True" />
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
                checkItem.StaysOpenOnClick = true;
                radioItem.StaysOpenOnClick = true;
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
                CuiPropertyPresentation.Set(radioItem, "ToggleType", MenuItemToggleType.None,
                    CuiPropertyLevel.Instance, "host menu mode update");
                Assert.False(radioMark.IsVisible);
                CuiPropertyPresentation.Set(radioItem, "ToggleType", MenuItemToggleType.Radio,
                    CuiPropertyLevel.Instance, "host menu mode update");
                Assert.True(radioMark.IsVisible);
                Assert.Equal("●", radioMark.Text);
                var checkToggle = Assert.IsAssignableFrom<IToggleProvider>(
                    ControlAutomationPeer.CreatePeerForElement(checkItem)!.GetProvider<IToggleProvider>());
                var directClickCount = 0;
                checkItem.Click += (_, _) => ++directClickCount;
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
                Assert.Equal(1, directClickCount);
                Assert.Equal(new[] { "ToggleGrid" }, dispatcher.Commands);
                Assert.Equal(string.Empty, checkMark.Text);
                Assert.True(owner.IsSubMenuOpen); // StaysOpenOnClick retains a checked-settings menu.
                var radioToggle = Assert.IsAssignableFrom<IToggleProvider>(
                    ControlAutomationPeer.CreatePeerForElement(radioItem)!.GetProvider<IToggleProvider>());
                Assert.Equal(ToggleState.On, radioToggle.ToggleState);
                radioItem.IsChecked = false;
                radioToggle.Toggle();
                Assert.True(radioItem.IsChecked);
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
    public async Task Space_opens_top_level_submenu_and_focuses_first_enabled_child()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Menu">
                  <Button Type="Menu Item" Content="File">
                    <Button Type="Menu Item" Content="Unavailable" Interactive="False" />
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
                Assert.False(children[0].IsEffectivelyEnabled);
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
            Assert.NotNull(root);
            var window = new Window { Width = 400, Height = 200, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var item = Assert.Single(root.GetVisualDescendants().OfType<CuiMenuItem>());
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Context_popup_focus_clear_recovers_within_its_actual_popup_root(bool moveAndClearFocus)
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
                if (moveAndClearFocus)
                {
                    Assert.True(editor.Focus());
                    // The first clear may restore the scope's remembered control;
                    // a second clear then leaves focus intentionally empty.
                    window.FocusManager!.Focus(null);
                    window.FocusManager.Focus(null);
                    Assert.Null(window.FocusManager.GetFocusedElement());
                }
                Dispatcher.UIThread.RunJobs();
                var focusAfterRecovery = popupRoot.FocusManager.GetFocusedElement();
                if (moveAndClearFocus) Assert.Null(focusAfterRecovery);
                else if (focusAfterDisable is null) Assert.Same(items[1], focusAfterRecovery);
                else Assert.Same(focusAfterDisable, focusAfterRecovery);
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
                window.KeyRelease(Avalonia.Input.Key.Apps, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
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
