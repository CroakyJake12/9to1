using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Themes;
using Haven.CUI.DevTools;
using Xunit;

namespace Haven.CUI.DevTools.Tests;

public sealed class CuiThemeResourceInspectorTests
{
    private static readonly CuiThemeResourceInspector Inspector = new();

    private static CuiDocument Parse(string source) => new CuiRichParser().Parse(source, "theme.cui");

    private static ElementId IdOf(CuiDocument document, string type, string? name) =>
        new ElementId(document.Components
            .SelectMany(component => component.DescendantsAndSelf())
            .First(component => component.Type == type && component.Name == name)
            .StableId);

    [Fact]
    public void Descendant_theme_scope_returns_to_the_parent_theme_when_it_closes()
    {
        var document = Parse("<Cui>\n    <DefaultTheme value=\"Glow\">\n        <Text id=\"a\">a</Text>\n        <DefaultTheme value=\"Bubble\">\n            <Text id=\"b\">b</Text>\n        </DefaultTheme>\n        <Text id=\"c\">c</Text>\n    </DefaultTheme>\n</Cui>\n");
        var inspection = Inspector.Inspect(document);

        Assert.Equal(2, inspection.Scopes.Count);
        Assert.Equal("Glow", inspection.EffectiveThemeAt(new ElementId("id:a")));
        Assert.Equal("Bubble", inspection.EffectiveThemeAt(new ElementId("id:b")));
        Assert.Equal("Glow", inspection.EffectiveThemeAt(new ElementId("id:c")));
        Assert.Equal(1, inspection.Scopes.First(scope => scope.EffectiveTheme == "Bubble").Depth);
        Assert.Empty(inspection.Findings);
    }

    [Fact]
    public void User_preference_resolves_through_the_home_managed_preference_and_is_not_a_theme()
    {
        var document = Parse("<Cui>\n    <DefaultTheme value=\"user_preference\">\n        <Text id=\"a\">a</Text>\n    </DefaultTheme>\n</Cui>\n");
        var inspection = Inspector.Inspect(document, customThemes: null, globalDefault: CuiTheme.Professional);

        var scope = Assert.Single(inspection.Scopes);
        Assert.Equal("Professional", scope.EffectiveTheme);
        Assert.Equal("user-preference-resolver", scope.ResolutionSource);
        Assert.Equal("Professional", inspection.EffectiveThemeAt(new ElementId("id:a")));
        Assert.Empty(inspection.Findings);
    }

    [Fact]
    public void Unknown_theme_name_reports_the_documented_fallback()
    {
        var document = Parse("<Cui>\n    <DefaultTheme value=\"Sunset\">\n        <Text id=\"a\">a</Text>\n    </DefaultTheme>\n</Cui>\n");
        var inspection = Inspector.Inspect(document);

        var finding = Assert.Single(inspection.Findings, item => item.Rule.Code == "CUIT001");
        Assert.Contains("Sunset", finding.Diagnostic.Message);
        Assert.Equal("Glow", inspection.EffectiveThemeAt(new ElementId("id:a")));
    }

    [Fact]
    public void Catalogue_theme_missing_from_the_specification_built_in_list_is_reported()
    {
        var document = Parse("<Cui>\n    <DefaultTheme value=\"Retro\">\n        <Text id=\"a\">a</Text>\n    </DefaultTheme>\n</Cui>\n");
        var finding = Assert.Single(Inspector.Inspect(document).Findings, item => item.Rule.Code == "CUIT002");

        Assert.Contains("Retro", finding.Diagnostic.Message);
        Assert.Equal("FW-017", finding.Rule.RequirementId);
    }

    [Fact]
    public void Declared_custom_theme_resolves_and_the_compiler_validation_gap_is_reported()
    {
        var catalog = new CuiThemeDefinitionCatalog();
        catalog.Add(new CuiThemeDefinition("Rainbow", "Professional", [
            new CuiThemeElementRule("Text", null, new Dictionary<string, object?> { ["Color"] = "Green" })
        ]));

        var document = Parse("<Cui>\n    <DefaultTheme value=\"Rainbow\">\n        <Text id=\"a\">a</Text>\n    </DefaultTheme>\n</Cui>\n");
        var inspection = Inspector.Inspect(document, catalog);

        var scope = Assert.Single(inspection.Scopes);
        Assert.Equal("Rainbow", scope.EffectiveTheme);
        Assert.Equal("custom-theme-definition", scope.ResolutionSource);

        var finding = Assert.Single(inspection.Findings, item => item.Rule.Code == "CUIT003");
        Assert.Contains("Rainbow", finding.Diagnostic.Message);
    }

    [Fact]
    public void Canonical_theme_element_that_the_model_does_not_flag_as_a_scope_is_reported()
    {
        var document = Parse("<Cui>\n    <Theme Name=\"Glow\">\n        <Text id=\"a\">a</Text>\n    </Theme>\n</Cui>\n");
        var inspection = Inspector.Inspect(document);

        var finding = Assert.Single(inspection.Findings, item => item.Rule.Code == "CUIT009");
        Assert.Contains("does not flag it as one", finding.Diagnostic.Message);
        Assert.Equal("Glow", inspection.EffectiveThemeAt(new ElementId("id:a")));
    }

    [Fact]
    public void Resource_reference_chain_resolves_through_declared_keys()
    {
        var document = Parse("<Cui>\n    <Resources>\n        <Resource key=\"accent\" value=\"{Resource brand}\" />\n        <Resource key=\"brand\" value=\"#FF8800\" />\n    </Resources>\n    <Page>\n        <Text id=\"t\" Text=\"{Resource accent}\">x</Text>\n    </Page>\n</Cui>\n");
        var inspection = Inspector.Inspect(document);

        var accent = inspection.Resource("accent");
        Assert.NotNull(accent);
        Assert.Equal(["accent", "brand"], accent.ReferenceChain.ToArray());
        Assert.Contains(new ElementId("id:t"), accent.ReferencedBy);
        Assert.Equal("resource reference", accent.ValueKind);

        var brand = inspection.Resource("brand");
        Assert.NotNull(brand);
        Assert.Equal("literal", brand.ValueKind);
        Assert.Contains(new ElementId("resource:accent"), brand.ReferencedBy);
        Assert.Empty(inspection.Findings);
    }

    [Fact]
    public void Resource_reference_to_an_undeclared_key_is_reported()
    {
        var document = Parse("<Cui>\n    <Resources>\n        <Resource key=\"accent\" value=\"{Resource missing}\" />\n    </Resources>\n    <Page>\n        <Text id=\"t\" Text=\"{Resource accent}\">x</Text>\n    </Page>\n</Cui>\n");
        var finding = Assert.Single(Inspector.Inspect(document).Findings, item => item.Rule.Code == "CUIT004");

        Assert.Contains("missing", finding.Diagnostic.Message);
    }

    [Fact]
    public void Resource_reference_cycle_is_reported()
    {
        var document = Parse("<Cui>\n    <Resources>\n        <Resource key=\"a\" value=\"{Resource b}\" />\n        <Resource key=\"b\" value=\"{Resource a}\" />\n    </Resources>\n    <Page>\n        <Text id=\"t\" Text=\"{Resource a}\">x</Text>\n    </Page>\n</Cui>\n");
        var finding = Assert.Single(Inspector.Inspect(document).Findings, item => item.Rule.Code == "CUIT005");

        Assert.Contains("cycle", finding.Diagnostic.Message);
    }

    [Fact]
    public void Declared_resource_with_no_reference_is_reported()
    {
        var document = Parse("<Cui>\n    <Resources>\n        <Resource key=\"dead\" value=\"#FFFFFF\" />\n    </Resources>\n    <Page>\n        <Text id=\"t\">x</Text>\n    </Page>\n</Cui>\n");
        var finding = Assert.Single(Inspector.Inspect(document).Findings, item => item.Rule.Code == "CUIT006");

        Assert.Contains("dead", finding.Diagnostic.Message);
    }

    [Fact]
    public void Style_targets_resolve_by_id_and_group()
    {
        var document = Parse("<Cui>\n    <Styles>\n        <Style selector=\"{::save}\"><Setter property=\"Color\" value=\"Pink\" /></Style>\n        <Style selector=\"{:::labels}\"><Setter property=\"Color\" value=\"Blue\" /></Style>\n    </Styles>\n    <Page>\n        <Button id=\"save\" />\n        <Text id=\"l\" group=\"labels\">x</Text>\n    </Page>\n</Cui>\n");
        var inspection = Inspector.Inspect(document);

        var byId = inspection.Style("{::save}");
        Assert.NotNull(byId);
        Assert.Equal("id", byId.TargetKind);
        Assert.Contains(new ElementId("id:save"), byId.MatchedElements);

        var byGroup = inspection.Style("{:::labels}");
        Assert.NotNull(byGroup);
        Assert.Equal("group", byGroup.TargetKind);
        Assert.Contains(new ElementId("id:l"), byGroup.MatchedElements);

        Assert.Empty(inspection.Findings);
    }

    [Fact]
    public void Style_target_that_resolves_to_no_element_is_reported()
    {
        var document = Parse("<Cui>\n    <Styles>\n        <Style selector=\"{::ghost}\"><Setter property=\"Color\" value=\"Pink\" /></Style>\n    </Styles>\n    <Page>\n        <Button id=\"save\" />\n    </Page>\n</Cui>\n");
        var finding = Assert.Single(Inspector.Inspect(document).Findings, item => item.Rule.Code == "CUIT007");

        Assert.Contains("ghost", finding.Diagnostic.Message);
    }

    [Fact]
    public void Precedence_resolves_from_the_highest_level_present()
    {
        var overlay = Parse("<Cui>\n    <Styles>\n        <Style selector=\"{::t}\"><Setter property=\"Color\" value=\"Pink\" /></Style>\n    </Styles>\n    <Page>\n        <Text id=\"t\" Color=\"Green\">x</Text>\n    </Page>\n</Cui>\n");
        Assert.Equal(CuiThemeResourceInspector.PrecedenceStyleOverlay,
            Inspector.Inspect(overlay).PrecedenceLevelOf(new ElementId("id:t"), "Color"));

        var region = Parse("<Cui>\n    <Page>\n        <$Color:Green><Text id=\"t\">x</Text></$>\n    </Page>\n</Cui>\n");
        var regionId = IdOf(region, "Property", null);
        Assert.Equal(CuiThemeResourceInspector.PrecedenceScopedRegion,
            Inspector.Inspect(region).PrecedenceLevelOf(regionId, "Color"));

        var instance = Parse("<Cui>\n    <Page>\n        <Text id=\"t\" Color=\"Green\">x</Text>\n    </Page>\n</Cui>\n");
        Assert.Equal(CuiThemeResourceInspector.PrecedenceExplicitInstance,
            Inspector.Inspect(instance).PrecedenceLevelOf(new ElementId("id:t"), "Color"));

        var definition = Parse("<Cui>\n    <Text (Definition) ID=\"Callout\" Color=\"Green\">x</Text>\n    <Page>\n        <Callout ID=\"inst\" />\n    </Page>\n</Cui>\n");
        Assert.Equal(CuiThemeResourceInspector.PrecedenceDefinitionDefault,
            Inspector.Inspect(definition).PrecedenceLevelOf(new ElementId("id:inst"), "Color"));

        var catalog = new CuiThemeDefinitionCatalog();
        catalog.Add(new CuiThemeDefinition("Rainbow", "Professional", [
            new CuiThemeElementRule("Text", null, new Dictionary<string, object?> { ["Color"] = "Green" })
        ]));
        var theme = Parse("<Cui>\n    <DefaultTheme value=\"Rainbow\">\n        <Text id=\"t\">x</Text>\n    </DefaultTheme>\n</Cui>\n");
        Assert.Equal(CuiThemeResourceInspector.PrecedenceInheritedTheme,
            Inspector.Inspect(theme, catalog).PrecedenceLevelOf(new ElementId("id:t"), "Color"));

        var plain = Parse("<Cui>\n    <Page>\n        <Text id=\"t\">x</Text>\n    </Page>\n</Cui>\n");
        Assert.Equal(CuiThemeResourceInspector.PrecedenceBuiltInDefault,
            Inspector.Inspect(plain).PrecedenceLevelOf(new ElementId("id:t"), "Color"));
    }

    [Fact]
    public void Id_and_group_using_the_same_identifier_in_one_scope_is_reported()
    {
        var document = Parse("<Cui>\n    <Page>\n        <Text id=\"t\" group=\"t\">x</Text>\n    </Page>\n</Cui>\n");
        var finding = Assert.Single(Inspector.Inspect(document).Findings, item => item.Rule.Code == "CUIT008");

        Assert.Contains("both an ID and a Group", finding.Diagnostic.Message);
    }

    [Fact]
    public void Snapshot_reports_only_resources_that_resolve()
    {
        var document = Parse("<Cui>\n    <Resources>\n        <Resource key=\"ok\" value=\"#FFFFFF\" />\n        <Resource key=\"broken\" value=\"{Resource nope}\" />\n    </Resources>\n    <Page>\n        <Text id=\"t\" Text=\"{Resource ok}\">x</Text>\n    </Page>\n</Cui>\n");
        var snapshot = Inspector.Inspect(document).Snapshot();

        Assert.Equal(2, snapshot.Values.Count);
        Assert.True(snapshot.Values.First(value => value.Key == "ok").WasFound);
        Assert.False(snapshot.Values.First(value => value.Key == "broken").WasFound);
    }

    [Fact]
    public void Every_rule_cites_an_authoritative_specification_range()
    {
        Assert.All(Inspector.Rules, rule =>
        {
            Assert.StartsWith("development-specification.txt ", rule.SpecLines);
            Assert.StartsWith("FW-", rule.RequirementId);
        });
    }
}