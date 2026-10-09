using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed partial class CanonicalDiResolutionTests08
{
    [Fact]
    public async Task Canonical_process_cohort_resolves_the_same_host_chat_agent_and_authority_owners()
    {
        var root = Path.Combine(Path.GetTempPath(), "haven-canonical-process-di", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var services = new ServiceCollection();
        services.AddHavenInfrastructure();
        services.AddSingleton<IAppPaths>(new Paths(root));
        // Actual maintained required Chat services; optional Browser/Automation ports
        // remain absent through the supported nullable constructor parameters. This is
        // a canonical owner identity control, not complete native/browser host proof.
        services.AddSingleton<CapabilityPreflightService>();
        services.AddSingleton<TerminalCommandActivityHub>();
        services.AddSingleton<WorkspaceToolRuntime>();
        services.AddSingleton<ComputerToolRuntime>();
        services.AddSingleton<ChatSessionService>(provider => new ChatSessionService(
            provider.GetRequiredService<IConversationRepository>(),
            provider.GetRequiredService<IProviderModelClient>(),
            provider.GetRequiredService<CapabilityPreflightService>(),
            provider.GetRequiredService<IConversationSafetyService>(),
            provider.GetRequiredService<WorkspaceToolRuntime>(),
            provider.GetRequiredService<ComputerToolRuntime>(),
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
        var provider = services.BuildServiceProvider();
        var failures = new List<Exception>();
        TaskRunCanonicalProcessRetirementOwner? owner = null;
        try
        {
            // Resolve the real cohort FIRST; it must borrow the SAME host Agent/Chat graph.
            owner = provider.GetRequiredService<TaskRunCanonicalProcessRetirementOwner>();
            var coordinator = provider.GetRequiredService<TaskExecutionCoordinator>();
            var agents = provider.GetRequiredService<AgentTaskRuntimeService>();
            var authority = provider.GetRequiredService<TaskRunPermissionAuthority>();
            var frames = provider.GetRequiredService<TaskRunOriginalFrameOwner>();
            Assert.Same(owner, provider.GetRequiredService<TaskRunCanonicalProcessRetirementOwner>());
            Assert.True(owner.HasOriginalComposition(coordinator, agents, authority, frames));
            Assert.Same(coordinator, provider.GetRequiredService<ITaskRunProcessRetirementParticipant>());
            Assert.Same(authority, provider.GetRequiredService<ITaskRunAdmissionAuthority>());
            Assert.Same(frames, provider.GetRequiredService<ITaskRunRuntimeSettlement>());
            Assert.Same(frames, provider.GetRequiredService<ITaskRunOriginalFrameProcessJoinGuard>());
            Assert.False(authority.IsOriginalAdmissionSealed);
            owner.RequestOriginalProcessRetirement();
            Assert.True(authority.IsOriginalAdmissionSealed);
        }
        catch (Exception error) { Add(failures, error); }
        finally
        {
            // The process cohort is a borrower. Its actual whole close precedes DI disposal.
            Task? actualBusinessClose = null;
            if (owner is not null)
            {
                try { actualBusinessClose = owner.CloseAndSuspendOriginalProducersAsync(); }
                catch (Exception error) { Add(failures, error); }
                if (actualBusinessClose is not null)
                    try { await actualBusinessClose; }
                    catch (Exception error) { AddTask(failures, error, actualBusinessClose); }
            }
            Task? actualProviderClose = null;
            try { actualProviderClose = provider.DisposeAsync().AsTask(); }
            catch (Exception error) { Add(failures, error); }
            if (actualProviderClose is not null)
                try { await actualProviderClose; }
                catch (Exception error) { AddTask(failures, error, actualProviderClose); }
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
    }
}
