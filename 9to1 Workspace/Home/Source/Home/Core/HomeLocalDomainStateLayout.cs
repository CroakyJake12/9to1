using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HavenOS.Home.Core;

/// <summary>Owning-process Linux layout observation. The retained descriptors establish
/// actual private euid ownership and no-symlink resolution at each check. The legacy Home
/// store still performs named reads/writes; this is not a filesystem jail or adversarial CAS.</summary>
internal sealed class HomeLocalDomainStateLayout : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly string _parentPath;
    private readonly string _name;
    private int _parent = -1;
    private int _file = -1;
    private readonly Identity _parentIdentity;
    private Identity? _fileIdentity;
    private bool _everExisted;
    private bool _retiring;
    private Task? _close;
    private readonly List<Exception> _causes = [];
    [StructLayout(LayoutKind.Sequential)] private struct OpenHow { internal ulong Flags, Mode, Resolve; }
    private readonly record struct Identity(ulong Inode, uint Major, uint Minor, ushort Mode, uint Uid, uint Links)
    {
        internal bool SameNode(Identity other) => Inode == other.Inode && Major == other.Major && Minor == other.Minor;
    }

    internal HomeLocalDomainStateLayout(FileHomeCoreStateStore sameStore)
    {
        if (!OperatingSystem.IsLinux() || !Environment.Is64BitProcess ||
            RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("A supported Linux64 domain layout primitive is required.");
        var path = sameStore.OriginalLocalDomainStatePath;
        _parentPath = Path.GetDirectoryName(path) ?? throw new ArgumentException("The actual Home state parent is absent.");
        _name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(_name) || _name is "." or ".." || _name.Contains('/'))
            throw new ArgumentException("The actual Home state filename is invalid.");
        try
        {
            _parent = Open(-100, _parentPath, directory: true);
            _parentIdentity = Read(_parent);
            DemandPrivate(_parentIdentity, directory: true);
            DemandCurrent();
        }
        catch (Exception primary)
        {
            List<Exception> failures = [primary];
            CloseDescriptor(ref _file, failures); CloseDescriptor(ref _parent, failures);
            throw new AggregateException("The original Home layout acquisition and cleanup failed.", failures);
        }
    }

    internal void DemandCurrent()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_causes.Count != 0) throw new AggregateException("The actual Home layout has unresolved native causes.", _causes);
            try
            {
                var held = Read(_parent); DemandPrivate(held, directory: true);
                if (!held.SameNode(_parentIdentity)) throw new UnauthorizedAccessException("The retained Home parent identity changed.");
                int named = -1;
                try
                {
                    named = Open(-100, _parentPath, directory: true);
                    var current = Read(named); DemandPrivate(current, directory: true);
                    if (!current.SameNode(_parentIdentity)) throw new UnauthorizedAccessException("The actual Home path names another parent.");
                }
                finally { CloseDescriptor(ref named, _causes); }
                if (_causes.Count != 0) throw new AggregateException("Original Home parent observation cleanup failed.", _causes);
                ObserveFile();
            }
            catch (Exception error)
            {
                Add(_causes, error);
                throw;
            }
        }
    }

    private void ObserveFile()
    {
        int acquired = -1;
        try
        {
            try { acquired = Open(_parent, _name, directory: false); }
            catch (FileNotFoundException) when (!_everExisted) { return; }
            var observed = Read(acquired); DemandPrivate(observed, directory: false);
            // The descriptor and currently named child must be the SAME inode, not a
            // path-only precheck. A racing rename after this observation is not excluded.
            var named = ReadNamed(_parent, _name); DemandPrivate(named, directory: false);
            if (!observed.SameNode(named)) throw new UnauthorizedAccessException("The original Home file observation was superseded.");
            if (_fileIdentity is { } previous && previous.SameNode(observed))
            {
                var held = Read(_file); DemandPrivate(held, directory: false);
                if (!held.SameNode(observed)) throw new UnauthorizedAccessException("The retained Home file identity changed.");
            }
            else
            {
                // This sealed helper has no asynchronous reader or escaping descriptor.
                // Retire the old observation only after its SAME finite use, genuine close
                // and exact new descriptor capture. Unknown close makes this guard fail-stop.
                CloseDescriptor(ref _file, _causes);
                if (_causes.Count != 0) throw new AggregateException("Original Home file retirement is unresolved.", _causes);
                _file = acquired; acquired = -1; _fileIdentity = observed;
            }
            _everExisted = true;
        }
        finally { CloseDescriptor(ref acquired, _causes); }
        if (_causes.Count != 0) throw new AggregateException("Original Home file observation cleanup failed.", _causes);
    }

    private static void DemandPrivate(Identity actual, bool directory)
    {
        var kind = directory ? 0x4000 : 0x8000;
        var permissions = directory ? 0x1c0 : 0x180; // 0700 or 0600, no special bits.
        if ((actual.Mode & 0xf000) != kind || (actual.Mode & 0xfff) != permissions ||
            actual.Uid != GetEuid() || actual.Inode == 0 || actual.Links == 0 || (!directory && actual.Links != 1))
            throw new UnauthorizedAccessException("Use the actual euid-owned private Home directory and regular state file; no adoption or chmod occurs.");
    }
    private static int Open(int parent, string path, bool directory)
    {
        // O_NONBLOCK refuses FIFO/device candidates without blocking before kind checks.
        var how = new OpenHow { Flags = 0x80000UL | 0x800UL | (directory ? 0x10000UL : 0UL), Mode = 0, Resolve = 0x6UL };
        var result = OpenAt2(437, parent, path, ref how, (ulong)Marshal.SizeOf<OpenHow>());
        if (result >= 0) return checked((int)result);
        var error = Marshal.GetLastPInvokeError();
        if (error == 2) throw new FileNotFoundException("The original Home component is absent.", path);
        if (error is 22 or 38 or 95) throw new PlatformNotSupportedException("The required Home descriptor primitive is unavailable.");
        if (error is 18 or 40) throw new UnauthorizedAccessException("The actual Home path crosses an unsupported root or symlink.");
        throw new Win32Exception(error, "The actual Home descriptor acquisition failed.");
    }
    private static Identity Read(int descriptor)
    {
        if (descriptor < 0) throw new ObjectDisposedException("actual Home descriptor");
        return ReadAt(descriptor, "", 0x1000);
    }
    private static Identity ReadNamed(int parent, string name) => ReadAt(parent, name, 0x100);
    private static Identity ReadAt(int descriptor, string name, int flags)
    {
        var bytes = new byte[256];
        if (Statx(descriptor, name, flags, 0x10fU, bytes) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The actual Home descriptor metadata is unavailable.");
        const uint consumed = 0x10fU; // TYPE, MODE, NLINK, UID, INO.
        if ((BitConverter.ToUInt32(bytes, 0) & consumed) != consumed)
            throw new PlatformNotSupportedException("The kernel did not provide every consumed Home identity/privacy field.");
        return new(BitConverter.ToUInt64(bytes, 32), BitConverter.ToUInt32(bytes, 136), BitConverter.ToUInt32(bytes, 140),
            BitConverter.ToUInt16(bytes, 28), BitConverter.ToUInt32(bytes, 20), BitConverter.ToUInt32(bytes, 16));
    }
    private static void CloseDescriptor(ref int actual, List<Exception> errors)
    {
        var captured = actual; actual = -1;
        // Never retry an uncertain close against a possibly reused descriptor number.
        if (captured < 0) return;
        try
        {
            if (Close(captured) != 0)
                Add(errors, new Win32Exception(Marshal.GetLastPInvokeError(), "The SAME original Home descriptor close is unresolved."));
        }
        catch (Exception error) { Add(errors, error); }
    }
    private static void Add(List<Exception> errors, Exception error)
    {
        if (error is AggregateException group) { foreach (var item in group.InnerExceptions) Add(errors, item); return; }
        if (!errors.Any(item => ReferenceEquals(item, error))) errors.Add(error);
    }
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource start;
        Task actual;
        lock (_gate)
        {
            if (_close is not null) return new(_close);
            _retiring = true;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = _close = CloseOriginal(start.Task);
        }
        start.SetResult(); return new(actual);
    }
    private async Task CloseOriginal(Task gate)
    {
        await gate.ConfigureAwait(false);
        List<Exception> errors;
        lock (_gate)
        {
            errors = [.. _causes];
            CloseDescriptor(ref _file, errors);
            CloseDescriptor(ref _parent, errors);
        }
        if (errors.Count != 0) throw new AggregateException("The SAME Home layout observations retired with actual native causes.", errors);
    }

    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static extern long OpenAt2(long number, int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, ref OpenHow how, ulong size);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, [Out] byte[] result);
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint GetEuid();
    [DllImport("libc", EntryPoint = "close", SetLastError = true)] private static extern int Close(int descriptor);
}
