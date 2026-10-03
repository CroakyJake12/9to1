using Haven.Application;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HavenOS.Apps.Terminal;

/// <summary>Registers the Terminal owner into the host's existing Home graph before the provider is built.
/// The host registers its actual session instance with the returned singleton registry; no session or Home store is created here.</summary>
public static class TerminalNativeComposition
{
    public static IServiceCollection AddTerminalNativeActions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(item => item.ServiceType == typeof(RegistrationMarker))) return services;
        services.AddSingleton(new RegistrationMarker());
        services.TryAddSingleton<TerminalOwnedSessionRegistry>();
        services.AddSingleton<ICanonicalResourceAccessResolver>(provider => new SessionResourceResolver(provider.GetRequiredService<TerminalOwnedSessionRegistry>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHomeActionPolicySource, TerminalSignalActionPolicies>());
        services.TryAddSingleton<TerminalOwnedSignalExecutor>();
        services.TryAddSingleton<HomeTerminalActionBroker>();
        services.AddSingleton<ITerminalActionBroker>(provider => provider.GetRequiredService<HomeTerminalActionBroker>());
        return services;
    }
    private sealed class RegistrationMarker { }
    private sealed class SessionResourceResolver(TerminalOwnedSessionRegistry registry) : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => registry.ResourceKind;
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken cancellationToken)
            => registry.EvaluateAsync(actor, actionId, scope, cancellationToken);
    }
}
