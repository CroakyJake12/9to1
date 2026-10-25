using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperNewSourceFileTests
{
    [Fact]
    public void Generated_CUI_page_is_real_parseable_source_and_empty_source_remains_exactly_empty()
    {
        var page = DeveloperNewSourceFile.Prepare("NewPage.cui", true);
        Assert.True(page.Succeeded); Assert.Equal("cui-page", page.Value!.TemplateId);
        var parsed = DeveloperCuiSourceAnalysis.Analyze(page.Value.RelativePath, page.Value.Text);
        Assert.Empty(parsed.Problems); Assert.Contains(parsed.Outline, element => element.AuthoredId == "title");
        var empty = DeveloperNewSourceFile.Prepare("module.py", false);
        Assert.True(empty.Succeeded); Assert.Equal("", empty.Value!.Text); Assert.Equal("empty-source", empty.Value.TemplateId);
    }

    [Theory]
    [InlineData("../Page.cui")]
    [InlineData("nested/Page.cui")]
    [InlineData("nested\\Page.cui")]
    [InlineData("C:Page.cui")]
    [InlineData(" Page.cui")]
    [InlineData("Page.cui.")]
    [InlineData("")]
    public void Unsupported_paths_are_refused_before_any_source_owner_is_needed(string name) =>
        Assert.False(DeveloperNewSourceFile.Prepare(name, true).Succeeded);

    [Fact]
    public void CUI_template_never_silently_generates_a_different_source_language() =>
        Assert.False(DeveloperNewSourceFile.Prepare("Program.cs", true).Succeeded);
}
