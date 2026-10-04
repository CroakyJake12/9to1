namespace HavenOS.Connect.Calls;

public enum RecordingState { Disabled, AwaitingConsent, Active, PausedForConsent, Declined, Stopped }
public enum RecordingDecision { Pending, Approved, Declined }
public sealed record RecordingParticipant(Guid ParticipantId, RecordingDecision Decision, DateTimeOffset? DecidedAt = null);
public sealed record RecordingAuditEvent(Guid EventId, string Kind, Guid? ActorId, DateTimeOffset At);
public sealed record RecordingSession(Guid RecordingId, Guid CallId, Guid RequesterId, RecordingState State,
    long Revision, IReadOnlyList<RecordingParticipant> Participants, IReadOnlyList<RecordingAuditEvent> Audit,
    Guid? ArtifactId = null)
{
    // Consent and membership belong to a revision. Never retain caller-owned mutable storage,
    // including when a host restores a session or assigns these properties with a record copy.
    private readonly IReadOnlyList<RecordingParticipant> _participants = Array.AsReadOnly(Participants.ToArray());
    private readonly IReadOnlyList<RecordingAuditEvent> _audit = Array.AsReadOnly(Audit.ToArray());

    public IReadOnlyList<RecordingParticipant> Participants
    {
        get => _participants;
        init => _participants = Array.AsReadOnly(value.ToArray());
    }

    public IReadOnlyList<RecordingAuditEvent> Audit
    {
        get => _audit;
        init => _audit = Array.AsReadOnly(value.ToArray());
    }
}
public sealed record RecordingError(string Code, string Message, Guid CallId, bool Recoverable);
public sealed record RecordingResult(RecordingSession? Session, RecordingError? Error)
{
    public bool IsSuccess => Error is null;
}

/// <summary>Pure canonical recording transitions; host persists each revision before including any media.</summary>
public static class RecordingConsent
{
    public static RecordingSession Request(Guid callId, Guid requesterId, IReadOnlyList<Guid> participants, DateTimeOffset now)
    {
        if (callId == Guid.Empty || requesterId == Guid.Empty || participants.Count == 0 || participants.Any(id => id == Guid.Empty) ||
            participants.Distinct().Count() != participants.Count || !participants.Contains(requesterId))
            throw new ArgumentException("Call, requester and distinct active participant identities are required.");
        return new(Guid.NewGuid(), callId, requesterId, RecordingState.AwaitingConsent, 1,
            participants.Select(id => new RecordingParticipant(id, RecordingDecision.Pending)).ToArray(),
            [new(Guid.NewGuid(), "RecordingRequested", requesterId, now)]);
    }

    public static RecordingResult Decide(RecordingSession session, Guid authenticatedParticipantId, RecordingDecision decision,
        long expectedRevision, DateTimeOffset now)
    {
        if (session.Revision != expectedRevision) return Fail(session, "RevisionConflict", "Recording consent changed; inspect the current request.");
        if (decision == RecordingDecision.Pending || !Enum.IsDefined(decision)) return Fail(session, "InvalidDecision", "Explicit approve or decline is required.");
        if (session.State is not (RecordingState.AwaitingConsent or RecordingState.PausedForConsent)) return Fail(session, "InvalidState", "This consent request is no longer open.");
        var participant = session.Participants.FirstOrDefault(p => p.ParticipantId == authenticatedParticipantId);
        if (participant is null) return Fail(session, "MembershipDenied", "Only current call participants can decide their own consent.");
        var participants = session.Participants.Select(p => p.ParticipantId == authenticatedParticipantId ? p with { Decision = decision, DecidedAt = now } : p).ToArray();
        var state = decision == RecordingDecision.Declined ? RecordingState.Declined :
            participants.All(p => p.Decision == RecordingDecision.Approved) ? RecordingState.Active : session.State;
        return Success(session, participants, state, "RecordingConsent" + decision, authenticatedParticipantId, now);
    }

    public static RecordingResult ParticipantJoined(RecordingSession session, Guid participantId, long expectedRevision, DateTimeOffset now)
    {
        if (session.Revision != expectedRevision) return Fail(session, "RevisionConflict", "Call participants changed.");
        if (participantId == Guid.Empty) return Fail(session, "InvalidParticipant", "A stable participant identity is required.");
        if (session.Participants.Any(p => p.ParticipantId == participantId)) return new(session, null);
        var state = session.State == RecordingState.Active ? RecordingState.PausedForConsent : session.State;
        return Success(session, [.. session.Participants, new(participantId, RecordingDecision.Pending)], state, "ParticipantConsentPending", participantId, now);
    }

    public static RecordingResult Withdraw(RecordingSession session, Guid authenticatedParticipantId,
        long expectedRevision, DateTimeOffset now)
    {
        if (session.Revision != expectedRevision) return Fail(session, "RevisionConflict", "Recording consent changed.");
        if (session.State is RecordingState.Disabled or RecordingState.Stopped or RecordingState.Declined)
            return Fail(session, "InvalidState", "Recording is already disabled.");
        if (!session.Participants.Any(p => p.ParticipantId == authenticatedParticipantId))
            return Fail(session, "MembershipDenied", "Only current participants can withdraw their own consent.");
        var participants = session.Participants.Select(p => p.ParticipantId == authenticatedParticipantId
            ? p with { Decision = RecordingDecision.Declined, DecidedAt = now } : p).ToArray();
        return Success(session, participants, RecordingState.Declined, "RecordingConsentWithdrawn", authenticatedParticipantId, now);
    }

    /// <summary>Trusted call membership event; media must stop for departed identities before this transition commits.</summary>
    public static RecordingResult ParticipantLeft(RecordingSession session, Guid departedParticipantId,
        long expectedRevision, DateTimeOffset now)
    {
        if (session.Revision != expectedRevision) return Fail(session, "RevisionConflict", "Call membership changed.");
        if (!session.Participants.Any(p => p.ParticipantId == departedParticipantId)) return new(session, null);
        var participants = session.Participants.Where(p => p.ParticipantId != departedParticipantId).ToArray();
        var state = participants.Length == 0 ? RecordingState.Stopped : session.State;
        // Departure alone never constitutes approval or resumes a paused recording.
        return Success(session, participants, state, "RecordingParticipantLeft", departedParticipantId, now);
    }

    public static bool MayIncludeMedia(RecordingSession session, Guid participantId) => session.State == RecordingState.Active &&
        session.Participants.All(p => p.Decision == RecordingDecision.Approved) && session.Participants.Any(p => p.ParticipantId == participantId);

    public static RecordingResult Stop(RecordingSession session, Guid authenticatedParticipantId, long expectedRevision, DateTimeOffset now)
    {
        if (session.Revision != expectedRevision) return Fail(session, "RevisionConflict", "Recording state changed.");
        if (!session.Participants.Any(p => p.ParticipantId == authenticatedParticipantId)) return Fail(session, "MembershipDenied", "Only call participants can stop recording.");
        return Success(session, session.Participants, RecordingState.Stopped, "RecordingStopped", authenticatedParticipantId, now);
    }

    private static RecordingResult Success(RecordingSession session, IReadOnlyList<RecordingParticipant> participants,
        RecordingState state, string kind, Guid actor, DateTimeOffset now) => new(session with
        {
            Participants = participants, State = state, Revision = checked(session.Revision + 1),
            Audit = [.. session.Audit, new(Guid.NewGuid(), kind, actor, now)]
        }, null);
    private static RecordingResult Fail(RecordingSession session, string code, string message) => new(null, new(code, message, session.CallId, code == "RevisionConflict"));
}
