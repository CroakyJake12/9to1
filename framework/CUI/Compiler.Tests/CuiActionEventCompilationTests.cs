using CakeOS.Cui.Compiler;
using Xunit;

namespace CakeOS.Cui.Compiler.Tests;

public sealed class CuiActionEventCompilationTests
{
    private static CuiCompilerOptions Options => new(new("1.0", "1.0", "2.0"));

    [Theory]
    [InlineData("<ActionEvent Type=\"Clicked\" Action=\"SaveDocument\"/>")]
    [InlineData("<ActionEvent Type=\"OnceClick\" Bubble=\"False\" Action=\"SaveDocument\"/>")]
    [InlineData("<&Clicked Action:SaveDocument/>")]
    [InlineData("<actionevent type=\"clicked\" action=\"SaveDocument\"/>")]
    [InlineData("<ActionEvent Type=\"PointerEnter\" Action=\"Preview\"/>")]
    [InlineData("<&PointerExit Action:ClearPreview/>")]
    [InlineData("<ActionEvent Type=\"Focused\" Action=\"Preview\"/>")]
    [InlineData("<&Blurred Action:ClearPreview/>")]
    public void Owned_button_named_actions_compile_as_nonvisual_declarations(string declaration)
    {
        var result = new CuiCompiler().Compile($"<Page><Button Text=\"Save\">{declaration}</Button></Page>", "action.cui", Options);
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var action = Assert.Single(Assert.Single(Assert.Single(result.Output!.Components).Children).Children);
        Assert.Equal("ActionEvent", action.Type);
        if (action.Properties.TryGetValue("Bubble", out var bubble)) Assert.Equal("boolean", bubble.LanguageType);
    }

    [Theory]
    [InlineData("<ActionEvent Type=\"TypoEvent\" Action=\"Save\"/>", "CUIE001")]
    [InlineData("<ActionEvent Action=\"Save\"/>", "CUIE002")]
    [InlineData("<ActionEvent Type=\"Clicked\" Bubble=\"maybe\" Action=\"Save\"/>", "CUIE002")]
    [InlineData("<ActionEvent Type=\"Clicked\" Bubbel=\"False\" Action=\"Save\"/>", "CUIE002")]
    [InlineData("<ActionEvent Type=\"Clicked\" Bubble=\"True\" Action=\"Save\"/>", "CUIE003")]
    [InlineData("<ActionEvent Type=\"Hover\" Action=\"Save\"/>", "CUIE003")]
    [InlineData("<ActionEvent Type=\"Clicked\" TargetID=\"Other\" Action=\"Save\"/>", "CUIE003")]
    [InlineData("<ActionEvent Type=\"Clicked\" TargetGroup=\"Group\" Action=\"Save\"/>", "CUIE003")]
    [InlineData("<ActionEvent Type=\"Clicked\"/>", "CUIE003")]
    [InlineData("<ActionEvent Type=\"Clicked\" Action=\"Save\"><Text>Not a visual container</Text></ActionEvent>", "CUIE003")]
    public void Invalid_and_required_but_unimplemented_forms_fail_with_authored_diagnostics(string declaration, string code)
    {
        var result = new CuiCompiler().Compile($"<Page>\n<Button Text=\"Save\">\n{declaration}\n</Button></Page>", "bad-action.cui", Options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code && diagnostic.Span.SourceName == "bad-action.cui" && diagnostic.Span.Start.Line == 3);
    }

    [Theory]
    [InlineData("<Page><Button Action=\"Direct\"><ActionEvent Type=\"Clicked\" Action=\"Other\"/></Button></Page>")]
    [InlineData("<Page><Button><ActionEvent Type=\"Clicked\" Action=\"One\"/><ActionEvent Type=\"OnceClick\" Action=\"Two\"/></Button></Page>")]
    [InlineData("<Page><Button><ActionEvent Type=\"PointerEnter\" Action=\"One\"/><ActionEvent Type=\"pointerenter\" Action=\"Two\"/></Button></Page>")]
    [InlineData("<Page><Button><ActionEvent Type=\"Focused\" Action=\"One\"/><ActionEvent Type=\"focused\" Action=\"Two\"/></Button></Page>")]
    [InlineData("<Page><Button Action=\"One\"><ActionEvent Type=\"OnceClick\" Action=\"Two\"/><ActionEvent Type=\"PointerExit\" Action=\"Clear\"/></Button></Page>")]
    public void Competing_action_routes_fail_instead_of_silently_dropping_one(string source)
    {
        var result = new CuiCompiler().Compile(source, "competing.cui", Options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIE004");
    }

    [Theory]
    [InlineData("Action=\"Choose\"", "")]
    [InlineData("", "<ActionEvent Type=\"Clicked\" Action=\"Choose\"/>")]
    public void Distinct_pointer_focus_and_click_routes_compile_together(string direct, string clicked)
    {
        var result = new CuiCompiler().Compile($"""
            <Page><Button {direct}>
              <ActionEvent Type="PointerEnter" Action="Preview"/>
              <ActionEvent Type="PointerExit" Action="ClearPreview"/>
              <ActionEvent Type="Focused" Action="FocusPreview"/>
              <ActionEvent Type="Blurred" Action="ClearFocusPreview"/>
              {clicked}
            </Button></Page>
            """, "pointer-actions.cui", Options);
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var button = Assert.Single(Assert.Single(result.Output!.Components).Children);
        Assert.Equal(string.IsNullOrEmpty(clicked) ? 4 : 5, button.Children.Count);
    }

    [Fact]
    public void Unused_templates_and_event_context_bindings_cannot_hide_unsupported_semantics()
    {
        var template = new CuiCompiler().Compile("<Cui><Templates><Template name=\"Unused\"><Button><ActionEvent Type=\"Typo\" Action=\"Save\"/></Button></Template></Templates><Page/></Cui>", "template.cui", Options);
        Assert.False(template.Succeeded);
        Assert.Contains(template.Diagnostics, diagnostic => diagnostic.Code == "CUIE001");
        var context = new CuiCompiler().Compile("<Cui><Actions><Action name=\"Save\" command=\"save\" parameter=\"{Binding Event.Source}\"/></Actions><Page><Button><ActionEvent Type=\"Clicked\" Action=\"Save\"/></Button></Page></Cui>", "context.cui", Options);
        Assert.False(context.Succeeded);
        Assert.Contains(context.Diagnostics, diagnostic => diagnostic.Code == "CUIE003");
    }
}
