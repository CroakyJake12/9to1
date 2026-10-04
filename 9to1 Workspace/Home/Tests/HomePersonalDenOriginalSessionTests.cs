using System.Runtime.ExceptionServices;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Actual Home/store/Den writer and original policy. No Execute grant, installed readiness,
/// signed socket, or full Agent runtime is supplied by this fixture.</summary>
public sealed class HomePersonalDenOriginalSessionTests
{
    [Fact]
    public Task Same_issued_session_rechecks_current_authority_and_definition_inside_actual_Save_writer() =>
        WithFixtureAsync(async fixture =>
        {
            var original = await fixture.OpenBoundAsync();
            var definition = await original.Den.SaveAsync(Agent(), 0, "seed-agent", fixture.Token);
            var policy = new WriterPolicy(async (write, token) =>
            {
                Assert.True(await fixture.Factory.IsCurrentOriginalAsync(original, token));
                Assert.False(await fixture.Factory.IsCurrentOriginalAsync(original with { }, token));
                Assert.False(await fixture.Factory.IsCurrentOriginalAsync(
                    new(original.Actor, original.DenId, new DulcheDen(original.Den.Store,
                        original.Den.AccessPolicy, original.Den.PrincipalId)), token));
                Assert.False(await fixture.OtherFactory.IsCurrentOriginalAsync(original, token));
                if (write == 2)
                {
                    // The second Write callback is invoked after DenStore owns its actual writer lease.
                    var current = await original.Den.GetAsync<AgentDefinitionRecord>("personal", definition.Id, token);
                    Assert.Equal(definition.Revision, current!.Revision);
                    Assert.Same(original.Den.Store, fixture.Provider!.Store);
                }
                return true;
            }, original.Den.AccessPolicy);
            var proposed = definition with { DisplayName = "updated" };
            var writer = new DulcheDen(original.Den.Store, policy, original.Den.PrincipalId);
            var save = fixture.Track(writer.SaveAsync(proposed, definition.Revision,
                "original-writer-save", fixture.Token));
            var committed = await save;
            Assert.Equal(3, policy.Writes);
            Assert.Equal(definition.Revision + 1, committed.Revision);
            Assert.Equal("updated", (await original.Den.GetAsync<AgentDefinitionRecord>(
                "personal", committed.Id, fixture.Token))!.DisplayName);
            Assert.False(await original.Den.AccessPolicy.IsAllowedAsync(original.Actor.ActorId,
                "personal", committed.Id, DenPermission.Execute, fixture.Token));
            Assert.False(await original.Den.AccessPolicy.IsAllowedAsync(original.Actor.ActorId,
                "personal", committed.Id, DenPermission.Administer, fixture.Token));
        });

    [Theory]
    [InlineData("actor")]
    [InlineData("binding")]
    public Task Current_retirement_while_same_Save_owns_writer_refuses_without_publishing(string retirement) =>
        WithFixtureAsync(async fixture =>
        {
            var original = await fixture.OpenBoundAsync();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.ReleaseOnClose(() => release.TrySetResult());
            var policy = new WriterPolicy(async (write, token) =>
            {
                if (write == 2)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(token);
                }
                return await fixture.Factory.IsCurrentOriginalAsync(original, token);
            }, original.Den.AccessPolicy);
            var writer = new DulcheDen(original.Den.Store, policy, original.Den.PrincipalId);
            var save = fixture.Track(writer.SaveAsync(Agent(), 0, "retired-writer-save", fixture.Token));
            await entered.Task.WaitAsync(fixture.Token);
            if (retirement == "actor")
                fixture.Actors!.Changed = original.Actor with { AuthenticationRevision = "retired-original" };
            else
            {
                var binding = Assert.Single((await fixture.Home!.ReadAsync(fixture.Token)).State!.Records,
                    item => item.RecordType == "home.local-store-ownership");
                Assert.True((await fixture.Home!.WriteAsync(binding, binding.Revision, fixture.Token)).IsSuccess);
            }
            release.TrySetResult();
            var failure = await Record.ExceptionAsync(() => save);
            var denied = Assert.IsType<DenException>(failure);
            fixture.Expect(denied);
            Assert.Equal(DenErrorCode.Forbidden, denied.Code);
            Assert.False(await fixture.Factory.IsCurrentOriginalAsync(original, fixture.Token));
            Assert.Empty(Directory.GetFiles(Path.Combine(fixture.DenRoot, "records"),
                "*.json", SearchOption.AllDirectories));
        });

    [Fact]
    public Task Original_policy_reads_changed_personal_namespace_without_using_retained_manifest_cache() =>
        WithFixtureAsync(async fixture =>
        {
            var original = await fixture.OpenBoundAsync();
            await fixture.OpenOtherStoreAsync();
            var other = fixture.OtherStore!;
            var admin = new DulcheDen(other,
                new NamespaceAccessPolicy([new("admin", other.Manifest.DenId, DenPermission.Administer)]), "admin");
            await admin.SetNamespaceSharingAsync("personal", true, other.Manifest.Revision,
                "changed-personal-sharing", fixture.Token);
            Assert.False(fixture.Provider!.Store.Manifest.Namespaces.Single().Shared);
            var save = fixture.Track(original.Den.SaveAsync(Agent(), 0, "shared-denied", fixture.Token));
            var denied = Assert.IsType<DenException>(await Record.ExceptionAsync(() => save));
            fixture.Expect(denied);
            Assert.Equal(DenErrorCode.Forbidden, denied.Code);
            Assert.False(await original.Den.AccessPolicy.IsAllowedAsync(original.Actor.ActorId,
                "personal", "agent", DenPermission.Read, fixture.Token));
            Assert.Empty(Directory.GetFiles(Path.Combine(fixture.DenRoot, "records"),
                "*.json", SearchOption.AllDirectories));
        });

    [Fact]
    public Task Same_original_session_cannot_survive_its_owned_provider_retirement() =>
        WithFixtureAsync(async fixture =>
        {
            var original = await fixture.OpenBoundAsync();
            var close = fixture.CloseProviderAsync();
            Assert.Same(close, fixture.CloseProviderAsync());
            await close;
            Assert.False(await fixture.Factory.IsCurrentOriginalAsync(original, fixture.Token));
            Assert.False(await fixture.Factory.IsCurrentOriginalAsync(original with { }, fixture.Token));
        });

    private static AgentDefinitionRecord Agent() => new()
    { Id = "agent", NamespaceId = "personal", DisplayName = "Agent", Version = "1" };

    private sealed class WriterPolicy(Func<int, CancellationToken, Task<bool>> current, IDenAccessPolicy original) : IDenAccessPolicy
    {
        public int Writes { get; private set; }
        public async ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId,
            DenPermission permission, CancellationToken cancellationToken = default)
        {
            if (permission == DenPermission.Write)
            {
                Writes++;
                if (!await current(Writes, cancellationToken)) return false;
            }
            return await original.IsAllowedAsync(principalId, namespaceId, objectId, permission, cancellationToken);
        }
    }

    private sealed class Actors(HomeLocalProfileIdentity profiles) : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor? Changed { get; set; }
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) =>
            Changed is null ? profiles.GetCurrentAsync(cancellationToken) : ValueTask.FromResult<AuthenticatedResourceActor?>(Changed);
    }

    private static async Task WithFixtureAsync(Func<Fixture, Task> body)
    {
        var fixture = new Fixture();
        var failures = new List<Exception>();
        try { await fixture.InitialiseAsync(); await body(fixture); }
        catch (Exception error) { AddFailure(failures, error); }
        await fixture.CloseAsync(failures);
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
    }

    private static void AddFailure(List<Exception> failures, Exception error)
    {
        if (!failures.Any(existing => ReferenceEquals(existing, error))) failures.Add(error);
    }

    private sealed class Fixture
    {
        // The actual Home.Tests project is xUnit 2; its original caller has no v3 TestContext token.
        private readonly CancellationTokenSource _lifetime = new();
        private readonly List<Task> _originals = [];
        private readonly List<Action> _releases = [];
        private readonly HashSet<Exception> _expected = new(ReferenceEqualityComparer.Instance);
        private Task? _providerClose;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "astra-den-original-session-" + Guid.NewGuid().ToString("N"));
        public string DenRoot => Path.Combine(Root, "den");
        public CancellationToken Token => _lifetime.Token;
        public FileHomeCoreStateStore? Home { get; private set; }
        public Actors? Actors { get; private set; }
        public HomeDenStoreEvidenceProvider? Provider { get; private set; }
        public DenStore? OtherStore { get; private set; }
        public HomeLocalStoreOwnership Ownership { get; private set; } = null!;
        public HomePersonalDenFactory Factory { get; private set; } = null!;
        public HomePersonalDenFactory OtherFactory { get; private set; } = null!;

        public async Task InitialiseAsync()
        {
            _lifetime.CancelAfter(TimeSpan.FromSeconds(30));
            Home = new FileHomeCoreStateStore(Path.Combine(Root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(Home, new OperatingSystemPrincipalSource());
            Actors = new(profiles);
            Provider = await HomeDenStoreEvidenceProvider.CreateAsync(DenRoot, Actors, Token);
            Ownership = new(Home, profiles, new HomeLocalStoreEvidenceRegistry([Provider]),
                new HomePermissionTrustService(Home, (_, _) => null));
            var authority = new HomeResourceStoreOwnershipAuthority(Ownership, Actors);
            Factory = new(Provider, authority, Actors);
            OtherFactory = new(Provider, authority, Actors);
        }
        public async Task<HomePersonalDenSession> OpenBoundAsync()
        {
            await Ownership.BindNewEmptyAsync("den", Provider!.Store.Manifest.DenId, Token);
            return await Factory.OpenAsync(Token);
        }
        public async Task OpenOtherStoreAsync() => OtherStore = await DenStore.OpenAsync(DenRoot, Token);
        public Task<T> Track<T>(Task<T> original) { _originals.Add(original); return original; }
        public void ReleaseOnClose(Action release) => _releases.Add(release);
        public void Expect(Exception original) => _expected.Add(original);
        public Task CloseProviderAsync() => _providerClose ??= Provider is null
            ? Task.CompletedTask : Provider.DisposeAsync().AsTask();

        public async Task CloseAsync(List<Exception> failures)
        {
            foreach (var release in _releases)
                try { release(); } catch (Exception error) { AddFailure(failures, error); }
            try { _lifetime.Cancel(); } catch (Exception error) { AddFailure(failures, error); }
            foreach (var original in _originals)
                try { await original; } catch (Exception error) { if (!_expected.Contains(error)) AddFailure(failures, error); }
            if (OtherStore is not null)
                try { await OtherStore.DisposeAsync(); } catch (Exception error) { AddFailure(failures, error); }
            try { await CloseProviderAsync(); } catch (Exception error) { AddFailure(failures, error); }
            try { _lifetime.Dispose(); } catch (Exception error) { AddFailure(failures, error); }
            try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
            catch (Exception error) { AddFailure(failures, error); }
        }
    }
}
