using Haven.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed partial class CanonicalDiResolutionTests08
{
    [Fact]
    public Task Canonical_coordinator_resolves_first_and_borrows_the_same_live_unstarted_permission_issuer()
        => InspectAsync(provider =>
        {
            // Resolve coordinator FIRST. Its required owner borrows only the existing LAZY
            // coordinator lookup; resolving this real production graph must not recurse.
            var coordinator = provider.GetRequiredService<TaskExecutionCoordinator>();
            var sameIssuer = provider.GetRequiredService<TaskRunCloudPermissionRemediationOwner>();
            Assert.Same(sameIssuer, Original(coordinator, "_unstartedPermissionSource"));
            var originalLookup = Assert.IsType<Func<TaskExecutionCoordinator>>(Original(sameIssuer, "_tasks"));
            Assert.Same(coordinator, originalLookup());
            Assert.Same(sameIssuer, provider.GetRequiredService<TaskRunCloudPermissionRemediationOwner>());
            Assert.Same(provider.GetRequiredService<TaskRunCentralCloudUsePermissionSource>(), Original(sameIssuer, "_source"));
            Assert.Same(provider.GetRequiredService<TaskRunPermissionAuthority>(), Original(sameIssuer, "_authority"));
            // The test supplies no actor, Ask, grant, provider readiness or synthetic registration.
            // InspectAsync joins actual ServiceProvider.DisposeAsync and all its causes.
            return Task.CompletedTask;
        });
}
