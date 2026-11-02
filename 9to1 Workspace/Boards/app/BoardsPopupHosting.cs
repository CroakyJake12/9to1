using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace CakeOS.Apps.Boards.App;

/// <summary>
/// Shared hosting configuration and outside-click closing for the Boards popups that live in the
/// window's overlay layer (the insertion menu and the block-actions menu).
///
/// Why this exists, measured rather than assumed. On the running Linux build (2 November 2026, instances
/// run41 to run48, recorded in docs/development/qwen-boards-theme-continuation-2026-11-01.md rows 86-91),
/// a popup with the framework light-dismiss overlay enabled could not be operated at all: a pointer press
/// aimed at a drawn menu row was delivered to the page underneath it (a window-level press probe recorded
/// the hit chain ending at CuiContainerControl[EditorScroll] / CuiContainerControl[PageCard]), no menu
/// action was dispatched across four separate sequences, keyboard Return drove the block editor instead of
/// the menu, and the physical .9to1board file never changed - while the identical route worked in the
/// headless mount. The same binary with light dismiss switched off for the insertion menu, and left on for
/// the block-actions menu as the control case, completed the whole route by mouse: trigger, destination row,
/// filtered type row, and a real table block written to the file.
///
/// The framework overlay measures the popup itself as zero-sized (its Popup control reports bounds
/// 0,0,0,0 after layout, while the hosted OverlayPopupHost measures 289.5,212.7,326.4x410.3), so closing
/// is decided here against the bounds of the surface that is actually drawn. This is a Boards-side
/// workaround for a framework-level popup input defect; the defect itself stays recorded as open, and the
/// workaround must be removed if and when the overlay hit-testing in framework/CUI plus the vendored
/// Avalonia is fixed and re-verified on this platform.
/// </summary>
internal static class BoardsPopupHosting
{
    /// <summary>
    /// Applies the overlay-host configuration: in-window overlay hosting, framework light dismiss off, and
    /// outside-press closing owned by Boards. Call this instead of setting those members on the initializer.
    /// </summary>
    internal static void ConfigureOverlayPopup(Popup popup)
    {
        ArgumentNullException.ThrowIfNull(popup);
        popup.ShouldUseOverlayLayer = true;
        // See the type comment: with this on, the drawn popup cannot receive a pointer press on this platform.
        popup.IsLightDismissEnabled = false;
        AttachOutsidePressDismissal(popup);
    }

    /// <summary>
    /// Closes the popup when a press lands outside the drawn surface. Registered on the popup's own visual
    /// root while it is open, on the tunneling route so the decision runs before the pressed control sees
    /// the event, and removed again when the popup closes.
    /// </summary>
    private static void AttachOutsidePressDismissal(Popup popup)
    {
        InputElement? registered = null;
        EventHandler<PointerPressedEventArgs>? onPointerPressed = null;

        void Detach()
        {
            if (registered is null || onPointerPressed is null) return;
            // This fork's RemoveHandler takes no route argument.
            registered.RemoveHandler(InputElement.PointerPressedEvent, onPointerPressed);
            registered = null;
        }

        onPointerPressed = new EventHandler<PointerPressedEventArgs>((_, e) =>
        {
            if (!popup.IsOpen)
            {
                Detach();
                return;
            }
            // A surface that is no longer in a visual tree is stale: acting on its geometry would press a
            // button on behalf of a popup that nobody can see. This is not hypothetical - a mounted-surface
            // test that leaves a popup open was enough to make later, unrelated cases fail when the watcher
            // acted on stale bounds (measured 2 November 2026: the same eight-class shard passed 33/33 before
            // this file existed and reported 13 failures after it, and the pass below is what closed that gap).
            if (popup.Child is not Visual visualSurface || !visualSurface.IsAttachedToVisualTree())
            {
                Detach();
                return;
            }
            // Coordinates are taken in the surface's own space, so a press that lands inside the drawn
            // menu is left alone and reaches the control under it.
            if (visualSurface is Control surfaceControl &&
                new Rect(surfaceControl.Bounds.Size).Contains(e.GetPosition(surfaceControl)))
                return;
            popup.Close();
        });

        popup.Opened += (_, _) =>
        {
            Detach();
            // This fork keeps GetVisualRoot internal, so the root is the last element of the public ancestor
            // walk. The walk starts at the placement target rather than at the popup surface because the
            // surface is not attached yet when Opened fires, and the target's root is the element that
            // receives presses aimed at anything outside the popup.
            if (popup.PlacementTarget is Visual anchor)
            {
                if (anchor.GetSelfAndVisualAncestors().LastOrDefault() is InputElement root)
                {
                    registered = root;
                    root.AddHandler(InputElement.PointerPressedEvent, onPointerPressed, RoutingStrategies.Tunnel);
                }
                anchor.DetachedFromVisualTree += OnAnchorDetached;
            }
        };
        popup.Closed += (_, _) => Detach();

        void OnAnchorDetached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            // Both Boards hosts already close their popup when the trigger is detached, so the watcher
            // must stop with them instead of keeping a handler on a window that is being torn down.
            if (sender is Visual visual) visual.DetachedFromVisualTree -= OnAnchorDetached;
            Detach();
        }
    }
}
