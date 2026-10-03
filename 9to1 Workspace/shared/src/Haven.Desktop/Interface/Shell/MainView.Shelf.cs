using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Shelf;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private readonly SemaphoreSlim _shelfOpen = new(1, 1);
    private async void OpenShelfLibrary()
    {
        try { await OpenShelfLibraryAsync(CancellationToken.None); }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException
            or NotSupportedException or OperationCanceledException)
        {
            if (!IsDisposed) _notifications.Show("Shelf unavailable", error.Message,
                ToastKind.Warning, TimeSpan.FromSeconds(5));
        }
    }
    public async Task OpenShelfLibraryAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var services = App.Services ?? throw new InvalidOperationException("The actual Shelf host is unavailable.");
        var originalActor = await services.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(token)
            ?? throw new UnauthorizedAccessException("Shelf requires its original authenticated display actor.");
        bool OriginalHostCurrent() => !IsDisposed && ReferenceEquals(App.Services, services);
        var owner = services.GetRequiredService<HomeShelfLibraryOwner>();
        // Capture actual canonical Settings UUID/actor/private selection BEFORE the queued turn.
        var originalDisplay = await owner.LoadForDisplayAsync(originalActor, token, OriginalHostCurrent);
        ShelfNativeWorkspaceHost? candidate = null;
        var queued = false;
        try
        {
            await _shelfOpen.WaitAsync(token); queued = true;
            var actors = services.GetRequiredService<IAuthenticatedResourceActorSource>();
            if (!OriginalHostCurrent() || await actors.GetCurrentAsync(token) != originalActor)
                throw new UnauthorizedAccessException("The original Shelf host or actor changed.");
            candidate = await ShelfNativeWorkspaceHost.OpenForOriginalDisplayAsync(owner, originalDisplay.Selection,
                id => ReviewHomeRequestForOriginalHostAsync(id, services, originalActor),
                async action => await Dispatcher.UIThread.InvokeAsync(() => { if (OriginalHostCurrent()) action(); }), token);
            if (!OriginalHostCurrent() || await actors.GetCurrentAsync(token) != originalActor)
                throw new UnauthorizedAccessException("The original Shelf host or actor changed before mount.");
            token.ThrowIfCancellationRequested();
            AddOrSelectTab("shelf-library-" + Guid.NewGuid().ToString("N"), "Shelf library", candidate,
                closeable: true, surface: HavenSurface.Shelf, forceNewTab: true);
            candidate = null;
        }
        finally
        {
            candidate?.Dispose(); originalDisplay.Selection.Dispose();
            if (queued) _shelfOpen.Release();
        }
    }
    public static async Task<ShelfNativeWorkspaceHost> CreateShelfWorkspaceAsync(IServiceProvider services,
        AuthenticatedResourceActor originalActor, Func<string, Task> actualHomeReview,
        Func<Action, Task> actualUiRender, Func<bool> hostIsCurrent, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalActor);
        if (!hostIsCurrent()) throw new ObjectDisposedException("Shelf host");
        var actors = services.GetRequiredService<IAuthenticatedResourceActorSource>();
        if (await actors.GetCurrentAsync(token) != originalActor || !hostIsCurrent())
            throw new UnauthorizedAccessException("The original Shelf display actor or host changed.");
        var candidate = await ShelfNativeWorkspaceHost.OpenAsync(services.GetRequiredService<HomeShelfLibraryOwner>(),
            originalActor, actualHomeReview, actualUiRender, token, hostIsCurrent);
        try
        {
            if (await actors.GetCurrentAsync(token) != originalActor || !hostIsCurrent())
                throw new UnauthorizedAccessException("The original Shelf display actor or host changed before mount.");
            token.ThrowIfCancellationRequested();
            return candidate;
        }
        catch { candidate.Dispose(); throw; }
    }
}
