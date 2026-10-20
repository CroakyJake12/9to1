using Haven.Core;

namespace Haven.Application.Haven;

/// <summary>
/// Trusted host context passed from a real native host to an authored app surface.
///
/// The embedded-versus-standalone distinction and the availability of the shared
/// model/notifications/profile controls are resolved by the actual host from its
/// composition and services. They are never derived from visual text, DOM ancestry,
/// a query parameter or an asset key, so an ordinary webpage or a misconfigured
/// surface cannot authenticate a context it does not own.
/// </summary>
public interface IHavenHostContext
{
    /// <summary>True when this surface is hosted inside the Haven/Browse frame rather than
    /// running as a standalone app window.</summary>
    bool IsEmbedded { get; }

    /// <summary>True when the real Home model picker is available to this host.</summary>
    bool ModelSupported { get; }

    /// <summary>True when the durable Home notification service is available to this host.</summary>
    bool NotificationsSupported { get; }

    /// <summary>True when the native account/profile owner is available to this host.</summary>
    bool ProfileSupported { get; }

    /// <summary>The typed gateway that opens the genuine borrowed shared surfaces.
    /// Returns null when a host does not own the shared control cluster.</summary>
    IHavenSharedControlGateway? SharedControls { get; }

    /// <summary>Optional typed snapshot of the current effective model's presentation and
    /// resource-measurement state for the top-bar cluster. Null means the host does not yet
    /// supply measurement telemetry; callers MUST render a truthful unavailable state and never
    /// invent a percentage. The percentage beside the model name (HavenTopBarModelInfo.EffortLabel)
    /// is exclusively the selected reasoning-effort tier for that exact model/route.</summary>
    HavenTopBarModelInfo? ModelInfo => null;
}

/// <summary>
/// Truthful, typed top-bar model presentation. The percentage beside the model name is ONLY the
/// currently selected reasoning-effort tier for that model/provider/route (never usage, quota,
/// context fill, confidence, CPU/GPU/resource utilisation or health). Usage/context are SEPARATE
/// labelled metrics with actual provenance; absent measurement remains unknown/unavailable, never an
/// invented 0% or 100%.
/// </summary>
public sealed record HavenTopBarModelInfo(
    string Name,
    bool EffortConfigurable,
    int EffortTierCount,
    string EffortLabel,
    bool IsLocal,
    bool RemoteUsageAvailable,
    string? UsageSummary,
    string? UsageScope,
    bool ContextMeasured,
    string? ContextSummary,
    int EffortLevelOrdinal = -1)
{
    /// <summary>Remote model with no trustworthy provider usage telemetry: hide the usage arc and
    /// surface an accessible "M" note ("Provider usage unavailable; this model may still be
    /// metered"). Never a bogus zero percentage.</summary>
    public bool MeterMarker => !IsLocal && !RemoteUsageAvailable;

    /// <summary>The only percentage shown beside the model name: the bijective tier for the
    /// selected reasoning-effort level on this exact model/provider/route. It is never CPU usage,
    /// quota, context fill, confidence or health. When the scale/ordinal is unknown this is empty
    /// (truthful), never a fabricated percentage.</summary>
    public string EffortPercent =>
        ReasoningScale.TryTierPercentage(EffortTierCount, EffortLevelOrdinal, out var percentage)
            ? percentage + "%"
            : string.Empty;

    /// <summary>Whether the compact usage arc may be shown (only a genuinely measured numerator and
    /// denominator for the applicable scope/window, and never for a local model).</summary>
    public bool UsageVisible => !IsLocal && RemoteUsageAvailable && UsageSummary is not null;

    /// <summary>Whether the independent context occupancy metric may be shown (only when actually
    /// measured).</summary>
    public bool ContextVisible => ContextMeasured && ContextSummary is not null;
}

/// <summary>
/// The outcome of an attempt to open one of the genuine Home-owned shared surfaces.
/// A host that does not own the capability returns <see cref="Denied"/> without
/// inventing state or a dead queue. A real open never reports success until the
/// borrowed surface's show/init/account panel has actually settled; faults and
/// refusals are surfaced as structured <see cref="Code"/>/<see cref="Message"/>
/// values rather than being swallowed as success.
/// </summary>
public readonly record struct HavenSharedOpenResult(bool Opened, string? Code = null, string? Message = null)
{
    public static readonly HavenSharedOpenResult Denied = new(false, "PermissionDenied", "The requested shared control capability is unavailable.");
    public static HavenSharedOpenResult Refused(string code, string message) => new(false, code, message);
    public static HavenSharedOpenResult Succeeded(string? message = null) => new(true, "Ready", message);
}

/// <summary>
/// Typed gateway to the genuine Home-owned shared surfaces. Every method returns the
/// actual asynchronous open task and never fabricates an open when the capability is
/// unavailable. Callers (the Browse rail dispatcher and the host) await the real
/// show/init/account open result and retain failures/refusals.
/// </summary>
public interface IHavenSharedControlGateway
{
    /// <summary>Opens the real Home Universal Model Picker against the current profile.</summary>
    Task<HavenSharedOpenResult> OpenModelPickerAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens the durable Home notification centre/history.</summary>
    Task<HavenSharedOpenResult> OpenNotificationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens (or reactivates) the native CAKE account/profile window.</summary>
    Task<HavenSharedOpenResult> OpenProfileAsync(CancellationToken cancellationToken = default);
}
