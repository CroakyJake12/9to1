using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dulche.Runtime;
using Haven.Application;
using Haven.Application.Haven;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HavenOS.Home.PermissionsTrustNotifications;
using NineToOne.Cui.AI;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

/// <summary>
/// Focused producer evidence for the ACTUAL <see cref="ChatSessionService"/>: it reads the selected
/// reasoning request from the SAME Home-scoped typed owner (<see cref="HomeChatReasoningSelectionReader"/>
/// over <see cref="HomeModelSelectionSource"/>) and carries the exact advertised ID plus its genuine
/// bound provider/model identity on the real plain and tool requests. Nothing is set mechanically on
/// the request: the test dispatch supplies neither <c>ReasoningId</c> nor <c>GenerationSettings</c>;
/// the producer resolves the scoped Home fixture itself and the binding is carried verbatim.
///
/// The target model is matched by its exact full registered name (native "qwen:tag" is never split
/// into a fake provider/model). Wrong provider/model, an incoherent scope, or an absent selection omits
/// (ordinary generation); a stale/unsynchronized selection raises typed Unsupported BEFORE any provider
/// invocation.
/// </summary>
public sealed class ChatSessionSelectedReasoningProducerTests
{
    private const string NativeModelName = "qwen:tag";

    private static AuthenticatedResourceActor Actor => new("actor-1", "p1", null, null, "rev-9");
    private static AuthenticatedResourceActor ChangedActor => new("actor-1", "p1", null, null, "rev-10");

    private static string StableKey(string providerId, string modelId) => new ModelIdentity(providerId, modelId).StableKey;

    private static ConfiguredModelRoute Route(ModelRouteScope scope, params ModelRouteCandidate[] candidates) =>
        new("chat::p1", 1, scope, "p1", ModelCapabilityCategory.Chat, candidates, new ProviderPolicy());

    private static HomeModelSelectionSource MakeSource(
        IModelProviderRegistry registry,
        SelectedReasoningState? selection,
        Func<string, string, CancellationToken, Task<IReadOnlyList<string>?>>? advertised,
        ModelRouteScope scope = ModelRouteScope.User,
        Func<CancellationToken, Task<AuthenticatedResourceActor?>>? actorSource = null,
        Func<ModelCapabilityCategory, CancellationToken, Task<ConfiguredModelRoute?>>? routeReader = null)
    {
        var route = Route(scope, new ModelRouteCandidate(new ModelIdentity("ollama", NativeModelName), Enabled: true, Order: 0));
        var store = new FileHomeCoreStateStore(
            Path.Combine(Path.GetTempPath(), "home-sel-" + Guid.NewGuid().ToString("N") + ".json"));
        return new HomeModelSelectionSource(
            registry,
            new HomeVersionedModelRouteRepository(store),
            routeReader ?? ((category, ct) => Task.FromResult(category == ModelCapabilityCategory.Chat ? route : (ConfiguredModelRoute?)null)),
            actorSource ?? (_ => Task.FromResult<AuthenticatedResourceActor?>(Actor)),
            advertised, selection);
    }

    private static async Task<List<ChatStreamEvent>> SendAsync(
        ChatSessionService service, Conversation conversation, ModelDescriptor model, string? root,
        IReadOnlyCollection<ToolCapability>? explicitCapabilities = null)
    {
        var events = new List<ChatStreamEvent>();
        await foreach (var item in service.SendAsync(
            conversation, "hello", model, EffortLevel.Medium, [], "Default", "",
            DuoMode.Solo, root, "", "", null, CancellationToken.None,
            explicitCapabilities: explicitCapabilities))
            events.Add(item);
        return events;
    }

    private static ModelDescriptor TextModel() => new(NativeModelName, 1, "test", "test", "test",
        new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow);

    private static ModelDescriptor ToolModel() => new(NativeModelName, 1, "test", "test", "test",
        new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, DateTimeOffset.UtcNow);

    private static Func<string, string, CancellationToken, Task<IReadOnlyList<string>?>> Advertised() =>
        (_, modelId, ct) => Task.FromResult<IReadOnlyList<string>?>(["low", "medium", "high"]);

    /// <summary>Persists a genuine Chat personal route through the REAL guarded CAS route-edit owner
    /// (FileHomeCoreStateStore + HomeVersionedModelRouteRepository.TrySaveGuardedAsync) carrying a
    /// candidate-bound reasoning level, then builds a freshly-recreated selection source over the same
    /// store. The source restores that saved default without any in-memory commit.</summary>
    private static async Task<HomeModelSelectionSource> MakePersistedSource(IModelProviderRegistry registry, string reasoningLevelId)
    {
        var store = new FileHomeCoreStateStore(Path.Combine(Path.GetTempPath(),
            "home-sel-" + Guid.NewGuid().ToString("N") + ".json"));
        var repo = new HomeVersionedModelRouteRepository(store);
        var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
        var actor = await profiles.GetCurrentAsync(CancellationToken.None) ?? throw new InvalidOperationException("Actual Home profile unavailable.");
        var routeId = HomeModelPickerFeatureProvider.RouteId(actor.ProfileId, ModelCapabilityCategory.Chat);
        var route = new ConfiguredModelRoute(routeId, 1, ModelRouteScope.User, actor.ProfileId, ModelCapabilityCategory.Chat,
            [new ModelRouteCandidate(new ModelIdentity("ollama", NativeModelName), Enabled: true, Order: 0, ReasoningLevelId: reasoningLevelId)],
            new ProviderPolicy());
        await repo.TrySaveGuardedAsync(route, 0, actor, profiles, CancellationToken.None);
        // Reload the genuine persisted record so the freshly-recreated source reads the actual stored
        // candidate default (never a local constant route).
        return new HomeModelSelectionSource(registry, repo,
            async (cat, ct) => cat == ModelCapabilityCategory.Chat ? await repo.GetAsync(routeId, ct) : null,
            _ => Task.FromResult<AuthenticatedResourceActor?>(actor),
            Advertised());
    }

    [Fact]
    public async Task Actual_ChatSessionService_produces_committed_selection_on_plain_request_native_qwen_tag()
    {
        var provider = new FakeProvider("ollama", true, NativeModelName);
        var registry = new ModelProviderRegistry([provider]);
        var source = MakeSource(registry, null, Advertised());
        var commit = await source.CommitSelectionAsync(Actor, NativeModelName, StableKey("ollama", NativeModelName), "User:p1", "medium", CancellationToken.None);
        Assert.True(commit.Succeeded);
        var reader = new HomeChatReasoningSelectionReader(source);

        var client = new CapturingOllama(TextModel());
        var service = BuildService(client, reader);
        var conversation = NewConversation(HavenMode.Chat);

        var events = await SendAsync(service, conversation, TextModel(), root: null);

        // The scoped Home fixture was read and its exact advertised ID produced on the real request,
        // bound to the genuine provider/model identity (native qwen:tag preserved verbatim).
        Assert.NotNull(client.LastChatRequest);
        Assert.Equal("medium", client.LastChatRequest!.ReasoningId);
        Assert.Equal("ollama", client.LastChatRequest.ReasoningTargetProviderId);
        Assert.Equal(NativeModelName, client.LastChatRequest.ReasoningTargetModelName);
        Assert.Equal(NativeModelName, client.LastChatRequest.Model);
        Assert.Contains(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
    }

    [Fact]
    public async Task Actual_ChatSessionService_produces_committed_selection_on_tool_request_tasks_surface()
    {
        var provider = new FakeProvider("ollama", true, NativeModelName);
        var registry = new ModelProviderRegistry([provider]);
        // Genuine personal route is User-scoped; the advisory Tasks mode text never becomes a canonical
        // scope (production HomePersonalModelRoutes admits User routes).
        var source = MakeSource(registry, null, Advertised(), scope: ModelRouteScope.User);
        var commit = await source.CommitSelectionAsync(Actor, NativeModelName, StableKey("ollama", NativeModelName), "User:p1", "high", CancellationToken.None);
        Assert.True(commit.Succeeded);
        var reader = new HomeChatReasoningSelectionReader(source);

        var client = new CapturingOllama(ToolModel());
        var service = BuildService(client, reader);
        var root = Path.Combine(Path.GetTempPath(), "haven-sel-tool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var conversation = NewConversation(HavenMode.Tasks);

        var events = await SendAsync(service, conversation, ToolModel(), root,
            explicitCapabilities: new[] { ToolCapability.Tools });

        // Tool turn ran and carried the exact committed ID on the real tool request, bound to the
        // genuine provider/model identity.
        Assert.NotNull(client.LastToolRequest);
        Assert.Equal("high", client.LastToolRequest!.ReasoningId);
        Assert.Equal("ollama", client.LastToolRequest.ReasoningTargetProviderId);
        Assert.Equal(NativeModelName, client.LastToolRequest.ReasoningTargetModelName);
        Assert.True(client.ToolCalls >= 1);
        Assert.Contains(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
    }

    [Fact]
    public async Task Actual_approved_review_commit_journey_persists_then_produces_selection_on_plain_and_tool_requests()
    {
        // Real file store/profile/resources/permission service/broker, with BOTH Home route owners.
        // No stub provider success and no direct repository seeding stands in for this journey.
        var store = new FileHomeCoreStateStore(Path.Combine(Path.GetTempPath(),
            "home-appr-" + Guid.NewGuid().ToString("N") + ".json"));
        var repo = new HomeVersionedModelRouteRepository(store);
        var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
        var actor = await profiles.GetCurrentAsync(CancellationToken.None) ?? throw new InvalidOperationException("Actual Home profile unavailable.");
        var permissions = new HomePermissionTrustService(store, new HomeModelRouteActionPolicies().TryGet);
        var resources = new ResourceAuthorizationService(profiles,
            [new HomeModelRouteProfileOwner(profiles), new HomeModelRouteOwner(profiles, repo)]);
        var broker = new HomeResourceOperationBroker(resources, permissions);
        var provider = new FakeProvider("ollama", true, NativeModelName);
        var registry = new ModelProviderRegistry([provider]);
        var routeProvider = new HomeModelPickerFeatureProvider(profiles, repo, registry,
            new TestPrivacyPreferenceStore(), resources, broker);

        var routeId = HomeModelPickerFeatureProvider.RouteId(actor.ProfileId, ModelCapabilityCategory.Chat);
        var route = new ConfiguredModelRoute(routeId, 1, ModelRouteScope.User, actor.ProfileId, ModelCapabilityCategory.Chat,
            [new ModelRouteCandidate(new ModelIdentity("ollama", NativeModelName), Enabled: true, Order: 0, ReasoningLevelId: null)],
            new ProviderPolicy());
        Assert.True(await repo.TrySaveGuardedAsync(route, 0, actor, profiles, CancellationToken.None));

        Func<ModelCapabilityCategory, CancellationToken, Task<ConfiguredModelRoute?>> reader =
            async (cat, ct) => cat == ModelCapabilityCategory.Chat ? await repo.GetAsync(routeId, ct) : null;

        var source = new HomeModelSelectionSource(registry, repo, reader,
            _ => Task.FromResult<AuthenticatedResourceActor?>(actor),
            Advertised(), selection: null, routeProvider: routeProvider);

        var changed = 0;
        source.SelectionChanged += (_, _) => changed++;
        var committed = 0;
        source.RouteChanged += (_, _) => committed++;

        // 1) First commit is a REAL authorization -> pending approval; no success/publication.
        var surfaceScope = "User:" + actor.ProfileId;
        var first = await source.CommitSelectionAsync(actor, NativeModelName, StableKey("ollama", NativeModelName), surfaceScope, "medium", CancellationToken.None);
        Assert.False(first.Succeeded);
        var requestId = source.GetPendingApprovalRequestId(actor.ProfileId, StableKey("ollama", NativeModelName), surfaceScope);
        Assert.False(string.IsNullOrWhiteSpace(requestId));
        Assert.Equal(0, committed);
        Assert.Equal(0, changed);
        Assert.False(source.Selection.TryGet(new SelectionKey(actor.ProfileId, StableKey("ollama", NativeModelName), surfaceScope), out _));

        // 2) Genuine Home decision on the SAME review.
        var decided = await permissions.DecideAsync(requestId!, HomeApprovalChoice.Accept, cancellationToken: CancellationToken.None);
        Assert.True(decided.Succeeded);

        // 3) Second commit resumes the SAME payload -> claim/CAS -> committed success.
        var second = await source.CommitSelectionAsync(actor, NativeModelName, StableKey("ollama", NativeModelName), surfaceScope, "medium", CancellationToken.None);
        Assert.True(second.Succeeded);
        Assert.Equal("medium", second.Value);
        Assert.Equal(1, committed);      // one canonical commit published
        Assert.Equal(1, changed);        // memory selected set exactly once
        Assert.Null(source.GetPendingApprovalRequestId(actor.ProfileId, StableKey("ollama", NativeModelName), surfaceScope));

        // 4) Source recreation over the same real store reads the exact committed default.
        var recreated = new HomeModelSelectionSource(registry, repo, reader,
            _ => Task.FromResult<AuthenticatedResourceActor?>(actor),
            Advertised(), routeProvider: routeProvider);
        var snapshot = await recreated.ResolveAsync(1, CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal("medium", snapshot!.SelectedReasoningId);

        // 5) Real ChatSession plain and tool requests carry the exact committed level.
        var chatReader = new HomeChatReasoningSelectionReader(recreated);
        var plainClient = new CapturingOllama(TextModel());
        var chatService = BuildService(plainClient, chatReader);
        await SendAsync(chatService, NewConversation(HavenMode.Chat), TextModel(), root: null);
        Assert.NotNull(plainClient.LastChatRequest);
        Assert.Equal("medium", plainClient.LastChatRequest!.ReasoningId);
        Assert.Equal("ollama", plainClient.LastChatRequest.ReasoningTargetProviderId);
        Assert.Equal(NativeModelName, plainClient.LastChatRequest.ReasoningTargetModelName);

        var toolRoot = Path.Combine(Path.GetTempPath(), "haven-sel-appr-tool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(toolRoot);
        var toolClient = new CapturingOllama(ToolModel());
        var toolService = BuildService(toolClient, chatReader);
        await SendAsync(toolService, NewConversation(HavenMode.Tasks), ToolModel(), toolRoot,
            explicitCapabilities: new[] { ToolCapability.Tools });
        Assert.NotNull(toolClient.LastToolRequest);
        Assert.Equal("medium", toolClient.LastToolRequest!.ReasoningId);
        Assert.Equal("ollama", toolClient.LastToolRequest.ReasoningTargetProviderId);
        Assert.Equal(NativeModelName, toolClient.LastToolRequest.ReasoningTargetModelName);
    }

    [Fact]
    public async Task Wrong_dispatch_model_omits_stale_selection_and_preserves_ordinary_generation()
    {
        var provider = new FakeProvider("ollama", true, NativeModelName);
        var registry = new ModelProviderRegistry([provider]);
        var source = MakeSource(registry, null, Advertised());
        var commit = await source.CommitSelectionAsync(Actor, NativeModelName, StableKey("ollama", NativeModelName), "User:p1", "medium", CancellationToken.None);
        Assert.True(commit.Succeeded);
        var reader = new HomeChatReasoningSelectionReader(source);

        // Dispatch target ("other") does not match the projected model (native qwen:tag) -> omit.
        var other = new ModelDescriptor("other", 1, "test", "test", "test",
            new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow);
        var client = new CapturingOllama(other);
        var service = BuildService(client, reader);
        var conversation = NewConversation(HavenMode.Chat);

        var events = await SendAsync(service, conversation, other, root: null);

        Assert.NotNull(client.LastChatRequest);
        Assert.Null(client.LastChatRequest!.ReasoningId);
        Assert.Null(client.LastChatRequest.ReasoningTargetProviderId);
        Assert.Contains(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
    }

    [Fact]
    public async Task Mismatched_surface_scope_omits_selection_ordinary_generation()
    {
        // A Chat (User-scoped) conversation against a Task-scoped Home selection is incoherent: the
        // selection must not be applied to a different surface.
        var provider = new FakeProvider("ollama", true, NativeModelName);
        var registry = new ModelProviderRegistry([provider]);
        var source = MakeSource(registry, null, Advertised(), scope: ModelRouteScope.Task);
        var commit = await source.CommitSelectionAsync(Actor, NativeModelName, StableKey("ollama", NativeModelName), "Task:p1", "medium", CancellationToken.None);
        Assert.True(commit.Succeeded);
        var reader = new HomeChatReasoningSelectionReader(source);

        var client = new CapturingOllama(TextModel());
        var service = BuildService(client, reader);
        var conversation = NewConversation(HavenMode.Chat);

        var events = await SendAsync(service, conversation, TextModel(), root: null);

        Assert.NotNull(client.LastChatRequest);
        Assert.Null(client.LastChatRequest!.ReasoningId);
        Assert.Contains(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
    }

    [Fact]
    public async Task Absent_selection_preserves_ordinary_generation()
    {
        var provider = new FakeProvider("ollama", true, NativeModelName);
        var registry = new ModelProviderRegistry([provider]);
        var source = MakeSource(registry, null, Advertised());
        var reader = new HomeChatReasoningSelectionReader(source);

        var client = new CapturingOllama(TextModel());
        var service = BuildService(client, reader);
        var conversation = NewConversation(HavenMode.Chat);

        var events = await SendAsync(service, conversation, TextModel(), root: null);

        Assert.NotNull(client.LastChatRequest);
        Assert.Null(client.LastChatRequest!.ReasoningId);
        Assert.Contains(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
    }

    [Fact]
    public async Task Actor_change_during_metadata_await_rejects_stale_publication()
    {
        // The actor/authentication revision changes between the source's first read and its post-await
        // re-read, so the earlier model selection is stale and must be rejected (ordinary generation).
        var provider = new FakeProvider("ollama", true, NativeModelName);
        var registry = new ModelProviderRegistry([provider]);
        var selection = new SelectedReasoningState();
        // Seed through a stable real commit owner; arm the actor transition only for the read under test.
        var commitSource = MakeSource(registry, selection, Advertised());
        var commit = await commitSource.CommitSelectionAsync(Actor, NativeModelName, StableKey("ollama", NativeModelName), "User:p1", "medium", CancellationToken.None);
        Assert.True(commit.Succeeded);
        var calls = 0;
        var source = MakeSource(registry, selection, Advertised(), actorSource: _ =>
        {
            calls++;
            return Task.FromResult<AuthenticatedResourceActor?>(calls == 1 ? Actor : ChangedActor);
        });
        var reader = new HomeChatReasoningSelectionReader(source);

        var client = new CapturingOllama(TextModel());
        var service = BuildService(client, reader);
        var conversation = NewConversation(HavenMode.Chat);

        await SendAsync(service, conversation, TextModel(), root: null);

        Assert.NotNull(client.LastChatRequest);
        Assert.Null(client.LastChatRequest!.ReasoningId);
    }

    [Fact]
    public async Task Stale_selection_raises_typed_unsupported_before_any_provider_invocation()
    {
        // Simulate a canonical state that is out of sync with the model's advertised levels (the reader
        // re-checks at dispatch): selection "medium" committed, but the model's advertised levels no
        // longer list it.
        var selection = new SelectedReasoningState();
        Assert.True(selection.TrySet(new SelectionKey("p1", StableKey("ollama", NativeModelName), "User:p1"), "medium"));
        var provider = new CountingProvider("ollama", true, NativeModelName);
        var registry = new ModelProviderRegistry([provider]);
        var source = MakeSource(registry, selection, (_, modelId, ct) => Task.FromResult<IReadOnlyList<string>?>(["low", "high"]));
        var reader = new HomeChatReasoningSelectionReader(source);

        var client = new CapturingOllama(TextModel());
        var service = BuildService(client, reader);
        var conversation = NewConversation(HavenMode.Chat);

        await Assert.ThrowsAsync<UnsupportedCapabilityException>(async () =>
        {
            await foreach (var _ in service.SendAsync(
                conversation, "hello", TextModel(), EffortLevel.Medium, [], "Default", "",
                DuoMode.Solo, null, "", "", null, CancellationToken.None)) { }
        });

        // Typed refusal happened before any provider invocation.
        Assert.Equal(0, client.StreamCalls);
        Assert.Equal(0, client.ToolCalls);
        Assert.Null(client.LastChatRequest);
    }

    [Fact]
    public async Task Reader_honours_cancellation_lifetime()
    {
        var provider = new FakeProvider("ollama", true, NativeModelName);
        var registry = new ModelProviderRegistry([provider]);
        var source = MakeSource(registry, null, Advertised());
        var reader = new HomeChatReasoningSelectionReader(source);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await reader.ResolveAsync(new ChatReasoningSelectionQuery("c1", NativeModelName, "ollama", false, "User"), cts.Token));
    }

    [Fact]
    public async Task Gated_metadata_actor_switch_rejects_stale_publication()
    {
        // The actor/authentication revision flips WHILE the metadata read is pending (after the source's
        // first actor read). The post-metadata coherence recheck must reject the stale selection so no
        // stale reasoning ID ever reaches the captured provider.
        var provider = new FakeProvider("ollama", true, NativeModelName);
        var registry = new ModelProviderRegistry([provider]);
        var selection = new SelectedReasoningState();
        // Commit on a non-gated source sharing the same canonical selection state (the gate below is
        // installed only on the resolve path so the commit itself does not block).
        var commitSource = MakeSource(registry, selection, Advertised());
        var commit = await commitSource.CommitSelectionAsync(Actor, NativeModelName, StableKey("ollama", NativeModelName), "User:p1", "medium", CancellationToken.None);
        Assert.True(commit.Succeeded);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Func<string, string, CancellationToken, Task<IReadOnlyList<string>?>> advertised = async (_, _, ct) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return ["low", "medium", "high"];
        };
        var source = MakeSource(registry, selection, advertised, actorSource: _ =>
        {
            calls++;
            return Task.FromResult<AuthenticatedResourceActor?>(calls == 1 ? Actor : ChangedActor);
        });
        var reader = new HomeChatReasoningSelectionReader(source);

        var client = new CapturingOllama(TextModel());
        var service = BuildService(client, reader);
        var conversation = NewConversation(HavenMode.Chat);

        var send = SendAsync(service, conversation, TextModel(), root: null);
        // Wait until the reader is blocked inside the metadata read (after the first actor read).
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        release.TrySetResult(); // release metadata; the post-metadata recheck then sees the changed actor
        await send;

        Assert.NotNull(client.LastChatRequest);
        Assert.Null(client.LastChatRequest!.ReasoningId);
        Assert.Null(client.LastChatRequest.ReasoningTargetProviderId);
    }

    [Fact]
    public async Task Gated_metadata_route_switch_rejects_stale_publication()
    {
        // The genuine route revision flips WHILE the metadata read is pending. The post-metadata route
        // recheck must reject the stale selection (no stale reasoning reaches a captured provider).
        var provider = new FakeProvider("ollama", true, NativeModelName);
        var registry = new ModelProviderRegistry([provider]);
        var selection = new SelectedReasoningState();
        var commitSource = MakeSource(registry, selection, Advertised());
        var commit = await commitSource.CommitSelectionAsync(Actor, NativeModelName, StableKey("ollama", NativeModelName), "User:p1", "medium", CancellationToken.None);
        Assert.True(commit.Succeeded);

        var originalRoute = Route(ModelRouteScope.User,
            new ModelRouteCandidate(new ModelIdentity("ollama", NativeModelName), Enabled: true, Order: 0));
        var switchedRoute = new ConfiguredModelRoute("chat::p1", 2, ModelRouteScope.User, "p1",
            ModelCapabilityCategory.Chat,
            [new ModelRouteCandidate(new ModelIdentity("ollama", NativeModelName), Enabled: true, Order: 0)],
            new ProviderPolicy());
        var switchNow = false;

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<string, string, CancellationToken, Task<IReadOnlyList<string>?>> advertised = async (_, _, ct) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return ["low", "medium", "high"];
        };
        var source = MakeSource(registry, selection, advertised,
            routeReader: (category, ct) => Task.FromResult(
                category == ModelCapabilityCategory.Chat ? (switchNow ? switchedRoute : originalRoute) : (ConfiguredModelRoute?)null));
        var reader = new HomeChatReasoningSelectionReader(source);

        var client = new CapturingOllama(TextModel());
        var service = BuildService(client, reader);
        var conversation = NewConversation(HavenMode.Chat);

        var send = SendAsync(service, conversation, TextModel(), root: null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        switchNow = true; // flip the genuine route before the post-metadata re-read
        release.TrySetResult();
        await send;

        Assert.NotNull(client.LastChatRequest);
        Assert.Null(client.LastChatRequest!.ReasoningId);
        Assert.Null(client.LastChatRequest.ReasoningTargetProviderId);
    }

    [Fact]
    public async Task Integration_real_Home_producer_to_primary_NamedReasoning_dispatch_plain_chat()
    {
        // Decisive ACTUAL integration: the real HomeModelSelectionSource (genuine User:p1 route) ->
        // real HomeChatReasoningSelectionReader -> real ChatSessionService -> the primary routing client's
        // NamedReasoning dispatch. No fake reader, no manually prefilled request binding.
        var provider = new NamedReasoningProvider("ollama", true, NativeModelName);
        var registry = new ModelProviderRegistry([provider]);
        var source = MakeSource(registry, null, Advertised());
        var commit = await source.CommitSelectionAsync(Actor, NativeModelName, StableKey("ollama", NativeModelName), "User:p1", "medium", CancellationToken.None);
        Assert.True(commit.Succeeded);
        var reader = new HomeChatReasoningSelectionReader(source);

        var primary = new ProviderRoutingModelClient(new StubPrimaryOllamaClient(), registry, new StubPrimaryPrivacy());
        var service = BuildService(primary, reader);
        var conversation = NewConversation(HavenMode.Chat);

        var events = new List<ChatStreamEvent>();
        await foreach (var item in service.SendAsync(
            conversation, "hello", FullModel(), EffortLevel.Medium, [], "Default", "",
            DuoMode.Solo, null, "", "", null, CancellationToken.None))
            events.Add(item);

        // The genuine produced binding reached the actual reasoning port: exact native model identity and
        // the exact advertised ID survive verbatim on the real wire dispatch.
        Assert.True(provider.ReasoningStreamCalls >= 1);
        Assert.Equal("medium", provider.LastReasoningId);
        Assert.Equal(NativeModelName, provider.LastChatRequest?.Model);
        Assert.Equal(NativeModelName, provider.LastChatRequest?.ReasoningTargetModelName);
        Assert.Contains(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
    }

    [Fact]
    public async Task Integration_real_Home_producer_to_primary_NamedReasoning_dispatch_tool_tasks()
    {
        // Same genuine User:p1 personal route, Tasks conversation: the advisory mode text never becomes a
        // canonical scope; the selection is carried through the real tool dispatch to the reasoning port.
        var provider = new NamedReasoningProvider("ollama", true, NativeModelName);
        var registry = new ModelProviderRegistry([provider]);
        var source = MakeSource(registry, null, Advertised());
        var commit = await source.CommitSelectionAsync(Actor, NativeModelName, StableKey("ollama", NativeModelName), "User:p1", "high", CancellationToken.None);
        Assert.True(commit.Succeeded);
        var reader = new HomeChatReasoningSelectionReader(source);

        var primary = new ProviderRoutingModelClient(new StubPrimaryOllamaClient(), registry, new StubPrimaryPrivacy());
        var service = BuildService(primary, reader);
        var root = Path.Combine(Path.GetTempPath(), "haven-int-tool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var conversation = NewConversation(HavenMode.Tasks);

        var events = new List<ChatStreamEvent>();
        await foreach (var item in service.SendAsync(
            conversation, "do it", FullModel(), EffortLevel.Medium, [], "Default", "",
            DuoMode.Solo, root, "", "", null, CancellationToken.None,
            explicitCapabilities: new[] { ToolCapability.Tools }))
            events.Add(item);

        Assert.True(provider.ReasoningToolCalls >= 1);
        Assert.Equal("high", provider.LastReasoningId);
        Assert.Equal(NativeModelName, provider.LastToolRequest?.Model);
        Assert.Equal(NativeModelName, provider.LastToolRequest?.ReasoningTargetModelName);
    }

    [Fact]
    public async Task Direct_primary_refuses_wrong_binding_provider_same_model_plain()
    {
        // The primary resolves the native request to Ollama B; the explicit binding names a different
        // provider (same model). Both fields are supplied but the provider does not match the resolved
        // primary -> typed refusal BEFORE any ordinary or named invocation.
        var b = new NamedReasoningProvider("ollama", true, NativeModelName);
        var primary = new ProviderRoutingModelClient(new StubPrimaryOllamaClient(), new ModelProviderRegistry([b]), new StubPrimaryPrivacy());
        var request = new OllamaChatRequest(NativeModelName, [new OllamaMessage("user", "hi")], EffortLevel.Medium)
        {
            ReasoningId = "medium",
            ReasoningTargetProviderId = "provider-x",
            ReasoningTargetModelName = NativeModelName
        };

        await Assert.ThrowsAsync<UnsupportedCapabilityException>(async () =>
        {
            await foreach (var _ in primary.StreamChatAsync(request, CancellationToken.None)) { }
        });
        Assert.Equal(0, b.ReasoningStreamCalls);
        Assert.Equal(0, b.OrdinaryStreamCalls);
        Assert.Null(b.LastChatRequest);
    }

    [Fact]
    public async Task Direct_primary_refuses_wrong_binding_provider_same_model_tool()
    {
        var b = new NamedReasoningProvider("ollama", true, NativeModelName);
        var primary = new ProviderRoutingModelClient(new StubPrimaryOllamaClient(), new ModelProviderRegistry([b]), new StubPrimaryPrivacy());
        var request = new OllamaToolRequest(NativeModelName, [new OllamaToolTurn("user", "do it")], [], EffortLevel.Medium)
        {
            ReasoningId = "high",
            ReasoningTargetProviderId = "provider-x",
            ReasoningTargetModelName = NativeModelName
        };

        await Assert.ThrowsAsync<UnsupportedCapabilityException>(async () =>
            await primary.ChatWithToolsAsync(request, CancellationToken.None));
        Assert.Equal(0, b.ReasoningToolCalls);
        Assert.Equal(0, b.OrdinaryToolCalls);
        Assert.Null(b.LastToolRequest);
    }

    [Fact]
    public async Task Direct_primary_refuses_wrong_binding_model_same_provider_plain()
    {
        var b = new NamedReasoningProvider("ollama", true, NativeModelName);
        var primary = new ProviderRoutingModelClient(new StubPrimaryOllamaClient(), new ModelProviderRegistry([b]), new StubPrimaryPrivacy());
        var request = new OllamaChatRequest(NativeModelName, [new OllamaMessage("user", "hi")], EffortLevel.Medium)
        {
            ReasoningId = "medium",
            ReasoningTargetProviderId = "ollama",
            ReasoningTargetModelName = "different-model"
        };

        await Assert.ThrowsAsync<UnsupportedCapabilityException>(async () =>
        {
            await foreach (var _ in primary.StreamChatAsync(request, CancellationToken.None)) { }
        });
        Assert.Equal(0, b.ReasoningStreamCalls);
        Assert.Equal(0, b.OrdinaryStreamCalls);
        Assert.Null(b.LastChatRequest);
    }

    [Fact]
    public async Task Direct_primary_refuses_wrong_binding_model_same_provider_tool()
    {
        var b = new NamedReasoningProvider("ollama", true, NativeModelName);
        var primary = new ProviderRoutingModelClient(new StubPrimaryOllamaClient(), new ModelProviderRegistry([b]), new StubPrimaryPrivacy());
        var request = new OllamaToolRequest(NativeModelName, [new OllamaToolTurn("user", "do it")], [], EffortLevel.Medium)
        {
            ReasoningId = "high",
            ReasoningTargetProviderId = "ollama",
            ReasoningTargetModelName = "different-model"
        };

        await Assert.ThrowsAsync<UnsupportedCapabilityException>(async () =>
            await primary.ChatWithToolsAsync(request, CancellationToken.None));
        Assert.Equal(0, b.ReasoningToolCalls);
        Assert.Equal(0, b.OrdinaryToolCalls);
        Assert.Null(b.LastToolRequest);
    }

    [Fact]
    public async Task Direct_primary_refuses_partial_binding_provider_only_plain()
    {
        var b = new NamedReasoningProvider("ollama", true, NativeModelName);
        var primary = new ProviderRoutingModelClient(new StubPrimaryOllamaClient(), new ModelProviderRegistry([b]), new StubPrimaryPrivacy());
        // Exactly one binding field (provider) supplied -> typed refusal before invocation.
        var request = new OllamaChatRequest(NativeModelName, [new OllamaMessage("user", "hi")], EffortLevel.Medium)
        {
            ReasoningId = "medium",
            ReasoningTargetProviderId = "ollama",
            ReasoningTargetModelName = null
        };

        await Assert.ThrowsAsync<UnsupportedCapabilityException>(async () =>
        {
            await foreach (var _ in primary.StreamChatAsync(request, CancellationToken.None)) { }
        });
        Assert.Equal(0, b.ReasoningStreamCalls);
        Assert.Equal(0, b.OrdinaryStreamCalls);
        Assert.Null(b.LastChatRequest);
    }

    [Fact]
    public async Task Direct_primary_refuses_partial_binding_model_only_plain()
    {
        var b = new NamedReasoningProvider("ollama", true, NativeModelName);
        var primary = new ProviderRoutingModelClient(new StubPrimaryOllamaClient(), new ModelProviderRegistry([b]), new StubPrimaryPrivacy());
        var request = new OllamaChatRequest(NativeModelName, [new OllamaMessage("user", "hi")], EffortLevel.Medium)
        {
            ReasoningId = "medium",
            ReasoningTargetProviderId = null,
            ReasoningTargetModelName = NativeModelName
        };

        await Assert.ThrowsAsync<UnsupportedCapabilityException>(async () =>
        {
            await foreach (var _ in primary.StreamChatAsync(request, CancellationToken.None)) { }
        });
        Assert.Equal(0, b.ReasoningStreamCalls);
        Assert.Equal(0, b.OrdinaryStreamCalls);
        Assert.Null(b.LastChatRequest);
    }

    [Fact]
    public async Task Direct_primary_refuses_partial_binding_provider_only_tool()
    {
        var b = new NamedReasoningProvider("ollama", true, NativeModelName);
        var primary = new ProviderRoutingModelClient(new StubPrimaryOllamaClient(), new ModelProviderRegistry([b]), new StubPrimaryPrivacy());
        var request = new OllamaToolRequest(NativeModelName, [new OllamaToolTurn("user", "do it")], [], EffortLevel.Medium)
        {
            ReasoningId = "high",
            ReasoningTargetProviderId = "ollama",
            ReasoningTargetModelName = null
        };

        await Assert.ThrowsAsync<UnsupportedCapabilityException>(async () =>
            await primary.ChatWithToolsAsync(request, CancellationToken.None));
        Assert.Equal(0, b.ReasoningToolCalls);
        Assert.Equal(0, b.OrdinaryToolCalls);
        Assert.Null(b.LastToolRequest);
    }

    [Fact]
    public async Task Direct_primary_refuses_partial_binding_model_only_tool()
    {
        var b = new NamedReasoningProvider("ollama", true, NativeModelName);
        var primary = new ProviderRoutingModelClient(new StubPrimaryOllamaClient(), new ModelProviderRegistry([b]), new StubPrimaryPrivacy());
        var request = new OllamaToolRequest(NativeModelName, [new OllamaToolTurn("user", "do it")], [], EffortLevel.Medium)
        {
            ReasoningId = "high",
            ReasoningTargetProviderId = null,
            ReasoningTargetModelName = NativeModelName
        };

        await Assert.ThrowsAsync<UnsupportedCapabilityException>(async () =>
            await primary.ChatWithToolsAsync(request, CancellationToken.None));
        Assert.Equal(0, b.ReasoningToolCalls);
        Assert.Equal(0, b.OrdinaryToolCalls);
        Assert.Null(b.LastToolRequest);
    }

    [Fact]
    public async Task Direct_primary_matching_binding_proceeds_to_named_port_plain()
    {
        // Both supplied binding fields exactly match the resolved primary (Ollama B native qwen:tag)
        // -> the named reasoning port is entered, never a refusal.
        var b = new NamedReasoningProvider("ollama", true, NativeModelName);
        var primary = new ProviderRoutingModelClient(new StubPrimaryOllamaClient(), new ModelProviderRegistry([b]), new StubPrimaryPrivacy());
        var request = new OllamaChatRequest(NativeModelName, [new OllamaMessage("user", "hi")], EffortLevel.Medium)
        {
            ReasoningId = "medium",
            ReasoningTargetProviderId = "ollama",
            ReasoningTargetModelName = NativeModelName
        };

        var chunks = new List<string>();
        await foreach (var chunk in primary.StreamChatAsync(request, CancellationToken.None)) chunks.Add(chunk);
        Assert.Equal(1, b.ReasoningStreamCalls);
        Assert.Equal(0, b.OrdinaryStreamCalls);
        Assert.Equal("medium", b.LastReasoningId);
        Assert.Equal(NativeModelName, b.LastChatRequest?.Model);
        Assert.Equal(NativeModelName, b.LastChatRequest?.ReasoningTargetModelName);
    }

    [Fact]
    public async Task Direct_primary_absent_reasoning_id_keeps_ordinary_behavior()
    {
        var b = new NamedReasoningProvider("ollama", true, NativeModelName);
        var primary = new ProviderRoutingModelClient(new StubPrimaryOllamaClient(), new ModelProviderRegistry([b]), new StubPrimaryPrivacy());
        var request = new OllamaChatRequest(NativeModelName, [new OllamaMessage("user", "hi")], EffortLevel.Medium);
        var chunks = new List<string>();
        await foreach (var chunk in primary.StreamChatAsync(request, CancellationToken.None)) chunks.Add(chunk);
        Assert.Equal(1, b.OrdinaryStreamCalls);
        Assert.Equal(0, b.ReasoningStreamCalls);
        Assert.Null(b.LastReasoningId);
    }

    [Fact]
    public async Task Integration_Home_binding_A_refused_when_primary_resolves_to_B_plain_chat()
    {
        // Real Home pipeline: HomeModelSelectionSource -> HomeChatReasoningSelectionReader ->
        // ChatSessionService produces A's qwen:tag binding while the actual primary resolves the native
        // request to Ollama B. No fake reader, no manually prefilled request binding. The real binding
        // refusal must occur with ZERO ordinary AND named B calls.
        var a = new NamedReasoningProvider("provider-a", true, NativeModelName);
        var homeRegistry = new ModelProviderRegistry([a]);
        var routeA = Route(ModelRouteScope.User,
            new ModelRouteCandidate(new ModelIdentity("provider-a", NativeModelName), Enabled: true, Order: 0));
        var source = new HomeModelSelectionSource(
            homeRegistry,
            new HomeVersionedModelRouteRepository(new FileHomeCoreStateStore(Path.Combine(Path.GetTempPath(),
                "home-prim-" + Guid.NewGuid().ToString("N") + ".json"))),
            (_, ct) => Task.FromResult<ConfiguredModelRoute?>(routeA),
            _ => Task.FromResult<AuthenticatedResourceActor?>(Actor),
            Advertised());
        var commit = await source.CommitSelectionAsync(Actor, NativeModelName, StableKey("provider-a", NativeModelName), "User:p1", "medium", CancellationToken.None);
        Assert.True(commit.Succeeded);
        var reader = new HomeChatReasoningSelectionReader(source);

        var b = new NamedReasoningProvider("ollama", true, NativeModelName);
        var primary = new ProviderRoutingModelClient(new StubPrimaryOllamaClient(), new ModelProviderRegistry([b]), new StubPrimaryPrivacy());
        var service = BuildService(primary, reader);
        var conversation = NewConversation(HavenMode.Chat);

        await Assert.ThrowsAsync<UnsupportedCapabilityException>(async () =>
        {
            await foreach (var _ in service.SendAsync(
                conversation, "hello", FullModel(), EffortLevel.Medium, [], "Default", "",
                DuoMode.Solo, null, "", "", null, CancellationToken.None)) { }
        });

        Assert.Equal(0, b.ReasoningStreamCalls);
        Assert.Equal(0, b.OrdinaryStreamCalls);
        Assert.Null(b.LastChatRequest);
    }

    [Fact]
    public async Task Integration_Home_binding_A_refused_when_primary_resolves_to_B_tool_tasks()
    {
        // Same genuine User:p1 personal route on Tasks: A's binding is produced but the primary resolves
        // the native request to Ollama B -> real binding refusal with zero ordinary/named B tool calls.
        var a = new NamedReasoningProvider("provider-a", true, NativeModelName);
        var homeRegistry = new ModelProviderRegistry([a]);
        var routeA = Route(ModelRouteScope.User,
            new ModelRouteCandidate(new ModelIdentity("provider-a", NativeModelName), Enabled: true, Order: 0));
        var source = new HomeModelSelectionSource(
            homeRegistry,
            new HomeVersionedModelRouteRepository(new FileHomeCoreStateStore(Path.Combine(Path.GetTempPath(),
                "home-prim-tool-" + Guid.NewGuid().ToString("N") + ".json"))),
            (_, ct) => Task.FromResult<ConfiguredModelRoute?>(routeA),
            _ => Task.FromResult<AuthenticatedResourceActor?>(Actor),
            Advertised());
        var commit = await source.CommitSelectionAsync(Actor, NativeModelName, StableKey("provider-a", NativeModelName), "User:p1", "high", CancellationToken.None);
        Assert.True(commit.Succeeded);
        var reader = new HomeChatReasoningSelectionReader(source);

        var b = new NamedReasoningProvider("ollama", true, NativeModelName);
        var primary = new ProviderRoutingModelClient(new StubPrimaryOllamaClient(), new ModelProviderRegistry([b]), new StubPrimaryPrivacy());
        var service = BuildService(primary, reader);
        var root = Path.Combine(Path.GetTempPath(), "haven-prim-tool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var conversation = NewConversation(HavenMode.Tasks);

        await Assert.ThrowsAsync<UnsupportedCapabilityException>(async () =>
        {
            await foreach (var _ in service.SendAsync(
                conversation, "do it", FullModel(), EffortLevel.Medium, [], "Default", "",
                DuoMode.Solo, root, "", "", null, CancellationToken.None,
                explicitCapabilities: new[] { ToolCapability.Tools })) { }
        });

        Assert.Equal(0, b.ReasoningToolCalls);
        Assert.Equal(0, b.OrdinaryToolCalls);
        Assert.Null(b.LastToolRequest);
    }

    [Fact]
    public async Task Actual_ChatSessionService_produces_persisted_saved_default_on_plain_request()
    {
        // The saved personal route default (candidate-bound exact advertised ID persisted through the
        // genuine guarded CAS route-edit store) is restored by the source and produced by the ACTUAL
        // ChatSessionService on a real plain request — without any in-memory commit step.
        var provider = new FakeProvider("ollama", true, NativeModelName);
        var registry = new ModelProviderRegistry([provider]);
        var source = await MakePersistedSource(registry, "medium");
        var reader = new HomeChatReasoningSelectionReader(source);

        var client = new CapturingOllama(TextModel());
        var service = BuildService(client, reader);
        var conversation = NewConversation(HavenMode.Chat);

        var events = await SendAsync(service, conversation, TextModel(), root: null);

        Assert.NotNull(client.LastChatRequest);
        Assert.Equal("medium", client.LastChatRequest!.ReasoningId);
        Assert.Equal("ollama", client.LastChatRequest.ReasoningTargetProviderId);
        Assert.Equal(NativeModelName, client.LastChatRequest.ReasoningTargetModelName);
        Assert.Contains(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
    }

    [Fact]
    public async Task Actual_ChatSessionService_produces_persisted_saved_default_on_tool_request()
    {
        // Same genuine saved route default produced on a REAL tool request.
        var provider = new FakeProvider("ollama", true, NativeModelName);
        var registry = new ModelProviderRegistry([provider]);
        var source = await MakePersistedSource(registry, "high");
        var reader = new HomeChatReasoningSelectionReader(source);

        var client = new CapturingOllama(ToolModel());
        var service = BuildService(client, reader);
        var root = Path.Combine(Path.GetTempPath(), "haven-sel-tool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var conversation = NewConversation(HavenMode.Tasks);

        var events = await SendAsync(service, conversation, ToolModel(), root,
            explicitCapabilities: new[] { ToolCapability.Tools });

        Assert.NotNull(client.LastToolRequest);
        Assert.Equal("high", client.LastToolRequest!.ReasoningId);
        Assert.Equal("ollama", client.LastToolRequest.ReasoningTargetProviderId);
        Assert.Equal(NativeModelName, client.LastToolRequest.ReasoningTargetModelName);
        Assert.True(client.ToolCalls >= 1);
        Assert.Contains(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
    }

    private static ModelDescriptor FullModel() => new(NativeModelName, 1, "test", "test", "test",
        new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Streaming, ToolCapability.Tools },
        DateTimeOffset.UtcNow);

    private static Conversation NewConversation(HavenMode mode)
    {
        var now = DateTimeOffset.UtcNow;
        return new Conversation(Guid.NewGuid(), mode, ConversationKind.Chat, "Test", null, null, false, true, now, now);
    }

    private static ChatSessionService BuildService(IOllamaClient client, IChatReasoningSelectionReader reader) =>
        new(new FakeConversations(), client, new CapabilityPreflightService(), new PermitSafety(),
            new WorkspaceToolRuntime(new TestWorkspaceTools()), new ComputerToolRuntime(new TestComputerTools()),
            reasoningSelection: reader);

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

        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class CountingProvider(string id, bool isLocal, string modelName) : IModelProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public bool IsLocal => isLocal;
        public bool CanManageModels => false;
        public ModelProviderKind Kind => isLocal ? ModelProviderKind.Ollama : ModelProviderKind.OpenAI;
        public ProviderModelDescriptor Model { get; } = new(id, isLocal, new ModelDescriptor(
            modelName, 123, "synthetic", "7B", "Q8",
            new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch));

        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([Model]); }

        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) =>
            Task.FromResult(new ProviderHealthStatus(id, true, "synthetic", TimeSpan.Zero, DateTimeOffset.UnixEpoch));

        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new NotSupportedException();
    }

    /// <summary>Captures the actual plain/tool provider calls; never sets ReasoningId or the binding target
    /// itself — it records the produced request exactly as it left ChatSessionService.</summary>
    private sealed class CapturingOllama(ModelDescriptor model) : IOllamaClient
    {
        public OllamaChatRequest? LastChatRequest { get; private set; }
        public OllamaToolRequest? LastToolRequest { get; private set; }
        public int StreamCalls { get; private set; }
        public int ToolCalls { get; private set; }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([model]);

        public async IAsyncEnumerable<string> StreamChatAsync(
            OllamaChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastChatRequest = request;
            StreamCalls++;
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return "ok";
        }

        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) => Task.FromResult("ok");

        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken)
        {
            LastToolRequest = request;
            ToolCalls++;
            return Task.FromResult(new OllamaToolResponse("done", []));
        }
    }

    private sealed class FakeConversations : IConversationRepository
    {
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Conversation>>([]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Conversation?>(null);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class PermitSafety : IConversationSafetyService
    {
        public Task<ConversationSafetySnapshot> GetSnapshotAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult(new ConversationSafetySnapshot(conversationId, 0, ConversationSafetyState.Active, null, 0));
        public Task<ConversationSafetyFlagResult> RecordConfirmedFlagAsync(Guid conversationId, ConfirmedSafetyFlag flag, CancellationToken cancellationToken) =>
            Task.FromResult(new ConversationSafetyFlagResult(true, false, new ConversationSafetySnapshot(conversationId, 1, ConversationSafetyState.Active, null, 1)));
        public Task EnsureMayActAsync(Guid conversationId, string operation, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestWorkspaceTools : IWorkspaceToolService
    {
        public string ResolveWorkspacePath(string workspaceRoot, string relativePath)
        {
            var root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var result = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!result.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("outside workspace");
            return result;
        }

        public Task<string> ReadTextAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken) =>
            File.ReadAllTextAsync(ResolveWorkspacePath(workspaceRoot, relativePath), cancellationToken);

        public async Task WriteTextAtomicAsync(string workspaceRoot, string relativePath, string content, CancellationToken cancellationToken)
        {
            var path = ResolveWorkspacePath(workspaceRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content, cancellationToken);
        }

        public Task<IReadOnlyList<string>> SearchFilesAsync(string workspaceRoot, string searchPattern, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class TestComputerTools : IComputerToolService
    {
        public bool IsSupported => true;
        public ValueTask<bool> VerifyTargetAsync(string canonicalAppId, string toolName, JsonElement arguments, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(false); }
        public Task<string> SnapshotAsync(CancellationToken cancellationToken) => Task.FromResult("snapshot");
        public Task<string> ListWindowsAsync(CancellationToken cancellationToken) => Task.FromResult("[]");
        public Task<string> LaunchAppAsync(string name, CancellationToken cancellationToken) => Task.FromResult($"opened {name}");
        public Task<string> FocusWindowAsync(string title, CancellationToken cancellationToken) => Task.FromResult($"focused {title}");
        public Task<string> InvokeAsync(string windowTitle, string name, string automationId, CancellationToken cancellationToken) => Task.FromResult("invoked");
        public Task<string> ClickAsync(string windowTitle, int x, int y, string button, CancellationToken cancellationToken) => Task.FromResult("clicked");
        public Task<string> TypeAsync(string windowTitle, string text, CancellationToken cancellationToken) => Task.FromResult("typed");
        public Task<string> PressAsync(string windowTitle, string keys, CancellationToken cancellationToken) => Task.FromResult("pressed");
        public Task<string> CloseWindowAsync(string title, CancellationToken cancellationToken) => Task.FromResult("closed");
    }

    /// <summary>Real IModelProvider + INamedReasoningProvider double exposing the exact native
    /// "qwen:tag" model identity. It records reasoning calls so the integration gate can prove the
    /// actual dispatch reached the reasoning port with the native model name and exact ID intact.</summary>
    private sealed class NamedReasoningProvider(string id, bool isLocal, string modelName)
        : IModelProvider, INamedReasoningProvider
    {
        public int OrdinaryStreamCalls { get; private set; }
        public int ReasoningStreamCalls { get; private set; }
        public int OrdinaryToolCalls { get; private set; }
        public int ReasoningToolCalls { get; private set; }
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
            new HashSet<ToolCapability>
            {
                ToolCapability.Text, ToolCapability.Streaming, ToolCapability.Tools
            }, DateTimeOffset.UnixEpoch));

        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([Model]); }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) =>
            Task.FromResult(new ProviderHealthStatus(id, true, "synthetic", TimeSpan.Zero, DateTimeOffset.UnixEpoch));

        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken token)
        {
            OrdinaryStreamCalls++;
            LastChatRequest = request;
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            yield return "ok";
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => Task.FromResult("ok");
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token)
        {
            OrdinaryToolCalls++;
            LastToolRequest = request;
            return Task.FromResult(new OllamaToolResponse("ok", []));
        }

        public async IAsyncEnumerable<string> StreamChatWithReasoningAsync(
            OllamaChatRequest request, string reasoningId, [EnumeratorCancellation] CancellationToken token)
        {
            ReasoningStreamCalls++;
            LastChatRequest = request;
            LastReasoningId = reasoningId;
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            yield return "ok";
        }
        public Task<OllamaToolResponse> ChatWithToolsWithReasoningAsync(OllamaToolRequest request, string reasoningId, CancellationToken token)
        {
            ReasoningToolCalls++;
            LastToolRequest = request;
            LastReasoningId = reasoningId;
            return Task.FromResult(new OllamaToolResponse("ok", []));
        }
    }

    private sealed class StubPrimaryOllamaClient : IOllamaClient
    {
        public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class StubPrimaryPrivacy : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current { get; private set; } = PrivacyPreferences.Default;
        public Task UpdateAsync(PrivacyPreferences value, CancellationToken token)
        { Current = value; return Task.CompletedTask; }
    }

    private sealed class RealRig
    {
        public required FileHomeCoreStateStore Store { get; init; }
        public required HomeVersionedModelRouteRepository Repo { get; init; }
        public required HomeLocalProfileIdentity Profiles { get; init; }
        public required AuthenticatedResourceActor Actor { get; init; }
        public required HomePermissionTrustService Permissions { get; init; }
        public required ResourceAuthorizationService Resources { get; init; }
        public required HomeResourceOperationBroker Broker { get; init; }
        public required HomeModelPickerFeatureProvider RouteProvider { get; init; }
        public required ModelProviderRegistry Registry { get; init; }
        public required string RouteId { get; init; }
        public required Func<ModelCapabilityCategory, CancellationToken, Task<ConfiguredModelRoute?>> Reader { get; init; }
    }

    /// <summary>Builds the GENUINE Home backend (file store/profile/permissions/resources/broker with
    /// BOTH route owners) and seeds the real Chat personal route. No StubRouteProvider and no direct
    /// repository commit stand in for the broker/approval/audit/guarded-CAS owner.</summary>
    private static async Task<RealRig> BuildRealRigAsync(string tag)
    {
        var store = new FileHomeCoreStateStore(Path.Combine(Path.GetTempPath(),
            $"home-appr-{tag}-" + Guid.NewGuid().ToString("N") + ".json"));
        var repo = new HomeVersionedModelRouteRepository(store);
        var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
        var actor = await profiles.GetCurrentAsync(CancellationToken.None) ?? throw new InvalidOperationException("Actual Home profile unavailable.");
        var permissions = new HomePermissionTrustService(store, new HomeModelRouteActionPolicies().TryGet);
        var resources = new ResourceAuthorizationService(profiles,
            [new HomeModelRouteProfileOwner(profiles), new HomeModelRouteOwner(profiles, repo)]);
        var broker = new HomeResourceOperationBroker(resources, permissions);
        var provider = new FakeProvider("ollama", true, NativeModelName);
        var registry = new ModelProviderRegistry([provider]);
        var routeProvider = new HomeModelPickerFeatureProvider(profiles, repo, registry,
            new TestPrivacyPreferenceStore(), resources, broker);
        var routeId = HomeModelPickerFeatureProvider.RouteId(actor.ProfileId, ModelCapabilityCategory.Chat);
        var route = new ConfiguredModelRoute(routeId, 1, ModelRouteScope.User, actor.ProfileId, ModelCapabilityCategory.Chat,
            [new ModelRouteCandidate(new ModelIdentity("ollama", NativeModelName), Enabled: true, Order: 0, ReasoningLevelId: null)],
            new ProviderPolicy());
        Assert.True(await repo.TrySaveGuardedAsync(route, 0, actor, profiles, CancellationToken.None));
        Func<ModelCapabilityCategory, CancellationToken, Task<ConfiguredModelRoute?>> reader =
            async (cat, ct) => cat == ModelCapabilityCategory.Chat ? await repo.GetAsync(routeId, ct) : null;
        return new RealRig
        {
            Store = store, Repo = repo, Profiles = profiles, Actor = actor, Permissions = permissions,
            Resources = resources, Broker = broker, RouteProvider = routeProvider, Registry = registry,
            RouteId = routeId, Reader = reader
        };
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string message, int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(25);
        }
        throw new TimeoutException(message);
    }

    /// <summary>Real AUTHORED click -> pending -> early same click keeps ONE review -> HomeApprovals
    /// "Accept once" button click decides that SAME review -> same payload resume -> exactly one guarded
    /// CAS -> source recreation -> real ChatSession request carries the committed level.</summary>
    [AvaloniaFact]
    public async Task UI_gate_authorized_Accept_click_resumes_same_review_one_CAS_recreate_and_real_ChatSession()
    {
        var rig = await BuildRealRigAsync("uigate");
        var surfaceScope = "User:" + rig.Actor.ProfileId;
        var stableKey = StableKey("ollama", NativeModelName);
        var source = new HomeModelSelectionSource(rig.Registry, rig.Repo, rig.Reader,
            _ => Task.FromResult<AuthenticatedResourceActor?>(rig.Actor),
            Advertised(), routeProvider: rig.RouteProvider);

        var committed = 0;
        source.RouteChanged += (_, _) => committed++;

        // 1) Authored choice click -> a REAL authorization is created; no success/publication.
        var first = await source.CommitSelectionAsync(rig.Actor, NativeModelName, stableKey, surfaceScope, "medium", CancellationToken.None);
        Assert.False(first.Succeeded);
        var requestId = source.GetPendingApprovalRequestId(rig.Actor.ProfileId, stableKey, surfaceScope);
        Assert.False(string.IsNullOrWhiteSpace(requestId));
        Assert.Equal(0, committed);

        // 2) Early same click before any decision keeps ONE review: the genuine typed PendingApproval is
        //    observed, so no duplicate authorization/re-Begin happens and the provider is not re-entered.
        var early = await source.CommitSelectionAsync(rig.Actor, NativeModelName, stableKey, surfaceScope, "medium", CancellationToken.None);
        Assert.False(early.Succeeded);
        Assert.Equal(requestId, source.GetPendingApprovalRequestId(rig.Actor.ProfileId, stableKey, surfaceScope));
        var pending1 = (await rig.Permissions.GetSnapshotAsync(cancellationToken: CancellationToken.None)).PendingRequests;
        Assert.Single(pending1, item => item.RequestId == requestId);
        Assert.Equal(0, committed);

        // 3) The real HomeApprovalCuiSurface approve the SAME review via the visible enabled "Accept once"
        //    Button.ClickEvent (never DecideAsync/substitute). The surface shares the SAME permissions owner.
        var runtime = new HomeCoreRuntime([new HomeCoreStateService(rig.Store), new HomePermissionsCoreService(rig.Permissions, rig.Profiles)]);
        var surface = new HomeApprovalCuiSurface(runtime, rig.Profiles, rig.Permissions);
        var window = new Window { Content = surface, Width = 900, Height = 700 };
        try
        {
            await surface.InitializeAsync(CancellationToken.None);
            Assert.True(await surface.FocusRequestAsync(requestId, CancellationToken.None));
            window.Show();
            var accept = Assert.Single(surface.GetVisualDescendants().OfType<Button>(),
                control => control.Name == "approval-accept");
            Assert.True(accept.IsVisible && accept.IsEffectivelyEnabled);
            accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(
                async () => (await rig.Permissions.GetAuthorizationAsync(requestId, CancellationToken.None)).IsAllowed,
                $"HomeApprovals Accept click did not approve the review {requestId}.");
        }
        finally
        {
            try { window.Close(); } catch { }
            try { await surface.DisposeAsync(); } catch { }
            try { await runtime.DisposeAsync(); } catch { }
        }

        // 4) Resume commit: the SAME payload/revision/actor/broker resumes via the SAME request id -> a
        //    committed success with exactly one guarded CAS publication.
        var second = await source.CommitSelectionAsync(rig.Actor, NativeModelName, stableKey, surfaceScope, "medium", CancellationToken.None);
        Assert.True(second.Succeeded);
        Assert.Equal("medium", second.Value);
        Assert.Equal(1, committed);
        Assert.True(source.Selection.TryGet(new SelectionKey(rig.Actor.ProfileId, stableKey, surfaceScope), out var sel) && sel == "medium");
        Assert.Null(source.GetPendingApprovalRequestId(rig.Actor.ProfileId, stableKey, surfaceScope));

        // 5) Source recreation over the same real store reads the exact committed default.
        var recreated = new HomeModelSelectionSource(rig.Registry, rig.Repo, rig.Reader,
            _ => Task.FromResult<AuthenticatedResourceActor?>(rig.Actor), Advertised(), routeProvider: rig.RouteProvider);
        var snapshot = await recreated.ResolveAsync(1, CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal("medium", snapshot!.SelectedReasoningId);

        // 6) A real ChatSession plain request carries the exact committed level.
        var reader = new HomeChatReasoningSelectionReader(recreated);
        var client = new CapturingOllama(TextModel());
        var service = BuildService(client, reader);
        await SendAsync(service, NewConversation(HavenMode.Chat), TextModel(), root: null);
        Assert.NotNull(client.LastChatRequest);
        Assert.Equal("medium", client.LastChatRequest!.ReasoningId);
        Assert.Equal("ollama", client.LastChatRequest.ReasoningTargetProviderId);
        Assert.Equal(NativeModelName, client.LastChatRequest.ReasoningTargetModelName);
    }

    /// <summary>Real review -> save -> clear -> review -> save null; recreation over the same store stays
    /// null despite any stale in-memory selection (canonical persisted default is the only truth).</summary>
    [Fact]
    public async Task Real_clear_review_save_recreate_null_despite_stale_memory()
    {
        var rig = await BuildRealRigAsync("clear");
        var surfaceScope = "User:" + rig.Actor.ProfileId;
        var stableKey = StableKey("ollama", NativeModelName);
        var source = new HomeModelSelectionSource(rig.Registry, rig.Repo, rig.Reader,
            _ => Task.FromResult<AuthenticatedResourceActor?>(rig.Actor),
            Advertised(), routeProvider: rig.RouteProvider);

        // Review -> save "medium".
        var first = await source.CommitSelectionAsync(rig.Actor, NativeModelName, stableKey, surfaceScope, "medium", CancellationToken.None);
        Assert.False(first.Succeeded);
        var requestId = source.GetPendingApprovalRequestId(rig.Actor.ProfileId, stableKey, surfaceScope);
        var decided = await rig.Permissions.DecideAsync(requestId!, HomeApprovalChoice.Accept, cancellationToken: CancellationToken.None);
        Assert.True(decided.Succeeded);
        var saved = await source.CommitSelectionAsync(rig.Actor, NativeModelName, stableKey, surfaceScope, "medium", CancellationToken.None);
        Assert.True(saved.Succeeded);
        Assert.Equal("medium", saved.Value);

        // Clear -> review -> save null.
        var clear = await source.CommitSelectionAsync(rig.Actor, NativeModelName, stableKey, surfaceScope, null, CancellationToken.None);
        Assert.False(clear.Succeeded);
        var clearRequestId = source.GetPendingApprovalRequestId(rig.Actor.ProfileId, stableKey, surfaceScope);
        Assert.False(string.IsNullOrWhiteSpace(clearRequestId));
        var clearDecided = await rig.Permissions.DecideAsync(clearRequestId!, HomeApprovalChoice.Accept, cancellationToken: CancellationToken.None);
        Assert.True(clearDecided.Succeeded);
        var cleared = await source.CommitSelectionAsync(rig.Actor, NativeModelName, stableKey, surfaceScope, null, CancellationToken.None);
        Assert.True(cleared.Succeeded);
        Assert.True(cleared.Value is null);

        // Recreation over the same real store reads null despite any stale in-memory selection.
        var recreated = new HomeModelSelectionSource(rig.Registry, rig.Repo, rig.Reader,
            _ => Task.FromResult<AuthenticatedResourceActor?>(rig.Actor), Advertised(), routeProvider: rig.RouteProvider);
        var snapshot = await recreated.ResolveAsync(1, CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Null(snapshot!.SelectedReasoningId);
    }

    /// <summary>Genuine decline of the pending review invalidates the retained review and leaves the
    /// canonical route/selection/events untouched (a Denied state is never conflated with pending).</summary>
    [Fact]
    public async Task Real_decline_cancels_retained_review_leaving_canonical_events_unchanged()
    {
        var rig = await BuildRealRigAsync("decline");
        var surfaceScope = "User:" + rig.Actor.ProfileId;
        var stableKey = StableKey("ollama", NativeModelName);
        var source = new HomeModelSelectionSource(rig.Registry, rig.Repo, rig.Reader,
            _ => Task.FromResult<AuthenticatedResourceActor?>(rig.Actor),
            Advertised(), routeProvider: rig.RouteProvider);
        var committed = 0;
        var changed = 0;
        source.RouteChanged += (_, _) => committed++;
        source.SelectionChanged += (_, _) => changed++;

        var first = await source.CommitSelectionAsync(rig.Actor, NativeModelName, stableKey, surfaceScope, "medium", CancellationToken.None);
        Assert.False(first.Succeeded);
        var requestId = source.GetPendingApprovalRequestId(rig.Actor.ProfileId, stableKey, surfaceScope);
        Assert.False(string.IsNullOrWhiteSpace(requestId));

        await rig.Permissions.DecideAsync(requestId!, HomeApprovalChoice.Decline, cancellationToken: CancellationToken.None);

        var retry = await source.CommitSelectionAsync(rig.Actor, NativeModelName, stableKey, surfaceScope, "medium", CancellationToken.None);
        Assert.False(retry.Succeeded);
        Assert.Null(source.GetPendingApprovalRequestId(rig.Actor.ProfileId, stableKey, surfaceScope)); // invalidated
        Assert.Equal(0, committed); // ZERO canonical publication
        Assert.Equal(0, changed);   // ZERO selection change
        Assert.False(source.Selection.TryGet(new SelectionKey(rig.Actor.ProfileId, stableKey, surfaceScope), out _));
    }

    /// <summary>A changed payload invalidates the old retained review; a fresh authorization is requested
    /// for the changed payload but NO canonical route/selection state or event changes (unapproved).</summary>
    [Fact]
    public async Task Real_changed_payload_invalidates_old_review_leaves_canonical_events_unchanged()
    {
        var rig = await BuildRealRigAsync("changed");
        var surfaceScope = "User:" + rig.Actor.ProfileId;
        var stableKey = StableKey("ollama", NativeModelName);
        var source = new HomeModelSelectionSource(rig.Registry, rig.Repo, rig.Reader,
            _ => Task.FromResult<AuthenticatedResourceActor?>(rig.Actor),
            Advertised(), routeProvider: rig.RouteProvider);
        var committed = 0;
        var changed = 0;
        source.RouteChanged += (_, _) => committed++;
        source.SelectionChanged += (_, _) => changed++;

        var first = await source.CommitSelectionAsync(rig.Actor, NativeModelName, stableKey, surfaceScope, "medium", CancellationToken.None);
        Assert.False(first.Succeeded);
        var originalRequestId = source.GetPendingApprovalRequestId(rig.Actor.ProfileId, stableKey, surfaceScope);

        // Request a DIFFERENT level while a review is pending -> old review no longer matches.
        var changedPayload = await source.CommitSelectionAsync(rig.Actor, NativeModelName, stableKey, surfaceScope, "high", CancellationToken.None);
        Assert.False(changedPayload.Succeeded);
        var freshRequestId = source.GetPendingApprovalRequestId(rig.Actor.ProfileId, stableKey, surfaceScope);
        Assert.False(string.IsNullOrWhiteSpace(freshRequestId));
        Assert.NotEqual(originalRequestId, freshRequestId); // fresh authorization for the changed payload
        Assert.Equal(0, committed); // no canonical publication yet
        Assert.Equal(0, changed);
        Assert.False(source.Selection.TryGet(new SelectionKey(rig.Actor.ProfileId, stableKey, surfaceScope), out _));
    }
}
