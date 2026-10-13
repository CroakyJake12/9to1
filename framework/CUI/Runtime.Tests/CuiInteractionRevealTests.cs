using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiInteractionRevealTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("actions")]
    public async Task Unknown_or_self_owner_is_a_strict_source_diagnostic(string owner)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var loaded = loader.LoadMarkup($"<Container ID=\"message\" Type=\"Vertical Stack\"><Container ID=\"actions\" Type=\"Vertical Stack\" RevealWith=\"{owner}\"><Button Content=\"Copy\" /></Container></Container>");
            Assert.Null(loaded.Root);
            Assert.Contains(loaded.Diagnostics, error => error.Code == "CUIR059");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Pointer_reveal_keeps_geometry_and_hidden_actions_are_not_hit_targets()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var loaded = loader.LoadMarkup(Markup); Assert.Empty(loaded.Diagnostics); var root = loaded.Root!;
            var window = new Window { Width = 500, Height = 400, Content = root }; window.Show();
            try
            {
                window.MouseMove(new(-10, -10)); Pump(window);
                var owner = loader.FindAuthoredControl<Control>(root, "message");
                var strip = loader.FindAuthoredControl<Control>(root, "actions");
                var copy = loader.FindAuthoredControl<Button>(root, "copy");
                var measured = owner.Bounds; var stripMeasured = strip.Bounds;
                Assert.Equal(0, strip.Opacity); Assert.False(strip.IsHitTestVisible); Assert.True(copy.IsEffectivelyVisible);
                var point = Centre(copy, window);
                var hiddenHit = window.InputHitTest(point) as Visual;
                Assert.False(ReferenceEquals(hiddenHit, copy) || hiddenHit is not null && copy.IsVisualAncestorOf(hiddenHit));
                var text = loader.FindAuthoredControl<Control>(root, "message-body");
                window.MouseMove(Centre(text, window)); Pump(window);
                Assert.Equal(.8, strip.Opacity); Assert.True(strip.IsHitTestVisible);
                Assert.Equal(measured, owner.Bounds); Assert.Equal(stripMeasured, strip.Bounds);
                window.MouseMove(new(-10, -10)); Pump(window);
                Assert.Equal(0, strip.Opacity); Assert.Equal(measured, owner.Bounds);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Authored_continuous_owner_keeps_actions_reachable_across_the_body_to_button_gap()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var model = new CuiViewModel(); var calls = 0; model.On("copy", _ => calls++);
            using var loader = new CuiControlLoader(); loader.SetActionDispatcher(model);
            var loaded = loader.LoadMarkup(Markup); Assert.Empty(loaded.Diagnostics);
            var root = loaded.Root!; loader.WireBindings(root);
            var window = new Window { Width = 500, Height = 400, Content = root }; window.Show();
            try
            {
                window.MouseMove(new(-10, -10)); Pump(window);
                var owner = loader.FindAuthoredControl<Control>(root, "message");
                var body = loader.FindAuthoredControl<Control>(root, "message-body");
                var strip = loader.FindAuthoredControl<Control>(root, "actions");
                var copy = loader.FindAuthoredControl<Button>(root, "copy");
                var ownerBounds = owner.Bounds; var stripBounds = strip.Bounds;
                var bodyBottom = body.TranslatePoint(new(0, body.Bounds.Height), window)!.Value.Y;
                var stripTop = strip.TranslatePoint(new(0, 0), window)!.Value.Y;
                var copyCentre = Centre(copy, window);
                Assert.True(stripTop > bodyBottom); // Exercise actual authored CellGap, not a routed event shortcut.
                Assert.Equal(0, strip.Opacity);
                window.MouseMove(Centre(body, window)); Pump(window);
                foreach (var point in new[] { new Point(copyCentre.X, (bodyBottom + stripTop) / 2),
                    new Point(copyCentre.X, stripTop + .5), copyCentre })
                {
                    window.MouseMove(point); Pump(window);
                    Assert.Equal(.8, strip.Opacity); Assert.True(strip.IsHitTestVisible);
                    Assert.Equal(ownerBounds, owner.Bounds); Assert.Equal(stripBounds, strip.Bounds);
                }
                var actualHit = window.InputHitTest(copyCentre) as Visual;
                Assert.True(ReferenceEquals(actualHit, copy) || actualHit is not null && copy.IsVisualAncestorOf(actualHit));
                window.MouseDown(copyCentre, MouseButton.Left); window.MouseUp(copyCentre, MouseButton.Left);
                await loader.WhenActionsIdleAsync(); Assert.Equal(1, calls);
                Assert.Same(copy, loader.FindAuthoredControl<Button>(root, "copy"));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Physical_keyboard_focus_reveals_original_action_and_survives_pointer_exit_and_theme_change()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var model = new CuiViewModel(); var calls = 0; model.On("copy", _ => calls++);
            using var loader = new CuiControlLoader(); loader.SetActionDispatcher(model);
            var loaded = loader.LoadMarkup(Markup); Assert.Empty(loaded.Diagnostics); var root = loaded.Root!; loader.WireBindings(root);
            var window = new Window { Width = 500, Height = 400, Content = root }; window.Show();
            try
            {
                window.MouseMove(new(-10, -10)); Pump(window);
                var strip = loader.FindAuthoredControl<Control>(root, "actions");
                var copy = loader.FindAuthoredControl<Button>(root, "copy");
                var before = loader.FindAuthoredControl<Button>(root, "before"); before.Focus();
                Assert.Equal(0, strip.Opacity);
                Key(window, Avalonia.Input.Key.Tab, PhysicalKey.Tab); Pump(window);
                Assert.True(copy.IsFocused); Assert.Equal(.8, strip.Opacity);
                window.MouseMove(new(-10, -10)); loader.SetAppearance(CuiAppearance.Bright); Pump(window);
                Assert.True(copy.IsFocused); Assert.Equal(.8, strip.Opacity);
                Key(window, Avalonia.Input.Key.Enter, PhysicalKey.Enter); await loader.WhenActionsIdleAsync(); Assert.Equal(1, calls);
                Key(window, Avalonia.Input.Key.Tab, PhysicalKey.Tab); Pump(window);
                Assert.True(loader.FindAuthoredControl<Button>(root, "after").IsFocused); Assert.Equal(0, strip.Opacity);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Routed_touch_reveals_without_dispatch_and_outside_touch_does_not_hide_focused_action()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var model = new CuiViewModel(); var calls = 0; model.On("copy", _ => calls++);
            using var loader = new CuiControlLoader(); loader.SetActionDispatcher(model);
            var loaded = loader.LoadMarkup(Markup); Assert.Empty(loaded.Diagnostics); var root = loaded.Root!; loader.WireBindings(root);
            var window = new Window { Width = 500, Height = 400, Content = root }; window.Show();
            try
            {
                window.MouseMove(new(-10, -10)); Pump(window);
                var strip = loader.FindAuthoredControl<Control>(root, "actions");
                var copy = loader.FindAuthoredControl<Button>(root, "copy");
                Touch(window, loader.FindAuthoredControl<Control>(root, "message-body"));
                Assert.Equal(.8, strip.Opacity); Assert.Equal(0, calls);
                copy.Focus(); Touch(window, loader.FindAuthoredControl<Control>(root, "before"));
                Assert.True(copy.IsFocused); Assert.Equal(.8, strip.Opacity);
                loader.FindAuthoredControl<Button>(root, "after").Focus(); Pump(window);
                Assert.Equal(0, strip.Opacity); Assert.Equal(0, calls);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Original_loader_disposal_restores_authored_state_and_detaches_reveal_listeners()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var loader = new CuiControlLoader(); var loaded = loader.LoadMarkup(Markup);
            Assert.Empty(loaded.Diagnostics); var root = loaded.Root!;
            var window = new Window { Width = 500, Height = 400, Content = root }; window.Show();
            try
            {
                window.MouseMove(new(-10, -10)); Pump(window);
                var strip = loader.FindAuthoredControl<Control>(root, "actions"); Assert.Equal(0, strip.Opacity);
                loader.Dispose(); await loader.WhenActionsIdleAsync();
                // Retiring the entire loader also releases its authored opacity; the
                // backend default returns after the reveal overlay is removed.
                Assert.Equal(1, strip.Opacity); Assert.True(strip.IsHitTestVisible);
                window.MouseMove(new(250, 75)); window.MouseMove(new(-10, -10)); Pump(window);
                Assert.Equal(1, strip.Opacity);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Later_conditional_with_nested_repeat_resolves_the_published_authored_ancestor()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var state = new RevealRows { Rows = [new("first", "First", [new("copy", "Copy")])] };
            using var loader = new CuiControlLoader(); loader.SetBindingContext(state);
            var loaded = loader.LoadMarkup("""
                <Container ID="message" Type="Vertical Stack" Background="Transparent" Padding="12">
                  <Text ID="message-body" Text="Retained containing message" />
                  <If condition="{Binding Visible}">
                    <Repeat Source="{Binding Rows}" As="row" Key="{Binding row.Id}">
                      <Repeat Source="{Binding row.Children}" As="choice" Key="{Binding choice.Id}">
                        <Container ID="actions" Type="Vertical Stack" RevealWith="message" Opacity="0.8">
                          <Button Content="{Binding choice.Label}" />
                        </Container>
                      </Repeat>
                    </Repeat>
                  </If>
                </Container>
                """);
            Assert.Empty(loaded.Diagnostics); var root = loaded.Root!; loader.WireBindings(root);
            var window = new Window { Width = 500, Height = 400, Content = root }; window.Show();
            try
            {
                window.MouseMove(new(-10, -10)); Pump(window);
                Assert.DoesNotContain(root.GetVisualDescendants().OfType<Control>(), control => control.Name == "actions");
                state.Visible = true; state.NotifyAll(); Pump(window);
                var actions = loader.FindAuthoredControl<Control>(root, "actions");
                Assert.Equal(0, actions.Opacity); Assert.False(actions.IsHitTestVisible);
                window.MouseMove(Centre(loader.FindAuthoredControl<Control>(root, "message-body"), window)); Pump(window);
                Assert.Equal(.8, actions.Opacity);
                window.MouseMove(new(-10, -10)); state.Visible = false; state.NotifyAll(); Pump(window);
                Assert.Equal(1, actions.Opacity); Assert.True(actions.IsHitTestVisible);
                state.Visible = true; state.NotifyAll(); Pump(window);
                var replacement = loader.FindAuthoredControl<Control>(root, "actions");
                Assert.NotSame(actions, replacement); Assert.Equal(0, replacement.Opacity);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task New_outer_repeat_items_attach_nested_reveals_to_their_own_owner_and_detach_removed_listeners()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var state = new RevealRows();
            using var loader = new CuiControlLoader(); loader.SetBindingContext(state);
            var loaded = loader.LoadMarkup("""
                <Container Type="Vertical Stack" CellGap="20" Padding="12">
                  <Repeat Source="{Binding Rows}" As="row" Key="{Binding row.Id}">
                    <Container ID="message" Type="Vertical Stack" Background="Transparent" Padding="12">
                      <Text ID="message-body" Text="{Binding row.Label}" />
                      <Repeat Source="{Binding row.Children}" As="choice" Key="{Binding choice.Id}">
                        <Container ID="actions" Type="Vertical Stack" RevealWith="message" Opacity="0.8">
                          <Button ID="copy" Content="{Binding choice.Label}" />
                        </Container>
                      </Repeat>
                    </Container>
                  </Repeat>
                </Container>
                """);
            Assert.Empty(loaded.Diagnostics); var root = loaded.Root!; loader.WireBindings(root);
            var window = new Window { Width = 500, Height = 500, Content = root }; window.Show();
            try
            {
                window.MouseMove(new(-10, -10)); Pump(window);
                state.Rows = [new("first", "First message", [new("first-copy", "Copy first")]),
                    new("second", "Second message", [new("second-copy", "Copy second")])];
                state.NotifyAll(); Pump(window);
                var actions = root.GetVisualDescendants().OfType<Control>().Where(control => control.Name == "actions").ToArray();
                var bodies = root.GetVisualDescendants().OfType<Control>().Where(control => control.Name == "message-body").ToArray();
                Assert.Equal(2, actions.Length); Assert.All(actions, action => Assert.Equal(0, action.Opacity));
                window.MouseMove(Centre(bodies[0], window)); Pump(window);
                Assert.Equal(.8, actions[0].Opacity); Assert.Equal(0, actions[1].Opacity);
                window.MouseMove(Centre(bodies[1], window)); Pump(window);
                Assert.Equal(0, actions[0].Opacity); Assert.Equal(.8, actions[1].Opacity);
                window.MouseMove(new(-10, -10)); Pump(window);
                state.Rows = [state.Rows[1]]; state.NotifyAll(); Pump(window);
                Assert.Equal(1, actions[0].Opacity); Assert.True(actions[0].IsHitTestVisible);
                Assert.Same(actions[1], loader.FindAuthoredControl<Control>(root, "actions"));
                Assert.Equal(0, actions[1].Opacity);
                window.MouseMove(Centre(bodies[1], window)); Pump(window);
                Assert.Equal(.8, actions[1].Opacity); Assert.Equal(1, actions[0].Opacity);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private sealed record RevealChoice(string Id, string Label);
    private sealed record RevealRow(string Id, string Label, IReadOnlyList<RevealChoice> Children);
    private sealed class RevealRows : ICuiBindingContext, ICuiRepeatItemBindingContext, INotifyPropertyChanged
    {
        public bool Visible { get; set; }
        public IReadOnlyList<RevealRow> Rows { get; set; } = [];
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool TryGetItemValue(object item, string path, out object? value)
        {
            value = (item, path) switch
            {
                (RevealRow row, "Id") => row.Id,
                (RevealRow row, "Label") => row.Label,
                (RevealRow row, "Children") => row.Children,
                (RevealChoice choice, "Id") => choice.Id,
                (RevealChoice choice, "Label") => choice.Label,
                _ => null
            };
            return value is not null;
        }
        public bool TrySetItemValue(object item, string path, object? value) => false;
        public void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        public bool TryGetValue(string path, out object? value)
        { value = path switch { "Visible" => Visible, "Rows" => Rows, _ => null }; return value is not null; }
    }

    private static readonly string Markup = """
        <Container Type="Vertical Stack" CellGap="8" Padding="12">
          <Button ID="before" Content="Before" />
          <Container ID="message" Type="Vertical Stack" CellGap="8" Background="Transparent" Padding="8">
            <Text ID="message-body" Text="A real retained message body" />
            <Container ID="actions" Type="Horizontal Stack" RevealWith="message" Opacity="0.8">
              <Button ID="copy" Action="copy" Content="Copy" />
            </Container>
          </Container>
          <Button ID="after" Content="After" />
        </Container>
        """;
    private static void Pump(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private static Point Centre(Control control, Window window) => control.TranslatePoint(new(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    private static void Key(Window window, Key key, PhysicalKey physical)
    { window.KeyPress(key, RawInputModifiers.None, physical, null); window.KeyRelease(key, RawInputModifiers.None, physical, null); }
    private static void Touch(Window window, Control actualTarget)
    {
        using var touch = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true);
        actualTarget.RaiseEvent(new PointerPressedEventArgs(actualTarget, touch, window, Centre(actualTarget, window), 1,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
        Pump(window);
    }
}
