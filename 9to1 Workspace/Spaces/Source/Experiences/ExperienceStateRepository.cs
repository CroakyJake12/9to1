using System.Text.Json;
using Haven.Application;

namespace HavenOS.Apps.Spaces.Experiences;

/// <summary>
/// Persists canonical Experience state in the Home-provided versioned settings store. Register as
/// a singleton so a single repository serializes read/modify/write operations for its state key.
/// The repository never edits conversation records or executes external actions.
/// </summary>
public sealed class ExperienceStateRepository
{
    private const string SettingsKey = "spaces.experiences.state";
    private const int CurrentSchemaVersion = 1;
    private readonly IVersionedSettingsStore _settings;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ExperienceStateRepository(IVersionedSettingsStore settings)
        : this(settings, () => DateTimeOffset.UtcNow)
    {
    }

    internal ExperienceStateRepository(IVersionedSettingsStore settings, Func<DateTimeOffset> clock)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ExperienceTimeline> CreateAsync(
        Guid spaceId,
        Guid conversationId,
        Guid conversationBranchId,
        string title,
        ExperienceStateDocument? initialState = null,
        CancellationToken cancellationToken = default)
    {
        RequireId(spaceId, nameof(spaceId));
        RequireId(conversationId, nameof(conversationId));
        RequireId(conversationBranchId, nameof(conversationBranchId));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var state = initialState ?? ExperienceStateDocument.Empty;
        Validate(state);

        return await MutateAsync(snapshot =>
        {
            if (snapshot.Experiences.Any(item => item.ConversationId == conversationId))
                throw new ExperienceStateException(
                    ExperienceStateErrorCode.RevisionConflict,
                    $"Conversation '{conversationId}' already has canonical Experience state.");

            var now = _clock();
            var revision = new ExperienceStateRevision(
                Guid.NewGuid(), 1, conversationBranchId, null, state, "Experience created", now);
            var timeline = new ExperienceTimeline(
                Guid.NewGuid(), spaceId, conversationId, title.Trim(), [revision], revision.RevisionId, [], now, now);
            return (snapshot with { Experiences = [.. snapshot.Experiences, timeline] }, timeline);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExperienceTimeline?> GetAsync(Guid experienceId, CancellationToken cancellationToken = default)
    {
        RequireId(experienceId, nameof(experienceId));
        var snapshot = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Experiences.SingleOrDefault(item => item.ExperienceId == experienceId);
    }

    public async Task<ExperienceTimeline> CommitStateAsync(
        Guid experienceId,
        Guid conversationBranchId,
        Guid expectedRevisionId,
        ExperienceStateDocument nextState,
        string transition,
        CancellationToken cancellationToken = default)
    {
        RequireId(experienceId, nameof(experienceId));
        RequireId(conversationBranchId, nameof(conversationBranchId));
        RequireId(expectedRevisionId, nameof(expectedRevisionId));
        ArgumentNullException.ThrowIfNull(nextState);
        ArgumentException.ThrowIfNullOrWhiteSpace(transition);
        Validate(nextState);

        return await MutateAsync(snapshot =>
        {
            var timeline = Find(snapshot, experienceId);
            var current = timeline.ActiveRevision;
            RequireExpectedRevision(timeline, conversationBranchId, expectedRevisionId);
            var revision = new ExperienceStateRevision(
                Guid.NewGuid(), checked(timeline.Revisions.Max(item => item.RevisionNumber) + 1),
                conversationBranchId, current.RevisionId, nextState, transition.Trim(), _clock());
            var updated = timeline with
            {
                Revisions = [.. timeline.Revisions, revision],
                ActiveRevisionId = revision.RevisionId,
                UpdatedAt = revision.CreatedAt
            };
            return (Replace(snapshot, updated), updated);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a matching Experience-state branch from an existing conversation branch point.
    /// The original revision and any observed external effects remain unchanged.
    /// </summary>
    public async Task<ExperienceTimeline> ForkBranchAsync(
        Guid experienceId,
        Guid sourceRevisionId,
        Guid targetConversationBranchId,
        string transition = "Experience branched",
        CancellationToken cancellationToken = default)
    {
        RequireId(experienceId, nameof(experienceId));
        RequireId(sourceRevisionId, nameof(sourceRevisionId));
        RequireId(targetConversationBranchId, nameof(targetConversationBranchId));
        ArgumentException.ThrowIfNullOrWhiteSpace(transition);

        return await MutateAsync(snapshot =>
        {
            var timeline = Find(snapshot, experienceId);
            var source = timeline.Revisions.SingleOrDefault(item => item.RevisionId == sourceRevisionId)
                ?? throw new ExperienceStateException(
                    ExperienceStateErrorCode.RevisionNotFound,
                    $"Experience revision '{sourceRevisionId}' was not found.");
            if (timeline.Revisions.Any(item => item.ConversationBranchId == targetConversationBranchId))
                throw new ExperienceStateException(
                    ExperienceStateErrorCode.RevisionConflict,
                    $"Conversation branch '{targetConversationBranchId}' already has an Experience revision.");

            var revision = new ExperienceStateRevision(
                Guid.NewGuid(), checked(timeline.Revisions.Max(item => item.RevisionNumber) + 1),
                targetConversationBranchId, source.RevisionId, source.State, transition.Trim(), _clock());
            var updated = timeline with
            {
                Revisions = [.. timeline.Revisions, revision],
                ActiveRevisionId = revision.RevisionId,
                UpdatedAt = revision.CreatedAt
            };
            return (Replace(snapshot, updated), updated);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Points the visible Experience state at the latest revision for a conversation branch.</summary>
    public async Task<ExperienceTimeline> SelectBranchAsync(
        Guid experienceId,
        Guid conversationBranchId,
        CancellationToken cancellationToken = default)
    {
        RequireId(experienceId, nameof(experienceId));
        RequireId(conversationBranchId, nameof(conversationBranchId));

        return await MutateAsync(snapshot =>
        {
            var timeline = Find(snapshot, experienceId);
            var revision = timeline.Revisions
                .Where(item => item.ConversationBranchId == conversationBranchId)
                .MaxBy(item => item.RevisionNumber)
                ?? throw new ExperienceStateException(
                    ExperienceStateErrorCode.BranchNotFound,
                    $"Conversation branch '{conversationBranchId}' has no matching Experience state.");
            var updated = timeline with { ActiveRevisionId = revision.RevisionId, UpdatedAt = _clock() };
            return (Replace(snapshot, updated), updated);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records an externally executed action only after its owning app reports the result. Reusing
    /// an action ID with the same record is idempotent; reusing it for another result is rejected.
    /// </summary>
    public async Task<ExperienceTimeline> RecordExternalEffectAsync(
        Guid experienceId,
        ExperienceExternalEffect effect,
        CancellationToken cancellationToken = default)
    {
        RequireId(experienceId, nameof(experienceId));
        ArgumentNullException.ThrowIfNull(effect);
        RequireId(effect.ActionId, nameof(effect.ActionId));
        RequireId(effect.ConversationBranchId, nameof(effect.ConversationBranchId));
        ArgumentException.ThrowIfNullOrWhiteSpace(effect.TargetApp);
        ArgumentException.ThrowIfNullOrWhiteSpace(effect.ActionName);
        ValidateJson(effect.ResultJson, "external action result");

        return await MutateAsync(snapshot =>
        {
            var timeline = Find(snapshot, experienceId);
            if (!timeline.Revisions.Any(item => item.ConversationBranchId == effect.ConversationBranchId))
                throw new ExperienceStateException(
                    ExperienceStateErrorCode.BranchNotFound,
                    $"Conversation branch '{effect.ConversationBranchId}' has no matching Experience state.");
            var prior = timeline.ExternalEffects.SingleOrDefault(item => item.ActionId == effect.ActionId);
            if (prior is not null)
            {
                if (prior != effect)
                    throw new ExperienceStateException(
                        ExperienceStateErrorCode.RevisionConflict,
                        $"External action ID '{effect.ActionId}' is already recorded with a different result.");
                return (snapshot, timeline);
            }

            var updated = timeline with
            {
                ExternalEffects = [.. timeline.ExternalEffects, effect],
                UpdatedAt = _clock()
            };
            return (Replace(snapshot, updated), updated);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns only state allowed by the Experience's actor visibility policy.</summary>
    public async Task<ExperienceVisibleState> GetVisibleStateAsync(
        Guid experienceId,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        RequireId(actorId, nameof(actorId));
        var timeline = await GetAsync(experienceId, cancellationToken).ConfigureAwait(false)
            ?? throw NotFound(experienceId);
        var active = timeline.ActiveRevision;
        var state = active.State;
        var visibleActorIds = state.Actors
            .Where(actor => IsVisible(actor.Visibility, actor.VisibleToActorIds, actor.ActorId, actorId))
            .Select(actor => actor.ActorId)
            .ToHashSet();
        if (!visibleActorIds.Contains(actorId))
            throw new ExperienceStateException(
                ExperienceStateErrorCode.ActorNotFound,
                $"Actor '{actorId}' is not visible in this Experience.");

        return new ExperienceVisibleState(
            timeline.ExperienceId,
            active.ConversationBranchId,
            active.RevisionId,
            state.Scene,
            state.InWorldTime,
            state.Actors.Where(item => visibleActorIds.Contains(item.ActorId)).ToArray(),
            state.Relationships.Where(item => visibleActorIds.Contains(item.SubjectActorId) && visibleActorIds.Contains(item.TargetActorId)).ToArray(),
            state.Inventory.Where(item => item.OwnerActorId is null || visibleActorIds.Contains(item.OwnerActorId.Value)).ToArray(),
            state.Objectives.Where(item => item.OwnerActorId is null || visibleActorIds.Contains(item.OwnerActorId.Value)).ToArray(),
            state.Flags,
            state.Events.Where(item => item.ActorId is null || visibleActorIds.Contains(item.ActorId.Value)).ToArray(),
            state.PrivateFacts.Where(item => IsVisible(item.Visibility, item.VisibleToActorIds, Guid.Empty, actorId)).ToArray());
    }

    private async Task<TResult> MutateAsync<TResult>(
        Func<ExperienceStateSnapshot, (ExperienceStateSnapshot Snapshot, TResult Result)> mutation,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
            var (next, result) = mutation(current);
            if (!ReferenceEquals(current, next))
                await _settings.SetAsync(SettingsKey, next, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ExperienceStateSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ReadCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task<ExperienceStateSnapshot> ReadCoreAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _settings.GetAsync<ExperienceStateSnapshot>(SettingsKey, cancellationToken).ConfigureAwait(false)
            ?? ExperienceStateSnapshot.Empty;
        if (snapshot.Version != CurrentSchemaVersion)
            throw new InvalidDataException(
                $"Unsupported Experience state schema version {snapshot.Version}; expected {CurrentSchemaVersion}.");
        if (snapshot.Experiences is null)
            throw new InvalidDataException("Experience state storage has no experience collection.");
        return snapshot;
    }

    private static ExperienceTimeline Find(ExperienceStateSnapshot snapshot, Guid experienceId) =>
        snapshot.Experiences.SingleOrDefault(item => item.ExperienceId == experienceId)
        ?? throw NotFound(experienceId);

    private static ExperienceStateSnapshot Replace(ExperienceStateSnapshot snapshot, ExperienceTimeline updated) =>
        snapshot with { Experiences = [.. snapshot.Experiences.Select(item => item.ExperienceId == updated.ExperienceId ? updated : item)] };

    private static void RequireExpectedRevision(
        ExperienceTimeline timeline,
        Guid conversationBranchId,
        Guid expectedRevisionId)
    {
        var current = timeline.ActiveRevision;
        if (current.ConversationBranchId != conversationBranchId || current.RevisionId != expectedRevisionId)
            throw new ExperienceStateException(
                ExperienceStateErrorCode.RevisionConflict,
                $"Experience revision changed. Current revision is '{current.RevisionId}' on branch '{current.ConversationBranchId}'.");
    }

    private static bool IsVisible(
        ExperienceVisibility visibility,
        IReadOnlyList<Guid> visibleToActorIds,
        Guid ownerActorId,
        Guid requestingActorId) => visibility switch
        {
            ExperienceVisibility.Public => true,
            ExperienceVisibility.OwnerOnly => ownerActorId == requestingActorId,
            ExperienceVisibility.SelectedActors => visibleToActorIds.Contains(requestingActorId),
            _ => false
        };

    private static void Validate(ExperienceStateDocument state)
    {
        if (state.Actors is null || state.Relationships is null || state.Inventory is null ||
            state.Objectives is null || state.Flags is null || state.Events is null || state.PrivateFacts is null)
            throw Invalid("Experience state collections cannot be null.");

        var actorIds = new HashSet<Guid>();
        foreach (var actor in state.Actors)
        {
            RequireId(actor.ActorId, nameof(actor.ActorId));
            ArgumentException.ThrowIfNullOrWhiteSpace(actor.Name);
            if (!actorIds.Add(actor.ActorId)) throw Invalid($"Actor ID '{actor.ActorId}' is duplicated.");
            if (actor.PersistentAgentId == Guid.Empty) throw Invalid("Persistent Agent IDs cannot be empty GUIDs.");
            ValidateVisibility(actor.Visibility, actor.VisibleToActorIds);
        }

        foreach (var relationship in state.Relationships)
        {
            RequireActor(actorIds, relationship.SubjectActorId);
            RequireActor(actorIds, relationship.TargetActorId);
            ArgumentException.ThrowIfNullOrWhiteSpace(relationship.Kind);
            ValidateJson(relationship.ValueJson, "relationship value");
        }
        foreach (var item in state.Inventory)
        {
            RequireId(item.ItemId, nameof(item.ItemId));
            ArgumentException.ThrowIfNullOrWhiteSpace(item.Name);
            ValidateJson(item.ValueJson, "inventory value");
            if (item.OwnerActorId is { } owner) RequireActor(actorIds, owner);
        }
        foreach (var objective in state.Objectives)
        {
            RequireId(objective.ObjectiveId, nameof(objective.ObjectiveId));
            ArgumentException.ThrowIfNullOrWhiteSpace(objective.Title);
            ArgumentException.ThrowIfNullOrWhiteSpace(objective.Status);
            if (objective.OwnerActorId is { } owner) RequireActor(actorIds, owner);
        }
        if (state.Flags.Any(item => string.IsNullOrWhiteSpace(item.Key)))
            throw Invalid("Experience flag keys cannot be empty.");
        foreach (var flag in state.Flags) ValidateJson(flag.ValueJson, "flag value");
        foreach (var item in state.Events)
        {
            RequireId(item.EventId, nameof(item.EventId));
            ArgumentException.ThrowIfNullOrWhiteSpace(item.Kind);
            if (item.ActorId is { } actorId) RequireActor(actorIds, actorId);
            ValidateJson(item.PayloadJson, "event payload");
        }
        foreach (var fact in state.PrivateFacts)
        {
            RequireId(fact.FactId, nameof(fact.FactId));
            ArgumentException.ThrowIfNullOrWhiteSpace(fact.Key);
            ValidateJson(fact.ValueJson, "private fact value");
            ValidateVisibility(fact.Visibility, fact.VisibleToActorIds);
            if (fact.VisibleToActorIds.Any(id => !actorIds.Contains(id)))
                throw Invalid($"Private fact '{fact.Key}' references an unknown actor.");
        }
    }

    private static void ValidateVisibility(ExperienceVisibility visibility, IReadOnlyList<Guid> allowedActorIds)
    {
        if (!Enum.IsDefined(visibility)) throw Invalid("Experience visibility value is unknown.");
        if (allowedActorIds is null || allowedActorIds.Any(id => id == Guid.Empty) || allowedActorIds.Distinct().Count() != allowedActorIds.Count)
            throw Invalid("Experience visibility actor IDs must be non-empty and unique.");
        if (visibility == ExperienceVisibility.SelectedActors && allowedActorIds.Count == 0)
            throw Invalid("SelectedActors visibility requires at least one allowed actor.");
        if (visibility != ExperienceVisibility.SelectedActors && allowedActorIds.Count > 0)
            throw Invalid("An actor allow-list is valid only for SelectedActors visibility.");
    }

    private static void RequireActor(HashSet<Guid> actorIds, Guid actorId)
    {
        if (!actorIds.Contains(actorId)) throw Invalid($"Actor '{actorId}' is not present in Experience state.");
    }

    private static void ValidateJson(string json, string field)
    {
        try { using var _ = JsonDocument.Parse(json); }
        catch (JsonException exception) { throw Invalid($"The {field} must contain valid JSON: {exception.Message}"); }
    }

    private static void RequireId(Guid value, string parameter)
    {
        if (value == Guid.Empty) throw new ArgumentException("A stable non-empty ID is required.", parameter);
    }

    private static ExperienceStateException NotFound(Guid id) => new(
        ExperienceStateErrorCode.ExperienceNotFound,
        $"Experience '{id}' was not found.");

    private static ExperienceStateException Invalid(string message) => new(
        ExperienceStateErrorCode.InvalidState,
        message);

    private sealed record ExperienceStateSnapshot(int Version, IReadOnlyList<ExperienceTimeline> Experiences)
    {
        public static ExperienceStateSnapshot Empty { get; } = new(CurrentSchemaVersion, []);
    }
}
