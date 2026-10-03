/*
 * FILE DOCUMENTATION
 * Where: HavenOS Apps/Terminal/Tests/Program.cs.
 * What: Focused executable specifications for Terminal app host gating, permission approval, persistent sessions, cwd, and restart behavior.
 * Why: The migration must prove it reuses the shared terminal contracts and fails closed without a host execution capability.
 */
using Haven.Application;
using Haven.Core;
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

    if(args.Contains("--owned-signal-only")){await OwnedSignalSpecs.RunAsync();return;}
    await TerminalEnvironmentSpecs.RunAsync();
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
        await ApprovalBindsDisplayedCommandAndDirectoryAsync();
        await WorkingDirectoryAndNewSessionUseHostSessionContractAsync();
        await FailedReplacementPreservesHealthySessionAsync();
        CreationFailureFailsClosedAndRedactsReason();
        await AdviceNeverExecutesSuggestedCommandsAsync();
        await AiResolutionCannotOutliveItsSessionContextAsync();
        await RetainedAuditSurvivesCancellationAndSessionChangeWithoutActionReplayAsync();
    }

    private static async Task AiResolutionCannotOutliveItsSessionContextAsync()
    {
        var factory=new FakeSessionFactory();var broker=new DelayedActionBroker();
        using var surface=new TerminalAppSurface(new(factory,()=>PermissionMode.FullAccess,NaturalLanguageActions:broker));
        surface.SetMode(TerminalInputMode.AI);
        var pending=surface.SubmitAsync("show files");surface.SetMode(TerminalInputMode.Command);broker.Complete();
        Check((await pending).State==TerminalAppCommandState.Cancelled&&surface.ResolvedAction is null,"late AI result cannot resurrect after mode change");
        surface.SetMode(TerminalInputMode.AI);
        using(var cancelled=new CancellationTokenSource())
        {
            pending=surface.SubmitAsync("show files",cancelled.Token);cancelled.Cancel();broker.Complete();
            try{await pending;throw new Exception("cancelled resolution published");}catch(OperationCanceledException){}
            Check(surface.ResolvedAction is null,"broker ignoring cancellation cannot publish cancelled resolution");
        }
        surface.SetMode(TerminalInputMode.AI);pending=surface.SubmitAsync("show files");broker.Complete(foreign:true);
        Check((await pending).State==TerminalAppCommandState.Cancelled&&surface.ResolvedAction is null,"foreign resolved session target rejected");
        pending=surface.SubmitAsync("show files");broker.Complete();await pending;var old=surface.ResolvedAction!;
        Check(surface.NewSession(),"replacement native session created");
        Check((await surface.ExecuteResolvedActionAsync(old.Id.ToString("D"),null)).State==TerminalAppCommandState.Unavailable&&broker.Executions==0,"old session AI action cannot dispatch after replacement");
        pending=surface.SubmitAsync("show files");broker.Complete();await pending;var current=surface.ResolvedAction!;
        broker.Objects[0]="injected";Check(current.AffectedObjects.Single()=="fixture-file","resolved object scope detached from broker mutable list");
        Check((await surface.ExecuteResolvedActionAsync(current.Id.ToString("D"),"fixture-home-verification")).State==TerminalAppCommandState.Succeeded,"current action delegated to canonical broker");
        Check((await surface.ExecuteResolvedActionAsync(current.Id.ToString("D"),"fixture-home-verification")).State==TerminalAppCommandState.Unavailable&&broker.Executions==1,"resolved action consumed once");
        pending=surface.SubmitAsync("show files");broker.Complete();await pending;current=surface.ResolvedAction!;
        broker.RequireApproval = true;
        var approval = await surface.ExecuteResolvedActionAsync(current.Id.ToString("D"), null);
        Check(approval.State == TerminalAppCommandState.RequiresApproval && surface.ResolvedAction?.Id == current.Id, "pending Home approval preserves exact draft without execution");
        broker.RequireApproval = false;
        Check((await surface.ExecuteResolvedActionAsync(current.Id.ToString("D"), "home-request")).State == TerminalAppCommandState.Succeeded && surface.ResolvedAction is null, "approved retry consumes draft");
        pending=surface.SubmitAsync("show files");broker.Complete();await pending;current=surface.ResolvedAction!;
        broker.RequireApproval = true;
        broker.BeforeExecution = () => surface.SetMode(TerminalInputMode.Command);
        Check((await surface.ExecuteResolvedActionAsync(current.Id.ToString("D"), null)).State == TerminalAppCommandState.Unavailable && surface.ResolvedAction is null, "pending approval cannot restore after mode change");
        broker.RequireApproval = false; broker.BeforeExecution = null; surface.SetMode(TerminalInputMode.AI);
        pending=surface.SubmitAsync("show files");broker.Complete();await pending;current=surface.ResolvedAction!;
        await surface.SetWorkingDirectoryAsync(Path.GetTempPath());
        Check((await surface.ExecuteResolvedActionAsync(current.Id.ToString("D"),null)).State==TerminalAppCommandState.Unavailable,"directory transition invalidates resolved action");
    }
    private static async Task RetainedAuditSurvivesCancellationAndSessionChangeWithoutActionReplayAsync()
    {
        var broker = new DelayedActionBroker { PauseExecution = true };
        using var surface = new TerminalAppSurface(new(new FakeSessionFactory(), () => PermissionMode.FullAccess, NaturalLanguageActions: broker));
        surface.SetMode(TerminalInputMode.AI);
        var resolution = surface.SubmitAsync("controlled first action"); broker.Complete(); await resolution;
        var first = surface.ResolvedAction!;
        using var cancellation = new CancellationTokenSource();
        var execution = surface.ExecuteResolvedActionAsync(first.Id.ToString("D"), null, cancellation.Token);
        resolution = surface.SubmitAsync("controlled later action"); broker.Complete(); await resolution;
        var later = surface.ResolvedAction!;
        Check((await surface.ExecuteResolvedActionAsync(later.Id.ToString("D"), null)).State == TerminalAppCommandState.Unavailable && broker.Executions == 1,
            "another action cannot dispatch while the first owner outcome is pending");
        cancellation.Cancel(); broker.CompleteExecution();
        Check((await execution).State == TerminalAppCommandState.NeedsRecovery && surface.AuditRecoveryActionId == first.Id,
            "cancellation after owning result cannot discard exact audit recovery");
        Check(surface.NewSession(), "replacement session for audit boundary created");
        surface.SetMode(TerminalInputMode.Command);
        Check((await surface.RetryActionAuditAsync(Guid.NewGuid())).State == TerminalAppCommandState.Unavailable && broker.AuditRetries == 0,
            "foreign displayed action cannot retry retained audit");
        Check((await surface.RetryActionAuditAsync(first.Id)).State == TerminalAppCommandState.Succeeded && surface.AuditRecoveryActionId is null &&
            broker.Executions == 1 && broker.AuditRetries == 1, "session change audit retry cannot redispatch the consumed owner action");
    }

    private sealed class DelayedActionBroker:ITerminalActionBroker
    {
        private TaskCompletionSource<TerminalResolvedAction> _pending=null!;private Guid _session;private TerminalEnvironmentId _environment;
        public List<string> Objects {get;private set;}=[];public int Executions {get;private set;}
        public bool RequireApproval { get; set; }
        public bool PauseExecution; public int AuditRetries;
        private readonly TaskCompletionSource<TerminalActionExecutionResult> _execution = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void CompleteExecution() => _execution.TrySetResult(new(false, "NeedsRecovery", "Controlled owner outcome needs audit recovery."));
        public Task<TerminalActionExecutionResult> RetryAuditAsync(Guid actionId, CancellationToken ct = default)
        { AuditRetries++; return Task.FromResult(new TerminalActionExecutionResult(false, "AuditRecorded", "Controlled exact audit observed.")); }
        public Action? BeforeExecution { get; set; }
        public Task<TerminalResolvedAction> ResolveAsync(Guid session,TerminalEnvironmentId environment,string request,CancellationToken ct)
        { _session=session;_environment=environment;_pending=new(TaskCreationOptions.RunContinuationsAsynchronously);Objects=["fixture-file"];return _pending.Task; }
        public void Complete(bool foreign=false)=>_pending.SetResult(new(Guid.NewGuid(),foreign?Guid.NewGuid():_session,_environment,TerminalActionKind.TypedApi,"Files","List","Show files",Objects,TerminalActionRisk.ReadOnly,true,false));
        public Task<TerminalActionExecutionResult> ExecuteAsync(TerminalResolvedAction action,string? verificationToken,CancellationToken ct)
        { BeforeExecution?.Invoke(); if (RequireApproval) return Task.FromResult(new TerminalActionExecutionResult(false,"PermissionRequired","Review in Home", "home-request")); Executions++;return PauseExecution ? _execution.Task : Task.FromResult(new TerminalActionExecutionResult(true,"Executed","Fixture broker executed"));}
    }

    private static async Task AdviceNeverExecutesSuggestedCommandsAsync()
    {
        var factory=new FakeSessionFactory();var advice=new AdviceFixture();
        using var surface=new TerminalAppSurface(new(factory,()=>PermissionMode.FullAccess,Advice:advice));
        var result=await surface.SubmitAsync("$Ask how do I remove old files?");
        Check(result.State==TerminalAppCommandState.Succeeded,"advice returned");
        Check(factory.LastSession!.ExecuteCount==0,"suggested command never reaches native shell");
        Check(advice.Question=="how do I remove old files?","reserved Ask prefix routed to advice");
        var count=factory.CreateCount;surface.SetMode(TerminalInputMode.AI);surface.SetMode(TerminalInputMode.Command);
        Check(factory.CreateCount==count,"mode switch preserves persistent PTY");
    }
    private sealed class AdviceFixture:ITerminalAdviceService
    {
        public string? Question {get;private set;}
        public Task<TerminalAdviceResult> AskAsync(Haven.Application.TerminalAdviceContext context,string question,CancellationToken token)
        {Question=question;return Task.FromResult(new TerminalAdviceResult("Suggested command: rm old-file (review first)",["rm old-file"]));}
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
        var approved = await surface.ApprovePendingAsync();
        Check(approved.State == TerminalAppCommandState.Succeeded, "approved command should execute");

        var second = await surface.SubmitAsync("set-state two");
        Check(second.State == TerminalAppCommandState.RequiresApproval, "second command should independently await approval");
        var approvedSecond = await surface.ApprovePendingAsync();
        Check(approvedSecond.State == TerminalAppCommandState.Succeeded, "second approved command should execute");
        Check(factory.CreateCount == 1, "multiple commands must reuse one persistent host session");
        Check(factory.LastSession!.ExecuteCount == 2, "both commands must execute through that session");
    }

    private static async Task ApprovalBindsDisplayedCommandAndDirectoryAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "terminal-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var factory = new FakeSessionFactory();
            using var surface = new TerminalAppSurface(new(factory, () => PermissionMode.Ask));
            await surface.SubmitAsync("first-command");
            var first = surface.PendingCommandId!.Value;
            await surface.SubmitAsync("second-command");
            var second = surface.PendingCommandId!.Value;
            Check(first != second, "each manual review has its own stable identity");
            Check((await surface.ApprovePendingAsync(first)).State == TerminalAppCommandState.Denied, "old review cannot approve replacement command");
            surface.DenyPending(first);
            Check(surface.PendingCommandId == second && factory.LastSession!.ExecuteCount == 0, "old deny cannot clear replacement review");
            Check((await surface.ApprovePendingAsync(second)).State == TerminalAppCommandState.Succeeded && factory.LastSession!.LastCommand == "second-command", "exact displayed review executes once");
            await surface.SubmitAsync("directory-sensitive");
            var beforeDirectory = surface.PendingCommandId!.Value;
            await surface.SetWorkingDirectoryAsync(directory);
            Check(surface.PendingCommandId is null && (await surface.ApprovePendingAsync(beforeDirectory)).State != TerminalAppCommandState.Succeeded, "owner directory change invalidates pending review");
            await surface.SubmitAsync("external-directory-sensitive");
            var external = surface.PendingCommandId!.Value;
            await factory.LastSession!.SetWorkingDirectoryAsync(Path.GetTempPath(), default);
            Check((await surface.ApprovePendingAsync(external)).State == TerminalAppCommandState.Denied && factory.LastSession.ExecuteCount == 1, "external session directory change denies old review");
            await surface.SubmitAsync("replaced-session");
            var replaced = surface.PendingCommandId!.Value;
            surface.NewSession();
            Check((await surface.ApprovePendingAsync(replaced)).State != TerminalAppCommandState.Succeeded && factory.LastSession!.ExecuteCount == 0, "old review cannot target replacement shell");
        }
        finally { Directory.Delete(directory, true); }
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
            false) {EnvironmentId=new TerminalEnvironmentId("fixture-local")};
    }

    public int ExecuteCount { get; private set; }
    public string? LastCommand { get; private set; }
    public TerminalSessionMetadata Metadata => _metadata;
    public int? ProcessId => 1234;
    public event EventHandler<TerminalSessionOutput>? OutputReceived;
    public event EventHandler<TerminalSessionMetadata>? MetadataChanged;

    public Task<TerminalSessionCommandResult> ExecuteAsync(Guid commandId, string command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExecuteCount++;
        LastCommand = command;
        _metadata = _metadata with { State = TerminalSessionLifecycleState.Running };
        MetadataChanged?.Invoke(this, _metadata);
        OutputReceived?.Invoke(this, new(_metadata.SessionId, commandId, TerminalOutputStream.StandardOutput, command, DateTimeOffset.UtcNow));
        _metadata = _metadata with { State = TerminalSessionLifecycleState.Ready };
        MetadataChanged?.Invoke(this, _metadata);
        return Task.FromResult(new TerminalSessionCommandResult(
            _metadata.SessionId,
            commandId,
            0,
            false,
            TimeSpan.FromMilliseconds(1),
            _metadata.CurrentWorkingDirectory,
            true,
            false));
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
