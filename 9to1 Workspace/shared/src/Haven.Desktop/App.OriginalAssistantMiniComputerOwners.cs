#if !ANDROID
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Assistants;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Assistants.MiniComputer;
using HavenOS.Apps.MiniComputer;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private JsonMiniComputerCatalogStore? _actualAssistantMiniComputerStore;
    private JsonProviderIdentityMapStore? _actualAssistantMiniComputerIdentityMap;
    private LocalVirtualDiskLocationProvider? _actualAssistantMiniComputerDiskLocations;
    private VirtualBoxCliProvider? _actualAssistantMiniComputerProvider;
    private VirtualisationProviderRegistry? _actualAssistantMiniComputerRegistry;
    private DenyWithoutHomeAuthorization? _actualAssistantMiniComputerLegacyAuthorization;
    private MiniComputerEngine? _actualAssistantMiniComputerEngine;
    private CanonicalMiniComputerCatalogOriginalOwner? _actualAssistantMiniComputerCatalog;
    private AssistantMiniComputerSource? _actualAssistantMiniComputerSource;
    private HomeCanonicalMiniComputerOperationSource? _actualAssistantMiniComputerOperations;
    private HomeCanonicalMiniComputerCatalogIdentitySource? _actualAssistantMiniComputerIdentityWrites;
    private HomeOriginalLocalStoreImportSession? _actualAssistantMiniComputerImports;
    private HomeMiniComputerOperationResourceResolver? _actualAssistantMiniComputerOperationResolver;
    private HomeMiniComputerOperationActionPolicySource? _actualAssistantMiniComputerOperationPolicy;
    private HomeMiniComputerCatalogIdentityResourceResolver? _actualAssistantMiniComputerIdentityResolver;
    private HomeMiniComputerCatalogIdentityActionPolicySource? _actualAssistantMiniComputerIdentityPolicy;
    private Task? _actualAssistantMiniComputerDrain;

    private void PrepareOriginalAssistantMiniComputerConfiguration(IAppPaths samePaths)
    {
        if (_actualAssistantMiniComputerEngine is not null)
            throw new InvalidOperationException("Retain the SAME actual configured Mini Computer engine.");
        // These maintained constructors only capture paths/configuration. They do not
        // discover a provider, read/write a catalogue, allocate a disk or create a VM.
        _actualAssistantMiniComputerStore = new(Path.Combine(samePaths.DataDirectory, "mini-computer.catalog.json"));
        _actualAssistantMiniComputerIdentityMap = new(Path.Combine(samePaths.DataDirectory, "mini-computer.provider-identities.json"));
        _actualAssistantMiniComputerDiskLocations = new(Path.Combine(samePaths.DataDirectory, "MiniComputer", "Disks"));
        _actualAssistantMiniComputerProvider = VirtualBoxCliProvider.CreateOriginalDeferred(
            _actualAssistantMiniComputerIdentityMap, _actualAssistantMiniComputerDiskLocations);
        _actualAssistantMiniComputerRegistry = new([_actualAssistantMiniComputerProvider], VirtualBoxCliProvider.DefaultProviderID);
        _actualAssistantMiniComputerLegacyAuthorization = new();
        _actualAssistantMiniComputerEngine = new(_actualAssistantMiniComputerRegistry,
            _actualAssistantMiniComputerStore, _actualAssistantMiniComputerLegacyAuthorization);
        _actualAssistantMiniComputerOperationResolver = new(() => _actualAssistantMiniComputerOperations
            ?? throw new InvalidOperationException("The SAME original Mini Computer Home operation owner is not captured."));
        _actualAssistantMiniComputerOperationPolicy = new();
        _actualAssistantMiniComputerIdentityResolver = new(() => _actualAssistantMiniComputerIdentityWrites
            ?? throw new InvalidOperationException("The SAME original Mini Computer catalogue identity Home owner is not captured."));
        _actualAssistantMiniComputerIdentityPolicy = new();
        // This external provider is not the separately maintained bundled provider.
        // Missing catalogue identity/provider/viewer remains source-observed setup.
    }

    private IHomeLocalStoreEvidenceProvider CreateOriginalAssistantMiniComputerCatalogEvidence(HomeLocalProfileIdentity sameProfiles)
    {
        if (_actualAssistantMiniComputerCatalog is not null || _actualAssistantMiniComputerStore is null)
            throw new InvalidOperationException("Retain one configured Mini Computer catalogue before Home publication.");
        return _actualAssistantMiniComputerCatalog = new(_actualAssistantMiniComputerStore, sameProfiles);
    }

    private T AcquireOriginalAssistantMiniComputerSingleton<T>(Func<T> acquire)
    {
        T actual = default!;
        _originalAppWork.RunSynchronous(original =>
        {
            original.DemandPublication();
            actual = AcquireOriginalAppSynchronous(original, acquire);
            original.DemandPublication();
        });
        return actual;
    }

    private void ConfigureOriginalAssistantMiniComputerOwnerRegistrations(IServiceCollection collection)
    {
        if (_actualWindowsHome is not { } home) return;
        _originalAppWork.RunSynchronous(original => AcquireOriginalAppSynchronous(original, () =>
        {
            original.DemandPublication();
            Type[] owned = [typeof(MiniComputerEngine), typeof(IMiniComputerCatalogStore), typeof(JsonMiniComputerCatalogStore),
                typeof(IVirtualisationProviderRegistry), typeof(VirtualisationProviderRegistry), typeof(VirtualBoxCliProvider),
                typeof(CanonicalMiniComputerCatalogOriginalOwner), typeof(AssistantMiniComputerSource),
                typeof(ICanonicalMiniComputerOperationSource), typeof(HomeCanonicalMiniComputerOperationSource),
                typeof(ICanonicalMiniComputerHomeOperationSource), typeof(ICanonicalMiniComputerCatalogIdentitySource),
                typeof(HomeCanonicalMiniComputerCatalogIdentitySource), typeof(ICanonicalMiniComputerCatalogIdentityHomeSource)];
            if (collection.Any(row => owned.Contains(row.ServiceType)))
                throw new InvalidOperationException("The SAME maintained Mini Computer owners cannot be replaced or duplicated.");
            var store = _actualAssistantMiniComputerStore ?? throw new InvalidOperationException("No actual Mini Computer catalogue configuration.");
            var engine = _actualAssistantMiniComputerEngine ?? throw new InvalidOperationException("No actual Mini Computer engine configuration.");
            var catalog = _actualAssistantMiniComputerCatalog ?? throw new InvalidOperationException("No actual protected Mini Computer catalogue evidence.");
            collection.AddSingleton(store); collection.AddSingleton<IMiniComputerCatalogStore>(store);
            collection.AddSingleton(_actualAssistantMiniComputerProvider!);
            collection.AddSingleton(_actualAssistantMiniComputerRegistry!);
            collection.AddSingleton<IVirtualisationProviderRegistry>(_actualAssistantMiniComputerRegistry!);
            collection.AddSingleton(engine); collection.AddSingleton(catalog);
            collection.AddSingleton<ICanonicalMiniComputerCatalogIdentitySource>(catalog);
            collection.AddSingleton<AssistantMiniComputerSource>(provider => AcquireOriginalAssistantMiniComputerSingleton(() =>
            {
                _ = provider.RequireOriginalWindowsHomeComponents(home);
                if (_actualAssistantMiniComputerSource is not null || _actualAssistantMiniComputerOperations is not null ||
                    _actualAssistantMiniComputerIdentityWrites is not null)
                    throw new InvalidOperationException("Retain the actual Mini Computer source and Home operation owner.");
                // Lazy acquisition follows real installed dependency + Home/Den open.
                // No Chat constructor alias, eager Den, IO or permission creation.
                var factory = provider.GetRequiredService<HomePersonalDenFactory>();
                // The existing catalogue owns this distinct, manually reviewed identity-only
                // WRITE. Import and operation grants cannot lend it permission.
                var identityWrites = _actualAssistantMiniComputerIdentityWrites = new(home.StateStore, home.Profiles,
                    home.Resources, home.Broker, home.Permissions, catalog);
                catalog.BindOriginalIdentitySetupHomeSource(identityWrites);
                var source = _actualAssistantMiniComputerSource = new(factory,
                    provider.GetRequiredService<IConversationRepository>(), engine, catalog, home.Profiles, home.Ownership);
                var operations = _actualAssistantMiniComputerOperations = new(home.StateStore, home.Profiles,
                    home.Resources, home.Broker, home.Permissions, source);
                source.BindOriginalHomeOperationSource(operations);
                var imports = _actualAssistantMiniComputerImports = new(CanonicalMiniComputerCatalogOriginalOwner.CatalogResourceKind,
                    catalog, home.Profiles, home.LocalStoreOwnership, catalog, home.Permissions, home.Ownership);
                source.BindOriginalMiniComputerImportSession(imports);
                DemandOriginalAssistantMiniComputerComposition(provider, home, factory, source);
                return source;
            }));
            collection.AddSingleton<ICanonicalMiniComputerOperationSource>(provider => provider.GetRequiredService<AssistantMiniComputerSource>());
            collection.AddSingleton<HomeCanonicalMiniComputerOperationSource>(provider => AcquireOriginalAssistantMiniComputerSingleton(() =>
            {
                _ = provider.GetRequiredService<AssistantMiniComputerSource>();
                return _actualAssistantMiniComputerOperations ?? throw new InvalidOperationException("No actual Mini Computer Home operation source.");
            }));
            collection.AddSingleton<ICanonicalMiniComputerHomeOperationSource>(provider => provider.GetRequiredService<HomeCanonicalMiniComputerOperationSource>());
            collection.AddSingleton<HomeCanonicalMiniComputerCatalogIdentitySource>(provider => AcquireOriginalAssistantMiniComputerSingleton(() =>
            {
                _ = provider.GetRequiredService<AssistantMiniComputerSource>();
                return _actualAssistantMiniComputerIdentityWrites ?? throw new InvalidOperationException("No actual Mini Computer catalogue identity Home owner.");
            }));
            collection.AddSingleton<ICanonicalMiniComputerCatalogIdentityHomeSource>(provider => provider.GetRequiredService<HomeCanonicalMiniComputerCatalogIdentitySource>());
            return true;
        }));
    }

    private void DemandOriginalAssistantMiniComputerComposition(IServiceProvider provider,
        HomeNativeWindowsComposition home, HomePersonalDenFactory factory, AssistantMiniComputerSource source)
    {
        if (!ReferenceEquals(provider, _services) || !ReferenceEquals(home, _actualWindowsHome) ||
            !ReferenceEquals(source, _actualAssistantMiniComputerSource) || source.OriginalClose is not null ||
            _actualAssistantMiniComputerEngine is not { } engine || _actualAssistantMiniComputerStore is not { } store ||
            _actualAssistantMiniComputerCatalog is not { } catalog || catalog.OriginalClose is not null ||
            _actualAssistantMiniComputerOperations is not { } operations || operations.OriginalClose is not null ||
            _actualAssistantMiniComputerIdentityWrites is not { } identityWrites || identityWrites.OriginalClose is not null ||
            _actualAssistantMiniComputerImports is not { } imports || imports.OriginalClose is not null ||
            _actualAssistantMiniComputerProvider is not { } actualProvider || actualProvider.OriginalClose is not null ||
            !ReferenceEquals(provider.GetRequiredService<MiniComputerEngine>(), engine) ||
            !ReferenceEquals(provider.GetRequiredService<JsonMiniComputerCatalogStore>(), store) ||
            !ReferenceEquals(provider.GetRequiredService<IMiniComputerCatalogStore>(), store) ||
            !ReferenceEquals(provider.GetRequiredService<CanonicalMiniComputerCatalogOriginalOwner>(), catalog) ||
            !ReferenceEquals(provider.GetRequiredService<VirtualBoxCliProvider>(), actualProvider) ||
            !ReferenceEquals(provider.GetRequiredService<IVirtualisationProviderRegistry>(), _actualAssistantMiniComputerRegistry) ||
            !ReferenceEquals(engine.OriginalProviderRegistry, _actualAssistantMiniComputerRegistry) ||
            !ReferenceEquals(engine.OriginalCatalogStore, store) ||
            !ReferenceEquals(engine.OriginalHighRiskActionAuthorizer, _actualAssistantMiniComputerLegacyAuthorization) ||
            !engine.HasOriginalComposition(_actualAssistantMiniComputerRegistry!, store) ||
            !engine.HasOriginalOperationSource(source) ||
            !catalog.HasOriginalComposition(store, home.Profiles) ||
            !source.HasOriginalComposition(factory, provider.GetRequiredService<IConversationRepository>(), engine,
                catalog, home.Profiles, home.Ownership) || !source.HasOriginalHomeOperationSource(operations) ||
            !operations.HasOriginalComposition(home.StateStore, home.Profiles, source) ||
            !ReferenceEquals(operations.OriginalProducer, source) ||
            !ReferenceEquals(provider.GetRequiredService<ICanonicalMiniComputerCatalogIdentitySource>(), catalog) ||
            !catalog.HasOriginalIdentitySetupHomeSource(identityWrites) ||
            !identityWrites.HasOriginalComposition(home.StateStore, home.Profiles, catalog) ||
            !ReferenceEquals(identityWrites.OriginalProducer, catalog) ||
            !imports.IsOriginalSource(catalog, catalog, home.Profiles, home.Ownership) ||
            !source.HasOriginalMiniComputerImportSession(imports))
            throw new UnauthorizedAccessException("Use the SAME actual App/Home/Den/Mini engine/provider/catalogue/operation/import owners.");
        // Interface aliases are checked after factory completion, avoiding recursive DI.
    }

    private IAssistantMiniComputerController AcquireOriginalAssistantMiniComputerManagement(IServiceProvider provider,
        HomeNativeWindowsComposition home, HomePersonalDenFactory factory, AssistantsWorkspaceController controller)
    {
        var source = provider.GetRequiredService<AssistantMiniComputerSource>();
        DemandOriginalAssistantMiniComputerComposition(provider, home, factory, source);
        if (!ReferenceEquals(provider.GetRequiredService<ICanonicalMiniComputerOperationSource>(), source) ||
            !ReferenceEquals(provider.GetRequiredService<ICanonicalMiniComputerHomeOperationSource>(), _actualAssistantMiniComputerOperations) ||
            !ReferenceEquals(provider.GetRequiredService<HomeCanonicalMiniComputerCatalogIdentitySource>(), _actualAssistantMiniComputerIdentityWrites) ||
            !ReferenceEquals(provider.GetRequiredService<ICanonicalMiniComputerCatalogIdentityHomeSource>(), _actualAssistantMiniComputerIdentityWrites))
            throw new UnauthorizedAccessException("Retain the SAME actual Mini Computer producer and Home operation aliases.");
        return new AssistantMiniComputerController(source, controller);
    }

    private void RequestOriginalAssistantMiniComputerPendingReviewWithdrawals(List<Exception> failures)
    {
        void Request(Action request, Func<Task?> observe)
        {
            Task? raw = null;
            try { _originalAppWork.RunCloseCallback(() =>
            {
                try { request(); }
                finally { raw = observe(); }
            }); }
            catch (Exception cause) { AddAppCause(failures, cause); }
            // Retain before page/controller joins, even if the request callback failed.
            if (raw is not null)
                lock (_actualStartupAcquisitionGate) _actualAssistantAcquisitionSources.Add(raw);
        }
        if (_actualAssistantMiniComputerOperations is { } operations)
            Request(operations.RequestOriginalPendingReviewWithdrawals, () => operations.OriginalPendingReviewWithdrawalTask);
        if (_actualAssistantMiniComputerIdentityWrites is { } identityWrites)
            Request(identityWrites.RequestOriginalPendingReviewWithdrawals, () => identityWrites.OriginalPendingReviewWithdrawalTask);
    }

    private void DemandOriginalAssistantMiniComputerInputsJoin()
    {
        _actualAssistantMiniComputerSource?.DemandExternalOriginalRetirementJoin();
        _actualAssistantMiniComputerOperations?.DemandExternalOriginalJoin();
        _actualAssistantMiniComputerIdentityWrites?.DemandExternalOriginalJoin();
        _actualAssistantMiniComputerImports?.DemandExternalOriginalRetirementJoin();
        _actualAssistantMiniComputerCatalog?.DemandExternalOriginalRetirementJoin();
        _actualAssistantMiniComputerProvider?.DemandExternalOriginalRetirementJoin();
    }

    private Task JoinOriginalAssistantMiniComputerAfterBorrowersAsync()
    {
        DemandOriginalAssistantMiniComputerInputsJoin();
        TaskCompletionSource start; Task actual;
        lock (_actualStartupAcquisitionGate)
        {
            if (_actualAssistantMiniComputerDrain is { } same) return same;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = _actualAssistantMiniComputerDrain = JoinOriginalAssistantMiniComputerAfterBorrowersCoreAsync(start.Task);
        }
        start.SetResult(); return actual;
    }

    private async Task JoinOriginalAssistantMiniComputerAfterBorrowersCoreAsync(Task start)
    {
        await start;
        OriginalAssistantsAcquisition[] acquisitions; NativeAssistantsDesktopPage[] pages;
        lock (_actualStartupAcquisitionGate)
        { acquisitions = _actualAssistantAcquisitions.ToArray(); pages = _actualAssistantPresentations.ToArray(); }
        if ((_actualAssistantConversationProcess is { } business && business.OriginalClose?.IsCompletedSuccessfully != true) ||
            pages.Any(page => page.OriginalClose?.IsCompletedSuccessfully != true) ||
            acquisitions.Any(actual => actual.OriginalController.OriginalClose?.IsCompletedSuccessfully != true ||
                (actual.OriginalMiniComputerManagement is { } mini && mini.OriginalClose?.IsCompletedSuccessfully != true)))
            throw new InvalidOperationException("The actual Mini Computer borrowers have not settled; retain the SAME source/Home/catalogue/provider.");
        var failures = new List<Exception>();
        async Task Join(Func<Task> acquire)
        {
            Task? raw = null;
            try { _originalAppWork.RunCloseCallback(() => raw = acquire()); }
            catch (Exception cause) { AddAppCause(failures, cause); }
            if (raw is not null) await JoinOriginalAppTaskAsync(raw, failures);
            ThrowAppCauses(failures);
        }
        // Both issuer withdrawals are acquired before either join. Neither withdraws
        // approved work, changes catalogue bytes, or requests a VM stop.
        var withdrawals = new List<Task>();
        void RequestWithdrawal(Action request, Func<Task?> observe)
        {
            Task? raw = null;
            try { _originalAppWork.RunCloseCallback(() =>
            {
                try { request(); } finally { raw = observe(); }
            }); }
            catch (Exception cause) { AddAppCause(failures, cause); }
            if (raw is not null)
            {
                withdrawals.Add(raw);
                lock (_actualStartupAcquisitionGate) _actualAssistantAcquisitionSources.Add(raw);
            }
        }
        if (_actualAssistantMiniComputerOperations is { } pendingOwner)
            RequestWithdrawal(pendingOwner.RequestOriginalPendingReviewWithdrawals, () => pendingOwner.OriginalPendingReviewWithdrawalTask);
        if (_actualAssistantMiniComputerIdentityWrites is { } pendingIdentityOwner)
            RequestWithdrawal(pendingIdentityOwner.RequestOriginalPendingReviewWithdrawals, () => pendingIdentityOwner.OriginalPendingReviewWithdrawalTask);
        foreach (var raw in withdrawals) await JoinOriginalAppTaskAsync(raw, failures);
        ThrowAppCauses(failures);
        if (_actualAssistantMiniComputerSource is { } source) await Join(source.CloseAndDrainAsync);
        if (_actualAssistantMiniComputerOperations is { } operations) await Join(operations.CloseAndDrainOriginalAsync);
        if (_actualAssistantMiniComputerIdentityWrites is { } identityWrites) await Join(identityWrites.CloseAndDrainOriginalAsync);
        if (_actualAssistantMiniComputerImports is { } imports) await Join(imports.CloseAndDrainAsync);
        if (_actualAssistantMiniComputerCatalog is { } catalog) await Join(catalog.CloseAndDrainOriginalAsync);
        if (_actualAssistantMiniComputerProvider is { } provider) await Join(provider.CloseAndDrainOriginalAsync);
        // Closing view/application handles NEVER requests VM stop, shutdown or power off.
    }
}
#endif
