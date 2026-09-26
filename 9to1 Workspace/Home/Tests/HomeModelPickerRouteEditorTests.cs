using System.Text.Json;
using HavenOS.Home;
using HavenOS.Home.Core;
using CakeOS.Cui;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeModelPickerRouteEditorTests
{
    [Fact]
    public void CUI_document_exposes_all_model_categories_and_accessible_route_actions()
    {
        var path = Path.Combine(Environment.CurrentDirectory, "9to1 Workspace", "Home", "UI", "ModelPicker.cui");
        var document = new CuiRichParser().ParseFile(path);

        Assert.Contains(document.RootProperties, property => property.Key == "id"
            && property.Value is CuiLiteralValue { Value: "home-model-picker" });
        var names = Flatten(document.Components).Select(component => component.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("compact-model-picker", names);
        Assert.Contains("category-chat", names);
        Assert.Contains("category-image", names);
        Assert.Contains("category-voice", names);
        Assert.Contains("category-audio", names);
        Assert.Contains("category-video", names);
        Assert.Contains("save-model-route", names);
        Assert.Contains("model-route-preview-trace", names);
    }

    [Fact]
    public void CUI_surface_queues_only_well_formed_typed_candidate_actions()
    {
        var surface = new HomeModelPickerCuiSurface(new CuiRichParser().Parse("<Cui><Stack id=\"root\" /></Cui>"));

        Assert.False(surface.Request(new HomeModelPickerActionRequest(HomeModelPickerAction.SetCandidateEnabled,
            ProviderId: "provider", ModelId: "model", ArtifactRevision: "rev")));
        Assert.False(surface.Request(new HomeModelPickerActionRequest(HomeModelPickerAction.MoveCandidateUp,
            ProviderId: "provider", ModelId: "model")));
        Assert.True(surface.Request(new HomeModelPickerActionRequest(HomeModelPickerAction.SetCandidateEnabled,
            ProviderId: "provider", ModelId: "model", ArtifactRevision: "rev", Enabled: false)));
        Assert.True(surface.TryDequeueAction(out var queued));
        Assert.Equal(HomeModelPickerAction.SetCandidateEnabled, queued.Action);
        Assert.False(surface.TryDequeueAction(out _));
    }

    [Fact]
    public async Task CUI_controller_dispatches_category_selection_and_updates_surface_state()
    {
        var provider = new FakeProvider(Snapshot());
        var controller = new HomeModelPickerCuiController(new HomeModelPickerRouteEditor(provider),
            new HomeModelPickerCuiSurface(new CuiRichParser().Parse("<Cui><Stack id=\"root\" /></Cui>")));

        var result = await controller.ExecuteAsync(new HomeModelPickerActionRequest(
            HomeModelPickerAction.SelectCategory, Scope: "global", Category: "chat"));

        Assert.True(result.Succeeded);
        Assert.Equal("chat", controller.Surface.Category);
        Assert.Equal("chat.active", controller.Surface.State.SelectedRouteId);
    }

    [Fact]
    public async Task Refresh_selects_first_route_and_exposes_empty_state_without_inventing_a_default()
    {
        var provider = new FakeProvider(Snapshot());
        var editor = new HomeModelPickerRouteEditor(provider);

        var loaded = await editor.RefreshAsync("global", "chat");

        Assert.True(loaded.Succeeded);
        Assert.Equal("chat.active", editor.Current.SelectedRouteId);
        Assert.False(editor.Current.HasUnsavedChanges);

        provider.Snapshot = Snapshot(routes: []);
        var empty = await editor.RefreshAsync("global", "chat");

        Assert.True(empty.Succeeded);
        Assert.Null(editor.Current.SelectedRouteId);
        Assert.Equal("NoRoutes", editor.Current.StatusCode);
    }

    [Fact]
    public void Catalogue_projection_keeps_stable_identity_and_does_not_guess_provider_availability()
    {
        var projected = HomeModelCatalogueEntry.From(new HomeModelPickerCatalogueEntry(
            "provider", "model", "artifact-7", "Display", "Provider", null,
            new HashSet<string>(["text", "reasoning"], StringComparer.OrdinalIgnoreCase), 8192,
            "installed", "local", "alias"));

        Assert.Equal("provider:model:artifact-7", projected.Identity!.StableKey);
        Assert.Equal(HomeModelProviderAvailability.Unknown, projected.ProviderFacts.Availability);
        Assert.Null(projected.ProviderFacts.IsLocal);
        Assert.Null(projected.ProviderFacts.LifecycleState);
        Assert.Equal("Provider", projected.ProviderName);
        Assert.Equal("local", projected.PrivacyResidency);
        Assert.Equal("installed", projected.LifecycleLabel);
        Assert.True(projected.HasDeclaredCapability("TEXT"));
        Assert.True(projected.MatchesQuery("alias"));
    }

    [Fact]
    public async Task Reorder_and_enable_edits_are_saved_as_one_revision_checked_operation()
    {
        var provider = new FakeProvider(Snapshot());
        var editor = new HomeModelPickerRouteEditor(provider);
        await editor.RefreshAsync("global", "chat");

        var moved = editor.MoveCandidate("provider", "model-b", "rev-b", 0);
        Assert.True(moved.Succeeded);
        var disabled = editor.SetCandidateEnabled("provider", "model-a", "rev-a", false);
        Assert.True(disabled.Succeeded);
        Assert.True(editor.Current.HasUnsavedChanges);

        var saved = await editor.SaveAsync();

        Assert.True(saved.Succeeded);
        Assert.False(editor.Current.HasUnsavedChanges);
        Assert.Equal(7, provider.LastEdit!.ExpectedRevision);
        Assert.Equal(5, provider.LastEdit.Route.Version);
        Assert.Equal(new[] { "model-b", "model-a" }, provider.LastEdit.Route.Candidates.OrderBy(x => x.Order).Select(x => x.ModelId));
        Assert.False(provider.LastEdit.Route.Candidates.Single(x => x.ModelId == "model-a").Enabled);
    }

    [Fact]
    public async Task Failed_revision_save_preserves_the_draft_for_conflict_recovery()
    {
        var provider = new FakeProvider(Snapshot()) { UpdateResult = Failure<HomeModelPickerSnapshot>("RevisionConflict", "Routes changed elsewhere.") };
        var editor = new HomeModelPickerRouteEditor(provider);
        await editor.RefreshAsync("global", "chat");
        editor.MoveCandidate("provider", "model-b", "rev-b", 0);

        var result = await editor.SaveAsync();

        Assert.False(result.Succeeded);
        Assert.True(editor.Current.HasUnsavedChanges);
        Assert.Equal("RevisionConflict", editor.Current.StatusCode);
        Assert.Equal("model-b", editor.Current.Candidates.OrderBy(x => x.Order).First().ModelId);
    }

    [Fact]
    public async Task Unsaved_edits_block_route_switch_refresh_and_preview_until_explicit_resolution()
    {
        var provider = new FakeProvider(Snapshot());
        var editor = new HomeModelPickerRouteEditor(provider);
        await editor.RefreshAsync("global", "chat");
        editor.MoveCandidate("provider", "model-b", "rev-b", 0);

        Assert.Equal("UnsavedChanges", editor.SelectRoute("background").Code);
        Assert.Equal("UnsavedChanges", (await editor.RefreshAsync("global", "chat")).Code);
        Assert.Equal("UnsavedChanges", (await editor.PreviewAsync("text")).Code);

        Assert.True((await editor.RefreshAsync("global", "chat", discardUnsavedChanges: true)).Succeeded);
        Assert.False(editor.Current.HasUnsavedChanges);
    }

    [Fact]
    public async Task Resolution_preview_is_delegated_and_trace_is_retained_verbatim()
    {
        var provider = new FakeProvider(Snapshot())
        {
            PreviewResult = Success(new HomeModelRoutePreview(
                "chat.active", 4, "provider:model-b:rev-b", "Fallback selected",
                ["model-a: unsupported required capability", "model-b: selected"])),
        };
        var editor = new HomeModelPickerRouteEditor(provider);
        await editor.RefreshAsync("global", "chat");

        var preview = await editor.PreviewAsync("image-generation", appId: "app.image");

        Assert.True(preview.Succeeded);
        Assert.Equal("image-generation", provider.LastPreviewRequest!.Capability);
        Assert.Equal("app.image", provider.LastPreviewRequest.AppId);
        Assert.Equal("{}", provider.LastPreviewRequest.Context.GetRawText());
        Assert.Equal(new[] { "model-a: unsupported required capability", "model-b: selected" }, editor.Current.Preview!.Trace);
    }

    [Fact]
    public async Task Invalid_provider_snapshots_fail_closed_and_do_not_become_editable()
    {
        var provider = new FakeProvider(Snapshot(routes: [Route("chat.active", candidates: [Candidate("provider", "same", "rev", true, 0), Candidate("provider", "same", "rev", false, 1)])]));
        var editor = new HomeModelPickerRouteEditor(provider);

        var result = await editor.RefreshAsync("global", "chat");

        Assert.False(result.Succeeded);
        Assert.Equal("HomeServiceUnavailable", editor.Current.StatusCode);
        Assert.Null(editor.Current.SelectedRouteId);
    }

    [Fact]
    public async Task Missing_route_candidate_and_invalid_position_return_structured_errors()
    {
        var editor = new HomeModelPickerRouteEditor(new FakeProvider(Snapshot()));
        await editor.RefreshAsync("global", "chat");

        Assert.Equal("CandidateNotFound", editor.MoveCandidate("provider", "missing", "rev", 0).Code);
        Assert.Equal("InvalidOrder", editor.MoveCandidate("provider", "model-a", "rev-a", 3).Code);
        Assert.Equal("InvalidModelIdentity", editor.SetCandidateEnabled("", "model-a", "rev-a", false).Code);
    }

    private static HomeModelPickerSnapshot Snapshot(IReadOnlyList<HomeModelRouteContract>? routes = null) => new(
        7, "global", "chat", routes ?? [Route("chat.active"), Route("background", "background")]);

    private static HomeModelRouteContract Route(string id, string category = "chat",
        IReadOnlyList<HomeModelRouteCandidate>? candidates = null) => new(
        id, 4, "global", category, null, null,
        candidates ?? [Candidate("provider", "model-a", "rev-a", true, 0), Candidate("provider", "model-b", "rev-b", true, 1)],
        EmptyJson());

    private static HomeModelRouteCandidate Candidate(string provider, string model, string revision, bool enabled, int order) =>
        new(provider, model, revision, enabled, order);

    private static JsonElement EmptyJson()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static IEnumerable<CuiComponent> Flatten(IEnumerable<CuiComponent> components)
    {
        foreach (var component in components)
        {
            yield return component;
            foreach (var child in Flatten(component.Children)) yield return child;
            foreach (var child in Flatten(component.ElseChildren)) yield return child;
        }
    }

    private static HomeCoreOperationResult<T> Success<T>(T value, long revision = 7) =>
        new(true, "Succeeded", "Done.", value, true, revision);

    private static HomeCoreOperationResult<T> Failure<T>(string code, string message) =>
        new(false, code, message);

    private sealed class FakeProvider(HomeModelPickerSnapshot snapshot) : IHomeModelPickerFeatureProvider
    {
        public HomeModelPickerSnapshot Snapshot { get; set; } = snapshot;
        public HomeCoreOperationResult<HomeModelPickerSnapshot>? UpdateResult { get; init; }
        public HomeCoreOperationResult<HomeModelRoutePreview>? PreviewResult { get; init; }
        public HomeModelRouteEdit? LastEdit { get; private set; }
        public HomeModelRoutePreviewRequest? LastPreviewRequest { get; private set; }

        public Task<HomeCoreOperationResult<HomeModelCataloguePage>> GetCatalogueAsync(string? query = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Success(new HomeModelCataloguePage([], 0, 50, false, 1)));
        }

        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> GetSnapshotAsync(string scope, string category,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Success(Snapshot));
        }

        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> UpdateRouteAsync(HomeModelRouteEdit edit,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastEdit = edit;
            if (UpdateResult is not null) return Task.FromResult(UpdateResult);
            var updatedRoute = Snapshot.Routes.First(route => route.RouteId == edit.Route.RouteId);
            Snapshot = Snapshot with
            {
                Revision = Snapshot.Revision + 1,
                Routes = Snapshot.Routes.Select(route => route.RouteId == updatedRoute.RouteId ? edit.Route : route).ToArray(),
            };
            return Task.FromResult(Success(Snapshot, Snapshot.Revision));
        }

        public Task<HomeCoreOperationResult<HomeModelRoutePreview>> PreviewResolutionAsync(HomeModelRoutePreviewRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastPreviewRequest = request;
            return Task.FromResult(PreviewResult ?? Success(new HomeModelRoutePreview(
                request.RouteId, 4, null, "No eligible candidate.", [], "ProviderUnavailable")));
        }
    }
}
