#if !ANDROID
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Assistants;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private NativeAssistantsSetupDesktopPage? _originalAssistantsSetupPage;

    internal Task OpenOriginalAssistantsSetupAsync(OriginalAssistantsDependencyStatus actualStatus,
        CancellationToken appLifetime, CancellationToken windowLifetime) =>
        _originalShellWork.RunAsync(async original =>
        {
            Dispatcher.UIThread.VerifyAccess();
            if (actualStatus.IsObserved)
                throw new ArgumentException("The actual observed dependency must use its configured Assistants route.", nameof(actualStatus));
            var actualWindow = TopLevel.GetTopLevel(this) as Window
                ?? throw new InvalidOperationException("The original Assistants setup shell is not attached to its native window.");
            original.BindPublicationGuard(() => !IsDisposed && actualWindow.IsVisible &&
                ReferenceEquals(TopLevel.GetTopLevel(this), actualWindow));
            original.DemandPublication();
            var page = _originalAssistantsSetupPage;
            NativeAssistantsSetupDesktopPage? acquired = null;
            try
            {
                if (page?.OriginalClose is { } previous)
                {
                    if (!previous.IsCompletedSuccessfully)
                        throw new InvalidOperationException("The SAME original Assistants setup close remains unresolved.");
                    page = null;
                }
                if (page is not null && (!page.IsOriginalWindow(actualWindow, appLifetime, windowLifetime) ||
                    !page.IsOriginalStatus(actualStatus)))
                {
                    await original.AwaitAsync(AcquireOriginalShellSynchronous(original, page.CloseAndDrainAsync));
                    page = null;
                }
                if (page is null)
                {
                    DemandCanonicalPageAcquisition();
                    NativeAssistantsSetupDesktopPage? retained = null;
                    void CapturePartial(NativeAssistantsSetupDesktopPage actual)
                    {
                        if (retained is not null && !ReferenceEquals(retained, actual))
                            throw new InvalidOperationException("The native setup factory supplied different partial page owners.");
                        retained = actual; acquired = actual; page = actual; _originalAssistantsSetupPage = actual;
                        RetainOriginalCanonicalPage(actual); // Capture before constructor/renderer callbacks.
                        original.DemandPublication();
                    }
                    var created = AcquireOriginalShellSynchronous(original, () => NativeAssistantsSetupDesktopPage.BindOriginal(
                        actualStatus, appLifetime, windowLifetime, actualWindow, CapturePartial));
                    if (!ReferenceEquals(created, retained))
                        throw new InvalidOperationException("The actual setup page was not retained before publication.");
                    page = created;
                    await original.AwaitAsync(AcquireOriginalShellSynchronous(original, () => created.InitializeAsync(original.Token)));
                }
                var actualPage = page ?? throw new InvalidOperationException("The dedicated setup page is unavailable.");
                await PublishOriginalCanonicalTabAsync(original, () => actualPage.PublishOriginalTab(() =>
                {
                    original.DemandPublication();
                    var prior = OpenTabs.Where(tab => tab.Key == "assistants-setup").ToArray();
                    if (prior.Length > 1 || prior.Any(tab => !ReferenceEquals(tab.Page, actualPage) &&
                        tab.Page is not NativeAssistantsSetupDesktopPage { OriginalClose.IsCompletedSuccessfully: true }))
                        throw new InvalidOperationException("The SAME original setup tab still owns another unclosed page.");
                    AddOrSelectTab("assistants-setup", "Assistants", actualPage, false,
                        HavenSurface.Assistants, forceNewTab: false);
                    original.DemandPublication();
                    if (SelectedTab is not { Key: "assistants-setup" } selected ||
                        !ReferenceEquals(selected.Page, actualPage) || CurrentSurface != HavenSurface.Assistants ||
                        !ReferenceEquals(TopLevel.GetTopLevel(actualPage), actualWindow))
                        throw new UnauthorizedAccessException("The actual Assistants setup tab/window association changed.");
                }));
            }
            catch (Exception failure)
            {
                original.Retain(failure);
                if (acquired is not null)
                {
                    Task? close = null;
                    try { close = AcquireOriginalShellSynchronous(original, acquired.CloseAndDrainAsync); }
                    catch (Exception cleanupFailure) { original.Retain(cleanupFailure); }
                    if (close is not null)
                        try { await original.AwaitAsync(close); }
                        catch (Exception cleanupFailure) { original.Capture(close, cleanupFailure); }
                }
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        });
}
#endif
