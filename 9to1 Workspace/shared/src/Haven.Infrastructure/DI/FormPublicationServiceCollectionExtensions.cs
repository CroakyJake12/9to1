using Haven.Application;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure;

/// <summary>Explicit native-module opt-in. Registration itself never binds a Forms store or activates publication capabilities.</summary>
public static class FormPublicationServiceCollectionExtensions
{
    public static IServiceCollection AddHavenFormsPublication(this IServiceCollection services,
        IFormProjectPublicationValidator validator)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(validator);
        if (services.Any(item => item.ServiceType == typeof(FormPublicationService) ||
            item.ServiceType == typeof(FormLocalStoreAuthority) || item.ServiceType == typeof(IFormStoreAuthority) ||
            item.ServiceType == typeof(IFormStoreCommitAuthority) || item.ServiceType == typeof(IFormProjectPublicationValidator) ||
            item.ServiceType == typeof(FormAuthoringService) || item.ServiceType == typeof(FormResponseSessionService)))
            throw new InvalidOperationException("A Forms publication owner is already configured; competing owner graphs are unavailable.");
        services.AddSingleton<IFormProjectPublicationValidator>(validator);
        services.AddSingleton<FormLocalStoreAuthority>(provider => new(
            provider.GetRequiredService<IVersionedSettingsStore>(), OwningIdentity(provider),
            provider.GetRequiredService<IAuthenticatedResourceActorSource>(),
            provider.GetRequiredService<IResourceStoreOwnershipAuthority>()));
        services.AddSingleton<IFormStoreAuthority>(provider => provider.GetRequiredService<FormLocalStoreAuthority>());
        services.AddSingleton<IFormStoreCommitAuthority>(provider => provider.GetRequiredService<FormLocalStoreAuthority>());
        services.AddSingleton<FormPublicationService>(provider => new(
            provider.GetRequiredService<IVersionedSettingsStore>(), OwningIdentity(provider),
            provider.GetRequiredService<IFormStoreAuthority>(), provider.GetRequiredService<IFormProjectPublicationValidator>(),
            actors: provider.GetRequiredService<IAuthenticatedResourceActorSource>()));
        services.AddSingleton<FormAuthoringService>();
        services.AddSingleton<FormResponseSessionService>(provider => new(
            provider.GetRequiredService<FormPublicationService>(), provider.GetRequiredService<IVersionedSettingsStore>(),
            OwningIdentity(provider), provider.GetRequiredService<IFormStoreAuthority>(),
            provider.GetRequiredService<IAuthenticatedResourceActorSource>()));
        services.AddSingleton<IHomeLocalStoreEvidenceProvider>(provider => new FormLocalStoreEvidenceProvider(
            provider.GetRequiredService<IVersionedSettingsStore>(), OwningIdentity(provider)));
        return services;
    }

    private static IResourceStoreIdentitySource OwningIdentity(IServiceProvider provider) =>
        provider.GetRequiredService<IVersionedSettingsStore>() as IResourceStoreIdentitySource
        ?? throw new InvalidOperationException("The actual Forms settings owner cannot provide its own durable identity.");
}
