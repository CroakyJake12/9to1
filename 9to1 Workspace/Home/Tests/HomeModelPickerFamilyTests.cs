using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using Dulche.Runtime;
using HavenOS.Home;
using HavenOS.Home.Core;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeModelPickerFamilyTests
{
    private static HomeModelPickerCatalogueEntry Fallback()
    {
        var identity = new ModelIdentity("actual-provider", "opaque-real-model-id", "fixture-artifact");
        var record = new ModelRecord
        {
            Id = "separate-den-id", NamespaceId = "software-fixture", Provider = identity.ProviderId,
            DisplayName = "Fixture metadata", ArtifactRevision = identity.ArtifactRevision!,
            OfficialArtifact = new("fixture/repo", identity.ArtifactRevision!, "fixture.gguf", 100,
                new string('0', 64), "fixture-quant", "Qwen/Qwen3.8-Flash-Next")
        };
        var presentation = HomeModelCatalogueMetadataProjection.Project(identity, new(identity, "exact-binding", record), new());
        return new(identity.ProviderId, identity.ModelId, identity.ArtifactRevision, "Actual Qwen3.8 provider label", "Provider", true,
            new HashSet<string> { "Text" }, 2048, "unavailable", null, null, presentation);
    }

    private static HomeModelPickerCuiSurface Surface() => new(new CuiRichParser().Parse("<Cui><Page/></Cui>"));
    private static T Value<T>(ICuiBindingContext context, string name)
    { Assert.True(context.TryGetValue(name, out var value)); return Assert.IsAssignableFrom<T>(value); }

    [Fact]
    public void PlannedHeaderHasNoExecutableSelectionAndReportsMissingFallback()
    {
        var surface = Surface();
        var group = Assert.Single(surface.Projection.Families);
        Assert.Empty(group.Children);
        Assert.False(group.TryGetValue("Selection", out _));
        Assert.False(group.TryGetValue("ProviderId", out _));
        Assert.Equal("Refresh to check for a Qwen3.8 fallback.", Value<string>(group, "FallbackStatus"));
        Assert.Empty(surface.Catalogue);
        Assert.False(Value<bool>(surface, "HasCatalogue"));
        surface.ApplyCatalogue(new([], 0, 0, false, 1), "Read completed");
        Assert.Equal("No Qwen3.8 fallback is registered yet.", Value<string>(Assert.Single(surface.Projection.Families), "FallbackStatus"));
        surface.ApplyCatalogue(null, "Service unavailable");
        Assert.Equal("Could not check the Qwen3.8 fallback. Try refreshing.", Value<string>(Assert.Single(surface.Projection.Families), "FallbackStatus"));
    }

    [Fact]
    public void OnlyExactLocalBoundFallbackIsGroupedAndRealSelectionIsUnchanged()
    {
        var actual = Fallback();
        var misleading = actual with { ModelId = "looks-like-qwen4", DisplayName = "Qwen3.8-Flash-Next", Presentation = null };
        var remote = actual with { ModelId = "remote-fallback", IsLocal = false };
        var wrong = actual with { ModelId = "other-external", Presentation = actual.Presentation! with { ExternalModelId = "other/publisher" } };
        var surface = Surface();
        surface.ApplyCatalogue(new([actual, misleading, remote, wrong], 0, 4, false, 1), "Loaded");
        var child = Assert.Single(Assert.Single(surface.Projection.Families).Children);
        Assert.Equal(3, surface.Projection.UngroupedCatalogue.Count);
        Assert.Equal(4, surface.Projection.Catalogue.Count); // Existing full inventory remains available.
        Assert.Equal(new HomeModelPickerCatalogueSelection(actual.ProviderId, actual.ModelId, actual.ArtifactRevision),
            Value<HomeModelPickerCatalogueSelection>(child, "Selection"));
        Assert.Equal(actual.DisplayName, Value<string>(child, "DisplayName"));
        Assert.Contains("unavailable", Value<string>(child, "RuntimeState"));
        Assert.Equal("Small · Model file: <0.01 GiB · Required memory: unknown", Value<string>(child, "ResourceSummary"));
    }

    [Fact]
    public async Task TypedExpansionSurvivesRefreshAndRejectsUnknownGroup()
    {
        var provider = new Provider([Fallback()]);
        var controller = new HomeModelPickerCuiController(new(provider), Surface());
        await controller.OpenAsync("User", "chat");
        await controller.ExecuteAsync(new(HomeModelPickerAction.RefreshCatalogue));
        var selection = Value<HomeModelPickerFamilySelection>(Assert.Single(controller.Surface.Projection.Families), "ToggleSelection");
        Assert.True((await controller.ExecuteAsync(new(HomeModelPickerAction.ToggleFamily, FamilyKey: selection.FamilyKey))).Succeeded);
        await controller.ExecuteAsync(new(HomeModelPickerAction.RefreshCatalogue));
        Assert.True(Assert.Single(controller.Surface.Projection.Families).Expanded);
        Assert.False((await controller.ExecuteAsync(new(HomeModelPickerAction.ToggleFamily, FamilyKey: "forged"))).Succeeded);
        Assert.True(Assert.Single(controller.Surface.Projection.Families).Expanded);
        controller.PublicationGate = () => false;
        Assert.False((await controller.ExecuteAsync(new(HomeModelPickerAction.ToggleFamily, FamilyKey: selection.FamilyKey))).Succeeded);
        Assert.True(Assert.Single(controller.Surface.Projection.Families).Expanded);
    }

    [Fact]
    public async Task ForgedGroupCannotEnterRouteAndActualChildKeepsArtifactIdentity()
    {
        var actual = Fallback();
        var provider = new Provider([actual]);
        var controller = new HomeModelPickerCuiController(new(provider), Surface());
        await controller.OpenAsync("User", "chat");
        await controller.ExecuteAsync(new(HomeModelPickerAction.RefreshCatalogue));
        var fake = await controller.ExecuteAsync(new(HomeModelPickerAction.AddCatalogueCandidate,
            ProviderId: "planned", ModelId: HomeModelPickerFamilyRow.PlannedQwen4Key));
        Assert.False(fake.Succeeded);
        Assert.Equal("CatalogueEntryNotLoaded", fake.Code);
        Assert.Empty(controller.Surface.State.Candidates);
        var real = await controller.ExecuteAsync(new(HomeModelPickerAction.AddCatalogueCandidate,
            ProviderId: actual.ProviderId, ModelId: actual.ModelId, ArtifactRevision: actual.ArtifactRevision));
        Assert.True(real.Succeeded);
        var selected = Assert.Single(controller.Surface.State.Candidates);
        Assert.Equal(actual.ModelId, selected.ModelId);
        Assert.Equal(actual.ArtifactRevision, selected.ArtifactRevision);
        Assert.DoesNotContain("Qwen4", selected.ModelId);
    }

    [Fact]
    public void PlannedHeaderDoesNotSurviveAnExplicitSupportedReleasePhase()
    {
        var actual = Fallback();
        var surface = Surface();
        surface.ApplyCatalogue(new([actual], 0, 1, false, 1,
            new(Phase: ModelFamilyCataloguePhase.SupportedLocalRelease)), "Supported phase supplied by owner");
        Assert.Empty(surface.Projection.Families);
        Assert.Single(surface.Projection.UngroupedCatalogue);
    }

    [Fact]
    public void MissingRuntimeStateIsUnknownRatherThanInstalledOrReady()
    {
        var surface = Surface();
        surface.ApplyCatalogue(new([Fallback() with { LifecycleState = null }], 0, 1, false, 1), "Loaded");
        var child = Assert.Single(Assert.Single(surface.Projection.Families).Children);
        Assert.Equal("Availability unknown", Value<string>(child, "RuntimeState"));
    }

    [Fact]
    public void ResourceDetailsSeparateReadableModelFileSizeAndRequiredMemory()
    {
        var entry = Fallback();
        entry = entry with { Presentation = entry.Presentation! with
            { ResourceClass = ModelResourceClass.Large, KnownStorageBytes = 32L * 1024 * 1024 * 1024,
              KnownHardwareMemoryBytes = 48L * 1024 * 1024 * 1024 } };
        var surface = Surface();
        surface.ApplyCatalogue(new([entry], 0, 1, false, 1), "Updated");
        var row = Assert.Single(Assert.Single(surface.Projection.Families).Children);
        Assert.Equal("Large · Model file: 32 GiB · Required memory: 48 GiB", Value<string>(row, "ResourceSummary"));
        Assert.Equal(new HomeModelPickerCatalogueSelection(entry.ProviderId, entry.ModelId, entry.ArtifactRevision),
            Value<HomeModelPickerCatalogueSelection>(row, "Selection"));
    }

    [Fact]
    public void ReportedValuesDoNotReplaceKnownArtifactAndMemoryFacts()
    {
        const long gib = 1024L * 1024 * 1024;
        var entry = Fallback();
        entry = entry with
        {
            ReportedMetadata = new HomeModelProviderReportedMetadata(8 * gib, "Q4_K_M"),
            Presentation = entry.Presentation! with
            {
                KnownStorageBytes = 2 * gib, KnownHardwareMemoryBytes = 3 * gib,
                Quantisation = "Q8_0", ResourceClass = ModelResourceClass.Small
            }
        };
        var surface = Surface();
        surface.ApplyCatalogue(new([entry], 0, 1, false, 1), "Updated");
        var row = Assert.Single(Assert.Single(surface.Projection.Families).Children);
        Assert.Equal("Small · Model file: 2 GiB · Required memory: 3 GiB", Value<string>(row, "ResourceSummary"));
        Assert.Equal("Q8_0", Value<string>(row, "Quantisation"));
        Assert.Equal(new HomeModelPickerCatalogueSelection(entry.ProviderId, entry.ModelId, entry.ArtifactRevision),
            Value<HomeModelPickerCatalogueSelection>(row, "Selection"));
    }

    private sealed class Provider(IReadOnlyList<HomeModelPickerCatalogueEntry> entries) : IHomeModelPickerFeatureProvider
    {
        public Task<HomeCoreOperationResult<HomeModelCataloguePage>> GetCatalogueAsync(string? query = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HomeCoreOperationResult<HomeModelCataloguePage>(true, "Ready", "Actual provider catalogue message.", new(entries, 0, entries.Count, false, 1)));
        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> GetSnapshotAsync(string scope, string category, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HomeCoreOperationResult<HomeModelPickerSnapshot>(true, "Ready", "Route", new(1, "User", "chat",
                [new("fixture-route", 1, "User", "chat", null, null, [], JsonSerializer.SerializeToElement(new { }))])));
        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> UpdateRouteAsync(HomeModelRouteEdit edit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HomeCoreOperationResult<HomeModelRoutePreview>> PreviewResolutionAsync(HomeModelRoutePreviewRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
