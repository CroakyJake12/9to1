using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using System.ComponentModel;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiInteractionActionEventTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("attribute")]
    [InlineData("Clicked")]
    [InlineData("OnceClick")]
    public Task Real_pointer_keyboard_and_click_routes_coexist_without_duplicate_wiring(string click) => Run(async f =>
    {
        f.Mount(click);
        await f.Prime();
        f.Loader.WireBindings(f.Root); f.Loader.WireBindings(f.Root);
        f.Loader.SetActionDispatcher(f.Recorder("current"));
        var first = new object(); f.Model.Set("Selected", first);

        f.Window.MouseMove(f.Center(f.Target)); await f.Idle();
        Assert.True(f.Target.IsPointerOver);
        Assert.Equal("enter", Assert.Single(f.Calls).Command);
        Assert.Same(first, f.Calls[0].Parameter);

        var latest = new object(); f.Model.Set("Selected", latest);
        f.Window.MouseMove(Fixture.Outside); await f.Idle();
        f.Window.MouseMove(f.Center(f.Target)); await f.Idle();
        Assert.Equal(new[] { "enter", "exit", "enter" }, f.Commands);

        f.Key(Key.Tab); await f.Idle(); Assert.True(f.Target.IsFocused);
        f.Key(Key.Space); await f.Idle();
        f.Key(Key.Tab); await f.Idle(); Assert.True(f.After.IsFocused);
        f.Window.MouseMove(Fixture.Outside); await f.Idle();
        Assert.Equal(new[] { "enter", "exit", "enter", "focus", "click", "blur", "exit" }, f.Commands);
        Assert.All(f.Calls, call => Assert.Equal("current", call.Owner));
        Assert.All(f.Calls.Skip(1), call => Assert.Same(latest, call.Parameter));
    });

    [Fact]
    public Task Pointer_only_action_never_becomes_a_click_action() => Run(async f =>
    {
        f.Mount("none"); await f.Prime();
        var point = f.Center(f.Target);
        f.Window.MouseMove(point); f.Window.MouseDown(point, MouseButton.Left); f.Window.MouseUp(point, MouseButton.Left);
        await f.Idle();
        Assert.Contains("enter", f.Commands); Assert.Contains("focus", f.Commands);
        Assert.DoesNotContain("click", f.Commands);
        Assert.Equal(1, f.Calls.Count(call => call.Command == "enter"));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Disabled_control_or_ancestor_rejects_interaction_actions(bool ancestor) => Run(async f =>
    {
        var disabled = ancestor ? f.Root : f.Target;
        Assert.True(CuiLifecycleProperties.SetLiteral(disabled, "Interactive", "False", CuiPropertyLevel.Instance, "test"));
        Assert.False(f.Target.IsEffectivelyEnabled);
        f.Window.MouseMove(f.Center(f.Target));
        foreach (var type in new[] { "PointerEnter", "PointerExit", "Focused", "Blurred" }) f.Raise(f.Target, type);
        await f.Idle(); Assert.Empty(f.Calls);
        f.Window.MouseMove(Fixture.Outside);
        Assert.True(CuiLifecycleProperties.SetLiteral(disabled, "Interactive", "True", CuiPropertyLevel.Instance, "test"));
        f.Window.MouseMove(f.Center(f.Target)); await f.Idle();
        Assert.Equal("enter", Assert.Single(f.Calls).Command);
    });

    [Fact]
    public Task Descendant_focus_does_not_invoke_the_parent_owned_focus_route() => Run(async f =>
    {
        f.MountMarkup("""
            <Container Type="Vertical Stack"><Button ID="Parent"><ActionEvent Type="Focused" Action="parent"/>
              <Button ID="Child" Text="Child"><ActionEvent Type="Focused" Action="child"/></Button>
            </Button></Container>
            """);
        Assert.True(f.Find<Button>("Child").Focus()); await f.Idle();
        Assert.Equal("child", Assert.Single(f.Calls).Command);
    });

    [Theory]
    [InlineData("PointerEnter")]
    [InlineData("Focused")]
    public Task Retained_query_owner_and_current_repeat_row_survive_refresh_and_native_detach(string type) => Run(async f =>
    {
        var first = new Dictionary<string, object?> { ["Id"] = "stable", ["Name"] = "First" };
        f.Model.Set("Source", new RowSource(first));
        f.MountMarkup($$"""
            <Cui><Actions><Action Name="Use" Command="use-row" Parameter="{Binding row}"/></Actions>
              <Container Type="QueryViewport" Source="{Binding Source}">
                <Repeat Source="{Binding Items}" As="row" Key="{Binding row.Id}">
                  <Button ID="Row" Text="{Binding row.Name}"><ActionEvent Type="{{type}}" Action="Use"/></Button>
                </Repeat>
              </Container>
            </Cui>
            """);
        var row = f.Find<Button>("Row");
        f.Raise(row, type); await f.Idle(); Assert.Same(first, Assert.Single(f.Calls).Parameter);
        var latest = new Dictionary<string, object?> { ["Id"] = "stable", ["Name"] = "Latest" };
        f.Model.Set("Source", new RowSource(latest)); f.Pump();
        Assert.Same(row, f.Find<Button>("Row")); Assert.Equal("Latest", row.Content);
        f.Window.Content = null;
        f.Model.Set("row", new object()); f.Loader.SetActionDispatcher(f.Recorder("unrelated-global"));
        f.Raise(row, type); await f.Idle();
        Assert.Equal(2, f.Calls.Count); Assert.Same(latest, f.Calls[1].Parameter);
        Assert.All(f.Calls, call => Assert.Equal("initial", call.Owner));
    });

    [Fact]
    public Task Pointer_actions_can_update_per_loader_local_state_without_a_host_dispatcher() => Run(async f =>
    {
        const string markup = """
            <Cui><Variable Type="bool" ID="Preview">False</Variable><Actions>
              <Action Name="Show"><Set Target="Preview" Value="True"/></Action>
              <Action Name="Hide"><Set Target="Preview" Value="False"/></Action>
            </Actions><Container Type="Vertical Stack">
              <Button ID="Target" Text="Preview" Width="180" Height="40">
                <ActionEvent Type="PointerEnter" Action="Show"/>
                <ActionEvent Type="PointerExit" Action="Hide"/>
              </Button><Text ID="State" Text="{Binding Preview}"/>
            </Container></Cui>
            """;
        f.MountMarkup(markup);
        using var other = new CuiControlLoader();
        var loaded = other.LoadMarkup(markup); Assert.Empty(loaded.Diagnostics);
        var otherState = Assert.Single(loaded.Root!.GetVisualDescendants().OfType<TextBlock>());
        Assert.Equal("False", f.Find<TextBlock>("State").Text);
        f.Window.MouseMove(f.Center(f.Target)); await f.Idle();
        Assert.Equal("True", f.Find<TextBlock>("State").Text); Assert.Equal("False", otherState.Text);
        f.Window.MouseMove(Fixture.Outside); await f.Idle();
        Assert.Equal("False", f.Find<TextBlock>("State").Text);
    }, withDispatcher: false);

    [Fact]
    public Task Authored_context_menu_item_receives_real_pointer_and_keyboard_actions() => Run(async f =>
    {
        f.MountMarkup("""
            <Container Type="Vertical Stack"><Input ID="Owner" Type="Text" Value="document">
              <Container Type="Context Menu">
                <Button Type="Menu Item" Text="Destination" Action="choose">
                  <ActionEvent Type="PointerEnter" Action="enter"/>
                  <ActionEvent Type="PointerExit" Action="exit"/>
                  <ActionEvent Type="Focused" Action="focus"/>
                  <ActionEvent Type="Blurred" Action="blur"/>
                </Button>
                <Button Type="Menu Item" Text="Other"/>
              </Container>
            </Input></Container>
            """);
        var owner = f.Find<TextBox>("Owner"); var menu = owner.ContextMenu!;
        owner.Focus(); menu.Open(owner); f.Pump();
        var items = menu.Items.OfType<CuiMenuItem>().ToArray(); Assert.Equal(2, items.Length);
        var target = items[0]; var popup = TopLevel.GetTopLevel(target)!;
        Assert.NotNull(popup); using (var frame = popup.CaptureRenderedFrame()) { }
        Assert.True(items[1].Focus()); await f.Idle(); f.Calls.Clear();
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), popup)!.Value;
        popup.MouseMove(point); await f.Idle(); Assert.True(target.IsPointerOver);
        Assert.Equal(1, f.Calls.Count(call => call.Command == "enter"));
        Assert.True(items[1].Focus()); await f.Idle(); f.Calls.Clear();
        f.Key(Key.Up); await f.Idle(); Assert.True(target.IsFocused);
        Assert.Equal("focus", Assert.Single(f.Calls).Command);
        f.Key(Key.Enter); await f.Idle();
        Assert.Equal(1, f.Calls.Count(call => call.Command == "choose"));
        Assert.False(menu.IsOpen);
    });

    [Theory]
    [InlineData("PointerEnter", "replace")]
    [InlineData("PointerEnter", "retire")]
    [InlineData("PointerEnter", "dispose")]
    [InlineData("Focused", "replace")]
    [InlineData("Focused", "retire")]
    [InlineData("Focused", "dispose")]
    public Task Parameter_getter_cannot_start_a_command_after_retiring_its_origin(string type, string retirement) => Run(async f =>
    {
        var reentered = false; var armed = false;
        var context = new GetterContext(f.Model, () =>
        {
            if (armed && !reentered)
            {
                reentered = true;
                if (retirement == "replace") f.Mount();
                else if (retirement == "retire") f.Model.Set("Show", false);
                else f.Loader.Dispose();
            }
            return "original getter value";
        });
        f.Loader.SetBindingContext(context); f.Mount(); await f.Prime();
        Assert.False(reentered); armed = true;
        var original = f.Target;
        // Adversarial callback boundary: genuine input is covered above; a directly raised
        // gesture isolates synchronous parameter reentry from later native pointer retargeting.
        f.Raise(original, type); await f.Idle();
        Assert.True(reentered); Assert.Empty(f.Calls);
        f.Raise(original, type); await f.Idle(); Assert.Empty(f.Calls);
        if (retirement != "dispose")
        {
            if (retirement == "retire") f.Model.Set("Show", true);
            Assert.NotSame(original, f.Target);
            f.Raise(f.Target, type); await f.Idle();
            Assert.Equal(type == "Focused" ? "focus" : "enter", Assert.Single(f.Calls).Command);
        }
    });

    [Theory]
    [InlineData("replace")]
    [InlineData("retire")]
    [InlineData("dispose")]
    public Task Retired_routes_cannot_dispatch_and_teardown_does_not_publish_old_departures(string retirement) => Run(async f =>
    {
        var original = f.Target;
        f.Window.MouseMove(f.Center(original)); f.Key(Key.Tab); await f.Idle();
        Assert.True(original.IsPointerOver); Assert.True(original.IsFocused);
        f.Calls.Clear();
        if (retirement == "replace") f.Mount();
        else if (retirement == "retire") f.Model.Set("Show", false);
        else f.Loader.Dispose();
        await f.Idle();
        // A replacement at the same screen position legitimately receives a fresh pointer enter.
        // Teardown must not publish departures from the retired row into that replacement frame.
        if (retirement == "replace")
        {
            Assert.Equal("enter", Assert.Single(f.Calls).Command);
            Assert.NotSame(original, f.Target); Assert.True(f.Target.IsPointerOver);
        }
        else Assert.Empty(f.Calls);
        var admitted = f.Calls.Count;
        foreach (var type in new[] { "PointerEnter", "PointerExit", "Focused", "Blurred" }) f.Raise(original, type);
        original.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await f.Idle(); Assert.Equal(admitted, f.Calls.Count);
    });

    [Theory]
    [InlineData("PointerEnter")]
    [InlineData("Focused")]
    public Task Pending_interaction_retains_its_task_and_cancellation_while_native_typing_continues(string type) => Run(async f =>
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = default(CancellationToken); var calls = 0;
        f.Loader.SetActionDispatcher(new CallbackDispatcher((command, _, current) =>
        {
            if (command != (type == "Focused" ? "focus" : "enter")) return ValueTask.CompletedTask;
            Assert.False(f.Loader.WhenActionsIdleAsync().IsCompleted);
            calls++; token = current; entered.TrySetResult(); return new ValueTask(release.Task);
        }));
        try
        {
            if (type == "PointerEnter") f.Window.MouseMove(f.Center(f.Target)); else f.Key(Key.Tab);
            await entered.Task.WaitAsync(Bound);
            var pending = f.Loader.WhenActionsIdleAsync(); Assert.False(pending.IsCompleted);
            Assert.True(f.After.Focus()); f.Window.KeyTextInput("input stays responsive");
            Assert.Equal("input stays responsive", f.After.Text);
            f.Loader.Dispose(); Assert.True(token.IsCancellationRequested);
            var closing = f.Loader.WhenActionsIdleAsync(); Assert.False(closing.IsCompleted);
            f.Raise(f.Target, type); Assert.Equal(1, calls);
            release.TrySetResult(); await pending.WaitAsync(Bound); await closing.WaitAsync(Bound);
        }
        finally { release.TrySetResult(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Typed_direct_click_alias_collision_is_rejected_even_without_an_owned_event(bool definition)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            // on:click is an existing typed-document route, not namespace-qualified CUI markup.
            var parser = new CuiRichParser();
            var doc = parser.Parse(definition
                ? "<Cui><Button (Definition) ID=\"Defined\" Action=\"One\"/><Defined/></Cui>"
                : "<Button Action=\"One\"/>");
            Assert.Empty(parser.Diagnostics.Diagnostics);
            var component = doc.Components[^1];
            var actions = new Dictionary<string, CuiActionReference>(component.Actions)
                { ["on:click"] = new("Two", component.Span) };
            var updated = new CuiComponent(component.Type, component.Name, component.Classes, component.Properties,
                actions, component.Children, component.Condition, component.List, component.Span);
            doc = doc with { Components = definition ? [doc.Components[0], updated] : [updated] };
            if (!definition) Assert.Contains(CuiActionEventContract.Validate(doc), diagnostic => diagnostic.Code == "CUIE004");
            using var loader = new CuiControlLoader();
            var result = loader.TryLoad(doc);
            Assert.Null(result.Root); Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIE004");
        }, CancellationToken.None);
    }

    private static async Task Run(Func<Fixture, Task> test, bool withDispatcher = true)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var f = new Fixture(withDispatcher);
            try { f.Mount(); await f.Prime(); await test(f); }
            finally { f.Loader.Dispose(); await f.Idle(); f.Window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    private sealed class GetterContext(CuiViewModel model, Func<object?> selected) : ICuiBindingContext, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add => model.PropertyChanged += value;
            remove => model.PropertyChanged -= value;
        }

        public bool TryGetValue(string path, out object? value)
        {
            if (path != "Selected") return model.TryGetValue(path, out value);
            value = selected(); return true;
        }
    }

    private sealed class CallbackDispatcher(Func<string, object?, CancellationToken, ValueTask> callback) : ICuiActionDispatcher
    {
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) =>
            callback(command, parameter, cancellationToken);
    }

    private sealed class RowSource(object row) : ICuiPagedSource
    {
        public CuiPagedResult Evaluate(string? query, CuiPageCursor? cursor) => new([row], 1, 0, null, null);
    }

    private sealed class Fixture
    {
        public static readonly Point Outside = new(450, 300);
        public CuiControlLoader Loader { get; } = new();
        public CuiViewModel Model { get; } = new();
        public Window Window { get; } = new() { Width = 500, Height = 340 };
        public Control Root { get; private set; } = null!;
        public List<(string Owner, string Command, object? Parameter)> Calls { get; } = [];
        public IEnumerable<string> Commands => Calls.Select(call => call.Command);
        public Button Target => Find<Button>("Target");
        public TextBox Before => Find<TextBox>("Before");
        public TextBox After => Find<TextBox>("After");

        public Fixture(bool withDispatcher)
        {
            Model.Set("Selected", new object()); Model.Set("Show", true);
            Loader.SetBindingContext(Model);
            if (withDispatcher) Loader.SetActionDispatcher(Recorder("initial"));
        }

        public ICuiActionDispatcher Recorder(string owner) => new CallbackDispatcher((command, parameter, _) =>
        { Calls.Add((owner, command, parameter)); return ValueTask.CompletedTask; });

        public void Mount(string click = "attribute")
        {
            var direct = click == "attribute" ? "Action=\"Choose\"" : "";
            var owned = click is "Clicked" or "OnceClick" ? $"<ActionEvent Type=\"{click}\" Action=\"Choose\"/>" : "";
            MountMarkup($$"""
                <Cui><Actions>
                  <Action Name="Enter" Command="enter" Parameter="{Binding Selected}"/>
                  <Action Name="Exit" Command="exit" Parameter="{Binding Selected}"/>
                  <Action Name="Focus" Command="focus" Parameter="{Binding Selected}"/>
                  <Action Name="Blur" Command="blur" Parameter="{Binding Selected}"/>
                  <Action Name="Choose" Command="click" Parameter="{Binding Selected}"/>
                </Actions><Container Type="Vertical Stack">
                  <Input ID="Before" Type="Text" Width="180" Height="36"/>
                  <If Condition="{Binding Show}"><Button ID="Target" Text="Destination" Width="180" Height="40" {{direct}}>
                    <ActionEvent Type="PointerEnter" Action="Enter"/>
                    <ActionEvent Type="PointerExit" Action="Exit"/>
                    <ActionEvent Type="Focused" Action="Focus"/>
                    <ActionEvent Type="Blurred" Action="Blur"/>
                    {{owned}}
                  </Button></If>
                  <Input ID="After" Type="Text" Width="180" Height="36"/>
                </Container></Cui>
                """);
        }

        public void MountMarkup(string markup)
        {
            var loaded = Loader.LoadMarkup(markup);
            Assert.Empty(loaded.Diagnostics); Root = Assert.IsAssignableFrom<Control>(loaded.Root);
            Loader.WireBindings(Root); Window.Content = Root; Window.Show(); Pump();
        }

        public async Task Prime()
        {
            Window.MouseMove(Outside); Assert.True(Before.Focus()); await Idle(); Calls.Clear();
        }
        public Task Idle() => Loader.WhenActionsIdleAsync().WaitAsync(Bound);
        public T Find<T>(string id) where T : Control => Root.GetVisualDescendants().OfType<T>().Single(control => control.Name == id);
        public Point Center(Control control)
        {
            Pump(); return control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)!.Value;
        }
        public void Key(Key key)
        {
            Window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            Window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null); Pump();
        }
        public void Pump()
        {
            Window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using var frame = Window.CaptureRenderedFrame();
        }
        public void Raise(Control control, string type)
        {
            if (type == "Focused") control.RaiseEvent(new FocusChangedEventArgs(InputElement.GotFocusEvent));
            else if (type == "Blurred") control.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            else
            {
                using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
                control.RaiseEvent(new PointerEventArgs(type == "PointerEnter" ? InputElement.PointerEnteredEvent : InputElement.PointerExitedEvent,
                    control, pointer, Window, default, 0, new PointerPointProperties(), KeyModifiers.None));
            }
        }
    }
}
