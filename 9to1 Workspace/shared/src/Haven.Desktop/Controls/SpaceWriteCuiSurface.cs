using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using NineToOne.Cui.AI;

namespace Haven.Desktop.Controls;

/// <summary>Read-only canonical Write package objects through the shared lossless Notes renderer.
/// This is not a LibreOffice pagination or document-engine implementation.</summary>
public sealed class SpaceWriteCuiSurface(SpaceFilesArtifactAction action, SpaceFilesArtifactActionRouter router,
    NativeFilesWorkspaceAuthority files, IWriteNativeDocumentPackageStore packages, HomeCoreRuntime home,
    IAuthenticatedResourceActorSource actors, ResourceAuthorizationService resources)
    : UserControl, IActivatablePage, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<HomeProductivityCuiSurface> _objects = [];
    private Haven.Desktop.Services.HomeResourceCuiReadiness? _readiness;
    private bool _initialized;
    private bool _disposed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) throw new InvalidOperationException("This Write source is already open.");
        _initialized = true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        try
        {
            if (action.Writing) throw new ArgumentException("Opening Write requires a read route.", nameof(action));
            var target = await router.ResolveAsync(action, token);
            if (target.Artifact.OwnerAppId != "write") throw new InvalidDataException("The source is not a Write document.");
            _readiness = new(home, actors, resources, target.ActionId, _ => ValueTask.FromResult(target.Scopes));
            await RequireReadyAsync(token);
            var workspace = await files.GetCurrentAsync(token) ?? throw new UnauthorizedAccessException("Files is unavailable.");
            var bridge = new WriteFilesArtifactBridge(actors, actor => actor == workspace.Actor ? workspace.Provider : null,
                workspace.Directories, packages, resources, () => AppAiAccessMode.ReadOnly);
            var document = await bridge.OpenAsync(target.Artifact.FileId, token);
            await router.ResolveAsync(action, token);
            token.ThrowIfCancellationRequested();
            if (document.Sections.Any(section => !string.IsNullOrEmpty(section.Header) || !string.IsNullOrEmpty(section.Footer) ||
                section.Pages.Any(page => page.CanvasObjects.Count != 0)))
                throw new NotSupportedException("This document requires the owning Write layout engine for headers, footers or canvas objects.");
            var engine = new HomeProductivityEngine();
            var content = new StackPanel { Spacing = 12 };
            foreach (var block in document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks))
            {
                // Unsupported rich families fail explicitly; no block is flattened or silently omitted.
                var projected = HomeNotesSharedObjects.Project(block);
                var surface = new HomeProductivityCuiSurface(engine.RenderObject(projected));
                _objects.Add(surface); content.Children.Add(surface);
            }
            await router.ResolveAsync(action, token);
            token.ThrowIfCancellationRequested();
            await RequireReadyAsync(token);
            Content = new ScrollViewer { Content = content };
        }
        catch { Dispose(); throw; }
    }

    private async Task RequireReadyAsync(CancellationToken token)
    {
        var state = await _readiness!.CheckAsync(token);
        if (state.State != CuiSceneAvailabilityState.Ready) throw new UnauthorizedAccessException(state.Message);
    }

    public async Task ActivateAsync(CancellationToken cancellationToken)
    {
        if (_disposed || !_initialized) return;
        try { await router.ResolveAsync(action, cancellationToken); await RequireReadyAsync(cancellationToken); }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or InvalidOperationException or OperationCanceledException)
        { Clear(); }
    }
    public void Deactivate() { }
    private void Clear()
    {
        Content = null;
        foreach (var item in _objects) item.Dispose();
        _objects.Clear();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); Clear(); _lifetime.Dispose();
    }
}
