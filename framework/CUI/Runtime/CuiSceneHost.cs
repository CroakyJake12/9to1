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

    public async Task<CuiSceneAvailability> ShowAsync(CuiNativeScene scene, CancellationToken cancellationToken = default)
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

    public static async Task<Window> CreateWindowAsync(CuiNativeScene scene, double width = 1100, double height = 760,
        CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        var host = new CuiSceneHost(scene.ControlRegistry);
        try { await host.ShowAsync(scene, cancellationToken); }
        catch { host.Dispose(); throw; }
        var window = new Window { Title = scene.Title, Width = width, Height = height, Content = host };
        window.Closed += (_, _) => host.Dispose();
        return window;
    }

    public void Dispose()
    {
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
