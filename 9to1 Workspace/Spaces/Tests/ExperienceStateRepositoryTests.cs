using Haven.Application;
using HavenOS.Apps.Spaces.Experiences;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class ExperienceStateRepositoryTests
{
    [Fact]
    public async Task Experience_state_is_canonical_versioned_and_separate_from_narrative_content()
    {
        var repository = new ExperienceStateRepository(new MemorySettingsStore());
        var branchId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var actors = new List<ExperienceActorState>
        {
            new(actorId, "Mira", null, ExperienceVisibility.Public, [])
        };
        var initialState = new ExperienceStateDocument(
            "Harbour", "Dawn", actors, [], [],
            [new ExperienceObjective(Guid.NewGuid(), "Find the lighthouse keeper", "Active", actorId)],
            [new ExperienceFlag("storm_warning", "true")],
            [new ExperienceEvent(Guid.NewGuid(), "SceneStarted", null, "{}", DateTimeOffset.UtcNow)], []);

        var timeline = await repository.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), branchId, "Harbour mystery", initialState);
        actors.Clear();

        Assert.NotEqual(Guid.Empty, timeline.ExperienceId);
        Assert.Equal(branchId, timeline.ActiveRevision.ConversationBranchId);
        Assert.Equal("Harbour", timeline.ActiveRevision.State.Scene);
        Assert.Single(timeline.ActiveRevision.State.Actors);
        Assert.Equal(1, timeline.ActiveRevision.RevisionNumber);

        var exposedActors = Assert.IsType<ExperienceActorState[]>(timeline.ActiveRevision.State.Actors);
        exposedActors[0] = exposedActors[0] with { Name = "Tampered result" };
        var persisted = await repository.GetAsync(timeline.ExperienceId);
        Assert.Equal("Mira", Assert.Single(persisted!.ActiveRevision.State.Actors).Name);
    }

    [Fact]
    public async Task Branches_keep_matching_state_revisions_and_do_not_rewrite_external_effects()
    {
        var repository = new ExperienceStateRepository(new MemorySettingsStore());
        var sourceBranch = Guid.NewGuid();
        var targetBranch = Guid.NewGuid();
        var timeline = await repository.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), sourceBranch, "Scenario");
        var sourceRevision = timeline.ActiveRevision;
        var effect = new ExperienceExternalEffect(
            Guid.NewGuid(), sourceBranch, "Planner", "CreateItem", "{\"id\":\"task-1\"}", DateTimeOffset.UtcNow);
        timeline = await repository.RecordExternalEffectAsync(timeline.ExperienceId, effect);
        timeline = await repository.ForkBranchAsync(timeline.ExperienceId, sourceRevision.RevisionId, targetBranch);

        var changedState = ExperienceStateDocument.Empty with { Scene = "Alternate scene" };
        timeline = await repository.CommitStateAsync(
            timeline.ExperienceId,
            targetBranch,
            timeline.ActiveRevisionId,
            changedState,
            "The protagonist chose the other path");
        var targetRevision = timeline.ActiveRevision;
        var sourceTimeline = await repository.SelectBranchAsync(timeline.ExperienceId, sourceBranch);

        Assert.Equal("Alternate scene", targetRevision.State.Scene);
        Assert.Equal("", sourceTimeline.ActiveRevision.State.Scene);
        Assert.Equal(sourceRevision.RevisionId, sourceTimeline.ActiveRevision.RevisionId);
        Assert.Single(sourceTimeline.ExternalEffects);
        Assert.Equal(sourceBranch, sourceTimeline.ExternalEffects[0].ConversationBranchId);
        Assert.Equal(3, targetRevision.RevisionNumber);
    }

    [Fact]
    public async Task External_action_records_are_idempotent_and_reject_changed_replays()
    {
        var repository = new ExperienceStateRepository(new MemorySettingsStore());
        var branchId = Guid.NewGuid();
        var timeline = await repository.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), branchId, "Scenario");
        var effect = new ExperienceExternalEffect(Guid.NewGuid(), branchId, "Planner", "CreateItem", "{}", DateTimeOffset.UtcNow);

        await repository.RecordExternalEffectAsync(timeline.ExperienceId, effect);
        var replay = await repository.RecordExternalEffectAsync(timeline.ExperienceId, effect);
        Assert.Single(replay.ExternalEffects);

        var changed = effect with { ResultJson = "{\"id\":\"different\"}" };
        var exception = await Assert.ThrowsAsync<ExperienceStateException>(
            () => repository.RecordExternalEffectAsync(timeline.ExperienceId, changed));
        Assert.Equal(ExperienceStateErrorCode.RevisionConflict, exception.Code);
    }

    [Fact]
    public async Task Actor_projection_filters_private_characters_and_facts()
    {
        var repository = new ExperienceStateRepository(new MemorySettingsStore());
        var playerId = Guid.NewGuid();
        var hiddenActorId = Guid.NewGuid();
        var state = ExperienceStateDocument.Empty with
        {
            Actors =
            [
                new(playerId, "Player", null, ExperienceVisibility.Public, []),
                new(hiddenActorId, "Hidden keeper", null, ExperienceVisibility.OwnerOnly, [])
            ],
            PrivateFacts =
            [
                new(Guid.NewGuid(), "keeper_secret", "\"knows_the_code\"", ExperienceVisibility.SelectedActors, [playerId]),
                new(Guid.NewGuid(), "hidden_secret", "\"unseen\"", ExperienceVisibility.SelectedActors, [hiddenActorId])
            ]
        };
        var timeline = await repository.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Scenario", state);

        var visible = await repository.GetVisibleStateAsync(timeline.ExperienceId, playerId);

        Assert.Single(visible.Actors);
        Assert.Equal(playerId, visible.Actors[0].ActorId);
        Assert.Equal("keeper_secret", Assert.Single(visible.VisiblePrivateFacts).Key);
    }

    [Fact]
    public async Task State_commits_require_the_active_revision_and_valid_typed_state()
    {
        var repository = new ExperienceStateRepository(new MemorySettingsStore());
        var branchId = Guid.NewGuid();
        var timeline = await repository.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), branchId, "Scenario");
        var committed = await repository.CommitStateAsync(
            timeline.ExperienceId, branchId, timeline.ActiveRevisionId,
            ExperienceStateDocument.Empty with { Scene = "Updated" }, "Move to the archive");

        await Assert.ThrowsAsync<ExperienceStateException>(() => repository.CommitStateAsync(
            timeline.ExperienceId, branchId, timeline.ActiveRevisionId,
            ExperienceStateDocument.Empty, "Stale update"));
        var invalid = ExperienceStateDocument.Empty with
        {
            Flags = [new ExperienceFlag("broken", "not-json")]
        };
        await Assert.ThrowsAsync<ExperienceStateException>(() => repository.CommitStateAsync(
            timeline.ExperienceId, branchId, committed.ActiveRevisionId, invalid, "Invalid update"));
    }

    [Fact]
    public async Task Actor_projection_redacts_private_entries_and_audiences_until_a_committed_reveal()
    {
        var repository = new ExperienceStateRepository(new MemorySettingsStore());
        var player = Guid.NewGuid();
        var keeper = Guid.NewGuid();
        var hidden = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var state = ExperienceStateDocument.Empty with
        {
            Actors =
            [
                new(player, "Player", null, ExperienceVisibility.Public, []),
                new(keeper, "Keeper", null, ExperienceVisibility.SelectedActors, [player, hidden, keeper]),
                new(hidden, "Hidden character", null, ExperienceVisibility.OwnerOnly, [])
            ],
            Relationships = [new(keeper, player, "SECRET_RELATIONSHIP", "true", ExperienceVisibility.OwnerOnly)],
            Inventory =
            [
                new(Guid.NewGuid(), "SECRET_CLUE", "{}", keeper, ExperienceVisibility.OwnerOnly),
                new(Guid.NewGuid(), "Visible clue", "{}", null, ExperienceVisibility.SelectedActors, [player, hidden])
            ],
            Objectives = [new(Guid.NewGuid(), "SECRET_OBJECTIVE", "Active", keeper, ExperienceVisibility.OwnerOnly)],
            Flags = [new("SECRET_FLAG", "true", ExperienceVisibility.SelectedActors, [keeper])],
            Events = [new(Guid.NewGuid(), "SECRET_EVENT", keeper, "{}", DateTimeOffset.UtcNow, ExperienceVisibility.OwnerOnly)],
            PrivateFacts =
            [
                new(Guid.NewGuid(), "visible_fact", "true", ExperienceVisibility.SelectedActors, [player, hidden]),
                new(Guid.NewGuid(), "SECRET_FACT", "true", ExperienceVisibility.SelectedActors, [keeper])
            ]
        };
        var timeline = await repository.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), branch, "Mystery", state);

        var playerView = await repository.GetVisibleStateAsync(timeline.ExperienceId, player);
        var serializedView = JsonSerializer.Serialize(playerView);
        Assert.DoesNotContain("SECRET_", serializedView);
        Assert.DoesNotContain(hidden.ToString(), serializedView, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { player }, playerView.Actors.Single(actor => actor.ActorId == keeper).VisibleToActorIds);
        Assert.Equal(new[] { player }, Assert.Single(playerView.Inventory).VisibleToActorIds);
        Assert.Equal(new[] { player }, Assert.Single(playerView.VisiblePrivateFacts).VisibleToActorIds);
        Assert.Empty(playerView.Relationships);
        Assert.Empty(playerView.Objectives);
        Assert.Empty(playerView.Flags);
        Assert.Empty(playerView.Events);

        var keeperView = await repository.GetVisibleStateAsync(timeline.ExperienceId, keeper);
        Assert.Contains(keeperView.Inventory, item => item.Name == "SECRET_CLUE");
        Assert.Contains(keeperView.Objectives, item => item.Title == "SECRET_OBJECTIVE");
        Assert.Contains(keeperView.Flags, item => item.Key == "SECRET_FLAG");
        Assert.Contains(keeperView.Events, item => item.Kind == "SECRET_EVENT");

        var revealed = state with
        {
            Inventory =
            [
                state.Inventory[0] with { Visibility = ExperienceVisibility.SelectedActors, VisibleToActorIds = [player] },
                state.Inventory[1]
            ]
        };
        await repository.CommitActionAsync(timeline.ExperienceId, branch, timeline.ActiveRevisionId,
            Guid.NewGuid(), revealed, "Keeper reveals the clue");
        playerView = await repository.GetVisibleStateAsync(timeline.ExperienceId, player);
        Assert.Contains(playerView.Inventory, item => item.Name == "SECRET_CLUE");
        Assert.DoesNotContain("SECRET_OBJECTIVE", JsonSerializer.Serialize(playerView));
    }

    [Fact]
    public async Task Invalid_entry_audience_is_rejected_without_changing_the_current_revision()
    {
        var repository = new ExperienceStateRepository(new MemorySettingsStore());
        var actor = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var state = ExperienceStateDocument.Empty with
        {
            Actors = [new(actor, "Player", null, ExperienceVisibility.Public, [])]
        };
        var timeline = await repository.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), branch, "Mystery", state);
        var invalid = state with
        {
            Inventory = [new(Guid.NewGuid(), "Clue", "{}", actor, ExperienceVisibility.SelectedActors, [Guid.NewGuid()])]
        };

        var exception = await Assert.ThrowsAsync<ExperienceStateException>(() =>
            repository.CommitActionAsync(timeline.ExperienceId, branch, timeline.ActiveRevisionId,
                Guid.NewGuid(), invalid, "Acquire clue"));
        Assert.Equal(ExperienceStateErrorCode.InvalidState, exception.Code);
        Assert.Equal(timeline.ActiveRevisionId, (await repository.GetAsync(timeline.ExperienceId))!.ActiveRevisionId);
    }

    [Fact]
    public async Task Reopen_replays_one_action_without_reapplying_it_or_switching_the_active_branch()
    {
        using var paths = new TemporaryPaths();
        var repository = new ExperienceStateRepository(new VersionedAtomicSettingsStore(paths));
        var branch = Guid.NewGuid();
        var otherBranch = Guid.NewGuid();
        var action = Guid.NewGuid();
        var timeline = await repository.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), branch, "Mystery");
        var originalRevision = timeline.ActiveRevisionId;
        var clue = new ExperienceInventoryItem(Guid.NewGuid(), "Letter", "{}", null);
        var proposal = ExperienceStateDocument.Empty with { Inventory = [clue] };
        var committed = await repository.CommitActionAsync(timeline.ExperienceId, branch, originalRevision, action, proposal, "Find letter");
        var effect = new ExperienceExternalEffect(Guid.NewGuid(), branch, "Files", "Publish", "{}", DateTimeOffset.UtcNow);
        await repository.RecordExternalEffectAsync(timeline.ExperienceId, effect);
        await repository.ForkBranchAsync(timeline.ExperienceId, originalRevision, otherBranch);

        var reopened = new ExperienceStateRepository(new VersionedAtomicSettingsStore(paths));
        var replay = await reopened.CommitActionAsync(timeline.ExperienceId, branch, originalRevision, action, proposal, "Find letter");
        var restored = (await reopened.GetAsync(timeline.ExperienceId))!;
        Assert.False(committed.WasReplay);
        Assert.True(replay.WasReplay);
        Assert.Equal(committed.Revision.RevisionId, replay.Revision.RevisionId);
        Assert.Equal(otherBranch, restored.ActiveRevision.ConversationBranchId);
        Assert.Empty(restored.ActiveRevision.State.Inventory);
        Assert.Single(restored.ExternalEffects);
        Assert.Equal(3, restored.Revisions.Count);
        Assert.Single(restored.Revisions, item => item.ActionId == action);

        var changed = proposal with { Scene = "Changed proposal" };
        var conflict = await Assert.ThrowsAsync<ExperienceStateException>(() =>
            reopened.CommitActionAsync(timeline.ExperienceId, branch, originalRevision, action, changed, "Find letter"));
        Assert.Equal(ExperienceStateErrorCode.RevisionConflict, conflict.Code);
        var originalBranch = await reopened.SelectBranchAsync(timeline.ExperienceId, branch);
        Assert.Equal(clue.ItemId, Assert.Single(originalBranch.ActiveRevision.State.Inventory).ItemId);
    }

    [Fact]
    public async Task Competing_repository_instances_cannot_overwrite_a_newer_revision()
    {
        using var paths = new TemporaryPaths();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var actualStore = new VersionedAtomicSettingsStore(paths);
        var peer = new ExperienceStateRepository(new VersionedAtomicSettingsStore(paths));
        var branch = Guid.NewGuid();
        var timeline = await peer.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), branch, "Mystery", cancellationToken: token);
        var held = new HeldCompareStore(actualStore);
        var waiting = new ExperienceStateRepository(held);
        var stale = waiting.CommitActionAsync(timeline.ExperienceId, branch, timeline.ActiveRevisionId,
            Guid.NewGuid(), ExperienceStateDocument.Empty with { Scene = "Stale" }, "Stale action", token);
        await held.Entered.Task.WaitAsync(token);
        try
        {
            var winner = await peer.CommitActionAsync(timeline.ExperienceId, branch, timeline.ActiveRevisionId,
                Guid.NewGuid(), ExperienceStateDocument.Empty with { Scene = "Winner" }, "Winning action", token);
            held.Release.TrySetResult();
            var error = await Assert.ThrowsAsync<ExperienceStateException>(() => stale);
            Assert.Equal(ExperienceStateErrorCode.RevisionConflict, error.Code);
            var restored = (await new ExperienceStateRepository(new VersionedAtomicSettingsStore(paths))
                .GetAsync(timeline.ExperienceId, token))!;
            Assert.Equal(winner.Revision.RevisionId, restored.ActiveRevisionId);
            Assert.Equal("Winner", restored.ActiveRevision.State.Scene);
            Assert.Equal(2, restored.Revisions.Count);
        }
        finally
        {
            held.Release.TrySetResult();
        }
    }

    [Fact]
    public async Task Concurrent_identical_action_is_replayed_and_freezes_the_proposal_before_storage()
    {
        using var paths = new TemporaryPaths();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var peer = new ExperienceStateRepository(new VersionedAtomicSettingsStore(paths));
        var branch = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var timeline = await peer.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), branch, "Mystery",
            ExperienceStateDocument.Empty with { Actors = [new(actor, "Player", null, ExperienceVisibility.Public, [])] }, token);
        var audience = new List<Guid> { actor };
        var clue = new ExperienceInventoryItem(Guid.NewGuid(), "Letter", "{}", actor, ExperienceVisibility.SelectedActors, audience);
        var proposal = timeline.ActiveRevision.State with { Inventory = [clue] };
        var action = Guid.NewGuid();
        var held = new HeldCompareStore(new VersionedAtomicSettingsStore(paths));
        var waiting = new ExperienceStateRepository(held);
        var pending = waiting.CommitActionAsync(timeline.ExperienceId, branch, timeline.ActiveRevisionId,
            action, proposal, "Find letter", token);
        await held.Entered.Task.WaitAsync(token);
        try
        {
            var winner = await peer.CommitActionAsync(timeline.ExperienceId, branch, timeline.ActiveRevisionId,
                action, proposal, "Find letter", token);
            audience.Clear();
            held.Release.TrySetResult();
            var replay = await pending;
            Assert.True(replay.WasReplay);
            Assert.Equal(winner.Revision.RevisionId, replay.Revision.RevisionId);
            Assert.Equal(new[] { actor }, Assert.Single(replay.Revision.State.Inventory).VisibleToActorIds);
            var restored = (await peer.GetAsync(timeline.ExperienceId, token))!;
            Assert.Equal(2, restored.Revisions.Count);
            Assert.Equal(new[] { actor }, Assert.Single(restored.ActiveRevision.State.Inventory).VisibleToActorIds);
        }
        finally
        {
            held.Release.TrySetResult();
        }
    }

    [Fact]
    public async Task Legacy_stored_entries_without_new_visibility_fields_remain_public()
    {
        using var paths = new TemporaryPaths();
        var store = new VersionedAtomicSettingsStore(paths);
        var repository = new ExperienceStateRepository(store);
        var actor = Guid.NewGuid();
        var state = ExperienceStateDocument.Empty with
        {
            Actors = [new(actor, "Player", null, ExperienceVisibility.Public, [])],
            Inventory = [new(Guid.NewGuid(), "Old clue", "{}", null)]
        };
        var timeline = await repository.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Old mystery", state);
        var manifest = await store.ExportAsync(default);
        var json = JsonNode.Parse(manifest.Settings["spaces.experiences.state"])!;
        json["Version"] = 1;
        var revision = json["Experiences"]![0]!["Revisions"]![0]!;
        revision.AsObject().Remove("ActionId");
        var item = revision["State"]!["Inventory"]![0]!.AsObject();
        item.Remove("Visibility");
        item.Remove("VisibleToActorIds");
        manifest.Settings["spaces.experiences.state"] = json.ToJsonString();
        await store.ImportAsync(manifest, default);

        var reopened = new ExperienceStateRepository(new VersionedAtomicSettingsStore(paths));
        var visible = await reopened.GetVisibleStateAsync(timeline.ExperienceId, actor);
        Assert.Equal("Old clue", Assert.Single(visible.Inventory).Name);
        Assert.Equal(ExperienceVisibility.Public, visible.Inventory[0].Visibility);
        await reopened.SelectBranchAsync(timeline.ExperienceId, timeline.ActiveRevision.ConversationBranchId);
        var upgraded = await store.ExportAsync(default);
        Assert.Equal(2, JsonNode.Parse(upgraded.Settings["spaces.experiences.state"])!["Version"]!.GetValue<int>());
    }

    private sealed class TemporaryPaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Directory.CreateDirectory(
            Path.Combine("/tmp/opencode", $"q46-experiences-{Guid.NewGuid():N}")).FullName;
        public string DatabasePath => Path.Combine(DataDirectory, "data.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }

    private sealed class HeldCompareStore(VersionedAtomicSettingsStore inner) : IVersionedSettingsStore, IVersionedSettingsCompareExchange
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => inner.GetAsync<T>(key, token);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => inner.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token) => inner.RemoveAsync(key, token);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => inner.ExportAsync(token);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => inner.ImportAsync(manifest, token);
        public async Task<SettingsCompareExchangeResult> CompareExchangeAsync(
            string key, string? expectedJson, string? replacementJson, CancellationToken token)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(token);
            return await inner.CompareExchangeAsync(key, expectedJson, replacementJson, token);
        }
    }

    private sealed class MemorySettingsStore : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_values.TryGetValue(key, out var value) ? (T?)value : null);
        }

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
