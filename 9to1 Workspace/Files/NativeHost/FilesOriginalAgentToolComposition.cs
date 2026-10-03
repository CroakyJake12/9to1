using Haven.Application;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace HavenOS.Files.NativeHost;

public static class FilesOriginalAgentToolComposition
{
    /// <summary>Explicit supported-route registration after the actual deferred Home admission
    /// bridge. The ordinary Files host and null/local tool route are unchanged.</summary>
    public static IServiceCollection AddFilesOriginalAgentTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(item => item.ServiceType == typeof(Registration))) return services;
        if (!services.Any(item => item.ServiceType == typeof(IChatExecutionAdmission)))
            throw new InvalidOperationException("Register the actual deferred Home execution admission bridge first.");
        if (services.Any(item => item.ServiceType == typeof(IWorkspaceOriginalToolDispatcher) ||
            item.ServiceType == typeof(IHomeAgentOriginalToolPolicySource)))
            throw new InvalidOperationException("Compose supported owning tool routes explicitly; do not replace another owner.");
        services.AddFilesNativeHost();
        services.AddSingleton(new Registration());
        services.AddSingleton<FilesAgentOriginalToolDispatcher>();
        services.AddSingleton<IWorkspaceOriginalToolDispatcher>(provider => provider.GetRequiredService<FilesAgentOriginalToolDispatcher>());
        services.AddSingleton<IHomeAgentOriginalToolPolicySource>(provider => provider.GetRequiredService<FilesAgentOriginalToolDispatcher>());
        services.AddSingleton<IHomeActionPolicySource>(provider => provider.GetRequiredService<FilesAgentOriginalToolDispatcher>());
        return services;
    }

    private sealed class Registration { }
}
