using NineToOne.Dulche.Den;

namespace Dulche.Den.Tests;

public sealed class ModelFamilyPolicyTests
{
    private const string Qwen38 = "Qwen/Qwen3.8-Flash-Next";
    private const string Qwen36 = "Qwen/Qwen3.6-35B-A3B";
    private const long GiB = 1024L * 1024 * 1024;

    [Fact]
    public void PlannedLocalFamilyShowsTheActualQwen38FallbackAndPreservesItsIdentity()
    {
        var model = Fixture(Qwen38, displayName: "Qwen3.8-Flash-Next", family: "qwen4_exp",
            modelId: "fixture-den-model-38", source: "fixture source");

        var result = ModelFamilyPolicy.Evaluate(model, ModelFamilyCatalogueAudience.Internal,
            ModelFamilyCataloguePhase.PlannedLocalRelease);

        Assert.Equal("fixture-den-model-38", result.ModelId);
        Assert.Equal("Qwen3.8-Flash-Next", result.DisplayName);
        Assert.Equal("fixture source", result.Source);
        Assert.Equal(Qwen38, result.ExternalModelId);
        Assert.Equal(ModelFamilyCatalogueLabel.TemporaryDevelopmentFallbackForPlannedQwen4, result.Label);
        Assert.Equal(ModelFamilyRecommendation.TemporaryDevelopmentFallback, result.Recommendation);
        Assert.Equal("Qwen4 (planned local family)", result.IntendedFamilyLabel);
        Assert.Equal(ModelFamilyCatalogueAudience.Internal, result.Audience);
        Assert.Equal(ModelFamilyCataloguePhase.PlannedLocalRelease, result.Phase);
    }

    [Fact]
    public void SupportedLocalQwen4PhaseKeepsQwen38AsSeparatelyNamedLegacyOption()
    {
        var model = Fixture(Qwen38, displayName: "Qwen3.8-Flash-Next", modelId: "fixture-legacy-38");

        var result = ModelFamilyPolicy.Evaluate(model, ModelFamilyCatalogueAudience.Internal,
            ModelFamilyCataloguePhase.SupportedLocalRelease);

        Assert.Equal(ModelFamilyCatalogueLabel.Legacy, result.Label);
        Assert.Equal(ModelFamilyRecommendation.LegacyOption, result.Recommendation);
        Assert.Equal(ModelFamilyPublicCatalogueDisposition.Unchanged, result.PublicCatalogueDisposition);
        Assert.Equal(model.Id, result.ModelId);
        Assert.Equal(model.DisplayName, result.DisplayName);
        Assert.Equal(model.OfficialArtifact!.ExternalModelId, result.ExternalModelId);
    }

    [Fact]
    public void PublicRolloutRetainsFallbackAndLegacyRowsUntilThePublicReadyPhase()
    {
        var model = Fixture(Qwen38, displayName: "Qwen3.8-Flash-Next");

        var planned = ModelFamilyPolicy.Evaluate(model, ModelFamilyCatalogueAudience.Public,
            ModelFamilyCataloguePhase.PlannedLocalRelease);
        var supported = ModelFamilyPolicy.Evaluate(model, ModelFamilyCatalogueAudience.Public,
            ModelFamilyCataloguePhase.SupportedLocalRelease);

        Assert.Equal(ModelFamilyCatalogueLabel.TemporaryDevelopmentFallbackForPlannedQwen4, planned.Label);
        Assert.Equal(ModelFamilyRecommendation.TemporaryDevelopmentFallback, planned.Recommendation);
        Assert.Equal(ModelFamilyPublicCatalogueDisposition.Unchanged, planned.PublicCatalogueDisposition);
        Assert.Equal("Qwen3.8-Flash-Next", planned.DisplayName);
        Assert.Equal(Qwen38, planned.ExternalModelId);
        Assert.Equal(ModelFamilyCatalogueLabel.Legacy, supported.Label);
        Assert.Equal(ModelFamilyRecommendation.LegacyOption, supported.Recommendation);
        Assert.Equal(ModelFamilyPublicCatalogueDisposition.Unchanged, supported.PublicCatalogueDisposition);
        Assert.Equal("Qwen3.8-Flash-Next", supported.DisplayName);
        Assert.Equal(Qwen38, supported.ExternalModelId);
    }

    [Fact]
    public void PublicReadyExcludesQwen38UnlessExplicitRegressionFallbackIsEnabled()
    {
        var model = Fixture(Qwen38, displayName: "Qwen3.8-Flash-Next");

        var excluded = ModelFamilyPolicy.Evaluate(model, ModelFamilyCatalogueAudience.Public,
            ModelFamilyCataloguePhase.PublicReady);
        var includedForRegression = ModelFamilyPolicy.Evaluate(model, ModelFamilyCatalogueAudience.Public,
            ModelFamilyCataloguePhase.PublicReady, allowPublicQwen38ForSignificantQwen4Regression: true);
        var earlyFlagDoesNotExposeFallback = ModelFamilyPolicy.Evaluate(model, ModelFamilyCatalogueAudience.Public,
            ModelFamilyCataloguePhase.PlannedLocalRelease, allowPublicQwen38ForSignificantQwen4Regression: true);

        Assert.Equal(ModelFamilyRecommendation.Excluded, excluded.Recommendation);
        Assert.Equal(ModelFamilyPublicCatalogueDisposition.Excluded, excluded.PublicCatalogueDisposition);
        Assert.Equal(ModelFamilyRecommendation.ExplicitSignificantRegressionFallback, includedForRegression.Recommendation);
        Assert.Equal(ModelFamilyPublicCatalogueDisposition.IncludedForExplicitSignificantRegressionFallback,
            includedForRegression.PublicCatalogueDisposition);
        Assert.Equal(ModelFamilyPublicCatalogueDisposition.Unchanged, earlyFlagDoesNotExposeFallback.PublicCatalogueDisposition);
        Assert.Equal("Qwen3.8-Flash-Next", includedForRegression.DisplayName);
        Assert.Equal(Qwen38, includedForRegression.ExternalModelId);
    }

    [Fact]
    public void Qwen36IsExcludedFromRecommendationsWithoutChangingCatalogueOrCompatibilityData()
    {
        var model = Fixture(Qwen36, displayName: "Qwen3.6-35B-A3B", modelId: "fixture-preserved-qwen36",
            source: "fixture preserved source");

        var result = ModelFamilyPolicy.Evaluate(model, ModelFamilyCatalogueAudience.Internal,
            ModelFamilyCataloguePhase.SupportedLocalRelease);

        Assert.Equal(ModelFamilyRecommendation.Excluded, result.Recommendation);
        Assert.Equal(ModelFamilyCatalogueLabel.Unchanged, result.Label);
        Assert.Equal(ModelFamilyPublicCatalogueDisposition.Unchanged, result.PublicCatalogueDisposition);
        Assert.Equal(model.Id, result.ModelId);
        Assert.Equal(model.DisplayName, result.DisplayName);
        Assert.Equal(model.Source, result.Source);
        Assert.Equal(model.OfficialArtifact!.ExternalModelId, result.ExternalModelId);
        Assert.Equal("fixture-preserved-qwen36", model.Id);
    }

    [Fact]
    public void MutableNamesAndArchitectureDoNotCreateQwenIdentityOrAnOfficialQwen4Variant()
    {
        var notVerified = Fixture("fixture/unverified-model-id", displayName: "Qwen3.8-Flash-Next",
            family: "qwen4_exp", modelId: "fixture-not-verified-qwen4");

        var result = ModelFamilyPolicy.Evaluate(notVerified, ModelFamilyCatalogueAudience.Internal,
            ModelFamilyCataloguePhase.PlannedLocalRelease);

        Assert.Equal(ModelFamilyCatalogueLabel.Unchanged, result.Label);
        Assert.Equal(ModelFamilyRecommendation.Unchanged, result.Recommendation);
        Assert.Null(result.IntendedFamilyLabel);
        Assert.Equal("fixture-not-verified-qwen4", result.ModelId);
        Assert.Equal("Qwen3.8-Flash-Next", result.DisplayName);
        Assert.Equal("fixture/unverified-model-id", result.ExternalModelId);
    }

    [Fact]
    public void ResourceClassUsesKnownStorageOrMemoryBytesAndNeverActiveParameterCounts()
    {
        var small = Fixture("fixture/generic", storageBytes: PortableMetric.Measured(6L * GiB),
            memoryBytes: PortableMetric.Measured(4L * GiB)) with
        {
            ActiveMoeParameters = PortableMetric.Measured(999_000_000_000m)
        };
        var large = Fixture("fixture/generic", storageBytes: PortableMetric.Measured(6L * GiB),
            memoryBytes: PortableMetric.Measured(40L * GiB));
        var unknown = Fixture("fixture/generic", includeOfficialArtifact: false) with
        {
            ActiveMoeParameters = PortableMetric.Measured(999_000_000_000m)
        };

        Assert.Equal(ModelResourceClass.Small, ModelFamilyPolicy.Evaluate(small,
            ModelFamilyCatalogueAudience.Internal, ModelFamilyCataloguePhase.PlannedLocalRelease).ResourceClass);
        Assert.Equal(ModelResourceClass.Large, ModelFamilyPolicy.Evaluate(large,
            ModelFamilyCatalogueAudience.Internal, ModelFamilyCataloguePhase.PlannedLocalRelease).ResourceClass);
        Assert.Equal(ModelResourceClass.Unknown, ModelFamilyPolicy.Evaluate(unknown,
            ModelFamilyCatalogueAudience.Internal, ModelFamilyCataloguePhase.PlannedLocalRelease).ResourceClass);
    }

    [Fact]
    public void UnrelatedModelRowsPassThroughWithoutRecommendationOrPublicCatalogueChanges()
    {
        var model = Fixture("fixture/other-family", displayName: "Fixture other model", modelId: "fixture-other");

        var result = ModelFamilyPolicy.Evaluate(model, ModelFamilyCatalogueAudience.Public,
            ModelFamilyCataloguePhase.PublicReady);

        Assert.Equal(ModelFamilyCatalogueLabel.Unchanged, result.Label);
        Assert.Equal(ModelFamilyRecommendation.Unchanged, result.Recommendation);
        Assert.Equal(ModelFamilyPublicCatalogueDisposition.Unchanged, result.PublicCatalogueDisposition);
        Assert.Equal(model.Id, result.ModelId);
        Assert.Equal(model.Source, result.Source);
        Assert.Equal(model.OfficialArtifact!.ExternalModelId, result.ExternalModelId);
    }

    [Fact]
    public void UndefinedAudienceAndPhaseAreRejectedInsteadOfBeingClassifiedAsFallbackOrLegacy()
    {
        var model = Fixture(Qwen38, displayName: "Qwen3.8-Flash-Next");

        Assert.Throws<ArgumentOutOfRangeException>(() => ModelFamilyPolicy.Evaluate(model,
            (ModelFamilyCatalogueAudience)999, ModelFamilyCataloguePhase.SupportedLocalRelease));
        Assert.Throws<ArgumentOutOfRangeException>(() => ModelFamilyPolicy.Evaluate(model,
            ModelFamilyCatalogueAudience.Public, (ModelFamilyCataloguePhase)999));
    }

    private static ModelRecord Fixture(string externalModelId, string displayName = "Fixture model",
        string family = "fixture-family", string modelId = "fixture-model", string source = "fixture source",
        PortableMetric? storageBytes = null, PortableMetric? memoryBytes = null,
        bool includeOfficialArtifact = true)
    {
        // Synthetic unit-test record only. External model IDs are the exact publisher identities in the
        // task evidence; fixture artifact fields are not persisted or asserted as publisher metadata.
        return new ModelRecord
        {
            Id = modelId,
            NamespaceId = "fixture-namespace",
            DisplayName = displayName,
            ArtifactRevision = "fixture-only-revision",
            Source = source,
            Family = family,
            ActiveMoeParameters = PortableMetric.Empty(),
            StorageBytes = storageBytes ?? PortableMetric.Empty(),
            HardwareMemoryBytes = memoryBytes ?? PortableMetric.Empty(),
            OfficialArtifact = includeOfficialArtifact ? new OfficialArtifactTuple("fixture-repository", "fixture-revision",
                "fixture-artifact.bin", 1,
                "0000000000000000000000000000000000000000000000000000000000000000",
                "fixture-quantization", externalModelId) : null
        };
    }
}
