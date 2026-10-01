namespace NineToOne.Accounts.Admission;

// Policy changes require explicit authority migration; mismatch denies, never resets budget.
// Server-only port: transport-derived keyed partition, authoritative clock and configuration.
// Serialized decisions are neither authentication nor a Home authority grant.
public enum AdmissionStatus { Reserved, Limited, Unavailable, CompletionUnknown }
public sealed record AttemptPolicy(int Maximum, TimeSpan Window, TimeSpan Lockout)
{
    public static AttemptPolicy RequestedBaseline => new(8, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30));
    public bool IsValid => Maximum > 0 && Window > TimeSpan.Zero && Lockout >= Window;
}
public sealed record AttemptState(DateTimeOffset WindowStarted, int ReservedCount, DateTimeOffset? LockedUntil, DateTimeOffset LastObserved, string PolicyFingerprint);
public sealed record AttemptTransition(AdmissionStatus Status, AttemptState State, TimeSpan? RetryAfter);
public static class AttemptPolicyReducer
{
    // The backing authority MUST serialize read/reduce/write atomically globally.
    // Every expensive attempt counts, regardless of whether the identifier exists.
    // Denied traffic does not extend lockout. No success callback resets reservations.
    public static AttemptTransition Reserve(AttemptState? prior, AttemptPolicy policy, DateTimeOffset now)
    {
        var fingerprint = FormattableString.Invariant($"{policy.Maximum}:{policy.Window.Ticks}:{policy.Lockout.Ticks}");
        if (!policy.IsValid || prior is { ReservedCount: < 0 } ||
            (prior is not null && prior.PolicyFingerprint != fingerprint))
            return new(AdmissionStatus.Unavailable, prior ?? new(now, 0, null, now, fingerprint), null);
        if (prior is not null && (now < prior.LastObserved || now < prior.WindowStarted || prior.LastObserved < prior.WindowStarted))
            return new(AdmissionStatus.Unavailable, prior, null);
        if (prior?.LockedUntil is { } until && until > now)
            return new(AdmissionStatus.Limited, prior with { LastObserved = now }, until - now);
        var state = prior is null || now - prior.WindowStarted >= policy.Window
            ? new AttemptState(now, 0, null, now, fingerprint) : prior with { LockedUntil = null };
        if (state.ReservedCount >= policy.Maximum)
            return new(AdmissionStatus.Limited, state, policy.Window - (now - state.WindowStarted));
        var count = checked(state.ReservedCount + 1);
        DateTimeOffset? lockedUntil = null;
        try { if (count == policy.Maximum) lockedUntil = now + policy.Lockout; }
        catch (ArgumentOutOfRangeException) { return new(AdmissionStatus.Unavailable, state, null); }
        var next = state with { ReservedCount = count, LockedUntil = lockedUntil, LastObserved = now };
        return new(AdmissionStatus.Reserved, next, null);
    }
}
public sealed record AdmissionRequest(string ServerDerivedPartitionDigest, string KeyVersion, string OperationNonce);
public sealed record AdmissionDecision(AdmissionStatus Status, string? ServerReceipt, int? RetryAfterSeconds);
public interface ICakePreverificationAdmission
{
    // Called BEFORE credential-row lookup/hash verification/session creation.
    // Implementation authenticates trusted transport/proxy context, derives identifier+IP HMAC,
    // validates configured bounds, and atomically reserves rate+approved cost budget.
    // Same nonce must not admit another costly invocation; unknown outcomes are recovered by receipt.
    ValueTask<AdmissionDecision> ReserveAsync(AdmissionRequest request, CancellationToken cancellationToken);
}
public enum UsageDimension { WorkerIngressRequests, D1RowsExamined, D1RowsWritten, D1StorageBytes, CpuMilliseconds }
public sealed record UsageBudget(UsageDimension Dimension, long? ApprovedLimit, long? ReservedRevocationBudget,
    string? MeasurementScope, DateTimeOffset? VerifiedAt);
public interface ICakeAdmissionReporting
{
    // Account-wide scope and other Workers consumption must be explicit; result-row count is not D1 read cost.
    // Per-isolate counters cannot implement globally authoritative admission.
    ValueTask<IReadOnlyList<UsageBudget>> ReadAsync(CancellationToken cancellationToken);
}
