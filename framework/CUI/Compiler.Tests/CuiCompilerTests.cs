using CakeOS.Cui.Compiler;
using Xunit;

namespace CakeOS.Cui.Compiler.Tests;

public sealed class CuiCompilerTests
{
    private static CuiCompilerOptions Options() => new(new CuiCompilationMetadata("1.0", "1.0", "2.0", ["layout"]));

    [Fact]
    public void Compile_emits_compatibility_metadata_and_authored_identity()
    {
        var result = new CuiCompiler().Compile("<Page id=\"home\"><Text id=\"title\" Text=\"Welcome\" /></Page>", "home.cui", Options());

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Output);
        Assert.Equal("1.0", result.Output.Metadata.LanguageVersion);
        Assert.Contains(result.Output.SourceMap, entry => entry.AuthoredId == "title" && entry.IsUnambiguous);
        Assert.Contains(result.Output.Components[0].Children, node => node.StableId.Contains("id:title", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("<Page><Buton /></Page>", "CUIC011")]
    [InlineData("<Page><Text Widht=\"10\" /></Page>", "CUIC012")]
    public void Compile_reports_unknown_elements_and_properties(string source, string code)
    {
        var result = new CuiCompiler().Compile(source, "bad.cui", Options());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void Compile_accepts_checked_menu_item_state_only_on_the_menu_item_button_type()
    {
        var accepted = new CuiCompiler().Compile(
            "<Page><Button Type=\"Menu Item\" Content=\"Grid\" ToggleType=\"CheckBox\" Checked=\"True\" StaysOpenOnClick=\"True\" /></Page>",
            "checked-menu.cui", Options());
        Assert.True(accepted.Succeeded, string.Join(Environment.NewLine, accepted.Diagnostics));

        var canonicalChecked = new CuiCompiler().Compile(
            "<Page><Button Type=\"Menu Item\" Content=\"Grid\" ToggleType=\"CheckBox\" IsChecked=\"True\" /></Page>",
            "checked-menu-canonical.cui", Options());
        Assert.True(canonicalChecked.Succeeded, string.Join(Environment.NewLine, canonicalChecked.Diagnostics));

        var rejected = new CuiCompiler().Compile(
            "<Page><Button Type=\"Action\" Content=\"Run\" ToggleType=\"CheckBox\" /></Page>",
            "invalid-checked-button.cui", Options());
        Assert.False(rejected.Succeeded);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == "CUIC013");
    }

    [Fact]
    public void Compile_rejects_axaml_source_names()
    {
        var result = new CuiCompiler().Compile("<Page />", "legacy.axaml", Options());

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC001");
    }

    [Fact]
    public void Compile_resolves_imported_definitions_through_an_alias()
    {
        var result = new CuiCompiler().Compile(
            "<Cui><Import Source:\"shared/ui\" As:SharedUI/><Page><SharedUI.Card/></Page></Cui>",
            "imports.cui",
            new CuiCompilerOptions(Options().Metadata, [new CuiImportedSymbols("shared/ui", ["Card"], [])]));

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Output);
        Assert.Contains(result.Output.Dependencies.Entries.Values.SelectMany(entry => entry),
            dependency => dependency.Kind == CuiDependencyKind.Import && dependency.Name == "shared/ui");
    }

    [Fact]
    public void Compile_rejects_ambiguous_imported_symbols()
    {
        var result = new CuiCompiler().Compile(
            "<Cui><Import Source:\"a\"/><Import Source:\"b\"/><Page><Card/></Page></Cui>",
            "ambiguous.cui",
            new CuiCompilerOptions(Options().Metadata, [
                new CuiImportedSymbols("a", ["Card"], []),
                new CuiImportedSymbols("b", ["Card"], []),
            ]));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC019");
    }

    [Fact]
    public void Compile_rejects_circular_import_dependencies()
    {
        var result = new CuiCompiler().Compile(
            "<Cui><Import Source:\"a\"/><Import Source:\"b\"/><Page><Card/></Page></Cui>",
            "circular.cui",
            new CuiCompilerOptions(Options().Metadata, [
                new CuiImportedSymbols("a", ["Card"], ["b"]),
                new CuiImportedSymbols("b", ["Grid"], ["a"]),
            ]));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC020");
    }

    [Fact]
    public void Compile_reports_unresolved_imports()
    {
        var result = new CuiCompiler().Compile(
            "<Cui><Import Source:\"missing\"/><Page><Text>Hello</Text></Page></Cui>",
            "missing.cui",
            new CuiCompilerOptions(Options().Metadata, Array.Empty<CuiImportedSymbols>()));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC018");
    }

    [Fact]
    public void Compile_requires_prefab_type_to_be_static_or_dynamic()
    {
        var invalid = new CuiCompiler().Compile(
            "<Cui><Prefab (Definition) Type=\"shared\" ID=\"AIBar\"><Text>Bar</Text></Prefab></Cui>",
            "prefab.cui",
            Options());
        var valid = new CuiCompiler().Compile(
            "<Cui><Prefab (Definition) Type=\"static\" ID=\"AIBar\"><Text>Bar</Text></Prefab><Page><AIBar/></Page></Cui>",
            "prefab.cui",
            Options());

        Assert.False(invalid.Succeeded);
        Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.Code == "CUIC021");
        Assert.True(valid.Succeeded, string.Join(Environment.NewLine, valid.Diagnostics));
    }

    [Fact]
    public void Definition_instances_keep_property_name_validation_without_a_builtin_descriptor()
    {
        var result = new CuiCompiler().Compile(
            "<Cui><Paragraph (Definition)><Text>Reusable</Text></Paragraph><Page><Paragraph Widht=\"10\"/></Page></Cui>",
            "definition-instance.cui",
            Options());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC012");
    }

    [Fact]
    public void Definition_instances_with_registry_known_properties_are_not_rejected_by_a_missing_builtin_descriptor()
    {
        var result = new CuiCompiler().Compile(
            "<Cui><Paragraph (Definition)><Text>Reusable</Text></Paragraph><Page><Paragraph Width=\"10\"/></Page></Cui>",
            "definition-instance-property.cui",
            Options());

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Fact]
    public void Accessibility_name_synonym_resolves_to_the_canonical_accessible_name()
    {
        var result = new CuiCompiler().Compile(
            "<Cui><Page><Text accessibility-name=\"Mail status\">Status</Text></Page></Cui>",
            "accessibility-alias.cui",
            Options());

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Fact]
    public void Toolchain_indexes_local_theme_and_prefab_files_for_import_resolution()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cui-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "shared"));
        File.WriteAllText(Path.Combine(root, "shared", "ui.cui-prefab"),
            "<Prefab (Definition) Type=\"dynamic\" ID=\"Card\"><Text>Card</Text></Prefab>");
        File.WriteAllText(Path.Combine(root, "shared", "brand.cui-theme"),
            "<Theme (Definition) Name=\"Rainbow\" Inherits=\"Professional\"><Button /></Theme>");

        var index = new CuiSourceIndex().Index(root);

        var prefab = Assert.Single(index, entry => entry.Source == "shared/ui.cui-prefab");
        Assert.Contains("Card", prefab.Symbols);
        var theme = Assert.Single(index, entry => entry.Source == "shared/brand.cui-theme");
        Assert.Contains("Rainbow", theme.Symbols);

        var result = new CuiCompiler().Compile(
            "<Cui><Import Source:\"shared/ui.cui-prefab\"/><Page><Card/></Page></Cui>",
            "consumer.cui",
            new CuiCompilerOptions(Options().Metadata, index));

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Fact]
    public void Indexed_import_graph_reports_a_transitive_cycle_at_the_owning_import()
    {
        using var project = new ImportProject(new Dictionary<string, string>
        {
            ["a.cui"] = "<Cui><Import Source=\"b.cui\"/><Prefab (Definition) Type=\"static\" ID=\"RootCard\"><Text>A</Text></Prefab></Cui>",
            ["b.cui"] = "<Cui><Import Source=\"c.cui\"/><Page/></Cui>",
            ["c.cui"] = "<Cui><Import Source=\"b.cui\"/><Page/></Cui>",
        });

        var result = project.Compile("<Cui>\n  <Import Source=\"a.cui\"/>\n  <Page><RootCard/></Page>\n</Cui>");

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "CUIC020");
        Assert.Contains("a.cui -> b.cui -> c.cui -> b.cui", diagnostic.Message);
        Assert.Equal("consumer.cui", diagnostic.Span.SourceName);
        Assert.Equal(2, diagnostic.Span.Start.Line);
        Assert.True(diagnostic.Span.Start.Column > 0);
        Assert.Null(result.Output);
    }

    [Fact]
    public void Indexed_import_graph_reports_a_missing_transitive_dependency()
    {
        using var project = new ImportProject(new Dictionary<string, string>
        {
            ["a.cui"] = "<Cui><Import Source=\"b.cui\"/><Prefab (Definition) Type=\"static\" ID=\"RootCard\"><Text>A</Text></Prefab></Cui>",
            ["b.cui"] = "<Cui><Import Source=\"missing.cui\"/><Page/></Cui>",
        });

        var result = project.Compile("<Cui>\n  <Import Source=\"a.cui\"/>\n  <Page><RootCard/></Page>\n</Cui>");

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "CUIC018");
        Assert.Contains("a.cui -> b.cui -> missing.cui", diagnostic.Message);
        Assert.Equal("consumer.cui", diagnostic.Span.SourceName);
        Assert.Equal(2, diagnostic.Span.Start.Line);
        Assert.Null(result.Output);
    }

    [Fact]
    public void Indexed_import_aliases_disambiguate_the_same_symbol_from_distinct_packages()
    {
        using var project = ImportProject.WithCards();

        var result = project.Compile("<Cui><Import Source=\"a.cui\" As=\"First\"/><Import Source=\"b.cui\" As=\"Second\"/><Page><First.Card/><Second.Card/></Page></Cui>");

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var dependencies = result.Output!.Dependencies.Entries.Values.SelectMany(value => value).ToArray();
        Assert.Contains(dependencies, item => item.Kind == CuiDependencyKind.Import && item.Name == "a.cui");
        Assert.Contains(dependencies, item => item.Kind == CuiDependencyKind.Import && item.Name == "b.cui");
    }

    [Fact]
    public void Indexed_import_aliases_can_bind_one_package_under_two_explicit_names()
    {
        using var project = ImportProject.WithCards();

        var result = project.Compile("<Cui><Import Source=\"a.cui\" As=\"First\"/><Import Source=\"a.cui\" As=\"Second\"/><Page><First.Card/><Second.Card/></Page></Cui>");

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Output);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" As=\"Same\"")]
    public void Indexed_import_aliases_still_reject_colliding_exposed_names(string alias)
    {
        using var project = ImportProject.WithCards();

        var result = project.Compile($"<Cui><Import Source=\"a.cui\"{alias}/><Import Source=\"b.cui\"{alias}/><Page/></Cui>");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, item => item.Code == "CUIC019");
        Assert.Null(result.Output);
    }

    [Fact]
    public void Indexed_import_graph_allows_shared_dependencies_without_reexporting_their_symbols()
    {
        using var project = new ImportProject(new Dictionary<string, string>
        {
            ["a.cui"] = "<Cui><Import Source=\"shared.cui\"/><Prefab (Definition) Type=\"static\" ID=\"First\"><Text>A</Text></Prefab></Cui>",
            ["b.cui"] = "<Cui><Import Source=\"shared.cui\"/><Prefab (Definition) Type=\"static\" ID=\"Second\"><Text>B</Text></Prefab></Cui>",
            ["shared.cui"] = "<Prefab (Definition) Type=\"static\" ID=\"PrivateCard\"><Text>Shared</Text></Prefab>",
        });
        const string imports = "<Import Source=\"a.cui\"/><Import Source=\"b.cui\"/>";

        var valid = project.Compile($"<Cui>{imports}<Page><First/><Second/></Page></Cui>");
        var undeclared = project.Compile($"<Cui>{imports}<Page><PrivateCard/></Page></Cui>");

        Assert.True(valid.Succeeded, string.Join(Environment.NewLine, valid.Diagnostics));
        Assert.False(undeclared.Succeeded);
        Assert.Contains(undeclared.Diagnostics, item => item.Code == "CUIC011");
    }

    private sealed class ImportProject : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"cui-import-{Guid.NewGuid():N}");

        public ImportProject(IReadOnlyDictionary<string, string> sources)
        {
            Directory.CreateDirectory(_root);
            foreach (var (name, source) in sources)
                File.WriteAllText(Path.Combine(_root, name), source);
        }

        public static ImportProject WithCards() => new(new Dictionary<string, string>
        {
            ["a.cui"] = "<Prefab (Definition) Type=\"static\" ID=\"Card\"><Text>A</Text></Prefab>",
            ["b.cui"] = "<Prefab (Definition) Type=\"static\" ID=\"Card\"><Text>B</Text></Prefab>",
        });

        public CuiCompilation Compile(string source) => new CuiCompiler().Compile(source, "consumer.cui",
            new CuiCompilerOptions(Options().Metadata, new CuiSourceIndex().Index(_root)));

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
