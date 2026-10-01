using NineToOne.Accounts.Admission;

// Fictional in-memory serialization fixture, NOT a distributed limiter/backend.
internal static class PreverificationAdmissionSpecs
{
    public static async Task RunAsync()
    {
        var gate = new object();
        AttemptState? state = null;
        var now = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        var policy = AttemptPolicy.RequestedBaseline;
        var expensiveCalls = 0;
        var denied = 0;
        await Task.WhenAll(Enumerable.Range(0, 700).Select(_ => Task.Run(() =>
        {
            AttemptTransition decision;
            lock (gate)
            {
                decision = AttemptPolicyReducer.Reserve(state, policy, now);
                state = decision.State;
            }
            if (decision.Status == AdmissionStatus.Reserved)
                Interlocked.Increment(ref expensiveCalls); // Fictional verifier boundary only.
            else if (decision.Status == AdmissionStatus.Limited)
                Interlocked.Increment(ref denied);
            else throw new InvalidOperationException("Unexpected fixture admission status");
        })));
        Require(expensiveCalls == 8 && denied == 692, "700 concurrent attempts must bound costly work");
        var expiry = state!.LockedUntil;
        var rejected = AttemptPolicyReducer.Reserve(state, policy, now.AddMinutes(20));
        Require(rejected.Status == AdmissionStatus.Limited && rejected.State.LockedUntil == expiry,
            "Denied traffic cannot extend lockout");
        var fresh = AttemptPolicyReducer.Reserve(state, policy, now.AddMinutes(30));
        Require(fresh.Status == AdmissionStatus.Reserved && fresh.State.ReservedCount == 1,
            "Expiry permits a fresh bounded window");
        var otherPartition = AttemptPolicyReducer.Reserve(null, policy, now);
        Require(otherPartition.Status == AdmissionStatus.Reserved, "Independent partition");
        var invalid = AttemptPolicyReducer.Reserve(null, new(0, policy.Window, policy.Lockout), now);
        Require(invalid.Status == AdmissionStatus.Unavailable, "Missing/invalid policy fails closed");
        var advanced = AttemptPolicyReducer.Reserve(null, policy, now).State;
        advanced = AttemptPolicyReducer.Reserve(advanced, policy, now.AddMinutes(10)).State;
        Require(AttemptPolicyReducer.Reserve(advanced, policy, now.AddMinutes(5)).Status == AdmissionStatus.Unavailable, "Within-window clock regression");
        Require(AttemptPolicyReducer.Reserve(advanced, policy with { Maximum = 1 }, now.AddMinutes(10)).Status == AdmissionStatus.Unavailable, "Policy change requires explicit authority migration");
        var nearMax = AttemptPolicyReducer.Reserve(null, new(1, policy.Window, policy.Lockout), DateTimeOffset.MaxValue);
        Require(nearMax.Status == AdmissionStatus.Unavailable, "Overflow must deny without throwing");
        var malformed = advanced with { LastObserved = advanced.WindowStarted.AddSeconds(-1) };
        Require(AttemptPolicyReducer.Reserve(malformed, policy, now).Status == AdmissionStatus.Unavailable, "Malformed state coherence");
        var backwards = AttemptPolicyReducer.Reserve(state, policy, now.AddSeconds(-1));
        Require(backwards.Status == AdmissionStatus.Unavailable, "Clock regression fails closed");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
