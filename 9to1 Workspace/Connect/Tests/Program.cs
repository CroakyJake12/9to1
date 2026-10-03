using HavenOS.Connect.Calls;

var owner = Guid.NewGuid();
var second = Guid.NewGuid();
var newcomer = Guid.NewGuid();
var now = DateTimeOffset.UtcNow;
var recording = RecordingConsent.Request(Guid.NewGuid(), owner, [owner, second], now);
Assert(!RecordingConsent.MayIncludeMedia(recording, owner), "Request must not begin recording.");
recording = RecordingConsent.Decide(recording, owner, RecordingDecision.Approved, recording.Revision, now).Session!;
Assert(!RecordingConsent.MayIncludeMedia(recording, owner), "Unanimous consent required.");
Assert(!RecordingConsent.Decide(recording, newcomer, RecordingDecision.Approved, recording.Revision, now).IsSuccess, "Outsider cannot consent.");
recording = RecordingConsent.Decide(recording, second, RecordingDecision.Approved, recording.Revision, now).Session!;
Assert(RecordingConsent.MayIncludeMedia(recording, owner), "Unanimous approval enables media.");
recording = RecordingConsent.ParticipantJoined(recording, newcomer, recording.Revision, now).Session!;
Assert(recording.State == RecordingState.PausedForConsent, "New participant pauses entire capture until consent.");
Assert(!RecordingConsent.MayIncludeMedia(recording, owner), "Existing participant media must pause too.");
recording = RecordingConsent.Decide(recording, newcomer, RecordingDecision.Declined, recording.Revision, now).Session!;
Assert(recording.State == RecordingState.Declined && !RecordingConsent.MayIncludeMedia(recording, newcomer), "Decline prevents recording.");
Assert(RecordingConsent.Decide(recording, newcomer, RecordingDecision.Approved, recording.Revision, now).Error!.Code == "InvalidState", "Declined session cannot silently restart.");
var fresh = RecordingConsent.Request(Guid.NewGuid(), owner, [owner, second], now);
fresh = RecordingConsent.Decide(fresh, owner, RecordingDecision.Approved, fresh.Revision, now).Session!;
fresh = RecordingConsent.Decide(fresh, second, RecordingDecision.Approved, fresh.Revision, now).Session!;
fresh = RecordingConsent.ParticipantLeft(fresh, second, fresh.Revision, now).Session!;
Assert(!RecordingConsent.MayIncludeMedia(fresh, second), "Departed participant cannot remain included.");
fresh = RecordingConsent.Withdraw(fresh, owner, fresh.Revision, now).Session!;
Assert(!RecordingConsent.MayIncludeMedia(fresh, owner) && fresh.State == RecordingState.Declined, "Withdrawing active consent immediately disables capture.");
Console.WriteLine("Connect recording-consent transitions passed.");
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
