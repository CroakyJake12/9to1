using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Stacks.NativeUI;

/// <summary>Mounted by the existing authenticated host graph. This control owns no Home store,
/// Files registration, actor authentication, write permission or process execution service.</summary>
public sealed class StackNativeBrowserSurface : UserControl, IDisposable, ICuiActionDispatcher, ICuiActionAvailability
{
    private readonly StackCuiController _controller;
    private readonly ICuiSceneReadiness _readiness;
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationTokenRegistration _hostClosed;
    private CuiSceneHost? _scene;
    private bool _initialized, _disposed;

    public StackNativeBrowserSurface(IStackNativeProjectSource source, ICuiSceneReadiness readiness,
        CancellationToken authenticatedHostLifetime)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(readiness);
        _controller = new(source); _readiness = readiness;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(authenticatedHostLifetime);
        _hostClosed = authenticatedHostLifetime.Register(() => Dispatcher.UIThread.Post(Dispose));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) throw new InvalidOperationException("The original Stacks surface is already mounted.");
        _initialized = true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            await DemandReadyAsync(linked.Token);
            await _controller.DispatchAsync("RefreshProjects", null, linked.Token);
            await DemandReadyAsync(linked.Token);
            var scene = new CuiSceneHost();
            _scene = scene;
            var availability = await scene.ShowAsync(StackNativeScene.Create(_controller, _readiness) with { Actions = this }, linked.Token);
            if (availability.State != CuiSceneAvailabilityState.Ready)
                throw new UnauthorizedAccessException(availability.Message);
            await DemandReadyAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            Content = scene;
        }
        catch { Dispose(); throw; }
    }

    public bool? IsActionAvailable(string command) =>
        !_disposed && !_lifetime.IsCancellationRequested && _scene is not null
            ? _controller.IsActionAvailable(command) : false;

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            await DemandReadyAsync(linked.Token);
            await _controller.DispatchAsync(command, parameter, linked.Token);
            await DemandReadyAsync(linked.Token);
        }
        catch (UnauthorizedAccessException) { Dispose(); throw; }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { Dispose(); throw; }
    }

    /// <summary>Owning host focus refresh rechecks canonical read bindings without granting any edit.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await DispatchAsync("RefreshProjects", null, cancellationToken);
        if (_controller.IsActionAvailable("RefreshProject") == true)
            await DispatchAsync("RefreshProject", null, cancellationToken);
    }

    private async Task DemandReadyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _readiness.CheckAsync(cancellationToken);
        if (result.State != CuiSceneAvailabilityState.Ready)
            throw new UnauthorizedAccessException(result.Message);
    }

    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        Content = null;
        _scene?.Dispose(); _scene = null;
        _hostClosed.Dispose();
        _lifetime.Dispose();
    }
}
