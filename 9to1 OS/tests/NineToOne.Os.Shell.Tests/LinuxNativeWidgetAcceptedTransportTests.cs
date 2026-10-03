using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Os.Shell.Authority;
namespace NineToOne.Os.Shell.Tests;

public sealed class LinuxNativeWidgetAcceptedTransportTests
{
    [Fact]
    public async Task ActualUnixPeerFramedCaptureUsesExactOriginalReferenceAndDisconnectRemovesRegistration()
    {
        await using var f = await Fixture.Create(); var serve = f.Serve();
        await f.Register();
        var admitted = JsonSerializer.Deserialize<LinuxNativeWidgetRegistrationResult>(await Read(f.Stream));
        Assert.Equal("RegisteredLiveOwner", admitted!.Code);
        Assert.Equal(Environment.ProcessId, f.Verifier.Observed!.ProcessId);
        Assert.StartsWith("unix-euid:", f.Verifier.Observed.OperatingSystemPrincipalId);
        var reference = Assert.Single(await f.Registry.ListForActorAsync(f.Actors.Current)).Reference;
        var capture = f.Registry.CaptureAsync(reference, f.Actors.Current, new(2, 2), 240, 120).AsTask(); f.Pending.Add(capture);
        var request = JsonSerializer.Deserialize<LinuxNativeWidgetCapture>(await Read(f.Stream))!;
        Assert.Equal(reference, request.Request.Reference);
        Assert.Equal(f.Actors.Current, request.OriginalActor);
        await Write(f.Stream, new LinuxNativeWidgetSurface(request.RequestId, reference, "clock.surface.v1",
            "<Text>{Binding heading}</Text>", new() {{ "heading", JsonSerializer.SerializeToElement("Actual framed data") }}));
        var surface = await capture.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.NotNull(surface); Assert.Equal("Actual framed data", surface!.Data["heading"].GetString());
        f.Client.Dispose(); await Assert.ThrowsAnyAsync<IOException>(() => serve);
        Assert.Null(await f.Registry.ResolveForActorAsync(reference, f.Actors.Current));
    }
    [Fact]
    public async Task WrongActualResponseCorrelationClosesOriginalRegistrationWithoutDeliveringSurface()
    {
        await using var f = await Fixture.Create(); var serve = f.Serve(); await f.Register(); await Read(f.Stream);
        var reference = Assert.Single(await f.Registry.ListForActorAsync(f.Actors.Current)).Reference;
        var capture = f.Registry.CaptureAsync(reference, f.Actors.Current, new(2, 2), 240, 120).AsTask(); f.Pending.Add(capture);
        await Read(f.Stream);
        await Write(f.Stream, new LinuxNativeWidgetSurface(Guid.NewGuid(), reference, "clock.surface.v1", "<Text />", new()));
        await Assert.ThrowsAsync<InvalidDataException>(() => serve);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture);
        Assert.Empty(await f.Registry.ListForActorAsync(f.Actors.Current));
    }
    [Fact]
    public async Task ActorRetiredDuringActualRegistrationFrameNeverReachesControlledVerifier()
    {
        await using var f = await Fixture.Create(); var serve = f.Serve();
        await f.Actors.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(3));
        f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "replacement" };
        await f.Register(); await serve.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, f.Verifier.Calls); Assert.Empty(await f.Registry.ListForActorAsync(f.Actors.Current));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualOwnerConnectionChecksOriginalBackendAndWithholdsDataAfterSourceRevocation(bool revokeSource)
    {
        await using var f = await Fixture.Create(); var serve = f.Serve();
        var backend = new Backend(f.Actors); if (revokeSource) backend.OnRead = () => f.ScopeResolver.Allowed = false;
        using var owner = await LinuxNativeWidgetOwnerConnection.ConnectAsync(f.Client,
            new("fixture.host", "controlled-host-requirement"), new HostVerifier(f.Actors), f.Verifier, new OperatingSystemPrincipalSource(), f.Actors,
            f.Resources, f.Declarations, backend, f.Token);
        Assert.NotNull(owner); var ownerLoop = owner!.ServeCapturesAsync(); f.Pending.Add(ownerLoop);
        var reference = Assert.Single(await f.Registry.ListForActorAsync(f.Actors.Current)).Reference;
        var capture = f.Registry.CaptureAsync(reference, f.Actors.Current, new(2,2), 240, 120).AsTask(); f.Pending.Add(capture);
        if (revokeSource)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ownerLoop);
            await Assert.ThrowsAnyAsync<IOException>(() => serve);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture);
            Assert.Empty(await f.Registry.ListForActorAsync(f.Actors.Current));
        }
        else
        {
            var surface = await capture.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("Controlled original backend", surface!.Data["heading"].GetString());
            owner.Dispose();
            try { await ownerLoop; } catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
            await Assert.ThrowsAnyAsync<IOException>(() => serve);
        }
        Assert.Equal(1, backend.Reads); Assert.Equal(f.Actors.Current, backend.Observed);
    }
    [Fact]
    public async Task ActualOwnerWireRefCannotReplaceIndependentlyVerifiedInstalledOwnerBeforeBackendRead()
    {
        await using var f = await Fixture.Create(); var backend = new Backend(f.Actors);
        using var hostStream = f.HostStream();
        var enrolled = Task.Run(async () =>
        {
            var registration = JsonSerializer.Deserialize<LinuxNativeWidgetRegistration>(await Read(hostStream));
            Assert.Equal(1, registration!.Schema);
            await Write(hostStream, new LinuxNativeWidgetRegistrationResult(1, "RegisteredLiveOwner"));
        }); f.Pending.Add(enrolled);
        using var owner = await LinuxNativeWidgetOwnerConnection.ConnectAsync(f.Client,
            new("fixture.host", "controlled-host-requirement"), new HostVerifier(f.Actors), f.Verifier,
            new OperatingSystemPrincipalSource(), f.Actors, f.Resources, f.Declarations, backend, f.Token);
        Assert.NotNull(owner); await enrolled;
        var actual = await f.Verifier.VerifyForActorAsync(new(Environment.ProcessId,
            (await new OperatingSystemPrincipalSource().GetPrincipalAsync(f.Token))!), f.Actors.Current, f.Token);
        Assert.NotNull(actual); var loop = owner!.ServeCapturesAsync(); f.Pending.Add(loop);
        var forged = new HomeNativeWidgetReference(actual!.AppId, Guid.NewGuid(), actual.InstallationRevision, "clock", "r1");
        await Write(hostStream, new LinuxNativeWidgetCapture(Guid.NewGuid(),
            new(forged, "clock.surface.v1", new(2,2), 240,120), f.Actors.Current));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => loop);
        Assert.Equal(0, backend.Reads);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        Assert.Equal(0, await hostStream.ReadAsync(new byte[1], deadline.Token));
    }
    private sealed class HostVerifier(Actors actors) : IHomeNativeSessionHostOriginalActorVerifier
    {
        private readonly Guid _id = Guid.NewGuid();
        public ValueTask<HomeNativeInstalledPeer?> VerifyHostAsync(HomeNativeObservedPeer observed, HomeNativeSessionHostRequirement trusted, CancellationToken ct) =>
            throw new InvalidOperationException("Ambient host fallback forbidden.");
        public ValueTask<HomeNativeInstalledPeer?> VerifyHostForActorAsync(HomeNativeObservedPeer observed,
            HomeNativeSessionHostRequirement trusted, AuthenticatedResourceActor original, CancellationToken ct) =>
            ValueTask.FromResult<HomeNativeInstalledPeer?>(original == actors.Current ? new(trusted.AppId, _id, "host-r1", "controlled-host",
                new HashSet<string> { HomeNativeWidgetRegistry.ServiceId }) { Roles = new HashSet<string> { HomeNativeSessionHostRequirement.RequiredRole } } : null);
    }
    private sealed class Backend(Actors actors) : IHomeNativeWidgetOriginalActorRuntimeEndpoint
    {
        public int Reads; public AuthenticatedResourceActor? Observed; public Action? OnRead;
        public ValueTask<HomeNativeWidgetSurface?> CaptureAsync(HomeNativeWidgetCaptureRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("Ambient backend fallback forbidden.");
        public ValueTask<HomeNativeWidgetSurface?> CaptureForActorAsync(HomeNativeWidgetCaptureRequest request,
            AuthenticatedResourceActor original, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); if (original != actors.Current) return ValueTask.FromResult<HomeNativeWidgetSurface?>(null);
            Reads++; Observed = original; OnRead?.Invoke();
            return ValueTask.FromResult<HomeNativeWidgetSurface?>(HomeNativeWidgetSurface.Capture(request.Reference, request.SurfaceReference,
                "<Text>{Binding heading}</Text>", new Dictionary<string,JsonElement> { { "heading", JsonSerializer.SerializeToElement("Controlled original backend") } }));
        }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "original");
        public TaskCompletionSource FirstRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); var observed = Current; FirstRead.TrySetResult(); return ValueTask.FromResult<AuthenticatedResourceActor?>(observed); }
    }
    private sealed class Verifier(Actors actors) : IHomeNativeInstalledPeerOriginalActorVerifier
    {
        public ValueTask<HomeNativeInstalledPeer?> VerifyForActorAsync(HomeNativeObservedPeer observed,
            AuthenticatedResourceActor original, CancellationToken ct) => original == actors.Current
                ? VerifyAsync(observed, ct) : ValueTask.FromResult<HomeNativeInstalledPeer?>(null);
        private readonly Guid _installedId = Guid.NewGuid();
        public HomeNativeObservedPeer? Observed; public int Calls;
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer observed, CancellationToken ct)
        { Calls++; Observed = observed; return ValueTask.FromResult<HomeNativeInstalledPeer?>(new("fixture.clock", _installedId, "r1", "controlled-not-installed", new HashSet<string> { HomeNativeWidgetRegistry.ServiceId })); }
    }
    private sealed class Resolver : ICanonicalResourceAccessResolver
    {
        public bool Allowed = true;
        public string ResourceKind => "fixture.clock";
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken ct) =>
            ValueTask.FromResult(new ResourceAccessDecision(Allowed && actionId == HomeNativeWidgetRegistry.RenderActionId, "controlled", actor.ActorId, "r1", actor.OrganisationId));
    }
    private sealed class Fixture : IDisposable, IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "widget-" + Guid.NewGuid().ToString("N") + ".sock");
        public readonly List<Task> Pending = [];
        public readonly Actors Actors = new(); public readonly Verifier Verifier;
        public HomeNativeWidgetRegistry Registry { get; }
        public readonly Resolver ScopeResolver = new(); public ResourceAuthorizationService Resources { get; }
        public CancellationToken Token => _lifetime.Token;
        public IReadOnlyList<HomeNativeWidgetDefinition> Declarations => [new("clock", "r1", "Clock", new(1,1), new(2,2), new(4,4),
            "clock.configuration.v1", "clock.surface.v1", HomeNativeWidgetUpdateMode.Manual, null, [], [new("fixture.clock", "clock", "r1", ResourceAccess.Read)])];
        public Socket Client { get; } = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        private Socket? _accepted; public NetworkStream Stream { get; private set; } = null!;
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(10));
        private Fixture() { Verifier = new(Actors); Resources = new(Actors, [ScopeResolver]); Registry = new(Verifier, Actors, Resources); }
        public static async Task<Fixture> Create()
        {
            Assert.True(OperatingSystem.IsLinux(), "Actual Linux kernel Unix-peer fixture requires Linux; no prerequisite skip.");
            var f = new Fixture();
            try
            {
                using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                listener.Bind(new UnixDomainSocketEndPoint(f._path)); listener.Listen(1);
                var accepted = listener.AcceptAsync(f._lifetime.Token).AsTask();
                await f.Client.ConnectAsync(new UnixDomainSocketEndPoint(f._path), f._lifetime.Token);
                f._accepted = await accepted; f.Stream = new(f.Client, ownsSocket: false); return f;
            }
            catch { f.Dispose(); throw; }
        }
        public NetworkStream HostStream() => new(_accepted!, ownsSocket: false);
        public Task Serve() { var task = LinuxNativeWidgetAcceptedTransport.ServeAcceptedAsync(_accepted!, Registry, Actors, () => true, _lifetime.Token); Pending.Add(task); return task; }
        public Task Register() => Write(Stream, new LinuxNativeWidgetRegistration(1, [new("clock", "r1", "Clock", new(1,1), new(2,2), new(4,4),
            "clock.configuration.v1", "clock.surface.v1", HomeNativeWidgetUpdateMode.Manual, null, [], [new("fixture.clock", "clock", "r1", ResourceAccess.Read)])]));
        public async ValueTask DisposeAsync()
        {
            _lifetime.Cancel(); Stream?.Dispose(); Client.Dispose(); _accepted?.Dispose();
            try { await Task.WhenAll(Pending); } catch { /* Observe every task before deleting fixture transport; preserve assertion failures. */ }
            _lifetime.Dispose(); File.Delete(_path);
        }
        public void Dispose() { _lifetime.Cancel(); Stream?.Dispose(); Client.Dispose(); _accepted?.Dispose(); _lifetime.Dispose(); File.Delete(_path); }
    }
    private static async Task<byte[]> Read(Stream stream)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var header = new byte[4]; await stream.ReadExactlyAsync(header, deadline.Token);
        var length = BinaryPrimitives.ReadInt32BigEndian(header); Assert.InRange(length, 1, 384 * 1024);
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, deadline.Token); return bytes;
    }
    private static async Task Write<T>(Stream stream, T value)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value); var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, bytes.Length);
        await stream.WriteAsync(header, deadline.Token); await stream.WriteAsync(bytes, deadline.Token); await stream.FlushAsync(deadline.Token);
    }
}
