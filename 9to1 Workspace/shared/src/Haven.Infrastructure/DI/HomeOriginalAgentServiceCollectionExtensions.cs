using Dulche.Runtime.Agents;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NineToOne.Dulche.Den;

namespace Haven.Infrastructure;

/// <summary>Opt-in designated Home owner composition. This never acquires a Home lease,
/// creates a Den or substitutes profile/install observations for the original accepted connection.</summary>
public static class HomeOriginalAgentServiceCollectionExtensions
{
    public static IServiceCollection AddHavenOriginalAgentExecution(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHomeActionPolicySource, HomeAgentExecutionActionPolicies>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHomeActionPolicySource, HomeCoreServiceReadActionPolicies>());
        services.TryAddSingleton<HomeNativeCoreApiSessions>(provider => new(
            provider.GetRequiredService<HomePermissionTrustService>(),
            provider.GetRequiredService<IAuthenticatedResourceActorSource>(),
            provider.GetRequiredService<IHomeNativeInstalledPeerVerifier>(),
            () => new HomeCoreApi(provider.GetRequiredService<HomeCoreRuntime>(),
                provider.GetRequiredService<HomeNativeCoreApiSessions>())));
        services.TryAddSingleton<IHomeCoreAuthorization>(provider => provider.GetRequiredService<HomeNativeCoreApiSessions>());
        services.TryAddSingleton<IHomeAgentCurrentDenSource, HomeRegisteredCurrentDenSource>();
        services.TryAddSingleton<IHomeAgentExecutionSessionComposer>(provider => new OriginalAgentSessionComposer(
            provider.GetRequiredService<AgentDependencyCatalogService>(), provider.GetRequiredService<IModelProviderRegistry>(),
            provider.GetRequiredService<IModelRouter>(), () => provider.GetRequiredService<AgentTaskRuntimeService>()));
        services.TryAddSingleton<HomeAgentExecutionHost>(provider => new(
            provider.GetRequiredService<HomeNativeCoreApiSessions>(),
            provider.GetRequiredService<IHomeAgentCurrentDenSource>(),
            provider.GetRequiredService<IHomeAgentExecutionSessionComposer>(),
            provider.GetRequiredService<IAuthenticatedResourceActorSource>(),
            provider.GetRequiredService<HomePermissionTrustService>(),
            provider.GetRequiredService<IModelProviderRegistry>(),
            provider.GetRequiredService<ResourceAuthorizationService>(),
            // Deferred owner lookup avoids issuer -> owner dispatcher -> registered admission cycles.
            () => UniqueOriginalToolOwner(provider.GetServices<IHomeAgentOriginalToolPolicySource>()),
            provider.GetService<HomePersonalModelRoutes>(), provider.GetService<IHomeApprovalPromptPresenter>()));
        services.TryAddSingleton<IHomeAgentExecutionHost>(provider => provider.GetRequiredService<HomeAgentExecutionHost>());
        services.TryAddSingleton<IChatExecutionAdmission>(provider => provider.GetRequiredService<HomeAgentExecutionHost>());
        services.TryAddSingleton<CapabilityPreflightService>();
        services.TryAddSingleton<TerminalCommandActivityHub>();
        services.TryAddSingleton<WorkspaceToolRuntime>();
        services.TryAddSingleton<ComputerToolRuntime>();
        services.TryAddSingleton<AgentDependencyCatalogService>();
        services.TryAddSingleton<FloatingActivityStateStore>();
        services.TryAddSingleton<ChatSessionService>(provider => new(
            provider.GetRequiredService<IConversationRepository>(),
            provider.GetRequiredService<ProviderRoutingModelClient>(),
            provider.GetRequiredService<CapabilityPreflightService>(),
            provider.GetRequiredService<IConversationSafetyService>(),
            provider.GetRequiredService<WorkspaceToolRuntime>(),
            provider.GetRequiredService<ComputerToolRuntime>(),
            provider.GetService<BrowserToolRuntime>(), provider.GetService<AutomationToolRuntime>(),
            mcpTools: provider.GetService<McpToolRuntime>(), calendarTools: provider.GetService<CalendarConnectionToolRuntime>(),
            pluginTools: provider.GetService<PluginToolRuntime>(), executionEvents: provider.GetService<IExecutionEventSink>(),
            recovery: provider.GetService<AutonomousRecoveryService>(), remediations: provider.GetService<RemediationCoordinator>(),
            personalities: provider.GetService<ModelPersonalityService>(), modelPermissions: provider.GetService<ModelPermissionEvaluator>(),
            defaultProviders: provider.GetService<IDefaultProviderStore>(), checkpoints: provider.GetService<CheckpointService>(),
            projectInstructionFiles: provider.GetService<IProjectInstructionSource>(), memorySource: provider.GetService<IMemoryQuerySource>(),
            originalExecutionAdmissions: provider.GetRequiredService<IChatExecutionAdmission>()));
        services.TryAddSingleton<AgentTaskRuntimeService>(provider => new(
            provider.GetRequiredService<ICatalogRepository>(), provider.GetRequiredService<IAgentRunRepository>(),
            provider.GetRequiredService<ProviderRoutingModelClient>(), provider.GetRequiredService<CapabilityRegistryService>(),
            provider.GetRequiredService<ChatSessionService>(), provider.GetRequiredService<IPermissionDecisionEngine>(),
            provider.GetService<FloatingActivityStateStore>(), provider.GetRequiredService<IChatExecutionAdmission>()));
        services.TryAddSingleton<IRecordedAgentInvocationSource>(provider => provider.GetRequiredService<AgentTaskRuntimeService>());
        return services;
    }
    private static IHomeAgentOriginalToolPolicySource? UniqueOriginalToolOwner(IEnumerable<IHomeAgentOriginalToolPolicySource> sources)
    {
        var owners = sources.Take(2).ToArray();
        return owners.Length switch { 0 => null, 1 => owners[0], _ =>
            throw new InvalidOperationException("The owning original Agent tool policy source is ambiguous.") };
    }
    private sealed class OriginalAgentSessionComposer(AgentDependencyCatalogService dependencies,
        IModelProviderRegistry models, IModelRouter router, Func<AgentTaskRuntimeService> sameChatRuntime) : IHomeAgentExecutionSessionComposer
    {
        public IAgentExecutionStateStore CreateRunReader(DulcheDen sameBoundDen, string originalNamespace) =>
            new DenAgentExecutionStateStore(sameBoundDen, originalNamespace);
        public IDenAgentExecutionSessionFactory Compose(HomeAgentExecutionAdmissions issuer,
            HomeAgentExecutionAdmissions.OriginalInvocation sameAdmission, DenAgentReference reference) =>
            new DenAgentExecutionSessionFactory(sameAdmission.Den, reference, sameAdmission.Context, sameAdmission.OriginalLifetime,
                issuer, issuer, dependencies, models, router, sameChatRuntime());
    }
}
