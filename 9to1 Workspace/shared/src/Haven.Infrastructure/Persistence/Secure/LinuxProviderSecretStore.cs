using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

/// <summary>Explicit Linux local credential custody behind the maintained secret interface.
/// This grants no OAuth/account/Home permission. The existing private application directory
/// must already be owned by the actual euid; existing insecure directories/files are refused.
/// Files use descriptor-relative no-follow access and AES-GCM with a private per-store key.
/// There are no asynchronous internal factories or external callbacks in finite operations.</summary>
public sealed class LinuxProviderSecretStore : IProviderSecretStore, IDisposable
{
    private const ulong CloseOnExec = 0x80000, DirectoryFlag = 0x10000, NonBlocking = 0x800;
    private const ulong AnonymousWrite = 0x410000 | 2 | CloseOnExec;
    private const int MaximumSecretBytes = 65536;
    private readonly object _gate = new();
    private readonly IAppPaths _paths;
    private readonly uint _uid;
    private readonly string _root;
    private readonly SafeFileHandle _directory = null!, _keyHandle = null!;
    private readonly Identity _directoryIdentity, _keyIdentity;
    private readonly byte[] _key = [];
    private bool _closed;
    public LinuxProviderSecretStore(IAppPaths originalPaths)
    {
        ArgumentNullException.ThrowIfNull(originalPaths);
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("The local credential owner requires supported 64-bit Linux openat2/statx.");
        _paths = originalPaths; _uid = EffectiveUid();
        var data = Path.TrimEndingDirectorySeparator(Path.GetFullPath(originalPaths.DataDirectory));
        if (data == "/") throw new UnauthorizedAccessException("A bounded private application data directory is required.");
        _root = Path.Combine(data, "Credentials");
        SafeFileHandle? volume = null, parent = null, directory = null, key = null;
        var errors = new List<Exception>();
        try
        {
            volume = Wrap(Open("/", DirectoryFlag | CloseOnExec, 0));
            parent = OpenAt(volume, data.TrimStart('/'), DirectoryFlag | CloseOnExec, beneath: true);
            DemandPrivate(parent, directory: true); DemandPath(parent, data);
            if (MkdirAt(Fd(parent), "Credentials", 0x1c0) != 0 && Marshal.GetLastPInvokeError() != 17) NativeFailure();
            directory = OpenAt(parent, "Credentials", DirectoryFlag | CloseOnExec);
            DemandPrivate(directory, directory: true); DemandPath(directory, _root);
            _directoryIdentity = ReadIdentity(directory);
            key = OpenOrCreateKey(directory);
            DemandPrivate(key, directory: false); _keyIdentity = ReadIdentity(key);
            if (_keyIdentity.Size != 32) throw new InvalidDataException("The protected local credential key requires recovery; it is not reset.");
            _key = ReadBounded(key, 32);
            if (_key.Length != 32 || ReadIdentity(key) != _keyIdentity) throw new IOException("The local credential key changed during acquisition.");
            _directory = directory; directory = null; _keyHandle = key; key = null;
        }
        catch (Exception cause) { errors.Add(cause); }
        finally
        {
            foreach (var actual in new[] { key, directory, parent, volume })
                try { actual?.Dispose(); } catch (Exception cause) { errors.Add(cause); }
        }
        if (errors.Count != 0)
        {
            foreach (var actual in new[] { _keyHandle, _directory })
                try { actual?.Dispose(); } catch (Exception cause) { errors.Add(cause); }
            CryptographicOperations.ZeroMemory(_key);
            throw new AggregateException("Protected Linux credential acquisition or cleanup failed.", errors);
        }
    }
    public bool HasOriginalComposition(IAppPaths samePaths) => ReferenceEquals(_paths, samePaths);
    public Task<string?> GetAsync(string providerId, string secretName, CancellationToken cancellationToken) =>
        Run<string?>(cancellationToken, () =>
        {
            var context = Context(providerId, secretName); var name = Name(context);
            SafeFileHandle? actual = null; byte[] bytes = [], plain = [];
            var errors = new List<Exception>(); string? result = null;
            try
            {
                try { actual = OpenAt(_directory, name, NonBlocking | CloseOnExec); }
                catch (FileNotFoundException) { DemandCurrent(); return null; }
                DemandPrivate(actual, directory: false); var before = ReadIdentity(actual);
                bytes = ReadBounded(actual, MaximumSecretBytes + 29);
                if (before != ReadIdentity(actual) || bytes.Length < 29 || bytes[0] != 1)
                    throw new InvalidDataException("The protected credential record is unavailable or changed.");
                plain = new byte[bytes.Length - 29];
                using var cipher = new AesGcm(_key, 16);
                cipher.Decrypt(bytes.AsSpan(1, 12), bytes.AsSpan(13, plain.Length), bytes.AsSpan(bytes.Length - 16), plain, context);
                result = new UTF8Encoding(false, true).GetString(plain); DemandCurrent();
            }
            catch (Exception cause) { errors.Add(cause); }
            finally
            {
                try { actual?.Dispose(); } catch (Exception cause) { errors.Add(cause); }
                CryptographicOperations.ZeroMemory(bytes); CryptographicOperations.ZeroMemory(plain);
            }
            if (errors.Count != 0) throw new AggregateException("Original local credential read and cleanup failed.", errors);
            return result;
        });
    public Task SetAsync(string providerId, string secretName, string secret, CancellationToken cancellationToken) =>
        Run(cancellationToken, () =>
        {
            ArgumentNullException.ThrowIfNull(secret);
            if (secret.Length > MaximumSecretBytes) throw new ArgumentException("The local credential exceeds its bounded storage size.");
            var context = Context(providerId, secretName);
            var plain = new UTF8Encoding(false, true).GetBytes(secret); byte[] record = [];
            SafeFileHandle? actual = null; string? temporary = null; var errors = new List<Exception>();
            try
            {
                if (plain.Length > MaximumSecretBytes) throw new ArgumentException("The local credential exceeds its bounded storage size.");
                record = new byte[plain.Length + 29]; record[0] = 1; RandomNumberGenerator.Fill(record.AsSpan(1, 12));
                using (var cipher = new AesGcm(_key, 16))
                    cipher.Encrypt(record.AsSpan(1, 12), plain, record.AsSpan(13, plain.Length), record.AsSpan(record.Length - 16), context);
                var name = Name(context);
                // An existing record is inspected without following a symlink, not adopted.
                try { using var old = OpenAt(_directory, name, NonBlocking | CloseOnExec); DemandPrivate(old, directory: false); }
                catch (FileNotFoundException) { }
                actual = OpenAt(_directory, ".", AnonymousWrite, 0x180); DemandPrivate(actual, directory: false, anonymous: true);
                RandomAccess.Write(actual, record, 0); Flush(actual);
                temporary = ".pending-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                if (LinkAt(Fd(actual), "", Fd(_directory), temporary, 0x1000) != 0) NativeFailure();
                DemandCurrent();
                if (RenameAt(Fd(_directory), temporary, Fd(_directory), name, 0) != 0) NativeFailure();
                temporary = null; Flush(_directory); DemandCurrent();
            }
            catch (Exception cause) { errors.Add(cause); }
            finally
            {
                try { actual?.Dispose(); } catch (Exception cause) { errors.Add(cause); }
                if (temporary is not null)
                    try { if (UnlinkAt(Fd(_directory), temporary, 0) != 0 && Marshal.GetLastPInvokeError() != 2) NativeFailure(); }
                    catch (Exception cause) { errors.Add(cause); }
                CryptographicOperations.ZeroMemory(plain); CryptographicOperations.ZeroMemory(record);
            }
            if (errors.Count != 0) throw new AggregateException("Original local credential write and cleanup failed.", errors);
            return true;
        });
    public Task DeleteAsync(string providerId, string secretName, CancellationToken cancellationToken) =>
        Run(cancellationToken, () =>
        {
            var name = Name(Context(providerId, secretName));
            try { using var actual = OpenAt(_directory, name, NonBlocking | CloseOnExec); DemandPrivate(actual, directory: false); }
            catch (FileNotFoundException) { DemandCurrent(); return true; }
            DemandCurrent(); if (UnlinkAt(Fd(_directory), name, 0) != 0) NativeFailure(); Flush(_directory); DemandCurrent(); return true;
        });
    private Task<T> Run<T>(CancellationToken token, Func<T> finite)
    {
        lock (_gate)
        {
            if (token.IsCancellationRequested) return Task.FromCanceled<T>(token);
            var errors = new List<Exception>(); var locked = false; T result = default!;
            try
            {
                DemandCurrent(); if (Flock(Fd(_keyHandle), 6) != 0) NativeFailure(); locked = true;
                DemandCurrent(); result = finite();
            }
            catch (Exception cause) { errors.Add(cause); }
            finally
            {
                if (locked) try { if (Flock(Fd(_keyHandle), 8) != 0) NativeFailure(); } catch (Exception cause) { errors.Add(cause); }
            }
            return errors.Count == 0 ? Task.FromResult(result) : Task.FromException<T>(new AggregateException("Protected local credential operation failed.", errors));
        }
    }
    private void DemandCurrent()
    {
        if (_closed) throw new ObjectDisposedException(nameof(LinuxProviderSecretStore));
        if (EffectiveUid() != _uid) throw new UnauthorizedAccessException("The local credential operating-system principal changed.");
        DemandPrivate(_directory, directory: true); DemandPath(_directory, _root);
        if (!ReadIdentity(_directory).SameFile(_directoryIdentity)) throw new IOException("The protected local credential root changed.");
        DemandPrivate(_keyHandle, directory: false); DemandPath(_keyHandle, Path.Combine(_root, ".key"));
        if (ReadIdentity(_keyHandle) != _keyIdentity) throw new IOException("The protected local credential key changed.");
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed) return; _closed = true; var errors = new List<Exception>();
            foreach (var actual in new[] { _keyHandle, _directory })
                try { actual.Dispose(); } catch (Exception cause) { errors.Add(cause); }
            CryptographicOperations.ZeroMemory(_key);
            if (errors.Count != 0) throw new AggregateException("Original Linux credential owner cleanup failed.", errors);
        }
    }
    private SafeFileHandle OpenOrCreateKey(SafeFileHandle directory)
    {
        try { return OpenAt(directory, ".key", CloseOnExec | NonBlocking); }
        catch (FileNotFoundException) { }
        if (Directory.EnumerateFileSystemEntries("/proc/self/fd/" + Fd(directory)).Take(1).Any())
            throw new InvalidDataException("A missing private credential key with existing state requires explicit recovery; no new key is minted.");
        using var candidate = OpenAt(directory, ".", AnonymousWrite, 0x180);
        DemandPrivate(candidate, directory: false, anonymous: true); var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            RandomAccess.Write(candidate, key, 0); Flush(candidate);
            if (LinkAt(Fd(candidate), "", Fd(directory), ".key", 0x1000) != 0 && Marshal.GetLastPInvokeError() != 17) NativeFailure();
            Flush(directory); return OpenAt(directory, ".key", CloseOnExec | NonBlocking);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private static byte[] Context(string provider, string name)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(name) || provider.Length > 512 || name.Length > 512 || provider.Contains('\0') || name.Contains('\0'))
            throw new ArgumentException("Bounded provider and credential names are required.");
        return new UTF8Encoding(false, true).GetBytes(provider + "\0" + name);
    }
    private static string Name(byte[] context) => Convert.ToHexString(SHA256.HashData(context)).ToLowerInvariant() + ".credential";
    private static byte[] ReadBounded(SafeFileHandle file, int limit)
    {
        var size = ReadIdentity(file).Size;
        if (size > (ulong)limit) throw new InvalidDataException("The private credential file exceeds its bounded size.");
        var bytes = new byte[(int)size]; var at = 0;
        while (at < bytes.Length) { var count = RandomAccess.Read(file, bytes.AsSpan(at), at); if (count == 0) throw new EndOfStreamException("The private credential read was incomplete."); at += count; }
        return bytes;
    }
    private void DemandPrivate(SafeFileHandle actual, bool directory, bool anonymous = false)
    {
        var value = ReadIdentity(actual);
        if (value.Uid != _uid || (value.Mode & 0x3f) != 0 || (value.Mode & 0xe00) != 0 ||
            (value.Mode & 0xf000) != (directory ? 0x4000 : 0x8000) || (!directory && value.Links != (anonymous ? 0U : 1U)))
            throw new UnauthorizedAccessException("Actual euid-owned private directory/regular-file custody is required; insecure existing state is not adopted.");
    }
    private readonly record struct Identity(ulong Inode, uint DeviceMajor, uint DeviceMinor, ulong Mount, uint Uid, ushort Mode, uint Links, ulong Size, long Modified, uint ModifiedNanos, long Changed, uint ChangedNanos)
    {
        internal bool SameFile(Identity other) => Inode == other.Inode && DeviceMajor == other.DeviceMajor && DeviceMinor == other.DeviceMinor && Mount == other.Mount;
    }
    private static Identity ReadIdentity(SafeFileHandle actual)
    {
        var bytes = new byte[256]; if (Statx(Fd(actual), "", 0x1000, 0x7ff | 0x1000, bytes) != 0) NativeFailure();
        if ((BitConverter.ToUInt32(bytes, 0) & 0x3cf) != 0x3cf) throw new PlatformNotSupportedException("The kernel omitted required private file observations.");
        return new(BitConverter.ToUInt64(bytes, 32), BitConverter.ToUInt32(bytes, 136), BitConverter.ToUInt32(bytes, 140), BitConverter.ToUInt64(bytes, 144),
            BitConverter.ToUInt32(bytes, 20), BitConverter.ToUInt16(bytes, 28), BitConverter.ToUInt32(bytes, 16), BitConverter.ToUInt64(bytes, 40),
            BitConverter.ToInt64(bytes, 112), BitConverter.ToUInt32(bytes, 120), BitConverter.ToInt64(bytes, 96), BitConverter.ToUInt32(bytes, 104));
    }
    [StructLayout(LayoutKind.Sequential)] private struct OpenHow { public ulong Flags, Mode, Resolve; }
    private static SafeFileHandle OpenAt(SafeFileHandle parent, string relative, ulong flags, ulong mode = 0, bool beneath = true)
    {
        var how = new OpenHow { Flags = flags, Mode = mode, Resolve = 0x6UL | (beneath ? 8UL : 0) };
        var fd = OpenAt2(437, Fd(parent), relative, ref how, 24);
        if (fd >= 0) return new((IntPtr)fd, true);
        var error = Marshal.GetLastPInvokeError(); if (error == 2) throw new FileNotFoundException("The private credential component is absent.");
        if (error is 38 or 22 or 95) throw new PlatformNotSupportedException("The kernel/filesystem lacks the required private credential operation.");
        if (error is 18 or 40) throw new UnauthorizedAccessException("A private credential component is a symlink or escapes the original root.");
        throw new Win32Exception(error, "The private credential descriptor could not be opened.");
    }
    private static SafeFileHandle Wrap(int fd) { if (fd < 0) NativeFailure(); return new((IntPtr)fd, true); }
    private static int Fd(SafeFileHandle file) { if (file.IsClosed || file.IsInvalid) throw new ObjectDisposedException("private credential descriptor"); return file.DangerousGetHandle().ToInt32(); }
    private static void Flush(SafeFileHandle file) { if (Fsync(Fd(file)) != 0) NativeFailure(); }
    private static void NativeFailure() => throw new Win32Exception(Marshal.GetLastPInvokeError(), "The original private credential filesystem operation failed.");
    private static void DemandPath(SafeFileHandle file, string expected)
    {
        var buffer = new byte[32769]; var length = ReadLink("/proc/self/fd/" + Fd(file), buffer, (ulong)buffer.Length);
        if (length < 0) NativeFailure();
        if (length >= buffer.Length || Encoding.UTF8.GetString(buffer, 0, checked((int)length)) != expected)
            throw new UnauthorizedAccessException("The original private credential path was replaced or removed.");
    }
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint EffectiveUid();
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, ulong flags, uint mode);
    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)] private static extern long OpenAt2(long number, int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, ref OpenHow how, ulong size);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)] private static extern int Statx(int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, [Out] byte[] bytes);
    [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)] private static extern int MkdirAt(int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
    [DllImport("libc", EntryPoint = "readlink", SetLastError = true)] private static extern long ReadLink([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [Out] byte[] bytes, ulong size);
    [DllImport("libc", EntryPoint = "linkat", SetLastError = true)] private static extern int LinkAt(int from, [MarshalAs(UnmanagedType.LPUTF8Str)] string oldPath, int to, [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath, int flags);
    [DllImport("libc", EntryPoint = "renameat2", SetLastError = true)] private static extern int RenameAt(int from, [MarshalAs(UnmanagedType.LPUTF8Str)] string oldPath, int to, [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath, uint flags);
    [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)] private static extern int UnlinkAt(int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)] private static extern int Fsync(int fd);
    [DllImport("libc", EntryPoint = "flock", SetLastError = true)] private static extern int Flock(int fd, int operation);
}
