using Haven.Application;
using HavenOS.Apps.Spaces.Experiences;
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
