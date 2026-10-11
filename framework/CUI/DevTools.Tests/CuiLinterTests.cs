using CakeOS.Cui;
using CakeOS.Cui.Language;
using Haven.CUI.DevTools;
using Xunit;

namespace Haven.CUI.DevTools.Tests;

public sealed class CuiLinterTests
{
    private static readonly CuiLinter Linter = new();

    private static CuiDocument Parse(string source) => new CuiRichParser().Parse(source, "lint.cui");

    [Fact]
    public void Nested_comment_marker_is_reported()
    {
        var source = "<Cui>\n    <Page>\n        <# outer <# inner #> #>\n        <Text id=\"t\">Hello</Text>\n    </Page>\n</Cui>\n";
        var finding = Assert.Single(Linter.Lint(source, Parse(source)), item => item.Rule.Code == "CUIL001");

        Assert.Equal(3, finding.Diagnostic.Span.Start.Line);
        Assert.Equal("lint.cui", finding.Diagnostic.Span.SourceName);
        Assert.Contains("do not nest", finding.SuggestedCorrection);
    }

    [Fact]
    public void Underscored_and_spaced_symbol_in_the_same_scope_is_ambiguous()
    {
        var source = "<Cui>\n    <Templates>\n        <Template name=\"Vertical_Stack\"><Text id=\"a\">a</Text></Template>\n        <Template name=\"Vertical Stack\"><Text id=\"b\">b</Text></Template>\n    </Templates>\n    <Page>\n        <Vertical_Stack />\n        <Vertical Stack />\n    </Page>\n</Cui>\n";
        var finding = Assert.Single(Linter.Lint(source, Parse(source)), item => item.Rule.Code == "CUIL002");

        Assert.Equal("Vertical_Stack", finding.Diagnostic.Message.Split('\'')[1]);
        Assert.Contains("ambiguous", finding.SuggestedCorrection);
    }

    [Fact]
    public void Case_only_user_symbol_collision_is_reported()
    {
        var source = "<Cui>\n    <Prefab (Definition) Type=\"dynamic\" ID=\"AIBar\"><Text id=\"a\">x</Text></Prefab>\n    <Prefab (Definition) Type=\"dynamic\" ID=\"aibar\"><Text id=\"b\">y</Text></Prefab>\n    <Page>\n        <AIBar />\n        <aibar />\n    </Page>\n</Cui>\n";
        var findings = Linter.Lint(source, Parse(source));
        var reported = findings.Where(item => item.Rule.Code == "CUIL003").ToArray();

        Assert.Equal(2, reported.Length);
        Assert.Contains(reported, item => item.Diagnostic.Message.Contains("AIBar", StringComparison.Ordinal));
        Assert.Contains(reported, item => item.Diagnostic.Message.Contains("aibar", StringComparison.Ordinal));
    }

    [Fact]
    public void Definition_that_is_never_instantiated_is_reported()
    {
        var source = "<Cui>\n    <Paragraph (Definition) ID=\"Callout\"><Text id=\"t\">Reusable</Text></Paragraph>\n    <Page>\n        <Text id=\"x\">Hello</Text>\n    </Page>\n</Cui>\n";
        var finding = Assert.Single(Linter.Lint(source, Parse(source)), item => item.Rule.Code == "CUIL004");

        Assert.Contains("Callout", finding.Diagnostic.Message);
    }

    [Fact]
    public void Prefab_binding_to_an_undeclared_input_captures_caller_state()
    {
        var source = "<Cui>\n    <Prefab (Definition) Type=\"dynamic\" ID=\"AIBar\">\n        <Variable (Definition) Type=\"string\" ID=\"Title\" />\n        <Text id=\"t\" Text=\"{Binding CallerState, mode=OneWay}\" />\n    </Prefab>\n    <Page>\n        <AIBar />\n    </Page>\n</Cui>\n";
        var finding = Assert.Single(Linter.Lint(source, Parse(source)), item => item.Rule.Code == "CUIL005");

        Assert.Contains("CallerState", finding.Diagnostic.Message);
    }

    [Fact]
    public void Static_prefab_rejects_per_instance_mutable_input()
    {
        var source = "<Cui>\n    <Prefab (Definition) Type=\"static\" ID=\"AIBar\">\n        <Variable (Definition) Type=\"string\" ID=\"Title\" />\n        <Text id=\"t\" Text=\"{Binding Title, mode=TwoWay}\" />\n    </Prefab>\n    <Page>\n        <AIBar />\n    </Page>\n</Cui>\n";
        var finding = Assert.Single(Linter.Lint(source, Parse(source)), item => item.Rule.Code == "CUIL006");

        Assert.Contains("Title", finding.Diagnostic.Message);
    }

    [Fact]
    public void Action_without_a_stable_semantic_selector_is_reported()
    {
        var source = "<Cui>\n    <Page>\n        <Button action=\"save\" />\n    </Page>\n</Cui>\n";
        var finding = Assert.Single(Linter.Lint(source, Parse(source)), item => item.Rule.Code == "CUIL007");

        Assert.Contains("raw coordinates", finding.SuggestedCorrection);
    }

    [Fact]
    public void Semantically_targeted_clean_fixture_produces_no_findings()
    {
        var source = "<Cui>\n    <Page>\n        <Button id=\"save\" role=\"button\" accessibility-name=\"Save\" action=\"save\" />\n    </Page>\n</Cui>\n";

        Assert.Empty(Linter.Lint(source, Parse(source)));
    }

    [Fact]
    public void Every_rule_cites_an_authoritative_specification_range()
    {
        Assert.All(Linter.Rules, rule =>
        {
            Assert.StartsWith("development-specification.txt ", rule.SpecLines);
            Assert.StartsWith("FW-", rule.RequirementId);
        });
    }
}