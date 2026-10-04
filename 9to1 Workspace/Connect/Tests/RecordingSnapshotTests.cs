using System.Text.Json;
using HavenOS.Connect.Calls;

internal static class RecordingSnapshotTests
{
    public static void Run()
    {
        var owner = Guid.NewGuid();
        var second = Guid.NewGuid();
        var newcomer = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var request = RecordingConsent.Request(Guid.NewGuid(), owner, [owner, second], now);

        // A second approval must come from that participant's decision, even when the
        // consumer can cast a returned read-only interface to a mutable collection.
        ExpectReadOnly(request.Participants, 1, new(second, RecordingDecision.Approved, now));
        var awaiting = Approve(request, owner, now);
        ExpectReadOnly(awaiting.Participants, 1, new(second, RecordingDecision.Approved, now));
        var stillAwaiting = Approve(awaiting, owner, now);
        Expect(!RecordingConsent.MayIncludeMedia(stillAwaiting, owner), "A caller cannot forge another participant's approval.");
        Expect(request.Revision == 1 && request.Participants.All(p => p.Decision == RecordingDecision.Pending),
            "A decision must preserve the prior consent revision.");

        var active = Approve(awaiting, second, now);
        ExpectReadOnly(active.Participants, 1, new(newcomer, RecordingDecision.Approved, now));
        Expect(!RecordingConsent.MayIncludeMedia(active, newcomer), "An outsider needs a trusted membership event and their own consent.");

        var stopped = RecordingConsent.Stop(active, owner, active.Revision, now).Session!;
        ExpectReadOnly(stopped.Participants, 1, new(second, RecordingDecision.Declined, now));
        Expect(RecordingConsent.MayIncludeMedia(active, second), "Stopping must preserve the prior active consent snapshot.");
        Expect(!RecordingConsent.MayIncludeMedia(stopped, owner), "Stopped sessions cannot include media.");

        // Hosts restoring records must not retain mutable storage from a caller or
        // deserializer. The same applies to the record's public init properties.
        var participantInput = new[] { new RecordingParticipant(owner, RecordingDecision.Pending) };
        var auditInput = new[] { new RecordingAuditEvent(Guid.NewGuid(), "RecordingRequested", owner, now) };
        var restored = new RecordingSession(Guid.NewGuid(), request.CallId, owner, RecordingState.AwaitingConsent,
            1, participantInput, auditInput);
        participantInput[0] = participantInput[0] with { Decision = RecordingDecision.Approved };
        auditInput[0] = auditInput[0] with { Kind = "ForgedApproval" };
        Expect(restored.Participants[0].Decision == RecordingDecision.Pending, "Constructor input aliases cannot change consent.");
        Expect(restored.Audit[0].Kind == "RecordingRequested", "Constructor input aliases cannot rewrite audit history.");

        var replacementParticipants = new List<RecordingParticipant> { new(owner, RecordingDecision.Pending) };
        var replacementAudit = new List<RecordingAuditEvent> { restored.Audit[0] };
        var copy = restored with { Participants = replacementParticipants, Audit = replacementAudit };
        replacementParticipants[0] = replacementParticipants[0] with { Decision = RecordingDecision.Approved };
        replacementAudit.Clear();
        Expect(copy.Participants[0].Decision == RecordingDecision.Pending && copy.Audit.Count == 1,
            "Record-copy input aliases cannot change a revision's consent or audit.");
        ExpectReadOnly(copy.Participants, 0, new(owner, RecordingDecision.Approved, now));
        ExpectReadOnly(copy.Audit, 0, copy.Audit[0] with { Kind = "ForgedApproval" });

        var roundTrip = JsonSerializer.Deserialize<RecordingSession>(JsonSerializer.Serialize(active))!;
        Expect(roundTrip.RecordingId == active.RecordingId && roundTrip.CallId == active.CallId &&
            roundTrip.RequesterId == active.RequesterId && roundTrip.Revision == active.Revision &&
            roundTrip.State == active.State && roundTrip.ArtifactId == active.ArtifactId &&
            roundTrip.Participants.SequenceEqual(active.Participants) && roundTrip.Audit.SequenceEqual(active.Audit),
            "Serialization must preserve the existing record shape, identities, approvals and audit.");
        ExpectReadOnly(roundTrip.Participants, 1, new(newcomer, RecordingDecision.Approved, now));
        ExpectReadOnly(roundTrip.Audit, 0, roundTrip.Audit[0] with { Kind = "ForgedApproval" });
        var (_, _, _, _, _, deconstructedParticipants, deconstructedAudit, _) = roundTrip;
        ExpectReadOnly(deconstructedParticipants, 1, new(newcomer, RecordingDecision.Approved, now));
        ExpectReadOnly(deconstructedAudit, 0, deconstructedAudit[0] with { Kind = "ForgedApproval" });

        Console.WriteLine("Connect recording-consent snapshot regressions passed.");
    }

    private static RecordingSession Approve(RecordingSession session, Guid participant, DateTimeOffset now) =>
        RecordingConsent.Decide(session, participant, RecordingDecision.Approved, session.Revision, now).Session!;

    private static void ExpectReadOnly<T>(IReadOnlyList<T> values, int index, T replacement)
    {
        if (values is not IList<T> mutable) return;
        try
        {
            mutable[index] = replacement;
        }
        catch (NotSupportedException)
        {
            return;
        }
        throw new InvalidOperationException("A consent or audit collection allowed mutation outside its revisioned transitions.");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
