#if !ANDROID
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Assistants;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Assistants.NativeUI;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Migration;
using HavenOS.Apps.Assistants.Memory;
using HavenOS.Apps.Assistants.MiniComputer;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private NativeAssistantsDesktopPage? _originalAssistantsPage;

    // Root passes the SAME already-opened App-owned Den and actual presentation
    // controller created by AssistantsWorkspaceFactory. It verifies the installed
    // Spaces dependency before calling this route. No Space or business owner is created here.
    internal Task OpenOriginalAssistantsAsync(IServiceProvider provider,
        HomeNativeWindowsComposition home, OriginalAssistantPersonalDenHost actualDenHost,
        HomePersonalDenFactory actualDenFactory, HomePersonalDenSession actualOpenedDen,
        AssistantsWorkspaceController actualScopedController,
        CancellationToken appLifetime, CancellationToken windowLifetime,
        ILegacyAgentMigrationController? actualMigration = null, string? migrationUnavailableReason = null,
        IAssistantMemoryManagementController? actualMemoryManagement = null, string? memoryUnavailableReason = null,
        IAssistantMiniComputerController? actualMiniComputerManagement = null, string? miniComputerUnavailableReason = null,
        IAssistantGeneratedUiHost? actualGeneratedUiHost = null) =>
        _originalShellWork.RunAsync(async original =>
        {
            Dispatcher.UIThread.VerifyAccess();
            NativeAssistantsDesktopPage? page = _originalAssistantsPage;
            NativeAssistantsDesktopPage? acquiredPage = null;
            Exception? primary = null;
            try
            {
                if (!ReferenceEquals(provider, global::Haven.Desktop.App.Services) ||
                    !ReferenceEquals(provider.GetRequiredService<HomePersonalDenFactory>(), actualDenFactory) ||
                    actualScopedController.OriginalCanonicalBridge is not DenAssistantCanonicalBridge bridge ||
                    !ReferenceEquals(bridge.OriginalHomeDenFactory, actualDenFactory) ||
                    !ReferenceEquals(bridge.OriginalTaskOwner, provider.GetRequiredService<Haven.Application.TaskExecutionCoordinator>()) ||
                    (actualMigration is not null && !actualMigration.IsOriginalCanonicalBridge(bridge)) ||
                    (actualMemoryManagement is not null && !actualMemoryManagement.IsOriginalCanonicalBridge(bridge)) ||
                    (actualMiniComputerManagement is not null && !actualMiniComputerManagement.IsOriginalCanonicalBridge(bridge)) ||
                    (actualGeneratedUiHost is not null && !actualGeneratedUiHost.IsOriginalController(actualScopedController)))
                    throw new UnauthorizedAccessException("Retain the SAME actual Assistants App/Home-Den/Task composition.");
                WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(provider, home);
                var actualWindow = TopLevel.GetTopLevel(this) as Window
                    ?? throw new InvalidOperationException("The actual Assistant shell is not attached to its native window.");
                original.BindPublicationGuard(() => !IsDisposed &&
                    ReferenceEquals(provider, global::Haven.Desktop.App.Services) && actualWindow.IsVisible &&
                    ReferenceEquals(TopLevel.GetTopLevel(this), actualWindow));
                original.DemandPublication();
                if (page?.OriginalClose is { } priorClose)
                {
                    if (!priorClose.IsCompletedSuccessfully)
                        throw new InvalidOperationException("The original Assistant page close remains unresolved; inspect its actual owner.");
                    page = null;
                }
                var isNew = page is null;
                if (page is null)
                {
                    DemandCanonicalPageAcquisition();
                    NativeAssistantsDesktopPage? retained = null;
                    var developmentRoute = AcquireOriginalShellSynchronous(original, () =>
                        CreateOriginalAssistantDevelopmentRoute(provider, home, actualScopedController,
                            appLifetime, windowLifetime, actualWindow));
                    void CapturePartial(NativeAssistantsDesktopPage actual)
                    {
                        if (retained is not null && !ReferenceEquals(retained, actual))
                            throw new InvalidOperationException("The native Assistant factory supplied multiple different partial pages.");
                        retained = actual; page = actual; acquiredPage = actual; _originalAssistantsPage = actual;
                        developmentRoute?.CaptureOriginalPage(actual);
                        RetainOriginalCanonicalPage(actual); // Before actual constructor/native callbacks.
                        if (global::Avalonia.Application.Current is not global::Haven.Desktop.App owningApp)
                            throw new UnauthorizedAccessException("The actual Assistants App owner is unavailable.");
                        owningApp.RetainOriginalAssistantsPresentation(actualScopedController, actual);
                        original.DemandPublication();
                    }
                    var created = AcquireOriginalShellSynchronous(original, () => NativeAssistantsDesktopPage.BindOriginal(
                        provider, home, actualDenHost, actualOpenedDen, actualScopedController,
                        appLifetime, windowLifetime, actualWindow, CapturePartial, developmentRoute,
                        actualMigration, migrationUnavailableReason, actualMemoryManagement, memoryUnavailableReason,
                        actualMiniComputerManagement, miniComputerUnavailableReason, actualGeneratedUiHost));
                    if (!ReferenceEquals(created, retained))
                        throw new InvalidOperationException("The actual original Assistant page was not captured before publication.");
                    page = created;
                }
                var actualPage = page ?? throw new InvalidOperationException("No actual Assistant page was acquired.");
                if (!actualPage.IsOriginalComposition(provider, home) ||
                    !actualPage.IsOriginalWindow(actualWindow, appLifetime, windowLifetime) ||
                    !ReferenceEquals(actualPage.OriginalController, actualScopedController) ||
                    !ReferenceEquals(actualPage.OriginalMigration, actualMigration) ||
                    !ReferenceEquals(actualPage.OriginalMemoryManagementController, actualMemoryManagement) ||
                    !ReferenceEquals(actualPage.OriginalMiniComputerManagementController, actualMiniComputerManagement) ||
                    !ReferenceEquals(actualPage.OriginalGeneratedUiHost, actualGeneratedUiHost))
                    throw new UnauthorizedAccessException("Retain the SAME native Assistant page and scoped controller.");
                if (isNew)
                    await original.AwaitAsync(AcquireOriginalShellSynchronous(original, () => actualPage.InitializeAsync(original.Token)));
                if (actualPage.OriginalInitialization?.IsCompletedSuccessfully != true)
                    throw new InvalidOperationException("The actual Assistant CUI initialization did not complete.");
                await PublishOriginalCanonicalTabAsync(original, () => actualPage.PublishOriginalTab(() =>
                {
                    original.DemandPublication();
                    var existing = OpenTabs.Where(tab => tab.Key == "assistants-native").ToArray();
                    if (existing.Length > 1 || existing.Any(tab => !ReferenceEquals(tab.Page, actualPage)))
                        throw new UnauthorizedAccessException("Retain one actual dedicated Assistants tab and native page.");
                    AddOrSelectTab("assistants-native", "Assistants", actualPage, false,
                        HavenSurface.Assistants, forceNewTab: false);
                    original.DemandPublication();
                    if (SelectedTab is not { Key: "assistants-native" } selected ||
                        !ReferenceEquals(selected.Page, actualPage) || CurrentSurface != HavenSurface.Assistants ||
                        !ReferenceEquals(TopLevel.GetTopLevel(actualPage), actualWindow))
                        throw new UnauthorizedAccessException("The actual Assistant tab/window association changed during publication.");
                    if (isNew && actualPage.OriginalDevelopmentRoute is not null)
                    {
                        var sameSurface = actualPage.OriginalSurface
                            ?? throw new InvalidOperationException("No actual Assistant surface was retained for its Dev callback.");
                        ConfigureOriginalAssistantDevelopmentSource(sameSurface, actualPage, provider, home,
                            appLifetime, windowLifetime, actualWindow);
                        original.DemandPublication();
                    }
                }));
                return;
            }
            catch (Exception failure) { primary = failure; original.Retain(failure); }
            // Both late partial pages and a controller acquired before a refused page
            // construction are joined. App-owned Home/Den/Chat/Tasks remain borrowed.
            Task? close = null;
            try
            {
                if (acquiredPage is not null)
                    close = AcquireOriginalShellSynchronous(original, acquiredPage.CloseAndDrainAsync);
                else if (!ReferenceEquals(_originalAssistantsPage?.OriginalController, actualScopedController))
                {
                    var borrowerHealthy = true;
                    if (actualMiniComputerManagement is not null)
                    {
                        Task? miniClose = null;
                        try { miniClose = AcquireOriginalShellSynchronous(original, actualMiniComputerManagement.CloseAndDrainAsync); }
                        catch (Exception cause) { original.Retain(cause); }
                        if (miniClose is not null)
                            try { await original.AwaitAsync(miniClose); }
                            catch (Exception cause) { original.Capture(miniClose, cause); }
                        borrowerHealthy &= miniClose?.IsCompletedSuccessfully == true;
                    }
                    if (actualMemoryManagement is not null)
                    {
                        Task? memoryClose = null;
                        try { memoryClose = AcquireOriginalShellSynchronous(original, actualMemoryManagement.CloseAndDrainAsync); }
                        catch (Exception cause) { original.Retain(cause); }
                        if (memoryClose is not null)
                            try { await original.AwaitAsync(memoryClose); }
                            catch (Exception cause) { original.Capture(memoryClose, cause); }
                        borrowerHealthy &= memoryClose?.IsCompletedSuccessfully == true;
                    }
                    if (actualMigration is not null)
                    {
                        var migrationClose = AcquireOriginalShellSynchronous(original, actualMigration.CloseAndDrainAsync);
                        try { await original.AwaitAsync(migrationClose); }
                        catch (Exception cause) { original.Capture(migrationClose, cause); }
                        borrowerHealthy &= migrationClose.IsCompletedSuccessfully;
                    }
                    if (borrowerHealthy) close = AcquireOriginalShellSynchronous(original, actualScopedController.CloseAndDrainAsync);
                }
            }
            catch (Exception failure) { original.Retain(failure); }
            if (close is not null)
                try { await original.AwaitAsync(close); }
                catch (Exception failure) { original.Capture(close, failure); }
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary!).Throw();
        });
}
#endif
