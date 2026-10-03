using System.Net.Sockets;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Real local sockets/held Home lease and actual canonical broker/API/store.
/// The controlled installed verifier is synthetic; signed-package/native dispatch acceptance is separate.</summary>
public sealed class HomeNativeCoreApiSessionTests
{
    [Fact]
    public async Task Public_caller_records_and_unavailable_installed_verification_cannot_issue_authority()
    {
        await using var rig = await Rig.CreateAsync();
        var copied = new HomeCallerIdentity("installed:" + rig.Verifier.Peer.InstalledApplicationId.ToString("N"),
            "installed-native", "kernel-peer+controlled-installed-runtime");
        Assert.False(await rig.Sessions.IsAllowedAsync(copied, "9to1.Home.GetServices", Scopes(), rig.Token));
        var denied = new HomeNativeCoreApiSessions(rig.Permissions, rig.Actors,
            new UnavailableHomeNativeInstalledPeerVerifier(), () => rig.Api);
        Assert.Null(await denied.AcceptUnixAsync(rig.Accepted, rig.Lease, rig.Connection.Token, rig.Token));
        Assert.Null(await rig.Sessions.AcceptUnixAsync(rig.Accepted, rig.Lease, CancellationToken.None, rig.Token));
        rig.Verifier.Peer = rig.Verifier.Peer with { AllowedServiceIds = new HashSet<string>() };
        Assert.Null(await rig.Sessions.AcceptUnixAsync(rig.Accepted, rig.Lease, rig.Connection.Token, rig.Token));
        Assert.Equal(0, rig.Api.Reads);
        Assert.Empty((await rig.Permissions.GetSnapshotAsync(cancellationToken: rig.Token)).RecentAuditEvents);
    }

    [Fact]
    public async Task Exact_broker_approval_executes_original_read_but_equal_copies_and_replay_remain_denied()
    {
        await using var rig = await Rig.CreateAsync();
        var session = await rig.AdmitAsync();
        var pending = await session.GetServicesAsync(rig.Token);
        Assert.Equal("PermissionRequired", pending.Operation.Code); Assert.False(pending.Operation.Succeeded);
        Assert.Null(pending.Operation.Value); Assert.Equal(0, rig.Api.Reads);
        Assert.Equal(pending.PermissionRequestId, (await session.GetServicesAsync(rig.Token)).PermissionRequestId);
        var original = Assert.IsType<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest>(await rig.Permissions.ReadRequestObservationAsync(pending.PermissionRequestId!, rig.Token));
        Assert.True(original.Caller.IsVerified);
        Assert.Equal("9to1.Home.GetServices", original.Scope.ActionName);
        Assert.Equal(rig.Actors.Current.ProfileId, Assert.Single(original.Scope.Objects).ObjectId);
        Assert.False((await rig.Permissions.BeginExecutionAsync(original.RequestId, rig.Token)).IsAllowed);
        Assert.True((await rig.Permissions.DecideAsync(original.RequestId, HomeApprovalChoice.Accept,
            cancellationToken: rig.Token)).Succeeded);
        rig.Api.AfterRead = async (caller, token) =>
        {
            Assert.True(await rig.Sessions.IsAllowedAsync(caller, "9to1.Home.GetServices", Scopes(), token));
            Assert.False(await rig.Sessions.IsAllowedAsync(caller with { }, "9to1.Home.GetServices", Scopes(), token));
            Assert.False(await rig.Sessions.IsAllowedAsync(caller, "9to1.Home.GetState", Scopes(), token));
            Assert.False(await rig.Sessions.IsAllowedAsync(caller, "9to1.Home.GetServices",
                new HashSet<string> { "home.services.read", "invented.scope" }, token));
        };
        var read = await session.GetServicesAsync(rig.Token);
        Assert.True(read.Operation.Succeeded); Assert.NotEmpty(read.Operation.Value!); Assert.Equal(1, rig.Api.Reads);
        Assert.Equal(original.RequestId, read.PermissionRequestId);
        var completed = await rig.Permissions.ReadRequestObservationAsync(original.RequestId, rig.Token);
        Assert.Equal(HomePermissionRequestState.Succeeded, completed!.State);
        var audit = await rig.Permissions.GetSnapshotAsync(cancellationToken: rig.Token);
        Assert.Single(audit.RecentAuditEvents, row => row.RequestId == original.RequestId && row.Kind == HomePermissionAuditKind.ExecutionStarted);
        Assert.Single(audit.RecentAuditEvents, row => row.RequestId == original.RequestId && row.Kind == HomePermissionAuditKind.ExecutionCompleted);
        Assert.False(await rig.Sessions.IsAllowedAsync(rig.Api.LastCaller!, "9to1.Home.GetServices", Scopes(), rig.Token));
        await session.DisposeAsync();
        Assert.Equal("CallerIdentityUnverified", (await session.GetServicesAsync(rig.Token)).Operation.Code);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("installation")]
    [InlineData("lease")]
    public async Task Original_authority_change_during_real_read_prevents_response_publication(string change)
    {
        await using var rig = await Rig.CreateAsync(); var session = await rig.AdmitAsync();
        var pending = await session.GetServicesAsync(rig.Token);
        Assert.True((await rig.Permissions.DecideAsync(pending.PermissionRequestId!, HomeApprovalChoice.Accept,
            cancellationToken: rig.Token)).Succeeded);
        rig.Api.AfterRead = (_, _) =>
        {
            switch (change)
            {
                case "actor": rig.Actors.Current = rig.Actors.Current with { AuthenticationRevision = "retired" }; break;
                case "installation": rig.Verifier.Peer = rig.Verifier.Peer with { InstallationRevision = "retired" }; break;
                case "lease": rig.Lease.Dispose(); break;
                default: throw new InvalidOperationException("Unknown authority boundary.");
            }
            return Task.CompletedTask;
        };
        var refused = await session.GetServicesAsync(rig.Token);
        Assert.Equal(1, rig.Api.Reads); Assert.False(refused.Operation.Succeeded); Assert.Null(refused.Operation.Value);
        Assert.Equal("PermissionDenied", refused.Operation.Code);
        Assert.Equal(HomePermissionRequestState.Failed,
            (await rig.Permissions.ReadRequestObservationAsync(pending.PermissionRequestId!, rig.Token))!.State);
    }

    [Fact]
    public async Task Actual_persistent_grant_revocation_before_original_read_publication_is_enforced()
    {
        await using var rig = await Rig.CreateAsync(); var session = await rig.AdmitAsync();
        var pending = await session.GetServicesAsync(rig.Token);
        Assert.True((await rig.Permissions.DecideAsync(pending.PermissionRequestId!, HomeApprovalChoice.AcceptAndTrust,
            cancellationToken: rig.Token)).Succeeded);
        var granted = Assert.IsType<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest>(await rig.Permissions.ReadRequestObservationAsync(pending.PermissionRequestId!, rig.Token));
        Assert.NotNull(granted.AppliedGrantId);
        rig.Api.AfterRead = async (_, token) =>
            Assert.True((await rig.Permissions.RevokeGrantAsync(granted.AppliedGrantId!, token)).Succeeded);
        var read = await session.GetServicesAsync(rig.Token);
        Assert.False(read.Operation.Succeeded); Assert.Null(read.Operation.Value); Assert.Equal("PermissionDenied", read.Operation.Code);
        Assert.Equal(1, rig.Api.Reads);
        Assert.True((await rig.Permissions.GetSnapshotAsync(cancellationToken: rig.Token)).Grants.Single().IsRevoked);
        Assert.False(await rig.Sessions.IsAllowedAsync(rig.Api.LastCaller!, "9to1.Home.GetServices", Scopes(), rig.Token));
    }

    [Fact]
    public async Task Original_connection_retirement_cancels_and_drains_the_same_read_task_and_audits_cancellation()
    {
        await using var rig = await Rig.CreateAsync(); var session = await rig.AdmitAsync();
        var pending = await session.GetServicesAsync(rig.Token);
        Assert.True((await rig.Permissions.DecideAsync(pending.PermissionRequestId!, HomeApprovalChoice.Accept,
            cancellationToken: rig.Token)).Succeeded);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Api.AfterRead = async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); };
        var original = session.GetServicesAsync(rig.Token);
        try
        {
            await entered.Task.WaitAsync(rig.Token); rig.Connection.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original);
            Assert.True(original.IsCompleted);
            Assert.Equal(HomePermissionRequestState.Cancelled,
                (await rig.Permissions.ReadRequestObservationAsync(pending.PermissionRequestId!, rig.Token))!.State);
        }
        finally { rig.Connection.Cancel(); try { await original; } catch (OperationCanceledException) when (original.IsCanceled) { } }
    }

    [Theory]
    [InlineData(HomeApprovalChoice.AcceptAndTrust)]
    [InlineData(HomeApprovalChoice.AcceptAndAlwaysTrust)]
    [InlineData(HomeApprovalChoice.GrantTemporaryTrustedAccess)]
    public async Task Newly_created_actual_grant_is_bound_to_the_same_original_approved_request(HomeApprovalChoice choice)
    {
        await using var rig = await Rig.CreateAsync(); var session = await rig.AdmitAsync();
        var pending = await session.GetServicesAsync(rig.Token);
        var warning = choice != HomeApprovalChoice.AcceptAndTrust;
        if (warning) Assert.True((await rig.Permissions.MarkAlwaysTrustWarningShownAsync(pending.PermissionRequestId!, rig.Token)).Succeeded);
        Assert.True((await rig.Permissions.DecideAsync(pending.PermissionRequestId!, choice,
            userConfirmedAlwaysTrustWarning: warning,
            temporaryOptions: choice == HomeApprovalChoice.GrantTemporaryTrustedAccess
                ? new HomeTrustGrantOptions(TimeSpan.FromMinutes(1), null, HomeTemporaryGrantFallback.ManualApproval) : null,
            cancellationToken: rig.Token)).Succeeded);
        var approved = Assert.IsType<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest>(await rig.Permissions.ReadRequestObservationAsync(pending.PermissionRequestId!, rig.Token));
        var grant = Assert.Single((await rig.Permissions.GetSnapshotAsync(cancellationToken: rig.Token)).Grants);
        Assert.Equal(grant.GrantId, approved.AppliedGrantId);
        Assert.Equal(grant.Caller, approved.Caller);
        Assert.Equal(grant.Scope.TargetAppId, approved.Scope.TargetAppId);
        Assert.Equal(grant.Scope.ActionName, approved.Scope.ActionName);
        Assert.Equal(grant.Scope.Objects, approved.Scope.Objects);
        var original = await session.GetServicesAsync(rig.Token);
        Assert.True(original.Operation.Succeeded); Assert.Equal(pending.PermissionRequestId, original.PermissionRequestId);
        Assert.Equal(HomePermissionRequestState.Succeeded,
            (await rig.Permissions.ReadRequestObservationAsync(pending.PermissionRequestId!, rig.Token))!.State);
        Assert.Equal(1, rig.Api.Reads);
    }

    [Fact]
    public async Task Compatibility_binds_verified_app_and_detaches_requirements_before_awaited_review()
    {
        await using var rig = await Rig.CreateAsync(); var session = await rig.AdmitAsync();
        var wrong = new HomeCompatibilityRequest("forged", "1", [new("home.core", 1)]);
        Assert.Equal("CallerIdentityUnverified", (await session.GetCompatibilityAsync(wrong, rig.Token)).Operation.Code);
        var mutable = new List<HomeServiceRequirement> { new("home.core", 1) };
        var pendingTask = session.GetCompatibilityAsync(new(rig.Verifier.Peer.AppId, "1", mutable), rig.Token);
        mutable.Clear(); mutable.Add(new("invented.after-snapshot", 99));
        var pending = await pendingTask;
        Assert.Equal("PermissionRequired", pending.Operation.Code);
        Assert.True((await rig.Permissions.DecideAsync(pending.PermissionRequestId!, HomeApprovalChoice.Accept,
            cancellationToken: rig.Token)).Succeeded);
        var result = await session.GetCompatibilityAsync(new(rig.Verifier.Peer.AppId, "1", [new("home.core", 1)]), rig.Token);
        Assert.True(result.Operation.Succeeded); Assert.True(result.Operation.Value!.CanStartNormally);
        Assert.Equal(pending.PermissionRequestId, result.PermissionRequestId);
    }

    [Fact]
    public async Task Actual_service_reads_project_only_installed_allowlist_and_undeclared_queries_do_no_work()
    {
        await using var rig = await Rig.CreateAsync(); var session = await rig.AdmitAsync();
        Assert.Equal("PermissionDenied", (await session.GetServiceAsync("home.widgets", rig.Token)).Operation.Code);
        Assert.Equal("PermissionDenied", (await session.GetCompatibilityAsync(
            new(rig.Verifier.Peer.AppId, "1", [new("home.widgets", 1, Required: false)]), rig.Token)).Operation.Code);
        Assert.Equal(0, rig.Api.Reads);
        Assert.Empty((await rig.Permissions.GetSnapshotAsync(cancellationToken: rig.Token)).RecentAuditEvents);
        var state = await ApproveAsync(() => session.GetStateAsync(rig.Token));
        Assert.Equal("home.core", Assert.Single(Assert.IsType<HomeCoreStateSnapshot>(state.Operation.Value).Services).ServiceId);
        var services = await ApproveAsync(() => session.GetServicesAsync(rig.Token));
        Assert.Equal("home.core", Assert.Single(services.Operation.Value!).ServiceId);
        var service = await ApproveAsync(() => session.GetServiceAsync("home.core", rig.Token));
        Assert.Equal("home.core", Assert.IsType<HomeServiceDescriptor>(service.Operation.Value).ServiceId);
        var compatibility = await ApproveAsync(() => session.GetCompatibilityAsync(
            new(rig.Verifier.Peer.AppId, "1", [new("home.core", 1)]), rig.Token));
        Assert.Equal("home.core", Assert.Single(Assert.IsType<HomeCompatibilityResult>(compatibility.Operation.Value).AcceptedServices).ServiceId);

        async Task<HomeNativeCoreApiResult<T>> ApproveAsync<T>(Func<Task<HomeNativeCoreApiResult<T>>> read)
        {
            var pending = await read(); Assert.Equal("PermissionRequired", pending.Operation.Code);
            Assert.True((await rig.Permissions.DecideAsync(pending.PermissionRequestId!, HomeApprovalChoice.Accept,
                cancellationToken: rig.Token)).Succeeded);
            var original = await read(); Assert.True(original.Operation.Succeeded); return original;
        }
    }

    [Fact]
    public async Task Installed_allowlist_change_during_actual_read_refuses_original_publication()
    {
        await using var rig = await Rig.CreateAsync(); var session = await rig.AdmitAsync();
        var pending = await session.GetServicesAsync(rig.Token);
        Assert.True((await rig.Permissions.DecideAsync(pending.PermissionRequestId!, HomeApprovalChoice.Accept,
            cancellationToken: rig.Token)).Succeeded);
        rig.Api.AfterRead = (_, _) =>
        {
            rig.Verifier.Peer = rig.Verifier.Peer with { AllowedServiceIds = new HashSet<string>() };
            return Task.CompletedTask;
        };
        var refused = await session.GetServicesAsync(rig.Token);
        Assert.False(refused.Operation.Succeeded); Assert.Null(refused.Operation.Value);
        Assert.Equal("PermissionDenied", refused.Operation.Code); Assert.Equal(1, rig.Api.Reads);
        Assert.Equal(HomePermissionRequestState.Failed,
            (await rig.Permissions.ReadRequestObservationAsync(pending.PermissionRequestId!, rig.Token))!.State);
    }

    [Fact]
    public async Task Issuer_shutdown_retains_same_task_and_waits_for_original_read_cleanup_before_retirement()
    {
        await using var rig = await Rig.CreateAsync(); var session = await rig.AdmitAsync();
        var pending = await session.GetServicesAsync(rig.Token);
        Assert.True((await rig.Permissions.DecideAsync(pending.PermissionRequestId!, HomeApprovalChoice.Accept,
            cancellationToken: rig.Token)).Succeeded);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Api.AfterRead = async (_, token) =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                cancelled.SetResult(); await release.Task.WaitAsync(rig.Token); throw;
            }
        };
        var original = session.GetServicesAsync(rig.Token);
        Task? shutdown = null; Exception? primary = null; List<Exception> cleanup = [];
        try
        {
            await entered.Task.WaitAsync(rig.Token);
            shutdown = rig.Sessions.DisposeAsync().AsTask();
            Assert.Same(shutdown, rig.Sessions.DisposeAsync().AsTask());
            await cancelled.Task.WaitAsync(rig.Token);
            Assert.False(original.IsCompleted); Assert.False(shutdown.IsCompleted);
            release.SetResult(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original);
            await shutdown.WaitAsync(rig.Token);
            Assert.True(original.IsCanceled); Assert.True(shutdown.IsCompletedSuccessfully);
            Assert.Null(await rig.Sessions.AcceptUnixAsync(rig.Accepted, rig.Lease, rig.Connection.Token, rig.Token));
            Assert.Equal("CallerIdentityUnverified", (await session.GetServicesAsync(rig.Token)).Operation.Code);
            Assert.Equal(HomePermissionRequestState.Cancelled,
                (await rig.Permissions.ReadRequestObservationAsync(pending.PermissionRequestId!, rig.Token))!.State);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            release.TrySetResult();
            try { await original; }
            catch (OperationCanceledException) when (original.IsCanceled && cancelled.Task.IsCompletedSuccessfully) { }
            catch (Exception error) { if (!ReferenceEquals(error, primary)) cleanup.Add(error); }
            try { if (shutdown is not null) await shutdown; }
            catch (Exception error) { if (!ReferenceEquals(error, primary) && !cleanup.Any(row => ReferenceEquals(row, error))) cleanup.Add(error); }
        }
        if (cleanup.Count != 0) throw new AggregateException("Original issuer shutdown test and read cleanup failures retained.",
            primary is null ? cleanup : new[] { primary }.Concat(cleanup));
        if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
    }

    [Fact]
    public async Task Session_retirement_started_first_remains_in_issuer_custody_until_original_read_cleanup_settles()
    {
        await using var rig = await Rig.CreateAsync(); var session = await rig.AdmitAsync();
        var pending = await session.GetServicesAsync(rig.Token);
        Assert.True((await rig.Permissions.DecideAsync(pending.PermissionRequestId!, HomeApprovalChoice.Accept,
            cancellationToken: rig.Token)).Succeeded);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Api.AfterRead = async (_, token) =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                cancelled.SetResult(); await release.Task.WaitAsync(rig.Token); throw;
            }
        };
        var original = session.GetServicesAsync(rig.Token);
        Task? retirement = null; Task? shutdown = null;
        Exception? primary = null; List<Exception> cleanup = [];
        try
        {
            await entered.Task.WaitAsync(rig.Token);
            retirement = session.DisposeAsync().AsTask();
            await cancelled.Task.WaitAsync(rig.Token);
            Assert.False(original.IsCompleted); Assert.False(retirement.IsCompleted);
            shutdown = rig.Sessions.DisposeAsync().AsTask();
            Assert.Same(shutdown, rig.Sessions.DisposeAsync().AsTask());
            Assert.False(shutdown.IsCompleted);
            release.SetResult(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original);
            await retirement.WaitAsync(rig.Token); await shutdown.WaitAsync(rig.Token);
            Assert.True(original.IsCanceled); Assert.True(retirement.IsCompletedSuccessfully);
            Assert.True(shutdown.IsCompletedSuccessfully);
            Assert.Null(await rig.Sessions.AcceptUnixAsync(rig.Accepted, rig.Lease, rig.Connection.Token, rig.Token));
            Assert.Equal("CallerIdentityUnverified", (await session.GetServicesAsync(rig.Token)).Operation.Code);
            Assert.Equal(HomePermissionRequestState.Cancelled,
                (await rig.Permissions.ReadRequestObservationAsync(pending.PermissionRequestId!, rig.Token))!.State);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            release.TrySetResult();
            try { await original; }
            catch (OperationCanceledException) when (original.IsCanceled && cancelled.Task.IsCompletedSuccessfully) { }
            catch (Exception error) { if (!ReferenceEquals(error, primary)) cleanup.Add(error); }
            foreach (var actual in new[] { retirement, shutdown })
                try { if (actual is not null) await actual; }
                catch (Exception error)
                { if (!ReferenceEquals(error, primary) && !cleanup.Any(row => ReferenceEquals(row, error))) cleanup.Add(error); }
        }
        if (cleanup.Count != 0) throw new AggregateException("Original session and issuer retirement failures retained.",
            primary is null ? cleanup : new[] { primary }.Concat(cleanup));
        if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private static IReadOnlySet<string> Scopes() => new HashSet<string> { "home.services.read" };

    private sealed class Rig : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "home-core-api-" + Guid.NewGuid().ToString("N"));
        private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(30));
        private readonly Socket _listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        private readonly Socket _client = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        internal readonly CancellationTokenSource Connection = new();
        internal readonly Actors Actors = new();
        internal readonly Verifier Verifier;
        internal readonly HomePermissionTrustService Permissions;
        internal readonly HomeNativeCoreApiSessions Sessions;
        internal readonly HomeCoreRuntime Runtime;
        internal readonly CapturingApi Api;
        internal Socket Accepted { get; private set; } = null!;
        internal HomeNativeSessionLease Lease { get; private set; } = null!;
        internal CancellationToken Token => _deadline.Token;
        private HomeNativeCoreApiSessions.Session? _session;
        private Rig()
        {
            Verifier = new(Actors);
            var policy = new HomeCoreServiceReadActionPolicies();
            Permissions = new(new FileHomeCoreStateStore(Path.Combine(_root, "permissions.json")), policy.TryGet);
            IHomeCoreApi? api = null;
            Sessions = new(Permissions, Actors, Verifier, () => api ?? throw new InvalidOperationException("Canonical API not initialized."));
            Runtime = new(authorization: Sessions);
            Api = new(new HomeCoreApi(Runtime, Sessions, Actors)); api = Api;
        }
        internal static async Task<Rig> CreateAsync()
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This actual Unix peer fixture requires Linux; no synthetic native success is substituted.");
            var rig = new Rig();
            try
            {
                Directory.CreateDirectory(rig._root);
                var address = new UnixDomainSocketEndPoint(Path.Combine(rig._root, "peer.sock"));
                rig._listener.Bind(address); rig._listener.Listen(1);
                await rig._client.ConnectAsync(address, rig.Token); rig.Accepted = await rig._listener.AcceptAsync(rig.Token);
                var observed = HomeNativePeerObservation.FromAcceptedUnixSocket(rig.Accepted);
                Assert.Equal(Environment.ProcessId, observed!.ProcessId);
                rig.Lease = await HomeNativeSessionLease.TryAcquireAsync(rig.Actors, new Paths(rig._root), rig.Token)
                    ?? throw new InvalidOperationException("The actual isolated Home lease was not acquired.");
                await rig.Runtime.StartAsync(rig.Token); return rig;
            }
            catch (Exception primary)
            {
                try { await rig.DisposeAsync(); }
                catch (Exception cleanup) when (!ReferenceEquals(primary, cleanup)) { throw new AggregateException(primary, cleanup); }
                throw;
            }
        }
        internal async Task<HomeNativeCoreApiSessions.Session> AdmitAsync() => _session =
            await Sessions.AcceptUnixAsync(Accepted, Lease, Connection.Token, Token)
            ?? throw new UnauthorizedAccessException("Synthetic installed peer with actual socket/lease was refused.");
        public async ValueTask DisposeAsync()
        {
            List<Exception> failures = [];
            void Attempt(Action action) { try { action(); } catch (Exception e) { failures.Add(e); } }
            Attempt(Connection.Cancel);
            try { if (_session is not null) await _session.DisposeAsync(); } catch (Exception e) { failures.Add(e); }
            try { await Sessions.DisposeAsync(); } catch (Exception e) { failures.Add(e); }
            try { await Runtime.DisposeAsync(); } catch (Exception e) { failures.Add(e); }
            Attempt(() => Accepted?.Dispose()); Attempt(() => Lease?.Dispose());
            Attempt(_client.Dispose); Attempt(_listener.Dispose); Attempt(Connection.Dispose); Attempt(_deadline.Dispose);
            Attempt(() => { if (Directory.Exists(_root)) Directory.Delete(_root, true); });
            if (failures.Count != 0) throw new AggregateException("Original fixture cleanup failures retained.", failures);
        }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        internal AuthenticatedResourceActor Current = new("native-test", "fresh-profile", null, null, "v1");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<AuthenticatedResourceActor?>(Current); }
    }
    private sealed class Verifier(Actors actors) : IHomeNativeInstalledPeerOriginalActorVerifier
    {
        internal HomeNativeInstalledPeer Peer = new("synthetic-native", Guid.NewGuid(), "installed-v1", "synthetic-executable",
            new HashSet<string> { "home.core" });
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer observedPeer, CancellationToken token)
            => ValueTask.FromResult<HomeNativeInstalledPeer?>(null); // no original actor: no admission.
        public ValueTask<HomeNativeInstalledPeer?> VerifyForActorAsync(HomeNativeObservedPeer observed,
            AuthenticatedResourceActor originalActor, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<HomeNativeInstalledPeer?>(observed.ProcessId == Environment.ProcessId &&
                observed.OperatingSystemPrincipalId.StartsWith("unix-euid:", StringComparison.Ordinal) && originalActor == actors.Current ? Peer : null);
        }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root; public string DatabasePath => Path.Combine(root, "database");
        public string BrowserProfileDirectory => Path.Combine(root, "browser"); public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs"); public string LegacyStatePath => Path.Combine(root, "legacy");
    }
    private sealed class CapturingApi(IHomeCoreApi actual) : IHomeCoreApi
    {
        internal int Reads; internal HomeCallerIdentity? LastCaller;
        internal Func<HomeCallerIdentity, CancellationToken, Task>? AfterRead;
        public Task<HomeCoreOperationResult<HomeCoreStateSnapshot>> GetStateAsync(HomeCallerIdentity caller, CancellationToken token = default)
            => actual.GetStateAsync(caller, token);
        public async Task<HomeCoreOperationResult<IReadOnlyList<HomeServiceDescriptor>>> GetServicesAsync(HomeCallerIdentity caller, CancellationToken token = default)
        {
            LastCaller = caller; Reads++; var original = await actual.GetServicesAsync(caller, token);
            if (AfterRead is { } hook) await hook(caller, token); return original;
        }
        public Task<HomeCoreOperationResult<HomeServiceDescriptor>> GetServiceAsync(HomeCallerIdentity caller, string id, CancellationToken token = default)
            => actual.GetServiceAsync(caller, id, token);
        public Task<HomeCompatibilityResult> GetCompatibilityAsync(HomeCallerIdentity caller, HomeCompatibilityRequest request, CancellationToken token = default)
            => actual.GetCompatibilityAsync(caller, request, token);
    }
}
