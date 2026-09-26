using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using System.Collections.ObjectModel;
using CakeOS.Cui.Language;
using CakeOS.Cui.Themes;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// Loads a CuiDocument and instantiates live Avalonia control trees.
/// This is the bridge between the CUI language model and the Avalonia rendering pipeline.
/// </summary>
public sealed class CuiControlLoader
{
    private readonly CuiControlRegistry _controlRegistry;
    private readonly Dictionary<string, string> _resourceScope;
    private readonly List<CuiDiagnostic> _runtimeDiagnostics = [];
    private readonly List<(Control Control, string PropertyName, CuiBindingValue Binding)> _liveBindings = new();
    private readonly Dictionary<Control, CuiComponent> _authoredControls = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Control, Dictionary<string, CuiObservedBinding>> _observedBindings = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Control> _wiredActions = new(ReferenceEqualityComparer.Instance);
    private ICuiBindingContext? _bindingContext;
    private ICuiActionDispatcher? _actionDispatcher;
    private CuiThemeScopeStack _themeStack = new(CuiSurfacePaletteCatalog.ActiveTheme);
    private string _currentSurface = "Home";

    /// <summary>Set a binding context for live {Binding path} resolution.</summary>
    public void SetBindingContext(ICuiBindingContext context) => _bindingContext = context;

    /// <summary>Set an action dispatcher for action= and on:click= attributes.</summary>
    public void SetActionDispatcher(ICuiActionDispatcher dispatcher) => _actionDispatcher = dispatcher;

    /// <summary>Set the surface name for theme resolution (default: "Home").</summary>
    public void SetSurface(string surface) => _currentSurface = surface;

    /// <summary>The currently active theme at the current point in the tree.</summary>
    public CuiTheme CurrentTheme => _themeStack.Current;

    /// <summary>Read-only diagnostic snapshot for a control created by this loader.</summary>
    public CuiControlDiagnostics? Inspect(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (!_authoredControls.TryGetValue(control, out var component)) return null;
        var command = control.Tag as string;
        return new CuiControlDiagnostics(
            component.Span, component.Type, component.Name,
            new ReadOnlyDictionary<string, CuiValue>(new Dictionary<string, CuiValue>(component.Properties)),
            new ReadOnlyDictionary<string, CuiActionReference>(new Dictionary<string, CuiActionReference>(component.Actions)),
            _observedBindings.TryGetValue(control, out var bindings)
                ? Array.AsReadOnly(bindings.Values.ToArray()) : [],
            _actionDispatcher is not null, _wiredActions.Contains(control),
            command is null ? null : (_actionDispatcher as ICuiActionAvailability)?.IsActionAvailable(command),
            command is null || _actionDispatcher is not CuiViewModel viewModel
                ? null : viewModel.HasAction(command));
    }

    public CuiControlLoader()
        : this(new CuiControlRegistry())
    {
    }

    public CuiControlLoader(CuiControlRegistry controlRegistry)
    {
        _controlRegistry = controlRegistry ?? throw new ArgumentNullException(nameof(controlRegistry));
        _resourceScope = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>Register a specialised component host before loading a document.</summary>
    public void RegisterControlType(string typeName, Func<CuiComponent, Control> factory) =>
        _controlRegistry.RegisterControlType(typeName, factory);

    /// <summary>Register a typed renderer host for CUI Object components.</summary>
    public void RegisterObjectRenderer(string objectType, Func<CuiComponent, Control> factory) =>
        _controlRegistry.RegisterObjectRenderer(objectType, factory);

    /// <summary>
    /// Loads a CuiDocument and returns the root Avalonia control tree.
    /// </summary>
    public Control? Load(CuiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        // Reusing a loader must not leak definitions or live controls from a prior document.
        _resourceScope.Clear();
        _liveBindings.Clear();
        _authoredControls.Clear();
        _observedBindings.Clear();
        _wiredActions.Clear();
        _runtimeDiagnostics.Clear();
        _themeStack = new CuiThemeScopeStack(CuiSurfacePaletteCatalog.ActiveTheme);

        // Populate resource scope
        foreach (var (key, def) in document.Resources)
        {
            if (def.Value is CuiLiteralValue literal)
                _resourceScope[key] = literal.Value;
        }

        if (document.Components.Count == 0)
            return null;

        // The first component is the root
        var root = LoadComponent(document.Components[0]);

        // Set DataContext for live bindings if we have a binding context
        if (_bindingContext is not null)
            root.DataContext = _bindingContext;

        return root;
    }

    /// <summary>
    /// Instantiates a .cui file and returns the root Avalonia control tree.
    /// </summary>
    public (Control? Root, IReadOnlyList<CuiDiagnostic> Diagnostics) LoadFile(string filePath)
    {
        var parser = new CuiRichParser();
        var document = parser.ParseFile(filePath);
        return LoadWithDiagnostics(document, parser.Diagnostics.Diagnostics);
    }

    /// <summary>
    /// Instantiates CUI markup from a string and returns the root control tree.
    /// </summary>
    public (Control? Root, IReadOnlyList<CuiDiagnostic> Diagnostics) LoadMarkup(string cuiMarkup, string sourceName = "markup.cui")
    {
        var parser = new CuiRichParser();
        var document = parser.Parse(cuiMarkup, sourceName);
        return LoadWithDiagnostics(document, parser.Diagnostics.Diagnostics);
    }

    private (Control? Root, IReadOnlyList<CuiDiagnostic> Diagnostics) LoadWithDiagnostics(
        CuiDocument document,
        IReadOnlyList<CuiDiagnostic> parserDiagnostics)
    {
        if (parserDiagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
        {
            _runtimeDiagnostics.Clear();
            return (null, parserDiagnostics.ToArray());
        }

        try
        {
            var root = Load(document);
            return (root, parserDiagnostics.Concat(_runtimeDiagnostics).ToArray());
        }
        catch (CuiRuntimeLoadException exception)
        {
            _runtimeDiagnostics.Add(exception.Diagnostic);
            return (null, parserDiagnostics.Concat(_runtimeDiagnostics).ToArray());
        }
    }

    /// <summary>
    /// Wire live bindings and events on a control tree after loading.
    /// Call after DataContext is set on the root.
    /// </summary>
    public void WireBindings(Control root)
    {
        WireBindingsRecursive(root);
    }

    private void WireBindingsRecursive(Control control)
    {
        // Wire button clicks to actions
        if (control is Button button && button.Tag is string actionName && _actionDispatcher is not null)
        {
            var dispatcher = _actionDispatcher;
            button.Click += async (s, e) => await dispatcher.DispatchAsync(actionName, null);
            _wiredActions.Add(button);
        }

        // Recurse into visual children
        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
                if (child is Control ctrl) WireBindingsRecursive(ctrl);
        }
        else if (control is Decorator decorator && decorator.Child is Control decChild)
        {
            WireBindingsRecursive(decChild);
        }
        else if (control is ContentControl cc && cc.Content is Control ccChild)
        {
            WireBindingsRecursive(ccChild);
        }
        else if (control is ItemsControl ic)
        {
            foreach (var item in ic.Items)
                if (item is Control icChild) WireBindingsRecursive(icChild);
        }
    }

    private Control LoadComponent(CuiComponent component)
    {
        // === DefaultTheme is a scope marker, not a visual control ===
        if (component.IsThemeScope && component.DefaultTheme is not null)
        {
            var resolvedTheme = CuiThemeScope.ResolveThemeName(component.DefaultTheme, CuiSurfacePaletteCatalog.ActiveTheme);
            _themeStack.Push(resolvedTheme);

            try
            {
                // Load the first child that produces a visual control
                Control? result = null;
                foreach (var child in component.Children)
                {
                    var childControl = LoadComponent(child);
                    if (result is null)
                    {
                        result = childControl;
                    }
                    else
                    {
                        // If there are multiple children, wrap in a Panel
                        if (result is Panel panel)
                        {
                            panel.Children.Add(childControl);
                        }
                        else
                        {
                            var wrapper = new Panel();
                            wrapper.Children.Add(result);
                            wrapper.Children.Add(childControl);
                            result = wrapper;
                        }
                    }
                }

                // Apply theme resources to the resulting control
                if (result is not null)
                {
                    CuiThemeScopeApplier.ApplyThemeToControl(result, resolvedTheme, _currentSurface);
                }

                return result ?? new Panel();
            }
            finally
            {
                _themeStack.Pop();
            }
        }

        var control = CreateControl(component);
        _authoredControls.Add(control, component);
        if (!string.IsNullOrWhiteSpace(component.Name))
        {
            control.Name = component.Name;
            Avalonia.Automation.AutomationProperties.SetAutomationId(control, component.Name);
        }
        ApplyProperties(control, component);
        ApplyAuthoredText(control, component);
        ApplyActionTag(control, component);
        ApplyClasses(control, component);

        // Apply theme resources to this control if it's a container
        ApplyThemeToControlIfContainer(control);

        foreach (var child in component.Children)
        {
            var childControl = LoadComponent(child);
            AddChild(control, childControl);
        }

        return control;
    }

    private Control CreateControl(CuiComponent component)
    {
        if (_controlRegistry.TryCreate(component, ResolveValue, out var control, out var error))
            return control!;

        var code = component.Type.Equals("Object", StringComparison.OrdinalIgnoreCase)
            ? "CUIR002"
            : "CUIR001";
        throw new CuiRuntimeLoadException(new CuiDiagnostic(
            code,
            CuiDiagnosticSeverity.Error,
            error ?? $"Unable to lower CUI component '{component.Type}'.",
            component.Span));
    }

    private void ApplyProperties(Control control, CuiComponent component)
    {
        foreach (var (propName, value) in component.Properties)
        {
            // Live binding: if value is a CuiBindingValue, create a real Avalonia binding
            if (value is CuiBindingValue binding && _bindingContext is not null)
            {
                ApplyLiveBinding(control, propName, binding);
                continue;
            }

            var resolved = ResolveValue(value);
            if (resolved is null) continue;

            ApplyLiteralProperty(control, propName, resolved);
        }
    }

    private static void ApplyAuthoredText(Control control, CuiComponent component)
    {
        if (string.IsNullOrWhiteSpace(component.Text)
            || component.Properties.ContainsKey("text")
            || component.Properties.ContainsKey("content"))
            return;

        switch (control)
        {
            case TextBlock textBlock:
                textBlock.Text = component.Text;
                break;
            case TextBox textBox:
                textBox.Text = component.Text;
                break;
            case ContentControl contentControl:
                contentControl.Content = component.Text;
                break;
        }
    }

    private void ApplyLiveBinding(Control control, string propName, CuiBindingValue binding)
    {
        // Track for periodic refresh
        _liveBindings.Add((control, propName, binding));

        // Apply initial value
        var resolved = ResolveObservedBinding(control, propName, binding);
        if (resolved is null) return;
        ApplyLiteralProperty(control, propName, resolved);
    }

    /// <summary>
    /// Refresh all tracked live bindings from the current binding context.
    /// Call this when the ViewModel data changes to push updates to the UI.
    /// </summary>
    public void RefreshBindings()
    {
        foreach (var (control, propName, binding) in _liveBindings)
        {
            if (control.IsLoaded == false) continue;
            var resolved = ResolveObservedBinding(control, propName, binding);
            if (resolved is null) continue;
            ApplyLiteralProperty(control, propName, resolved);
        }
    }

    private void ApplyLiteralProperty(Control control, string propName, string resolved)
    {
        var normalizedPropertyName = propName.Replace("-", string.Empty, StringComparison.Ordinal);
        switch (normalizedPropertyName.ToLowerInvariant())
        {
                case "width":
                    if (double.TryParse(resolved, out var w))
                        control.Width = w;
                    break;
                case "height":
                    if (double.TryParse(resolved, out var h))
                        control.Height = h;
                    break;
                case "minwidth":
                    if (double.TryParse(resolved, out var minW))
                        control.MinWidth = minW;
                    break;
                case "minheight":
                    if (double.TryParse(resolved, out var minH))
                        control.MinHeight = minH;
                    break;
                case "maxwidth":
                    if (double.TryParse(resolved, out var maxW))
                        control.MaxWidth = maxW;
                    break;
                case "maxheight":
                    if (double.TryParse(resolved, out var maxH))
                        control.MaxHeight = maxH;
                    break;
                case "margin":
                    control.Margin = ParseThickness(resolved);
                    break;
                case "padding":
                    if (control is Decorator d)
                        d.Padding = ParseThickness(resolved);
                    break;
                case "background":
                    var bg = ParseBrush(resolved);
                    if (control is Panel p)
                        p.Background = bg;
                    else if (control is Border b)
                        b.Background = bg;
                    else if (control is Avalonia.Controls.Primitives.TemplatedControl tc)
                        tc.Background = bg;
                    break;
                case "foreground":
                    if (control is TextBlock fgtb)
                        fgtb.Foreground = ParseBrush(resolved);
                    else if (control is Avalonia.Controls.Primitives.TemplatedControl fgtc)
                        fgtc.Foreground = ParseBrush(resolved);
                    break;
                case "borderbrush":
                    if (control is Border bb)
                        bb.BorderBrush = ParseBrush(resolved);
                    else if (control is Avalonia.Controls.Primitives.TemplatedControl tbc)
                        tbc.BorderBrush = ParseBrush(resolved);
                    break;
                case "borderthickness":
                    if (control is Border bt)
                        bt.BorderThickness = ParseThickness(resolved);
                    else if (control is Avalonia.Controls.Primitives.TemplatedControl tbct)
                        tbct.BorderThickness = ParseThickness(resolved);
                    break;
                case "cornerradius":
                    if (control is Border brd && TryParseCornerRadius(resolved, out var cr))
                        brd.CornerRadius = cr;
                    break;
                case "isvisible":
                    control.IsVisible = bool.TryParse(resolved, out var vis) && vis;
                    break;
                case "opacity":
                    if (double.TryParse(resolved, out var op))
                        control.Opacity = op;
                    break;
                case "horizontalalignment":
                    if (Enum.TryParse<HorizontalAlignment>(resolved, true, out var ha))
                        control.HorizontalAlignment = ha;
                    break;
                case "verticalalignment":
                    if (Enum.TryParse<VerticalAlignment>(resolved, true, out var va))
                        control.VerticalAlignment = va;
                    break;
                case "name":
                case "id":
                    control.Name = resolved;
                    break;
                case "title":
                    if (control is Window win)
                        win.Title = resolved;
                    break;
                case "text":
                    if (control is TextBlock tb)
                        tb.Text = resolved;
                    else if (control is TextBox tbx)
                        tbx.Text = resolved;
                    break;
                case "content":
                    if (control is ContentControl cc)
                        cc.Content = resolved;
                    else if (control is Button btn)
                        btn.Content = resolved;
                    break;
                case "orientation":
                    if (control is StackPanel sp && Enum.TryParse<Orientation>(resolved, true, out var or))
                        sp.Orientation = or;
                    break;
                case "ischecked":
                    if (control is CheckBox cb && bool.TryParse(resolved, out var chk))
                        cb.IsChecked = chk;
                    break;
                case "placeholdertext":
                    if (control is TextBox ptb)
                        ptb.PlaceholderText = resolved;
                    break;
                // Actions / events: store in Tag for wiring
                case "action":
                case "on:click":
                case "onclick":
                    control.Tag = resolved; // Will be wired in WireBindingsRecursive
                    break;
                // Grid attached properties
                case "grid.columndefinitions":
                case "grid-columndefinitions":
                case "columndefinitions":
                case "columns":
                    if (control is Grid gridCols)
                    {
                        try { gridCols.ColumnDefinitions = new Avalonia.Controls.ColumnDefinitions(resolved); }
                        catch (FormatException) { }
                    }
                    break;
                case "grid.rowdefinitions":
                case "grid-rowdefinitions":
                case "rowdefinitions":
                case "rows":
                    if (control is Grid gridRows)
                    {
                        try { gridRows.RowDefinitions = new Avalonia.Controls.RowDefinitions(resolved); }
                        catch (FormatException) { }
                    }
                    break;
                case "grid.column":
                case "grid-column":
                case "gridcolumn":
                    Avalonia.Controls.Grid.SetColumn(control, int.TryParse(resolved, out var gridColumn) ? gridColumn : 0);
                    break;
                case "column":
                    if (control is Grid gridCol && int.TryParse(resolved, out var colCount) && colCount > 0)
                    {
                        gridCol.ColumnDefinitions = new Avalonia.Controls.ColumnDefinitions(
                            string.Join(",", Enumerable.Repeat("1*", colCount)));
                    }
                    else if (control is Control gc2)
                    {
                        Avalonia.Controls.Grid.SetColumn(gc2, int.TryParse(resolved, out var col) ? col : 0);
                    }
                    break;
                case "grid.row":
                case "grid-row":
                case "gridrow":
                    Avalonia.Controls.Grid.SetRow(control, int.TryParse(resolved, out var gridRowIndex) ? gridRowIndex : 0);
                    break;
                case "row":
                    if (control is Grid gridRow && int.TryParse(resolved, out var rowCount) && rowCount > 0)
                    {
                        gridRow.RowDefinitions = new Avalonia.Controls.RowDefinitions(
                            string.Join(",", Enumerable.Repeat("1*", rowCount)));
                    }
                    else if (control is Control gr2)
                    {
                        Avalonia.Controls.Grid.SetRow(gr2, int.TryParse(resolved, out var row) ? row : 0);
                    }
                    break;
                case "grid.columnspan":
                case "grid-columnspan":
                case "gridcolumnspan":
                case "columnspan":
                    if (control is Control gcs)
                        Avalonia.Controls.Grid.SetColumnSpan(gcs, int.TryParse(resolved, out var cs) ? cs : 1);
                    break;
                case "grid.rowspan":
                case "grid-rowspan":
                case "gridrowspan":
                case "rowspan":
                    if (control is Control grs)
                        Avalonia.Controls.Grid.SetRowSpan(grs, int.TryParse(resolved, out var rs) ? rs : 1);
                    break;
                // TextBlock properties
                case "fontfamily":
                    if (control is TextBlock fttb)
                        fttb.FontFamily = new Avalonia.Media.FontFamily(resolved);
                    else if (control is Avalonia.Controls.Primitives.TemplatedControl fttc)
                        fttc.FontFamily = new Avalonia.Media.FontFamily(resolved);
                    break;
                case "fontsize":
                    if (control is TextBlock fstb && double.TryParse(resolved, out var fs))
                        fstb.FontSize = fs;
                    else if (control is Avalonia.Controls.Primitives.TemplatedControl fstc && double.TryParse(resolved, out fs))
                        fstc.FontSize = fs;
                    break;
                case "fontweight":
                    if (control is TextBlock fwTb && Enum.TryParse<Avalonia.Media.FontWeight>(resolved, true, out var fw))
                        fwTb.FontWeight = fw;
                    else if (control is Avalonia.Controls.Primitives.TemplatedControl fwTc && Enum.TryParse(resolved, true, out fw))
                        fwTc.FontWeight = fw;
                    break;
                case "fontstyle":
                    if (Enum.TryParse<Avalonia.Media.FontStyle>(resolved, true, out var fst)
                        || TryParseFontStyle(resolved, out fst))
                    {
                        if (control is TextBlock fsTb)
                            fsTb.FontStyle = fst;
                        else if (control is TextBox fsTbx)
                            fsTbx.FontStyle = fst;
                    }
                    break;
                case "textdecorations":
                case "textdecoration":
                    if (control is TextBlock tdTb)
                        tdTb.TextDecorations = ParseTextDecorations(resolved);
                    break;
                case "textwrapping":
                    if (Enum.TryParse<Avalonia.Media.TextWrapping>(resolved, true, out var tw))
                    {
                        if (control is TextBlock twTb)
                            twTb.TextWrapping = tw;
                        else if (control is TextBox twTbx)
                            twTbx.TextWrapping = tw;
                    }
                    break;
                case "acceptsreturn":
                case "multiline":
                    if (control is TextBox arTbx && bool.TryParse(resolved, out var ar))
                        arTbx.AcceptsReturn = ar;
                    break;
                case "canvas.left":
                case "canvasleft":
                case "canvas-left":
                case "left":
                    if (double.TryParse(resolved, out var cl))
                        Avalonia.Controls.Canvas.SetLeft(control, cl);
                    break;
                case "canvas.top":
                case "canvastop":
                case "canvas-top":
                case "top":
                    if (double.TryParse(resolved, out var ct))
                        Avalonia.Controls.Canvas.SetTop(control, ct);
                    break;
                // Automation / Accessibility
                case "accessible-name":
                case "accessiblename":
                    Avalonia.Automation.AutomationProperties.SetName(control, resolved);
                    break;
                case "automationid":
                    Avalonia.Automation.AutomationProperties.SetAutomationId(control, resolved);
                    break;
            }
    }

    private void ApplyActionTag(Control control, CuiComponent component)
    {
        // The parser routes action="Cmd" into CuiComponent.Actions (not Properties),
        // so honor it here; WireBindingsRecursive dispatches via control.Tag.
        if (control.Tag is string existing && !string.IsNullOrWhiteSpace(existing))
            return;
        if (component.Actions.TryGetValue("action", out var actionRef)
            && !string.IsNullOrWhiteSpace(actionRef.Name))
            control.Tag = actionRef.Name;
        else if (component.Actions.TryGetValue("on:click", out var clickRef)
            && !string.IsNullOrWhiteSpace(clickRef.Name))
            control.Tag = clickRef.Name;
    }

    private static bool TryParseFontStyle(string value, out Avalonia.Media.FontStyle result)
    {
        result = Avalonia.Media.FontStyle.Normal;
        if (string.Equals(value, "italic", StringComparison.OrdinalIgnoreCase))
        {
            result = Avalonia.Media.FontStyle.Italic;
            return true;
        }
        if (string.Equals(value, "oblique", StringComparison.OrdinalIgnoreCase))
        {
            result = Avalonia.Media.FontStyle.Oblique;
            return true;
        }
        if (string.Equals(value, "normal", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static Avalonia.Media.TextDecorationCollection? ParseTextDecorations(string value)
    {
        if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.Equals(value, "underline", StringComparison.OrdinalIgnoreCase))
            return Avalonia.Media.TextDecorations.Underline;
        if (string.Equals(value, "strikethrough", StringComparison.OrdinalIgnoreCase))
            return Avalonia.Media.TextDecorations.Strikethrough;
        return null;
    }

    private void ApplyClasses(Control control, CuiComponent component)
    {
        foreach (var cls in component.Classes)
        {
            control.Classes.Add(cls);
        }
    }

    private void AddChild(Control parent, Control child)
    {
        switch (parent)
        {
            case Panel panel:
                panel.Children.Add(child);
                break;
            case Decorator decorator:
                decorator.Child = child;
                break;
            case ContentControl contentControl:
                contentControl.Content = child;
                break;
            case Menu menu:
                if (child is MenuItem menuItem)
                    menu.Items.Add(menuItem);
                break;
            case TabControl tabControl:
                if (child is TabItem tabItem)
                    tabControl.Items.Add(tabItem);
                break;
            case ItemsControl itemsControl:
                itemsControl.Items.Add(child);
                break;
        }
    }

    private string? ResolveValue(CuiValue value)
    {
        return value switch
        {
            CuiLiteralValue literal => literal.Value,
            CuiBindingValue binding => ResolveBindingValue(binding),
            CuiResourceValue resource => ResolveResourceValue(resource.Key),
            _ => null,
        };
    }

    private string? ResolveResourceValue(string key)
    {
        if (_resourceScope.TryGetValue(key, out var value))
            return value;

        var palette = CuiSurfacePaletteCatalog.For(
            _currentSurface,
            CuiThemeScopeApplier.DetectAppearance(),
            _themeStack.Current);
        return key switch
        {
            "CuiBackgroundBrush" => palette.TideBase.ToString(),
            "CuiTextBrush" => palette.Text.ToString(),
            "CuiTextSoftBrush" => palette.TextSoft.ToString(),
            "CuiMutedBrush" => palette.Muted.ToString(),
            "CuiPanelBrush" => palette.Panel.ToString(),
            "CuiPanel2Brush" => palette.Panel2.ToString(),
            "CuiPanel3Brush" => palette.Panel3.ToString(),
            "CuiPanelHoverBrush" => palette.PanelHover.ToString(),
            "CuiLineBrush" => palette.Line.ToString(),
            "CuiLineStrongBrush" => palette.LineStrong.ToString(),
            "CuiButtonBrush" => palette.Button.ToString(),
            "CuiFocusBrush" => palette.Focus.ToString(),
            "CuiAccentSoftBrush" => palette.AccentSoft.ToString(),
            _ => null,
        };
    }

    private string? ResolveBindingValue(CuiBindingValue binding)
    {
        // Check special theme-related bindings first
        var special = ResolveSpecialBinding(binding.Path);
        if (special is not null) return special;

        // Try live binding context
        if (_bindingContext is not null && _bindingContext.TryGetValue(binding.Path, out var result))
        {
            return result?.ToString();
        }
        // Fall back to declared fallback value
        return binding.Fallback;
    }

    private string? ResolveObservedBinding(Control control, string property, CuiBindingValue binding)
    {
        string? value = null;
        string? sourceType = null;
        var sourceFound = false;
        var usedFallback = false;
        string? error = null;
        try
        {
            value = ResolveSpecialBinding(binding.Path);
            if (value is not null)
            {
                sourceFound = true;
                sourceType = "CUI theme context";
            }
            else if (_bindingContext is not null && _bindingContext.TryGetValue(binding.Path, out var result))
            {
                sourceFound = true;
                sourceType = result?.GetType().Name;
                value = result?.ToString();
            }
            else
            {
                usedFallback = binding.Fallback is not null;
                value = binding.Fallback;
            }
        }
        catch (Exception exception)
        {
            error = $"{exception.GetType().Name}: {exception.Message}";
            usedFallback = binding.Fallback is not null;
            value = binding.Fallback;
        }

        if (!_observedBindings.TryGetValue(control, out var observed))
            _observedBindings[control] = observed = new Dictionary<string, CuiObservedBinding>(StringComparer.OrdinalIgnoreCase);
        observed[property] = new CuiObservedBinding(property, binding, value, sourceType, usedFallback, sourceFound, error);
        return value;
    }

    private static Thickness ParseThickness(string value)
    {
        var parts = value.Split(',', ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            1 when double.TryParse(parts[0], out var v) => new Thickness(v),
            2 when double.TryParse(parts[0], out var h) && double.TryParse(parts[1], out var v) => new Thickness(h, v),
            4 when double.TryParse(parts[0], out var l) && double.TryParse(parts[1], out var t)
                  && double.TryParse(parts[2], out var r) && double.TryParse(parts[3], out var b)
                => new Thickness(l, t, r, b),
            _ => default,
        };
    }

    private static Avalonia.Media.IBrush ParseBrush(string value)
    {
        try
        {
            var color = Avalonia.Media.Color.Parse(value);
            return new Avalonia.Media.SolidColorBrush(color);
        }
        catch
        {
            return Avalonia.Media.Brushes.Transparent;
        }
    }

    private static bool TryParseCornerRadius(string value, out Avalonia.CornerRadius result)
    {
        var parts = value.Split(',', ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        result = default;
        if (parts.Length == 1 && double.TryParse(parts[0], out var single))
        {
            result = new Avalonia.CornerRadius(single);
            return true;
        }
        if (parts.Length == 4
            && double.TryParse(parts[0], out var tl)
            && double.TryParse(parts[1], out var tr)
            && double.TryParse(parts[2], out var br)
            && double.TryParse(parts[3], out var bl))
        {
            result = new Avalonia.CornerRadius(tl, tr, br, bl);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Applies theme resources to a control if it's a container type.
    /// This ensures theme resources cascade to children.
    /// </summary>
    private void ApplyThemeToControlIfContainer(Control control)
    {
        if (control is Panel || control is Border || control is ContentControl || control is Decorator)
        {
            var currentTheme = _themeStack.Current;
            CuiThemeScopeApplier.ApplyThemeToControl(control, currentTheme, _currentSurface);
        }
    }

    /// <summary>
    /// Resolves special theme-related binding paths.
    /// {Theme} → current theme name (e.g. "Glow")
    /// {Appearance} → current appearance (e.g. "Dark")
    /// </summary>
    private string? ResolveSpecialBinding(string path)
    {
        return path switch
        {
            "Theme" or "theme" => CuiThemeCatalog.Name(_themeStack.Current),
            "Appearance" or "appearance" => CuiThemeScopeApplier.DetectAppearance().ToString(),
            _ => null
        };
    }
}
