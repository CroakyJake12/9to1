using Haven.Application;
using Haven.Core;
using Haven.Infrastructure.Terminal;
using HavenOS.Apps.Terminal;
using HavenOS.Home.Core;

internal static class TerminalEnvironmentSpecs
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-terminal-environment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var authority = new TerminalLocalEnvironmentAuthority(home, profiles, profiles);
            var leases = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => authority.GetOrCreateAsync()));
            Check(leases.Select(item => item.EnvironmentId).Distinct().Count() == 1, "Concurrent Home creators must return one winning environment identity.");
            var reopened = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var reopenedProfiles = new HomeLocalProfileIdentity(reopened, new OperatingSystemPrincipalSource());
            var next = await new TerminalLocalEnvironmentAuthority(reopened, reopenedProfiles, reopenedProfiles).GetOrCreateAsync();
            Check(next.EnvironmentId == leases[0].EnvironmentId, "Environment identity must survive actual Home reopen.");
            await authority.RequireCurrentAsync(leases[0]);
            var record = (await home.ReadAsync()).State!.Records.Single(item => item.RecordType == "terminal.local-environment");
            Check((await home.WriteGuardedAsync(record, record.Revision, leases[0].Actor, profiles)).IsSuccess, "Actual guarded revision update failed.");
            await Denied(() => authority.RequireCurrentAsync(leases[0]).AsTask());
            var current = await authority.GetOrCreateAsync();
            Check(current.EnvironmentId == leases[0].EnvironmentId, "Revision change must not invent a replacement locator.");
            var changedActors = new ChangedActors(profiles, current.Actor with { AuthenticationRevision = "revoked-fixture-session" });
            await Denied(() => new TerminalLocalEnvironmentAuthority(home, changedActors, profiles).RequireCurrentAsync(current).AsTask());
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This acceptance requires the genuine Linux PTY.");
            var environment = new TerminalEnvironmentDescriptor(current.EnvironmentId, "owned-linux-fixture", TerminalEnvironmentKind.LocalHost,
                "Actual owned Linux test process", TerminalEnvironmentConnectionState.Ready, "linux", "x64",
                TerminalEnvironmentCapability.InteractivePty | TerminalEnvironmentCapability.Resize | TerminalEnvironmentCapability.Signals);
            var actual = new PtyTerminalSessionFactory(environment, new UnixPtyProcessFactory(),
                [new("owned-shell", "Actual shell fixture", "/bin/sh", ["-c", "exec sleep 30"], "owned-linux-fixture", false, TerminalEnvironmentConnectionState.Ready)]);
            var bound = new HomeBoundTerminalSessionFactory(new ExactEnvironmentOnlyFactory(actual), authority, current);
            using (var live = new TerminalAppSurface(new(bound, () => PermissionMode.FullAccess), root))
            {
                var session = live.InteractiveSession ?? throw new Exception("The owning factory did not return its original actual PTY.");
                Check(session.Metadata.EnvironmentId == current.EnvironmentId && session.ProcessId > 0, "Actual process metadata lost the durable Home environment identity.");
                var echoed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var transcript = new System.Text.StringBuilder();
                session.OutputReceived += (_, output) => { lock (transcript) { transcript.Append(output.Text); if (transcript.ToString().Contains("owned-input-proof", StringComparison.Ordinal)) echoed.TrySetResult(); } };
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await live.SendInteractiveInputAsync(session.Metadata.SessionId, System.Text.Encoding.UTF8.GetBytes("owned-input-proof\n"), deadline.Token);
                await echoed.Task.WaitAsync(deadline.Token);
                var currentRecord = (await home.ReadAsync()).State!.Records.Single(item => item.RecordType == "terminal.local-environment");
                Check((await home.WriteGuardedAsync(currentRecord, currentRecord.Revision, current.Actor, profiles)).IsSuccess, "Could not advance actual owner record for revocation test.");
                await Denied(() => live.SendInteractiveInputAsync(session.Metadata.SessionId, new byte[] { 65 }).AsTask());
                Check((await live.SubmitAsync("must-not-dispatch")).State == TerminalAppCommandState.Denied, "Revoked Home environment permitted manual execution.");
                Check(!live.NewSession() && ReferenceEquals(live.InteractiveSession, session), "Revoked factory replaced its existing owned session.");
            }


            var factory = new PausedAdmissionFactory();
            using var surface = new TerminalAppSurface(new(factory, () => PermissionMode.FullAccess), root);
            var command = surface.SubmitAsync("must-not-run");
            await factory.Entered.Task;
            TerminalSessionMetadata? replacementNotice = null;
            surface.MetadataChanged += (_, metadata) => replacementNotice = metadata;
            surface.NewSession();
            Check(replacementNotice?.SessionId == surface.SessionMetadata?.SessionId && replacementNotice is not null,
                "Successful replacement must publish the actual new session metadata for host ownership registration.");
            factory.Release.TrySetResult();
            Check((await command).State == TerminalAppCommandState.Denied && factory.Created.All(item => item.ExecuteCount == 0),
                "A replacement session during authority admission must not execute the old or replacement command.");
            factory.Revoked = true;
            Check((await surface.SubmitAsync("revoked-command")).State == TerminalAppCommandState.Denied, "Revoked owner admission must deny a manual command.");
            Check(factory.Created.All(item => item.ExecuteCount == 0), "Denied admission dispatched a command.");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Denied(Func<Task> action)
    {
        try { await action(); } catch (UnauthorizedAccessException) { return; }
        throw new Exception("Changed Terminal authority was accepted.");
    }
    private sealed class ChangedActors(HomeLocalProfileIdentity profiles, AuthenticatedResourceActor changed) : IAuthenticatedResourceActorSource
    {
        public async ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct)
        { _ = await profiles.GetCurrentAsync(ct); return changed; }
    }
    // The real PTY remains the executor; this boundary rejects an unscoped default spawn.
    private sealed class ExactEnvironmentOnlyFactory(ITerminalInteractiveSessionFactory actual) : ITerminalInteractiveSessionFactory
    {
        public ITerminalSession Create(string directory, string? name = null)
            => throw new Exception("The Home-bound factory must select its exact environment before any process starts.");
        public IReadOnlyList<TerminalEnvironmentDescriptor> ListEnvironments() => actual.ListEnvironments();
        public IReadOnlyList<TerminalShellProfile> ListShellProfiles(TerminalEnvironmentId? environmentId = null) => actual.ListShellProfiles(environmentId);
        public ITerminalInteractiveSession Create(TerminalSessionStartRequest request) => actual.Create(request);
        public Task<ITerminalInteractiveSession> CreateAsync(TerminalSessionStartRequest request, CancellationToken ct = default) => actual.CreateAsync(request, ct);
    }
    private sealed class PausedAdmissionFactory : ITerminalSessionFactory, ITerminalSessionAdmission
    {
        public List<FakeSession> Created { get; } = [];
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Revoked { get; set; }
        public ITerminalSession Create(string initialDirectory, string? displayName = null)
        { var session = new FakeSession(initialDirectory, displayName ?? "Admission fixture"); Created.Add(session); return session; }
        public async ValueTask RequireSessionAsync(TerminalSessionMetadata session, CancellationToken ct)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            if (Revoked) throw new UnauthorizedAccessException("Fixture owner revoked.");
        }
    }
}
