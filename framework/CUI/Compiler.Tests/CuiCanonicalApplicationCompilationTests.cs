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
        var result = new CuiCompiler().Compile(File.ReadAllText(path), path,
            new(new("1", "1", "2.0"), new CuiSourceIndex().Index(RepositoryRoot())));
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Output);
    }

    /// <summary>
    /// The Spaces and Chat surfaces this merge is delivering, compiled against the real
    /// canonical compiler rather than assumed to be canonical.
    ///
    /// These are the files most likely to drift into an invented dialect - elements such as
    /// <c>Aside</c>, <c>AdaptiveSplit</c>, <c>Heading</c> and <c>List</c> read naturally but are
    /// not in CUI's vocabulary, and a source file can look like CUI while compiling as nothing.
    /// </summary>
    [Theory]
    [InlineData("9to1 Workspace/Spaces/UI/Home/SpacesHome.cui")]
    [InlineData("9to1 Workspace/Spaces/UI/Chat/ChatSpace.cui")]
    [InlineData("9to1 Workspace/Spaces/UI/Chat/ChatComposer.cui")]
    [InlineData("9to1 Workspace/Spaces/UI/Tasks/SpaceTaskWidget.cui")]
    [InlineData("9to1 Workspace/Boards/app/Boards.cui")]
    [InlineData("9to1 Workspace/Home/UI/Home.cui")]
    [InlineData("9to1 Workspace/Home/UI/ModelPicker.cui")]
    [InlineData("9to1 Workspace/Mail/UI/MailWorkspace.cui")]
    public void Actual_spaces_and_chat_source_compiles_against_canonical_cui(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath);
        Assert.True(File.Exists(path), $"Shipping source missing: {relativePath}");
        var result = new CuiCompiler().Compile(File.ReadAllText(path), path,
            new(new("1", "1", "2.0"), new CuiSourceIndex().Index(RepositoryRoot())));
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Output);
    }
    private static string RepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "../../.."));
}
