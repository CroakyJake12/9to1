using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiActionShelfRuntimeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);
    private const string Markup = """
        <Cui><Actions>
          <Action name="shelf.activate" command="shelf.activate" />
          <Action name="shelf.next" command="shelf.next" />
        </Actions><Page><Container Type="Vertical Stack">
          <Input id="editor" Type="Text" AcceptsReturn="True" />
          <Container Type="ActionShelf" Source="{Binding Eligible}" Width="70" Height="70">
            <Layer ZIndex="0"><Button id="rear" action="shelf.next" Width="56" Height="56" TranslateX="14"
              HorizontalAlignment="Left" VerticalAlignment="Top" IsVisible="{Binding Shelf.HasNext}"
              content="next" /></Layer>
            <Layer ZIndex="1"><Button id="front" action="shelf.activate" Width="56" Height="56"
              HorizontalAlignment="Left" VerticalAlignment="Top" IsVisible="{Binding Shelf.HasFront}"
              content="{Binding Shelf.FrontLabel}" /></Layer>
            <Layer ZIndex="2" Interactive="False"><Text id="dots" text="{Binding Shelf.Dots}"
              IsVisible="{Binding Shelf.IsNavigating}" VerticalAlignment="Bottom" /></Layer>
          </Container></Container></Page></Cui>
        """;
    private sealed class Context : ICuiBindingContext, ICuiActionDispatcher, INotifyPropertyChanged
    {
        public IReadOnlyList<CuiActionShelfItem> Eligible = [Item("a"), Item("b"), Item("c")];
        public List<string> Calls = [];
        public Exception? Failure;
        public bool TryGetValue(string path, out object? value) { value = Eligible; return path == "Eligible"; }
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        { Calls.Add(command); return Failure is null ? ValueTask.CompletedTask : ValueTask.FromException(Failure); }
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Replace(params CuiActionShelfItem[] items) { Eligible = items; PropertyChanged?.Invoke(this, new(null)); }
    }
    private static CuiActionShelfItem Item(string id) => new(id, "app." + id, id.ToUpperInvariant(), "", "Use " + id);
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static Button Button(Control root, string name) => root.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent, button));

    [Fact]
    public async Task Authored_overlap_browses_without_activation_preserves_editor_and_retires_its_old_scope()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            var context = new Context(); using var loader = new CuiControlLoader();
            loader.SetBindingContext(context); loader.SetActionDispatcher(context);
            var loaded = loader.LoadMarkup(Markup); Assert.Empty(loaded.Diagnostics);
            var root = Assert.IsAssignableFrom<Control>(loaded.Root); loader.WireBindings(root);
            var window = new Window { Width = 320, Height = 200, Content = root };
            try
            {
                window.Show(); Pump(window);
                var shelf = Assert.Single(root.GetVisualDescendants().OfType<CuiActionShelf>());
                var front = Button(root, "front"); var rear = Button(root, "rear");
                var editor = Assert.Single(root.GetVisualDescendants().OfType<TextBox>());
                editor.Text = "unchanged draft"; editor.SelectionStart = 2; editor.SelectionEnd = 7;
                Assert.Equal(56, front.Bounds.Width);
                Assert.Equal(14, rear.TranslatePoint(default, window)!.Value.X - front.TranslatePoint(default, window)!.Value.X, 3);
                Click(rear); await loader.WhenActionsIdleAsync().WaitAsync(Bound); Pump(window);
                Assert.Equal("B", front.Content);
                Assert.Empty(context.Calls);
                Assert.Equal("unchanged draft", editor.Text); Assert.Equal(2, editor.SelectionStart); Assert.Equal(7, editor.SelectionEnd);
                Assert.True(shelf.TryGetValue("Shelf.Dots", out var dots)); Assert.Equal("○ ● ○", dots);
                Assert.True(front.Focus());
                window.KeyPress(Key.Right, RawInputModifiers.None); Pump(window);
                Assert.Equal("C", front.Content);
                window.KeyPress(Key.Right, RawInputModifiers.None); Pump(window); // Deliberate wrap, actual3 positions.
                Assert.Equal("A", front.Content);
                var frontPoint = front.TranslatePoint(new Point(28, 28), window)!.Value;
                window.MouseWheel(frontPoint, new Vector(0, -1)); Pump(window);
                Assert.Equal("B", front.Content);
                window.MouseWheel(frontPoint, new Vector(-1, 0)); Pump(window);
                Assert.Equal("C", front.Content);
                window.MouseWheel(new Point(240, 150), new Vector(0, -1)); Pump(window);
                Assert.Equal("C", front.Content); // Unrelated page scrolling is outside this scope.
                window.MouseDown(frontPoint, MouseButton.Left);
                window.MouseMove(frontPoint + new Vector(-16, 0));
                window.MouseUp(frontPoint + new Vector(-16, 0), MouseButton.Left);
                await loader.WhenActionsIdleAsync().WaitAsync(Bound); Pump(window);
                Assert.Equal("A", front.Content);
                Assert.Empty(context.Calls); // Finishing a browse gesture never activates.
                Click(front); await loader.WhenActionsIdleAsync().WaitAsync(Bound);
                Assert.Equal(new[] { "app.a" }, context.Calls);
                context.Replace(Item("x")); Pump(window);
                Assert.False(rear.IsVisible); Assert.Equal("X", front.Content);
                var next = loader.LoadMarkup("<Cui><Page><Text text=\"replacement\" /></Page></Cui>");
                Assert.Empty(next.Diagnostics);
                await shelf.DispatchAsync("shelf.next", null);
                await shelf.DispatchAsync("app.foreign", null);
                Assert.Equal(new[] { "app.a" }, context.Calls); // Old scope is inert, including forwarding.
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task A_press_replaced_before_release_cannot_activate_its_replacement_and_actual_faults_remain_joined()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            var context = new Context(); using var loader = new CuiControlLoader();
            loader.SetBindingContext(context); loader.SetActionDispatcher(context);
            var loaded = loader.LoadMarkup(Markup); Assert.Empty(loaded.Diagnostics);
            var root = Assert.IsAssignableFrom<Control>(loaded.Root); loader.WireBindings(root);
            var window = new Window { Width = 320, Height = 200, Content = root };
            try
            {
                window.Show(); Pump(window); var front = Button(root, "front");
                var point = front.TranslatePoint(new Point(28, 28), window)!.Value;
                window.MouseMove(point); window.MouseDown(point, MouseButton.Left);
                context.Replace(Item("a") with { ContextIdentity = Guid.NewGuid() }); Pump(window);
                window.MouseUp(point, MouseButton.Left);
                await loader.WhenActionsIdleAsync().WaitAsync(Bound);
                Assert.Empty(context.Calls);
                var exact = context.Failure = new IOException("actual command owner failed");
                Click(front);
                var failure = await Assert.ThrowsAsync<IOException>(() => loader.WhenActionsIdleAsync().WaitAsync(Bound));
                Assert.Same(exact, failure);
                Assert.Equal(new[] { "app.a" }, context.Calls);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Eligible_port_rejects_fillers_and_unbounded_lists()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var shelf = new CuiActionShelf();
            Assert.Throws<ArgumentException>(() => shelf.Source = [Item("a"), Item("a")]);
            Assert.Throws<ArgumentException>(() => shelf.Source = Enumerable.Range(0, 6).Select(i => Item(i.ToString())).ToArray());
        }, CancellationToken.None);
    }
}
