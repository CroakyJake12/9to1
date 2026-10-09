#if !ANDROID
using Haven.Application;
using Haven.Infrastructure;
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
            Services.WindowsNativePersonalTaskStoreSetupCommand.DemandCurrentConfiguredPath();
            var paths = new AppPaths();
            Services.WindowsNativePersonalTaskStoreSetupCommand.DemandSamePaths(paths);
            var principal = new OperatingSystemPrincipalSource();
            NativeFilesWorkspaceService? files = null;
            NativeFilesWorkspaceAuthority? authority = null;
            FilesArtifactResourceResolver? resolver = null;
            Services.SitesNativeWorkspaceAuthority? sites = null;
            SiteNativeProjectAccessResolver? sitesResolver = null;
            // Retain the actual constructed owner before any registration can fail. This
            // uses the genuine OS principal; CAKE login cannot substitute for this actor.
            _actualWindowsHome = new(new FileHomeCoreStateStore(Path.Combine(paths.DataDirectory, "Home", "state.json")),
                principal, paths, new("9to1.home.desktop.v1"),
                originalResourceResolvers: OriginalWindowsDeveloperResolvers(),
                originalActionPolicies: OriginalWindowsDeveloperPolicies().Concat([new FilesNativeBrowserActionPolicySource(), new SiteNativeActionPolicies()]),
                configureOriginalOwners: owners => new Dictionary<Type, object>
                {
                    [typeof(SiteNativeAuthoringSession)] = new SiteNativeAuthoringSession(sites!, owners.Profiles, owners.Resources, owners.Broker)
                },
                configureOriginalStores: identity =>
                {
                    files = new(identity.StateStore, identity.Profiles);
                    return new([files], new Dictionary<Type, object> { [typeof(NativeFilesWorkspaceService)] = files });
                },
                configureOriginalResolvers: ownership =>
                {
                    authority = new(files!, ownership.Identity.Profiles, ownership.Ownership);
                    resolver = new(authority);
                    sites = new(authority);
                    sitesResolver = new(sites);
                    return new([resolver, sitesResolver], new Dictionary<Type, object>
                    {
                        [typeof(NativeFilesWorkspaceAuthority)] = authority, [typeof(FilesArtifactResourceResolver)] = resolver,
                        [typeof(ISiteNativeWorkspaceAuthority)] = sites, [typeof(SiteNativeProjectAccessResolver)] = sitesResolver
                    });
                });
            original.DemandPublication();
            collection.AddHavenOwnedWindowsHomeDomain(_actualWindowsHome, paths, principal);
            collection.AddSingleton(files!); collection.AddSingleton(authority!); collection.AddSingleton(resolver!);
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
        return home.StartOriginalAsync();
    }

    private void DemandOriginalWindowsHomeRetirementJoin() => _actualWindowsHome?.DemandExternalOriginalProcessJoin();

    private Task JoinOriginalWindowsHomeAfterBorrowersAsync() =>
        _actualWindowsHome?.CloseAndDrainAsync() ?? Task.CompletedTask;

    private Task JoinOriginalUntransferredWindowsHomeAsync()
    {
        lock (_actualStartupAcquisitionGate)
            return _windowsHomeBorrowerTransferred ? Task.CompletedTask : JoinOriginalWindowsHomeAfterBorrowersAsync();
    }
}
#endif
