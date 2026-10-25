using System.Diagnostics;
using System.Buffers.Binary;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
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
                using var helper = code.OpenOriginalFile(root, "home-root-launcher");
                using var entry = code.OpenOriginalFile(home, home.EntrypointRelativePath);
                var helperIdentity = LinuxOriginalDownloadPaths.ReadIdentity(helper);
                var entryIdentity = LinuxOriginalDownloadPaths.ReadIdentity(entry);
                if ((helperIdentity.Mode & 0x1ff) != 0x1ed || (entryIdentity.Mode & 0x1ff) != 0x1ed)
                    throw new UnauthorizedAccessException("The exact signed helper and Home native entrypoint must retain mode0755.");
                using var endpoint = new OriginalEndpoint(configuration.ExistingControlDirectory, user.UserId);
                _kernel.DemandOriginalCurrent(user); await code.DemandPinnedCurrentAsync(token).ConfigureAwait(false);
                using var process = new Process { StartInfo = HelperStart(root, home, entryIdentity, user, endpoint.Path), EnableRaisingEvents = true };
                if (!process.Start()) throw new IOException("The actual signed native child supervisor did not start.");
                var errors = ReadBoundedErrors(process.StandardError, CancellationToken.None);
                Task? exit = null, monitor = null;
                try
                {
                    using (var actualSupervisorImage = File.OpenHandle("/proc/" + process.Id.ToString(CultureInfo.InvariantCulture) + "/exe", FileMode.Open, FileAccess.Read, FileShare.Read))
                        if (!helperIdentity.SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(actualSupervisorImage)))
                            throw new UnauthorizedAccessException("The actual native supervisor must execute the SAME retained signed helper inode/version.");
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(15));
                    var frame = await ReadLine(process.StandardOutput, deadline.Token).ConfigureAwait(false);
                    var parts = frame.Split(':');
                    if (parts.Length != 3 || parts[0] != "OriginalLinuxHomeChildV1" || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var child) || child <= 0 ||
                        parts[2] != user.UserId.ToString(CultureInfo.InvariantCulture))
                        throw new UnauthorizedAccessException("The original native supervisor did not issue its actual held child/UID observation.");
                    _kernel.DemandOriginalCurrent(user); await code.DemandPinnedCurrentAsync(token).ConfigureAwait(false);
                    await process.StandardInput.WriteAsync("R".AsMemory(), deadline.Token).ConfigureAwait(false);
                    await process.StandardInput.FlushAsync(deadline.Token).ConfigureAwait(false);
                    if (await ReadLine(process.StandardOutput, deadline.Token).ConfigureAwait(false) != "OriginalLinuxHomeExecV1")
                        throw new IOException("The original native helper did not confirm its SAME fd-exec effect.");
                    deadline.CancelAfter(TimeSpan.FromSeconds(45));
                    // The supervisor retains its child's pidfd and does not reap it
                    // until this owner retires. PID recycling cannot create a peer.
                    exit = ReadLine(process.StandardOutput, token);
                    monitor = MonitorChild(user, process, exit, child, entryIdentity, token);
                    using var accepted = await AcceptOriginalHome(endpoint.Listener, child, user, process, exit, deadline.Token).ConfigureAwait(false);
                    using var stream = new NetworkStream(accepted, ownsSocket: false);
                    var hello = await ReadFrame<NativeWindowsHomeRootWire.Request>(stream, deadline.Token).ConfigureAwait(false);
                    if (hello is not { SchemaVersion: 1, Command: "hello-home" } || hello.CorrelationId == Guid.Empty)
                        throw new UnauthorizedAccessException("The original controlled Home must complete the bounded Root control protocol.");
                    DemandChild(user, process, exit, child, entryIdentity);
                    await code.DemandPinnedCurrentAsync(token).ConfigureAwait(false);
                    await WriteFrame(stream, new NativeWindowsHomeRootWire.Response(1, hello.CorrelationId, true, null)
                    { Activation = home, PackageId = home.PackageId, CatalogueRevision = home.CatalogueRevision }, deadline.Token).ConfigureAwait(false);
                    _startup.TrySetResult(new(true, null));
                    while (!token.IsCancellationRequested)
                    {
                        var request = await ReadFrame<NativeWindowsHomeRootWire.Request>(stream, token).ConfigureAwait(false);
                        if (request is null) break;
                        DemandChild(user, process, exit, child, entryIdentity);
                        await code.DemandPinnedCurrentAsync(token).ConfigureAwait(false);
                        var package = request.PackageId is null ? null : code.Packages.SingleOrDefault(value => value.PackageId == request.PackageId);
                        var response = request.SchemaVersion != 1 || request.CorrelationId == Guid.Empty
                            ? throw new InvalidDataException("The original Root control command identity is invalid.")
                            : request.Command == "inspect-package" && package is not null
                                ? new NativeWindowsHomeRootWire.Response(1, request.CorrelationId, true, null) { Activation = package,
                                    SignedDescriptor = Convert.FromBase64String(package.SignedDescriptorBase64), DescriptorPayload = Convert.FromBase64String(package.DescriptorPayloadBase64),
                                    PackageId = package.PackageId, CatalogueRevision = package.CatalogueRevision }
                                : request.Command == "check-home" ? new NativeWindowsHomeRootWire.Response(1, request.CorrelationId, true, null)
                                : new NativeWindowsHomeRootWire.Response(1, request.CorrelationId, false, "OriginalLinuxCommandNotImplemented");
                        await WriteFrame(stream, response, token).ConfigureAwait(false);
                    }

                }
                finally
                {
                    _stop.Cancel();
                    process.StandardInput.Close(); // native supervisor kills/joins the SAME child
                    var wait = process.WaitForExitAsync(CancellationToken.None);
                    try { await wait.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false); }
                    catch (TimeoutException) { process.Kill(entireProcessTree: false); await wait.ConfigureAwait(false); }
                    var failures = new List<Exception>();
                    if (exit is not null)
                        try { await exit.ConfigureAwait(false); }
                        catch (OperationCanceledException cause) when (token.IsCancellationRequested && cause.CancellationToken == token) { }
                        catch (EndOfStreamException) { /* owned supervisor retired after its actual stdin close/exit */ }
                        catch (Exception cause) { failures.Add(cause); }
                    if (monitor is not null)
                        try { await monitor.ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
                    try
                    {
                        var diagnostic = await errors.ConfigureAwait(false);
                        if (process.ExitCode != 0)
                            failures.Add(new IOException("The actual signed native supervisor refused or failed (exit " + process.ExitCode + "): " + diagnostic));
                    }
                    catch (Exception cause) { failures.Add(cause); }
                    if (failures.Count != 0) throw new AggregateException("Original Linux Root child retirement retained actual failures.", failures);
                }
            }, linked.Token).ConfigureAwait(false);
            _startup.TrySetResult(new(false, "OriginalLinuxHomeControlEnded"));
        }
        catch (OperationCanceledException cause) when (linked.IsCancellationRequested && (cause.CancellationToken == linked.Token || cause.CancellationToken == lifetime || cause.CancellationToken == _stop.Token))
        { _startup.TrySetCanceled(linked.Token); }
        catch (Exception cause) { _startup.TrySetException(cause); throw; }
    }
    private async Task MonitorChild(LinuxHomeRegisteredRootServiceContext.InteractiveUser user, Process supervisor,
        Task exit, int child, LinuxOriginalDownloadIdentity image, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            { DemandChild(user, supervisor, exit, child, image); await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false); }
        }
        catch (OperationCanceledException cause) when (token.IsCancellationRequested && cause.CancellationToken == token) { }
        catch { _stop.Cancel(); throw; }
    }

    private void DemandChild(LinuxHomeRegisteredRootServiceContext.InteractiveUser user, Process supervisor, Task exit, int child,
        LinuxOriginalDownloadIdentity originalImage)
    {
        _kernel.DemandOriginalCurrent(user);
        if (supervisor.HasExited || exit.IsCompleted) throw new UnauthorizedAccessException("The original controlled Home/supervisor lifetime retired.");
        using var image = File.OpenHandle("/proc/" + child.ToString(CultureInfo.InvariantCulture) + "/exe", FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!originalImage.SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(image)))
            throw new UnauthorizedAccessException("The actual controlled child's executable changed after fd-exec.");
    }
    private static ProcessStartInfo HelperStart(HomePackageOriginalInstalledActivationRecord root, HomePackageOriginalInstalledActivationRecord home,
        LinuxOriginalDownloadIdentity image, LinuxHomeRegisteredRootServiceContext.InteractiveUser user, string endpoint)
    {
        var start = new ProcessStartInfo(Path.Combine(root.InstallationRoot, "home-root-launcher"))
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = root.InstallationRoot, CreateNoWindow = true };
        start.Environment.Clear();
        foreach (var value in new[] { "--original-home-v1", user.UserId.ToString(CultureInfo.InvariantCulture), home.InstallationRoot, home.EntrypointRelativePath,
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
    private static async Task<T?> ReadFrame<T>(Stream stream, CancellationToken token) where T : class
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
    private static async Task WriteFrame<T>(Stream stream, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, WireJson);
        if (bytes.Length is <= 0 or > 2 * 1024 * 1024) throw new InvalidDataException("The original Root frame exceeds its bound.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, token).ConfigureAwait(false); await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    private static async Task<string> ReadLine(StreamReader reader, CancellationToken token)
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
    private static async Task<string> ReadBoundedErrors(StreamReader reader, CancellationToken token)
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
