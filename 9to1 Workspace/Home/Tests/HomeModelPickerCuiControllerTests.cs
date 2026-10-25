using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using HavenOS.Home;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Actual controller/session tests for the Home model-picker CUI projection. These
/// exercise the publication fence and structured refusals on the real controller seams; the
/// typed dispatcher/row argument validation is covered by the browse-side shared-control tests
/// and the host composition tests.</summary>
public sealed class HomeModelPickerCuiControllerTests
{
    private sealed class StubProvider(HomeModelPickerSnapshot snapshot) : IHomeModelPickerFeatureProvider
    {
        public HomeModelPickerSnapshot Snapshot { get; private set; } = snapshot;
        public HomeModelCataloguePage Catalogue { get; init; } = new([], 0, 20, false, 1);
        public HomeModelRouteEdit? LastEdit { get; private set; }
        public Task<HomeCoreOperationResult<object>> RetryAuditAsync(string requestId, CancellationToken ct = default) =>
            Task.FromResult(new HomeCoreOperationResult<object>(false, "AuditNotOwned", "This host does not own the original route audit."));
        public Task<HomeCoreOperationResult<HomeModelCataloguePage>> GetCatalogueAsync(string? query = null, CancellationToken ct = default) =>
            Task.FromResult(new HomeCoreOperationResult<HomeModelCataloguePage>(true, "Ready", "Current authorised provider catalogue.", Catalogue));
        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> GetSnapshotAsync(string scope, string category, CancellationToken ct = default) =>
            Task.FromResult(new HomeCoreOperationResult<HomeModelPickerSnapshot>(true, "Ready", "Current personal Home route.", Snapshot));
        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> UpdateRouteAsync(HomeModelRouteEdit edit, CancellationToken ct = default)
        {
            LastEdit = edit;
            Snapshot = Snapshot with
            {
                Revision = edit.Route.Version,
                Routes = Snapshot.Routes.Select(route => route.RouteId == edit.Route.RouteId ? edit.Route : route).ToArray()
            };
            return Task.FromResult(new HomeCoreOperationResult<HomeModelPickerSnapshot>(true, "Saved", "Saved.", Snapshot));
        }
        public Task<HomeCoreOperationResult<HomeModelRoutePreview>> PreviewResolutionAsync(HomeModelRoutePreviewRequest request, CancellationToken ct = default) =>
            Task.FromResult(new HomeCoreOperationResult<HomeModelRoutePreview>(false, "PermissionDenied", "n/a"));
    }

    [Fact]
    public async Task Publication_gate_suppresses_publication_when_the_panel_is_retired()
    {
        var route = new HomeModelRouteContract("chat.active", 4, "User", "chat", null, null,
            [new("provider", "model", "rev", true, 0)], JsonSerializer.SerializeToElement(new { }));
        var snapshot = new HomeModelPickerSnapshot(7, "User", "chat", [route]);
        var editor = new HomeModelPickerRouteEditor(new StubProvider(snapshot));
        var surface = new HomeModelPickerCuiSurface(new CuiRichParser().Parse("<Cui><Page/></Cui>"));
        var controller = new HomeModelPickerCuiController(editor, surface) { PublicationGate = () => false };
        var published = 0;
        surface.PropertyChanged += (_, _) => published++;

        var result = await controller.OpenAsync("User", "chat");

        Assert.True(result.Succeeded);
        Assert.Equal(0, published);        // retired panel never publishes
        Assert.False(surface.HasSnapshot); // current valid scene preserved
        Assert.Equal("global", surface.Scope);
    }

    [Fact]
    public async Task Publication_gate_still_publishes_when_the_panel_remains_current()
    {
        var route = new HomeModelRouteContract("chat.active", 4, "User", "chat", null, null,
            [new("provider", "model", "rev", true, 0)], JsonSerializer.SerializeToElement(new { }));
        var snapshot = new HomeModelPickerSnapshot(7, "User", "chat", [route]);
        var editor = new HomeModelPickerRouteEditor(new StubProvider(snapshot));
        var surface = new HomeModelPickerCuiSurface(new CuiRichParser().Parse("<Cui><Page/></Cui>"));
        var controller = new HomeModelPickerCuiController(editor, surface) { PublicationGate = () => true };
        var published = 0;
        surface.PropertyChanged += (_, _) => published++;

        var result = await controller.OpenAsync("User", "chat");

        Assert.True(result.Succeeded);
        Assert.Equal(1, published);
        Assert.True(surface.HasSnapshot);
        Assert.Equal("chat.active", surface.State.SelectedRouteId);
    }

    [Fact]
    public async Task Malformed_route_selection_refuses_structurally_without_mutating_the_surface()
    {
        var route = new HomeModelRouteContract("chat.active", 4, "User", "chat", null, null,
            [new("provider", "model", "rev", true, 0)], JsonSerializer.SerializeToElement(new { }));
        var snapshot = new HomeModelPickerSnapshot(7, "User", "chat", [route]);
        var editor = new HomeModelPickerRouteEditor(new StubProvider(snapshot));
        var surface = new HomeModelPickerCuiSurface(new CuiRichParser().Parse("<Cui><Page/></Cui>"));
        var controller = new HomeModelPickerCuiController(editor, surface);

        // A route selection without a route ID must refuse structurally, never silently succeed.
        var result = await controller.ExecuteAsync(new HomeModelPickerActionRequest(HomeModelPickerAction.SelectRoute));

        Assert.False(result.Succeeded);
        Assert.Equal("InvalidArgument", result.Code);
        Assert.False(surface.HasSnapshot);
    }

    [Fact]
    public async Task Candidate_toggle_requires_enabled_state_and_refuses_otherwise()
    {
        var editor = new HomeModelPickerRouteEditor(new StubProvider(new HomeModelPickerSnapshot(0, "User", "chat", [])));
        var surface = new HomeModelPickerCuiSurface(new CuiRichParser().Parse("<Cui><Page/></Cui>"));
        var controller = new HomeModelPickerCuiController(editor, surface);

        var result = await controller.ExecuteAsync(new HomeModelPickerActionRequest(
            HomeModelPickerAction.SetCandidateEnabled, ProviderId: "p", ModelId: "m", Enabled: null));

        Assert.False(result.Succeeded);
        Assert.Equal("InvalidArgument", result.Code);
    }

    [Fact]
    public async Task Catalogue_refresh_exposes_authorised_model_and_adds_only_that_entry_to_route_draft()
    {
        var route = new HomeModelRouteContract("chat.active", 7, "User", "chat", null, null,
            [new("provider", "existing", null, true, 0)], JsonSerializer.SerializeToElement(new { }));
        var catalogueEntry = new HomeModelPickerCatalogueEntry("provider-b", "model-b", null,
            "Friendly model B", "Provider B", true, new HashSet<string>(["Chat", "Tools"], StringComparer.Ordinal),
            32768, "Ready", "Local", null);
        var provider = new StubProvider(new HomeModelPickerSnapshot(7, "User", "chat", [route]))
        {
            Catalogue = new([catalogueEntry], 0, 1, false, 8)
        };
        var surface = new HomeModelPickerCuiSurface(new CuiRichParser().Parse("<Cui><Page/></Cui>"));
        var controller = new HomeModelPickerCuiController(new HomeModelPickerRouteEditor(provider), surface);
        await controller.OpenAsync("User", "chat");

        var loaded = await controller.ExecuteAsync(new(HomeModelPickerAction.RefreshCatalogue));

        Assert.True(loaded.Succeeded);
        Assert.True(Value<bool>(surface, "HasCatalogue"));
        var row = Assert.Single(surface.Projection.Catalogue);
        Assert.Equal("Friendly model B", Value<string>(row, "DisplayName"));
        Assert.Equal("Local", Value<string>(row, "Locality"));
        Assert.Equal("Chat · Tools", Value<string>(row, "Capabilities"));

        var unknown = await controller.ExecuteAsync(new(HomeModelPickerAction.AddCatalogueCandidate,
            ProviderId: "not-listed", ModelId: "model-b"));

        Assert.False(unknown.Succeeded);
        Assert.Equal("CatalogueEntryNotLoaded", unknown.Code);
        Assert.Equal(new[] { "existing" }, surface.State.Candidates.Select(candidate => candidate.ModelId));

        var added = await controller.ExecuteAsync(new(HomeModelPickerAction.AddCatalogueCandidate,
            ProviderId: "provider-b", ModelId: "model-b"));

        Assert.True(added.Succeeded);
        Assert.Equal(new[] { ("existing", 0), ("model-b", 1) },
            surface.State.Candidates.Select(candidate => (candidate.ModelId, candidate.Order)));
        Assert.True(surface.State.HasUnsavedChanges);
        Assert.Equal(7, surface.State.Snapshot!.Revision); // adding is draft-only; canonical save remains explicit

        var saved = await controller.ExecuteAsync(new(HomeModelPickerAction.SaveRoute));

        Assert.True(saved.Succeeded);
        Assert.NotNull(provider.LastEdit);
        Assert.Equal(7, provider.LastEdit!.ExpectedRevision);
        Assert.Equal(new[] { ("existing", 0), ("model-b", 1) },
            provider.LastEdit.Route.Candidates.Select(candidate => (candidate.ModelId, candidate.Order)));
        Assert.False(surface.State.HasUnsavedChanges);
    }

    [Fact]
    public async Task SetReasoningLevel_action_dispatches_to_editor_and_marks_unsaved_draft()
    {
        var route = new HomeModelRouteContract("chat.active", 4, "User", "chat", null, null,
            [new("provider", "model", "rev", true, 0)], JsonSerializer.SerializeToElement(new { }));
        var snapshot = new HomeModelPickerSnapshot(7, "User", "chat", [route]);
        var controller = new HomeModelPickerCuiController(new HomeModelPickerRouteEditor(new StubProvider(snapshot)),
            new HomeModelPickerCuiSurface(new CuiRichParser().Parse("<Cui><Page/></Cui>")));
        await controller.OpenAsync("User", "chat");

        var result = await controller.ExecuteAsync(new HomeModelPickerActionRequest(
            HomeModelPickerAction.SetReasoningLevel, ProviderId: "provider", ModelId: "model",
            ArtifactRevision: "rev", ReasoningLevelId: "medium"));

        Assert.True(result.Succeeded);
        var candidate = Assert.Single(controller.Surface.State.Candidates);
        Assert.Equal("medium", candidate.ReasoningLevelId);
        Assert.True(controller.Surface.State.HasUnsavedChanges);
        Assert.Equal("UnsavedChanges", controller.Surface.State.StatusCode);
    }

    [Fact]
    public async Task ClearReasoningLevel_action_clears_the_draft_candidate_default()
    {
        var route = new HomeModelRouteContract("chat.active", 4, "User", "chat", null, null,
            [new("provider", "model", "rev", true, 0, "medium")], JsonSerializer.SerializeToElement(new { }));
        var snapshot = new HomeModelPickerSnapshot(7, "User", "chat", [route]);
        var controller = new HomeModelPickerCuiController(new HomeModelPickerRouteEditor(new StubProvider(snapshot)),
            new HomeModelPickerCuiSurface(new CuiRichParser().Parse("<Cui><Page/></Cui>")));
        await controller.OpenAsync("User", "chat");

        var result = await controller.ExecuteAsync(new HomeModelPickerActionRequest(
            HomeModelPickerAction.SetReasoningLevel, ProviderId: "provider", ModelId: "model",
            ArtifactRevision: "rev", ReasoningLevelId: null));

        Assert.True(result.Succeeded);
        var candidate = Assert.Single(controller.Surface.State.Candidates);
        Assert.Null(candidate.ReasoningLevelId);
        Assert.True(controller.Surface.State.HasUnsavedChanges);
    }

    [Fact]
    public async Task SetReasoningLevel_rejects_blank_supplied_level_structurally()
    {
        var editor = new HomeModelPickerRouteEditor(new StubProvider(new HomeModelPickerSnapshot(0, "User", "chat", [])));
        var controller = new HomeModelPickerCuiController(editor,
            new HomeModelPickerCuiSurface(new CuiRichParser().Parse("<Cui><Page/></Cui>")));

        var result = await controller.ExecuteAsync(new HomeModelPickerActionRequest(
            HomeModelPickerAction.SetReasoningLevel, ProviderId: "p", ModelId: "m", ReasoningLevelId: " "));

        Assert.False(result.Succeeded);
        Assert.Equal("InvalidReasoningLevel", result.Code);
    }

    private static HomeModelRouteContract Route(string id, params HomeModelRouteCandidate[] candidates) =>
        new(id, 4, "User", "chat", null, null, candidates, JsonSerializer.SerializeToElement(new { }));

    private static T Value<T>(ICuiBindingContext context, string path)
    {
        Assert.True(context.TryGetValue(path, out var value));
        return Assert.IsType<T>(value);
    }
}
