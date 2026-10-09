#if !ANDROID
using Avalonia.Threading;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop;

public sealed partial class App
{
    internal Task OpenOriginalAssistantsForShellAsync(MainView actualShell, CancellationToken caller) =>
        _originalAppWork.RunAsync(async original =>
        {
            Dispatcher.UIThread.VerifyAccess();
            var provider = _services ?? throw new InvalidOperationException("The actual App provider is unavailable.");
            var home = _actualWindowsHome ?? throw new UnauthorizedAccessException("The actual Windows Home owner is unavailable.");
            var window = _actualSameProcessNativeWindow
                ?? throw new InvalidOperationException("The actual native App window is unavailable.");
            void DemandCurrent()
            {
                Dispatcher.UIThread.VerifyAccess();
                caller.ThrowIfCancellationRequested(); original.DemandPublication();
                if (!ReferenceEquals(provider, _services) || !ReferenceEquals(home, _actualWindowsHome) ||
                    !ReferenceEquals(actualShell, _actualSameProcessNativeShell) ||
                    !ReferenceEquals(window, _actualSameProcessNativeWindow) ||
                    !ReferenceEquals(window.DataContext, actualShell) || !window.IsVisible)
                    throw new UnauthorizedAccessException("Retain the SAME actual App shell/window/provider/Home for Assistants.");
                WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(provider, home);
            }
            DemandCurrent();
            original.BindPublicationGuard(() => ReferenceEquals(actualShell, _actualSameProcessNativeShell) &&
                ReferenceEquals(window, _actualSameProcessNativeWindow) && ReferenceEquals(window.DataContext, actualShell) &&
                window.IsVisible && ReferenceEquals(provider, _services) && ReferenceEquals(home, _actualWindowsHome));
            using var acquisition = CancellationTokenSource.CreateLinkedTokenSource(caller, original.Token);
            var windowLifetime = AcquireOriginalAppSynchronous(original, window.AcquireOriginalWindowLifetime);
            var dependency = await original.AwaitAsync(AcquireOriginalAppSynchronous(original,
                () => ObserveOriginalAssistantSpacesDependencyAsync(original, provider, home, acquisition.Token)));
            DemandCurrent();
            if (!dependency.IsObserved)
            {
                await original.AwaitAsync(AcquireOriginalAppSynchronous(original,
                    () => actualShell.OpenOriginalAssistantsSetupAsync(dependency, original.Token, windowLifetime)));
                DemandCurrent();
                return;
            }
            var actual = await original.AwaitAsync(AcquireOriginalAppSynchronous(original,
                () => CreateOriginalAssistantsControllerAsync(provider, home, acquisition.Token)));
            DemandCurrent();
            if (actual is null)
            {
                // The factory rechecks the actual dependency immediately before
                // Den access. A changed/unavailable dependency is a setup result,
                // not a fault that poisons the App's original shutdown lifetime.
                await original.AwaitAsync(AcquireOriginalAppSynchronous(original,
                    () => actualShell.OpenOriginalAssistantsSetupAsync(
                        OriginalAssistantsDependencyStatus.SetupRequired(), original.Token, windowLifetime)));
                DemandCurrent();
                return;
            }
            // Page lifetime is the persistent SAME App/window originals. The transient
            // caller/acquisition token is used only by the actual factory/read stages.
            await original.AwaitAsync(AcquireOriginalAppSynchronous(original,
                () => actualShell.OpenOriginalAssistantsAsync(provider, home, actual.OriginalDenHost,
                    actual.OriginalDenFactory, actual.OriginalOpenedDen, actual.OriginalController,
                    original.Token, windowLifetime, actual.OriginalMigration,
                    actual.OriginalMigration is null ? "The original saved Agent migration source is not configured." : null,
                    actual.OriginalMemoryManagement, actual.OriginalMemoryManagement is null
                        ? "The original local Assistant memory source is not configured." : null,
                    actual.OriginalMiniComputerManagement, actual.OriginalMiniComputerManagement is null
                        ? "The original protected Mini Computer catalogue and operation source are not configured." : null,
                    actual.OriginalGeneratedUiHost)));
            DemandCurrent();
        });
}
#endif
