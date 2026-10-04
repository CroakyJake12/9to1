using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using System.Security.Principal;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace HavenOS.Home.Tests;

/// <summary>Actual Windows pipes, actual canonical FileHome profile and original Session.
/// Installed receipts and Files owner transactions are controlled fixture inputs, NOT protected
/// installation authority or canonical Files metadata acceptance. All cases are authored/unrun.</summary>
public sealed class HomeNativeFilesRpcPublicationTests
{
    private sealed class WindowsFactAttribute : FactAttribute
    { public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Requires actual Windows pipes and kernel credentials."; } }
    private sealed class WindowsTheoryAttribute : TheoryAttribute
    { public WindowsTheoryAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Requires actual Windows pipes and kernel credentials."; } }

    [WindowsTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public Task Missing_retained_guard_returns_typed_unavailable_before_owner_invocation(bool installed, bool owner) =>
        WithRigAsync(installed, owner, async rig =>
        {
            var reply = await rig.Session!.InvokeOriginalFilesAsync(new("BrowseRoot"), rig.Token);
            Assert.Equal("Unavailable", reply.State);
            Assert.Equal("FilesPublicationGuardUnavailable", reply.Code);
            Assert.Null(reply.Page);
            Assert.Null(reply.PermissionRequestId);
            Assert.Equal(0, rig.Owner.Invocations);
            var service = rig.Sessions!.CreateOriginalFilesCoreService()!;
            await service.StartAsync(rig.Token);
            Assert.Equal(HomeServiceLifecycleState.Unavailable, service.Descriptor.State);
            Assert.False(service.Descriptor.IsAvailable);
            await rig.PublishAsync(reply, rig.Server!);
            var frame = await HomeUnixDiscoveryTransport.ReadFrameAsync(rig.Client!, rig.Token);
            Assert.Equal(reply, HomeNativeFilesProtocol.ReadResponse(frame, rig.OriginalFrame!));
            Assert.Equal(0, rig.Verifier.InstalledAcquisitions);
        });

    [WindowsTheory]
    [InlineData("profile")]
    [InlineData("installation")]
    public Task Held_original_owner_check_then_real_retirement_refuses_physical_reply(string retirement) =>
        WithRigAsync(true, true, async rig =>
        {
            var entered = Signal(); var release = Signal();
            Task? publication = null;
            Exception? primary = null; List<Exception> cleanup = [];
            try
            {
                var reply = await rig.Session!.InvokeOriginalFilesAsync(new("BrowseRoot"), rig.Token);
                rig.Owner.BeforePublication = async token =>
                { entered.TrySetResult(); await release.Task.WaitAsync(token); };
                publication = rig.PublishAsync(reply, rig.Server!);
                await entered.Task.WaitAsync(rig.Token);
                if (retirement == "profile") await rig.ChangeActualProfileAsync();
                else rig.Verifier.Peer = rig.Verifier.Peer with { InstallationRevision = "retired-fixture-receipt" };
                release.TrySetResult();
                var failure = await Record.ExceptionAsync(() => publication);
                Assert.NotNull(failure);
                Assert.Contains(Flatten(failure!), error => error is UnauthorizedAccessException);
                rig.Observe(failure!);
                Assert.Equal(0, rig.PhysicalWrites);
                Assert.Equal(0, rig.Owner.PublicationAcquisitions);
            }
            catch (Exception error) { primary = error; }
            finally
            {
                release.TrySetResult();
                try { if (publication is not null) await publication; }
                catch (Exception error) { if (!rig.IsObserved(error)) Add(cleanup, error); }
            }
            Throw(primary, cleanup);
        });

    [WindowsFact]
    public Task Held_actual_frame_payload_write_keeps_guards_and_same_close_until_finally_settles() =>
        WithRigAsync(true, true, async rig =>
        {
            var writer = new HeldPayloadStream(rig.Server!);
            Task? publication = null, close = null;
            Task<HomeStateWriteResult>? waitingWrite = null;
            Exception? primary = null; List<Exception> cleanup = [];
            try
            {
                var reply = await rig.Session!.InvokeOriginalFilesAsync(new("BrowseRoot"), rig.Token);
                publication = rig.PublishAsync(reply, writer);
                await writer.Entered.Task.WaitAsync(rig.Token);
                Assert.True(rig.Verifier.LastGuard!.IsHeld);
                Assert.True(rig.Owner.Guard.IsHeld);
                Assert.Equal(1, writer.HeaderWrites);
                waitingWrite = rig.RewriteActualProfileAsync(changeIdentity: false);
                Assert.False(waitingWrite.IsCompleted); // Actual FileHome publication lease owns its state gate.
                close = rig.Session.DisposeAsync().AsTask();
                Assert.Same(close, rig.Session.DisposeAsync().AsTask());
                await writer.Cancelled.Task.WaitAsync(rig.Token);
                Assert.False(close.IsCompleted);
                Assert.True(rig.Verifier.LastGuard.IsHeld);
                Assert.True(rig.Owner.Guard.IsHeld);
                writer.Release.TrySetResult();
                var failure = await Record.ExceptionAsync(() => publication);
                Assert.NotNull(failure);
                Assert.Contains(Flatten(failure!), error => error is OperationCanceledException);
                rig.Observe(failure!);
                await close;
                Assert.True((await waitingWrite).IsSuccess);
                Assert.False(rig.Verifier.LastGuard.IsHeld);
                Assert.False(rig.Owner.Guard.IsHeld);
                Assert.Equal(1, rig.Owner.Closes);
            }
            catch (Exception error) { primary = error; }
            finally
            {
                writer.Release.TrySetResult();
                foreach (var original in new Task?[] { publication, close, waitingWrite })
                    try { if (original is not null) await original; }
                    catch (Exception error) { if (!rig.IsObserved(error)) Add(cleanup, error); }
            }
            Throw(primary, cleanup);
        });

    [WindowsFact]
    public Task Both_original_checks_and_both_disposals_survive_independent_failures_without_frame() =>
        WithRigAsync(true, true, async rig =>
        {
            var installedCheck = new UnauthorizedAccessException("exact installed guard check");
            var ownerCheck = new IOException("exact Files guard check");
            var ownerClose = new IOException("exact Files guard close");
            var installedClose = new IOException("exact installed guard close");
            rig.Verifier.CheckFailure = installedCheck;
            rig.Verifier.CloseFailure = installedClose;
            rig.Owner.Guard.CheckFailure = ownerCheck;
            rig.Owner.Guard.CloseFailure = ownerClose;
            var reply = await rig.Session!.InvokeOriginalFilesAsync(new("BrowseRoot"), rig.Token);
            var original = rig.PublishAsync(reply, rig.Server!);
            var failure = await Record.ExceptionAsync(() => original);
            Assert.NotNull(failure);
            foreach (var expected in new Exception[] { installedCheck, ownerCheck, ownerClose, installedClose })
                Assert.Contains(Flatten(failure!), error => ReferenceEquals(expected, error));
            rig.Observe(failure!);
            Assert.Equal(0, rig.PhysicalWrites);
            Assert.Equal(1, rig.Owner.Guard.Disposals);
            Assert.Equal(1, rig.Verifier.LastGuard!.Disposals);
            Assert.False(rig.Verifier.LastGuard.IsHeld);
            Assert.False(rig.Owner.Guard.IsHeld);
        });

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static IEnumerable<Exception> Flatten(Exception error)
    {
        yield return error;
        if (error is AggregateException aggregate)
            foreach (var child in aggregate.InnerExceptions)
                foreach (var row in Flatten(child)) yield return row;
    }
    private static void Add(List<Exception> errors, Exception error)
    { if (!errors.Any(row => ReferenceEquals(row, error))) errors.Add(error); }
    private static void Throw(Exception? primary, List<Exception> cleanup)
    {
        if (primary is not null) Add(cleanup, primary);
        if (cleanup.Count == 1) ExceptionDispatchInfo.Capture(cleanup[0]).Throw();
        if (cleanup.Count > 1) throw new AggregateException("Original Files protocol fixture and independent cleanup failed.", cleanup);
    }
    private static async Task WithRigAsync(bool installed, bool owner, Func<Rig, Task> body)
    {
        var rig = new Rig(installed, owner);
        Exception? primary = null; List<Exception> cleanup = [];
        try { await rig.OpenAsync(); await body(rig); }
        catch (Exception error) { primary = error; }
        finally { try { await rig.CloseAsync(); } catch (Exception error) { Add(cleanup, error); } }
        Throw(primary, cleanup);
    }

    private sealed class Rig(bool installed, bool owner)
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "files-rpc-original-" + Guid.NewGuid().ToString("N"));
        private readonly string _pipeName = "files.rpc.fixture." + Guid.NewGuid().ToString("N");
        private readonly List<Exception> _observed = [];
        internal CancellationTokenSource? Bound, Lifetime;
        internal FileHomeCoreStateStore? Store;
        internal HomeLocalProfileIdentity? Profiles;
        internal HomePermissionTrustService? Permissions;
        internal HomeNativeCoreApiSessions? Sessions;
        internal HomeNativeSessionLease? Lease;
        internal NamedPipeServerStream? Server;
        internal NamedPipeClientStream? Client;
        internal HomeNativeCoreApiSessions.Session? Session;
        internal HomeCoreRuntime? Runtime;
        internal readonly ControlledOwner Owner = new(owner);
        internal GuardedVerifier Verifier = null!;
        internal HomeNativeFilesFrame? OriginalFrame;
        internal int PhysicalWrites;
        internal CancellationToken Token => Bound!.Token;
        internal async Task OpenAsync()
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Actual Windows required; no substituted pass.");
            Bound = new(); Bound.CancelAfter(TimeSpan.FromSeconds(30)); Lifetime = new();
            Directory.CreateDirectory(_root);
            Store = new(Path.Combine(_root, "home.json"));
            Profiles = new(Store, new OperatingSystemPrincipalSource());
            var actor = await Profiles.GetCurrentAsync(Token) ?? throw new InvalidOperationException("Actual Home profile unavailable.");
            using var identity = WindowsIdentity.GetCurrent();
            var principal = "windows-sid:" + (identity.User?.Value ?? throw new InvalidOperationException("Actual SID unavailable."));
            Verifier = new(Profiles, Store, principal);
            IHomeNativeInstalledPeerVerifier originalVerifier = installed ? Verifier : new PlainVerifier(Verifier);
            var policy = new HomeCoreServiceReadActionPolicies();
            Permissions = new(Store, policy.TryGet);
            IHomeCoreApi? actual = null;
            Sessions = new(Permissions, Profiles, originalVerifier,
                () => actual ?? throw new InvalidOperationException("Actual Core unavailable."), _ => Owner);
            Runtime = new([new HomeCoreStateService(Store), new HomePermissionsCoreService(Permissions, Profiles)], Sessions);
            actual = new HomeCoreApi(Runtime, Sessions, Profiles);
            Lease = await HomeNativeSessionLease.TryAcquireAsync(Profiles, new Paths(_root), Token)
                ?? throw new InvalidOperationException("Actual original Home lease unavailable.");
            Server = new(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, 4096, 4096);
            Client = new(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
            var originalAccept = Server.WaitForConnectionAsync(Token);
            Exception? primary = null; List<Exception> cleanup = [];
            try { await Client.ConnectAsync(Token); await originalAccept; }
            catch (Exception error) { primary = error; }
            finally
            {
                if (primary is not null)
                {
                    try { Lifetime.Cancel(); } catch (Exception error) { Add(cleanup, error); }
                    try { await Server.DisposeAsync(); } catch (Exception error) { Add(cleanup, error); }
                    try { await originalAccept; } catch (Exception error) { if (!ReferenceEquals(primary, error)) Add(cleanup, error); }
                }
            }
            Throw(primary, cleanup);
            Session = await Sessions.AcceptWindowsPipeAsync(Server, Lease, Lifetime.Token, Token)
                ?? throw new UnauthorizedAccessException("The actual pipe with controlled installed receipt was refused.");
            Owner.ExpectedActor = actor;
        }
        internal Task PublishAsync(HomeNativeFilesReply reply, Stream originalStream)
        {
            OriginalFrame = new(HomeNativeFilesProtocol.Version, Guid.NewGuid().ToString("N"), new("BrowseRoot"));
            var bytes = HomeNativeFilesProtocol.Payload(OriginalFrame, reply);
            return Session!.PublishOriginalFilesReplyAsync(reply, async token =>
            {
                PhysicalWrites++;
                await HomeUnixDiscoveryTransport.WriteFrameAsync(originalStream, bytes, token);
            }, Token);
        }
        internal async Task ChangeActualProfileAsync() => Assert.True((await RewriteActualProfileAsync(true)).IsSuccess);
        internal async Task<HomeStateWriteResult> RewriteActualProfileAsync(bool changeIdentity)
        {
            var read = await Store!.ReadAsync(Token);
            var record = read.State!.Records.Single(row => row.RecordId == "home.local-profile");
            var original = record.Payload.Deserialize<HomeLocalProfile>()!;
            var after = changeIdentity ? original with { ProfileId = Guid.NewGuid() } : original;
            return await Store.WriteAsync(record with { Revision = record.Revision + 1,
                Payload = JsonSerializer.SerializeToElement(after) }, record.Revision, Token);
        }
        internal void Observe(Exception error) { foreach (var same in Flatten(error)) Add(_observed, same); }
        internal bool IsObserved(Exception error) => Flatten(error).All(same => _observed.Any(row => ReferenceEquals(row, same)));
        internal async Task CloseAsync()
        {
            List<Exception> failures = [];
            async Task Settle(Func<Task> acquire)
            {
                try { await acquire(); }
                catch (Exception error) { if (!IsObserved(error)) Add(failures, error); }
            }
            if (Session is not null) await Settle(() => Session.DisposeAsync().AsTask());
            if (Sessions is not null) await Settle(() => Sessions.DisposeAsync().AsTask());
            if (Client is not null) await Settle(() => Client.DisposeAsync().AsTask());
            if (Server is not null) await Settle(() => Server.DisposeAsync().AsTask());
            if (Runtime is not null) await Settle(() => Runtime.DisposeAsync().AsTask());
            try { Lease?.Dispose(); } catch (Exception error) { Add(failures, error); }
            try { Lifetime?.Dispose(); } catch (Exception error) { Add(failures, error); }
            try { Bound?.Dispose(); } catch (Exception error) { Add(failures, error); }
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (Exception error) { Add(failures, error); }
            Throw(null, failures);
        }
    }

    private sealed class ControlledOwner(bool supported) : IHomeNativeFilesPublicationOwner
    {
        internal AuthenticatedResourceActor? ExpectedActor;
        internal int Invocations, PublicationAcquisitions, Closes;
        internal Func<CancellationToken, Task>? BeforePublication;
        internal readonly OwnerGuard Guard = new();
        private HomeNativeFilesOriginalConnection? _connection;
        private HomeNativeFilesReply? _reply;
        public bool SupportsOriginalPublication => supported;
        public Task<HomeNativeFilesReply> InvokeOriginalAsync(HomeNativeFilesOriginalConnection connection,
            HomeNativeFilesRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.Equal(ExpectedActor, connection.OriginalActor);
            Assert.Equal("files", connection.InstalledAppId);
            Invocations++; _connection = connection;
            _reply = new("Succeeded", "ControlledFixtureRead", "No canonical Files or installed authority claimed.",
                new(Guid.NewGuid(), Guid.NewGuid(), new string('A', 64), null, "Controlled fixture", [], false));
            return Task.FromResult(_reply);
        }
        public async Task DemandOriginalReplyCurrentAsync(HomeNativeFilesOriginalConnection connection,
            HomeNativeFilesReply reply, CancellationToken token)
        {
            Assert.Same(_connection, connection); Assert.Same(_reply, reply);
            if (BeforePublication is { } original) await original(token);
            token.ThrowIfCancellationRequested();
        }
        public ValueTask<IHomeNativeFilesPublicationGuard?> AcquireOriginalReplyPublicationAsync(
            HomeNativeFilesOriginalConnection connection, HomeNativeFilesReply reply, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Assert.Same(_connection, connection); Assert.Same(_reply, reply);
            PublicationAcquisitions++; Guard.Held = true;
            return ValueTask.FromResult<IHomeNativeFilesPublicationGuard?>(Guard);
        }
        public Task CloseOriginalConnectionAsync(HomeNativeFilesOriginalConnection connection)
        { Assert.Same(_connection, connection); Closes++; return Task.CompletedTask; }
    }
    private sealed class OwnerGuard : IHomeNativeFilesPublicationGuard
    {
        internal bool Held;
        internal Exception? CheckFailure, CloseFailure;
        internal int Disposals;
        public bool IsHeld => Held;
        public ValueTask DemandOriginalCurrentAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (CheckFailure is { } error) throw error; return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync()
        { Held = false; Disposals++; if (CloseFailure is { } error) throw error; return ValueTask.CompletedTask; }
    }
    private sealed class PlainVerifier(GuardedVerifier original) : IHomeNativeInstalledPeerOriginalActorVerifier
    {
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer observed, CancellationToken token) =>
            original.VerifyAsync(observed, token);
        public ValueTask<HomeNativeInstalledPeer?> VerifyForActorAsync(HomeNativeObservedPeer observed,
            AuthenticatedResourceActor actor, CancellationToken token) => original.VerifyForActorAsync(observed, actor, token);
    }
    private sealed class GuardedVerifier(HomeLocalProfileIdentity profiles, FileHomeCoreStateStore store,
        string principal) : IHomeNativeFilesInstalledPublicationVerifier
    {
        internal HomeNativeInstalledPeer Peer = new("files", Guid.NewGuid(), "CONTROLLED_RECEIPT_V1",
            "SYNTHETIC_INSTALLED_FIXTURE_NO_PROTECTED_PACKAGE", new HashSet<string> { "home.core", "home.state", "files.native" });
        internal int InstalledAcquisitions;
        internal InstalledGuard? LastGuard;
        internal Exception? CheckFailure, CloseFailure;
        private bool Actual(HomeNativeObservedPeer observed) => observed.ProcessId == Environment.ProcessId &&
            observed.OperatingSystemPrincipalId == principal;
        private bool Same(HomeNativeInstalledPeer expected) => Peer.AppId == expected.AppId &&
            Peer.InstalledApplicationId == expected.InstalledApplicationId &&
            Peer.InstallationRevision == expected.InstallationRevision && Peer.ExecutableIdentity == expected.ExecutableIdentity;
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer observed, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<HomeNativeInstalledPeer?>(Actual(observed) ? Peer : null); }
        public async ValueTask<HomeNativeInstalledPeer?> VerifyForActorAsync(HomeNativeObservedPeer observed,
            AuthenticatedResourceActor actor, CancellationToken token) =>
            Actual(observed) && await profiles.GetCurrentAsync(token) == actor ? Peer : null;
        public async ValueTask<IHomeNativeFilesPublicationGuard?> AcquireOriginalFilesPublicationAsync(
            HomeNativeObservedPeer observed, AuthenticatedResourceActor actor, HomeNativeInstalledPeer installed,
            CancellationToken originalLifetime, CancellationToken token)
        {
            InstalledAcquisitions++;
            if (!Actual(observed) || !Same(installed) || originalLifetime.IsCancellationRequested) return null;
            var held = await store.AcquireLocalOperationLeaseAsync(profiles, actor, token);
            if (held is null) return null;
            LastGuard = new(held, () => Actual(observed) && Same(installed) && !originalLifetime.IsCancellationRequested,
                CheckFailure, CloseFailure);
            return LastGuard;
        }
    }
    private sealed class InstalledGuard(IHomeLocalOperationLease originalLease, Func<bool> originalCurrent,
        Exception? checkFailure, Exception? closeFailure) : IHomeNativeFilesPublicationGuard
    {
        private bool _held = true;
        internal int Disposals;
        public bool IsHeld => _held;
        public async ValueTask DemandOriginalCurrentAsync(CancellationToken token)
        {
            if (checkFailure is not null) throw checkFailure;
            token.ThrowIfCancellationRequested();
            if (!_held || !originalCurrent() || !await originalLease.IsCurrentAsync(token))
                throw new UnauthorizedAccessException("The actual profile or controlled installed tuple retired.");
        }
        public async ValueTask DisposeAsync()
        {
            _held = false; Disposals++;
            Exception? primary = null;
            try { await originalLease.DisposeAsync(); } catch (Exception error) { primary = error; }
            if (primary is not null && closeFailure is not null)
                throw new AggregateException("Actual lease close and controlled close failure.", primary, closeFailure);
            if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
            if (closeFailure is not null) throw closeFailure;
        }
    }

    private sealed class HeldPayloadStream(Stream originalPipe) : Stream
    {
        internal readonly TaskCompletionSource Entered = Signal(), Cancelled = Signal(), Release = Signal();
        internal int HeaderWrites;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => originalPipe.Flush();
        public override Task FlushAsync(CancellationToken token) => originalPipe.FlushAsync(token);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) =>
            new(WriteOriginalAsync(buffer, token));
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
            WriteOriginalAsync(buffer.AsMemory(offset, count), token);
        private async Task WriteOriginalAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
        {
            if (HeaderWrites == 0)
            { await originalPipe.WriteAsync(bytes, token); HeaderWrites++; return; }
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { Cancelled.TrySetResult(); await Release.Task; }
        }
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        // Borrowed pipe is disposed only by Rig after the SAME original publication/Session drains.
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
}
