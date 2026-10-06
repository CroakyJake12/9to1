using Avalonia.Automation;
using Avalonia.Threading;
using Avalonia.Controls;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Services;
using System.Runtime.ExceptionServices;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.HavenUI.GenerativeUi;
using Haven.UI;
using HavenInput = Haven.UI.Components.Input;
using HavenNativeHost = Haven.UI.Components.NativeHost;

namespace Haven.Desktop.Views.Pages.Chat;

internal sealed class ChatGenUiSurfaceMount : IDisposable, IAsyncDisposable
{
    private readonly ChatGenUiNativeControlResolver _nativeResolver;
    private readonly DesktopOriginalWorkLifetime _originalWork;
    private readonly Func<bool>? _originalPresentationCurrent;
    private readonly object _childGate = new();
    private readonly List<Task> _childCloses = [];
    private readonly List<Exception> _childStopFailures = [];
    private HavenGenUiSceneSurface? _sceneSurface;
    private GenerativeUiSurface? _nativeSurface;
    private readonly HavenNativeHost? _nativeHost;
    private bool _disposed;
    private long _generation;

    private ChatGenUiSurfaceMount(ChatGenUiNativeControlResolver nativeResolver, HavenNativeHost? nativeHost, Func<bool>? originalPresentationCurrent)
    {
        _originalPresentationCurrent = originalPresentationCurrent;
        _nativeResolver = nativeResolver;
        _nativeHost = nativeHost;
        Root = (HavenElement?)nativeHost ?? new Haven.UI.Components.Container(); // Unmounted until the original child acquisition returns.
        _originalWork = new DesktopOriginalWorkLifetime(StopChildrenAsync, CleanupChildrenAsync);
    }

    public HavenElement Root { get; private set; }
    public GenUiDocument? Document => _sceneSurface?.Document ?? _nativeSurface?.Document;
    public bool UsesNativeHost => _nativeSurface is not null;
    public Task? OriginalClose => _originalWork.OriginalClose;

    public static ChatGenUiSurfaceMount Create(GenUiRenderingDecision rendering, GenerativeUiEventRouter router,
        GenUiInstanceStore instances, ChatGenUiNativeControlResolver nativeResolver,
        Action<ChatGenUiSurfaceMount>? originalAcquired = null, Func<bool>? originalPresentationCurrent = null)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(nativeResolver);
        if (rendering.AllowsExecutableCode) throw new InvalidOperationException("Generated executable code cannot be mounted by Chat GenUI.");
        var native = rendering.Layer is GenUiRenderingLayer.Native or GenUiRenderingLayer.Composite;
        var mount = new ChatGenUiSurfaceMount(nativeResolver, native ? new HavenNativeHost() : null, originalPresentationCurrent);
        try
        {
            mount._originalWork.RunSynchronous(original =>
            {
                original.BindPublicationGuard(() => !mount._disposed && (mount._originalPresentationCurrent?.Invoke() ?? true));
                original.DemandPublication();
                originalAcquired?.Invoke(mount); // Retain SAME mount before child setters/subscriptions.
                original.DemandPublication();
                if (native)
                {
                    var host = mount._nativeHost!;
                    host.SetValue(HavenProperties.Width, HavenLength.Percent(100));
                    original.DemandPublication();
                    host.SetValue(HavenProperties.MinHeight, HavenLength.Px(180));
                    original.DemandPublication();
                    host.Accessibility.AccessibleName = "Generated native interface";
                    original.DemandPublication();
                    var surface = new GenerativeUiSurface(router, instances, () => !mount._disposed && (mount._originalPresentationCurrent?.Invoke() ?? true));
                    mount._nativeSurface = surface; // Before any property setters can notify.
                    if (mount._originalWork.IsRetiring) mount.CaptureChildStops();
                    original.DemandPublication();
                    surface.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
                    original.DemandPublication();
                    surface.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
                    original.DemandPublication();
                    surface.MinHeight = 180;
                    original.DemandPublication();
                    AutomationProperties.SetAutomationId(surface, "ChatGeneratedNativeSurface");
                    original.DemandPublication();
                    nativeResolver.Register(host, surface);
                    original.DemandPublication();
                }
                else
                {
                    mount._sceneSurface = new HavenGenUiSceneSurface(router, instances, () => !mount._disposed && (mount._originalPresentationCurrent?.Invoke() ?? true));
                    if (mount._originalWork.IsRetiring) mount.CaptureChildStops();
                    original.DemandPublication();
                    mount.Root = mount._sceneSurface.Root;
                    original.DemandPublication();
                }
            });
            return mount;
        }
        catch
        {
            // The acquired callback's page cohort owns the returned close, including
            // any child captured before failure. A legacy no-callback caller has
            // no returned acquisition proof on failure and must supply that callback.
            mount.RequestRetirement();
            throw;
        }
    }

    public void Present(GenUiDocument document) => PresentOriginal(document, existing: false);
    public void PresentExisting(GenUiDocument document) => PresentOriginal(document, existing: true);
    private void PresentOriginal(GenUiDocument document, bool existing)
    {
        ThrowIfDisposed();
        _originalWork.RunSynchronous(original =>
        {
            var generation = ++_generation;
            original.BindPublicationGuard(() => !_disposed && _generation == generation && (_originalPresentationCurrent?.Invoke() ?? true));
            original.DemandPublication();
            if (_sceneSurface is { } scene)
            { if (existing) scene.PresentExisting(document); else scene.Present(document); }
            else
            { if (existing) _nativeSurface!.PresentExisting(document); else _nativeSurface!.Present(document); }
            original.DemandPublication();
        });
    }

    public bool OwnsInput(HavenInput input) => !_disposed && _sceneSurface?.OwnsInput(input) == true;
    public Task SubmitInputAsync(HavenInput input, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var surface = _sceneSurface;
        var generation = _generation;
        return _originalWork.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => !_disposed && _generation == generation && ReferenceEquals(surface, _sceneSurface) && (_originalPresentationCurrent?.Invoke() ?? true));
            original.DemandPublication();
            if (surface is null) return;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken);
            await original.AwaitAsync(surface.SubmitInputAsync(input, linked.Token));
            original.DemandPublication();
        });
    }

    private void ThrowIfDisposed()
    {
        if (_disposed || _originalWork.IsRetiring) throw new ObjectDisposedException(nameof(ChatGenUiSurfaceMount));
    }
    public void RequestRetirement()
    {
        _originalWork.RequestRetirement();
        _disposed = true;
    }
    internal void DemandOriginalExternalClose()
    {
        _originalWork.DemandExternalClose();
        _nativeSurface?.DemandOriginalExternalClose();
        _sceneSurface?.DemandOriginalExternalClose();
    }
    public Task CloseAndDrainAsync()
    {
        DemandOriginalExternalClose();
        var close = _originalWork.CloseAndDrainAsync();
        _disposed = true;
        return close;
    }
    public void Dispose() => RequestRetirement();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private void CaptureChildStops()
    {
        void Capture(Action stop, Func<Task?> actualClose)
        {
            try { stop(); }
            catch (Exception cause) { lock (_childGate) Add(_childStopFailures, cause); }
            try
            {
                var close = actualClose() ?? throw new InvalidOperationException("The actual child retirement did not publish its original close.");
                lock (_childGate) if (!_childCloses.Any(item => ReferenceEquals(item, close))) _childCloses.Add(close);
            }
            catch (Exception cause) { lock (_childGate) Add(_childStopFailures, cause); }
        }
        if (_nativeSurface is { } native) Capture(native.RequestRetirement, () => native.OriginalClose);
        if (_sceneSurface is { } scene) Capture(scene.RequestRetirement, () => scene.OriginalClose);
    }
    private Task StopChildrenAsync()
    {
        _disposed = true;
        CaptureChildStops(); // Both actual stops start before mount originals are joined.
        return Task.CompletedTask;
    }
    private async Task CleanupChildrenAsync()
    {
        // Construction may have acquired a child after the earlier stop snapshot.
        // Mount originals are now terminal; capture and join that exact late child too.
        CaptureChildStops();
        Task[] closes;
        var failures = new List<Exception>();
        lock (_childGate)
        { closes = _childCloses.ToArray(); foreach (var cause in _childStopFailures) Add(failures, cause); }
        foreach (var close in closes)
        {
            try { await close; }
            catch (Exception cause)
            {
                if (close.Exception is { InnerExceptions.Count: > 0 } group)
                    foreach (var direct in group.InnerExceptions) Add(failures, direct);
                else Add(failures, cause);
            }
        }
        if (failures.Count != 0)
        {
            if (failures.Count > 1 || failures[0] is OperationCanceledException)
                throw new AggregateException("Original generated child stop or close failed; physical mount retained.", failures);
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        // Preserve physical mount/resolver until all actual child owners acknowledge
        // successful close. Borrowed router and instance store remain unowned.
        if (_nativeHost is not null && _nativeSurface is not null)
        {
            var actualDispatcher = Dispatcher.UIThread.InvokeAsync(() => _originalWork.RunCloseCallback(() =>
                _nativeResolver.Unregister(_nativeHost, _nativeSurface))).GetTask();
            try { await actualDispatcher; }
            catch (Exception cause)
            {
                var errors = actualDispatcher.Exception is { InnerExceptions.Count: > 0 } group
                    ? group.InnerExceptions.ToArray() : new[] { cause };
                if (errors.Length > 1 || errors[0] is OperationCanceledException)
                    throw new AggregateException("Original generated resolver cleanup failed.", errors);
                ExceptionDispatchInfo.Capture(errors[0]).Throw();
            }
        }
    }
    private static void Add(List<Exception> errors, Exception cause)
    { if (!errors.Any(item => ReferenceEquals(item, cause))) errors.Add(cause); }
}

internal sealed class ChatGenUiNativeControlResolver : IHavenAvaloniaNativeControlResolver
{
    private readonly Dictionary<HavenElement, Control> _controls = [];

    public void Register(HavenElement element, Control control)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(control);
        if (!_controls.TryAdd(element, control)) throw new InvalidOperationException("This generated native host is already registered.");
    }

    public void Unregister(HavenElement element) => _controls.Remove(element);

    public void Unregister(HavenElement element, Control originalControl)
    {
        if (!_controls.TryGetValue(element, out var current)) return;
        if (!ReferenceEquals(current, originalControl))
            throw new InvalidOperationException("The original generated host control was replaced; its owner cannot unregister a different control.");
        _controls.Remove(element);
    }

    public bool TryCreate(HavenElement element, out Control? control)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (_controls.TryGetValue(element, out var found))
        {
            control = found;
            return true;
        }
        control = null;
        return false;
    }
}
