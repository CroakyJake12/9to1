using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Apps.Dev;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Haven.Desktop.Tests;

// PRIVATE genuine Linux Home/SQLite/Files/native fixture. Each creation uses a distinct
// manual Home WRITE, separate from the still-live original Studio project READ.
public sealed partial class CanonicalCompatibleTaskContextCompositionTests
{
    private static async Task Run(Func<Rig, Task> body)
    {
        var rig = new Rig(); var errors = new List<Exception>();
        try { await rig.InitializeAsync(); await body(rig); } catch (Exception cause) { errors.Add(cause); }
        await rig.JoinOriginalsAsync(errors);
        // Keep all evidence on failure or unknown cleanup. No reclamation is performed.
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count != 0) throw new AggregateException("Actual compatible Tasks fixture failed; retained at " + rig.Root, errors);
    }
    private sealed partial class Rig(bool includeOriginalDen = false)
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "actual-compatible-tasks-" + Guid.NewGuid().ToString("N"));
        private readonly CancellationTokenSource _active = new(TimeSpan.FromSeconds(60));
        private readonly object _gate = new(); private readonly List<Task> _raw = [];
        private readonly HashSet<Exception> _expected = new(ReferenceEqualityComparer.Instance);
        private readonly List<HomeCompatibleTaskContextWriteSource> _allWrites = [];
        private readonly List<CanonicalProjectTaskContextCreateOwner> _allCreators = [];
        private CloudflareLocalDomainRegistration? _registration;
        private HomeCompatibleTaskContextWriteSource? _writes;
        private Paths _paths = null!;
        internal ServiceProvider Provider = null!; internal HomeLocalDomainComposition Home = null!;
        internal CanonicalSqliteOriginalStoreOwner Store = null!;
        internal CanonicalProjectContextStoreReadOwner Contexts = null!;
        internal CanonicalProjectTaskContextCreateOwner Creator = null!;
        internal CanonicalProjectTaskContextCreateOwner CurrentCreator => Creator;
        internal HomeColdProjectReadReconciliation ProjectReads = null!;
        internal Conversation OriginalStudio = null!; internal ContainerDefinition OriginalStudioContainer = null!;
        internal IDeveloperOriginalProjectCommandRead LiveRead = null!;
        internal AuthenticatedResourceActor Actor = null!;
        internal SqliteDatabase Database = null!; internal ConversationRepository Conversations = null!;
        internal ContainerRepository Containers = null!;
        internal string ProjectReference = null!;
        internal List<string> ApprovalRequestIds { get; } = [];
        internal CancellationToken Token => _active.Token;
        internal void Scope(Action body) => body();
        internal void Retain(Task actual) { lock (_gate) _raw.Add(actual); }
        internal Task[] ObserveRetainedOriginalSources()
        { lock (_gate) return _raw.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray(); }
        internal bool IsAcknowledgedOriginalHomeWriteRefusal(Task actual) =>
            _allWrites.Any(owner => owner.IsAcknowledgedOriginalWriteRefusal(actual));
        internal Task<T> Keep<T>(Task<T> actual) { Retain(actual); return actual; }
        internal Task Keep(Task actual) { Retain(actual); return actual; }
        internal void KeepExpected(Task actual, Exception observed)
        { lock (_gate) foreach (var cause in References(actual.Exception ?? observed)) _expected.Add(cause); }
        private static IEnumerable<Exception> References(Exception cause)
        { yield return cause; if (cause is AggregateException group) foreach (var child in group.InnerExceptions) foreach (var value in References(child)) yield return value; }

        internal async Task InitializeAsync()
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The genuine fixture requires Linux native owners.");
            Directory.CreateDirectory(Root); File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var homeRoot = Path.Combine(Root, "Home"); Directory.CreateDirectory(homeRoot);
            File.SetUnixFileMode(homeRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            _paths = new(Root); Database = new(_paths);
            NativeFilesWorkspaceService? files = null; NativeFilesWorkspaceAuthority? authority = null;
            HomeColdProjectReadReconciliation? reads = null;
            _registration = CloudflareLocalDomainRegistration.CreateOriginal(new FileHomeCoreStateStore(Path.Combine(homeRoot, "state.json")),
                new OperatingSystemPrincipalSource(), _paths,
                originalResolvers: [new HomeColdProjectReadResourceResolver(() => reads ?? throw new InvalidOperationException("Actual READ owner unavailable.")),
                    new HomeCompatibleTaskContextWriteResourceResolver(() => _writes ?? throw new InvalidOperationException("Actual WRITE owner unavailable."))],
                originalPolicies: [new FixtureLocalProfileImportPolicy(), new HomeColdProjectReadActionPolicySource(), new HomeCompatibleTaskContextWriteActionPolicy()],
                configureOriginalStores: identity =>
                {
                    files = new(identity.StateStore, identity.Profiles);
                    Store = new(Database, _paths, identity.Profiles);
                    var canonical = new CanonicalSqliteOriginalStoreEvidenceProvider(Store, "canonical.sqlite");
                    var evidence = new List<IHomeLocalStoreEvidenceProvider> { files, canonical };
                    var stores = new Dictionary<Type, object> { [typeof(NativeFilesWorkspaceService)] = files,
                        [typeof(CanonicalSqliteOriginalStoreOwner)] = Store };
                    ConfigureOriginalDenEvidence(identity.Profiles, evidence, stores);
                    return new(evidence, stores);
                },
                configureOriginalResolvers: components =>
                {
                    authority = new(files!, components.Identity.Profiles, components.Ownership);
                    return new([], new Dictionary<Type, object> { [typeof(NativeFilesWorkspaceAuthority)] = authority });
                });
            Home = _registration.OriginalHome;
            var services = new ServiceCollection(); services.AddHavenInfrastructure();
            // The normal Desktop App registers this SAME maintained Calendar/Planner graph before Task/Chat.
            services.AddHavenPlannerInfrastructure();
            // Use the SAME maintained Task/Chat/runtime graph required by native Dev and the real Den controller.
            services.AddHavenOriginalTaskExecutionServices();
            services.AddHavenOwnedNativeTaskColdRecovery(NativePersonalTaskColdRecoveryConfiguration.ParseOriginalOptIn("1"));
            _registration.ConfigureOriginalServices(services);
            // Preserve the maintained singleton graph while selecting the SAME precreated database.
            var databaseDescriptor = Assert.Single(services, value => value.ServiceType == typeof(SqliteDatabase));
            services[services.IndexOf(databaseDescriptor)] = ServiceDescriptor.Singleton(Database);
            services.AddSingleton(files!); services.AddSingleton(authority!);
            services.AddFilesNativeHost(); services.AddHavenOriginalNativeDevelopment();
            services.AddHavenOwnedNativeColdProjectResources(Home);
            Provider = services.BuildServiceProvider(); ProjectReads = Provider.GetRequiredService<HomeColdProjectReadReconciliation>(); reads = ProjectReads;
            Assert.Same(Database, Provider.GetRequiredService<SqliteDatabase>());
            await Keep(Database.InitializeAsync(Token));
            foreach (var file in new[] { _paths.DatabasePath, _paths.DatabasePath + "-wal", _paths.DatabasePath + "-shm" })
                if (File.Exists(file)) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Actor = (await Keep(Home.Profiles.GetCurrentAsync(Token).AsTask()))!;
            await InitializeOriginalDenAsync();
            Conversations = Provider.GetRequiredService<ConversationRepository>();
            Containers = (ContainerRepository)Provider.GetRequiredService<IContainerRepository>();
            Contexts = new(Store, Database, Conversations, Containers, Home.Ownership);
            Creator = new(Contexts, Store, ProjectReads); BindWrites();
            var filesRoot = Path.Combine(Root, "chosen-files"); Directory.CreateDirectory(filesRoot);
            var workspace = await Keep(files!.ConfigureNewAsync(filesRoot, Home.LocalStoreOwnership, Token));
            var projectId = Guid.NewGuid(); var rootId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            var projectPath = Path.Combine(filesRoot, "actual-studio-project"); Directory.CreateDirectory(projectPath);
            File.SetUnixFileMode(projectPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var folder = HostedItemId.New();
            var operation = new FilesOperation(new(Guid.NewGuid()), workspace.Actor.ActorId, folder, null, null,
                "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null);
            Assert.True((await Keep(workspace.Provider.MutateAsync(operation, "actual-studio-project", Token))).IsSuccess);
            Assert.True((await Keep(workspace.Directories.RegisterProfileAsync(Guid.Parse(workspace.Actor.ProfileId), folder,
                "dev.project." + projectId.ToString("N"), projectPath, Token))).IsSuccess);
            var draft = DeveloperWorkspace.Create([new(rootId, projectPath)]) with
            { Projects = [new(projectId, "generic", "actual Studio project", [rootId], "C#", null, "none", [], [], [], [], null)] };
            var saved = await Keep(Provider.GetRequiredService<FileDeveloperWorkspaceStore>().CreateAsync(draft, Token)); Assert.True(saved.Succeeded);
            var actualWorkspace = saved.Value!;
            ProjectReference = JsonSerializer.Serialize(new DeveloperProjectReference(actualWorkspace.WorkspaceId, actualWorkspace.Revision,
                projectId, actualWorkspace.Projects[0].Revision, rootId));
            OriginalStudioContainer = new(Guid.NewGuid(), HavenMode.Studio, "actual Studio container", projectPath, ProjectReference,
                "actual Studio instructions", now, now);
            await Keep(Containers.UpsertAsync(OriginalStudioContainer, Token));
            OriginalStudio = new(Guid.NewGuid(), HavenMode.Studio, ConversationKind.StudioChat, "actual Studio conversation",
                OriginalStudioContainer.Id, null, false, false, now, now);
            Assert.True(await Keep(Conversations.TryCreateConversationAsync(OriginalStudio, Token)));
            await Keep(Conversations.AddMessageAsync(new(Guid.NewGuid(), OriginalStudio.Id, MessageRole.User,
                "existing original Studio history", null, null, null, now), Token));
            var production = Provider.GetRequiredService<IConversationProductionRepository>();
            var rootBranch = await Keep(production.EnsureRootBranchAsync(OriginalStudio.Id, Token));
            var branch = await Keep(production.CreateBranchAsync(OriginalStudio.Id, rootBranch.Id, null,
                "Existing alternate history", ConversationBranchReason.Manual, Token));
            await Keep(production.SaveDraftAsync(new(OriginalStudio.Id, branch.Id,
                "Existing original Studio draft", "[]", now), Token));
            await ImportCanonicalStore();
            var read = Keep(ProjectReads.AcquireOriginalCommandReadWithinSourceAsync(OriginalStudio, OriginalStudioContainer,
                ProjectReference, Scope, Retain, Token));
            await DecideNextAsync(read, HomeColdProjectReadReconciliation.ReadAction, HomeApprovalChoice.Accept);
            LiveRead = await read; Assert.True(ProjectReads.IsIssuedOriginalCommandRead(LiveRead));
        }
        private void BindWrites()
        {
            _writes = new(Home.StateStore, Home.Profiles, Home.Resources, Home.Broker, Home.Permissions, ProjectReads, Creator);
            _allCreators.Add(Creator); _allWrites.Add(_writes); Creator.BindOriginalHomeWriteSource(_writes);
        }
        internal async Task ImportCanonicalStore()
        {
            var identity = await Keep(Store.GetStoreIdentityWithinOriginalSourceAsync(Actor, Scope, Retain, Token));
            var storeId = identity.StoreId.ToString("D");
            Assert.Null(await Keep(Home.LocalStoreOwnership.GetVerifiedAsync("canonical.sqlite", storeId, Token)));
            var approval = await Keep(Home.LocalStoreOwnership.RequestImportAsync(Actor, "canonical.sqlite", storeId, Actor.AuthenticationRevision, Token));
            Assert.Equal(HomePermissionRequestState.PendingApproval, approval.State);
            var request = Assert.Single((await Keep(Home.Permissions.GetSnapshotAsync(cancellationToken: Token))).PendingRequests,
                value => value.RequestId == approval.RequestId);
            Assert.Equal("9to1.home.local-profile", request.Scope.TargetAppId);
            Assert.Equal("home.profile.importStore", request.Scope.ActionName);
            Assert.Equal(new HomeObjectReference("local-resource-store", "canonical.sqlite:" + storeId), Assert.Single(request.Scope.Objects));
            Assert.Equal(Actor.ActorId, request.Caller.CallerId);
            Assert.Equal(Actor.AuthenticationRevision, request.Caller.IdentityVersion);
            Assert.True(request.Caller.IsVerified);
            Assert.Equal(new HomePermissionActionPolicy(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High,
                false, false, true), request.Policy);
            Assert.Null(await Keep(Home.LocalStoreOwnership.GetVerifiedAsync("canonical.sqlite", storeId, Token)));
            Assert.True((await Keep(Home.Permissions.DecideAsync(approval.RequestId, HomeApprovalChoice.Accept, cancellationToken: Token))).Succeeded);
            var imported = await Keep(Home.LocalStoreOwnership.CompleteImportAsync(approval.RequestId, Token));
            Assert.Equal(Actor.ProfileId, imported.ProfileId);
            Assert.Equal("canonical.sqlite", imported.ResourceKind);
            Assert.Equal(storeId, imported.StoreId);
            Assert.Equal(approval.RequestId, imported.ImportApprovalId);
            Assert.Equal(imported, await Keep(Home.LocalStoreOwnership.GetVerifiedAsync("canonical.sqlite", storeId, Token)));
        }
        internal async Task<ICanonicalProjectTaskContextCreationIntent> PrepareStudioIntentAsync(Guid taskId, string title, Guid operationId)
        {
            var observed = await Keep(Contexts.ReadOriginalProjectContextsWithinSourceAsync(Actor, 1, Scope, Retain, Token,
                OriginalStudio.Id));
            return await Keep(Creator.PrepareOriginalCreationIntentWithinSourceAsync(observed, LiveRead, taskId, title, operationId, Scope, Retain, Token));
        }
        internal Task<ICanonicalProjectTaskContextCreation> StartOriginalCommit(ICanonicalProjectTaskContextCreationIntent intent)
            => Keep(Creator.CommitOriginalCreationWithinSourceAsync(intent, LiveRead, Scope, Retain, Token));
        internal Task AcceptNextWriteOrJoinAsync(Task actual) => DecideNextAsync(actual, HomeCompatibleTaskContextWriteSource.WriteAction, HomeApprovalChoice.Accept);
        internal async Task<Conversation> ReadStoredConversationAsync(Guid id)
        {
            var observed = await Keep(Contexts.ReadOriginalProjectContextsWithinSourceAsync(Actor, 1, Scope, Retain, Token, id));
            return Assert.Single(observed.Conversations);
        }
        internal async Task<ICanonicalProjectTaskContextCreation> CommitWithManualWriteAsync(ICanonicalProjectTaskContextCreationIntent intent)
        {
            var actual = StartOriginalCommit(intent);
            await AcceptNextWriteOrJoinAsync(actual);
            return await actual;
        }
        internal async Task<ICanonicalProjectTaskContextCreationIntent> ReopenIntentAsync(Guid taskId, string title, Guid operationId)
        {
            await Keep(Creator.CloseAndDrainOriginalAsync());
            await Keep(_writes!.CloseAndDrainOriginalAsync());
            Creator = new(Contexts, Store, ProjectReads); BindWrites();
            return await PrepareStudioIntentAsync(taskId, title, operationId);
        }
        internal async Task DecideNextAsync(Task actual, string action, HomeApprovalChoice choice)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(15))
            {
                var snapshot = await Keep(Home.Permissions.GetSnapshotAsync(cancellationToken: Token));
                var pending = snapshot.PendingRequests.Where(value => value.Scope.ActionName == action && !ApprovalRequestIds.Contains(value.RequestId, StringComparer.Ordinal)).ToArray();
                if (pending.Length != 0)
                {
                    var request = Assert.Single(pending); Assert.True(request.Policy.RequiresPerActionApproval);
                    Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
                    Assert.True((await Keep(Home.Permissions.DecideAsync(request.RequestId, choice, cancellationToken: Token))).Succeeded);
                    if (action == HomeCompatibleTaskContextWriteSource.WriteAction) ApprovalRequestIds.Add(request.RequestId); return;
                }
                if (actual.IsCompleted) { await actual; throw new InvalidOperationException("Original operation completed without separate manual Home approval."); }
                await Task.Delay(TimeSpan.FromMilliseconds(10), Token);
            }
            throw new TimeoutException("No actual individual Home approval for " + action);
        }
        internal async Task<byte[]> ReadStudioRowsAndHistoryBytesAsync()
        {
            // Full maintained original row/message projections; branches/draft are read from the
            // actual protected SQLite snapshot below rather than inferred from a product facade.
            var lease = await Keep(Store.AcquireOriginalProtectedReadWithinSourceAsync(Actor, false, Scope, Retain, Token));
            var values = new List<object?>(); Exception? primary = null;
            try
            {
                foreach (var sql in new[] { "SELECT * FROM conversations WHERE id=$id ORDER BY id", "SELECT * FROM containers WHERE id=$container ORDER BY id",
                    "SELECT * FROM messages WHERE conversation_id=$id ORDER BY id", "SELECT * FROM conversation_branches WHERE conversation_id=$id ORDER BY id",
                    "SELECT m.* FROM conversation_branch_messages m JOIN conversation_branches b ON b.id=m.branch_id WHERE b.conversation_id=$id ORDER BY m.branch_id,m.sequence",
                    "SELECT * FROM conversation_drafts WHERE conversation_id=$id ORDER BY branch_id",
                    "SELECT v.* FROM message_versions v JOIN messages m ON m.id=v.message_id WHERE m.conversation_id=$id ORDER BY v.id",
                    "SELECT * FROM conversation_turns WHERE conversation_id=$id ORDER BY id",
                    "SELECT * FROM message_attachments WHERE conversation_id=$id ORDER BY id",
                    "SELECT * FROM message_bookmarks WHERE conversation_id=$id ORDER BY id",
                    "SELECT * FROM response_usage WHERE conversation_id=$id ORDER BY id" })
                {
                    var command = lease.CreateOriginalCommand();
                    lease.InvokeOriginalSource(() => { command.CommandText = sql; command.Parameters.AddWithValue("$id", OriginalStudio.Id.ToString()); command.Parameters.AddWithValue("$container", OriginalStudioContainer.Id.ToString()); return true; });
                    var reader = await Keep(lease.ReadOriginalSourceAsync(() => command.ExecuteReaderAsync(Token)));
                    while (await Keep(lease.ReadOriginalSourceAsync(() => reader.ReadAsync(Token))))
                        values.Add(lease.InvokeOriginalSource(() => Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray()));
                    await Keep(lease.CloseOriginalResourceAsync(reader)); await Keep(lease.CloseOriginalResourceAsync(command));
                }
            }
            catch (Exception cause) { primary = cause; }
            var errors = new List<Exception>(); try { await Keep(lease.CloseAndDrainAsync()); } catch (Exception cause) { errors.Add(cause); }
            if (primary is not null) errors.Insert(0, primary);
            if (errors.Count != 0) throw new AggregateException("Actual original Studio snapshot read/close failed.", errors);
            return JsonSerializer.SerializeToUtf8Bytes(values);
        }
        internal async Task<(IReadOnlyList<Conversation> Conversations, IReadOnlyList<ContainerDefinition> Containers)> ReadTasksPairsAsync()
        {
            var observed = await Keep(Contexts.ReadOriginalProjectContextsWithinSourceAsync(Actor, 64, Scope, Retain, Token, includeStudioContexts: true));
            return (observed.Conversations.Where(value => value.Mode == HavenMode.Tasks).ToArray(), observed.Containers.Where(value => value.Mode == HavenMode.Tasks).ToArray());
        }
        internal async Task<Conversation> SeedCollidingConversationAsync(Guid id)
        {
            var now = DateTimeOffset.UtcNow;
            var collision = new Conversation(id, HavenMode.Studio, ConversationKind.StudioChat, "existing unrelated collision", OriginalStudioContainer.Id, null, false, false, now, now);
            Assert.True(await Keep(Conversations.TryCreateConversationAsync(collision, Token))); return collision;
        }
        internal Task ChangeOriginalStudioTitleAsync(string title) => Keep(Conversations.UpsertConversationAsync(OriginalStudio with { Title = title }, Token));
        internal async Task JoinOriginalsAsync(List<Exception> errors)
        {
            // Business/store borrowers first, then original Home. Acquire all actual closes independently.
            await JoinOriginalAssistantBorrowersAsync(errors);
            var closes = new List<Task>();
            foreach (var creator in _allCreators) try { closes.Add(Keep(creator.CloseAndDrainOriginalAsync())); } catch (Exception cause) { AddUnexpected(cause, errors); }
            foreach (var close in closes) try { await close; } catch (Exception cause) { AddUnexpected(close.Exception ?? cause, errors); }
            closes.Clear();
            foreach (var owner in _allWrites) try { closes.Add(Keep(owner.CloseAndDrainOriginalAsync())); } catch (Exception cause) { AddUnexpected(cause, errors); }
            foreach (var close in closes) try { await close; } catch (Exception cause) { AddUnexpected(close.Exception ?? cause, errors); }
            foreach (Func<Task> factory in new Func<Task>[] { () => LiveRead?.CloseAndDrainOriginalAsync() ?? Task.CompletedTask,
                () => ProjectReads?.CloseAndDrainOriginalAsync() ?? Task.CompletedTask, () => Store?.CloseAndDrainAsync() ?? Task.CompletedTask })
            { Task? close = null; try { close = Keep(factory()); await close; } catch (Exception cause) { AddUnexpected(close?.Exception ?? cause, errors); } }
            await JoinOriginalAssistantDenAsync(errors);
            Task[] raw; lock (_gate) raw = _raw.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
            foreach (var actual in raw)
                try { await actual; }
                catch (Exception cause)
                {
                    // The creator owns the exact retained Home/inner source occurrence,
                    // and acknowledges it only after healthy no-SQL refusal cleanup. Join
                    // that SAME task before querying its issuer; exception aliases and
                    // unknown or mixed source failures remain ordinary cleanup failures.
                    if (!_allCreators.Any(owner => owner.IsAcknowledgedOriginalCreationSourceRefusal(actual)))
                        AddUnexpected(actual.Exception ?? cause, errors);
                }
            Task? providerClose = null, homeClose = null;
            try { if (Provider is not null) { providerClose = Provider.DisposeAsync().AsTask(); await providerClose; } } catch (Exception cause) { AddUnexpected(providerClose?.Exception ?? cause, errors); }
            try { if (_registration is not null) { Home.RequestOriginalProcessRetirement(); homeClose = Home.CloseAndDrainAsync(); await homeClose; } } catch (Exception cause) { AddUnexpected(homeClose?.Exception ?? cause, errors); }
            _active.Dispose();
        }
        private void AddUnexpected(Exception cause, List<Exception> errors)
        {
            lock (_gate) if (_expected.Contains(cause)) return;
            if (cause is AggregateException { InnerExceptions.Count: > 0 } group) { foreach (var child in group.InnerExceptions) AddUnexpected(child, errors); return; }
            if (!errors.Any(prior => ReferenceEquals(prior, cause))) errors.Add(cause);
        }
    }
    // Register the same exact trusted import action declared by HomeAppAiServices.
    // This is catalogue metadata; only the actual Home broker's individual Accept
    // can authorize the populated canonical store import below.
    private sealed class FixtureLocalProfileImportPolicy : IHomeActionPolicySource
    {
        public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
            appId == "9to1.home.local-profile" && actionId == "home.profile.importStore"
                ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, true)
                : null;
    }

    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root; public string DatabasePath => Path.Combine(root, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser"); public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs"); public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
