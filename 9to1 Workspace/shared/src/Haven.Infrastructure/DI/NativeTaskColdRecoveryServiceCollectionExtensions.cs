using Haven.Application;
using Microsoft.Extensions.DependencyInjection;
namespace Haven.Infrastructure;

public static class NativeTaskColdRecoveryServiceCollectionExtensions
{
    /// <summary>Explicit configuration after the maintained Infrastructure registrations and
    /// before provider construction. Unknown or unsupported setup exposes a precise refusal;
    /// protected-store readiness is never inferred from descriptors or this opt-in.</summary>
    public static IServiceCollection AddHavenOwnedNativeTaskColdRecovery(this IServiceCollection services,
        NativePersonalTaskColdRecoveryConfiguration actualConfiguration)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(actualConfiguration);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(NativePersonalTaskColdRecoveryHost) ||
            descriptor.ServiceType == typeof(SqliteTaskRunColdRecoveryJournal) || descriptor.ServiceType == typeof(ITaskRunColdRecoveryJournal) ||
            descriptor.ServiceType == typeof(ITaskRunColdContextAuthority)))
            throw new InvalidOperationException("Original cold recovery composition must be registered once; foreign producers cannot be silently replaced.");
        foreach (var required in new[] { typeof(SqliteDatabase), typeof(IAppPaths), typeof(HostLocalTaskActorSource),
            typeof(IDatabaseMaintenance), typeof(TaskRunPermissionAuthority), typeof(TaskExecutionCoordinator) })
            if (!services.Any(descriptor => descriptor.ServiceType == required))
                throw new InvalidOperationException("The maintained SAME database/actor/authority/coordinator registration prefix is required.");
        services.AddSingleton(actualConfiguration);
        services.AddSingleton<NativePersonalTaskColdRecoveryHost>();
        if (actualConfiguration.OriginalStatus.Kind != NativePersonalTaskColdRecoveryConfigurationKind.RequestedUnverified) return services;
        services.AddSingleton<SqliteTaskRunColdRecoveryJournal>(provider => new SqliteTaskRunColdRecoveryJournal(
            provider.GetRequiredService<SqliteDatabase>(), provider.GetRequiredService<IAppPaths>(),
            provider.GetRequiredService<HostLocalTaskActorSource>(), provider.GetRequiredService<IDatabaseMaintenance>(),
            () => provider.GetRequiredService<ChatSessionService>()));
        services.AddSingleton<ITaskRunColdRecoveryJournal>(provider => provider.GetRequiredService<SqliteTaskRunColdRecoveryJournal>());
        services.AddSingleton<ITaskRunColdContextAuthority>(provider => provider.GetRequiredService<SqliteTaskRunColdRecoveryJournal>());
        return services;
    }
}
