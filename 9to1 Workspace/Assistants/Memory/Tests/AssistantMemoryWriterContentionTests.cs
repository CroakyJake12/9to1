using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [Fact]
    public Task Actual_provider_contention_settles_failure_releases_Den_and_preserves_all_original_rows() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync();
        await rig.AddAsync(binding, "retained baseline during contention", scope: "global");
        var before = await rig.CaptureMemoryAsync(); var input = await rig.PrepareAsync(binding);
        var intent = await rig.Memory.PrepareOriginalWriteWithinSourceAsync(input.Input!, binding.Conversation,
            "Contended memory", "Keep this draft if the actual store is busy", Guid.NewGuid(), Scope, rig.Retain, Token);
        await using var competingConnection = await rig.Database.OpenAsync(Token);
        await using var competingWriter = competingConnection.BeginTransaction(deferred: false);
        var atomicAdmitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Retain(Task original)
        {
            rig.Retain(original);
            if (original is Task<ICanonicalAssistantMemoryWriteAcknowledgment> atomic &&
                rig.Memory.IsOriginalAtomicWriteTask(intent, atomic)) atomicAdmitted.TrySetResult();
        }
        var actual = rig.Memory.CommitOriginalWriteWithinSourceAsync(intent, Scope, Retain, Token);
        await DecideMemoryWrite(rig, actual, HomeApprovalChoice.Accept);
        await atomicAdmitted.Task.WaitAsync(TimeSpan.FromSeconds(12), Token);
        // Watchdog only, not an elapsed-time performance assertion. The real competing
        // SQLite writer remains held until the product's original failure has settled.
        await Assert.ThrowsAnyAsync<Exception>(() => actual.WaitAsync(TimeSpan.FromSeconds(12), Token));
        Assert.True(actual.IsFaulted); Assert.False(rig.Memory.IsAcknowledgedOriginalWriteRefusal(actual));
        var update = rig.Bridge.UpdateAsync(binding.Definition.Identity, binding.Definition.Revision,
            binding.Definition.Configuration with { Description = "Den remains editable after contended original settles" },
            Guid.NewGuid(), Token);
        Assert.Equal(binding.Definition.Revision + 1, (await update.WaitAsync(TimeSpan.FromSeconds(12), Token)).Revision);
        Assert.Equal(before, await rig.CaptureMemoryAsync());
        var memoryClose = rig.Memory.CloseAndDrainAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => memoryClose); Assert.True(memoryClose.IsFaulted); rig.ExpectedMemoryClose = memoryClose;
        var writesClose = rig.OriginalMemoryWrites.CloseAndDrainOriginalAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => writesClose); Assert.True(writesClose.IsFaulted); rig.ExpectedWriteClose = writesClose;
        var storeClose = rig.Store.CloseAndDrainAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => storeClose); Assert.True(storeClose.IsFaulted); rig.ExpectedStoreClose = storeClose;
    });
}
