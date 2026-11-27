using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Haven.SharedHeader;

/// <summary>
/// One implementation of the shared-panel backdrop's root-popover dismissal rule, wired by
/// every host of the authored <c>HavenSharedPanelShell</c> backdrop. While the compact model
/// picker is mounted as the anchored root popover, a left-press anywhere on the backdrop and
/// outside the mounted panel retires it through the SAME owner as the Close button. Presses
/// inside the panel are ordinary interactions and never dismiss. Full decision panels
/// (model settings, notifications, profile) are not root popovers: they keep their own
/// explicit Close/Esc semantics. The dismissing press is consumed, so it can never click
/// through to a control behind the backdrop.
/// </summary>
public static class HavenSharedPanelBackdropDismissal
{
    public static void Wire(
        Interactive backdrop,
        Func<bool> isRootPopoverOpen,
        Func<Control?> mountedPanelRoot,
        Action dismiss)
    {
        ArgumentNullException.ThrowIfNull(backdrop);
        ArgumentNullException.ThrowIfNull(isRootPopoverOpen);
        ArgumentNullException.ThrowIfNull(mountedPanelRoot);
        ArgumentNullException.ThrowIfNull(dismiss);
        backdrop.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.Handled || e.Source is not Visual source) return;
            if (!e.GetCurrentPoint(backdrop as Visual).Properties.IsLeftButtonPressed) return;
            if (!isRootPopoverOpen()) return;
            if (mountedPanelRoot() is { } panel && source.GetSelfAndVisualAncestors().Contains(panel))
                return; // Inside the panel: normal interaction, never a dismissal.
            e.Handled = true; // Consume the dismissing press: no click-through behind the panel.
            dismiss();
        }, RoutingStrategies.Tunnel);
    }
}
