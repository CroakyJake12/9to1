using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Compiler.Tests;

public sealed class CuiCanonicalBoundaryTests
{
    private static CuiCompilerOptions Options => new(new("1.0", "1.0", "2.0"));
    [Theory]
    [InlineData("Grid")][InlineData("StackPanel")][InlineData("WrapPanel")][InlineData("ScrollViewer")]
    [InlineData("TextBlock")][InlineData("Border")][InlineData("TextBox")][InlineData("MenuItem")]
    [InlineData("Avalonia.Controls.Grid")]
    public void Donor_elements_and_definition_reexports_are_rejected(string name)
    {
        foreach (var source in new[] { $"<Page><{name}/></Page>", $"<Cui><{name} (Definition)><Text>Hidden donor alias</Text></{name}><Page/></Cui>" })
        {
            var compiled = new CuiCompiler().Compile(source, "boundary.cui", Options);
            Assert.False(compiled.Succeeded);
            Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == "CUI045");
        }
    }
    [Theory]
    [InlineData("Grid.Row")][InlineData("Grid.Column")][InlineData("RowDefinitions")][InlineData("ColumnDefinitions")]
    public void Donor_layout_syntax_is_rejected(string property)
    {
        var compiled = new CuiCompiler().Compile($"<Container Type=\"Grid\" {property}=\"1\"/>", "boundary.cui", Options);
        Assert.False(compiled.Succeeded);
        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == "CUI046");
    }
    [Theory]
    [InlineData("bad.axaml")][InlineData("bad.hui")]
    public void Direct_parser_and_compiler_both_reject_foreign_application_input(string path)
    {
        var parser = new CuiRichParser(); parser.Parse("<Page/>", path);
        Assert.Contains(parser.Diagnostics.Diagnostics, diagnostic => diagnostic.Code == "CUI044");
        Assert.False(new CuiCompiler().Compile("<Page/>", path, Options).Succeeded);
    }
    [Fact]
    public void Canonical_layout_input_and_legitimate_definitions_keep_direct_typed_compilation()
    {
        var result = new CuiCompiler().Compile("""
            <Cui>
              <Paragraph (Definition)><Text>Reusable</Text></Paragraph>
              <Text (Definition) Type="Emphasis" FontWeight="Bold" />
              <Page><Container Type:Grid RowSizes:Auto_1fr ColumnSizes:1fr_2fr CellGap="8">
                <Text Type="Emphasis" Row="0" Column="1">Heading</Text>
                <Container Type="Vertical Stack" Row="1" HorizontalScrolling="False" VerticalScrolling="True">
                  <Input Type="Text" Value="Editing"/>
                  <Paragraph Width="100"/>
                </Container>
              </Container></Page>
            </Cui>
            """, "canonical.cui", Options);
        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics));
        Assert.NotNull(result.Output);
        Assert.Equal("1.0", result.Output.Metadata.RuntimeAbiVersion);
    }
    [Theory]
    [InlineData("<Container Type=\"Checkbox\"/>")]
    [InlineData("<Input Type=\"Grid\"/>")]
    [InlineData("<Container Type=\"Horizontal Stack\" RowSizes=\"1fr\"/>")]
    [InlineData("<Container Type=\"Grid\" RowSizes=\"1*\"/>")]
    public void Types_and_layout_values_are_validated_per_canonical_element(string source) =>
        Assert.False(new CuiCompiler().Compile(source, "bad.cui", Options).Succeeded);
    [Fact]
    public void Public_registry_has_ten_visual_primitives_and_separate_anchor()
    {
        Assert.Equal(CuiLanguageVocabulary.VisualElements.Order(StringComparer.Ordinal), CuiControlRegistry.Default.Elements.Where(element => element.IsVisual).Select(element => element.Name).Order(StringComparer.Ordinal));
        Assert.True(CuiControlRegistry.Default.TryResolveElement("Anchor", out var anchor));
        Assert.False(anchor.IsVisual);
        Assert.Throws<ArgumentException>(() => new CuiControlRegistry().RegisterControlType("Grid", _ => new Avalonia.Controls.Grid()));
    }
    [Fact]
    public void Compiler_refuses_a_caller_invented_registry_identity()
    {
        var result = new CuiCompiler().Compile("<Page/>", "version.cui", new(new("1", "1", "1")));
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC024");
        var valid = new CuiCompiler().Compile("<Page/>", "version.cui", Options);
        Assert.Equal(CuiControlRegistry.ComponentRegistryVersion, valid.Output!.Metadata.ComponentRegistryVersion);
    }
}
