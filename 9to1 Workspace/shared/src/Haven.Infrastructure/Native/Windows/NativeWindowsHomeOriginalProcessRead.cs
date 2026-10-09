using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Windows;

/// <summary>Kernel observations of one retained process. Neither a PID, image
/// path nor a hash establishes installation, controlled launch or a Home lease.
/// The installed verifier must independently check those original owners.</summary>
[SupportedOSPlatform("windows")]
internal sealed class NativeWindowsHomeOriginalProcessRead
{
    private sealed class Resource(IDisposable actual)
    {
        internal readonly IDisposable Actual = actual;
        internal Task? Close;
    }
    private readonly object _originalVerifier;
    internal NativeWindowsHomeOriginalProcessRead(object originalVerifier) => _originalVerifier = originalVerifier;
    private readonly List<Resource> _resources = [];
    private SafeProcessHandle? _process;
    private SafeAccessTokenHandle? _token;
    private WindowsIdentity? _identity;
    private WindowsOriginalRoot? _root;
    private SafeFileHandle? _image;
    private readonly List<Task> _imageReads = [];
    private IncrementalHash? _hash;
    private WindowsOriginalFileIdentity _imageIdentity;
    private string? _imageSecurity;
    private ulong _start;
    private string? _principal, _path, _digest;
    private Task? _close;
    internal int ProcessId { get; private set; }
    internal string OperatingSystemPrincipalId => "windows-sid:" + _principal;
    internal string ProcessStartIdentity => "windows-filetime:" + _start.ToString(System.Globalization.CultureInfo.InvariantCulture);
    internal string ExecutableIdentity => "sha256:" + (_digest ?? throw new InvalidOperationException("The original executable read has not settled."));
    internal string ExecutablePath => _path ?? throw new InvalidOperationException("The actual process has not been observed.");
    internal Task? OriginalClose => _close;

    // Caller captures THIS object before entering any native or borrowed callback.
    // Each acquired object is then rooted here before the following source boundary.
    internal bool OpenOriginal(HomeNativeObservedPeer observed, CloudflareOriginalTaskLedger source)
    {
        if (_process is not null || observed.ProcessId <= 0) throw new InvalidOperationException("One original kernel read is required.");
        ProcessId = observed.ProcessId;
        source.Invoke(() =>
        {
            _process = OpenProcess(0x00100000 | 0x1000, false, (uint)ProcessId); // SYNCHRONIZE | QUERY_LIMITED_INFORMATION
            _resources.Add(new(_process));
            if (_process.IsInvalid) throw Native("OpenProcess");
            return true;
        });
        if (!source.Invoke(IsRunning)) return false;
        source.Invoke(() =>
        {
            if (!OpenProcessToken(_process!, 0x0008, out var token)) throw Native("OpenProcessToken");
            _token = token; _resources.Add(new(token));
            _identity = new WindowsIdentity(token.DangerousGetHandle()); _resources.Add(new(_identity));
            _principal = _identity.User?.Value ?? throw new UnauthorizedAccessException("The real process token has no Windows principal.");
            _start = ReadStart(); _path = ReadImagePath();
            return true;
        });
        if (observed.OperatingSystemPrincipalId != OperatingSystemPrincipalId) return false;
        source.Invoke(() =>
        {
            _path = WindowsOriginalFileCustody.NormalizeLocalPath(_path!);
            _root = WindowsOriginalFileCustody.RetainRoot(Path.GetDirectoryName(_path)!); _resources.Add(new(_root));
            _image = _root.OpenRead(_path); _resources.Add(new(_image));
            WindowsOriginalFileCustody.DemandPath(_image, _path, false);
            _imageIdentity = WindowsOriginalFileCustody.ReadIdentity(_image);
            if (!_imageIdentity.IsRegular || _imageIdentity.Links != 1 || _imageIdentity.Size is 0 or > 512UL * 1024 * 1024)
                throw new UnauthorizedAccessException("The original executable is not a bounded ordinary single-link image.");
            _imageSecurity = WindowsOriginalFileCustody.ReadSecurity(_image).Fingerprint;
            _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); _resources.Add(new(_hash));
            return true;
        });
        return true;
    }
    internal async Task ReadOriginalImageAsync(CloudflareOriginalTaskLedger source, Action<Task> retain, CancellationToken token)
    {
        if (_image is null || _hash is null) throw new InvalidOperationException("The SAME original image pin is required.");
        var buffer = new byte[65536]; long offset = 0;
        while ((ulong)offset != _imageIdentity.Size)
        {
            Task<int>? raw = null; Exception? publication = null;
            try
            {
                source.Invoke(() =>
                {
                    raw = RandomAccess.ReadAsync(_image, buffer.AsMemory(0,
                        (int)Math.Min(buffer.Length, (long)_imageIdentity.Size - offset)), offset, token).AsTask();
                    _imageReads.Add(raw); _ = source.Track(raw); retain(raw); return true;
                });
            }
            catch (Exception cause) { publication = cause; source.Retain(cause); }
            var count = raw is null ? 0 : await source.AwaitAsync(raw).ConfigureAwait(false);
            if (publication is not null) throw publication;
            if (count <= 0) throw new EndOfStreamException("The SAME pinned image read ended early.");
            source.Invoke(() => { _hash.AppendData(buffer.AsSpan(0, count)); return true; });
            offset += count;
        }
        source.Invoke(() => { _digest = Convert.ToHexString(_hash.GetHashAndReset()); DemandCurrent(); return true; });
    }
    internal async Task<byte[]> ReadOriginalBytesAsync(long offset, int length,
        CloudflareOriginalTaskLedger source, Action<Task> retain, CancellationToken token)
    {
        if (_image is null || offset < 0 || length < 1 || length > 16 * 1024 * 1024 ||
            (ulong)offset > _imageIdentity.Size || (ulong)length > _imageIdentity.Size - (ulong)offset)
            throw new InvalidDataException("The SAME original executable byte range is invalid or unbounded.");
        var bytes = new byte[length]; var read = 0;
        while (read != length)
        {
            Task<int>? raw = null; Exception? publication = null;
            try { source.Invoke(() => { raw = RandomAccess.ReadAsync(_image, bytes.AsMemory(read), offset + read, token).AsTask();
                _imageReads.Add(raw); _ = source.Track(raw); retain(raw); return true; }); }
            catch (Exception cause) { publication = cause; source.Retain(cause); }
            var count = raw is null ? 0 : await source.AwaitAsync(raw).ConfigureAwait(false);
            if (publication is not null) throw publication;
            if (count <= 0) throw new EndOfStreamException("The original pinned executable range ended early.");
            read += count;
        }
        source.Invoke(() => { DemandCurrent(); return true; }); return bytes;
    }
    internal void DemandCurrent()
    {
        if (_close is not null || _process is null || _token is null || _image is null || _root is null ||
            !IsRunning() || ReadStart() != _start || !StringComparer.OrdinalIgnoreCase.Equals(ReadImagePath(), _path) || ReadCurrentSid() != _principal)
            throw new UnauthorizedAccessException("The real original process exited or its start/image identity changed.");
        _root.DemandCurrent(); WindowsOriginalFileCustody.DemandPath(_image, _path!, false);
        if (!WindowsOriginalFileCustody.ReadIdentity(_image).SameReadVersion(_imageIdentity) ||
            WindowsOriginalFileCustody.ReadSecurity(_image).Fingerprint != _imageSecurity)
            throw new UnauthorizedAccessException("The original pinned executable or ACL changed.");
    }
    internal Task CloseAndDrainOriginalAsync(CloudflareOriginalTaskLedger source)
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        if (_close is not null) return _close;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _close = Close(start.Task); start.SetResult(); return _close;
        async Task Close(Task gate)
        {
            await gate.ConfigureAwait(false);
            using var original = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            // Join only THIS reader's exact image children, never the encompassing
            // verifier/source cohort which may already retain this close Task.
            foreach (var raw in _imageReads)
                try { await source.AwaitAsync(raw).ConfigureAwait(false); }
                catch (Exception cause) { source.Capture(raw, cause); }
            foreach (var resource in _resources.AsEnumerable().Reverse())
            {
                if (resource.Close is null)
                {
                    var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    resource.Close = Dispose(begin.Task, resource.Actual);
                    _ = source.Track(resource.Close); begin.SetResult();
                }
                try { await source.AwaitAsync(resource.Close).ConfigureAwait(false); }
                catch (Exception cause) { source.Capture(resource.Close, cause); }
            }
            if (source.OriginalErrors.Count != 0)
                throw new AggregateException("The SAME process/image original cleanup remains failed and rooted.", source.OriginalErrors);
        }
        async Task Dispose(Task gate, IDisposable actual)
        {
            await gate.ConfigureAwait(false);
            CloudflareOriginalExecutionGuard.InvokeOriginal(_originalVerifier, () =>
                CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { actual.Dispose(); return true; }));
        }
    }
    private string? ReadCurrentSid()
    {
        if (!OpenProcessToken(_process!, 0x0008, out var token)) throw Native("OpenProcessToken current");
        _resources.Add(new(token));
        var identity = new WindowsIdentity(token.DangerousGetHandle()); _resources.Add(new(identity));
        return identity.User?.Value;
    }
    private bool IsRunning()
    {
        if (!GetExitCodeProcess(_process!, out var code)) throw Native("GetExitCodeProcess");
        return code == 259; // STILL_ACTIVE
    }
    private ulong ReadStart()
    {
        if (!GetProcessTimes(_process!, out var created, out _, out _, out _)) throw Native("GetProcessTimes");
        return ((ulong)created.High << 32) | created.Low;
    }
    private string ReadImagePath()
    {
        var value = new StringBuilder(32768); var count = value.Capacity;
        if (!QueryFullProcessImageName(_process!, 0, value, ref count) || count <= 0) throw Native("QueryFullProcessImageName");
        return value.ToString();
    }
    private static Win32Exception Native(string operation) => new(Marshal.GetLastWin32Error(), operation + " did not return known original kernel evidence.");
    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low, High; }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint id);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint code);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeProcessHandle process, out FileTime created, out FileTime exited, out FileTime kernel, out FileTime user);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryFullProcessImageNameW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref int size);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
}
