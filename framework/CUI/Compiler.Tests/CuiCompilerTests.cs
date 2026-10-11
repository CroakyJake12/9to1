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
}
