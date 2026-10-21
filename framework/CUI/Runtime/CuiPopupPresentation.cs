using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

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

    private static readonly ConditionalWeakTable<Popup, object> Requested = new();

    /// <summary>
    /// Narrow producer alpha request. The real CUI popup producers call this when they create their
    /// <see cref="Popup"/>. It subscribes to the popup's pre-show <see cref="Popup.BeforeShow"/>
    /// seam, so the request is applied to EACH fresh destination BEFORE its first Show — never after
    /// Show via Opened, and never only on the first open (reopening the same popup creates a fresh
    /// destination and receives a fresh request). The negotiated receipt is observed by the
    /// <see cref="CuiTransparencyFallback"/> projection via the TopLevel transparency report hook
    /// (or an explicit <see cref="ObserveDestinationAlpha"/> receipt). The request is idempotent:
    /// repeated calls never double-subscribe.
    /// </summary>
    public static void RequestTransparency(Popup popup)
    {
        ArgumentNullException.ThrowIfNull(popup);
        if (Requested.TryGetValue(popup, out _)) return;
        popup.BeforeShow += OnBeforeShow;
        Requested.Add(popup, new object());
    }

    private static void OnBeforeShow(object? sender, Popup.PopupBeforeShowEventArgs args)
    {
        // Apply the alpha request to the fresh destination before its first show. The handler stays
        // subscribed for the lifetime of the popup, so every open (including reopens) negotiates.
        args.Destination.TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        EstablishBacking(args.Destination);
    }

    /// <summary>
    /// Establish the CUI-owned backing (transparent root background) on a genuine native popup
    /// destination before its first Show. The donor <see cref="PopupRoot"/> default Background is
    /// white, and the CUI popup template clears it with a raw native LocalValue — but a raw local
    /// value is not guaranteed to survive (a theme/ancestry or a CUI projection write can out-rank
    /// it), which produced the observed white rectangle behind the rounded chrome. This path sets
    /// the backing through the typed CUI property presentation at <see cref="CuiPropertyLevel.Overlay"/>
    /// precedence (applied at Animation native priority), so it wins over the donor white default,
    /// over any raw LocalValue, and over every lower CUI layer. It runs per-fresh-destination, so
    /// reopening a popup (which creates a fresh destination) receives a fresh backing. The alpha-aware
    /// fallback border (<c>PART_TransparencyFallback</c>) remains the sole painter of a positively
    /// opaque destination and stays unpainted while alpha is unknown or negotiated.
    /// </summary>
    private static void EstablishBacking(PopupRoot destination)
    {
        if (destination is null) return;
        // Lightweight projection: no templated-control setup is required to carry one overridden
        // property. If a full projection already exists (installed by the CUI popup template), the
        // lookup returns it and preserves its existing state.
        CuiPropertyPresentation.Attach(destination, ownsTemplate: false);
        CuiPropertyPresentation.Set(destination, "Background", Brushes.Transparent,
            CuiPropertyLevel.Overlay, "CUI popup backing", -100);
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
