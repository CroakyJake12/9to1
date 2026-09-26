using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using System.ComponentModel;
using System.Collections.ObjectModel;
using CakeOS.Cui.Language;
using CakeOS.Cui.Themes;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// Loads a CuiDocument and instantiates live Avalonia control trees.
/// This is the bridge between the CUI language model and the Avalonia rendering pipeline.
/// </summary>
public sealed class CuiControlLoader : IDisposable
{
    private readonly CuiControlRegistry _controlRegistry;
    private readonly Dictionary<string, string> _resourceScope;
    private readonly List<CuiDiagnostic> _runtimeDiagnostics = [];
    private readonly List<CuiLiveBinding> _liveBindings = [];
    private readonly List<CuiLiveConditional> _liveConditionals = [];
    private readonly List<CuiRepeatState> _repeats = [];
    private readonly Dictionary<RepeatItemScope, PropertyChangedEventHandler> _scopeChangedHandlers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Control, CuiComponent> _authoredControls = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Control, Dictionary<string, CuiObservedBinding>> _observedBindings = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Control, CuiActionInvocation> _actionInvocations = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Control> _wiredActions = new(ReferenceEqualityComparer.Instance);
    private ICuiBindingContext? _bindingContext;
    private ICuiActionDispatcher? _actionDispatcher;
    private IReadOnlyDictionary<string, CuiActionDefinition> _documentActions =
        new Dictionary<string, CuiActionDefinition>(StringComparer.Ordinal);
    private CuiThemeScopeStack _themeStack = new(CuiSurfacePaletteCatalog.ActiveTheme);
    private string _currentSurface = "Home";
    private int _currentLayer;
    private string? _currentRepeatIdentity;
    private PropertyChangedEventHandler? _bindingChangedHandler;

    /// <summary>Set a binding context for live {Binding path} resolution.</summary>
    public void SetBindingContext(ICuiBindingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_bindingContext is INotifyPropertyChanged previous && _bindingChangedHandler is not null)
            previous.PropertyChanged -= _bindingChangedHandler;

        _bindingContext = context;
        _bindingChangedHandler = context is INotifyPropertyChanged observable
            ? (_, args) => OnBindingPropertyChanged(args.PropertyName)
            : null;
        if (context is INotifyPropertyChanged current && _bindingChangedHandler is not null)
            current.PropertyChanged += _bindingChangedHandler;
    }

    /// <summary>Set an action dispatcher for action= and on:click= attributes.</summary>
    public void SetActionDispatcher(ICuiActionDispatcher dispatcher) => _actionDispatcher = dispatcher;

    public void Dispose()
    {
        if (_bindingContext is INotifyPropertyChanged observable && _bindingChangedHandler is not null)
            observable.PropertyChanged -= _bindingChangedHandler;
        _bindingChangedHandler = null;
        _liveBindings.Clear();
        _liveConditionals.Clear();
        ClearRepeatSubscriptions();
        _repeats.Clear();
        _wiredActions.Clear();
        _actionInvocations.Clear();
        _currentRepeatIdentity = null;
    }

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
        _liveConditionals.Clear();
        ClearRepeatSubscriptions();
        _repeats.Clear();
        _authoredControls.Clear();
        _observedBindings.Clear();
        _wiredActions.Clear();
        _actionInvocations.Clear();
        _documentActions = document.Actions;
        _runtimeDiagnostics.Clear();
        _themeStack = new CuiThemeScopeStack(CuiSurfacePaletteCatalog.ActiveTheme);
        _currentLayer = 0;
        _currentRepeatIdentity = null;

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

    /// <summary>Load a document without throwing runtime diagnostics to the host.</summary>
    public (Control? Root, IReadOnlyList<CuiDiagnostic> Diagnostics) TryLoad(CuiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        try
        {
            var root = Load(document);
            return (root, _runtimeDiagnostics.ToArray());
        }
        catch (CuiRuntimeLoadException exception)
        {
            _runtimeDiagnostics.Clear();
            _runtimeDiagnostics.Add(exception.Diagnostic);
            return (null, _runtimeDiagnostics.ToArray());
        }
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
        if (control is Button button && _actionDispatcher is not null
            && TryGetActionInvocation(button, out var invocation))
        {
            var dispatcher = _actionDispatcher;
            button.Click += async (_, _) => await dispatcher.DispatchAsync(invocation.Command, invocation.Parameter);
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
        if (component.Type.Equals("Repeat", StringComparison.OrdinalIgnoreCase))
            return LoadRepeat(component);
        if (component.Type.Equals("If", StringComparison.OrdinalIgnoreCase))
            return LoadConditional(component);

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
        var parentLayer = _currentLayer;
        var layer = ResolveLayer(component, parentLayer);
        control.ZIndex = layer;
        _authoredControls.Add(control, component);
        CuiRuntimeIdentity.SetStableId(control, ComposeStableId(component.StableId));
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

        _currentLayer = layer;
        try
        {
            foreach (var child in component.Children)
            {
                var childControl = LoadComponent(child);
                AddChild(control, childControl);
            }
        }
        finally
        {
            _currentLayer = parentLayer;
        }

        return WrapScrollable(control, component);
    }

    private Control WrapScrollable(Control control, CuiComponent component)
    {
        var horizontal = GetBooleanProperty(component, "HorizontalScrolling");
        var vertical = GetBooleanProperty(component, "VerticalScrolling");
        if (!horizontal && !vertical)
            return control;

        var scrollViewer = new ScrollViewer
        {
            Content = control,
            HorizontalScrollBarVisibility = horizontal
                ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = vertical
                ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        CuiRuntimeIdentity.SetStableId(scrollViewer, $"{ComposeStableId(component.StableId)}:scroll");
        _authoredControls[scrollViewer] = component;
        return scrollViewer;
    }

    private bool GetBooleanProperty(CuiComponent component, string name)
    {
        var value = component.Properties.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        return value is not null && bool.TryParse(ResolveValue(value), out var result) && result;
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

    private Control LoadConditional(CuiComponent component)
    {
        if (component.Condition is null)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR010", CuiDiagnosticSeverity.Error,
                "If elements require a parsed condition before runtime lowering.", component.Span));

        var layer = ResolveLayer(component, _currentLayer);
        var host = new ContentControl
        {
            Name = component.Name,
            Content = BuildConditionalBranch(component, EvaluateCondition(component.Condition), layer)
        };
        host.ZIndex = layer;
        CuiRuntimeIdentity.SetStableId(host, ComposeStableId(component.StableId));
        if (component.Condition.IsLive && component.Condition.Test is CuiBindingValue binding)
            _liveConditionals.Add(new CuiLiveConditional(host, component, binding.Path, layer, _bindingContext!));
        return host;
    }

    private Panel BuildConditionalBranch(CuiComponent component, bool condition, int layer)
    {
        var branch = new Panel();
        var children = condition ? component.Children : component.ElseChildren;
        var previousLayer = _currentLayer;
        _currentLayer = layer;
        try
        {
            foreach (var child in children)
                branch.Children.Add(LoadComponent(child));
        }
        finally
        {
            _currentLayer = previousLayer;
        }
        return branch;
    }

    private int ResolveLayer(CuiComponent component, int inheritedLayer)
    {
        var layerValue = component.Properties.FirstOrDefault(
            pair => pair.Key.Equals("Layer", StringComparison.OrdinalIgnoreCase)).Value;
        if (layerValue is null)
            return inheritedLayer;
        var resolved = ResolveValue(layerValue);
        if (int.TryParse(resolved, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var layer))
            return layer;
        throw new CuiRuntimeLoadException(new CuiDiagnostic(
            "CUIR040", CuiDiagnosticSeverity.Error,
            $"Layer value '{resolved}' must be a signed integer.", layerValue.Span));
    }

    private Control LoadRepeat(CuiComponent component)
    {
        if (component.Repeat is null)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR050", CuiDiagnosticSeverity.Error,
                "Repeat elements require a parsed source, item name, and stable key.", component.Span));

        var layer = ResolveLayer(component, _currentLayer);
        var host = new Panel { Name = component.Name, ZIndex = layer };
        CuiRuntimeIdentity.SetStableId(host, ComposeStableId(component.StableId));
        _authoredControls[host] = component;
        var state = new CuiRepeatState(host, component, layer);
        _repeats.Add(state);
        ReconcileRepeat(state);
        return host;
    }

    private void ReconcileRepeat(CuiRepeatState state)
    {
        var definition = state.Component.Repeat!;
        var source = ResolveObject(definition.Source);
        if (source is null)
        {
            ReplaceRepeatItems(state, []);
            return;
        }
        if (source is string || source is not System.Collections.IEnumerable items)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR050", CuiDiagnosticSeverity.Error,
                $"Repeat source '{definition.Source}' did not resolve to a non-string enumerable value.",
                definition.Source.Span));

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var nextItems = new List<(string Key, object? Item)>();
        foreach (var item in items)
        {
            var itemScope = new RepeatItemScope(this, definition.ItemName, item, _bindingContext);
            if (!TryResolveRepeatKey(definition.Key, itemScope, out var key) || key is null)
                throw new CuiRuntimeLoadException(new CuiDiagnostic(
                    "CUIR051", CuiDiagnosticSeverity.Error,
                    "Every Repeat item must resolve to a non-null stable key.", definition.Key.Span));
            if (!seenKeys.Add(key))
                throw new CuiRuntimeLoadException(new CuiDiagnostic(
                    "CUIR052", CuiDiagnosticSeverity.Error,
                    $"Repeat key '{key}' is duplicated in this source collection.", definition.Key.Span));
            nextItems.Add((key, item));
        }

        ReplaceRepeatItems(state, nextItems);
        state.SetSource(source, () => RequestRepeatRefresh(state));
    }

    private bool TryResolveRepeatKey(CuiValue keyValue, RepeatItemScope scope, out string? key)
    {
        object? value = keyValue switch
        {
            CuiLiteralValue literal => literal.Value,
            CuiBindingValue binding when scope.TryGetValue(binding.Path, out var resolved) => resolved,
            _ => null,
        };
        key = value is null ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        return key is not null;
    }

    private void ReplaceRepeatItems(CuiRepeatState state, IReadOnlyList<(string Key, object? Item)> nextItems)
    {
        var nextInstances = new Dictionary<string, RepeatItemInstance>(StringComparer.Ordinal);
        var nextRoots = new List<Control>(nextItems.Count);
        foreach (var (key, item) in nextItems)
        {
            if (state.Items.TryGetValue(key, out var existing))
            {
                existing.Scope.Update(item);
                RefreshBindingsForContext(existing.Scope, null);
                nextInstances.Add(key, existing);
                nextRoots.Add(existing.Root);
                continue;
            }

            var identity = CreateRepeatIdentityPrefix(state.Component, key);
            var scope = new RepeatItemScope(this, state.Component.Repeat!.ItemName, item, _bindingContext);
            var itemRoot = new Panel { DataContext = scope };
            CuiRuntimeIdentity.SetStableId(itemRoot, identity);
            var previousContext = _bindingContext;
            var previousIdentity = _currentRepeatIdentity;
            _bindingContext = scope;
            _currentRepeatIdentity = identity;
            try
            {
                foreach (var child in state.Component.Children)
                    itemRoot.Children.Add(LoadComponent(child));
            }
            finally
            {
                _bindingContext = previousContext;
                _currentRepeatIdentity = previousIdentity;
            }

            var instance = new RepeatItemInstance(scope, itemRoot);
            SubscribeScope(scope);
            nextInstances.Add(key, instance);
            nextRoots.Add(itemRoot);
        }

        foreach (var (key, removed) in state.Items)
        {
            if (nextInstances.ContainsKey(key))
                continue;
            UnsubscribeScope(removed.Scope);
            var removedControls = EnumerateControls(removed.Root).ToHashSet(ReferenceEqualityComparer.Instance);
            _liveBindings.RemoveAll(binding => removedControls.Contains(binding.Control));
            foreach (var control in removedControls)
            {
                _authoredControls.Remove(control);
                _observedBindings.Remove(control);
                _actionInvocations.Remove(control);
            }
        }

        state.Items.Clear();
        foreach (var entry in nextInstances)
            state.Items.Add(entry.Key, entry.Value);
        state.Host.Children.Clear();
        foreach (var root in nextRoots)
            state.Host.Children.Add(root);
    }

    private string CreateRepeatIdentityPrefix(CuiComponent component, string key)
    {
        var segment = $"{component.StableId}|key:{key.Length}:{key}";
        return _currentRepeatIdentity is null ? segment : $"{_currentRepeatIdentity}/{segment}";
    }

    private string ComposeStableId(string stableId) =>
        _currentRepeatIdentity is null ? stableId : $"{_currentRepeatIdentity}/{stableId}";

    private void SubscribeScope(RepeatItemScope scope)
    {
        PropertyChangedEventHandler handler = (_, args) =>
        {
            void Refresh() => RefreshBindingsForContext(scope, args.PropertyName);
            if (Dispatcher.UIThread.CheckAccess())
                Refresh();
            else
                Dispatcher.UIThread.Post(Refresh);
        };
        _scopeChangedHandlers.Add(scope, handler);
        scope.PropertyChanged += handler;
    }

    private void UnsubscribeScope(RepeatItemScope scope)
    {
        if (_scopeChangedHandlers.Remove(scope, out var handler))
            scope.PropertyChanged -= handler;
    }

    private void ClearRepeatSubscriptions()
    {
        foreach (var repeat in _repeats)
            repeat.Dispose();
        foreach (var scope in _scopeChangedHandlers.Keys.ToArray())
            UnsubscribeScope(scope);
    }

    private void RequestRepeatRefresh(CuiRepeatState state)
    {
        void Refresh()
        {
            try
            {
                ReconcileRepeat(state);
            }
            catch (CuiRuntimeLoadException exception)
            {
                _runtimeDiagnostics.Add(exception.Diagnostic);
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
            Refresh();
        else
            Dispatcher.UIThread.Post(Refresh);
    }

    private void RefreshBindingsForContext(ICuiBindingContext context, string? propertyName)
    {
        foreach (var binding in _liveBindings.Where(binding => ReferenceEquals(binding.Context, context)).ToArray())
        {
            var leaf = binding.Binding.Path[(binding.Binding.Path.LastIndexOf('.') + 1)..];
            if (!string.IsNullOrEmpty(propertyName)
                && !binding.Binding.Path.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                && !leaf.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                continue;
            var resolved = ResolveObservedBinding(
                binding.Control, binding.PropertyName, binding.Binding, context);
            if (resolved is not null)
                ApplyLiteralProperty(binding.Control, binding.PropertyName, resolved, binding.Span);
        }
        RefreshLiveConditionals(propertyName, context);
    }

    private bool EvaluateCondition(CuiCondition condition)
    {
        var value = ResolveObject(condition.Test);
        bool result;
        if (value is bool boolean)
            result = boolean;
        else if (value is string text && bool.TryParse(text, out var parsed))
            result = parsed;
        else
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR011", CuiDiagnosticSeverity.Error,
                "A CUI condition must resolve to a boolean value.", condition.Span));

        return condition.Negate ? !result : result;
    }

    private void ApplyProperties(Control control, CuiComponent component)
    {
        foreach (var (propName, value) in component.Properties)
        {
            // Type selects the native lowering of core polymorphic elements.
            if (propName.Equals("Type", StringComparison.OrdinalIgnoreCase)
                && (component.Type.Equals("Container", StringComparison.OrdinalIgnoreCase)
                    || component.Type.Equals("Input", StringComparison.OrdinalIgnoreCase)
                    || component.Type.Equals("Object", StringComparison.OrdinalIgnoreCase)))
                continue;

            if (value is CuiInvalidValue invalid)
            {
                throw new CuiRuntimeLoadException(new CuiDiagnostic(
                    invalid.DiagnosticCode, CuiDiagnosticSeverity.Error, invalid.Message, invalid.Span));
            }

            // Live binding: if value is a CuiBindingValue, create a real Avalonia binding
            if (value is CuiBindingValue binding && _bindingContext is not null)
            {
                ApplyLiveBinding(control, propName, binding, component.Span);
                continue;
            }

            var resolved = ResolveValue(value);
            if (resolved is null) continue;

            ApplyLiteralProperty(control, propName, resolved, component.Span);
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

    private void ApplyLiveBinding(Control control, string propName, CuiBindingValue binding, CuiSourceSpan sourceSpan)
    {
        // Live values update from host notifications; OneTime is a snapshot.
        if (binding.Mode != CuiBindingMode.OneTime)
            _liveBindings.Add(new CuiLiveBinding(control, propName, binding, sourceSpan, _bindingContext!));

        // Apply initial value
        var context = _bindingContext!;
        var resolved = ResolveObservedBinding(control, propName, binding, context);
        if (resolved is null) return;
        ApplyLiteralProperty(control, propName, resolved, sourceSpan);

        if (binding.Mode == CuiBindingMode.TwoWay)
            WireInputWriteBack(control, binding, sourceSpan, context);
    }

    /// <summary>
    /// Refresh all tracked live bindings from the current binding context.
    /// Call this when the ViewModel data changes to push updates to the UI.
    /// </summary>
    public void RefreshBindings()
    {
        foreach (var record in _liveBindings)
        {
            var resolved = ResolveObservedBinding(record.Control, record.PropertyName, record.Binding, record.Context);
            if (resolved is null) continue;
            ApplyLiteralProperty(record.Control, record.PropertyName, resolved, record.Span);
        }
        RefreshLiveConditionals(null);
        foreach (var repeat in _repeats.ToArray())
            RequestRepeatRefresh(repeat);
    }

    private void OnBindingPropertyChanged(string? propertyName)
    {
        void RefreshAffectedBindings()
        {
            foreach (var record in _liveBindings)
            {
                var leaf = record.Binding.Path[(record.Binding.Path.LastIndexOf('.') + 1)..];
                if (!string.IsNullOrEmpty(propertyName)
                    && !record.Binding.Path.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                    && !leaf.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var resolved = ResolveObservedBinding(record.Control, record.PropertyName, record.Binding, record.Context);
                if (resolved is not null)
                    ApplyLiteralProperty(record.Control, record.PropertyName, resolved, record.Span);
            }
            RefreshLiveConditionals(propertyName);
            foreach (var repeat in _repeats.ToArray())
            {
                var sourcePath = (repeat.Component.Repeat?.Source as CuiBindingValue)?.Path;
                var leaf = sourcePath is null ? null : sourcePath[(sourcePath.LastIndexOf('.') + 1)..];
                if (string.IsNullOrEmpty(propertyName) || sourcePath is null
                    || sourcePath.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                    || leaf!.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                    RequestRepeatRefresh(repeat);
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
            RefreshAffectedBindings();
        else
            Dispatcher.UIThread.Post(RefreshAffectedBindings);
    }

    private void RefreshLiveConditionals(string? propertyName, ICuiBindingContext? context = null)
    {
        foreach (var conditional in _liveConditionals)
        {
            var leaf = conditional.Path[(conditional.Path.LastIndexOf('.') + 1)..];
            if (!string.IsNullOrEmpty(propertyName)
                && !conditional.Path.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                && !leaf.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (context is not null && !ReferenceEquals(context, conditional.Context))
                continue;
            var previousContext = _bindingContext;
            _bindingContext = conditional.Context;
            try
            {
                conditional.Host.Content = BuildConditionalBranch(
                    conditional.Component,
                    EvaluateCondition(conditional.Component.Condition!),
                    conditional.Layer);
            }
            finally
            {
                _bindingContext = previousContext;
            }
        }
    }

    private object? ResolveObject(CuiValue value)
    {
        return value switch
        {
            CuiLiteralValue literal => literal.Value,
            CuiBindingValue binding when _bindingContext is not null
                && _bindingContext.TryGetValue(binding.Path, out var resolved) => resolved,
            CuiBindingValue binding => binding.Fallback,
            CuiResourceValue resource => ResolveResourceValue(resource.Key),
            _ => null,
        };
    }

    private void WireInputWriteBack(Control control, CuiBindingValue binding, CuiSourceSpan sourceSpan, ICuiBindingContext context)
    {
        if (context is not ICuiWritableBindingContext writable)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR006", CuiDiagnosticSeverity.Error,
                $"Two-way binding '{binding.Path}' requires a host that implements {nameof(ICuiWritableBindingContext)}.",
                sourceSpan));

        void Update(object? rawValue)
        {
            if (!TryConvertInput(binding.Path, rawValue, binding.TargetType, context, out var converted))
            {
                CuiInputValidationProperties.SetHasError(control, true);
                return;
            }

            if (!writable.TrySetValue(binding.Path, converted))
            {
                CuiInputValidationProperties.SetHasError(control, true);
                return;
            }

            CuiInputValidationProperties.SetHasError(control, false);
        }

        switch (control)
        {
            case TextBox textBox:
                textBox.TextChanged += (_, _) => Update(textBox.Text);
                break;
            case CheckBox checkBox:
                checkBox.IsCheckedChanged += (_, _) => Update(checkBox.IsChecked);
                break;
            case Slider slider:
                slider.ValueChanged += (_, args) => Update(args.NewValue);
                break;
            case NumericUpDown number:
                number.ValueChanged += (_, args) => Update(args.NewValue);
                break;
            default:
                throw new CuiRuntimeLoadException(new CuiDiagnostic(
                    "CUIR007", CuiDiagnosticSeverity.Error,
                    $"Two-way binding is not supported on native control '{control.GetType().Name}'.",
                    sourceSpan));
        }
    }

    private bool TryConvertInput(string path, object? rawValue, string? declaredType, ICuiBindingContext context, out object? converted)
    {
        converted = null;
        var sourceType = declaredType?.ToLowerInvariant() switch
        {
            "boolean" or "bool" => typeof(bool),
            "integer" or "int" => typeof(int),
            "decimal" => typeof(decimal),
            "number" or "double" => typeof(double),
            "string" or "text" => typeof(string),
            _ when context.TryGetValue(path, out var sourceValue)
                && sourceValue is not null => sourceValue.GetType(),
            _ => typeof(string)
        };

        if (rawValue is null)
        {
            if (sourceType == typeof(string) || !sourceType.IsValueType)
            {
                converted = string.Empty;
                return true;
            }
            return false;
        }

        var text = rawValue.ToString() ?? string.Empty;
        if (sourceType == typeof(string))
        {
            converted = text;
            return true;
        }
        if (sourceType == typeof(bool) && bool.TryParse(text, out var boolean))
        {
            converted = boolean;
            return true;
        }
        if (sourceType == typeof(int) && int.TryParse(text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var integer))
        {
            converted = integer;
            return true;
        }
        if (sourceType == typeof(decimal) && decimal.TryParse(text, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var decimalValue))
        {
            converted = decimalValue;
            return true;
        }
        if (sourceType == typeof(double) && double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var numberValue)
            && double.IsFinite(numberValue))
        {
            converted = numberValue;
            return true;
        }
        return false;
    }

    private void ApplyLiteralProperty(Control control, string propName, string resolved, CuiSourceSpan sourceSpan)
    {
        var normalizedPropertyName = propName.Replace("-", string.Empty, StringComparison.Ordinal);
            switch (normalizedPropertyName.ToLowerInvariant())
            {
                case "rotate":
                case "scale":
                case "scalex":
                case "scaley":
                case "skew":
                case "skewx":
                case "skewy":
                case "translate":
                case "translatex":
                case "translatey":
                    CuiRuntimeTransformProperties.Set(control, normalizedPropertyName, resolved);
                    break;
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
                    if (ContainsBackdropModifier(resolved))
                    {
                        if (_controlRegistry.TryApplyBackdrop(control, resolved, sourceSpan, out var backdropError))
                            break;
                        throw new CuiRuntimeLoadException(new CuiDiagnostic(
                            "CUIR032", CuiDiagnosticSeverity.Error,
                            backdropError ?? "The registered compositor rejected the Background modifier chain.", sourceSpan));
                    }
                    var bg = ParseBrush(resolved, sourceSpan);
                    if (control is Panel p)
                        p.Background = bg;
                    else if (control is Border b)
                        b.Background = bg;
                    else if (control is Avalonia.Controls.Primitives.TemplatedControl tc)
                        tc.Background = bg;
                    break;
                case "foreground":
                    if (control is TextBlock fgtb)
                        fgtb.Foreground = ParseBrush(resolved, sourceSpan);
                    else if (control is Avalonia.Controls.Primitives.TemplatedControl fgtc)
                        fgtc.Foreground = ParseBrush(resolved, sourceSpan);
                    break;
                case "borderbrush":
                    if (control is Border bb)
                        bb.BorderBrush = ParseBrush(resolved, sourceSpan);
                    else if (control is Avalonia.Controls.Primitives.TemplatedControl tbc)
                        tbc.BorderBrush = ParseBrush(resolved, sourceSpan);
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
                case "active":
                    if (bool.TryParse(resolved, out var active))
                        control.IsVisible = active;
                    break;
                case "layer":
                    if (int.TryParse(resolved, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var layer))
                        control.ZIndex = layer;
                    break;
                case "hidden":
                    if (bool.TryParse(resolved, out var hidden))
                        control.IsVisible = !hidden;
                    break;
                case "interactive":
                    if (bool.TryParse(resolved, out var interactive))
                    {
                        control.IsHitTestVisible = interactive;
                        control.Focusable = interactive;
                    }
                    break;
                case "modal":
                    if (bool.TryParse(resolved, out var modal) && modal)
                        throw new CuiRuntimeLoadException(new CuiDiagnostic(
                            "CUIR041", CuiDiagnosticSeverity.Error,
                            "Modal behavior requires a registered surface-level focus and input host.", sourceSpan));
                    break;
                case "activeindependently":
                    if (bool.TryParse(resolved, out var independent) && independent)
                        throw new CuiRuntimeLoadException(new CuiDiagnostic(
                            "CUIR042", CuiDiagnosticSeverity.Error,
                            "ActiveIndependently requires a runtime activity-scope host that is not registered.", sourceSpan));
                    break;
                case "ishittestvisible":
                    if (bool.TryParse(resolved, out var hitTest))
                        control.IsHitTestVisible = hitTest;
                    break;
                case "effect":
                    control.Effect = ParseEffect(resolved, sourceSpan, isShadow: false);
                    break;
                case "shadow":
                    control.Effect = ParseEffect(resolved, sourceSpan, isShadow: true);
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
                case "value":
                    if (control is TextBox inputText)
                        inputText.Text = resolved;
                    else if (control is Slider inputSlider && double.TryParse(resolved, System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out var sliderValue))
                        inputSlider.Value = sliderValue;
                    else if (control is NumericUpDown inputNumber && decimal.TryParse(resolved, System.Globalization.NumberStyles.Number,
                                 System.Globalization.CultureInfo.InvariantCulture, out var numberValue))
                        inputNumber.Value = numberValue;
                    break;
                case "checked":
                case "ischecked":
                    if (control is CheckBox checkBox && bool.TryParse(resolved, out var isChecked))
                        checkBox.IsChecked = isChecked;
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
        string? reference = control.Tag as string;
        if (string.IsNullOrWhiteSpace(reference)
            && component.Actions.TryGetValue("action", out var actionRef))
            reference = actionRef.Name;
        if (string.IsNullOrWhiteSpace(reference)
            && component.Actions.TryGetValue("on:click", out var clickRef))
            reference = clickRef.Name;

        if (string.IsNullOrWhiteSpace(reference))
            return;

        control.Tag = reference;
        var command = reference;
        object? parameter = null;
        if (_documentActions.TryGetValue(reference, out var definition))
        {
            command = definition.Command;
            if (definition.Parameter is not null)
                parameter = ResolveObject(definition.Parameter);
        }
        _actionInvocations[control] = new CuiActionInvocation(command, parameter);
    }

    private bool TryGetActionInvocation(Control control, out CuiActionInvocation invocation)
    {
        if (_actionInvocations.TryGetValue(control, out invocation!))
            return true;
        if (control.Tag is string command && !string.IsNullOrWhiteSpace(command))
        {
            invocation = new CuiActionInvocation(command, null);
            return true;
        }
        invocation = null!;
        return false;
    }

    private sealed record CuiActionInvocation(string Command, object? Parameter);

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

    private string? ResolveObservedBinding(
        Control control,
        string property,
        CuiBindingValue binding,
        ICuiBindingContext context)
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
            else if (context.TryGetValue(binding.Path, out var result))
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

    private static Avalonia.Media.IBrush ParseBrush(string value, CuiSourceSpan sourceSpan)
    {
        if (ContainsBackdropModifier(value))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR032", CuiDiagnosticSeverity.Error,
                "Live Background Blur/Refract modifiers require a compositor backdrop host, which is not registered.",
                sourceSpan));
        try
        {
            if (TryParseGradient(value, out var gradient))
                return gradient;
            var color = Avalonia.Media.Color.Parse(value);
            return new Avalonia.Media.SolidColorBrush(color);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR030", CuiDiagnosticSeverity.Error,
                $"Invalid brush value '{value}': {exception.Message}", sourceSpan));
        }
    }

    private static bool ContainsBackdropModifier(string value) =>
        value.Contains("Blur(", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Refract(", StringComparison.OrdinalIgnoreCase);

    private static Avalonia.Media.IEffect ParseEffect(string value, CuiSourceSpan sourceSpan, bool isShadow)
    {
        var open = value.IndexOf('(');
        if (open <= 0 || !value.EndsWith(')'))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR031", CuiDiagnosticSeverity.Error,
                $"Effect value '{value}' must use a supported function form such as Blur(0.5).", sourceSpan));

        var name = value[..open].Trim();
        var argument = value[(open + 1)..^1].Trim();
        if (!double.TryParse(argument.TrimEnd('%'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var ratio))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR031", CuiDiagnosticSeverity.Error,
                $"Effect amount '{argument}' is not a valid number or percentage.", sourceSpan));
        if (argument.EndsWith('%'))
            ratio /= 100d;
        if (ratio < 0 || ratio > 1)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR031", CuiDiagnosticSeverity.Error,
                "Effect blur ratios must be between 0 and 1 (or 0% and 100%).", sourceSpan));

        if (name.Equals("Blur", StringComparison.OrdinalIgnoreCase) && !isShadow)
            return new Avalonia.Media.BlurEffect { Radius = ratio * 32d };
        if ((name.Equals("DropShadow", StringComparison.OrdinalIgnoreCase)
             || name.Equals("Shadow", StringComparison.OrdinalIgnoreCase)) && isShadow)
            return new Avalonia.Media.DropShadowEffect { BlurRadius = ratio * 32d };

        throw new CuiRuntimeLoadException(new CuiDiagnostic(
            "CUIR031", CuiDiagnosticSeverity.Error,
            $"Effect '{name}' is not registered for {(isShadow ? "Shadow" : "Effect")}.", sourceSpan));
    }

    private static bool TryParseGradient(string value, out Avalonia.Media.IBrush brush)
    {
        brush = null!;
        if (!value.StartsWith("Gradient(", StringComparison.OrdinalIgnoreCase))
            return false;

        var typeEnd = value.IndexOf(')');
        if (typeEnd < 0)
            throw new FormatException("Gradient requires a closing ')' after its type.");
        var type = value["Gradient(".Length..typeEnd].Trim();
        if (!type.Equals("Linear", StringComparison.OrdinalIgnoreCase)
            && !type.Equals("Radial", StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"Gradient type '{type}' is not registered.");

        var stopText = value[(typeEnd + 1)..].Trim().TrimStart('.', ',').Trim();
        var tokens = stopText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
            throw new FormatException("A gradient requires at least two color stops.");

        var stops = tokens.Select(ParseGradientStop).ToArray();
        AssignGradientOffsets(stops);
        var gradientStops = new Avalonia.Media.GradientStops();
        foreach (var stop in stops)
            gradientStops.Add(new Avalonia.Media.GradientStop(stop.Color, stop.Offset!.Value));

        brush = type.Equals("Linear", StringComparison.OrdinalIgnoreCase)
            ? new Avalonia.Media.LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops = gradientStops,
            }
            : new Avalonia.Media.RadialGradientBrush { GradientStops = gradientStops };
        return true;
    }

    private static GradientStopValue ParseGradientStop(string token)
    {
        string? position = null;
        var open = token.LastIndexOf('(');
        if (open >= 0 && token.EndsWith(')'))
        {
            position = token[(open + 1)..^1].Trim();
            token = token[..open].Trim();
        }

        var color = Avalonia.Media.Color.Parse(token);
        double? offset = null;
        if (position is not null)
        {
            if (!position.EndsWith('%')
                || !double.TryParse(position[..^1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var percent)
                || percent is < 0 or > 100)
                throw new FormatException($"Gradient stop position '{position}' must be a percentage from 0% to 100%.");
            offset = percent / 100d;
        }
        return new GradientStopValue(color, offset);
    }

    private static void AssignGradientOffsets(IList<GradientStopValue> stops)
    {
        if (stops.All(stop => stop.Offset is null))
        {
            for (var index = 0; index < stops.Count; index++)
                stops[index] = stops[index] with { Offset = index / (double)(stops.Count - 1) };
            return;
        }

        stops[0] = stops[0] with { Offset = stops[0].Offset ?? 0 };
        stops[^1] = stops[^1] with { Offset = stops[^1].Offset ?? 1 };
        var left = 0;
        while (left < stops.Count - 1)
        {
            var right = left + 1;
            while (right < stops.Count && stops[right].Offset is null)
                right++;
            if (right == stops.Count)
                break;
            var leftOffset = stops[left].Offset!.Value;
            var rightOffset = stops[right].Offset!.Value;
            if (rightOffset < leftOffset)
                throw new FormatException("Gradient stop positions must be in ascending order.");
            var interval = rightOffset - leftOffset;
            for (var index = left + 1; index < right; index++)
                stops[index] = stops[index] with { Offset = leftOffset + interval * (index - left) / (right - left) };
            left = right;
        }
    }

    private sealed record GradientStopValue(Avalonia.Media.Color Color, double? Offset);

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

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        yield return root;
        switch (root)
        {
            case Panel panel:
                foreach (var child in panel.Children.OfType<Control>())
                foreach (var descendant in EnumerateControls(child))
                    yield return descendant;
                break;
            case Decorator { Child: Control child }:
                foreach (var descendant in EnumerateControls(child))
                    yield return descendant;
                break;
            case ContentControl { Content: Control child }:
                foreach (var descendant in EnumerateControls(child))
                    yield return descendant;
                break;
        }
    }

    private sealed record CuiLiveBinding(
        Control Control,
        string PropertyName,
        CuiBindingValue Binding,
        CuiSourceSpan Span,
        ICuiBindingContext Context);

    private sealed record CuiLiveConditional(
        ContentControl Host,
        CuiComponent Component,
        string Path,
        int Layer,
        ICuiBindingContext Context);

    private sealed record RepeatItemInstance(RepeatItemScope Scope, Panel Root);

    private sealed class CuiRepeatState : IDisposable
    {
        private object? _source;
        private System.Collections.Specialized.INotifyCollectionChanged? _observableSource;
        private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _handler;

        public CuiRepeatState(Panel host, CuiComponent component, int layer)
        {
            Host = host;
            Component = component;
            Layer = layer;
        }

        public Panel Host { get; }
        public CuiComponent Component { get; }
        public int Layer { get; }
        public Dictionary<string, RepeatItemInstance> Items { get; } = new(StringComparer.Ordinal);

        public void SetSource(object source, Action changed)
        {
            if (ReferenceEquals(source, _source))
                return;
            if (_observableSource is not null && _handler is not null)
                _observableSource.CollectionChanged -= _handler;
            _source = source;
            _observableSource = source as System.Collections.Specialized.INotifyCollectionChanged;
            _handler = _observableSource is null ? null : (_, _) => changed();
            if (_observableSource is not null && _handler is not null)
                _observableSource.CollectionChanged += _handler;
        }

        public void Dispose()
        {
            if (_observableSource is not null && _handler is not null)
                _observableSource.CollectionChanged -= _handler;
            _observableSource = null;
            _handler = null;
            _source = null;
        }
    }

    private sealed class RepeatItemScope : ICuiWritableBindingContext, INotifyPropertyChanged
    {
        private readonly CuiControlLoader _owner;
        private readonly ICuiBindingContext? _parent;
        private object? _item;
        private INotifyPropertyChanged? _observableItem;

        public RepeatItemScope(CuiControlLoader owner, string itemName, object? item, ICuiBindingContext? parent)
        {
            _owner = owner;
            ItemName = itemName;
            _parent = parent;
            Update(item);
        }

        public string ItemName { get; }
        public event PropertyChangedEventHandler? PropertyChanged;

        public void Update(object? item)
        {
            if (_observableItem is not null)
                _observableItem.PropertyChanged -= OnItemPropertyChanged;
            _item = item;
            _observableItem = item as INotifyPropertyChanged;
            if (_observableItem is not null)
                _observableItem.PropertyChanged += OnItemPropertyChanged;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }

        public bool TryGetValue(string path, out object? value)
        {
            if (path.Equals(ItemName, StringComparison.Ordinal))
            {
                value = _item;
                return true;
            }
            if (path.StartsWith(ItemName + ".", StringComparison.Ordinal))
                return _owner.TryResolveItemValue(_item, path[(ItemName.Length + 1)..], out value);
            return _parent is not null && _parent.TryGetValue(path, out value);
        }

        public bool TrySetValue(string path, object? value)
        {
            if (path.Equals(ItemName, StringComparison.Ordinal))
                return false;
            if (path.StartsWith(ItemName + ".", StringComparison.Ordinal))
                return _owner.TrySetItemValue(_item, path[(ItemName.Length + 1)..], value);
            return _parent is ICuiWritableBindingContext writable && writable.TrySetValue(path, value);
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs args) =>
            PropertyChanged?.Invoke(this, args);
    }

    private bool TryResolveItemValue(object? item, string path, out object? value)
    {
        value = null;
        if (item is null)
            return false;
        if (_bindingContext is ICuiRepeatItemBindingContext provider
            && provider.TryGetItemValue(item, path, out value))
            return true;
        if (item is ICuiBindingContext itemContext && itemContext.TryGetValue(path, out value))
            return true;
        return TryResolveDictionaryPath(item, path, out value);
    }

    private bool TrySetItemValue(object? item, string path, object? value)
    {
        if (item is null)
            return false;
        if (_bindingContext is ICuiRepeatItemBindingContext provider
            && provider.TrySetItemValue(item, path, value))
            return true;
        if (item is ICuiWritableBindingContext writable && writable.TrySetValue(path, value))
            return true;
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && item is IDictionary<string, object?> dictionary && dictionary.ContainsKey(parts[0]))
        {
            dictionary[parts[0]] = value;
            return true;
        }
        return false;
    }

    private static bool TryResolveDictionaryPath(object item, string path, out object? value)
    {
        value = item;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (value is IReadOnlyDictionary<string, object?> readOnly
                && readOnly.TryGetValue(segment, out var next))
                value = next;
            else if (value is IDictionary<string, object?> dictionary
                && dictionary.TryGetValue(segment, out next))
                value = next;
            else
            {
                value = null;
                return false;
            }
        }
        return true;
    }
}
