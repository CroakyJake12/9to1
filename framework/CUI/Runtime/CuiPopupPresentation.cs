using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// CUI-owned per-popup presentation policy. The floating popup chrome (rounded surface + shadow
/// margin) is authored by the CUI popup template; this type decides which popup host is truthful
/// for a given popup, per popup (never a process/platform-wide overlay flag).
/// </summary>
public static class CuiPopupPresentation
{
    /// <summary>Observed alpha capability of the popup destination platform surface.</summary>
    public enum AlphaCapability
    {
        /// <summary>The platform negotiated (or can negotiate) per-pixel alpha.</summary>
        Available,
        /// <summary>Alpha is known to be unavailable (no compositor / transparency negotiation).</summary>
        Unavailable,
        /// <summary>The capability has not been positively established.</summary>
        Unknown
    }

    /// <summary>Whether the owning top-level is known to be free of native child-window airspace.</summary>
    public enum Airspace
    {
        /// <summary>No native child-window (NativeControlHost or hosted native child) is present.</summary>
        Clear,
        /// <summary>A native child-window is present and could cover a same-window overlay.</summary>
        Present,
        /// <summary>Airspace presence has not been positively established.</summary>
        Unknown
    }

    /// <summary>
    /// The owner-specified hosting matrix. Overlay is used ONLY when native alpha is unavailable
    /// AND the owner is positively free of native child-window airspace. Every other state retains
    /// native hosting: an overlay behind a browser native child would be covered, so native is the
    /// safe default whenever alpha works, airspace is present, or either fact is unknown.
    /// </summary>
    public static bool PreferOverlayHost(AlphaCapability alpha, Airspace airspace) =>
        alpha == AlphaCapability.Unavailable && airspace == Airspace.Clear;

    /// <summary>
    /// Conservative live fallback for callers that have not positively established both signals:
    /// retain native hosting. This matches the matrix (Unknown retains native) and never infers
    /// overlay safety from a default/unnegotiated transparency state.
    /// </summary>
    public static bool PreferOverlayHostUnknown() => false;

    /// <summary>
    /// Alpha-aware projection for the popup-root transparency fallback. Only a positively opaque
    /// destination (<see cref="WindowTransparencyLevel.None"/>) paints the latest guaranteed-opaque
    /// semantic color; Transparent and every alpha-capable surface (Blur/AcrylicBlur/Mica) keep the
    /// fallback null (unpainted) so a later theme change never repaints a negotiated-through
    /// backdrop. Unknown is never inferred as opaque from a default or a parent window's alpha.
    /// </summary>
    public static IBrush? ResolveOpaquePaint(WindowTransparencyLevel level, IBrush? opaqueBrush) =>
        level == WindowTransparencyLevel.None ? opaqueBrush : null;
}
