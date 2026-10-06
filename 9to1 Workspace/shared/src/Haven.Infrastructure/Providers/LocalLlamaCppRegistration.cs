using System.Globalization;
using Dulche.Runtime;
using Haven.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure;

/// <summary>Opt-in composition. Reads only explicit process-local configuration; no source,
/// account, context, model availability or capability is inferred from these values.</summary>
public static class LocalLlamaCppRegistration
{
    public static LlamaCppLocalEndpointOptions ReadExplicitEnvironment(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        return new(
            Enabled: bool.TryParse(read("HAVEN_LOCAL_LLAMA_ENABLED"), out var enabled) && enabled,
            SocketPath: read("HAVEN_LOCAL_LLAMA_SOCKET"),
            ExpectedExecutableSha256: read("HAVEN_LOCAL_LLAMA_EXECUTABLE_SHA256"),
            ModelPath: read("HAVEN_LOCAL_LLAMA_MODEL"),
            ExpectedModelSha256: read("HAVEN_LOCAL_LLAMA_MODEL_SHA256"),
            ModelAlias: read("HAVEN_LOCAL_LLAMA_ALIAS"));
    }

    /// <summary>Call once from Root's real AddHavenInfrastructure. Same provider/source and
    /// same Dulche catalogue resolver; all actors, stores, frame owners and policy stay existing.</summary>
    public static IServiceCollection AddHavenObservedLocalLlamaCpp(this IServiceCollection services,
        LlamaCppLocalEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton(options);
        services.AddSingleton<LlamaCppModelProvider>();
        services.AddSingleton<IModelProvider>(provider => provider.GetRequiredService<LlamaCppModelProvider>());
        services.AddSingleton<ILocalModelEndpointObservationSource>(provider => provider.GetRequiredService<LlamaCppModelProvider>());
        services.AddSingleton<ModelRouteResolver>();
        services.AddSingleton<IProviderCatalogueEligibility>(provider => provider.GetRequiredService<ModelRouteResolver>());
        return services;
    }
}
