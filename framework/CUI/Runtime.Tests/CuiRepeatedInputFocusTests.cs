using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiRepeatedInputFocusTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fresh_same_key_rows_keep_per_character_typing_focus_and_middle_caret(bool notifyAll)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var model = new ReplacingRows(notifyAll);
            using var loader = new CuiControlLoader(); loader.SetBindingContext(model);
            var loaded = loader.LoadMarkup("""
                <Container Type="Vertical Stack"><Repeat Source="{Binding Rows}" As="row" Key="{Binding row.Id}">
                  <Input Type="Text" Value="{Binding row.Text, Mode=TwoWay}" />
                </Repeat></Container>
                """);
            Assert.Empty(loaded.Diagnostics); var root = loaded.Root!; loader.WireBindings(root);
            var window = new Window { Width = 500, Height = 400, Content = root };
            window.Show(); window.UpdateLayout();
            try
            {
                var editor = root.GetVisualDescendants().OfType<TextBox>().First();
                var detached = 0; editor.DetachedFromVisualTree += (_, _) => detached++;
                var stale = model.LastRows[0];
                editor.Focus(); editor.SelectAll();
                foreach (var letter in "Design review")
                {
                    window.KeyTextInput(letter.ToString());
                    Assert.True(editor.IsFocused);
                    Assert.Equal(editor.Text!.Length, editor.CaretIndex);
                    Assert.Same(editor, root.GetVisualDescendants().OfType<TextBox>().First());
                }
                Assert.Equal("Design review", model.Text);
                Assert.False(stale.TrySetValue("Text", "stale generation"));
                editor.CaretIndex = 6;
                foreach (var letter in " complete") window.KeyTextInput(letter.ToString());
                Assert.Equal("Design complete review", model.Text);
                Assert.Equal(15, editor.CaretIndex);
                Assert.Equal(0, detached);

                editor.SelectionStart = 0; editor.SelectionEnd = 6;
                model.Reverse(); window.UpdateLayout();
                Assert.Same(editor, root.GetVisualDescendants().OfType<TextBox>().Last());
                Assert.True(editor.IsFocused); Assert.Equal(0, editor.SelectionStart); Assert.Equal(6, editor.SelectionEnd);
                Assert.Equal(0, detached);
                model.RemoveEditedRow(); window.UpdateLayout();
                Assert.False(editor.IsFocused); Assert.Equal(1, detached);
                editor.Text = "detached stale input";
                Assert.Equal("Design complete review", model.Text);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fresh_same_key_numeric_rows_accept_negative_fraction_character_by_character(bool notifyAll)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var model = new ReplacingRows(notifyAll);
            using var loader = new CuiControlLoader(); loader.SetBindingContext(model);
            var loaded = loader.LoadMarkup("""
                <Container Type="Vertical Stack"><Repeat Source="{Binding Rows}" As="row" Key="{Binding row.Id}">
                  <Input Type="Number" Value="{Binding row.Number, Mode=TwoWay}" />
                </Repeat></Container>
                """);
            Assert.Empty(loaded.Diagnostics); var root = loaded.Root!; loader.WireBindings(root);
            var window = new Window { Width = 500, Height = 400, Content = root };
            window.Show(); window.UpdateLayout();
            try
            {
                var number = root.GetVisualDescendants().OfType<NumericUpDown>().First();
                var editor = Assert.Single(number.GetVisualDescendants().OfType<TextBox>());
                var detached = 0; editor.DetachedFromVisualTree += (_, _) => detached++;
                editor.Focus(); editor.SelectAll();
                foreach (var letter in "-3.5")
                {
                    window.KeyTextInput(letter.ToString());
                    Assert.True(editor.IsFocused);
                    Assert.Same(number, root.GetVisualDescendants().OfType<NumericUpDown>().First());
                }
                Assert.Equal(-3.5m, model.Number); Assert.Equal(-3.5m, number.Value);
                Assert.Equal("-3.5", editor.Text); Assert.Equal(4, editor.CaretIndex); Assert.Equal(0, detached);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private sealed class ReplacingRows(bool notifyAll) : ICuiBindingContext, INotifyPropertyChanged
    {
        private int _generation;
        private string[] _ids = ["edited", "other"];
        public string Text { get; private set; } = "Before";
        public decimal Number { get; private set; } = 1;
        public Row[] LastRows { get; private set; } = [];
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool TryGetValue(string path, out object? value)
        {
            if (path != "Rows") { value = null; return false; }
            var generation = _generation;
            LastRows = _ids.Select(id => new Row(id, id == "edited" ? Text : "Other", id == "edited" ? Number : 2, (property, next) =>
            {
                if (generation != _generation || !_ids.Contains(id)) return false;
                if (id != "edited") return false;
                if (property == "Text" && next is string text) Text = text;
                else if (property == "Number") Number = Convert.ToDecimal(next, CultureInfo.InvariantCulture);
                else return false;
                Changed(); return true;
            })).ToArray();
            value = LastRows; return true;
        }
        public void Reverse() { Array.Reverse(_ids); Changed(); }
        public void RemoveEditedRow() { _ids = ["other"]; Changed(); }
        private void Changed() { _generation++; PropertyChanged?.Invoke(this, new(notifyAll ? null : "Rows")); }
    }

    private sealed record Row(string Id, string Text, decimal Number, Func<string, object?, bool> Write) : ICuiWritableBindingContext
    {
        public bool TryGetValue(string path, out object? value)
        { value = path switch { "Id" => Id, "Text" => Text, "Number" => Number, _ => null }; return path is "Id" or "Text" or "Number"; }
        public bool TrySetValue(string path, object? value) => Write(path, value);
    }
}
