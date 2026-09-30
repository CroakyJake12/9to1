using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class DenSyncAuthorityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_read_or_sharing_revocation_at_destination_publication_prevents_copy(bool sharing)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-den-sync-" + Guid.NewGuid().ToString("N"));
        var leftRoot = Path.Combine(root, "left"); var rightRoot = Path.Combine(root, "right");
        try
        {
            await using var left = await DenStore.CreateAsync(leftRoot, [new("shared", "team", true)]);
            await using var right = await DenStore.CreateAsync(rightRoot, [new("shared", "team", true)]);
            var admin = new NamespaceAccessPolicy([new("owner", "shared", DenPermission.Administer), new("owner", right.Manifest.DenId, DenPermission.Administer)]);
            var rightAdmin = new DulcheDen(right, admin, "owner");
            await rightAdmin.SaveAsync(new AgentDefinitionRecord { Id = "agent", NamespaceId = "shared", DisplayName = "Keep private after revoke", Version = "1" }, 0, "seed");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var sourceAccess = new SourceAccess();
            var targetAccess = new TargetAccess(leftRoot);
            targetAccess.AtPublication = async () =>
            {
                if (sharing) await rightAdmin.SetNamespaceSharingAsync("shared", false, right.Manifest.Revision, "disable-sync", deadline.Token);
                else sourceAccess.ReadAllowed = false;
            };
            var target = new DulcheDen(left, targetAccess, "owner");
            var source = new DulcheDen(right, sourceAccess, "owner");
            var error = await Assert.ThrowsAsync<DenException>(() => target.SyncWithAsync(source, ["shared"], deadline.Token));
            Assert.Equal(DenErrorCode.Forbidden, error.Code);
            Assert.True(targetAccess.Changed);
            if (sharing) Assert.False((await right.ReadAuthoritySnapshotAsync()).Namespaces.Single().Shared);
            else Assert.False(sourceAccess.ReadAllowed);
            Assert.Null(await new DulcheDen(left, admin, "owner").GetAsync<AgentDefinitionRecord>("shared", "agent"));
            Assert.Empty(Directory.GetDirectories(Path.Combine(leftRoot, "transactions")));
            Assert.NotNull(await rightAdmin.GetAsync<AgentDefinitionRecord>("shared", "agent"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Fresh_sharing_manifest_and_current_source_read_are_required_on_reopened_peer()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-den-sync-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var left = await DenStore.CreateAsync(Path.Combine(root, "left"), [new("shared", "team", true)]);
            await using var right = await DenStore.CreateAsync(Path.Combine(root, "right"), [new("shared", "team", true)]);
            var access = new NamespaceAccessPolicy([new("owner", "shared", DenPermission.Administer), new("owner", right.Manifest.DenId, DenPermission.Administer)]);
            var local = new DulcheDen(left, access, "owner"); var remote = new DulcheDen(right, access, "owner");
            await remote.SaveAsync(new AgentDefinitionRecord { Id = "agent", NamespaceId = "shared", DisplayName = "Agent", Version = "1" }, 0, "seed");
            Assert.Single(await local.SyncWithAsync(remote, ["shared"]));
            Assert.NotNull(await local.GetAsync<AgentDefinitionRecord>("shared", "agent"));
            await using var changed = await DenStore.OpenAsync(Path.Combine(root, "right"));
            await new DulcheDen(changed, access, "owner").SetNamespaceSharingAsync("shared", false, changed.Manifest.Revision, "revoke");
            var error = await Assert.ThrowsAsync<DenException>(() => local.SyncWithAsync(remote, ["shared"]));
            Assert.Equal(DenErrorCode.Forbidden, error.Code);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Guarded_sync_retains_two_way_copy_disjoint_merge_and_explicit_conflict_history()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-den-sync-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var left = await DenStore.CreateAsync(Path.Combine(root, "left"), [new("shared", "team", true)]);
            await using var right = await DenStore.CreateAsync(Path.Combine(root, "right"), [new("shared", "team", true)]);
            var access = new NamespaceAccessPolicy([new("owner", "shared", DenPermission.Administer), new("owner", right.Manifest.DenId, DenPermission.Administer)]);
            var local = new DulcheDen(left, access, "owner"); var remote = new DulcheDen(right, access, "owner");
            await local.SaveAsync(new AgentDefinitionRecord { Id = "left", NamespaceId = "shared", DisplayName = "Left", Version = "1" }, 0, "left-seed");
            await remote.SaveAsync(new AgentDefinitionRecord { Id = "right", NamespaceId = "shared", DisplayName = "Right", Version = "1" }, 0, "right-seed");
            await local.SyncWithAsync(remote, ["shared"]);
            Assert.NotNull(await remote.GetAsync<AgentDefinitionRecord>("shared", "left"));
            Assert.NotNull(await local.GetAsync<AgentDefinitionRecord>("shared", "right"));
            var a = (await local.GetAsync<AgentDefinitionRecord>("shared", "left"))!;
            var b = (await remote.GetAsync<AgentDefinitionRecord>("shared", "left"))!;
            await local.SaveAsync(a with { DisplayName = "Merged label" }, a.Revision, "left-edit");
            await remote.SaveAsync(b with { Version = "2" }, b.Revision, "right-edit");
            await local.SyncWithAsync(remote, ["shared"]);
            a = (await local.GetAsync<AgentDefinitionRecord>("shared", "left"))!;
            b = (await remote.GetAsync<AgentDefinitionRecord>("shared", "left"))!;
            Assert.Equal("Merged label", a.DisplayName); Assert.Equal("2", a.Version);
            Assert.Equal(a.DisplayName, b.DisplayName); Assert.Equal(a.Version, b.Version);
            await local.SaveAsync(a with { DisplayName = "Local choice" }, a.Revision, "left-conflict");
            await remote.SaveAsync(b with { DisplayName = "Remote choice" }, b.Revision, "right-conflict");
            await local.SyncWithAsync(remote, ["shared"]);
            Assert.Single(await local.ListAsync<ConflictRecord>("shared"));
            Assert.Single(await remote.ListAsync<ConflictRecord>("shared"));
            Assert.Equal("Local choice", (await local.GetAsync<AgentDefinitionRecord>("shared", "left"))!.DisplayName);
            Assert.Equal("Remote choice", (await remote.GetAsync<AgentDefinitionRecord>("shared", "left"))!.DisplayName);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Independent_handles_to_same_root_cannot_enter_sync()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-den-sync-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var first = await DenStore.CreateAsync(root, [new("shared", "team", true)]);
            await using var second = await DenStore.OpenAsync(root);
            var access = new SourceAccess();
            var error = await Assert.ThrowsAsync<DenException>(() =>
                new DulcheDen(first, access, "owner").SyncWithAsync(new DulcheDen(second, access, "owner"), ["shared"]));
            Assert.Equal(DenErrorCode.InvalidRecord, error.Code);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Independent_instances_keep_same_root_writer_exclusion_and_revision_conflict()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-den-sync-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var first = await DenStore.CreateAsync(root, [new("shared", "team", true)]);
            await using var second = await DenStore.OpenAsync(root);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var access = new TargetAccess(root) { AtPublication = async () =>
            { entered.SetResult(); await release.Task.WaitAsync(deadline.Token); } };
            var record = new AgentDefinitionRecord { Id = "agent", NamespaceId = "shared", DisplayName = "First", Version = "1" };
            var firstSave = new DulcheDen(first, access, "owner").SaveAsync(record, 0, "first", deadline.Token);
            await entered.Task.WaitAsync(deadline.Token);
            var secondSave = new DulcheDen(second, new SourceAccess(), "owner").SaveAsync(
                record with { DisplayName = "Second" }, 0, "second", deadline.Token);
            try { Assert.False(secondSave.IsCompleted); }
            finally { release.TrySetResult(); }
            await firstSave;
            var error = await Assert.ThrowsAsync<DenException>(async () => await secondSave);
            Assert.Equal(DenErrorCode.Conflict, error.Code);
            Assert.Equal("First", (await new DulcheDen(second, new SourceAccess(), "owner")
                .GetAsync<AgentDefinitionRecord>("shared", "agent"))!.DisplayName);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class SourceAccess : IDenAccessPolicy
    {
        public bool ReadAllowed = true;
        public ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId, DenPermission permission, CancellationToken ct) =>
            ValueTask.FromResult(permission != DenPermission.Read || ReadAllowed);
    }
    private sealed class TargetAccess(string root) : IDenAccessPolicy
    {
        public bool Changed; public Func<Task>? AtPublication;
        public async ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId, DenPermission permission, CancellationToken ct)
        {
            if (!Changed && permission == DenPermission.Write && Directory.GetDirectories(Path.Combine(root, "transactions")).Length > 0)
            { Changed = true; await AtPublication!(); }
            return true;
        }
    }
}
