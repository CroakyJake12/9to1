using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Themes;

namespace CakeOS.Cui.Runtime;

public enum CuiChoiceDecision { Primary, Secondary, Cancel }

/// <summary>All callbacks are supplied before any UI load. They belong to the
/// caller's actual operation; a decision creates no permission or account grant.</summary>
public sealed record CuiChoicePromptOptions(string Title, string Description,
    string PrimaryLabel, string SecondaryLabel, string CancelLabel,
    Func<Task> Primary, Func<Task> Secondary, Func<Task> Cancel);

/// <summary>One canonical, awaitable choice mounted in an existing empty overlay
/// host. The caller retains this object before ShowAsync and joins CloseAsync
/// outside its callbacks before releasing their original owners.</summary>
public sealed class CuiChoicePrompt : ICuiBindingContext, ICuiActionDispatcher,
    INotifyPropertyChanged, IDisposable, IAsyncDisposable
{
    private const string ResourceName = "CakeOS.Cui.ChoicePrompt.cui";
    private static readonly AsyncLocal<CuiChoicePrompt?> ExecutingCallback = new();
    private readonly CuiChoicePromptOptions _options;
    private readonly string _surface;
    private readonly CuiThemePreferenceContext? _homeTheme;
    private readonly TaskCompletionSource<CuiChoiceDecision> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Exception> _failures = [];
    private ContentControl? _parent;
    private Control? _root, _returnFocus;
    private Button? _cancelButton;
    private CuiControlLoader? _loader;
    private ResourceDictionary? _resources;
    private CancellationTokenRegistration _ownerCancellation;
    private Task? _operation, _close;
    private CuiChoiceDecision? _decision;
    private bool _shown, _closing, _detached;
    private int _withdrawalRequested;
    private string _status = "";
    private double _maxHeight = 720;

    public CuiChoicePrompt(CuiChoicePromptOptions options, string surface,
        CuiThemePreferenceContext? currentHomeTheme = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.PrimaryLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SecondaryLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.CancelLabel);
        ArgumentNullException.ThrowIfNull(options.Primary);
        ArgumentNullException.ThrowIfNull(options.Secondary);
        ArgumentNullException.ThrowIfNull(options.Cancel);
        ArgumentException.ThrowIfNullOrWhiteSpace(surface);
        _surface = surface; _homeTheme = currentHomeTheme;
    }

    public Task<CuiChoiceDecision> Completion => _completion.Task;
    public Task? OriginalClose => _close;
    public event PropertyChangedEventHandler? PropertyChanged;

    public Task<CuiChoiceDecision> ShowAsync(ContentControl actualOverlayHost, CancellationToken originalOwnerLifetime)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(actualOverlayHost);
        if (_shown || _closing) throw new InvalidOperationException("This choice prompt has already been shown or closed.");
        if (actualOverlayHost.Content is not null) throw new InvalidOperationException("The existing overlay host is occupied.");
        _shown = true; _parent = actualOverlayHost;
        try
        {
            _ownerCancellation = originalOwnerLifetime.Register(static owner => ((CuiChoicePrompt)owner!).RequestWithdrawal(), this);
            if (Volatile.Read(ref _withdrawalRequested) != 0) { BeginChoice(CuiChoiceDecision.Cancel); return Completion; }
            var appearance = CuiThemeScopeApplier.DetectAppearance();
            _resources = CuiSceneVisualResources.Create(_surface, appearance);
            var loader = _loader = new CuiControlLoader();
            loader.SetSurface(_surface); loader.SetAppearance(appearance);
            if (_homeTheme is not null) loader.SetThemePreferenceContext(_homeTheme);
            loader.SetBindingContext(this); loader.SetActionDispatcher(this);
            using var stream = typeof(CuiChoicePrompt).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidDataException("The canonical choice prompt resource is missing.");
            using var reader = new StreamReader(stream);
            var parser = new CuiRichParser(); var document = parser.Parse(reader.ReadToEnd(), "ChoicePrompt.cui");
            if (parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("The canonical choice prompt document is invalid.");
            var loaded = loader.TryLoad(document); _root = loaded.Root;
            if (_root is null || loaded.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("The canonical choice prompt could not be rendered.");
            loader.WireBindings(_root);
            _root.Resources.MergedDictionaries.Add(_resources);
            _root.SetValue(ThemeVariantScope.RequestedThemeVariantProperty, CuiSceneVisualResources.Variant(appearance));
            KeyboardNavigation.SetTabNavigation(_root, KeyboardNavigationMode.Cycle);
            _root.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            _root.DetachedFromVisualTree += OnDetached;
            _root.SizeChanged += OnSizeChanged;
            _cancelButton = loader.FindAuthoredControl<Button>(_root, "choice-cancel");
            var top = TopLevel.GetTopLevel(actualOverlayHost);
            _returnFocus = top?.FocusManager?.GetFocusedElement() as Control;
            if (Volatile.Read(ref _withdrawalRequested) != 0) { BeginChoice(CuiChoiceDecision.Cancel); return Completion; }
            actualOverlayHost.Content = _root;
            ScheduleFocus(top);
        }
        catch (Exception error)
        {
            _failures.Add(error);
            _completion.TrySetException(error);
        }
        return Completion;
    }

    private void ScheduleFocus(TopLevel? originalTop)
    {
        var previous = _returnFocus;
        Dispatcher.UIThread.Post(() =>
        {
            if (_closing || _decision is not null || _root is null || originalTop is null ||
                TopLevel.GetTopLevel(_root) != originalTop || _cancelButton is not { IsEffectivelyEnabled: true }) return;
            var current = originalTop.FocusManager?.GetFocusedElement();
            if (current is not null && !ReferenceEquals(current, previous)) return;
            // Cancellation is the safe initial keyboard focus; Enter cannot
            // silently opt into Memory or discard an unsaved conversation.
            _cancelButton.Focus(NavigationMethod.Unspecified);
        }, DispatcherPriority.Loaded);
    }
    private void OnSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (_root is null || _root.Bounds.Height <= 0) return;
        var next = Math.Max(44, _root.Bounds.Height - 32);
        if (Math.Abs(next - _maxHeight) < .5) return;
        _maxHeight = next; Changed();
    }
    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (_closing || args.Handled || args.Key != Key.Escape || args.KeyModifiers != KeyModifiers.None || _root is null) return;
        var focused = TopLevel.GetTopLevel(_root)?.FocusManager?.GetFocusedElement() as Control;
        if (focused is null || !_root.IsVisualAncestorOf(focused)) return;
        args.Handled = true;
        if (_cancelButton is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true })
            _cancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (_closing) return;
        _detached = true; RequestWithdrawal();
    }
    private void RequestWithdrawal()
    {
        Interlocked.Exchange(ref _withdrawalRequested, 1);
        Dispatcher.UIThread.Post(() =>
        {
            if (_closing || _completion.Task.IsCompleted || _decision is not null) return;
            BeginChoice(CuiChoiceDecision.Cancel);
        });
    }

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "prompt.title" => _options.Title, "prompt.description" => _options.Description,
            "prompt.primary" => _options.PrimaryLabel, "prompt.secondary" => _options.SecondaryLabel,
            "prompt.cancel" => _options.CancelLabel, "prompt.status" => _status,
            "prompt.canChoose" => !_closing && !_detached && _decision is null && Volatile.Read(ref _withdrawalRequested) == 0,
            "prompt.maxHeight" => _maxHeight, _ => null
        };
        return value is not null;
    }
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        if (_closing) return ValueTask.FromException(new ObjectDisposedException(nameof(CuiChoicePrompt)));
        if (!_shown || _root is null || _parent is null || !ReferenceEquals(_parent.Content, _root) ||
            TopLevel.GetTopLevel(_root) is null)
            return ValueTask.FromException(new InvalidOperationException("This choice is not mounted in its original overlay host."));
        var decision = command switch
        {
            "Primary" => CuiChoiceDecision.Primary, "Secondary" => CuiChoiceDecision.Secondary,
            "Cancel" => CuiChoiceDecision.Cancel, _ => throw new InvalidOperationException("Unknown prompt choice.")
        };
        if (Volatile.Read(ref _withdrawalRequested) != 0) decision = CuiChoiceDecision.Cancel;
        return new(BeginChoice(decision));
    }
    private Task BeginChoice(CuiChoiceDecision decision)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_operation is not null) return _operation;
        if (_completion.Task.IsCompleted) return Task.CompletedTask;
        _decision = decision;
        var start = new TaskCompletionSource();
        _operation = InvokeChoiceAsync(start.Task, decision); // Retain before the external callback.
        try { Changed(); }
        catch (Exception error) { _failures.Add(error); }
        finally { start.SetResult(); }
        return _operation;
    }
    private async Task InvokeChoiceAsync(Task start, CuiChoiceDecision decision)
    {
        await start;
        Task? actual = null;
        var previous = ExecutingCallback.Value;
        try
        {
            ExecutingCallback.Value = this;
            try
            {
                actual = (decision switch { CuiChoiceDecision.Primary => _options.Primary, CuiChoiceDecision.Secondary => _options.Secondary, _ => _options.Cancel })()
                    ?? throw new InvalidOperationException("The owning choice callback returned no operation.");
            }
            catch (OperationCanceledException synchronous)
            { throw new AggregateException("The choice owner faulted synchronously without a canceled source Task.", synchronous); }
            await actual;
            _completion.TrySetResult(decision);
        }
        catch (Exception error)
        {
            if (actual?.IsCanceled == true) _completion.TrySetCanceled();
            else
            {
                var original = actual?.Exception ?? error;
                _failures.Add(original); _completion.TrySetException(original);
                _status = "This choice could not complete. Your owning workflow has the failure details.";
                if (!_closing) Changed();
            }
            if (actual?.IsFaulted == true) throw actual.Exception!;
            throw;
        }
        finally { ExecutingCallback.Value = previous; }
    }
    private void Changed() => PropertyChanged?.Invoke(this, new(null));

    public Task CloseAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        // A copied ExecutionContext can outlive the original callback. Refuse
        // only an actual live self-join; completed originals still permit cleanup.
        if (ReferenceEquals(ExecutingCallback.Value, this) && _operation is { IsCompleted: false })
            throw new InvalidOperationException("Join the choice prompt close outside its original choice callback.");
        if (_close is not null) return _close;
        var start = new TaskCompletionSource();
        _closing = true;
        _close = CloseCoreAsync(start.Task); // Retain close before cancellation can call an owner.
        try { Changed(); }
        catch (Exception error) { _failures.Add(error); }
        finally { start.SetResult(); }
        return _close;
    }
    private async Task CloseCoreAsync(Task start)
    {
        await start;
        var errors = new List<Exception>(_failures);
        if (_operation is null && !_completion.Task.IsCompleted) _ = BeginChoice(CuiChoiceDecision.Cancel); // Retained in _operation and joined below.
        if (_operation is { } operation) await Join(operation, errors);
        try { _ownerCancellation.Dispose(); } catch (Exception error) { errors.Add(error); }
        var top = _root is null ? null : TopLevel.GetTopLevel(_root);
        var focus = top?.FocusManager?.GetFocusedElement() as Control;
        // Choosing disables the original buttons and can clear their focus before
        // close starts. An empty focus is still ours to restore only while this
        // exact prompt remains mounted; never override a replacement or another
        // control the user has focused in the meantime.
        var restore = _root is not null && !_detached && ReferenceEquals(_parent?.Content, _root)
            && (focus is null || _root.IsVisualAncestorOf(focus));
        if (_root is { } root)
        {
            Try(() => root.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown), errors);
            Try(() => root.DetachedFromVisualTree -= OnDetached, errors);
            Try(() => root.SizeChanged -= OnSizeChanged, errors);
        }
        if (_loader is { } loader)
        {
            try { loader.Dispose(); } catch (Exception error) { errors.Add(error); }
            Task? idle = null;
            Try(() => idle = loader.WhenActionsIdleAsync(), errors);
            if (idle is not null) await Join(idle, errors);
        }
        // Never clear another owner's replacement overlay.
        if (_parent is not null && ReferenceEquals(_parent.Content, _root)) Try(() => _parent.Content = null, errors);
        if (_resources is not null && _root is not null) Try(() => _root.Resources.MergedDictionaries.Remove(_resources), errors);
        if (restore && _returnFocus is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true, Focusable: true } target && TopLevel.GetTopLevel(target) == top)
        {
            var current = top?.FocusManager?.GetFocusedElement();
            if (current is null || ReferenceEquals(current, focus)) Try(() => target.Focus(NavigationMethod.Unspecified), errors);
        }
        _root = null; _cancelButton = null; _returnFocus = null;
        if (errors.Count > 0) throw new AggregateException(errors.Distinct<Exception>(ReferenceEqualityComparer.Instance));
    }
    private static async Task Join(Task actual, List<Exception> errors)
    {
        try { await actual; }
        catch (Exception error)
        {
            if (actual.IsFaulted) errors.AddRange(actual.Exception!.InnerExceptions);
            else errors.Add(error);
        }
    }
    private static void Try(Action operation, List<Exception> errors)
    {
        try { operation(); } catch (Exception error) { errors.Add(error); }
    }
    public void Dispose() => _ = CloseAsync();
    public ValueTask DisposeAsync() => new(CloseAsync());
}
