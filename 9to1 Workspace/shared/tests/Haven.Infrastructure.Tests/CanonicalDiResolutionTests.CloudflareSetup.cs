using Haven.Application;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Haven.Infrastructure.Tests;
public sealed partial class CanonicalDiResolutionTests08
{
    [Fact] public Task Reviewed_action_process_frame_and_MCP_aliases_resolve_same_real_owners_without_CF_optin() => InspectAsync(provider =>
    {
        var coordinator = provider.GetRequiredService<TaskExecutionCoordinator>();
        Assert.Same(coordinator, provider.GetRequiredService<ITaskRunOriginalActionAdmissionSource>());
        Assert.Same(coordinator, provider.GetRequiredService<ITaskRunProcessRetirementParticipant>());
        Assert.Same(provider.GetRequiredService<TaskRunOriginalFrameOwner>(), provider.GetRequiredService<ITaskRunOriginalAttemptRegistrationSource>());
        Assert.Same(provider.GetRequiredService<McpConnectionClient>(), provider.GetRequiredService<IMcpConnectionClient>());
        Assert.Same(provider.GetRequiredService<WorkspaceTaskRunToolActionOwner>(), provider.GetRequiredService<ITaskRunToolActionOwner>());
        Assert.Same(provider.GetRequiredService<WorkspaceTaskRunReceiptAuthority>(), provider.GetRequiredService<ITaskRunActionReceiptAuthority>());
        Assert.Null(provider.GetService<CloudflareTaskRunToolActionOwner>());
        Assert.Null(provider.GetService<ICloudflareProductionSetupSource>());
        return Task.CompletedTask;
    });
}
