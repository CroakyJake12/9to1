using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Apps.Dev;
using Xunit;

namespace Haven.Desktop.Tests;

// Authored NOT RUN. Narrow source/partition refusal controls. No genuine Home approval,
// native READ, successful inference, task issuance or packaged UI outcome is simulated.
public sealed class DeveloperTaskDraftRecoveryContractTests
{
    private static DeveloperDraftRecoveryScope Partition() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    [Fact]
    public void Task_recovery_partition_preserves_exact_profile_task_run_and_context()
    {
        var actual = Partition();
        DeveloperTaskDraftRecoveryOwner.DemandTaskRecoveryPartition(actual, actual with { });
        DeveloperDraftRecoveryScope[] wrong =
        [
            actual with { ProfileId = Guid.NewGuid() }, actual with { TaskId = Guid.NewGuid() },
            actual with { ExecutionId = Guid.NewGuid() }, actual with { ContextId = Guid.NewGuid() },
            actual with { IsManual = true }, actual with { TaskId = Guid.Empty },
            actual with { ExecutionId = Guid.Empty }, actual with { ContextId = Guid.Empty },
            actual with { ProfileId = Guid.Empty }
        ];
        Assert.All(wrong, value => Assert.Throws<UnauthorizedAccessException>(() =>
            DeveloperTaskDraftRecoveryOwner.DemandTaskRecoveryPartition(value, actual)));
    }
    [Fact]
    public void A_manual_partition_cannot_become_task_recovery_even_with_matching_copied_fields()
    {
        var manual = new DeveloperDraftRecoveryScope(Guid.NewGuid(), Guid.Empty, Guid.Empty, Guid.Empty, IsManual: true);
        Assert.Throws<UnauthorizedAccessException>(() => DeveloperTaskDraftRecoveryOwner.DemandTaskRecoveryPartition(manual, manual));
        var emptyTask = manual with { IsManual = false };
        Assert.Throws<UnauthorizedAccessException>(() => DeveloperTaskDraftRecoveryOwner.DemandTaskRecoveryPartition(emptyTask, emptyTask));
    }
    [Fact]
    public async Task Real_task_restore_refuses_a_substitute_key_owner_before_touching_task_or_source()
    {
        var keys = new SubstituteKeys();
        var owner = new DeveloperTaskDraftRecoveryOwner(null!, null!, null!, keys, new Paths(), null!, null!, null!, null!,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var actual = owner.RestoreAsync(null!, callback => callback(), CancellationToken.None);
        var cause = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.Contains(cause.InnerExceptions, error => error is UnauthorizedAccessException);
        Assert.Equal(0, keys.Calls); Assert.True(actual.IsFaulted);
    }
    private sealed class SubstituteKeys : IProviderSecretStore
    {
        internal int Calls;
        public Task<string?> GetAsync(string provider, string name, CancellationToken token)
        { Calls++; throw new InvalidOperationException("A substitute credential owner must never be consulted."); }
        public Task SetAsync(string provider, string name, string value, CancellationToken token)
        { Calls++; throw new InvalidOperationException("No substitute key write is authorised."); }
        public Task DeleteAsync(string provider, string name, CancellationToken token)
        { Calls++; throw new InvalidOperationException("No key reset is authorised."); }
    }
    private sealed class Paths : IAppPaths
    {
        public string DataDirectory => "/no-fixture-data";
        public string DatabasePath => "/no-fixture-data/db";
        public string BrowserProfileDirectory => "/no-fixture-data/browser";
        public string AttachmentsDirectory => "/no-fixture-data/attachments";
        public string LogsDirectory => "/no-fixture-data/logs";
        public string LegacyStatePath => "/no-fixture-data/legacy";
    }
}
