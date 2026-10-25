using System.Diagnostics;
using System.Buffers.Binary;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Security.Cryptography;
using Haven.Application;
using Haven.Infrastructure.Native.Windows;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Linux;

/// <summary>Actual Linux Root cold service composition. It retains the SAME signed
/// canonical activation throughout its controlled native Home child and private
/// command channel. Ordinary startup never registers or invokes this service.</summary>
public sealed class LinuxHomeRootServiceRuntime : IAsyncDisposable
{
    private readonly LinuxHomeBootstrapActivationOwner _activation;
    private readonly LinuxHomeRegisteredRootServiceContext _kernel = new();
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _run, _close;
    private bool _retiring;
    public sealed record Configuration(LinuxHomeBootstrapActivationOwner.Request OriginalActivation, string ExistingControlDirectory);
    public sealed record StartupObservation(bool HasAcceptedOriginalHomeControl, string? MissingPrerequisite);
    private readonly TaskCompletionSource<StartupObservation> _startup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<StartupObservation> OriginalStartup => _startup.Task;
    public LinuxHomeRootServiceRuntime(LinuxHomeBootstrapActivationOwner sameActivationOwner)
    { _activation = sameActivationOwner ?? throw new ArgumentNullException(nameof(sameActivationOwner)); }
    public Task RunOriginalAsync(Configuration configuration, CancellationToken lifetime)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_run is not null) return _run;
            if (!lifetime.CanBeCanceled) throw new ArgumentException("The actual registered service lifetime must be cancellable.");
            return _run = Run(configuration, lifetime);
        }
    }
    private async Task Run(Configuration configuration, CancellationToken lifetime)
    {
        using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, lifetime);
        try
        {
            var observed = _kernel.ObserveOriginalActiveUser();
            if (observed.OriginalUser is not { } user)
            { _startup.TrySetResult(new(false, observed.UnavailableReason)); return; }
            await _activation.WithinSettledCodeAsync(configuration.OriginalActivation, async (code, token) =>
            {
                var home = code.Packages.Single(value => value.AppId == "home");
                var root = code.Packages.Single(value => value.AppId == "root");
                if (!root.ActivatedFiles.Any(value => value.RelativePath == "home-root-launcher"))
                { _startup.TrySetResult(new(false, "PublisherSignedNativeRootLauncherCohortRequired")); return; }
                using var rootImage = code.OpenOriginalFile(root, root.EntrypointRelativePath);
                using var self = File.OpenHandle("/proc/self/exe", FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!LinuxOriginalDownloadPaths.ReadIdentity(rootImage).SameFile(LinuxOriginalDownloadPaths.ReadIdentity(self)))
                    throw new UnauthorizedAccessException("The actual registered service executable must be the SAME enrolled activated Root ELF.");
                using var endpoint = new OriginalEndpoint(configuration.ExistingControlDirectory, user.UserId);
                LinuxHomeOriginalControlledProcess? controlled = null;
                Task? monitor = null; Exception? runtimeFailure = null;
                CancellationToken startupToken = default;
                try
                {
                    controlled = await LinuxHomeOriginalControlledProcess.CreateOriginalAsync(this, code, _kernel, user, root, home, endpoint.Path, token).ConfigureAwait(false);
                    await controlled.ResumeOriginalAsync(token).ConfigureAwait(false);
                    var exit = controlled.OriginalExit ?? throw new IOException("The SAME native controlled child exit observation is absent.");
                    monitor = MonitorControlledChild(controlled, token);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(45));
                    startupToken = deadline.Token;
                    using var accepted = await AcceptOriginalHome(endpoint.Listener, controlled.ProcessId, user, controlled.OriginalSupervisor, exit, deadline.Token).ConfigureAwait(false);
                    using var stream = new NetworkStream(accepted, ownsSocket: false);
                    var hello = await ReadFrame<LinuxHomeRootControlWire.Request>(stream, deadline.Token).ConfigureAwait(false);
                    if (hello is not { SchemaVersion: 1, Command: "hello-home" } || hello.CorrelationId == Guid.Empty)
                        throw new UnauthorizedAccessException("The original controlled Home must complete the bounded Root control protocol.");
                    controlled.DemandOriginalCurrent();
                    await code.DemandPinnedCurrentAsync(token).ConfigureAwait(false);
                    await WriteFrame(stream, new LinuxHomeRootControlWire.Response(1, hello.CorrelationId, true, null)
                    { Activation = root, PackageId = home.PackageId, CatalogueRevision = home.CatalogueRevision,
                        PackageIds = code.Packages.Select(value => value.PackageId).ToArray() }, deadline.Token).ConfigureAwait(false);
                    NativeWindowsHomeRootWire.Listening? listening = null;
                    _startup.TrySetResult(new(true, null));
                    while (!token.IsCancellationRequested)
                    {
                        var request = await ReadFrame<LinuxHomeRootControlWire.Request>(stream, token).ConfigureAwait(false);
                        if (request is null)
                        {
                            if (token.IsCancellationRequested) token.ThrowIfCancellationRequested();
                            throw new EndOfStreamException("The original controlled Home ended its Root channel without registered service retirement.");
                        }
                        if (request.SchemaVersion != 1 || request.CorrelationId == Guid.Empty)
                            throw new InvalidDataException("The original Root control command identity is invalid.");
                        controlled.DemandOriginalCurrent();
                        await code.DemandPinnedCurrentAsync(token).ConfigureAwait(false);
                        var package = request.PackageId is null ? null : code.Packages.SingleOrDefault(value => value.PackageId == request.PackageId);
                        if (request.Command == "publish-listening")
                        {
                            var actual = request.Listening;
                            if (actual is null || actual.Actor is null || actual.Actor.AccountId is not null || actual.Actor.OrganisationId is not null ||
                                actual.Actor.ProfileId != actual.ProfileId || string.IsNullOrWhiteSpace(actual.ProfileId) ||
                                actual.LeaseIdentity == Guid.Empty || actual.InstalledApplicationId == Guid.Empty ||
                                actual.InstallationRevision != Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(home))) ||
                                actual.OriginalSessionId != actual.LeaseIdentity.ToString("D") || !System.IO.Path.IsPathFullyQualified(actual.PipeName) ||
                                System.Text.Encoding.UTF8.GetByteCount(actual.PipeName) > 100 || listening is not null && listening != actual)
                                throw new UnauthorizedAccessException("Only the SAME controlled Home's current original actor/listening lease may be published.");
                            listening = actual;
                        }
                        var response = request.SchemaVersion != 1 || request.CorrelationId == Guid.Empty
                            ? throw new InvalidDataException("The original Root control command identity is invalid.")
                            : request.Command == "inspect-package" && package is not null
                                ? new LinuxHomeRootControlWire.Response(1, request.CorrelationId, true, null) { Activation = package,
                                    SignedDescriptor = Convert.FromBase64String(package.SignedDescriptorBase64), DescriptorPayload = Convert.FromBase64String(package.DescriptorPayloadBase64),
                                    PackageId = package.PackageId, CatalogueRevision = package.CatalogueRevision }
                                : request.Command is "check-home" or "publish-listening" ? new LinuxHomeRootControlWire.Response(1, request.CorrelationId, true, null) { Listening = listening,
                                    OriginalHomeProcess = new(controlled.ProcessId, "unix-euid:" + user.UserId.ToString(CultureInfo.InvariantCulture),
                                        controlled.ProcessStartIdentity ?? throw new UnauthorizedAccessException("The actual Home process start identity is unavailable."),
                                        "home", home.PackageId, Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(home))),
                                        "sha256:" + home.ActivatedFiles.Single(value => value.RelativePath == home.EntrypointRelativePath).Sha256) }
                                : new LinuxHomeRootControlWire.Response(1, request.CorrelationId, false, "OriginalLinuxCommandNotImplemented");
                        await WriteFrame(stream, response, token).ConfigureAwait(false);
                    }

                }
                catch (OperationCanceledException cause) when ((lifetime.IsCancellationRequested || _retiring) &&
                    (cause.CancellationToken == token && token.IsCancellationRequested ||
                     cause.CancellationToken == startupToken && startupToken.IsCancellationRequested)) { }
                catch (Exception cause) { runtimeFailure = cause; }
                finally
                {
                    var failures = new List<Exception>();
                    if (runtimeFailure is not null) failures.Add(runtimeFailure);
                    try { _stop.Cancel(); } catch (Exception cause) { failures.Add(cause); }
                    if (controlled is not null)
                        try { await controlled.CloseAndDrainAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
                    if (monitor is not null)
                        try { await monitor.ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
                    if (failures.Count != 0) throw new AggregateException("Original Linux Root runtime/child retirement retained actual failures.", failures);
                }
            }, linked.Token).ConfigureAwait(false);
            _startup.TrySetResult(new(false, "OriginalLinuxHomeControlEnded"));
        }
        catch (OperationCanceledException cause) when (linked.IsCancellationRequested && (cause.CancellationToken == linked.Token || cause.CancellationToken == lifetime || cause.CancellationToken == _stop.Token))
        { _startup.TrySetCanceled(linked.Token); }
        catch (Exception cause) { _startup.TrySetException(cause); throw; }
    }
    private async Task MonitorControlledChild(LinuxHomeOriginalControlledProcess controlled, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            { controlled.DemandOriginalCurrent(); await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false); }
        }
        catch (OperationCanceledException cause) when (token.IsCancellationRequested && cause.CancellationToken == token) { }
        catch { _stop.Cancel(); throw; }
    }

    internal static ProcessStartInfo HelperStart(HomePackageOriginalInstalledActivationRecord root, HomePackageOriginalInstalledActivationRecord home,
        LinuxOriginalDownloadIdentity image, LinuxHomeRegisteredRootServiceContext.InteractiveUser user, string endpoint)
    {
        var start = new ProcessStartInfo(Path.Combine(root.InstallationRoot, "home-root-launcher"))
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = root.InstallationRoot, CreateNoWindow = true };
        start.Environment.Clear();
        foreach (var value in new[] { home.AppId == "home" ? "--original-home-v1" : "--original-selected-v1", user.UserId.ToString(CultureInfo.InvariantCulture), home.InstallationRoot, home.EntrypointRelativePath,
            image.Inode.ToString(CultureInfo.InvariantCulture), image.DeviceMajor.ToString(CultureInfo.InvariantCulture), image.DeviceMinor.ToString(CultureInfo.InvariantCulture),
            image.Size.ToString(CultureInfo.InvariantCulture), image.ModifiedSeconds.ToString(CultureInfo.InvariantCulture), image.ModifiedNanos.ToString(CultureInfo.InvariantCulture),
            image.ChangedSeconds.ToString(CultureInfo.InvariantCulture), image.ChangedNanos.ToString(CultureInfo.InvariantCulture), endpoint, user.SessionType, user.Display ?? "" })
            start.ArgumentList.Add(value);
        return start;
    }
    private async Task<Socket> AcceptOriginalHome(Socket listener, int child, LinuxHomeRegisteredRootServiceContext.InteractiveUser user,
        Process supervisor, Task exit, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var accepted = await listener.AcceptAsync(token).ConfigureAwait(false);
            var observed = HomeNativePeerObservation.FromAcceptedUnixSocket(accepted);
            if (!supervisor.HasExited && !exit.IsCompleted && observed?.ProcessId == child && observed.OperatingSystemPrincipalId == user.OperatingSystemPrincipalId)
                return accepted;
            accepted.Dispose(); _kernel.DemandOriginalCurrent(user);
        }
    }
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    { MaxDepth = 32, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    // Same maintained Root message schema and two-MiB length framing. These direct
    // awaits belong to this service lifetime; no borrowed callback/parallel graph.
    internal static async Task<T?> ReadFrame<T>(Stream stream, CancellationToken token) where T : class
    {
        var header = new byte[4];
        var first = await stream.ReadAsync(header.AsMemory(0, 1), token).ConfigureAwait(false);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > 2 * 1024 * 1024) throw new InvalidDataException("The original Root frame exceeds its bound.");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(bytes, WireJson) ?? throw new InvalidDataException("The original Root frame is absent.");
    }
    internal static async Task WriteFrame<T>(Stream stream, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, WireJson);
        if (bytes.Length is <= 0 or > 2 * 1024 * 1024) throw new InvalidDataException("The original Root frame exceeds its bound.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, token).ConfigureAwait(false); await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    internal static async Task<string> ReadLine(StreamReader reader, CancellationToken token)
    {
        var result = new System.Text.StringBuilder(); var character = new char[1];
        while (result.Length <= 512)
        {
            if (await reader.ReadAsync(character, token).ConfigureAwait(false) == 0) throw new EndOfStreamException("The original native control frame ended.");
            if (character[0] == '\n') return result.ToString();
            result.Append(character[0]);
        }
        throw new InvalidDataException("The original native control frame exceeds its bound.");
    }
    internal static async Task<string> ReadBoundedErrors(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[1024]; var result = new System.Text.StringBuilder(); int count;
        while ((count = await reader.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (result.Length + count > 8192) throw new IOException("The original native supervisor diagnostic bound was exceeded.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
    public Task CloseAndDrainAsync()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_gate) { _retiring = true; return _close ??= Close(); }
        async Task Close() { _stop.Cancel(); if (_run is { } run) await run.ConfigureAwait(false); _stop.Dispose(); }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private sealed class OriginalEndpoint : IDisposable
    {
        private readonly SafeFileHandle _directory;
        private readonly string _leaf;
        private readonly LinuxOriginalDownloadIdentity _identity;
        internal Socket Listener { get; }
        internal string Path { get; }
        internal OriginalEndpoint(string directory, uint user)
        {
            directory = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(directory));
            LinuxHomePublisherEnrollmentOwner.DemandProtectedMachineDirectory(directory);
            _directory = LinuxOriginalDownloadPaths.OpenAbsoluteDirectory(directory, mutable: true);
            LinuxOriginalDownloadPaths.DemandOwner(_directory, 0);
            if ((LinuxOriginalDownloadPaths.ReadIdentity(_directory).Mode & 1) == 0)
            { _directory.Dispose(); throw new UnauthorizedAccessException("The protected Root control directory must already allow local-user traversal; private state directories are not adopted."); }
            _leaf = "root-control-" + Guid.NewGuid().ToString("N") + ".sock";
            Path = System.IO.Path.Combine(directory, _leaf);
            Listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                var address = System.Text.Encoding.UTF8.GetBytes(Path);
                if (address.Length > 100) throw new ArgumentException("The explicit Root control socket path exceeds the native bound.");
                var native = new byte[110]; native[0] = 1; address.CopyTo(native, 2);
                if (Bind(Listener.SafeHandle, native, (uint)(address.Length + 3)) != 0) throw new IOException("The original Root control endpoint bind failed: " + Marshal.GetLastPInvokeError());
                using var pin = LinuxOriginalDownloadPaths.OpenAt(_directory, _leaf, 0x200000UL | 0x80000UL);
                _identity = LinuxOriginalDownloadPaths.ReadIdentity(pin);
                if (Chmod("/proc/self/fd/" + pin.DangerousGetHandle().ToInt64().ToString(CultureInfo.InvariantCulture), 0x180) != 0 ||
                    ChownAt(pin, "", user, uint.MaxValue, 0x1000 | 0x100) != 0) throw new IOException("The bounded original Home control socket permissions failed.");
                using var current = LinuxOriginalDownloadPaths.OpenAt(_directory, _leaf, 0x200000UL | 0x80000UL);
                if (!_identity.SameFile(LinuxOriginalDownloadPaths.ReadIdentity(current))) throw new IOException("The original Root control socket changed before listening.");
                Listener.Listen(8);
            }
            catch { Listener.Dispose(); Cleanup(); _directory.Dispose(); throw; }
        }
        private void Cleanup()
        {
            try
            {
                using var current = LinuxOriginalDownloadPaths.OpenAt(_directory, _leaf, 0x200000UL | 0x80000UL);
                if (_identity.SameFile(LinuxOriginalDownloadPaths.ReadIdentity(current)) && UnlinkAt(_directory, _leaf, 0) != 0)
                    throw new IOException("The original Root control socket removal failed.");
            }
            catch (FileNotFoundException) { }
        }
        public void Dispose() { try { Listener.Dispose(); Cleanup(); } finally { _directory.Dispose(); } }
        [DllImport("libc", EntryPoint = "bind", SetLastError = true)] private static extern int Bind(SafeSocketHandle socket, byte[] address, uint length);
        [DllImport("libc", EntryPoint = "chmod", SetLastError = true)] private static extern int Chmod([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
        [DllImport("libc", EntryPoint = "fchownat", SetLastError = true)] private static extern int ChownAt(SafeFileHandle file, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint user, uint group, int flags);
        [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)] private static extern int UnlinkAt(SafeFileHandle directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string leaf, int flags);
    }
}

// Linux uses the same Root command/correlation/listening semantics and bounded
// framing; these detached records grant no installed role or launch authority.
internal static class LinuxHomeRootControlWire
{
    internal sealed record ProcessWitness(int ProcessId, string OperatingSystemPrincipalId, string ProcessStartIdentity,
        string AppId, string PackageId, string InstallationRevision, string ExecutableIdentity);
    internal sealed record Request(int SchemaVersion, Guid CorrelationId, string Command)
    { public string? PackageId { get; init; } public NativeWindowsHomeRootWire.Listening? Listening { get; init; } }
    internal sealed record Response(int SchemaVersion, Guid CorrelationId, bool Accepted, string? Reason)
    {
        public string? PackageId { get; init; }
        public string? CatalogueRevision { get; init; }
        public IReadOnlyList<string>? PackageIds { get; init; }
        public HomePackageOriginalInstalledActivationRecord? Activation { get; init; }
        public byte[]? SignedDescriptor { get; init; }
        public byte[]? DescriptorPayload { get; init; }
        public NativeWindowsHomeRootWire.Listening? Listening { get; init; }
        public ProcessWitness? OriginalHomeProcess { get; init; }
    }
}
