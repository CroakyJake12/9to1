using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Language;

namespace CakeOS.Cui.Runtime;

public enum CuiSceneAvailabilityState { Ready, Degraded, Unavailable }
public sealed record CuiSceneAvailability(CuiSceneAvailabilityState State, string Code, string Message);

/// <summary>Native host performs the real Home compatibility, authentication and required service handshake here.</summary>
public interface ICuiSceneReadiness
{
    ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken);
}

public sealed record CuiNativeScene(string AppId, string Title, string Surface, CuiDocument Document,
    ICuiBindingContext Bindings, ICuiActionDispatcher Actions, ICuiSceneReadiness Readiness);

/// <summary>Retained canonical CUI scene adapter, shared by native app windows and embedded Desktop surfaces.</summary>
public sealed class CuiSceneHost(CuiControlRegistry? registry = null) : ContentControl, IDisposable
{
    private readonly CuiControlRegistry _registry = registry ?? new();
    private CuiControlLoader? _loader;
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
            var next = new CuiControlLoader(_registry);
            next.SetSurface(scene.Surface);
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
            _loader = next;
            Content = loaded.Root;
            Diagnostics = loaded.Diagnostics;
            Availability = availability;
            old?.Dispose();
        });
        return availability;
    }

    public static async Task<Window> CreateWindowAsync(CuiNativeScene scene, double width = 1100, double height = 760,
        CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        var host = new CuiSceneHost();
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
        _loader = null;
        _lifetime.Dispose();
    }
}
