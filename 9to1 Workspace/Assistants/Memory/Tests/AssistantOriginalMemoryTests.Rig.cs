using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static void Scope(Action body) => body();

    private static async Task RunAsync(Func<Rig, Task> test, bool importMemory = true, bool bindMemoryImport = false)
    {
        var rig = new Rig(bindMemoryImport: bindMemoryImport); var failures = new List<Exception>();
        try { await rig.InitializeAsync(true, importMemory); await test(rig); } catch (Exception failure) { failures.Add(failure); }
        try { await rig.CloseAsync(); } catch (Exception failure) { failures.Add(failure); }
        // Explicit current instruction: retain all newly task-owned fixtures, never delete them.
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Actual memory fixture retained at " + rig.Root, failures);
    }

    private sealed class Rig(Func<CanonicalSqliteOriginalStoreOwner, HomeLocalProfileIdentity, IHomeOriginalScopedLocalStoreEvidenceProvider>? originalLegacyFactory = null, bool bindMemoryImport = false,
        IHomeActionPolicySource? originalAdditionalPolicy = null,
        Func<HomeLocalProfileIdentity, IAssistantOriginalModelSelectionOwner>? configuredModelFactory = null,
        Func<Rig, TaskExecutionCoordinator>? configuredTaskFactory = null, Func<Task>? closeConfiguredTasks = null,
        Func<Rig, ConversationProductionRepository, IAppPaths, IAssistantOriginalAttachmentOwner>? configuredAttachmentFactory = null,
        Func<Task>? closeConfiguredAttachments = null,
        Func<Rig, IConversationRepository, TaskExecutionCoordinator, ChatSessionService>? configuredChatFactory = null)
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "assistants-memory-" + Guid.NewGuid().ToString("N"));
        private readonly List<Task> _retained = [];
        private HomeDenStoreEvidenceProvider _den = null!;
        private AssistantOriginalConversationHost _ordinary = null!;
        internal IConversationRepository OriginalConversations = null!;
        internal SqliteDatabase Database = null!;
        internal KnowledgeLibraryService Knowledge = null!;
        internal CanonicalSqliteOriginalStoreOwner Store = null!;
        internal AssistantOriginalMemorySource Memory = null!;
        internal DenAssistantCanonicalBridge Bridge = null!;
        internal HomePersonalDenFactory Home = null!;
        internal HomePermissionTrustService Permissions = null!;
        internal FileHomeCoreStateStore OriginalStateStore = null!;
        internal HomeLocalProfileIdentity Profiles = null!;
        internal HomeLocalStoreOwnership Ownership = null!;
        internal HomeResourceStoreOwnershipAuthority Authority = null!;
        internal IHomeOriginalScopedLocalStoreEvidenceProvider? OriginalLegacyEvidence;
        private HomeCanonicalAssistantMemoryWriteSource? _memoryWrites;
        internal HomeOriginalLocalStoreImportSession? OriginalMemoryImports;
        internal HomeCanonicalAssistantMemoryWriteSource OriginalMemoryWrites => _memoryWrites ?? throw new InvalidOperationException("The actual Home memory writer is absent.");
        internal Task? ExpectedMemoryClose = null, ExpectedStoreClose = null, ExpectedWriteClose = null;
        internal void Retain(Task task) { if (!_retained.Any(prior => ReferenceEquals(prior, task))) _retained.Add(task); }
        internal async Task InitializeAsync(bool create, bool importMemory = true)
        {
            Directory.CreateDirectory(Root);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var paths = new Paths(Root); Database = new(paths); await Database.InitializeAsync(Token);
            if (!OperatingSystem.IsWindows()) foreach (var path in new[] { paths.DatabasePath, paths.DatabasePath + "-wal", paths.DatabasePath + "-shm" })
                if (File.Exists(path)) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Knowledge = new(Database, new RetrievalIndexService(Database, new LocalHashEmbeddingService()));
            var state = new FileHomeCoreStateStore(Path.Combine(Root, "home.json")); OriginalStateStore = state;
            var profiles = new HomeLocalProfileIdentity(state, new OperatingSystemPrincipalSource()); Profiles = profiles;
            var actor = await profiles.GetCurrentAsync(Token) ?? throw new InvalidOperationException("An actual local OS Home profile is required.");
            Store = new(Database, paths, profiles);
            OriginalLegacyEvidence = originalLegacyFactory?.Invoke(Store, profiles);
            var evidence = new CanonicalSqliteOriginalStoreEvidenceProvider(Store, AssistantOriginalMemorySource.ResourceKind);
            var catalogueEvidence = new CanonicalSqliteOriginalStoreEvidenceProvider(Store, "canonical.sqlite");
            _den = create ? await HomeDenStoreEvidenceProvider.CreateAsync(Path.Combine(Root, "den"), profiles, Token)
                : await HomeDenStoreEvidenceProvider.OpenAsync(Path.Combine(Root, "den"), profiles, Token);
            var permissions = new HomePermissionTrustService(state, (app, action) =>
                app == "9to1.home.local-profile" && action == "home.profile.importStore" ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, true) :
                new HomeCanonicalAssistantMemoryWriteActionPolicy().TryGet(app, action) ?? originalAdditionalPolicy?.TryGet(app, action));
            Permissions = permissions;
            var originalEvidence = new List<IHomeLocalStoreEvidenceProvider> { _den, evidence, catalogueEvidence };
            if (OriginalLegacyEvidence is not null) originalEvidence.Add(OriginalLegacyEvidence);
            var ownership = new HomeLocalStoreOwnership(state, profiles, new HomeLocalStoreEvidenceRegistry(originalEvidence), permissions); Ownership = ownership;
            if (create)
            {
                await ownership.BindNewEmptyAsync(actor, "den", _den.Store.Manifest.DenId, Token);
                var chosen = importMemory ? evidence : catalogueEvidence;
                var setup = new HomeLocalStoreSetupSession(chosen.ResourceKind, Store, profiles, ownership, chosen);
                var inspected = await setup.InspectAsync(Token);
                Assert.Equal((await Store.GetStoreIdentityAsync(Token)).StoreId, inspected.StoreId);
                Assert.False(inspected.IsOwned); Assert.Null(inspected.PendingRequestId);
                var request = await setup.RequestImportAsync(Token);
                Assert.True((await permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept, cancellationToken: Token)).Succeeded);
                await setup.CompleteImportAsync(Token);
            }
            var authority = new HomeResourceStoreOwnershipAuthority(ownership, profiles); Authority = authority;
            var home = new HomePersonalDenFactory(_den, authority, profiles); Home = home;
            var conversations = new ConversationRepository(Database); OriginalConversations = conversations;
            var tasks = configuredTaskFactory?.Invoke(this) ?? new TaskExecutionCoordinator(new TaskExecutionRepository(Database), new NoExecutionEvents());
            Memory = new(home, conversations, tasks, Knowledge, Store, profiles, authority,
                OriginalLegacyEvidence as IAssistantOriginalLegacyMemorySource);
            if (bindMemoryImport)
            {
                OriginalMemoryImports = new(AssistantOriginalMemorySource.ResourceKind, Memory, profiles, ownership,
                    evidence, permissions, authority);
                Memory.BindOriginalMemoryImportSession(OriginalMemoryImports, evidence);
            }
            var resources = new ResourceAuthorizationService(profiles, [new HomeCanonicalAssistantMemoryWriteResourceResolver(
                () => _memoryWrites ?? throw new InvalidOperationException("Memory WRITE unavailable"))]);
            var broker = new HomeResourceOperationBroker(resources, permissions);
            _memoryWrites = new(state, profiles, resources, broker, permissions, Memory);
            Memory.BindOriginalHomeWriteSource(_memoryWrites);
            var chat = configuredChatFactory?.Invoke(this, conversations, tasks) ?? new ChatSessionService(conversations, new NoModelCalls(), new CapabilityPreflightService(), new Safety(),
                new WorkspaceToolRuntime(new Workspace()), new ComputerToolRuntime(new Computer()));
            chat.BindOriginalPersistentMemorySource(Memory);
            _ordinary = new();
            var production = new ConversationProductionRepository(Database, conversations);
            var attachments = configuredAttachmentFactory?.Invoke(this, production, paths);
            Bridge = new(home, conversations, production, chat, tasks, _ordinary,
                models: configuredModelFactory?.Invoke(profiles), attachments: attachments, development: null, capabilities: null, memory: Memory);
        }
        internal async Task<AssistantConversationBinding> CreateAsync(AssistantConfiguration? configuration = null)
        {
            var definition = await Bridge.CreateAsync(ConfiguredIdentityKind.Assistant,
                configuration ?? new() { Name = "Fictional scoped helper", Memory = new(true) }, Guid.NewGuid(), Token);
            return await Bridge.CreateConversationAsync(definition.Identity, definition.Revision, Guid.NewGuid(), "Local memory controls", Guid.NewGuid(), Token);
        }
        internal Task<AssistantOriginalMemoryPreparation> PrepareAsync(AssistantConversationBinding binding) =>
            Memory.PrepareOriginalAssistantMemoryInputWithinSourceAsync(binding, binding.Definition, Scope, Retain, Token);
        internal async Task<KnowledgeRecord> AddAsync(AssistantConversationBinding binding, string summary,
            string? scope = null, string? agentId = null, string? app = null, string? project = null,
            KnowledgePrivacyClass privacy = KnowledgePrivacyClass.Private, DateTimeOffset? expires = null, Guid? bankId = null)
        {
            if (scope is null)
            {
                var prepared = await PrepareAsync(binding);
                Assert.True(prepared.IsPrepared, prepared.Reason); Assert.NotNull(prepared.OriginalStorageScope);
                scope = prepared.OriginalStorageScope;
            }
            var now = DateTimeOffset.UtcNow;
            var record = new KnowledgeRecord(Guid.NewGuid(), KnowledgeCategory.LearnMe, "Scoped memory controls", summary, summary,
                privacy, 1, true, now, now, expires, "Explicit fictional test preference",
                [new("fixture", "Actual canonical fixture source", "conversation", null, null, now, now, null, "User")],
                Scope: scope, Origin: KnowledgeOrigin.Explicit, IsUserLocked: true, AppId: app, ProjectId: project,
                AgentId: agentId ?? binding.Definition.Identity.DefinitionId, KnowledgeBankId: bankId);
            var actual = await Knowledge.UpsertAsync(record, summary, Token); Assert.Equal(record.Id, actual.Id); return actual;
        }
        private sealed record MemoryTableCapture(string? Type, string? Schema,
            IReadOnlyList<SortedDictionary<string, object?>>? Rows);
        internal async Task<string> CaptureMemoryAsync()
        {
            var captured = new Dictionary<string, MemoryTableCapture>();
            await using var connection = await Database.OpenAsync(Token);
            await using var transaction = connection.BeginTransaction(deferred: true);
            foreach (var table in new[] { "knowledge_records", "knowledge_record_details", "knowledge_banks", "retrieval_documents", "retrieval_chunks" })
            {
                string? type = null, schema = null;
                await using (var inspect = connection.CreateCommand())
                {
                    inspect.Transaction = transaction;
                    inspect.CommandText = "SELECT type,sql FROM sqlite_schema WHERE name=$name;";
                    inspect.Parameters.AddWithValue("$name", table);
                    await using var metadata = await inspect.ExecuteReaderAsync(Token);
                    if (await metadata.ReadAsync(Token))
                    {
                        type = metadata.GetString(0); schema = metadata.IsDBNull(1) ? null : metadata.GetString(1);
                        Assert.False(await metadata.ReadAsync(Token), "The exact fixture schema name has multiple owners.");
                    }
                }
                // Genuine absence and an unexpected non-table are distinct captured
                // states. Do not initialize schema or execute an unknown view to make
                // a no-mutation assertion pass.
                if (type != "table") { captured.Add(table, new(type, schema, null)); continue; }
                await using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = $"SELECT * FROM {table} ORDER BY id;";
                await using var reader = await command.ExecuteReaderAsync(Token); var values = new List<SortedDictionary<string, object?>>();
                while (await reader.ReadAsync(Token))
                {
                    var value = new SortedDictionary<string, object?>(StringComparer.Ordinal);
                    for (var index = 0; index < reader.FieldCount; index++) value.Add(reader.GetName(index), reader.IsDBNull(index) ? null : reader.GetValue(index));
                    values.Add(value);
                }
                captured.Add(table, new(type, schema, values));
            }
            return JsonSerializer.Serialize(captured);
        }
        internal async Task PreserveMemoryTableUnderOlderNameAsync()
        {
            // This fresh fixture simulates an older, incomplete schema without deleting
            // either a table or its rows. A read must not create a replacement table.
            await using var connection = await Database.OpenAsync(Token); await using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE knowledge_record_details RENAME TO retained_knowledge_record_details;";
            await command.ExecuteNonQueryAsync(Token);
        }
        internal async Task<long> CountMemoryTablesAsync()
        {
            await using var connection = await Database.OpenAsync(Token); await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('knowledge_records','knowledge_record_details');";
            return Convert.ToInt64(await command.ExecuteScalarAsync(Token));
        }
        internal async Task ReopenAsync() { await CloseAsync(); _retained.Clear(); await InitializeAsync(false); }
        internal async Task CloseAsync()
        {
            var failures = new List<Exception>();
            if (Memory is not null) { var close = Memory.CloseAndDrainAsync(); try { await close; } catch (Exception failure) { if (!ReferenceEquals(close, ExpectedMemoryClose)) failures.Add(failure); } }
            if (Bridge is not null) try { await Bridge.CloseAndDrainAsync(); } catch (Exception failure) { failures.Add(failure); }
            if (_ordinary is not null) try { await _ordinary.CloseAndDrainAsync(); } catch (Exception failure) { failures.Add(failure); }
            if (closeConfiguredAttachments is not null)
            {
                Task? actual = null;
                try { actual = closeConfiguredAttachments(); Retain(actual); await actual; }
                catch (Exception failure) { failures.Add(actual?.Exception ?? failure); }
                if (actual?.IsCompletedSuccessfully != true)
                    throw new AggregateException("Actual attachment borrowers remain unresolved; retain their global stores.", failures);
            }
            if (closeConfiguredTasks is not null)
            {
                Task? actual = null;
                try { actual = closeConfiguredTasks(); Retain(actual); await actual; }
                catch (Exception failure) { failures.Add(actual?.Exception ?? failure); }
            }
            if (_memoryWrites is not null) { var close = _memoryWrites.CloseAndDrainOriginalAsync(); try { await close; } catch (Exception failure) { if (!ReferenceEquals(close, ExpectedWriteClose)) failures.Add(failure); } }
            if (OriginalMemoryImports is { } imports) try { await imports.CloseAndDrainAsync(); } catch (Exception failure) { failures.Add(failure); }
            if (OriginalLegacyEvidence is IAsyncDisposable legacy) try { await legacy.DisposeAsync(); } catch (Exception failure) { failures.Add(failure); }
            if (Store is not null) { var close = Store.CloseAndDrainAsync(); try { await close; } catch (Exception failure) { if (!ReferenceEquals(close, ExpectedStoreClose)) failures.Add(failure); } }
            if (failures.Count > 0) throw new AggregateException("Actual memory borrowers did not join; owners retained.", failures);
            if (_den is not null) await _den.DisposeAsync();
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
        private static Exception Refuse() => new InvalidOperationException("These memory source controls authorize no model invocation.");
        public Task<bool> IsAvailableAsync(CancellationToken token) => throw Refuse();
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) => throw Refuse();
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => throw Refuse();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw Refuse();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw Refuse();
    }
    private sealed class NoExecutionEvents : IExecutionEventSink
    { public bool TryPublish(ExecutionEvent value) => throw new InvalidOperationException("Migration creates no new Task/Run."); }
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
