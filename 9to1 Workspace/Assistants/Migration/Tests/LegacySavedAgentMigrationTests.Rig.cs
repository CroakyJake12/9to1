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
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Apps.Assistants.Migration.Tests;

public sealed partial class LegacySavedAgentMigrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task RunImportAsync(Func<Rig, Task> body)
    {
        var rig = new Rig(); var failures = new List<Exception>();
        try { await rig.InitializeAsync(true, importSource: false); await body(rig); }
        catch (Exception failure) { failures.Add(failure); }
        finally { try { await rig.CloseAsync(); } catch (Exception failure) { failures.Add(failure); } }
        Finish(rig, failures);
    }

    private static async Task RunAsync(Func<Rig, Task> body)
    {
        var rig = new Rig(); var failures = new List<Exception>();
        try { await rig.InitializeAsync(true); await body(rig); }
        catch (Exception error) { failures.Add(error); }
        finally { try { await rig.CloseAsync(); } catch (Exception error) { failures.Add(error); } }
        Finish(rig, failures);
    }
    private static void Finish(Rig rig, List<Exception> failures)
    {
        // Retain task-owned fixtures under the current explicit no-deletion instruction.
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Migration fixture retained at " + rig.Root, failures);
    }

    private sealed class Rig
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "assistants-legacy-migration-" + Guid.NewGuid().ToString("N"));
        internal readonly Guid First = Guid.NewGuid(), Second = Guid.NewGuid(), RunId = Guid.NewGuid(), MemoryId = Guid.NewGuid(), ConversationId = Guid.NewGuid();
        internal SqliteDatabase Database = null!;
        internal KnowledgeLibraryService Knowledge = null!;
        private CanonicalSqliteOriginalStoreOwner _sqliteOwner = null!;
        internal LegacySavedAgentSqliteSource Source = null!;
        internal HomePersonalDenFactory Factory = null!;
        internal DenAssistantCanonicalBridge Bridge = null!;
        internal LegacyAgentMigrationController Controller = null!;
        internal HomeOriginalLocalStoreImportSession ImportSession = null!;
        internal HomePermissionTrustService Permissions = null!;
        internal Task? ExpectedControllerClose = null;
        private HomeDenStoreEvidenceProvider _den = null!;
        private HomeResourceStoreOwnershipAuthority _authority = null!;
        internal IResourceStoreOwnershipReceiptAuthority Authority => _authority;
        private AssistantOriginalConversationHost _ordinary = null!;
        private ChatSessionService _chat = null!;
        private ConversationProductionRepository _production = null!;
        private TaskExecutionCoordinator _tasks = null!;
        private readonly List<LegacyAgentMigrationController> _controllers = [];
        private readonly List<DenAssistantCanonicalBridge> _bridges = [];

        internal async Task InitializeAsync(bool create, bool importSource = true)
        {
            Directory.CreateDirectory(Root);
            // Only this newly task-owned fixture is prepared with private native custody.
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var paths = new Paths(Root);
            Database = new(paths); await Database.InitializeAsync(Token);
            Knowledge = new(Database, new RetrievalIndexService(Database, new LocalHashEmbeddingService()));
            if (create) await SeedActualLegacyAsync();
            var state = new FileHomeCoreStateStore(Path.Combine(Root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(state, new OperatingSystemPrincipalSource());
            var actor = await profiles.GetCurrentAsync(Token) ?? throw new InvalidOperationException("An actual local OS profile is required.");
            if (!OperatingSystem.IsWindows())
                foreach (var path in new[] { paths.DatabasePath, paths.DatabasePath + "-wal", paths.DatabasePath + "-shm" })
                    if (File.Exists(path)) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            _sqliteOwner = new(Database, paths, profiles);
            Source = new(_sqliteOwner, profiles); await Source.PrepareIdentityAsync(Token);
            _den = create ? await HomeDenStoreEvidenceProvider.CreateAsync(Path.Combine(Root, "den"), profiles, Token)
                : await HomeDenStoreEvidenceProvider.OpenAsync(Path.Combine(Root, "den"), profiles, Token);
            var permissions = new HomePermissionTrustService(state, (app, action) =>
                app == "9to1.home.local-profile" && action == "home.profile.importStore" ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, true) : null);
            Permissions = permissions;
            var ownership = new HomeLocalStoreOwnership(state, profiles, new HomeLocalStoreEvidenceRegistry([_den, Source]), permissions);
            if (create) await ownership.BindNewEmptyAsync(actor, "den", _den.Store.Manifest.DenId, Token);
            if (create && importSource)
            {
                var setup = new HomeLocalStoreSetupSession(LegacySavedAgentSqliteSource.LegacyResourceKind, Source, profiles, ownership, Source);
                var inspect = await setup.InspectAsync(Token); Assert.False(inspect.CanBindEmpty); Assert.False(inspect.IsOwned);
                var request = await setup.RequestImportAsync(Token); Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
                Assert.True((await permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept, cancellationToken: Token)).Succeeded);
                await setup.CompleteImportAsync(Token); Assert.True((await setup.InspectAsync(Token)).IsOwned);
            }
            _authority = new(ownership, profiles); Factory = new(_den, _authority, profiles);
            ImportSession = new(LegacySavedAgentSqliteSource.LegacyResourceKind, Source, profiles, ownership,
                Source, permissions, _authority);
            var conversations = new ConversationRepository(Database);
            _production = new ConversationProductionRepository(Database, conversations);
            _chat = new ChatSessionService(conversations, new NoModelCalls(), new CapabilityPreflightService(), new Safety(),
                new WorkspaceToolRuntime(new Workspace()), new ComputerToolRuntime(new Computer()));
            _ordinary = new();
            _tasks = new(new TaskExecutionRepository(Database), new NoExecutionEvents());
            Bridge = NewBridge(conversations);
            Controller = NewController();
        }
        internal DenAssistantCanonicalBridge NewBridge(IConversationRepository conversations)
        { var bridge = new DenAssistantCanonicalBridge(Factory, conversations, _production, _chat, _tasks, _ordinary); _bridges.Add(bridge); return bridge; }
        internal LegacyAgentMigrationController NewController(IResourceStoreOwnershipReceiptAuthority? authority = null)
        { var controller = new LegacyAgentMigrationController(Source, Factory, authority ?? _authority, Bridge,
            authority is null ? ImportSession : null); _controllers.Add(controller); return controller; }
        internal async Task ReopenAsync()
        { await CloseAsync(); _controllers.Clear(); _bridges.Clear(); await InitializeAsync(false); }
        internal async Task CloseAsync()
        {
            var failures = new List<Exception>();
            foreach (var controller in _controllers)
            {
                var close = controller.CloseAndDrainAsync();
                try { await close; } catch (Exception error) { if (!ReferenceEquals(close, ExpectedControllerClose)) failures.Add(error); }
            }
            foreach (var bridge in _bridges)
                try { await bridge.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
            if (_ordinary is not null) try { await _ordinary.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
            if (ImportSession is not null) try { await ImportSession.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
            if (Source is not null) try { await Source.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
            if (_sqliteOwner is not null) try { await _sqliteOwner.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
            if (failures.Count > 0) throw new AggregateException("Actual migration borrowers did not join.", failures);
            if (_den is not null) await _den.DisposeAsync();
            if (Database is not null)
            {
                var connection = await Database.OpenAsync(CancellationToken.None);
                await connection.DisposeAsync(); SqliteConnection.ClearPool(connection);
            }
        }
        internal async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = await Database.OpenAsync(Token); await using var command = connection.CreateCommand();
            command.CommandText = sql; foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            await command.ExecuteNonQueryAsync(Token);
        }
        private async Task SeedActualLegacyAsync()
        {
            await ExecuteAsync("ALTER TABLE agents ADD COLUMN unknown_future_configuration TEXT;");
            await ExecuteAsync("""
                INSERT INTO agents(id,name,description,instructions,icon_key,preferred_model,fallback_model,detection_rules,permissions_json,is_built_in,is_enabled,updated_at,unknown_future_configuration)
                VALUES($id,'Fictional migration helper','Legacy description','Original role and instructions','helper','legacy-model','legacy-fallback','explicit-only','{"resourceIds":["old-resource"],"unknown":true}',0,1,$now,'retained extension');
                INSERT INTO agents(id,name,description,instructions,icon_key,preferred_model,detection_rules,permissions_json,is_built_in,is_enabled,updated_at)
                VALUES($second,'Unselected disabled helper','Keep untouched','Unselected instructions','helper','','','{}',0,0,$now);
                INSERT INTO agent_runs(id,agent_id,agent_name,task,status,model_name,result,error,capabilities_json,activity_json,created_at,resource_reference)
                VALUES($run,$id,'Fictional migration helper','Existing historical task',2,'legacy-model','existing result','','[]',$activity,$now,'canonical://existing-resource');
                """, ("$id", First.ToString("D")), ("$second", Second.ToString("D")), ("$run", RunId.ToString("D")),
                ("$now", DateTimeOffset.UtcNow.ToString("O")),
                ("$activity", JsonSerializer.Serialize(new { CanonicalBindingVersion = 1, CanonicalTask = new { TaskId = Guid.NewGuid(), ContextId = ConversationId, ExecutionId = Guid.NewGuid(), PersistenceRevision = 1, State = 2 }, Activities = new[] { "retained activity" }, UnknownFutureField = "retained" })));
            var conversations = new ConversationRepository(Database); var now = DateTimeOffset.UtcNow;
            await conversations.UpsertConversationAsync(new(ConversationId, HavenMode.Tasks, ConversationKind.Chat,
                "Existing original conversation", null, null, false, false, now, now), Token);
            await conversations.AddMessageAsync(new(Guid.NewGuid(), ConversationId, MessageRole.User,
                "Existing original message", "Fictional migration helper", null, "{\"originalMetadata\":true}", now), Token);
            await Knowledge.UpsertAsync(new(MemoryId, KnowledgeCategory.LearnMe, "Migration fixture", "Original scoped memory",
                "Existing personal memory", KnowledgePrivacyClass.Private, 1, true, now, now, null,
                "Explicit original user configuration", [new("original-source", "Original source", "conversation", null, null, now, now, null, "User")],
                Scope: "agent", Origin: KnowledgeOrigin.Explicit, IsUserLocked: true, AgentId: First.ToString("D")),
                "Existing personal memory", Token);
        }
        internal async Task<string> CaptureLegacyAsync()
        {
            var captured = new Dictionary<string, List<SortedDictionary<string, object?>>>();
            await using var connection = await Database.OpenAsync(Token);
            foreach (var table in new[] { "agents", "agent_runs", "knowledge_records", "knowledge_record_details", "retrieval_documents", "retrieval_chunks", "conversations", "messages" })
            {
                await using var command = connection.CreateCommand(); command.CommandText = $"SELECT * FROM {table} ORDER BY id;";
                await using var reader = await command.ExecuteReaderAsync(Token); var rows = new List<SortedDictionary<string, object?>>();
                while (await reader.ReadAsync(Token))
                {
                    var row = new SortedDictionary<string, object?>(StringComparer.Ordinal);
                    for (var index = 0; index < reader.FieldCount; index++) row.Add(reader.GetName(index), reader.IsDBNull(index) ? null : reader.GetValue(index));
                    rows.Add(row);
                }
                captured.Add(table, rows);
            }
            return JsonSerializer.Serialize(captured);
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
        private static Exception Refuse() => new InvalidOperationException("Migration authorizes no model invocation.");
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
