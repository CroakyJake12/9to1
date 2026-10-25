using CakeOS.Cui;
using CakeOS.Cui.Language;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperCuiPropertyEditorTests
{
    [Fact]
    public void Text_child_insertion_preserves_every_character_outside_exact_closing_anchor_and_compiles_literal_entities()
    {
        const string prefix = "<Cui version='1'>\r\n<!-- keep <Container id='fake' /> exactly -->\r\n<Page id='page'><Container id='box' Type='Vertical Stack' margin='9' accessible-name='literal /> &amp; >'>\r\n<Text id='keep' text='A &amp; B' />\r\n";
        const string suffix = "</Container></Page>\r\n</Cui>";
        const string value = "New < & \" ' \t\r\n😀";
        var source = prefix + suffix; var inspection = DeveloperCuiPropertyEditor.Inspect("Main.cui", source, "box");
        var changed = DeveloperCuiPropertyEditor.AddTextChild(inspection, source, "new-child", value);
        Assert.Null(changed.Error); Assert.StartsWith(prefix, changed.Text); Assert.EndsWith(suffix, changed.Text);
        Assert.Equal(source, changed.Text.Remove(prefix.Length, changed.Text.Length - source.Length));
        var preview = DeveloperCuiDraftPreviewCompiler.Compile("Main.cui", changed.Text);
        Assert.True(preview.Succeeded, string.Join(Environment.NewLine, preview.Diagnostics));
        var parent = Assert.Single(Assert.Single(preview.Document!.Components).Children);
        var inserted = Assert.Single(parent.Children, node => node.AuthoredId == "new-child");
        Assert.True(inserted.TryGetLiteralAttribute("text", out var actual)); Assert.Equal(value, actual);
        Assert.Equal("keep", parent.Children[0].AuthoredId); Assert.Equal(2, parent.Children.Count);
    }

    [Fact]
    public void Empty_parent_expansion_changes_only_original_slash_close_and_preserves_quoted_fake_boundaries()
    {
        const string prefix = "<Cui><Page><Container id='box' Type='Vertical Stack' accessible-name='literal /> &amp; >' ";
        const string suffix = "</Page><!-- trailing -->\r\n</Cui>";
        var source = prefix + "/>" + suffix;
        var inspection = DeveloperCuiPropertyEditor.Inspect("Main.cui", source, "box");
        var changed = DeveloperCuiPropertyEditor.AddTextChild(inspection, source, "new-child", "Child");
        Assert.Null(changed.Error); Assert.Equal(prefix + "><Text id=\"new-child\" text=\"Child\" /></Container>" + suffix, changed.Text);
        Assert.True(DeveloperCuiDraftPreviewCompiler.Compile("Main.cui", changed.Text).Succeeded);
    }

    [Fact]
    public void Text_child_refuses_stale_inspection_duplicate_child_and_ambiguous_parent_without_source_loss()
    {
        const string source = "<Cui><Page><Container id='box' Type='Vertical Stack'><Text id='already' text='Keep' /></Container></Page></Cui>";
        var inspection = DeveloperCuiPropertyEditor.Inspect("Main.cui", source, "box");
        var stale = DeveloperCuiPropertyEditor.AddTextChild(inspection, source + " ", "new-child", "Child");
        Assert.NotNull(stale.Error); Assert.Equal(source + " ", stale.Text);
        var duplicate = DeveloperCuiPropertyEditor.AddTextChild(inspection, source, "already", "Child");
        Assert.Contains("already exists", duplicate.Error!); Assert.Equal(source, duplicate.Text);
        const string ambiguous = "<Cui><Page><Container id='box' Type='Vertical Stack' /><Container id='box' Type='Vertical Stack' /></Page></Cui>";
        var refused = DeveloperCuiPropertyEditor.AddTextChild(DeveloperCuiPropertyEditor.Inspect("Main.cui", ambiguous, "box"), ambiguous, "new-child", "Child");
        Assert.NotNull(refused.Error); Assert.Equal(ambiguous, refused.Text);
    }

    [Fact]
    public void Text_child_proposed_source_must_pass_maintained_compiler_before_adoption()
    {
        const string source = "<Cui><Page><Container id='box' Type='Vertical Stack'><Text text='Keep' BogusDesignerProperty='x' /></Container></Page></Cui>";
        var inspection = DeveloperCuiPropertyEditor.Inspect("Main.cui", source, "box");
        Assert.Null(inspection.UnavailableReason);
        var refused = DeveloperCuiPropertyEditor.AddTextChild(inspection, source, "new-child", "Child");
        Assert.Contains("did not compile", refused.Error!); Assert.Equal(source, refused.Text);
    }

    [Fact]
    public void Text_child_rejects_bindings_invalid_ID_unsupported_parent_and_exact_cancelled_request()
    {
        const string source = "<Cui><Page><Text id='title' text='Keep' /></Page></Cui>";
        var parent = DeveloperCuiPropertyEditor.Inspect("Main.cui", source, "title");
        foreach (var (id, text) in new[] { ("valid", "{Binding Secret}"), ("invalid id", "Child"), ("valid", "Child") })
        {
            var result = DeveloperCuiPropertyEditor.AddTextChild(parent, source, id, text);
            Assert.NotNull(result.Error); Assert.Equal(source, result.Text);
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>((Action)(() => { _ = DeveloperCuiPropertyEditor.AddTextChild(parent, source, "valid", "Child", cancelled.Token); }));
    }

    [Fact]
    public void Text_child_refuses_deep_or_oversized_trees_and_shorthand_without_rewriting_them()
    {
        var sources = new[]
        {
            "<Cui><Page id='page'>" + string.Concat(Enumerable.Repeat("<Container Type='Vertical Stack'>", 34)) + string.Concat(Enumerable.Repeat("</Container>", 34)) + "</Page></Cui>",
            "<Cui><Page id='page'>" + string.Concat(Enumerable.Repeat("<Text text='Keep' />", 500)) + "</Page></Cui>",
            "<Cui><Page id='page'><Text #title text='Keep' /></Page></Cui>"
        };
        foreach (var source in sources)
        {
            var inspection = DeveloperCuiPropertyEditor.Inspect("Main.cui", source, "page");
            var result = DeveloperCuiPropertyEditor.AddTextChild(inspection, source, "new-child", "Child");
            Assert.NotNull(result.Error); Assert.Equal(source, result.Text);
        }
    }

    [Theory]
    [InlineData('\'')]
    [InlineData('"')]
    public void Exact_literal_edit_preserves_comments_entities_quotes_spacing_and_other_source_characters(char quote)
    {
        var prefix = "<Cui>\r\n<!-- keep & raw comment spelling -->\r\n<Page><Text id='title'  text=" + quote;
        const string original = "A &amp; B &quot;quoted&quot;";
        var suffix = quote + "  font-size='18' accessible-name='keep &amp; this' /></Page>\r\n</Cui>";
        var source = prefix + original + suffix;
        var inspection = DeveloperCuiPropertyEditor.Inspect("Main.cui", source, "title");
        Assert.Null(inspection.UnavailableReason);
        var text = Assert.Single(inspection.Properties, property => property.Name == "text");
        Assert.True(text.CanEdit, text.UnavailableReason); Assert.Equal("A & B \"quoted\"", text.Value);
        const string value = "New < & \" ' \t\r\n😀";
        var changed = DeveloperCuiPropertyEditor.Change(inspection, source, "text", value);
        Assert.Null(changed.Error); Assert.StartsWith(prefix, changed.Text); Assert.EndsWith(suffix, changed.Text);
        var parser = new CuiRichParser(); var document = parser.Parse(changed.Text, "Main.cui");
        Assert.DoesNotContain(parser.Diagnostics.Diagnostics, problem => problem.Severity == CuiDiagnosticSeverity.Error);
        var again = DeveloperCuiPropertyEditor.Inspect("Main.cui", changed.Text, "title");
        Assert.Equal(value, Assert.Single(again.Properties, property => property.Name == "text").Value);
        Assert.Equal("18", Assert.Single(again.Properties, property => property.Name == "font-size").Value);
        Assert.Equal("keep & this", Assert.Single(again.Properties, property => property.Name == "accessible-name").Value);
        Assert.NotEmpty(document.Components);
    }

    [Fact]
    public void Stale_source_and_ambiguous_ids_are_refused_without_changing_any_source()
    {
        const string source = "<Cui><Page><Text id='title' text='before' /></Page></Cui>";
        var inspection = DeveloperCuiPropertyEditor.Inspect("Main.cui", source, "title");
        var stale = source.Replace("before", "external", StringComparison.Ordinal);
        var refused = DeveloperCuiPropertyEditor.Change(inspection, stale, "text", "new");
        Assert.Equal(stale, refused.Text); Assert.Contains("changed after inspection", refused.Error!);
        var duplicates = source.Replace("</Page>", "<Text id='title' text='other' /></Page>", StringComparison.Ordinal);
        var ambiguous = DeveloperCuiPropertyEditor.Inspect("Main.cui", duplicates, "title");
        Assert.Contains("unique authored id", ambiguous.UnavailableReason!); Assert.Empty(ambiguous.Properties);
        Assert.Equal(duplicates, DeveloperCuiPropertyEditor.Change(ambiguous, duplicates, "text", "new").Text);
    }

    [Fact]
    public void Bindings_shorthand_identity_changes_and_invalid_typed_values_stay_code_defined()
    {
        const string source = "<Cui><Page><Text id='title' text='{Binding Title}' font-size='18' /></Page></Cui>";
        var inspection = DeveloperCuiPropertyEditor.Inspect("Main.cui", source, "title");
        var binding = Assert.Single(inspection.Properties, property => property.Name == "text");
        Assert.False(binding.CanEdit); Assert.Contains("Bindings", binding.UnavailableReason!);
        Assert.NotNull(DeveloperCuiPropertyEditor.Change(inspection, source, "text", "new").Error);
        Assert.NotNull(DeveloperCuiPropertyEditor.Change(inspection, source, "id", "different").Error);
        foreach (var invalid in new[] { "-3", "NaN", "{Binding Size}", "huge" })
        {
            var result = DeveloperCuiPropertyEditor.Change(inspection, source, "font-size", invalid);
            Assert.NotNull(result.Error); Assert.Equal(source, result.Text);
        }
        const string shorthand = "<Cui><Page><Text#title text:Hello /></Page></Cui>";
        var unsupported = DeveloperCuiPropertyEditor.Inspect("Main.cui", shorthand, "title");
        Assert.Contains("shorthand", unsupported.UnavailableReason!);
        Assert.Equal(shorthand, DeveloperCuiPropertyEditor.Change(unsupported, shorthand, "text", "new").Text);
    }

    [Fact]
    public void Cancellation_and_unchanged_values_preserve_the_complete_original_text()
    {
        const string source = "<Cui><Page><Text id='title' text='A &amp; B' /></Page></Cui>";
        var inspection = DeveloperCuiPropertyEditor.Inspect("Main.cui", source, "title");
        Assert.Equal(source, DeveloperCuiPropertyEditor.Change(inspection, source, "text", "A & B").Text);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => DeveloperCuiPropertyEditor.Change(inspection, source, "text", "new", cancellation.Token));
    }
}
