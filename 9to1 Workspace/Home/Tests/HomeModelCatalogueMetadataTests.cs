using Dulche.Runtime;
using HavenOS.Home;
using HavenOS.Home.Core;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeModelCatalogueMetadataTests
{
    private static readonly ModelIdentity ProviderIdentity = new("fixture-provider", "opaque-provider-id", "fixture-revision");
    private static ModelRecord Metadata(bool official = true) => new()
    {
        Id = "different-den-id", NamespaceId = "software-fixture", DisplayName = "Qwen4-looking alias",
        Provider = ProviderIdentity.ProviderId, ArtifactRevision = "fixture-revision", Source = "software fixture source",
        Family = "qwen4-looking-architecture", Format = "GGUF", Quantisation = "fixture-quant",
        StorageBytes = PortableMetric.Empty(), HardwareMemoryBytes = PortableMetric.Unavailable(),
        OfficialArtifact = official ? new("fixture/repo", "fixture-revision", "fixture.gguf", 123,
            new string('0', 64), "fixture-quant", "Qwen/Qwen3.8-Flash-Next") : null
    };

    [Fact]
    public void ExactBindingPreservesDistinctIdentitiesAndAppliesExistingPolicy()
    {
        var projection = HomeModelCatalogueMetadataProjection.Project(ProviderIdentity,
            new(ProviderIdentity, "owner-binding-revision", Metadata()), new());
        Assert.NotNull(projection);
        Assert.Equal("different-den-id", projection.DenModelId);
        Assert.Equal("Qwen/Qwen3.8-Flash-Next", projection.ExternalModelId);
        Assert.Equal(ModelFamilyCatalogueLabel.TemporaryDevelopmentFallbackForPlannedQwen4, projection.FamilyLabel);
        Assert.Equal(123, projection.KnownStorageBytes);
        Assert.Null(projection.KnownHardwareMemoryBytes);
        Assert.Equal(MetricAvailability.ProviderUnavailable, projection.HardwareMemoryMetric.Availability);
    }

    [Fact]
    public void NameAliasAndArchitectureNeverCreatePublisherIdentity()
    {
        var projection = HomeModelCatalogueMetadataProjection.Project(ProviderIdentity,
            new(ProviderIdentity, "owner-binding-revision", Metadata(false)), new());
        Assert.NotNull(projection);
        Assert.Null(projection.ExternalModelId);
        Assert.Equal(ModelFamilyCatalogueLabel.Unchanged, projection.FamilyLabel);
        Assert.Equal(ModelResourceClass.Unknown, projection.ResourceClass);
        Assert.Null(HomeModelCatalogueMetadataProjection.Project(ProviderIdentity, null, new()));
    }

    [Fact]
    public void WrongProviderModelOrRevisionNeverMatchesExactBinding()
    {
        foreach (var identity in new[] { ProviderIdentity with { ProviderId = "other" }, ProviderIdentity with { ModelId = "other" },
                     ProviderIdentity with { ArtifactRevision = "other" }, ProviderIdentity with { ArtifactRevision = null } })
            Assert.Throws<InvalidDataException>(() => HomeModelCatalogueMetadataProjection.Project(ProviderIdentity,
                new(identity, "owner-binding-revision", Metadata()), new()));
        Assert.Throws<InvalidDataException>(() => HomeModelCatalogueMetadataProjection.Project(ProviderIdentity,
            new(ProviderIdentity, "owner-binding-revision", Metadata() with { ArtifactRevision = "different-record-revision" }), new()));
    }

    [Fact]
    public void ExplicitUnversionedBindingDoesNotUpgradeLegacyRouteIdentity()
    {
        var identity = ProviderIdentity with { ArtifactRevision = null };
        var presentation = HomeModelCatalogueMetadataProjection.Project(identity, new(identity, "bound-unversioned-row", Metadata()), new());
        var row = new HomeModelPickerCatalogueEntry(identity.ProviderId, identity.ModelId, null,
            "Actual provider name", "Provider", true, null, null, null, null, null, presentation);
        var rich = HomeModelCatalogueEntry.From(row);
        Assert.Null(rich.Identity!.ArtifactRevision);
        Assert.Equal(identity.ModelId, rich.ModelId);
        Assert.Equal("Actual provider name", rich.DisplayName);
        Assert.Equal("fixture-revision", rich.Presentation!.ArtifactRevision);
        Assert.Equal("fixture-quant", rich.Quantisation);
        Assert.Equal(MetricAvailability.Empty, rich.StorageBytes.Availability);
        Assert.Equal(MetricAvailability.ProviderUnavailable, rich.HardwareMemoryBytes.Availability);
        Assert.Equal("Qwen/Qwen3.8-Flash-Next", rich.ExternalModelId);
    }

    [Fact]
    public void RichDenProjectionRetainsPublisherIdWithoutGrantingAvailability()
    {
        var rich = HomeModelCatalogueEntry.From(Metadata());
        Assert.Equal("Qwen/Qwen3.8-Flash-Next", rich.ExternalModelId);
        Assert.Equal(HomeModelProviderAvailability.Unknown, rich.ProviderFacts.Availability);
        Assert.Null(rich.Presentation); // No trusted provider binding/release-policy context supplied.
    }

    [Fact]
    public void OfficialArtifactMismatchFailsRatherThanProducingFamilyClaims()
    {
        Assert.Throws<DenException>(() => HomeModelCatalogueMetadataProjection.Project(ProviderIdentity,
            new(ProviderIdentity, "binding", Metadata() with { ArtifactSha256 = new string('1', 64) }), new()));
    }

    [Theory]
    [InlineData("C:\\Users\\private-user\\secret-models\\weights.gguf")]
    [InlineData("https://user:secret@example.invalid/model?token=credential")]
    [InlineData("Bearer secret-credential")]
    [InlineData("../private-folder/model.gguf")]
    public void UnconstrainedDenSourceIsNotExposedInPickerPresentation(string privateSource)
    {
        var record = Metadata() with { Source = privateSource };
        var presentation = HomeModelCatalogueMetadataProjection.Project(ProviderIdentity, new(ProviderIdentity, "binding", record), new());
        Assert.Null(presentation!.Source);
        Assert.Null(HomeModelCatalogueEntry.From(record).Source);
        var json = System.Text.Json.JsonSerializer.Serialize(presentation);
        Assert.DoesNotContain(privateSource, json);
        Assert.Equal("Qwen/Qwen3.8-Flash-Next", presentation.ExternalModelId);
    }
}
