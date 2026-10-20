using System.Text.Json;
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
        public Task<HomeCoreOperationResult<object>> RetryAuditAsync(string requestId, CancellationToken ct = default) =>
            Task.FromResult(new HomeCoreOperationResult<object>(false, "AuditNotOwned", "This host does not own the original route audit."));
        public Task<HomeCoreOperationResult<HomeModelCataloguePage>> GetCatalogueAsync(string? query = null, CancellationToken ct = default) =>
            Task.FromResult(new HomeCoreOperationResult<HomeModelCataloguePage>(true, "Ready", "Current authorised provider catalogue.", new([], 0, 20, false, 0)));
        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> GetSnapshotAsync(string scope, string category, CancellationToken ct = default) =>
            Task.FromResult(new HomeCoreOperationResult<HomeModelPickerSnapshot>(true, "Ready", "Current personal Home route.", snapshot));
        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> UpdateRouteAsync(HomeModelRouteEdit edit, CancellationToken ct = default) =>
            Task.FromResult(new HomeCoreOperationResult<HomeModelPickerSnapshot>(false, "PermissionDenied", "n/a"));
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

    private static HomeModelRouteContract Route(string id, params HomeModelRouteCandidate[] candidates) =>
        new(id, 4, "User", "chat", null, null, candidates, JsonSerializer.SerializeToElement(new { }));
}
