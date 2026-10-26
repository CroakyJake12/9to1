using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using PopupPositioning = Avalonia.Controls.Primitives.PopupPositioning;

namespace Haven.Desktop.Views.Pages.Chat;

internal sealed partial class ChatHavenScene
{
    private Popup? _addMenuPopup;
    private HavenSceneControl? _addMenuPopupHost;
    private TopLevel? _addMenuPopupOwner;
    private Control? _focusBeforeAddMenuPopup;
    private bool _retiringAddMenuPopup;

    // These read-only seams let the owning Avalonia regression inspect the actual native popup
    // and hosted scene, rather than treating an in-scene z-index as proof of correct layering.
    internal Popup? AddMenuPopup => _addMenuPopup;
    internal HavenSceneControl? AddMenuPopupHost => _addMenuPopupHost;

    private void InitializeAddMenuPopup()
    {
        _addMenuPopupHost = new HavenSceneControl(new HavenDesktopImageResolver(),
            new HavenAvaloniaNativeControlResolver())
        {
            Root = AddMenuPrefab,
            Focusable = true
        };
        _addMenuPopupHost.AddHandler(InputElement.KeyDownEvent, OnAddMenuPopupKeyDown,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        _addMenuPopupHost.LayoutUpdated += OnAddMenuPopupLayoutUpdated;

        _addMenuPopup = new Popup
        {
            PlacementTarget = ComposerView,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupPositioning.PopupAnchor.TopLeft,
            // This keeps the full-client surface origin at the calculated window-client origin.
            PlacementGravity = PopupPositioning.PopupGravity.BottomRight,
            PlacementConstraintAdjustment = PopupPositioning.PopupPositionerConstraintAdjustment.None,
            IsLightDismissEnabled = true,
            OverlayDismissEventPassThrough = false,
            WindowManagerAddShadowHint = false,
            Child = _addMenuPopupHost
        };
        // The menu's authored root is transparent and fills the client area. Establish the native
        // PopupRoot transparency before each open so its donor white default cannot cover the scene.
        CuiPopupPresentation.RequestTransparency(_addMenuPopup);
        _addMenuPopup.Closed += OnAddMenuPopupClosed;
        _addMenu.Dismissed += OnAddMenuDismissed;
        ComposerView.AttachedToVisualTree += OnComposerAttachedToVisualTree;
        ComposerView.DetachedFromVisualTree += OnComposerDetachedFromVisualTree;
        ComposerView.LayoutUpdated += OnComposerLayoutUpdated;
    }

    private void OnComposerAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_addMenu.IsOpen) OpenAddMenuPopupIfAttached();
    }

    private void OnComposerDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // The native layer belongs to this exact mounted composer. Retire it when the native host
        // leaves its owner, even if the parent Window remains open for another tab.
        HideAddMenu();
    }

    private void OnComposerLayoutUpdated(object? sender, EventArgs e)
    {
        if (_addMenuPopup?.IsOpen == true) UpdateAddMenuPopupGeometry();
    }

    private void OnAddMenuPopupLayoutUpdated(object? sender, EventArgs e)
    {
        if (_addMenuPopup?.IsOpen == true) UpdateAddMenuPopupGeometry();
    }

    private void OnAddMenuPopupKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || _addMenuPopup?.IsOpen != true) return;
        e.Handled = true;
        HideAddMenu();
    }

    private void OnAddMenuDismissed(object? sender, EventArgs e) => CloseAddMenuPopup();

    private void OpenAddMenuPopupIfAttached()
    {
        if (_retiringAddMenuPopup || _disposed || !_addMenu.IsOpen || _addMenuPopup?.IsOpen == true
            || !_composerView.IsAttachedToVisualTree() || _addMenuPopup is null)
            return;

        var owner = TopLevel.GetTopLevel(_composerView);
        if (owner is null || !UpdateAddMenuPopupGeometry(owner)) return;

        _addMenuPopupOwner = owner;
        _focusBeforeAddMenuPopup = owner.FocusManager?.GetFocusedElement() as Control;
        try
        {
            _addMenuPopup.Open();
            // The popup is a full-client input surface; making its host focusable gives Escape a
            // reliable route when the catalogue's own search field is intentionally hidden.
            _addMenuPopupHost?.Focus(NavigationMethod.Unspecified);
        }
        catch
        {
            _addMenuPopupOwner = null;
            _focusBeforeAddMenuPopup = null;
            _addMenu.Hide();
            throw;
        }
    }

    private bool UpdateAddMenuPopupGeometry() =>
        TopLevel.GetTopLevel(_composerView) is { } owner && UpdateAddMenuPopupGeometry(owner);

    private bool UpdateAddMenuPopupGeometry(TopLevel owner)
    {
        if (_addMenuPopup is null || _addMenuPopupHost is null
            || !ReferenceEquals(TopLevel.GetTopLevel(_composerView), owner)) return false;

        // Avalonia popup offsets are relative to PlacementTarget. Translate the actual composer
        // into owner-client DIPs, then subtract that origin so the full-client HUI overlay keeps
        // the exact coordinate space used by the scene (including monitor scaling and padding).
        var composerOrigin = _composerView.TranslatePoint(default, owner);
        var clientSize = owner.ClientSize;
        if (composerOrigin is null || !double.IsFinite(clientSize.Width) || !double.IsFinite(clientSize.Height)
            || clientSize.Width <= 0 || clientSize.Height <= 0) return false;

        var width = Math.Max(1, clientSize.Width);
        var height = Math.Max(1, clientSize.Height);
        if (!double.IsFinite(_addMenuPopup.Width) || Math.Abs(_addMenuPopup.Width - width) > 0.1)
            _addMenuPopup.Width = width;
        if (!double.IsFinite(_addMenuPopup.Height) || Math.Abs(_addMenuPopup.Height - height) > 0.1)
            _addMenuPopup.Height = height;
        if (!double.IsFinite(_addMenuPopupHost.Width) || Math.Abs(_addMenuPopupHost.Width - width) > 0.1)
            _addMenuPopupHost.Width = width;
        if (!double.IsFinite(_addMenuPopupHost.Height) || Math.Abs(_addMenuPopupHost.Height - height) > 0.1)
            _addMenuPopupHost.Height = height;
        if (Math.Abs(_addMenuPopup.HorizontalOffset + composerOrigin.Value.X) > 0.1)
            _addMenuPopup.HorizontalOffset = -composerOrigin.Value.X;
        if (Math.Abs(_addMenuPopup.VerticalOffset + composerOrigin.Value.Y) > 0.1)
            _addMenuPopup.VerticalOffset = -composerOrigin.Value.Y;
        return true;
    }

    private void OnAddMenuPopupClosed(object? sender, EventArgs e)
    {
        var owner = _addMenuPopupOwner;
        var focus = _focusBeforeAddMenuPopup;
        _addMenuPopupOwner = null;
        _focusBeforeAddMenuPopup = null;

        _addMenu.Hide();
        if (_disposed || owner is null || focus is null || !focus.Focusable || !focus.IsEffectivelyEnabled
            || !focus.IsEffectivelyVisible || !focus.IsAttachedToVisualTree()
            || !ReferenceEquals(TopLevel.GetTopLevel(focus), owner)) return;

        // Restore only the pre-open focus target. A command that moved focus while dismissing the
        // menu keeps its destination; popup teardown never steals focus back afterward.
        if (!ReferenceEquals(owner.FocusManager?.GetFocusedElement(), focus))
            focus.Focus(NavigationMethod.Unspecified);
    }

    private void CloseAddMenuPopup()
    {
        if (_retiringAddMenuPopup || _addMenuPopup?.IsOpen != true) return;
        _addMenuPopup.Close();
    }

    private void RetireAddMenuPopup()
    {
        if (_retiringAddMenuPopup) return;
        _retiringAddMenuPopup = true;
        try
        {
            ComposerView.AttachedToVisualTree -= OnComposerAttachedToVisualTree;
            ComposerView.DetachedFromVisualTree -= OnComposerDetachedFromVisualTree;
            ComposerView.LayoutUpdated -= OnComposerLayoutUpdated;
            _addMenu.Dismissed -= OnAddMenuDismissed;
            if (_addMenuPopup is not null)
            {
                _addMenuPopup.Closed -= OnAddMenuPopupClosed;
                if (_addMenuPopup.IsOpen) _addMenuPopup.Close();
                _addMenuPopup.Child = null;
                _addMenuPopup = null;
            }
            if (_addMenuPopupHost is not null)
            {
                _addMenuPopupHost.RemoveHandler(InputElement.KeyDownEvent, OnAddMenuPopupKeyDown);
                _addMenuPopupHost.LayoutUpdated -= OnAddMenuPopupLayoutUpdated;
                _addMenuPopupHost.Root = null;
                _addMenuPopupHost = null;
            }
            _addMenu.Hide();
            _addMenuPopupOwner = null;
            _focusBeforeAddMenuPopup = null;
        }
        finally { _retiringAddMenuPopup = false; }
    }
}
