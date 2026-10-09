#if !ANDROID
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private CanonicalCapabilityCatalogueReadOwner? _actualAssistantCapabilityCatalogue;
    private CanonicalConnectionCapabilityReadOwner? _actualAssistantConnectionCapabilityRead;
    private AssistantOriginalCapabilityOwner? _actualAssistantCapabilityOwner;

    private void ConfigureOriginalAssistantCapabilityOwnerRegistrations(IServiceCollection collection)
    {
        if (_actualWindowsHome is not { } home) return;
        _originalAppWork.RunSynchronous(original => AcquireOriginalAppSynchronous(original, () =>
        {
            original.DemandPublication();
            Type[] owned = [typeof(CanonicalCapabilityCatalogueReadOwner), typeof(ICapabilityOriginalRepositoryReadSource),
                typeof(CanonicalConnectionCapabilityReadOwner),
                typeof(AssistantOriginalCapabilityOwner), typeof(IAssistantOriginalCapabilityOwner),
                typeof(IAssistantOriginalPreparedProjectCapabilityContextOwner),
                typeof(CanonicalCapabilityCatalogueInitializationOwner), typeof(ICapabilityOriginalInitializationSource),
                typeof(ICapabilityOriginalInitializationProcessSource), typeof(HomeCapabilityCatalogueInitializationWriteSource),
                typeof(ICapabilityOriginalInitializationHomeWriteSource), typeof(ICapabilityOriginalInitializationHomeReviewWithdrawalSource)];
            if (collection.Any(row => owned.Contains(row.ServiceType)))
                throw new InvalidOperationException("The SAME protected catalogue and original capability owners cannot be replaced.");

            // This READ owner borrows the maintained catalogue/registry. Construction
            // neither opens Den nor seeds SQLite or discovers remote capabilities.
            collection.AddSingleton<CanonicalCapabilityCatalogueReadOwner>(provider => AcquireOriginalAssistantCapabilitySingleton(() =>
            {
                _ = provider.RequireOriginalWindowsHomeComponents(home);
                if (_actualAssistantCapabilityCatalogue is not null)
                    throw new InvalidOperationException("Retain the actual protected capability READ owner.");
                if (provider.GetRequiredService<ICapabilityRepository>() is not CapabilityRepository repository)
                    throw new UnauthorizedAccessException("The actual maintained SQLite capability repository is required.");
                var registry = provider.GetRequiredService<CapabilityRegistryService>();
                var actual = _actualAssistantCapabilityCatalogue = new(_actualAssistantSqliteStore!,
                    _actualAssistantSqliteDatabase!, provider.GetRequiredService<IAppPaths>(),
                    repository, registry, home.Ownership);
                BindOriginalAssistantConnectionCapabilityRead(provider, actual, registry);
                return actual;
            }));
            collection.AddSingleton<CanonicalConnectionCapabilityReadOwner>(provider => AcquireOriginalAssistantCapabilitySingleton(() =>
            {
                var catalogue = provider.GetRequiredService<CanonicalCapabilityCatalogueReadOwner>();
                var actual = _actualAssistantConnectionCapabilityRead ??
                    throw new InvalidOperationException("Retain the SAME actual protected connected-capability READ owner.");
                DemandOriginalAssistantConnectionCapabilityComposition(provider, catalogue, catalogue.OriginalRegistry, actual);
                return actual;
            }));
            collection.AddSingleton<ICapabilityOriginalRepositoryReadSource>(provider =>
                provider.GetRequiredService<CanonicalCapabilityCatalogueReadOwner>());
            if (WindowsColdProjectRequested)
                collection.AddSingleton<IAssistantOriginalPreparedProjectCapabilityContextOwner>(provider =>
                    provider.GetRequiredService<HavenOS.Apps.Assistants.Canonical.DenAssistantOriginalDevelopmentOwner>());

            // Only the genuine dependency/actor/Den acquisition resolves this factory.
            // The optional prepared-input port is the SAME process owner; no new input,
            // synthetic cold request or additional READ approval is created here.
            ConfigureOriginalAssistantCapabilityInitializationRegistrations(collection, home);
            collection.AddSingleton<AssistantOriginalCapabilityOwner>(provider => AcquireOriginalAssistantCapabilitySingleton(() =>
            {
                _ = provider.RequireOriginalWindowsHomeComponents(home);
                if (_actualAssistantCapabilityOwner is not null)
                    throw new InvalidOperationException("Retain the actual capability source and its original close.");
                var factory = provider.GetRequiredService<HomePersonalDenFactory>();
                var catalogue = provider.GetRequiredService<CanonicalCapabilityCatalogueReadOwner>();
                var actual = _actualAssistantCapabilityOwner = new(factory,
                    provider.GetRequiredService<IConversationRepository>(), provider.GetRequiredService<CapabilityRegistryService>(),
                    provider.GetRequiredService<ChatSessionService>(), catalogue,
                    provider.GetService<IAssistantOriginalPreparedProjectCapabilityContextOwner>(),
                    provider.GetRequiredService<ICapabilityOriginalInitializationProcessSource>());
                DemandOriginalAssistantCapabilityComposition(provider, home, factory, actual);
                return actual;
            }));
            collection.AddSingleton<IAssistantOriginalCapabilityOwner>(provider =>
                provider.GetRequiredService<AssistantOriginalCapabilityOwner>());
            return true;
        }));
    }

    private T AcquireOriginalAssistantCapabilitySingleton<T>(Func<T> actualSource)
    {
        T actual = default!;
        _originalAppWork.RunSynchronous(original =>
        {
            original.DemandPublication();
            actual = AcquireOriginalAppSynchronous(original, actualSource);
            original.DemandPublication();
        });
        return actual;
    }

    private void DemandOriginalAssistantCapabilityComposition(IServiceProvider provider,
        HomeNativeWindowsComposition home, HomePersonalDenFactory factory, AssistantOriginalCapabilityOwner actual)
    {
        var catalogue = _actualAssistantCapabilityCatalogue;
        var contexts = provider.GetService<IAssistantOriginalPreparedProjectCapabilityContextOwner>();
        if (!ReferenceEquals(provider, _services) || !ReferenceEquals(home, _actualWindowsHome) ||
            !ReferenceEquals(actual, _actualAssistantCapabilityOwner) || actual.OriginalClose is not null ||
            catalogue is null || !ReferenceEquals(catalogue.OriginalStore, _actualAssistantSqliteStore) ||
            !ReferenceEquals(catalogue.OriginalOwnership, home.Ownership) ||
            !catalogue.HasOriginalCapabilityRepository(provider.GetRequiredService<ICapabilityRepository>()) ||
            !ReferenceEquals(catalogue.OriginalRegistry, provider.GetRequiredService<CapabilityRegistryService>()) ||
            !ReferenceEquals(provider.GetRequiredService<ICapabilityOriginalRepositoryReadSource>(), catalogue) ||
            !actual.HasOriginalComposition(factory, provider.GetRequiredService<IConversationRepository>(),
                provider.GetRequiredService<CapabilityRegistryService>(), provider.GetRequiredService<ChatSessionService>(), catalogue, contexts) ||
            (WindowsColdProjectRequested && (contexts is null || !ReferenceEquals(contexts, _actualAssistantDevelopmentOwner))) ||
            (!WindowsColdProjectRequested && contexts is not null))
            throw new UnauthorizedAccessException("Use the SAME actual App/Home/Den/Chat/catalogue/project-input owners.");
        DemandOriginalAssistantConnectionCapabilityComposition(provider, catalogue, catalogue.OriginalRegistry,
            provider.GetRequiredService<CanonicalConnectionCapabilityReadOwner>());
        DemandOriginalAssistantCapabilityInitializationComposition(provider, home, catalogue,
            provider.GetRequiredService<CanonicalCapabilityCatalogueInitializationOwner>());
        if (!ReferenceEquals(actual.OriginalInitializationSource, _actualAssistantCapabilityInitialization))
            throw new UnauthorizedAccessException("Use the SAME actual process-owned capability setup source.");
        // During the singleton factory its interface alias cannot be recursively
        // resolved. The actual acquisition checks that alias after factory completion.
    }

    private void BindOriginalAssistantConnectionCapabilityRead(IServiceProvider provider,
        CanonicalCapabilityCatalogueReadOwner catalogue, CapabilityRegistryService registry)
    {
        if (_actualAssistantConnectionCapabilityRead is not null || registry.OriginalDynamicReadSources.Count != 0)
            throw new InvalidOperationException("Protected dynamic READ composition must be bound once before original discovery.");
        if (provider.GetRequiredService<IExternalConnectionRepository>() is not ExternalConnectionRepository connections ||
            provider.GetRequiredService<IPlannerRepository>() is not PlannerRepository planner)
            throw new UnauthorizedAccessException("The SAME maintained canonical SQLite connection and planner repositories are required.");
        // Resolve the maintained registered instance. This invokes no discovery,
        // remote service, repository read, schema initialization or seed.
        var configured = provider.GetServices<IDynamicCapabilityProvider>().Take(65).ToArray();
        var matching = configured.OfType<ConnectionCapabilityProvider>().ToArray();
        if (configured.Length > 64 || configured.Any(value => value is null) ||
            configured.Distinct(ReferenceEqualityComparer.Instance).Count() != configured.Length || matching.Length != 1)
            throw new UnauthorizedAccessException("Use exactly the SAME maintained connection provider in the bounded actual provider set.");
        var actual = _actualAssistantConnectionCapabilityRead = new(catalogue, _actualAssistantSqliteDatabase!,
            connections, planner, matching[0]);
        registry.BindOriginalDynamicReadSources([actual]);
        DemandOriginalAssistantConnectionCapabilityComposition(provider, catalogue, registry, actual);
    }

    private void DemandOriginalAssistantConnectionCapabilityComposition(IServiceProvider provider,
        CanonicalCapabilityCatalogueReadOwner catalogue, CapabilityRegistryService registry,
        CanonicalConnectionCapabilityReadOwner actual)
    {
        var configured = provider.GetServices<IDynamicCapabilityProvider>().Take(65).ToArray();
        var reads = registry.OriginalDynamicReadSources;
        if (!ReferenceEquals(provider, _services) || !ReferenceEquals(catalogue, _actualAssistantCapabilityCatalogue) ||
            !ReferenceEquals(actual, _actualAssistantConnectionCapabilityRead) ||
            !ReferenceEquals(actual.OriginalCatalogue, catalogue) ||
            !ReferenceEquals(catalogue.OriginalRegistry, registry) ||
            !ReferenceEquals(registry, provider.GetRequiredService<CapabilityRegistryService>()) ||
            !ReferenceEquals(actual.OriginalConnections, provider.GetRequiredService<IExternalConnectionRepository>()) ||
            !ReferenceEquals(actual.OriginalPlanner, provider.GetRequiredService<IPlannerRepository>()) ||
            configured.Length > 64 || configured.Count(value => ReferenceEquals(value, actual.OriginalProvider)) != 1 ||
            !actual.HasOriginalDynamicProvider(actual.OriginalProvider) || reads.Count != 1 || !ReferenceEquals(reads[0], actual))
            throw new UnauthorizedAccessException("Use the SAME protected catalogue/registry/repositories/maintained provider and its original dynamic READ source.");
        // This metadata owner borrows the SAME generic-store source lifetime.
        // Capability commands are joined before that store retires; no second
        // owner, ordinary discovery call or independent disposal is introduced.
    }

    private void DemandOriginalAssistantCapabilityInputsJoin()
    {
        _actualAssistantCapabilityOwner?.DemandExternalOriginalRetirementJoin();
        _actualAssistantCapabilityInitialization?.DemandExternalOriginalJoin();
        _actualAssistantCapabilityInitializationWrites?.DemandExternalOriginalJoin();
    }
}
#endif
