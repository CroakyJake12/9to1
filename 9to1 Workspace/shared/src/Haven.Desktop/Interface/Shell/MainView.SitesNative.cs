#if !ANDROID
using Avalonia.Threading;
using Haven.Core;
using Haven.Desktop.Views.Pages.Sites;
using HavenOS.Home.Core;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private NativeSitesDesktopPage? _originalSitesPage;

    // Called by the actual product startup over the original provider/Home/window.
    // No serialized app route, root path or caller-selected actor creates this scope.
    internal Task OpenOriginalSitesAsync(IServiceProvider provider, HomeNativeWindowsComposition home,
        CancellationToken appLifetime, CancellationToken windowLifetime) => _originalShellWork.RunAsync(async original =>
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var page = _originalSitesPage;
        var isNew = page is null;
        if (page is null)
            page = _originalSitesPage = NativeSitesDesktopPage.BindOriginal(provider, home, appLifetime, windowLifetime);
        if (!page.IsOriginalComposition(provider, home))
            throw new UnauthorizedAccessException("Retain the SAME Sites page and original native composition.");
        original.BindPublicationGuard(() => !IsDisposed && ReferenceEquals(_originalSitesPage, page));
        Exception? primary = null; Task? actualClose = null;
        try
        {
            if (isNew) await original.AwaitAsync(page.InitializeAsync(original.Token));
            await original.AwaitAsync(page.RevalidateAsync(original.Token));
            original.DemandPublication();
            page.PublishOriginalTab(() =>
            {
                original.DemandPublication();
                var existing = OpenTabs.Where(tab => tab.Key == "sites-native").ToArray();
                if (existing.Length > 1 || existing.Any(tab => !ReferenceEquals(tab.Page, page)))
                    throw new UnauthorizedAccessException("Retain one original Sites tab and page.");
                AddOrSelectTab("sites-native", "Sites", page, false, HavenSurface.Sites, forceNewTab: true);
                original.DemandPublication();
                if (SelectedTab is not { Key: "sites-native" } tab || !ReferenceEquals(tab.Page, page) || CurrentSurface != HavenSurface.Sites)
                    throw new UnauthorizedAccessException("The original Sites tab changed during publication.");
            });
            return;
        }
        catch (Exception error) { primary = error; original.Retain(error); }
        // Failed mount/publication owns the actual acquired page until its same
        // close really settles, including cleanup faults. The parent awaits this
        // full body; a borrowed Home/provider is never stopped by the page.
        try { actualClose = page.CloseAndDrainAsync(); }
        catch (Exception error) { original.Retain(error); }
        if (actualClose is not null)
            try { await original.AwaitAsync(actualClose); }
            catch (Exception error) { original.Retain(actualClose.IsFaulted ? actualClose.Exception! : error); }
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary!).Throw();
    });
}
#endif
