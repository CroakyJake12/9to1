using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Actual local sockets, lease, canonical store/broker/runtime/API. Installed peer
/// verification is deliberately synthetic. These authored facts do not prove signed native dispatch.</summary>
public sealed class HomeUnixCoreTransportTests
{
    [Fact]
    public Task Legacy_first_frame_keeps_original_default_discovery_without_a_second_socket_read() =>
        WithRigAsync(async rig =>
        {
            rig.StartServer();
            var response = await HomeUnixDiscoveryClient.DiscoverAsync(rig.ClientSocket, rig.Verifier,
                Host(), [Requirement()], rig.Token);
            Assert.Equal("HomeDiscoveryReady", response.Code);
            Assert.Equal("home.core", Assert.Single(response.Snapshot!.Services).ServiceId);
            await rig.Server!.WaitAsync(rig.Token);
            Assert.Equal(0, rig.Api.Reads);
            Assert.Empty((await rig.Permissions.GetSnapshotAsync(cancellationToken: rig.Token)).RecentAuditEvents);
        });

    [Fact]
    public Task Same_Core_connection_preserves_pending_review_across_genuine_65_second_idle_and_original_retry() =>
        WithRigAsync(async rig =>
        {
            rig.StartServer(); await rig.AttachAsync();
            var pending = await rig.Client!.GetServicesAsync(rig.Token);
            Assert.Equal("PermissionRequired", pending.Operation.Code); Assert.Null(pending.Operation.Value);
            Assert.Equal(0, rig.Api.Reads);
            await Task.Delay(TimeSpan.FromSeconds(65), rig.Token);
            Assert.False(rig.Server!.IsCompleted);
            var same = await rig.Client.GetServicesAsync(rig.Token);
            Assert.Equal(pending.PermissionRequestId, same.PermissionRequestId);
            Assert.True((await rig.Permissions.DecideAsync(pending.PermissionRequestId!, HomeApprovalChoice.Accept,
                cancellationToken: rig.Token)).Succeeded);
            var read = await rig.Client.GetServicesAsync(rig.Token);
            Assert.True(read.Operation.Succeeded);
            Assert.Equal(pending.PermissionRequestId, read.PermissionRequestId);
            Assert.Equal("home.core", Assert.Single(read.Operation.Value!).ServiceId);
            Assert.Equal(1, rig.Api.Reads);
            Assert.Equal(HomePermissionRequestState.Succeeded,
                (await rig.Permissions.ReadRequestObservationAsync(pending.PermissionRequestId!, rig.Token))!.State);
        });

    [Theory]
    [InlineData("GetState")]
    [InlineData("GetServices")]
    [InlineData("GetService")]
    [InlineData("GetCompatibility")]
    public Task Four_read_operations_use_the_canonical_private_session_and_actual_pending_broker(string operation) =>
        WithRigAsync(async rig =>
        {
            rig.StartServer(); await rig.AttachAsync();
            string? requestId = operation switch
            {
                "GetState" => (await rig.Client!.GetStateAsync(rig.Token)).PermissionRequestId,
                "GetServices" => (await rig.Client!.GetServicesAsync(rig.Token)).PermissionRequestId,
                "GetService" => (await rig.Client!.GetServiceAsync("home.core", rig.Token)).PermissionRequestId,
                "GetCompatibility" => (await rig.Client!.GetCompatibilityAsync(
                    new("synthetic-native", "1", [Requirement()]), rig.Token)).PermissionRequestId,
                _ => throw new InvalidOperationException("Unexpected test operation."),
            };
            Assert.NotNull(requestId);
            var original = Assert.IsType<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest>(
                await rig.Permissions.ReadRequestObservationAsync(requestId!, rig.Token));
            Assert.True(original.Caller.IsVerified);
            Assert.Equal("9to1.Home." + operation, original.Scope.ActionName);
            var scopedObject = Assert.Single(original.Scope.Objects);
            Assert.Equal(operation == "GetService" ? "home.service" : "home.service-registry", scopedObject.ObjectType);
            Assert.Equal(operation == "GetService" ? "home.core" : rig.Actors.Current.ProfileId, scopedObject.ObjectId);
            Assert.Equal(HomePermissionRequestState.PendingApproval, original.State);
            Assert.Equal(0, rig.Api.Reads);
        });

    [Theory]
    [InlineData("actor")]
    [InlineData("installation")]
    [InlineData("lease")]
    public Task Current_authority_retirement_during_actual_read_prevents_data_publication(string change) =>
        WithRigAsync(async rig =>
        {
            rig.StartServer(); await rig.AttachAsync();
            var pending = await rig.Client!.GetServicesAsync(rig.Token);
            Assert.True((await rig.Permissions.DecideAsync(pending.PermissionRequestId!, HomeApprovalChoice.Accept,
                cancellationToken: rig.Token)).Succeeded);
            rig.Api.AfterRead = (_, _) =>
            {
                if (change == "actor") rig.Actors.Current = rig.Actors.Current with { AuthenticationRevision = "retired" };
                else if (change == "installation") rig.Verifier.Peer = rig.Verifier.Peer with { InstallationRevision = "retired" };
                else rig.Lease!.Dispose();
                return Task.CompletedTask;
            };
            var request = rig.Client.GetServicesAsync(rig.Token);
            var serverFailure = await Record.ExceptionAsync(() => rig.Server!.WaitAsync(rig.Token));
            Assert.IsType<UnauthorizedAccessException>(serverFailure);
            rig.Observe(serverFailure!);
            // Server deliberately leaves the socket's ownership to this original fixture owner.
            rig.Accepted!.Shutdown(SocketShutdown.Both);
            var clientFailure = await Record.ExceptionAsync(() => request);
            Assert.NotNull(clientFailure); rig.Observe(clientFailure!);
            Assert.Equal(1, rig.Api.Reads);
            Assert.Equal(HomePermissionRequestState.Failed,
                (await rig.Permissions.ReadRequestObservationAsync(pending.PermissionRequestId!, rig.Token))!.State);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => rig.Client.GetServicesAsync(rig.Token));
        });

    [Fact]
    public Task Disconnect_drains_same_actual_read_finally_before_original_server_and_issuer_close() =>
        WithRigAsync(async rig =>
        {
            rig.StartServer(); await rig.AttachAsync();
            var pending = await rig.Client!.GetServicesAsync(rig.Token);
            Assert.True((await rig.Permissions.DecideAsync(pending.PermissionRequestId!, HomeApprovalChoice.Accept,
                cancellationToken: rig.Token)).Succeeded);
            var entered = Signal(); var cancelled = Signal(); var release = Signal();
            Task? request = null; Task? issuerClose = null;
            Exception? primary = null; List<Exception> cleanup = [];
            rig.Api.AfterRead = async (_, token) =>
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { cancelled.TrySetResult(); await release.Task; }
            };
            try
            {
                request = rig.Client.GetServicesAsync(rig.Token);
                await entered.Task.WaitAsync(rig.Token);
                await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Client.GetServicesAsync(rig.Token));
                rig.ClientSocket.Shutdown(SocketShutdown.Both);
                await cancelled.Task.WaitAsync(rig.Token);
                issuerClose = rig.Sessions.DisposeAsync().AsTask();
                Assert.Same(issuerClose, rig.Sessions.DisposeAsync().AsTask());
                Assert.False(rig.Server!.IsCompleted); Assert.False(issuerClose.IsCompleted);
                release.TrySetResult();
                var serverFailure = await Record.ExceptionAsync(() => rig.Server.WaitAsync(rig.Token));
                Assert.IsAssignableFrom<OperationCanceledException>(serverFailure); rig.Observe(serverFailure!);
                var clientFailure = await Record.ExceptionAsync(() => request);
                Assert.NotNull(clientFailure); rig.Observe(clientFailure!);
                await issuerClose.WaitAsync(rig.Token);
                Assert.True(rig.Server.IsCompleted); Assert.True(request.IsCompleted); Assert.True(issuerClose.IsCompleted);
                Assert.Equal(HomePermissionRequestState.Cancelled,
                    (await rig.Permissions.ReadRequestObservationAsync(pending.PermissionRequestId!, rig.Token))!.State);
            }
            catch (Exception error) { primary = error; }
            finally
            {
                release.TrySetResult();
                try { rig.Connection.Cancel(); } catch (Exception error) { Add(cleanup, error); }
                foreach (var original in new[] { request, rig.Server, issuerClose })
                    try { if (original is not null) await original; }
                    catch (Exception error) { if (!rig.IsObserved(error)) Add(cleanup, error); }
            }
            Throw(primary, cleanup);
        });

    [Theory]
    [InlineData("{\"Protocol\":\"9to1.Home.Core.Read/9\",\"RequestId\":\"00000000000000000000000000000001\",\"Operation\":\"GetState\"}")]
    [InlineData("{\"Protocol\":\"9to1.Home.Core.Read/1\",\"RequestId\":\"00000000000000000000000000000001\",\"Operation\":\"GetState\",\"Actor\":\"forged\"}")]
    [InlineData("{\"Protocol\":\"9to1.Home.Core.Read/1\",\"Protocol\":\"9to1.Home.Core.Read/1\",\"RequestId\":\"00000000000000000000000000000001\",\"Operation\":\"GetState\"}")]
    public Task Versioned_or_forged_authority_fields_are_refused_before_any_Core_broker_admission(string json) =>
        WithRigAsync(async rig =>
        {
            rig.StartServer();
            using var stream = new NetworkStream(rig.ClientSocket, ownsSocket: false);
            await HomeUnixDiscoveryTransport.WriteFrameAsync(stream, Encoding.UTF8.GetBytes(json), rig.Token);
            var failure = await Record.ExceptionAsync(() => rig.Server!.WaitAsync(rig.Token));
            Assert.IsType<InvalidDataException>(failure); rig.Observe(failure!);
            Assert.Equal(0, rig.Api.Reads);
            var snapshot = await rig.Permissions.GetSnapshotAsync(cancellationToken: rig.Token);
            Assert.Empty(snapshot.PendingRequests); Assert.Empty(snapshot.RecentAuditEvents);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Partial_header_or_payload_has_one_real_absolute_five_second_bound(bool payloadStarted) =>
        WithRigAsync(async rig =>
        {
            using var received = new NetworkStream(rig.Accepted!, ownsSocket: false);
            using var sent = new NetworkStream(rig.ClientSocket, ownsSocket: false);
            var original = HomeUnixCoreTransport.ReadAfterFirstFrameAsync(received, rig.Token).AsTask();
            Exception? primary = null; Exception? observed = null; List<Exception> cleanup = [];
            try
            {
                var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, 32);
                await sent.WriteAsync(payloadStarted ? header : header.AsMemory(0, 1), rig.Token);
                if (payloadStarted) await sent.WriteAsync(new byte[] { 1 }, rig.Token);
                await sent.FlushAsync(rig.Token);
                observed = await Record.ExceptionAsync(() => original.WaitAsync(TimeSpan.FromSeconds(8), rig.Token));
                var timeout = Assert.IsType<TimeoutException>(observed);
                Assert.IsAssignableFrom<OperationCanceledException>(timeout.InnerException);
                Assert.True(original.IsFaulted); Assert.False(rig.Token.IsCancellationRequested);
            }
            catch (Exception error) { primary = error; }
            finally
            {
                try { rig.Bound.Cancel(); } catch (Exception error) { Add(cleanup, error); }
                try { await original; } catch (Exception error) { if (!ReferenceEquals(error, observed)) Add(cleanup, error); }
            }
            Throw(primary, cleanup);
        });

    [Fact]
    public Task Authenticated_client_rechecks_original_host_after_reply_and_retires_failed_exchange() =>
        WithRigAsync(async rig =>
        {
            rig.StartServer(); await rig.AttachAsync();
            rig.Verifier.RetireHostOnRead = rig.Verifier.HostReads + 2;
            var failure = await Record.ExceptionAsync(() => rig.Client!.GetServicesAsync(rig.Token));
            Assert.IsType<UnauthorizedAccessException>(failure); rig.Observe(failure!);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => rig.Client!.GetServicesAsync(rig.Token));
            Assert.Equal(0, rig.Api.Reads);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Serialized_original_reply_is_not_written_after_actor_or_installation_fence_refusal(bool installation) =>
        WithRigAsync(async rig =>
        {
            var sameSession = await rig.Sessions.AcceptUnixAsync(rig.Accepted!, rig.Lease!, rig.Connection.Token, rig.Token)
                ?? throw new UnauthorizedAccessException("Actual original fixture session unavailable.");
            var original = await sameSession.GetServicesAsync(rig.Token);
            var request = new HomeUnixCoreRequest("9to1.Home.Core.Read/1", Guid.NewGuid().ToString("N"), "GetServices");
            var serialized = HomeUnixCoreProtocol.Payload(request, original);
            using var document = JsonDocument.Parse(serialized);
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
            if (installation) rig.Verifier.Peer = rig.Verifier.Peer with { InstallationRevision = "retired-after-serialization" };
            else rig.Actors.Current = rig.Actors.Current with { AuthenticationRevision = "retired-after-serialization" };
            using var originalStream = new NetworkStream(rig.Accepted!, ownsSocket: false);
            var publication = HomeUnixCoreTransport.PublishOriginalAsync(originalStream, sameSession, serialized, rig.Token);
            var failure = await Record.ExceptionAsync(() => publication);
            Assert.IsType<UnauthorizedAccessException>(failure);
            Assert.Same(publication, HomeUnixCoreTransport.OriginalPublicationDrain(publication));
            Assert.Equal(0, rig.ClientSocket.Available); // Actual receiving socket: no header or reply bytes.
            Assert.Equal(0, rig.Api.Reads);
            await sameSession.DisposeAsync();
        });

    [Fact]
    public Task Admitted_original_socket_write_and_its_held_finally_survive_owned_close_until_exact_release() =>
        WithRigAsync(async rig =>
        {
            var sameSession = await rig.Sessions.AcceptUnixAsync(rig.Accepted!, rig.Lease!, rig.Connection.Token, rig.Token)
                ?? throw new UnauthorizedAccessException("Actual original fixture session unavailable.");
            var pending = await sameSession.GetServicesAsync(rig.Token);
            var request = new HomeUnixCoreRequest("9to1.Home.Core.Read/1", Guid.NewGuid().ToString("N"), "GetServices");
            var serialized = HomeUnixCoreProtocol.Payload(request, pending);
            var admitted = Signal(); var release = Signal();
            using var originalStream = new NetworkStream(rig.Accepted!, ownsSocket: false);
            using var gate = new HeldOriginalWrite(originalStream, admitted, release);
            Task? original = null; Task? close = null;
            Exception? observed = null; Exception? primary = null; List<Exception> cleanup = [];
            try
            {
                original = HomeUnixCoreTransport.PublishOriginalAsync(gate, sameSession, serialized, rig.Connection.Token);
                await admitted.Task.WaitAsync(rig.Token);
                Assert.Equal(4, rig.ClientSocket.Available); // The actual header was already admitted to the socket.
                rig.Connection.Cancel();
                close = HomeUnixCoreTransport.OriginalPublicationDrain(original);
                Assert.Same(original, close); Assert.False(original.IsCompleted); Assert.False(close.IsCompleted);
                // Core identity retirement is separate from owning the in-flight socket write.
                await sameSession.DisposeAsync();
                Assert.False(close.IsCompleted);
                release.TrySetResult();
                observed = await Record.ExceptionAsync(() => original);
                var cancelled = Assert.IsAssignableFrom<OperationCanceledException>(observed);
                Assert.Equal(rig.Connection.Token, cancelled.CancellationToken);
                var closeError = await Record.ExceptionAsync(() => close);
                Assert.Same(observed, closeError); Assert.True(close.IsCompleted);
                Assert.Equal(4, rig.ClientSocket.Available); // No false claim that already-in-flight bytes vanished.
            }
            catch (Exception error) { primary = error; }
            finally
            {
                release.TrySetResult();
                try { rig.Connection.Cancel(); } catch (Exception error) { Add(cleanup, error); }
                foreach (var actual in new[] { original, close })
                    try { if (actual is not null) await actual; }
                    catch (Exception error) { if (!ReferenceEquals(error, observed)) Add(cleanup, error); }
                try { await sameSession.DisposeAsync(); } catch (Exception error) { Add(cleanup, error); }
            }
            Throw(primary, cleanup);
        });

    [Fact]
    public Task Elapsed_original_partial_socket_deadline_is_retained_after_later_owner_close() =>
        WithRigAsync(async rig =>
        {
            using var owner = new CancellationTokenSource();
            using var received = new NetworkStream(rig.Accepted!, ownsSocket: false);
            using var sent = new NetworkStream(rig.ClientSocket, ownsSocket: false);
            var held = new TaskCompletionSource<OperationCanceledException>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = Signal();
            using var originalRead = new HeldOriginalRead(received, held, release);
            var original = HomeUnixCoreTransport.ReadAfterFirstFrameAsync(originalRead, owner.Token).AsTask();
            Exception? observed = null; Exception? primary = null; List<Exception> cleanup = [];
            try
            {
                await sent.WriteAsync(new byte[] { 0 }, rig.Token); // Actual partial header on the SAME socket.
                await sent.FlushAsync(rig.Token);
                var actualDeadlineCancellation = await held.Task.WaitAsync(TimeSpan.FromSeconds(8), rig.Token);
                Assert.False(owner.IsCancellationRequested); Assert.False(original.IsCompleted);
                owner.Cancel();
                Assert.False(original.IsCompleted); // Original read finally still owns its continuation.
                release.TrySetResult();
                observed = await Record.ExceptionAsync(() => original.WaitAsync(rig.Token));
                var timeout = Assert.IsType<TimeoutException>(observed);
                Assert.Same(actualDeadlineCancellation, timeout.InnerException);
                Assert.True(original.IsFaulted); Assert.False(original.IsCanceled);
            }
            catch (Exception error) { primary = error; }
            finally
            {
                release.TrySetResult();
                try { owner.Cancel(); } catch (Exception error) { Add(cleanup, error); }
                try { await original; } catch (Exception error) { if (!ReferenceEquals(error, observed)) Add(cleanup, error); }
            }
            Throw(primary, cleanup);
        });

    [Fact]
    public async Task Earlier_foreign_API_cancellation_survives_held_original_finally_and_owned_Rig_close()
    {
        var rig = new Rig(); using var foreign = new CancellationTokenSource();
        var entered = Signal(); var release = Signal();
        var originalFailure = new OperationCanceledException("Original foreign API cancellation.", foreign.Token);
        Task? request = null; Task? close = null;
        Exception? requestObserved = null; Exception? closeObserved = null; Exception? primary = null;
        List<Exception> cleanup = [];
        CancellationToken bound = default;
        try
        {
            await rig.OpenAsync(); bound = rig.Token;
            rig.StartServer(); await rig.AttachAsync();
            var pending = await rig.Client.GetServicesAsync(bound);
            Assert.True((await rig.Permissions.DecideAsync(pending.PermissionRequestId!, HomeApprovalChoice.Accept,
                cancellationToken: bound)).Succeeded);
            rig.Api.AfterRead = async (_, _) =>
            {
                try { foreign.Cancel(); entered.TrySetResult(); throw originalFailure; }
                finally { await release.Task; }
            };
            request = rig.Client.GetServicesAsync(bound);
            await entered.Task.WaitAsync(bound);
            Assert.True(foreign.IsCancellationRequested); Assert.False(rig.Connection.IsCancellationRequested);
            close = rig.CloseAsync();
            Assert.True(rig.Connection.IsCancellationRequested);
            Assert.False(rig.Server.IsCompleted); Assert.False(close.IsCompleted);
            requestObserved = await Record.ExceptionAsync(() => request.WaitAsync(bound));
            Assert.NotNull(requestObserved); rig.Observe(requestObserved!);
            release.TrySetResult();
            closeObserved = await Record.ExceptionAsync(() => close.WaitAsync(bound));
            Assert.Same(originalFailure, closeObserved); // Broad late-cancellation suppression would make this null.
            var serverObserved = await Record.ExceptionAsync(() => rig.Server);
            Assert.Same(originalFailure, serverObserved);
            Assert.Equal(foreign.Token, originalFailure.CancellationToken);
            Assert.True(rig.Server.IsCanceled); Assert.True(close.IsCanceled);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            release.TrySetResult();
            if (close is null) close = rig.CloseAsync();
            try { await close; } catch (Exception error) { if (!ReferenceEquals(error, closeObserved)) Add(cleanup, error); }
            if (request is not null)
                try { await request; } catch (Exception error) { if (!ReferenceEquals(error, requestObserved)) Add(cleanup, error); }
        }
        Throw(primary, cleanup);
    }

    // Actual original NetworkStream read/cancellation is held, not replaced or clock-substituted.
    private sealed class HeldOriginalRead(Stream original,
        TaskCompletionSource<OperationCanceledException> cancelled, TaskCompletionSource release) : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> bytes, CancellationToken token = default)
        {
            try { return await original.ReadAsync(bytes, token); }
            catch (OperationCanceledException error)
            {
                cancelled.TrySetResult(error);
                throw;
            }
            finally { if (cancelled.Task.IsCompleted) await release.Task; }
        }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long length) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { base.Dispose(disposing); }
    }

    // A controlled original continuation gate, not a replacement socket or protocol implementation.
    // The exact real NetworkStream.WriteAsync completes before the finally is held.
    private sealed class HeldOriginalWrite(Stream original, TaskCompletionSource admitted, TaskCompletionSource release) : Stream
    {
        private bool _first = true;
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
        {
            await original.WriteAsync(bytes, token);
            if (_first) { _first = false; admitted.TrySetResult(); await release.Task; }
        }
        public override Task FlushAsync(CancellationToken token) => original.FlushAsync(token);
        public override void Flush() => original.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long length) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { base.Dispose(disposing); } // Actual stream remains original fixture-owned.
    }

    private static HomeNativeSessionHostRequirement Host() => new("synthetic-home", "synthetic-os-home");
    private static HomeServiceRequirement Requirement() => new("home.core", HomeCoreServiceCatalog.CurrentContractVersion.Major);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Add(List<Exception> errors, Exception error)
    { if (!errors.Any(row => ReferenceEquals(row, error))) errors.Add(error); }
    private static void Throw(Exception? primary, List<Exception> cleanup)
    {
        List<Exception> errors = []; if (primary is not null) Add(errors, primary);
        foreach (var error in cleanup) Add(errors, error);
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Original transport fixture and all cleanup failures retained.", errors);
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
        private readonly string _root = Path.Combine(Path.GetTempPath(), "home-core-wire-" + Guid.NewGuid().ToString("N"));
        internal readonly CancellationTokenSource Bound = new(TimeSpan.FromSeconds(100));
        internal readonly CancellationTokenSource Connection = new();
        private readonly Socket _listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        internal readonly Socket ClientSocket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        internal Socket? Accepted; internal HomeNativeSessionLease? Lease;
        internal readonly Actors Actors = new();
        internal readonly Verifier Verifier;
        internal readonly HomePermissionTrustService Permissions;
        internal readonly HomeNativeCoreApiSessions Sessions;
        internal readonly HomeCoreRuntime Runtime;
        internal readonly CapturingApi Api;
        internal readonly HomeNativeDiscoverySession Discovery;
        internal HomeUnixCoreClient Client { get; private set; } = null!;
        internal Task Server { get; private set; } = null!;
        private readonly List<Exception> _observed = [];
        internal CancellationToken Token => Bound.Token;
        internal Rig()
        {
            Verifier = new(Actors);
            var policy = new HomeCoreServiceReadActionPolicies();
            Permissions = new(new FileHomeCoreStateStore(Path.Combine(_root, "permissions.json")), policy.TryGet);
            IHomeCoreApi? api = null;
            Sessions = new(Permissions, Actors, Verifier, () => api ?? throw new InvalidOperationException("API not initialized."));
            Runtime = new(authorization: Sessions);
            Api = new(new HomeCoreApi(Runtime, Sessions, Actors)); api = Api;
            Discovery = new(Verifier, Actors, Runtime);
        }
        internal async Task OpenAsync()
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Actual Unix transport facts require Linux; no substituted native pass.");
            Directory.CreateDirectory(_root);
            var address = new UnixDomainSocketEndPoint(Path.Combine(_root, "original.sock"));
            _listener.Bind(address); _listener.Listen(1);
            await ClientSocket.ConnectAsync(address, Token); Accepted = await _listener.AcceptAsync(Token);
            Assert.Equal(Environment.ProcessId, HomeNativePeerObservation.FromAcceptedUnixSocket(Accepted)!.ProcessId);
            Lease = await HomeNativeSessionLease.TryAcquireAsync(Actors, new Paths(_root), Token)
                ?? throw new InvalidOperationException("Actual isolated Home lease unavailable.");
            await Runtime.StartAsync(Token);
        }
        internal void StartServer() => Server = HomeUnixCoreTransport.ServeAcceptedAsync(Accepted!, Discovery, Sessions, Lease!, Connection.Token);
        internal async Task AttachAsync() => Client = await HomeUnixCoreClient.AttachAsync(ClientSocket, Verifier, Host(), Connection.Token, Token)
            ?? throw new UnauthorizedAccessException("Actual socket with synthetic installed host was refused.");
        internal void Observe(Exception error) => Add(_observed, error);
        internal bool IsObserved(Exception error) => _observed.Any(row => ReferenceEquals(row, error));
        internal async Task CloseAsync()
        {
            List<Exception> errors = [];
            void Attempt(Action action) { try { action(); } catch (Exception error) { if (!IsObserved(error)) Add(errors, error); } }
            Attempt(Connection.Cancel);
            if (Server is not null)
                try { await Server; }
                catch (OperationCanceledException error) when (Server.IsCanceled && Connection.IsCancellationRequested && error.CancellationToken == Connection.Token)
                { Observe(error); } // Actual original server-owned cancellation is an expected test shutdown outcome.
                catch (Exception error) { if (!IsObserved(error)) Add(errors, error); }
            if (Client is not null) try { await Client.DisposeAsync(); } catch (Exception error) { if (!IsObserved(error)) Add(errors, error); }
            try { await Sessions.DisposeAsync(); } catch (Exception error) { Add(errors, error); }
            try { await Runtime.DisposeAsync(); } catch (Exception error) { Add(errors, error); }
            Attempt(() => Accepted?.Dispose()); Attempt(() => Lease?.Dispose());
            Attempt(ClientSocket.Dispose); Attempt(_listener.Dispose); Attempt(Connection.Dispose); Attempt(Bound.Dispose);
            Attempt(() => { if (Directory.Exists(_root)) Directory.Delete(_root, true); });
            Throw(null, errors);
        }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        internal AuthenticatedResourceActor Current = new("wire-fixture", "fresh-profile", null, null, "v1");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<AuthenticatedResourceActor?>(Current); }
    }
    private sealed class Verifier(Actors actors) : IHomeNativeInstalledPeerOriginalActorVerifier, IHomeNativeSessionHostVerifier
    {
        internal HomeNativeInstalledPeer Peer = new("synthetic-native", Guid.NewGuid(), "installed-v1", "synthetic-executable",
            new HashSet<string> { "home.core" });
        private readonly Guid _host = Guid.NewGuid();
        internal int HostReads; internal int RetireHostOnRead = int.MaxValue;
        private static bool Actual(HomeNativeObservedPeer peer) => peer.ProcessId == Environment.ProcessId &&
            peer.OperatingSystemPrincipalId.StartsWith("unix-euid:", StringComparison.Ordinal);
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer peer, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<HomeNativeInstalledPeer?>(Actual(peer) ? Peer : null); }
        public ValueTask<HomeNativeInstalledPeer?> VerifyForActorAsync(HomeNativeObservedPeer peer, AuthenticatedResourceActor actor, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<HomeNativeInstalledPeer?>(Actual(peer) && actor == actors.Current ? Peer : null); }
        public ValueTask<HomeNativeInstalledPeer?> VerifyHostAsync(HomeNativeObservedPeer peer, HomeNativeSessionHostRequirement required, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); HostReads++;
            return ValueTask.FromResult<HomeNativeInstalledPeer?>(Actual(peer) && required == Host() ?
                new("synthetic-home", _host, HostReads >= RetireHostOnRead ? "retired" : "host-v1", "synthetic-host-executable",
                    new HashSet<string> { "home.core" }) { Roles = new HashSet<string> { HomeNativeSessionHostRequirement.RequiredRole } } : null);
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
        internal int Reads;
        internal Func<HomeCallerIdentity, CancellationToken, Task>? AfterRead;
        public Task<HomeCoreOperationResult<HomeCoreStateSnapshot>> GetStateAsync(HomeCallerIdentity caller, CancellationToken token = default)
            => actual.GetStateAsync(caller, token);
        public async Task<HomeCoreOperationResult<IReadOnlyList<HomeServiceDescriptor>>> GetServicesAsync(HomeCallerIdentity caller, CancellationToken token = default)
        {
            Reads++; var result = await actual.GetServicesAsync(caller, token);
            if (AfterRead is { } hook) await hook(caller, token); return result;
        }
        public Task<HomeCoreOperationResult<HomeServiceDescriptor>> GetServiceAsync(HomeCallerIdentity caller, string serviceId, CancellationToken token = default)
            => actual.GetServiceAsync(caller, serviceId, token);
        public Task<HomeCompatibilityResult> GetCompatibilityAsync(HomeCallerIdentity caller, HomeCompatibilityRequest request, CancellationToken token = default)
            => actual.GetCompatibilityAsync(caller, request, token);
    }
}
