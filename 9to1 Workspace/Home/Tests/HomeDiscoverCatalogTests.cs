using HavenOS.Home.Discover;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeDiscoverCatalogTests
{
    [Fact]
    public async Task Search_keeps_provider_scoped_identity_and_unknown_metadata()
    {
        var local = Source("local", Model("local", "same-model", "Shared name", categories: null));
        var cloud = Source("cloud", Model("cloud", "same-model", "Shared name",
            categories: new HashSet<HomeDiscoverCategory> { HomeDiscoverCategory.Chat }));
        var catalog = new HomeDiscoverCatalog([local, cloud]);

        var page = await catalog.SearchAsync(new HomeDiscoverQuery(SearchText: "shared", PageSize: 10));
        var filtered = await catalog.SearchAsync(new HomeDiscoverQuery(Category: HomeDiscoverCategory.Chat));

        Assert.Equal(HomeDiscoverSourceState.Available, page.State);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Models.Select(model => model.Identity.StableKey).Distinct(StringComparer.Ordinal).Count());
        Assert.Null(Assert.Single(page.Models, model => model.Identity.ProviderId == "local").Categories);
        Assert.Single(filtered.Models);
        Assert.Equal("cloud", filtered.Models[0].Identity.ProviderId);
    }

    [Fact]
    public async Task A_provider_failure_is_reported_without_hiding_other_catalogues()
    {
        var healthy = Source("healthy", Model("healthy", "model-1", "Healthy model"));
        var failed = new FakeDiscoverSource("failed") { Failure = new IOException("private endpoint details") };
        var catalog = new HomeDiscoverCatalog([healthy, failed]);

        var page = await catalog.SearchAsync(new HomeDiscoverQuery());

        Assert.Equal(HomeDiscoverSourceState.Partial, page.State);
        Assert.Single(page.Models);
        var failure = Assert.Single(page.Providers, provider => provider.ProviderId == "failed");
        Assert.Equal(HomeDiscoverSourceState.Failed, failure.State);
        Assert.Equal("The configured model provider could not be queried.", failure.ErrorMessage);
        Assert.Equal("Discover.ProviderUnavailable", failure.ErrorCode);
    }

    [Fact]
    public async Task Search_uses_bounded_stable_cursors_and_rejects_stale_provider_revisions()
    {
        var source = Source("provider",
            Model("provider", "a", "A"),
            Model("provider", "b", "B"),
            Model("provider", "c", "C"));
        var catalog = new HomeDiscoverCatalog([source]);

        var first = await catalog.SearchAsync(new HomeDiscoverQuery(PageSize: 1));
        var second = await catalog.SearchAsync(new HomeDiscoverQuery(PageSize: 1, PageToken: first.NextPageToken));

        Assert.Equal("a", Assert.Single(first.Models).Identity.ModelId);
        Assert.Equal("b", Assert.Single(second.Models).Identity.ModelId);
        Assert.NotNull(second.NextPageToken);

        source.Snapshot = CreateSnapshot("provider", "r2", Model("provider", "a", "A"), Model("provider", "b", "B"));
        var stale = await Assert.ThrowsAsync<HomeDiscoverCursorException>(() =>
            catalog.SearchAsync(new HomeDiscoverQuery(PageSize: 1, PageToken: second.NextPageToken)));
        Assert.Equal("Discover.CursorStale", stale.Code);
    }

    [Fact]
    public async Task Compare_reports_missing_models_without_collapsing_provider_identity()
    {
        var source = Source("provider-a", Model("provider-a", "model", "Model A"));
        var other = Source("provider-b", Model("provider-b", "model", "Model B"));
        var catalog = new HomeDiscoverCatalog([source, other]);

        var comparison = await catalog.CompareAsync([
            new("provider-a", "model"),
            new("provider-b", "model"),
            new("provider-c", "model"),
        ]);

        Assert.Equal(2, comparison.Models.Count);
        Assert.Single(comparison.MissingIdentities);
        Assert.Equal("provider-c", comparison.MissingIdentities[0].ProviderId);
    }

    [Fact]
    public void Voice_recommendations_are_mode_specific_and_require_measured_evidence()
    {
        var conversation = Model("provider", "conversation", "Conversational",
            categories: new HashSet<HomeDiscoverCategory> { HomeDiscoverCategory.Voice }) with
        {
            Voice = new(
                new HomeDiscoverVoiceCapabilities(true, false, false, false, null, null, true, true,
                    ["en"], null, null, null, null, null, TimeSpan.FromMilliseconds(250)),
                [new(HomeDiscoverVoiceMode.Conversational, 92m, "eval://conversation", DateTimeOffset.UnixEpoch,
                    ["Best measured interruption handling.", "Natural conversational cadence."])] ,
                [new("voice-1", "North", "en-GB")]),
        };
        var monologueOnly = Model("provider", "monologue", "Monologue",
            categories: new HashSet<HomeDiscoverCategory> { HomeDiscoverCategory.Voice }) with
        {
            Voice = new(
                new HomeDiscoverVoiceCapabilities(false, true, false, false, null, null, true, false,
                    ["en"], null, null, null, null, null, null),
                [new(HomeDiscoverVoiceMode.Monologue, 99m, "eval://monologue", DateTimeOffset.UnixEpoch, ["Clear long-form readout."])],
                []),
        };
        var unmeasured = Model("provider", "unknown", "Unknown",
            categories: new HashSet<HomeDiscoverCategory> { HomeDiscoverCategory.Voice }) with
        {
            Voice = new(
                new HomeDiscoverVoiceCapabilities(true, null, null, null, null, null, null, null,
                    null, null, null, null, null, null, null),
                [new(HomeDiscoverVoiceMode.Conversational, null, null, null, null)],
                null),
        };

        var conversational = HomeDiscoverCatalog.RecommendVoices([conversation, monologueOnly, unmeasured],
            HomeDiscoverVoiceMode.Conversational);
        var monologue = HomeDiscoverCatalog.RecommendVoices([conversation, monologueOnly, unmeasured],
            HomeDiscoverVoiceMode.Monologue);

        Assert.Equal("conversation", Assert.Single(conversational).Identity.ModelId);
        Assert.Contains("interruption", conversational[0].Reasons[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Best for Conversational", conversational[0].Label);
        Assert.Equal("monologue", Assert.Single(monologue).Identity.ModelId);
        Assert.Equal("Best for Monologue", monologue[0].Label);
    }

    [Fact]
    public async Task Provider_actions_require_advertised_support_and_preserve_structured_result()
    {
        var model = Model("provider", "model", "Model", actions: [HomeDiscoverAction.Pull]);
        var source = Source("provider", model);
        source.ActionResult = new("operation-1", model.Identity, HomeDiscoverAction.Pull, true,
            "Succeeded", "Pull completed.", false, true, 3);
        var catalog = new HomeDiscoverCatalog([source]);

        var rejected = await catalog.ExecuteAsync(new HomeDiscoverActionRequest(model.Identity,
            HomeDiscoverAction.Connect, "operation-connect"));
        var result = await catalog.ExecuteAsync(new HomeDiscoverActionRequest(model.Identity,
            HomeDiscoverAction.Pull, "operation-pull"));

        Assert.False(rejected.Succeeded);
        Assert.Equal("Discover.ActionUnsupported", rejected.Code);
        Assert.Equal(0, source.ActionCalls);
        Assert.True(result.Succeeded);
        Assert.Equal("operation-1", result.OperationId);
        Assert.Equal("provider", result.Identity.ProviderId);
        Assert.Equal(1, source.ActionCalls);
    }

    [Fact]
    public void Duplicate_provider_registrations_are_rejected()
    {
        var first = Source("same", Model("same", "a", "A"));
        var second = Source("same", Model("same", "b", "B"));

        Assert.Throws<ArgumentException>(() => new HomeDiscoverCatalog([first, second]));
    }

    private static FakeDiscoverSource Source(string providerId, params HomeDiscoverModel[] models) =>
        new(providerId) { Snapshot = CreateSnapshot(providerId, "r1", models) };

    private static HomeDiscoverProviderSnapshot CreateSnapshot(string providerId, string revision,
        params HomeDiscoverModel[] models) => new(providerId, providerId, HomeDiscoverConnectionState.Connected,
        models.Length == 0 ? HomeDiscoverSourceState.Empty : HomeDiscoverSourceState.Available,
        revision, models);

    private static HomeDiscoverModel Model(string providerId, string modelId, string name,
        IReadOnlySet<HomeDiscoverCategory>? categories = null,
        IReadOnlySet<HomeDiscoverAction>? actions = null) => new(
        new HomeDiscoverIdentity(providerId, modelId), name, null, categories, null, null, null,
        null, null, HomeDiscoverLocality.Unknown, HomeDiscoverInstallState.Unknown,
        HomeDiscoverConnectionState.Unknown, null, null, null, null, null, null,
        actions ?? new HashSet<HomeDiscoverAction>());

    private sealed class FakeDiscoverSource(string providerId) : IHomeDiscoverCatalogueSource
    {
        public string ProviderId { get; } = providerId;
        public HomeDiscoverProviderSnapshot Snapshot { get; set; } = HomeDiscoverCatalogTests.CreateSnapshot(providerId, "empty");
        public Exception? Failure { get; init; }
        public HomeDiscoverActionResult? ActionResult { get; set; }
        public int ActionCalls { get; private set; }

        public Task<HomeDiscoverProviderSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null) return Task.FromException<HomeDiscoverProviderSnapshot>(Failure);
            return Task.FromResult(Snapshot);
        }

        public Task<HomeDiscoverActionResult> ExecuteAsync(HomeDiscoverActionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ActionCalls++;
            return Task.FromResult(ActionResult ?? new HomeDiscoverActionResult("operation-default",
                request.Identity, request.Action, false, "Provider.NotConfigured", "No action result configured.",
                false, true));
        }
    }
}
