using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NineToOne.Cui.AI;
using Haven.Application;
using Haven.Infrastructure.Terminal;
using HavenOS.Apps.Terminal;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

internal static class OwnedSignalSpecs
{
    public static async Task RunAsync()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This acceptance requires actual Linux forkpty.");
        var root = Path.Combine(Path.GetTempPath(), "astra-terminal-owner-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var state = new CompletionFaultStore(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            var actors = new HomeLocalProfileIdentity(state, new OperatingSystemPrincipalSource());
            var services = new ServiceCollection();
            services.AddSingleton<IAuthenticatedResourceActorSource>(actors);
            services.AddSingleton<IDulcheAppClient>(new SignalClient());
            services.AddSingleton(provider => new ResourceAuthorizationService(actors, provider.GetServices<ICanonicalResourceAccessResolver>()));
            services.AddSingleton(provider => new HomePermissionTrustService(state, (app, action) => provider.GetServices<IHomeActionPolicySource>().Select(policy => policy.TryGet(app, action)).SingleOrDefault(policy => policy is not null)));
            services.AddSingleton<HomeResourceOperationBroker>();
            services.AddTerminalNativeActions(); services.AddTerminalNativeActions();
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            var registry = provider.GetRequiredService<TerminalOwnedSessionRegistry>();
            var resources = provider.GetRequiredService<ResourceAuthorizationService>();
            var permissions = provider.GetRequiredService<HomePermissionTrustService>();
            var home = provider.GetRequiredService<HomeResourceOperationBroker>();
            var executor = provider.GetRequiredService<TerminalOwnedSignalExecutor>();
            if (provider.GetServices<ICanonicalResourceAccessResolver>().Count() != 1) throw new Exception("Terminal composition duplicated its resource resolver.");
            var environment = new TerminalEnvironmentDescriptor(new("fixture-live-local"), "fixture-forkpty", TerminalEnvironmentKind.LocalHost,
                "Actual Linux fixture", TerminalEnvironmentConnectionState.Ready, "linux", "x64",
                TerminalEnvironmentCapability.InteractivePty | TerminalEnvironmentCapability.Resize | TerminalEnvironmentCapability.Signals);
            var factory = new PtyTerminalSessionFactory(environment, new UnixPtyProcessFactory(),
                [new("fixture-shell", "Fixture shell", "/bin/sh", ["-c", "printf 'native-ready\\n'; exec sleep 30"], "fixture-forkpty", false, TerminalEnvironmentConnectionState.Ready)]);
            Console.WriteLine("Native acceptance: starting actual Linux forkpty session.");
            await using var session = await factory.CreateAsync(new(environment.Id, "fixture-shell", root));
            Console.WriteLine("Native acceptance: live session created; testing input.");
            await AssertNoInheritedPtyMasterAsync();
            var echo = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var observedOutput = new System.Text.StringBuilder();
            session.OutputReceived += (_, output) =>
            {
                lock (observedOutput)
                {
                    observedOutput.Append(output.Text);
                    if (observedOutput.ToString().Contains("fixture-input", StringComparison.Ordinal)) echo.TrySetResult();
                }
            };
            await session.ResizeAsync(100, 35);
            await Task.Delay(100);
            using (var inputDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            {
                await session.SendInputAsync(System.Text.Encoding.UTF8.GetBytes("fixture-input\n"), inputDeadline.Token);
                await echo.Task.WaitAsync(inputDeadline.Token);
            }
            Console.WriteLine("Native acceptance: PTY input, echoed output and resize verified.");
            var snapshot = await registry.RegisterAsync(session);
            if (snapshot.ProcessID <= 0) throw new Exception("Real forkpty process did not start.");
            var intent = await TerminalSignalIntent.CaptureAsync(registry, snapshot.SessionID, TerminalProcessSignal.Terminate);
            var pending = await home.AuthorizeAsync(TerminalSignalIntent.AppID, TerminalSignalIntent.ActionID, intent.Scopes, intent.Arguments,
                $"Terminate only fixture process {snapshot.ProcessID} in its actual Terminal session", null,
                (await actors.GetCurrentAsync(default))!.AuthenticationRevision);
            if (pending.State != HomePermissionRequestState.PendingApproval || session.ProcessId is null) throw new Exception("No process may be signalled before review.");
            if (await home.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments) is not null) throw new Exception("Unapproved signal obtained capability.");
            if (!(await permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded) throw new Exception("Fixture Home approval failed.");
            var capability = await home.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments) ?? throw new Exception("Approved signal capability missing.");
            var changed = await TerminalSignalIntent.CaptureAsync(registry, snapshot.SessionID, TerminalProcessSignal.Kill);
            try { await executor.ExecuteAsync(changed, capability); throw new Exception("Changed signal accepted."); } catch (UnauthorizedAccessException) { }
            var foreign = new TerminalOwnedSignalExecutor(registry, new(resources, permissions));
            try { await foreign.ExecuteAsync(intent, capability); throw new Exception("Foreign issuer accepted."); } catch (UnauthorizedAccessException) { }
            await executor.ExecuteAsync(intent, capability);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while ((session.ProcessId is not null || !HasExited(snapshot.ProcessID)) && DateTimeOffset.UtcNow < deadline) await Task.Delay(20);
            if (session.ProcessId is not null || !HasExited(snapshot.ProcessID)) throw new Exception("Authorised actual process did not terminate.");
            try { await executor.ExecuteAsync(intent, capability); throw new Exception("Signal replay accepted."); } catch (InvalidOperationException) { }
            registry.Unregister(session);
            await using var second = await factory.CreateAsync(new(environment.Id, "fixture-shell", root));
            var secondSnapshot = await registry.RegisterAsync(second);
            var broker = provider.GetRequiredService<ITerminalActionBroker>();
            var preview = await broker.ResolveAsync(secondSnapshot.SessionID, environment.Id, "Terminate this fixture process");
            var review = await broker.ExecuteAsync(preview);
            if (review.State != "PermissionRequired" || string.IsNullOrWhiteSpace(review.ResultText) || second.ProcessId is null)
                throw new Exception("Real action broker did not retain pending approval without effect.");
            if ((await broker.ExecuteAsync(preview, "unbound-token")).Executed) throw new Exception("Unbound approval reference accepted.");
            if (!(await permissions.DecideAsync(review.ResultText, HomeApprovalChoice.Accept)).Succeeded) throw new Exception("Broker Home review failed.");
            if (!(await broker.ExecuteAsync(preview, review.ResultText)).Executed) throw new Exception("Exact approved broker retry did not dispatch.");
            var completed = await permissions.GetAuthorizationAsync(review.ResultText);
            if (completed.State != HomePermissionRequestState.Succeeded || completed.Code != "TerminalSignalSent")
                throw new Exception("Actual broker signal did not record its canonical completed outcome.");
            deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while ((second.ProcessId is not null || !HasExited(secondSnapshot.ProcessID)) && DateTimeOffset.UtcNow < deadline) await Task.Delay(20);
            if (second.ProcessId is not null || !HasExited(secondSnapshot.ProcessID) || (await broker.ExecuteAsync(preview, review.ResultText)).Executed)
                throw new Exception("Broker termination or replay rejection failed.");
            registry.Unregister(second);
            foreach (var afterPublication in new[] { false, true })
            {
                await using var recoverySession = await factory.CreateAsync(new(environment.Id, "fixture-shell", root));
                var recoverySnapshot = await registry.RegisterAsync(recoverySession);
                var recoveryPreview = await broker.ResolveAsync(recoverySnapshot.SessionID, environment.Id, "Terminate this fixture process");
                var recoveryReview = await broker.ExecuteAsync(recoveryPreview);
                if (!(await permissions.DecideAsync(recoveryReview.ResultText!, HomeApprovalChoice.Accept)).Succeeded)
                    throw new Exception("Recovery fixture review failed.");
                state.AfterPublication = afterPublication; state.Fail = true;
                var observed = await broker.ExecuteAsync(recoveryPreview, recoveryReview.ResultText);
                if (observed.State != "NeedsRecovery") throw new Exception("Audit fault concealed actual sent signal recovery.");
                deadline = DateTimeOffset.UtcNow.AddSeconds(5);
                while ((recoverySession.ProcessId is not null || !HasExited(recoverySnapshot.ProcessID)) && DateTimeOffset.UtcNow < deadline) await Task.Delay(20);
                if (recoverySession.ProcessId is not null || !HasExited(recoverySnapshot.ProcessID))
                    throw new Exception("Actual signal was not observed under audit fault.");
                if ((await broker.RetryAuditAsync(recoveryPreview.Id)).State != "AuditRecorded")
                    throw new Exception("Exact sealed completion audit did not recover.");
                if ((await broker.ExecuteAsync(recoveryPreview, recoveryReview.ResultText)).Executed ||
                    (await broker.RetryAuditAsync(recoveryPreview.Id)).State != "Unavailable")
                    throw new Exception("Consumed signal or cleared audit recovery replayed.");
                var completion = await permissions.GetAuthorizationAsync(recoveryReview.ResultText!);
                if (completion.State != HomePermissionRequestState.Succeeded || completion.Code != "TerminalSignalSent")
                    throw new Exception("Audit retry changed actual owner outcome.");
                var audits = (await permissions.GetSnapshotAsync()).RecentAuditEvents.Where(item =>
                    item.RequestId == recoveryReview.ResultText && item.Kind == HomePermissionAuditKind.ExecutionCompleted).ToArray();
                if (audits.Length != 1 || audits[0].AffectedObjects.Single().ObjectId != recoverySnapshot.SessionID.ToString("D"))
                    throw new Exception("Recovery omitted exact canonical affected session or duplicated audit.");
                registry.Unregister(recoverySession);
            }
            foreach (var deniedNegativeWrites in new[] { 0, 2 })
            {
                await using var admittedSession = await factory.CreateAsync(new(environment.Id, "fixture-shell", root));
                var admittedSnapshot = await registry.RegisterAsync(admittedSession);
                var admittedPreview = await broker.ResolveAsync(admittedSnapshot.SessionID, environment.Id, "Terminate this fixture process");
                var admittedReview = await broker.ExecuteAsync(admittedPreview);
                if (!(await permissions.DecideAsync(admittedReview.ResultText!, HomeApprovalChoice.Accept)).Succeeded)
                    throw new Exception("Dispatch fault fixture review failed.");
                state.FailBegin = true; state.DenyNegativeWrites = deniedNegativeWrites;
                var stopped = await broker.ExecuteAsync(admittedPreview, admittedReview.ResultText);
                if (stopped.State != "NeedsRecovery" || admittedSession.ProcessId != admittedSnapshot.ProcessID || HasExited(admittedSnapshot.ProcessID))
                    throw new Exception("Rejected dispatch issued a process signal or hid admission failure.");
                var retry = await broker.RetryAuditAsync(admittedPreview.Id);
                if (retry.State != (deniedNegativeWrites == 0 ? "Unavailable" : "AuditRecorded"))
                    throw new Exception("Exact negative dispatch audit recovery was not observed.");
                var decision = await permissions.GetAuthorizationAsync(admittedReview.ResultText!);
                if (decision.State != HomePermissionRequestState.Failed || decision.Code != "HOME_RESOURCE_BEGIN_REJECTED" ||
                    admittedSession.ProcessId != admittedSnapshot.ProcessID || HasExited(admittedSnapshot.ProcessID))
                    throw new Exception("Dispatch negative retry changed the owning process or failed to record exact negative outcome.");
                if ((await broker.ExecuteAsync(admittedPreview, admittedReview.ResultText)).Executed)
                    throw new Exception("Rejected consumed dispatch replayed.");
                registry.Unregister(admittedSession);
            }
            Console.WriteLine("PASS actual Linux rejected dispatch with published Begin fault: process remains alive, fixed negative audit-only retry, no signal replay.");
            Console.WriteLine("PASS actual Linux signals with before/after durable audit faults: exact audit-only retry, retained owner success, no action replay.");
            Console.WriteLine("Native acceptance: approved owner/broker signals passed; testing immediate disposal.");
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var immediate = await factory.CreateAsync(new(environment.Id, "fixture-shell", root));
                var child = immediate.ProcessId ?? throw new Exception("Immediate-disposal fixture did not own a child.");
                await immediate.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                if (!HasExited(child)) throw new Exception("Immediate disposal left a live child.");
            }
            Console.WriteLine("PASS actual Linux PTY: unrelated exec inherits no PTY master; eight immediate disposals leave no live child.");
            Console.WriteLine("PASS actual Linux forkpty/Home owned signal: pending approval leaves process alive; exact one-use termination, changed signal/foreign issuer/replay denied.");
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class CompletionFaultStore(IHomeCoreStateStore inner) : IHomeCoreStateStore
    {
        public bool Fail; public bool AfterPublication;
        private int _completionCount;
        private int _startCount;
        public bool FailBegin; public int DenyNegativeWrites;
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default) => inner.ReadAsync(ct);
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long expected, AuthenticatedResourceActor actor, IHomeStateCommitActorGuard guard, CancellationToken ct = default) =>
            inner.WriteGuardedAsync(record, expected, actor, guard, ct);
        public async Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expected, CancellationToken ct = default)
        {
            var json = record.Payload.GetRawText();
            var startCount = json.Split("HOME_EXECUTION_STARTED", StringSplitOptions.None).Length - 1;
            if (FailBegin && startCount > _startCount)
            {
                FailBegin = false;
                if (!(await inner.WriteAsync(record, expected, ct)).IsSuccess) throw new Exception("Injected Begin did not publish.");
                throw new IOException("Injected published Begin acknowledgement fault.");
            }
            if (DenyNegativeWrites > 0 && json.Contains("HOME_RESOURCE_BEGIN_REJECTED", StringComparison.Ordinal))
            {
                DenyNegativeWrites--;
                throw new UnauthorizedAccessException("Injected fixed negative dispatch audit fault.");
            }
            var completionCount = json.Split("TerminalSignalSent", StringSplitOptions.None).Length - 1;
            if (Fail && completionCount > _completionCount)
            {
                Fail = false;
                if (AfterPublication) await inner.WriteAsync(record, expected, ct);
                throw new IOException("Injected canonical completion audit acknowledgement failure.");
            }
            var result = await inner.WriteAsync(record, expected, ct);
            if (result.IsSuccess) { _completionCount = completionCount; _startCount = startCount; }
            return result;
        }
    }
    private static async Task AssertNoInheritedPtyMasterAsync()
    {
        var masters = 0;
        foreach (var descriptor in Directory.EnumerateFiles("/proc/self/fd"))
        {
            var target = new FileInfo(descriptor).LinkTarget;
            if (target is not ("/dev/pts/ptmx" or "/dev/ptmx")) continue;
            masters++;
            var flagsLine = File.ReadLines("/proc/self/fdinfo/" + Path.GetFileName(descriptor)).Single(line => line.StartsWith("flags:", StringComparison.Ordinal));
            var flags = Convert.ToInt64(flagsLine[6..].Trim(), 8);
            if ((flags & 0x80000) == 0) throw new Exception("Owned PTY master lacks kernel close-on-exec flag.");
        }
        if (masters < 2) throw new Exception("Fixture did not observe both actual PTY master streams.");
        var start = new System.Diagnostics.ProcessStartInfo("/bin/sh")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("for fd in /proc/self/fd/*; do readlink \"$fd\" || :; done");
        using var child = System.Diagnostics.Process.Start(start) ?? throw new Exception("Descriptor observer did not start.");
        var output = await child.StandardOutput.ReadToEndAsync();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        if (child.ExitCode != 0 || output.Contains("/dev/pts/ptmx", StringComparison.Ordinal) || output.Contains("/dev/ptmx", StringComparison.Ordinal))
            throw new Exception("An unrelated exec inherited a Terminal PTY master.");
    }
    private static bool HasExited(int processId)
    {
        try { using var process = System.Diagnostics.Process.GetProcessById(processId); return process.HasExited; }
        catch (ArgumentException) { return true; }
    }
    private sealed class SignalClient : IDulcheAppClient
    {
        public async IAsyncEnumerable<AppAiResponseChunk> StreamAsync(AppAiPrompt prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (prompt.AccessMode != AppAiAccessMode.ReadOnly) throw new Exception("Model resolution may only be read-only.");
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new("{\"signal\":\"Terminate\"}", true);
        }
    }
}
