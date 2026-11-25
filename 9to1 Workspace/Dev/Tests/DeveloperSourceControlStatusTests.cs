using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

/// <summary>APP-BUILD-001 / ED-BUILD-001: Code provides source control and the workspace owns source-control connections.
/// Proves per-file state reporting, foreign-path NotInRepository (no guessing), and conflict surfacing.</summary>
public sealed class DeveloperSourceControlStatusTests
{
    private static readonly Guid DocA = Guid.NewGuid();
    private static readonly Guid DocB = Guid.NewGuid();
    private static readonly Guid DocForeign = Guid.NewGuid();

    private static DeveloperSourceControlStatus Status() => DeveloperSourceControlStatus.From("binding-1", "repo-abc", [
        new DeveloperSourceControlEntry("src/A.cs", DeveloperFileSourceState.Modified),
        new DeveloperSourceControlEntry("src/B.cs", DeveloperFileSourceState.Conflicted)]);

    [Fact]
    public void ReportsPerFileStateWithinTheBoundRepository()
    {
        var status = Status();
        Assert.Equal(DeveloperFileSourceState.Modified, status.StatusFor(DocA, "src/A.cs").State);
        Assert.Equal("repo-abc", status.StatusFor(DocA, "src/A.cs").CanonicalRepositoryId);
    }

    [Fact]
    public void ForeignPathIsNotInRepositoryNeverGuessed()
    {
        // A path with no observed entry is NotInRepository, not assumed clean.
        var result = Status().StatusFor(DocForeign, "outside/file.txt");
        Assert.Equal(DeveloperFileSourceState.NotInRepository, result.State);
    }

    [Fact]
    public void ConflictsAreSurfacedExplicitly()
    {
        var status = Status();
        Assert.True(status.StatusFor(DocB, "src/B.cs").IsConflicted);
        Assert.Contains("src/B.cs", status.ConflictedPaths());
        Assert.DoesNotContain("src/A.cs", status.ConflictedPaths());
    }

    [Fact]
    public void BackslashPathIsNormalizedBeforeLookup()
    {
        var result = Status().StatusFor(DocA, "src\\A.cs");
        Assert.Equal(DeveloperFileSourceState.Modified, result.State);
    }

    [Fact]
    public void InvalidEntriesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => new DeveloperSourceControlEntry("", DeveloperFileSourceState.Clean).Capture());
        Assert.Throws<InvalidDataException>(() => new DeveloperSourceControlEntry("../escape", DeveloperFileSourceState.Clean).Capture());
        // NotInRepository is a projection result, never an observed entry state.
        Assert.Throws<InvalidDataException>(() => new DeveloperSourceControlEntry("a.cs", DeveloperFileSourceState.NotInRepository).Capture());
    }

    [Fact]
    public void DuplicatePathIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => DeveloperSourceControlStatus.From("b", "repo", [
            new DeveloperSourceControlEntry("a.cs", DeveloperFileSourceState.Clean),
            new DeveloperSourceControlEntry("a.cs", DeveloperFileSourceState.Modified)]));
    }

    [Fact]
    public void EmptyStatusReportsNotInRepositoryForEverything()
    {
        var status = DeveloperSourceControlStatus.From("b", "repo", []);
        Assert.Equal(DeveloperFileSourceState.NotInRepository, status.StatusFor(DocA, "src/A.cs").State);
        Assert.Empty(status.ConflictedPaths());
    }
}
