#if !ANDROID
using HavenOS.Apps.Assistants.Contracts;

namespace Haven.Desktop;

/// <summary>States of the Assistants rail-icon read. No-access/unavailable are kept distinct
/// from "none configured", from "usage not recorded", and from cancellation, so the UI never
/// conflates a genuine read failure, a truthful empty configuration, a missing usage aggregate,
/// or a cancelled caller.</summary>
public enum AssistantRailIconState
{
    /// <summary>An exact selection was resolved from genuine authorised reads.</summary>
    Available,

    /// <summary>Authorised reads prove zero configured Assistants; the generic icon is used.</summary>
    NoneConfigured,

    /// <summary>A required authorised read (catalogue, running, or usage) could not be performed
    /// (access denied / unavailable / fault). It is NOT a truthful "none" or "unrecorded".</summary>
    Unavailable,

    /// <summary>Configured Assistants exist, and an authorised running read proved none is
    /// running, but no genuine canonical usage aggregate is recorded, so a most-used selection
    /// cannot be made truthfully.</summary>
    UsageNotRecorded,

    /// <summary>The caller's token was cancelled; this is not an empty configuration, an
    /// unavailable read, or a cached success.</summary>
    Cancelled
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
/// distinguish the state (none vs unavailable vs usage-not-recorded vs cancelled) rather than
/// inventing one. <see cref="Code"/>/<see cref="Message"/> carry meaningful diagnostic provenance
/// for unavailable/cancelled reads.</summary>
public readonly record struct AssistantRailIconReadResult(
    AssistantRailIconState State,
    AssistantRailIconSelection Selection,
    string? Code = null,
    string? Message = null)
{
    public static AssistantRailIconReadResult NoneConfigured => new(AssistantRailIconState.NoneConfigured, AssistantRailIconSelection.Generic);
    public static AssistantRailIconReadResult Unavailable(string code, string message) => new(AssistantRailIconState.Unavailable, AssistantRailIconSelection.Generic, code, message);
    public static AssistantRailIconReadResult UsageNotRecorded => new(AssistantRailIconState.UsageNotRecorded, AssistantRailIconSelection.Generic, "UsageNotRecorded", null);
    public static AssistantRailIconReadResult Cancelled(string? message = null) => new(AssistantRailIconState.Cancelled, AssistantRailIconSelection.Generic, "Cancelled", message);
    public static AssistantRailIconReadResult Running(AssistantDefinitionSnapshot definition) =>
        new(AssistantRailIconState.Available, new("Running", definition.Identity, definition.Revision, definition.Configuration.IconResourceId));
    public static AssistantRailIconReadResult MostUsed(AssistantDefinitionSnapshot definition) =>
        new(AssistantRailIconState.Available, new("MostUsed", definition.Identity, definition.Revision, definition.Configuration.IconResourceId));
}

/// <summary>
/// Genuine, bounded reads behind the Assistants rail-icon projection. Implementations must read
/// real authorised state only; they must never fabricate running or usage data.
///
/// Contract for the nullable reads: return a snapshot ONLY when an authorised, successful read
/// proves that specific configured assistant is running / is the most-used. Return null ONLY for
/// an authorised successful read proving NONE is running / NO usage is recorded. Throw the typed
/// access denial / source fault when the read cannot be performed, so the projection can surface a
/// distinct unavailable/denied result with provenance rather than conflating the failure with "none"
/// or "unrecorded". A caller that is not composed must return null (honest absence), never invent a
/// running/usage claim.
/// </summary>
public interface IAssistantRailIconSource
{
    /// <summary>Authorised configured-Assistant catalogue read (e.g. bridge ListAsync). Returns
    /// null when unavailable; throws on access denial.</summary>
    Task<AssistantCatalogueObservation?> ReadConfiguredCatalogueAsync(CancellationToken token);

    /// <summary>True current running-Assistant read from running Task/Den execution ownership.
    /// Return the configured assistant that actually owns a running canonical Task/Den execution
    /// (null = authorised none running); throw on access denial / source fault.</summary>
    Task<AssistantDefinitionSnapshot?> ReadCurrentRunningAsync(
        IReadOnlyList<AssistantDefinitionSnapshot> configured, CancellationToken token);

    /// <summary>Most-used configured-Assistant read from a genuine canonical usage aggregate.
    /// Return the configured assistant with the genuine highest recorded usage (null = authorised
    /// no usage recorded); ties break by stable identity then revision (deterministic, never a
    /// first-item-as-running inference). Throw on access denial / source fault.</summary>
    Task<AssistantDefinitionSnapshot?> ReadMostUsedAsync(
        IReadOnlyList<AssistantDefinitionSnapshot> configured, CancellationToken token);
}

/// <summary>
/// Small canonical read projection for the Assistants rail icon. Resolution order is the owner's:
/// the user's currently running configured Assistant first; otherwise their most-used configured
/// Assistant; otherwise the generic icon only when authorised reads prove none is configured.
/// The projection never invents running/usage state: a failed running/usage read is surfaced as a
/// typed unavailable/denied result (never downgraded to "none"/"unrecorded"), caller cancellation is
/// surfaced distinctly, and every returned running/most-used snapshot is validated against the fresh
/// authorised configured catalogue before it is published (a stale/unrelated snapshot is never
/// trusted).
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
        catch (OperationCanceledException)
        {
            return AssistantRailIconReadResult.Cancelled("The assistant catalogue read was cancelled.");
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

        // Running-first: an authorised running read is the only way to establish precedence. A
        // fault here must NOT be downgraded to "no running" (which would let a most-used selection
        // proceed despite inability to establish running-first precedence).
        AssistantDefinitionSnapshot? running;
        try
        {
            running = await _source.ReadCurrentRunningAsync(configured, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return AssistantRailIconReadResult.Cancelled("The running-assistant read was cancelled.");
        }
        catch (Exception error)
        {
            return AssistantRailIconReadResult.Unavailable("RunningReadFailed", error.Message);
        }
        if (running is not null)
        {
            // Only publish a running snapshot that matches a fresh authorised configured entry
            // (identity + revision). A stale/unrelated snapshot is never trusted.
            if (TryMatchConfigured(configured, running) is { } runningMatch)
                return AssistantRailIconReadResult.Running(runningMatch);
            return AssistantRailIconReadResult.Unavailable("StaleRunningSnapshot",
                "The running-assistant read returned a snapshot that is not in the current authorised configuration.");
        }

        // Most-used: a fault here must NOT be downgraded to "usage not recorded".
        AssistantDefinitionSnapshot? mostUsed;
        try
        {
            mostUsed = await _source.ReadMostUsedAsync(configured, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return AssistantRailIconReadResult.Cancelled("The most-used-assistant read was cancelled.");
        }
        catch (Exception error)
        {
            return AssistantRailIconReadResult.Unavailable("UsageReadFailed", error.Message);
        }
        if (mostUsed is not null)
        {
            if (TryMatchConfigured(configured, mostUsed) is { } usedMatch)
                return AssistantRailIconReadResult.MostUsed(usedMatch);
            return AssistantRailIconReadResult.Unavailable("StaleUsageSnapshot",
                "The most-used-assistant read returned a snapshot that is not in the current authorised configuration.");
        }

        // Configured assistants exist, an authorised running read proved none is running, and no
        // genuine usage aggregate is recorded.
        return AssistantRailIconReadResult.UsageNotRecorded;
    }

    private static AssistantDefinitionSnapshot? TryMatchConfigured(
        IReadOnlyList<AssistantDefinitionSnapshot> configured, AssistantDefinitionSnapshot candidate)
    {
        foreach (var definition in configured)
        {
            if (definition.Identity == candidate.Identity && definition.Revision == candidate.Revision)
                return definition;
        }
        return null;
    }
}
#endif
