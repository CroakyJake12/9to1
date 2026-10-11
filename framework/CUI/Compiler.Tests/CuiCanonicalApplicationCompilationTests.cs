using System.Runtime.CompilerServices;
using CakeOS.Cui.Compiler;
using Xunit;

namespace CakeOS.Cui.Compiler.Tests;

/// <summary>Static acceptance of actual shipping source; never substitutes for app/runtime acceptance.</summary>
public sealed class CuiCanonicalApplicationCompilationTests
{
    [Theory]
    [InlineData("apps/Web/Home/Home.cui")]
    [InlineData("apps/Web/Write/Write.cui")]
    [InlineData("apps/Web/Productivity/Present/Present.cui")]
    [InlineData("apps/Web/Productivity/Boards/Boards.cui")]
    [InlineData("apps/Web/Picture/Picture.cui")]
    [InlineData("apps/Web/Wave/Wave.cui")]
    public void Actual_first_party_source_obeys_the_canonical_compiler(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath);
        Assert.True(File.Exists(path), $"Shipping source missing: {relativePath}");
        var result = new CuiCompiler().Compile(File.ReadAllText(path), path, new(new("1", "1", "2.0")));
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Output);
    }
    private static string RepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "../../.."));
}
