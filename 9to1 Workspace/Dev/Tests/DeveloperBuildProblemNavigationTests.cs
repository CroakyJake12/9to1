using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperBuildProblemNavigationTests
{
    [Fact]
    public void OnlyConfinedLiteralRelativePathsCanEnterNormalAuthorizedOpen()
    {
        var diagnostic = Problem("src/actual source.cs", 0, 0);
        Assert.True(DeveloperBuildProblemNavigation.TryGetRelativePath(diagnostic, out var path)); Assert.Equal(diagnostic.RelativePath, path);
        foreach (var refused in new[] { "../outside.cs", "src/../other.cs", "/outside.cs", "C:\\outside.cs", "//server/share.cs", "src//file.cs", "src/./file.cs", "bad\0.cs" })
            Assert.False(DeveloperBuildProblemNavigation.TryGetRelativePath(diagnostic with { RelativePath = refused }, out _));
        if (!OperatingSystem.IsWindows()) Assert.False(DeveloperBuildProblemNavigation.TryGetRelativePath(diagnostic with { RelativePath = "src\\file.cs" }, out _));
    }

    [Fact]
    public void ReportedCoordinatesResolveOnlyAgainstActualCurrentTextWithoutClampingOrSplittingSurrogates()
    {
        const string text = "first\r\n😀value\n";
        Assert.True(DeveloperBuildProblemNavigation.TryResolveCurrentSavedText(Problem("source.cs", 1, 2), text, out var offset)); Assert.Equal(9, offset);
        foreach (var coordinate in new[] { (1, 1), (50, 0), (0, 50), (-1, 0), (int.MaxValue, 0) })
            Assert.False(DeveloperBuildProblemNavigation.TryResolveCurrentSavedText(Problem("source.cs", coordinate.Item1, coordinate.Item2), text, out _));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => { _ = DeveloperBuildProblemNavigation.TryResolveCurrentSavedText(Problem("source.cs", 0, 1), text, out _, cancelled.Token); });
    }
    private static CodeDiagnostic Problem(string path, int line, int column) => new(path,
        new(new(line, column), new(line, column)), CodeDiagnosticSeverity.Error, "TEST", "dotnet build", "reported problem");
}
