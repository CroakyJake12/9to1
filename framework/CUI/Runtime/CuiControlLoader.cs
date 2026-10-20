using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Frozen;
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
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    /// <summary>Incremented at the start of every Load attempt. External callbacks encountered
    /// while mounting source-driven choose-once variables (source getter, enumerator, chooser) may
    /// synchronously reload or dispose this loader; this generation/index the stale outer attempt
    /// after any such callback so it cannot overwrite a replacement mount or resume after disposal.</summary>
    private int _loadGeneration;
    private readonly object _actionTasksGate = new();
    private readonly HashSet<Task> _pendingActionTasks = [];
    private const int MaximumRetainedActionTasks = 128;
    private Task? _admissionFailureTask;
    private Task? _disposalTask;
    public event EventHandler<CuiActionFailure>? ActionFailed;
    private readonly Dictionary<string, string> _resourceScope;
    private readonly List<CuiDiagnostic> _runtimeDiagnostics = [];
    private readonly List<CuiLiveBinding> _liveBindings = [];
    private readonly HashSet<Control> _applyingHostValues = new(ReferenceEqualityComparer.Instance);
    private readonly List<CuiLiveConditional> _liveConditionals = [];
    private readonly List<CuiRepeatState> _repeats = [];
    private readonly Dictionary<RepeatItemScope, PropertyChangedEventHandler> _scopeChangedHandlers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Control, CuiComponent> _authoredControls = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Control, Dictionary<string, CuiObservedBinding>> _observedBindings = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Control, CuiActionInvocation> _actionInvocations = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Control> _wiredActions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Control, EventHandler<RoutedEventArgs>> _actionHandlers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Control, ContextMenu> _ownedContextMenus = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ContextMenu, (EventHandler<AvaloniaPropertyChangedEventArgs> Opening, EventHandler<RoutedEventArgs> Closed)> _contextMenuFocusHandlers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ContextMenu, Task> _contextMenuCloses = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, CuiComponent> _typeDefinitions = new(StringComparer.Ordinal);
    private const string LocalToggleCommand = "cui.local-toggle";
    private readonly Dictionary<string, CuiVariableDeclaration> _declaredVariables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _localState = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<CuiLiveBinding>> _localBindings = new(StringComparer.Ordinal);
    /// <summary>Override for the generic random-choose chooser (deterministic tests/hosts set this
    /// to a fixed pick; null uses the shared random source). The pool is never empty when invoked.</summary>
    public Func<IReadOnlyList<string>, string>? RandomSelectionOverride { get; set; }
    private bool _rootOwnsBackdrop;
    private int _loadingDepth;
    private bool _isCurrentRootControl;
    private CuiThemePreferenceContext? _themePreference;
    private CuiThemeDefinitionCatalog _localThemes = new();
    private readonly Stack<string> _themeNames = new();
    private readonly Dictionary<Control, string> _controlThemeNames = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Control, IResourceProvider> _controlThemeResources = new(ReferenceEqualityComparer.Instance);
    private readonly record struct ThemeResourceKey(CuiPalette Palette, CuiAccessibilitySettings Accessibility, string Name, string? Fingerprint, string? ExplicitAccent);
    private readonly Dictionary<Control, ThemeResourceKey> _appliedThemeKeys = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ThemeResourceKey,
        FrozenDictionary<object, object?>> _themeResourceSnapshots = new();
    private ICuiBindingContext? _bindingContext;
    private ICuiActionDispatcher? _actionDispatcher;
    private IReadOnlyDictionary<string, CuiActionDefinition> _documentActions =
        new Dictionary<string, CuiActionDefinition>(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, CuiTemplateDefinition> _documentTemplates =
        new Dictionary<string, CuiTemplateDefinition>(StringComparer.Ordinal);
    private readonly Dictionary<string, CuiComponent> _definitions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _definitionPath = new(StringComparer.Ordinal);
    private string? _definitionScope;
    private CuiThemeScopeStack _themeStack = new(CuiSurfacePaletteCatalog.ActiveTheme);
    private string _currentSurface = "Home";
    private string? _accentAppId;
    private CuiAppearance? _appearance;
    private int _currentLayer;
    private string? _currentRepeatIdentity;
    private PropertyChangedEventHandler? _bindingChangedHandler;

    public void SetThemePreferenceContext(CuiThemePreferenceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (ReferenceEquals(context, _themePreference)) return;
        if (_themePreference is not null) _themePreference.Changed -= ThemePreferenceChanged;
        _themePreference = context; context.Changed += ThemePreferenceChanged;
        ThemePreferenceChanged(this, EventArgs.Empty);
    }

    private CuiThemeResolution ResolveTheme(string name)
    {
        var resolved = _themePreference?.ResolveScope(name);
        if (resolved is not null && !resolved.UsedFallback) return resolved;
        return CuiThemeScope.Resolve(name, _localThemes, _themePreference, CuiSurfacePaletteCatalog.ActiveTheme);
    }

    private void ThemePreferenceChanged(object? sender, EventArgs args)
    {
        void RefreshTheme()
        {
            if (_disposed) return;
            List<Exception>? failures = null;
            foreach (var (control, name) in _controlThemeNames.ToArray())
            {
                try
                {
                    var resolution = ResolveTheme(name);
                    if (!ApplyResolvedTheme(control, resolution)) continue;
                    if (!_authoredControls.TryGetValue(control, out var component)) continue;
                    _themeStack.Push(resolution.Theme.BaseTheme);
                    try
                    {
                        foreach (var (property, value) in component.Properties)
                        {
                            if (value is CuiResourceValue resource) ApplyResourceProperty(control, property, resource);
                            else if ((control is Image && property.Equals("Source", StringComparison.OrdinalIgnoreCase)
                                || control is MenuItem && property.Equals("IconSource", StringComparison.OrdinalIgnoreCase)) && value is CuiLiteralValue literal)
                                ApplyLiteralProperty(control, property, literal.Value, value.Span);
                        }
                    }
                    finally { _themeStack.Pop(); }
                }
                catch (Exception error)
                {
                    // Resource projection can fail after the provider was attached.
                    // An identical-revision retry must still visit that control.
                    _appliedThemeKeys.Remove(control);
                    (failures ??= []).Add(error);
                }
            }
            // A failing renderer is local to that control. Let the remaining
            // controls observe the committed Home preference, while retaining
            // the loader's failed-observer status for an identical-revision retry.
            if (failures is not null) throw new AggregateException("One or more CUI controls could not apply the theme.", failures);
        }
        // Preserve failure custody with the preference context even when the
        // settings owner publishes from an I/O thread. Fire-and-forget posting
        // would mark the observer successful before any control was refreshed.
        if (Dispatcher.UIThread.CheckAccess()) RefreshTheme(); else Dispatcher.UIThread.Invoke(RefreshTheme);
    }

    private bool ApplyResolvedTheme(Control control, CuiThemeResolution resolution)
    {
        // Populate off-tree, then publish once. Mutating an attached dictionary
        // broadcasts every individual token to this control and its descendants,
        // repeatedly projecting interaction states against incomplete palettes.
        var explicitAccent = resolution.EffectiveAccent.ResolveColour(_accentAppId ?? string.Empty);
        var palette = CuiSurfacePaletteCatalog.For(_currentSurface,
            _appearance ?? resolution.Appearance ?? CuiThemeScopeApplier.DetectAppearance(), resolution.Theme.BaseTheme,
            explicitAccent, _accentAppId);
        var key = new ThemeResourceKey(palette, resolution.Accessibility, resolution.Theme.Name, resolution.Theme.CustomFingerprint, explicitAccent);
        // Editing a named draft changes the shared catalog. Scopes whose effective
        // palette and custom definition are unchanged retain their resource owners.
        if (_appliedThemeKeys.TryGetValue(control, out var applied) && applied == key) return false;
        if (!_themeResourceSnapshots.TryGetValue(key, out var snapshot))
        {
            var source = new ResourceDictionary();
            CuiThemeResourceApplier.ApplyToResources(source, palette, resolution.Accessibility);
            if (resolution.Theme.CustomTheme is { } custom)
                CuiCustomThemeResourceApplier.ApplyToResources(source,
                    CuiCustomThemeAccentResolver.Resolve(custom, explicitAccent), resolution.Accessibility);
            source["CuiEffectiveThemeName"] = resolution.Theme.Name;
            // Backend resource dictionaries have one owner. Reuse their immutable
            // values, never the attached dictionary or a mutable brush instance.
            var immutable = new Dictionary<Avalonia.Media.IBrush, Avalonia.Media.IBrush>(ReferenceEqualityComparer.Instance);
            snapshot = source.Select(pair =>
            {
                object? value = pair.Value;
                if (value is Avalonia.Media.ISolidColorBrush brush)
                {
                    if (!immutable.TryGetValue(brush, out var shared))
                        immutable.Add(brush, shared = Avalonia.Media.BrushExtensions.ToImmutable(brush));
                    value = shared;
                }
                return new KeyValuePair<object, object?>(pair.Key, value);
            }).ToFrozenDictionary(pair => pair.Key, pair => pair.Value);
            if (_themeResourceSnapshots.Count >= 32) _themeResourceSnapshots.Clear();
            _themeResourceSnapshots.Add(key, snapshot);
        }
        // Each provider has one native owner; only its read-only palette is shared.
        // Mutable gradients are created lazily for this owner when actually used.
        var resources = new CuiThemeResourceProvider(snapshot);
        if (_controlThemeResources.TryGetValue(control, out var previous))
        {
            var index = control.Resources.MergedDictionaries.IndexOf(previous);
            if (index >= 0) control.Resources.MergedDictionaries[index] = resources;
            else control.Resources.MergedDictionaries.Add(resources);
        }
        else control.Resources.MergedDictionaries.Add(resources);
        _controlThemeResources[control] = resources;
        var store = CuiPropertyPresentation.For(control);
        store.RemoveSource(CuiPropertyLevel.Theme, "CUI custom theme");
        if (_authoredControls.TryGetValue(control, out var component))
        {
            var type = component.Properties.FirstOrDefault(pair => pair.Key.Equals("Type", StringComparison.OrdinalIgnoreCase)).Value as CuiLiteralValue;
            foreach (var rule in resolution.Theme.Rules.Where(rule => rule.Key.ElementName.Equals(component.Type, StringComparison.OrdinalIgnoreCase)
                && (rule.Key.TypeName is null || rule.Key.TypeName.Equals(type?.Value, StringComparison.Ordinal))).OrderBy(rule => rule.Key.TypeName is null ? 0 : 1))
                foreach (var (property, value) in rule.Value)
                {
                    if (!_controlRegistry.TryGetProperty(property, out var descriptor)) throw new CuiRuntimeLoadException(new("CUIR056", CuiDiagnosticSeverity.Error, $"Unknown custom theme property '{property}'.", component.Span));
                    if (value is Avalonia.Media.IBrush or Avalonia.Media.IImage)
                    {
                        if (!CuiPropertyPresentation.Set(control, descriptor.Name, value, CuiPropertyLevel.Theme, "CUI custom theme", rule.Key.TypeName is null ? 0 : 1))
                            throw new CuiRuntimeLoadException(new("CUIR056", CuiDiagnosticSeverity.Error, $"Custom theme property '{property}' has no typed projection for {component.Type}.", component.Span));
                        continue;
                    }
                    var literal = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                    if (!CuiLayoutProperties.SetLiteral(control, descriptor.Name, literal, CuiPropertyLevel.Theme, "CUI custom theme", rule.Key.TypeName is null ? 0 : 1)
                        && !CuiPropertyPresentation.TrySetLiteral(control, descriptor.Name, literal, CuiPropertyLevel.Theme, "CUI custom theme", rule.Key.TypeName is null ? 0 : 1))
                        throw new CuiRuntimeLoadException(new("CUIR056", CuiDiagnosticSeverity.Error, $"Custom theme property '{property}' has no typed projection for {component.Type}.", component.Span));
                }
        }
        _appliedThemeKeys[control] = key;
        return true;
    }

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

    /// <summary>Snapshot active dispatches and retained failed native control dispatch pipelines already accepted by this loader.
    /// Call after raising the click. This does not dispatch an action, attest success, or include future clicks.
    /// Captured tasks remain awaitable after disposal, including original dispatcher, observer and cleanup failures.
    /// An action must not await a snapshot containing itself; its owning caller joins outside the dispatch.</summary>
    public Task WhenActionsIdleAsync()
    {
        lock (_actionTasksGate)
        {
            var tasks = _pendingActionTasks.ToList();
            if (_admissionFailureTask is not null) tasks.Add(_admissionFailureTask);
            if (_disposalTask is not null) tasks.Add(_disposalTask);
            return Task.WhenAll(tasks);
        }
    }

    private void ObserveActionDispatch(Control control, Control? contextMenuOwner)
    {
        if (_disposed || !control.IsEnabled || !_wiredActions.Contains(control) ||
            !TryGetActionInvocation(control, out var invocation)) return;
        var isLocal = invocation.Command == LocalToggleCommand;
        // A control inside a QueryViewport's authored subtree dispatches through that viewport (the
        // generic runtime query/state owner); the viewport forwards unrelated commands to the scene
        // host dispatcher. Outside such a scope, the scene/app dispatcher is used directly.
        var dispatcher = !isLocal ? ScopedDispatcherFor(control) ?? _actionDispatcher : _actionDispatcher;
        if (!isLocal && dispatcher is null) return;

        if (control is MenuItem && !control.IsEffectivelyEnabled) return;
        if (contextMenuOwner is not null && (!contextMenuOwner.IsEffectivelyEnabled ||
            !_ownedContextMenus.TryGetValue(contextMenuOwner, out var ownedMenu) ||
            !ReferenceEquals(contextMenuOwner.ContextMenu, ownedMenu))) return;

        // The actual async pipeline waits at this gate. Publish it before releasing any
        // dispatcher/observer callback; the original UI context is retained by the await.
        var start = new TaskCompletionSource();
        lock (_actionTasksGate)
        {
            if (_disposed || _admissionFailureTask is not null) return;
            _pendingActionTasks.RemoveWhere(static task => task.IsCompletedSuccessfully);
            if (_pendingActionTasks.Count >= MaximumRetainedActionTasks)
                _admissionFailureTask = RejectActionDispatchAsync(start.Task, control);
            else
                _pendingActionTasks.Add(isLocal
                    ? DispatchLocalToggleAsync(start.Task, control, invocation)
                    : DispatchActionAsync(start.Task, control, dispatcher!, invocation));
        }
        start.SetResult();
    }

    public void Dispose()
    {
        var start = new TaskCompletionSource();
        var failures = new List<Exception>();
        lock (_actionTasksGate)
        {
            if (_disposed) return;
            _disposed = true;
            var actions = _admissionFailureTask is null ? _pendingActionTasks.ToArray()
                : _pendingActionTasks.Append(_admissionFailureTask).ToArray();
            // Retain the actual terminal task before cancellation or unsubscribe reentry.
            _disposalTask = CompleteDisposalAsync(start.Task, actions, failures);
        }
        void Attempt(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception error) { AddDistinctFailure(failures, error); }
        }
        try
        {
            Attempt(_lifetime.Cancel);
            ActionFailed = null;
            if (_themePreference is not null) Attempt(() => _themePreference.Changed -= ThemePreferenceChanged);
            if (_bindingContext is INotifyPropertyChanged observable && _bindingChangedHandler is not null)
                Attempt(() => observable.PropertyChanged -= _bindingChangedHandler);
            _bindingChangedHandler = null;
            _liveBindings.Clear();
            _localBindings.Clear();
            _localState.Clear();
            _declaredVariables.Clear();
            _liveConditionals.Clear();
            foreach (var repeat in _repeats.ToArray()) Attempt(repeat.Dispose);
            foreach (var scope in _scopeChangedHandlers.Keys.ToArray()) Attempt(() => UnsubscribeScope(scope));
            _repeats.Clear();
            foreach (var (control, handler) in _actionHandlers.ToArray()) Attempt(() => UnwireAction(control, handler));
            foreach (var owner in _ownedContextMenus.Keys.ToArray()) Attempt(() => CloseOwnedContextMenu(owner));
            foreach (var control in _authoredControls.Keys.ToArray()) Attempt(() => DetachPresentation(control));
            _actionHandlers.Clear();
            _wiredActions.Clear();
            _actionInvocations.Clear();
            _currentRepeatIdentity = null;
        }
        catch (Exception error) { AddDistinctFailure(failures, error); }
        finally { start.SetResult(); }
        if (CombineFailures(failures) is { } failure)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private async Task CompleteDisposalAsync(Task start, Task[] actions, List<Exception> cleanupFailures)
    {
        await start;
        // Copy only after all synchronous cleanup attempts have settled and opened the gate.
        var failures = new List<Exception>(cleanupFailures);
        foreach (var action in actions.Concat(_contextMenuCloses.Values).Distinct<Task>(ReferenceEqualityComparer.Instance))
        {
            try { await action; }
            catch (Exception error) { AddOriginalTaskFailure(failures, action, error); }
        }
        try { _lifetime.Dispose(); }
        catch (Exception error) { AddDistinctFailure(failures, error); }
        ThrowRetainedFailures(failures);
    }

    private static void AddOriginalTaskFailure(List<Exception> failures, Task? original, Exception caught)
    {
        // Read the actual Task payload once. Do not Flatten: an opaque/empty aggregate
        // can itself be the original cause, and every direct sibling must survive await.
        if (original?.Exception is { InnerExceptions.Count: > 0 } group)
            foreach (var cause in group.InnerExceptions) AddDistinctFailure(failures, cause);
        else AddDistinctFailure(failures, caught);
    }

    private static void AddDistinctFailure(List<Exception> failures, Exception error)
    {
        if (!failures.Any(previous => ReferenceEquals(previous, error))) failures.Add(error);
    }

    private static Exception? CombineFailures(List<Exception> failures) => failures.Count switch
    {
        0 => null,
        1 => failures[0],
        _ => new AggregateException("Original CUI action or cleanup failures were retained.", failures)
    };

    private static void ThrowRetainedFailures(List<Exception> failures)
    {
        if (CombineFailures(failures) is not { } failure) return;
        // Async methods otherwise convert a thrown OCE into a canceled Task with no
        // Exception payload. Keep that exact unknown cause as a fault, never a waiver.
        if (failure is OperationCanceledException)
            throw new AggregateException("The original CUI cancellation cause was retained.", failure);
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>Set the surface name for theme resolution (default: "Home").</summary>
    public void SetSurface(string surface) => SetSurface(surface, CuiAppAccentCatalog.Find(surface)?.AppId);
    public void SetSurface(string surface, string? appId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surface);
        if (appId is not null) _ = CuiAccentPreference.Default.WithAppColour(appId, null);
        _currentSurface = surface;
        _accentAppId = appId;
    }
    public void SetAppearance(CuiAppearance appearance)
    {
        if (!Enum.IsDefined(appearance)) throw new ArgumentOutOfRangeException(nameof(appearance));
        _appearance = appearance;
    }

    /// <summary>The currently active theme at the current point in the tree.</summary>
    public CuiTheme CurrentTheme => _themeStack.Current;

    /// <summary>Nonallocating authored-control membership, without creating a diagnostic snapshot or querying action policy.</summary>
    public bool IsAuthoredControl(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return !_disposed && _authoredControls.ContainsKey(control);
    }

    /// <summary>Whether the loaded scene's root component declared ownership of its own backdrop.</summary>
    public bool RootOwnsBackdrop => _rootOwnsBackdrop;

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

    /// <summary>Bind a prepared control to one authored component identity without replacing a reserved built-in type.</summary>
    public void BindAuthoredControl(string authoredId, Func<CuiComponent, Control> factory) =>
        _controlRegistry.BindAuthoredControl(authoredId, factory);

    /// <summary>
    /// Loads a CuiDocument and returns the root Avalonia control tree.
    /// </summary>
    public Control? Load(CuiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var generation = ++_loadGeneration;
        if (!CuiProjectItems.IsCuiSourceFamily(document.SourceName))
            throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR051", CuiDiagnosticSeverity.Error, "Production CUI loading rejects non-CUI/AXAML/HUI application input.", document.Span));
        document = CuiThemeCreatorComponents.Compose(CuiAppComponents.Compose(document));
        // Validate executable declarations before retiring the previous control lineage.
        // Unsupported event semantics must not turn into ignored or visual components.
        if (CuiActionEventContract.Validate(document).FirstOrDefault() is { } eventError)
            throw new CuiRuntimeLoadException(eventError);
        // Validate the effective Definition/Prefab/template composition as well. Raw
        // declarations can look harmless until inherited Action and direct caller
        // routes are merged, so reject conflicts before any prior lineage is retired.
        if (ValidateEffectiveActionEventComposition(document) is { } effectiveEventError)
            throw new CuiRuntimeLoadException(effectiveEventError);
        // Preflight source-driven choose-once (Selection="RandomOnce") variables BEFORE retiring
        // any prior lineage: strictly validate every declaration shape and snapshot its typed
        // candidate pool, then select once. A failure leaves the previous valid tree/input/draft/
        // actions operational; nothing has been retired yet. External callbacks (source getter,
        // enumerator, chooser) may synchronously reload or dispose this loader; the load-attempt
        // generation/dispose guard rejects any stale outer attempt rather than overwriting a
        // replacement mount or resuming after disposal.
        var pendingDeclared = new Dictionary<string, CuiVariableDeclaration>(StringComparer.Ordinal);
        var pendingLocal = new Dictionary<string, object?>(StringComparer.Ordinal);
        var randomOncePlans = new List<RandomOncePlan>();
        // PASS 1: validate every declaration shape and snapshot every source-driven candidate pool
        // BEFORE any chooser selection. This rejects malformed new-initializer shapes (including a
        // Source presented without a Selection) before a single draw or any lineage retirement, and
        // guarantees a later invalid declaration is discovered before an earlier valid one draws.
        foreach (var declaration in document.Variables.Values)
        {
            pendingDeclared[declaration.Name] = declaration;
            if (!string.IsNullOrEmpty(declaration.Selection))
            {
                randomOncePlans.Add(new RandomOncePlan(
                    declaration, SnapshotRandomOnceCandidates(declaration, generation), generation));
            }
            else if (string.Equals(declaration.Type, "bool", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(declaration.Source)
                && bool.TryParse(declaration.Value, out var parsed))
            {
                pendingLocal[declaration.Name] = parsed;
            }
            else
            {
                // Malformed local initializer: a string Source without Selection, a non-bool type
                // outside RandomOnce, or a bool without a literal true/false. Reject loudly before
                // any prior lineage is retired or any chooser draws.
                throw new CuiRuntimeLoadException(new CuiDiagnostic(
                    "CUIR077", CuiDiagnosticSeverity.Error,
                    $"Local variable '{declaration.Name}' has an invalid initializer shape; expected a bool literal or a string RandomOnce source selection.", declaration.Span));
            }
        }
        // PASS 2: only after every declaration/pool has validated, pick once per frame. Reject a
        // stale outer attempt BEFORE starting any selection and again immediately before retirement,
        // so a reentrant reload/dispose cannot be overwritten by the outer continuation.
        if (randomOncePlans.Count > 0 && !IsLoadAttemptCurrent(generation))
            throw ReentrancyError(randomOncePlans[0].Declaration);
        foreach (var plan in randomOncePlans)
            pendingLocal[plan.Declaration.Name] = SelectRandomOnce(plan.Declaration, plan.Candidates, plan.Generation);

        if (randomOncePlans.Count > 0 && !IsLoadAttemptCurrent(generation))
            throw ReentrancyError(randomOncePlans[randomOncePlans.Count - 1].Declaration);

        // Close attached native menus before replacing their authored control lineage.
        foreach (var owner in _ownedContextMenus.Keys.ToArray()) CloseOwnedContextMenu(owner);
        // Reusing a loader must not leak definitions or live controls from a prior document.
        _resourceScope.Clear();
        _liveBindings.Clear();
        _liveConditionals.Clear();
        ClearRepeatSubscriptions();
        _repeats.Clear();
        foreach (var control in _authoredControls.Keys.ToArray()) DetachPresentation(control);
        _controlThemeNames.Clear(); _controlThemeResources.Clear(); _appliedThemeKeys.Clear(); _themeNames.Clear(); _themeNames.Push("user_preference");
        _authoredControls.Clear();
        _observedBindings.Clear();
        ClearActionHandlers();
        _rootOwnsBackdrop = false;
        _actionInvocations.Clear();
        _documentActions = document.Actions;
        // Commit the validated, pre-selected per-instance local-state frame.
        _declaredVariables.Clear();
        _localState.Clear();
        _localBindings.Clear();
        foreach (var (name, declaration) in pendingDeclared) _declaredVariables[name] = declaration;
        foreach (var (name, value) in pendingLocal) _localState[name] = value;
        _runtimeDiagnostics.Clear();
        _themeStack = new CuiThemeScopeStack(_themePreference?.Current.Theme.BaseTheme ?? CuiSurfacePaletteCatalog.ActiveTheme);
        _currentLayer = 0;
        _currentRepeatIdentity = null;
        _documentTemplates = document.Templates;
        _definitions.Clear();
        _definitionPath.Clear();
        _definitionScope = null;
        _typeDefinitions.Clear(); _localThemes = new();
        foreach (var definition in document.Components.SelectMany(DefinitionsOutsideTheme))
        {
            if (CuiLanguageVocabulary.IsForbiddenElement(definition.Type))
                throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR054", CuiDiagnosticSeverity.Error, "A Definition cannot re-export a forbidden donor element.", definition.Span));
            if (definition.Type.Equals("Theme", StringComparison.OrdinalIgnoreCase))
            {
                var inherits = definition.Properties.FirstOrDefault(pair => pair.Key.Equals("Inherits", StringComparison.OrdinalIgnoreCase)).Value as CuiLiteralValue;
                var rules = definition.Children.Where(child => child.IsDefinition).Select(child => new CuiThemeElementRule(child.Type,
                    (child.Properties.FirstOrDefault(pair => pair.Key.Equals("Type", StringComparison.OrdinalIgnoreCase)).Value as CuiLiteralValue)?.Value,
                    child.Properties.Where(pair => !pair.Key.Equals("Type", StringComparison.OrdinalIgnoreCase)).ToDictionary(pair => pair.Key,
                        pair => pair.Value is CuiLiteralValue literal ? (object?)literal.Value : throw new CuiRuntimeLoadException(new("CUIR056", CuiDiagnosticSeverity.Error, "Theme defaults must have typed literal values.", pair.Value.Span)), StringComparer.OrdinalIgnoreCase))).ToArray();
                _localThemes.Add(new CuiThemeDefinition(definition.Name ?? throw new CuiRuntimeLoadException(new("CUIR056", CuiDiagnosticSeverity.Error, "Theme Definition requires Name.", definition.Span)), inherits?.Value ?? "Professional", rules));
            }
            else if (CuiLanguageVocabulary.VisualElements.Contains(definition.Type))
            {
                if (definition.Name is not null) _definitions[definition.Name] = definition;
                var definedType = definition.Properties.FirstOrDefault(pair => pair.Key.Equals("Type", StringComparison.OrdinalIgnoreCase)).Value as CuiLiteralValue;
                if (definedType is not null && definition.Name is null) _typeDefinitions[definition.Type + "\0" + definedType.Value] = definition;
            }
            else if (!definition.Type.Equals("Theme", StringComparison.OrdinalIgnoreCase)) _definitions[definition.Name ?? definition.Type] = definition;
        }

        // Populate resource scope
        foreach (var (key, def) in document.Resources)
        {
            if (def.Value is CuiLiteralValue literal)
                _resourceScope[key] = literal.Value;
        }

        var visibleRoot = document.Components.FirstOrDefault(component => !component.IsDefinition);
        if (visibleRoot is null)
            return null;

        // The first component is the root
        var root = LoadComponent(visibleRoot);

        // Record whether the authored root owns its own composite backdrop (so a
        // rounded authored surface, not a square host fill, owns the scene).
        _rootOwnsBackdrop = _authoredControls.TryGetValue(root, out var rootComponent)
            && TryGetDeclaredBoolean(rootComponent, "ownsbackdrop");

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

    private async Task DispatchActionAsync(Task start, Control control, ICuiActionDispatcher dispatcher,
        CuiActionInvocation invocation)
    {
        await start;
        Task? actualDispatcher = null;
        try
        {
            // Resolve live bound parameters only after this actual pipeline has
            // been retained, using the SAME configured context captured at load.
            // App row owners still validate current selection and provenance.
            var parameter = invocation.ResolveCurrentParameter();
            // Consume the returned ValueTask once and retain its actual Task before await.
            actualDispatcher = dispatcher is ICuiLifetimeAwareActionDispatcher aware
                ? aware.DispatchWithLifetimeAsync(invocation.Command, parameter,
                    new CuiActionDispatchLifetime(_lifetime.Token, CancellationToken.None)).AsTask()
                : dispatcher.DispatchAsync(invocation.Command, parameter, _lifetime.Token).AsTask();
            await actualDispatcher;
        }
        catch (Exception error)
        {
            var failures = new List<Exception>();
            AddOriginalTaskFailure(failures, actualDispatcher, error);
            RetainActionFailure(control, error, failures);
            ThrowRetainedFailures(failures);
        }
    }

    /// <summary>Executes a pure-local Toggle in the loader's per-instance local-state frame without
    /// any external dispatcher, then refreshes bindings that observe the changed variable. It joins
    /// the same action admission/lifetime/failure pipeline as external dispatches.</summary>
    private async Task DispatchLocalToggleAsync(Task start, Control control, CuiActionInvocation invocation)
    {
        await start;
        var target = invocation.Parameter as string;
        try
        {
            if (target is null || !_declaredVariables.ContainsKey(target))
                throw new InvalidOperationException($"Local toggle target '{target}' is not a declared document-root variable.");
            var current = _localState.TryGetValue(target, out var value) && value is true;
            _localState[target] = !current;
            RefreshLocalVariable(target);
        }
        catch (Exception error)
        {
            var failures = new List<Exception>();
            AddDistinctFailure(failures, error);
            RetainActionFailure(control, error, failures);
            ThrowRetainedFailures(failures);
        }
    }

    private async Task RejectActionDispatchAsync(Task start, Control control)
    {
        await start;
        var original = new InvalidOperationException("The loader retained-action limit was reached; further effects require owner drainage and a new loader.");
        var failures = new List<Exception> { original };
        RetainActionFailure(control, original, failures);
        ThrowRetainedFailures(failures);
    }

    private void RetainActionFailure(Control control, Exception original, List<Exception> failures)
    {
        if (!_disposed)
        {
            var failure = original is OperationCanceledException
                ? new CuiActionFailure("CUIA_CANCELLED", "The action was cancelled.", true)
                : new CuiActionFailure("CUIA_FAILED", "The action could not complete. Check the current state before trying again.", false);
            try
            {
                if (ReportActionFailure(control, failure) is { } observerFailure)
                    AddDistinctFailure(failures, observerFailure);
            }
            catch (Exception error) { AddDistinctFailure(failures, error); }
        }
    }

    private Exception? ReportActionFailure(Control control, CuiActionFailure failure)
    {
        var span = _authoredControls.TryGetValue(control, out var authored) ? authored.Span : default;
        _runtimeDiagnostics.Add(new(failure.Code, failure.Cancelled ? CuiDiagnosticSeverity.Info : CuiDiagnosticSeverity.Error, failure.Message, span));
        try { ActionFailed?.Invoke(this, failure); }
        catch (Exception error)
        {
            _runtimeDiagnostics.Add(new("CUIA_OBSERVER_FAILED", CuiDiagnosticSeverity.Error, "The action status could not be displayed.", span));
            return error;
        }
        return null;
    }

    private void WireBindingsRecursive(Control control, Control? contextMenuOwner = null)
    {
        // Native menu items use the same retained pipeline as native buttons. Pure-local toggles
        // are wired even without an external dispatcher; external commands require one.
        if (control is Button or MenuItem && !_wiredActions.Contains(control)
            && TryGetActionInvocation(control, out var actionInvocation)
            && (_actionDispatcher is not null || actionInvocation.Command == LocalToggleCommand))
        {
            EventHandler<RoutedEventArgs> handler = (_, args) =>
            {
                // A submenu click bubbles through its parent; dispatch only the clicked item.
                var originOnly = control is MenuItem || TryGetActionInvocation(control, out var current) && current.OriginOnly;
                if (!originOnly || ReferenceEquals(args.Source, control)) ObserveActionDispatch(control, contextMenuOwner);
            };
            if (control is Button button) button.Click += handler;
            else if (control is MenuItem item) item.Click += handler;
            _actionHandlers[control] = handler;
            _wiredActions.Add(control);
        }
        // ContextMenu is outside the visual tree while closed. Recurse only the
        // exact menu attached by this loader to the actual owning control.
        if (_ownedContextMenus.TryGetValue(control, out var menu) && ReferenceEquals(control.ContextMenu, menu))
            WireBindingsRecursive(menu, control);

        // Recurse into visual children
        if (control is CuiContainerControl container)
        {
            // Inactive view cells are detached from native layout, not retired.
            foreach (var child in container.AuthoredChildren) WireBindingsRecursive(child, contextMenuOwner);
        }
        else if (control is Panel panel)
        {
            foreach (var child in panel.Children)
                if (child is Control ctrl) WireBindingsRecursive(ctrl, contextMenuOwner);
        }
        else if (control is Decorator decorator && decorator.Child is Control decChild)
        {
            WireBindingsRecursive(decChild, contextMenuOwner);
        }
        else if (control is ContentControl cc && cc.Content is Control ccChild)
        {
            WireBindingsRecursive(ccChild, contextMenuOwner);
        }
        else if (control is ItemsControl ic)
        {
            foreach (var item in ic.Items)
                if (item is Control icChild) WireBindingsRecursive(icChild, contextMenuOwner);
        }
    }

    private void ClearActionHandlers()
    {
        foreach (var (control, handler) in _actionHandlers) UnwireAction(control, handler);
        _actionHandlers.Clear();
        _wiredActions.Clear();
    }

    private static void UnwireAction(Control control, EventHandler<RoutedEventArgs> handler)
    {
        if (control is Button button) button.Click -= handler;
        else if (control is MenuItem item) item.Click -= handler;
    }

    private void CloseOwnedContextMenu(Control owner)
    {
        if (!_ownedContextMenus.TryGetValue(owner, out var menu)) return;
        if (_contextMenuCloses.TryGetValue(menu, out var originalClose))
        {
            if (!originalClose.IsCompleted)
                throw new InvalidOperationException("A native context menu close cannot join its own active callback.");
            if (originalClose.Exception is { InnerExceptions.Count: > 0 } failed)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failed.InnerExceptions[0]).Throw();
            return;
        }
        // Avalonia Close is synchronous. Publish its once-only receipt before
        // entering native Closing callbacks; a failed close is never replayed.
        var close = new TaskCompletionSource();
        _contextMenuCloses.Add(menu, close.Task);
        lock (_actionTasksGate) _pendingActionTasks.Add(close.Task);
        try
        {
            menu.Close();
            if (menu.IsOpen)
                throw new InvalidOperationException("The owned native context menu did not close; its owner remains retained.");
            if (ReferenceEquals(owner.ContextMenu, menu)) owner.ContextMenu = null;
            if (_contextMenuFocusHandlers.Remove(menu, out var focusHandlers))
            {
                menu.PropertyChanged -= focusHandlers.Opening;
                menu.Closed -= focusHandlers.Closed;
            }
            _ownedContextMenus.Remove(owner);
            close.SetResult();
            _contextMenuCloses.Remove(menu);
        }
        catch (Exception error)
        {
            close.SetException(error);
            throw;
        }
    }

    private Control LoadComponent(CuiComponent component, string? identityOverride = null, bool definitionInstance = false, Control? parent = null)
    {
        if (component.Type.Equals("Repeat", StringComparison.OrdinalIgnoreCase))
            return LoadRepeat(component, parent);
        if (component.Type.Equals("If", StringComparison.OrdinalIgnoreCase))
            return LoadConditional(component);

        // === DefaultTheme is a scope marker, not a visual control ===
        if (component.IsThemeScope && component.DefaultTheme is not null)
        {
            var resolution = ResolveTheme(component.DefaultTheme);
            var resolvedTheme = resolution.Theme.BaseTheme;
            _themeStack.Push(resolvedTheme); _themeNames.Push(component.DefaultTheme);

            try
            {
                // Load the first child that produces a visual control
                Control? result = null;
                foreach (var child in component.Children.Where(child => !child.IsDefinition))
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
                    _controlThemeNames[result] = component.DefaultTheme;
                    ApplyResolvedTheme(result, resolution);
                }

                return result ?? new Panel();
            }
            finally
            {
                _themeStack.Pop();
                _themeNames.Pop();
            }
        }

        if (!component.IsDefinition && TryResolveDefinition(component.Type, out var definition))
            return InstantiateDefinition(component, definition);
        if (!component.IsDefinition && _documentTemplates.TryGetValue(component.Type, out var template))
            return InstantiateTemplate(component, template);

        var isRoot = _loadingDepth == 0;
        _loadingDepth++;
        _isCurrentRootControl = isRoot;
        try
        {
            var control = CreateControl(component, definitionInstance);
            CuiPropertyPresentation.Attach(control, ownsTemplate: component.Type.ToLowerInvariant() is not ("object" or "audio" or "video"));
            CuiLifecycleProperties.Attach(control);
            var parentLayer = _currentLayer;
            var layer = ResolveLayer(component, parentLayer);
            control.ZIndex = layer;
            _authoredControls.Add(control, component);
            CuiRuntimeIdentity.SetStableId(control, identityOverride ?? ComposeStableId(component.StableId));
            if (!string.IsNullOrWhiteSpace(component.Name))
            {
                control.Name = component.Name;
                Avalonia.Automation.AutomationProperties.SetAutomationId(control, component.Name);
            }
            // Typed resource references need the effective scope before authored values.
            ApplyThemeToControlIfContainer(control);
            ApplyProperties(control, component);
            ApplyAuthoredText(control, component);
            ApplyActionTag(control, component);
            ApplyClasses(control, component);

            _currentLayer = layer;
            try
            {
                // A QueryViewport is the generic runtime state/query scope for a bounded paged
                // picker. While loading its authored subtree, the viewport becomes the active binding
                // context and action dispatcher: child bindings (Items/Query/PageLabel/HasNext) and
                // pager actions resolve against the viewport, which owns the query string + opaque
                // cursor and delegates all data filtering/pagination to its typed ICuiPagedSource.
                // The viewport defers nonlocal lookups and non-pager commands to the scene/app host,
                // so the parent context/dispatcher are captured and restored around the subtree.
                ICuiBindingContext? viewportPreviousContext = null;
                ICuiActionDispatcher? viewportPreviousDispatcher = null;
                var isQueryViewport = control is CuiQueryViewport viewport;
                if (isQueryViewport)
                {
                    viewport.InnerContext = _bindingContext;
                    viewport.InnerDispatcher = _actionDispatcher;
                    viewport.RefreshHost = () => OnBindingPropertyChanged(null);
                    if (viewport.Source is null
                        && _bindingContext?.TryGetValue("Composer.EmojiCatalogueSource", out var source) == true
                        && source is ICuiPagedSource pagedSource)
                        viewport.Source = pagedSource;
                    viewportPreviousContext = _bindingContext;
                    viewportPreviousDispatcher = _actionDispatcher;
                    _bindingContext = viewport;
                    _actionDispatcher = viewport;
                }
                try
                {
                    foreach (var child in component.Children.Where(child => !child.IsDefinition && !CuiActionEventContract.IsActionEvent(child)))
                    {
                        var childControl = LoadComponent(child, parent: control);
                        if (childControl is not null) AddChild(control, childControl);
                    }
                }
                finally
                {
                    if (isQueryViewport)
                    {
                        _bindingContext = viewportPreviousContext;
                        _actionDispatcher = viewportPreviousDispatcher;
                    }
                }
            }
            finally
            {
                _currentLayer = parentLayer;
            }

            return WrapScrollable(control, component);
        }
        finally
        {
            _loadingDepth--;
            _isCurrentRootControl = false;
        }
    }

    private static IEnumerable<CuiComponent> DefinitionsOutsideTheme(CuiComponent component)
    {
        if (component.IsDefinition) yield return component;
        if (component.Type.Equals("Theme", StringComparison.OrdinalIgnoreCase) && component.IsDefinition) yield break;
        foreach (var child in component.Children.Concat(component.ElseChildren))
            foreach (var definition in DefinitionsOutsideTheme(child)) yield return definition;
    }

    private Control WrapScrollable(Control control, CuiComponent component)
    {
        if (control is CuiContainerControl) return control; // Canonical Container keeps scrolling inside its identity/decorated bounds.
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

    private Control CreateControl(CuiComponent component, bool allowContainerFallback = false)
    {
        var creation = component;
        var declaredType = component.Properties.FirstOrDefault(pair => pair.Key.Equals("Type", StringComparison.OrdinalIgnoreCase)).Value as CuiLiteralValue;
        if (declaredType is not null && component.Type == "Text" && _typeDefinitions.ContainsKey(component.Type + "\0" + declaredType.Value))
            creation = new(component.Type, component.Name, component.Classes,
                component.Properties.Where(pair => !pair.Key.Equals("Type", StringComparison.OrdinalIgnoreCase)).ToDictionary(pair => pair.Key, pair => pair.Value),
                component.Actions, component.Children, component.Condition, component.List, component.Span, groups: component.Groups);
        if (_controlRegistry.TryCreate(creation, ResolveValue, out var control, out var error))
            return control!;

        // A definition authored as a component tree has no built-in element contract to extend,
        // so its instance lowers as a container.
        if (allowContainerFallback && !CuiLanguageVocabulary.IsForbiddenElement(component.Type))
            return new CuiContainerControl("Overlay");

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
        var conditionValue = EvaluateCondition(component.Condition);
        var host = new ContentControl
        {
            Name = component.Name,
            Content = BuildConditionalBranch(component, conditionValue, layer)
        };
        host.ZIndex = layer;
        CuiRuntimeIdentity.SetStableId(host, ComposeStableId(component.StableId));
        if (component.Condition.IsLive && component.Condition.Test is CuiBindingValue binding)
            _liveConditionals.Add(new CuiLiveConditional(host, component, binding.Path, layer, _bindingContext!, _themeNames.Peek()) { LastValue = conditionValue });
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
            foreach (var child in children.Where(child => !child.IsDefinition))
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

    private Control LoadRepeat(CuiComponent component, Control? parent)
    {
        if (component.Repeat is null)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR050", CuiDiagnosticSeverity.Error,
                "Repeat elements require a parsed source, item name, and stable key.", component.Span));

        var layer = ResolveLayer(component, _currentLayer);
        var panelValue = component.Properties.FirstOrDefault(pair => pair.Key.Equals("Panel", StringComparison.OrdinalIgnoreCase)).Value;
        var explicitPanel = panelValue is not null;

        // Canonical container-range mode: Repeat is a keyed generated CHILD RANGE of the actual
        // owning Container, so each item's visual root participates directly in that Container's
        // declared Wrap/Horizontal Stack/Vertical Stack/Grid layout. No hidden vertical sub-panel
        // is invented, and the repeat itself carries no layout/visual/input policy. The explicit
        // legacy `Panel=` extension remains as a versioned compatibility escape for existing
        // consumers; it is not the canonical repair and is never implied automatically.
        if (!explicitPanel && parent is CuiContainerControl container)
        {
            var cellHost = new ContainerCellRepeatHost(container, container.AuthoredChildCount);
            var cellState = new CuiRepeatState(cellHost, component, layer,
                _bindingContext, _currentRepeatIdentity, _themeNames.Peek(), container);
            _repeats.Add(cellState);
            ReconcileRepeat(cellState);
            return new CuiRepeatFragment { State = cellState };
        }

        var panelName = explicitPanel
            ? panelValue is CuiLiteralValue literal ? literal.Value :
                throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR050", CuiDiagnosticSeverity.Error, "Repeat Panel must be a structural literal.", component.Span))
            : "Vertical Stack";
        Panel host = panelName.ToLowerInvariant() switch
        {
            "vertical stack" => new StackPanel(),
            "horizontal stack" => new StackPanel { Orientation = Orientation.Horizontal },
            "wrap" => new WrapPanel(),
            "grid" => new Grid(),
            _ => throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR050", CuiDiagnosticSeverity.Error, "Repeat Panel supports Vertical Stack, Horizontal Stack, Wrap or Grid.", component.Span))
        };
        host.Name = component.Name; host.ZIndex = layer;
        CuiRuntimeIdentity.SetStableId(host, ComposeStableId(component.StableId));
        _authoredControls[host] = component;
        CuiPropertyPresentation.Attach(host); CuiLifecycleProperties.Attach(host); ApplyThemeToControlIfContainer(host);
        ApplyProperties(host, component);
        var state = new CuiRepeatState(new LegacyPanelRepeatHost(host), component, layer,
            _bindingContext, _currentRepeatIdentity, _themeNames.Peek(), host);
        _repeats.Add(state);
        ReconcileRepeat(state);
        return host;
    }

    private static string? RepeatItemPlacement(string property) => property.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant() switch
    {
        "itemrow" => "Row", "itemcolumn" => "Column",
        "itemrowspan" => "RowSpan", "itemcolumnspan" => "ColumnSpan", _ => null
    };

    private void ReconcileRepeat(CuiRepeatState state)
    {
        var previousContext = _bindingContext; var previousIdentity = _currentRepeatIdentity; var previousLayer = _currentLayer;
        _bindingContext = state.Context; _currentRepeatIdentity = state.IdentityPrefix; _currentLayer = state.Layer;
        _themeStack.Push(ResolveTheme(state.ThemeName).Theme.BaseTheme); _themeNames.Push(state.ThemeName);
        try { ReconcileRepeatCore(state); }
        finally { _themeStack.Pop(); _themeNames.Pop(); _bindingContext = previousContext; _currentRepeatIdentity = previousIdentity; _currentLayer = previousLayer; }
    }

    private void ReconcileRepeatCore(CuiRepeatState state)
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
        var nextItems = new List<(string Key, object? Item, string? Template)>();
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
            nextItems.Add((key, item, ResolveRepeatTemplate(definition.Template, itemScope, key)));
        }

        ReplaceRepeatItems(state, nextItems);
        state.SetSource(source, () => RequestRepeatRefresh(state));
    }

    /// <summary>
    /// Resolves the per-item template name for a Repeat.
    ///
    /// This is what lets one list mix shapes. HUI's DynamicUI chose a template per item
    /// (<c>CreateItem(template, location, id, values)</c>) and the Chat transcript relies on it,
    /// because messages and tool-activity cards are structurally different. CUI previously had no
    /// equivalent: a Repeat instantiated every child for every item, so a mixed list could not
    /// be expressed. A Repeat that declares a Template resolves it against each item and
    /// instantiates only the matching Definition child.
    /// </summary>
    private string? ResolveRepeatTemplate(CuiValue? template, RepeatItemScope scope, string key)
    {
        if (template is null)
        {
            return null;
        }

        var resolved = template switch
        {
            CuiLiteralValue literal => literal.Value,
            CuiBindingValue binding when scope.TryGetValue(binding.Path, out var value) => value,
            _ => null
        };
        var name = resolved is null
            ? null
            : Convert.ToString(resolved, System.Globalization.CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(name))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR053", CuiDiagnosticSeverity.Error,
                $"Repeat item '{key}' must resolve its Template to a non-empty template name.", template.Span));
        return name;
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

    private void ReplaceRepeatItems(CuiRepeatState state, IReadOnlyList<(string Key, object? Item, string? Template)> nextItems)
    {
        var nextInstances = new Dictionary<string, RepeatItemInstance>(StringComparer.Ordinal);
        var nextRoots = new List<Control>(nextItems.Count);
        // Instances whose template changed keep their key, so the removal pass below would skip
        // them and leave the superseded scope subscribed and its control tree attached. They are
        // retired here instead.
        var replaced = new List<RepeatItemInstance>();
        foreach (var (key, item, template) in nextItems)
        {
            // Reuse only when the shape still matches. A per-item template can change under a
            // stable key (a message that becomes an assistant reply), and reusing the old control
            // would leave the wrong subtree on screen; the key alone is not sufficient identity.
            if (state.Items.TryGetValue(key, out var existing)
                && string.Equals(existing.Template, template, StringComparison.Ordinal))
            {
                existing.Scope.Update(item);
                // Update publishes the new item through the registered scope
                // observer. A second pass re-reads every field and reprojects
                // the same native inputs during each character edit.
                nextInstances.Add(key, existing);
                nextRoots.Add(existing.Root);
                continue;
            }

            if (state.Items.TryGetValue(key, out var superseded))
            {
                replaced.Add(superseded);
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
                foreach (var (propertyName, value) in state.Component.Properties)
                {
                    var placement = RepeatItemPlacement(propertyName);
                    if (placement is null) continue;
                    if (!state.Host.IsGrid) throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR050", CuiDiagnosticSeverity.Error, "Repeat item placement requires Panel Grid.", state.Component.Span));
                    if (value is CuiBindingValue binding) ApplyLiveBinding(itemRoot, placement, binding, state.Component.Span);
                    else if (ResolveValue(value) is { } resolved) ApplyLiteralProperty(itemRoot, placement, resolved, state.Component.Span);
                }
                foreach (var child in state.Component.Children.Where(child => !child.IsDefinition))
                    itemRoot.Children.Add(LoadComponent(child));
                if (template is not null)
                {
                    // Instantiate only the selected Definition child. An unmatched name is an
                    // error rather than an empty row: silently rendering nothing would hide a
                    // template typo behind an apparently working list.
                    var selected = state.Component.Children.FirstOrDefault(child =>
                        child.IsDefinition
                        && string.Equals(child.Name ?? child.StableId, template, StringComparison.Ordinal));
                    if (selected is null)
                        throw new CuiRuntimeLoadException(new CuiDiagnostic(
                            "CUIR054", CuiDiagnosticSeverity.Error,
                            $"Repeat item '{key}' selected template '{template}', which is not a Definition child of this Repeat.",
                            state.Component.Span));
                    itemRoot.Children.Add(LoadComponent(selected));
                }
            }
            finally
            {
                _bindingContext = previousContext;
                _currentRepeatIdentity = previousIdentity;
            }

            var instance = new RepeatItemInstance(scope, itemRoot, template);
            SubscribeScope(scope);
            WireBindingsRecursive(itemRoot);
            nextInstances.Add(key, instance);
            nextRoots.Add(itemRoot);
        }

        foreach (var (key, removed) in state.Items)
        {
            if (nextInstances.ContainsKey(key))
                continue;
            UnsubscribeScope(removed.Scope);
            DetachControlTree(removed.Root);
            state.Host.Remove(removed.Root);
        }

        foreach (var retired in replaced)
        {
            UnsubscribeScope(retired.Scope);
            DetachControlTree(retired.Root);
            // The key survives into nextInstances, so the removal pass above deliberately skips
            // it. Without this the superseded row stays parented and renders alongside its
            // replacement.
            state.Host.Remove(retired.Root);
        }

        state.Items.Clear();
        foreach (var entry in nextInstances)
            state.Items.Add(entry.Key, entry.Value);
        // Keyed identity includes the native attachment, not only the retained
        // control instance. Clearing and re-adding unchanged rows drops input
        // focus on every writeback and interrupts caret/selection/composition.
        for (var index = 0; index < nextRoots.Count; index++)
        {
            var current = state.Host.IndexOf(nextRoots[index]);
            if (current < 0) state.Host.Insert(index, nextRoots[index]);
            else if (current != index) state.Host.Move(current, index);
        }
    }

    private void DetachControlTree(Control root)
    {
        var removed = EnumerateControls(root).ToHashSet<Control>(ReferenceEqualityComparer.Instance);
        foreach (var repeat in _repeats.Where(repeat => repeat.HostControl is not null && removed.Contains(repeat.HostControl)).ToArray())
        {
            foreach (var item in repeat.Items.Values) UnsubscribeScope(item.Scope);
            repeat.Dispose();
            _repeats.Remove(repeat);
        }
        _liveBindings.RemoveAll(binding => removed.Contains(binding.Control));
        _liveConditionals.RemoveAll(conditional => removed.Contains(conditional.Host));
        foreach (var control in removed)
        {
            DetachPresentation(control);
            _controlThemeNames.Remove(control); _controlThemeResources.Remove(control); _appliedThemeKeys.Remove(control);
            _authoredControls.Remove(control);
            _observedBindings.Remove(control);
            _actionInvocations.Remove(control);
            _wiredActions.Remove(control);
            if (_actionHandlers.Remove(control, out var handler)) UnwireAction(control, handler);
            CloseOwnedContextMenu(control);
        }
    }

    private static void DetachPresentation(Control control)
    {
        CuiLayoutProperties.Detach(control);
        CuiLifecycleProperties.Detach(control);
        CuiPropertyPresentation.Detach(control);
        if (control is CuiContainerControl container) container.Dispose();
    }

    private string CreateRepeatIdentityPrefix(CuiComponent component, string key)
    {
        var segment = $"{component.StableId}|key:{key.Length}:{key}";
        var scoped = _definitionScope is null ? segment : $"{_definitionScope}/{segment}";
        return _currentRepeatIdentity is null ? scoped : $"{_currentRepeatIdentity}/{scoped}";
    }

    private string ComposeStableId(string stableId)
    {
        // The repeat prefix already carries the definition scope, so it is applied exactly once.
        if (_currentRepeatIdentity is not null)
            return $"{_currentRepeatIdentity}/{stableId}";
        return _definitionScope is null ? stableId : $"{_definitionScope}/{stableId}";
    }

    /// <summary>A user definition never silently shadows a built-in element name.</summary>
    private bool TryResolveDefinition(string name, out CuiComponent definition)
    {
        definition = null!;
        if (_controlRegistry.TryResolveElement(name, out _))
            return false;
        return _definitions.TryGetValue(name, out definition!);
    }

    private Control InstantiateDefinition(CuiComponent instance, CuiComponent definition) =>
        InstantiateInstance(instance, definition.Name ?? definition.Type,
            MergeDefinitionInstance(definition, instance), IsSharedStatePrefab(definition));

    private Control InstantiateTemplate(CuiComponent instance, CuiTemplateDefinition template)
    {
        if (!_definitionPath.Add(template.Name))
            throw CycleDependency(instance, template.Name);

        var hostScope = ComposeStableId(instance.StableId);
        var previousScope = _definitionScope;
        _definitionScope = hostScope;
        try
        {
            var root = new Panel();
            root.ZIndex = ResolveLayer(instance, _currentLayer);
            if (!string.IsNullOrWhiteSpace(instance.Name))
            {
                root.Name = instance.Name;
                Avalonia.Automation.AutomationProperties.SetAutomationId(root, instance.Name);
            }
            CuiRuntimeIdentity.SetStableId(root, hostScope);
            _authoredControls.Add(root, instance);
            ApplyProperties(root, instance);
            ApplyClasses(root, instance);
            ApplyActionTag(root, instance);
            foreach (var child in template.Content.Where(child => !child.IsDefinition))
                root.Children.Add(LoadComponent(WithTemplateInputs(child, instance, template)));
            return root;
        }
        finally
        {
            _definitionScope = previousScope;
            _definitionPath.Remove(template.Name);
        }
    }

    private Control InstantiateInstance(CuiComponent instance, string key, CuiComponent effective, bool sharedInternalScope)
    {
        // Finite caller-supplied children can nest the same reusable container
        // (for example a menu inside a menu). Only definition-authored expansion
        // contributes a dependency; otherwise normal nesting is a false cycle.
        var trackDependency = instance.Children.Count == 0 || !CuiLanguageVocabulary.VisualElements.Contains(effective.Type);
        if (trackDependency && !_definitionPath.Add(key))
            throw CycleDependency(instance, key);

        var hostScope = ComposeStableId(instance.StableId);
        var internalScope = sharedInternalScope
            ? (_definitionScope is null ? $"definition:{key}" : $"{_definitionScope}/definition:{key}")
            : hostScope;

        var previousScope = _definitionScope;
        _definitionScope = internalScope;
        try
        {
            return LoadComponent(effective, hostScope, definitionInstance: true);
        }
        finally
        {
            _definitionScope = previousScope;
            if (trackDependency) _definitionPath.Remove(key);
        }
    }

    private CuiRuntimeLoadException CycleDependency(CuiComponent instance, string key) =>
        new(new CuiDiagnostic(
            "CUIR060",
            CuiDiagnosticSeverity.Error,
            $"Cyclic definition dependency: {string.Join(" -> ", _definitionPath.Concat(new[] { key }))}.",
            instance.Span));

    /// <summary>
    /// Side-effect-free validation of the effective Definition/Prefab/template
    /// composition. Expands inherited routes without invoking custom factories or
    /// mutating loader state, then reuses the shared ActionEvent contract.
    /// </summary>
    private CuiDiagnostic? ValidateEffectiveActionEventComposition(CuiDocument document)
    {
        var definitions = new Dictionary<string, CuiComponent>(StringComparer.Ordinal);
        foreach (var definition in document.Components.SelectMany(DefinitionsOutsideTheme))
        {
            if (CuiLanguageVocabulary.IsForbiddenElement(definition.Type))
                throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR054", CuiDiagnosticSeverity.Error,
                    "A Definition cannot re-export a forbidden donor element.", definition.Span));
            if (definition.Type.Equals("Theme", StringComparison.OrdinalIgnoreCase))
                continue;
            if (CuiLanguageVocabulary.VisualElements.Contains(definition.Type))
            {
                if (definition.Name is not null) definitions[definition.Name] = definition;
                // Type-scoped defaults are applied as properties during normal loading;
                // they do not replace a component or merge its ActionEvent routes.
            }
            else
            {
                definitions[definition.Name ?? definition.Type] = definition;
            }
        }

        // Type definitions are keyed separately in the runtime; expose them by
        // their declared name when present so effective validation can resolve
        // inherited routes that use a named Definition.
        // (No additional action required: named definitions already collected.)

        var effectiveTemplates = new Dictionary<string, CuiTemplateDefinition>(StringComparer.Ordinal);
        foreach (var (name, template) in document.Templates)
        {
            var expandedContent = template.Content
                .Select(child => ExpandEffectiveComponent(child, definitions, document.Templates, new HashSet<string>(StringComparer.Ordinal)))
                .ToArray();
            effectiveTemplates[name] = template with { Content = expandedContent };
        }

        var effectiveComponents = document.Components
            .Select(component => component.IsDefinition
                ? component
                : ExpandEffectiveComponent(component, definitions, document.Templates, new HashSet<string>(StringComparer.Ordinal)))
            .ToArray();

        var effectiveDocument = document with
        {
            Components = effectiveComponents,
            Templates = effectiveTemplates,
        };

        if (ValidateFontWeightLiterals(effectiveDocument) is { } fontWeightError)
            return fontWeightError;
        return CuiActionEventContract.Validate(effectiveDocument).FirstOrDefault();
    }

    private bool TryResolveDefinitionForValidation(string name, Dictionary<string, CuiComponent> definitions) =>
        !_controlRegistry.TryResolveElement(name, out _) && definitions.ContainsKey(name);

    private CuiComponent ExpandEffectiveComponent(
        CuiComponent component,
        Dictionary<string, CuiComponent> definitions,
        IReadOnlyDictionary<string, CuiTemplateDefinition> templates,
        HashSet<string> path)
    {
        if (component.IsThemeScope)
        {
            return RebuildEffectiveComponent(component,
                component.Children.Where(child => !child.IsDefinition)
                    .Select(child => ExpandEffectiveComponent(child, definitions, templates, path)).ToArray(),
                component.ElseChildren.Where(child => !child.IsDefinition)
                    .Select(child => ExpandEffectiveComponent(child, definitions, templates, path)).ToArray());
        }

        if (!component.IsDefinition && TryResolveDefinitionForValidation(component.Type, definitions))
        {
            var definition = definitions[component.Type];
            var key = definition.Name ?? definition.Type;
            // Mirror the runtime's finite caller-child handling: a caller-supplied
            // child list is not a definition dependency, so it must not be treated
            // as a cycle.
            var trackDependency = component.Children.Count == 0
                || !CuiLanguageVocabulary.VisualElements.Contains(definition.Type);
            if (trackDependency && !path.Add(key))
                throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR060", CuiDiagnosticSeverity.Error,
                    $"Cyclic definition dependency: {string.Join(" -> ", path.Concat(new[] { key }))}.", component.Span));
            var effective = MergeDefinitionInstance(definition, component);
            var expanded = ExpandEffectiveComponent(effective, definitions, templates, path);
            if (trackDependency) path.Remove(key);
            return expanded;
        }

        if (!component.IsDefinition && templates.TryGetValue(component.Type, out var template))
        {
            if (!path.Add(template.Name))
                throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR060", CuiDiagnosticSeverity.Error,
                    $"Cyclic template dependency: {string.Join(" -> ", path.Concat(new[] { template.Name }))}.", component.Span));
            var children = template.Content
                .Select(child => WithTemplateInputs(child, component, template))
                .Select(child => ExpandEffectiveComponent(child, definitions, templates, path))
                .ToArray();
            var host = new CuiComponent(
                "Container",
                component.Name,
                component.Classes,
                component.Properties,
                component.Actions,
                children,
                component.Condition,
                component.List,
                component.Span,
                component.DefaultTheme,
                component.Repeat,
                Array.Empty<CuiComponent>(),
                component.Groups,
                isDefinition: false,
                component.PropertyRegion)
            {
                Text = component.Text,
                TextParts = component.TextParts,
            };
            path.Remove(template.Name);
            return host;
        }

        return RebuildEffectiveComponent(component,
            component.Children.Where(child => !child.IsDefinition)
                .Select(child => ExpandEffectiveComponent(child, definitions, templates, path)).ToArray(),
            component.ElseChildren.Where(child => !child.IsDefinition)
                .Select(child => ExpandEffectiveComponent(child, definitions, templates, path)).ToArray());
    }

    private CuiDiagnostic? ValidateFontWeightLiterals(CuiDocument document)
    {
        var components = EnumerateComponents(document.Components)
            .Concat(document.Templates.Values.SelectMany(template => EnumerateComponents(template.Content)));
        foreach (var component in components)
        {
            // Resolve property identity through the same typed registry used by
            // ApplyProperties, including its case and alias rules. An exact key
            // lookup would reject an aliased invalid value only after the previous
            // live lineage had already been retired.
            foreach (var (propertyName, value) in component.Properties)
            {
                if (_controlRegistry.TryGetProperty(propertyName, out var descriptor)
                    && descriptor.LanguageType == "font-weight"
                    && value is CuiLiteralValue literal
                    && !Enum.TryParse<Avalonia.Media.FontWeight>(literal.Value, true, out _))
                    return new CuiDiagnostic("CUIR053", CuiDiagnosticSeverity.Error,
                        $"Invalid CUI FontWeight: '{literal.Value}' is not a supported named or numeric weight.", literal.Span);
            }
        }
        return null;
    }

    private static IEnumerable<CuiComponent> EnumerateComponents(IEnumerable<CuiComponent> components)
    {
        foreach (var component in components)
        {
            yield return component;
            foreach (var child in EnumerateComponents(component.Children)) yield return child;
            foreach (var child in EnumerateComponents(component.ElseChildren)) yield return child;
        }
    }

    private static CuiComponent RebuildEffectiveComponent(CuiComponent source, CuiComponent[] children, CuiComponent[] elseChildren) =>
        new CuiComponent(
            source.Type,
            source.Name,
            source.Classes,
            source.Properties,
            source.Actions,
            children,
            source.Condition,
            source.List,
            source.Span,
            source.DefaultTheme,
            source.Repeat,
            elseChildren,
            source.Groups,
            source.IsDefinition,
            source.PropertyRegion)
        {
            Text = source.Text,
            TextParts = source.TextParts,
        };

    private static bool IsSharedStatePrefab(CuiComponent definition) =>
        definition.Type.Equals("Prefab", StringComparison.OrdinalIgnoreCase)
        && definition.TryGetLiteralAttribute("Type", out var prefabType)
        && prefabType is not null
        && prefabType.Equals("static", StringComparison.OrdinalIgnoreCase);

    /// <summary>The instance supplies identity and inputs; the definition supplies defaults.</summary>
    private static CuiComponent MergeDefinitionInstance(CuiComponent definition, CuiComponent instance)
    {
        var properties = new Dictionary<string, CuiValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in definition.Properties) properties[name] = value;
        foreach (var (name, value) in instance.Properties) properties[name] = value;

        var actions = new Dictionary<string, CuiActionReference>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in definition.Actions) actions[name] = value;
        foreach (var (name, value) in instance.Actions) actions[name] = value;

        return new CuiComponent(
            definition.Type,
            instance.Name ?? definition.Name,
            definition.Classes.Concat(instance.Classes).ToArray(),
            properties,
            actions,
            instance.Children.Count > 0 ? instance.Children : definition.Children,
            instance.Condition ?? definition.Condition,
            instance.List ?? definition.List,
            instance.Span,
            instance.DefaultTheme ?? definition.DefaultTheme,
            instance.Repeat ?? definition.Repeat,
            instance.ElseChildren.Count > 0 ? instance.ElseChildren : definition.ElseChildren,
            instance.Groups.Count > 0 ? instance.Groups : definition.Groups,
            isDefinition: false,
            instance.PropertyRegion ?? definition.PropertyRegion)
        {
            Text = string.IsNullOrWhiteSpace(instance.Text) ? definition.Text : instance.Text,
            TextParts = instance.TextParts.Count > 0 ? instance.TextParts : definition.TextParts,
        };
    }

    /// <summary>Declared template inputs are the only caller state a template instance may capture.</summary>
    private static CuiComponent WithTemplateInputs(CuiComponent source, CuiComponent instance, CuiTemplateDefinition template)
    {
        if (template.Parameters.Count == 0)
            return source;

        var properties = new Dictionary<string, CuiValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in source.Properties)
            properties[name] = SubstituteInput(value, instance, template.Parameters);

        var textParts = source.TextParts
            .Select(part => part is CuiExpressionTextPart expression
                ? new CuiExpressionTextPart(SubstituteInput(expression.Value, instance, template.Parameters), expression.Span)
                : part)
            .ToArray();

        return new CuiComponent(
            source.Type,
            source.Name,
            source.Classes,
            properties,
            source.Actions,
            source.Children.Select(child => WithTemplateInputs(child, instance, template)).ToArray(),
            source.Condition,
            source.List,
            source.Span,
            source.DefaultTheme,
            source.Repeat,
            source.ElseChildren.Select(child => WithTemplateInputs(child, instance, template)).ToArray(),
            source.Groups,
            source.IsDefinition,
            source.PropertyRegion)
        {
            Text = source.Text,
            TextParts = textParts,
        };
    }

    private static CuiValue SubstituteInput(CuiValue value, CuiComponent instance, IReadOnlyList<string> parameters)
    {
        if (value is not CuiBindingValue binding || !parameters.Contains(binding.Path, StringComparer.Ordinal))
            return value;
        return instance.Properties.TryGetValue(binding.Path, out var supplied)
            ? supplied
            : new CuiLiteralValue(binding.Fallback ?? string.Empty, binding.Span);
    }

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
            if (!_repeats.Contains(state)) return;
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
            ApplyObservedBinding(binding.Control, binding.PropertyName, binding.Binding, context, binding.Span);
        }
        RefreshLiveConditionals(propertyName, context);
    }

    private bool EvaluateCondition(CuiCondition condition)
    {
        // A condition whose binding source is absent degrades to its declared fallback, or to an unmet branch.
        // Only a source that resolves to a non-boolean value is a genuine type error.
        if (condition.Test is CuiBindingValue missingSource &&
            (_bindingContext is null || !_bindingContext.TryGetValue(missingSource.Path, out _)))
        {
            var fallbackResult = missingSource.Fallback is string text && bool.TryParse(text, out var parsed)
                ? parsed
                : false;
            return condition.Negate ? !fallbackResult : fallbackResult;
        }

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
        var objectType = component.Type == "Object"
            ? component.Properties.FirstOrDefault(pair => pair.Key.Equals("Type", StringComparison.OrdinalIgnoreCase)).Value
            : null;
        var resolvedObjectType = objectType is null ? null : ResolveValue(objectType);
        var typeName = component.Properties.FirstOrDefault(pair => pair.Key.Equals("Type", StringComparison.OrdinalIgnoreCase)).Value as CuiLiteralValue;
        if (typeName is not null && _typeDefinitions.TryGetValue(component.Type + "\0" + typeName.Value, out var typeDefinition))
            foreach (var (name, value) in typeDefinition.Properties.Where(pair => !pair.Key.Equals("Type", StringComparison.OrdinalIgnoreCase)))
                ApplyAuthoredValue(control, name, value, CuiPropertyLevel.Definition);
        foreach (var (propName, value) in component.Properties)
        {
            if (component.Repeat is not null && (propName.Equals("Panel", StringComparison.OrdinalIgnoreCase) || RepeatItemPlacement(propName) is not null)) continue;
            if (!_controlRegistry.TryGetProperty(propName, out var descriptor))
                throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR052", CuiDiagnosticSeverity.Error, $"Unknown or forbidden CUI property '{propName}'.", value.Span));
            if (_controlRegistry.TryResolveElement(component.Type, out var element) && !_controlRegistry.SupportsProperty(element, resolvedObjectType, descriptor))
                throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR052", CuiDiagnosticSeverity.Error, $"CUI property '{propName}' is not supported on {component.Type}.", value.Span));
            // Type selects the native lowering of core polymorphic elements.
            if (propName.Equals("Type", StringComparison.OrdinalIgnoreCase))
                continue;

            // Generic framework directives (compound focus, embedded input, local
            // toggle, backdrop ownership) are behaviours, not native properties.
            var normalizedDirective = NormalizeProperty(propName);
            if (normalizedDirective is "compoundfocus" or "embeddedinput" or "localtoggle" or "ownsbackdrop")
            {
                ApplyDirective(control, normalizedDirective, value, value.Span);
                continue;
            }

            if (value is CuiInvalidValue invalid)
            {
                throw new CuiRuntimeLoadException(new CuiDiagnostic(
                    invalid.DiagnosticCode, CuiDiagnosticSeverity.Error, invalid.Message, invalid.Span));
            }

            // Live binding: if value is a CuiBindingValue, create a real Avalonia binding. A
            // binding that targets a declared document-root local variable is registered even
            // without an external binding context (pure-local state never depends on host it).
            if (value is CuiBindingValue binding
                && (_bindingContext is not null || _declaredVariables.ContainsKey(binding.Path)))
            {
                ApplyLiveBinding(control, descriptor.Name, binding, component.Span);
                continue;
            }

            ApplyAuthoredValue(control, descriptor.Name, value);
        }
    }

    private void ApplyDirective(Control control, string normalizedName, CuiValue value, CuiSourceSpan span)
    {
        // These are registered static boolean contracts. Only a declared literal true/false is
        // meaningful; malformed/invalid/bound/unsupported values are rejected consistently in the
        // compiler and this actual direct loader before any scene state is produced.
        if (!TryParseDeclaredBoolean(value, out var enabled))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR053", CuiDiagnosticSeverity.Error,
                $"CUI directive '{normalizedName}' requires a declared true/false literal value.", span));

        switch (normalizedName)
        {
            case "compoundfocus":
                if (!enabled) break;
                if (control is not CuiContainerControl container)
                    throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR053", CuiDiagnosticSeverity.Error,
                        "CompoundFocus requires a Container.", span));
                container.EnableCompoundFocus();
                break;
            case "embeddedinput":
                if (!enabled) break;
                if (control is not TextBox textBox)
                    throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR053", CuiDiagnosticSeverity.Error,
                        "EmbeddedInput requires a text Input.", span));
                CuiPropertyPresentation.ConfigureCompoundFocus(textBox);
                break;
            case "ownsbackdrop":
                // Only the actual document root owns the scene backdrop; an arbitrary nested
                // container must never claim it.
                if (enabled && !_isCurrentRootControl)
                    throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR053", CuiDiagnosticSeverity.Error,
                        "OwnsBackdrop belongs only to the document root container.", span));
                break;
        }
    }

    private static bool TryParseDeclaredBoolean(CuiValue value, out bool result)
    {
        result = false;
        return value is CuiLiteralValue literal && bool.TryParse(literal.Value, out result);
    }

    private static bool TryGetDeclaredBoolean(CuiComponent component, string normalizedName)
    {
        foreach (var (key, value) in component.Properties)
            if (NormalizeProperty(key) == normalizedName && TryParseDeclaredBoolean(value, out var parsed) && parsed)
                return true;
        return false;
    }

    private void ApplyAuthoredValue(Control control, string property, CuiValue value, CuiPropertyLevel level = CuiPropertyLevel.Instance)
    {
        if (value is CuiResourceValue resource) ApplyResourceProperty(control, property, resource, level);
        else if (ResolveValue(value) is { } literal) ApplyLiteralProperty(control, property, literal, value.Span, level);
    }

    private void ApplyResourceProperty(Control control, string property, CuiResourceValue resource, CuiPropertyLevel level = CuiPropertyLevel.Instance)
    {
        var value = ResolveResourceObject(resource.Key, control);
        if (value is null) throw new CuiRuntimeLoadException(new("CUIR057", CuiDiagnosticSeverity.Error,
            $"CUI resource '{resource.Key}' is unavailable in this scope.", resource.Span));
        if (value is string literal) ApplyLiteralProperty(control, property, literal, resource.Span, level);
        else ApplyTypedProperty(control, property, value, resource.Span, level);
    }

    private void ApplyTypedProperty(Control control, string property, object? value, CuiSourceSpan span, CuiPropertyLevel level = CuiPropertyLevel.Instance)
    {
        try
        {
            if (CuiPropertyPresentation.Set(control, property, value, level, "CUI authored " + level)) return;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { throw new CuiRuntimeLoadException(new("CUIR053", CuiDiagnosticSeverity.Error, $"Invalid CUI {property}: {error.Message}", span)); }
        throw new CuiRuntimeLoadException(new("CUIR053", CuiDiagnosticSeverity.Error,
            $"CUI property '{property}' has no typed projection for this resource.", span));
    }

    private void ApplyAuthoredText(Control control, CuiComponent component)
    {
        if (string.IsNullOrWhiteSpace(component.Text)
            || component.Properties.Keys.Any(name => name.Equals("Text", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Content", StringComparison.OrdinalIgnoreCase) || name.Equals("Value", StringComparison.OrdinalIgnoreCase)))
            return;

        switch (control)
        {
            case TextBlock:
                ApplyLiteralProperty(control, "Text", component.Text, component.Span);
                break;
            case TextBox:
                ApplyLiteralProperty(control, "Value", component.Text, component.Span);
                break;
            case ContentControl:
                ApplyLiteralProperty(control, "Content", component.Text, component.Span);
                break;
        }
    }

    private void ApplyLiveBinding(Control control, string propName, CuiBindingValue binding, CuiSourceSpan sourceSpan)
    {
        // A binding whose path resolves to a declared document-root local variable is owned by this
        // loader's per-instance local-state frame. It never consults the external binding context
        // and is refreshed by local Toggle mutations, not host notifications.
        if (_declaredVariables.ContainsKey(binding.Path))
        {
            if (!_localBindings.TryGetValue(binding.Path, out var localBindings))
                _localBindings[binding.Path] = localBindings = new List<CuiLiveBinding>();
            localBindings.Add(new CuiLiveBinding(control, propName, binding, sourceSpan, null!));
            ApplyObservedBinding(control, propName, binding, null!, sourceSpan);
            return;
        }

        // Live values update from host notifications; OneTime is a snapshot.
        if (binding.Mode != CuiBindingMode.OneTime)
            _liveBindings.Add(new CuiLiveBinding(control, propName, binding, sourceSpan, _bindingContext!));

        // Apply initial value
        var context = _bindingContext!;
        ApplyObservedBinding(control, propName, binding, context, sourceSpan);

        var inputDefaultWriteBack = binding.Mode != CuiBindingMode.OneTime && context is ICuiWritableBindingContext
            && _authoredControls.TryGetValue(control, out var authoredInput) && authoredInput.Type.Equals("Input", StringComparison.OrdinalIgnoreCase)
            && NormalizeProperty(propName) is "value" or "text" or "checked" or "ischecked" or "selectedindex"
            && !(authoredInput.Properties.FirstOrDefault(pair => pair.Key.Equals("WriteBack", StringComparison.OrdinalIgnoreCase)).Value is CuiLiteralValue writeBack && writeBack.Value.Equals("false", StringComparison.OrdinalIgnoreCase));
        if (binding.Mode == CuiBindingMode.TwoWay || inputDefaultWriteBack)
            WireInputWriteBack(control, propName, binding, sourceSpan, context);
    }

    /// <summary>
    /// Refresh all tracked live bindings from the current binding context.
    /// Call this when the ViewModel data changes to push updates to the UI.
    /// </summary>
    public void RefreshBindings()
    {
        foreach (var record in _liveBindings)
        {
            ApplyObservedBinding(record.Control, record.PropertyName, record.Binding, record.Context, record.Span);
        }
        RefreshLiveConditionals(null);
        foreach (var repeat in _repeats.ToArray())
            RequestRepeatRefresh(repeat);
    }

    /// <summary>A validated, snapshotted RandomOnce plan awaiting once-per-frame selection in the
    /// second preflight pass.</summary>
    private sealed record RandomOncePlan(CuiVariableDeclaration Declaration, IReadOnlyList<string> Candidates, int Generation);

    /// <summary>Upper bound for an explicit finite ordered typed candidate pool captured before any
    /// allocation, so a hostile/lazy Count cannot force an unbounded snapshot.</summary>
    private const int MaxRandomOncePoolSize = 8192;

    /// <summary>
    /// First-pass RandomOnce validation: checks every declaration shape and snapshots the typed
    /// candidate pool. No chooser is invoked here, so a later invalid declaration is discovered
    /// before an earlier valid one draws. The shared strict contract holds at runtime even when a
    /// document bypasses the compiler (direct Load/LoadMarkup admission), not only after
    /// compilation. A failure here retires nothing; the previous tree stays operational.
    /// </summary>
    private IReadOnlyList<string> SnapshotRandomOnceCandidates(CuiVariableDeclaration declaration, int generation)
    {
        if (!string.Equals(declaration.Selection, "RandomOnce", StringComparison.OrdinalIgnoreCase))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR074", CuiDiagnosticSeverity.Error,
                $"Local variable '{declaration.Name}' declares unsupported Selection '{declaration.Selection}'; only 'RandomOnce' is supported.", declaration.Span));
        if (!string.Equals(declaration.Type, "string", StringComparison.OrdinalIgnoreCase))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR075", CuiDiagnosticSeverity.Error,
                $"RandomOnce local variable '{declaration.Name}' requires Type 'string'; got '{declaration.Type}'.", declaration.Span));
        if (string.IsNullOrWhiteSpace(declaration.Source))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR070", CuiDiagnosticSeverity.Error,
                $"Local variable '{declaration.Name}' requires a Source binding candidate pool.", declaration.Span));
        if (declaration.Value.Length > 0)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR076", CuiDiagnosticSeverity.Error,
                $"RandomOnce local variable '{declaration.Name}' cannot also carry a literal initializer '{declaration.Value}'.", declaration.Span));

        IReadOnlyList<string> candidates;
        if (_bindingContext is not null && _bindingContext.TryGetValue(declaration.Source, out var sourceValue))
        {
            // The binding-context getter is an external callback; it may synchronously reload or
            // dispose this loader. Reject a stale outer attempt after it returns (even when the
            // lookup itself failed).
            if (!IsLoadAttemptCurrent(generation)) throw ReentrancyError(declaration);
            candidates = SnapshotTypedStringPool(sourceValue, declaration, generation);
            if (!IsLoadAttemptCurrent(generation)) throw ReentrancyError(declaration);
        }
        else
        {
            if (!IsLoadAttemptCurrent(generation)) throw ReentrancyError(declaration);
            candidates = ImmutableStringList.Empty;
        }

        if (candidates.Count == 0)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR071", CuiDiagnosticSeverity.Error,
                $"Local variable '{declaration.Name}' RandomOnce source '{declaration.Source}' produced an empty candidate pool.", declaration.Span));
        return candidates;
    }

    /// <summary>
    /// Second-pass RandomOnce selection: invoked only after every declaration/pool validated. The
    /// public chooser result is validated to be nonnull/nonempty and EXACTLY in the snapshotted
    /// pool, and the load generation/dispose guard rejects a stale outer continuation after the
    /// chooser callback reentered/replaced/disposed the loader.
    /// </summary>
    private string SelectRandomOnce(CuiVariableDeclaration declaration, IReadOnlyList<string> candidates, int generation)
    {
        var chooser = RandomSelectionOverride ?? DefaultRandomSelection;
        string chosen;
        try
        {
            chosen = chooser(candidates) ?? string.Empty;
        }
        catch (CuiRuntimeLoadException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR079", CuiDiagnosticSeverity.Error,
                $"Local variable '{declaration.Name}' RandomOnce chooser failed: {error.Message}", declaration.Span));
        }
        if (!IsLoadAttemptCurrent(generation)) throw ReentrancyError(declaration);
        if (chosen.Length == 0)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR080", CuiDiagnosticSeverity.Error,
                $"Local variable '{declaration.Name}' RandomOnce chooser returned an empty candidate.", declaration.Span));
        if (!ContainsOriginalCandidate(candidates, chosen))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR081", CuiDiagnosticSeverity.Error,
                $"Local variable '{declaration.Name}' RandomOnce chooser returned a candidate not present in the snapshotted pool.", declaration.Span));
        return chosen;
    }

    /// <summary>Validates a chooser result against the private immutable original pool contents,
    /// never against any view the chooser could mutate.</summary>
    private static bool ContainsOriginalCandidate(IReadOnlyList<string> candidates, string chosen) =>
        candidates is ImmutableStringList snapshot ? snapshot.Contains(chosen) : candidates.Contains(chosen, StringComparer.Ordinal);

    /// <summary>
    /// Snapshots the candidate pool under a truthful explicit finite ordered typed candidate-list
    /// contract: only <see cref="IReadOnlyList{T}"/> of string (finite and ordered) is accepted.
    /// Scalar strings (which would enumerate UTF-16 code units and split emoji), non-string and
    /// arbitrary/unbounded enumerables (int[], mixed arrays, yield-based iterators, lazy sequences)
    /// and null/empty entries are rejected rather than silently filtered. The full Unicode
    /// sequences are retained verbatim, and the returned snapshot is an OWNED immutable copy, never
    /// the live host list, so later host mutation cannot affect this frame's chooser or consumers.
    /// </summary>
    private IReadOnlyList<string> SnapshotTypedStringPool(object? sourceValue, CuiVariableDeclaration declaration, int generation)
    {
        switch (sourceValue)
        {
            case null:
                return ImmutableStringList.Empty;
            case string or char:
                throw new CuiRuntimeLoadException(new CuiDiagnostic(
                    "CUIR072", CuiDiagnosticSeverity.Error,
                    $"Local variable '{declaration.Name}' RandomOnce source '{declaration.Source}' must be a typed string collection; got scalar '{sourceValue.GetType().Name}'.", declaration.Span));
            case IReadOnlyList<string> list:
                return SnapshotOrderedStringList(list, declaration, generation);
            case System.Collections.IEnumerable:
                throw new CuiRuntimeLoadException(new CuiDiagnostic(
                    "CUIR072", CuiDiagnosticSeverity.Error,
                    $"Local variable '{declaration.Name}' RandomOnce source '{declaration.Source}' must be an explicit finite ordered typed string list (IReadOnlyList&lt;string&gt;); got enumerable '{sourceValue.GetType().Name}'.", declaration.Span));
            default:
                throw new CuiRuntimeLoadException(new CuiDiagnostic(
                    "CUIR072", CuiDiagnosticSeverity.Error,
                    $"Local variable '{declaration.Name}' RandomOnce source '{declaration.Source}' is not a readable candidate pool ('{sourceValue.GetType().Name}').", declaration.Span));
        }
    }

    /// <summary>Reads and validates the indexed pool exactly once. Count is captured ONCE, the load
    /// attempt is checked immediately after it and after every attempted host Count/index read, and
    /// an explicit supported size bound is enforced before allocation. The host enumerator/CopyTo/
    /// ToArray are never invoked, so indexed and enumerated views cannot diverge.</summary>
    private ImmutableStringList SnapshotOrderedStringList(IReadOnlyList<string> list, CuiVariableDeclaration declaration, int generation)
    {
        int count;
        try { count = list.Count; }
        catch (Exception error) { throw PoolReadError(declaration, $"failed to read the pool Count: {error.Message}"); }
        if (!IsLoadAttemptCurrent(generation)) throw ReentrancyError(declaration);
        if (count < 0 || count > MaxRandomOncePoolSize)
            throw PoolReadError(declaration, $"has unsupported Count {count}; an explicit finite ordered typed pool supports 1..{MaxRandomOncePoolSize} entries");

        var copy = new string[count];
        for (var i = 0; i < count; i++)
        {
            string item;
            try { item = list[i]; }
            catch (Exception error) { throw PoolReadError(declaration, $"failed to read pool index {i}: {error.Message}"); }
            if (!IsLoadAttemptCurrent(generation)) throw ReentrancyError(declaration);
            if (item is null)
                throw PoolReadError(declaration, $"contains a null entry at index {i}");
            if (item.Length == 0)
                throw PoolReadError(declaration, $"contains an empty entry at index {i}");
            copy[i] = item;
        }
        return new ImmutableStringList(copy);
    }

    private static CuiRuntimeLoadException PoolReadError(CuiVariableDeclaration declaration, string detail) =>
        new(new CuiDiagnostic(
            "CUIR072", CuiDiagnosticSeverity.Error,
            $"Local variable '{declaration.Name}' RandomOnce source '{declaration.Source}' {detail}.", declaration.Span));

    /// <summary>A genuinely read-only owned snapshot of the finite ordered candidate pool. The
    /// underlying array is private and never exposed, so a hostile caller cannot cast the
    /// IReadOnlyList view back to a mutable string[] and mutate membership.</summary>
    private sealed class ImmutableStringList : IReadOnlyList<string>
    {
        public static readonly ImmutableStringList Empty = new(Array.Empty<string>());
        private readonly string[] _items;
        public ImmutableStringList(string[] items) => _items = items;
        public int Count => _items.Length;
        public string this[int index] => _items[index];
        /// <summary>Ordinal membership against the private original contents, never a caller-mutable view.</summary>
        public bool Contains(string value) => Array.IndexOf(_items, value) >= 0;
        public IEnumerator<string> GetEnumerator()
        {
            for (var i = 0; i < _items.Length; i++) yield return _items[i];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>True while the named load attempt is still the current one and the loader is
    /// not disposed. Used to reject stale outer attempts after an external callback reentered
    /// (reloaded) or disposed the loader.</summary>
    private bool IsLoadAttemptCurrent(int generation) => !_disposed && generation == _loadGeneration;

    private static CuiRuntimeLoadException ReentrancyError(CuiVariableDeclaration declaration) =>
        new(new CuiDiagnostic(
            "CUIR073", CuiDiagnosticSeverity.Error,
            $"Local variable '{declaration.Name}' RandomOnce source/chooser reentered the loader during mount; the attempted load was abandoned and the existing frame was left operational.", declaration.Span));

    /// <summary>The generic random choose-once chooser used when no override is supplied.</summary>
    public static Func<IReadOnlyList<string>, string> DefaultRandomSelection { get; } =
        candidates => candidates[Random.Shared.Next(candidates.Count)];

    /// <summary>Refreshes every live local binding that references the named document-root local
    /// variable after a local Toggle mutation. Synchronous observer reentry is safe: the binding
    /// list is snapshotted before notifications, and a reentrant toggle re-applies the new value.</summary>
    private void RefreshLocalVariable(string name)
    {
        if (!_localBindings.TryGetValue(name, out var bindings)) return;
        foreach (var record in bindings.ToArray())
        {
            if (_disposed) return;
            ApplyObservedBinding(record.Control, record.PropertyName, record.Binding, null!, record.Span);
        }
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

                ApplyObservedBinding(record.Control, record.PropertyName, record.Binding, record.Context, record.Span);
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
        foreach (var conditional in _liveConditionals.ToArray())
        {
            if (!_liveConditionals.Contains(conditional)) continue;
            var leaf = conditional.Path[(conditional.Path.LastIndexOf('.') + 1)..];
            if (!string.IsNullOrEmpty(propertyName)
                && !conditional.Path.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                && !leaf.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (context is not null && !ReferenceEquals(context, conditional.Context))
                continue;
            var previousContext = _bindingContext;
            _bindingContext = conditional.Context;
            _themeStack.Push(ResolveTheme(conditional.ThemeName).Theme.BaseTheme); _themeNames.Push(conditional.ThemeName);
            try
            {
                var conditionValue = EvaluateCondition(conditional.Component.Condition!);
                // Scalar notifications already refresh live bindings. Keeping the
                // selected branch preserves editor identity, focus and original owners.
                if (conditionValue == conditional.LastValue) continue;
                var branch = BuildConditionalBranch(conditional.Component, conditionValue, conditional.Layer);
                if (conditional.Host.Content is Control oldBranch) DetachControlTree(oldBranch);
                conditional.Host.Content = branch;
                conditional.LastValue = conditionValue;
                WireBindingsRecursive(branch);
            }
            finally
            {
                _themeStack.Pop();
                _themeNames.Pop();
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
            CuiResourceValue resource => ResolveResourceObject(resource.Key),
            _ => null,
        };
    }

    private void WireInputWriteBack(Control control, string propertyName, CuiBindingValue binding, CuiSourceSpan sourceSpan, ICuiBindingContext context)
    {
        if (context is not ICuiWritableBindingContext writable)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR006", CuiDiagnosticSeverity.Error,
                $"Two-way binding '{binding.Path}' requires a host that implements {nameof(ICuiWritableBindingContext)}.",
                sourceSpan));

        void Update(object? rawValue)
        {
            if (_disposed || _applyingHostValues.Contains(control) || !_liveBindings.Any(record =>
                ReferenceEquals(record.Control, control) && ReferenceEquals(record.Context, context) && record.Binding == binding)) return;
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
                textBox.TextChanging += (_, _) => Update(textBox.Text);
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
            case Avalonia.Controls.Primitives.SelectingItemsControl selection when NormalizeProperty(propertyName) == "selectedindex":
                selection.SelectionChanged += (_, _) => Update(selection.SelectedIndex);
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

    private void ApplyLiteralProperty(Control control, string propName, string resolved, CuiSourceSpan sourceSpan, CuiPropertyLevel level = CuiPropertyLevel.Instance)
    {
        try
        {
            if (propName.Equals("IconSource", StringComparison.OrdinalIgnoreCase))
            {
                if (control is not MenuItem menu) throw new ArgumentException("IconSource requires a menu item.");
                menu.Icon = _controlRegistry.ResolveImageSource(resolved, control)
                    ?? throw new ArgumentException($"Icon source '{resolved}' is not registered by this host.");
                return;
            }
            if (control is Image && propName.Equals("Source", StringComparison.OrdinalIgnoreCase))
            {
                var image = _controlRegistry.ResolveImageSource(resolved, control);
                if (image is null) throw new ArgumentException($"Image source '{resolved}' is not registered by this host.");
                ApplyTypedProperty(control, propName, image, sourceSpan, level);
                return;
            }
            if (CuiLifecycleProperties.SetLiteral(control, propName, resolved, level, "CUI authored " + level)) return;
            if (CuiLayoutProperties.SetLiteral(control, propName, resolved, level, "CUI authored " + level)) return;
            if (CuiPropertyPresentation.TrySetLiteral(control, propName, resolved, level, "CUI authored " + level, sourceSpan: sourceSpan)) return;
        }
        catch (Exception error) when (error is FormatException or ArgumentException or InvalidOperationException)
        { throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR053", CuiDiagnosticSeverity.Error, $"Invalid CUI {propName}: {error.Message}", sourceSpan)); }
        var normalizedPropertyName = propName.Replace("-", string.Empty, StringComparison.Ordinal);
            switch (normalizedPropertyName.ToLowerInvariant())
            {
                case "spacing":
                    if (control is StackPanel spaced && double.TryParse(resolved, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var spacing) && double.IsFinite(spacing) && spacing >= 0) spaced.Spacing = spacing;
                    else throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR041", CuiDiagnosticSeverity.Error, "Spacing requires a nonnegative finite StackPanel value.", sourceSpan));
                    break;
                case "isenabled":
                    if (bool.TryParse(resolved, out var enabled)) control.IsEnabled = enabled;
                    else throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR041", CuiDiagnosticSeverity.Error, "IsEnabled must be true or false.", sourceSpan));
                    break;
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
                    else if (control is Button textButton)
                        textButton.Content = resolved;
                    else if (control is CuiMarkdownView markdown)
                        markdown.Text = resolved;
                    else if (control is ICuiTextBindingTarget textTarget &&
                             _authoredControls.TryGetValue(control, out var textComponent) &&
                             textComponent.Type.Equals("Object", StringComparison.OrdinalIgnoreCase))
                        textTarget.Text = resolved;
                    break;
                case "header":
                    if (control is MenuItem menuItem) menuItem.Header = resolved;
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
                case "selectedindex":
                    if (control is Avalonia.Controls.Primitives.SelectingItemsControl selection &&
                        int.TryParse(resolved, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var selectedIndex) && selectedIndex >= -1)
                        selection.SelectedIndex = selectedIndex;
                    else throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR007", CuiDiagnosticSeverity.Error,
                        "SelectedIndex requires a native selector and an integer of at least -1.", sourceSpan));
                    break;
                case "itemssource":
                    throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR007", CuiDiagnosticSeverity.Error,
                        "ItemsSource requires an explicit collection binding.", sourceSpan));
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
                    if (!Enum.TryParse<Avalonia.Media.FontWeight>(resolved, true, out var fw))
                        throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR053", CuiDiagnosticSeverity.Error, "Invalid CUI FontWeight: a supported named or numeric weight is required.", sourceSpan));
                    if (control is TextBlock fwTb)
                        fwTb.FontWeight = fw;
                    else if (control is Avalonia.Controls.Primitives.TemplatedControl fwTc)
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
                case "tooltip":
                    ToolTip.SetTip(control, resolved);
                    break;
                case "shortcut":
                    var gesture = Avalonia.Input.KeyGesture.Parse(resolved);
                    if (control is MenuItem shortcutMenu) shortcutMenu.InputGesture = gesture;
                    else HotKeyManager.SetHotKey(control, gesture);
                    CakeOS.Cui.Themes.CuiAccessibilityProperties.SetShortcut(control, resolved);
                    break;
                case "selected":
                    CuiPropertyPresentation.SetSelected(control, bool.Parse(resolved));
                    break;
                case "resizeedge":
                    if (control is not CuiResizeInput resize || resolved is not ("Start" or "End"))
                        throw new ArgumentException("ResizeEdge must be Start or End on a Resize input.");
                    resize.FromStart = resolved == "Start";
                    break;
                case "accessibledescription":
                    Avalonia.Automation.AutomationProperties.SetHelpText(control, resolved);
                    break;
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
        var ownedEvent = CuiActionEventContract.OwnedAction(component);
        if (ownedEvent is not null && CuiActionEventContract.HasCompetingActionRoutes(component))
            throw new CuiRuntimeLoadException(new("CUIE004", CuiDiagnosticSeverity.Error,
                "The effective Button contains competing direct and ActionEvent routes after Definition merging.", ownedEvent.Span));
        if (ownedEvent is not null && control is not (Button or MenuItem))
            throw new CuiRuntimeLoadException(new("CUIE003", CuiDiagnosticSeverity.Error,
                "The registered Button implementation cannot execute the owned click ActionEvent.", ownedEvent.Span));
        // The parser routes action="Cmd" into CuiComponent.Actions (not Properties),
        // so honor it here; WireBindingsRecursive dispatches via control.Tag.
        string? reference = control.Tag as string;
        if (string.IsNullOrWhiteSpace(reference)
            && component.Actions.TryGetValue("action", out var actionRef))
            reference = actionRef.Name;
        if (string.IsNullOrWhiteSpace(reference)
            && component.Actions.TryGetValue("on:click", out var clickRef))
            reference = clickRef.Name;
        if (ownedEvent is not null) reference = ownedEvent.Name;

        if (string.IsNullOrWhiteSpace(reference))
            return;

        control.Tag = reference;
        var command = reference;
        object? parameter = null;
        if (_documentActions.TryGetValue(reference, out var definition))
        {
            if (definition.LocalTarget is { } localTarget)
            {
                _actionInvocations[control] = new CuiActionInvocation(LocalToggleCommand, localTarget, null, null, ownedEvent is not null);
                return;
            }
            command = definition.Command;
            if (definition.Parameter is not null && definition.Parameter is not CuiBindingValue)
                parameter = ResolveObject(definition.Parameter);
        }
        var liveParameter = _documentActions.TryGetValue(reference, out var boundDefinition)
            ? boundDefinition.Parameter as CuiBindingValue : null;
        _actionInvocations[control] = new CuiActionInvocation(command, parameter, _bindingContext, liveParameter, ownedEvent is not null);
    }

    /// <summary>Resolve the nearest authored QueryViewport scope ancestor of a control, if any. The
    /// viewport owns the paged-query runtime state and routes its authored controls' actions; the
    /// viewport forwards non-pager commands to the scene host dispatcher.</summary>
    private static ICuiActionDispatcher? ScopedDispatcherFor(Control control)
    {
        Control? current = control;
        while (current is not null)
        {
            if (current is CuiQueryViewport viewport) return viewport;
            current = current.GetVisualParent();
        }
        return null;
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

    private sealed record CuiActionInvocation(string Command, object? Parameter,
        ICuiBindingContext? ParameterContext = null, CuiBindingValue? LiveParameter = null, bool OriginOnly = false)
    {
        internal object? ResolveCurrentParameter() => LiveParameter is not null
            ? ParameterContext is not null && ParameterContext.TryGetValue(LiveParameter.Path, out var current)
                ? current : LiveParameter.Fallback
            : Parameter;
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
        // A canonical container-mode Repeat already attached its item range to the owning Container
        // during LoadRepeat; the returned fragment is a pure bookkeeping marker with no layout,
        // visual or input effect and must never be added as a cell.
        if (child is CuiRepeatFragment) return;
        if (child is ContextMenu menu)
        {
            if (parent.ContextMenu is not null || _ownedContextMenus.ContainsKey(parent))
                throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR013", CuiDiagnosticSeverity.Error,
                    "A control can own only one authored ContextMenu.", _authoredControls[child].Span));
            // Capture ownership before native attachment can enter any callback.
            _ownedContextMenus.Add(parent, menu);
            TopLevel? openingRoot = null;
            Control? previousFocus = null;
            var openingGeneration = 0;
            EventHandler<AvaloniaPropertyChangedEventArgs> captureFocus = (_, change) =>
            {
                if (change.Property != ContextMenu.IsOpenProperty || !menu.IsOpen) return;
                // IsOpen is published before the popup takes focus, for both
                // context requests and programmatic Open (which bypasses Opening).
                openingGeneration++;
                openingRoot = TopLevel.GetTopLevel(parent);
                var focused = openingRoot?.FocusManager?.GetFocusedElement() as Control;
                previousFocus = focused is not null && (ReferenceEquals(focused, parent) || parent.IsVisualAncestorOf(focused))
                    ? focused : null;
            };
            EventHandler<RoutedEventArgs> restoreFocus = (_, _) =>
            {
                var root = openingRoot;
                var target = previousFocus ?? parent;
                var generation = openingGeneration;
                var focused = root?.FocusManager?.GetFocusedElement();
                // Wait for light-dismiss/action routing to settle, then return keyboard
                // input to the same editor inside the owner if focus has not moved.
                // Object/Container owners can be nonfocusable composition controls.
                Dispatcher.UIThread.Post(() =>
                {
                    if (_disposed || menu.IsOpen || generation != openingGeneration || root is null
                        || !ReferenceEquals(TopLevel.GetTopLevel(parent), root)
                        || !ReferenceEquals(parent.ContextMenu, menu) || !parent.IsEffectivelyEnabled || !parent.IsEffectivelyVisible
                        || !target.Focusable || !target.IsEffectivelyEnabled || !target.IsEffectivelyVisible
                        || !ReferenceEquals(TopLevel.GetTopLevel(target), root)
                        || !(ReferenceEquals(target, parent) || parent.IsVisualAncestorOf(target))
                        || !ReferenceEquals(root.FocusManager?.GetFocusedElement(), focused)) return;
                    if (focused is Control current && current.IsEffectivelyVisible && TopLevel.GetTopLevel(current) is not null
                        && !ReferenceEquals(current, menu) && !current.GetLogicalAncestors().Contains(menu)) return;
                    target.Focus(NavigationMethod.Unspecified);
                });
            };
            _contextMenuFocusHandlers.Add(menu, (captureFocus, restoreFocus));
            menu.PropertyChanged += captureFocus;
            menu.Closed += restoreFocus;
            parent.ContextMenu = menu;
            return;
        }
        switch (parent)
        {
            case CuiContainerControl container:
                if (CuiAbsoluteLayout.HasPlacement(child) && container.Layout is not CuiAbsolutePanel)
                    throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR058", CuiDiagnosticSeverity.Error,
                        "Free coordinates require a Container Type Absolute parent.", _authoredControls[child].Span));
                container.AddChild(child);
                break;
            case Panel panel:
                panel.Children.Add(child);
                break;
            case Decorator decorator:
                decorator.Child = child;
                break;
            case ContentControl contentControl:
                contentControl.Content = child;
                break;
            case Menu nativeMenu:
                if (child is MenuItem menuItem)
                    nativeMenu.Items.Add(menuItem);
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

    private string? ResolveResourceValue(string key) => ResolveResourceObject(key)?.ToString();

    private object? ResolveResourceObject(string key, Control? control = null)
    {
        if (_resourceScope.TryGetValue(key, out var value))
            return value;
        if (control is not null && control.Resources.TryGetResource(key, null, out var scoped)) return scoped;
        if (control is null)
        {
            var resources = new ResourceDictionary();
            CuiThemeResourceApplier.ApplyToResources(resources, CuiSurfacePaletteCatalog.For(_currentSurface,
                _appearance ?? _themePreference?.Current.Appearance ?? CuiThemeScopeApplier.DetectAppearance(), _themeStack.Current));
            if (resources.TryGetResource(key, null, out var themed)) return themed;
        }
        return Application.Current?.Resources.TryGetResource(key, null, out var shared) == true ? shared : null;
    }

    private string? ResolveBindingValue(CuiBindingValue binding)
    {
        // Local document-root variables resolve before any external/theme context; they are the
        // sole owner of these names for this loaded document instance.
        if (_localState.TryGetValue(binding.Path, out var local))
            return Convert.ToString(local, System.Globalization.CultureInfo.InvariantCulture);

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

    private static string NormalizeProperty(string name) => name.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private void ApplyObservedBinding(Control control, string property, CuiBindingValue binding,
        ICuiBindingContext context, CuiSourceSpan span)
    {
        var alreadyApplying = !_applyingHostValues.Add(control);
        try
        {
            if (NormalizeProperty(property) == "itemssource")
            {
                if (control is not ItemsControl items || binding.Mode == CuiBindingMode.TwoWay ||
                    !context.TryGetValue(binding.Path, out var source) || source is string || source is not System.Collections.IEnumerable values)
                    throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR007", CuiDiagnosticSeverity.Error,
                        "ItemsSource requires a one-way bounded collection binding on an ItemsControl.", span));
                var snapshot = new List<object?>();
                foreach (var value in values)
                {
                    if (snapshot.Count == 10000) throw new CuiRuntimeLoadException(new CuiDiagnostic("CUIR007", CuiDiagnosticSeverity.Error,
                        "ItemsSource exceeds the supported 10000-item bound.", span));
                    snapshot.Add(value);
                }
                // A notification can concern another field. Retain native item
                // containers, focus and open-popup selection for equal values.
                var unchanged = items.ItemsSource is not null &&
                    items.ItemsSource.Cast<object?>().SequenceEqual(snapshot);
                if (!unchanged) items.ItemsSource = snapshot;
                if (!_observedBindings.TryGetValue(control, out var observed))
                    _observedBindings[control] = observed = new(StringComparer.OrdinalIgnoreCase);
                observed[property] = new(property, binding, $"{snapshot.Count} items", source.GetType().Name, false, true, null);
                // Reapply the host's selection after replacement; native reset notifications are not user edits.
                foreach (var selected in _liveBindings.Where(record => !unchanged && ReferenceEquals(record.Control, control) &&
                    NormalizeProperty(record.PropertyName) == "selectedindex").ToArray())
                    ApplyObservedBinding(control, selected.PropertyName, selected.Binding, selected.Context, selected.Span);
                return;
            }
            var previous = _observedBindings.GetValueOrDefault(control)?.GetValueOrDefault(property)?.ResolvedValue;
            var resolved = ResolveObservedBinding(control, property, binding, context, out var typed, out var hasTypedValue);
            // Renderer objects are already typed. Stringifying them discards gradients,
            // vector image data and other nonliteral identity/state.
            if (hasTypedValue && typed is Avalonia.Media.IBrush or Avalonia.Media.IImage)
            {
                ApplyTypedProperty(control, property, typed, span);
                return;
            }
            if (control is Image && property.Equals("Source", StringComparison.OrdinalIgnoreCase)
                && hasTypedValue && typed is null)
            {
                ApplyTypedProperty(control, property, null, span);
                return;
            }
            if (CuiInputValidationProperties.GetHasError(control) && resolved == previous && NormalizeProperty(property) is "value" or "text" or "checked" or "selectedindex") return;
            if (resolved is not null) ApplyLiteralProperty(control, property, resolved, span);
        }
        catch
        {
            if (NormalizeProperty(property) == "itemssource" && control is ItemsControl items)
                items.ItemsSource = Array.Empty<object>();
            throw;
        }
        finally { if (!alreadyApplying) _applyingHostValues.Remove(control); }
    }

    private string? ResolveObservedBinding(
        Control control,
        string property,
        CuiBindingValue binding,
        ICuiBindingContext context, out object? typedValue, out bool hasTypedValue)
    {
        typedValue = null;
        hasTypedValue = false;
        string? value = null;
        string? sourceType = null;
        var sourceFound = false;
        var usedFallback = false;
        string? error = null;
        try
        {
            if (_localState.TryGetValue(binding.Path, out var local))
            {
                typedValue = local;
                hasTypedValue = true;
                sourceFound = true;
                sourceType = "CUI local variable";
                value = Convert.ToString(local, System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                value = ResolveSpecialBinding(binding.Path);
                if (value is not null)
                {
                    sourceFound = true;
                    sourceType = "CUI theme context";
                }
                else if (context.TryGetValue(binding.Path, out var result))
                {
                    // Diagnostics and rendering must use the same host snapshot.
                    // Re-reading can allocate fresh projections or observe a newer
                    // value than the one recorded for this binding refresh.
                    typedValue = result;
                    hasTypedValue = true;
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
        var parts = value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

    internal static Avalonia.Media.IBrush ParseBrush(string value, CuiSourceSpan sourceSpan)
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
        var name = _themeNames.Count > 0 ? _themeNames.Peek() : "user_preference";
        _controlThemeNames[control] = name;
        ApplyResolvedTheme(control, ResolveTheme(name));
    }

    /// <summary>
    /// Resolves special theme-related binding paths.
    /// {Theme} â†’ current theme name (e.g. "Glow")
    /// {Appearance} â†’ current appearance (e.g. "Dark")
    /// </summary>
    private string? ResolveSpecialBinding(string path)
    {
        return path switch
        {
            "Theme" or "theme" => CuiThemeCatalog.Name(_themeStack.Current),
            "Appearance" or "appearance" => (_appearance ?? _themePreference?.Current.Appearance ?? CuiThemeScopeApplier.DetectAppearance()).ToString(),
            _ => null
        };
    }

    private IEnumerable<Control> EnumerateControls(Control root)
    {
        yield return root;
        if (_ownedContextMenus.TryGetValue(root, out var menu))
            foreach (var descendant in EnumerateControls(menu)) yield return descendant;
        switch (root)
        {
            case CuiContainerControl container:
                foreach (var child in container.AuthoredChildren)
                foreach (var descendant in EnumerateControls(child)) yield return descendant;
                break;
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
            case ItemsControl items:
                foreach (var item in items.Items.OfType<Control>())
                foreach (var descendant in EnumerateControls(item)) yield return descendant;
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
        ICuiBindingContext Context,
        string ThemeName)
    {
        public bool LastValue { get; set; }
    }

    private sealed record RepeatItemInstance(RepeatItemScope Scope, Panel Root, string? Template);

    /// <summary>Attachment abstraction for a Repeat's generated item roots. The canonical container
    /// host emits each root into the owning Container's layout as its own cell; the legacy panel
    /// host retains the historical "one hidden panel per Repeat" behaviour for explicit Panel= and
    /// non-container parents. Both preserve stable keyed identity without clearing/re-adding.</summary>
    private interface ICuiRepeatHost
    {
        int Count { get; }
        int IndexOf(Control child);
        void Insert(int index, Control child);
        bool Remove(Control child);
        void Move(int fromIndex, int toIndex);
        /// <summary>Whether the layout the repeat items are placed into is a Grid (the only layout
        /// that supports per-item Row/Column placement).</summary>
        bool IsGrid { get; }
    }

    private sealed class LegacyPanelRepeatHost : ICuiRepeatHost
    {
        private readonly Panel _panel;
        public LegacyPanelRepeatHost(Panel panel) => _panel = panel;
        public int Count => _panel.Children.Count;
        public int IndexOf(Control child) => _panel.Children.IndexOf(child);
        public void Insert(int index, Control child) => _panel.Children.Insert(index, child);
        public bool Remove(Control child) => _panel.Children.Remove(child);
        public void Move(int from, int to) => _panel.Children.Move(from, to);
        public bool IsGrid => _panel is Grid;
    }

    private sealed class ContainerCellRepeatHost : ICuiRepeatHost
    {
        private readonly CuiContainerControl _container;
        private readonly int _baseIndex;
        private readonly List<Control> _roots = [];
        public ContainerCellRepeatHost(CuiContainerControl container, int baseIndex)
        {
            _container = container;
            _baseIndex = baseIndex;
        }
        public int Count => _roots.Count;
        public int IndexOf(Control child) => _roots.IndexOf(child);
        public void Insert(int index, Control child)
        {
            _container.InsertAuthoredChild(_baseIndex + index, child);
            _roots.Insert(Math.Clamp(index, 0, _roots.Count), child);
        }
        public bool Remove(Control child)
        {
            var index = _roots.IndexOf(child);
            if (index < 0) return false;
            _container.RemoveChild(child);
            _roots.RemoveAt(index);
            return true;
        }
        public void Move(int from, int to)
        {
            if (from == to) return;
            _container.MoveAuthoredChild(_baseIndex + from, _baseIndex + to);
            var root = _roots[from];
            _roots.RemoveAt(from);
            _roots.Insert(to, root);
        }
        public bool IsGrid => _container.Layout is Grid;
    }

    /// <summary>Sentinel returned by a canonical container-mode LoadRepeat. It is never attached to
    /// the visual tree and carries no layout/visual/input effect; it only signals AddChild to skip
    /// adding a (already-attached) repeat range as a single cell.</summary>
    private sealed class CuiRepeatFragment : Avalonia.Controls.Decorator
    {
        public CuiRepeatState State = null!;
    }

    private sealed class CuiRepeatState : IDisposable
    {
        private object? _source;
        private System.Collections.Specialized.INotifyCollectionChanged? _observableSource;
        private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _handler;

        public CuiRepeatState(ICuiRepeatHost host, CuiComponent component, int layer, ICuiBindingContext? context, string? identityPrefix, string themeName, Control? hostControl)
        {
            Host = host;
            HostControl = hostControl;
            Component = component;
            Layer = layer; Context = context; IdentityPrefix = identityPrefix; ThemeName = themeName;
        }

        public ICuiBindingContext? Context { get; }
        public string? IdentityPrefix { get; }
        public string ThemeName { get; }
        public ICuiRepeatHost Host { get; }
        /// <summary>The physical visual anchor (the owning Container for canonical mode, the legacy
        /// panel otherwise) used for subtree teardown membership checks.</summary>
        public Control? HostControl { get; }
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

    private sealed class RepeatItemScope : ICuiWritableBindingContext, ICuiRepeatItemBindingContext, INotifyPropertyChanged
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
            value = null;
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

        public bool TryGetItemValue(object item, string path, out object? value)
        {
            value = null;
            return _parent is ICuiRepeatItemBindingContext provider && provider.TryGetItemValue(item, path, out value);
        }

        public bool TrySetItemValue(object item, string path, object? value) =>
            _parent is ICuiRepeatItemBindingContext provider && provider.TrySetItemValue(item, path, value);

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

