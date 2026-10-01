using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;

namespace Haven.Desktop.Tests;

public sealed class OwnedSpaceConversationsTests
{
    [Fact]
    public async Task Actual_two_owned_stores_preserve_chat_fields_and_reject_changed_space_or_stale_row()
    {
        var token = TestContext.Current.CancellationToken;
        using var fixture = new Fixture(); await fixture.InitializeAsync(token);
        var source = await fixture.Spaces.CreateAsync("Source", cancellationToken: token);
        var destination = await fixture.Spaces.CreateAsync("Destination", cancellationToken: token);
        var now = DateTimeOffset.UtcNow;
        var original = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Preserved title",
            Guid.NewGuid(), null, true, false, now, now, SpaceId: source.Id);
        await fixture.Repository.UpsertConversationAsync(original, token);
        var owner = fixture.Owner(fixture.Repository);
        var result = await owner.AssignAsync(original, Snapshot(source), Snapshot(destination), token);
        Assert.Equal(ConversationSpaceCommitStatus.Committed, result.Status);
        var saved = Assert.IsType<Conversation>(await fixture.Repository.GetAsync(original.Id, token));
        Assert.Equal(original with { SpaceId = destination.Id, UpdatedAt = saved.UpdatedAt }, saved);
        Assert.Equal(ConversationSpaceCommitStatus.RevisionConflict,
            (await owner.AssignAsync(original, Snapshot(source), Snapshot(destination), token)).Status);
        await fixture.Spaces.DeleteAsync(source.Id, token);
        Assert.Equal(ConversationSpaceCommitStatus.AdmissionRejected,
            (await owner.AssignAsync(saved, Snapshot(destination), Snapshot(source), token)).Status);
        Assert.Equal(saved, await fixture.Repository.GetAsync(original.Id, token));
    }

    [Fact]
    public async Task Destination_archived_while_actual_sql_writer_lease_is_held_denies_assignment()
    {
        var token = TestContext.Current.CancellationToken;
        using var fixture = new Fixture(); await fixture.InitializeAsync(token);
        var destination = await fixture.Spaces.CreateAsync("Destination", cancellationToken: token);
        var now = DateTimeOffset.UtcNow;
        var original = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Unchanged",
            null, null, false, false, now, now);
        await fixture.Repository.UpsertConversationAsync(original, token);
        await using var held = await fixture.Database.OpenAsync(token);
        await using var writer = held.BeginTransaction(deferred: false);
        var factory = new SignallingFactory(fixture.Database);
        var owner = fixture.Owner(new ConversationRepository(factory));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var pending = Task.Run(() => owner.AssignAsync(original, null, Snapshot(destination), deadline.Token), deadline.Token);
        try
        {
            await factory.Opened.Task.WaitAsync(deadline.Token);
            Assert.False(pending.IsCompleted);
            await fixture.Spaces.DeleteAsync(destination.Id, deadline.Token);
        }
        finally { writer.Rollback(); }
        Assert.Equal(ConversationSpaceCommitStatus.AdmissionRejected, (await pending).Status);
        Assert.Equal(original, await fixture.Repository.GetAsync(original.Id, token));
    }

    [Fact]
    public async Task New_chat_requires_separate_explicit_SQL_binding_and_preserves_canonical_identity_on_replay()
    {
        var token = TestContext.Current.CancellationToken;
        using var fixture = new Fixture(); await fixture.InitializeAsync(token, bindConversations: false);
        var destination = await fixture.Spaces.CreateAsync("New chat destination", cancellationToken: token);
        var now = DateTimeOffset.UtcNow;
        var proposed = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Actual new chat",
            null, null, false, false, now, now, SpaceId: destination.Id);
        var owner = fixture.Owner(fixture.Repository);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.CreateChatAsync(proposed, Snapshot(destination), token));
        Assert.Null(await fixture.Repository.GetAsync(proposed.Id, token));
        var sql = await fixture.Database.GetStoreIdentityAsync(token);
        await fixture.Ownership.BindNewEmptyAsync(fixture.Actor, "conversation", sql.StoreId.ToString("D"), token);
        Assert.Equal(ConversationSpaceCommitStatus.Committed, (await owner.CreateChatAsync(proposed, Snapshot(destination), token)).Status);
        Assert.Equal(proposed, await fixture.Repository.GetAsync(proposed.Id, token));
        Assert.Equal(ConversationSpaceCommitStatus.RevisionConflict, (await owner.CreateChatAsync(proposed, Snapshot(destination), token)).Status);
        Assert.Equal(proposed, await fixture.Repository.GetAsync(proposed.Id, token));
        Assert.Single(await fixture.Repository.GetBySpaceAsync(destination.Id, 10, token));
    }

    private static OwnedSpaceConversations.SpaceSnapshot Snapshot(SpaceDefinition space) => new(space.Id, space.Revision);
    private sealed class SignallingFactory(ISqliteConnectionFactory inner) : ISqliteConnectionFactory
    {
        public TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<SqliteConnection> OpenAsync(CancellationToken token)
        { var result = await inner.OpenAsync(token); Opened.TrySetResult(); return result; }
    }
    private sealed class Fixture : IAppPaths, IDisposable
    {
        public Fixture()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "astra-space-chat-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            Settings = new(this); Database = new(this); Repository = new(Database);
            Home = new(Path.Combine(DataDirectory, "home.json")); Actors = new(Home, new OperatingSystemPrincipalSource());
            Ownership = new(Home, Actors, new HomeLocalStoreEvidenceRegistry([
                new SpacesLocalStoreEvidenceProvider(Settings, Settings), new ConversationLocalStoreEvidenceProvider(Database)]),
                new HomePermissionTrustService(Home, (_, _) => null));
            Receipts = new(Ownership, Actors);
            var authority = new SpaceLocalStoreAuthority(Settings, Actors, Receipts, () => true);
            Spaces = new(Settings, token => authority.CaptureWriteAdmissionForActorAsync(Actor, token));
        }
        public async Task InitializeAsync(CancellationToken token, bool bindConversations = true)
        {
            await new ConversationProductionDatabase(Database).InitializeAsync(token);
            Actor = (await Actors.GetCurrentAsync(token))!;
            var settings = await Settings.GetStoreIdentityAsync(token);
            var sql = await Database.GetStoreIdentityAsync(token);
            await Ownership.BindNewEmptyAsync(Actor, "spaces", settings.StoreId.ToString("D"), token);
            if (bindConversations) await Ownership.BindNewEmptyAsync(Actor, "conversation", sql.StoreId.ToString("D"), token);
        }
        public OwnedSpaceConversations Owner(IConversationSpaceCommitStore repository) =>
            new(Actor, Settings, Database, Receipts, new(Actors, Receipts), repository, () => true);
        public VersionedAtomicSettingsStore Settings { get; }
        public SqliteDatabase Database { get; }
        public ConversationRepository Repository { get; }
        public SpaceRegistry Spaces { get; }
        public FileHomeCoreStateStore Home { get; }
        public HomeLocalProfileIdentity Actors { get; }
        public AuthenticatedResourceActor Actor { get; private set; } = null!;
        public HomeLocalStoreOwnership Ownership { get; }
        public HomeResourceStoreOwnershipAuthority Receipts { get; }
        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "conversation.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(DataDirectory, true); }
    }
}
