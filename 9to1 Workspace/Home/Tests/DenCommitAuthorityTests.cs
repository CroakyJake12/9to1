using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class DenCommitAuthorityTests
{
    private static AgentDefinitionRecord Agent(IReadOnlyList<string>? tools = null) => new()
    { Id = "agent", NamespaceId = "personal", DisplayName = "Original", Version = "1", ToolIds = tools ?? [] };
    private static NamespaceAccessPolicy Owner() => new([new("owner", "personal", DenPermission.Administer)]);

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public async Task Revocation_at_admission_or_publication_does_not_commit_or_leave_a_recoverable_replay(int denyAt, bool existing)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-den-commit-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            var owner = new DulcheDen(store, Owner(), "owner");
            var original = existing ? await owner.SaveAsync(Agent(), 0, "seed") : null;
            var revoking = new Revoking(denyAt);
            var caller = new DulcheDen(store, revoking, "owner");
            var failure = await Assert.ThrowsAsync<DenException>(() => caller.SaveAsync(Agent() with { DisplayName = "Must not publish" }, original?.Revision ?? 0, "denied"));
            Assert.Equal(DenErrorCode.Forbidden, failure.Code);
            Assert.Equal(denyAt, revoking.Writes);
            Assert.Empty(Directory.EnumerateDirectories(Path.Combine(root, "transactions")));
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "history"), "*.json", SearchOption.AllDirectories));
            Assert.False(File.Exists(Path.Combine(root, "journal", "denied.json")));
            await using var reopened = await DenStore.OpenAsync(root);
            var after = await new DulcheDen(reopened, Owner(), "owner").GetAsync<AgentDefinitionRecord>("personal", "agent");
            if (existing) { Assert.Equal(original!.Revision, after!.Revision); Assert.Equal("Original", after.DisplayName); }
            else Assert.Null(after);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Submitted_record_is_detached_before_waiting_for_authorization()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-den-snapshot-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            var access = new Blocking(); var den = new DulcheDen(store, access, "owner");
            var tools = new List<string> { "captured" };
            var pending = den.SaveAsync(Agent(tools), 0, "capture");
            await access.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            tools[0] = "injected"; access.Release.SetResult();
            var saved = await pending;
            Assert.Equal("captured", Assert.Single(saved.ToolIds));
            Assert.Equal("captured", Assert.Single((await den.GetAsync<AgentDefinitionRecord>("personal", "agent"))!.ToolIds));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Interrupted_transaction_blocks_new_writes_until_owning_recovery_runs()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-den-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            Directory.CreateDirectory(Path.Combine(root, "transactions", "interrupted"));
            var den = new DulcheDen(store, Owner(), "owner");
            var failure = await Assert.ThrowsAsync<DenException>(() => den.SaveAsync(Agent(), 0, "after-interruption"));
            Assert.Equal(DenErrorCode.StorageFailure, failure.Code);
            Assert.Null(await den.GetAsync<AgentDefinitionRecord>("personal", "agent"));
            await using var reopened = await DenStore.OpenAsync(root);
            var saved = await new DulcheDen(reopened, Owner(), "owner").SaveAsync(Agent(), 0, "after-recovery");
            Assert.Equal(1, saved.Revision);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class Revoking(int denyAt) : IDenAccessPolicy
    {
        public int Writes { get; private set; }
        public ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId, DenPermission permission, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(permission != DenPermission.Write || ++Writes < denyAt);
    }
    private sealed class Blocking : IDenAccessPolicy
    {
        private int _writes;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId, DenPermission permission, CancellationToken cancellationToken = default)
        {
            if (permission == DenPermission.Write && Interlocked.Increment(ref _writes) == 1)
            { Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            return true;
        }
    }
}
