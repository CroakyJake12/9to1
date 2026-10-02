using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Haven.Infrastructure;

/// <summary>Trusted native-module opt-in before provider construction. This never registers
/// a Browser peer/resolver, opens a second Home store, or supplies a remote authentication guard.</summary>
public static class WebMcpOriginalActorServiceCollectionExtensions
{
    public static IServiceCollection AddHavenLocalWebMcpOwner(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(item => item.ServiceType == typeof(HomeWebMcpOriginalActorApproval) ||
            item.ServiceType == typeof(IWebMcpOriginalActorApproval)))
            throw new InvalidOperationException("A WebMcp owner is already configured; competing owner graphs are unavailable.");
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHomeActionPolicySource, HomeWebMcpActionPolicySource>());
        services.AddSingleton<HomeWebMcpOriginalActorApproval>(provider =>
        {
            var store = provider.GetRequiredService<IHomeCoreStateStore>() as FileHomeCoreStateStore
                ?? throw new NotSupportedException("The configured Home store has no supported local operation lease.");
            var profiles = provider.GetRequiredService<HomeLocalProfileIdentity>();
            if (!ReferenceEquals(provider.GetRequiredService<IAuthenticatedResourceActorSource>(), profiles))
                throw new NotSupportedException("The original canonical local Home profile actor source is required.");
            // The actual native module must register its private original-session registry
            // as the sole canonical owner. A kind string is not a permission or endpoint grant.
            if (provider.GetServices<ICanonicalResourceAccessResolver>().Count(owner => owner.ResourceKind == "webmcp.document") != 1)
                throw new InvalidOperationException("Exactly one actual owning browser document resolver is required.");
            return new(store, profiles, provider.GetRequiredService<HomeResourceOperationBroker>(),
                provider.GetRequiredService<HomePermissionTrustService>());
        });
        services.AddSingleton<IWebMcpOriginalActorApproval>(provider => provider.GetRequiredService<HomeWebMcpOriginalActorApproval>());
        return services;
    }
}
