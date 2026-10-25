using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperProjectFileNavigationTests
{
    [Fact]
    public void Actual_paths_are_ranked_by_literal_filename_then_path_and_preserve_case_identity()
    {
        string[] paths = ["test/Other.cs", "src/foobar.cs", "foo-dir/readme.txt", "lib/afoo.cs", "foo.cs", "FOO.CS", "x/foo.cs"];
        var result = DeveloperProjectFileNavigation.Search(paths, "foo.cs");
        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(new[] { "FOO.CS", "foo.cs", "x/foo.cs", "lib/afoo.cs" }, result.Value!.Matches.Select(value => value.RelativePath));
        Assert.Equal(new[] { 0, 0, 0, 2 }, result.Value.Matches.Select(value => value.Rank));
        paths[4] = "changed after match";
        Assert.Contains(result.Value.Matches, value => value.RelativePath == "foo.cs");
    }

    [Fact]
    public void Regex_characters_and_spaces_are_literal_and_folder_query_does_not_rewrite_identity()
    {
        var result = DeveloperProjectFileNavigation.Search(["src/Foo [old].cs", "src/Foo old.cs", "src2/Foo [old].cs"], "src\\Foo [old]");
        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal("src/Foo [old].cs", Assert.Single(result.Value!.Matches).RelativePath);
        Assert.Equal("src\\Foo [old]", result.Value.Query);
        var empty = DeveloperProjectFileNavigation.Search(["src/File.cs"], "missing");
        Assert.True(empty.Succeeded); Assert.Empty(empty.Value!.Matches);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_query_is_an_explicit_refusal(string query)
    { var result = DeveloperProjectFileNavigation.Search(["File.cs"], query); Assert.False(result.Succeeded); Assert.Null(result.Value); }

    [Fact]
    public void Bounds_duplicates_and_untrusted_paths_are_refused_without_returning_a_partial_catalogue()
    {
        Assert.False(DeveloperProjectFileNavigation.Search(["File.cs"], new string('x', 257)).Succeeded);
        foreach (var paths in new[] { new[] { "File.cs", "File.cs" }, new[] { "../File.cs" }, new[] { "/File.cs" }, new[] { "a//File.cs" } })
        {
            var result = DeveloperProjectFileNavigation.Search(paths, "File"); Assert.False(result.Succeeded); Assert.Null(result.Value);
        }
        var tooLarge = DeveloperProjectFileNavigation.Search(Enumerable.Range(0, DeveloperProjectFileNavigation.MaximumFiles + 1).Select(index => "File" + index + ".cs"), "File");
        Assert.False(tooLarge.Succeeded); Assert.Null(tooLarge.Value); Assert.Contains("50,000", tooLarge.Error!.Message);
    }

    [Fact]
    public void Enumeration_cancellation_is_observed_before_any_result_is_returned()
    {
        using var cancellation = new CancellationTokenSource();
        IEnumerable<string> Paths() { yield return "File1.cs"; cancellation.Cancel(); yield return "File2.cs"; }
        Assert.Throws<OperationCanceledException>((Action)(() => { _ = DeveloperProjectFileNavigation.Search(Paths(), "File", cancellation.Token); }));
    }
}
