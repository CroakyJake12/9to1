using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CakeOS.Cui;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiInputFocusRequestTests
{
    [Fact]
    public async Task Opening_selects_once_and_global_per_character_refresh_keeps_middle_caret_then_reopening_selects_current_text()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var model = new Model(); model.Set("Open", false);
            using var loader = new CuiControlLoader(); loader.SetBindingContext(model);
            var loaded = loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <Input Type="Text" Value="Other input"/>
                  <Input Type="Text" Active="{Binding Open}" Value="{Binding Name, Mode=TwoWay}"
                    FocusRequested="{Binding Open}" SelectOnFocusRequest="True"/>
                </Container>
                """);
            Assert.Empty(loaded.Diagnostics); var root = Assert.IsType<CuiContainerControl>(loaded.Root);
            loader.WireBindings(root);
            var other = Assert.IsType<TextBox>(root.AuthoredChildren[0]);
            var name = Assert.IsType<TextBox>(root.AuthoredChildren[1]);
            var window = new Window { Content = root, Width = 420, Height = 220 };
            try
            {
                window.Show(); Pump(window); other.Focus();
                model.Set("Open", true); Pump(window);
                Assert.True(name.IsFocused); Assert.Equal("Original name", name.SelectedText);
                foreach (var letter in "Slide name")
                {
                    window.KeyTextInput(letter.ToString()); Pump(window);
                    Assert.True(name.IsFocused); Assert.Equal(name.Text!.Length, name.CaretIndex);
                    Assert.Equal(name.SelectionStart, name.SelectionEnd);
                }
                Assert.Equal("Slide name", model.Name);
                name.CaretIndex = name.SelectionStart = name.SelectionEnd = 5;
                foreach (var letter in " revised") { window.KeyTextInput(letter.ToString()); Pump(window); }
                Assert.Equal("Slide revised name", model.Name); Assert.Equal(13, name.CaretIndex);
                other.Focus(); model.Notify(); Pump(window);
                Assert.True(other.IsFocused); // A persistent true request is not an input focus lock.
                model.Set("Open", false); Pump(window); model.Set("Open", true); Pump(window);
                Assert.True(name.IsFocused); Assert.Equal(model.Name, name.SelectedText);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Initial_request_waits_for_attachment_and_selects_bound_value_after_layout()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var loaded = loader.LoadMarkup("<Input Type=\"Text\" FocusRequested=\"True\" SelectOnFocusRequest=\"True\" Value=\"Initial value\"/>");
            Assert.Empty(loaded.Diagnostics); var input = Assert.IsType<TextBox>(loaded.Root);
            Dispatcher.UIThread.RunJobs(); Assert.False(input.IsFocused);
            var window = new Window { Content = input, Width = 420, Height = 150 };
            try
            {
                window.Show(); Pump(window);
                Assert.True(input.IsFocused); Assert.Equal("Initial value", input.SelectedText);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("withdrawn")]
    [InlineData("hidden")]
    [InlineData("disabled")]
    [InlineData("transparent-parent")]
    [InlineData("detached")]
    [InlineData("disposed")]
    public async Task Pending_request_cannot_focus_after_its_owner_or_input_gate_expires(string scenario)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var model = new Model();
            using var loader = new CuiControlLoader(); loader.SetBindingContext(model);
            var loaded = loader.LoadMarkup("""
                <Container Type="Vertical Stack" Opacity="{Binding LayerOpacity}"><Input Type="Text" Value="Other input"/>
                  <Input Type="Text" Value="Original name" FocusRequested="{Binding Request}"
                    Hidden="{Binding Hidden}" Interactive="{Binding Allow}" SelectOnFocusRequest="True"/>
                </Container>
                """);
            Assert.Empty(loaded.Diagnostics); var root = Assert.IsType<CuiContainerControl>(loaded.Root);
            var other = Assert.IsType<TextBox>(root.AuthoredChildren[0]); var input = Assert.IsType<TextBox>(root.AuthoredChildren[1]);
            var window = new Window { Content = root, Width = 420, Height = 220 };
            try
            {
                window.Show(); Pump(window); other.Focus(); model.Set("Request", true);
                switch (scenario)
                {
                    case "withdrawn": model.Set("Request", false); break;
                    case "hidden": model.Set("Hidden", true); break;
                    case "disabled": model.Set("Allow", false); break;
                    case "transparent-parent": model.Set("LayerOpacity", 0d); break;
                    case "detached": window.Content = null; break;
                    case "disposed": loader.Dispose(); break;
                }
                Pump(window); Assert.False(input.IsFocused);
                if (scenario == "disposed") return;
                model.Set("LayerOpacity", 1d); window.Content = root; model.Set("Hidden", false); model.Set("Allow", true);
                Pump(window); other.Focus(); model.Notify(); Pump(window); Assert.True(other.IsFocused);
                model.Set("Request", false); model.Set("Request", true); Pump(window);
                Assert.True(input.IsFocused); Assert.Equal("Original name", input.SelectedText);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Enter_and_escape_dispatch_the_actual_active_buttons_and_keep_multiline_enter_owned_by_input()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var model = new Model(); model.Set("Open", true); var calls = new List<string>();
            using var loader = new CuiControlLoader(); loader.SetBindingContext(model); loader.SetActionDispatcher(new Actions(calls));
            var loaded = loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <Input Type="Text" Value="Name" FocusRequested="{Binding Open}"/>
                  <Input Type="Text" Value="Notes" AcceptsReturn="True"/>
                  <Button Content="Rename" Action="Confirm" DefaultAction="True" Active="{Binding Open}" Interactive="{Binding Allow}"/>
                  <Button Content="Cancel" Action="Cancel" CancelAction="True" Active="{Binding Open}" Interactive="{Binding Allow}"/>
                  <Button Content="Inactive form" Action="Inactive" DefaultAction="True" CancelAction="True" Active="False"/>
                </Container>
                """);
            Assert.Empty(loaded.Diagnostics); var root = Assert.IsType<CuiContainerControl>(loaded.Root); loader.WireBindings(root);
            var input = Assert.IsType<TextBox>(root.AuthoredChildren[0]); var notes = Assert.IsType<TextBox>(root.AuthoredChildren[1]);
            var window = new Window { Content = root, Width = 420, Height = 340 };
            try
            {
                window.Show(); Pump(window); Assert.True(input.IsFocused);
                PressKey(window, Key.Enter); await loader.WhenActionsIdleAsync();
                PressKey(window, Key.Escape); await loader.WhenActionsIdleAsync();
                Assert.Equal(new[] { "Confirm", "Cancel" }, calls);
                notes.Focus(); notes.CaretIndex = notes.Text!.Length;
                PressKey(window, Key.Enter); await loader.WhenActionsIdleAsync(); Assert.Equal(2, calls.Count);
                Assert.Contains('\n', notes.Text!);
                input.Focus(); model.Set("Allow", false); Pump(window);
                PressKey(window, Key.Enter); PressKey(window, Key.Escape); await loader.WhenActionsIdleAsync(); Assert.Equal(2, calls.Count);
                model.Set("Allow", true); model.Set("Open", false); Pump(window);
                PressKey(window, Key.Enter); PressKey(window, Key.Escape); await loader.WhenActionsIdleAsync(); Assert.Equal(2, calls.Count);
                model.Set("Open", true); Pump(window); PressKey(window, Key.Enter); await loader.WhenActionsIdleAsync();
                Assert.Equal(new[] { "Confirm", "Cancel", "Confirm" }, calls);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None));
    }

    private static void Pump(Window window)
    { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
    private static void PressKey(Window window, Key key)
    { window.KeyPress(key, Avalonia.Input.RawInputModifiers.None, PhysicalKey.None, null); window.KeyRelease(key, Avalonia.Input.RawInputModifiers.None, PhysicalKey.None, null); }
    private sealed class Actions(List<string> calls) : ICuiActionDispatcher
    {
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        { calls.Add(command); return ValueTask.CompletedTask; }
    }
    private sealed class Model : ICuiWritableBindingContext, INotifyPropertyChanged
    {
        private readonly Dictionary<string, object?> _values = new() { ["Open"] = true, ["Request"] = false, ["Hidden"] = false, ["Allow"] = true, ["LayerOpacity"] = 1d, ["Name"] = "Original name" };
        public string Name => (string)_values["Name"]!;
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool TryGetValue(string path, out object? value) => _values.TryGetValue(path, out value);
        public bool TrySetValue(string path, object? value) { Set(path, value); return true; }
        public void Set(string path, object? value) { _values[path] = value; Notify(); }
        public void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
