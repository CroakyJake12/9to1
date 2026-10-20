using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// CUI-owned per-popup presentation policy. The floating popup chrome (rounded surface + shadow
/// margin) is authored by the CUI popup template; this type decides which popup host is truthful
/// for a given popup, per popup (never a process/platform-wide overlay flag). It also owns the
/// narrow producer seam that requests destination alpha at the popup open boundary and records the
/// negotiated receipt, so the transparency fallback projection can model real (not default) alpha.
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

    /// <summary>Optional typed native child-window airspace inspector. When not registered, airspace
    /// is honestly Unknown and native hosting is retained.</summary>
    public interface ICuiAirspaceInspector
    {
        Airspace Inspect(TopLevel topLevel);
    }

    private static ICuiAirspaceInspector? _airspaceInspector;

    /// <summary>Registers the single typed airspace inspector for the process. Unknown/absent keeps
    /// native hosting (the safe default).</summary>
    public static void RegisterAirspaceInspector(ICuiAirspaceInspector inspector)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        if (_airspaceInspector is not null)
            throw new InvalidOperationException("A CUI airspace inspector is already registered.");
        _airspaceInspector = inspector;
    }

    /// <summary>Resolve the current owner airspace verdict (Unknown when no inspector is registered).</summary>
    public static Airspace QueryAirspace(TopLevel topLevel) =>
        _airspaceInspector?.Inspect(topLevel) ?? Airspace.Unknown;

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

    /// <summary>
    /// Narrow producer alpha request. The real CUI popup producers call this immediately after
    /// constructing their <see cref="Popup"/> so that, at the popup open boundary (the earliest
    /// public point the CUI host is reachable), the destination requests per-pixel alpha. The
    /// negotiated receipt is observed by the <see cref="CuiTransparencyFallback"/> projection via
    /// <see cref="TopLevel.ActualTransparencyLevelProperty"/> (or an explicit
    /// <see cref="ObserveDestinationAlpha"/> receipt). If a low-level pre-first-present hook is
    /// required, the caller should submit it as a typed integration dependency; this open-boundary
    /// request is the honest public seam today and never invents capability.
    /// </summary>
    public static void RequestTransparency(Popup popup)
    {
        ArgumentNullException.ThrowIfNull(popup);
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            popup.Opened -= handler;
            // Reach the CUI popup host through real visual topology (never reflection into an
            // internal Host field): the authored popup content is attached inside the PopupRoot.
            if (popup.Child is not Control content) return;
            var root = content.GetVisualAncestors().OfType<PopupRoot>().FirstOrDefault();
            if (root is null) return;
            root.TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        };
        popup.Opened += handler;
    }

    /// <summary>
    /// Record a producer receipt of the destination's negotiated transparency level. This is the
    /// explicit observation channel for the projection; it is never inferred from a default or a
    /// parent-window alpha. Used by controlled platform fixtures and honest producer callers.
    /// </summary>
    public static void ObserveDestinationAlpha(PopupRoot root, WindowTransparencyLevel level)
    {
        ArgumentNullException.ThrowIfNull(root);
        CuiTransparencyFallback.Observe(root, level);
    }

    /// <summary>
    /// Apply the verified hosting decision to a real CUI popup before it opens. Overlay hosting
    /// (<see cref="Popup.ShouldUseOverlayLayer"/>) is chosen only when alpha is verified unavailable
    /// AND the owner is verified free of native child-window airspace. Callers that have not
    /// positively established both signals must not call this; they retain native hosting (the safe
    /// default) via the host's default.
    /// </summary>
    public static void ApplyHostDecision(Popup popup, AlphaCapability alpha, Airspace airspace)
    {
        ArgumentNullException.ThrowIfNull(popup);
        popup.ShouldUseOverlayLayer = PreferOverlayHost(alpha, airspace);
    }
}
