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
/// Typed gateway to the genuine Home-owned shared surfaces. Every method returns
/// whether the requested surface was actually opened. A host that does not own a
/// capability returns false rather than inventing state or a dead queue.
/// </summary>
public interface IHavenSharedControlGateway
{
    /// <summary>Opens the real Home Universal Model Picker against the current profile.</summary>
    bool OpenModelPicker();

    /// <summary>Opens the durable Home notification centre/history.</summary>
    bool OpenNotifications();

    /// <summary>Opens (or reactivates) the native CAKE account/profile window.</summary>
    bool OpenProfile();
}
