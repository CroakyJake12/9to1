using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiFormattedTextInputTests
{
    [Fact]
    public async Task Canonical_typed_input_shapes_real_runs_and_keeps_native_selection_caret_and_editing()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var runs = new[]
            {
                new CuiFormattedTextRun("ab", FontWeight.Normal),
                new CuiFormattedTextRun("CD", FontWeight.Bold, FontStyle.Italic, FontSize: 24,
                    Underline: true, StrikeThrough: true, Baseline: BaselineAlignment.Superscript,
                    Foreground: Colors.Red, Background: Colors.Yellow)
            };
            var presentation = new CuiFormattedText("abCD", runs);
            runs[1] = new CuiFormattedTextRun("CD"); // snapshot owns its collection
            var model = new CuiViewModel(); model.Set("Text", "abCD"); model.Set("Rich", presentation);
            using var loader = new CuiControlLoader(); loader.SetBindingContext(model);
            var loaded = loader.LoadMarkup("""
                <Cui><Input Type="Text" Value="{Binding Text, Mode=TwoWay}" FormattedText="{Binding Rich}" /></Cui>
                """);
            Assert.Empty(loaded.Diagnostics);
            var input = Assert.IsType<TextBox>(loaded.Root);
            var window = new Window { Content = input, Width = 500, Height = 180 };
            try
            {
                window.Show(); input.Focus(); window.UpdateLayout();
                var presenter = Assert.Single(input.GetVisualDescendants().OfType<CuiFormattedTextPresenter>());
                Assert.Equal("abCD", LayoutText(presenter));
                Assert.Equal(FontWeight.Normal, At(presenter, 0).Typeface.Weight);
                var rich = At(presenter, 2);
                Assert.Equal(FontWeight.Bold, rich.Typeface.Weight); Assert.Equal(FontStyle.Italic, rich.Typeface.Style);
                Assert.Equal(24, rich.FontRenderingEmSize); Assert.Equal(BaselineAlignment.Superscript, rich.BaselineAlignment);
                Assert.Equal(Colors.Red, Assert.IsAssignableFrom<ISolidColorBrush>(rich.ForegroundBrush).Color);
                Assert.Equal(Colors.Yellow, Assert.IsAssignableFrom<ISolidColorBrush>(rich.BackgroundBrush).Color);
                Assert.Contains(rich.TextDecorations!, d => d.Location == TextDecorationLocation.Underline);
                Assert.Contains(rich.TextDecorations!, d => d.Location == TextDecorationLocation.Strikethrough);
                input.SelectionForegroundBrush = Brushes.Blue; input.SelectionStart = 4; input.SelectionEnd = 1;
                window.UpdateLayout();
                var selected = At(presenter, 2);
                Assert.Equal(FontWeight.Bold, selected.Typeface.Weight); Assert.Equal(FontStyle.Italic, selected.Typeface.Style);
                Assert.Equal(BaselineAlignment.Superscript, selected.BaselineAlignment);
                Assert.Null(selected.BackgroundBrush); // native selection is not hidden by run highlight
                Assert.Contains(selected.TextDecorations!, d => d.Location == TextDecorationLocation.Strikethrough);
                Assert.Equal(Colors.Blue, Assert.IsAssignableFrom<ISolidColorBrush>(selected.ForegroundBrush).Color);
                Assert.NotEmpty(presenter.TextLayout.HitTestTextRange(1, 3));
                var caret = presenter.TextLayout.HitTestTextPosition(2);
                Assert.Equal(2, presenter.TextLayout.HitTestPoint(new Point(caret.X, caret.Y + caret.Height / 2)).TextPosition);
                var anchor = input.SelectionStart; var end = input.SelectionEnd;
                var caretChanges = 0; presenter.CaretBoundsChanged += (_, _) => caretChanges++;
                model.Set("Rich", new CuiFormattedText("abCD", [new("abCD", FontWeight.Bold, FontSize: 40)]));
                window.UpdateLayout();
                Assert.Same(presenter, Assert.Single(input.GetVisualDescendants().OfType<CuiFormattedTextPresenter>()));
                Assert.Equal(anchor, input.SelectionStart); Assert.Equal(end, input.SelectionEnd);
                Assert.True(caretChanges > 0); // same native IME/caret geometry refreshes for changed metrics
                Assert.Equal(FontWeight.Bold, At(presenter, 0).Typeface.Weight);
                input.CaretIndex = 4; input.SelectionStart = 4; input.SelectionEnd = 4;
                window.KeyTextInput("!"); window.UpdateLayout();
                Assert.Equal("abCD!", input.Text); Assert.Equal("abCD!", model.Get("Text"));
                Assert.Equal("abCD!", LayoutText(presenter)); // stale snapshot never removes/duplicates input text
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Native_preedit_keeps_styled_surrounding_text_and_underlines_composition_without_model_mutation()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var input = new TextBox { Text = "abCD", FontWeight = FontWeight.Normal };
            var presentation = new CuiFormattedText("abCD", [new("ab", FontWeight.Bold, StrikeThrough: true), new("CD", FontWeight.Normal)]);
            CuiFormattedTextInput.SetPresentation(input, presentation);
            var window = new Window { Content = input, Width = 500, Height = 180 };
            try
            {
                window.Show(); input.Focus(); window.UpdateLayout();
                var presenter = Assert.Single(input.GetVisualDescendants().OfType<CuiFormattedTextPresenter>());
                input.CaretIndex = 2; input.SelectionStart = 2; input.SelectionEnd = 2;
                presenter.PreeditText = "xy"; window.UpdateLayout();
                Assert.Equal("abxyCD", LayoutText(presenter)); Assert.Equal("abCD", input.Text);
                Assert.Same(presentation, CuiFormattedTextInput.GetPresentation(input));
                Assert.Equal(FontWeight.Bold, At(presenter, 2).Typeface.Weight);
                Assert.Contains(At(presenter, 2).TextDecorations!, d => d.Location == TextDecorationLocation.Underline);
                Assert.Contains(At(presenter, 2).TextDecorations!, d => d.Location == TextDecorationLocation.Strikethrough);
                Assert.Equal(FontWeight.Normal, At(presenter, 4).Typeface.Weight);
                presenter.PreeditText = null; window.UpdateLayout();
                Assert.Equal("abCD", LayoutText(presenter)); Assert.Equal(2, input.CaretIndex);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Plain_stale_and_password_inputs_keep_stock_text_layout_and_no_stale_run_styles()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var input = new TextBox { Text = "plain", FontWeight = FontWeight.Normal };
            var window = new Window { Content = input, Width = 500, Height = 180 };
            try
            {
                window.Show(); window.UpdateLayout();
                var presenter = Assert.Single(input.GetVisualDescendants().OfType<CuiFormattedTextPresenter>());
                Assert.Equal("plain", LayoutText(presenter)); Assert.Equal(FontWeight.Normal, At(presenter, 0).Typeface.Weight);
                CuiFormattedTextInput.SetPresentation(input, new CuiFormattedText("other", [new("other", FontWeight.Bold)]));
                window.UpdateLayout(); Assert.Equal("plain", LayoutText(presenter));
                Assert.Equal(FontWeight.Normal, At(presenter, 0).Typeface.Weight);
                CuiFormattedTextInput.SetPresentation(input, new CuiFormattedText("plain", [new("plain", FontWeight.Bold)]));
                window.UpdateLayout(); Assert.Equal(FontWeight.Bold, At(presenter, 0).Typeface.Weight);
                input.PasswordChar = '*'; window.UpdateLayout();
                Assert.Equal("*****", LayoutText(presenter)); Assert.Equal(FontWeight.Normal, At(presenter, 0).Typeface.Weight);
                input.RevealPassword = true; window.UpdateLayout();
                Assert.Equal("plain", LayoutText(presenter)); Assert.Equal(FontWeight.Normal, At(presenter, 0).Typeface.Weight);
                input.PasswordChar = default; CuiFormattedTextInput.SetPresentation(input, null); window.UpdateLayout();
                Assert.Equal("plain", LayoutText(presenter)); Assert.Equal(FontWeight.Normal, At(presenter, 0).Typeface.Weight);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Invalid_run_partitions_and_unsupported_or_two_way_CUI_ports_fail_explicitly()
    {
        Assert.Throws<ArgumentException>(() => new CuiFormattedText("abcd", [new("abc")]));
        Assert.Throws<ArgumentException>(() => new CuiFormattedText("abcd", [new("ab"), new("XX")]));
        Assert.Throws<ArgumentException>(() => new CuiFormattedText("x", [new("x", FontSize: double.NaN)]));
        Assert.Throws<ArgumentException>(() => new CuiFormattedText("😀", [new("\ud83d"), new("\ude00")]));
        Assert.Empty(new CuiFormattedText("", []).Runs);
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            foreach (var (value, markup) in new (object?, string)[]
            {
                ("not typed", "<Cui><Input Type='Text' FormattedText='{Binding Rich}' /></Cui>"),
                (new CuiFormattedText("x", [new("x")]), "<Cui><Input Type='Text' FormattedText='{Binding Rich, Mode=TwoWay}' /></Cui>"),
                (null, "<Cui><Input Type='Text' FormattedText='literal' /></Cui>"),
                (new CuiFormattedText("x", [new("x")]), "<Cui><Input Type='Number' FormattedText='{Binding Rich}' /></Cui>")
            })
            {
                var model = new CuiViewModel(); model.Set("Rich", value);
                using var loader = new CuiControlLoader(); loader.SetBindingContext(model);
                var loaded = loader.LoadMarkup(markup);
                Assert.Contains(loaded.Diagnostics, diagnostic => diagnostic.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            }
        }, CancellationToken.None);
    }

    private static string LayoutText(CuiFormattedTextPresenter presenter) =>
        string.Concat(presenter.TextLayout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().Select(run => run.Text.ToString()));

    private static TextRunProperties At(CuiFormattedTextPresenter presenter, int index)
    {
        var offset = 0;
        foreach (var run in presenter.TextLayout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>())
        {
            if (index < offset + run.Length) return run.Properties;
            offset += run.Length;
        }
        throw new InvalidOperationException("Expected character was absent from the actual shaped layout.");
    }
}
