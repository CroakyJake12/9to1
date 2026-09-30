using Haven.Application;
using Haven.Desktop;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Android;

/// <summary>Both Android HOME and the main UI attach to the same bundled Home service graph.</summary>
internal static class AndroidHomeServiceHost
{
    private const string CompositionId = "com.cakemods.haven.bundled-home";

    internal static ValueTask<HomeNativeHostResult> EnsureAsync(bool installedApplications,
        CancellationToken cancellationToken = default)
    {
        var services = App.Services;
        if (services is null)
            return ValueTask.FromResult(new HomeNativeHostResult(HomeNativeHostState.RequiresHomeRepair,
                "HomeServicesUnavailable", "9-1 Home could not initialise. Open Home to repair its preserved state.", null, null));
        return HomeNativeServiceHost.Process.EnsureAsync(CompositionId,
            _ => Task.FromResult(new HomeNativeServiceSession(services,
                services.GetRequiredService<HomeCoreRuntime>(), services.GetRequiredService<IAuthenticatedResourceActorSource>())),
            installedApplications ? ["home.core", "home.state", "apps.installed"] : ["home.core", "home.state"], cancellationToken);
    }
}
