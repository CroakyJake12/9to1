using NineToOne.Cui.Markup;
using Xunit;

namespace NineToOne.Cui.Markup.Tests;

public sealed class CuiMarkupTests
{
    [Fact]
    public void ParserBuildsNativeCuiDocument()
    {
        var document = new CuiMarkupParser().Parse("<Cui><Stack gap=\"8\"><Text>Hello</Text></Stack></Cui>", "dashboard.cui");

        var stack = Assert.Single(document.Root.Children);
        Assert.Equal("Cui", document.Root.Name);
        Assert.Equal("Stack", stack.Name);
        Assert.Equal("8", stack.Attributes["gap"]);
        Assert.Equal("Hello", Assert.Single(stack.Children).Text);
    }

    [Fact]
    public void LoaderLoadsCuiFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.cui");
        try
        {
            File.WriteAllText(path, "<Cui><Text>Loaded</Text></Cui>");

            var document = new CuiMarkupLoader().Load(path);

            Assert.Equal(Path.GetFullPath(path), document.SourceName);
            Assert.Equal("Loaded", Assert.Single(document.Root.Children).Text);
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
            () => new CuiMarkupParser().Parse("<Cui />", sourceName));
        var loaderException = Assert.Throws<NotSupportedException>(
            () => new CuiMarkupLoader().Load(Path.Combine(Path.GetTempPath(), sourceName)));

        Assert.Contains(".cui", parserException.Message, StringComparison.Ordinal);
        Assert.Contains(".cui", loaderException.Message, StringComparison.Ordinal);
    }
}
