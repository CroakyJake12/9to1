using System.Security;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// The registered Markdown Object renders structured content through the canonical
/// ten-element language. It retains the actual renderer, binding and streaming path.
/// </summary>
public sealed class CuiMarkdownElementTests
{
    private const string SampleMarkdown =
        "# Heading\n\nSome **bold** and `inline code` and a [link](https://example.com).\n\n" +
        "- one\n- two\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n> a quote\n\n" +
        "```csharp\nvar x = 1;\n```";

    private static Control Load(string markup, out CuiControlLoader loader)
    {
        loader = new CuiControlLoader();
        loader.SetBindingContext(new CuiViewModel());
        var (tree, diagnostics) = loader.LoadMarkup(markup);
        Assert.DoesNotContain(diagnostics,
            diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
        return Assert.IsAssignableFrom<Control>(tree);
    }

    // Literal XML attribute whitespace is normalized by the parser. Character references
    // preserve the Markdown line boundaries that an authored multiline value requires.
    private static string AuthoredMarkdown(string text) =>
        "<Cui><Object Type=\"Markdown\" id=\"body\" text=\"" + SecurityElement.Escape(text)!
            .Replace("\r", "&#13;").Replace("\n", "&#10;").Replace("\t", "&#9;") + "\" /></Cui>";

    /// <summary>The element may be the authored root, so search self-or-descendants.</summary>
    private static CuiMarkdownView MarkdownOf(Control root)
    {
        if (root is CuiMarkdownView itself) return itself;
        return root.GetVisualDescendants().OfType<CuiMarkdownView>().Single();
    }

    [Fact]
    public void Markdown_is_a_registered_object_type_not_an_extra_primitive()
    {
        Assert.DoesNotContain(CuiControlRegistry.Default.Elements, element => element.Name == "Markdown");
        Assert.True(CuiControlRegistry.Default.IsRegisteredRuntimeType("Markdown"));
        Assert.Equal(10, CuiControlRegistry.Default.Elements.Count(element => element.IsVisual));
        Assert.True(CuiControlRegistry.Default.TryResolveElement("Object", out var descriptor));
        Assert.Contains("Text", descriptor.AllowedProperties);
    }

    [Fact]
    public async Task Authored_markdown_renders_structure_not_one_opaque_block()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var root = Load(AuthoredMarkdown(SampleMarkdown), out var loader);
            var window = new Window { Content = root, Width = 800, Height = 900 };
            try
            {
                window.Show(); window.UpdateLayout();
                var markdown = MarkdownOf(root);
                Assert.Equal(SampleMarkdown, markdown.Text);
                var controls = markdown.GetVisualDescendants().OfType<Control>().ToArray();
                var heading = Assert.Single(controls.OfType<TextBlock>(), block => block.Text == "Heading");
                Assert.Equal(FontWeight.SemiBold, heading.FontWeight);
                Assert.True(heading.Bounds.Width > 0 && heading.Bounds.Height > 0);

                var runs = controls.OfType<TextBlock>()
                    .SelectMany(block => block.Inlines?.OfType<Run>() ?? Enumerable.Empty<Run>()).ToArray();
                Assert.Contains(runs, run => run.Text == "bold" && run.FontWeight == FontWeight.SemiBold);
                Assert.Contains(runs, run => run.Text == "inline code");
                Assert.Contains(runs, run => run.Text == "link");
                Assert.Contains(controls.OfType<TextBlock>(), block => block.Text == "one");
                Assert.Contains(controls.OfType<TextBlock>(), block => block.Text == "two");
                Assert.Equal(2, controls.OfType<TextBlock>().Count(block => block.Text == "•"));
                Assert.Contains(controls.OfType<TextBlock>(),
                    block => block.Text == "a quote" && block.FontStyle == FontStyle.Italic);

                // Table cells must occupy distinct rows and columns, not an opaque paragraph.
                var table = Assert.Single(controls.OfType<ScrollViewer>(),
                    viewer => viewer.Content is Grid { RowDefinitions.Count: 2, ColumnDefinitions.Count: 2 });
                var grid = Assert.IsType<Grid>(table.Content);
                AssertTableCell(grid, 0, 0, "a");
                AssertTableCell(grid, 0, 1, "b");
                AssertTableCell(grid, 1, 0, "1");
                AssertTableCell(grid, 1, 1, "2");
                var code = Assert.Single(controls.OfType<TextBox>());
                Assert.Equal("var x = 1;", code.Text);
                Assert.True(code.IsReadOnly);
                return true;
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); window.Close(); }
        }, lifetime.Token);
    }

    [Fact]
    public async Task Updating_bound_text_rebuilds_the_rendered_document()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var model = new CuiViewModel();
            model.Set("Body", "first paragraph");

            var loader = new CuiControlLoader();
            loader.SetBindingContext(model);
            var window = new Window { Width = 800, Height = 600 };
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup(
                    "<Cui><Object Type=\"Markdown\" id=\"body\" text=\"{Binding Body}\" FontFamily=\"Montserrat\" /></Cui>");
                Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
                var root = Assert.IsAssignableFrom<Control>(tree);
                loader.WireBindings(root);
                window.Content = root; window.Show(); window.UpdateLayout();

                var markdown = MarkdownOf(root);
                Assert.Equal("Montserrat", markdown.FontFamily.Name);
                var original = Assert.Single(markdown.GetVisualDescendants().OfType<TextBlock>());
                Assert.Equal("first paragraph", original.Text);
                Assert.True(original.Bounds.Width > 0 && original.Bounds.Height > 0);

                // This is the streaming path: a partial document must render, not be withheld
                // until it is complete.
                model.Set("Body", "first paragraph\n\n## Growing\n\n- item");
                await loader.WhenActionsIdleAsync();
                window.UpdateLayout();

                Assert.Same(markdown, MarkdownOf(root));
                var updated = markdown.GetVisualDescendants().OfType<TextBlock>().ToArray();
                Assert.DoesNotContain(original, updated);
                Assert.Contains(updated, block => block.Text == "first paragraph");
                Assert.Contains(updated, block => block.Text == "Growing" && block.FontWeight == FontWeight.SemiBold);
                Assert.Contains(updated, block => block.Text == "item");
                Assert.Single(updated, block => block.Text == "•");
                return true;
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); window.Close(); }
        }, lifetime.Token);
    }

    [Fact]
    public async Task Registered_markdown_preserves_current_code_action_requests_after_live_updates()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var model = new CuiViewModel();
            model.Set("Body", "```csharp\nvar first = 1;\n```");
            var loader = new CuiControlLoader();
            loader.SetBindingContext(model);
            var window = new Window { Width = 800, Height = 600 };
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup(
                    "<Cui><Object Type=\"Markdown\" id=\"body\" text=\"{Binding Body}\" /></Cui>");
                Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
                var root = Assert.IsAssignableFrom<Control>(tree);
                loader.WireBindings(root);
                window.Content = root; window.Show(); window.UpdateLayout();
                var markdown = MarkdownOf(root);
                var requests = new List<CuiMarkdownCodeActionRequest>();
                markdown.CodeActionRequested += requests.Add;
                var original = Assert.Single(markdown.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Copy"));
                original.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var first = Assert.Single(requests);
                Assert.Equal(CuiMarkdownCodeAction.Copy, first.Action);
                Assert.Equal("csharp", first.Language);
                Assert.Equal("var first = 1;", first.Code);
                Assert.True(markdown.IsCurrentOriginalCodeAction(first));

                model.Set("Body", "```csharp\nvar second = 2;\n```");
                await loader.WhenActionsIdleAsync();
                window.UpdateLayout();
                Assert.False(markdown.IsCurrentOriginalCodeAction(first));
                original.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Single(requests);
                var current = Assert.Single(markdown.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Ask to run"));
                current.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(2, requests.Count);
                Assert.Equal(CuiMarkdownCodeAction.AskToRun, requests[1].Action);
                Assert.Equal("var second = 2;", requests[1].Code);
                Assert.True(markdown.IsCurrentOriginalCodeAction(requests[1]));
                return true;
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); window.Close(); }
        }, lifetime.Token);
    }

    [Fact]
    public async Task An_unterminated_code_fence_still_renders_what_has_arrived()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            // Mid-stream, a fenced block is almost always unterminated. If that produced no
            // output the transcript would appear frozen until the model finished.
            const string partial = "before\n\n```csharp\nvar x = 1;";
            var root = Load(AuthoredMarkdown(partial), out var loader);
            var window = new Window { Content = root, Width = 800, Height = 600 };
            try
            {
                window.Show(); window.UpdateLayout();
                var markdown = MarkdownOf(root);
                Assert.Equal(partial, markdown.Text);
                var controls = markdown.GetVisualDescendants().OfType<Control>().ToArray();

                Assert.Contains(controls.OfType<TextBlock>(), block => block.Text == "before");
                Assert.Contains(controls.OfType<TextBlock>(), block => block.Text == "csharp");
                var code = Assert.Single(controls.OfType<TextBox>());
                Assert.Equal("var x = 1;", code.Text);
                Assert.True(code.IsReadOnly);
                Assert.True(code.Bounds.Width > 0 && code.Bounds.Height > 0);
                return true;
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); window.Close(); }
        }, lifetime.Token);
    }

    private static void AssertTableCell(Grid table, int row, int column, string expected)
    {
        var cell = Assert.Single(table.Children, child => Grid.GetRow(child) == row && Grid.GetColumn(child) == column);
        var border = Assert.IsType<Border>(cell);
        Assert.Equal(expected, Assert.IsType<TextBlock>(border.Child).Text);
    }
}
