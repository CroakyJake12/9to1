namespace Haven.Application;

public sealed record PrivacyPreferences(
    bool LocalOnlyMode,
    bool BackgroundLearningEnabled,
    bool ModelImprovementSharingEnabled,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// Learning consent permits local derivation and storage only. A separate opt-in is required
    /// before learned context may be disclosed to a remote provider.
    /// </summary>
    public bool BackgroundLearningCloudDisclosureEnabled { get; init; }

    /// <summary>Contributor and sync scope for local-first background learning.</summary>
    public BackgroundLearningContributorPolicy BackgroundLearningPolicy { get; init; } = new();

    public static PrivacyPreferences Default { get; } = new(
        LocalOnlyMode: false,
        BackgroundLearningEnabled: false,
        ModelImprovementSharingEnabled: false,
        DateTimeOffset.UnixEpoch);
}

public sealed record BackgroundLearningContributorPolicy(
    bool RestrictApps = false,
    IReadOnlyList<string>? AllowedAppIds = null,
    bool RestrictProjects = false,
    IReadOnlyList<string>? AllowedProjectIds = null,
    bool AllowCrossDeviceSync = false)
{
    public bool Allows(string? appId, string? projectId)
    {
        if (RestrictApps && (string.IsNullOrWhiteSpace(appId) ||
            !(AllowedAppIds ?? []).Contains(appId, StringComparer.OrdinalIgnoreCase))) return false;
        if (RestrictProjects && (string.IsNullOrWhiteSpace(projectId) ||
            !(AllowedProjectIds ?? []).Contains(projectId, StringComparer.OrdinalIgnoreCase))) return false;
        return true;
    }
}

public interface IPrivacyPreferenceStore
{
    PrivacyPreferences Current { get; }
    Task UpdateAsync(PrivacyPreferences preferences, CancellationToken cancellationToken);
}
