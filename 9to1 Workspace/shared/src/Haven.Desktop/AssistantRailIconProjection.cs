#if !ANDROID
using HavenOS.Apps.Assistants.Contracts;

namespace Haven.Desktop;

/// <summary>States of the Assistants rail-icon read. No-access/unavailable are kept distinct
/// from "none configured" and from "usage not recorded", so the UI never conflates a genuine
/// read failure with a truthful empty configuration or with a missing usage aggregate.</summary>
public enum AssistantRailIconState
{
    /// <summary>An exact selection was resolved from genuine authorised reads.</summary>
    Available,

    /// <summary>Authorised reads prove zero configured Assistants; the generic icon is used.</summary>
    NoneConfigured,

    /// <summary>The authorised configured-Assistant read could not be performed (access denied /
    /// unavailable). It is NOT a truthful "none".</summary>
    Unavailable,

    /// <summary>Configured Assistants exist, but no genuine canonical usage aggregate is recorded
    /// and none is running, so a most-used selection cannot be made truthfully.</summary>
    UsageNotRecorded
}

/// <summary>A resolved Assistants rail-icon selection. Only <see cref="Kind"/> == "Running" or
/// "MostUsed" carry an identity; "Generic" is the owner fallback. Stable identity + revision are
/// carried so a selection is never a name-based or first-item-as-running inference.</summary>
public readonly record struct AssistantRailIconSelection(
    string Kind,
    AssistantIdentity? Identity,
    long Revision,
    string? IconResourceId)
{
    public static AssistantRailIconSelection Generic => new("Generic", null, 0, null);
}

/// <summary>The result of an Assistants rail-icon read. When <see cref="State"/> is not
/// <see cref="AssistantRailIconState.Available"/>, <see cref="Selection"/> is generic; callers
/// distinguish the state (none vs unavailable vs usage-not-recorded) rather than inventing one.</summary>
public readonly record struct AssistantRailIconReadResult(AssistantRailIconState State, AssistantRailIconSelection Selection)
{
    public static AssistantRailIconReadResult NoneConfigured => new(AssistantRailIconState.NoneConfigured, AssistantRailIconSelection.Generic);
    public static AssistantRailIconReadResult Unavailable(string code, string message) => new(AssistantRailIconState.Unavailable, AssistantRailIconSelection.Generic);
    public static AssistantRailIconReadResult UsageNotRecorded => new(AssistantRailIconState.UsageNotRecorded, AssistantRailIconSelection.Generic);
    public static AssistantRailIconReadResult Running(AssistantDefinitionSnapshot definition) =>
        new(AssistantRailIconState.Available, new("Running", definition.Identity, definition.Revision, definition.Configuration.IconResourceId));
    public static AssistantRailIconReadResult MostUsed(AssistantDefinitionSnapshot definition) =>
        new(AssistantRailIconState.Available, new("MostUsed", definition.Identity, definition.Revision, definition.Configuration.IconResourceId));
}

/// <summary>
/// Genuine, bounded reads behind the Assistants rail-icon projection. Implementations must read
/// real authorised state only; they must never fabricate running or usage data. When a source is
/// not composed for a given concern it returns null (or throws the typed access denial), which the
/// projection surfaces as <see cref="AssistantRailIconState.UsageNotRecorded"/> or
/// <see cref="AssistantRailIconState.Unavailable"/> rather than inventing " most-used assistant.
/// </summary>
public interface IAssistantRailIconSource
{
    /// <summary>Authorised configured-Assistant catalogue read (e.g. bridge ListAsync). Returns
    /// null when unavailable; throws on access denial.</summary>
    Task<AssistantCatalogueObservation?> ReadConfiguredCatalogueAsync(CancellationToken token);

    /// <summary>True current running-Assistant read from running Task/Den execution ownership.
    /// Return the configured assistant that actually owns a running canonical Task/Den execution,
    /// or null when none is running or the running read is not composed.</summary>
    Task<AssistantDefinitionSnapshot?> ReadCurrentRunningAsync(
        IReadOnlyList<AssistantDefinitionSnapshot> configured, CancellationToken token);

    /// <summary>Most-used configured-Assistant read from a genuine canonical usage aggregate.
    /// Return the configured assistant with the genuine highest recorded usage; ties break by
    /// stable identity then revision (deterministic, never a first-item-as-running inference).
    /// Return null when no genuine usage aggregate exists (usage not recorded).</summary>
    Task<AssistantDefinitionSnapshot?> ReadMostUsedAsync(
        IReadOnlyList<AssistantDefinitionSnapshot> configured, CancellationToken token);
}

/// <summary>
/// Small canonical read projection for the Assistants rail icon. Resolution order is the owner's:
/// the user's currently running configured Assistant first; otherwise their most-used configured
/// Assistant; otherwise the generic icon only when authorised reads prove none is configured.
/// The projection never invents running/usage state: it reports the distinct typed unavailable /
/// usage-not-recorded states when the genuine data is absent.
/// </summary>
public sealed class AssistantRailIconProjection(IAssistantRailIconSource source)
{
    private readonly IAssistantRailIconSource _source = source
        ?? throw new ArgumentNullException(nameof(source));

    public async Task<AssistantRailIconReadResult> ResolveAsync(CancellationToken token = default)
    {
        AssistantCatalogueObservation? catalogue;
        try
        {
            catalogue = await _source.ReadConfiguredCatalogueAsync(token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            return AssistantRailIconReadResult.Unavailable("AssistantReadFailed", error.Message);
        }
        if (catalogue is null)
            return AssistantRailIconReadResult.Unavailable("AssistantUnavailable", "The authorised configured-Assistant catalogue is unavailable.");

        var configured = catalogue.Definitions
            .Where(definition => definition.Kind == ConfiguredIdentityKind.Assistant
                && definition.Configuration.Enabled && !definition.Configuration.Archived)
            .ToArray();
        if (configured.Length == 0)
            return AssistantRailIconReadResult.NoneConfigured;

        AssistantDefinitionSnapshot? running = null;
        try
        {
            running = await _source.ReadCurrentRunningAsync(configured, token).ConfigureAwait(false);
        }
        catch { /* running read unavailable: treat as no running assistant; never invent one */ }
        if (running is not null)
            return AssistantRailIconReadResult.Running(running);

        AssistantDefinitionSnapshot? mostUsed = null;
        try
        {
            mostUsed = await _source.ReadMostUsedAsync(configured, token).ConfigureAwait(false);
        }
        catch { /* usage read unavailable: report usage-not-recorded; never invent most-used */ }
        if (mostUsed is not null)
            return AssistantRailIconReadResult.MostUsed(mostUsed);

        // Configured assistants exist but no genuine running/usage evidence is recorded.
        return AssistantRailIconReadResult.UsageNotRecorded;
    }
}
#endif
