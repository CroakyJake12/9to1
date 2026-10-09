#if !ANDROID
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Core;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Home.Core;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private Func<App.OriginalInstalledHomeAppsPort?>? _originalInstalledHomeAppsFactory;
    private Func<HomeNativeStartupObservation?>? _originalInstalledHomeStartupObservation;
    private CancellationToken _originalInstalledHomeAppLifetime, _originalInstalledHomeWindowLifetime;
    private OriginalInstalledHomeAppsPage? _originalInstalledHomeAppsPage;
    internal void ConfigureOriginalInstalledHomeAppsFactory(Func<App.OriginalInstalledHomeAppsPort?> factory,
        Func<HomeNativeStartupObservation?> startupObservation, CancellationToken appLifetime, CancellationToken windowLifetime)
    {
        Dispatcher.UIThread.VerifyAccess(); _originalShellWork.DemandAdmission();
        ArgumentNullException.ThrowIfNull(factory); ArgumentNullException.ThrowIfNull(startupObservation);
        if (_originalInstalledHomeAppsFactory is not null || !appLifetime.CanBeCanceled || !windowLifetime.CanBeCanceled)
            throw new InvalidOperationException("Configure the original installed Home Apps route and native lifetimes once.");
        _originalInstalledHomeAppsFactory = factory; _originalInstalledHomeStartupObservation = startupObservation;
        _originalInstalledHomeAppLifetime = appLifetime; _originalInstalledHomeWindowLifetime = windowLifetime;
    }
    private void OpenInstalledHomeApps() { _ = OpenOriginalInstalledHomeAppsAsync(); }
    internal Task OpenOriginalInstalledHomeAppsAsync() => _originalShellWork.RunAsync(async original =>
    {
        Dispatcher.UIThread.VerifyAccess();
        var window = TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("The actual Apps shell has no window.");
        original.BindPublicationGuard(() => !IsDisposed && window.IsVisible && ReferenceEquals(TopLevel.GetTopLevel(this), window));
        var factory = _originalInstalledHomeAppsFactory ?? throw new InvalidOperationException("The installed Apps route is not configured.");
        var observation = _originalInstalledHomeStartupObservation ?? throw new InvalidOperationException("The actual startup observation port is absent.");
        var source = AcquireOriginalShellSynchronous(original, factory);
        var page = _originalInstalledHomeAppsPage;
        if (page?.OriginalClose is { } close)
        {
            await original.AwaitAsync(close);
            page = null;
        }
        if (page is not null && !page.HasOriginalBinding(source, window,
            _originalInstalledHomeAppLifetime, _originalInstalledHomeWindowLifetime))
            throw new UnauthorizedAccessException("The installed Apps page composition changed.");
        if (page is null)
        {
            DemandCanonicalPageAcquisition();
            OriginalInstalledHomeAppsPage? retained = null;
            void Capture(OriginalInstalledHomeAppsPage actual)
            {
                if (retained is not null && !ReferenceEquals(retained, actual)) throw new InvalidOperationException("The Apps constructor returned different actual owners.");
                retained = _originalInstalledHomeAppsPage = actual;
                RetainOriginalCanonicalPage(actual); original.DemandPublication();
            }
            page = AcquireOriginalShellSynchronous(original, () => OriginalInstalledHomeAppsPage.BindOriginal(source, observation,
                window, _originalInstalledHomeAppLifetime, _originalInstalledHomeWindowLifetime, Capture));
            if (!ReferenceEquals(page, retained)) throw new InvalidOperationException("Retain the actual Apps page before publication.");
            // Publish the actual tree before its source read so currentness can
            // verify the same native window; source reads remain finite observers.
        }
        var samePage = page;
        await PublishOriginalCanonicalTabAsync(original, () => samePage.PublishOriginalTab(() =>
        {
            var existing = OpenTabs.Where(tab => tab.Key == "home-installed-apps").ToArray();
            if (existing.Length > 1 || existing.Any(tab => !ReferenceEquals(tab.Page, samePage) &&
                tab.Page is not OriginalInstalledHomeAppsPage { OriginalClose.IsCompletedSuccessfully: true }))
                throw new InvalidOperationException("The installed Apps tab retains another unresolved page.");
            AddOrSelectTab("home-installed-apps", "Installed apps", samePage, true, HavenSurface.Home, forceNewTab: false);
            if (SelectedTab is not { Key: "home-installed-apps" } selected || !ReferenceEquals(selected.Page, samePage) ||
                !ReferenceEquals(TopLevel.GetTopLevel(samePage), window))
                throw new UnauthorizedAccessException("The SAME Apps tab/window association is required.");
        }));
        await original.AwaitAsync(AcquireOriginalShellSynchronous(original, samePage.InitializeAsync));
    });
}
#endif
