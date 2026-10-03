using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomePersonalDenFactoryTests
{
    [Fact]
    public async Task Actual_new_Den_requires_Home_binding_then_personal_records_work_without_Admin_or_Execute()
    {
        await using var fixture = await Fixture.Create();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Factory.OpenAsync());
        await fixture.Ownership.BindNewEmptyAsync("den", fixture.Provider.Store.Manifest.DenId);
        var session = await fixture.Factory.OpenAsync();
        var evidenceBefore = await fixture.Provider.ReadAsync(session.DenId, default);
        var agent = await session.Den.SaveAsync(Agent(), 0, "save-agent");
        Assert.Equal(agent.Id, (await session.Den.GetAsync<AgentDefinitionRecord>("personal", agent.Id))!.Id);
        var attachment = await session.Den.AddAttachmentAsync("personal", agent.Id, "agent", "application/octet-stream", new byte[] { 1, 2, 3 }, "attach");
        Assert.Equal(new byte[] { 1, 2, 3 }, await session.Den.ReadAttachmentAsync("personal", attachment.Id));
        Assert.False(await session.Den.AccessPolicy.IsAllowedAsync(session.Actor.ActorId, "personal", agent.Id, DenPermission.Execute));
        Assert.False(await session.Den.AccessPolicy.IsAllowedAsync(session.Actor.ActorId, "personal", agent.Id, DenPermission.Administer));
        var evidenceAfter = await fixture.Provider.ReadAsync(session.DenId, default);
        Assert.NotEqual(evidenceBefore!.Revision, evidenceAfter!.Revision);
        Assert.False(evidenceAfter.IsEmpty);
    }

    [Fact]
    public async Task Retained_session_observes_namespace_sharing_changed_by_another_real_store()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Ownership.BindNewEmptyAsync("den", fixture.Provider.Store.Manifest.DenId);
        var session = await fixture.Factory.OpenAsync();
        await session.Den.SaveAsync(Agent(), 0, "agent");
        await using var other = await DenStore.OpenAsync(fixture.DenRoot);
        var admin = new DulcheDen(other, new NamespaceAccessPolicy([new("admin", other.Manifest.DenId, DenPermission.Administer)]), "admin");
        await admin.SetNamespaceSharingAsync("personal", true, other.Manifest.Revision, "share");
        Assert.False(fixture.Provider.Store.Manifest.Namespaces.Single().Shared); // deliberately retained old cache
        var denied = await Assert.ThrowsAsync<DenException>(() => session.Den.GetAsync<AgentDefinitionRecord>("personal", "agent"));
        Assert.Equal(DenErrorCode.Forbidden, denied.Code);
    }

    [Fact]
    public async Task Retained_session_rejects_changed_Home_binding_receipt()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Ownership.BindNewEmptyAsync("den", fixture.Provider.Store.Manifest.DenId);
        var session = await fixture.Factory.OpenAsync();
        var binding = Assert.Single((await fixture.Home.ReadAsync()).State!.Records, item => item.RecordType == "home.local-store-ownership");
        Assert.True((await fixture.Home.WriteAsync(binding, binding.Revision)).IsSuccess);
        var denied = await Assert.ThrowsAsync<DenException>(() => session.Den.SaveAsync(Agent(), 0, "denied"));
        Assert.Equal(DenErrorCode.Forbidden, denied.Code);
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.DenRoot, "records"), "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Retained_session_rejects_changed_authentication_revision_without_new_grants()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Ownership.BindNewEmptyAsync("den", fixture.Provider.Store.Manifest.DenId);
        var session = await fixture.Factory.OpenAsync();
        fixture.Actors.Changed = session.Actor with { AuthenticationRevision = "changed-session" };
        var denied = await Assert.ThrowsAsync<DenException>(() => session.Den.SaveAsync(Agent(), 0, "denied"));
        Assert.Equal(DenErrorCode.Forbidden, denied.Code);
        Assert.Null(await fixture.Provider.ReadAsync(session.DenId, default));
    }

    [Fact]
    public async Task Existing_even_empty_Den_is_not_automatically_claimed_as_new()
    {
        await using var fixture = await Fixture.Create(existing: true);
        var observed = await fixture.Provider.ReadAsync(fixture.Provider.Store.Manifest.DenId, default);
        Assert.True(observed!.IsEmpty);
        Assert.False(observed.NewlyCreated);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Ownership.BindNewEmptyAsync("den", observed.StoreId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Factory.OpenAsync());
    }

    [Fact]
    public async Task Ownership_observation_rejects_linked_storage_instead_of_claiming_external_bytes()
    {
        if (OperatingSystem.IsWindows()) return;
        await using var fixture = await Fixture.Create();
        var outside = Path.Combine(fixture.Root, "external");
        await File.WriteAllTextAsync(outside, "outside Den");
        File.CreateSymbolicLink(Path.Combine(fixture.DenRoot, "linked"), outside);
        var denied = await Assert.ThrowsAsync<DenException>(() => fixture.Provider.ReadAsync(fixture.Provider.Store.Manifest.DenId, default).AsTask());
        Assert.Equal(DenErrorCode.InvalidManifest, denied.Code);
    }

    private static AgentDefinitionRecord Agent() => new() { Id = "agent", NamespaceId = "personal", DisplayName = "Agent", Version = "1" };
    private sealed class Actors(HomeLocalProfileIdentity profiles) : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor? Changed { get; set; }
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) =>
            Changed is null ? profiles.GetCurrentAsync(cancellationToken) : ValueTask.FromResult<AuthenticatedResourceActor?>(Changed);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public required string Root { get; init; }
        public string DenRoot => Path.Combine(Root, "den");
        public required FileHomeCoreStateStore Home { get; init; }
        public required Actors Actors { get; init; }
        public required HomeDenStoreEvidenceProvider Provider { get; init; }
        public required HomeLocalStoreOwnership Ownership { get; init; }
        public required HomePersonalDenFactory Factory { get; init; }
        public static async Task<Fixture> Create(bool existing = false)
        {
            var root = Path.Combine(Path.GetTempPath(), "astra-personal-den-" + Guid.NewGuid().ToString("N"));
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var actors = new Actors(profiles);
            var denRoot = Path.Combine(root, "den");
            if (existing) { await using var original = await DenStore.CreateAsync(denRoot, [new("personal", "personal")]); }
            var provider = existing ? await HomeDenStoreEvidenceProvider.OpenAsync(denRoot, actors)
                : await HomeDenStoreEvidenceProvider.CreateAsync(denRoot, actors);
            var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([provider]),
                new HomePermissionTrustService(home, (_, _) => null));
            var authority = new HomeResourceStoreOwnershipAuthority(ownership, actors);
            return new() { Root = root, Home = home, Actors = actors, Provider = provider, Ownership = ownership,
                Factory = new(provider, authority, actors) };
        }
        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
