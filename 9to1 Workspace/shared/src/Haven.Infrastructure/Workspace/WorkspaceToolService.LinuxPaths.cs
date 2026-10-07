using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService
{
    /// <summary>Linux descriptor-relative original boundary, not an OS jail or an external-writer
    /// CAS. openat2 rejects symlinks/magic links at acquisition and anchors each operation to the
    /// SAME held root/parent. Linux permits external renames/writes: current observed path/inode
    /// checks refuse known substitution, without promising Windows share-delete exclusion.</summary>
    private sealed class OriginalLinuxPathLease : IDisposable
    {
        private readonly string _root;
        private readonly SafeFileHandle _rootHandle = null!;
        private readonly LinuxIdentity _rootIdentity;
        private bool _closed;
        public OriginalLinuxPathLease(string root)
        {
            if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
                throw new PlatformNotSupportedException("The Linux original requires a supported 64-bit Linux openat2 ABI.");
            _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if (_root == "/") throw new UnauthorizedAccessException("The system root is not a bounded development workspace.");
            RequireLinuxExports();
            SafeFileHandle? volume = null; SafeFileHandle? acquired = null;
            var errors = new List<Exception>();
            try
            {
                volume = WrapLinuxDescriptor(LinuxOpen("/", LinuxPath | LinuxDirectory | LinuxCloseOnExec, 0), "retain Linux volume");
                acquired = OpenLinuxAt(volume, _root.TrimStart('/'), LinuxPath | LinuxDirectory | LinuxCloseOnExec, 0, noMountCrossing: false);
                DemandLinuxDescriptorPath(acquired, _root);
                _rootIdentity = ReadLinuxIdentity(acquired);
                if (!_rootIdentity.IsDirectory) throw new UnauthorizedAccessException("The original Linux root is not a directory.");
                _rootHandle = acquired; acquired = null;
            }
            catch (Exception error) { AddOriginalErrors(errors, null, error); }
            finally
            {
                try { acquired?.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
                try { volume?.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            }
            ThrowOriginalErrors(errors);
            if (_rootHandle is null) throw new InvalidOperationException("No Linux original root descriptor was acquired.");
        }
        public void DemandCurrent()
        {
            if (_closed) throw new ObjectDisposedException(nameof(OriginalLinuxPathLease));
            DemandLinuxDescriptorPath(_rootHandle, _root);
            if (!ReadLinuxIdentity(_rootHandle).SameFile(_rootIdentity))
                throw new UnauthorizedAccessException("The actual Linux original root identity changed.");
        }
        private string Relative(string full)
        {
            full = Path.GetFullPath(full);
            if (!IsWithinRoot(_root, full, StringComparison.Ordinal)) throw new UnauthorizedAccessException("The Linux original escapes its root.");
            return full == _root ? "." : Path.GetRelativePath(_root, full);
        }
        public SafeFileHandle OpenDirectory(string full, bool flushable = false)
        {
            DemandCurrent(); var original = OpenLinuxAt(_rootHandle, Relative(full), (flushable ? 0UL : LinuxPath) | LinuxDirectory | LinuxCloseOnExec);
            try
            {
                DemandLinuxDescriptorPath(original, Path.TrimEndingDirectorySeparator(Path.GetFullPath(full)));
                if (!ReadLinuxIdentity(original).IsDirectory) throw new UnauthorizedAccessException("The original Linux parent is not a directory.");
                return original;
            }
            catch (Exception originalError)
            {
                var errors = new List<Exception> { originalError };
                try { original.Dispose(); } catch (Exception closeError) { AddOriginalErrors(errors, null, closeError); }
                ThrowOriginalErrors(errors); throw;
            }
        }
        public void EnsureDirectory(string full)
        { using var actual = OpenDirectory(full); }
        public SafeFileHandle OpenRead(string full)
        {
            DemandCurrent(); var original = OpenLinuxAt(_rootHandle, Relative(full), LinuxCloseOnExec | LinuxNonBlocking);
            try
            {
                DemandLinuxDescriptorPath(original, Path.GetFullPath(full));
                var identity = ReadLinuxIdentity(original);
                if (!identity.IsRegular || identity.Links != 1)
                    throw new UnauthorizedAccessException("The Linux original read requires a regular file without hard-link aliases.");
                if (identity.Size > 4UL * 1024 * 1024) throw new InvalidOperationException("The original file exceeds the existing 4 MB text-read limit.");
                return original;
            }
            catch (Exception originalError)
            {
                var errors = new List<Exception> { originalError };
                try { original.Dispose(); } catch (Exception closeError) { AddOriginalErrors(errors, null, closeError); }
                ThrowOriginalErrors(errors); throw;
            }
        }
        // These children are opened relative to the SAME retained directory, never a
        // mutable recursive path. The owning invocation supplies its fresh read fence.
        public SafeFileHandle OpenTraversalChild(SafeFileHandle parent, string name, string full)
        {
            DemandCurrent();
            var child = OpenLinuxAt(parent, name, LinuxPath | LinuxCloseOnExec);
            try
            {
                DemandLinuxDescriptorPath(child, full);
                var identity = ReadLinuxIdentity(child);
                if (!identity.IsDirectory && (!identity.IsRegular || identity.Links != 1))
                    throw new UnauthorizedAccessException("Original traversal refuses special files and hard-link aliases.");
                return child;
            }
            catch (Exception error)
            {
                var errors = new List<Exception> { error };
                try { child.Dispose(); } catch (Exception close) { AddOriginalErrors(errors, null, close); }
                ThrowOriginalErrors(errors); throw;
            }
        }
        public SafeFileHandle OpenTraversalRead(SafeFileHandle parent, string name, string full, LinuxIdentity expected)
        {
            DemandCurrent();
            var child = OpenLinuxAt(parent, name, LinuxCloseOnExec | LinuxNonBlocking);
            try
            {
                DemandLinuxDescriptorPath(child, full);
                var identity = ReadLinuxIdentity(child);
                if (!identity.IsRegular || identity.Links != 1 || identity.Size > 2UL * 1024 * 1024 || !expected.SameReadVersion(identity))
                    throw new IOException("The original traversal file changed before its bounded held-handle read.");
                return child;
            }
            catch (Exception error)
            {
                var errors = new List<Exception> { error };
                try { child.Dispose(); } catch (Exception close) { AddOriginalErrors(errors, null, close); }
                ThrowOriginalErrors(errors); throw;
            }
        }
        public string ProcessWorkingDirectory
        {
            get { DemandCurrent(); return $"/proc/{Environment.ProcessId}/fd/{LinuxDescriptor(_rootHandle)}"; }
        }
        public async Task<string> ReadTextAsync(string full, CancellationToken token)
        {
            var errors = new List<Exception>(); SafeFileHandle? handle = null; FileStream? stream = null; StreamReader? reader = null;
            Task<int>? actualRead = null; string? result = null;
            try
            {
                token.ThrowIfCancellationRequested(); handle = OpenRead(full); var before = ReadLinuxIdentity(handle);
                stream = new FileStream(handle, FileAccess.Read, 4096, isAsync: false);
                reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
                var text = new StringBuilder(); var buffer = new char[4096];
                while (true)
                {
                    actualRead = reader.ReadAsync(buffer.AsMemory(), token).AsTask();
                    var count = await actualRead.ConfigureAwait(false); if (count == 0) break;
                    if (text.Length + count > 4 * 1024 * 1024) throw new InvalidOperationException("The Linux source grew beyond the bounded original text-read limit.");
                    text.Append(buffer, 0, count);
                }
                DemandCurrent(); DemandLinuxDescriptorPath(handle, full); var after = ReadLinuxIdentity(handle);
                if (!after.IsRegular || after.Links != 1 || after.Size > 4UL * 1024 * 1024 || !before.SameReadVersion(after))
                    throw new IOException("The actual Linux original read identity changed.");
                result = text.ToString();
            }
            catch (Exception error) { AddOriginalErrors(errors, actualRead, error); }
            finally
            {
                try { reader?.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
                Task? actualClose = null;
                try { if (stream is not null) { actualClose = stream.DisposeAsync().AsTask(); await actualClose.ConfigureAwait(false); } }
                catch (Exception error) { AddOriginalErrors(errors, actualClose, error); }
                try { handle?.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            }
            ThrowOriginalErrors(errors, actualRead?.IsCanceled == true);
            return result ?? throw new InvalidDataException("The original Linux read returned no text.");
        }
        public void Dispose() { if (_closed) return; _closed = true; _rootHandle.Dispose(); }
    }

    private const ulong LinuxPath = 0x200000, LinuxDirectory = 0x10000, LinuxCloseOnExec = 0x80000, LinuxNonBlocking = 0x800;
    private const ulong LinuxAnonymousWrite = 0x410000 | 2 | LinuxCloseOnExec;
    private readonly record struct LinuxIdentity(ulong Inode, uint DeviceMajor, uint DeviceMinor, ulong MountId, ushort Mode, uint Links, ulong Size, long ModifiedSeconds, uint ModifiedNanos, long ChangedSeconds, uint ChangedNanos)
    {
        public bool IsDirectory => (Mode & 0xf000) == 0x4000;
        public bool IsRegular => (Mode & 0xf000) == 0x8000;
        public bool SameReadVersion(LinuxIdentity actual) => SameFile(actual) && Size == actual.Size && ModifiedSeconds == actual.ModifiedSeconds && ModifiedNanos == actual.ModifiedNanos && ChangedSeconds == actual.ChangedSeconds && ChangedNanos == actual.ChangedNanos;
        public bool SameFile(LinuxIdentity actual) => Inode == actual.Inode && DeviceMajor == actual.DeviceMajor && DeviceMinor == actual.DeviceMinor && MountId == actual.MountId;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxOpenHow { public ulong Flags, Mode, Resolve; }
    private static SafeFileHandle OpenLinuxAt(SafeFileHandle directory, string relative, ulong flags, ulong mode = 0, bool noMountCrossing = true)
    {
        var how = new LinuxOpenHow { Flags = flags, Mode = mode, Resolve = 0xeUL | (noMountCrossing ? 1UL : 0UL) };
        var actual = LinuxOpenAt2(437, LinuxDescriptor(directory), relative, ref how, (ulong)Marshal.SizeOf<LinuxOpenHow>());
        if (actual >= 0) return new SafeFileHandle((IntPtr)actual, ownsHandle: true);
        var error = Marshal.GetLastPInvokeError();
        if (error is 38 or 22 or 95) throw new PlatformNotSupportedException("The filesystem/kernel does not support the required original Linux operation.");
        if (error == 2) throw new FileNotFoundException("The original Linux component is absent.", relative);
        if (error is 18 or 40) throw new UnauthorizedAccessException("The original Linux path crosses a mount, symlink or kernel root boundary.");
        throw new Win32Exception(error, "The original Linux descriptor could not be opened.");
    }
    private static SafeFileHandle WrapLinuxDescriptor(int descriptor, string operation)
    { if (descriptor < 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), operation); return new((IntPtr)descriptor, ownsHandle: true); }
    private static int LinuxDescriptor(SafeFileHandle original)
    { if (original.IsInvalid || original.IsClosed) throw new ObjectDisposedException("original Linux descriptor"); return original.DangerousGetHandle().ToInt32(); }
    private static LinuxIdentity ReadLinuxIdentity(SafeFileHandle original)
    {
        var bytes = new byte[256];
        if (LinuxStatx(LinuxDescriptor(original), "", 0x1000, 0x7ffU | 0x1000U, bytes) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The original Linux descriptor identity is unavailable.");
        return ParseLinuxIdentity(bytes);
    }
    private static LinuxIdentity ParseLinuxIdentity(byte[] bytes)
    {
        if (bytes.Length != 256) throw new InvalidDataException("The actual statx observation has an unsupported native layout.");
        var mask = BitConverter.ToUInt32(bytes, 0);
        const uint consumed = 0x100U | 0x1U | 0x4U | 0x200U | 0x40U | 0x80U;
        if ((mask & consumed) != consumed)
            throw new PlatformNotSupportedException("The kernel did not provide the consumed original identity/kind/link/size/mtime/ctime fields.");
        return new(BitConverter.ToUInt64(bytes, 32), BitConverter.ToUInt32(bytes, 136), BitConverter.ToUInt32(bytes, 140),
            (mask & 0x1000U) != 0 ? BitConverter.ToUInt64(bytes, 144) : 0, BitConverter.ToUInt16(bytes, 28), BitConverter.ToUInt32(bytes, 16), BitConverter.ToUInt64(bytes, 40), BitConverter.ToInt64(bytes, 112), BitConverter.ToUInt32(bytes, 120), BitConverter.ToInt64(bytes, 96), BitConverter.ToUInt32(bytes, 104));
    }
    private static void DemandLinuxDescriptorPath(SafeFileHandle original, string expected)
    {
        var buffer = new byte[32769]; var length = LinuxReadLink($"/proc/self/fd/{LinuxDescriptor(original)}", buffer, (ulong)buffer.Length);
        if (length < 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "The actual Linux descriptor path is unavailable.");
        if (length >= buffer.Length) throw new InvalidDataException("The original Linux path exceeds its bounded observation.");
        var actual = Encoding.UTF8.GetString(buffer, 0, checked((int)length));
        if (!StringComparer.Ordinal.Equals(actual, Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected))))
            throw new UnauthorizedAccessException("The actual original Linux descriptor resolves to another or removed path.");
    }
    private static void RequireLinuxExports()
    {
        if (!NativeLibrary.TryLoad("libc.so.6", out var actual)) throw new PlatformNotSupportedException("A supported Linux C library is unavailable.");
        try { foreach (var name in new[] { "syscall", "statx", "readlink", "linkat", "renameat2", "fsync", "fchmod" }) if (!NativeLibrary.TryGetExport(actual, name, out _)) throw new PlatformNotSupportedException("The Linux original native entrypoint is unavailable: " + name); }
        finally { NativeLibrary.Free(actual); }
    }
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int LinuxOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, ulong flags, uint mode);
    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)] private static extern long LinuxOpenAt2(long number, int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, ref LinuxOpenHow how, ulong size);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)] private static extern int LinuxStatx(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, [Out] byte[] result);
    [DllImport("libc", EntryPoint = "readlink", SetLastError = true)] private static extern long LinuxReadLink([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [Out] byte[] result, ulong size);
    [DllImport("libc", EntryPoint = "linkat", SetLastError = true)] private static extern int LinuxLinkAt(int original, [MarshalAs(UnmanagedType.LPUTF8Str)] string oldPath, int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath, int flags);
    [DllImport("libc", EntryPoint = "renameat2", SetLastError = true)] private static extern int LinuxRenameAt2(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string oldPath, int targetParent, [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath, uint flags);
    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)] private static extern int LinuxFsync(int descriptor);
    [DllImport("libc", EntryPoint = "fchmod", SetLastError = true)] private static extern int LinuxFchmod(int descriptor, uint mode);
}
