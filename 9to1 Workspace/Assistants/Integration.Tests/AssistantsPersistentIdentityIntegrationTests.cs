using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Apps.Assistants.Tests;

/// <summary>Actual FileHome/OS-profile/Den/SQLite owners behind the product controller.
/// Metadata/draft persistence controls only: no model, task execution, UI or installed-host proof.</summary>
public sealed partial class AssistantsPersistentIdentityIntegrationTests
{
    [Fact]
    public Task Edited_identity_and_two_distinct_conversation_drafts_survive_real_owner_reopen() =>
        RunOriginalAsync(async rig =>
        {
            var controller = rig.CreateController();
            await rig.Retain(controller.InitializeAsync(Token));
            var created = await rig.Retain(controller.CreateAsync(new() { Name = "Research helper", Instructions = "Original instructions" }, Guid.NewGuid(), Token));
            var identity = created.SelectedAssistant!.Identity;
            var edited = await rig.Retain(controller.ConfigureAsync(identity, created.SelectedAssistant.Revision,
                created.SelectedAssistant.Configuration with { Name = "Persistent helper", Instructions = "Updated saved instructions" }, Guid.NewGuid(), Token));
            var savedRevision = edited.SelectedAssistant!.Revision;
            Assert.Equal(identity, edited.SelectedAssistant.Identity);
            Assert.True(savedRevision > created.SelectedAssistant.Revision);

            var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
            await rig.Retain(controller.NewConversationAsync(firstId, "First conversation", Guid.NewGuid(), Token));
            await rig.Retain(controller.SaveDraftAsync(null, "First saved draft", [], Token));
            await rig.Retain(controller.NewConversationAsync(secondId, "Second conversation", Guid.NewGuid(), Token));
            await rig.Retain(controller.SaveDraftAsync(null, "Second saved draft", [], Token));
            Assert.Equal(identity, controller.Snapshot.SelectedAssistant!.Identity);
            Assert.Equal(2, controller.Snapshot.Conversations.Count);
            Assert.Null(await rig.Retain(rig.Tasks.GetByContextAsync(firstId, Token)));
            Assert.Null(await rig.Retain(rig.Tasks.GetByContextAsync(secondId, Token)));
            var oldActor = rig.Actor;
            var oldDenId = rig.Provider.Store.Manifest.DenId;

            await rig.ReopenActualOwnersAsync();
            Assert.Equal(oldActor.ProfileId, rig.Actor.ProfileId);
            Assert.NotEqual(oldActor.AuthenticationRevision, rig.Actor.AuthenticationRevision);
            Assert.Equal(oldDenId, rig.Provider.Store.Manifest.DenId);
            var reopened = rig.CreateController();
            var catalogue = await rig.Retain(reopened.InitializeAsync(Token));
            Assert.Equal(identity, Assert.Single(catalogue.Assistants).Identity);
            var selected = await rig.Retain(reopened.OpenAssistantAsync(identity, Token));
            Assert.Equal(savedRevision, selected.SelectedAssistant!.Revision);
            Assert.Equal("Persistent helper", selected.SelectedAssistant.Configuration.Name);
            Assert.Equal("Updated saved instructions", selected.SelectedAssistant.Configuration.Instructions);
            Assert.Equal(new[] { firstId, secondId }.Order(), selected.Conversations.Select(row => row.ConversationId).Order());
            var first = await rig.Retain(reopened.OpenConversationAsync(firstId, Token));
            Assert.Equal("First saved draft", first.Conversation!.Draft!.Content);
            Assert.Null(first.Conversation.Conversation.SpaceId);
            Assert.False(first.Conversation.Conversation.IsTemporary);
            var second = await rig.Retain(reopened.OpenConversationAsync(secondId, Token));
            Assert.Equal("Second saved draft", second.Conversation!.Draft!.Content);
            Assert.Equal(identity, second.ConversationBinding!.Definition.Identity);
            Assert.Null(second.Work!.CanonicalTask);
            Assert.Equal(0, rig.ProviderInvocations);
        });

    [Fact]
    public Task Stale_configuration_cas_preserves_acknowledged_real_definition_and_can_refresh() =>
        RunOriginalAsync(async rig =>
        {
            var first = rig.CreateController();
            var operation = Guid.NewGuid();
            var config = new AssistantConfiguration { Name = "CAS helper", Instructions = "Original" };
            var saved = await rig.Retain(first.CreateAsync(config, operation, Token));
            var identity = saved.SelectedAssistant!.Identity;
            var replay = await rig.Retain(first.CreateAsync(config, operation, Token));
            Assert.Equal(identity, replay.SelectedAssistant!.Identity);
            Assert.Equal(saved.SelectedAssistant.Revision, replay.SelectedAssistant.Revision);
            var second = rig.CreateController();
            var stale = await rig.Retain(second.OpenAssistantAsync(identity, Token));
            var winner = await rig.Retain(first.ConfigureAsync(identity, saved.SelectedAssistant.Revision,
                config with { Instructions = "Acknowledged winner" }, Guid.NewGuid(), Token));
            var actualConflict = rig.Retain(second.ConfigureAsync(identity, stale.SelectedAssistant!.Revision,
                config with { Instructions = "Unacknowledged loser" }, Guid.NewGuid(), Token));
            var conflict = await Assert.ThrowsAsync<DenException>(() => actualConflict);
            Assert.Equal(DenErrorCode.Conflict, conflict.Code);
            Assert.True(second.IsAcknowledgedOriginalCommandRefusal(actualConflict));
            rig.Expect(actualConflict, conflict);
            Assert.Equal("Original", second.Snapshot.SelectedAssistant!.Configuration.Instructions);
            var refreshed = await rig.Retain(second.OpenAssistantAsync(identity, Token));
            Assert.Equal(winner.SelectedAssistant!.Revision, refreshed.SelectedAssistant!.Revision);
            Assert.Equal("Acknowledged winner", refreshed.SelectedAssistant.Configuration.Instructions);
            var home = await rig.Retain(rig.Factory.OpenAsync(Token));
            Assert.Single(await rig.Retain(home.Den.ListAsync<AgentDefinitionRecord>("personal", Token)));
        });

    [Fact]
    public Task Conversation_membership_stays_with_original_configured_identity_across_other_conversations() =>
        RunOriginalAsync(async rig =>
        {
            var controller = rig.CreateController();
            var first = await rig.Retain(controller.CreateAsync(new() { Name = "First helper" }, Guid.NewGuid(), Token));
            var firstIdentity = first.SelectedAssistant!.Identity;
            var firstConversation = Guid.NewGuid();
            await rig.Retain(controller.NewConversationAsync(firstConversation, "First membership", Guid.NewGuid(), Token));
            var second = await rig.Retain(controller.CreateAsync(new() { Name = "Second helper" }, Guid.NewGuid(), Token));
            var secondIdentity = second.SelectedAssistant!.Identity;
            Assert.NotEqual(firstIdentity, secondIdentity);
            var secondConversation = Guid.NewGuid();
            await rig.Retain(controller.NewConversationAsync(secondConversation, "Second membership", Guid.NewGuid(), Token));
            var foreignOpen = rig.Retain(controller.OpenConversationAsync(firstConversation, Token));
            var refusal = await Assert.ThrowsAsync<AssistantCommandRefusedException>(() => foreignOpen);
            Assert.True(controller.IsAcknowledgedOriginalCommandRefusal(foreignOpen));
            rig.Expect(foreignOpen, refusal);
            await rig.Retain(controller.OpenAssistantAsync(firstIdentity, Token));
            Assert.Equal(firstConversation, Assert.Single(controller.Snapshot.Conversations).ConversationId);
            await rig.Retain(controller.OpenConversationAsync(firstConversation, Token));
            Assert.Equal(firstIdentity, controller.Snapshot.ConversationBinding!.Definition.Identity);
            await rig.Retain(controller.OpenAssistantAsync(secondIdentity, Token));
            Assert.Equal(secondConversation, Assert.Single(controller.Snapshot.Conversations).ConversationId);
            Assert.NotNull(await rig.Retain(rig.Conversations.GetAsync(firstConversation, Token)));
            Assert.NotNull(await rig.Retain(rig.Conversations.GetAsync(secondConversation, Token)));
        });

    [Fact]
    public Task Existing_canonical_conversation_cannot_be_overwritten_or_adopted_as_new_membership() =>
        RunOriginalAsync(async rig =>
        {
            var controller = rig.CreateController();
            await rig.Retain(controller.CreateAsync(new() { Name = "Independent helper" }, Guid.NewGuid(), Token));
            var now = DateTimeOffset.UtcNow;
            var existing = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat,
                "Existing unrelated canonical conversation", null, null, false, false, now, now);
            Assert.True(await rig.Retain(rig.Conversations.TryCreateConversationAsync(existing, Token)));
            var refusedCreation = rig.Retain(controller.NewConversationAsync(existing.Id, "Replacement", Guid.NewGuid(), Token));
            var refusal = await Assert.ThrowsAsync<AssistantCommandRefusedException>(() => refusedCreation);
            Assert.True(controller.IsAcknowledgedOriginalCommandRefusal(refusedCreation));
            rig.Expect(refusedCreation, refusal);
            Assert.Equal(existing, await rig.Retain(rig.Conversations.GetAsync(existing.Id, Token)));
            var home = await rig.Retain(rig.Factory.OpenAsync(Token));
            Assert.Empty(await rig.Retain(home.Den.ListAsync<SessionRecord>("personal", Token)));
        });

    [Fact]
    public Task Named_identity_survives_reopen_before_any_conversation_or_task_exists() =>
        RunOriginalAsync(async rig =>
        {
            var first = rig.CreateController();
            var created = await rig.Retain(first.CreateAsync(new() { Name = "Independent identity", Purpose = "Persistent configuration" }, Guid.NewGuid(), Token));
            var identity = created.SelectedAssistant!.Identity;
            Assert.Empty(created.Conversations);
            await rig.ReopenActualOwnersAsync();
            var reopened = rig.CreateController();
            var catalogue = await rig.Retain(reopened.InitializeAsync(Token));
            Assert.Equal(identity, Assert.Single(catalogue.Assistants).Identity);
            var selected = await rig.Retain(reopened.OpenAssistantAsync(identity, Token));
            Assert.Empty(selected.Conversations);
            Assert.Null(selected.ConversationBinding);
            Assert.Equal("Persistent configuration", selected.SelectedAssistant!.Configuration.Purpose);
        });

    [Fact]
    public Task Disabled_archived_definition_keeps_its_identity_and_existing_history_after_reopen() =>
        RunOriginalAsync(async rig =>
        {
            var controller = rig.CreateController();
            var created = await rig.Retain(controller.CreateAsync(new() { Name = "Retained helper" }, Guid.NewGuid(), Token));
            var identity = created.SelectedAssistant!.Identity;
            var conversationId = Guid.NewGuid();
            await rig.Retain(controller.NewConversationAsync(conversationId, "Retained conversation", Guid.NewGuid(), Token));
            await rig.Retain(controller.SaveDraftAsync(null, "Retained unsent draft", [], Token));
            var disabled = await rig.Retain(controller.DisableAsync(controller.Snapshot.SelectedAssistant!.Revision, Guid.NewGuid(), Token));
            Assert.False(disabled.SelectedAssistant!.Configuration.Enabled);
            var archived = await rig.Retain(controller.ArchiveAsync(disabled.SelectedAssistant.Revision, Guid.NewGuid(), Token));
            Assert.Equal(identity, archived.SelectedAssistant!.Identity);
            await rig.ReopenActualOwnersAsync();
            var reopened = rig.CreateController();
            var selected = await rig.Retain(reopened.OpenAssistantAsync(identity, Token));
            Assert.False(selected.SelectedAssistant!.Configuration.Enabled);
            Assert.True(selected.SelectedAssistant.Configuration.Archived);
            Assert.Equal(conversationId, Assert.Single(selected.Conversations).ConversationId);
            await rig.Retain(reopened.OpenConversationAsync(conversationId, Token));
            Assert.Equal("Retained unsent draft", reopened.Snapshot.Conversation!.Draft!.Content);
            Assert.Equal(0, rig.ProviderInvocations);
        });

    [Fact]
    public Task Previous_presentation_binding_cannot_be_used_after_real_owner_reopen_but_same_ids_can_reopen() =>
        RunOriginalAsync(async rig =>
        {
            var first = rig.CreateController();
            var created = await rig.Retain(first.CreateAsync(new() { Name = "Reopen helper" }, Guid.NewGuid(), Token));
            var identity = created.SelectedAssistant!.Identity;
            var conversationId = Guid.NewGuid();
            var opened = await rig.Retain(first.NewConversationAsync(conversationId, "Stable conversation", Guid.NewGuid(), Token));
            var oldBinding = opened.ConversationBinding!;
            Assert.True(first.OriginalCanonicalBridge.IsIssuedOriginalBinding(oldBinding));
            await rig.ReopenActualOwnersAsync();
            var next = rig.CreateController();
            Assert.False(next.OriginalCanonicalBridge.IsIssuedOriginalBinding(oldBinding));
            var foreignRead = rig.Retain(next.OriginalCanonicalBridge.ReadConversationAsync(oldBinding, Token));
            var refusal = await Assert.ThrowsAsync<AssistantCommandRefusedException>(() => foreignRead);
            rig.Expect(foreignRead, refusal);
            await rig.Retain(next.OpenAssistantAsync(identity, Token));
            var fresh = await rig.Retain(next.OpenConversationAsync(conversationId, Token));
            Assert.True(next.OriginalCanonicalBridge.IsIssuedOriginalBinding(fresh.ConversationBinding!));
            Assert.NotSame(oldBinding, fresh.ConversationBinding);
            Assert.Equal(identity, fresh.ConversationBinding!.Definition.Identity);
        });

    [Fact]
    public Task Configuration_revision_change_refuses_stale_conversation_draft_until_fresh_reopen() =>
        RunOriginalAsync(async rig =>
        {
            var first = rig.CreateController();
            var created = await rig.Retain(first.CreateAsync(new() { Name = "Revision helper" }, Guid.NewGuid(), Token));
            var identity = created.SelectedAssistant!.Identity;
            var conversationId = Guid.NewGuid();
            await rig.Retain(first.NewConversationAsync(conversationId, "Revision conversation", Guid.NewGuid(), Token));
            await rig.Retain(first.SaveDraftAsync(null, "Acknowledged draft", [], Token));
            var second = rig.CreateController();
            await rig.Retain(second.OpenAssistantAsync(identity, Token));
            await rig.Retain(second.OpenConversationAsync(conversationId, Token));
            await rig.Retain(first.ConfigureAsync(identity, first.Snapshot.SelectedAssistant!.Revision,
                first.Snapshot.SelectedAssistant.Configuration with { Instructions = "New revision" }, Guid.NewGuid(), Token));
            var staleSave = rig.Retain(second.SaveDraftAsync(null, "Stale draft write", [], Token));
            var refusal = await Assert.ThrowsAsync<AssistantCommandRefusedException>(() => staleSave);
            Assert.True(second.IsAcknowledgedOriginalCommandRefusal(staleSave));
            rig.Expect(staleSave, refusal);
            Assert.Equal("Acknowledged draft", (await rig.Retain(rig.Production.GetDraftAsync(conversationId, null, Token)))!.Content);
            await rig.Retain(second.OpenAssistantAsync(identity, Token));
            await rig.Retain(second.OpenConversationAsync(conversationId, Token));
            await rig.Retain(second.SaveDraftAsync(null, "Fresh acknowledged draft", [], Token));
            Assert.Equal("Fresh acknowledged draft", (await rig.Retain(rig.Production.GetDraftAsync(conversationId, null, Token)))!.Content);
        });

    [Fact]
    public Task Concurrent_real_conversation_creation_keeps_one_original_membership_and_never_adopts_it() =>
        RunOriginalAsync(async rig =>
        {
            var first = rig.CreateController(); var second = rig.CreateController();
            var firstDefinition = await rig.Retain(first.CreateAsync(new() { Name = "First contender" }, Guid.NewGuid(), Token));
            var secondDefinition = await rig.Retain(second.CreateAsync(new() { Name = "Second contender" }, Guid.NewGuid(), Token));
            var conversationId = Guid.NewGuid();
            var firstOriginal = rig.Retain(first.NewConversationAsync(conversationId, "First original", Guid.NewGuid(), Token));
            var secondOriginal = rig.Retain(second.NewConversationAsync(conversationId, "Second original", Guid.NewGuid(), Token));
            var successes = new List<(AssistantsWorkspaceController Controller, AssistantsWorkspaceSnapshot Saved)>();
            foreach (var pair in new[] { (Controller: first, Actual: firstOriginal), (Controller: second, Actual: secondOriginal) })
            {
                try { successes.Add((pair.Controller, await pair.Actual)); }
                catch (Exception refusal)
                {
                    Assert.True(pair.Controller.IsAcknowledgedOriginalCommandRefusal(pair.Actual));
                    rig.Expect(pair.Actual, refusal);
                }
            }
            var winner = Assert.Single(successes);
            Assert.Equal(conversationId, winner.Saved.ConversationBinding!.Conversation.Id);
            var firstRows = await rig.Retain(first.OriginalCanonicalBridge.ReadConversationsAsync(firstDefinition.SelectedAssistant!.Identity, token: Token));
            var secondRows = await rig.Retain(second.OriginalCanonicalBridge.ReadConversationsAsync(secondDefinition.SelectedAssistant!.Identity, token: Token));
            Assert.Equal(1, firstRows.Count + secondRows.Count);
            var home = await rig.Retain(rig.Factory.OpenAsync(Token));
            var sessions = await rig.Retain(home.Den.ListAsync<SessionRecord>("personal", Token));
            Assert.Equal(conversationId.ToString("D"), Assert.Single(sessions).ConversationId);
            var actualConversation = await rig.Retain(rig.Conversations.GetAsync(conversationId, Token));
            Assert.Equal(winner.Saved.ConversationBinding.Conversation, actualConversation);
        });

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    // Actual dependency cleanup is independent of test-runner withdrawal.
    private static CancellationToken IndependentOwnerCleanupToken => CancellationToken.None;

    private static async Task RunOriginalAsync(Func<Rig, Task> body, bool withCapabilities = false)
    {
        var rig = new Rig(withCapabilities);
        var errors = new List<Exception>();
        try { await rig.InitializeActualOwnersAsync(create: true); await body(rig); }
        catch (Exception error) { Add(errors, error); }
        finally
        {
            try { await rig.JoinActualOwnersAsync(); } catch (Exception error) { Add(errors, error); }
            foreach (var actual in rig.Originals)
            {
                try { await actual; }
                catch (Exception observed)
                {
                    var direct = actual.Exception?.InnerExceptions.ToArray() ?? [observed];
                    foreach (var cause in direct) if (!rig.IsExpected(actual, cause)) Add(errors, cause);
                }
            }
            // No delete while an original source/dependency close is unknown or failed.
            if (errors.Count == 0)
                try { Directory.Delete(rig.Root, true); } catch (Exception error) { Add(errors, error); }
        }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Real Assistants persistence control and original cleanup failed; fixture retained at " + rig.Root, errors);
    }

    private static void Add(List<Exception> errors, Exception cause)
    { if (!errors.Any(prior => ReferenceEquals(prior, cause))) errors.Add(cause); }

    private sealed class Rig(bool withCapabilities = false)
    {
        private readonly bool _withCapabilities = withCapabilities;
        private CanonicalSqliteOriginalStoreOwner? _capabilityStore;
        private HomeLocalStoreOwnership? _capabilityOwnership;
        private HomePermissionTrustService? _capabilityPermissions;
        internal CapabilityRepository CapabilityRepository = null!;
        internal AssistantOriginalCapabilityOwner? CapabilityOwner;
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "assistants-real-den-" + Guid.NewGuid().ToString("N"));
        internal readonly List<Task> Originals = [];
        private readonly List<(Task Original, Exception Cause)> _expected = [];
        private readonly List<AssistantsWorkspaceController> _controllers = [];
        private HomeDenStoreEvidenceProvider? _provider;
        private AssistantOriginalConversationHost? _ordinary;
        private SqliteDatabase? _database;
        private Paths? _paths;
        private ChatSessionService? _chat;
        private readonly NoModelCalls _models = new();
        internal int ProviderInvocations => _models.Invocations;
        internal HomeDenStoreEvidenceProvider Provider => _provider!;
        internal HomePersonalDenFactory Factory = null!;
        internal AuthenticatedResourceActor Actor = null!;
        internal ConversationRepository Conversations = null!;
        internal ConversationProductionRepository Production = null!;
        internal TaskExecutionCoordinator Tasks = null!;

        internal Task<T> Retain<T>(Task<T> actual) { Originals.Add(actual); return actual; }
        internal Task Retain(Task actual) { Originals.Add(actual); return actual; }
        internal void Expect(Task actual, Exception cause) => _expected.Add((actual, cause));
        internal bool IsExpected(Task actual, Exception cause) => _expected.Any(value => ReferenceEquals(value.Original, actual) && ReferenceEquals(value.Cause, cause));

        internal async Task InitializeActualOwnersAsync(bool create)
        {
            Directory.CreateDirectory(Root);
            var state = new FileHomeCoreStateStore(Path.Combine(Root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(state, new OperatingSystemPrincipalSource());
            Actor = await Retain(profiles.GetCurrentAsync(Token).AsTask())
                ?? throw new InvalidOperationException("The actual supported OS principal is unavailable.");
            var denRoot = Path.Combine(Root, "den");
            _provider = await Retain(create ? HomeDenStoreEvidenceProvider.CreateAsync(denRoot, profiles, Token)
                : HomeDenStoreEvidenceProvider.OpenAsync(denRoot, profiles, Token));
            var permissions = new HomePermissionTrustService(state, (target, action) =>
                target == "9to1.home.local-profile" && action == "home.profile.importStore"
                    ? new(HomePermissionRisk.High, false, false, true) : null);
            var evidence = new List<IHomeLocalStoreEvidenceProvider> { _provider };
            if (_withCapabilities)
            {
                // This fixture owns the write initialization separately from every
                // protected capability READ, with genuine private kernel file modes.
                if (OperatingSystem.IsLinux()) File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                _paths = new(Root); _database = new(_paths); await Retain(_database.InitializeAsync(Token));
                if (OperatingSystem.IsLinux())
                    foreach (var path in new[] { _paths.DatabasePath, _paths.DatabasePath + "-wal", _paths.DatabasePath + "-shm" })
                        if (File.Exists(path)) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                _capabilityStore = new(_database, _paths, profiles);
                evidence.Add(new CanonicalSqliteOriginalStoreEvidenceProvider(_capabilityStore, "canonical.sqlite"));
                _capabilityPermissions = permissions;
            }
            var ownership = new HomeLocalStoreOwnership(state, profiles, new HomeLocalStoreEvidenceRegistry(evidence), permissions);
            if (_withCapabilities) _capabilityOwnership = ownership;
            if (create) await Retain(ownership.BindNewEmptyAsync(Actor, "den", _provider.Store.Manifest.DenId, Token));
            Factory = new(_provider, new HomeResourceStoreOwnershipAuthority(ownership, profiles), profiles);
            var opened = await Retain(Factory.OpenAsync(Token));
            Assert.Equal(Actor, opened.Actor);
            Assert.Same(_provider.Store, opened.Den.Store);
            if (_database is null)
            {
                _paths = new(Root); _database = new(_paths);
                await Retain(_database.InitializeAsync(Token));
            }
            Conversations = new(_database);
            Production = new(_database, Conversations);
            Tasks = new(new TaskExecutionRepository(_database), new NoExecutionEvents());
            _chat = new(Conversations, _models, new CapabilityPreflightService(), new Safety(),
                new WorkspaceToolRuntime(new Workspace()), new ComputerToolRuntime(new Computer()));
            _ordinary = new();
            if (_withCapabilities)
            {
                CapabilityRepository = new(_database); var registry = new CapabilityRegistryService(CapabilityRepository);
                var source = new CanonicalCapabilityCatalogueReadOwner(_capabilityStore!, _database, _paths!,
                    CapabilityRepository, registry, new HomeResourceStoreOwnershipAuthority(ownership, profiles));
                CapabilityOwner = new(Factory, Conversations, registry, _chat, source);
            }
        }

        internal async Task ImportActualCapabilityStoreAsync()
        {
            var store = _capabilityStore ?? throw new InvalidOperationException("The actual fixture capability store was not requested.");
            var identity = await Retain(store.GetStoreIdentityWithinOriginalSourceAsync(Actor, body => body(), actual => Retain(actual), Token));
            var request = await Retain(_capabilityOwnership!.RequestImportAsync(Actor, "canonical.sqlite", identity.StoreId.ToString("D"), Actor.AuthenticationRevision, Token));
            Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
            Assert.True((await Retain(_capabilityPermissions!.DecideAsync(request.RequestId, HomeApprovalChoice.Accept, cancellationToken: Token))).Succeeded);
            await Retain(_capabilityOwnership.CompleteImportAsync(request.RequestId, Token));
        }
        internal Task<IReadOnlyList<CapabilityDefinition>> SeedActualCapabilityFixtureAsync() => Retain(CapabilityRepository.GetCapabilitiesAsync(Token));

        internal AssistantsWorkspaceController CreateController()
        {
            var actual = AssistantsWorkspaceFactory.Create(Factory, Conversations, Production, _chat!, Tasks, _ordinary!, actualCapabilities: CapabilityOwner);
            _controllers.Add(actual);
            return actual;
        }

        internal async Task ReopenActualOwnersAsync()
        {
            await JoinActualOwnersAsync();
            _controllers.Clear(); _provider = null; _ordinary = null; _database = null;
            CapabilityOwner = null; _capabilityStore = null; _capabilityOwnership = null; _capabilityPermissions = null;
            await InitializeActualOwnersAsync(create: false);
        }

        internal async Task JoinActualOwnersAsync()
        {
            var errors = new List<Exception>(); var closes = new List<Task>();
            foreach (var controller in _controllers)
                try { closes.Add(Retain(controller.CloseAndDrainAsync())); } catch (Exception error) { Add(errors, error); }
            if (_ordinary is { } business)
                try { closes.Add(Retain(business.CloseAndDrainAsync())); } catch (Exception error) { Add(errors, error); }
            foreach (var actual in closes)
                try { await actual; } catch (Exception observed)
                { foreach (var direct in actual.Exception?.InnerExceptions.ToArray() ?? [observed]) Add(errors, direct); }
            if (errors.Count != 0) throw new AggregateException("Actual borrowers did not join; Den dependency is retained.", errors);
            if (CapabilityOwner is { } capabilities) await Retain(capabilities.CloseAndDrainAsync());
            if (_capabilityStore is { } capabilityStore) await Retain(capabilityStore.CloseAndDrainAsync());
            if (_provider is { } provider) await Retain(provider.DisposeAsync().AsTask());
            if (_database is { } database)
            {
                // Clear only this fixture's actual connection pool, after all its borrowers joined.
                // The fresh handle identifies the owning pool; no global ClearAllPools call.
                var connection = await Retain(database.OpenAsync(IndependentOwnerCleanupToken));
                await Retain(connection.DisposeAsync().AsTask());
                SqliteConnection.ClearPool(connection);
            }
        }
    }

    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "conversations.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }

    private sealed class NoModelCalls : IOllamaClient
    {
        internal int Invocations;
        private Exception Refuse() { Interlocked.Increment(ref Invocations); return new InvalidOperationException("Persistent identity controls authorize no model invocation."); }
        public Task<bool> IsAvailableAsync(CancellationToken token) => throw Refuse();
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) => throw Refuse();
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => throw Refuse();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw Refuse();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw Refuse();
    }
    private sealed class NoExecutionEvents : IExecutionEventSink
    { public bool TryPublish(ExecutionEvent value) => throw new InvalidOperationException("Persistent metadata controls authorize no new Task/Run."); }
    private sealed class Safety : IConversationSafetyService
    {
        public Task<ConversationSafetySnapshot> GetSnapshotAsync(Guid id, CancellationToken token) => Task.FromResult(new ConversationSafetySnapshot(id, 0, ConversationSafetyState.Active, null, 0));
        public Task<ConversationSafetyFlagResult> RecordConfirmedFlagAsync(Guid id, ConfirmedSafetyFlag flag, CancellationToken token) => throw new InvalidOperationException("Unexpected safety flag.");
        public Task EnsureMayActAsync(Guid id, string operation, CancellationToken token) => Task.CompletedTask;
    }
    private sealed class Workspace : IWorkspaceToolService
    {
        public string ResolveWorkspacePath(string root, string path) => throw new InvalidOperationException("No workspace grant.");
        public Task<string> ReadTextAsync(string root, string path, CancellationToken token) => throw new InvalidOperationException("No workspace grant.");
        public Task WriteTextAtomicAsync(string root, string path, string content, CancellationToken token) => throw new InvalidOperationException("No workspace grant.");
        public Task<IReadOnlyList<string>> SearchFilesAsync(string root, string pattern, CancellationToken token) => throw new InvalidOperationException("No workspace grant.");
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken token) => throw new InvalidOperationException("No execution grant.");
    }
    private sealed class Computer : IComputerToolService
    {
        public bool IsSupported => false;
        public Task<string> SnapshotAsync(CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> ListWindowsAsync(CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> LaunchAppAsync(string name, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> FocusWindowAsync(string title, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> InvokeAsync(string title, string name, string automationId, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> ClickAsync(string title, int x, int y, string button, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> TypeAsync(string title, string text, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> PressAsync(string title, string keys, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
        public Task<string> CloseWindowAsync(string title, CancellationToken token) => throw new InvalidOperationException("No computer grant.");
    }
}
