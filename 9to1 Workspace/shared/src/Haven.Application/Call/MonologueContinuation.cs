namespace Haven.Application.Call;

/// <summary>Detached original canonical playback checkpoint, not audio, tool or SourceRefs access authority.
/// Position is the offset within CurrentSection; completed earlier sections remain in the outline for context.</summary>
public sealed record MonologueContinuation(Guid SessionId, Guid ConversationId, Guid? SpaceId,
    long Revision, Guid RunId, string Objective, TimeSpan? TargetDuration,
    IReadOnlyList<string> Sections, int CurrentSection, TimeSpan Position,
    IReadOnlyList<string> SourceRefs);
