using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Dulche.Runtime;
using Haven.Application;
using Haven.Application.Haven;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using NineToOne.Cui.AI;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

/// <summary>
/// Owning coverage for the Home-owned resolved-selection source: real route eligibility
/// (disabled/out-of-order/unavailable/blocked), the advertised reasoning-ID bridge (exact Ordinal
/// membership -> exact ID preserved), the validated commit path, the optional named-reasoning
/// capability port (supported reaches a capable provider unchanged; unsupported raises typed
/// UnsupportedCapability BEFORE invocation), legacy/no-explicit ordinary generation, and truthful
/// unknown metadata.
/// </summary>
public sealed class HomeModelSelectionSourceTests
{
    private static AuthenticatedResourceActor Actor => new("actor-1", "p1", null, null, "rev-9");
    private static AuthenticatedResourceActor ChangedActor => new("actor-1", "p1", null, null, "rev-10");
    private static AuthenticatedResourceActor ForeignActor => new("intruder", "p2", Guid.NewGuid(), Guid.NewGuid(), "rev-99");

    private static ConfiguredModelRoute Route(params ModelRouteCandidate[] candidates) =>
        new(HomeModelPickerFeatureProvider.RouteId("p1", ModelCapabilityCategory.Chat), 1, ModelRouteScope.User, "p1", ModelCapabilityCategory.Chat, candidates, new ProviderPolicy());

    private static string StableKey(string providerId, string modelId) => new ModelIdentity(providerId, modelId).StableKey;

    private static Func<string, string, CancellationToken, Task<IReadOnlyList<string>?>> Advertised() =>
        (_, _, ct) => Task.FromResult<IReadOnlyList<string>?>(["low", "medium", "high"]);

    private static SelectionKey BindingKey(string modelId = "m1") => new("p1", StableKey("local-a", modelId), "User:p1");

    private static HomeModelSelectionSource Source(
        IModelProviderRegistry registry, ConfiguredModelRoute? chat, ConfiguredModelRoute? active = null,
        Func<string, string, CancellationToken, Task<IReadOnlyList<string>?>>? advertised = null,
        SelectedReasoningState? selection = null,
        Func<CancellationToken, Task<AuthenticatedResourceActor?>>? actorSource = null,
        Func<ModelCapabilityCategory, CancellationToken, Task<ConfiguredModelRoute?>>? routeReader = null) =>
        new(registry, new HomeVersionedModelRouteRepository(
                new FileHomeCoreStateStore(Path.Combine(Path.GetTempPath(),
                    "home-routes-" + Guid.NewGuid().ToString("N") + ".json"))),
            routeReader ?? ((cat, ct) => Task.FromResult(cat == ModelCapabilityCategory.Chat ? chat : active)),
            actorSource ?? (_ => Task.FromResult<AuthenticatedResourceActor?>(Actor)),
            advertised, selection);

    [Fact]
    public async Task Disabled_candidates_are_skipped_and_enabled_order_is_resolved()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1", "m2")]);
        var route = Route(
            new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: false, Order: 0),
            new ModelRouteCandidate(new ModelIdentity("local-a", "m2"), Enabled: true, Order: 5));
        var snapshot = await Source(registry, route).ResolveAsync(1, CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal("m2", snapshot!.ModelId);
        Assert.Equal("User", snapshot.ScopeName);
        Assert.Equal("p1", snapshot.ScopeId);
        Assert.Equal("rev-9", snapshot.AuthenticationRevision);
    }

    [Fact]
    public async Task Enabled_candidates_resolve_by_order_not_by_declaration_order()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1", "m2")]);
        var route = Route(
            new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 9),
            new ModelRouteCandidate(new ModelIdentity("local-a", "m2"), Enabled: true, Order: 0));
        var snapshot = await Source(registry, route).ResolveAsync(1, CancellationToken.None);
        Assert.Equal("m2", snapshot!.ModelId);
    }

    [Fact]
    public async Task Unavailable_provider_candidate_is_skipped_for_the_next_eligible()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]);
        var route = Route(
            new ModelRouteCandidate(new ModelIdentity("missing-provider", "ghost"), Enabled: true, Order: 0),
            new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 1));
        var snapshot = await Source(registry, route).ResolveAsync(1, CancellationToken.None);
        Assert.Equal("m1", snapshot!.ModelId);
    }

    [Fact]
    public async Task All_candidates_unavailable_yields_no_selection()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]);
        var route = Route(new ModelRouteCandidate(new ModelIdentity("missing", "ghost"), Enabled: true, Order: 0));
        Assert.Null(await Source(registry, route).ResolveAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task Policy_blocked_candidate_is_skipped_and_no_selection_when_none_eligible()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]);
        var route = new ConfiguredModelRoute(HomeModelPickerFeatureProvider.RouteId("p1", ModelCapabilityCategory.Chat), 1, ModelRouteScope.User, "p1", ModelCapabilityCategory.Chat,
            [new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0)],
            new ProviderPolicy(RequiredCapabilities: new HashSet<string> { "vision" }));
        Assert.Null(await Source(registry, route).ResolveAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task Falls_back_from_chat_to_active_route_when_chat_is_absent()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]);
        var active = new ConfiguredModelRoute(HomeModelPickerFeatureProvider.RouteId("p1", ModelCapabilityCategory.Active), 1, ModelRouteScope.User, "p1", ModelCapabilityCategory.Active,
            [new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0)],
            new ProviderPolicy());
        var snapshot = await Source(registry, null, active).ResolveAsync(1, CancellationToken.None);
        Assert.Equal("m1", snapshot!.ModelId);
    }

    [Fact]
    public async Task Advertised_reasoning_id_maps_to_exact_ordinal_and_preserved_id()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]);
        var route = Route(new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0));
        var selection = new SelectedReasoningState();
        var source = Source(registry, route,
            advertised: (_, modelId, ct) => Task.FromResult<IReadOnlyList<string>?>(["low", "medium", "high"]),
            selection: selection);

        var snapshot = await source.ResolveAsync(1, CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal(new[] { "low", "medium", "high" }, snapshot!.AdvertisedReasoningIds);
        Assert.Null(snapshot.SelectedReasoningOrdinal); // nothing committed yet -> no percentage
        Assert.Equal(string.Empty, snapshot.ToPresentation().EffortPercent);

        // Commit through the validated production path and re-resolve.
        var commit = await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", CancellationToken.None);
        Assert.True(commit.Succeeded);
        Assert.Equal("medium", commit.Value);
        var again = await source.ResolveAsync(2, CancellationToken.None);
        Assert.Equal(1, again!.SelectedReasoningOrdinal); // exact membership
        Assert.Equal("70%", again.ToPresentation().EffortPercent);

        // The exact advertised ID is preserved verbatim (exact Ordinal membership).
        var level = HomeModelSelectionSource.ReasoningLevelForRequest(again);
        Assert.True(level.Succeeded);
        Assert.Equal("medium", level.Value);
    }

    [Fact]
    public async Task CommitSelectionAsync_rejects_non_advertised_id_as_unsupported()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]);
        var route = Route(new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0));
        var source = Source(registry, route,
            advertised: (_, modelId, ct) => Task.FromResult<IReadOnlyList<string>?>(["low", "medium", "high"]));

        var result = await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "max", CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(DulcheErrorCode.UnsupportedCapability, result.Error!.Code);
        // Nothing was committed; the source stays truthfully absent.
        var snapshot = await source.ResolveAsync(1, CancellationToken.None);
        Assert.Null(snapshot!.SelectedReasoningId);
        Assert.Equal(string.Empty, snapshot.ToPresentation().EffortPercent);
    }

    [Fact]
    public void Invalid_advertised_metadata_stays_a_truthful_unknown()
    {
        var invalid = HavenModelProjectionSnapshot.CreateValidated(
            Actor, "rev-9", "User", "p1", "chat::p1", 1, 4,
            "local-a", "m1", true, true, ["", "high"], "high");
        Assert.Empty(invalid.AdvertisedReasoningIds);
        Assert.Null(invalid.SelectedReasoningId);
        Assert.Null(invalid.SelectedReasoningOrdinal);
        Assert.Equal(string.Empty, invalid.ToPresentation().EffortPercent);
    }

    [Fact]
    public async Task StreamAsync_carries_exact_reasoning_id_on_plain_chat_request()
    {
        var provider = new CapturingProvider("local-a", true, "m1");
        var registry = new ModelProviderRegistry([provider]);
        var route = Route(new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0));
        var source = Source(registry, route,
            advertised: (_, modelId, ct) => Task.FromResult<IReadOnlyList<string>?>(["low", "medium", "high"]));

        // Commit via the validated production path.
        var commit = await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", CancellationToken.None);
        Assert.True(commit.Succeeded);
        var snapshot = await source.ResolveAsync(1, CancellationToken.None);
        Assert.Equal(1, snapshot!.SelectedReasoningOrdinal);

        var services = new HomeAppAiServices(registry, new FileHomeCoreStateStore(Path.Combine(Path.GetTempPath(), "home-ai-"+Guid.NewGuid().ToString("N")+".json")),
            new HomePermissionCallerIdentity("actor-1", "Actor", null, null, true),
            new NoOpEventRepository(), new NoOpInvocationResolver(), modelSelectionSource: source);
        var prompt = new AppAiPrompt("hello", new AppAiContextSnapshot("app", "s", null, "s", null, new Dictionary<string, System.Text.Json.JsonElement>(), AppAiDataSensitivity.Restricted, DateTimeOffset.UtcNow), "c1", AppAiAccessMode.ReadOnly);

        var chunks = new List<AppAiResponseChunk>();
        await foreach (var chunk in services.StreamAsync(prompt, CancellationToken.None)) chunks.Add(chunk);
        // Provider chunk + the committed IsFinal chunk.
        Assert.Equal(2, chunks.Count);
        Assert.Equal("ok", chunks[0].Text);
        Assert.True(chunks[1].IsFinal);
        Assert.NotNull(provider.LastChatRequest);
        Assert.Equal("m1", provider.LastChatRequest!.Model);
        Assert.Equal("medium", provider.LastReasoningId); // exact advertised ID carried verbatim
    }

    [Fact]
    public async Task StreamAsync_carries_exact_reasoning_id_on_tool_request()
    {
        var provider = new CapturingProvider("local-a", true, "m1");
        var registry = new ModelProviderRegistry([provider]);
        var route = Route(new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0));
        var source = Source(registry, route,
            advertised: (_, modelId, ct) => Task.FromResult<IReadOnlyList<string>?>(["low", "medium", "high"]));
        var commit = await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "high", CancellationToken.None);
        Assert.True(commit.Succeeded);
        var snapshot = await source.ResolveAsync(1, CancellationToken.None);
        Assert.Equal(2, snapshot!.SelectedReasoningOrdinal);

        var services = new HomeAppAiServices(registry, new FileHomeCoreStateStore(Path.Combine(Path.GetTempPath(), "home-ai-"+Guid.NewGuid().ToString("N")+".json")),
            new HomePermissionCallerIdentity("actor-1", "Actor", null, null, true),
            new NoOpEventRepository(), new NoOpInvocationResolver(), modelSelectionSource: source);
        var actions = new[] { new AppAiActionDescriptor("act", "Act", "do", AppAiActionRisk.ReversibleChange, true, "{\"type\":\"object\"}") };
        var prompt = new AppAiPrompt("do it", new AppAiContextSnapshot("app", "s", null, "s", null, new Dictionary<string, System.Text.Json.JsonElement>(), AppAiDataSensitivity.Restricted, DateTimeOffset.UtcNow), "c2", AppAiAccessMode.Write, actions);

        var chunks = new List<AppAiResponseChunk>();
        await foreach (var chunk in services.StreamAsync(prompt, CancellationToken.None)) chunks.Add(chunk);
        Assert.Equal(2, chunks.Count);
        Assert.Equal("ok", chunks[0].Text);
        Assert.True(chunks[1].IsFinal);
        Assert.NotNull(provider.LastToolRequest);
        Assert.Equal("m1", provider.LastToolRequest!.Model);
        Assert.Equal("high", provider.LastReasoningId); // exact advertised ID carried verbatim
    }

    [Fact]
    public async Task StreamAsync_explicit_reasoning_on_non_capable_provider_is_rejected_before_invocation()
    {
        // An explicit reasoning selection on a provider that cannot carry it faithfully must raise a
        // typed UnsupportedCapability BEFORE the provider is invoked — never a guess or a request to
        // a provider that would ignore it.
        var provider = new FakeProvider("local-a", true, "m1");
        var registry = new ModelProviderRegistry([provider]);
        var route = Route(new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0));
        var source = Source(registry, route,
            advertised: (_, modelId, ct) => Task.FromResult<IReadOnlyList<string>?>(["low", "medium", "high"]));
        var commit = await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", CancellationToken.None);
        Assert.True(commit.Succeeded);

        var services = new HomeAppAiServices(registry, new FileHomeCoreStateStore(Path.Combine(Path.GetTempPath(), "home-ai-"+Guid.NewGuid().ToString("N")+".json")),
            new HomePermissionCallerIdentity("actor-1", "Actor", null, null, true),
            new NoOpEventRepository(), new NoOpInvocationResolver(), modelSelectionSource: source);
        var prompt = new AppAiPrompt("hello", new AppAiContextSnapshot("app", "s", null, "s", null, new Dictionary<string, System.Text.Json.JsonElement>(), AppAiDataSensitivity.Restricted, DateTimeOffset.UtcNow), "c3", AppAiAccessMode.ReadOnly);

        await Assert.ThrowsAsync<UnsupportedCapabilityException>(async () =>
        {
            await foreach (var _ in services.StreamAsync(prompt, CancellationToken.None)) { }
        });
        Assert.Equal(0, provider.InvokedCount); // provider never invoked
    }

    [Fact]
    public async Task StreamAsync_legacy_no_source_empty_effort_defaults_to_ordinary_generation()
    {
        // Regression for the c644 defect: with NO shared selection source wired, a legacy
        // GetSelectionAsync -> StreamAsync selection carrying an empty/unparseable effort carrier
        // must still GENERATE ordinary generation (neutral default), never throw.
        var provider = new CapturingProvider("local-a", true, "m1");
        var registry = new ModelProviderRegistry([provider]);
        var services = new HomeAppAiServices(registry, new FileHomeCoreStateStore(Path.Combine(Path.GetTempPath(), "home-ai-"+Guid.NewGuid().ToString("N")+".json")),
            new HomePermissionCallerIdentity("actor-1", "Actor", null, null, true),
            new NoOpEventRepository(), new NoOpInvocationResolver());
        var prompt = new AppAiPrompt("hello", new AppAiContextSnapshot("app", "s", null, "s", null, new Dictionary<string, System.Text.Json.JsonElement>(), AppAiDataSensitivity.Restricted, DateTimeOffset.UtcNow), "c4", AppAiAccessMode.ReadOnly,
            ModelSelection: new AppAiModelSelection("local-a:m1", string.Empty));

        var chunks = new List<AppAiResponseChunk>();
        await foreach (var chunk in services.StreamAsync(prompt, CancellationToken.None)) chunks.Add(chunk);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("ok", chunks[0].Text);
        Assert.True(chunks[1].IsFinal);
        Assert.NotNull(provider.LastChatRequest);
        Assert.Equal(EffortLevel.Medium, provider.LastChatRequest!.Effort); // neutral default, not a failure
        Assert.Null(provider.LastReasoningId);
    }

    [Fact]
    public async Task StreamAsync_absent_reasoning_selection_does_not_forbid_ordinary_generation()
    {
        var provider = new CapturingProvider("local-a", true, "m1");
        var registry = new ModelProviderRegistry([provider]);
        var route = Route(new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0));
        var source = Source(registry, route,
            advertised: (_, modelId, ct) => Task.FromResult<IReadOnlyList<string>?>(["low", "medium", "high"]));
        var services = new HomeAppAiServices(registry, new FileHomeCoreStateStore(Path.Combine(Path.GetTempPath(), "home-ai-"+Guid.NewGuid().ToString("N")+".json")),
            new HomePermissionCallerIdentity("actor-1", "Actor", null, null, true),
            new NoOpEventRepository(), new NoOpInvocationResolver(), modelSelectionSource: source);
        var prompt = new AppAiPrompt("hello", new AppAiContextSnapshot("app", "s", null, "s", null, new Dictionary<string, System.Text.Json.JsonElement>(), AppAiDataSensitivity.Restricted, DateTimeOffset.UtcNow), "c5", AppAiAccessMode.ReadOnly);

        var chunks = new List<AppAiResponseChunk>();
        await foreach (var chunk in services.StreamAsync(prompt, CancellationToken.None)) chunks.Add(chunk);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("ok", chunks[0].Text);
        Assert.True(chunks[1].IsFinal);
        Assert.NotNull(provider.LastChatRequest);
        Assert.Equal(EffortLevel.Medium, provider.LastChatRequest!.Effort);
        Assert.Null(provider.LastReasoningId);
    }

    [Fact]
    public void SelectedReasoningState_commit_raises_live_typed_change_event()
    {
        var state = new SelectedReasoningState();
        var key = new SelectionKey("p1", "m1", "User:p1");
        var raised = new List<SelectionChangedEventArgs>();
        state.Changed += (_, e) => raised.Add(e);

        Assert.True(state.TrySet(key, "medium"));
        Assert.Single(raised);
        Assert.Equal(key, raised[0].Key);
        Assert.Equal("medium", raised[0].SelectedId);

        // A duplicate no-op commit does NOT raise (nothing changed).
        Assert.True(state.TrySet(key, "medium"));
        Assert.Single(raised);

        // A genuine change raises again.
        Assert.True(state.TrySet(key, "high"));
        Assert.Equal(2, raised.Count);
        Assert.Equal("high", raised[1].SelectedId);
    }

    [Fact]
    public async Task HomeModelSelectionSource_surfaces_live_change_subscription()
    {
        var provider = new FakeProvider("local-a", true, "m1");
        var registry = new ModelProviderRegistry([provider]);
        var route = Route(new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0));
        var source = Source(registry, route,
            advertised: (_, modelId, ct) => Task.FromResult<IReadOnlyList<string>?>(["low", "medium", "high"]));

        var selectionRaised = 0;
        var routeRaised = 0;
        source.SelectionChanged += (_, _) => selectionRaised++;
        source.RouteChanged += (_, _) => routeRaised++;

        source.NotifyRouteCommitted();
        Assert.Equal(1, routeRaised);
        Assert.Equal(0, selectionRaised);

        var commit = await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", CancellationToken.None);
        Assert.True(commit.Succeeded);
        Assert.Equal(1, selectionRaised);
        Assert.Equal(1, routeRaised);
    }

    [Fact]
    public async Task CommitSelectionAsync_rejects_foreign_supplied_actor()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]);
        var route = Route(new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0));
        var selection = new SelectedReasoningState();
        // Seed a real committed baseline through the validated path on a shared state.
        var seed = Source(registry, route, advertised: Advertised(), selection: selection);
        var seeded = await seed.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "low", CancellationToken.None);
        Assert.True(seeded.Succeeded);

        var source = Source(registry, route, advertised: Advertised(), selection: selection);
        var raised = 0;
        source.SelectionChanged += (_, _) => raised++;

        // Foreign actor (different profile/account/organisation) never matches the genuine current actor.
        var result = await source.CommitSelectionAsync(ForeignActor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(DulcheErrorCode.InvalidArgument, result.Error!.Code);
        Assert.Equal(0, raised); // ZERO notifications
        Assert.True(selection.TryGet(BindingKey(), out var kept));
        Assert.Equal("low", kept); // preexisting state unchanged
    }

    [Fact]
    public async Task CommitSelectionAsync_rejects_stale_actor_auth_change_during_metadata()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]);
        var route = Route(new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0));
        var selection = new SelectedReasoningState();
        var seed = Source(registry, route, advertised: Advertised(), selection: selection);
        var seeded = await seed.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "low", CancellationToken.None);
        Assert.True(seeded.Succeeded);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Func<string, string, CancellationToken, Task<IReadOnlyList<string>?>> advertised = async (_, _, ct) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return ["low", "medium", "high"];
        };
        // The genuine actor flips its authentication revision WHILE the metadata read is pending.
        var source = Source(registry, route, advertised: advertised, selection: selection,
            actorSource: _ =>
            {
                calls++;
                return Task.FromResult<AuthenticatedResourceActor?>(calls == 1 ? Actor : ChangedActor);
            });
        var raised = 0;
        source.SelectionChanged += (_, _) => raised++;

        var commitTask = source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        release.TrySetResult(); // release metadata; the post-metadata actor recheck sees the changed revision
        var result = await commitTask;

        Assert.False(result.Succeeded);
        Assert.Equal(DulcheErrorCode.Conflict, result.Error!.Code);
        Assert.Equal(0, raised);
        Assert.True(selection.TryGet(BindingKey(), out var kept));
        Assert.Equal("low", kept);
    }

    [Fact]
    public async Task CommitSelectionAsync_rejects_stale_route_revision_change_during_metadata()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]);
        var originalRoute = Route(new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0));
        var switchedRoute = new ConfiguredModelRoute(HomeModelPickerFeatureProvider.RouteId("p1", ModelCapabilityCategory.Chat), 2, ModelRouteScope.User, "p1", ModelCapabilityCategory.Chat,
            [new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0)], new ProviderPolicy());
        var selection = new SelectedReasoningState();
        var seed = Source(registry, originalRoute, advertised: Advertised(), selection: selection);
        var seeded = await seed.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "low", CancellationToken.None);
        Assert.True(seeded.Succeeded);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var switchNow = false;
        Func<string, string, CancellationToken, Task<IReadOnlyList<string>?>> advertised = async (_, _, ct) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return ["low", "medium", "high"];
        };
        // The genuine route revision flips WHILE the metadata read is pending.
        var source = Source(registry, null, advertised: advertised, selection: selection,
            routeReader: (cat, ct) => Task.FromResult(cat == ModelCapabilityCategory.Chat ? (switchNow ? switchedRoute : originalRoute) : (ConfiguredModelRoute?)null));
        var raised = 0;
        source.SelectionChanged += (_, _) => raised++;

        var commitTask = source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        switchNow = true; // flip the genuine route before the post-metadata route re-read
        release.TrySetResult();
        var result = await commitTask;

        Assert.False(result.Succeeded);
        Assert.Equal(DulcheErrorCode.Conflict, result.Error!.Code);
        Assert.Equal(0, raised);
        Assert.True(selection.TryGet(BindingKey(), out var kept));
        Assert.Equal("low", kept);
    }

    [Fact]
    public async Task CommitSelectionAsync_rejects_wrong_caller_model_stable_key()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]);
        var route = Route(new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0));
        var selection = new SelectedReasoningState();
        var seed = Source(registry, route, advertised: Advertised(), selection: selection);
        var seeded = await seed.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "low", CancellationToken.None);
        Assert.True(seeded.Succeeded);

        var source = Source(registry, route, advertised: Advertised(), selection: selection);
        var raised = 0;
        source.SelectionChanged += (_, _) => raised++;

        var result = await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "ghost"), "User:p1", "medium", CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(DulcheErrorCode.InvalidArgument, result.Error!.Code);
        Assert.Equal(0, raised);
        Assert.True(selection.TryGet(BindingKey(), out var kept));
        Assert.Equal("low", kept);
    }

    [Fact]
    public async Task CommitSelectionAsync_rejects_wrong_caller_surface_scope()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]);
        var route = Route(new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0));
        var selection = new SelectedReasoningState();
        var seed = Source(registry, route, advertised: Advertised(), selection: selection);
        var seeded = await seed.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "low", CancellationToken.None);
        Assert.True(seeded.Succeeded);

        var source = Source(registry, route, advertised: Advertised(), selection: selection);
        var raised = 0;
        source.SelectionChanged += (_, _) => raised++;

        var result = await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "Task:p1", "medium", CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(DulcheErrorCode.InvalidArgument, result.Error!.Code);
        Assert.Equal(0, raised);
        Assert.True(selection.TryGet(BindingKey(), out var kept));
        Assert.Equal("low", kept);
    }

    [Fact]
    public async Task CommitSelectionAsync_cancellation_returns_typed_refusal_without_mutation()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]);
        var route = Route(new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0));
        var selection = new SelectedReasoningState();
        var seed = Source(registry, route, advertised: Advertised(), selection: selection);
        var seeded = await seed.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "low", CancellationToken.None);
        Assert.True(seeded.Succeeded);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<string, string, CancellationToken, Task<IReadOnlyList<string>?>> advertised = async (_, _, ct) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return ["low", "medium", "high"];
        };
        var source = Source(registry, route, advertised: advertised, selection: selection);
        var raised = 0;
        source.SelectionChanged += (_, _) => raised++;

        using var cts = new CancellationTokenSource();
        var commitTask = source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        cts.Cancel(); // cancel while the metadata read is pending
        release.TrySetResult();
        var result = await commitTask;

        Assert.False(result.Succeeded);
        Assert.Equal(DulcheErrorCode.InvalidState, result.Error!.Code);
        Assert.Equal(0, raised);
        Assert.True(selection.TryGet(BindingKey(), out var kept));
        Assert.Equal("low", kept);
    }

    [Fact]
    public async Task CommitSelectionAsync_valid_commit_raises_exactly_one_event()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]);
        var route = Route(new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0));
        var source = Source(registry, route, advertised: Advertised());
        var raised = 0;
        source.SelectionChanged += (_, _) => raised++;

        var result = await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Equal(1, raised);
        Assert.True(source.Selection.TryGet(BindingKey(), out var kept));
        Assert.Equal("medium", kept);
    }

    [Fact]
    public async Task CommitSelectionAsync_no_change_commit_raises_no_event()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]);
        var route = Route(new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0));
        var source = Source(registry, route, advertised: Advertised());
        var raised = 0;
        source.SelectionChanged += (_, _) => raised++;

        var first = await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", CancellationToken.None);
        Assert.True(first.Succeeded);
        Assert.Equal(1, raised);

        var second = await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", CancellationToken.None);
        Assert.True(second.Succeeded);
        Assert.Equal(1, raised); // no additional notification
        Assert.True(source.Selection.TryGet(BindingKey(), out var kept));
        Assert.Equal("medium", kept);
    }

    [Fact]
    public async Task Persisted_route_candidate_reasoning_default_wins_over_stale_memory_selection()
    {
        // Persist a genuine route through the REAL guarded CAS route-edit owner (FileHomeCoreStateStore
        // + HomeVersionedModelRouteRepository.TrySaveGuardedAsync) carrying a candidate-bound reasoning
        // level. Then a freshly-recreated source must restore that saved default as the selected ID,
        // even when the in-memory state seeds a conflicting stale selection.
        var store = new FileHomeCoreStateStore(Path.Combine(Path.GetTempPath(),
            "home-routes-" + Guid.NewGuid().ToString("N") + ".json"));
        var repo = new HomeVersionedModelRouteRepository(store);
        var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
        var actor = await profiles.GetCurrentAsync(CancellationToken.None) ?? throw new InvalidOperationException("Actual Home profile unavailable.");

        var routeId = HomeModelPickerFeatureProvider.RouteId(actor.ProfileId, ModelCapabilityCategory.Chat);
        var route = new ConfiguredModelRoute(routeId, 1, ModelRouteScope.User, actor.ProfileId, ModelCapabilityCategory.Chat,
            [new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0, ReasoningLevelId: "medium")],
            new ProviderPolicy());
        Assert.True(await repo.TrySaveGuardedAsync(route, 0, actor, profiles, CancellationToken.None));

        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]);
        // Stale in-memory selection that disagrees with the persisted default; it must NOT override.
        var selection = new SelectedReasoningState();
        selection.TrySet(new SelectionKey(actor.ProfileId, StableKey("local-a", "m1"), "User:" + actor.ProfileId), "low");

        var source = new HomeModelSelectionSource(registry, repo,
            async (cat, ct) => cat == ModelCapabilityCategory.Chat ? await repo.GetAsync(routeId, ct) : null,
            _ => Task.FromResult<AuthenticatedResourceActor?>(actor),
            (_, _, ct) => Task.FromResult<IReadOnlyList<string>?>(["low", "medium", "high"]),
            selection);

        var snapshot = await source.ResolveAsync(1, CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal("medium", snapshot!.SelectedReasoningId); // persisted saved default wins over stale memory
        Assert.Equal(1, snapshot.SelectedReasoningOrdinal);
        Assert.Equal("70%", snapshot.ToPresentation().EffortPercent);
        Assert.Equal("medium", HomeModelSelectionSource.ReasoningLevelForRequest(snapshot).Value);
    }

    [Fact]
    public async Task Canonical_cleared_candidate_default_stays_cleared_despite_stale_memory()
    {
        // In canonical production mode (guarded route-CAS owner wired) a matched route candidate with a
        // cleared/null saved default is authoritative and must NOT resurrect stale in-memory selection:
        // a successful UI clear/save followed by source recreation stays cleared.
        var (provider, repo, actor, profiles) = GenuineProviderFixture();
        var routeId = HomeModelPickerFeatureProvider.RouteId(actor.ProfileId, ModelCapabilityCategory.Chat);
        var route = new ConfiguredModelRoute(routeId, 1, ModelRouteScope.User, actor.ProfileId, ModelCapabilityCategory.Chat,
            [new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0, ReasoningLevelId: null)],
            new ProviderPolicy());
        Assert.True(await repo.TrySaveGuardedAsync(route, 0, actor, profiles, CancellationToken.None));

        // Stale in-memory selection that must NOT resurrect over the cleared persisted default.
        var selection = new SelectedReasoningState();
        selection.TrySet(new SelectionKey(actor.ProfileId, StableKey("local-a", "m1"), "User:" + actor.ProfileId), "high");

        var source = new HomeModelSelectionSource(
            new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]), repo,
            async (cat, ct) => cat == ModelCapabilityCategory.Chat ? await repo.GetAsync(routeId, ct) : null,
            _ => Task.FromResult<AuthenticatedResourceActor?>(actor),
            (_, _, ct) => Task.FromResult<IReadOnlyList<string>?>(["low", "medium", "high"]),
            selection, routeProvider: provider);

        var snapshot = await source.ResolveAsync(1, CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Null(snapshot!.SelectedReasoningId); // cleared stays cleared despite stale memory
        Assert.Null(snapshot.SelectedReasoningOrdinal);
    }

    [Fact]
    public async Task UpdateRouteForActorAsync_approval_pending_refuses_without_mutating_store_or_publishing()
    {
        var (provider, repo, actor, _) = GenuineProviderFixture();
        var notified = 0;
        var seam = new NotifyingOriginalActorProvider(provider, () => notified++);

        var result = await seam.UpdateRouteForActorAsync(actor, ValidChatEdit(actor), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("ApprovalRequired", result.Code);
        Assert.Equal(0, notified); // ZERO publication: no success reached the commit seam
        Assert.Null(await repo.GetAsync(HomeModelPickerFeatureProvider.RouteId(actor.ProfileId, ModelCapabilityCategory.Chat), CancellationToken.None)); // store unchanged
    }

    [Fact]
    public async Task UpdateRouteForActorAsync_rejects_foreign_actor_without_publishing()
    {
        var (provider, repo, actor, _) = GenuineProviderFixture();
        var notified = 0;
        var seam = new NotifyingOriginalActorProvider(provider, () => notified++);

        var result = await seam.UpdateRouteForActorAsync(ForeignActor, ValidChatEdit(actor), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("PermissionDenied", result.Code);
        Assert.Equal(0, notified); // ZERO publication
        Assert.Null(await repo.GetAsync(HomeModelPickerFeatureProvider.RouteId(actor.ProfileId, ModelCapabilityCategory.Chat), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateRouteCoreAsync_rejects_stale_expected_revision_CAS_conflict_without_publishing()
    {
        var (provider, repo, actor, profiles) = GenuineProviderFixture();
        var notified = 0;
        var seam = new NotifyingOriginalActorProvider(provider, () => notified++);

        // Persist the route at revision 1 first, then attempt an edit against a stale expected revision 0.
        var routeId = HomeModelPickerFeatureProvider.RouteId(actor.ProfileId, ModelCapabilityCategory.Chat);
        var saved = new ConfiguredModelRoute(routeId, 1, ModelRouteScope.User, actor.ProfileId, ModelCapabilityCategory.Chat,
            [new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0, ReasoningLevelId: "medium")],
            new ProviderPolicy());
        Assert.True(await repo.TrySaveGuardedAsync(saved, 0, actor, profiles, CancellationToken.None));

        var staleEdit = new HomeModelRouteEdit(
            new HomeModelRouteContract(routeId, 1, "User", "Chat", null, null,
                [new HomeModelRouteCandidate("local-a", "m1", null, true, 0, "high")],
                JsonSerializer.SerializeToElement(new ProviderPolicy()), actor.ProfileId), 0);

        // The genuine guarded CAS refuses because the store already holds revision 1 (expected 0). The
        // route-conflict guard fires before approval, so no success is published.
        var result = await seam.UpdateRouteForActorAsync(actor, staleEdit, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Conflict", result.Code);
        Assert.Equal(0, notified); // ZERO publication
        var canonical = await repo.GetAsync(routeId, CancellationToken.None);
        Assert.NotNull(canonical);
        Assert.Equal("medium", canonical!.Candidates.Single().ReasoningLevelId); // canonical state preserved
    }

    [Fact]
    public async Task CommitSelectionAsync_production_delegates_through_guarded_route_CAS_and_publishes_on_success()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]);
        var route = Route(new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0));
        var selection = new SelectedReasoningState();
        var stub = new StubRouteProvider(new HomeCoreOperationResult<HomeModelPickerSnapshot>(true, "Saved", "Route save.",
            new(1, "User", "Chat", [
                new HomeModelRouteContract(route.RouteId, 2, "User", "Chat", null, null,
                    [new HomeModelRouteCandidate("local-a", "m1", null, true, 0, "medium")],
                    JsonSerializer.SerializeToElement(new ProviderPolicy()), "p1")])));
        var source = new HomeModelSelectionSource(registry,
            new HomeVersionedModelRouteRepository(new FileHomeCoreStateStore(Path.Combine(Path.GetTempPath(),
                "home-routes-" + Guid.NewGuid().ToString("N") + ".json"))),
            (cat, ct) => Task.FromResult(cat == ModelCapabilityCategory.Chat ? route : (ConfiguredModelRoute?)null),
            _ => Task.FromResult<AuthenticatedResourceActor?>(Actor),
            (_, _, ct) => Task.FromResult<IReadOnlyList<string>?>(["low", "medium", "high"]),
            selection, routeProvider: stub);

        var routeRaised = 0;
        source.RouteChanged += (_, _) => routeRaised++;

        var result = await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("medium", result.Value);
        Assert.Equal(Actor, stub.LastActor);
        Assert.NotNull(stub.LastEdit);
        Assert.Equal(1, stub.LastEdit!.ExpectedRevision);            // genuine current route revision
        Assert.Equal(2, stub.LastEdit.Route.Version);                    // expected revision + 1
        Assert.Equal("medium", stub.LastEdit.Route.Candidates.Single().ReasoningLevelId);
        Assert.Equal(1, routeRaised); // committed canonical success refreshes live label
    }

    [Fact]
    public async Task CommitSelectionAsync_production_approval_pending_refuses_with_zero_publication_and_retains_review()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]);
        var route = Route(new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0));
        var selection = new SelectedReasoningState();
        var stub = new StubRouteProvider(new HomeCoreOperationResult<HomeModelPickerSnapshot>(false, "ApprovalRequired",
            "Review this exact route change in Home before saving.",
            new(0, "User", "Chat", []) { PendingApprovalRequestId = "req-1" }));
        var source = new HomeModelSelectionSource(registry,
            new HomeVersionedModelRouteRepository(new FileHomeCoreStateStore(Path.Combine(Path.GetTempPath(),
                "home-routes-" + Guid.NewGuid().ToString("N") + ".json"))),
            (cat, ct) => Task.FromResult(cat == ModelCapabilityCategory.Chat ? route : (ConfiguredModelRoute?)null),
            _ => Task.FromResult<AuthenticatedResourceActor?>(Actor),
            (_, _, ct) => Task.FromResult<IReadOnlyList<string>?>(["low", "medium", "high"]),
            selection, routeProvider: stub);

        var routeRaised = 0;
        source.RouteChanged += (_, _) => routeRaised++;

        var result = await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", CancellationToken.None);

        Assert.False(result.Succeeded);           // no approval-pending success
        Assert.Equal(0, routeRaised);             // ZERO publication
        Assert.False(selection.TryGet(BindingKey(), out _)); // canonical state untouched (no memory commit)
        Assert.Equal("req-1", source.GetPendingApprovalRequestId("p1", StableKey("local-a", "m1"), "User:p1"));
    }

    [Fact]
    public async Task CommitSelectionAsync_production_resumes_retained_review_after_approval_only()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]);
        var route = Route(new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0));
        var selection = new SelectedReasoningState();
        var stub = new StubRouteProvider(new HomeCoreOperationResult<HomeModelPickerSnapshot>(false, "ApprovalRequired",
            "Review this exact route change in Home before saving.",
            new(0, "User", "Chat", []) { PendingApprovalRequestId = "req-1" }));
        var source = new HomeModelSelectionSource(registry,
            new HomeVersionedModelRouteRepository(new FileHomeCoreStateStore(Path.Combine(Path.GetTempPath(),
                "home-routes-" + Guid.NewGuid().ToString("N") + ".json"))),
            (cat, ct) => Task.FromResult(cat == ModelCapabilityCategory.Chat ? route : (ConfiguredModelRoute?)null),
            _ => Task.FromResult<AuthenticatedResourceActor?>(Actor),
            (_, _, ct) => Task.FromResult<IReadOnlyList<string>?>(["low", "medium", "high"]),
            selection, routeProvider: stub);

        // First commit -> ApprovalRequired retained (fresh authorization carries no request id).
        var first = await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", CancellationToken.None);
        Assert.False(first.Succeeded);
        Assert.Equal("req-1", source.GetPendingApprovalRequestId("p1", StableKey("local-a", "m1"), "User:p1"));
        Assert.Null(stub.LastEdit!.ApprovalRequestId);
        Assert.Equal(1, stub.UpdateCalls);

        // An early repeat BEFORE approval observes the genuine typed PendingApproval state and keeps the
        // SAME single review: it must NOT reauthorize, Begin, or even invoke the route provider again.
        var beforeApproval = await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", CancellationToken.None);
        Assert.False(beforeApproval.Succeeded);
        Assert.Equal("req-1", source.GetPendingApprovalRequestId("p1", StableKey("local-a", "m1"), "User:p1"));
        Assert.Equal(1, stub.UpdateCalls);          // no duplicate authorization/begin
        Assert.Null(stub.LastEdit.ApprovalRequestId);

        // Genuine approval: the request is now observed as approved, so the next commit resumes the SAME
        // payload through the SAME request id and commits.
        stub.ApprovalAuthorization = new(HomePermissionRequestState.Approved, "HOME_PERMISSION_ACCEPTED",
            "Approved.", "req-1", null);
        stub.UpdateResult = new HomeCoreOperationResult<HomeModelPickerSnapshot>(true, "Saved", "Route save.",
            new(1, "User", "Chat", [new HomeModelRouteContract(route.RouteId, 2, "User", "Chat", null, null,
                [new HomeModelRouteCandidate("local-a", "m1", null, true, 0, "medium")],
                JsonSerializer.SerializeToElement(new ProviderPolicy()), "p1")]));
        var routeRaised = 0;
        source.RouteChanged += (_, _) => routeRaised++;

        var resumed = await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", CancellationToken.None);

        Assert.True(resumed.Succeeded);
        Assert.Equal("medium", resumed.Value);
        Assert.Equal("req-1", stub.LastEdit!.ApprovalRequestId); // SAME review resumed, no new request
        Assert.Equal(2, stub.UpdateCalls); // exactly one CAS resume
        Assert.Equal(1, routeRaised);
        Assert.Null(source.GetPendingApprovalRequestId("p1", StableKey("local-a", "m1"), "User:p1")); // cleared
    }

    [Fact]
    public async Task CommitSelectionAsync_production_denied_retained_review_invalidates_leaving_canonical_unchanged()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]);
        var route = Route(new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0));
        var selection = new SelectedReasoningState();
        var stub = new StubRouteProvider(new HomeCoreOperationResult<HomeModelPickerSnapshot>(false, "ApprovalRequired",
            "Review this exact route change in Home before saving.",
            new(0, "User", "Chat", []) { PendingApprovalRequestId = "req-1" }));
        var source = new HomeModelSelectionSource(registry,
            new HomeVersionedModelRouteRepository(new FileHomeCoreStateStore(Path.Combine(Path.GetTempPath(),
                "home-routes-" + Guid.NewGuid().ToString("N") + ".json"))),
            (cat, ct) => Task.FromResult(cat == ModelCapabilityCategory.Chat ? route : (ConfiguredModelRoute?)null),
            _ => Task.FromResult<AuthenticatedResourceActor?>(Actor),
            (_, _, ct) => Task.FromResult<IReadOnlyList<string>?>(["low", "medium", "high"]),
            selection, routeProvider: stub);

        await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", CancellationToken.None);
        Assert.Equal("req-1", source.GetPendingApprovalRequestId("p1", StableKey("local-a", "m1"), "User:p1"));

        // Genuine denial is observed as a typed Denied state (never guessed from an opaque result): the
        // retained review is invalidated and no success/route-commit is published.
        stub.ApprovalAuthorization = new(HomePermissionRequestState.Denied, "HOME_PERMISSION_DENIED", "Declined.", "req-1", null);
        var routeRaised = 0;
        source.RouteChanged += (_, _) => routeRaised++;

        var result = await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, routeRaised); // ZERO publication
        Assert.Equal(1, stub.UpdateCalls); // no Begin/claim/CAS attempted for a denied review
        Assert.Null(source.GetPendingApprovalRequestId("p1", StableKey("local-a", "m1"), "User:p1")); // cleared
        Assert.False(selection.TryGet(BindingKey(), out _)); // canonical state untouched
    }

    [Fact]
    public async Task CommitSelectionAsync_production_retains_audit_blocks_replay_and_finishes_once()
    {
        var registry = new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]);
        var route = Route(new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0));
        var selection = new SelectedReasoningState();
        var stub = new StubRouteProvider(new HomeCoreOperationResult<HomeModelPickerSnapshot>(false, "ApprovalRequired",
            "Review this exact route change in Home before saving.",
            new(0, "User", "Chat", []) { PendingApprovalRequestId = "req-1" }));
        var source = new HomeModelSelectionSource(registry,
            new HomeVersionedModelRouteRepository(new FileHomeCoreStateStore(Path.Combine(Path.GetTempPath(),
                "home-routes-" + Guid.NewGuid().ToString("N") + ".json"))),
            (cat, ct) => Task.FromResult(cat == ModelCapabilityCategory.Chat ? route : (ConfiguredModelRoute?)null),
            _ => Task.FromResult<AuthenticatedResourceActor?>(Actor),
            (_, _, ct) => Task.FromResult<IReadOnlyList<string>?>(["low", "medium", "high"]),
            selection, routeProvider: stub);

        await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", CancellationToken.None);

        // Approved resume commits the canonical save once but leaves an unconfirmed audit trail (SavedAuditPending).
        stub.ApprovalAuthorization = new(HomePermissionRequestState.Approved, "HOME_PERMISSION_ACCEPTED", "Approved.", "req-1", null);
        stub.UpdateResult = new HomeCoreOperationResult<HomeModelPickerSnapshot>(true, "SavedAuditPending",
            "Saved; audit trail unconfirmed.",
            new(1, "User", "Chat", [new HomeModelRouteContract(route.RouteId, 2, "User", "Chat", null, null,
                [new HomeModelRouteCandidate("local-a", "m1", null, true, 0, "medium")],
                JsonSerializer.SerializeToElement(new ProviderPolicy()), "p1")]) { PendingAuditRequestId = "aud-1" });
        var routeRaised = 0;
        source.RouteChanged += (_, _) => routeRaised++;

        var committed = await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", CancellationToken.None);
        Assert.True(committed.Succeeded);          // canonical save committed -> selection updates once
        Assert.Equal("medium", committed.Value);
        Assert.Equal(1, routeRaised);
        Assert.True(selection.TryGet(BindingKey(), out var sel) && sel == "medium");
        Assert.Equal("aud-1", source.GetPendingAuditRequestId("p1", StableKey("local-a", "m1"), "User:p1"));

        // Replay is blocked while the audit is unresolved: no extra CAS, no new authorization.
        var replay = await source.CommitSelectionAsync(Actor, "m1", StableKey("local-a", "m1"), "User:p1", "medium", CancellationToken.None);
        Assert.False(replay.Succeeded);
        Assert.Equal(2, stub.UpdateCalls); // the committed save + no further CAS
        Assert.Equal(1, routeRaised);      // still exactly one canonical publication

        // Finish/retry routes through the SAME owner; the first success clears the retained audit.
        stub.AuditResult = new(true, "AuditRecorded", "Audit confirmed.");
        var finish = await source.FinishAuditAsync("aud-1", CancellationToken.None);
        Assert.True(finish.Succeeded);
        Assert.Null(source.GetPendingAuditRequestId("p1", StableKey("local-a", "m1"), "User:p1"));
    }

    private sealed class StubRouteProvider(HomeCoreOperationResult<HomeModelPickerSnapshot> updateResult)
        : IHomeModelPickerOriginalActorFeatureProvider
    {
        public HomeModelRouteEdit? LastEdit { get; private set; }
        public AuthenticatedResourceActor? LastActor { get; private set; }
        public HomeCoreOperationResult<HomeModelPickerSnapshot> UpdateResult { get; set; } = updateResult;
        public int UpdateCalls { get; private set; }
        public HomePermissionAuthorization ApprovalAuthorization { get; set; } = new(
            HomePermissionRequestState.PendingApproval, "HOME_PERMISSION_REQUIRED", "Review in Home.", "req", null);
        public HomeCoreOperationResult<object> AuditResult { get; set; } = new(false, "AuditNotOwned", "n/a");

        public Task<HomeCoreOperationResult<HomeModelCataloguePage>> GetCatalogueAsync(string? query = null, CancellationToken ct = default) =>
            Task.FromResult(new HomeCoreOperationResult<HomeModelCataloguePage>(true, "Ready", "ok.", new([], 0, 20, false, 0)));
        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> GetSnapshotAsync(string scope, string category, CancellationToken ct = default) =>
            Task.FromResult(new HomeCoreOperationResult<HomeModelPickerSnapshot>(true, "Ready", "ok.", new(0, "User", category, [])));
        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> UpdateRouteAsync(HomeModelRouteEdit edit, CancellationToken ct = default)
        { UpdateCalls++; LastEdit = edit; return Task.FromResult(UpdateResult); }
        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> UpdateRouteForActorAsync(
            AuthenticatedResourceActor expectedActor, HomeModelRouteEdit edit, CancellationToken ct = default)
        { UpdateCalls++; LastActor = expectedActor; LastEdit = edit; return Task.FromResult(UpdateResult); }
        public Task<HomePermissionAuthorization?> GetApprovalAuthorizationAsync(string requestId, CancellationToken ct = default) =>
            Task.FromResult<HomePermissionAuthorization?>(ApprovalAuthorization);
        public Task<HomeCoreOperationResult<HomeModelRoutePreview>> PreviewResolutionAsync(HomeModelRoutePreviewRequest request, CancellationToken ct = default) =>
            Task.FromResult(new HomeCoreOperationResult<HomeModelRoutePreview>(false, "Denied", "n/a"));
        public Task<HomeCoreOperationResult<object>> RetryAuditAsync(string requestId, CancellationToken ct = default) =>
            Task.FromResult(AuditResult);
    }

    private sealed class CapturingProvider(string id, bool isLocal, string modelName)
        : IModelProvider, INamedReasoningProvider
    {
        public OllamaChatRequest? LastChatRequest { get; private set; }
        public OllamaToolRequest? LastToolRequest { get; private set; }
        public string? LastReasoningId { get; private set; }
        public string Id => id;
        public string DisplayName => id;
        public bool IsLocal => isLocal;
        public bool CanManageModels => false;
        public ModelProviderKind Kind => isLocal ? ModelProviderKind.Ollama : ModelProviderKind.OpenAI;
        public ProviderModelDescriptor Model { get; } = new(id, isLocal, new ModelDescriptor(
            modelName, 123, "synthetic", "7B", "Q8",
            new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, DateTimeOffset.UnixEpoch));
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([Model]); }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) =>
            Task.FromResult(new ProviderHealthStatus(id, true, "synthetic", TimeSpan.Zero, DateTimeOffset.UnixEpoch));
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken token)
        { LastChatRequest = request; await Task.Yield(); yield return "ok"; }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => Task.FromResult("ok");
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token)
        { LastToolRequest = request; return Task.FromResult(new OllamaToolResponse("ok", [])); }
        public async IAsyncEnumerable<string> StreamChatWithReasoningAsync(OllamaChatRequest request, string reasoningId, [EnumeratorCancellation] CancellationToken token)
        {
            LastChatRequest = request;
            LastReasoningId = reasoningId;
            token.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return "ok";
        }
        public Task<OllamaToolResponse> ChatWithToolsWithReasoningAsync(OllamaToolRequest request, string reasoningId, CancellationToken token)
        { LastToolRequest = request; LastReasoningId = reasoningId; return Task.FromResult(new OllamaToolResponse("ok", [])); }
    }

    private sealed class NoOpEventRepository : IExecutionEventRepository
    {
        public Task AppendAsync(IReadOnlyList<ExecutionEvent> events, CancellationToken token) => Task.CompletedTask;
        public Task<IReadOnlyList<ExecutionEvent>> GetExecutionAsync(Guid executionId, CancellationToken token) => Task.FromResult<IReadOnlyList<ExecutionEvent>>([]);
        public Task<IReadOnlyList<ExecutionSummary>> SearchExecutionsAsync(string? query, int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<ExecutionSummary>>([]);
    }

    private sealed class NoOpInvocationResolver : IInvocationResolver
    {
        public ValueTask<IReadOnlyList<InvocationToken>> ResolveAsync(IReadOnlyList<InvocationToken> tokens, CancellationToken token) => new(tokens);
    }

    private static (HomeModelPickerFeatureProvider Provider, HomeVersionedModelRouteRepository Repo, AuthenticatedResourceActor Actor, HomeLocalProfileIdentity Profiles) GenuineProviderFixture()
    {
        var store = new FileHomeCoreStateStore(Path.Combine(Path.GetTempPath(),
            "home-routes-" + Guid.NewGuid().ToString("N") + ".json"));
        var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
        var actor = profiles.GetCurrentAsync(CancellationToken.None).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("Actual Home profile unavailable.");
        var permissions = new HomePermissionTrustService(store, new HomeModelRouteActionPolicies().TryGet);
        var repo = new HomeVersionedModelRouteRepository(store);
        var resources = new ResourceAuthorizationService(profiles,
            [new HomeModelRouteProfileOwner(profiles), new HomeModelRouteOwner(profiles, repo)]);
        var broker = new HomeResourceOperationBroker(resources, permissions);
        var provider = new HomeModelPickerFeatureProvider(profiles, repo,
            new ModelProviderRegistry([new FakeProvider("local-a", true, "m1")]),
            new TestPrivacyPreferenceStore(), resources, broker);
        return (provider, repo, actor, profiles);
    }

    private static HomeModelRouteEdit ValidChatEdit(AuthenticatedResourceActor actor)
    {
        var routeId = HomeModelPickerFeatureProvider.RouteId(actor.ProfileId, ModelCapabilityCategory.Chat);
        var contract = new HomeModelRouteContract(routeId, 1, "User", "Chat", null, null,
            [new HomeModelRouteCandidate("local-a", "m1", null, true, 0, "medium")],
            JsonSerializer.SerializeToElement(new ProviderPolicy()), actor.ProfileId);
        return new HomeModelRouteEdit(contract, 0);
    }

    /// <summary>Mirrors the production commit seam: only a successful guarded CAS save notifies the
    /// retained Home-owned selection source. A refusal/pending/CAS-conflict/cancellation must never
    /// fire the notification.</summary>
    private sealed class NotifyingOriginalActorProvider(IHomeModelPickerOriginalActorFeatureProvider inner, Action onSuccess)
        : IHomeModelPickerOriginalActorFeatureProvider
    {
        public Task<HomeCoreOperationResult<HomeModelCataloguePage>> GetCatalogueAsync(string? query = null, CancellationToken ct = default) => inner.GetCatalogueAsync(query, ct);
        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> GetSnapshotAsync(string scope, string category, CancellationToken ct = default) => inner.GetSnapshotAsync(scope, category, ct);
        public async Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> UpdateRouteAsync(HomeModelRouteEdit edit, CancellationToken ct = default)
        {
            var result = await inner.UpdateRouteAsync(edit, ct);
            if (result.Succeeded) onSuccess();
            return result;
        }
        public async Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> UpdateRouteForActorAsync(AuthenticatedResourceActor expectedActor, HomeModelRouteEdit edit, CancellationToken ct = default)
        {
            var result = await inner.UpdateRouteForActorAsync(expectedActor, edit, ct);
            if (result.Succeeded) onSuccess();
            return result;
        }
        public Task<HomeCoreOperationResult<HomeModelRoutePreview>> PreviewResolutionAsync(HomeModelRoutePreviewRequest request, CancellationToken ct = default) => inner.PreviewResolutionAsync(request, ct);
        public Task<HomeCoreOperationResult<object>> RetryAuditAsync(string requestId, CancellationToken ct = default) => inner.RetryAuditAsync(requestId, ct);
        public Task<HomePermissionAuthorization?> GetApprovalAuthorizationAsync(string requestId, CancellationToken ct = default) => inner.GetApprovalAuthorizationAsync(requestId, ct);
    }

    private sealed class TestPrivacyPreferenceStore : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current { get; private set; } = PrivacyPreferences.Default;
        public Task UpdateAsync(PrivacyPreferences value, CancellationToken token) { Current = value; return Task.CompletedTask; }
    }

    private sealed class FakeProvider(string id, bool isLocal, params string[] modelNames) : IModelProvider
    {
        private readonly List<ProviderModelDescriptor> _models = modelNames
            .Select(name => new ProviderModelDescriptor(id, isLocal,
                new ModelDescriptor(name, 123, "synthetic", "7B", "Q8",
                    new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch)))
            .ToList();

        public int InvokedCount { get; private set; }

        public string Id => id;
        public string DisplayName => id;
        public bool IsLocal => isLocal;
        public bool CanManageModels => false;
        public ModelProviderKind Kind => isLocal ? ModelProviderKind.Ollama : ModelProviderKind.OpenAI;

        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>(_models);
        }

        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) =>
            Task.FromResult(new ProviderHealthStatus(id, true, "synthetic", TimeSpan.Zero, DateTimeOffset.UnixEpoch));

        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) { InvokedCount++; throw new NotSupportedException(); }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) { InvokedCount++; throw new NotSupportedException(); }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) { InvokedCount++; throw new NotSupportedException(); }
    }
}
