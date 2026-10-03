using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.ViewModels;
using HavenOS.Apps.Terminal;
using HavenOS.Apps.Terminal.NativeUI;

namespace Haven.Desktop.Views.Pages.Terminal;

/// <summary>Shared host lifetime for one actual owning Terminal session, viewport and Home registry entry.</summary>
internal sealed class HomeTerminalCuiPage : UserControl, IActivatablePage, IDisposable
{
    private readonly TerminalAppSurface _surface;
    private readonly TerminalLocalEnvironmentAuthority _authority;
    private readonly TerminalLocalEnvironmentLease _lease;
    private readonly TerminalOwnedSessionRegistry _registry;
    private readonly ICuiSceneReadiness _readiness;
    private readonly LibVTermViewport _viewport = new();
    private readonly TerminalCuiWorkspace _workspace;
    private CuiSceneHost? _scene;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _observation = new() { Interval = TimeSpan.FromSeconds(1) };
    private ITerminalInteractiveSession? _registered;
    private bool _disposed;
    private bool _initialized;
    private int _observing;
    private bool _active;

    public HomeTerminalCuiPage(TerminalAppSurface surface, TerminalLocalEnvironmentAuthority authority,
        TerminalLocalEnvironmentLease lease, TerminalOwnedSessionRegistry registry, ICuiSceneReadiness readiness,
        Func<string, CancellationToken, Task> reviewPermissions)
    {
        _surface = surface; _authority = authority; _lease = lease; _registry = registry; _readiness = readiness;
        _workspace = new(surface, _viewport, reviewPermissions);
        _surface.MetadataChanged += OnMetadataChanged;
        _observation.Tick += OnObservation;
    }

    public TerminalSessionMetadata? SessionMetadata => _surface.SessionMetadata;
    public bool IsClosed => _disposed;
    public void FocusCommandLine()
    {
        if (!_disposed) this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Focus();
    }

    public async Task InitializeAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) throw new InvalidOperationException("This Terminal page has already been initialized.");
        _initialized = true;
        try
        {
            await ValidateAndSynchronizeAsync(token);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            var nativeScene = TerminalNativeScene.Create(_workspace, _readiness);
            _scene = new CuiSceneHost(nativeScene.ControlRegistry);
            var availability = await _scene.ShowAsync(nativeScene, linked.Token);
            if (availability.State != CuiSceneAvailabilityState.Ready)
                throw new UnauthorizedAccessException(availability.Message);
            await ValidateAndSynchronizeAsync(linked.Token);
            Content = _scene;
            _active = true; _observation.Start();
        }
        catch { Dispose(); throw; }
    }

    public async Task ActivateAsync(CancellationToken token)
    {
        if (_disposed) return;
        try { await ValidateAndSynchronizeAsync(token); _active = true; _observation.Start(); }
        catch (Exception error) when (Expected(error)) { Dispose(); }
    }
    public void Deactivate() { _active = false; _observation.Stop(); }

    private void OnObservation(object? sender, EventArgs e)
    {
        if (_active) RequestObservation();
    }
    private void OnMetadataChanged(object? sender, TerminalSessionMetadata metadata) => RequestObservation();
    private void RequestObservation()
    {
        if (_disposed || Interlocked.CompareExchange(ref _observing, 1, 0) != 0) return;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (!_disposed) await ValidateAndSynchronizeAsync(_lifetime.Token);
            }
            catch (Exception error) when (Expected(error)) { Dispose(); }
            finally { Volatile.Write(ref _observing, 0); }
        });
    }

    private async Task ValidateAndSynchronizeAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _serial.WaitAsync(linked.Token);
        try
        {
            await _authority.RequireCurrentAsync(_lease, linked.Token);
            if ((await _readiness.CheckAsync(linked.Token)).State != CuiSceneAvailabilityState.Ready)
                throw new UnauthorizedAccessException("Current Home readiness does not permit this Terminal view.");
            var current = _surface.InteractiveSession ?? throw new InvalidOperationException("The interactive Terminal session is unavailable.");
            if (!ReferenceEquals(_registered, current))
            {
                if (_registered is not null) _registry.Unregister(_registered);
                _registered = null;
                await _registry.RegisterAsync(current, linked.Token);
                // No changed actor or replacement session can retain a newly registered entry.
                try
                {
                    await _authority.RequireCurrentAsync(_lease, linked.Token);
                    if (_disposed || !ReferenceEquals(current, _surface.InteractiveSession))
                        throw new UnauthorizedAccessException("The displayed Terminal session changed.");
                    _registered = current;
                }
                catch { _registry.Unregister(current); throw; }
            }
            linked.Token.ThrowIfCancellationRequested();
        }
        finally { _serial.Release(); }
    }

    private static bool Expected(Exception error) => error is UnauthorizedAccessException or IOException
        or InvalidOperationException or OperationCanceledException;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _active = false; _lifetime.Cancel(); _observation.Stop();
        _observation.Tick -= OnObservation; _surface.MetadataChanged -= OnMetadataChanged;
        Content = null;
        if (_registered is not null) { _registry.Unregister(_registered); _registered = null; }
        _scene?.Dispose(); _workspace.Dispose(); _viewport.Dispose(); _surface.Dispose(); _lifetime.Dispose();
        // A pending async check may still observe the canceled token; disposal does not grant authority.
    }
}
