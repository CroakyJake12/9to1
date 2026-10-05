using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CakeOS.Cui.Runtime;

namespace NineToOne.Web.Wave;

/// <summary>Wave-only text allocation through the maintained native CUI registry.</summary>
public static class WaveNativePresentation
{
    public const string CaptionComponentName = "WaveCaption";
    public const string TextComponentName = "WaveText";
    public const string ImportComponentName = "WaveImport";

    public static CuiControlRegistry CreateControlRegistry(Func<string>? captureFocus = null, Func<string, bool>? validateFocus = null)
    {
        var registry = new CuiControlRegistry();
        // Recorded native font1/2 ink exceeded advance-based own allocations by
        // up to 0.469 horizontal and 0.563 vertical units. One native unit on
        // each side allocates inside the real TextBlock clip; keep clipping and
        // shaping unchanged. The full native/browser checks still decide whether
        // this is sufficient. These factories are scoped to this Wave loader.
        registry.RegisterControlType(CaptionComponentName,
            _ => new TextBlock { Padding = new Thickness(1, 1) });
        // TextBlock is a reserved CUI type and cannot be overridden. WaveText
        // preserves the actual native type and all authored label properties;
        // only its vertical allocation changes, leaving wrap width unchanged.
        registry.RegisterControlType(TextComponentName,
            _ => new TextBlock { Padding = new Thickness(0, 1) });
        // Native-only callers have no browser focus authority: deny by default.
        registry.RegisterControlType(ImportComponentName,
            context => new WaveImportButton(captureFocus ?? (() => ""), validateFocus ?? (token => false)));
        return registry;
    }

    // Native owner of one initiating Import action. Domain busy/availability remain unchanged.
    private sealed class WaveImportButton : Button
    {
        private readonly Func<string> _capture;
        private readonly Func<string, bool> _validate;
        private TopLevel? _root;
        private IDisposable? _nativeFocusObserver;
        private TopLevel? _claimRoot;
        private long _inputVersion;
        private long _claimVersion;
        private string _claim = "";
        private bool _initiatingClick;
        private bool _pending;
        public WaveImportButton(Func<string> capture, Func<string, bool> validate)
        { _capture = capture; _validate = validate; }
        protected override Type StyleKeyOverride => typeof(Button);

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs args)
        {
            base.OnAttachedToVisualTree(args);
            _root = TopLevel.GetTopLevel(this);
            if (_root is null) return;
            // Public event observer sees native focus claims in every TopLevel; no global focus writer.
            _nativeFocusObserver = GotFocusEvent.AddClassHandler<InputElement>(OnNativeFocus, RoutingStrategies.Bubble, true);
            _root.AddHandler(PointerPressedEvent, OnRootPointer, RoutingStrategies.Tunnel, true);
            _root.AddHandler(KeyDownEvent, OnRootKey, RoutingStrategies.Tunnel, true);
        }
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args)
        {
            CancelLease();
            _nativeFocusObserver?.Dispose();
            _nativeFocusObserver = null;
            if (_root is { } root)
            {
                root.RemoveHandler(PointerPressedEvent, OnRootPointer);
                root.RemoveHandler(KeyDownEvent, OnRootKey);
            }
            _root = null;
            base.OnDetachedFromVisualTree(args);
        }
        private void OnNativeFocus(InputElement sender, FocusChangedEventArgs args)
        { if (!ReferenceEquals(args.Source, this)) NewInputClaim(); }
        private void OnRootPointer(object? sender, PointerPressedEventArgs args) => NewInputClaim();
        private void OnRootKey(object? sender, KeyEventArgs args) => NewInputClaim();
        private void NewInputClaim()
        {
            if (_inputVersion < long.MaxValue) _inputVersion++;
            CancelLease();
        }
        private void CancelLease()
        { _pending = false; _claim = ""; _claimRoot = null; }

        protected override void OnClick()
        {
            CancelLease();
            _initiatingClick = false;
            if (_root is { } root && IsFocused && IsEffectivelyEnabled
                && ReferenceEquals(root.FocusManager.GetFocusedElement(), this)
                && _inputVersion < long.MaxValue)
            {
                _claim = _capture();
                _claimRoot = root;
                _claimVersion = _inputVersion;
                _initiatingClick = _claim.Length != 0;
            }
            try { base.OnClick(); }
            finally
            {
                _initiatingClick = false;
                if (IsEffectivelyEnabled) CancelLease();
            }
        }
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != IsEffectivelyEnabledProperty) return;
            if (!IsEffectivelyEnabled)
            {
                // Owner SetAndRaise precedes its disable-induced Focus(null).
                _pending = _initiatingClick && IsFocused && _claim.Length != 0
                    && ReferenceEquals(_root, _claimRoot) && _inputVersion == _claimVersion;
                if (!_pending) CancelLease();
                return;
            }
            if (!_pending) return;
            var claim = _claim;
            var root = _claimRoot;
            var version = _claimVersion;
            CancelLease(); // Consume before Focus raises reentrant native events.
            if (root is null || !ReferenceEquals(_root, root)
                || !ReferenceEquals(TopLevel.GetTopLevel(this), root)
                || version != _inputVersion || !IsEffectivelyVisible || !Focusable
                || root.FocusManager.GetFocusedElement() is not null
                || !_validate(claim)) return;
            Focus();
        }
    }
}
