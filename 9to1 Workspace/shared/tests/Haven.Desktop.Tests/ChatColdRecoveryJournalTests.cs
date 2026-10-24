using System.Diagnostics;
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
    private const string ColdRestartDataDirectoryVariable = "NINETOONE_CHAT_COLD_RESTART_DATA_DIRECTORY";
    private const string ColdRestartParentProcessVariable = "NINETOONE_CHAT_COLD_RESTART_PARENT_PROCESS";

    [Fact]
    public async Task Cold_journal_real_process_restart_resumes_same_task_without_duplicating_accepted_input()
    {
        if (Environment.GetEnvironmentVariable(ColdRestartDataDirectoryVariable) is { Length: > 0 } childDirectory)
        {
            await ResumeColdTaskInChildProcessAsync(childDirectory, TestContext.Current.CancellationToken);
            return;
        }
        if (!OperatingSystem.IsLinux()) return;

        await using var fixture = await ColdPersonalFixture.CreateAsync(TestContext.Current.CancellationToken);
        var original = await fixture.StartAsync(TestContext.Current.CancellationToken);
        await InitialHostedProducer(original);
        await fixture.CaptureDriver(original);
        await original.DetachAndDrainAsync();
        var before = (await fixture.Rows.GetByContextAsync(fixture.Conversation.Id,
            TestContext.Current.CancellationToken)) ?? throw new InvalidOperationException("The initial actual Task row was not persisted.");
        var acceptedUser = Assert.Single(await fixture.Conversations.GetMessagesAsync(fixture.Conversation.Id,
            TestContext.Current.CancellationToken));
        Assert.Equal(fixture.Conversation.Id, before.ContextId);
        Assert.Equal(fixture.Conversation.Id, acceptedUser.ConversationId);
        Assert.Equal(MessageRole.User, acceptedUser.Role);
        // The authenticated NeverStartedAcceptedInput capsule forbids prior attempts,
        // plans or completed effects; the child must preserve that exact empty history.
        Assert.Empty(before.Attempts);
        Assert.Empty(before.Plan);
        Assert.Equal(TaskRunOriginalSettlementOutcome.NoAttemptAdmissionWasInvoked,
            before.RecoveryObservation?.SettlementOutcome);
        var parentActor = (await fixture.Actors.GetCurrentAsync(TestContext.Current.CancellationToken))
            ?? throw new UnauthorizedAccessException("The original fixture's actual native Task actor is unavailable.");
        Assert.Equal(before.OwnerBinding!.ActorId, parentActor.ActorId);
        Assert.Equal(before.OwnerBinding.ProfileId, parentActor.ProfileId);
        Assert.Equal(before.OwnerBinding.AuthenticationRevision, parentActor.AuthenticationRevision);

        // Seal and drain the actual process owner before another process opens the SAME
        // native-protected path. No second in-process coordinator is used as the restart.
        await fixture.Tasks.CloseAndSuspendOriginalProducersAsync();
        await fixture.Owner.CloseAndDrainAsync();
        await fixture.Authority.CloseAndDrainOwnerReauthenticationAsync();
        await fixture.Frames.CloseAndDrainAsync();
        fixture.Remediation.Dispose();
        var output = await RunColdResumeTestHostProcessAsync(fixture.Paths.DataDirectory,
            Environment.ProcessId, TestContext.Current.CancellationToken);
        Assert.True(output.ExitCode == 0,
            "The separate cold-resume test host failed. stdout:\n" + output.StandardOutput + "\nstderr:\n" + output.StandardError);

        var after = (await fixture.Rows.GetAsync(before.TaskId, TestContext.Current.CancellationToken))
            ?? throw new InvalidOperationException("The child process did not leave a canonical Task row.");
        Assert.Equal(before.TaskId, after.TaskId);
        Assert.Equal(before.ContextId, after.ContextId);
        Assert.Equal(before.ExecutionId, after.ExecutionId);
        Assert.Equal(before.OwnerBinding!.ActorId, after.OwnerBinding!.ActorId);
        Assert.Equal(before.OwnerBinding.ProfileId, after.OwnerBinding.ProfileId);
        Assert.Equal(before.OwnerBinding.AccountId, after.OwnerBinding.AccountId);
        Assert.Equal(before.OwnerBinding.OrganisationId, after.OwnerBinding.OrganisationId);
        Assert.NotEqual(before.OwnerBinding.AuthenticationRevision, after.OwnerBinding.AuthenticationRevision);
        Assert.True(after.PersistenceRevision > before.PersistenceRevision);
        Assert.Equal(TaskExecutionLifecycle.Suspended, after.State);
        Assert.Equal(before.Attempts.Select(attempt => attempt.Id), after.Attempts.Select(attempt => attempt.Id));
        Assert.Equal(before.Plan.Select(action => action.ActionId), after.Plan.Select(action => action.ActionId));
        Assert.Empty(before.Plan.Where(action => action.State == TaskPlanNodeState.Completed));
        Assert.Equal(before.RecoveryObservation!.ObservationId,
            Assert.Single(after.RecoveryHistory).ObservationId);
        var afterUsers = await fixture.Conversations.GetMessagesAsync(fixture.Conversation.Id,
            TestContext.Current.CancellationToken);
        Assert.Equal([acceptedUser], afterUsers);
        Assert.Equal(3L, await fixture.ScalarAsync(
            "SELECT claim_state FROM task_run_recovery_journal WHERE task_id='" + before.TaskId + "';",
            TestContext.Current.CancellationToken));
    }

    private static async Task ResumeColdTaskInChildProcessAsync(string dataDirectory, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The protected cold Task source is Linux-only.");
        var parentProcess = int.Parse(Environment.GetEnvironmentVariable(ColdRestartParentProcessVariable)
            ?? throw new InvalidOperationException("The actual parent-process identity was not supplied."),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.NotEqual(parentProcess, Environment.ProcessId);

        await using var fixture = await ColdPersonalFixture.OpenExistingAsync(dataDirectory, token);
        var before = Assert.Single(await fixture.Rows.GetResumableAsync(token));
        var savedActor = before.OwnerBinding ?? throw new UnauthorizedAccessException("The saved Task has no actual owner binding.");
        var actualActor = (await fixture.Actors.GetCurrentAsync(token))
            ?? throw new UnauthorizedAccessException("The restarted process has no actual native Task actor.");
        Assert.Equal(savedActor.ActorId, actualActor.ActorId);
        Assert.Equal(savedActor.ProfileId, actualActor.ProfileId);
        Assert.Equal(savedActor.AccountId, actualActor.AccountId);
        Assert.Equal(savedActor.OrganisationId, actualActor.OrganisationId);
        Assert.NotEqual(savedActor.AuthenticationRevision, actualActor.AuthenticationRevision);

        var acceptedConversation = (await fixture.Conversations.GetAsync(before.ContextId, token))
            ?? throw new InvalidOperationException("The accepted Task conversation is unavailable after process restart.");
        var acceptedUser = Assert.Single(await fixture.Conversations.GetMessagesAsync(before.ContextId, token));
        Assert.Equal(before.ContextId, acceptedConversation.Id);
        Assert.Equal(MessageRole.User, acceptedUser.Role);
        Assert.Equal(before.ContextId, acceptedUser.ConversationId);
        var capsule = await fixture.Tasks.ObserveOriginalColdInputAsync(before.TaskId, before.ExecutionId, token)
            ?? throw new InvalidOperationException("The same protected journal did not issue a cold input after process restart.");
        Assert.Equal(TaskRunColdBoundaryKind.NeverStartedAcceptedInput, capsule.Boundary);
        Assert.Equal(before.TaskId, capsule.AcknowledgedTask.TaskId);
        Assert.Equal(before.ExecutionId, capsule.AcknowledgedTask.ExecutionId);
        Assert.Equal(acceptedUser, capsule.AcceptedUserMessage);

        var availability = await fixture.Tasks.GetOriginalRunControlAvailabilityAsync(before.TaskId, before.ExecutionId, token);
        Assert.False(availability.CanPause);
        Assert.False(availability.CanStop);
        Assert.False(availability.CanResumeUnstartedOriginal);
        Assert.False(availability.CanResumeOriginalToolCheckpoint);
        Assert.True(availability.CanResumeColdOriginal);
        Assert.Equal(0L, await fixture.ScalarAsync(
            "SELECT claim_state FROM task_run_recovery_journal WHERE task_id='" + before.TaskId + "';", token));

        // The retained-live command guard remains strict even though the cold-only
        // availability entry above succeeds for this genuine new process activation.
        var liveCommandFailure = await Record.ExceptionAsync(() =>
            fixture.Authority.ValidateTaskCommandAsync(before, "task:pause-original", token));
        Assert.NotNull(liveCommandFailure);
        Assert.Contains(Leaves(liveCommandFailure!), cause => cause is UnauthorizedAccessException &&
            cause.Message.Contains("actor/revision is no longer current", StringComparison.Ordinal));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before),
            System.Text.Json.JsonSerializer.Serialize(await fixture.Rows.GetAsync(before.TaskId, token)));
        Assert.Equal(0L, await fixture.ScalarAsync(
            "SELECT claim_state FROM task_run_recovery_journal WHERE task_id='" + before.TaskId + "';", token));

        var observation = await fixture.Tasks.StartObservedOriginalRunResumeAsync(
            before.TaskId, before.ExecutionId, token);
        var terminal = await observation.WaitAsync().WaitAsync(TimeSpan.FromSeconds(90), token);
        Assert.Equal(TaskRunOriginalResumeObservationDisposition.ProducerTerminal, terminal.Disposition);
        Assert.True(terminal.WasColdRecovery);
        Assert.Equal(before.TaskId, terminal.CanonicalTaskContext?.TaskId);
        Assert.Equal(before.ExecutionId, terminal.CanonicalTaskContext?.ExecutionId);
        Assert.Equal(TaskExecutionLifecycle.Suspended, terminal.State);
        var currentPermission = Assert.Single(fixture.RemediationRows.Rows.Values);
        Assert.Equal(before.ExecutionId, currentPermission.ExecutionId);
        Assert.Equal(RemediationType.PermissionRequest, currentPermission.Type);
        Assert.Equal(RemediationState.Waiting, currentPermission.State);
        var denied = await fixture.Owner.DenyOriginalAsync(currentPermission.Id, token);
        Assert.Equal(PermissionDecisionKind.Denied, denied.Kind);
        Assert.Equal(0, fixture.Client.Dispatches);
        Assert.Equal(0, fixture.Provider.Starts);
        Assert.Equal(0, fixture.Workspace.Effects);

        var after = (await fixture.Rows.GetAsync(before.TaskId, token))
            ?? throw new InvalidOperationException("The resumed Task row disappeared.");
        Assert.Equal(before.TaskId, after.TaskId);
        Assert.Equal(before.ContextId, after.ContextId);
        Assert.Equal(before.ExecutionId, after.ExecutionId);
        Assert.Equal(savedActor.ActorId, after.OwnerBinding!.ActorId);
        Assert.Equal(savedActor.ProfileId, after.OwnerBinding.ProfileId);
        Assert.NotEqual(savedActor.AuthenticationRevision, after.OwnerBinding.AuthenticationRevision);
        Assert.Equal(before.Attempts.Select(attempt => attempt.Id), after.Attempts.Select(attempt => attempt.Id));
        Assert.Equal(before.Plan.Select(action => action.ActionId), after.Plan.Select(action => action.ActionId));
        Assert.Equal(before.RecoveryObservation!.ObservationId, Assert.Single(after.RecoveryHistory).ObservationId);
        Assert.Equal([acceptedUser], await fixture.Conversations.GetMessagesAsync(before.ContextId, token));
    }

    private static async Task<ChildTestProcessResult> RunColdResumeTestHostProcessAsync(
        string dataDirectory, int parentProcessId, CancellationToken cancellationToken)
    {
        var project = FindDesktopTestProject();
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrWhiteSpace(host)) host = "dotnet";
        var start = new ProcessStartInfo(host)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(project)!
        };
        start.ArgumentList.Add("test");
        start.ArgumentList.Add(project);
        start.ArgumentList.Add("--framework");
        start.ArgumentList.Add("net10.0");
        start.ArgumentList.Add("--configuration");
        start.ArgumentList.Add(FindBuildConfiguration());
        start.ArgumentList.Add("--no-build");
        start.ArgumentList.Add("--no-restore");
        start.ArgumentList.Add("--filter");
        start.ArgumentList.Add("FullyQualifiedName=Haven.Desktop.Tests.ChatCloudPermissionCallerTests.Cold_journal_real_process_restart_resumes_same_task_without_duplicating_accepted_input");
        start.Environment[ColdRestartDataDirectoryVariable] = dataDirectory;
        start.Environment[ColdRestartParentProcessVariable] = parentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        using var child = Process.Start(start)
            ?? throw new InvalidOperationException("The separate test-host process could not be started.");
        var standardOutput = child.StandardOutput.ReadToEndAsync();
        var standardError = child.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try { await child.WaitForExitAsync(timeout.Token); }
        catch
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        return new(child.ExitCode, await standardOutput, await standardError);
    }

    private static string FindDesktopTestProject()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Haven.Desktop.Tests.csproj");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("The owning Haven.Desktop.Tests project could not be located for the separate-process regression.");
    }

    private static string FindBuildConfiguration() =>
        new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name is { Length: > 0 } configuration
            ? configuration : "Debug";

    private sealed record ChildTestProcessResult(int ExitCode, string StandardOutput, string StandardError);

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
        internal readonly ColdPersonalPaths Paths;
        private readonly bool _ownsDataDirectory;
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
        internal ColdPersonalFixture(string? existingDataDirectory = null, bool ownsDataDirectory = true)
        {
            Paths = new(existingDataDirectory);
            _ownsDataDirectory = ownsDataDirectory;
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
            Authority.ConfigureOriginalColdRecoverySources(Journal, Journal);
            Tasks.ConfigureOriginalColdRecovery(Journal, Journal, () => Service);
        }
        internal static async Task<ColdPersonalFixture> CreateAsync(CancellationToken token)
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The protected personal-store fixture requires Linux.");
            var fixture = new ColdPersonalFixture();
            await fixture.Database.InitializeAsync(token);
            File.SetUnixFileMode(fixture.Paths.DatabasePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return fixture;
        }
        internal static async Task<ColdPersonalFixture> OpenExistingAsync(string dataDirectory, CancellationToken token)
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The protected personal-store fixture requires Linux.");
            if (!Directory.Exists(dataDirectory)) throw new DirectoryNotFoundException("The original protected Task store is unavailable.");
            if (!File.Exists(Path.Combine(dataDirectory, "haven.db"))
                || !File.Exists(Path.Combine(dataDirectory, ".task-recovery-auth.v1")))
                throw new FileNotFoundException("The original SQLite database and private recovery key are both required for process-restart coverage.");
            var fixture = new ColdPersonalFixture(dataDirectory, ownsDataDirectory: false);
            try
            {
                await fixture.Database.InitializeAsync(token);
                return fixture;
            }
            catch (Exception failure)
            {
                try { await fixture.DisposeAsync(); }
                catch (Exception cleanupFailure)
                { throw new AggregateException("Reopening the original protected fixture and its cleanup both failed.", failure, cleanupFailure); }
                throw;
            }
        }
        internal Task<TaskRunOriginalInitialChatObservationLease> StartAsync(CancellationToken token, GenerationOptions? generationOptions = null) =>
            Service.StartObservedOriginalTaskSendAsync(Conversation, "same initial hosted input",
                Provider.Model.Model with { Name = Provider.Model.Key, Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } },
                EffortLevel.Medium, [], "controlled", "", DuoMode.Solo, null, null, null, null, token, generationOptions: generationOptions);
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
            try { await Authority.CloseAndDrainOwnerReauthenticationAsync(); } catch (Exception cause) { errors.Add(cause); }
            try { await Frames.CloseAndDrainAsync(); } catch (Exception cause) { errors.Add(cause); }
            try { Remediation.Dispose(); } catch (Exception cause) { errors.Add(cause); }
            try { await Diagnostics.DisposeAsync(); } catch (Exception cause) { errors.Add(cause); }
            if (_ownsDataDirectory)
                try { Directory.Delete(Paths.DataDirectory, true); } catch (Exception cause) { errors.Add(cause); }
            if (errors.Count > 0) throw new AggregateException("The actual protected-store fixture cleanup requires inspection.", errors);
        }
    }
    private sealed class ColdPersonalPaths : IAppPaths
    {
        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public ColdPersonalPaths(string? existingDataDirectory = null)
        {
            DataDirectory = existingDataDirectory is null
                ? Path.Combine(Path.GetTempPath(), "astra-real-cold-journal-" + Guid.NewGuid().ToString("N"))
                : Path.GetFullPath(existingDataDirectory);
            if (existingDataDirectory is null)
            {
                Directory.CreateDirectory(DataDirectory);
                if (OperatingSystem.IsLinux())
                    File.SetUnixFileMode(DataDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            else if (!Directory.Exists(DataDirectory))
                throw new DirectoryNotFoundException("The original protected Task data directory is unavailable.");
        }
    }
}
