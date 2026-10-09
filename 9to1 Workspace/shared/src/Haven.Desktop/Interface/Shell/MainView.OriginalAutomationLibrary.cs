#if !ANDROID
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Automations;
using HavenOS.Home.Core;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private Func<(ICanonicalAutomationLibraryOriginalReadSource? Source, HomeLocalProfileIdentity? Profiles, ICanonicalAutomationDefinitionOriginalProcessSource? Writer)>? _originalAutomationLibraryFactory;
    private CancellationToken _originalAutomationLibraryAppLifetime, _originalAutomationLibraryWindowLifetime;
    private OriginalAutomationLibraryDesktopPage? _originalAutomationLibraryPage;
    internal void ConfigureOriginalAutomationLibraryFactory(
        Func<(ICanonicalAutomationLibraryOriginalReadSource? Source, HomeLocalProfileIdentity? Profiles, ICanonicalAutomationDefinitionOriginalProcessSource? Writer)> actual,
        CancellationToken appLifetime, CancellationToken windowLifetime)
    {
        Dispatcher.UIThread.VerifyAccess(); _originalShellWork.DemandAdmission();
        ArgumentNullException.ThrowIfNull(actual);
        if (_originalAutomationLibraryFactory is not null || !appLifetime.CanBeCanceled || !windowLifetime.CanBeCanceled)
            throw new InvalidOperationException("Retain the actual library factory and native lifetimes once.");
        _originalAutomationLibraryFactory = actual;
        _originalAutomationLibraryAppLifetime = appLifetime; _originalAutomationLibraryWindowLifetime = windowLifetime;
    }
    private void OpenSavedAutomationLibrary() { _ = OpenOriginalAutomationLibraryAsync(); }
    internal Task OpenOriginalAutomationLibraryAsync() => _originalShellWork.RunAsync(async original =>
    {
        Dispatcher.UIThread.VerifyAccess();
        var window = TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("The actual library shell has no window.");
        original.BindPublicationGuard(() => !IsDisposed && window.IsVisible && ReferenceEquals(TopLevel.GetTopLevel(this), window));
        original.DemandPublication();
        var factory = _originalAutomationLibraryFactory ?? throw new InvalidOperationException("The saved-library route is not configured.");
        var tuple = AcquireOriginalShellSynchronous(original, factory);
        OriginalAutomationLibraryDesktopPage? acquired = null;
        try
        {
            var page = _originalAutomationLibraryPage;
            if (page?.OriginalClose is { } prior)
            {
                if (!prior.IsCompletedSuccessfully) throw new InvalidOperationException("The actual saved-library close remains unresolved.");
                page = null;
            }
            if (page is not null && !page.HasOriginalBinding(tuple.Source, tuple.Profiles, window,
                _originalAutomationLibraryAppLifetime, _originalAutomationLibraryWindowLifetime, tuple.Writer))
                throw new UnauthorizedAccessException("The actual library page composition changed.");
            if (page is null)
            {
                DemandCanonicalPageAcquisition();
                OriginalAutomationLibraryDesktopPage? retained = null;
                void Capture(OriginalAutomationLibraryDesktopPage actual)
                {
                    if (retained is not null && !ReferenceEquals(retained, actual))
                        throw new InvalidOperationException("The library factory returned different actual page owners.");
                    retained = acquired = _originalAutomationLibraryPage = actual;
                    RetainOriginalCanonicalPage(actual); original.DemandPublication();
                }
                page = AcquireOriginalShellSynchronous(original, () => OriginalAutomationLibraryDesktopPage.BindOriginal(
                    tuple.Source, tuple.Profiles, window, _originalAutomationLibraryAppLifetime,
                    _originalAutomationLibraryWindowLifetime, Capture, tuple.Writer));
                if (!ReferenceEquals(page, retained)) throw new InvalidOperationException("Retain the actual page before library publication.");
                await original.AwaitAsync(AcquireOriginalShellSynchronous(original, () => page.InitializeAsync(original.Token)));
            }
            var actualPage = page;
            await PublishOriginalCanonicalTabAsync(original, () => actualPage.PublishOriginalTab(() =>
            {
                original.DemandPublication();
                var existing = OpenTabs.Where(tab => tab.Key == "automations-saved-library").ToArray();
                if (existing.Length > 1 || existing.Any(tab => !ReferenceEquals(tab.Page, actualPage) &&
                    tab.Page is not OriginalAutomationLibraryDesktopPage { OriginalClose.IsCompletedSuccessfully: true }))
                    throw new InvalidOperationException("The saved-library tab retains another unclosed page.");
                AddOrSelectTab("automations-saved-library", "Saved automations", actualPage, true,
                    HavenSurface.Automations, forceNewTab: false);
                original.DemandPublication();
                if (SelectedTab is not { Key: "automations-saved-library" } selected || !ReferenceEquals(selected.Page, actualPage) ||
                    CurrentSurface != HavenSurface.Automations || !ReferenceEquals(TopLevel.GetTopLevel(actualPage), window))
                    throw new UnauthorizedAccessException("The actual saved-library tab/window association changed.");
            }));
        }
        catch (Exception cause)
        {
            original.Retain(cause);
            if (acquired is not null)
            {
                Task? close = null;
                try { close = AcquireOriginalShellSynchronous(original, acquired.CloseAndDrainAsync); }
                catch (Exception failure) { original.Retain(failure); }
                if (close is not null) try { await original.AwaitAsync(close); }
                    catch (Exception failure) { original.Capture(close, failure); }
            }
            ExceptionDispatchInfo.Capture(cause).Throw();
        }
    });
}
#endif
