using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
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
    private sealed class StubProvider(HomeModelPickerSnapshot snapshot) : IHomeModelPickerOriginalActorFeatureProvider
    {
        public HomeModelPickerSnapshot Snapshot { get; private set; } = snapshot;
        public HomeModelCataloguePage Catalogue { get; init; } = new([], 0, 20, false, 1);
        public HomeModelRouteEdit? LastEdit { get; private set; }
        public HomeModelPickerSnapshot? VoiceSnapshot { get; init; }
        public List<string> RequestedCategories { get; } = [];
        public string? CatalogueFailure { get; set; }
        public bool FailRouteRefresh { get; set; }
        public Task<HomeCoreOperationResult<object>> RetryAuditAsync(string requestId, CancellationToken ct = default) =>
            Task.FromResult(new HomeCoreOperationResult<object>(false, "AuditNotOwned", "This host does not own the original route audit."));
        public Task<HomeCoreOperationResult<HomeModelCataloguePage>> GetCatalogueAsync(string? query = null, CancellationToken ct = default) =>
            Task.FromResult(CatalogueFailure is { } message
                ? new HomeCoreOperationResult<HomeModelCataloguePage>(false, "Offline", message)
                : new HomeCoreOperationResult<HomeModelCataloguePage>(true, "Ready", "Current authorised provider catalogue.", Catalogue));

        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> GetSnapshotAsync(string scope, string category, CancellationToken ct = default)
        {
            RequestedCategories.Add(category);
            if (FailRouteRefresh)
                return Task.FromResult(new HomeCoreOperationResult<HomeModelPickerSnapshot>(false, "Offline", "Route store offline."));
            var current = category == "voice" && VoiceSnapshot is not null ? VoiceSnapshot : Snapshot;
            return Task.FromResult(new HomeCoreOperationResult<HomeModelPickerSnapshot>(true, "Ready", "Current personal Home route.", current));
        }
        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> UpdateRouteAsync(HomeModelRouteEdit edit, CancellationToken ct = default)
        {
            if (edit.ExpectedRevision != Snapshot.Revision)
                return Task.FromResult(new HomeCoreOperationResult<HomeModelPickerSnapshot>(false, "Conflict", "Route revision changed."));
            LastEdit = edit;
            Snapshot = Snapshot with
            {
                Revision = edit.Route.Version,
                Routes = Snapshot.Routes.Select(route => route.RouteId == edit.Route.RouteId ? edit.Route : route).ToArray()
            };
            return Task.FromResult(new HomeCoreOperationResult<HomeModelPickerSnapshot>(true, "Saved", "Saved.", Snapshot));
        }
        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> UpdateRouteForActorAsync(
            AuthenticatedResourceActor expectedActor, HomeModelRouteEdit edit, CancellationToken cancellationToken = default) =>
            UpdateRouteAsync(edit, cancellationToken);

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
        Assert.Equal(2, published); // One publication for the routes and one for the authorised catalogue.
        Assert.True(surface.HasSnapshot);
        Assert.Equal("chat.active", surface.State.SelectedRouteId);
    }

    [Fact]
    public async Task Returning_to_picker_keeps_the_route_draft_until_explicit_discard()
    {
        var route = new HomeModelRouteContract("chat.active", 7, "User", "chat", null, null,
            [new("provider", "model", null, true, 0)], JsonSerializer.SerializeToElement(new { }));
        var provider = new StubProvider(new HomeModelPickerSnapshot(7, "User", "chat", [route]));
        var surface = new HomeModelPickerCuiSurface(new CuiRichParser().Parse("<Cui><Page/></Cui>"));
        var controller = new HomeModelPickerCuiController(new HomeModelPickerRouteEditor(provider), surface);
        Assert.True((await controller.OpenAsync("User", "chat")).Succeeded);

        surface.SetSettingsOpen(true);
        Assert.True((await controller.ExecuteAsync(new(HomeModelPickerAction.SetCandidateEnabled,
            ProviderId: "provider", ModelId: "model", Enabled: false))).Succeeded);
        var draft = surface.State;
        surface.SetSettingsOpen(false);
        surface.SetSettingsOpen(true);
        Assert.Same(draft, surface.State);
        Assert.True(Value<bool>(surface, "CanDiscardRoute"));
        Assert.False(Assert.Single(surface.State.Candidates).Enabled);

        Assert.True((await controller.ExecuteAsync(new(HomeModelPickerAction.DiscardRoute))).Succeeded);
        Assert.True(Assert.Single(surface.State.Candidates).Enabled);
        Assert.False(Value<bool>(surface, "CanDiscardRoute"));
        Assert.Equal(7, provider.Snapshot.Revision);
        Assert.Null(provider.LastEdit);
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

    [Fact]
    public async Task Compact_model_choice_prefers_the_current_catalogue_identity_and_saves_only_on_request()
    {
        var route = Route("chat.active", new("p", "current", null, true, 0),
            new("p", "desired", null, false, 1, "low"), new("p", "fallback", null, true, 2));
        var provider = new StubProvider(new HomeModelPickerSnapshot(4, "User", "chat", [route]))
        {
            Catalogue = new([Model("desired", "Text"), Model("vision-only", "Vision")], 0, 2, false, 1),
        };
        var controller = new HomeModelPickerCuiController(new HomeModelPickerRouteEditor(provider),
            new HomeModelPickerCuiSurface(new CuiRichParser().Parse("<Cui><Page/></Cui>")));
        await controller.OpenAsync("User", "chat");

        var entry = Assert.Single(Value<HomeModelPickerCatalogueRow[]>(controller.Surface, "CompactCatalogue"));
        var choice = Value<HomeModelPickerCatalogueSelection>(entry, "Selection");
        Assert.Equal("chat", choice.CompactRole);
        var ineligible = await controller.ExecuteAsync(new(HomeModelPickerAction.ChooseCompactModel,
            Category: "chat", ProviderId: "p", ModelId: "vision-only"));
        Assert.Equal("CatalogueEntryUnavailable", ineligible.Code);
        var stale = await controller.ExecuteAsync(new(HomeModelPickerAction.ChooseCompactModel,
            Category: "voice", ProviderId: choice.ProviderId, ModelId: choice.ModelId));
        Assert.Equal("RouteUnavailable", stale.Code);

        var staged = await controller.ExecuteAsync(new(HomeModelPickerAction.ChooseCompactModel, Category: choice.CompactRole,
            ProviderId: choice.ProviderId, ModelId: choice.ModelId, ArtifactRevision: choice.ArtifactRevision));

        Assert.True(staged.Succeeded);
        Assert.Null(provider.LastEdit);
        Assert.True(Value<bool>(controller.Surface, "CanSaveCompactRoute"));
        Assert.Equal("Friendly desired", Value<string>(controller.Surface, "CompactModelName"));
        Assert.Equal("Route preference pending", Value<string>(controller.Surface, "CompactModelState"));
        Assert.Equal(new[] { ("desired", true, 0), ("current", true, 1), ("fallback", true, 2) },
            controller.Surface.State.Candidates.Select(candidate => (candidate.ModelId, candidate.Enabled, candidate.Order)));
        Assert.Equal("low", controller.Surface.State.Candidates[0].ReasoningLevelId);
        var blocked = await controller.ExecuteAsync(new(HomeModelPickerAction.SelectCompactRole, Category: "voice"));
        Assert.Equal("UnsavedChanges", blocked.Code);
        Assert.Equal(new[] { "chat" }, provider.RequestedCategories);

        var saved = await controller.ExecuteAsync(new(HomeModelPickerAction.SaveRoute));

        Assert.True(saved.Succeeded);
        Assert.Equal(4, provider.LastEdit!.ExpectedRevision);
        Assert.Equal("desired", provider.LastEdit.Route.Candidates[0].ModelId);
        Assert.False(Value<bool>(controller.Surface, "CanSaveCompactRoute"));
    }

    [Fact]
    public async Task Compact_roles_keep_separate_disclosures_and_never_route_vision_to_image_generation()
    {
        var chat = Route("chat.active", new HomeModelRouteCandidate("p", "main", null, true, 0));
        var voice = Route("voice.active", new HomeModelRouteCandidate("p", "voice-pref", null, true, 0)) with { Category = "voice" };
        var provider = new StubProvider(new HomeModelPickerSnapshot(4, "User", "chat", [chat]))
        {
            VoiceSnapshot = new(4, "User", "voice", [voice]),
            Catalogue = new([Model("main", "Text"), Model("camera", "Vision"), Model("audio", "AudioInput", "AudioOutput")],
                0, 3, false, 1),
        };
        var surface = new HomeModelPickerCuiSurface(new CuiRichParser().Parse("<Cui><Page/></Cui>"));
        var controller = new HomeModelPickerCuiController(new HomeModelPickerRouteEditor(provider), surface);
        await controller.OpenAsync("User", "chat");
        await controller.ExecuteAsync(new(HomeModelPickerAction.ToggleCompactSection, FamilyKey: "context"));

        Assert.True((await controller.ExecuteAsync(new(HomeModelPickerAction.SelectCompactRole, Category: "voice"))).Succeeded);
        Assert.False(Value<bool>(surface, "CompactContextOpen"));
        Assert.Equal("voice-pref", Value<string>(surface, "CompactModelName"));
        Assert.Empty(Value<HomeModelPickerCatalogueRow[]>(surface, "CompactCatalogue"));
        await controller.ExecuteAsync(new(HomeModelPickerAction.ToggleCompactSection, FamilyKey: "model"));

        Assert.True((await controller.ExecuteAsync(new(HomeModelPickerAction.SelectCompactRole, Category: "vision"))).Succeeded);
        Assert.Equal("Choose a model", Value<string>(surface, "CompactModelName"));
        Assert.False(Value<bool>(surface, "CompactModelOpen"));
        Assert.False(Value<bool>(surface, "CanChooseCompactModel"));
        Assert.Equal("Friendly camera", Value<string>(Assert.Single(
            Value<HomeModelPickerCatalogueRow[]>(surface, "CompactCatalogue")), "DisplayName"));
        Assert.Equal(new[] { "chat", "voice" }, provider.RequestedCategories);

        surface.SetSettingsOpen(true);
        surface.SetSettingsOpen(false);
        Assert.Equal("vision", surface.CompactRole);
        Assert.True((await controller.ExecuteAsync(new(HomeModelPickerAction.SelectCompactRole, Category: "chat"))).Succeeded);
        Assert.True(Value<bool>(surface, "CompactContextOpen"));
        Assert.False(Value<bool>(surface, "CompactModelOpen"));
        Assert.Equal("Friendly main", Value<string>(surface, "CompactModelName"));
        Assert.True((await controller.ExecuteAsync(new(HomeModelPickerAction.SelectCompactRole, Category: "voice"))).Succeeded);
        Assert.True(Value<bool>(surface, "CompactModelOpen"));
        Assert.Null(provider.LastEdit);
    }

    [Fact]
    public async Task Compact_discovery_survives_a_catalogue_failure_and_can_retry()
    {
        var provider = new StubProvider(new HomeModelPickerSnapshot(4, "User", "chat", [Route("chat.active")]))
        {
            Catalogue = new([Model("main", "Text")], 0, 1, false, 1),
            CatalogueFailure = "Provider catalogue offline. Try again.",
        };
        var surface = new HomeModelPickerCuiSurface(new CuiRichParser().Parse("<Cui><Page/></Cui>"));
        var controller = new HomeModelPickerCuiController(new HomeModelPickerRouteEditor(provider), surface);

        Assert.True((await controller.OpenAsync("User", "chat")).Succeeded);
        Assert.True(Value<bool>(surface, "HasCompactCatalogueMessage"));
        Assert.Equal(provider.CatalogueFailure, Value<string>(surface, "CompactCatalogueMessage"));
        Assert.Empty(Value<HomeModelPickerCatalogueRow[]>(surface, "CompactCatalogue"));

        provider.CatalogueFailure = null;
        Assert.True((await controller.ExecuteAsync(new(HomeModelPickerAction.RefreshCatalogue))).Succeeded);
        Assert.False(Value<bool>(surface, "HasCompactCatalogueMessage"));
        Assert.Single(Value<HomeModelPickerCatalogueRow[]>(surface, "CompactCatalogue"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reasoning_save_rebases_a_later_model_preference_to_the_canonical_route_revision(bool refreshTemporarilyUnavailable)
    {
        var actor = new AuthenticatedResourceActor("actor", "profile", null, null, "auth-1");
        var routeId = HomeModelPickerFeatureProvider.RouteId(actor.ProfileId, ModelCapabilityCategory.Chat);
        var route = Route(routeId, new HomeModelRouteCandidate("p", "current", null, true, 0)) with { ScopeId = actor.ProfileId };
        var provider = new StubProvider(new HomeModelPickerSnapshot(4, "User", "Chat", [route]))
        {
            Catalogue = new([Model("current", "Text"), Model("next", "Text")], 0, 2, false, 1),
        };
        var source = new HomeModelSelectionSource(new ModelProviderRegistry([new ReasoningCatalogueProvider()]),
            new InMemoryModelRouteRepository(),
            (category, _) => Task.FromResult(category == ModelCapabilityCategory.Chat ? ReadCanonicalRoute() : null),
            _ => Task.FromResult<AuthenticatedResourceActor?>(actor),
            (_, _, _) => Task.FromResult<IReadOnlyList<string>?>(["low", "medium", "high"]),
            routeProvider: provider);
        var surface = new HomeModelPickerCuiSurface(new CuiRichParser().Parse("<Cui><Page/></Cui>"));
        var controller = new HomeModelPickerCuiController(new HomeModelPickerRouteEditor(provider), surface, source, actor);
        Assert.True((await controller.OpenAsync("User", "chat")).Succeeded);

        provider.FailRouteRefresh = refreshTemporarilyUnavailable;
        var reasoning = await controller.ExecuteAsync(new(HomeModelPickerAction.CommitReasoningLevel,
            ProviderId: "p", ModelId: "current", ReasoningLevelId: "medium"));

        Assert.True(reasoning.Succeeded);
        Assert.Equal(4, provider.LastEdit!.ExpectedRevision);
        Assert.Equal(5, provider.Snapshot.Revision);
        Assert.Equal("medium", provider.Snapshot.Routes.Single().Candidates.Single().ReasoningLevelId);

        var select = new HomeModelPickerActionRequest(HomeModelPickerAction.ChooseCompactModel,
            Category: "chat", ProviderId: "p", ModelId: "next");
        if (refreshTemporarilyUnavailable)
        {
            Assert.Equal("ReasoningSavedRouteRefreshRequired", reasoning.Code);
            Assert.True(Value<bool>(surface, "RouteRefreshRequired"));
            Assert.Equal("RouteRefreshRequired", (await controller.ExecuteAsync(select)).Code);
            provider.FailRouteRefresh = false;
            Assert.True((await controller.ExecuteAsync(new(HomeModelPickerAction.Refresh))).Succeeded);
        }
        Assert.Equal(5, surface.State.Snapshot!.Revision);
        Assert.False(Value<bool>(surface, "RouteRefreshRequired"));
        Assert.True((await controller.ExecuteAsync(select)).Succeeded);
        Assert.True((await controller.ExecuteAsync(new(HomeModelPickerAction.SaveRoute))).Succeeded);
        Assert.Equal(5, provider.LastEdit.ExpectedRevision);
        Assert.Equal(6, provider.Snapshot.Revision);
        Assert.Equal("next", provider.LastEdit.Route.Candidates[0].ModelId);
        Assert.Equal("medium", provider.LastEdit.Route.Candidates[1].ReasoningLevelId);

        ConfiguredModelRoute ReadCanonicalRoute()
        {
            var current = provider.Snapshot.Routes.Single();
            return new(routeId, current.Version, ModelRouteScope.User, actor.ProfileId, ModelCapabilityCategory.Chat,
                current.Candidates.Select(candidate => new ModelRouteCandidate(
                    new ModelIdentity(candidate.ProviderId, candidate.ModelId, candidate.ArtifactRevision),
                    candidate.Enabled, candidate.Order, candidate.ReasoningLevelId)).ToArray(), new ProviderPolicy());
        }
    }

    private sealed class ReasoningCatalogueProvider : IModelProvider
    {
        public string Id => "p";
        public string DisplayName => "Provider";
        public ModelProviderKind Kind => ModelProviderKind.Ollama;
        public bool IsLocal => true;
        public bool CanManageModels => false;
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderHealthStatus(Id, true, "Ready", TimeSpan.Zero, DateTimeOffset.UnixEpoch));
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>(new[] { "current", "next" }
                .Select(name => new ProviderModelDescriptor(Id, IsLocal, new ModelDescriptor(name, 0, "test", "1B", "Q8",
                    new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch))).ToArray());
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Picker actions must not start inference.");
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Picker actions must not start inference.");
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Picker actions must not start inference.");
    }

    private static HomeModelPickerCatalogueEntry Model(string id, params string[] capabilities) =>
        new("p", id, null, "Friendly " + id, "Provider", true,
            capabilities.ToHashSet(StringComparer.Ordinal), 4096, null, null, null);

    private static HomeModelRouteContract Route(string id, params HomeModelRouteCandidate[] candidates) =>
        new(id, 4, "User", "chat", null, null, candidates, JsonSerializer.SerializeToElement(new { }));

    private static T Value<T>(ICuiBindingContext context, string path)
    {
        Assert.True(context.TryGetValue(path, out var value));
        return Assert.IsType<T>(value);
    }
}
