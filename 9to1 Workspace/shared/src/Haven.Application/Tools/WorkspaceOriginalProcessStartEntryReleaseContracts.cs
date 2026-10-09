namespace Haven.Application;

/// <summary>Optional cleanup of the SAME separately issued Home/native process-start entry.
/// This interface grants no process/tool/domain permission. Physical callers first require
/// their actual final fence issuer, original task/call and exact request binding.
/// The implementing owner publishes and retains its SAME complete release driver before
/// callbacks, retains each actual resource close once, and joins independent raw causes.
/// No cancellation token permits abandoning this original cleanup.
/// Ordinary fences without a separate entry retain their existing attempt-pin behavior.</summary>
public interface IWorkspaceOriginalProcessStartEntryReleaseFence
{
    // Pure own live/physical ancestry preflight before joining even an existing release.
    // No policy/profile/store/native read, request, cancellation or borrowed-owner recursion.
    void DemandExternalOriginalProcessStartEntryJoin();
    // Invoke in the physical finite Start finally, including failed/never-started cases,
    // outside native/central/Home gates. Retain/join the actual returned Task before
    // subsequent async process wait or app/observer callbacks. Exact arguments observe
    // the original caller binding; they cannot independently issue a cleanup or grant.
    Task ReleaseOriginalProcessStartEntryAsync(string canonicalWorkspaceRoot,
        string canonicalTarget, string exactOriginalProcessRequestSha256);
}
