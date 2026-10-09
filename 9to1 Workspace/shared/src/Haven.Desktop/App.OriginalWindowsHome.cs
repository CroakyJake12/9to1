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
        if (!OperatingSystem.IsWindows()) return;
        _originalAppWork.RunSynchronous(original => AcquireOriginalAppSynchronous(original, () =>
        {
            original.DemandPublication();
            PrepareOriginalWindowsDeveloperPolicies();
            global::Haven.Desktop.Services.WindowsNativePersonalTaskStoreSetupCommand.DemandCurrentConfiguredPath();
            var paths = new AppPaths();
            global::Haven.Desktop.Services.WindowsNativePersonalTaskStoreSetupCommand.DemandSamePaths(paths);
            PrepareOriginalAssistantSqliteComposition(collection, paths);
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
            // Retain the actual constructed owner before any registration can fail. This
            // uses the genuine OS principal; CAKE login cannot substitute for this actor.
            _actualWindowsHome = new(new FileHomeCoreStateStore(Path.Combine(paths.DataDirectory, "Home", "state.json")),
                principal, paths, new("9to1.home.desktop.v1"),
                protectedInstalledPeerVerifier: rootClient,
                originalResourceResolvers: OriginalWindowsDeveloperResolvers().Concat(OriginalInstalledHomeAppsResolvers()).Concat([_actualAssistantTaskContextWriteResolver!, _actualAssistantMemoryWriteResolver!, _actualAssistantCapabilityInitializationResolver!, _actualAssistantMiniComputerOperationResolver!, _actualAssistantMiniComputerIdentityResolver!, _actualAutomationLibraryWriteResolver!, _actualAssistantAttachmentReadResolver!, _actualAssistantAttachmentImportResolver!, _actualAssistantAttachmentEgressResolver!, _actualGeneratedUiResolver!]),
                originalActionPolicies: OriginalWindowsDeveloperPolicies().Concat(OriginalInstalledHomeAppsPolicies()).Concat([new FilesNativeBrowserActionPolicySource(), new SiteNativeActionPolicies(), _actualAssistantTaskContextWritePolicy!, _actualAssistantMemoryWritePolicy!, _actualAssistantCapabilityInitializationPolicy!, _actualAssistantMiniComputerOperationPolicy!, _actualAssistantMiniComputerIdentityPolicy!, _actualAutomationLibraryWritePolicy!, _actualAssistantAttachmentReadPolicy!, _actualAssistantAttachmentImportPolicy!, _actualAssistantAttachmentEgressPolicy!, _actualGeneratedUiPolicy!]),
                configureOriginalOwners: owners => CaptureOriginalHomeRootOwnerComponents(original, owners, rootClient,
                    new SiteNativeAuthoringSession(sites!, owners.Profiles, owners.Resources, owners.Broker)),
                configureOriginalStores: identity =>
                {
                    var sqliteEvidence = CreateOriginalAssistantSqliteEvidence(identity, paths);
                    var miniEvidence = CreateOriginalAssistantMiniComputerCatalogEvidence(identity.Profiles);
                    files = new(identity.StateStore, identity.Profiles);
                    personalDen = RetainOriginalAssistantPersonalDen(paths, identity.Profiles);
                    return new([files, personalDen, miniEvidence, .. sqliteEvidence], new Dictionary<Type, object>
                    {
                        [typeof(NativeFilesWorkspaceService)] = files,
                        [typeof(OriginalAssistantPersonalDenHost)] = personalDen
                    });
                },
                configureOriginalResolvers: ownership =>
                {
                    var projectContexts = CreateOriginalAssistantProjectContextReadOwner(ownership);
                    authority = new(files!, ownership.Identity.Profiles, ownership.Ownership);
                    resolver = new(authority);
                    sites = new(authority);
                    sitesResolver = new(sites);
                    return new([resolver, sitesResolver], new Dictionary<Type, object>
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
            collection.AddSingleton(files!); collection.AddSingleton(authority!); collection.AddSingleton(resolver!);
            collection.AddSingleton(personalDen!);
            RegisterOriginalAssistantSqliteOwners(collection);
            collection.AddSingleton(ordinary);
            collection.AddSingleton<AssistantOriginalConversationHost>(ordinary.OriginalHost);
            var actualHome = _actualWindowsHome;
            // Factory registration is lazy: no Den or profile is opened by DI.
            // Its first real acquisition follows started Home and actual store setup.
            collection.AddSingleton<HomePersonalDenFactory>(_ => personalDen!.CreateOriginalFactory(actualHome.Ownership));
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

    private Task StartOriginalWindowsHomeAsync()
    {
        var home = _actualWindowsHome;
        if (home is null) return Task.CompletedTask; // Other platform owners are unchanged.
        _ = (_services ?? throw new InvalidOperationException("The original App provider is unavailable."))
            .RequireOriginalWindowsHomeComponents(home);
        return StartOriginalNativeHomeDependencyAsync(home);
    }

    private void DemandOriginalWindowsHomeRetirementJoin()
    {
        _actualWindowsHome?.DemandExternalOriginalProcessJoin();
        DemandOriginalHomeRootRetirementJoin();
    }

    private Task JoinOriginalWindowsHomeAfterBorrowersAsync() =>
        JoinOriginalHomeThenRootAfterBorrowersAsync();

    private Task JoinOriginalUntransferredWindowsHomeAsync()
    {
        lock (_actualStartupAcquisitionGate)
            return _windowsHomeBorrowerTransferred ? Task.CompletedTask : JoinOriginalWindowsHomeAfterBorrowersAsync();
    }
}
#endif
