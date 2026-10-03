using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell.Authority;

/// <summary>Private administrator spawn/shutdown composition. The signed native
/// helper owns atomic clone3 and waitid; this process retains the actual received
/// original child pidfd before GO. STARTED grants no target/actor/lease identity.
/// The caller must separately admit the actual final signed child/kernel tuple.</summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxNativeAtomicSupervisedSession : IAsyncDisposable
{
    private readonly Process _helper;
    private readonly LinuxOriginalSpawnPidfd _helperHandle;
    private readonly LinuxNativeIssuedChildPidfd _child;
    private readonly Socket _control;
    private readonly byte[] _nonce;
    private readonly string _controlPath;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _disposeGate = new();
    private readonly Task _watch;
    private Task? _dispose;
    private int _retired;
    private int _originalChildExitObserved;
    private int _intentionalOriginalHelperRetirement;
    private LinuxNativeAtomicSupervisedSession(Process helper, LinuxOriginalSpawnPidfd helperHandle,
        LinuxNativeIssuedChildPidfd child, Socket control, byte[] nonce, string controlPath)
    {
        _helper = helper; _helperHandle = helperHandle; _child = child; _control = control; _nonce = nonce; _controlPath = controlPath;
        _watch = WatchOriginalAsync();
    }
    internal int OriginalProcessId => _child.OriginalProcessId;
    internal ulong OriginalStartTicks => _child.OriginalStartTicks;
    internal bool OriginalExitObserved => Volatile.Read(ref _originalChildExitObserved) != 0;
    internal Task OriginalExitObservedTask => _watch;
    internal async Task<bool> IsOriginalLiveAsync(CancellationToken ct) =>
        Volatile.Read(ref _retired) == 0 && !_helper.HasExited &&
        await _child.IsOriginalLiveAsync(ct).ConfigureAwait(false) &&
        !_helper.HasExited && Volatile.Read(ref _retired) == 0;

    internal static async Task<LinuxNativeAtomicSupervisedSession?> StartForAdministratorAsync(
        LinuxRootAtomicSpawnHelperPreparation preparedHelper, string privateControlPath,
        string actualTargetWorkingDirectory, string actualTargetUserHome,
        IReadOnlyList<string> fixedSetprivArguments, Func<CancellationToken, Task<bool>> originalPreparedTargetCurrent, CancellationToken ct)
    {
        // Detach ALL caller-supplied launch scalars before the first owner await.
        ArgumentNullException.ThrowIfNull(originalPreparedTargetCurrent);
        var targetCurrent = originalPreparedTargetCurrent;
        var arguments = fixedSetprivArguments.ToArray();
        if (!OperatingSystem.IsLinux() || IntPtr.Size != 8 || arguments.Length > 127 ||
            arguments.Any(value => value is null || value.Length > 4096 || value.Contains('\0')) ||
            arguments.Sum(value => System.Text.Encoding.UTF8.GetByteCount(value) + 1) + System.Text.Encoding.UTF8.GetByteCount("/usr/bin/setpriv") + 1 > 32768 ||
            !Canonical(privateControlPath) || !Canonical(actualTargetWorkingDirectory) ||
            !Canonical(actualTargetUserHome) ||
            !LinuxRootOwnedFiles.DirectoryImmutable(Path.GetDirectoryName(privateControlPath)!) ||
            !LinuxRootOwnedFiles.DirectoryImmutable(actualTargetWorkingDirectory) ||
            File.Exists(privateControlPath) || Directory.Exists(privateControlPath) ||
            !LinuxRootOwnedFiles.RegularFileImmutable("/usr/bin/setpriv") ||
            await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct) != "unix-euid:0" ||
            !await HasActualKillCapabilityAsync(Environment.ProcessId, ct) ||
            !await preparedHelper.IsCurrentForAdministratorAsync(ct)) return null;
        var nonce = RandomNumberGenerator.GetBytes(32);
        using var listener = new Socket(AddressFamily.Unix, SocketType.Seqpacket, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(privateControlPath)); listener.Listen(1);
        File.SetUnixFileMode(privateControlPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var start = new ProcessStartInfo(preparedHelper.ExecutablePath) { UseShellExecute = false,
            WorkingDirectory = actualTargetWorkingDirectory };
        start.ArgumentList.Add(privateControlPath); start.ArgumentList.Add(Convert.ToHexStringLower(nonce));
        start.ArgumentList.Add("/usr/bin/setpriv"); foreach (var value in arguments) start.ArgumentList.Add(value);
        start.Environment.Clear(); start.Environment["HOME"] = actualTargetUserHome;
        start.Environment["PATH"] = "/usr/bin:/bin"; start.Environment["DOTNET_EnableDiagnostics"] = "0";
        var helper = Process.Start(start) ?? throw new IOException("Actual signed native supervisor did not start.");
        Socket? control = null; LinuxOriginalSpawnPidfd? helperHandle = null;
        LinuxNativeIssuedChildPidfd? child = null; var published = false; Exception? primary = null;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            control = await listener.AcceptAsync(deadline.Token).ConfigureAwait(false);
            helperHandle = await LinuxOriginalSpawnPidfd.CaptureAsync(helper, deadline.Token).ConfigureAwait(false);
            if (helperHandle is null) return null;
            child = await LinuxNativeIssuedChildPidfd.ReceiveCreatedAsync(control, helper, nonce, deadline.Token).ConfigureAwait(false);
            if (child is null || !await preparedHelper.MatchesActualRootHelperAsync(helper, deadline.Token) ||
                !await HasActualKillCapabilityAsync(helper.Id, deadline.Token) ||
                !await child.IsOriginalLiveAsync(deadline.Token) ||
                !await targetCurrent(deadline.Token).ConfigureAwait(false) ||
                !await child.IsOriginalLiveAsync(deadline.Token) ||
                !await preparedHelper.IsCurrentForAdministratorAsync(deadline.Token)) return null;
            await SendCommandAsync(control, nonce, 2, 1, deadline.Token).ConfigureAwait(false);
            var started = await ReadPacketAsync(control, nonce, child, 2, deadline.Token).ConfigureAwait(false);
            if (started.Kind == 4) throw new IOException("Actual native initial exec reported failure errno " + started.Value);
            if (started.Kind != 3 || started.Value != 0 || !await child.IsOriginalLiveAsync(deadline.Token) ||
                !await preparedHelper.MatchesActualRootHelperAsync(helper, deadline.Token)) return null;
            var result = new LinuxNativeAtomicSupervisedSession(helper, helperHandle, child, control, nonce, privateControlPath);
            published = true; control = null; child = null; helperHandle = null;
            return result;
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            if (!published)
            {
                Exception? cleanup = null; bool childExit = false, helperExit = false;
                void Attempt(Action action) { try { action(); } catch (Exception error) { cleanup ??= error; } }
                // EOF remains native-parent owned even when SCM_RIGHTS delivery/admission
                // failed; do not kill that helper before its bounded original-child drain.
                Attempt(() => control?.Dispose()); Attempt(listener.Dispose);
                try
                {
                    if (child is not null && !(childExit = await child.TerminateAndObserveOriginalExitAsync()))
                        throw new IOException("Original native child exit was not observed during startup cleanup.");
                }
                catch (Exception error) { cleanup ??= error; }
                try
                {
                    if (!helper.HasExited)
                    {
                        try { await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25)); }
                        catch (TimeoutException) when (childExit && helperHandle is not null)
                        { if (!await helperHandle.TerminateAndDrainSameActualSpawnAsync()) throw new IOException("Exact original helper drain failed."); }
                    }
                    helperExit = helper.HasExited;
                    if (child is not null && !child.OriginalExitObserved())
                        throw new IOException("Original native child remains live after helper startup failure.");
                    if (child is null && helper.ExitCode is not (0 or 70 or 71 or 72))
                        throw new IOException("Native no-descriptor failure did not establish parent cleanup.");
                    if (child is null && helperExit) childExit = true; // native parent status, not independent received-fd evidence
                }
                catch (Exception error) { cleanup ??= error; }
                Attempt(() => child?.Dispose()); Attempt(() => helperHandle?.Dispose()); Attempt(helper.Dispose);
                if (childExit && helperExit) Attempt(() => File.Delete(privateControlPath));
                if (cleanup is not null) throw primary is null ? cleanup : new AggregateException(primary, cleanup);
            }
        }
    }

    private async Task WatchOriginalAsync()
    {
        Exception? primary = null; Exception? cleanup = null; bool exitReceipt = false;
        try
        {
            while (!_child.OriginalExitObserved())
            {
                if (_helper.HasExited) break; // wrapper death is NOT child exit proof
                if (_control.Poll(0, SelectMode.SelectRead))
                {
                    using var packetDeadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                    packetDeadline.CancelAfter(TimeSpan.FromSeconds(10));
                    var packet = await ReadPacketAsync(_control, _nonce, _child, 3, packetDeadline.Token).ConfigureAwait(false);
                    if (packet.Kind != 5 || (packet.Value >> 16) is not (1 or 2 or 3) ||
                        (packet.Value & 0xffff) > 255 || !_child.OriginalExitObserved())
                        throw new IOException("Unexpected native control input while original child is live.");
                    exitReceipt = true; break;
                }
                await Task.Delay(10, _lifetime.Token).ConfigureAwait(false);
            }
            if (_helper.HasExited && !_child.OriginalExitObserved() && !await _child.TerminateAndObserveOriginalExitAsync())
                throw new IOException("Original UID-dropped child did not drain after native helper death.");
            if (!_helper.HasExited && !exitReceipt)
            {
                using var replyDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(replyDeadline.Token, _lifetime.Token);
                var exit = await ReadPacketAsync(_control, _nonce, _child, 3, linked.Token).ConfigureAwait(false);
                var code = exit.Value >> 16; var status = exit.Value & 0xffff;
                if (exit.Kind != 5 || code is not (1 or 2 or 3) || status > 255 || !_child.OriginalExitObserved())
                    throw new IOException("Actual native parent original waitid exit evidence required.");
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (EndOfStreamException) when (Volatile.Read(ref _intentionalOriginalHelperRetirement) != 0) { }
        catch (Exception error) { primary = error; }
        finally
        {
            Interlocked.Exchange(ref _retired, 1);
            try { if (!_child.OriginalExitObserved() && !await _child.TerminateAndObserveOriginalExitAsync())
                throw new IOException("Original received child pidfd drain failed.");
                if (_child.OriginalExitObserved()) Interlocked.Exchange(ref _originalChildExitObserved, 1); }
            catch (Exception error) { cleanup ??= error; }
            try { _control.Dispose(); } catch (Exception error) { cleanup ??= error; }
            try
            {
                if (!_helper.HasExited)
                {
                    try { await _helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25)); }
                    catch (TimeoutException) when (_child.OriginalExitObserved())
                    { if (!await _helperHandle.TerminateAndDrainSameActualSpawnAsync()) throw new IOException("Exact original helper exit was not observed."); }
                }
                if (_child.OriginalExitObserved()) Interlocked.Exchange(ref _originalChildExitObserved, 1);
                if (_helper.HasExited && OriginalExitObserved) File.Delete(_controlPath);
            }
            catch (Exception error) { cleanup ??= error; }
        }
        if (primary is not null && cleanup is not null) throw new AggregateException(primary, cleanup);
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        if (cleanup is not null) ExceptionDispatchInfo.Capture(cleanup).Throw();
    }
    internal async Task RetireOriginalHelperAndObserveChildExitForAdministratorAsync(CancellationToken ct)
    {
        if (await new OperatingSystemPrincipalSource().GetPrincipalAsync(ct) != "unix-euid:0" ||
            !await HasActualKillCapabilityAsync(Environment.ProcessId, ct) ||
            !await IsOriginalLiveAsync(ct))
            throw new UnauthorizedAccessException("Current original administrator session required.");
        // This intentionally retires the SAME signed helper. The watch must independently
        // drain the actual UID-dropped child through its already received original fd.
        Interlocked.Exchange(ref _intentionalOriginalHelperRetirement, 1);
        if (!await _helperHandle.TerminateAndDrainSameActualSpawnAsync())
            throw new IOException("Exact original native helper exit was not observed.");
        await _watch.ConfigureAwait(false);
        if (!OriginalExitObserved) throw new IOException("Original child exit after helper retirement was not observed.");
    }
    public ValueTask DisposeAsync()
    {
        lock (_disposeGate) { return new(_dispose ??= DisposeOwnedAsync()); }
    }
    private async Task DisposeOwnedAsync()
    {
        Exception? primary = null;
        Interlocked.Exchange(ref _retired, 1);
        try { _lifetime.Cancel(); } catch (Exception error) { primary = error; }
        try { await _watch.ConfigureAwait(false); } catch (Exception error) { primary ??= error; }
        void Attempt(Action action) { try { action(); } catch (Exception error) { primary ??= error; } }
        Attempt(_control.Dispose); Attempt(_child.Dispose); Attempt(_helperHandle.Dispose);
        Attempt(_helper.Dispose); Attempt(_lifetime.Dispose);
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private static async Task<(int Kind, uint Value)> ReadPacketAsync(Socket control, byte[] nonce,
        LinuxNativeIssuedChildPidfd child, ulong sequence, CancellationToken ct)
    {
        while (!control.Poll(0, SelectMode.SelectRead)) await Task.Delay(10, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var bytes = ReceiveNoRights(control); var count = bytes.Length;
        if (count != 64 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x31505341 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)) != 1 ||
            BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(8)) != sequence ||
            !bytes.AsSpan(16, 32).SequenceEqual(nonce) ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(48)) != child.OriginalProcessId ||
            BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(56)) != child.OriginalStartTicks)
            throw new InvalidDataException("Exact original native supervisor packet required.");
        return (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6)), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(52)));
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoVector { internal nint Data; internal nuint Length; }
    [StructLayout(LayoutKind.Sequential)] private struct Message
    { internal nint Name; internal uint NameLength; internal nint Vectors; internal nuint VectorCount;
      internal nint Control; internal nuint ControlLength; internal int Flags; }
    [DllImport("libc", SetLastError = true)] private static extern nint recvmsg(int socket, ref Message message, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int close(int descriptor);
    private static byte[] ReceiveNoRights(Socket socket)
    {
        var data = Marshal.AllocHGlobal(65); var ancillary = Marshal.AllocHGlobal(4096);
        var vector = Marshal.AllocHGlobal(16); var held = false; var descriptors = new List<int>();
        try
        {
            if (Marshal.SizeOf<Message>() != 56 || Marshal.SizeOf<IoVector>() != 16)
                throw new PlatformNotSupportedException("Exact Linux64 native message ABI required.");
            Marshal.StructureToPtr(new IoVector { Data = data, Length = 65 }, vector, false);
            var message = new Message { Vectors = vector, VectorCount = 1, Control = ancillary, ControlLength = 4096 };
            socket.SafeHandle.DangerousAddRef(ref held);
            var count = recvmsg(socket.SafeHandle.DangerousGetHandle().ToInt32(), ref message, 0x40000000 | 0x40);
            if (count < 0) throw new IOException("Native packet receipt failed; ancillary memory is not initialized.");
            var length = checked((int)message.ControlLength);
            if (length > 4096) throw new InvalidDataException("Native ancillary bound exceeded.");
            var offset = 0;
            while (offset + 16 <= length)
            {
                var size = unchecked((ulong)Marshal.ReadInt64(ancillary, offset));
                if (size < 16 || size > (ulong)(length - offset)) throw new InvalidDataException("Malformed native ancillary header.");
                var n = checked((int)size);
                if (Marshal.ReadInt32(ancillary, offset + 8) == 1 && Marshal.ReadInt32(ancillary, offset + 12) == 1)
                    for (var i = 16; i + 4 <= n; i += 4) descriptors.Add(Marshal.ReadInt32(ancillary, offset + i));
                offset += (n + 7) & ~7;
            }
            // Parse delivered rights even on a zero-byte packet before classifying EOF.
            if (count == 0 && length == 0 && (message.Flags & (0x20 | 0x8)) == 0) throw new EndOfStreamException("Original native helper control closed.");
            // Only CREATED may transfer a descriptor. Always close any delivered rights.
            if (count != 64 || length != 0 || (message.Flags & (0x20 | 0x8)) != 0)
                throw new InvalidDataException("Exact descriptor-free original native packet required.");
            var bytes = new byte[64]; Marshal.Copy(data, bytes, 0, 64); return bytes;
        }
        finally
        {
            foreach (var fd in descriptors) if (fd >= 0) close(fd);
            if (held) socket.SafeHandle.DangerousRelease();
            Marshal.FreeHGlobal(vector); Marshal.FreeHGlobal(ancillary); Marshal.FreeHGlobal(data);
        }
    }
    private static async Task SendCommandAsync(Socket control, byte[] nonce, ushort kind, ulong sequence, CancellationToken ct)
    {
        var bytes = new byte[64]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x31505341);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 1); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), kind);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), sequence); nonce.CopyTo(bytes, 16);
        if (await control.SendAsync(bytes, SocketFlags.None, ct).ConfigureAwait(false) != 64)
            throw new IOException("Original native supervisor command delivery incomplete.");
    }
    private static async Task<bool> HasActualKillCapabilityAsync(int processId, CancellationToken ct)
    {
        var status = await LinuxProcBoundedObservation.ReadAsync($"/proc/{processId}/status", 65536, ct);
        var rows = status?.Split('\n').Where(line => line.StartsWith("CapEff:", StringComparison.Ordinal)).ToArray();
        if (rows is null || rows.Length != 1) return false;
        var values = rows[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return values.Length == 2 && ulong.TryParse(values[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var caps) && (caps & (1ul << 5)) != 0;
    }
    private static bool Canonical(string path) => Path.IsPathFullyQualified(path) && Path.GetFullPath(path) == path;
}
