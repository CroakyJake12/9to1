#if !ANDROID
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private CanonicalAutomationLibraryOriginalReadOwner? _actualAutomationLibraryRead;
    private void ConfigureOriginalAutomationLibraryReadRegistrations(IServiceCollection collection)
    {
        if (_actualWindowsHome is not { } home) return;
        _originalAppWork.RunSynchronous(original => AcquireOriginalAppSynchronous(original, () =>
        {
            original.DemandPublication();
            if (collection.Any(row => row.ServiceType == typeof(CanonicalAutomationLibraryOriginalReadOwner) ||
                row.ServiceType == typeof(ICanonicalAutomationLibraryOriginalReadSource)))
                throw new InvalidOperationException("Retain the actual canonical automation library source once.");
            collection.AddSingleton<CanonicalAutomationLibraryOriginalReadOwner>(provider => AcquireOriginalAssistantCapabilitySingleton(() =>
            {
                _ = provider.RequireOriginalWindowsHomeComponents(home);
                if (!HasOriginalAutomationLibraryProviderTuple(provider, _services) || !ReferenceEquals(home, _actualWindowsHome) ||
                    _actualAutomationLibraryRead is not null || _actualAssistantSqliteStore is null || _actualAssistantSqliteDatabase is null ||
                    !ReferenceEquals(provider.GetRequiredService<SqliteDatabase>(), _actualAssistantSqliteDatabase) ||
                    !ReferenceEquals(provider.GetRequiredService<CanonicalSqliteOriginalStoreOwner>(), _actualAssistantSqliteStore))
                    throw new UnauthorizedAccessException("The actual configured App/Home/canonical store is required.");
                if (provider.GetRequiredService<IAutomationRepository>() is not AutomationRepository repository)
                    throw new UnauthorizedAccessException("Use the SAME maintained automation repository.");
                // Constructor checks references only. The source itself performs fresh
                // Home receipt and actor checks before its existing-table READ.
                var actual = _actualAutomationLibraryRead = new(_actualAssistantSqliteStore,
                    _actualAssistantSqliteDatabase, provider.GetRequiredService<IAppPaths>(), repository, home.Ownership);
                DemandOriginalAutomationLibraryComposition(provider, home, actual);
                return actual;
            }));
            collection.AddSingleton<ICanonicalAutomationLibraryOriginalReadSource>(provider =>
                provider.GetRequiredService<CanonicalAutomationLibraryOriginalReadOwner>());
            ConfigureOriginalAutomationLibraryWriteRegistrations(collection, home);
            return true;
        }));
    }
    internal static bool HasOriginalAutomationLibraryProviderTuple(IServiceProvider actual, IServiceProvider? configured)
    {
        // Microsoft DI supplies its root engine scope to singleton factories, while
        // the shell holds the outer provider. Compare only that SAME configured pair.
        return configured is not null && (ReferenceEquals(actual, configured) ||
            ReferenceEquals(actual, configured.GetRequiredService<IServiceProvider>()));
    }
    private void DemandOriginalAutomationLibraryComposition(IServiceProvider provider, HomeNativeWindowsComposition home,
        CanonicalAutomationLibraryOriginalReadOwner actual)
    {
        if (!HasOriginalAutomationLibraryProviderTuple(provider, _services) || !ReferenceEquals(home, _actualWindowsHome) ||
            !ReferenceEquals(actual, _actualAutomationLibraryRead) ||
            !ReferenceEquals(actual.OriginalStore, _actualAssistantSqliteStore) ||
            !ReferenceEquals(actual.OriginalRepository, provider.GetRequiredService<IAutomationRepository>()) ||
            !ReferenceEquals(actual.OriginalOwnership, home.Ownership) ||
            !actual.OriginalStore.HasOriginalProfiles(home.Profiles) ||
            !actual.OriginalRepository.HasOriginalSqliteFactory(_actualAssistantSqliteDatabase!))
            throw new UnauthorizedAccessException("The actual automation library composition changed.");
    }
    private void ConfigureOriginalAutomationLibraryShell(MainWindow window, MainView shell)
    {
        _originalAppWork.RunSynchronous(original => AcquireOriginalAppSynchronous(original, () =>
        {
            original.DemandPublication();
            if (!ReferenceEquals(window.DataContext, shell))
                throw new InvalidOperationException("Retain the actual library window and shell together.");
            var appLifetime = original.Token; var windowLifetime = window.AcquireOriginalWindowLifetime();
            shell.ConfigureOriginalAutomationLibraryFactory(() => AcquireOriginalAssistantCapabilitySingleton<(ICanonicalAutomationLibraryOriginalReadSource? Source, HomeLocalProfileIdentity? Profiles, ICanonicalAutomationDefinitionOriginalProcessSource? Writer)>(() =>
            {
                if (_actualWindowsHome is not { } home) return (null, null, null);
                var provider = _services ?? throw new InvalidOperationException("The actual App provider is unavailable.");
                _ = provider.RequireOriginalWindowsHomeComponents(home);
                var actual = provider.GetRequiredService<CanonicalAutomationLibraryOriginalReadOwner>();
                DemandOriginalAutomationLibraryComposition(provider, home, actual);
                var alias = provider.GetRequiredService<ICanonicalAutomationLibraryOriginalReadSource>();
                if (!ReferenceEquals(alias, actual)) throw new UnauthorizedAccessException("The canonical library alias changed.");
                var writer = provider.GetRequiredService<CanonicalAutomationDefinitionOriginalWriteOwner>();
                DemandOriginalAutomationLibraryWriteComposition(provider, home, writer);
                var process = provider.GetRequiredService<ICanonicalAutomationDefinitionOriginalProcessSource>();
                if (!ReferenceEquals(process, writer)) throw new UnauthorizedAccessException("The actual automation process alias changed.");
                return (alias, home.Profiles, process);
            }), appLifetime, windowLifetime);
            original.DemandPublication(); return true;
        }));
    }
}
#endif
