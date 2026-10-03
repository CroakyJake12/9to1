using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Actual accepted Unix socket, held Home lease, private Core Session and bound
/// AgentConnection/Work tasks. Installed verification is explicitly synthetic; no signed/native acceptance is claimed.</summary>
public sealed class HomeAgentOriginalConnectionDrainTests
{
    [Fact]
    public Task Owned_cancellation_through_same_Core_connection_preserves_provenance_and_drains_original_finally() =>
        WithRigAsync(async rig =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken originalOuter = default;
            OperationCanceledException? originalInner = null;
            OperationCanceledException? observed = null;
            Task? workClose = null; Task? sessionClose = null; Task? issuerClose = null;
            var calls = 0;
            var original = rig.Work.RunAsync(outer =>
            {
                originalOuter = outer;
                return rig.Connection.RunAsync<bool>(async inner =>
                {
                    calls++; entered.TrySetResult();
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, inner); }
                    catch (OperationCanceledException error) { originalInner = error; throw; }
                    finally { cleanup.TrySetResult(); await release.Task.ConfigureAwait(false); }
                    return true;
                }, outer);
            }, rig.Token);
            Exception? primary = null; List<Exception> failures = [];
            try
            {
                await entered.Task.WaitAsync(rig.Token);
                workClose = rig.Work.CloseAsync();
                await cleanup.Task.WaitAsync(rig.Token);
                sessionClose = rig.Session.DisposeAsync().AsTask();
                issuerClose = rig.Sessions.DisposeAsync().AsTask();
                Assert.Same(workClose, rig.Work.CloseAsync());
                Assert.False(workClose.IsCompleted); Assert.False(sessionClose.IsCompleted);
                Assert.False(issuerClose.IsCompleted); Assert.False(original.IsCompleted);
                release.TrySetResult();
                observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original);
                Assert.Equal(originalOuter, observed.CancellationToken);
                Assert.True(originalOuter.IsCancellationRequested);
                var innerFailure = Assert.IsAssignableFrom<OperationCanceledException>(observed.InnerException);
                Assert.Same(originalInner, innerFailure);
                Assert.NotEqual(originalOuter, innerFailure.CancellationToken);
                Assert.True(innerFailure.CancellationToken.IsCancellationRequested);
                await workClose.WaitAsync(rig.Token);
                await sessionClose.WaitAsync(rig.Token); await issuerClose.WaitAsync(rig.Token);
                Assert.Equal(1, calls); Assert.True(original.IsCompleted);
            }
            catch (Exception error) { primary = error; }
            finally
            {
                Attempt(failures, () => release.TrySetResult());
                Attempt(failures, rig.Bound.Cancel);
                try { await original.WaitAsync(TimeSpan.FromSeconds(20), CancellationToken.None); }
                catch (Exception error) when (ReferenceEquals(error, observed) || ReferenceEquals(error, primary)) { }
                catch (Exception error) { Add(failures, error); }
                foreach (var same in new[] { workClose, sessionClose, issuerClose })
                    if (same is not null)
                        try { await same.WaitAsync(TimeSpan.FromSeconds(20), CancellationToken.None); }
                        catch (Exception error) { Add(failures, error); }
            }
            Throw(primary, failures);
        });

    [Fact]
    public Task Caller_cancellation_in_same_Core_connection_survives_later_original_Work_close() =>
        WithRigAsync(async rig =>
        {
            using var caller = new CancellationTokenSource();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ownerCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ownerLifetime = rig.Work.Lifetime;
            CancellationTokenRegistration ownerWitness = default;
            CancellationToken originalOuter = default;
            OperationCanceledException? originalInner = null;
            OperationCanceledException? observed = null;
            Task? close = null;
            var original = rig.Work.RunAsync(outer =>
            {
                originalOuter = outer;
                return rig.Connection.RunAsync<bool>(async inner =>
                {
                    entered.TrySetResult();
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, inner); }
                    catch (OperationCanceledException error) { originalInner = error; throw; }
                    finally { cleanup.TrySetResult(); await release.Task.ConfigureAwait(false); }
                    return true;
                }, outer);
            }, caller.Token);
            Exception? primary = null; List<Exception> failures = [];
            try
            {
                ownerWitness = ownerLifetime.Register(() => ownerCancelled.TrySetResult());
                await entered.Task.WaitAsync(rig.Token);
                caller.Cancel();
                await cleanup.Task.WaitAsync(rig.Token);
                Assert.True(caller.IsCancellationRequested);
                Assert.False(ownerLifetime.IsCancellationRequested);
                Assert.False(original.IsCompleted);
                close = rig.Work.CloseAsync();
                Assert.Same(close, rig.Work.CloseAsync());
                await ownerCancelled.Task.WaitAsync(rig.Token);
                Assert.True(ownerLifetime.IsCancellationRequested);
                Assert.False(rig.Token.IsCancellationRequested);
                Assert.False(original.IsCompleted);
                Assert.False(close.IsCompleted);
                release.TrySetResult();
                observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original);
                Assert.Equal(originalOuter, observed.CancellationToken);
                Assert.NotEqual(caller.Token, observed.CancellationToken);
                Assert.True(originalOuter.IsCancellationRequested);
                Assert.Same(originalInner, observed.InnerException);
                rig.ExpectedWorkCloseFailure = observed;
                var closeFailure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => close);
                Assert.Same(observed, closeFailure);
                Assert.True(original.IsCompleted);
                Assert.True(close.IsCompleted);
            }
            catch (Exception error) { primary = error; }
            finally
            {
                Attempt(failures, () => release.TrySetResult());
                Attempt(failures, caller.Cancel);
                try { await original.WaitAsync(TimeSpan.FromSeconds(20), CancellationToken.None); }
                catch (Exception error) when (ReferenceEquals(error, observed) || ReferenceEquals(error, primary)) { }
                catch (Exception error) { Add(failures, error); }
                if (close is not null)
                    try { await close.WaitAsync(TimeSpan.FromSeconds(20), CancellationToken.None); }
                    catch (Exception error) when (ReferenceEquals(error, observed) || ReferenceEquals(error, primary)) { }
                    catch (Exception error) { Add(failures, error); }
                Attempt(failures, ownerWitness.Dispose);
            }
            Throw(primary, failures);
        });

    [Theory]
    [InlineData("default")]
    [InlineData("foreign")]
    [InlineData("inner-unrequested")]
    public Task Unrelated_or_unrequested_cancellation_preserves_exact_failure_in_same_bound_connection(string kind) =>
        WithRigAsync(async rig =>
        {
            using var foreign = new CancellationTokenSource();
            foreign.Cancel();
            OperationCanceledException? sameFailure = null;
            var original = rig.Connection.RunAsync<bool>(inner =>
            {
                var token = kind switch
                {
                    "default" => CancellationToken.None,
                    "foreign" => foreign.Token,
                    "inner-unrequested" => inner,
                    _ => throw new InvalidOperationException("Unknown cancellation provenance fixture.")
                };
                Assert.False(inner.IsCancellationRequested);
                sameFailure = new OperationCanceledException("Synthetic unrelated cancellation", null, token);
                return Task.FromException<bool>(sameFailure);
            }, rig.Token);
            var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original);
            Assert.Same(sameFailure, observed); Assert.Null(observed.InnerException);
            Assert.False(rig.Token.IsCancellationRequested);
        });

    private static async Task WithRigAsync(Func<Rig, Task> action)
    {
        var rig = new Rig(); Exception? primary = null; List<Exception> failures = [];
        try { await rig.OpenAsync(); await action(rig); }
        catch (Exception error) { primary = error; }
        try { await rig.CloseAsync(); } catch (Exception error) { Add(failures, error); }
        Throw(primary, failures);
    }
    private static void Attempt(List<Exception> failures, Action action)
    { try { action(); } catch (Exception error) { Add(failures, error); } }
    private static void Add(List<Exception> failures, Exception error)
    { if (!failures.Any(existing => ReferenceEquals(existing, error))) failures.Add(error); }
    private static void Throw(Exception? primary, IEnumerable<Exception> failures)
    {
        var all = failures.Where(error => !ReferenceEquals(error, primary)).ToList();
        if (primary is not null) all.Insert(0, primary);
        if (all.Count == 1) ExceptionDispatchInfo.Capture(all[0]).Throw();
        if (all.Count > 1) throw new AggregateException("Original connection fixture and independent drains failed.", all);
    }

    private sealed class Rig
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "home-agent-connection-" + Guid.NewGuid().ToString("N"));
        internal readonly CancellationTokenSource Bound = new(TimeSpan.FromSeconds(30));
        private readonly CancellationTokenSource _connectionLifetime = new();
        private readonly Socket _listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        private readonly Socket _client = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        private Socket? _accepted;
        private HomeNativeSessionLease? _lease;
        internal HomeNativeCoreApiSessions Sessions { get; private set; } = null!;
        internal HomeNativeCoreApiSessions.Session Session { get; private set; } = null!;
        internal HomeNativeCoreApiSessions.AgentConnection Connection { get; private set; } = null!;
        internal HomeAgentOriginalWork Work { get; } = new();
        internal Exception? ExpectedWorkCloseFailure;
        internal CancellationToken Token => Bound.Token;
        internal async Task OpenAsync()
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The actual Unix peer fixture requires Linux.");
            Directory.CreateDirectory(_root);
            var endpoint = new UnixDomainSocketEndPoint(Path.Combine(_root, "original.sock"));
            _listener.Bind(endpoint); _listener.Listen(1);
            await _client.ConnectAsync(endpoint, Token); _accepted = await _listener.AcceptAsync(Token);
            var actors = new Actors();
            _lease = await HomeNativeSessionLease.TryAcquireAsync(actors, new Paths(_root), Token)
                ?? throw new InvalidOperationException("Actual isolated Home lease unavailable.");
            var permissions = new HomePermissionTrustService(new FileHomeCoreStateStore(Path.Combine(_root, "permissions.json")),
                new HomeCoreServiceReadActionPolicies().TryGet);
            Sessions = new(permissions, actors, new Verifier(actors),
                () => throw new InvalidOperationException("No Core read is requested by this connection drain fixture."));
            Session = await Sessions.AcceptUnixAsync(_accepted, _lease, _connectionLifetime.Token, Token)
                ?? throw new UnauthorizedAccessException("The actual socket/lease with synthetic installed verifier was refused.");
            Connection = await Sessions.BindOriginalAgentAsync(Session, Token);
            Work.BindConnection(Connection);
        }
        internal async Task CloseAsync()
        {
            List<Exception> failures = [];
            Attempt(failures, Bound.Cancel); Attempt(failures, _connectionLifetime.Cancel);
            try { await Work.CloseAsync(); }
            catch (Exception error) when (ReferenceEquals(error, ExpectedWorkCloseFailure)) { }
            catch (Exception error) { Add(failures, error); }
            if (Session is not null)
                try { await Session.DisposeAsync(); } catch (Exception error) { Add(failures, error); }
            if (Sessions is not null)
                try { await Sessions.DisposeAsync(); } catch (Exception error) { Add(failures, error); }
            Attempt(failures, () => _accepted?.Dispose()); Attempt(failures, () => _lease?.Dispose());
            Attempt(failures, _client.Dispose); Attempt(failures, _listener.Dispose);
            Attempt(failures, _connectionLifetime.Dispose); Attempt(failures, Bound.Dispose);
            Attempt(failures, () => { if (Directory.Exists(_root)) Directory.Delete(_root, true); });
            Throw(null, failures);
        }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        internal readonly AuthenticatedResourceActor Actor = new("native-agent-fixture", "original-profile", null, null, "fixture1");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<AuthenticatedResourceActor?>(Actor); }
    }
    private sealed class Verifier(Actors actors) : IHomeNativeInstalledPeerOriginalActorVerifier
    {
        private readonly HomeNativeInstalledPeer _peer = new("synthetic-agent", Guid.NewGuid(), "fixture1", "synthetic-executable",
            new HashSet<string> { "home.core", "home.agent" });
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer observed, CancellationToken token) =>
            ValueTask.FromResult<HomeNativeInstalledPeer?>(null);
        public ValueTask<HomeNativeInstalledPeer?> VerifyForActorAsync(HomeNativeObservedPeer observed,
            AuthenticatedResourceActor actor, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<HomeNativeInstalledPeer?>(observed.ProcessId == Environment.ProcessId &&
                observed.OperatingSystemPrincipalId.StartsWith("unix-euid:", StringComparison.Ordinal) && actor == actors.Actor ? _peer : null);
        }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root; public string DatabasePath => Path.Combine(root, "db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy");
    }
}
