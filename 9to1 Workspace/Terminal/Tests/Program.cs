/*
 * FILE DOCUMENTATION
 * Where: HavenOS Apps/Terminal/Tests/Program.cs.
 * What: Focused executable specifications for Terminal app host gating, permission approval, persistent sessions, cwd, and restart behavior.
 * Why: The migration must prove it reuses the shared terminal contracts and fails closed without a host execution capability.
 */
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Haven.Infrastructure.Terminal;
using HavenOS.Apps.Terminal;

try
{
    if (args.Length == 1 && string.Equals(args[0], "--pty-output-buffer-only", StringComparison.Ordinal))
    {
        PtyProcessSpecs.RunOutputBufferSpec();
        Console.WriteLine("PTY output buffer spec passed.");
        return;
    }

    if (args.Length == 1 && string.Equals(args[0], "--pty-delayed-output-only", StringComparison.Ordinal))
    {
        await PtyProcessSpecs.RunDelayedOutputSpecAsync();
        Console.WriteLine("PTY delayed output spec passed.");
        return;
    }

    if (args.Length == 1 && string.Equals(args[0], "--pty-working-directory-only", StringComparison.Ordinal))
    {
        await PtyProcessSpecs.RunWorkingDirectorySpecAsync();
        Console.WriteLine("PTY working-directory, resize, exit, and disposal spec passed.");
        return;
    }

    await TerminalAppSurfaceSpecs.RunAsync();
    await PtyProcessSpecs.RunAsync();
    Console.WriteLine("Terminal specs passed.");
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}

internal static class TerminalAppSurfaceSpecs
{
    public static async Task RunAsync()
    {
        await MissingSessionCapabilityFailsClosedAsync();
        await MissingPermissionCapabilityFailsClosedAsync();
        await AskPermissionRequiresApprovalWithoutExecutingAsync();
        await CommandTextIsPreservedForNativeShellExecutionAsync();
        await ApprovalUsesTheSamePersistentSessionAsync();
        await ChangedTargetsInvalidateApprovalAsync();
        await StaleApprovalCannotApproveANewerCommandAsync();
        await ApprovalRechecksPermissionAndReadinessAsync();
        await SurfaceOperationsDoNotQueueBehindRunningCommandsAsync();
        await WorkingDirectoryAndNewSessionUseHostSessionContractAsync();
        await FailedReplacementPreservesHealthySessionAsync();
        await RealShellRejectsStaleDirectoryApprovalAsync();
        CreationFailureFailsClosedAndRedactsReason();
    }

    private static async Task MissingSessionCapabilityFailsClosedAsync()
    {
        using var surface = new TerminalAppSurface(new(null, () => PermissionMode.FullAccess));
        Check(surface.Availability == TerminalAppAvailability.HostCapabilityUnavailable, "missing session factory must be unavailable");
        var result = await surface.SubmitAsync("echo blocked");
        Check(result.State == TerminalAppCommandState.Unavailable, "missing session factory must not execute");
    }

    private static async Task MissingPermissionCapabilityFailsClosedAsync()
    {
        var factory = new FakeSessionFactory();
        using var surface = new TerminalAppSurface(new(factory, null));
        var result = await surface.SubmitAsync("echo blocked");
        Check(result.State == TerminalAppCommandState.Unavailable, "missing permission source must not execute");
        Check(factory.CreateCount == 0, "surface must not create a shell when a required host capability is missing");
    }

    private static async Task AskPermissionRequiresApprovalWithoutExecutingAsync()
    {
        var factory = new FakeSessionFactory();
        using var surface = new TerminalAppSurface(new(factory, () => PermissionMode.Ask));
        var result = await surface.SubmitAsync("echo hello token=super-secret");
        Check(result.State == TerminalAppCommandState.RequiresApproval, "Ask must require one-time approval");
        Check(result.Approval is not null && result.Approval.Command == "echo hello token=super-secret",
            "review must carry the exact command, not its redacted history representation");
        Check(result.Approval!.Target.SessionId == factory.LastSession!.Metadata.SessionId,
            "review must name the actual target session");
        Check(factory.LastSession!.ExecuteCount == 0, "approval gate must run before shell execution");
        Check(surface.History.Count == 1, "submitted command should enter visible history");
        Check(surface.History[0].Contains("<redacted>", StringComparison.Ordinal), "visible history must redact secrets");
        Check(!surface.History[0].Contains("super-secret", StringComparison.Ordinal), "visible history must not retain raw secrets");
    }

    private static async Task ApprovalUsesTheSamePersistentSessionAsync()
    {
        var factory = new FakeSessionFactory();
        using var surface = new TerminalAppSurface(new(factory, () => PermissionMode.Ask));
        var first = await surface.SubmitAsync("set-state one");
        Check(first.State == TerminalAppCommandState.RequiresApproval, "first command should await approval");
        var approved = await surface.ApprovePendingAsync(first.Approval!.Id);
        Check(approved.State == TerminalAppCommandState.Succeeded, "approved command should execute");

        var second = await surface.SubmitAsync("set-state two");
        Check(second.State == TerminalAppCommandState.RequiresApproval, "second command should independently await approval");
        var approvedSecond = await surface.ApprovePendingAsync(second.Approval!.Id);
        Check(approvedSecond.State == TerminalAppCommandState.Succeeded, "second approved command should execute");
        Check(factory.CreateCount == 1, "multiple commands must reuse one persistent host session");
        Check(factory.LastSession!.ExecuteCount == 2, "both commands must execute through that session");
    }

    private static async Task ChangedTargetsInvalidateApprovalAsync()
    {
        var changes = new Func<TerminalSessionMetadata, TerminalSessionMetadata>[]
        {
            m => m with { CurrentWorkingDirectory = m.CurrentWorkingDirectory + "-other" },
            m => m with { SessionId = Guid.NewGuid() },
            m => m with { Generation = m.Generation + 1 },
            m => m with { Revision = m.Revision + 1 },
            m => m with { ShellRuntime = "other-shell" },
            m => m with { ShellProfileId = "other-profile" },
            m => m with { EnvironmentId = new TerminalEnvironmentId("other-environment") },
            m => m with { IsElevated = !m.IsElevated },
            m => m with { Mode = TerminalSessionMode.Ai },
            m => m with { State = TerminalSessionLifecycleState.Running }
        };

        foreach (var change in changes)
        {
            var factory = new FakeSessionFactory();
            using var surface = new TerminalAppSurface(new(factory, () => PermissionMode.Ask));
            var session = factory.LastSession!;
            var original = session.Metadata;
            var review = (await surface.SubmitAsync("echo reviewed")).Approval!;
            session.UpdateMetadata(change(original));
            session.UpdateMetadata(original); // Returning to the old value must not revive a review.
            var result = await surface.ApprovePendingAsync(review.Id);
            Check(result.State == TerminalAppCommandState.Denied, "a changed target must invalidate its review");
            Check(session.ExecuteCount == 0, "a stale review must never reach the shell");
        }

        var directoryFactory = new FakeSessionFactory();
        using var directorySurface = new TerminalAppSurface(new(directoryFactory, () => PermissionMode.Ask));
        var pending = (await directorySurface.SubmitAsync("echo directory")).Approval!;
        Check(await directorySurface.SetWorkingDirectoryAsync(Environment.CurrentDirectory), "working directory update should reach the host");
        Check((await directorySurface.ApprovePendingAsync(pending.Id)).State == TerminalAppCommandState.Denied,
            "a working-directory operation must invalidate a pending review");
        Check(directoryFactory.LastSession!.ExecuteCount == 0, "invalidated directory review must not execute");
    }

    private static async Task StaleApprovalCannotApproveANewerCommandAsync()
    {
        var factory = new FakeSessionFactory();
        using var surface = new TerminalAppSurface(new(factory, () => PermissionMode.Ask));
        var first = (await surface.SubmitAsync("echo first")).Approval!;
        Check((await surface.SubmitAsync("echo replacement")).State == TerminalAppCommandState.Failed,
            "another submission must not silently replace a pending review");
        Check(surface.DenyPending(first.Id).State == TerminalAppCommandState.Denied, "the reviewed command can be denied");
        var second = (await surface.SubmitAsync("echo second")).Approval!;
        Check((await surface.ApprovePendingAsync(first.Id)).State == TerminalAppCommandState.Failed,
            "a stale approval must not approve a newer command");
        Check(surface.DenyPending(first.Id).State == TerminalAppCommandState.Failed,
            "a stale denial must not discard a newer review");
        Check(surface.HasPendingApproval, "the newer review must remain pending");
        Check((await surface.ApprovePendingAsync(second.Id)).State == TerminalAppCommandState.Succeeded,
            "the matching review may run once");
        Check((await surface.ApprovePendingAsync(second.Id)).State == TerminalAppCommandState.Failed,
            "repeated activation must not rerun the command");
        Check(factory.LastSession!.ExecuteCount == 1 && factory.LastSession.LastCommand == "echo second",
            "only the explicitly approved command should execute");
    }

    private static async Task ApprovalRechecksPermissionAndReadinessAsync()
    {
        var permission = PermissionMode.Ask;
        var factory = new FakeSessionFactory();
        using var surface = new TerminalAppSurface(new(factory, () => permission));
        var review = (await surface.SubmitAsync("echo blocked")).Approval!;
        permission = (PermissionMode)999;
        Check((await surface.ApprovePendingAsync(review.Id)).State == TerminalAppCommandState.Denied,
            "a revoked or unknown permission must not be overridden by one-time approval");
        Check(factory.LastSession!.ExecuteCount == 0, "revoked permission must prevent execution");

        permission = PermissionMode.Ask;
        factory.LastSession.UpdateMetadata(factory.LastSession.Metadata with { CurrentWorkingDirectory = null });
        Check((await surface.SubmitAsync("echo unknown-target")).State == TerminalAppCommandState.Unavailable,
            "an unknown working directory cannot be reviewed");
        factory.LastSession.UpdateMetadata(factory.LastSession.Metadata with { State = TerminalSessionLifecycleState.Ended });
        Check((await surface.SubmitAsync("echo ended")).State == TerminalAppCommandState.Unavailable,
            "an ended shell cannot be reviewed");
    }

    private static async Task SurfaceOperationsDoNotQueueBehindRunningCommandsAsync()
    {
        var factory = new FakeSessionFactory();
        using var surface = new TerminalAppSurface(new(factory, () => PermissionMode.FullAccess));
        var session = factory.LastSession!;
        session.ExecutionHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = surface.SubmitAsync("echo running");
        Check(session.ExecuteCount == 1, "the first command must have reached the session");
        Check((await surface.SubmitAsync("echo queued")).State == TerminalAppCommandState.Failed,
            "another command must not silently queue behind a running command");
        Check(!await surface.SetWorkingDirectoryAsync(Environment.CurrentDirectory),
            "a directory change must not silently queue behind a running command");
        Check(!surface.NewSession(), "a new session must not dispose a running surface operation");
        session.ExecutionHold.SetResult(true);
        Check((await running).State == TerminalAppCommandState.Succeeded, "the original command should complete");
        Check(session.ExecuteCount == 1, "blocked operations must not reach the shell later");
    }

    private static async Task RealShellRejectsStaleDirectoryApprovalAsync()
    {
        if (!OperatingSystem.IsWindows() && !(Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator).Any(path => File.Exists(Path.Combine(path, "pwsh"))))
        {
            Console.WriteLine("SKIP real PowerShell review spec: pwsh is unavailable.");
            return;
        }

        var tempBase = Directory.Exists("/tmp/opencode") ? "/tmp/opencode" : Path.GetTempPath();
        var root = Path.Combine(tempBase, "terminal-review-spec-" + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(destination);
        try
        {
            using var surface = new TerminalAppSurface(new(new PowerShellTerminalSessionFactory(), () => PermissionMode.Ask), root);
            Check(surface.IsAvailable, "real PowerShell session must start");
            var output = new System.Collections.Concurrent.ConcurrentQueue<string>();
            surface.OutputReceived += (_, value) => output.Enqueue(value.Text);
            var first = (await surface.SubmitAsync("Set-Variable -Name HavenReviewBypass -Value 1")).Approval!;
            Check(await surface.SetWorkingDirectoryAsync(destination), "real shell must change its working directory");
            Check((await surface.ApprovePendingAsync(first.Id)).State == TerminalAppCommandState.Denied,
                "real shell must reject approval for the old working directory");
            var second = (await surface.SubmitAsync(
                "if (Get-Variable HavenReviewBypass -ErrorAction SilentlyContinue) { 'REVIEW_BYPASS' } else { 'REVIEW_OK' }; (Get-Location).Path")).Approval!;
            Check(second.Target.CurrentWorkingDirectory == destination, "fresh review must name the real new directory");
            Check((await surface.ApprovePendingAsync(second.Id)).State == TerminalAppCommandState.Succeeded,
                "fresh review should execute through the real shell");
            Check(output.Any(line => line.Contains("REVIEW_OK", StringComparison.Ordinal)),
                "real shell output must prove the rejected command did not set its variable");
            Check(output.Any(line => line.Contains(destination, StringComparison.Ordinal)),
                "real shell output must confirm the working directory");
            Console.WriteLine("Real PowerShell review spec passed.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CommandTextIsPreservedForNativeShellExecutionAsync()
    {
        var factory = new FakeSessionFactory();
        using var surface = new TerminalAppSurface(new(factory, () => PermissionMode.FullAccess));
        const string command = "  $value = @'\nline one\nline two\n'@; Write-Output $value  ";

        var result = await surface.SubmitAsync(command);

        Check(result.State == TerminalAppCommandState.Succeeded, "nonblank native command should execute");
        Check(factory.LastSession!.LastCommand == command, "shell command text must reach the host session byte-for-byte as submitted");
        Check(surface.History[^1] == command, "command history must preserve non-secret whitespace and multiline input");
    }

    private static async Task WorkingDirectoryAndNewSessionUseHostSessionContractAsync()
    {
        var factory = new FakeSessionFactory();
        using var surface = new TerminalAppSurface(new(factory, () => PermissionMode.FullAccess));
        var expected = Path.GetFullPath(Environment.CurrentDirectory);
        Check(await surface.SetWorkingDirectoryAsync(expected), "existing working directory should be accepted");
        Check(surface.WorkingDirectory == expected, "working directory must come from host session metadata");

        var priorId = surface.SessionMetadata!.SessionId;
        Check(surface.NewSession(), "new session should be created through host factory");
        Check(factory.CreateCount == 2, "new-session action must request a second host session");
        Check(surface.SessionMetadata!.SessionId != priorId, "new-session action must replace the prior session");
    }

    private static void CreationFailureFailsClosedAndRedactsReason()
    {
        using var surface = new TerminalAppSurface(new(new ThrowingSessionFactory(), () => PermissionMode.FullAccess));
        Check(surface.Availability == TerminalAppAvailability.HostCapabilityUnavailable, "host creation failure must fail closed");
        Check(surface.UnavailableReason!.Contains("<redacted>", StringComparison.Ordinal), "host failure reason should be redacted");
        Check(!surface.UnavailableReason.Contains("secret-value", StringComparison.Ordinal), "raw secret from host failure must not reach UI state");
    }

    private static async Task FailedReplacementPreservesHealthySessionAsync()
    {
        var factory = new FakeSessionFactory();
        using var surface = new TerminalAppSurface(new(factory, () => PermissionMode.FullAccess));
        var original = factory.LastSession!;
        factory.FailNextCreate = true;

        Check(!surface.NewSession(), "failed replacement should report failure");
        Check(surface.IsAvailable, "failed replacement must leave the existing session available");
        Check(surface.SessionMetadata?.SessionId == original.Metadata.SessionId,
            "failed replacement must retain the existing session metadata");
        Check(original.Metadata.State != TerminalSessionLifecycleState.Disposed,
            "failed replacement must not dispose the existing session");
        var result = await surface.SubmitAsync("echo still-working");
        Check(result.State == TerminalAppCommandState.Succeeded,
            "the existing session must continue accepting commands after replacement failure");
        Check(original.ExecuteCount == 1, "commands after replacement failure must use the original session");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Spec failed: " + message);
    }
}

internal sealed class FakeSessionFactory : ITerminalSessionFactory
{
    public int CreateCount { get; private set; }
    public FakeSession? LastSession { get; private set; }
    public bool FailNextCreate { get; set; }

    public ITerminalSession Create(string initialDirectory, string? displayName = null)
    {
        CreateCount++;
        if (FailNextCreate)
        {
            FailNextCreate = false;
            throw new InvalidOperationException("replacement creation failed");
        }
        LastSession = new FakeSession(initialDirectory, displayName ?? "Terminal");
        return LastSession;
    }
}

internal sealed class ThrowingSessionFactory : ITerminalSessionFactory
{
    public ITerminalSession Create(string initialDirectory, string? displayName = null) =>
        throw new InvalidOperationException("token=secret-value");
}

internal sealed class FakeSession : ITerminalSession
{
    private TerminalSessionMetadata _metadata;

    public FakeSession(string initialDirectory, string displayName)
    {
        _metadata = new(
            Guid.NewGuid(),
            "fake-shell",
            displayName,
            initialDirectory,
            initialDirectory,
            TerminalSessionLifecycleState.Ready,
            DateTimeOffset.UtcNow,
            0,
            false);
    }

    public int ExecuteCount { get; private set; }
    public string? LastCommand { get; private set; }
    public TaskCompletionSource<bool>? ExecutionHold { get; set; }
    public TerminalSessionMetadata Metadata => _metadata;
    public int? ProcessId => 1234;
    public event EventHandler<TerminalSessionOutput>? OutputReceived;
    public event EventHandler<TerminalSessionMetadata>? MetadataChanged;

    public async Task<TerminalSessionCommandResult> ExecuteAsync(Guid commandId, string command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExecuteCount++;
        LastCommand = command;
        _metadata = _metadata with { State = TerminalSessionLifecycleState.Running };
        MetadataChanged?.Invoke(this, _metadata);
        if (ExecutionHold is not null)
            await ExecutionHold.Task.WaitAsync(cancellationToken);
        OutputReceived?.Invoke(this, new(_metadata.SessionId, commandId, TerminalOutputStream.StandardOutput, command, DateTimeOffset.UtcNow));
        _metadata = _metadata with { State = TerminalSessionLifecycleState.Ready };
        MetadataChanged?.Invoke(this, _metadata);
        return new TerminalSessionCommandResult(
            _metadata.SessionId,
            commandId,
            0,
            false,
            TimeSpan.FromMilliseconds(1),
            _metadata.CurrentWorkingDirectory,
            true,
            false);
    }

    public void UpdateMetadata(TerminalSessionMetadata metadata)
    {
        _metadata = metadata;
        MetadataChanged?.Invoke(this, _metadata);
    }

    public Task InterruptAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task SetWorkingDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _metadata = _metadata with { CurrentWorkingDirectory = path };
        MetadataChanged?.Invoke(this, _metadata);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _metadata = _metadata with { State = TerminalSessionLifecycleState.Disposed };
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
