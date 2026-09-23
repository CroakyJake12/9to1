using CakeOS.Cui.Language;
using Xunit;

namespace NineToOne.Cui.Markup.Tests;

public sealed class CuiMarkupTests
{
    [Fact]
    public void ParserBuildsNativeCuiDocument()
    {
        var document = new CuiRichParser().Parse("<Cui><Stack gap=\"8\"><Text>Hello</Text></Stack></Cui>", "dashboard.cui");

        var stack = Assert.Single(document.Components);
        Assert.Equal("Stack", stack.Type);
        Assert.True(stack.TryGetLiteralAttribute("gap", out var gap));
        Assert.Equal("8", gap);
        Assert.Equal("Hello", Assert.Single(stack.Children).Text);
    }

    [Fact]
    public void LoaderLoadsCuiFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.cui");
        try
        {
            File.WriteAllText(path, "<Cui><Text>Loaded</Text></Cui>");

            var document = new CuiRichParser().ParseFile(path);

            Assert.Equal(Path.GetFullPath(path), document.SourceName);
            Assert.Equal("Loaded", Assert.Single(document.Components).Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("legacy.axaml")]
    [InlineData("legacy.hui")]
    public void ParserAndLoaderRejectLegacyMarkupInputs(string sourceName)
    {
        var parserException = Assert.Throws<NotSupportedException>(
            () => new CuiRichParser().ParseFile(sourceName));
        var loaderException = Assert.Throws<NotSupportedException>(
            () => new CuiRichParser().ParseFile(Path.Combine(Path.GetTempPath(), sourceName)));

        Assert.Contains(".cui", parserException.Message, StringComparison.Ordinal);
        Assert.Contains(".cui", loaderException.Message, StringComparison.Ordinal);
    }
}
