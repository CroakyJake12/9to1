using System.Reflection;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Real protected local SQLite/native-principal + maintained canonical Chat/Task/frame
/// component controls. Catalogue/transport/privacy/cloud admission remain the existing controlled
/// no-effect sources. These do not establish a live model, cold AUTH activation or crash acceptance.</summary>
public sealed partial class ChatCloudPermissionCallerTests
{
    [Fact]
    public async Task Cold_journal_captures_actual_hosted_closed_input_and_new_source_graph_observes_same_Task_Run_User()
    {
        if (!OperatingSystem.IsLinux()) return; // This narrowly qualified domain is Linux-only.
        await using var fixture = await ColdPersonalFixture.CreateAsync(TestContext.Current.CancellationToken);
        var lease = await fixture.StartAsync(TestContext.Current.CancellationToken);
        await InitialHostedProducer(lease);
        await fixture.CaptureDriver(lease);
        var before = (await fixture.Rows.GetByContextAsync(fixture.Conversation.Id, TestContext.Current.CancellationToken))!;
        var user = Assert.Single(await fixture.Conversations.GetMessagesAsync(fixture.Conversation.Id, TestContext.Current.CancellationToken));
        var fresh = fixture.NewObservationCoordinator();
        var observed = Assert.IsType<TaskRunColdCapsule>(await fresh.ObserveOriginalColdInputAsync(before.TaskId,
            before.ExecutionId, TestContext.Current.CancellationToken));
        Assert.Equal(before.TaskId, observed.AcknowledgedTask.TaskId);
        Assert.Equal(before.ExecutionId, observed.AcknowledgedTask.ExecutionId);
        Assert.Equal(before.PersistenceRevision, observed.AcknowledgedTask.PersistenceRevision);
        Assert.Equal(before.OwnerBinding, observed.AcknowledgedTask.OwnerBinding);
        Assert.Equal(user, observed.AcceptedUserMessage);
        Assert.Equal(fixture.Conversation.Id, observed.AcceptedConversation.Id);
        Assert.Equal("same initial hosted input", observed.OriginalInput.Prompt);
        Assert.Empty(observed.AcknowledgedTask.Attempts);
        Assert.Empty(observed.AcknowledgedTask.Plan);
        Assert.Empty(observed.AcknowledgedTask.RecoveryHistory);
        Assert.Equal(0, fixture.Client.Dispatches);
        await lease.DetachAndDrainAsync();
        await fresh.CloseAndSuspendOriginalProducersAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cold_journal_refuses_tampered_capsule_or_public_claim_state_without_reconstructing_provenance(bool state)
    {
        if (!OperatingSystem.IsLinux()) return;
        await using var fixture = await ColdPersonalFixture.CreateAsync(TestContext.Current.CancellationToken);
        var lease = await fixture.StartAsync(TestContext.Current.CancellationToken);
        await InitialHostedProducer(lease); await fixture.CaptureDriver(lease); await lease.DetachAndDrainAsync();
        var before = (await fixture.Rows.GetByContextAsync(fixture.Conversation.Id, TestContext.Current.CancellationToken))!;
        await fixture.ExecuteAsync(state
            ? "UPDATE task_run_recovery_journal SET claim_state=1,claim_id='public-edit';"
            : "UPDATE task_run_recovery_journal SET capsule_json=replace(capsule_json,'same initial hosted input','replaced persisted input');",
            TestContext.Current.CancellationToken);
        var fresh = fixture.NewObservationCoordinator();
        var source = fresh.ObserveOriginalColdInputAsync(before.TaskId, before.ExecutionId, TestContext.Current.CancellationToken);
        _ = await Assert.ThrowsAnyAsync<Exception>(async () => { _ = await source; });
        Assert.True(source.IsFaulted);
        var after = (await fixture.Rows.GetAsync(before.TaskId, TestContext.Current.CancellationToken))!;
        Assert.Equal(before.TaskId, after.TaskId); Assert.Equal(before.ExecutionId, after.ExecutionId);
        Assert.Equal(before.PersistenceRevision, after.PersistenceRevision);
        Assert.Single(await fixture.Conversations.GetMessagesAsync(fixture.Conversation.Id, TestContext.Current.CancellationToken));
        Assert.Equal(0, fixture.Client.Dispatches);
        _ = await Record.ExceptionAsync(fresh.CloseAndSuspendOriginalProducersAsync);
    }

    [Fact]
    public async Task Cold_journal_refuses_changed_current_User_branch_and_keeps_original_accepted_input()
    {
        if (!OperatingSystem.IsLinux()) return;
        await using var fixture = await ColdPersonalFixture.CreateAsync(TestContext.Current.CancellationToken);
        var lease = await fixture.StartAsync(TestContext.Current.CancellationToken);
        await InitialHostedProducer(lease); await fixture.CaptureDriver(lease); await lease.DetachAndDrainAsync();
        var before = (await fixture.Rows.GetByContextAsync(fixture.Conversation.Id, TestContext.Current.CancellationToken))!;
        await fixture.ExecuteAsync("UPDATE message_versions SET content='changed actual current User';", TestContext.Current.CancellationToken);
        var fresh = fixture.NewObservationCoordinator();
        _ = await Assert.ThrowsAnyAsync<Exception>(async () =>
        { _ = await fresh.ObserveOriginalColdInputAsync(before.TaskId, before.ExecutionId, TestContext.Current.CancellationToken); });
        var changed = Assert.Single(await fixture.Conversations.GetMessagesAsync(fixture.Conversation.Id, TestContext.Current.CancellationToken));
        Assert.Equal("changed actual current User", changed.Content);
        Assert.Equal(before.TaskId, (await fixture.Rows.GetAsync(before.TaskId, TestContext.Current.CancellationToken))!.TaskId);
        Assert.Equal(0, fixture.Client.Dispatches);
        _ = await Record.ExceptionAsync(fresh.CloseAndSuspendOriginalProducersAsync);
    }

    [Fact]
    public async Task Cold_journal_missing_fresh_owner_authority_refuses_before_claim_or_business_dispatch()
    {
        if (!OperatingSystem.IsLinux()) return;
        await using var fixture = await ColdPersonalFixture.CreateAsync(TestContext.Current.CancellationToken);
        var lease = await fixture.StartAsync(TestContext.Current.CancellationToken);
        await InitialHostedProducer(lease); await fixture.CaptureDriver(lease); await lease.DetachAndDrainAsync();
        var before = (await fixture.Rows.GetByContextAsync(fixture.Conversation.Id, TestContext.Current.CancellationToken))!;
        var fresh = fixture.NewObservationCoordinator();
        // The actual configured old authority lacks the separate fresh cold source. Merely
        // authentic capsule fields must not revive it, invoke a provider, or consume a claim.
        _ = await Assert.ThrowsAnyAsync<Exception>(async () =>
        { _ = await fresh.StartObservedColdOriginalRunResumeAsync(fixture.CreateObservationChat(fresh), before.TaskId, before.ExecutionId, TestContext.Current.CancellationToken); });
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT claim_state FROM task_run_recovery_journal;", TestContext.Current.CancellationToken));
        Assert.Equal(before.PersistenceRevision, (await fixture.Rows.GetAsync(before.TaskId, TestContext.Current.CancellationToken))!.PersistenceRevision);
        Assert.Equal(0, fixture.Client.Dispatches);
        _ = await Record.ExceptionAsync(fresh.CloseAndSuspendOriginalProducersAsync);
    }

    [Fact]
    public async Task Cold_journal_refuses_group_readable_original_store_without_adopting_or_chmodding_it()
    {
        if (!OperatingSystem.IsLinux()) return;
        await using var fixture = await ColdPersonalFixture.CreateAsync(TestContext.Current.CancellationToken);
        var lease = await fixture.StartAsync(TestContext.Current.CancellationToken);
        await InitialHostedProducer(lease); await fixture.CaptureDriver(lease); await lease.DetachAndDrainAsync();
        var before = (await fixture.Rows.GetByContextAsync(fixture.Conversation.Id, TestContext.Current.CancellationToken))!;
        File.SetUnixFileMode(fixture.Paths.DatabasePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        try
        {
            var fresh = fixture.NewObservationCoordinator();
            _ = await Assert.ThrowsAnyAsync<Exception>(async () =>
            { _ = await fresh.ObserveOriginalColdInputAsync(before.TaskId, before.ExecutionId, TestContext.Current.CancellationToken); });
            Assert.True((File.GetUnixFileMode(fixture.Paths.DatabasePath) & UnixFileMode.GroupRead) != 0);
            Assert.Equal(0, fixture.Client.Dispatches);
            _ = await Record.ExceptionAsync(fresh.CloseAndSuspendOriginalProducersAsync);
        }
        finally { File.SetUnixFileMode(fixture.Paths.DatabasePath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    }

    [Fact]
    public async Task Cold_actual_configured_authority_refuses_same_kernel_activation_and_keeps_authentic_claim_sticky_without_replay()
    {
        if (!OperatingSystem.IsLinux()) return;
        await using var fixture = await ColdPersonalFixture.CreateAsync(TestContext.Current.CancellationToken);
        var initial = await fixture.StartAsync(TestContext.Current.CancellationToken);
        await InitialHostedProducer(initial); await fixture.CaptureDriver(initial); await initial.DetachAndDrainAsync();
        var before = (await fixture.Rows.GetByContextAsync(fixture.Conversation.Id, TestContext.Current.CancellationToken))!;
        var user = Assert.Single(await fixture.Conversations.GetMessagesAsync(fixture.Conversation.Id, TestContext.Current.CancellationToken));
        var fresh = fixture.CreateActualFreshColdGraph();
        Assert.True(fresh.Authority.HasOriginalColdRecoveryComposition(fresh.Journal, fresh.Journal));
        Assert.True(fresh.Tasks.HasOriginalColdRecoveryComposition(fresh.Journal, fresh.Journal));
        Assert.False(fresh.Tasks.HasOriginalColdRecoveryComposition(fixture.Journal, fixture.Journal));
        var currentActor = await fresh.Actors.GetCurrentAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before.OwnerBinding!.AuthenticationRevision, currentActor!.AuthenticationRevision);
        try
        {
            var acquisition = fresh.Tasks.StartObservedColdOriginalRunResumeAsync(fresh.Chat,
                before.TaskId, before.ExecutionId, TestContext.Current.CancellationToken);
            var observer = await acquisition;
            Assert.True(fresh.Tasks.IsIssuedOriginalRunResumeObservation(observer));
            var observation = observer.WaitAsync();
            var failure = await Record.ExceptionAsync(() => observation.WaitAsync(TestContext.Current.CancellationToken));
            Assert.NotNull(failure);
            Assert.Contains(Leaves(failure!), cause => cause is UnauthorizedAccessException &&
                cause.Message.Contains("fresh same-account/profile activation", StringComparison.Ordinal));
            Assert.True(observation.IsFaulted);
            // Actual native actor revision is process PID/start time, not a per-instance test epoch.
            // A second graph must not pretend this same process is a fresh authenticated activation.
            Assert.Equal(1L, await fixture.ScalarAsync("SELECT claim_state FROM task_run_recovery_journal;", TestContext.Current.CancellationToken));
            var after = (await fixture.Rows.GetAsync(before.TaskId, TestContext.Current.CancellationToken))!;
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(after));
            Assert.Equal(user, Assert.Single(await fixture.Conversations.GetMessagesAsync(fixture.Conversation.Id, TestContext.Current.CancellationToken)));
            Assert.Equal(0, fixture.Client.Dispatches); Assert.Equal(0, fixture.Provider.Starts);
            var secondRead = fresh.Tasks.ObserveOriginalColdInputAsync(before.TaskId, before.ExecutionId, TestContext.Current.CancellationToken);
            var refused = await Record.ExceptionAsync(() => secondRead);
            Assert.NotNull(refused); Assert.True(secondRead.IsFaulted);
            Assert.Contains(Leaves(refused!), cause => cause.Message.Contains("claim is retained/consumed", StringComparison.Ordinal));
            Assert.Equal(1L, await fixture.ScalarAsync("SELECT claim_state FROM task_run_recovery_journal;", TestContext.Current.CancellationToken));
            var detachDriver = observer.DetachAndDrainAsync();
            var detached = await Record.ExceptionAsync(() => detachDriver.WaitAsync(TestContext.Current.CancellationToken));
            Assert.NotNull(detached); Assert.True(detachDriver.IsFaulted);
            var originalActivationCause = Leaves(failure!).First(cause => cause is UnauthorizedAccessException &&
                cause.Message.Contains("fresh same-account/profile activation", StringComparison.Ordinal));
            Assert.Contains(Leaves(detached!), cause => ReferenceEquals(cause, originalActivationCause));
            Assert.Same(detachDriver, observer.DetachAndDrainAsync());
            // The actual observer joins its failed Wait; detach cannot erase that original cause.
            Assert.Equal(0, fixture.Client.Dispatches); Assert.Equal(0, fixture.Provider.Starts);
        }
        finally
        {
            fresh.Tasks.RequestOriginalProcessRetirement();
            _ = await Record.ExceptionAsync(fresh.Tasks.CloseAndSuspendOriginalProducersAsync);
            _ = await Record.ExceptionAsync(fresh.Authority.CloseAndDrainOwnerReauthenticationAsync);
            await fresh.Frames.CloseAndDrainAsync();
        }
    }

    [Fact]
    public async Task Cold_value_equal_actual_native_actor_instances_cannot_replace_same_configured_journal_source()
    {
        if (!OperatingSystem.IsLinux()) return;
        await using var fixture = await ColdPersonalFixture.CreateAsync(TestContext.Current.CancellationToken);
        var initial = await fixture.StartAsync(TestContext.Current.CancellationToken);
        await InitialHostedProducer(initial); await fixture.CaptureDriver(initial); await initial.DetachAndDrainAsync();
        var before = (await fixture.Rows.GetByContextAsync(fixture.Conversation.Id, TestContext.Current.CancellationToken))!;
        var foreign = new HostLocalTaskActorSource();
        Assert.Equal(await fixture.Actors.GetCurrentAsync(TestContext.Current.CancellationToken),
            await foreign.GetCurrentAsync(TestContext.Current.CancellationToken));
        Assert.True(fixture.Journal.HasOriginalTaskActorSource(fixture.Actors));
        Assert.False(fixture.Journal.HasOriginalTaskActorSource(foreign));
        var authority = new TaskRunPermissionAuthority(foreign, new ProviderRegistry(fixture.Provider),
            new Configurations(), new Privacy(), new(new Permissions()), cloud: new ControlledCloudAdmission());
        Assert.Throws<InvalidOperationException>(() => authority.ConfigureOriginalColdRecoverySources(fixture.Journal, fixture.Journal));
        Assert.False(authority.HasOriginalColdRecoveryComposition(fixture.Journal, fixture.Journal));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT claim_state FROM task_run_recovery_journal;", TestContext.Current.CancellationToken));
        Assert.Equal(before.PersistenceRevision, (await fixture.Rows.GetAsync(before.TaskId, TestContext.Current.CancellationToken))!.PersistenceRevision);
        Assert.Equal(0, fixture.Client.Dispatches); Assert.Equal(0, fixture.Provider.Starts);
        await authority.CloseAndDrainOwnerReauthenticationAsync();
    }

    [Fact]
    public async Task Cold_missing_original_authentication_key_refuses_without_regeneration_or_claim()
    {
        if (!OperatingSystem.IsLinux()) return;
        await using var fixture = await ColdPersonalFixture.CreateAsync(TestContext.Current.CancellationToken);
        var initial = await fixture.StartAsync(TestContext.Current.CancellationToken);
        await InitialHostedProducer(initial); await fixture.CaptureDriver(initial); await initial.DetachAndDrainAsync();
        var before = (await fixture.Rows.GetByContextAsync(fixture.Conversation.Id, TestContext.Current.CancellationToken))!;
        var originalKey = Path.Combine(fixture.Paths.DataDirectory, ".task-recovery-auth.v1");
        var retainedKey = originalKey + ".retained-control";
        File.Move(originalKey, retainedKey);
        var fresh = fixture.NewObservationCoordinator();
        try
        {
            var read = fresh.ObserveOriginalColdInputAsync(before.TaskId, before.ExecutionId, TestContext.Current.CancellationToken);
            var failure = await Record.ExceptionAsync(() => read);
            Assert.NotNull(failure); Assert.True(read.IsFaulted);
            Assert.Contains(Leaves(failure!), cause => cause is UnauthorizedAccessException && cause.Message.Contains("key is missing", StringComparison.Ordinal));
            Assert.False(File.Exists(originalKey)); Assert.True(File.Exists(retainedKey));
            Assert.Equal(0L, await fixture.ScalarAsync("SELECT claim_state FROM task_run_recovery_journal;", TestContext.Current.CancellationToken));
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before),
                System.Text.Json.JsonSerializer.Serialize(await fixture.Rows.GetAsync(before.TaskId, TestContext.Current.CancellationToken)));
            Assert.Equal(0, fixture.Client.Dispatches); Assert.Equal(0, fixture.Provider.Starts);
        }
        finally
        {
            File.Move(retainedKey, originalKey);
            _ = await Record.ExceptionAsync(fresh.CloseAndSuspendOriginalProducersAsync);
        }
    }

    private sealed class ColdPersonalFixture : IAsyncDisposable
    {
        internal readonly ColdPersonalPaths Paths = new();
        internal readonly HostLocalTaskActorSource Actors = new();
        internal readonly Provider Provider = new();
        internal readonly Events Events = new();
        internal readonly PermissionDecisionEngine Policy = new();
        internal readonly RemediationContinuationRegistry Registry = new();
        internal readonly RemediationRows RemediationRows = new();
        internal readonly Workspace Workspace = new();
        internal readonly ToolsOwner ToolsOwner = new();
        internal readonly SqliteDatabase Database;
        internal readonly TaskExecutionRepository Rows;
        internal readonly ConversationRepository Conversations;
        internal readonly TaskRunCentralCloudUsePermissionSource Source;
        internal readonly TaskRunPermissionAuthority Authority;
        internal readonly TaskRunOriginalFrameOwner Frames;
        internal readonly TaskExecutionCoordinator Tasks;
        internal readonly RemediationCoordinator Remediation;
        internal readonly TaskRunCloudPermissionRemediationOwner Owner;
        internal readonly Capture Capture;
        internal readonly Client Client;
        internal readonly ChatSessionService Service;
        internal readonly ProductionDiagnostics Diagnostics;
        internal readonly DatabaseMaintenanceService Maintenance;
        internal readonly SqliteTaskRunColdRecoveryJournal Journal;
        internal readonly Conversation Conversation = new(Guid.NewGuid(), HavenMode.Tasks, ConversationKind.Task,
            "genuine protected initial input", null, null, false, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        internal ColdPersonalFixture()
        {
            Database = new(Paths); Rows = new(Database); Conversations = new(Database);
            Diagnostics = new(Paths); Maintenance = new(Paths, Diagnostics);
            Source = new(Actors, Policy);
            Authority = new(Actors, new ProviderRegistry(Provider), new Configurations(), new Privacy(),
                new(new Permissions()), cloud: new ControlledCloudAdmission());
            TaskExecutionCoordinator? coordinator = null;
            Frames = new((task, run, attempt, token) => coordinator!.TryGetIssuedAttemptAsync(task, run, attempt, token));
            Tasks = coordinator = new(Rows, Events, admissionAuthority: Authority, runtimeSettlement: Frames);
            Remediation = new(RemediationRows, new Secrets(), Events, Registry);
            Owner = new(Source, Authority, () => Tasks, Remediation, RemediationRows, Registry, Events);
            Capture = new(Tasks, Authority, Source, Provider, Frames); Client = new(Capture);
            Service = new(Conversations, Client, new CapabilityPreflightService(), new Safety(),
                new WorkspaceToolRuntime(Workspace), new ComputerToolRuntime(new Computer()),
                taskCoordinator: Tasks, taskToolOwner: ToolsOwner, taskProviderContextCapture: Capture, taskCloudPermissionRemediation: Owner);
            Journal = new(Database, Paths, Actors, Maintenance, () => Service);
            Tasks.ConfigureOriginalColdRecovery(Journal, Journal);
        }
        internal static async Task<ColdPersonalFixture> CreateAsync(CancellationToken token)
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The protected personal-store fixture requires Linux.");
            var fixture = new ColdPersonalFixture();
            await fixture.Database.InitializeAsync(token);
            File.SetUnixFileMode(fixture.Paths.DatabasePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return fixture;
        }
        internal Task<TaskRunOriginalInitialChatObservationLease> StartAsync(CancellationToken token) =>
            Service.StartObservedOriginalTaskSendAsync(Conversation, "same initial hosted input",
                Provider.Model.Model with { Name = Provider.Model.Key, Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } },
                EffortLevel.Medium, [], "controlled", "", DuoMode.Solo, null, null, null, null, token);
        internal Task CaptureDriver(TaskRunOriginalInitialChatObservationLease lease)
        {
            var original = lease.GetType().GetField("Original", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lease)!;
            var table = typeof(ChatSessionService).GetField("_coldInitialCaptures", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Service)!;
            object?[] parameters = [original, null];
            Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, parameters)!);
            return Assert.IsAssignableFrom<Task>(parameters[1]!.GetType().GetField("Driver", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(parameters[1]));
        }
        internal ChatSessionService CreateObservationChat(TaskExecutionCoordinator sameCoordinator) =>
            new(Conversations, Client, new CapabilityPreflightService(), new Safety(),
                new WorkspaceToolRuntime(Workspace), new ComputerToolRuntime(new Computer()),
                taskCoordinator: sameCoordinator, taskToolOwner: ToolsOwner);
        internal TaskExecutionCoordinator NewObservationCoordinator()
        {
            var fresh = new TaskExecutionCoordinator(Rows, Events, admissionAuthority: Authority);
            var freshJournal = new SqliteTaskRunColdRecoveryJournal(Database, Paths, Actors, Maintenance, () => Service);
            Assert.True(freshJournal.HasOriginalComposition(Database, Paths, Actors));
            fresh.ConfigureOriginalColdRecovery(freshJournal, freshJournal);
            return fresh;
        }
        // A real new source graph on the SAME protected store, still in this actual process.
        // This cannot fabricate a new kernel activation or claim successful process restart.
        internal (HostLocalTaskActorSource Actors, TaskRunPermissionAuthority Authority,
            TaskRunOriginalFrameOwner Frames, TaskExecutionCoordinator Tasks, ChatSessionService Chat,
            SqliteTaskRunColdRecoveryJournal Journal) CreateActualFreshColdGraph()
        {
            var actors = new HostLocalTaskActorSource();
            var authority = new TaskRunPermissionAuthority(actors, new ProviderRegistry(Provider),
                new Configurations(), new Privacy(), new(new Permissions()), cloud: new ControlledCloudAdmission());
            TaskExecutionCoordinator? actual = null;
            var frames = new TaskRunOriginalFrameOwner((task, run, attempt, token) => actual!.TryGetIssuedAttemptAsync(task, run, attempt, token));
            var tasks = actual = new TaskExecutionCoordinator(Rows, Events, admissionAuthority: authority, runtimeSettlement: frames);
            var chat = CreateObservationChat(tasks);
            var journal = new SqliteTaskRunColdRecoveryJournal(Database, Paths, actors, Maintenance, () => chat);
            authority.ConfigureOriginalColdRecoverySources(journal, journal);
            tasks.ConfigureOriginalColdRecovery(journal, journal);
            return (actors, authority, frames, tasks, chat, journal);
        }
        internal async Task ExecuteAsync(string sql, CancellationToken token)
        { await using var connection = await Database.OpenAsync(token); await using var command = connection.CreateCommand(); command.CommandText = sql; _ = await command.ExecuteNonQueryAsync(token); }
        internal async Task<long> ScalarAsync(string sql, CancellationToken token)
        { await using var connection = await Database.OpenAsync(token); await using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt64(await command.ExecuteScalarAsync(token)); }
        public async ValueTask DisposeAsync()
        {
            var errors = new List<Exception>();
            try { Tasks.RequestOriginalProcessRetirement(); await Tasks.CloseAndSuspendOriginalProducersAsync(); } catch (Exception cause) { errors.Add(cause); }
            try { await Owner.CloseAndDrainAsync(); } catch (Exception cause) { errors.Add(cause); }
            try { await Frames.CloseAndDrainAsync(); } catch (Exception cause) { errors.Add(cause); }
            try { Remediation.Dispose(); } catch (Exception cause) { errors.Add(cause); }
            try { await Diagnostics.DisposeAsync(); } catch (Exception cause) { errors.Add(cause); }
            try { Directory.Delete(Paths.DataDirectory, true); } catch (Exception cause) { errors.Add(cause); }
            if (errors.Count > 0) throw new AggregateException("The actual protected-store fixture cleanup requires inspection.", errors);
        }
    }
    private sealed class ColdPersonalPaths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-real-cold-journal-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public ColdPersonalPaths()
        { Directory.CreateDirectory(DataDirectory); if (OperatingSystem.IsLinux()) File.SetUnixFileMode(DataDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }
}
