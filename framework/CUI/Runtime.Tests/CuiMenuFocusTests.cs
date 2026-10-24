using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiMenuFocusTests
{
    [Fact]
    public async Task CUI_cascading_menu_uses_down_right_left_and_escape_with_level_focus_return()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Menu">
                  <Button Type="Menu Item" Content="File">
                    <Button Type="Menu Item" Content="Open" />
                    <Button Type="Menu Item" Content="Export">
                      <Button Type="Menu Item" Content="PNG" />
                    </Button>
                  </Button>
                </Container>
                """);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            var window = new Window { Content = root, Width = 500, Height = 280 };
            window.Show();
            try
            {
                window.UpdateLayout();
                var file = Assert.Single(root!.GetVisualDescendants().OfType<CuiMenuItem>());
                file.IsSubMenuOpen = true;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var fileCommands = file.Items.OfType<CuiMenuItem>().ToArray();
                Assert.Equal(2, fileCommands.Length);
                Assert.True(fileCommands[0].Focus());
                var filePopup = TopLevel.GetTopLevel(fileCommands[0])!;

                PressKey(filePopup, Key.Down);
                Assert.True(fileCommands[1].IsFocused);
                PressKey(filePopup, Key.Right);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.True(fileCommands[1].IsSubMenuOpen);
                var nestedCommand = Assert.IsType<CuiMenuItem>(Assert.Single(fileCommands[1].Items));
                Assert.True(nestedCommand.IsFocused);
                var nestedPopup = TopLevel.GetTopLevel(nestedCommand)!;

                PressKey(nestedPopup, Key.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.False(fileCommands[1].IsSubMenuOpen);
                Assert.True(fileCommands[1].IsFocused);

                PressKey(filePopup, Key.Escape);
                Dispatcher.UIThread.RunJobs();
                Assert.False(file.IsSubMenuOpen);
                Assert.True(file.IsFocused);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Actual_menu_escape_returns_typing_without_resetting_caret_or_dispatching_an_action()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var fixture = new Fixture(); var input = fixture.Input;
            input.Focus(); input.CaretIndex = 3;
            Click(fixture.Top); Assert.True(fixture.Top.IsSubMenuOpen);
            PressKey(fixture.Window, Key.Escape); Pump(fixture.Window);
            Assert.False(fixture.Top.IsSubMenuOpen); Assert.True(input.IsFocused);
            Assert.Equal(3, input.CaretIndex); fixture.Window.KeyTextInput("!"); Assert.Equal("doc!ument", input.Text);
            Assert.Equal(0, fixture.ActionCount);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Nested_menu_command_keeps_its_explicit_new_canvas_focus()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            using var fixture = new Fixture(); fixture.Input.Focus();
            Click(fixture.Top); Click(fixture.Nested); Click(fixture.Leaf);
            await fixture.Loader.WhenActionsIdleAsync(); Pump(fixture.Window);
            Assert.Equal(1, fixture.ActionCount); Assert.True(fixture.Other.IsFocused);
            Assert.False(fixture.Top.IsSubMenuOpen); fixture.Window.KeyTextInput("x"); Assert.Equal("xcanvas", fixture.Other.Text);
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Deferred_menu_return_does_not_steal_new_focus_or_focus_hidden_disabled_detached_targets()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var fixture = new Fixture();
            fixture.Input.Focus(); Click(fixture.Top); fixture.Menu.Close(); fixture.Other.Focus(); Pump(fixture.Window);
            Assert.True(fixture.Other.IsFocused);
            foreach (var property in new[] { "Interactive", "Active" })
            {
                fixture.Input.Focus(); Click(fixture.Top); fixture.Menu.Close();
                CuiLifecycleProperties.SetLiteral(fixture.Input, property, "false", CuiPropertyLevel.Instance, "real target state");
                Pump(fixture.Window); Assert.False(fixture.Input.IsFocused);
                CuiLifecycleProperties.SetLiteral(fixture.Input, property, "true", CuiPropertyLevel.Instance, "real target state"); Pump(fixture.Window);
            }
            fixture.Input.Focus(); Click(fixture.Top); fixture.Menu.Close(); fixture.Window.Content = null; Pump(fixture.Window);
            Assert.False(fixture.Input.IsFocused);
        }, CancellationToken.None);
    }

    private static void Click(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!; var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        var hit = root.InputHitTest(point) as Visual;
        Assert.True(ReferenceEquals(hit, control) || hit?.GetVisualAncestors().Contains(control) == true);
        root.MouseMove(point); root.MouseDown(point, MouseButton.Left); root.MouseUp(point, MouseButton.Left);
        if (root is Window window) Pump(window); else { Dispatcher.UIThread.RunJobs(); using var frame = root.CaptureRenderedFrame(); }
    }
    private static void PressKey(Window window, Key key)
    { window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null); window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null); }
    private static void PressKey(TopLevel root, Key key)
    { root.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null); root.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null); }
    private static void Pump(Window window)
    { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }

    private sealed class Fixture : IDisposable, ICuiActionDispatcher
    {
        public CuiControlLoader Loader { get; } = new();
        public Window Window { get; }
        public Menu Menu { get; }
        public CuiMenuItem Top { get; }
        public CuiMenuItem Nested { get; }
        public CuiMenuItem Leaf { get; }
        public TextBox Input { get; }
        public TextBox Other { get; }
        public int ActionCount;
        public Fixture()
        {
            Loader.SetActionDispatcher(this);
            var loaded = Loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <Container Type="Menu"><Button Type="Menu Item" Content="Tools">
                    <Button Type="Menu Item" Content="Shapes"><Button Type="Menu Item" Content="Rectangle" Action="Begin"/></Button>
                  </Button></Container>
                  <Input Type="Text" Value="document"/>
                  <Input Type="Text" Value="canvas"/>
                </Container>
                """);
            Assert.Empty(loaded.Diagnostics); var root = Assert.IsType<CuiContainerControl>(loaded.Root);
            Loader.WireBindings(root); Menu = (Menu)root.AuthoredChildren[0]; Top = (CuiMenuItem)Menu.Items[0]!;
            Nested = (CuiMenuItem)Top.Items[0]!; Leaf = (CuiMenuItem)Nested.Items[0]!;
            Input = (TextBox)root.AuthoredChildren[1]; Other = (TextBox)root.AuthoredChildren[2];
            Window = new Window { Content = root, Width = 500, Height = 320 }; Window.Show(); Pump(Window);
        }
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        { Assert.Equal("Begin", command); ActionCount++; Other.Focus(); return ValueTask.CompletedTask; }
        public void Dispose() { Window.Close(); Loader.Dispose(); }
    }
}
