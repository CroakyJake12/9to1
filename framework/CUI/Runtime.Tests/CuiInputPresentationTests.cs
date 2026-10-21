using System.ComponentModel;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CakeOS.Cui;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Runtime coverage for the bounded typed input presentation capability (DisplayValue). The
/// friendly DisplayValue is shown while the input is unfocused; the raw writable Value is revealed
/// on focus so native edit/copy/selection/caret/undo operate on the canonical value; genuine user
/// edits are tracked so a later focus reveal never resurrects a stale value; and the state/handlers
/// are retired on reload/dispose.
/// </summary>
public sealed class CuiInputPresentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisplayValue_is_friendly_unfocused_raw_on_focus_and_restored_on_blur_and_refresh(bool displayFirst)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            const string raw = "9to1://spaces/selected/a1/chat/c2";
            const string friendly = "New conversation";
            var model = new Model(raw, friendly);
            using var loader = new CuiControlLoader(); loader.SetBindingContext(model);
            var markup = displayFirst ? """
                <Container Type="Vertical Stack">
                  <Input Type="Text" DisplayValue="{Binding AddressDisplay}" Value="{Binding Address, Mode=TwoWay}"/>
                  <Input Type="Text" Value="FocusTarget"/>
                </Container>
                """ : """
                <Container Type="Vertical Stack">
                  <Input Type="Text" Value="{Binding Address, Mode=TwoWay}" DisplayValue="{Binding AddressDisplay}"/>
                  <Input Type="Text" Value="FocusTarget"/>
                </Container>
                """;
            var loaded = loader.LoadMarkup(markup);
            Assert.Empty(loaded.Diagnostics);
            var root = Assert.IsType<CuiContainerControl>(loaded.Root);
            loader.WireBindings(root);
            var input = Assert.IsType<TextBox>(root.AuthoredChildren[0]);
            var target = Assert.IsType<TextBox>(root.AuthoredChildren[1]);
            var window = new Window { Content = root, Width = 420, Height = 220 };
            try
            {
                window.Show(); Pump(window);
                // Unfocused: the friendly display is what is rendered, never the GUID route.
                Assert.Equal(friendly, input.Text);
                Assert.Equal(raw, model.Address);
                // Focus reveals the canonical raw editable value (native edit/copy operate on it).
                input.Focus(); Pump(window);
                Assert.Equal(raw, input.Text);
                input.SelectAll();
                Assert.Equal(raw, input.SelectedText);
                // A genuine user edit is tracked and write-backs to the model.
                input.CaretIndex = input.Text!.Length;
                input.Text = "9to1://spaces";
                Pump(window);
                Assert.Equal("9to1://spaces", model.Address);
                // Blur restores the friendly display without corrupting the raw binding port.
                target.Focus(); Pump(window);
                Assert.Equal(friendly, input.Text);
                Assert.Equal("9to1://spaces", model.Address);
                // A host refresh re-applies the writable value but leaves the friendly unfocused view.
                model.Set("Address", raw); Pump(window);
                Assert.Equal(friendly, input.Text);
                Assert.Equal(raw, model.Address);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Reload_and_dispose_retire_input_presentation_state_and_handlers()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var model = new Model("9to1://spaces", "Spaces");
            var loader = new CuiControlLoader(); loader.SetBindingContext(model);
            var first = loader.LoadMarkup("""
                <Input Type="Text" Value="{Binding Address, Mode=TwoWay}" DisplayValue="{Binding AddressDisplay}"/>
                """);
            Assert.Empty(first.Diagnostics);
            var input = Assert.IsType<TextBox>(first.Root);
            loader.WireBindings(input);
            var window = new Window { Content = input, Width = 420, Height = 120 };
            window.Show(); Pump(window);
            Assert.Equal("Spaces", input.Text);
            // Reloading the loader retires the prior lineage and its presentation state.
            var second = loader.LoadMarkup("""
                <Input Type="Text" Value="{Binding Address, Mode=TwoWay}"/>
                """);
            Assert.Empty(second.Diagnostics);
            var secondInput = Assert.IsType<TextBox>(second.Root);
            loader.WireBindings(secondInput);
            window.Content = secondInput; Pump(window);
            Assert.Equal("9to1://spaces", secondInput.Text);
            Assert.Equal(0, InputPresentationCount(loader)); // no stale presentation state survives
            window.Close();
            // Disposal also fully retires input presentation state (handlers unsubscribed).
            loader.Dispose();
            Assert.Equal(0, InputPresentationCount(loader));
        }, CancellationToken.None);
    }

    private static int InputPresentationCount(CuiControlLoader loader)
    {
        var field = typeof(CuiControlLoader).GetField("_inputPresentation", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((System.Collections.IDictionary)field.GetValue(loader)!).Count;
    }

    private static void Pump(Window window)
    { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }

    private sealed class Model(string address, string display) : ICuiWritableBindingContext, INotifyPropertyChanged
    {
        public string Address { get; set; } = address;
        public string AddressDisplay => display;
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool TryGetValue(string path, out object? value)
        {
            value = path switch { "Address" => Address, "AddressDisplay" => AddressDisplay, _ => null };
            return value is not null;
        }
        public bool TrySetValue(string path, object? value)
        {
            if (path == "Address" && value is string text) { Address = text; Notify(); return true; }
            return false;
        }
        public void Set(string path, object? value)
        {
            if (path == "Address" && value is string text) Address = text;
            Notify();
        }
        private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
