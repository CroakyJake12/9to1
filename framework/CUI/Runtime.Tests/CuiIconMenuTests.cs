using Avalonia;
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
                    <Button Type="Menu Item" Content="Rectangle" IconSource="shape:rect" IconOnly="true" Width="56" Height="56" />
                    <Button Type="Menu Item" Content="Unavailable" IconSource="shape:rect" IconOnly="true" Interactive="false" Width="56" Height="56" />
                    <Button Type="Menu Item" Content="Ellipse" IconSource="shape:ellipse" IconOnly="true" Width="56" Height="56" />
                    <Button Type="Menu Item" Content="Triangle" IconSource="shape:triangle" IconOnly="true" Width="56" Height="56" />
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
                choices[0].Focus();
                window.KeyPress(Avalonia.Input.Key.Right, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null); window.KeyRelease(Avalonia.Input.Key.Right, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                Assert.True(choices[2].IsFocused);
                window.KeyPress(Avalonia.Input.Key.Home, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null); window.KeyRelease(Avalonia.Input.Key.Home, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                window.KeyPress(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null); window.KeyRelease(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
                Assert.True(choices[3].IsFocused);
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
                Assert.Equal("Duplicate slide", Assert.IsType<CuiMenuItem>(Assert.Single(menu.Items)).Header);
                provider.Collapse(); Dispatcher.UIThread.RunJobs();
                CuiLifecycleProperties.SetLiteral(menu, "Interactive", "false", CuiPropertyLevel.Instance, "test owner gate");
                Assert.False(menu.IsEffectivelyEnabled);
                provider.Expand();
                Assert.False(menu.IsSubMenuOpen);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
