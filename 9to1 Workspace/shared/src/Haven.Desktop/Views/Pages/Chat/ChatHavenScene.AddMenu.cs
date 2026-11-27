using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
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
    private TopLevel? _addMenuPopupTopLevel;
    private Control? _addMenuPopupTarget;
    private Control? _focusBeforeAddMenuPopup;
    private bool? _restoreFocusAfterPopupClose;
    private bool _focusPopupOnOpen;
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

        _addMenuPopup = new Popup
        {
            Placement = PlacementMode.AnchorAndGravity,
            // The compact card opens above and left-aligns with the real CUI Add button. Avalonia
            // flips/slides it when the available screen edge requires a safe alternate placement.
            PlacementAnchor = PopupPositioning.PopupAnchor.TopLeft,
            PlacementGravity = PopupPositioning.PopupGravity.TopRight,
            PlacementConstraintAdjustment = PopupPositioning.PopupPositionerConstraintAdjustment.All,
            VerticalOffset = -6,
            IsLightDismissEnabled = true,
            OverlayDismissEventPassThrough = false,
            WindowManagerAddShadowHint = false,
            Child = _addMenuPopupHost
        };
        // The card owns its opaque surface; keep only the native PopupRoot backing transparent.
        CuiPopupPresentation.RequestTransparency(_addMenuPopup);
        _addMenuPopup.Closed += OnAddMenuPopupClosed;
        _addMenu.Dismissed += OnAddMenuDismissed;
        ComposerView.AddHandler(InputElement.KeyDownEvent, OnComposerAddMenuKeyDown,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        ComposerView.AttachedToVisualTree += OnComposerAttachedToVisualTree;
        ComposerView.DetachedFromVisualTree += OnComposerDetachedFromVisualTree;
        ComposerView.LayoutUpdated += OnAddMenuOwnerLayoutUpdated;
    }

    private void OnComposerAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_addMenu.IsOpen) OpenAddMenuPopupIfAttached(_focusPopupOnOpen);
    }

    private void OnComposerDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // The native layer belongs to this exact mounted composer. Retire it when the native host
        // leaves its owner, even if the parent Window remains open for another tab.
        HideAddMenu();
    }

    private void OnAddMenuPopupKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _addMenuPopup?.IsOpen != true) return;
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideAddMenu();
            return;
        }
        var key = e.Key switch
        {
            Key.Up => HavenKey.Up,
            Key.Down => HavenKey.Down,
            Key.Home => HavenKey.Home,
            Key.End => HavenKey.End,
            _ => HavenKey.Unknown
        };
        if (_addMenu.TryGetAdjacentAttachCategory(key, out var next) && next is not null)
        {
            e.Handled = _addMenuPopupHost?.FocusElement(next) == true;
            _addMenu.RevealFocusedAttachControl();
        }
        else if (e.Key == Key.Tab)
        {
            // The existing Haven router owns Tab. Reveal its settled target without moving
            // focus or trapping the search input in a second keyboard/navigation owner.
            Dispatcher.UIThread.Post(() =>
            {
                if (!_disposed && !_retiringAddMenuPopup && _addMenuPopup?.IsOpen == true)
                    _addMenu.RevealFocusedAttachControl();
            }, DispatcherPriority.Input);
        }
    }

    private void OnComposerAddMenuKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _focusPopupOnOpen || _addMenuPopup?.IsOpen != true) return;
        e.Handled = true;
        HideAddMenu();
    }

    private void OnAddMenuDismissed(object? sender, EventArgs e) => CloseAddMenuPopup();

    private void OnAddMenuOwnerLayoutUpdated(object? sender, EventArgs e)
    {
        if (!_disposed && !_retiringAddMenuPopup && _addMenuPopup?.IsOpen == true)
            UpdateAddMenuPopupViewport();
    }

    private void UpdateAddMenuPopupViewport()
    {
        if (_addMenuPopup is null || _addMenuPopupHost is null || _addMenuPopupTarget is null
            || _addMenuPopupOwner is not { } owner) return;
        var viewportWidth = owner.ClientSize.Width;
        var viewportHeight = owner.ClientSize.Height;
        if (!double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight)
            || viewportWidth <= 0 || viewportHeight <= 0) return;
        var compact = viewportWidth <= 600;
        var availableWidth = Math.Max(80, viewportWidth - 24);
        var composerWidth = ComposerView.Bounds.Width > 0 ? ComposerView.Bounds.Width : availableWidth;
        var width = Math.Min(820, Math.Min(composerWidth, availableWidth));
        var maximumHeight = Math.Max(80, Math.Min(470, viewportHeight - (compact ? 190 : 200)));
        _addMenu.UpdateAttachViewport(width, maximumHeight, compact);
        _addMenuPopupHost.Width = width;
        _addMenuPopupHost.MaxHeight = maximumHeight;
        _addMenuPopupHost.MaxWidth = width;
        if (ComposerView.TranslatePoint(default, owner) is { } composerPoint
            && _addMenuPopupTarget.TranslatePoint(default, owner) is { } targetPoint)
        {
            var composerLeft = composerPoint.X + Math.Max(0, composerWidth - width) / 2;
            var safeLeft = Math.Clamp(composerLeft, 12, Math.Max(12, viewportWidth - width - 12));
            _addMenuPopup.HorizontalOffset = safeLeft - targetPoint.X;
        }
    }

    private void OpenAddMenuPopupIfAttached(bool focusPopup)
    {
        if (_retiringAddMenuPopup || _disposed || !_addMenu.IsOpen || _addMenuPopup is null
            || !ComposerView.IsAttachedToVisualTree()) return;

        if (_addMenuPopup.IsOpen)
        {
            // Mention search updates arrive on every composer edit. Keep its real input/caret as
            // the keyboard owner; only an explicit Add-button invocation may move focus to a row.
            _focusPopupOnOpen = focusPopup;
            UpdateAddMenuPopupViewport();
            if (focusPopup) FocusFirstAddMenuRow();
            return;
        }

        // Settle the authored formatting close before the new native popup takes focus. Do this
        // once per opening; live mention-query updates above keep their input/caret and do not
        // force a fresh layout on every edit.
        ComposerView.RefreshSurface();
        ComposerView.UpdateLayout();

        var addButton = FindComposerAddButton();
        if (addButton is null || TopLevel.GetTopLevel(addButton) is not { } owner) return;

        _addMenuPopupTarget = addButton;
        _addMenuPopup.PlacementTarget = addButton;
        _addMenuPopupOwner = owner;
        _focusBeforeAddMenuPopup = owner.FocusManager?.GetFocusedElement() as Control;
        _restoreFocusAfterPopupClose = null;
        _focusPopupOnOpen = focusPopup;
        UpdateAddMenuPopupViewport();
        try
        {
            // ContentPresenter realizes overlay content only while logically attached.
            // Keep this programmatic popup in the exact composer's logical tree, just
            // as authored popup controls do; a native PopupRoot alone masks this gap.
            ((ISetLogicalParent)_addMenuPopup).SetParent(ComposerView);
            _addMenuPopup.Open();
            _addMenuPopupTopLevel = TopLevel.GetTopLevel(_addMenuPopupHost!);
            if (focusPopup) FocusFirstAddMenuRow();
        }
        catch
        {
            ((ISetLogicalParent)_addMenuPopup).SetParent(null);
            _addMenuPopupOwner = null;
            _addMenuPopupTopLevel = null;
            _addMenuPopupTarget = null;
            _focusBeforeAddMenuPopup = null;
            _restoreFocusAfterPopupClose = null;
            _focusPopupOnOpen = false;
            _addMenu.Hide();
            throw;
        }
    }

    private Control? FindComposerAddButton() => ComposerView.GetVisualDescendants().OfType<Control>()
        .FirstOrDefault(control => AutomationProperties.GetName(control) == "Add files or actions");

    private void FocusFirstAddMenuRow()
    {
        if (_addMenuPopupHost?.IsAttachedToVisualTree() == true)
            _addMenuPopupHost.FocusElement(_addMenu.AttachFilesButton);
    }

    private bool PopupOwnsFocus()
    {
        var focused = _addMenuPopupTopLevel?.FocusManager?.GetFocusedElement() as Control;
        return focused is not null && _addMenuPopupHost is not null
            && (ReferenceEquals(focused, _addMenuPopupHost) || _addMenuPopupHost.IsVisualAncestorOf(focused)
                || focused.GetLogicalAncestors().Any(ancestor => ReferenceEquals(ancestor, _addMenuPopupHost)));
    }

    private void CapturePopupFocusRestoreRequest()
    {
        if (_restoreFocusAfterPopupClose.HasValue) return;
        var popupOwnsFocus = PopupOwnsFocus();
        var ownerFocus = _addMenuPopupOwner?.FocusManager?.GetFocusedElement() as Control;
        var ownerFocusUnchanged = ownerFocus is null || ReferenceEquals(ownerFocus, _focusBeforeAddMenuPopup);
        _restoreFocusAfterPopupClose = popupOwnsFocus && ownerFocusUnchanged;
    }

    private void OnAddMenuPopupClosed(object? sender, EventArgs e)
    {
        CapturePopupFocusRestoreRequest();
        var owner = _addMenuPopupOwner;
        var focus = _focusBeforeAddMenuPopup;
        var restoreFocus = _restoreFocusAfterPopupClose == true;
        _addMenuPopupOwner = null;
        _addMenuPopupTopLevel = null;
        _addMenuPopupTarget = null;
        _focusBeforeAddMenuPopup = null;
        _restoreFocusAfterPopupClose = null;
        _focusPopupOnOpen = false;

        if (_addMenuPopup is not null) ((ISetLogicalParent)_addMenuPopup).SetParent(null);
        _addMenu.Hide();
        ComposerView.RefreshSurface();
        if (!restoreFocus || _disposed || owner is null || focus is null || !focus.Focusable
            || !focus.IsEffectivelyEnabled || !focus.IsEffectivelyVisible || !focus.IsAttachedToVisualTree()
            || !ReferenceEquals(TopLevel.GetTopLevel(focus), owner)) return;

        // Restore only when the popup still owned focus and the owner has no newer focus target.
        var ownerFocus = owner.FocusManager?.GetFocusedElement() as Control;
        if (ownerFocus is null || ReferenceEquals(ownerFocus, focus))
            focus.Focus(NavigationMethod.Unspecified);
    }

    private void CloseAddMenuPopup()
    {
        if (_retiringAddMenuPopup || _addMenuPopup?.IsOpen != true) return;
        CapturePopupFocusRestoreRequest();
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
            ComposerView.LayoutUpdated -= OnAddMenuOwnerLayoutUpdated;
            ComposerView.RemoveHandler(InputElement.KeyDownEvent, OnComposerAddMenuKeyDown);
            _addMenu.Dismissed -= OnAddMenuDismissed;
            if (_addMenuPopup is not null)
            {
                _addMenuPopup.Closed -= OnAddMenuPopupClosed;
                if (_addMenuPopup.IsOpen) _addMenuPopup.Close();
                _addMenuPopup.Child = null;
                _addMenuPopup.PlacementTarget = null;
                ((ISetLogicalParent)_addMenuPopup).SetParent(null);
                _addMenuPopup = null;
            }
            if (_addMenuPopupHost is not null)
            {
                _addMenuPopupHost.RemoveHandler(InputElement.KeyDownEvent, OnAddMenuPopupKeyDown);
                _addMenuPopupHost.Root = null;
                _addMenuPopupHost = null;
            }
            _addMenu.Hide();
            _addMenuPopupOwner = null;
            _addMenuPopupTopLevel = null;
            _addMenuPopupTarget = null;
            _focusBeforeAddMenuPopup = null;
            _restoreFocusAfterPopupClose = null;
            _focusPopupOnOpen = false;
        }
        finally { _retiringAddMenuPopup = false; }
    }
}
