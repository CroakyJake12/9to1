using CakeOS.Cui.Compiler;
using Xunit;

namespace CakeOS.Cui.Compiler.Tests;

public sealed class CuiInputSubmitCompilationTests
{
    private static CuiCompilerOptions Options => new(new("1.0", "1.0", "2.0"));

    [Fact]
    public void Text_input_submit_action_requires_event_parameter_and_compiles_as_an_authored_input_property()
    {
        var result = new CuiCompiler().Compile("""
            <Cui>
              <Actions><Action name="Submit" command="chat.submit" parameter="{Binding Event}" /></Actions>
              <Page><Input id="message" Type="Text" AcceptsReturn="True" SubmitAction="Submit" /></Page>
            </Cui>
            """, "input-submit.cui", Options);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var input = Assert.Single(result.Output!.Components.Single().Children);
        Assert.Equal("Submit", Assert.IsType<CakeOS.Cui.Language.CuiLiteralValue>(input.Properties["SubmitAction"].Value).Value);
    }

    [Theory]
    [InlineData("<Page><Input SubmitAction=\"Missing\" /></Page>", "CUIC049")]
    [InlineData("<Cui><Actions><Action name=\"Submit\" command=\"go\" parameter=\"{Binding Draft}\"/></Actions><Page><Input SubmitAction=\"Submit\"/></Page></Cui>", "CUIC050")]
    [InlineData("<Cui><Actions><Action name=\"Submit\" command=\"go\" parameter=\"{Binding Event, Mode=TwoWay}\"/></Actions><Page><Input SubmitAction=\"Submit\"/></Page></Cui>", "CUIC050")]
    [InlineData("<Page><Input Type=\"Number\" SubmitAction=\"Submit\"/></Page>", "CUIC047")]
    [InlineData("<Page><Button SubmitAction=\"Submit\"/></Page>", "CUIC047")]
    [InlineData("<Cui><Actions><Action name=\"Submit\" command=\"go\" parameter=\"{Binding Event}\"/></Actions><Page><Input Action=\"Other\" SubmitAction=\"Submit\"/></Page></Cui>", "CUIC051")]
    public void Invalid_input_submit_routes_fail_during_compilation(string source, string diagnosticCode)
    {
        var result = new CuiCompiler().Compile(source, "invalid-input-submit.cui", Options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == diagnosticCode);
    }
}
