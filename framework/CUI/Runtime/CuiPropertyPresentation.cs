using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Media;
using CakeOS.Cui.Themes;

namespace CakeOS.Cui.Runtime;

/// <summary>Internal backend projection of resolved CUI values. Native style priorities are never a CUI layer.</summary>
public static class CuiPropertyPresentation
{
    internal enum TemplatePart { ScrollBar, ScrollThumb, RailButton, SliderThumb, SpinnerButton, DisclosureButton, EmbeddedInput, FloatingMenuItem }
    private static readonly ConditionalWeakTable<Control, Projection> Projections = new();
    public static CuiEffectivePropertyStore For(Control control) => Get(control).Store;
    /// <summary>Non-creating lookup for read-only diagnostics. Returns null when no projection has been attached.</summary>
    public static bool TryGetExisting(Control control, out CuiEffectivePropertyStore? store)
    {
        store = null;
        if (Projections.TryGetValue(control, out var projection))
        {
            store = projection.Store;
            return true;
        }
        return false;
    }
    public static void Attach(Control control,bool ownsTemplate=true) => _ = Get(control,ownsTemplate);
    internal static void ConfigurePart(Control control, TemplatePart part) => Get(control).ConfigurePart(part);
    public static void Detach(Control control)
    { if (Projections.TryGetValue(control, out var projection)) { projection.Dispose(); Projections.Remove(control); } }
    /// <summary>True when the control is configured as a CUI embedded (compound-focus) input — the typed
    /// marker used to identify a surface's primary editable input without scanning for the first visible
    /// text box (which would be ambiguous when auxiliary inputs such as a search box are present).</summary>
    public static bool IsEmbeddedInput(Control control)
    {
        if (!Projections.TryGetValue(control, out var projection)) return false;
        return projection.IsEmbeddedInput;
    }
    public static void Register(Control control, string name, Type valueType, object? builtInDefault, Action<object?> apply) =>
        Get(control).Register(name, valueType, builtInDefault, apply);
    /// <summary>Mark an input as embedded: it owns no inner hover/focus box; the outer compound control owns visible focus.</summary>
    public static void ConfigureCompoundFocus(Control control) => Get(control).SetCompoundFocus();
    public static void SetSelected(Control control, bool selected, string source = "CUI selected navigation") =>
        Get(control).Set("Selected", selected, CuiPropertyLevel.Instance, source, 0);
    private static Projection Get(Control control,bool ownsTemplate=true) => Projections.GetValue(control, target => new Projection(target,ownsTemplate));

    public static bool Set(Control control, string name, object? value, CuiPropertyLevel level, string source, long order = 0) =>
        Get(control).Set(name, value, level, source, order);
    public static bool Remove(Control control, string name, CuiPropertyLevel level, string source) =>
        Get(control).Store.Remove(Canonical(name), level, source);

    public static bool TrySetLiteral(Control control, string name, string value, CuiPropertyLevel level, string source, long order = 0, CakeOS.Cui.Language.CuiSourceSpan? sourceSpan = null)
    {
        var projection = Get(control);
        var property = projection.Property(name);
        var valueType = property?.PropertyType ?? (projection.Store.Contains(Canonical(name)) ? projection.Store.Resolve(Canonical(name)).ValueType : null);
        if (valueType is null) return false;
        var type = Nullable.GetUnderlyingType(valueType) ?? valueType;
        object? parsed;
        if (type == typeof(string) || type == typeof(object)) parsed = value;
        else if (typeof(IBrush).IsAssignableFrom(type)) parsed = CuiControlLoader.ParseBrush(value,
            sourceSpan ?? CakeOS.Cui.Language.CuiSourceSpan.At("runtime.cui", 1, 1));
        else if (type == typeof(Thickness)) parsed = Thickness.Parse(value);
        else if (type == typeof(CornerRadius)) parsed = CornerRadius.Parse(value);
        else if (type == typeof(FontFamily)) parsed = new FontFamily(value);
        else if (type == typeof(FontWeight)) parsed = Enum.Parse<FontWeight>(value,true);
        else if (type == typeof(double)) parsed = value.Equals("Auto", StringComparison.OrdinalIgnoreCase) ? double.NaN : double.Parse(value, CultureInfo.InvariantCulture);
        else if (type == typeof(bool)) parsed = bool.Parse(value);
        else if (type == typeof(int)) parsed = int.Parse(value, CultureInfo.InvariantCulture);
        else if (type == typeof(decimal)) parsed = decimal.Parse(value, CultureInfo.InvariantCulture);
        else if (type == typeof(char)) parsed = value.Length == 1 ? value[0] : throw new FormatException("A CUI character requires exactly one character.");
        else if (type.IsEnum) parsed = Enum.Parse(type, value, true);
        else return false;
        return projection.Set(name, parsed, level, source, order);
    }

    private static string Canonical(string name) => name.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant() switch
    {
        "color" or "foreground" => "Foreground", "bordercolor" or "borderbrush" => "BorderColor",
        "borderwidth" or "borderthickness" => "BorderWidth", "checked" or "ischecked" => "Checked",
        _ => name.Replace("-", string.Empty, StringComparison.Ordinal)
    };

    private sealed class Projection : IDisposable
    {
        private Border? _focusRing;
        private CuiInputFocusRequest? _focusRequest;
        private bool _selectOnFocusRequest;
        private readonly Control _control;
        private readonly Dictionary<string, AvaloniaProperty> _mapped = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Action<object?>> _custom = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<AvaloniaProperty, IDisposable> _nativeFences = [];
        private readonly Dictionary<string, object?> _resources = new(StringComparer.Ordinal);
        private readonly List<IDisposable> _subscriptions = [];
        private bool _applying;
        private bool _selected;
        private bool _dragging;
        private bool _compoundFocus;
        /// <summary>True once the control is configured as an embedded (compound-focus) input.</summary>
        public bool IsEmbeddedInput => _compoundFocus;
        private bool _chromeless;
        private CuiFontRole _fontRole;
        private CuiAccentRole _accentRole;
        private CuiCustomFonts? _customFonts;
        private double _customFontScale = 1;
        private double? _roundnessPercent;
        private TemplatePart? _part;
        public CuiEffectivePropertyStore Store { get; } = new();
        public void SetCompoundFocus()
        {
            _compoundFocus = true;
            // The generic embedded-input role selects the canonical template part and suppresses
            // every independent inner background/border/hover/focus decoration; the outer compound
            // control owns visible focus and validation.
            ConfigurePart(TemplatePart.EmbeddedInput);
            // Clear the independently installed FocusAdorner through the CUI effective-property
            // store at overlay precedence. A raw native LocalValue loses to this projection's own
            // Animation-priority write, so the null must win here in the typed store.
            Set("FocusAdorner", null, CuiPropertyLevel.Overlay, "CUI embedded input", -100);
            // A compound-focus embedded input preserves its REAL selection while focus moves to the
            // owned toolbar/menu (the pinned native TextBox otherwise clears it in OnLostFocus).
            // This is the generic backend capability of the embedded/compound role, applied through
            // the typed store; no app-specific selection cache is introduced.
            if (_control is TextBox)
                Set("ClearSelectionOnLostFocus", false, CuiPropertyLevel.Overlay, "CUI embedded input", -100);
        }
        public Projection(Control control,bool ownsTemplate)
        {
            _control = control;
            Store.Changed += (_, property) => Apply(property);
            control.PropertyChanged += NativeChanged;
            Register("Selected", typeof(bool), false, value => { _selected = value is true; UpdateState(); });
            if(!ownsTemplate)return; // Specialised renderer owns its content; CUI supplies its declared outer chrome separately.
            if (control is TextBox or NumericUpDown or ToggleButton or Slider or CuiResizeInput or ComboBox or ListBox)
            {
                Register("SelectOnFocusRequest", typeof(bool), false, value => _selectOnFocusRequest = value is true);
                Register("FocusRequested", typeof(bool), false, value =>
                {
                    if (value is true)
                        (_focusRequest ??= new CuiInputFocusRequest(control, () => _selectOnFocusRequest)).SetRequested(true);
                    else _focusRequest?.SetRequested(false);
                });
            }
            if(control is TemplatedControl)
            {
                if (!CuiControlTemplates.TryGet(control.GetType(),out var template))
                    throw new InvalidOperationException($"CUI_TEMPLATE_MISSING: ordinary control '{control.GetType().FullName}' has no CUI-owned template. Register its canonical adapter or host specialised content through Object.");
                Set("Template",template,CuiPropertyLevel.BuiltIn,"CUI-owned control template",0);
            }
            var interactive = control is Button or TextBox or SelectingItemsControl or ListBoxItem or RangeBase or MenuItem or Thumb or NumericUpDown;
            Watch("CuiInterfaceFontFamily", "FontFamily");
            Watch("CuiFontWeightBody", "FontWeight");
            Watch("CuiFontSizeBody", "FontSize");
            Register("FontRole", typeof(CuiFontRole), CuiFontRole.Main, value =>
            { _fontRole = (CuiFontRole)value!; RefreshFontRole(); });
            WatchCustom("CuiCustomTypography", value => { _customFonts = value as CuiCustomFonts; RefreshFontRole(); });
            WatchCustom("CuiCustomFontScale", value => { _customFontScale = value is double scale ? scale : 1; RefreshFontRole(); });
            if (control is Button)
            {
                Register("AccentRole", typeof(CuiAccentRole), CuiAccentRole.None, value =>
                { _accentRole = (CuiAccentRole)value!; RefreshAccentRole(); RefreshFontRole(); });
                // Generic chromeless/icon appearance: suppresses interaction (hover/pressed) paint
                // while retaining the real selected background and visible keyboard focus. The
                // setter refreshes the CUI interaction state immediately (mirroring the Selected
                // setter) so that flipping the effective/bound value while a pointer is already
                // hovered/pressed applies/clears the interaction paint without waiting for a new
                // native pointer notification.
                Register("Chromeless", typeof(bool), false, value => { _chromeless = value is true; UpdateState(); });
                WatchCustom("CuiButtonRoundnessPercent", value =>
                { _roundnessPercent = value is double percent ? percent : null; RefreshRoundness(); });
                foreach (var tier in new[] { "Primary", "Secondary", "Tertiary", "Text", "Negative" })
                {
                    WatchCustom($"CuiAccent{tier}Brush", _ => RefreshAccentRole());
                    WatchCustom($"CuiAccent{tier}ForegroundBrush", _ => RefreshAccentRole());
                    WatchCustom($"CuiAccent{tier}Background", _ => RefreshAccentRole());
                    WatchCustom($"CuiAccent{tier}HoverBackground", _ => UpdateState());
                    WatchCustom($"CuiAccent{tier}PressedBackground", _ => UpdateState());
                    Watch($"CuiAccent{tier}HoverBrush", null);
                    Watch($"CuiAccent{tier}PressedBrush", null);
                    Watch($"CuiAccent{tier}HoverForegroundBrush", null);
                    Watch($"CuiAccent{tier}PressedForegroundBrush", null);
                }
            }
            Watch("CuiTextBrush", "Foreground");
            if (control is Button or MenuItem)
                Watch("CuiButtonVerticalContentAlignment", "VerticalContentAlignment");
            if (control is Button and not (CheckBox or RadioButton or ToggleSwitch))
                Watch("CuiButtonHorizontalContentAlignment", "HorizontalContentAlignment");
            Watch("CuiFlowDirection", "FlowDirection");
            if (control is ContextMenu)
            {
                Watch("CuiPopupHorizontalOffset", "HorizontalOffset");
                Watch("CuiPopupVerticalOffset", "VerticalOffset");
            }
            // The generic Selected state (a single authored surface such as a tab pill) applies themed
            // selection fill/border to any CUI control, including non-interactive composition
            // containers. The selection brushes are therefore observed for every control; hover and
            // pressed brushes remain interactive-control-only so a quiet container never paints an
            // unwanted hover island.
            Watch("CuiSelectionBrush", null); Watch("CuiMenuSelectionBrush", null); Watch("CuiSelectionIndicatorBrush", null);
            if (!interactive) return;
            Set("FocusAdorner", new FuncTemplate<Control>(() =>
            {
                _focusRing = new Border { IsHitTestVisible = false };
                RefreshFocusRing();
                return _focusRing;
            }), CuiPropertyLevel.BuiltIn, "CUI keyboard focus indicator", 0);
            Watch("CuiControlPadding", "Padding"); Watch("CuiControlMinHeight", "MinHeight");
            Watch("CuiControlRadius", "CornerRadius"); Watch("CuiControlBorderWidth", "BorderWidth");
            Watch("CuiControlBorderBrush", "BorderColor");
            Watch(control is TextBox or ComboBox or NumericUpDown ? "CuiInputSurfaceBrush" : "CuiButtonBrush", "Background");
            if (control is TextBox or ComboBox or NumericUpDown) Watch("CuiInputRadius", "CornerRadius", "CUI field shape", 1);
            if (control is CheckBox or RadioButton) Watch("CuiChoiceBorderBrush", "BorderColor", "CUI choice boundary", 1);
            if (control is Menu)
            {
                Watch("CuiTransparentBrush", "Background", "CUI menu container", 1);
                Set("BorderWidth", new Thickness(0), CuiPropertyLevel.Theme, "CUI menu container", 1);
                Set("Padding", new Thickness(0), CuiPropertyLevel.Theme, "CUI menu container", 1);
            }
            if (control is MenuItem or ListBoxItem)
            {
                Watch("CuiTransparentBrush", "Background", "CUI menu row", 1);
                Watch("CuiMenuItemBorderWidth", "BorderWidth", "CUI menu row", 1);
                Watch("CuiMenuItemPadding", "Padding", "CUI menu row", 1);
                Watch("CuiMenuItemRadius", "CornerRadius", "CUI menu row", 1);
            }
            foreach (var key in new[] { "CuiButtonHoverBrush", "CuiButtonPressedBrush", "CuiButtonHoverForegroundBrush", "CuiButtonPressedForegroundBrush",
                "CuiFocusBrush", "CuiValidationBrush", "CuiDisabledOpacity", "CuiFocusIndicatorThickness" }) Watch(key, null);
            if (control is TextBox)
            {
                Watch("CuiTextBrush", "CaretBrush"); Watch("CuiTextSoftBrush", "PlaceholderForeground");
                Watch("CuiSelectionBrush", "SelectionBrush"); Watch("CuiTextBrush", "SelectionForegroundBrush");
            }
        }

        public AvaloniaProperty? Property(string name)
        {
            var canonical = Canonical(name);
            if (_mapped.TryGetValue(canonical, out var known)) return known;
            var backend = canonical.ToLowerInvariant() switch
            {
                "border" or "bordercolor" => "BorderBrush", "borderwidth" => "BorderThickness", "checked" => "IsChecked",
                "layer" => "ZIndex", "value" when _control is TextBox => "Text",
                "content" when _control is MenuItem => "Header",
                "defaultaction" when _control is Button => "IsDefault",
                "cancelaction" when _control is Button => "IsCancel", _ => canonical
            };
            var property = AvaloniaPropertyRegistry.Instance.GetRegistered(_control.GetType())
                .Concat(AvaloniaPropertyRegistry.Instance.GetRegisteredDirect(_control.GetType()))
                .FirstOrDefault(candidate => candidate.Name.Equals(backend, StringComparison.OrdinalIgnoreCase));
            if (property is null || property.IsReadOnly) return null;
            _mapped[canonical] = property;
            return property;
        }

        public bool Set(string name, object? value, CuiPropertyLevel level, string source, long order)
        {
            name = Canonical(name);
            if (_custom.ContainsKey(name)) { Store.Set(name, value, level, source, order); return true; }
            var property = Property(name);
            if (property is null) return false;
            if (!Store.Contains(name))
            {
                object? initial = name.Equals("Opacity", StringComparison.OrdinalIgnoreCase) ? 1d :
                    property.PropertyType.IsValueType ? Activator.CreateInstance(property.PropertyType) : null;
                Store.Register(name, property.PropertyType, initial);
            }
            Store.Set(name, value, level, source, order);
            Apply(Store.Resolve(name));
            return true;
        }
        public void Register(string name, Type valueType, object? builtInDefault, Action<object?> apply)
        {
            name = Canonical(name); ArgumentNullException.ThrowIfNull(apply);
            if (_mapped.ContainsKey(name) || _custom.ContainsKey(name)) throw new InvalidOperationException($"CUI projection '{name}' is already registered.");
            Store.Register(name, valueType, builtInDefault); _custom.Add(name, apply); Apply(Store.Resolve(name));
        }

        public void ConfigurePart(TemplatePart part)
        {
            if (_part == part) return;
            _part = part;
            const string source = "CUI template part";
            void Value(string name,object value) => Set(name,value,CuiPropertyLevel.Theme,source,1);
            if (part != TemplatePart.EmbeddedInput) Value("Padding",new Thickness(0));
            Value("BorderWidth",new Thickness(0));
            Value("MinWidth",0d);Value("MinHeight",0d);
            if (part is TemplatePart.ScrollBar or TemplatePart.RailButton or TemplatePart.DisclosureButton or TemplatePart.SpinnerButton or TemplatePart.EmbeddedInput)
                Watch("CuiTransparentBrush","Background",source,1);
            if (part == TemplatePart.ScrollThumb)
            {
                Watch("CuiScrollThumbBrush","Background",source,1);
                Watch("CuiScrollThumbRadius","CornerRadius",source,1);
                Watch("CuiScrollBarSize","MinWidth",source,1);Watch("CuiScrollBarSize","MinHeight",source,1);
                Watch("CuiScrollThumbHoverBrush",null);Watch("CuiScrollThumbPressedBrush",null);
            }
            if (_control is Thumb thumb)
            {
                void Started(object? sender,Avalonia.Input.VectorEventArgs args) { _dragging=true;UpdateState(); }
                void Completed(object? sender,Avalonia.Input.VectorEventArgs args) { _dragging=false;UpdateState(); }
                thumb.DragStarted+=Started;thumb.DragCompleted+=Completed;
                _subscriptions.Add(new CallbackDisposable(()=> {thumb.DragStarted-=Started;thumb.DragCompleted-=Completed;}));
            }
            if (part == TemplatePart.SliderThumb)
            {
                Watch("CuiSelectionIndicatorBrush","Background",source,1);
                Watch("CuiThumbSize","Width",source,1);Watch("CuiThumbSize","Height",source,1);
                Watch("CuiThumbRadius","CornerRadius",source,1);
            }
            if (part == TemplatePart.SpinnerButton)
            {
                Watch("CuiSpinnerButtonWidth","Width",source,1);Watch("CuiSpinnerButtonHeight","Height",source,1);
            }
            if (part == TemplatePart.FloatingMenuItem)
            {
                Watch("CuiFloatingMenuItemMinHeight", "MinHeight", source, 1);
                Watch("CuiFloatingMenuItemPadding", "Padding", source, 1);
            }
            if (part == TemplatePart.ScrollBar && _control is ScrollBar bar)
            {
                void Dimensions()
                {
                    if (!_resources.TryGetValue("CuiScrollBarSize",out var size) || size is not double width) return;
                    Value("Width",bar.Orientation == Avalonia.Layout.Orientation.Vertical ? width : double.NaN);
                    Value("Height",bar.Orientation == Avalonia.Layout.Orientation.Horizontal ? width : double.NaN);
                }
                _subscriptions.Add(_control.GetResourceObservable("CuiScrollBarSize").Subscribe(new ResourceObserver(value =>
                { if (value is double) { _resources["CuiScrollBarSize"]=value;Dimensions(); } })));
                bar.PropertyChanged += PartOrientationChanged;
                void PartOrientationChanged(object? sender,AvaloniaPropertyChangedEventArgs change)
                { if (change.Property == ScrollBar.OrientationProperty) Dimensions(); }
                _subscriptions.Add(new CallbackDisposable(()=>bar.PropertyChanged-=PartOrientationChanged));
                Dimensions();
            }
            UpdateState();
        }

        private void Watch(string key, string? property, string source = "CUI inherited theme", long order = 0)
        {
            object? previous = null;
            var observed = false;
            _subscriptions.Add(_control.GetResourceObservable(key).Subscribe(new ResourceObserver(value =>
            {
                if (value == AvaloniaProperty.UnsetValue || value is null) return;
                // Resource notifications are dictionary-wide, including ancestor
                // dictionaries shadowed by this control's own palette. Compare
                // per subscription: one key can project to several properties.
                if (observed && Equals(previous, value)) return;
                _resources[key] = value;
                if (property is not null) Set(property, value, CuiPropertyLevel.Theme, source, order);
                UpdateState();
                // Only a completed projection may suppress identical future
                // notifications. A failed observer must be able to retry.
                previous = value;
                observed = true;
            })));
        }

        private void WatchCustom(string key, Action<object?> changed)
        {
            object? previous = AvaloniaProperty.UnsetValue; var observed = false;
            _subscriptions.Add(_control.GetResourceObservable(key).Subscribe(new ResourceObserver(value =>
            {
                if (value == AvaloniaProperty.UnsetValue) value = null;
                if (observed && Equals(previous, value)) return;
                if (value is null) _resources.Remove(key); else _resources[key] = value;
                changed(value); previous = value; observed = true;
            })));
        }

        private void RefreshFontRole()
        {
            const string source = "CUI semantic font role";
            var role = _fontRole == CuiFontRole.Main ? _accentRole switch
            {
                CuiAccentRole.Primary or CuiAccentRole.Negative => CuiFontRole.PrimaryButton,
                CuiAccentRole.Secondary => CuiFontRole.SecondaryButton,
                CuiAccentRole.Tertiary => CuiFontRole.TertiaryButton,
                _ => _fontRole
            } : _fontRole;
            if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
            if (_customFonts is null)
            {
                Store.RemoveSource(CuiPropertyLevel.Theme, source);
                return;
            }
            var font = role switch
            {
                CuiFontRole.PrimaryButton => _customFonts.PrimaryButton,
                CuiFontRole.SecondaryButton => _customFonts.SecondaryButton,
                CuiFontRole.TertiaryButton => _customFonts.TertiaryButton,
                CuiFontRole.Heading => _customFonts.Heading, CuiFontRole.Body => _customFonts.Body,
                CuiFontRole.Code => _customFonts.Code, _ => _customFonts.Main
            };
            Set("FontFamily", new FontFamily(CuiCustomThemeFonts.ResourceFamily(font.Family)), CuiPropertyLevel.Theme, source, 2);
            Set("FontWeight", (FontWeight)font.Weight, CuiPropertyLevel.Theme, source, 2);
            Set("FontSize", font.Size * _customFontScale, CuiPropertyLevel.Theme, source, 2);
        }

        private void RefreshAccentRole()
        {
            const string source = "CUI semantic accent role";
            if (!Enum.IsDefined(_accentRole)) throw new ArgumentOutOfRangeException(nameof(_accentRole));
            if (_accentRole == CuiAccentRole.None) Store.RemoveSource(CuiPropertyLevel.Theme, source);
            else
            {
                if (_resources.TryGetValue($"CuiAccent{_accentRole}Background", out var brush) ||
                    _resources.TryGetValue($"CuiAccent{_accentRole}Brush", out brush))
                    Set("Background", brush, CuiPropertyLevel.Theme, source, 2);
                if (_resources.TryGetValue($"CuiAccent{_accentRole}ForegroundBrush", out var foreground))
                    Set("Foreground", foreground, CuiPropertyLevel.Theme, source, 2);
                else Store.Remove("Foreground", CuiPropertyLevel.Theme, source);
            }
            UpdateState();
        }

        private void RefreshRoundness()
        {
            const string source = "CUI button roundness";
            if (_roundnessPercent is not { } percent) { Store.RemoveSource(CuiPropertyLevel.Theme, source); return; }
            var side = Math.Min(_control.Bounds.Width, _control.Bounds.Height);
            if (side > 0) Set("CornerRadius", new CornerRadius(side * .5 * percent / 100), CuiPropertyLevel.Theme, source, 2);
        }

        private void NativeChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
        {
            if (_applying) return;
            if (change.Property == Visual.BoundsProperty && _control is Button) RefreshRoundness();
            var mapped = _mapped.FirstOrDefault(pair => pair.Value == change.Property);
            // Editable native input owns its in-progress representation (including
            // invalid text); the CUI writeback/validation contract owns commit.
            var inputRepresentation = IsInputRepresentation(change.Property);
            if (!inputRepresentation && mapped.Key is not null && Store.Contains(mapped.Key)) Apply(Store.Resolve(mapped.Key));
            if (change.Property.Name is "IsPointerOver" or "IsPressed" or "IsChecked" or "IsSelected" or "IsEnabled" or "IsEffectivelyEnabled" or "IsKeyboardFocusWithin" or "HasError")
                UpdateState();
        }

        private string AccentStateBackground(string state) =>
            _resources.ContainsKey($"CuiAccent{_accentRole}{state}Background")
                ? $"CuiAccent{_accentRole}{state}Background" : $"CuiAccent{_accentRole}{state}Brush";

        private string AccentStateForeground(string state) =>
            _resources.ContainsKey($"CuiAccent{_accentRole}{state}ForegroundBrush")
                ? $"CuiAccent{_accentRole}{state}ForegroundBrush" : $"CuiAccent{_accentRole}ForegroundBrush";

        private void UpdateState()
        {
            if (!_compoundFocus) RefreshFocusRing();
            // This is CUI's built-in state Style. Authored overlays use their own
            // source order, so they are not overridden by a donor selector.
            const string source = "CUI interaction state";
            var desired = new Dictionary<string, object?>();
            void State(string name, string key)
            { if (_resources.TryGetValue(key, out var value)) desired[name] = value; }
            var rail = _control is Menu || _part is TemplatePart.RailButton or TemplatePart.ScrollBar or TemplatePart.EmbeddedInput;
            if (_part == TemplatePart.ScrollThumb)
            {
                if (_control.IsPointerOver) State("Background","CuiScrollThumbHoverBrush");
                if (_dragging) State("Background","CuiScrollThumbPressedBrush");
            }
            else if (!rail && !_compoundFocus && !_chromeless && _control.IsPointerOver)
            { State("Background", _control is TextBox or NumericUpDown or ComboBox ? "CuiInputSurfaceBrush" : _accentRole == CuiAccentRole.None ? "CuiButtonHoverBrush" : AccentStateBackground("Hover")); State("Foreground", _accentRole == CuiAccentRole.None ? "CuiButtonHoverForegroundBrush" : AccentStateForeground("Hover")); }
            if (_selected || _control is ToggleButton { IsChecked: true } or ListBoxItem { IsSelected: true } or MenuItem { IsSelected: true })
            { State("Background", _control is MenuItem or ListBoxItem ? "CuiMenuSelectionBrush" : "CuiSelectionBrush"); if (!_compoundFocus) State("BorderColor", "CuiSelectionIndicatorBrush"); }
            if (!rail && !_compoundFocus && !_chromeless && _control is Button { IsPressed: true })
            { State("Background", _accentRole == CuiAccentRole.None ? "CuiButtonPressedBrush" : AccentStateBackground("Pressed")); State("Foreground", _accentRole == CuiAccentRole.None ? "CuiButtonPressedForegroundBrush" : AccentStateForeground("Pressed")); }
            if (!_compoundFocus && _control.IsKeyboardFocusWithin)
            {
                State("BorderColor", "CuiFocusBrush");
            }
            if (CuiInputValidationProperties.GetHasError(_control)) State("BorderColor", "CuiValidationBrush");
            if (!_control.IsEffectivelyEnabled) State("Opacity", "CuiDisabledOpacity");
            // Reconcile each final value once. Removing and reapplying an
            // unchanged hover/focus state exposes the base style and invalidates
            // layout/rendering for every unrelated palette notification.
            foreach (var name in new[] { "Background", "Foreground", "BorderColor", "BorderWidth", "Opacity" })
                if (desired.TryGetValue(name, out var value)) Set(name, value, CuiPropertyLevel.Overlay, source, -100);
                else Store.Remove(name, CuiPropertyLevel.Overlay, source);
        }

        private void RefreshFocusRing()
        {
            if (_focusRing is null) return;
            // The native adorner layer draws focus without changing control
            // measurements, so keyboard and pointer focus cannot move targets.
            _focusRing.BorderBrush = _resources.GetValueOrDefault("CuiFocusBrush") as IBrush;
            _focusRing.BorderThickness = new Thickness(_resources.GetValueOrDefault("CuiFocusIndicatorThickness") is double width ? width : 2);
            if (_control is TemplatedControl templated) _focusRing.CornerRadius = templated.CornerRadius;
        }

        private void Apply(CuiEffectiveProperty resolved)
        {
            if (_custom.TryGetValue(resolved.Name, out var apply)) { apply(resolved.Value); return; }
            if (!_mapped.TryGetValue(resolved.Name, out var property) || Equals(_control.GetValue(property), resolved.Value)) return;
            _applying = true;
            try
            {
                if(_nativeFences.Remove(property,out var previous))previous.Dispose();
                // Native priority is only the final renderer transport. It never
                // selects a CUI winner. Editable input representations stay local.
                // Range endpoints participate in native coercion together. The
                // pinned backend's animated/base-value coercion can restore its
                // default endpoint when the other endpoint changes. Transport
                // this input configuration at local priority, retaining the CUI
                // store and NativeChanged fence as its sole value authority.
                var rangeEndpoint = _control is RangeBase && property.Name is "Minimum" or "Maximum";
                var priority=property.IsDirect||IsInputRepresentation(property)||rangeEndpoint?BindingPriority.LocalValue:BindingPriority.Animation;
                var fence=_control.SetValue(property,resolved.Value,priority);
                if(fence is not null)_nativeFences[property]=fence;
            }
            finally { _applying = false; }
            // The backend raises its effective input-state notification inside
            // SetValue. That notification is intentionally ignored by the native
            // appearance fence above; refresh the CUI state once projection has
            // finished so releasing a busy/owner gate removes disabled styling.
            if (property.Name is "IsEnabled" or "IsChecked" or "IsSelected") UpdateState();
        }
        private bool IsInputRepresentation(AvaloniaProperty property) =>
            _control is TextBox && property.Name=="Text" || _control is ToggleButton or MenuItem && property.Name=="IsChecked" ||
            _control is RangeBase or NumericUpDown && property.Name=="Value" ||
            _control is SelectingItemsControl && property.Name is "SelectedIndex" or "SelectedItem";
        public void Dispose()
        {
            _focusRequest?.Dispose();
            _control.PropertyChanged-=NativeChanged;foreach(var subscription in _subscriptions)subscription.Dispose();_subscriptions.Clear();
            foreach(var fence in _nativeFences.Values)fence.Dispose();_nativeFences.Clear();
        }
    }

    private sealed class CallbackDisposable(Action cleanup) : IDisposable
    { public void Dispose() => cleanup(); }

    private sealed class ResourceObserver(Action<object?> next) : IObserver<object?>
    { public void OnNext(object? value) => next(value); public void OnCompleted() { } public void OnError(Exception error) => throw error; }
}
