using HavenOS.Apps.Browse.Runtime;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed class ChromiumRuntimeProvenanceTests
{
    private const string Source = ChromiumRuntimeProvenance.SnapshotSourceCommit;
    private const string Digest = ChromiumRuntimeProvenance.LinuxSnapshotExecutableSha256;

    [Fact]
    public void ExactReportedRevisionIsIdentifiedSeparately()
    {
        Assert.Contains("Runtime revision matches", ChromiumRuntimeProvenance.VerifyRevision(Source, Digest, Source, true));
    }

    [Fact]
    public void KnownZeroStampedSnapshotUsesArtifactProvenanceNotAClaimedRuntimeMatch()
    {
        var evidence = ChromiumRuntimeProvenance.VerifyRevision(Source, Digest, new string('0', 40), true);
        Assert.Contains("runtime reports an all-zero stamp", evidence);
        Assert.DoesNotContain("Runtime revision matches", evidence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("different")]
    [InlineData("1111111111111111111111111111111111111111")]
    public void MissingOrConflictingStampIsNeverHiddenByTheSnapshotException(string? reported)
    {
        Assert.Throws<InvalidDataException>(() => ChromiumRuntimeProvenance.VerifyRevision(Source, Digest, reported, true));
    }

    [Fact]
    public void UnknownBinaryCannotUseTheZeroStampException()
    {
        Assert.Throws<InvalidDataException>(() => ChromiumRuntimeProvenance.VerifyRevision(Source, new string('a', 64), new string('0', 40), true));
    }

    [Fact]
    public void AnotherSourceOrPlatformCannotUseTheKnownSnapshotException()
    {
        Assert.Throws<InvalidDataException>(() => ChromiumRuntimeProvenance.VerifyRevision(new string('a', 40), Digest, new string('0', 40), true));
        Assert.Throws<InvalidDataException>(() => ChromiumRuntimeProvenance.VerifyRevision(Source, Digest, new string('0', 40), false));
    }
}
