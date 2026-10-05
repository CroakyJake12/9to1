using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using System.Security.Principal;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Real Windows pipes, kernel PID/SID, lease, canonical File state, manual broker and
/// actual Core. Installed receipt verification is explicitly synthetic; these cases prove no
/// protected publisher/installed package or whole native app graph.</summary>
public sealed class HomeWindowsCoreTransportTests
{
    private sealed class WindowsFactAttribute : FactAttribute
    { public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Requires the actual Windows kernel."; } }
    private sealed class WindowsTheoryAttribute : TheoryAttribute
    { public WindowsTheoryAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Requires the actual Windows kernel."; } }

    [WindowsFact]
    public Task Actual_pipe_peer_manual_review_and_original_read_use_one_canonical_session() =>
        WithRigAsync(async rig =>
        {
            await rig.AttachClientAsync();
            var pending = await rig.Client!.GetServicesAsync(rig.Token);
            Assert.Equal("PermissionRequired", pending.Operation.Code);
            Assert.Null(pending.Operation.Value);
            Assert.Equal(0, rig.Api!.Reads);
            Assert.Equal(Environment.ProcessId, rig.Verifier!.LastClient!.ProcessId);
            Assert.Equal(rig.Principal, rig.Verifier.LastClient.OperatingSystemPrincipalId);
            Assert.Equal(Environment.ProcessId, rig.Verifier.LastHost!.ProcessId);
            var same = await rig.Client.GetServicesAsync(rig.Token);
            Assert.Equal(pending.PermissionRequestId, same.PermissionRequestId);
            Assert.True((await rig.Permissions!.DecideAsync(pending.PermissionRequestId!, HomeApprovalChoice.Accept,
                cancellationToken: rig.Token)).Succeeded);
            var result = await rig.Client.GetServicesAsync(rig.Token);
            Assert.True(result.Operation.Succeeded);
            Assert.Equal(pending.PermissionRequestId, result.PermissionRequestId);
            Assert.Equal(new[] { "home.core", "home.state" }, result.Operation.Value!.Select(row => row.ServiceId).Order().ToArray());
            Assert.Equal(1, rig.Api.Reads);
            Assert.Equal(HomePermissionRequestState.Succeeded,
                (await rig.Permissions.ReadRequestObservationAsync(pending.PermissionRequestId!, rig.Token))!.State);
        });

    [WindowsTheory]
    [InlineData("actor")]
    [InlineData("installation")]
    [InlineData("lease")]
    public Task Current_original_retirement_during_actual_read_prevents_reply_publication(string change) =>
        WithRigAsync(async rig =>
        {
            await rig.AttachClientAsync();
            var pending = await rig.Client!.GetServicesAsync(rig.Token);
            Assert.True((await rig.Permissions!.DecideAsync(pending.PermissionRequestId!, HomeApprovalChoice.Accept,
                cancellationToken: rig.Token)).Succeeded);
            rig.Api!.AfterRead = (_, _) =>
            {
                if (change == "actor") rig.Actors.Current = rig.Actors.Current with { AuthenticationRevision = "retired" };
                else if (change == "installation") rig.Verifier!.Peer = rig.Verifier.Peer with { InstallationRevision = "retired" };
                else rig.Lease!.Dispose();
                return Task.CompletedTask;
            };
            var original = rig.Client.GetServicesAsync(rig.Token);
            var failure = await Record.ExceptionAsync(() => original);
            Assert.NotNull(failure);
            rig.Observe(failure!);
            Assert.Equal(1, rig.Api.Reads);
            Assert.Equal(HomePermissionRequestState.Failed,
                (await rig.Permissions.ReadRequestObservationAsync(pending.PermissionRequestId!, rig.Token))!.State);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => rig.Client.GetServicesAsync(rig.Token));
            var close = rig.Host!.CloseAndDrainAsync();
            var serverFailure = await Record.ExceptionAsync(() => close);
            Assert.NotNull(serverFailure);
            Assert.Contains(Flatten(serverFailure!), error => error is UnauthorizedAccessException);
            rig.Observe(serverFailure!);
        });

    [WindowsFact]
    public Task Startup_retries_same_manual_review_then_rechecks_original_host_after_response() =>
        WithRigAsync(async rig =>
        {
            await rig.AttachStartupAsync();
            var pending = await rig.Startup!.CheckAsync(rig.Token);
            Assert.Equal(HomeNativeStartupState.AwaitingApproval, pending.State);
            Assert.False(pending.CanStartNormally);
            Assert.True((await rig.Permissions!.DecideAsync(pending.PermissionRequestId!, HomeApprovalChoice.Accept,
                cancellationToken: rig.Token)).Succeeded);
            var accepted = await rig.Startup.CheckAsync(rig.Token);
            Assert.True(accepted.CanStartNormally);
            Assert.Equal(pending.PermissionRequestId, accepted.PermissionRequestId);
            rig.Startup.AfterOriginalClientResponse = () =>
            {
                rig.Verifier!.HostRevision = "retired-after-reply";
                return Task.CompletedTask;
            };
            var original = rig.Startup.CheckAsync(rig.Token);
            var failure = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => original);
            rig.Observe(failure);
            var close = rig.Startup.CloseAndDrainAsync();
            Assert.Same(close, rig.Startup.CloseAndDrainAsync());
            var closeFailure = await Record.ExceptionAsync(() => close);
            Assert.NotNull(closeFailure);
            rig.Observe(closeFailure!);
        });

    [WindowsFact]
    public Task Host_close_retains_held_actual_read_finally_and_same_original_close() =>
        WithRigAsync(async rig =>
        {
            Task? original = null; Task? originalClose = null;
            var entered = Signal(); var cancelled = Signal(); var release = Signal();
            Exception? primary = null; List<Exception> cleanup = [];
            try
            {
                await rig.AttachClientAsync();
                var pending = await rig.Client!.GetServicesAsync(rig.Token);
                Assert.True((await rig.Permissions!.DecideAsync(pending.PermissionRequestId!, HomeApprovalChoice.Accept,
                    cancellationToken: rig.Token)).Succeeded);
                rig.Api!.AfterRead = async (_, token) =>
                {
                    entered.TrySetResult();
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                    finally { cancelled.TrySetResult(); await release.Task; }
                };
                original = rig.Client.GetServicesAsync(rig.Token);
                await entered.Task.WaitAsync(rig.Token);
                originalClose = rig.Host!.CloseAndDrainAsync();
                Assert.Same(originalClose, rig.Host.CloseAndDrainAsync());
                await cancelled.Task.WaitAsync(rig.Token);
                Assert.False(originalClose.IsCompleted);
                release.TrySetResult();
                var serverFailure = await Record.ExceptionAsync(() => originalClose);
                Assert.NotNull(serverFailure);
                Assert.Contains(Flatten(serverFailure!), error => error is OperationCanceledException);
                rig.Observe(serverFailure!);
                var clientFailure = await Record.ExceptionAsync(() => original);
                Assert.NotNull(clientFailure);
                rig.Observe(clientFailure!);
                Assert.True(original.IsCompleted);
                Assert.True(originalClose.IsCompleted);
            }
            catch (Exception error) { primary = error; }
            finally
            {
                release.TrySetResult();
                foreach (var same in new[] { original, originalClose })
                    try { if (same is not null) await same; }
                    catch (Exception error) { if (!rig.IsObserved(error)) Add(cleanup, error); }
            }
            Throw(primary, cleanup);
        });

    [WindowsFact]
    public Task Actual_designated_bootstrap_and_app_initializer_share_the_original_provider_and_manual_startup() =>
        WithRigAsync(async rig =>
        {
            var provider = new OriginalProvider(rig);
            rig.Bootstrap = HomeNativeWindowsBootstrap.Start(new(provider, rig.Runtime!, rig.Actors),
                new Paths(rig.Root), new(rig.PipeName), rig.Lifetime!.Token);
            rig.OriginalBootstrapStart = rig.Bootstrap.OriginalStartTask;
            await rig.OriginalBootstrapStart;
            rig.AppConnection = await HomeNativeWindowsAppConnection.ConnectAsync(new(rig.PipeName), rig.Verifier!,
                Host(), Requirements(), rig.Lifetime.Token, rig.Token);
            var pending = await rig.AppConnection.Startup.CheckAsync(rig.Token);
            Assert.Equal(HomeNativeStartupState.AwaitingApproval, pending.State);
            Assert.True((await rig.Permissions!.DecideAsync(pending.PermissionRequestId!, HomeApprovalChoice.Accept,
                cancellationToken: rig.Token)).Succeeded);
            var callbacks = 0;
            rig.OriginalInitializer = rig.AppConnection.InitializeOriginalAsync(token =>
            {
                token.ThrowIfCancellationRequested();
                Assert.Same(rig.Runtime, provider.GetService(typeof(HomeCoreRuntime)));
                callbacks++;
                return Task.CompletedTask;
            }, rig.Token);
            await rig.OriginalInitializer;
            Assert.Equal(1, callbacks);
            Assert.True((await rig.AppConnection.Startup.CheckAsync(rig.Token)).CanStartNormally);
            var appClose = rig.AppConnection.CloseAndDrainAsync();
            Assert.Same(appClose, rig.AppConnection.CloseAndDrainAsync());
            await appClose;
            var homeClose = rig.Bootstrap.CloseAndDrainAsync();
            Assert.Same(homeClose, rig.Bootstrap.CloseAndDrainAsync());
            await homeClose;
        }, startHost: false);

    [WindowsTheory]
    [InlineData("session", false)]
    [InlineData("issuer", false)]
    [InlineData("session", true)]
    [InlineData("issuer", true)]
    public Task Reentrant_original_close_is_same_task_until_actual_read_finally_settles(string owner, bool throwCallback) =>
        WithRigAsync(async rig =>
        {
            var entered = Signal(); var cancelled = Signal(); var release = Signal();
            Task? reenteredClose = null;
            var callbackFailure = new InvalidOperationException("EXACT_ORIGINAL_CANCELLATION_CALLBACK");
            try
            {
                await rig.AttachDirectSessionAsync();
                var pending = await rig.OriginalSession!.GetServicesAsync(rig.Token);
                Assert.Equal("PermissionRequired", pending.Operation.Code);
                Assert.True((await rig.Permissions!.DecideAsync(pending.PermissionRequestId!, HomeApprovalChoice.Accept,
                    cancellationToken: rig.Token)).Succeeded);
                rig.Api!.AfterRead = async (_, token) =>
                {
                    using var registration = token.Register(() =>
                    {
                        reenteredClose = owner == "session" ? rig.OriginalSession!.DisposeAsync().AsTask() :
                            rig.Sessions!.DisposeAsync().AsTask();
                        cancelled.TrySetResult();
                        if (throwCallback) throw callbackFailure;
                    });
                    entered.TrySetResult();
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                    finally { await release.Task; }
                };
                rig.OriginalDirectRead = rig.OriginalSession!.GetServicesAsync(rig.Token);
                await entered.Task.WaitAsync(rig.Token);
                rig.OriginalDirectClose = owner == "session" ? rig.OriginalSession!.DisposeAsync().AsTask() :
                    rig.Sessions!.DisposeAsync().AsTask();
                await cancelled.Task.WaitAsync(rig.Token);
                Assert.Same(rig.OriginalDirectClose, reenteredClose);
                Assert.False(rig.OriginalDirectClose!.IsCompleted);
                Assert.False(rig.OriginalDirectRead!.IsCompleted);
                release.TrySetResult();
                var readFailure = await Record.ExceptionAsync(() => rig.OriginalDirectRead!);
                Assert.NotNull(readFailure);
                Assert.Contains(Flatten(readFailure!), error => error is OperationCanceledException);
                rig.Observe(readFailure!);
                var closeFailure = await Record.ExceptionAsync(() => rig.OriginalDirectClose!);
                if (throwCallback)
                {
                    Assert.NotNull(closeFailure);
                    Assert.Contains(Flatten(closeFailure!), error => ReferenceEquals(error, callbackFailure));
                    rig.Observe(closeFailure!);
                }
                else Assert.Null(closeFailure);
                Assert.True(rig.OriginalDirectRead!.IsCompleted);
                Assert.True(rig.OriginalDirectClose!.IsCompleted);
                Assert.Same(rig.OriginalDirectClose, owner == "session" ? rig.OriginalSession!.DisposeAsync().AsTask() :
                    rig.Sessions!.DisposeAsync().AsTask());
            }
            finally { release.TrySetResult(); }
        }, startHost: false);

    [WindowsFact]
    public Task App_connect_captures_original_requirements_before_actual_pipe_connect_await() =>
        WithRigAsync(async rig =>
        {
            var requiredServices = new List<HomeServiceRequirement> { new("home.core", 1), new("home.state", 1) };
            var originalRequirements = new HomeCompatibilityRequest("synthetic-native", "candidate-test-only", requiredServices);
            rig.OriginalAppConnect = HomeNativeWindowsAppConnection.ConnectAsync(new(rig.PipeName), rig.Verifier!,
                Host(), originalRequirements, rig.Lifetime!.Token, rig.Token);
            Assert.False(rig.OriginalAppConnect!.IsCompleted);
            requiredServices[0] = requiredServices[0] with { MajorVersion = 2 };
            rig.Lease = await HomeNativeSessionLease.TryAcquireAsync(rig.Actors, new Paths(rig.Root), rig.Token)
                ?? throw new InvalidOperationException("Actual isolated Home lease unavailable.");
            rig.OriginalCoreStart = rig.Runtime!.StartAsync(rig.Token);
            await rig.OriginalCoreStart;
            rig.Host = HomeNativeWindowsCoreHost.Start(rig.Sessions!, rig.Lease, new(rig.PipeName), rig.Lifetime.Token);
            rig.AppConnection = await rig.OriginalAppConnect;
            var pending = await rig.AppConnection.Startup.CheckAsync(rig.Token);
            Assert.Equal(HomeNativeStartupState.AwaitingApproval, pending.State);
            Assert.True((await rig.Permissions!.DecideAsync(pending.PermissionRequestId!, HomeApprovalChoice.Accept,
                cancellationToken: rig.Token)).Succeeded);
            var originalCheck = rig.AppConnection.Startup.CheckAsync(rig.Token);
            var accepted = await originalCheck;
            Assert.True(accepted.CanStartNormally);
            Assert.Equal(pending.PermissionRequestId, accepted.PermissionRequestId);
            Assert.Equal(2, originalRequirements.RequiredServices[0].MajorVersion);
        }, startHost: false);

    private static HomeNativeSessionHostRequirement Host() => new("synthetic-home", "SYNTHETIC_WINDOWS_FIXTURE_ONLY");
    private static HomeCompatibilityRequest Requirements() => new("synthetic-native", "candidate-test-only",
        [new("home.core", 1), new("home.state", 1)]);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static IEnumerable<Exception> Flatten(Exception error)
    {
        yield return error;
        if (error is AggregateException aggregate)
            foreach (var child in aggregate.InnerExceptions)
                foreach (var item in Flatten(child)) yield return item;
    }
    private static void Add(List<Exception> errors, Exception error)
    { if (!errors.Any(value => ReferenceEquals(value, error))) errors.Add(error); }
    private static void Throw(Exception? primary, List<Exception> cleanup)
    {
        if (primary is not null) Add(cleanup, primary);
        if (cleanup.Count == 1) ExceptionDispatchInfo.Capture(cleanup[0]).Throw();
        if (cleanup.Count > 1) throw new AggregateException("Actual Windows fixture body and independent original cleanup failed.", cleanup);
    }
    private static async Task WithRigAsync(Func<Rig, Task> body, bool startHost = true)
    {
        var rig = new Rig();
        Exception? primary = null; List<Exception> cleanup = [];
        try { await rig.OpenAsync(startHost); await body(rig); }
        catch (Exception error) { primary = error; }
        finally { try { await rig.CloseAsync(); } catch (Exception error) { Add(cleanup, error); } }
        Throw(primary, cleanup);
    }

    private sealed class Rig
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "home-windows-original-" + Guid.NewGuid().ToString("N"));
        private readonly string _pipeName = "home.windows.fixture." + Guid.NewGuid().ToString("N");
        private readonly List<Exception> _observed = [];
        internal readonly Actors Actors = new();
        internal CancellationTokenSource? Bound;
        internal CancellationTokenSource? Lifetime;
        internal HomePermissionTrustService? Permissions;
        internal HomeNativeCoreApiSessions? Sessions;
        internal HomeCoreRuntime? Runtime;
        internal HomeNativeSessionLease? Lease;
        internal HomeNativeWindowsCoreHost? Host;
        internal HomeNativeWindowsBootstrap? Bootstrap;
        internal HomeNativeWindowsAppConnection? AppConnection;
        internal Task? OriginalInitializer;
        internal Task? OriginalBootstrapStart;
        internal Task<HomeNativeWindowsAppConnection>? OriginalAppConnect;
        internal NamedPipeServerStream? DirectServer;
        internal NamedPipeClientStream? DirectPipe;
        internal Task? OriginalDirectWait;
        internal Task? OriginalDirectConnect;
        internal Task<HomeNativeCoreApiSessions.Session?>? OriginalDirectAdmission;
        internal HomeNativeCoreApiSessions.Session? OriginalSession;
        internal Task<HomeNativeCoreApiResult<IReadOnlyList<HomeServiceDescriptor>>>? OriginalDirectRead;
        internal Task? OriginalDirectClose;
        internal string Root => _root;
        internal string PipeName => _pipeName;
        internal NamedPipeClientStream? Pipe;
        internal HomeWindowsCoreClient? Client;
        internal HomeNativeWindowsStartupSession? Startup;
        internal Verifier? Verifier;
        internal CapturingApi? Api;
        internal Task<HomeCoreStateSnapshot>? OriginalCoreStart;
        internal string Principal = "";
        internal CancellationToken Token => Bound!.Token;
        internal async Task OpenAsync(bool startHost)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows kernel required; no substituted pass.");
            Bound = new CancellationTokenSource();
            Bound.CancelAfter(TimeSpan.FromSeconds(30));
            Lifetime = new CancellationTokenSource();
            Directory.CreateDirectory(_root);
            using var identity = WindowsIdentity.GetCurrent();
            Principal = "windows-sid:" + (identity.User?.Value ?? throw new InvalidOperationException("Actual Windows SID unavailable."));
            Verifier = new(Actors, Principal);
            var policy = new HomeCoreServiceReadActionPolicies();
            Permissions = new(new FileHomeCoreStateStore(Path.Combine(_root, "permissions.json")), policy.TryGet);
            IHomeCoreApi? actual = null;
            Sessions = new(Permissions, Actors, Verifier, () => actual ?? throw new InvalidOperationException("Actual API not ready."));
            Runtime = new([new HomeCoreStateService(new FileHomeCoreStateStore(Path.Combine(_root, "core.json")))],
                authorization: Sessions);
            Api = new(new HomeCoreApi(Runtime, Sessions, Actors)); actual = Api;
            if (!startHost) return;
            Lease = await HomeNativeSessionLease.TryAcquireAsync(Actors, new Paths(_root), Token)
                ?? throw new InvalidOperationException("Actual isolated Home lease unavailable.");
            OriginalCoreStart = Runtime.StartAsync(Token);
            await OriginalCoreStart;
            Host = HomeNativeWindowsCoreHost.Start(Sessions, Lease, new(_pipeName), Lifetime.Token);
            Pipe = new(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
            await Pipe.ConnectAsync(Token);
        }
        internal async Task AttachDirectSessionAsync()
        {
            Lease = await HomeNativeSessionLease.TryAcquireAsync(Actors, new Paths(_root), Token)
                ?? throw new InvalidOperationException("Actual isolated Home lease unavailable.");
            OriginalCoreStart = Runtime!.StartAsync(Token);
            await OriginalCoreStart;
            DirectServer = new(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            OriginalDirectWait = DirectServer.WaitForConnectionAsync(Token);
            DirectPipe = new(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
                TokenImpersonationLevel.Impersonation);
            OriginalDirectConnect = DirectPipe.ConnectAsync(Token);
            await OriginalDirectConnect;
            await OriginalDirectWait;
            OriginalDirectAdmission = Sessions!.AcceptWindowsPipeAsync(DirectServer, Lease, Lifetime!.Token, Token).AsTask();
            OriginalSession = await OriginalDirectAdmission
                ?? throw new UnauthorizedAccessException("The real accepted original Windows session was refused.");
        }
        internal async Task AttachClientAsync() => Client = await HomeWindowsCoreClient.AttachAsync(Pipe!, Verifier!,
            HostRequirement(), Lifetime!.Token, Token)
            ?? throw new UnauthorizedAccessException("Real Windows pipe with explicitly synthetic installed verifier was refused.");
        private static HomeNativeSessionHostRequirement HostRequirement() => HomeWindowsCoreTransportTests.Host();
        internal async Task AttachStartupAsync() => Startup = await HomeNativeWindowsStartupSession.AttachAsync(Pipe!, Verifier!,
            HostRequirement(), Requirements(), Lifetime!.Token, Token)
            ?? throw new UnauthorizedAccessException("Real original startup attachment was refused.");
        internal void Observe(Exception error) { foreach (var same in Flatten(error)) Add(_observed, same); }
        internal bool IsObserved(Exception error) => Flatten(error).All(same => _observed.Any(value => ReferenceEquals(value, same)));
        internal async Task CloseAsync()
        {
            List<Exception> failures = [];
            async Task Settle(Func<Task> acquire)
            {
                try { await acquire(); }
                catch (Exception error) { if (!IsObserved(error)) Add(failures, error); }
            }
            if (OriginalAppConnect is not null)
                await Settle(async () => { AppConnection ??= await OriginalAppConnect; });
            if (AppConnection is not null) await Settle(AppConnection.CloseAndDrainAsync);
            if (OriginalSession is not null) await Settle(() => OriginalSession.DisposeAsync().AsTask());
            if (OriginalDirectRead is not null) await Settle(() => OriginalDirectRead);
            if (OriginalDirectClose is not null) await Settle(() => OriginalDirectClose);
            if (OriginalDirectAdmission is not null) await Settle(async () => { await OriginalDirectAdmission; });
            if (OriginalDirectConnect is not null) await Settle(() => OriginalDirectConnect);
            if (OriginalDirectWait is not null) await Settle(() => OriginalDirectWait);
            if (DirectPipe is not null) await Settle(() => DirectPipe.DisposeAsync().AsTask());
            if (DirectServer is not null) await Settle(() => DirectServer.DisposeAsync().AsTask());
            if (OriginalInitializer is not null) await Settle(() => OriginalInitializer);
            if (Bootstrap is not null) await Settle(Bootstrap.CloseAndDrainAsync);
            if (OriginalBootstrapStart is not null) await Settle(() => OriginalBootstrapStart);
            if (Startup is not null) await Settle(Startup.CloseAndDrainAsync);
            if (Client is not null) await Settle(() => Client.DisposeAsync().AsTask());
            if (Pipe is not null) await Settle(() => Pipe.DisposeAsync().AsTask());
            if (Host is not null) await Settle(Host.CloseAndDrainAsync);
            if (Sessions is not null) await Settle(() => Sessions.DisposeAsync().AsTask());
            if (OriginalCoreStart is not null) await Settle(() => OriginalCoreStart);
            if (Runtime is not null) await Settle(() => Runtime.DisposeAsync().AsTask());
            try { Lease?.Dispose(); } catch (Exception error) { Add(failures, error); }
            try { Lifetime?.Dispose(); } catch (Exception error) { Add(failures, error); }
            try { Bound?.Dispose(); } catch (Exception error) { Add(failures, error); }
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
            catch (Exception error) { Add(failures, error); }
            Throw(null, failures);
        }
    }
    private sealed class OriginalProvider(Rig original) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(HomeNativeCoreApiSessions) || serviceType == typeof(IHomeCoreAuthorization) ? original.Sessions :
            serviceType == typeof(HomeCoreRuntime) ? original.Runtime :
            serviceType == typeof(IAuthenticatedResourceActorSource) ? original.Actors : null;
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        internal AuthenticatedResourceActor Current = new("windows-wire-fixture", "isolated-profile", null, null, "v1");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<AuthenticatedResourceActor?>(Current); }
    }
    private sealed class Verifier(Actors actors, string principal) :
        IHomeNativeInstalledPeerOriginalActorVerifier, IHomeNativeSessionHostVerifier
    {
        internal HomeNativeInstalledPeer Peer = new("synthetic-native", Guid.NewGuid(), "installed-fixture-v1",
            "EXPLICIT_SYNTHETIC_RECEIPT_NO_PROTECTED_INSTALLATION", new HashSet<string> { "home.core", "home.state" });
        private readonly Guid _host = Guid.NewGuid();
        internal string HostRevision = "host-fixture-v1";
        internal HomeNativeObservedPeer? LastClient;
        internal HomeNativeObservedPeer? LastHost;
        private bool Actual(HomeNativeObservedPeer observed) => observed.ProcessId == Environment.ProcessId &&
            observed.OperatingSystemPrincipalId == principal;
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer observed, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<HomeNativeInstalledPeer?>(Actual(observed) ? Peer : null); }
        public ValueTask<HomeNativeInstalledPeer?> VerifyForActorAsync(HomeNativeObservedPeer observed,
            AuthenticatedResourceActor expected, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); LastClient = observed;
            return ValueTask.FromResult<HomeNativeInstalledPeer?>(Actual(observed) && expected == actors.Current ? Peer : null);
        }
        public ValueTask<HomeNativeInstalledPeer?> VerifyHostAsync(HomeNativeObservedPeer observed,
            HomeNativeSessionHostRequirement required, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); LastHost = observed;
            return ValueTask.FromResult<HomeNativeInstalledPeer?>(Actual(observed) && required == Host() ?
                new("synthetic-home", _host, HostRevision, "EXPLICIT_SYNTHETIC_HOST_NO_PROTECTED_INSTALLATION",
                    new HashSet<string> { "home.core", "home.state" })
                { Roles = new HashSet<string> { HomeNativeSessionHostRequirement.RequiredRole } } : null);
        }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "database");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy");
    }
    private sealed class CapturingApi(IHomeCoreApi actual) : IHomeCoreApi
    {
        internal int Reads;
        internal Func<HomeCallerIdentity, CancellationToken, Task>? AfterRead;
        public Task<HomeCoreOperationResult<HomeCoreStateSnapshot>> GetStateAsync(HomeCallerIdentity caller, CancellationToken token = default) =>
            actual.GetStateAsync(caller, token);
        public async Task<HomeCoreOperationResult<IReadOnlyList<HomeServiceDescriptor>>> GetServicesAsync(
            HomeCallerIdentity caller, CancellationToken token = default)
        {
            Reads++; var result = await actual.GetServicesAsync(caller, token);
            if (AfterRead is { } after) await after(caller, token);
            return result;
        }
        public Task<HomeCoreOperationResult<HomeServiceDescriptor>> GetServiceAsync(HomeCallerIdentity caller,
            string serviceId, CancellationToken token = default) => actual.GetServiceAsync(caller, serviceId, token);
        public Task<HomeCompatibilityResult> GetCompatibilityAsync(HomeCallerIdentity caller,
            HomeCompatibilityRequest request, CancellationToken token = default) => actual.GetCompatibilityAsync(caller, request, token);
    }
}
