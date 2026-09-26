using CakeOS.Cui.Compiler;
using Xunit;

namespace CakeOS.Cui.Compiler.Tests;

public sealed class CuiCompilerTests
{
    private static CuiCompilerOptions Options() => new(new CuiCompilationMetadata("1.0", "1.0", "1.0", ["layout"]));

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
}
