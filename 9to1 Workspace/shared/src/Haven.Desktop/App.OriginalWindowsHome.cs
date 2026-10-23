#if !ANDROID
using Haven.Application;
using Haven.Infrastructure;
using Haven.Desktop.Services;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Files.NativeHost;
using HavenOS.Apps.Sites.Application;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private HomeNativeWindowsComposition? _actualWindowsHome;
    private bool _windowsHomeBorrowerTransferred;

    private void ConfigureOriginalWindowsHomeRegistrations(IServiceCollection collection)
    {
        _originalAppWork.RunSynchronous(original => AcquireOriginalAppSynchronous(original, () =>
        {
            original.DemandPublication();
            PrepareOriginalWindowsDeveloperPolicies();
            global::Haven.Desktop.Services.WindowsNativePersonalTaskStoreSetupCommand.DemandCurrentConfiguredPath();
            var paths = new AppPaths();
            global::Haven.Desktop.Services.WindowsNativePersonalTaskStoreSetupCommand.DemandSamePaths(paths);
            PrepareOriginalAssistantSqliteComposition(collection, paths);
            var notes = PrepareOriginalNotesComposition(collection, paths);
            var notesEvidence = new NotesLocalStoreEvidenceProvider(notes);
            PrepareOriginalAssistantMiniComputerConfiguration(paths);
            var principal = new OperatingSystemPrincipalSource();
            var ordinary = RetainOriginalAssistantConversationProcess();
            var rootClient = RetainOriginalHomeRootClient(original);
            RetainOriginalNativeHomeStartup(original);
            PrepareOriginalInstalledHomeApps(rootClient);
            OriginalAssistantPersonalDenHost? personalDen = null;
            NativeFilesWorkspaceService? files = null;
            NativeFilesWorkspaceAuthority? authority = null;
            FilesArtifactResourceResolver? resolver = null;
            Services.SitesNativeWorkspaceAuthority? sites = null;
            SiteNativeProjectAccessResolver? sitesResolver = null;
            HomeNotesResourceAdmissionSource? notesAdmission = null;
            var notesResolver = new OriginalNotesResourceResolver(() => notesAdmission
                ?? throw new InvalidOperationException("The SAME original Notes admission owner is not captured."));
            // Retain the actual constructed owner before any registration can fail. This
            // uses the genuine OS principal; CAKE login cannot substitute for this actor.
            _actualWindowsHome = new(new FileHomeCoreStateStore(Path.Combine(paths.DataDirectory, "Home", "state.json")),
                principal, paths, new("9to1.home.desktop.v1"),
                protectedInstalledPeerVerifier: rootClient,
                originalResourceResolvers: OriginalWindowsDeveloperResolvers().Concat(OriginalInstalledHomeAppsResolvers()).Concat([_actualAssistantTaskContextWriteResolver!, _actualAssistantMemoryWriteResolver!, _actualAssistantCapabilityInitializationResolver!, _actualAssistantMiniComputerOperationResolver!, _actualAssistantMiniComputerIdentityResolver!, _actualAutomationLibraryWriteResolver!, _actualAssistantAttachmentReadResolver!, _actualAssistantAttachmentImportResolver!, _actualAssistantAttachmentEgressResolver!, _actualGeneratedUiResolver!, notesResolver]),
                originalActionPolicies: OriginalWindowsDeveloperPolicies().Concat(OriginalInstalledHomeAppsPolicies()).Concat([new FilesNativeBrowserActionPolicySource(), new SiteNativeActionPolicies(), _actualAssistantTaskContextWritePolicy!, _actualAssistantMemoryWritePolicy!, new HomeUcmlContextActionPolicy(), _actualAssistantCapabilityInitializationPolicy!, _actualAssistantMiniComputerOperationPolicy!, _actualAssistantMiniComputerIdentityPolicy!, _actualAutomationLibraryWritePolicy!, _actualAssistantAttachmentReadPolicy!, _actualAssistantAttachmentImportPolicy!, _actualAssistantAttachmentEgressPolicy!, _actualGeneratedUiPolicy!, new HomeModelRouteActionPolicies(), new HomeNotesActionPolicies()]),
                configureOriginalOwners: owners =>
                {
                    // Resources and its broker now exist. The resolver slot above and
                    // both service registries borrow this one original admission source.
                    notesAdmission = new(notes, owners.StateStore, owners.Profiles,
                        owners.Ownership, owners.Resources, owners.Permissions, owners.Broker);
                    var registered = CaptureOriginalHomeRootOwnerComponents(original, owners, rootClient,
                        new SiteNativeAuthoringSession(sites!, owners.Profiles, owners.Resources, owners.Broker))
                        .ToDictionary(pair => pair.Key, pair => pair.Value);
                    registered.Add(typeof(HomeNotesResourceAdmissionSource), notesAdmission);
                    return registered;
                },
                configureOriginalStores: identity =>
                {
                    var sqliteEvidence = CreateOriginalAssistantSqliteEvidence(identity, paths);
                    var miniEvidence = CreateOriginalAssistantMiniComputerCatalogEvidence(identity.Profiles);
                    files = new(identity.StateStore, identity.Profiles);
                    personalDen = RetainOriginalAssistantPersonalDen(paths, identity.Profiles);
                    // Metadata evidence does not initialise, import or bind a Notes store.
                    return new([files, personalDen, miniEvidence, notesEvidence, .. sqliteEvidence], new Dictionary<Type, object>
                    {
                        [typeof(NativeFilesWorkspaceService)] = files,
                        [typeof(OriginalAssistantPersonalDenHost)] = personalDen,
                        [typeof(NotesRepository)] = notes,
                        [typeof(NotesLocalStoreEvidenceProvider)] = notesEvidence
                    });
                },
                configureOriginalResolvers: ownership =>
                {
                    var projectContexts = CreateOriginalAssistantProjectContextReadOwner(ownership);
                    authority = new(files!, ownership.Identity.Profiles, ownership.Ownership);
                    resolver = new(authority);
                    sites = new(authority);
                    sitesResolver = new(sites);
                    return new([resolver, sitesResolver,
                        new HomeModelRouteProfileOwner(ownership.Identity.Profiles),
                        new HomeModelRouteOwner(ownership.Identity.Profiles, new HomeVersionedModelRouteRepository(ownership.Identity.StateStore))], new Dictionary<Type, object>
                    {
                        [typeof(CanonicalProjectContextStoreReadOwner)] = projectContexts,
                        [typeof(ICanonicalProjectContextStoreReadSource)] = projectContexts,
                        [typeof(NativeFilesWorkspaceAuthority)] = authority, [typeof(FilesArtifactResourceResolver)] = resolver,
                        [typeof(ISiteNativeWorkspaceAuthority)] = sites, [typeof(SiteNativeProjectAccessResolver)] = sitesResolver
                    });
                });
            original.DemandPublication();
            collection.AddHavenOwnedWindowsHomeDomain(_actualWindowsHome, paths, principal);
            RegisterOriginalHomeRootOwners(collection, _actualWindowsHome, rootClient);
            collection.AddSingleton(notesAdmission
                ?? throw new InvalidOperationException("The SAME original Notes admission owner is unavailable."));
            collection.AddSingleton(notesEvidence);
            RegisterOriginalBoardsStoreSetup(collection, _actualWindowsHome, notes, notesEvidence);
            collection.AddSingleton(files!); collection.AddSingleton(authority!); collection.AddSingleton(resolver!);
            collection.AddSingleton(personalDen!);
            RegisterOriginalAssistantSqliteOwners(collection);
            collection.AddSingleton(ordinary);
            collection.AddSingleton<AssistantOriginalConversationHost>(ordinary.OriginalHost);
            var actualHome = _actualWindowsHome;
            // Factory registration is lazy: no Den or profile is opened by DI.
            // Its first real acquisition follows started Home and actual store setup.
            collection.AddSingleton<HomePersonalDenFactory>(_ => personalDen!.CreateOriginalFactory(actualHome.Ownership));
            // ONE real Home-owned selection source, shared by the host top-bar, the universal picker
            // and actual request construction. It is wired to the SAME actual store/profiles/resources
            // and the SAME authorised single-Den factory as the rest of the composition, with the
            // genuine current-profile actor source (never a startup-cached actor). There is exactly one
            // singleton; the host consumes this exact instance when present.
            collection.AddSingleton<HomeModelSelectionSource>(provider =>
            {
                var registry = provider.GetRequiredService<IModelProviderRegistry>();
                var routes = new HomeVersionedModelRouteRepository(actualHome.StateStore);
                var personal = new HomePersonalModelRoutes(actualHome.Profiles, routes, actualHome.Resources);
                // Advertised reasoning IDs from the AUTHORISED single personal Den model record
                // (never a second Den), keyed by the GENUINE provider+model pair so another provider's
                // same-named model can never authorise this model's advertised scale. The single Den
                // factory is resolved lazily on use, only after the app lease has actually acquired a
                // personal Den; before any explicit open/create the truthful result is null (unknown),
                // so ordinary Home/Chat start without an assistant and without creating a fake Den.
                Func<string, string, CancellationToken, Task<IReadOnlyList<string>?>> advertisedLevels =
                    async (providerId, modelId, ct) =>
                    {
                        if (personalDen?.HasAcquiredProvider != true)
                            return null;
                        try
                        {
                            var homePersonalDenFactory = provider.GetRequiredService<HomePersonalDenFactory>();
                            var session = await homePersonalDenFactory.OpenAsync(ct);
                            var record = await session.Den.GetModelByID(modelId, cancellationToken: ct);
                            // Guard against a provider-scoped store returning another provider's same-named
                            // model: only accept metadata for the exact resolved provider identity.
                            if (record is not null && !string.IsNullOrWhiteSpace(providerId)
                                && record.Provider is { } recordProvider
                                && !string.Equals(providerId, recordProvider, StringComparison.Ordinal))
                                return null;
                            return record?.ReasoningLevels;
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                        catch { return null; }
                    };
                return new HomeModelSelectionSource(
                    registry, routes,
                    (category, ct) => personal.GetAsync(category, ct),
                    ct => actualHome.Profiles.GetCurrentAsync(ct).AsTask(),
                    advertisedLevels: advertisedLevels,
                    routeProvider: new HomeModelPickerFeatureProvider(
                        actualHome.Profiles, routes,
                        provider.GetRequiredService<IModelProviderRegistry>(),
                        provider.GetRequiredService<IPrivacyPreferenceStore>(),
                        actualHome.Resources, actualHome.Broker));
            });
            // The SAME Home-owned selection source is the typed read owner for actual ChatSessionService
            // request production (the producer seam). It validates current actor/revision, provider/model
            // identity, route/revision and genuine scope before returning the exact advertised reasoning ID.
            collection.AddSingleton<IChatReasoningSelectionReader>(provider =>
                new HomeChatReasoningSelectionReader(provider.GetRequiredService<HomeModelSelectionSource>()));
            collection.AddSingleton<IAssistantOriginalModelSelectionOwner>(provider =>
            {
                var routing = provider.GetRequiredService<ProviderRoutingModelClient>();
                return new AssistantOriginalModelSelectionOwner(RetainOriginalModelCatalogueRegistry(provider),
                    provider.GetRequiredService<IPrivacyPreferenceStore>(), provider.GetRequiredService<ModelPermissionEvaluator>(),
                    actualHome.Profiles, routing.ToCompatibilityDescriptor);
            });
            collection.AddSingleton<ISiteNativeWorkspaceAuthority>(sites!); collection.AddSingleton(sitesResolver!);
            collection.AddSingleton((SiteNativeAuthoringSession)(_actualWindowsHome.Services.GetService(typeof(SiteNativeAuthoringSession))
                ?? throw new InvalidOperationException("The SAME original Sites session is unavailable.")));
            // The maintained Files/Dev registration that follows reuses these exact
            // instances. No Files root, project or permission is created by registration.
            return true;
        }));
    }

    // Replace only the maintained, still-uninstantiated concrete owners. Their
    // existing interface aliases and verified/migrating wrappers keep their authority.
    private NotesRepository PrepareOriginalNotesComposition(IServiceCollection collection, AppPaths paths)
    {
        ServiceDescriptor Demand(Type service, Type implementation)
        {
            var rows = collection.Where(row => row.ServiceType == service).Take(2).ToArray();
            if (rows.Length != 1 || rows[0].Lifetime != ServiceLifetime.Singleton ||
                rows[0].ImplementationType != implementation)
                throw new InvalidOperationException("Use the maintained uninstantiated canonical owner: " + service.Name);
            return rows[0];
        }
        var diagnosticsDescriptor = Demand(typeof(ProductionDiagnostics), typeof(ProductionDiagnostics));
        var validatorDescriptor = Demand(typeof(INotesDocumentValidator), typeof(NotesDocumentValidator));
        var notesDescriptor = Demand(typeof(NotesRepository), typeof(NotesRepository));
        if (_productionDiagnostics is not null || collection.Any(row => row.ServiceType == typeof(HomeNotesResourceAdmissionSource)))
            throw new InvalidOperationException("The original Notes/diagnostics composition already exists.");
        var diagnostics = new ProductionDiagnostics(paths);
        // Retain before any later acquisition/registration can fail. The actual App
        // shutdown path joins this same diagnostics owner after all borrowers settle.
        _productionDiagnostics = diagnostics;
        var validator = new NotesDocumentValidator();
        var notes = new NotesRepository(paths, validator, diagnostics);
        collection.Remove(diagnosticsDescriptor); collection.AddSingleton(diagnostics);
        collection.Remove(validatorDescriptor); collection.AddSingleton<INotesDocumentValidator>(validator);
        collection.Remove(notesDescriptor); collection.AddSingleton(notes);
        return notes;
    }

    private sealed class OriginalNotesResourceResolver(Func<HomeNotesResourceAdmissionSource> original)
        : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => HomeNotesResourceAdmissionSource.ScopeKind;
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor,
            string actionId, ResourceScope scope, CancellationToken cancellationToken) =>
            original().EvaluateAsync(actor, actionId, scope, cancellationToken);
    }

    private Task StartOriginalWindowsHomeAsync()
    {
        var home = _actualWindowsHome;
        if (home is null) return Task.CompletedTask; // Other platform owners are unchanged.
        _ = (_services ?? throw new InvalidOperationException("The original App provider is unavailable."))
            .RequireOriginalWindowsHomeComponents(home);
        if (!OperatingSystem.IsWindows())
            return home.StartOriginalAsync();
        return StartOriginalNativeHomeDependencyAsync(home);
    }

    private void DemandOriginalWindowsHomeRetirementJoin()
    {
        DemandOriginalBoardsStoreSetupRetirementJoin();
        _actualWindowsHome?.DemandExternalOriginalProcessJoin();
        DemandOriginalHomeRootRetirementJoin();
    }

    private async Task JoinOriginalWindowsHomeAfterBorrowersAsync()
    {
        await JoinOriginalBoardsStoreSetupAfterBorrowersAsync();
        await JoinOriginalHomeThenRootAfterBorrowersAsync();
    }

    private Task JoinOriginalUntransferredWindowsHomeAsync()
    {
        lock (_actualStartupAcquisitionGate)
            return _windowsHomeBorrowerTransferred ? Task.CompletedTask : JoinOriginalWindowsHomeAfterBorrowersAsync();
    }
}
#endif
