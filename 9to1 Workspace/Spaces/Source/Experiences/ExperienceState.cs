using System.Text.Json;

namespace HavenOS.Apps.Spaces.Experiences;

/// <summary>
/// Canonical structured state for an Experience. Conversation prose is deliberately not used as
/// the authoritative source for these values. Scene and in-world time are public; callers must
/// classify sensitive entry values and payloads with their actual visibility policy.
/// </summary>
public sealed record ExperienceStateDocument(
    string Scene,
    string InWorldTime,
    IReadOnlyList<ExperienceActorState> Actors,
    IReadOnlyList<ExperienceRelationship> Relationships,
    IReadOnlyList<ExperienceInventoryItem> Inventory,
    IReadOnlyList<ExperienceObjective> Objectives,
    IReadOnlyList<ExperienceFlag> Flags,
    IReadOnlyList<ExperienceEvent> Events,
    IReadOnlyList<ExperiencePrivateFact> PrivateFacts)
{
    public static ExperienceStateDocument Empty { get; } = new(
        string.Empty, string.Empty, [], [], [], [], [], [], []);
}

public sealed record ExperienceActorState(
    Guid ActorId,
    string Name,
    Guid? PersistentAgentId,
    ExperienceVisibility Visibility,
    IReadOnlyList<Guid> VisibleToActorIds);

public sealed record ExperienceRelationship(
    Guid SubjectActorId,
    Guid TargetActorId,
    string Kind,
    string ValueJson,
    ExperienceVisibility Visibility = ExperienceVisibility.Public,
    IReadOnlyList<Guid>? VisibleToActorIds = null);

public sealed record ExperienceInventoryItem(
    Guid ItemId,
    string Name,
    string ValueJson,
    Guid? OwnerActorId,
    ExperienceVisibility Visibility = ExperienceVisibility.Public,
    IReadOnlyList<Guid>? VisibleToActorIds = null);

public sealed record ExperienceObjective(
    Guid ObjectiveId,
    string Title,
    string Status,
    Guid? OwnerActorId,
    ExperienceVisibility Visibility = ExperienceVisibility.Public,
    IReadOnlyList<Guid>? VisibleToActorIds = null);

public sealed record ExperienceFlag(
    string Key,
    string ValueJson,
    ExperienceVisibility Visibility = ExperienceVisibility.Public,
    IReadOnlyList<Guid>? VisibleToActorIds = null);

/// <summary>A committed typed event. Narrative text may describe it but does not replace it.</summary>
public sealed record ExperienceEvent(
    Guid EventId,
    string Kind,
    Guid? ActorId,
    string PayloadJson,
    DateTimeOffset RecordedAt,
    ExperienceVisibility Visibility = ExperienceVisibility.Public,
    IReadOnlyList<Guid>? VisibleToActorIds = null);

/// <summary>
/// Visibility controls the fact's audience. OwnerOnly facts belong to the experience owner and
/// are not included in actor projections; SelectedActors facts require an explicit audience.
/// </summary>
public sealed record ExperiencePrivateFact(
    Guid FactId,
    string Key,
    string ValueJson,
    ExperienceVisibility Visibility,
    IReadOnlyList<Guid> VisibleToActorIds);

public enum ExperienceVisibility
{
    Public = 0,
    // Actor entries use their actor; relationships use the subject; owned items/objectives use
    // their owner and events their actor. Without such an actor, OwnerOnly is private to the user.
    OwnerOnly = 1,
    SelectedActors = 2
}

public sealed record ExperienceStateRevision(
    Guid RevisionId,
    long RevisionNumber,
    Guid ConversationBranchId,
    Guid? ParentRevisionId,
    ExperienceStateDocument State,
    string Transition,
    DateTimeOffset CreatedAt,
    Guid? ActionId = null);

/// <summary>The durable revision created by an action, including when an identical request is replayed.</summary>
public sealed record ExperienceActionCommit(ExperienceStateRevision Revision, bool WasReplay);

/// <summary>
/// A real externally executed action remains in this history when simulated state is branched
/// or rewound. This record observes the result; it does not execute the external action.
/// </summary>
public sealed record ExperienceExternalEffect(
    Guid ActionId,
    Guid ConversationBranchId,
    string TargetApp,
    string ActionName,
    string ResultJson,
    DateTimeOffset ObservedAt);

public sealed record ExperienceTimeline(
    Guid ExperienceId,
    Guid SpaceId,
    Guid ConversationId,
    string Title,
    IReadOnlyList<ExperienceStateRevision> Revisions,
    Guid ActiveRevisionId,
    IReadOnlyList<ExperienceExternalEffect> ExternalEffects,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public ExperienceStateRevision ActiveRevision =>
        Revisions.Single(revision => revision.RevisionId == ActiveRevisionId);
}

/// <summary>A redacted read-only projection, not an editable replacement for canonical state.</summary>
public sealed record ExperienceVisibleState(
    Guid ExperienceId,
    Guid ConversationBranchId,
    Guid RevisionId,
    string Scene,
    string InWorldTime,
    IReadOnlyList<ExperienceActorState> Actors,
    IReadOnlyList<ExperienceRelationship> Relationships,
    IReadOnlyList<ExperienceInventoryItem> Inventory,
    IReadOnlyList<ExperienceObjective> Objectives,
    IReadOnlyList<ExperienceFlag> Flags,
    IReadOnlyList<ExperienceEvent> Events,
    IReadOnlyList<ExperiencePrivateFact> VisiblePrivateFacts);

public enum ExperienceStateErrorCode
{
    ExperienceNotFound,
    RevisionConflict,
    BranchNotFound,
    InvalidState,
    RevisionNotFound,
    ActorNotFound
}

public sealed class ExperienceStateException(
    ExperienceStateErrorCode code,
    string message) : InvalidOperationException(message)
{
    public ExperienceStateErrorCode Code { get; } = code;
}
