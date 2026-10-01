using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure.Tests;

public sealed class ConversationSpaceCommitTests
{
    [Fact]
    public async Task Ordinary_cached_content_saves_cannot_reattach_or_remove_guarded_membership()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync(); await fixture.BindAsync();
        var actor = (await fixture.Actors.GetCurrentAsync(default))!;
        var original = NewConversation() with { SpaceId = Guid.NewGuid() };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Repository.UpsertConversationAsync(original, default));
        Assert.Null(await fixture.Repository.GetAsync(original.Id, default));
        async Task CommitAsync(Conversation? before, Conversation after)
        {
            ConversationSpaceChange[] change = [new(before, after)];
            var admission = (await fixture.Authority.CaptureAsync(actor, fixture.Identity.StoreId, change, new AllowSpace()))!;
            Assert.Equal(ConversationSpaceCommitStatus.Committed,
                (await fixture.Repository.CompareExchangeSpaceAsync(fixture.Identity.StoreId, change, admission)).Status);
        }
        await CommitAsync(null, original);
        var detached = original with { SpaceId = null };
        await CommitAsync(original, detached);
        await fixture.Repository.UpsertConversationAsync(original with { Title = "Saved after detach", IsPinned = true }, default);
        var saved = (await fixture.Repository.GetAsync(original.Id, default))!;
        Assert.Null(saved.SpaceId); Assert.Equal("Saved after detach", saved.Title); Assert.True(saved.IsPinned);
        var reassigned = saved with { SpaceId = Guid.NewGuid() };
        await CommitAsync(saved, reassigned);
        await fixture.Repository.UpsertConversationAsync(saved with { Title = "Saved after reassignment" }, default);
        var current = (await fixture.Repository.GetAsync(original.Id, default))!;
        Assert.Equal(reassigned.SpaceId, current.SpaceId); Assert.Equal("Saved after reassignment", current.Title);
        var neutral = NewConversation(); await fixture.Repository.UpsertConversationAsync(neutral, default);
        Assert.Equal(neutral, await fixture.Repository.GetAsync(neutral.Id, default));
    }

    [Fact]
    public async Task Membership_pages_include_archived_and_temporary_rows_pin_actual_root_and_never_mutate()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        var space = Guid.NewGuid();
        var first = NewConversation() with { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), SpaceId = space, IsArchived = true };
        var second = NewConversation() with { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), SpaceId = space, IsTemporary = true };
        var other = NewConversation() with { SpaceId = Guid.NewGuid() };
        await fixture.BindAsync();
        var actor = (await fixture.Actors.GetCurrentAsync(default))!;
        ConversationSpaceChange[] seeds = [new(null, first), new(null, second), new(null, other)];
        var admission = (await fixture.Authority.CaptureAsync(actor, fixture.Identity.StoreId, seeds, new AllowSpace()))!;
        Assert.Equal(ConversationSpaceCommitStatus.Committed, (await fixture.Repository.CompareExchangeSpaceAsync(fixture.Identity.StoreId, seeds, admission)).Status);
        Assert.Empty(await fixture.Repository.GetBySpaceAsync(space, 100, default));
        var page = await fixture.Repository.ReadSpaceMembershipAsync(fixture.Identity.StoreId, space, limit: 1);
        Assert.Equal(ConversationSpaceReadStatus.Available, page.Status);
        Assert.Equal(fixture.Identity.StoreId, page.StoreIdentity.StoreId);
        Assert.Equal(first, Assert.Single(page.Rows)); Assert.True(page.HasMore);
        var next = await fixture.Repository.ReadSpaceMembershipAsync(fixture.Identity.StoreId, space, first.Id, 1);
        Assert.Equal(second, Assert.Single(next.Rows)); Assert.False(next.HasMore);
        var empty = await fixture.Repository.ReadSpaceMembershipAsync(fixture.Identity.StoreId, space, second.Id);
        Assert.Empty(empty.Rows); Assert.False(empty.HasMore);
        var foreign = await fixture.Repository.ReadSpaceMembershipAsync(Guid.NewGuid(), space);
        Assert.Equal(ConversationSpaceReadStatus.StoreMismatch, foreign.Status); Assert.Empty(foreign.Rows);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Repository.ReadSpaceMembershipAsync(fixture.Identity.StoreId, space, limit: 1001));
        foreach (var row in new[] { first, second, other }) Assert.Equal(row, await fixture.Repository.GetAsync(row.Id, default));
    }

    [Fact]
    public async Task Actual_home_binding_and_exact_sql_rows_preserve_content_and_reject_stale_duplicate_or_foreign_root()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        var actor = (await fixture.Actors.GetCurrentAsync(default))!;
        var first = NewConversation() with { IsPinned = true, IsArchived = true, CompactedAt = DateTimeOffset.UtcNow };
        var proposed = first with { SpaceId = Guid.NewGuid(), UpdatedAt = first.UpdatedAt.AddSeconds(1) };
        ConversationSpaceChange[] create = [new(null, first)];
        Assert.Null(await fixture.Authority.CaptureAsync(actor, fixture.Identity.StoreId, create, new AllowSpace()));
        await fixture.BindAsync();
        var admission = await fixture.Authority.CaptureAsync(actor, fixture.Identity.StoreId, create, new AllowSpace());
        Assert.NotNull(admission);
        Assert.Equal(ConversationSpaceCommitStatus.Committed, (await fixture.Repository.CompareExchangeSpaceAsync(fixture.Identity.StoreId, create, admission!)).Status);
        ConversationSpaceChange[] edit = [new(first, proposed)];
        admission = await fixture.Authority.CaptureAsync(actor, fixture.Identity.StoreId, edit, new AllowSpace());
        Assert.Equal(ConversationSpaceCommitStatus.Committed, (await fixture.Repository.CompareExchangeSpaceAsync(fixture.Identity.StoreId, edit, admission!)).Status);
        Assert.Equal(proposed, await fixture.Repository.GetAsync(first.Id, default));
        Assert.Equal(ConversationSpaceCommitStatus.RevisionConflict, (await fixture.Repository.CompareExchangeSpaceAsync(fixture.Identity.StoreId, edit, admission!)).Status);
        Assert.Equal(ConversationSpaceCommitStatus.StoreMismatch, (await fixture.Repository.CompareExchangeSpaceAsync(Guid.NewGuid(), edit, admission!)).Status);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Repository.CompareExchangeSpaceAsync(fixture.Identity.StoreId, [edit[0], edit[0]], admission!));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Repository.CompareExchangeSpaceAsync(fixture.Identity.StoreId,
            [new(proposed, proposed with { Title = "Unapproved overwrite" })], admission!));
        Assert.Equal(proposed, await fixture.Repository.GetAsync(first.Id, default));
        var evidence = await fixture.Provider.ReadAsync(fixture.Identity.StoreId.ToString("D"), default);
        Assert.NotNull(evidence); Assert.False(evidence!.IsEmpty);
    }

    [Fact]
    public async Task Batch_conflict_and_final_denial_roll_back_all_rows_and_caller_list_cannot_retarget()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync(); await fixture.BindAsync();
        var first = NewConversation(); var second = NewConversation();
        await fixture.Repository.UpsertConversationAsync(first, default); await fixture.Repository.UpsertConversationAsync(second, default);
        var target = Guid.NewGuid();
        var changes = new List<ConversationSpaceChange> { new(first, first with { SpaceId = target }), new(second, second with { SpaceId = target }) };
        var actor = (await fixture.Actors.GetCurrentAsync(default))!;
        var deny = new AllowSpace { Check = context => ValueTask.FromResult(context.Phase != ConversationSpaceCommitPhase.Publication) };
        var admission = await fixture.Authority.CaptureAsync(actor, fixture.Identity.StoreId, changes, deny);
        Assert.Equal(ConversationSpaceCommitStatus.AdmissionRejected, (await fixture.Repository.CompareExchangeSpaceAsync(fixture.Identity.StoreId, changes, admission!)).Status);
        Assert.Equal(first, await fixture.Repository.GetAsync(first.Id, default)); Assert.Equal(second, await fixture.Repository.GetAsync(second.Id, default));
        var stale = changes.ToArray(); stale[1] = new(second with { Title = "Stale" }, second with { Title = "Stale", SpaceId = target });
        admission = await fixture.Authority.CaptureAsync(actor, fixture.Identity.StoreId, stale, new AllowSpace());
        Assert.Equal(ConversationSpaceCommitStatus.RevisionConflict, (await fixture.Repository.CompareExchangeSpaceAsync(fixture.Identity.StoreId, stale, admission!)).Status);
        Assert.Equal(first, await fixture.Repository.GetAsync(first.Id, default));
        var mutateCaller = new AllowSpace { Check = _ => { changes[0] = new(first, first with { SpaceId = Guid.NewGuid() }); return ValueTask.FromResult(true); } };
        admission = await fixture.Authority.CaptureAsync(actor, fixture.Identity.StoreId, changes, mutateCaller);
        Assert.Equal(ConversationSpaceCommitStatus.Committed, (await fixture.Repository.CompareExchangeSpaceAsync(fixture.Identity.StoreId, changes, admission!)).Status);
        Assert.Equal(target, (await fixture.Repository.GetAsync(first.Id, default))!.SpaceId);
        Assert.Equal(target, (await fixture.Repository.GetAsync(second.Id, default))!.SpaceId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_sql_writer_wait_does_not_preserve_changed_home_session_or_binding(bool revokeBinding)
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync(); await fixture.BindAsync();
        var original = NewConversation(); await fixture.Repository.UpsertConversationAsync(original, default);
        ConversationSpaceChange[] changes = [new(original, original with { SpaceId = Guid.NewGuid() })];
        var actor = (await fixture.Actors.GetCurrentAsync(default))!;
        var admission = (await fixture.Authority.CaptureAsync(actor, fixture.Identity.StoreId, changes, new AllowSpace()))!;
        await using var held = await fixture.Database.OpenAsync(default);
        await using var writer = held.BeginTransaction(deferred: false);
        var factory = new SignallingFactory(fixture.Database);
        var repository = new ConversationRepository(factory);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var pending = Task.Run(() => repository.CompareExchangeSpaceAsync(fixture.Identity.StoreId, changes, admission, deadline.Token));
        try
        {
            await factory.Opened.Task.WaitAsync(deadline.Token);
            Assert.False(pending.IsCompleted);
            if (revokeBinding)
            {
                var record = (await fixture.Home.ReadAsync()).State!.Records.Single(item => item.RecordType == "home.local-store-ownership");
                var payload = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
                Assert.True((await fixture.Home.WriteAsync(record with { Payload = System.Text.Json.JsonSerializer.SerializeToElement(payload with { ProfileId = "revoked-profile" }) }, record.Revision)).IsSuccess);
            }
            else fixture.Actors.Inner = new HomeLocalProfileIdentity(fixture.Home, new OperatingSystemPrincipalSource());
        }
        finally { writer.Rollback(); }
        Assert.Equal(ConversationSpaceCommitStatus.AdmissionRejected, (await pending).Status);
        Assert.Equal(original, await fixture.Repository.GetAsync(original.Id, default));
    }

    [Fact]
    public async Task Home_binding_revocation_during_final_space_check_rolls_back_uncommitted_sql_assignment()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync(); await fixture.BindAsync();
        var original = NewConversation(); await fixture.Repository.UpsertConversationAsync(original, default);
        ConversationSpaceChange[] changes = [new(original, original with { SpaceId = Guid.NewGuid() })];
        var actor = (await fixture.Actors.GetCurrentAsync(default))!;
        var space = new AllowSpace { Check = async context =>
        {
            if (context.Phase == ConversationSpaceCommitPhase.Publication)
            {
                var record = (await fixture.Home.ReadAsync()).State!.Records.Single(item => item.RecordType == "home.local-store-ownership");
                var payload = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
                Assert.True((await fixture.Home.WriteAsync(record with { Payload = JsonSerializer.SerializeToElement(payload with { ProfileId = "revoked-profile" }) }, record.Revision)).IsSuccess);
            }
            return true;
        } };
        var admission = (await fixture.Authority.CaptureAsync(actor, fixture.Identity.StoreId, changes, space))!;
        Assert.Equal(ConversationSpaceCommitStatus.AdmissionRejected,
            (await fixture.Repository.CompareExchangeSpaceAsync(fixture.Identity.StoreId, changes, admission)).Status);
        Assert.Equal(original, await fixture.Repository.GetAsync(original.Id, default));
    }

    private static Conversation NewConversation()
    {
        var now = DateTimeOffset.UtcNow;
        return new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Retained canonical title", null, null, false, false, now, now);
    }
    private sealed class AllowSpace : IConversationSpaceCommitAdmission
    {
        public Func<ConversationSpaceCommitContext, ValueTask<bool>>? Check;
        public ValueTask<bool> CheckAsync(ConversationSpaceCommitContext context, CancellationToken ct) => Check?.Invoke(context) ?? ValueTask.FromResult(true);
    }
    private sealed class Actors(IAuthenticatedResourceActorSource initial) : IAuthenticatedResourceActorSource
    {
        public IAuthenticatedResourceActorSource Inner = initial;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => Inner.GetCurrentAsync(ct);
    }
    private sealed class SignallingFactory(ISqliteConnectionFactory inner) : ISqliteConnectionFactory
    {
        public TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<SqliteConnection> OpenAsync(CancellationToken ct)
        { var connection = await inner.OpenAsync(ct); Opened.TrySetResult(); return connection; }
    }
    private sealed class Fixture : IAppPaths, IDisposable
    {
        public Fixture()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "astra-conversation-guard-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(DataDirectory);
            Database = new(this); Repository = new(Database); Provider = new(Database);
            Home = new(Path.Combine(DataDirectory, "home.json")); Profiles = new(Home, new OperatingSystemPrincipalSource()); Actors = new(Profiles);
            Ownership = new(Home, Profiles, new HomeLocalStoreEvidenceRegistry([Provider]), new HomePermissionTrustService(Home, (_, _) => null));
            Authority = new(Actors, new HomeResourceStoreOwnershipAuthority(Ownership, Actors));
        }
        public SqliteDatabase Database { get; } public ConversationRepository Repository { get; }
        public ConversationLocalStoreEvidenceProvider Provider { get; } public FileHomeCoreStateStore Home { get; }
        public HomeLocalProfileIdentity Profiles { get; } public Actors Actors { get; }
        public HomeLocalStoreOwnership Ownership { get; } public ConversationLocalStoreAuthority Authority { get; }
        public ResourceStoreIdentity Identity { get; private set; } = null!;
        public async Task InitializeAsync() { await new ConversationProductionDatabase(Database).InitializeAsync(default); Identity = await Database.GetStoreIdentityAsync(default); }
        public Task<HomeLocalStoreBinding> BindAsync() => Ownership.BindNewEmptyAsync("conversation", Identity.StoreId.ToString("D"));
        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "conversation.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
