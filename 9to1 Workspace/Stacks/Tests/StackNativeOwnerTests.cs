using Haven.Application;
using HavenOS.Apps.Stacks.NativeUI;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace HavenOS.Apps.Stacks.Tests;

public sealed class StackNativeOwnerTests
{
    [Fact]
    public async Task Actual_Home_claim_creates_canonical_domain_and_consumed_handle_never_replays()
    {
        await using var fixture = await Fixture.Create();
        var intent = fixture.CreateDomainIntent("Actual owner branch");
        var capability = await fixture.Approve(intent);
        var result = await fixture.Owner.ExecuteAsync(intent, capability);
        Assert.True(result.CanonicalWriteAcknowledged);
        var reader = new StackEngine(new JsonFileStackProjectStore(fixture.Binding.ProjectDirectory));
        await reader.OpenProjectAsync();
        Assert.Contains(await reader.GetLineageAsync(), domain => domain.Id == result.DomainId && domain.Name == "Actual owner branch");
        var before = await File.ReadAllBytesAsync(fixture.ManifestPath);
        Assert.NotNull(await Record.ExceptionAsync(() => fixture.Owner.ExecuteAsync(intent, capability)));
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.ManifestPath));
        Assert.Equal(HomePermissionRequestState.Succeeded, (await fixture.Permissions.GetAuthorizationAsync(capability.RequestId)).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_first_owner_outcome_audit_retry_never_repeats_canonical_domain_write(bool afterPublication)
    {
        await using var fixture = await Fixture.Create(afterPublication);
        var intent = fixture.CreateDomainIntent("Audit-only retry");
        var capability = await fixture.Approve(intent);
        fixture.Store.FailAudit = true;
        var pending = await Assert.ThrowsAsync<StackNativeWriteAuditPendingException>(() => fixture.Owner.ExecuteAsync(intent, capability));
        var before = await File.ReadAllBytesAsync(fixture.ManifestPath);
        var foreign = new StackNativeWriteCoordinator(fixture, fixture.Home);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => foreign.RetryAuditAsync(pending));
        var result = await fixture.Owner.RetryAuditAsync(pending);
        Assert.True(result.CanonicalWriteAcknowledged);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.ManifestPath));
        var reader = new StackEngine(new JsonFileStackProjectStore(fixture.Binding.ProjectDirectory));
        await reader.OpenProjectAsync();
        Assert.Single(await reader.GetLineageAsync(), domain => domain.Name == "Audit-only retry");
        Assert.Equal(HomePermissionRequestState.Succeeded, (await fixture.Permissions.GetAuthorizationAsync(capability.RequestId)).State);
    }

    [Fact]
    public async Task Foreign_issuer_and_revoked_original_binding_cannot_edit_canonical_project()
    {
        await using var fixture = await Fixture.Create();
        var intent = fixture.CreateDomainIntent("Denied branch");
        var capability = await fixture.Approve(intent);
        var before = await File.ReadAllBytesAsync(fixture.ManifestPath);
        var foreign = new StackNativeWriteCoordinator(fixture, new(fixture.Resources, fixture.Permissions));
        Assert.NotNull(await Record.ExceptionAsync(() => foreign.ExecuteAsync(intent, capability)));
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.ManifestPath));
        fixture.Revoked = true;
        Assert.NotNull(await Record.ExceptionAsync(() => fixture.Owner.ExecuteAsync(intent, capability)));
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.ManifestPath));
    }

    // Real canonical Home/store/Stack owners; only Files folder issuance/admission is a
    // controlled fixture boundary. This is not production Files child mapping authority.
    private sealed class Fixture : IStackNativeWorkspaceAuthority, ICanonicalResourceAccessResolver, IAsyncDisposable
    {
        private string _root = "";
        public StackNativeWorkspaceBinding Binding { get; private set; } = null!;
        public StackEngine Engine { get; private set; } = null!;
        public StackDomainSnapshot Main { get; private set; } = null!;
        public ResourceAuthorizationService Resources { get; private set; } = null!;
        public HomePermissionTrustService Permissions { get; private set; } = null!;
        public HomeResourceOperationBroker Home { get; private set; } = null!;
        public StackNativeWriteCoordinator Owner { get; private set; } = null!;
        public FaultStore Store { get; private set; } = null!;
        public bool Revoked { get; set; }
        public string ResourceKind => "files.item";
        public string ManifestPath => Path.Combine(Binding.ProjectDirectory, ".branches", "stack.manifest.json");
        public static async Task<Fixture> Create(bool afterPublication = false)
        {
            var result = new Fixture { _root = Path.Combine(Path.GetTempPath(), "astra-stack-home-" + Guid.NewGuid().ToString("N")) };
            Directory.CreateDirectory(result._root);
            result.Store = new(new FileHomeCoreStateStore(Path.Combine(result._root, "home.json")), afterPublication);
            var profiles = new HomeLocalProfileIdentity(result.Store, new OperatingSystemPrincipalSource());
            var actor = await profiles.GetCurrentAsync(default) ?? throw new InvalidOperationException("OS profile unavailable.");
            var directory = Path.Combine(result._root, "project");
            result.Engine = new(new JsonFileStackProjectStore(directory));
            result.Main = await result.Engine.CreateProjectAsync(new("Owner fixture", StackStorageMode.Files, directory),
                new(actor.ActorId, Enum.GetValues<StackCapability>().ToHashSet()));
            result.Binding = new(actor.ProfileId, actor.ActorId, actor.AuthenticationRevision, Guid.NewGuid(), "controlled-folder-revision", result.Main.ProjectId, directory);
            result.Resources = new(profiles, [result, new StackNativeResourceAccessResolver(result, false), new StackNativeResourceAccessResolver(result, true)]);
            result.Permissions = new(result.Store, new StackNativeActionPolicies().TryGet);
            result.Home = new(result.Resources, result.Permissions); result.Owner = new(result, result.Home);
            return result;
        }
        public StackNativeWriteIntent CreateDomainIntent(string name) =>
            StackNativeWriteIntent.CreateDomain(Binding, Engine.LoadedRevisionToken!, Main, name);
        public async Task<HomeResourceExecutionCapability> Approve(StackNativeWriteIntent intent)
        {
            var pending = await Home.AuthorizeAsync("stacks", intent.ActionId, intent.Scopes, intent.Arguments,
                "Explicit controlled fixture Stack edit", null, Binding.AuthenticationRevision);
            Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
            Assert.True((await Permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            return Assert.IsType<HomeResourceExecutionCapability>(await Home.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments));
        }
        public Task<StackNativeWorkspaceBinding?> GetCurrentAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(!Revoked && projectId == Binding.ProjectId ? Binding : null);
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ResourceAccessDecision(!Revoked && actor.ActorId == Binding.ActorId && actor.ProfileId == Binding.ProfileId &&
                actor.AuthenticationRevision == Binding.AuthenticationRevision && scope.Id == Binding.FilesFolderId.ToString("D") &&
                scope.Revision == Binding.FolderRevision && scope.Access == ResourceAccess.Write &&
                actionId is "stacks.domain.create" or "stacks.domain.set-active" or "stacks.source.commit",
                "controlled-fixture-files-authority", actor.ActorId, scope.Revision, null));
        public ValueTask DisposeAsync() { Directory.Delete(_root, true); return ValueTask.CompletedTask; }
    }

    private sealed class FaultStore(IHomeCoreStateStore inner, bool afterPublication) : IHomeCoreStateStore
    {
        public bool FailAudit;
        public string FailureCode = "StackCanonicalWriteCommitted";
        public int FailAuditWrites;
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default) => inner.ReadAsync(ct);
        public async Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expected, CancellationToken ct = default)
        {
            if ((FailAudit || FailAuditWrites > 0) && record.RecordType == "home.permissions-trust" && record.Payload.GetRawText().Contains(FailureCode, StringComparison.Ordinal))
            {
                FailAudit = false;
                if (FailAuditWrites > 0) FailAuditWrites--;
                if (afterPublication) _ = await inner.WriteAsync(record, expected, ct);
                throw new IOException("Injected actual audit storage failure.");
            }
            return await inner.WriteAsync(record, expected, ct);
        }
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long expected,
            AuthenticatedResourceActor actor, IHomeStateCommitActorGuard guard, CancellationToken ct = default)
            => inner.WriteGuardedAsync(record, expected, actor, guard, ct);
    }
}
