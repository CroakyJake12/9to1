using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Application.Games;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using HavenOS.Games;
using HavenOS.Home.Core;

namespace Haven.Desktop.Controls;

/// <summary>Owning Games inspector over a captured Space source. Editing needs a separate brokered route.</summary>
public sealed class SpaceGamesCuiSurface(SpaceFilesArtifactAction action, SpaceFilesArtifactActionRouter router,
    GamesProjectEditorService editor, GamesSceneSessionService? scenes, HomeCoreRuntime home,
    IAuthenticatedResourceActorSource actors, ResourceAuthorizationService resources)
    : UserControl, IActivatablePage, ICuiActionDispatcher, ICuiActionAvailability, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private HomeResourceCuiReadiness? _readiness;
    private GamesCuiWorkspace? _bindings;
    private CuiSceneHost? _scene;
    private bool _available;
    private bool _disposed;
    private bool _initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) throw new InvalidOperationException("This Games source is already open.");
        _initialized = true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        try
        {
            if (action.Writing) throw new ArgumentException("Opening Games requires a read route.", nameof(action));
            var target = await router.ResolveAsync(action, token);
            if (target.Artifact.OwnerAppId != "games") throw new InvalidDataException("The source is not a Games project.");
            _readiness = new(home, actors, resources, target.ActionId, _ => ValueTask.FromResult(target.Scopes));
            await RequireReadyAsync(token);
            _available = true;
            _bindings = new(editor, scenes, () => action.FileId,
                command => _available && !_disposed && (command is "9to1.Games.Open" or "9to1.Games.NextNode" ||
                    command == "9to1.Games.Observe" && scenes is not null));
            await _bindings.DispatchAsync("9to1.Games.Open", null, token);
            await router.ResolveAsync(action, token);
            await RequireReadyAsync(token);
            _scene = new CuiSceneHost(new CuiControlRegistry());
            var state = await _scene.ShowAsync(new("games", "Games", "Games", GamesCuiWorkspace.LoadDocument(),
                _bindings, this, _readiness), token);
            token.ThrowIfCancellationRequested();
            if (state.State != CuiSceneAvailabilityState.Ready) throw new UnauthorizedAccessException(state.Message);
            Content = _scene;
        }
        catch { Dispose(); throw; }
    }

    public bool? IsActionAvailable(string command) => !_disposed && _available && _bindings?.IsActionAvailable(command) == true;

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (IsActionAvailable(command) != true) throw new UnauthorizedAccessException("This Games operation is unavailable.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            await RequireReadyAsync(linked.Token);
            await router.ResolveAsync(action, linked.Token);
            await _bindings!.DispatchAsync(command, parameter, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            await router.ResolveAsync(action, linked.Token);
            await RequireReadyAsync(linked.Token);
        }
        catch { Clear(); throw; }
    }

    private async Task RequireReadyAsync(CancellationToken token)
    {
        var state = await _readiness!.CheckAsync(token);
        if (state.State != CuiSceneAvailabilityState.Ready) throw new UnauthorizedAccessException(state.Message);
    }

    public async Task ActivateAsync(CancellationToken cancellationToken)
    {
        if (_disposed || !_available) return;
        try { await router.ResolveAsync(action, cancellationToken); await RequireReadyAsync(cancellationToken); }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or InvalidOperationException or OperationCanceledException)
        { Clear(); }
    }
    public void Deactivate() { }
    private void Clear() { _available = false; _bindings?.RefreshAvailability(); Content = null; }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel(); Clear(); _scene?.Dispose(); _lifetime.Dispose();
    }
}
