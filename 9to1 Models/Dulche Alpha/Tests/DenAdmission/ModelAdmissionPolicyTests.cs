using NineToOne.Dulche.Den;
using Xunit;

namespace Dulche.Den.Tests;

public sealed class ModelAdmissionPolicyTests
{
    private const long EightGiB = 8L * 1024 * 1024 * 1024;
    private const long OrnithQ4KMLength = 75843911648L;
    private const long SageQ4KMLength = 21713463520L;

    private const string OrnithCanonicalId = "ornith-ai/Ornith-1.5-122B";
    private const string OrnithRepoArn = "ornith-ai/Ornith-1.5-122B-GGUF";
    private const string OrnithRevision = "8d5893d767c449a4f18dd08f4bb6d0d6d4426175";
    private const string OrnithFile = "Ornith-1.5-122B-Q4_K_M.gguf";
    private const string OrnithSha = "729fe18c2491aaa446074a923e12f184f9f2239ffd5ce3dae47b0ce9cf8df40c";

    private const string SageCanonicalId = "InternScience/Sage-A1.5";
    private const string SageRepoArn = "InternScience/Sage-A1.5-Q4_K_M-GGUF";
    private const string SageRevision = "656dec1d8d7b74fe5952cb4e83bdc42f6708e52d";
    private const string SageFile = "Sage-A1.5-Q4_K_M.gguf";
    private const string SageSha = "a64fe0bbf936db3eb54f66f142c2b2eee569ade83673649f549a21daef8ca395";

    private static ModelPreflightRequest Ornith(
        string quant = "Q4_K_M", long length = OrnithQ4KMLength, string sha = OrnithSha,
        long quota = EightGiB, string format = "GGUF", string family = "qwen35moe",
        string file = OrnithFile, string revision = OrnithRevision)
        => new(OrnithCanonicalId, OrnithRepoArn, revision, file, length, sha, quant, format, family, quota);

    private static ModelPreflightRequest Sage(
        string quant = "Q4_K_M", long length = SageQ4KMLength, string sha = SageSha,
        long quota = EightGiB, string format = "GGUF", string family = "qwen35moe",
        string file = SageFile, string revision = SageRevision)
        => new(SageCanonicalId, SageRepoArn, revision, file, length, sha, quant, format, family, quota);

    [Fact]
    public void OrnithQ4KMIsRecognizedAndBlockedAtSourceCeiling()
    {
        var report = ModelAdmissionPolicy.Preflight(Ornith());

        Assert.True(report.Recognized);
        Assert.True(report.PinnedArtifactMatched);
        Assert.True(report.FormatAdmitted);
        Assert.False(report.WithinSourceCeiling);
        Assert.Equal(ModelAdmissionStage.SizeAboveCeilingBlocked, report.Stage);

        var profile = Assert.IsType<RequestedModelProfile>(report.Profile);
        Assert.Equal(OrnithCanonicalId, profile.ModelId);
        Assert.Equal(OrnithRepoArn, profile.Artifact.RepoArn);
        Assert.Equal(OrnithRevision, profile.Artifact.Revision);
        Assert.Equal(OrnithFile, profile.Artifact.FileName);
        Assert.Equal(OrnithQ4KMLength, profile.Artifact.LengthBytes);
        Assert.Equal(OrnithSha, profile.Artifact.Sha256);
        Assert.Equal("qwen35moe", profile.ArchitectureFamily);
        Assert.Equal("MIT", profile.Source.License);
    }

    [Fact]
    public void OrnithOfficialTextQuantizationListExcludesCommunitySubstitutesAndIsFrozen()
    {
        var quants = ModelAdmissionPolicy.Preflight(Ornith()).Profile!.Artifact.OfficialPinnedQuantizations;
        Assert.Equal(5, quants.Count);
        Assert.Contains("BF16", quants);
        Assert.Contains("Q4_K_M", quants);
        Assert.Contains("Q5_K_M", quants);
        Assert.Contains("Q6_K", quants);
        Assert.Contains("Q8_0", quants);
        Assert.DoesNotContain("Q3_K_M", quants);
        Assert.DoesNotContain("Q2_K_M", quants);
        Assert.DoesNotContain("IQ2", quants);
        // Frozen: not a mutable array/list; a caller cannot cast and mutate the global registry.
        Assert.False(quants is string[]);
        Assert.False(quants is List<string>);
        var again = ModelAdmissionPolicy.Preflight(Ornith()).Profile!.Artifact.OfficialPinnedQuantizations;
        Assert.Equal(quants.ToArray(), again.ToArray());
    }

    [Fact]
    public void OrnithUnofficialLowerQuantIsNotSilentlySubstituted()
    {
        var report = ModelAdmissionPolicy.Preflight(Ornith(quant: "Q3_K_M"));
        Assert.True(report.Recognized);
        Assert.False(report.PinnedArtifactMatched);
        Assert.False(report.FormatAdmitted);
        Assert.Equal(ModelAdmissionStage.ArtifactIdentityMismatch, report.Stage);
    }

    [Fact]
    public void NonGgufFormatIsBlockedEvenForRecognizedModel()
    {
        var report = ModelAdmissionPolicy.Preflight(Ornith(format: "Safetensors"));
        Assert.True(report.Recognized);
        Assert.True(report.PinnedArtifactMatched);
        Assert.False(report.FormatAdmitted);
        Assert.Equal(ModelAdmissionStage.FormatBlocked, report.Stage);
    }

    [Fact]
    public void SageQ4KMIsQuotaBlockedAtDefaultEightGiB()
    {
        var report = ModelAdmissionPolicy.Preflight(Sage());

        Assert.True(report.Recognized);
        Assert.True(report.PinnedArtifactMatched);
        Assert.True(report.FormatAdmitted);
        Assert.True(report.WithinSourceCeiling);
        Assert.False(report.WithinDeviceQuota);
        Assert.Equal(ModelAdmissionStage.QuotaBlocked, report.Stage);
        Assert.Equal("Apache-2.0", report.Profile!.Source.License);
    }

    [Fact]
    public void SageQ4KMPassesWhenOwnerAdjustsQuota()
    {
        var report = ModelAdmissionPolicy.Preflight(Sage(quota: 24L * 1024 * 1024 * 1024));
        Assert.True(report.WithinDeviceQuota);
        Assert.Equal(ModelAdmissionStage.Recognized, report.Stage);
        Assert.Contains("Q4_K_M", report.Profile!.Artifact.OfficialPinnedQuantizations);
    }

    [Fact]
    public void UnknownModelIsNotRecognized()
    {
        var report = ModelAdmissionPolicy.Preflight(new(
            "unknown/Model-1", OrnithRepoArn, OrnithRevision, OrnithFile, OrnithQ4KMLength,
            OrnithSha, "Q4_K_M", "GGUF", "qwen35moe", EightGiB));
        Assert.False(report.Recognized);
        Assert.False(report.PinnedArtifactMatched);
        Assert.Null(report.Profile);
        Assert.Equal(ModelAdmissionStage.Unknown, report.Stage);
    }

    [Fact]
    public void RecognitionIsCaseInsensitiveWithStableCanonicalId()
    {
        var upper = ModelAdmissionPolicy.Preflight(Ornith() with { ModelId = "ORNITH-AI/ORNITH-1.5-122B" });
        var lower = ModelAdmissionPolicy.Preflight(Ornith() with { ModelId = "ornith-ai/ornith-1.5-122b" });
        Assert.True(upper.Recognized);
        Assert.True(lower.Recognized);
        Assert.Equal(OrnithCanonicalId, upper.Profile!.ModelId);
        Assert.Equal(upper.Profile.ModelId, lower.Profile!.ModelId);
    }

    // ---- Finding 2: arbitrary/incorrect caller-supplied artifact identity must not admit ----

    [Fact]
    public void ZeroLengthIsNotAdmittedForRecognizedModel()
    {
        var report = ModelAdmissionPolicy.Preflight(Ornith(length: 0));
        Assert.True(report.Recognized);
        Assert.False(report.PinnedArtifactMatched);
        Assert.Equal(ModelAdmissionStage.ArtifactIdentityMismatch, report.Stage);
    }

    [Fact]
    public void NegativeLengthIsNotAdmittedForRecognizedModel()
    {
        var report = ModelAdmissionPolicy.Preflight(Ornith(length: -1));
        Assert.True(report.Recognized);
        Assert.False(report.PinnedArtifactMatched);
        Assert.Equal(ModelAdmissionStage.ArtifactIdentityMismatch, report.Stage);
    }

    [Fact]
    public void ShaMismatchIsNotAdmittedForRecognizedModel()
    {
        var report = ModelAdmissionPolicy.Preflight(Ornith(sha: "0000000000000000000000000000000000000000000000000000000000000000"));
        Assert.True(report.Recognized);
        Assert.False(report.PinnedArtifactMatched);
        Assert.Equal(ModelAdmissionStage.ArtifactIdentityMismatch, report.Stage);
    }

    [Fact]
    public void GgufRevisionMismatchIsNotAdmittedForRecognizedModel()
    {
        var report = ModelAdmissionPolicy.Preflight(Ornith(revision: "0000000000000000000000000000000000000000000000000000000000000000"));
        Assert.True(report.Recognized);
        Assert.False(report.PinnedArtifactMatched);
        Assert.Equal(ModelAdmissionStage.ArtifactIdentityMismatch, report.Stage);
    }

    [Fact]
    public void FileNameMismatchIsNotAdmittedForRecognizedModel()
    {
        var report = ModelAdmissionPolicy.Preflight(Ornith(file: "Ornith-1.5-122B-Q5_K_M.gguf"));
        Assert.True(report.Recognized);
        Assert.False(report.PinnedArtifactMatched);
        Assert.Equal(ModelAdmissionStage.ArtifactIdentityMismatch, report.Stage);
    }

    // ---- Argument guards ----

    [Fact]
    public void NegativeDeviceQuotaThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ModelAdmissionPolicy.Preflight(Ornith(quota: -1)));
    }

    [Fact]
    public void NullModelIdThrows()
    {
        Assert.Throws<ArgumentNullException>(() => ModelAdmissionPolicy.Preflight(Ornith() with { ModelId = null! }));
    }

    [Fact]
    public void NullRequestThrows()
    {
        Assert.Throws<ArgumentNullException>(() => ModelAdmissionPolicy.Preflight(null!));
    }

    // ---- Boundary / exact official identity ----

    [Fact]
    public void SourceCeilingBoundaryIsExactlySixtyFourGiB()
    {
        Assert.Equal(64L * 1024 * 1024 * 1024, ModelAdmissionPolicy.CandidateSourceCeilingBytes);
        Assert.True(OrnithQ4KMLength > ModelAdmissionPolicy.CandidateSourceCeilingBytes);
        Assert.True(SageQ4KMLength < ModelAdmissionPolicy.CandidateSourceCeilingBytes);
    }

    [Fact]
    public void PinnedArtifactIdentitiesAreExactOfficialMetadata()
    {
        var ornith = ModelAdmissionPolicy.Preflight(Ornith()).Profile!;
        Assert.Equal(OrnithRepoArn, ornith.Artifact.RepoArn);
        Assert.Equal(OrnithRevision, ornith.Artifact.Revision);
        Assert.Equal(OrnithFile, ornith.Artifact.FileName);
        Assert.Equal(OrnithSha, ornith.Artifact.Sha256);
        Assert.Equal(OrnithQ4KMLength, ornith.Artifact.LengthBytes);
        Assert.Equal("Q4_K_M", ornith.Artifact.Quantization);

        var sage = ModelAdmissionPolicy.Preflight(Sage()).Profile!;
        Assert.Equal(SageRepoArn, sage.Artifact.RepoArn);
        Assert.Equal(SageRevision, sage.Artifact.Revision);
        Assert.Equal(SageFile, sage.Artifact.FileName);
        Assert.Equal(SageSha, sage.Artifact.Sha256);
        Assert.Equal(SageQ4KMLength, sage.Artifact.LengthBytes);
        Assert.Equal("Q4_K_M", sage.Artifact.Quantization);
    }

    [Fact]
    public void RecognizedModelIdsAreStableAndFrozen()
    {
        var ids = ModelAdmissionPolicy.RecognizedModelIds;
        Assert.Equal(2, ids.Count);
        Assert.Contains("ornith-ai/Ornith-1.5-122B", ids);
        Assert.Contains("InternScience/Sage-A1.5", ids);
        Assert.False(ids is string[]);
        // Canonical publisher case, not the lowercased dictionary key: the lowercased key must not
        // appear as a recognized id, proving we expose profile.ModelId values not map keys.
        Assert.DoesNotContain("ornith-ai/ornith-1.5-122b", ids);
        Assert.DoesNotContain("internscience/sage-a1.5", ids);
        // Repeated enumeration is stable and yields the same canonical ids.
        Assert.Equal(ids.ToArray(), ModelAdmissionPolicy.RecognizedModelIds.ToArray());
    }

    [Fact]
    public void RegistryPreflightUsesConfiguredDeviceQuota()
    {
        var registry = new LocalModelRegistry("/opt/unused/nine-to-one-den-tests", "test-device");

        var sage = registry.Preflight(Sage());
        Assert.Equal(ModelAdmissionStage.QuotaBlocked, sage.Stage);
        Assert.False(sage.WithinDeviceQuota);

        var ornith = registry.Preflight(Ornith());
        Assert.Equal(ModelAdmissionStage.SizeAboveCeilingBlocked, ornith.Stage);

        var adjusted = new LocalModelRegistry("/opt/unused/nine-to-one-den-tests", "test-device",
            maximumModelWeightBytes: 24L * 1024 * 1024 * 1024);
        Assert.Equal(ModelAdmissionStage.Recognized, adjusted.Preflight(Sage()).Stage);
    }
}
