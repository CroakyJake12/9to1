namespace Haven.Application.Haven;

/// <summary>
/// Genuine, optional summary of the current selected principal as resolved by the real Home
/// owner. Null fields mean unknown/absent, never fabricated values. A generic icon is used
/// only when no avatar source is supplied by the owner.
/// </summary>
public sealed record HavenProfileSummary(
    string? DisplayName,
    string? ProfileId,
    string? AvatarKey);

/// <summary>
/// Genuine, optional storage quota snapshot. Only present when the real Home owner can resolve
/// actual quota state; absent means unknown/not-relevant, never a fabricated zero.
/// </summary>
public sealed record HavenStorageQuota(
    long UsedBytes,
    long QuotaBytes,
    string Label)
{
    /// <summary>True only when both sides are genuine, non-negative and the quota is non-zero.</summary>
    public bool IsRenderable => UsedBytes >= 0 && QuotaBytes > 0;
}

/// <summary>
/// One genuine usage window (e.g. rolling 5-hour or weekly) with actual measured units against a
/// real limit. Absent/empty means unknown/not-relevant; the presenter never invents a window.
/// </summary>
public sealed record HavenUsageWindow(
    string Name,
    long UsedUnits,
    long LimitUnits,
    string Unit)
{
    public bool IsRenderable => UsedUnits >= 0 && LimitUnits > 0 && !string.IsNullOrWhiteSpace(Unit);
}

/// <summary>
/// Optional typed Home-owned profile/usage/automation data contract supplied by a real host to a
/// borrowed surface. A host that does not own these capabilities omits the interface entirely (or
/// returns null members); the Browse surface then renders a truthful unavailable state and never
/// invents account/quota/user/automation data.
/// </summary>
public interface IHavenProfileDataContext
{
    /// <summary>Genuine current-principal summary, or null when the host cannot resolve one.</summary>
    HavenProfileSummary? Profile { get; }

    /// <summary>Genuine storage quota, or null when not available/relevant for this surface.</summary>
    HavenStorageQuota? Storage { get; }

    /// <summary>Genuine usage windows, or empty when not available/relevant for this surface.</summary>
    IReadOnlyList<HavenUsageWindow> UsageWindows { get; }

    /// <summary>Typed gateway to the genuine Home automation sidebar/library, or null when the
    /// host does not own an automation surface for this session.</summary>
    IHavenAutomationControlGateway? Automations { get; }
}

/// <summary>
/// Typed gateway that opens the genuine Home-owned automation sidebar/library as a retained
/// operation. It mirrors the shared-panel open contract: it never fabricates success and returns
/// the actual settlement so close joins real show/init/account faults and refusals.
/// </summary>
public interface IHavenAutomationControlGateway
{
    Task<HavenSharedOpenResult> OpenAsync(CancellationToken cancellationToken = default);
}
