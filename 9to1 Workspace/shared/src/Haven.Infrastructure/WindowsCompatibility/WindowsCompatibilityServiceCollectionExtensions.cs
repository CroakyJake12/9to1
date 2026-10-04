/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/WindowsCompatibility/WindowsCompatibilityServiceCollectionExtensions.cs.
 * What: Adds compatibility launch services to a caller-controlled dependency-injection scope.
 * How: Registration is explicit and lazy; constructing the services does not probe runtimes or start processes.
 * Why: Optional app compatibility must remain non-boot-critical, and host runtime providers must be registered explicitly.
 */

using Haven.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure.WindowsCompatibility;

public static class WindowsCompatibilityServiceCollectionExtensions
{
    public static IServiceCollection AddHavenWindowsExeCompatibility(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IWindowsExeCompatibilityService, WineWindowsExeCompatibilityService>();
        return services;
    }

    /// <summary>
    /// Registers the fail-closed APK launch bridge. Hosts must register one or more
    /// <see cref="IApkRuntimeProvider"/> implementations separately to enable launches.
    /// </summary>
    public static IServiceCollection AddHavenApkLaunch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IApkLaunchService, ApkLaunchService>();
        return services;
    }
}
