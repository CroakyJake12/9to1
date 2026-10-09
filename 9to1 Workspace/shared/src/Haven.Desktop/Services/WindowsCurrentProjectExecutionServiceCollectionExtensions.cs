using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

public static class WindowsCurrentProjectExecutionServiceCollectionExtensions
{
    /// <summary>After native project resources and Dev, before Build. Precreate the SAME
    /// resolver/policy in the domain constructor. This borrows one genuine command READ,
    /// native source and tool owner; manual execution consent remains independently required.</summary>
    public static IServiceCollection AddHavenOwnedCurrentProjectExecution(this IServiceCollection services,
        HomeNativeWindowsComposition sameHome, FilesDeveloperOriginalCurrentProjectExecutionResolver originalResolver,
        HomeDeveloperWorkspaceExecutionActionPolicySource originalPolicy)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(sameHome);
        ArgumentNullException.ThrowIfNull(originalResolver); ArgumentNullException.ThrowIfNull(originalPolicy);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The actual current project/native execution source requires Windows.");
        Type[] owners = [typeof(FilesDeveloperOriginalCurrentProjectExecutionBridge), typeof(FilesDeveloperOriginalCurrentProjectExecutionResolver),
            typeof(IDeveloperWorkspaceTrustService), typeof(IDeveloperWorkspaceOriginalProjectExecutionTrustService),
            typeof(IDeveloperWorkspaceOriginalProjectExecutionBindingSource), typeof(IDeveloperWorkspaceOriginalExecutionBindingSource),
            typeof(IDeveloperWorkspaceOriginalExecutionScopedBindingSource), typeof(IDeveloperWorkspaceOriginalExecutionCommitBindingSource),
            typeof(IDeveloperWorkspaceOriginalExecutionPinCustodySource)];
        if (services.Any(row => owners.Contains(row.ServiceType)))
            throw new InvalidOperationException("The actual current project execution issuer and aliases cannot replace an existing trust/source owner.");
        foreach (var type in new[] { typeof(HomeColdProjectReadReconciliation), typeof(IDeveloperOriginalProjectCommandReadSource),
            typeof(IDeveloperOriginalCurrentProjectNativeSource), typeof(ITaskRunToolActionOwner), typeof(DeveloperTaskWorkspaceService) })
        {
            var rows = services.Where(row => row.ServiceType == type).Take(2).ToArray();
            if (rows.Length != 1 || rows[0].Lifetime != ServiceLifetime.Singleton)
                throw new InvalidOperationException("One original project/native/Dev/tool singleton must already be configured: " + type.Name);
        }
        // The reviewed Home overload checks the exact original domain tuple and fixes
        // consent's lazy binding source to this SAME bridge, without resolving it here.
        services.AddHavenOwnedDeveloperExecutionConsent(sameHome,
            provider => provider.GetRequiredService<FilesDeveloperOriginalCurrentProjectExecutionBridge>(), originalPolicy);
        services.AddSingleton(originalResolver);
        if (!services.Any(row => row.ServiceType == typeof(ICanonicalResourceAccessResolver)
            && ReferenceEquals(row.ImplementationInstance, originalResolver)))
            services.AddSingleton<ICanonicalResourceAccessResolver>(originalResolver);
        services.AddSingleton<FilesDeveloperOriginalCurrentProjectExecutionBridge>(provider =>
        {
            var command = provider.GetRequiredService<HomeColdProjectReadReconciliation>();
            if (!ReferenceEquals(command, provider.GetRequiredService<IDeveloperOriginalProjectCommandReadSource>()))
                throw new InvalidOperationException("The command READ alias must be the SAME configured Home project source.");
            return new(command, provider.GetRequiredService<IDeveloperOriginalCurrentProjectNativeSource>(),
                () => provider.GetRequiredService<IWorkspaceOriginalProcessStartConsentSource>(),
                provider.GetRequiredService<ITaskRunToolActionOwner>());
        });
        services.AddSingleton<IDeveloperWorkspaceTrustService>(provider => provider.GetRequiredService<FilesDeveloperOriginalCurrentProjectExecutionBridge>());
        services.AddSingleton<IDeveloperWorkspaceOriginalProjectExecutionTrustService>(provider => provider.GetRequiredService<FilesDeveloperOriginalCurrentProjectExecutionBridge>());
        services.AddSingleton<IDeveloperWorkspaceOriginalProjectExecutionBindingSource>(provider => provider.GetRequiredService<FilesDeveloperOriginalCurrentProjectExecutionBridge>());
        services.AddSingleton<IDeveloperWorkspaceOriginalExecutionBindingSource>(provider => provider.GetRequiredService<FilesDeveloperOriginalCurrentProjectExecutionBridge>());
        services.AddSingleton<IDeveloperWorkspaceOriginalExecutionScopedBindingSource>(provider => provider.GetRequiredService<FilesDeveloperOriginalCurrentProjectExecutionBridge>());
        services.AddSingleton<IDeveloperWorkspaceOriginalExecutionCommitBindingSource>(provider => provider.GetRequiredService<FilesDeveloperOriginalCurrentProjectExecutionBridge>());
        services.AddSingleton<IDeveloperWorkspaceOriginalExecutionPinCustodySource>(provider => provider.GetRequiredService<FilesDeveloperOriginalCurrentProjectExecutionBridge>());
        return services;
    }
}
