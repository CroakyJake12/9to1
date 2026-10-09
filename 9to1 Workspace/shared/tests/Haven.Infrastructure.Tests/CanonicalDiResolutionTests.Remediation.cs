using System.Reflection;
using Haven.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed partial class CanonicalDiResolutionTests08
{
    [Fact]
    public Task Canonical_remediation_owner_resolves_first_and_borrows_same_source_authority_task_and_continuations()
        => InspectAsync(provider =>
        {
            // Resolve the newly registered owner FIRST: the task lookup must remain lazy and
            // must not introduce an owner -> coordinator -> authority -> owner creation cycle.
            var owner = provider.GetRequiredService<TaskRunCloudPermissionRemediationOwner>();
            Assert.Same(owner, provider.GetRequiredService<TaskRunCloudPermissionRemediationOwner>());
            Assert.Same(provider.GetRequiredService<TaskRunCentralCloudUsePermissionSource>(), Original(owner, "_source"));
            Assert.Same(provider.GetRequiredService<TaskRunPermissionAuthority>(), Original(owner, "_authority"));
            var originalLookup = Assert.IsType<Func<TaskExecutionCoordinator>>(Original(owner, "_tasks"));
            Assert.Same(provider.GetRequiredService<TaskExecutionCoordinator>(), originalLookup());
            Assert.Same(provider.GetRequiredService<IRemediationRepository>(), Original(owner, "_repository"));
            var actualContinuations = provider.GetRequiredService<RemediationContinuationRegistry>();
            Assert.Same(actualContinuations, Original(owner, "_continuations"));
            var actualRemediation = provider.GetRequiredService<RemediationCoordinator>();
            Assert.Same(actualRemediation, Original(owner, "_remediation"));
            Assert.Same(actualContinuations, Original(actualRemediation, "_continuations"));
            return Task.CompletedTask;
        });

    private static object Original(object owner, string field)
        => owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner)
            ?? throw new InvalidOperationException("The actual selected owner dependency is unavailable: " + field);
}
