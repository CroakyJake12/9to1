using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.AIStudio.Tests;

public sealed class AgentPresentationPlaybackTests
{
    [Fact]
    public async Task Coding_and_joke_use_one_agent_and_repeated_observation_does_not_repeat_transition()
    {
        await using var f = await Fixture.CreateAsync();
        var before = await f.Service.GetAsync("personal", "canonical-agent");
        f.Source.Current = new(f.Context, 1, AgentPresentationActivityKind.Coding);
        Assert.Equal("coding", (await f.Playback.ReadAsync(false)).StateId);
        Assert.Equal("coding", (await f.Playback.ReadAsync(false)).StateId);
        f.Source.Current = new(f.Context, 2, AgentPresentationActivityKind.Coding);
        Assert.Equal("busy", (await f.Playback.ReadAsync(false)).StateId);
        f.Source.Current = new(f.Context, 3, AgentPresentationActivityKind.UserJoke);
        var reaction = await f.Playback.ReadAsync(false);
        Assert.Equal("laugh", reaction.StateId); Assert.Equal("Joke reaction", reaction.ReadableActivity);
        var after = await f.Service.GetAsync("personal", "canonical-agent");
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(after));
        Assert.Equal(before.ToolIds, after.ToolIds);
        Assert.Equal("canonical-agent", reaction.AgentId); Assert.Equal(before.Revision, reaction.DefinitionRevision);
    }

    [Fact]
    public async Task Reduced_motion_and_missing_animation_use_accessible_static_fallback_without_forgetting_state()
    {
        await using var f = await Fixture.CreateAsync();
        f.Source.Current = new(f.Context, 1, AgentPresentationActivityKind.Coding);
        var reduced = await f.Playback.ReadAsync(true);
        Assert.False(reduced.Animated); Assert.Equal("asset:static", reduced.AssetReference);
        Assert.Equal("Canonical Agent", reduced.AccessibleName); Assert.Equal("Coding", reduced.ReadableActivity);
        Assert.Equal("coding", (await f.Playback.ReadAsync(false)).StateId);
        f.Assets.AnimationAvailable = false;
        Assert.Equal("asset:static", (await f.Playback.ReadAsync(false)).AssetReference);
        f.Assets.AnimationAvailable = true;
        Assert.Equal("coding", (await f.Playback.ReadAsync(false)).StateId);
    }

    [Fact]
    public async Task Wrong_context_replayed_revision_and_changed_configuration_are_visible_refusals_or_resets()
    {
        await using var f = await Fixture.CreateAsync();
        f.Source.Current = new(f.Context with { SourceId = "another-conversation" }, 1, AgentPresentationActivityKind.Coding);
        Assert.Equal(DenErrorCode.InvalidRecord, (await Assert.ThrowsAsync<DenException>(() => f.Playback.ReadAsync(false))).Code);
        f.Source.Current = new(f.Context, 3, AgentPresentationActivityKind.Coding);
        Assert.Equal("coding", (await f.Playback.ReadAsync(false)).StateId);
        f.Source.Current = new(f.Context, 2, AgentPresentationActivityKind.UserJoke);
        Assert.Equal(DenErrorCode.Conflict, (await Assert.ThrowsAsync<DenException>(() => f.Playback.ReadAsync(false))).Code);
        f.Source.Current = new(f.Context, 3, AgentPresentationActivityKind.UserJoke);
        Assert.Equal(DenErrorCode.Conflict, (await Assert.ThrowsAsync<DenException>(() => f.Playback.ReadAsync(false))).Code);
        var agent = await f.Service.GetAsync("personal", "canonical-agent");
        await f.Service.SetAsync(agent.NamespaceId, agent.Id, agent.Revision,
            agent.Presentation! with { InitialStateId = "laugh" }, "changed-definition");
        f.Source.Current = new(f.Context, 4, AgentPresentationActivityKind.Idle);
        Assert.Equal("laugh", (await f.Playback.ReadAsync(false)).StateId);
    }

    [Fact]
    public async Task Revoked_den_permission_or_revision_change_during_observation_never_publishes_a_frame()
    {
        await using var f = await Fixture.CreateAsync();
        f.Source.Current = new(f.Context, 1, AgentPresentationActivityKind.Coding);
        Assert.Equal(DenErrorCode.Forbidden, (await ReadPausedRefusalAsync(f, () =>
        { f.Policy.Allowed = false; return Task.CompletedTask; })).Code);
        f.Policy.Allowed = true;
        Assert.Equal(DenErrorCode.Conflict, (await ReadPausedRefusalAsync(f, async () =>
        {
            var agent = await f.Service.GetAsync("personal", "canonical-agent");
            await f.Service.SetAsync(agent.NamespaceId, agent.Id, agent.Revision, agent.Presentation!, "new-revision");
        })).Code);
    }

    [Fact]
    public async Task Typed_builder_preview_assign_and_reopen_preserve_identity_and_original_execution_fields()
    {
        await using var f = await Fixture.CreateAsync();
        var editor = new AgentAvatarEditor(f.Service);
        await editor.OpenAsync("personal", "canonical-agent");
        await editor.DispatchAsync("PreviewCoding", null);
        Assert.Equal("asset:coding", editor.Bindings.Get("PreviewAsset"));
        await editor.DispatchAsync("PreviewJoke", null);
        Assert.Equal("asset:laugh", editor.Bindings.Get("PreviewAsset"));
        editor.Bindings.Set("ReducedMotion", true);
        await editor.DispatchAsync("PreviewCoding", null);
        Assert.Equal("asset:static", editor.Bindings.Get("PreviewAsset"));
        await editor.DispatchAsync("AssignAvatar", null);
        var reopened = new AgentAvatarEditor(f.Service); await reopened.OpenAsync("personal", "canonical-agent");
        Assert.Equal(editor.Draft!.SchemaVersion, reopened.Draft!.SchemaVersion);
        Assert.Equal(editor.Draft.Mode, reopened.Draft.Mode);
        Assert.Equal(editor.Draft.StaticFallbackAssetReference, reopened.Draft.StaticFallbackAssetReference);
        Assert.Equal(editor.Draft.AccessibleName, reopened.Draft.AccessibleName);
        Assert.Equal(editor.Draft.InitialStateId, reopened.Draft.InitialStateId);
        Assert.Equal(editor.Draft!.States, reopened.Draft!.States);
        Assert.Equal(editor.Draft.Transitions, reopened.Draft.Transitions);
        Assert.Equal(editor.Draft.Reactions, reopened.Draft.Reactions);
        Assert.Equal(new[] { "original.tool" }, (await f.Service.GetAsync("personal", "canonical-agent")).ToolIds);
        Assert.Equal("canonical-agent", reopened.CurrentAgentIdentity!.Value.AgentID);
    }

    [Fact]
    public async Task Unavailable_activity_does_not_erase_the_same_context_sequence_high_water_mark()
    {
        await using var f = await Fixture.CreateAsync();
        var before = await f.Service.GetAsync("personal", "canonical-agent");
        f.Source.Current = new(f.Context, 5, AgentPresentationActivityKind.Coding);
        Assert.Equal("coding", (await f.Playback.ReadAsync(false)).StateId);
        f.Source.Current = null;
        var unavailable = await f.Playback.ReadAsync(false);
        Assert.Equal("Activity unavailable", unavailable.ReadableActivity);
        Assert.Equal("idle", unavailable.StateId);
        f.Source.Current = new(f.Context, 1, AgentPresentationActivityKind.UserJoke);
        Assert.Equal(DenErrorCode.Conflict,
            (await Assert.ThrowsAsync<DenException>(() => f.Playback.ReadAsync(false))).Code);
        f.Source.Current = new(f.Context, 5, AgentPresentationActivityKind.UserJoke);
        Assert.Equal(DenErrorCode.Conflict,
            (await Assert.ThrowsAsync<DenException>(() => f.Playback.ReadAsync(false))).Code);
        f.Source.Current = new(f.Context, 5, AgentPresentationActivityKind.Coding);
        Assert.Equal("coding", (await f.Playback.ReadAsync(false)).StateId);
        var after = await f.Service.GetAsync("personal", "canonical-agent");
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before),
            System.Text.Json.JsonSerializer.Serialize(after));
        Assert.Equal(before.ToolIds, after.ToolIds);
        Assert.Equal(before.Revision, after.Revision);
    }

    // Each pause owns exactly this original read, release signal and cancellation token.
    // An entry/assertion failure still releases or cancels and settles the same task before Den disposal.
    private static async Task<DenException> ReadPausedRefusalAsync(Fixture f, Func<Task> whilePaused)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        f.Source.Pause(); var release = f.Source.Release;
        var pending = f.Playback.ReadAsync(false, cancellation.Token);
        Exception? primary = null; DenException? observed = null;
        try
        {
            await f.Source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await whilePaused(); release.TrySetResult();
            try { await pending; }
            catch (DenException failure) { observed = failure; }
            Assert.NotNull(observed);
            return observed!;
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            var cleanupFailures = new List<Exception>();
            void Retain(Exception cleanup)
            {
                if (!ReferenceEquals(cleanup, observed) && !ReferenceEquals(cleanup, primary) &&
                    !cleanupFailures.Any(previous => ReferenceEquals(previous, cleanup))) cleanupFailures.Add(cleanup);
            }
            try { release.TrySetResult(); } catch (Exception cleanup) { Retain(cleanup); }
            try { cancellation.Cancel(); } catch (Exception cleanup) { Retain(cleanup); }
            try { await pending; }
            catch (OperationCanceledException canceled) when (canceled.CancellationToken == cancellation.Token) { }
            catch (Exception cleanup) { Retain(cleanup); }
            if (cleanupFailures.Count > 0)
            {
                if (primary is not null) cleanupFailures.Insert(0, primary);
                if (cleanupFailures.Count == 1)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailures[0]).Throw();
                throw new AggregateException(cleanupFailures);
            }
        }
    }

    private sealed class Policy : IDenAccessPolicy
    {
        public bool Allowed { get; set; } = true;
        public ValueTask<bool> IsAllowedAsync(string principal, string ns, string id, DenPermission permission,
            CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(Allowed && principal == "owner" && ns == "personal"); }
    }
    private sealed class Assets : IAgentPresentationAssetAccess
    {
        public bool AnimationAvailable { get; set; } = true;
        public ValueTask<bool> CanReadAsync(string principal, string ns, string asset, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(asset == "asset:static" || AnimationAvailable); }
    }
    // Explicit synthetic producer for controller fixtures, not proof of an authenticated host activity source.
    private sealed class Source : IAgentPresentationObservationSource
    {
        public AgentPresentationObservation? Current { get; set; }
        public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _paused;
        public void Pause()
        { _paused = true; Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); Release = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public async ValueTask<AgentPresentationObservation?> GetCurrentAsync(AgentPresentationContext context, CancellationToken ct)
        {
            if (_paused) { _paused = false; Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            ct.ThrowIfCancellationRequested(); return Current;
        }
    }
    private sealed class Fixture(string root, DenStore store, Policy policy, Assets assets, Source source,
        AgentPresentationService service, AgentPresentationContext context, AgentPresentationPlayback playback) : IAsyncDisposable
    {
        public Policy Policy { get; } = policy;
        public Assets Assets { get; } = assets;
        public Source Source { get; } = source;
        public AgentPresentationService Service { get; } = service;
        public AgentPresentationContext Context { get; } = context;
        public AgentPresentationPlayback Playback { get; } = playback;
        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "astra-canonical-avatar-" + Guid.NewGuid().ToString("N"));
            var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            var policy = new Policy(); var assets = new Assets(); var source = new Source();
            var den = new DulcheDen(store, policy, "owner"); var service = new AgentPresentationService(den, assets);
            try
            {
                var agent = await den.SaveAsync(new AgentDefinitionRecord { Id = "canonical-agent", NamespaceId = "personal",
                    DisplayName = "Canonical Agent", Version = "1", ToolIds = ["original.tool"] }, 0, "create");
                var states = new[] { "idle", "coding", "busy", "laugh" }.Select(s => new AgentAvatarState(s, s, "asset:" + s, true)).ToArray();
                await service.SetAsync("personal", agent.Id, agent.Revision,
                    new(1, AgentIconPresentation.Animated, "asset:static", "Canonical Agent", "idle", states,
                        [new("idle", "coding", AgentPresentationEvents.Coding), new("coding", "busy", AgentPresentationEvents.Coding)],
                        [new(AgentPresentationEvents.UserJoke, "laugh")]), "presentation");
                var context = new AgentPresentationContext("personal", agent.Id, "actual-context-fixture");
                return new(root, store, policy, assets, source, service, context, new(service, source, context));
            }
            catch { await store.DisposeAsync(); Directory.Delete(root, true); throw; }
        }
        public async ValueTask DisposeAsync() { await store.DisposeAsync(); Directory.Delete(root, true); }
    }
}
