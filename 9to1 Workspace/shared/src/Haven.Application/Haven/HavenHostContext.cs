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
