using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace HavenOS.Home.Core;

public sealed partial class HomeNativeLinuxCoreHost
{
    // Linux lacks an atomic unlink-if-inode-matches primitive. This custody refuses
    // observed path diversion, using an euid-private parent and no-symlink descriptors;
    // it does not promise exclusion of a hostile external writer with the same UID.
    private sealed class EndpointCustody : IDisposable
    {
        private readonly string _path, _parentPath, _leaf;
        private readonly SafeFileHandle _parent;
        private readonly EndpointIdentity _parentIdentity;
        private SafeFileHandle? _bound;
        private EndpointIdentity? _boundIdentity;
        private SafeFileHandle? _locator;
        private EndpointIdentity? _locatorIdentity;
        private string? _locatorLeaf;

        internal EndpointCustody(string path)
        {
            if (!OperatingSystem.IsLinux() || !BitConverter.IsLittleEndian ||
                RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
                throw new PlatformNotSupportedException("The Unix accepting host requires supported Linux64 openat2/statx.");
            _path = Path.GetFullPath(path); _parentPath = Path.GetDirectoryName(_path)!; _leaf = Path.GetFileName(_path);
            if (_leaf is "" or "." or ".." || _leaf.Any(value => value == '/' || char.IsControl(value)))
                throw new ArgumentException("A scalar Unix socket filename is required.");
            _parent = OpenEndpoint(-100, _parentPath, directory: true);
            try
            {
                _parentIdentity = ReadEndpoint(_parent);
                DemandPrivateParent(_parentIdentity);
                DemandParentCurrent();
            }
            catch { _parent.Dispose(); throw; }
        }

        internal void DemandParentCurrent()
        {
            var held = ReadEndpoint(_parent); DemandPrivateParent(held);
            using var named = OpenEndpoint(-100, _parentPath, directory: true);
            var actual = ReadEndpoint(named); DemandPrivateParent(actual);
            if (!_parentIdentity.SameNode(held) || !_parentIdentity.SameNode(actual))
                throw new UnauthorizedAccessException("The original private Unix endpoint directory changed.");
        }

        internal Socket BindOriginalSocket()
        {
            DemandParentCurrent();
            var descriptor = EndpointSocket(1, 1 | 0x80000, 0); // AF_UNIX, STREAM|CLOEXEC
            if (descriptor < 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Acquire the original Unix listening socket.");
            var held = new SafeSocketHandle((IntPtr)descriptor, ownsHandle: true);
            try
            {
                var name = Encoding.UTF8.GetBytes(_path);
                var address = new byte[2 + name.Length + 1]; address[0] = 1; // native sockaddr_un family
                name.CopyTo(address, 2);
                if (EndpointBind(held, address, (uint)address.Length) != 0)
                    throw new SocketException(Marshal.GetLastPInvokeError());
                CaptureBoundSocket();
                // Managed Bind would set BoundFileName: Dispose then blindly deletes
                // that name even if replaced. The SafeSocketHandle constructor loads
                // getsockname without taking that path-deletion ownership. Only this
                // custody owner may remove the original filesystem socket inode.
                return new Socket(held);
            }
            catch { held.Dispose(); throw; }
        }

        private void CaptureBoundSocket()
        {
            DemandParentCurrent();
            _bound = OpenEndpoint(Fd(_parent), _leaf, directory: false);
            var observed = ReadEndpoint(_bound);
            if ((observed.Mode & 0xf000) != 0xc000 || observed.Uid != GetEuid() || observed.Links != 1)
                throw new UnauthorizedAccessException("The actual bound Unix name is not an original euid-owned socket.");
            // chmod follows only THIS retained O_PATH inode via procfs, not a mutable
            // user-supplied name. No process-wide umask change or symlink-target chmod.
            if (Chmod("/proc/self/fd/" + Fd(_bound), 0x180) != 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Protect the SAME bound Unix socket inode.");
            _boundIdentity = ReadEndpoint(_bound);
            DemandCurrent();
            FlushEndpoint(_parent);
        }

        internal void DemandCurrent()
        {
            DemandParentCurrent();
            if (_bound is null || _boundIdentity is not { } original)
                throw new UnauthorizedAccessException("The original bound Unix socket has no retained identity.");
            var held = ReadEndpoint(_bound);
            var named = ReadEndpointAt(Fd(_parent), _leaf, 0x100);
            foreach (var observed in new[] { held, named })
                if (!original.SameNode(observed) || observed.Mode != 0xc180 || observed.Uid != GetEuid() || observed.Links != 1)
                    throw new UnauthorizedAccessException("The original private Unix socket name, owner or mode changed.");
        }

        internal void RemoveOwnedSocket()
        {
            if (_boundIdentity is null) return; // bind/capture failure never owns a colliding name
            DemandCurrent();
            if (UnlinkAt(Fd(_parent), _leaf, 0) != 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Remove only the original bound Unix socket name.");
            FlushEndpoint(_parent);
        }
        internal void PublishLocator(HomeNativeEndpointLocation endpoint)
        {
            DemandCurrent();
            var path = Path.GetFullPath(endpoint.LocatorPath);
            var leaf = Path.GetFileName(path);
            if (Path.GetDirectoryName(path) != _parentPath || leaf is "" or "." or ".." ||
                leaf.Any(value => value is '/' or '\\' || char.IsControl(value)) || path == _path)
                throw new UnauthorizedAccessException("The routing locator must be a distinct scalar file in the SAME private endpoint directory.");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(endpoint);
            if (bytes.Length > 8192) throw new InvalidDataException("The native Home endpoint locator exceeds its bound.");
            _locatorLeaf = "." + endpoint.Epoch.ToString("N") + ".endpoint-stage";
            var how = new EndpointOpenHow { Flags = 2UL | 0x40UL | 0x80UL | 0x80000UL, Mode = 0x180, Resolve = 0xf };
            var descriptor = OpenEndpointAt2(437, Fd(_parent), _locatorLeaf, ref how, (ulong)Marshal.SizeOf<EndpointOpenHow>());
            if (descriptor < 0 || descriptor > int.MaxValue)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Exclusively create the original private endpoint locator stage.");
            _locator = new((IntPtr)descriptor, ownsHandle: true);
            _locatorIdentity = ReadEndpoint(_locator);
            DemandLocatorCurrent();
            RandomAccess.Write(_locator, bytes, 0);
            FlushEndpoint(_locator);
            DemandCurrent(); DemandLocatorCurrent();
            // Publication never replaces a locator from another live or crashed
            // owner. That preserved record requires its own explicit recovery.
            if (EndpointRenameAt2(Fd(_parent), _locatorLeaf, Fd(_parent), leaf, 1) != 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Publish the original endpoint locator without replacement.");
            _locatorLeaf = leaf;
            DemandLocatorCurrent(); FlushEndpoint(_parent);
        }

        private void DemandLocatorCurrent()
        {
            DemandParentCurrent();
            if (_locator is null || _locatorIdentity is not { } original || _locatorLeaf is null)
                throw new UnauthorizedAccessException("The original endpoint locator has no retained inode.");
            foreach (var actual in new[] { ReadEndpoint(_locator), ReadEndpointAt(Fd(_parent), _locatorLeaf, 0x100) })
                if (!original.SameNode(actual) || actual.Mode != 0x8180 || actual.Uid != GetEuid() || actual.Links != 1)
                    throw new UnauthorizedAccessException("The original private endpoint locator name, owner or mode changed.");
        }

        internal void RemoveOwnedLocator()
        {
            if (_locatorIdentity is null) return;
            DemandLocatorCurrent();
            if (UnlinkAt(Fd(_parent), _locatorLeaf!, 0) != 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Remove only the original endpoint locator inode.");
            FlushEndpoint(_parent);
        }
        public void Dispose() { _locator?.Dispose(); _bound?.Dispose(); _parent.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct EndpointOpenHow { internal ulong Flags, Mode, Resolve; }
    private readonly record struct EndpointIdentity(ulong Inode, uint Major, uint Minor, ushort Mode, uint Uid, uint Links)
    {
        internal bool SameNode(EndpointIdentity other) => Inode == other.Inode && Major == other.Major && Minor == other.Minor;
    }
    private static void DemandPrivateParent(EndpointIdentity identity)
    {
        if (identity.Mode != 0x41c0 || identity.Uid != GetEuid() || identity.Inode == 0 || identity.Links == 0)
            throw new UnauthorizedAccessException("Use the existing euid-owned mode0700 Unix endpoint parent; no adoption or chmod occurs.");
    }
    private static SafeFileHandle OpenEndpoint(int parent, string path, bool directory)
    {
        var directoryFlag = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 0x4000UL : 0x10000UL;
        // Directory handles support fsync. The socket namespace inode uses O_PATH;
        // connecting/accepting remains owned by System.Net.Sockets, never this handle.
        var how = new EndpointOpenHow { Flags = 0x80000UL | (directory ? directoryFlag : 0x200000UL),
            Resolve = parent == -100 ? 0x6UL : 0xfUL };
        var descriptor = OpenEndpointAt2(437, parent, path, ref how, (ulong)Marshal.SizeOf<EndpointOpenHow>());
        if (descriptor >= 0 && descriptor <= int.MaxValue) return new((IntPtr)descriptor, ownsHandle: true);
        var error = Marshal.GetLastPInvokeError();
        if (error is 22 or 38 or 95) throw new PlatformNotSupportedException("The Unix endpoint needs actual openat2 descriptor custody.");
        if (error is 18 or 40) throw new UnauthorizedAccessException("The Unix endpoint traversed a mount or symbolic link.");
        throw new Win32Exception(error, "Retain the original Unix endpoint descriptor.");
    }
    private static EndpointIdentity ReadEndpoint(SafeFileHandle descriptor) => ReadEndpointAt(Fd(descriptor), "", 0x1000);
    private static EndpointIdentity ReadEndpointAt(int parent, string name, int flags)
    {
        var bytes = new byte[256]; const uint required = 0x10f;
        if (EndpointStatx(parent, name, flags, required, bytes) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Observe the original Unix endpoint inode.");
        if ((BitConverter.ToUInt32(bytes, 0) & required) != required)
            throw new PlatformNotSupportedException("The kernel omitted required Unix endpoint identity fields.");
        return new(BitConverter.ToUInt64(bytes, 32), BitConverter.ToUInt32(bytes, 136), BitConverter.ToUInt32(bytes, 140),
            BitConverter.ToUInt16(bytes, 28), BitConverter.ToUInt32(bytes, 20), BitConverter.ToUInt32(bytes, 16));
    }
    private static int Fd(SafeFileHandle handle)
    {
        if (handle.IsClosed || handle.IsInvalid) throw new ObjectDisposedException("original Unix endpoint descriptor");
        return handle.DangerousGetHandle().ToInt32();
    }
    private static void FlushEndpoint(SafeFileHandle descriptor)
    { if (EndpointFsync(Fd(descriptor)) != 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Flush the original Unix endpoint namespace."); }
    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static extern long OpenEndpointAt2(long number, int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, ref EndpointOpenHow how, ulong size);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int EndpointStatx(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags, uint mask, [Out] byte[] result);
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint GetEuid();
    [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
    private static extern int Chmod([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
    [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)]
    private static extern int UnlinkAt(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags);
    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)] private static extern int EndpointFsync(int descriptor);
    [DllImport("libc", EntryPoint = "socket", SetLastError = true)] private static extern int EndpointSocket(int family, int kind, int protocol);
    [DllImport("libc", EntryPoint = "bind", SetLastError = true)]
    private static extern int EndpointBind(SafeSocketHandle socket, byte[] address, uint length);
    [DllImport("libc", EntryPoint = "renameat2", SetLastError = true)]
    private static extern int EndpointRenameAt2(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string source,
        int destinationParent, [MarshalAs(UnmanagedType.LPUTF8Str)] string destination, uint flags);
}
