using CakeOS.Cui.Language;
using Haven.CUI.DevTools;
using Xunit;

namespace Haven.CUI.DevTools.Tests;

public sealed class CuiFormatterTests
{
    private static readonly CuiFormatter Formatter = new();

    [Fact]
    public void Format_is_deterministic_and_idempotent()
    {
        var source = "<Cui>\n    <Page>\n  <Container Type=\"Grid\">\n\t\t<Text>Hello</Text>\n    </Container>\n    </Page>\n</Cui>\n";

        var first = Formatter.Format(source, "app.cui");
        var second = Formatter.Format(source, "app.cui");
        var third = Formatter.Format(first.Output, "app.cui");

        Assert.Equal(first.Output, second.Output);
        Assert.Equal(first.Output, third.Output);
        Assert.Equal(
            "<Cui>\n    <Page>\n        <Container Type=\"Grid\">\n            <Text>Hello</Text>\n        </Container>\n    </Page>\n</Cui>\n",
            first.Output);
    }

    [Fact]
    public void Text_bearing_content_is_not_rewritten()
    {
        var result = Formatter.Format("<Cui>\n    <Page>\n        <Text>Hello   world</Text>\n    </Page>\n</Cui>\n", "app.cui");

        Assert.DoesNotContain(result.Diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);
        Assert.Contains("<Text>Hello   world</Text>", result.Output);
    }

    [Fact]
    public void Preserve_whitespace_policy_keeps_exact_authored_whitespace()
    {
        var result = Formatter.Format("<Cui>\n    <Page>\n        <Text Whitespace:Preserve>\n  kept\n</Text>\n    </Page>\n</Cui>\n", "app.cui");

        Assert.Contains("<Text Whitespace:Preserve>\n  kept\n</Text>", result.Output);
    }

    [Fact]
    public void Quoted_values_are_not_converted_to_shorthand()
    {
        var result = Formatter.Format("<Cui>\n    <Page>\n        <Container Type=\"Vertical_Stack\" />\n    </Page>\n</Cui>\n", "app.cui");

        Assert.Contains("Type=\"Vertical_Stack\"", result.Output);
    }

    [Fact]
    public void Comments_are_preserved_verbatim()
    {
        var result = Formatter.Format("<Cui>\n    <Page>\n        <# shared Dulche AI bar #>\n        <Text id=\"t\">Hello</Text>\n    </Page>\n</Cui>\n", "app.cui");

        Assert.Contains("<# shared Dulche AI bar #>", result.Output);
    }

    [Fact]
    public void Legacy_markup_is_rejected()
    {
        var result = Formatter.Format("<Page><Text>Hello</Text></Page>", "legacy.axaml");

        Assert.Equal(string.Empty, result.Output);
        Assert.Single(result.Diagnostics, item => item.Code == "CUIF001");
    }

    [Fact]
    public void Malformed_source_produces_no_output()
    {
        var result = Formatter.Format("<Cui>\n    <Page>\n        <# unterminated\n        <Text id=\"t\">Hello</Text>\n    </Page>\n</Cui>\n", "app.cui");

        Assert.Equal(string.Empty, result.Output);
        Assert.Single(result.Diagnostics, item => item.Code == "CUIF002");
    }

    [Fact]
    public void Formatting_does_not_change_the_semantic_document()
    {
        var source = "<Cui>\n    <Resources>\n        <Resource Key=\"accent\" Value=\"Blue\" />\n    </Resources>\n    <Page id=\"home\">\n        <# shared Dulche AI bar #>\n        <Container Type:Grid>\n            <Text>Hello   world</Text>\n            <Button id=\"save\" action=\"save\" />\n        </Container>\n    </Page>\n</Cui>\n";

        var parser = new CuiRichParser();
        var before = parser.Parse(source, "app.cui");
        var result = Formatter.Format(source, "app.cui");
        var after = new CuiRichParser().Parse(result.Output, "app.cui");

        Assert.DoesNotContain(result.Diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);
        Assert.Equal(before.Components.Select(item => item.StableId).Order(StringComparer.Ordinal),
            after.Components.Select(item => item.StableId).Order(StringComparer.Ordinal));
        Assert.Equal(before.Resources.Keys.Order(StringComparer.Ordinal), after.Resources.Keys.Order(StringComparer.Ordinal));
    }
}