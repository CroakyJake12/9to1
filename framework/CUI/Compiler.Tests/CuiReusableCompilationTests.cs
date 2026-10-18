using CakeOS.Cui.Compiler;
using Xunit;

namespace CakeOS.Cui.Compiler.Tests;

public sealed class CuiReusableCompilationTests
{
    private static CuiCompilation Compile(string source) =>
        new CuiCompiler().Compile(source, "reusable.cui", new(new("1.0", "1.0", "2.0")));

    [Theory]
    [InlineData("<Text (Definition) Type=\"Emphasis\" Widht=\"10\"/>", "CUIC012")]
    [InlineData("<Button (Definition) ID=\"Save\" Opacity=\"opaque\"/>", "CUIC015")]
    [InlineData("<Paragraph (Definition) Opacity=\"opaque\"><Text>Text</Text></Paragraph>", "CUIC015")]
    [InlineData("<Container (Definition) ID=\"Stack\" Type=\"Horizontal Stack\" RowSizes=\"1fr\"/>", "CUIC023")]
    public void Invalid_reusable_defaults_fail_before_instantiation(string declaration, string code)
    {
        var result = Compile($"<Cui>{declaration}<Page/></Cui>");
        Assert.False(result.Succeeded);
        Assert.Null(result.Output);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code && diagnostic.Span.SourceName == "reusable.cui");
    }

    [Fact]
    public void Invalid_custom_instance_literal_does_not_escape_typed_validation()
    {
        var result = Compile("<Cui><Paragraph (Definition)><Text>Text</Text></Paragraph><Page><Paragraph Opacity=\"opaque\"/></Page></Cui>");
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC015");
    }

    [Theory]
    [InlineData("<Buton/>", "CUIC011")]
    [InlineData("<Text Widht=\"4\"/>", "CUIC012")]
    [InlineData("<Text Opacity=\"opaque\"/>", "CUIC015")]
    public void Retained_template_bodies_receive_the_same_validation(string content, string code)
    {
        var result = Compile($"<Cui><Templates><Template name=\"Card\">{content}</Template></Templates><Page><Card/></Page></Cui>");
        Assert.False(result.Succeeded);
        Assert.Null(result.Output);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Theory]
    [InlineData("<Card (Definition)><Card/></Card>", "Card -> Card")]
    [InlineData("<Card (Definition)><Badge/></Card><Badge (Definition)><Card/></Badge>", "Card -> Badge -> Card")]
    [InlineData("<Prefab (Definition) ID=\"Card\" Type=\"dynamic\"><Badge/></Prefab><Badge (Definition)><Card/></Badge>", "Card -> Badge -> Card")]
    [InlineData("<Card (Definition)><If Condition=\"True\"><Text>One</Text></If><Else><Card/></Else></Card>", "Card -> Card")]
    public void Statically_known_reusable_cycles_are_compiler_errors(string declarations, string cycle)
    {
        var result = Compile($"<Cui>\n{declarations}\n<Page/></Cui>");
        Assert.False(result.Succeeded);
        Assert.Null(result.Output);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC025");
        Assert.Contains(cycle, diagnostic.Message);
        Assert.Equal(2, diagnostic.Span.Start.Line);
        Assert.True(diagnostic.Span.Start.Column > 0);
    }

    [Fact]
    public void Cycles_cannot_hide_in_retained_template_bodies()
    {
        var result = Compile("<Cui><Templates><Template name=\"Card\"><Card/></Template></Templates><Page/></Cui>");
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC025");
    }

    [Fact]
    public void Retained_templates_keep_their_local_definitions_and_type_metadata()
    {
        var result = Compile("""
            <Cui><Templates><Template name="Card">
              <Badge (Definition)><Text>Ready</Text></Badge>
              <Text (Definition) Type="Emphasis" FontWeight="Bold"/>
              <Badge/><Text Type="Emphasis">Title</Text>
            </Template></Templates><Page><Card/></Page></Cui>
            """);
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Contains(result.Output!.Dependencies.Entries.Values.SelectMany(value => value),
            dependency => dependency.Kind == CuiDependencyKind.Definition && dependency.Name == "Badge");
    }

    [Theory]
    [InlineData("<Badge/>", "CUIC011")]
    [InlineData("<Text Type=\"Emphasis\">Other</Text>", "CUIC017")]
    public void A_templates_local_symbols_do_not_leak_to_its_sibling(string content, string code)
    {
        var result = Compile($"""
            <Cui><Templates>
              <Template name="Card"><Badge (Definition)><Text>Ready</Text></Badge><Text (Definition) Type="Emphasis" FontWeight="Bold"/><Badge/></Template>
              <Template name="Other">{content}</Template>
            </Templates><Page/></Cui>
            """);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void Reuse_and_finite_instance_nesting_are_not_cycles()
    {
        var result = Compile("""
            <Cui>
              <Container (Definition) ID="CardHost" Type="Vertical Stack"/>
              <Badge (Definition)><Text>Ready</Text></Badge>
              <Card (Definition)><CardHost><Badge ID="first"/><Badge ID="second"/></CardHost></Card>
              <Page><CardHost><CardHost><Card/></CardHost></CardHost></Page>
            </Cui>
            """);
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Output);
    }

    [Fact]
    public void Finite_replacement_children_inside_a_visual_definition_are_not_a_cycle()
    {
        var result = Compile("""
            <Cui><Container (Definition) ID="Card" Type="Vertical Stack">
              <Card><Text>Finite leaf</Text></Card>
            </Container><Page><Card/></Page></Cui>
            """);
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Fact]
    public void Replacement_children_are_still_checked_for_real_expansion_cycles()
    {
        var result = Compile("""
            <Cui><Container (Definition) ID="Card" Type="Vertical Stack">
              <Card><Card/></Card>
            </Container><Page><Card/></Page></Cui>
            """);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC025");
    }

    [Fact]
    public void A_duplicate_declaration_cannot_hide_a_cycle_by_first_or_last_wins()
    {
        var result = Compile("""
            <Cui><Card (Definition)><Text>First</Text></Card>
              <Card (Definition)><Card/></Card><Page><Card/></Page></Cui>
            """);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC026");
    }

    [Fact]
    public void Named_custom_wrappers_include_the_root_type_in_cycle_analysis()
    {
        var result = Compile("<Cui><A (Definition) ID=\"B\"/><B (Definition) ID=\"A\"/><Page><A/></Page></Cui>");
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC025");
    }

    [Fact]
    public void Definition_symbols_are_case_sensitive_and_scope_local()
    {
        var result = Compile("""
            <Cui>
              <Card (Definition)><card/></Card>
              <card (Definition)><Text>Lowercase component</Text></card>
              <Page ID="first"><Badge (Definition)><Card/></Badge><Badge/></Page>
              <Page ID="second"><Card (Definition)><Badge/></Card><Badge (Definition)><Text>Local badge</Text></Badge><Card/></Page>
            </Cui>
            """);
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Fact]
    public void Theme_element_defaults_and_legitimate_type_definitions_remain_valid()
    {
        var result = Compile("""
            <Cui>
              <Theme (Definition) Name="Brand" Inherits="Professional">
                <Container (Definition) Padding="10"/>
                <Button (Definition) FontSize="16"/>
              </Theme>
              <Text (Definition) Type="Emphasis" FontWeight="Bold"/>
              <Page><Text Type="Emphasis">Ready</Text></Page>
            </Cui>
            """);
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
    }
}
