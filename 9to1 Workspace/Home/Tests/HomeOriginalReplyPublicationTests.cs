using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeOriginalReplyPublicationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Retired_actor_or_installation_refuses_the_original_reply_fence(bool changeInstallation) =>
        WithRigAsync(async rig =>
        {
            await rig.Session.DemandOriginalCurrentAsync(rig.Token);
            if (changeInstallation) rig.Verifier.Revision = "changed";
            else rig.Actors.Current = rig.Actors.Current with { AuthenticationRevision = "changed" };
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Session.DemandOriginalCurrentAsync(rig.Token).AsTask());
        });

    [Fact]
    public Task Same_reply_currentness_read_and_original_finally_remain_owned_by_session_and_issuer_close() =>
        WithRigAsync(async rig =>
        {
            var entered = Signal(); var cleanupEntered = Signal(); var release = Signal();
            Task? original = null; Task? sessionClose = null; Task? issuerClose = null;
            Exception? primary = null; Exception? observed = null; List<Exception> cleanup = [];
            rig.Actors.Read = async token =>
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { cleanupEntered.TrySetResult(); await release.Task; }
                return rig.Actors.Current;
            };
            try
            {
                original = rig.Session.DemandOriginalCurrentAsync(rig.Token).AsTask();
                await entered.Task.WaitAsync(rig.Token);
                sessionClose = rig.Session.DisposeAsync().AsTask();
                await cleanupEntered.Task.WaitAsync(rig.Token);
                issuerClose = rig.Issuer.DisposeAsync().AsTask();
                Assert.False(original.IsCompleted); Assert.False(sessionClose.IsCompleted); Assert.False(issuerClose.IsCompleted);
                Assert.Same(issuerClose, rig.Issuer.DisposeAsync().AsTask());
                release.TrySetResult();
                observed = await Record.ExceptionAsync(() => original);
                Assert.IsAssignableFrom<OperationCanceledException>(observed);
                await sessionClose; await issuerClose;
                Assert.True(original.IsCompleted); Assert.True(sessionClose.IsCompleted); Assert.True(issuerClose.IsCompleted);
            }
            catch (Exception error) { primary = error; }
            finally
            {
                release.TrySetResult();
                try { rig.Bound.Cancel(); } catch (Exception error) { Add(cleanup, error); }
                if (original is not null)
                    try { await original; } catch (Exception error) { if (!ReferenceEquals(error, observed)) Add(cleanup, error); }
                if (sessionClose is not null) try { await sessionClose; } catch (Exception error) { Add(cleanup, error); }
                if (issuerClose is not null) try { await issuerClose; } catch (Exception error) { Add(cleanup, error); }
            }
            Throw(primary, cleanup);
        });

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Add(List<Exception> errors, Exception error)
    { if (!errors.Any(previous => ReferenceEquals(previous, error))) errors.Add(error); }
    private static void Throw(Exception? primary, List<Exception> cleanup)
    {
        List<Exception> errors = [];
        if (primary is not null) Add(errors, primary);
        foreach (var error in cleanup) Add(errors, error);
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException(errors);
    }
    private static async Task WithRigAsync(Func<Rig, Task> body)
    {
        var rig = new Rig(); Exception? primary = null; List<Exception> cleanup = [];
        try { await rig.OpenAsync(); await body(rig); }
        catch (Exception error) { primary = error; }
        finally { try { await rig.CloseAsync(); } catch (Exception error) { Add(cleanup, error); } }
        Throw(primary, cleanup);
    }
    private sealed class Rig
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "home-reply-" + Guid.NewGuid().ToString("N"));
        internal readonly CancellationTokenSource Bound = new(TimeSpan.FromSeconds(30));
        private readonly CancellationTokenSource _connection = new();
        private readonly Socket _listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        private readonly Socket _client = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        private Socket? _accepted; private HomeNativeSessionLease? _lease;
        internal readonly Actors Actors = new();
        internal readonly Verifier Verifier = new();
        internal HomeNativeCoreApiSessions Issuer { get; private set; } = null!;
        internal HomeNativeCoreApiSessions.Session Session { get; private set; } = null!;
        internal CancellationToken Token => Bound.Token;
        internal async Task OpenAsync()
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This fixture uses the actual accepted Unix socket.");
            Directory.CreateDirectory(_root); var endpoint = new UnixDomainSocketEndPoint(Path.Combine(_root, "original.sock"));
            _listener.Bind(endpoint); _listener.Listen(1);
            await _client.ConnectAsync(endpoint, Token); _accepted = await _listener.AcceptAsync(Token);
            _lease = await HomeNativeSessionLease.TryAcquireAsync(Actors, new Paths(_root), Token)
                ?? throw new InvalidOperationException("The actual isolated Home lease was unavailable.");
            var permissions = new HomePermissionTrustService(new FileHomeCoreStateStore(Path.Combine(_root, "permissions.json")),
                new HomeCoreServiceReadActionPolicies().TryGet);
            Issuer = new(permissions, Actors, Verifier, () => throw new InvalidOperationException("A reply fence grants no Core action."));
            Session = await Issuer.AcceptUnixAsync(_accepted, _lease, _connection.Token, Token)
                ?? throw new UnauthorizedAccessException("The original accepted socket/lease was refused.");
        }
        internal async Task CloseAsync()
        {
            List<Exception> errors = [];
            void Attempt(Action action) { try { action(); } catch (Exception error) { Add(errors, error); } }
            Attempt(Bound.Cancel); Attempt(_connection.Cancel);
            if (Session is not null) try { await Session.DisposeAsync(); } catch (Exception error) { Add(errors, error); }
            if (Issuer is not null) try { await Issuer.DisposeAsync(); } catch (Exception error) { Add(errors, error); }
            Attempt(() => _accepted?.Dispose()); Attempt(() => _lease?.Dispose());
            Attempt(_client.Dispose); Attempt(_listener.Dispose); Attempt(_connection.Dispose); Attempt(Bound.Dispose);
            Attempt(() => { if (Directory.Exists(_root)) Directory.Delete(_root, true); });
            Throw(null, errors);
        }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        internal AuthenticatedResourceActor Current = new("reply-fixture", "original-profile", null, null, "original");
        internal Func<CancellationToken, ValueTask<AuthenticatedResourceActor?>>? Read;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Read is null ? ValueTask.FromResult<AuthenticatedResourceActor?>(Current) : Read(token);
        }
    }
    private sealed class Verifier : IHomeNativeInstalledPeerOriginalActorVerifier
    {
        internal string Revision = "original";
        private readonly Guid _installed = Guid.NewGuid();
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer peer, CancellationToken token) =>
            ValueTask.FromResult<HomeNativeInstalledPeer?>(null);
        public ValueTask<HomeNativeInstalledPeer?> VerifyForActorAsync(HomeNativeObservedPeer peer, AuthenticatedResourceActor actor, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<HomeNativeInstalledPeer?>(peer.ProcessId == Environment.ProcessId &&
                peer.OperatingSystemPrincipalId.StartsWith("unix-euid:", StringComparison.Ordinal) ?
                new("synthetic-reply", _installed, Revision, "synthetic-executable", new HashSet<string> { "home.core" }) : null);
        }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root; public string DatabasePath => Path.Combine(root, "db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs"); public string LegacyStatePath => Path.Combine(root, "legacy");
    }
}

