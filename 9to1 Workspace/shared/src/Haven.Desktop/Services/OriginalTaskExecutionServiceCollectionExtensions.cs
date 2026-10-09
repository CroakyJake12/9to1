using Haven.Application;
using Haven.Browser;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>The SAME maintained Desktop business registrations, shared by the normal
/// App and explicit local Task console. Configuration grants no model, workspace,
/// Home approval or canonical Task authority.</summary>
public static class OriginalTaskExecutionServiceCollectionExtensions
{
    public static IServiceCollection AddHavenOriginalTaskExecutionServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(item => item.ServiceType == typeof(Registration))) return services;
        Type[] owners = [typeof(ChatSessionService), typeof(AgentTaskRuntimeService),
            typeof(WorkspaceToolRuntime), typeof(ComputerToolRuntime), typeof(BrowserToolRuntime),
            typeof(AutomationToolRuntime), typeof(BrowserSessionService)];
        if (services.Any(item => owners.Contains(item.ServiceType)))
            throw new InvalidOperationException("The original Task execution services must be configured once before building their provider.");
        services.AddSingleton<BrowserSessionService>();
        services.AddSingleton<BrowserDataService>();
        services.AddSingleton<BrowserNavigationPolicy>();
        services.AddSingleton<IBrowserNavigationPolicy>(provider => provider.GetRequiredService<BrowserNavigationPolicy>());
        services.AddSingleton<BrowserAutomationStore>();
        services.AddSingleton<IBrowserAutomationStore>(provider => provider.GetRequiredService<BrowserAutomationStore>());
        services.AddSingleton<BrowserDownloadTransport>();
        services.AddSingleton<BrowserBackgroundPageLoader>();
        services.AddSingleton(provider => new BrowserAutomationService(
            provider.GetRequiredService<BrowserSessionService>(),
            provider.GetRequiredService<IBrowserNavigationPolicy>(),
            provider.GetRequiredService<IBrowserAutomationStore>(),
            provider.GetRequiredService<BrowserDownloadTransport>(),
            provider.GetRequiredService<BrowserBackgroundPageLoader>()));
        services.AddSingleton(provider => new SafeModeBrowserAutomationService(
            provider.GetRequiredService<BrowserAutomationService>(),
            provider.GetRequiredService<IProductionDiagnostics>()));
        services.AddSingleton(provider => new BrowserNativeDownloadAutomationService(
            provider.GetRequiredService<SafeModeBrowserAutomationService>(),
            provider.GetRequiredService<IBrowserNavigationPolicy>(),
            provider.GetRequiredService<IBrowserAutomationStore>()));
        services.AddSingleton<IBrowserAutomationService>(provider => provider.GetRequiredService<BrowserNativeDownloadAutomationService>());
        services.AddSingleton<IBrowserNativeDownloadService>(provider => provider.GetRequiredService<BrowserNativeDownloadAutomationService>());
        services.AddSingleton<IBrowserToolService>(provider => provider.GetRequiredService<BrowserSessionService>());
        services.AddSingleton<BrowserCompletionService>();
        services.AddSingleton<BrowserToolRuntime>();
        services.AddSingleton<AutomationToolRuntime>();
        services.AddSingleton<CapabilityPreflightService>();
        services.AddSingleton<TerminalCommandActivityHub>();
        services.AddSingleton<WorkspaceToolRuntime>();
        services.AddSingleton<ComputerToolRuntime>();
        services.AddSingleton(provider => new DualModelService(provider.GetRequiredService<IOllamaClient>(), provider.GetRequiredService<IExecutionEventSink>()));
        services.AddSingleton(provider => new JudgeService(new TrainingJudgeAdapter(provider.GetRequiredService<IOllamaClient>()), provider.GetRequiredService<IExecutionEventSink>()));
        services.AddSingleton<ChatSessionService>(provider => new ChatSessionService(
            provider.GetRequiredService<IConversationRepository>(),
            provider.GetRequiredService<IProviderModelClient>(),
            provider.GetRequiredService<CapabilityPreflightService>(),
            provider.GetRequiredService<IConversationSafetyService>(),
            provider.GetRequiredService<WorkspaceToolRuntime>(),
            provider.GetRequiredService<ComputerToolRuntime>(),
            provider.GetRequiredService<BrowserToolRuntime>(),
            provider.GetRequiredService<AutomationToolRuntime>(),
            mcpTools: provider.GetRequiredService<McpToolRuntime>(),
            calendarTools: provider.GetRequiredService<CalendarConnectionToolRuntime>(),
            pluginTools: provider.GetRequiredService<PluginToolRuntime>(),
            executionEvents: provider.GetRequiredService<IExecutionEventSink>(),
            recovery: provider.GetRequiredService<AutonomousRecoveryService>(),
            remediations: provider.GetRequiredService<RemediationCoordinator>(),
            personalities: provider.GetRequiredService<ModelPersonalityService>(),
            modelPermissions: provider.GetRequiredService<ModelPermissionEvaluator>(),
            defaultProviders: provider.GetRequiredService<IDefaultProviderStore>(),
            checkpoints: provider.GetRequiredService<CheckpointService>(),
            projectInstructionFiles: provider.GetRequiredService<IProjectInstructionSource>(),
            memorySource: provider.GetRequiredService<IMemoryQuerySource>(),
            taskCoordinator: provider.GetRequiredService<TaskExecutionCoordinator>(),
            taskToolOwner: provider.GetRequiredService<ITaskRunToolActionOwner>(),
            taskProviderContextCapture: provider.GetRequiredService<ITaskRunProviderContextCapture>(),
            taskCloudPermissionRemediation: provider.GetRequiredService<TaskRunCloudPermissionRemediationOwner>()));
        services.AddSingleton<AgentTaskRuntimeService>();
        services.AddSingleton(new Registration());
        return services;
    }
    private sealed class Registration { }
}
