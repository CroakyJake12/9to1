using CakeOS.Cui;
using CakeOS.Cui.Language;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperCuiPropertyEditorTests
{
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
