#if !ANDROID
using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private bool _configuredOriginalNativeDevelopment;
    private DeveloperTaskWorkspaceService? _actualOriginalNativeDevelopment;
    private FilesNativeBrowserService? _actualOriginalNativeDevelopmentFiles;
    private DeveloperTaskOriginalRetirementAdapter? _actualNativeDevelopmentRetirement;
    private FilesDeveloperOriginalRetirementAdapter? _actualNativeDevelopmentFilesRetirement;
    private readonly List<object> _actualNativeDevelopmentBorrowers = [];
    private bool _nativeDevelopmentBorrowersTransferred;

    // Configuration observation only. The default App has no original Home graph:
    // it keeps ordinary routes and does not manufacture a Home store, actor or startup.
    private void ConfigureOriginalNativeDevelopmentRegistrations(IServiceCollection actualCollection)
    {
        _originalAppWork.RunSynchronous(original =>
        {
            AcquireOriginalAppSynchronous(original, () =>
            {
                original.DemandPublication();
                Type[] required = [typeof(IHomeCoreStateStore), typeof(HomeLocalProfileIdentity),
                    typeof(IAuthenticatedResourceActorSource), typeof(IResourceStoreOwnershipAuthority),
                    typeof(ResourceAuthorizationService), typeof(HomePermissionTrustService), typeof(HomeResourceOperationBroker)];
                var configured = required.Count(type => actualCollection.Any(item => item.ServiceType == type));
                // A missing or partial original Home tuple leaves native Dev unconfigured.
                // Ordinary routes remain available; descriptors alone never certify bootstrap.
                if (configured != required.Length) return false;
                original.DemandPublication();
                actualCollection.AddFilesNativeHost();
                actualCollection.AddSingleton<IFilesOriginalBrowserDownloadNavigator>(provider =>
                    GetOriginalBrowserDownloadFilesNavigator(provider.GetRequiredService<FilesNativeBrowserService>()));
                original.DemandPublication();
                actualCollection.AddHavenOriginalNativeDevelopment();
                original.DemandPublication();
                _configuredOriginalNativeDevelopment = true;
                return true;
            });
        });
    }

    // The process owner invokes this during its genuine shutdown-cohort configuration,
    // before any window/tab publication. These borrowed globals are never stopped by a
    // Files window, Task widget, Dev page, hidden tab or observation detach.
    private object[] CaptureOriginalNativeDevelopmentBorrowers(IServiceProvider actualProvider)
    {
        if (!_configuredOriginalNativeDevelopment) return [];
        _originalAppWork.RunSynchronous(original =>
        {
            original.DemandPublication();
            var development = AcquireOriginalAppSynchronous(original, () =>
            {
                var acquired = actualProvider.GetRequiredService<DeveloperTaskWorkspaceService>();
                if (_actualOriginalNativeDevelopment is { } previous && !ReferenceEquals(previous, acquired))
                {
                    RetainOriginalNativeDevelopmentBorrower(new DeveloperTaskOriginalRetirementAdapter(acquired));
                    throw new InvalidOperationException("The original App Dev singleton was replaced.");
                }
                _actualOriginalNativeDevelopment = acquired;
                _actualNativeDevelopmentRetirement ??= new(acquired);
                RetainOriginalNativeDevelopmentBorrower(_actualNativeDevelopmentRetirement);
                return acquired;
            });
            original.DemandPublication();
            var files = AcquireOriginalAppSynchronous(original, () =>
            {
                var acquired = actualProvider.GetRequiredService<FilesNativeBrowserService>();
                if (_actualOriginalNativeDevelopmentFiles is { } previous && !ReferenceEquals(previous, acquired))
                {
                    RetainOriginalNativeDevelopmentBorrower(new FilesDeveloperOriginalRetirementAdapter(acquired));
                    throw new InvalidOperationException("The original App Files singleton was replaced.");
                }
                _actualOriginalNativeDevelopmentFiles = acquired;
                _actualNativeDevelopmentFilesRetirement ??= new(acquired);
                RetainOriginalNativeDevelopmentBorrower(_actualNativeDevelopmentFilesRetirement);
                return acquired;
            });
            original.DemandPublication();
            var canonical = AcquireOriginalAppSynchronous(original, actualProvider.GetRequiredService<TaskExecutionCoordinator>);
            original.DemandPublication();
            var factory = AcquireOriginalAppSynchronous(original, actualProvider.GetRequiredService<DeveloperProjectWorkbenchPageFactory>);
            original.DemandPublication();
            if (!factory.IsBoundToOriginalComposition(development, canonical, files))
                throw new InvalidOperationException("The native process must retain the SAME Dev, Task and Files singleton composition.");
            original.DemandPublication();
        });
        return [_actualNativeDevelopmentRetirement!, _actualNativeDevelopmentFilesRetirement!];
    }

    private void RetainOriginalNativeDevelopmentBorrower(object actual)
    {
        lock (_actualStartupAcquisitionGate)
            if (!_actualNativeDevelopmentBorrowers.Any(previous => ReferenceEquals(previous, actual)))
                _actualNativeDevelopmentBorrowers.Add(actual);
    }

    private async Task JoinOriginalUntransferredNativeDevelopmentBorrowersAsync()
    {
        var failures = new List<Exception>();
        object[] actuals;
        lock (_actualStartupAcquisitionGate)
            actuals = _nativeDevelopmentBorrowersTransferred ? [] : _actualNativeDevelopmentBorrowers.ToArray();
        // A failed startup has no configured shutdown cohort. Retain and retire each
        // acquired singleton independently; this cannot acknowledge clean startup/exit.
        var preflight = new List<Exception>();
        foreach (var actual in actuals)
            try { _originalAppWork.RunCloseCallback(() => ((IDesktopOriginalRetirementJoinGuard)actual).DemandExternalOriginalRetirementJoin()); }
            catch (Exception error) { AddAppCause(preflight, error); }
        if (preflight.Count != 0)
        {
            foreach (var error in preflight) AddAppCause(failures, error);
            ThrowAppCauses(failures); // No encompassing self-join may partially retire the cohort.
            return;
        }
        foreach (var actual in actuals)
            try { _originalAppWork.RunCloseCallback(((IDesktopOriginalRetirementParticipant)actual).RequestRetirement); }
            catch (Exception error) { AddAppCause(failures, error); }
        var originals = new List<Task>();
        foreach (var actual in actuals)
            try
            {
                _originalAppWork.RunCloseCallback(() =>
                    originals.Add(((IDesktopOriginalRetirementParticipant)actual).CloseAndDrainAsync()));
            }
            catch (Exception error) { AddAppCause(failures, error); }
        foreach (var actual in originals.Distinct<Task>(ReferenceEqualityComparer.Instance))
            await JoinOriginalAppTaskAsync(actual, failures);
        ThrowAppCauses(failures);
    }
}
#endif
