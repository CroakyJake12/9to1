using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiKeyboardTooltipHostTests
{
    [Fact]
    public async Task Escape_dismisses_keyboard_tooltip_without_reaching_outer_surface()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <Button Content="Save" Tooltip="Save changes" />
                </Container>
            """);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            CuiKeyboardTooltipHost.Attach(root!).Dispose(); // A duplicate lease must not retire the loader-owned host.
            var outerEscapeCount = 0;
            root!.AddHandler(InputElement.KeyDownEvent, (_, args) =>
            {
                if (args.Key == Key.Escape) ++outerEscapeCount;
            }, RoutingStrategies.Bubble);
            var window = new Window { Width = 320, Height = 180, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var button = Assert.Single(root.GetVisualDescendants().OfType<Button>());
                Assert.Equal(PlacementMode.Pointer, ToolTip.GetPlacement(button));
                Assert.True(button.Focus());
                Assert.True(ToolTip.GetIsOpen(button));
                Assert.Equal(PlacementMode.Right, ToolTip.GetPlacement(button));

                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);

                Assert.False(ToolTip.GetIsOpen(button));
                Assert.Equal(PlacementMode.Pointer, ToolTip.GetPlacement(button));
                Assert.Equal(0, outerEscapeCount);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Escape_already_claimed_by_inner_surface_does_not_dismiss_keyboard_tooltip()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <Button Content="Save" Tooltip="Save changes" />
                </Container>
                """);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            using var tooltips = CuiKeyboardTooltipHost.Attach(root);
            var window = new Window { Width = 320, Height = 180, Content = root };
            window.Show();
            try
            {
                window.UpdateLayout();
                var button = Assert.Single(root.GetVisualDescendants().OfType<Button>());
                button.KeyDown += (_, args) =>
                {
                    if (args.Key == Key.Escape) args.Handled = true;
                };
                Assert.True(button.Focus());
                Assert.True(ToolTip.GetIsOpen(button));
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.True(ToolTip.GetIsOpen(button));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Loader_replacement_retires_old_tooltip_host_and_attaches_to_new_root()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (firstRoot, firstDiagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack"><Button Content="First" Tooltip="First help" /></Container>
                """);
            Assert.Empty(firstDiagnostics);
            loader.WireBindings(firstRoot!);
            var window = new Window { Width = 320, Height = 180, Content = firstRoot };
            window.Show();
            try
            {
                window.UpdateLayout();
                var first = Assert.Single(firstRoot!.GetVisualDescendants().OfType<Button>());
                Assert.True(first.Focus());
                Assert.True(ToolTip.GetIsOpen(first));

                var (secondRoot, secondDiagnostics) = loader.LoadMarkup("""
                    <Container Type="Vertical Stack"><Button Content="Second" Tooltip="Second help" /></Container>
                    """);
                Assert.Empty(secondDiagnostics);
                loader.WireBindings(secondRoot!);
                Assert.False(ToolTip.GetIsOpen(first));
                window.Content = secondRoot;
                window.UpdateLayout();
                var second = Assert.Single(secondRoot!.GetVisualDescendants().OfType<Button>());
                Assert.True(second.Focus());
                Assert.True(ToolTip.GetIsOpen(second));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Detaching_root_does_not_reopen_tooltip_from_reentrant_focus_callback()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Horizontal Stack">
                  <Button Content="First" Tooltip="First help" />
                  <Button Content="Second" Tooltip="Second help" />
                </Container>
                """);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Width = 360, Height = 200, Content = root };
            window.Show();
            Button[] buttons = [];
            EventHandler<RoutedEventArgs> reentrantFocus = (_, _) => buttons.Last().Focus();
            try
            {
                window.UpdateLayout();
                buttons = root!.GetVisualDescendants().OfType<Button>().ToArray();
                Assert.Equal(2, buttons.Length);
                Assert.True(buttons[0].Focus());
                Assert.True(ToolTip.GetIsOpen(buttons[0]));
                buttons[0].AddHandler(ToolTip.ToolTipClosingEvent, reentrantFocus, RoutingStrategies.Bubble, handledEventsToo: true);

                window.Content = null;

                Assert.False(ToolTip.GetIsOpen(buttons[0]));
                Assert.False(ToolTip.GetIsOpen(buttons[1]));
                window.Content = root;
                window.UpdateLayout();
                Assert.True(buttons[1].Focus());
                Assert.True(ToolTip.GetIsOpen(buttons[1]));
            }
            finally
            {
                window.Close();
                if (buttons.Length > 0) buttons[0].RemoveHandler(ToolTip.ToolTipClosingEvent, reentrantFocus);
            }
        }, CancellationToken.None);
    }
}
