#if !ANDROID
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private SpacesOriginalStoreSetupBackend? _actualSpacesStoreSetup;

    private static VersionedAtomicSettingsStore PrepareOriginalSpacesSettingsComposition(IServiceCollection collection, AppPaths paths)
    {
        var rows = collection.Where(row => row.ServiceType == typeof(IVersionedSettingsStore)).Take(2).ToArray();
        if (rows.Length != 1 || rows[0].Lifetime != ServiceLifetime.Singleton ||
            rows[0].ImplementationType != typeof(VersionedAtomicSettingsStore) ||
            collection.Any(row => row.ServiceType == typeof(SpacesLocalStoreEvidenceProvider)))
            throw new InvalidOperationException("Use the maintained uninstantiated canonical Spaces settings owner.");
        // Capture ONE concrete owner before Home construction. Existing aliases and SpaceRegistry
        // resolve this same singleton. Its constructor performs no IO or identity initialisation.
        var actual = new VersionedAtomicSettingsStore(paths);
        collection.Remove(rows[0]); collection.AddSingleton<IVersionedSettingsStore>(actual);
        return actual;
    }

    private void RegisterOriginalSpacesStoreSetup(IServiceCollection collection, HomeNativeWindowsComposition home,
        VersionedAtomicSettingsStore settings, SpacesLocalStoreEvidenceProvider evidence)
    {
        if (collection.Any(row => row.ServiceType == typeof(SpacesOriginalStoreSetupBackend)))
            throw new InvalidOperationException("The original Spaces store setup owner already exists.");
        collection.AddSingleton<SpacesOriginalStoreSetupBackend>(provider =>
        {
            SpacesOriginalStoreSetupBackend actual = null!;
            _originalAppWork.RunSynchronous(original =>
            {
                original.DemandPublication();
                actual = AcquireOriginalAppSynchronous(original, () =>
                {
                    _ = provider.RequireOriginalWindowsHomeComponents(home);
                    if (_services is null || !ReferenceEquals(provider, _services.GetRequiredService<IServiceProvider>()) ||
                        !ReferenceEquals(home, _actualWindowsHome) ||
                        !ReferenceEquals(provider.GetRequiredService<IVersionedSettingsStore>(), settings) ||
                        !ReferenceEquals(provider.GetRequiredService<SpacesLocalStoreEvidenceProvider>(), evidence) ||
                        !provider.GetRequiredService<SpaceRegistry>().HasOriginalSettingsStore(settings))
                        throw new UnauthorizedAccessException("Retain the SAME original settings, Space registry and Home setup composition.");
                    var approvals = provider.GetRequiredService<HomeNativeApprovalWindowOwner>();
                    var same = _actualSpacesStoreSetup ??= new(settings, evidence, home, approvals, original.Token);
                    if (same.OriginalClose is not null || !same.HasOriginalComposition(settings, evidence, home, approvals))
                        throw new InvalidOperationException("The original Spaces store setup owner changed or retired.");
                    return same;
                });
                original.DemandPublication();
            });
            return actual;
        });
    }

    private void DemandOriginalSpacesStoreSetupRetirementJoin() =>
        _actualSpacesStoreSetup?.DemandExternalOriginalRetirementJoin();

    private Task JoinOriginalSpacesStoreSetupAfterBorrowersAsync() =>
        _actualSpacesStoreSetup?.CloseAndDrainAsync() ?? Task.CompletedTask;
}
#endif
