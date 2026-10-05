using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Language;
using CakeOS.Cui.Themes;

namespace CakeOS.Cui.Runtime;

public enum CuiSceneAvailabilityState { Ready, Degraded, Unavailable }
public sealed record CuiSceneAvailability(CuiSceneAvailabilityState State, string Code, string Message);

/// <summary>Native host performs the real Home compatibility, authentication and required service handshake here.</summary>
public interface ICuiSceneReadiness
{
    ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken);
}

public sealed record CuiNativeScene(string AppId, string Title, string Surface, CuiDocument Document,
    ICuiBindingContext Bindings, ICuiActionDispatcher Actions, ICuiSceneReadiness Readiness)
{
    // Supplied by the host's canonical profile/theme authority; null inherits the current canonical appearance.
    public CuiAppearance? Appearance { get; init; }
    /// <summary>Trusted in-process owning app factories for typed native Object controls.</summary>
    public CuiControlRegistry? ControlRegistry { get; init; }
    /// <summary>Optional synchronous native-owner observation for each publication notification.
    /// False retires this attempt; neither true nor absence grants authority or establishes readiness.
    /// The callback must not await, acquire owner locks, or publish UI. Predicate errors remain original failures.</summary>
    public Func<bool>? IsPublicationCurrent { get; init; }
}

/// <summary>Retained canonical CUI scene adapter, shared by native app windows and embedded Desktop surfaces.</summary>
public sealed class CuiSceneHost(CuiControlRegistry? registry = null) : ContentControl, IDisposable
{
    private readonly CuiControlRegistry _registry = registry ?? new();
    private CuiControlLoader? _loader;
    private CuiControlLoader? _failureLoader;
    private CuiViewModel? _failureModel;
    public CuiActionFailure? LastActionFailure { get; private set; }
    private Avalonia.Controls.ResourceDictionary? _visualResources;
    private readonly CancellationTokenSource _lifetime = new();
    private long _generation;
    private bool _disposed;
    public CuiSceneAvailability? Availability { get; private set; }
    public IReadOnlyList<CuiDiagnostic> Diagnostics { get; private set; } = [];


    private const int MaximumOriginalPublications = 128;
    private sealed class OriginalPublication
    {
        internal Task<CuiSceneAvailability>? Show;
        internal CuiControlLoader? Loader;
        internal Control? Root;
        internal Avalonia.Controls.ResourceDictionary? Resources;
        internal Task? Actions;
        internal Task? TerminalActions;
        internal bool Retired;
        internal bool DisposeAttempted;
        internal List<Exception> Errors { get; } = [];
        internal Exception[] ReportedShowErrors = []; // Immutable original-cause refs already reported by this Show.
    }

    private readonly object _originalGate = new();
    private readonly List<OriginalPublication> _originalPublications = [];
    private int _ownerMode; // 0 fresh, 1 legacy-only, 2 optional guarded owner.
    private Task? _originalClose;
    private Exception? _capacityRefusal;
    private OperationCanceledException? _ownerClosedRefusal;
    private CuiControlLoader? _contentLoader;
    private Control? _contentRoot;

    /// <summary>The SAME optional-owner close, once requested. Completion is settlement, not action success.</summary>
    public Task? OriginalClose { get { lock (_originalGate) return _originalClose; } }

    public Task<CuiSceneAvailability> ShowAsync(CuiNativeScene scene, CancellationToken cancellationToken = default)
    {
        var predicate = scene?.IsPublicationCurrent;
        if (predicate is null)
        {
            lock (_originalGate)
            {
                if (_ownerMode == 2)
                    return Task.FromException<CuiSceneAvailability>(new InvalidOperationException(
                        "An optional guarded owner requires the same guarded lifetime for every scene."));
                if (scene is not null) _ownerMode = 1;
            }
            return ShowCoreAsync(scene!, null, null, cancellationToken); // Original legacy path.
        }

        var start = new TaskCompletionSource(); // Publish before any readiness/native callback.
        OriginalPublication publication;
        Task<CuiSceneAvailability> original;
        lock (_originalGate)
        {
            if (_ownerMode == 1 || _originalClose is not null || _disposed)
                return Task.FromException<CuiSceneAvailability>(new InvalidOperationException(
                    "Optional guarded publication requires a fresh, open host."));
            if (_originalPublications.Count >= MaximumOriginalPublications)
                return Task.FromException<CuiSceneAvailability>(OriginalCapacityRefusal());
            _ownerMode = 2;
            publication = new OriginalPublication();
            original = publication.Show = RunOriginalShowAsync(start.Task, publication, scene!, predicate, cancellationToken);
            _originalPublications.Add(publication);
        }
        start.SetResult();
        return original;
    }

    private async Task<CuiSceneAvailability> RunOriginalShowAsync(Task start, OriginalPublication publication,
        CuiNativeScene scene, Func<bool> predicate, CancellationToken cancellationToken)
    {
        await start.ConfigureAwait(false);
        return await ShowCoreAsync(scene, publication, predicate, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Seals optional-owner admission and publishes one original close before cancellation.
    /// The native owner MUST join this outside accepted load/action dependencies, while its dispatcher lives.
    /// Guarded Show retains retirement; it never joins the action that may be awaiting that same Show.
    /// Unsupported loader observer/partial-disposal outcomes remain explicit; this is no success witness.
    /// Legacy-only hosts continue to use their original Dispose contract.
    /// </summary>
    public Task CloseOriginalAsync()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task original;
        lock (_originalGate)
        {
            if (_originalClose is not null) return _originalClose;
            if (_ownerMode == 1)
                return Task.FromException(new InvalidOperationException("Legacy-only hosts retain the original Dispose contract."));
            _ownerMode = 2;
            _ownerClosedRefusal ??= new OperationCanceledException("The original CUI owner closed.");
            original = _originalClose = CloseOriginalCoreAsync(start.Task);
        }
        start.SetResult();
        return original;
    }

    private async Task CloseOriginalCoreAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var errors = new List<Exception>();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _disposed = true;
                Interlocked.Increment(ref _generation);
                OriginalPublication[] admitted;
                lock (_originalGate) admitted = _originalPublications.ToArray();
                foreach (var publication in admitted) CaptureRetirement(publication, errors);
                AttemptOriginal(errors, _lifetime.Cancel);
                foreach (var publication in admitted) RetireOriginal(publication, errors);
            });
        }
        catch (Exception error) { AddOriginal(errors, error); }

        OriginalPublication[] originals;
        lock (_originalGate) originals = _originalPublications.ToArray();
        foreach (var publication in originals)
        {
            if (publication.Show is null) continue;
            try { await publication.Show.ConfigureAwait(false); }
            catch (Exception error) { AddOriginal(errors, error); }
        }
        // All admitted Show work has settled. No new loader/banner admission is permitted.
        // A pending readiness/action that ignores cancellation remains a genuinely pending close.
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var publication in originals) RetireOriginal(publication, errors);
            });
        }
        catch (Exception error) { AddOriginal(errors, error); }
        foreach (var publication in originals)
        {
            if (publication.Actions is { } pipeline)
                await JoinOriginalTaskAsync(pipeline, errors).ConfigureAwait(false);
            if (publication.TerminalActions is { } terminal)
                await JoinOriginalTaskAsync(terminal, errors).ConfigureAwait(false);
            foreach (var error in publication.Errors)
                if (!publication.ReportedShowErrors.Any(reported => ReferenceEquals(reported, error)))
                    AddOriginal(errors, error);
        }
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Sealed ownership, SAME assigned root and loader: no arbitrary Content alias cleanup.
                if (_contentLoader is not null && ReferenceEquals(Content, _contentRoot))
                    AttemptOriginal(errors, () => Content = null);
                _contentLoader = null; _contentRoot = null;
                _loader = null; _failureLoader = null; _failureModel = null;
                foreach (var publication in originals)
                {
                    if (publication.Resources is { } resources)
                        AttemptOriginal(errors, () => Resources.MergedDictionaries.Remove(resources));
                }
                _visualResources = null;
                AttemptOriginal(errors, _lifetime.Dispose);
            });
        }
        catch (Exception error) { AddOriginal(errors, error); }
        ThrowOriginal(errors);
    }

    private OriginalPublication AdmitOriginalBanner()
    {
        lock (_originalGate)
        {
            if (_originalClose is not null || _disposed)
                throw _ownerClosedRefusal ??= new OperationCanceledException("The original CUI owner closed.");
            if (_originalPublications.Count >= MaximumOriginalPublications)
                throw OriginalCapacityRefusal();
            var publication = new OriginalPublication();
            _originalPublications.Add(publication); // Custody BEFORE factory/load callbacks.
            return publication;
        }
    }

    // Called under _originalGate only AFTER checking the actual unchanged capacity boundary.
    // A stable refusal object bounds repeated refused-observation custody; it is no currentness grant.
    private Exception OriginalCapacityRefusal() => _capacityRefusal ??= new InvalidOperationException(
        "Join the original owner close before admitting more retained CUI publications.");

    private static void CaptureRetirement(OriginalPublication publication, List<Exception> errors)
    {
        if (publication.Retired || publication.Loader is not { } loader) return;
        publication.Retired = true;
        // Actual accepted pipeline snapshot, retained persistently before any owning cancellation.
        AttemptOriginal(errors, () => publication.Actions = loader.WhenActionsIdleAsync());
    }

    private static void RetireOriginal(OriginalPublication publication, List<Exception> errors)
    {
        CaptureRetirement(publication, errors);
        if (publication.DisposeAttempted || publication.Loader is not { } loader) return;
        publication.DisposeAttempted = true; // One original close attempt before callbacks.
        AttemptOriginal(errors, loader.Dispose);
        // Dispose publishes its terminal completion before cancellation/cleanup callbacks.
        // Capture even after a synchronous failure; the earlier action snapshot predates it.
        AttemptOriginal(errors, () => publication.TerminalActions = loader.WhenActionsIdleAsync());
    }

    private OriginalPublication? PublicationFor(CuiControlLoader? loader)
    {
        if (loader is null) return null;
        lock (_originalGate)
            return _originalPublications.FirstOrDefault(publication => ReferenceEquals(publication.Loader, loader));
    }

    private async Task<CuiSceneAvailability> ShowCoreAsync(CuiNativeScene scene, OriginalPublication? publication,
        Func<bool>? isPublicationCurrent, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(scene.AppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scene.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(scene.Surface);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        var generation = Interlocked.Increment(ref _generation);
        var availability = await scene.Readiness.CheckAsync(request.Token).ConfigureAwait(false);
        if (!Enum.IsDefined(availability.State) || string.IsNullOrWhiteSpace(availability.Code) || string.IsNullOrWhiteSpace(availability.Message))
            throw new InvalidOperationException("The scene readiness handshake returned an invalid result.");
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            request.Token.ThrowIfCancellationRequested();
            if (isPublicationCurrent is not null)
            {
                ShowGuarded(scene, availability, generation, publication!, isPublicationCurrent, request.Token);
                return;
            }
            if (_disposed || generation != Interlocked.Read(ref _generation)) return;
            var effectiveAppearance = scene.Appearance ?? CuiThemeScopeApplier.DetectAppearance();
            var visualResources = CuiSceneVisualResources.Create(scene.Surface, effectiveAppearance);
            if (_visualResources is not null) Resources.MergedDictionaries.Remove(_visualResources);
            Resources.MergedDictionaries.Add(visualResources);
            _visualResources = visualResources;
            SetValue(ThemeVariantScope.RequestedThemeVariantProperty, CuiSceneVisualResources.Variant(effectiveAppearance));
            Background = (Avalonia.Media.IBrush?)visualResources["CuiBackgroundBrush"];
            Foreground = (Avalonia.Media.IBrush?)visualResources["CuiTextBrush"];
            var next = new CuiControlLoader(_registry);
            next.SetSurface(scene.Surface);
            next.SetAppearance(effectiveAppearance);
            if (availability.State == CuiSceneAvailabilityState.Ready)
            {
                next.SetBindingContext(scene.Bindings);
                next.SetActionDispatcher(scene.Actions);
            }
            else
            {
                var status = new CuiViewModel();
                status.Set("AvailabilityMessage", availability.Message);
                next.SetBindingContext(status);
                next.SetActionDispatcher(status);
            }
            var document = availability.State == CuiSceneAvailabilityState.Ready ? scene.Document :
                new CuiRichParser().Parse("<Cui><StackPanel><TextBlock id=\"scene-availability\" text=\"{Binding AvailabilityMessage}\" accessible-name=\"Application availability\" /></StackPanel></Cui>");
            var loaded = next.TryLoad(document);
            if (loaded.Root is null || loaded.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            {
                next.Dispose();
                throw new InvalidDataException("The canonical CUI scene did not produce a usable root control.");
            }
            var old = _loader;
            next.WireBindings(loaded.Root);
            next.ActionFailed += (_, failure) => ShowActionFailure(next, failure);
            _failureLoader?.Dispose(); _failureLoader = null; _failureModel = null; LastActionFailure = null;
            _loader = next;
            Content = loaded.Root;
            Diagnostics = loaded.Diagnostics;
            Availability = availability;
            old?.Dispose();
        });
        return availability;
    }

    private void ShowActionFailure(CuiControlLoader sender, CuiActionFailure failure)
    {
        if (_disposed || !ReferenceEquals(sender, _loader)) return;
        LastActionFailure = failure;
        Diagnostics = Diagnostics.Append(new CuiDiagnostic(failure.Code, failure.Cancelled ? CuiDiagnosticSeverity.Info : CuiDiagnosticSeverity.Error,
            failure.Message, default)).ToArray();
        if (_failureModel is null)
        {
            _failureModel = new CuiViewModel();
            _failureLoader = new CuiControlLoader(_registry); _failureLoader.SetBindingContext(_failureModel);
            var banner = _failureLoader.Load(new CuiRichParser().Parse(
                "<Cui><TextBlock id=\"scene-action-status\" text=\"{Binding ActionStatus}\" text-wrapping=\"Wrap\" margin=\"12\" /></Cui>")) ?? throw new InvalidDataException("The action status CUI did not produce a control.");
            var original = Content as Control; Content = null;
            var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            layout.Children.Add(banner);
            if (original is not null) { Grid.SetRow(original, 1); layout.Children.Add(original); }
            Content = layout;
        }
        _failureModel.Set("ActionStatus", failure.Message);
    }



    private void ShowGuarded(CuiNativeScene scene, CuiSceneAvailability availability, long generation,
        OriginalPublication publication, Func<bool> isPublicationCurrent, CancellationToken cancellationToken)
    {
        CuiControlLoader? next = null;
        Control? root = null;
        Avalonia.Controls.ResourceDictionary? visualResources = null;
        OriginalPublication? previous = null;
        OriginalPublication? previousFailure = null;
        var transferred = false;
        var complete = false;
        var errors = publication.Errors;
        try
        {
            DemandPublicationCurrent(generation, isPublicationCurrent, cancellationToken);
            var appearance = scene.Appearance ?? CuiThemeScopeApplier.DetectAppearance();
            visualResources = publication.Resources = CuiSceneVisualResources.Create(scene.Surface, appearance);
            next = publication.Loader = new CuiControlLoader(_registry);
            next.SetSurface(scene.Surface);
            next.SetAppearance(appearance);
            if (availability.State == CuiSceneAvailabilityState.Ready)
            {
                next.SetBindingContext(scene.Bindings);
                next.SetActionDispatcher(scene.Actions);
            }
            else
            {
                var status = new CuiViewModel();
                status.Set("AvailabilityMessage", availability.Message);
                next.SetBindingContext(status);
                next.SetActionDispatcher(status);
            }
            DemandPublicationCurrent(generation, isPublicationCurrent, cancellationToken);
            var document = availability.State == CuiSceneAvailabilityState.Ready ? scene.Document :
                new CuiRichParser().Parse("<Cui><StackPanel><TextBlock id=\"scene-availability\" text=\"{Binding AvailabilityMessage}\" accessible-name=\"Application availability\" /></StackPanel></Cui>");
            var loaded = next.TryLoad(document);
            root = publication.Root = loaded.Root;
            if (root is null || loaded.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("The canonical CUI scene did not produce a usable root control.");
            DemandPublicationCurrent(generation, isPublicationCurrent, cancellationToken);
            next.WireBindings(root);
            DemandPublicationCurrent(generation, isPublicationCurrent, cancellationToken);
            var originalLoader = next;
            next.ActionFailed += (_, failure) => ShowGuardedActionFailure(originalLoader, failure, generation, isPublicationCurrent);
            if (_visualResources is { } oldResources)
                Publish(() => Resources.MergedDictionaries.Remove(oldResources));
            _visualResources = visualResources;
            Publish(() => Resources.MergedDictionaries.Add(visualResources));
            Publish(() => SetValue(ThemeVariantScope.RequestedThemeVariantProperty, CuiSceneVisualResources.Variant(appearance)));
            Publish(() => Background = (Avalonia.Media.IBrush?)visualResources["CuiBackgroundBrush"]);
            Publish(() => Foreground = (Avalonia.Media.IBrush?)visualResources["CuiTextBrush"]);

            previous = PublicationFor(_loader);
            previousFailure = PublicationFor(_failureLoader);
            _failureLoader = null; _failureModel = null; LastActionFailure = null;
            _loader = next;
            _contentLoader = next; _contentRoot = root; // Exact assigned Content custody before its notification.
            transferred = true;
            Publish(() => Content = root);
            Publish(() => Diagnostics = loaded.Diagnostics);
            Publish(() => Availability = availability);
            complete = true;
        }
        catch (Exception error) { AddOriginal(errors, error); }

        if (!complete)
        {
            var ownsContent = transferred && ReferenceEquals(_loader, next) &&
                ReferenceEquals(_contentLoader, next) && ReferenceEquals(Content, _contentRoot);
            if (ReferenceEquals(_loader, next)) _loader = null;
            if (ownsContent)
            {
                _contentLoader = null; _contentRoot = null;
                AttemptOriginal(errors, () => Content = null);
            }
            if (visualResources is not null)
            {
                if (ReferenceEquals(_visualResources, visualResources)) _visualResources = null;
                AttemptOriginal(errors, () => Resources.MergedDictionaries.Remove(visualResources));
            }
            RetireOriginal(publication, errors);
        }
        if (transferred)
        {
            if (previousFailure is not null) RetireOriginal(previousFailure, errors);
            if (previous is not null) RetireOriginal(previous, errors);
        }
        if (complete) AttemptOriginal(errors, () => DemandPublicationCurrent(generation, isPublicationCurrent, cancellationToken));
        publication.ReportedShowErrors = errors.ToArray(); // Preserve prefix without clearing later error custody.
        ThrowOriginal(publication.ReportedShowErrors.ToList());

        void Publish(Action write)
        {
            DemandPublicationCurrent(generation, isPublicationCurrent, cancellationToken);
            write();
            DemandPublicationCurrent(generation, isPublicationCurrent, cancellationToken);
        }
    }


    private void ShowGuardedActionFailure(CuiControlLoader sender, CuiActionFailure failure,
        long generation, Func<bool> isPublicationCurrent)
    {
        if (_disposed || !ReferenceEquals(sender, _loader)) return;
        lock (_originalGate)
            if (_originalClose is not null) return; // Sealed observer retires before any host history/model mutation.
        var originalOwner = PublicationFor(sender);
        OriginalPublication? publication = null;
        CuiControlLoader? acquired = null;
        CuiViewModel? model = null;
        Grid? layout = null;
        var complete = false;
        var errors = new List<Exception>();
        try
        {
            publication = AdmitOriginalBanner();
            errors = publication.Errors;
            DemandPublicationCurrent(generation, isPublicationCurrent, CancellationToken.None);
            Publish(() => LastActionFailure = failure);
            Publish(() => Diagnostics = Diagnostics.Append(new CuiDiagnostic(failure.Code,
                failure.Cancelled ? CuiDiagnosticSeverity.Info : CuiDiagnosticSeverity.Error, failure.Message, default)).ToArray());
            model = _failureModel;
            if (model is null)
            {
                model = new CuiViewModel();
                acquired = publication.Loader = new CuiControlLoader(_registry);
                acquired.SetBindingContext(model);
                var banner = acquired.Load(new CuiRichParser().Parse(
                    "<Cui><TextBlock id=\"scene-action-status\" text=\"{Binding ActionStatus}\" text-wrapping=\"Wrap\" margin=\"12\" /></Cui>"))
                    ?? throw new InvalidDataException("The action status CUI did not produce a control.");
                DemandPublicationCurrent(generation, isPublicationCurrent, CancellationToken.None);
                var original = Content as Control;
                _failureLoader = acquired; _failureModel = model;
                Publish(() => Content = null);
                layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
                Publish(() => layout.Children.Add(banner));
                if (original is not null)
                {
                    Publish(() => Grid.SetRow(original, 1));
                    Publish(() => layout.Children.Add(original));
                }
                publication.Root = layout;
                _contentLoader = sender; _contentRoot = layout;
                Publish(() => Content = layout);
            }
            Publish(() => model.Set("ActionStatus", failure.Message));
            complete = true;
        }
        catch (Exception error)
        {
            AddOriginal(errors, error);
            if (publication is null && originalOwner is not null) AddOriginal(originalOwner.Errors, error);
        }

        if (!complete && acquired is not null)
        {
            var ownsContent = ReferenceEquals(_loader, sender) && ReferenceEquals(_failureLoader, acquired) &&
                ReferenceEquals(_contentLoader, sender) && layout is not null &&
                ReferenceEquals(Content, layout) && ReferenceEquals(_contentRoot, layout);
            if (ReferenceEquals(_failureLoader, acquired)) _failureLoader = null;
            if (ReferenceEquals(_failureModel, model)) _failureModel = null;
            if (ownsContent)
            {
                _contentLoader = null; _contentRoot = null;
                AttemptOriginal(errors, () => Content = null);
            }
            RetireOriginal(publication!, errors);
        }
        // Our own observer errors remain in persistent owner custody. The loader's safe diagnostics
        // still cannot establish complete original dispatcher/observer exception fidelity.
        ThrowOriginal(errors);

        void Publish(Action write)
        {
            DemandPublicationCurrent(generation, isPublicationCurrent, CancellationToken.None);
            write();
            DemandPublicationCurrent(generation, isPublicationCurrent, CancellationToken.None);
        }
    }

    private void DemandPublicationCurrent(long generation, Func<bool> isPublicationCurrent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_originalGate)
            if (_originalClose is not null) throw _ownerClosedRefusal!;
        if (_disposed || generation != Interlocked.Read(ref _generation))
            throw new OperationCanceledException("The original CUI scene publication retired.");
        var current = isPublicationCurrent(); // Synchronous owner observation; errors keep their original identity.
        cancellationToken.ThrowIfCancellationRequested();
        lock (_originalGate)
            if (_originalClose is not null) throw _ownerClosedRefusal!;
        if (!current || _disposed || generation != Interlocked.Read(ref _generation))
            throw new OperationCanceledException("The original CUI scene publication retired.");
    }

    private static async Task JoinOriginalTaskAsync(Task original, List<Exception> errors)
    {
        try { await original.ConfigureAwait(false); }
        catch (Exception error)
        {
            // Await selects one cause from WhenAll. Preserve every actual fault payload,
            // including a payload that is itself an empty/unknown AggregateException.
            if (original.Exception is { } fault && fault.InnerExceptions.Count != 0)
                foreach (var cause in fault.InnerExceptions) AddOriginal(errors, cause);
            else AddOriginal(errors, error);
        }
    }

    private static void AddOriginal(List<Exception> errors, Exception error)
    {
        if (!errors.Any(original => ReferenceEquals(original, error))) errors.Add(error);
    }

    private static void AttemptOriginal(List<Exception> errors, Action callback)
    {
        try { callback(); }
        catch (Exception error) { AddOriginal(errors, error); }
    }

    private static void ThrowOriginal(List<Exception> errors)
    {
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Original CUI publication and independent cleanup failed.", errors);
    }

    public static async Task<Window> CreateWindowAsync(CuiNativeScene scene, double width = 1100, double height = 760,
        CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(scene);
        if (scene.IsPublicationCurrent is not null)
            throw new InvalidOperationException("Optional guarded scenes require a fresh explicit host and an independently joined native-owner CloseOriginalAsync.");
        var host = new CuiSceneHost(scene.ControlRegistry);
        try { await host.ShowAsync(scene, cancellationToken); }
        catch { host.Dispose(); throw; }
        var window = new Window { Title = scene.Title, Width = width, Height = height, Content = host };
        window.Closed += (_, _) => host.Dispose();
        return window;
    }

    public void Dispose()
    {
        lock (_originalGate)
        {
            if (_ownerMode == 2)
            {
                // The actual task is retained in OriginalClose. Native owners MUST independently join it.
                CloseOriginalAsync();
                return;
            }
        }
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _generation);
        _lifetime.Cancel();
        _loader?.Dispose();
        _failureLoader?.Dispose(); _failureLoader = null; _failureModel = null;
        _loader = null;
        _lifetime.Dispose();
    }
}
