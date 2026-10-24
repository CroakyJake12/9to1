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

    [Theory]
    [InlineData(CuiTheme.Professional)]
    [InlineData(CuiTheme.Glow)]
    [InlineData(CuiTheme.Bubble)]
    public void Unknown_theme_name_reports_Professional_independently_of_the_global_preference(CuiTheme global)
    {
        var document = Parse("<Cui>\n    <DefaultTheme value=\"Sunset\">\n        <Text id=\"a\">a</Text>\n    </DefaultTheme>\n</Cui>\n");
        var inspection = Inspector.Inspect(document, globalDefault: global);

        var finding = Assert.Single(inspection.Findings, item => item.Rule.Code == "CUIT001");
        Assert.Contains("Sunset", finding.Diagnostic.Message);
        Assert.Contains("Professional", finding.Diagnostic.Message);
        Assert.Equal("Professional", inspection.EffectiveThemeAt(new ElementId("id:a")));
        Assert.Equal(CuiThemeScope.Resolve("Sunset", new(), globalDefault: global).Theme.Name,
            inspection.EffectiveThemeAt(new ElementId("id:a")));
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
    public void Canonical_Theme_creates_a_scope_without_a_false_parser_gap_warning()
    {
        var document = Parse("<Cui>\n    <Theme Name=\"Glow\">\n        <Text id=\"a\">a</Text>\n    </Theme>\n</Cui>\n");
        var inspection = Inspector.Inspect(document);

        Assert.True(Assert.Single(document.Components).IsThemeScope);
        var scope = Assert.Single(inspection.Scopes);
        Assert.Equal("Glow", scope.AuthoredThemeName);
        Assert.Equal("built-in", scope.ResolutionSource);
        Assert.Empty(inspection.Findings);
        Assert.Equal("Glow", inspection.EffectiveThemeAt(new ElementId("id:a")));
    }

    [Fact]
    public void Canonical_nested_Theme_returns_to_its_parent_and_does_not_leak_to_siblings()
    {
        var document = Parse("""
            <Cui>
                <Theme Name="Glow">
                    <Text ID="a">a</Text>
                    <Theme Name="Bubble"><Text ID="b">b</Text></Theme>
                    <Text ID="c">c</Text>
                </Theme>
                <Text ID="outside">outside</Text>
            </Cui>
            """);
        var inspection = Inspector.Inspect(document, globalDefault: CuiTheme.Professional);

        Assert.Equal(2, inspection.Scopes.Count);
        Assert.Equal("Glow", inspection.EffectiveThemeAt(new("id:a")));
        Assert.Equal("Bubble", inspection.EffectiveThemeAt(new("id:b")));
        Assert.Equal("Glow", inspection.EffectiveThemeAt(new("id:c")));
        Assert.Equal("Professional", inspection.EffectiveThemeAt(new("id:outside")));
        Assert.Empty(inspection.Findings);
    }

    [Fact]
    public void A_Theme_definition_is_not_an_active_scope()
    {
        var document = Parse("""
            <Cui>
                <Theme (Definition) Name="Rainbow" Inherits="Professional">
                    <Text (Definition) Color="Green" />
                </Theme>
                <Text ID="outside">outside</Text>
            </Cui>
            """);
        var inspection = Inspector.Inspect(document, globalDefault: CuiTheme.Glow);

        Assert.Empty(inspection.Scopes);
        Assert.DoesNotContain(inspection.Findings, finding => finding.Rule.Code is "CUIT001" or "CUIT009");
        Assert.Equal("Glow", inspection.EffectiveThemeAt(new("id:outside")));
    }

    [Theory]
    [InlineData("Color", "Foreground")]
    [InlineData("Foreground", "Color")]
    [InlineData("color", "foreground")]
    public void Inherited_theme_precedence_uses_registered_property_aliases(string authored, string queried)
    {
        var catalog = new CuiThemeDefinitionCatalog();
        catalog.Add(new("Base", "Professional", [new("Text", null,
            new Dictionary<string, object?> { [authored] = "Green" })]));
        catalog.Add(new("Child", "Base", [new("Text", null,
            new Dictionary<string, object?> { ["Opacity"] = "0.8" })]));
        var document = Parse("<Cui><Theme Name=\"Child\"><Text ID=\"t\">x</Text></Theme></Cui>");

        Assert.Equal(CuiThemeResourceInspector.PrecedenceInheritedTheme,
            Inspector.Inspect(document, catalog).PrecedenceLevelOf(new("id:t"), queried));
    }

    [Theory]
    [InlineData("Emphasis", CuiThemeResourceInspector.PrecedenceInheritedTheme)]
    [InlineData("Other", CuiThemeResourceInspector.PrecedenceBuiltInDefault)]
    [InlineData("emphasis", CuiThemeResourceInspector.PrecedenceBuiltInDefault)]
    [InlineData(null, CuiThemeResourceInspector.PrecedenceBuiltInDefault)]
    public void A_type_specific_theme_rule_only_matches_the_authored_element_Type(string? type, string expected)
    {
        var catalog = new CuiThemeDefinitionCatalog();
        catalog.Add(new("Rainbow", "Professional", [new("Text", "Emphasis",
            new Dictionary<string, object?> { ["Foreground"] = "Green" })]));
        var typeAttribute = type is null ? "" : $" Type=\"{type}\"";
        var document = Parse($"<Cui><Theme Name=\"Rainbow\"><Text ID=\"t\"{typeAttribute}>x</Text></Theme></Cui>");

        Assert.Equal(expected, Inspector.Inspect(document, catalog).PrecedenceLevelOf(new("id:t"), "Color"));
    }

    [Fact]
    public void Invalid_custom_theme_inheritance_reports_Professional_fallback()
    {
        var catalog = new CuiThemeDefinitionCatalog();
        catalog.Add(new("Broken", "Missing", []));
        var document = Parse("<Cui><Theme Name=\"Broken\"><Text ID=\"t\">x</Text></Theme></Cui>");

        var inspection = Inspector.Inspect(document, catalog, CuiTheme.Bubble);

        Assert.Equal("Professional", inspection.EffectiveThemeAt(new("id:t")));
        Assert.Equal("unresolved-fallback", Assert.Single(inspection.Scopes).ResolutionSource);
        Assert.Contains("Professional", Assert.Single(inspection.Findings).Diagnostic.Message);
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
    public void Resource_used_by_an_action_parameter_is_not_reported_unreferenced()
    {
        var document = Parse("<Cui><Resources><Resource Key=\"message\" Value=\"Hello\" /></Resources><Actions><Action Name=\"Save\" Command=\"save\" Parameter=\"{Resource message}\" /></Actions><Page /></Cui>");

        var inspection = Inspector.Inspect(document);

        var resource = Assert.Single(inspection.Resources);
        Assert.Contains(new ElementId("action:Save"), resource.ReferencedBy);
        Assert.DoesNotContain(inspection.Findings, item => item.Rule.Code == "CUIT006");
    }

    [Fact]
    public void Resource_used_only_inside_a_template_is_not_reported_unreferenced()
    {
        var document = Parse("<Cui><Resources><Resource Key=\"accent\" Value=\"Blue\" /></Resources><Templates><Template Name=\"Card\"><Text ID=\"title\" Foreground=\"{Resource accent}\">Title</Text></Template></Templates><Page><Card /></Page></Cui>");

        var inspection = Inspector.Inspect(document);

        var resource = Assert.Single(inspection.Resources);
        Assert.Contains(new ElementId("template:Card"), resource.ReferencedBy);
        Assert.DoesNotContain(inspection.Findings, item => item.Rule.Code == "CUIT006");
    }

    [Fact]
    public void Resource_references_in_supported_conditions_are_accounted_for()
    {
        var document = Parse("<Cui><Resources><Resource Key=\"enabled\" Value=\"True\" /></Resources><Variable Type=\"bool\" ID=\"Ready\">False</Variable><Actions><Action Name=\"Complete\" Command=\"complete\"><Set Target=\"Ready\" Value=\"True\" When=\"{Resource enabled}\" /></Action></Actions><Effects><Effect Name=\"completion\" Condition=\"{Resource enabled}\" Action=\"Complete\" /></Effects><Page><If ID=\"conditional\" Condition=\"{Resource enabled}\"><Text>Visible</Text></If></Page></Cui>");

        var inspection = Inspector.Inspect(document);
        var resource = Assert.Single(inspection.Resources);

        Assert.Contains(new ElementId("id:conditional"), resource.ReferencedBy);
        Assert.Contains(new ElementId("action:Complete"), resource.ReferencedBy);
        Assert.Contains(new ElementId("effect:completion"), resource.ReferencedBy);
        Assert.DoesNotContain(inspection.Findings, item => item.Rule.Code == "CUIT006");
    }

    [Fact]
    public void Resource_used_by_a_property_region_is_not_reported_unreferenced()
    {
        var document = Parse("<Cui><Resources><Resource Key=\"accent\" Value=\"Blue\" /></Resources><Page><Property Property=\"Foreground\" Value=\"{Resource accent}\"><Text ID=\"title\">Title</Text></Property></Page></Cui>");

        var inspection = Inspector.Inspect(document);

        var resource = Assert.Single(inspection.Resources);
        Assert.Contains(IdOf(document, "Property", null), resource.ReferencedBy);
        Assert.DoesNotContain(inspection.Findings, item => item.Rule.Code == "CUIT006");
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
    public void Style_snapshot_includes_authored_component_classes()
    {
        var document = Parse("<Cui><Styles><Style selector=\"accent\"><Setter property=\"Foreground\" value=\"Blue\" /></Style></Styles><Page><Text ID=\"title\" Class=\"accent compact\">Title</Text></Page></Cui>");
        var inspection = Inspector.Inspect(document);

        var snapshot = inspection.SnapshotFor(IdOf(document, "Text", "title"));

        Assert.Equal(["accent", "compact"], snapshot.Classes);
        Assert.Empty(snapshot.PseudoClasses);
        var setter = Assert.Single(snapshot.Values);
        Assert.Equal("Foreground", setter.Property);
        Assert.Equal("Blue", setter.Value);
        Assert.Equal("accent", setter.Rule);
    }

    [Fact]
    public void Duplicate_style_selectors_merge_and_report_the_source_order_winner()
    {
        var document = Parse("<Cui><Styles><Style selector=\"{::title}\"><Setter property=\"Color\" value=\"Red\" /></Style><Style selector=\"{::title}\"><Setter property=\"Foreground\" value=\"Blue\" /></Style></Styles><Page><Text ID=\"title\">Title</Text></Page></Cui>");
        var inspection = Inspector.Inspect(document);
        var titleId = IdOf(document, "Text", "title");

        Assert.Equal(2, inspection.Styles.Count);
        Assert.Equal(2, inspection.Style("{::title}")!.Setters.Count);
        var values = inspection.SnapshotFor(titleId).Values;
        Assert.Equal(2, values.Count);
        Assert.Contains(values, value => value.Value == "Red" && value.IsOverridden);
        Assert.Contains(values, value => value.Value == "Blue" && !value.IsOverridden);
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
        var definitionInspection = Inspector.Inspect(definition);
        Assert.Equal(CuiThemeResourceInspector.PrecedenceDefinitionDefault,
            definitionInspection.PrecedenceLevelOf(new ElementId("id:inst"), "Color"));
        var definitionId = IdOf(definition, "Text", "Callout");
        Assert.Equal(CuiThemeResourceInspector.PrecedenceDefinitionDefault,
            definitionInspection.PrecedenceLevelOf(definitionId, "Color"));
        Assert.Equal(CuiThemeResourceInspector.PrecedenceBuiltInDefault,
            definitionInspection.PrecedenceLevelOf(definitionId, "FontSize"));

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
