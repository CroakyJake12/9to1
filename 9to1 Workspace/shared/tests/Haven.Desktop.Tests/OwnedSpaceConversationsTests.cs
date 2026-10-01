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
        await fixture.SeedInSpaceAsync(original, source, token);
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

    [Fact]
    public async Task Pending_deletion_survives_restart_blocks_edits_and_detaches_exact_archived_chat()
    {
        var token = TestContext.Current.CancellationToken;
        using var fixture = new Fixture(); await fixture.InitializeAsync(token);
        var space = await fixture.Spaces.CreateAsync("Recoverable", cancellationToken: token);
        var now = DateTimeOffset.UtcNow;
        var original = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Retained",
            null, null, false, false, now, now, IsArchived: true, SpaceId: space.Id);
        await fixture.SeedInSpaceAsync(original, space, token);
        var operationId = Guid.NewGuid();
        var pending = await fixture.Spaces.BeginDeletionAsync(space.Id, space.Revision, operationId, token);
        Assert.Equal(SpaceDeletionStage.AwaitingConversationDetach, pending.Stage);
        var restarted = new SpaceRegistry(fixture.Settings);
        Assert.Equal(pending, await restarted.ReadDeletionAsync(operationId, token));
        Assert.Equal(pending, await fixture.Spaces.BeginDeletionAsync(space.Id, space.Revision, operationId, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Spaces.DeleteAsync(space.Id, token));
        var forged = pending with { ArchivedRevision = pending.ArchivedRevision + 1 };
        Assert.Equal(ConversationSpaceCommitStatus.AdmissionRejected,
            (await fixture.Owner(fixture.Repository).DetachForDeletionAsync(forged, [original], token)).Status);
        Assert.Equal(original, await fixture.Repository.GetAsync(original.Id, token));
        var result = await fixture.Owner(fixture.Repository).DetachForDeletionAsync(pending, [original], token);
        Assert.Equal(ConversationSpaceCommitStatus.Committed, result.Status);
        var detached = Assert.IsType<Conversation>(await fixture.Repository.GetAsync(original.Id, token));
        Assert.Equal(original with { SpaceId = null, UpdatedAt = detached.UpdatedAt }, detached);
        var completed = await fixture.Spaces.CompleteDeletionAsync(operationId, pending.ArchivedRevision, token);
        Assert.Equal(SpaceDeletionStage.Complete, completed.Stage);
        Assert.Equal(completed, await fixture.Spaces.CompleteDeletionAsync(operationId, pending.ArchivedRevision, token));
        Assert.Empty(await restarted.ReadPendingDeletionsAsync(token));
        Assert.True((await restarted.ReadExistingAsync(space.Id, token))!.IsArchived);
    }

    [Fact]
    public async Task Deletion_recovery_reads_archived_and_temporary_memberships_then_preserves_another_selection()
    {
        var token = TestContext.Current.CancellationToken;
        using var fixture = new Fixture(); await fixture.InitializeAsync(token);
        var space = await fixture.Spaces.CreateAsync("Delete", cancellationToken: token);
        var other = await fixture.Spaces.CreateAsync("Keep selected", cancellationToken: token);
        var now = DateTimeOffset.UtcNow;
        var archived = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Archived",
            null, null, false, false, now, now, IsArchived: true, SpaceId: space.Id);
        var temporary = archived with { Id = Guid.NewGuid(), Title = "Temporary", IsArchived = false, IsTemporary = true };
        await fixture.SeedInSpaceAsync(archived, space, token);
        await fixture.SeedInSpaceAsync(temporary, space, token);
        var pending = await fixture.Spaces.BeginDeletionAsync(space.Id, space.Revision, Guid.NewGuid(), token);
        await fixture.Spaces.SetCurrentSpaceIdAsync(other.Id, token);
        // Recreate the coordinator after the durable archive, as after closing the application.
        var recovery = new OwnedSpaceDeletion(fixture.Spaces, fixture.Owner(fixture.Repository));
        var completed = await recovery.ResumeAsync(pending.OperationId, token);
        Assert.Equal(SpaceDeletionStage.Complete, completed.Stage);
        foreach (var original in new[] { archived, temporary })
        {
            var saved = Assert.IsType<Conversation>(await fixture.Repository.GetAsync(original.Id, token));
            Assert.Equal(original with { SpaceId = null, UpdatedAt = saved.UpdatedAt }, saved);
        }
        Assert.Equal(other.Id, await fixture.Spaces.GetCurrentSpaceIdAsync(token));
        Assert.Equal(completed, await recovery.ResumeAsync(pending.OperationId, token));
    }

    [Fact]
    public async Task Schema_two_discovery_is_read_only_and_explicit_guarded_deletion_preserves_space_identity()
    {
        var token = TestContext.Current.CancellationToken;
        using var fixture = new Fixture(); await fixture.InitializeAsync(token);
        var space = await fixture.Spaces.CreateAsync("Existing schema two", cancellationToken: token);
        var all = await fixture.Spaces.GetAllAsync(includeArchived: true, cancellationToken: token);
        await fixture.Settings.SetAsync("spaces.registry", new { Version = 2, Spaces = all }, token);
        var before = (await fixture.Settings.ExportAsync(token)).Settings["spaces.registry"];
        var observer = new SpaceRegistry(fixture.Settings);
        var observed = Assert.IsType<SpaceDefinition>(await observer.ReadExistingAsync(space.Id, token));
        Assert.Equal(space.Id, observed.Id); Assert.Equal(space.Revision, observed.Revision);
        Assert.Equal(before, (await fixture.Settings.ExportAsync(token)).Settings["spaces.registry"]);
        var pending = await fixture.Spaces.BeginDeletionAsync(space.Id, space.Revision, Guid.NewGuid(), token);
        Assert.Equal(space.Id, pending.SpaceId); Assert.Equal(space.Revision + 1, pending.ArchivedRevision);
        var after = (await fixture.Settings.ExportAsync(token)).Settings["spaces.registry"];
        using var json = System.Text.Json.JsonDocument.Parse(after);
        Assert.Equal(3, json.RootElement.GetProperty("Version").GetInt32());
        Assert.Equal(all.Count, (await fixture.Spaces.GetAllAsync(includeArchived: true, cancellationToken: token)).Count);
    }

    [Theory]
    [InlineData(ConversationKind.Call)]
    [InlineData(ConversationKind.AutomationRun)]
    [InlineData(ConversationKind.Training)]
    public async Task Assignment_preserves_existing_system_managed_conversation_restrictions(ConversationKind kind)
    {
        var token = TestContext.Current.CancellationToken;
        using var fixture = new Fixture(); await fixture.InitializeAsync(token);
        var destination = await fixture.Spaces.CreateAsync("Destination", cancellationToken: token);
        var now = DateTimeOffset.UtcNow;
        var original = new Conversation(Guid.NewGuid(), HavenMode.Chat, kind, "System managed", null, null,
            false, false, now, now);
        await fixture.Repository.UpsertConversationAsync(original, token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Owner(fixture.Repository).AssignAsync(original, null, Snapshot(destination), token));
        Assert.Equal(original, await fixture.Repository.GetAsync(original.Id, token));
    }

    [Fact]
    public async Task Shared_membership_writer_returns_exact_acknowledged_rows_through_actual_Space_service()
    {
        var token = TestContext.Current.CancellationToken;
        using var fixture = new Fixture(); await fixture.InitializeAsync(token);
        var source = await fixture.Spaces.CreateAsync("Source", cancellationToken: token);
        var destination = await fixture.Spaces.CreateAsync("Destination", cancellationToken: token);
        var now = DateTimeOffset.UtcNow;
        var proposed = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Canonical",
            null, null, false, false, now, now, SpaceId: source.Id);
        ISpaceConversationWriter writer = fixture.Owner(fixture.Repository);
        Assert.Same(proposed, await writer.CreateAsync(proposed, source, token));
        var service = new SpaceConversationService(fixture.Spaces, fixture.Repository, writer);
        var assigned = await service.AssignAsync(proposed.Id, destination.Id, token);
        Assert.Equal(assigned, await fixture.Repository.GetAsync(proposed.Id, token));
        Assert.Equal(proposed with { SpaceId = destination.Id, UpdatedAt = assigned.UpdatedAt }, assigned);
        var detached = await service.RemoveAsync(proposed.Id, token);
        Assert.Null(detached.SpaceId);
        Assert.Equal(detached, await fixture.Repository.GetAsync(proposed.Id, token));
        await Assert.ThrowsAsync<SpaceConversationWriteException>(() => writer.AssignAsync(assigned, destination, source, token));
        Assert.Equal(detached, await fixture.Repository.GetAsync(proposed.Id, token));
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
        public async Task SeedInSpaceAsync(Conversation value, SpaceDefinition space, CancellationToken token)
        {
            var initial = value with { IsArchived = false, IsTemporary = false };
            var acknowledged = await ((ISpaceConversationWriter)Owner(Repository)).CreateAsync(initial, space, token);
            Assert.Equal(initial, acknowledged);
            // Subsequent ordinary content updates retain the existing canonical membership.
            if (initial != value) await Repository.UpsertConversationAsync(value, token);
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
