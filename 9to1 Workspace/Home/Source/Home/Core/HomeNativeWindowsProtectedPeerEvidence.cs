using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace HavenOS.Home.Core;

// Physical observations only. The owning verifier must additionally authenticate
// its original signed receipt, descriptor, current actor and controlled launch.
[SupportedOSPlatform("windows")]
internal sealed class HomeNativeWindowsProtectedPeerEvidence : IDisposable
{
    internal readonly record struct FileIdentity(uint Volume, ulong File, ulong Created,
        ulong Changed, ulong Length, uint Attributes);
    private readonly SafeProcessHandle _process;
    private readonly List<(string Path, SafeFileHandle Handle, FileIdentity Identity)> _files = [];
    private readonly HashSet<string> _protectedOwners;
    private bool _disposed;

    internal int ProcessId { get; }
    internal string Principal { get; }
    internal string StartIdentity { get; }
    internal string ImagePath { get; }

    private HomeNativeWindowsProtectedPeerEvidence(SafeProcessHandle process, int pid,
        string principal, string start, string image, HashSet<string> protectedOwners)
    {
        _process = process; ProcessId = pid; Principal = principal;
        StartIdentity = start; ImagePath = image; _protectedOwners = protectedOwners;
    }

    internal static HomeNativeWindowsProtectedPeerEvidence Open(int pid, string observedPrincipal,
        IReadOnlySet<string> protectedOwners)
    {
        if (pid <= 0 || protectedOwners.Count is < 1 or > 16)
            throw new UnauthorizedAccessException("A bounded protected-owner policy is required.");
        var owners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sid in protectedOwners)
        {
            var parsed = new SecurityIdentifier(sid);
            if (parsed.Value != sid || !owners.Add(sid))
                throw new UnauthorizedAccessException("The original protected-owner policy is invalid.");
        }
        var process = OpenProcess(0x00100000 | 0x00001000, false, checked((uint)pid));
        if (process.IsInvalid) { process.Dispose(); throw NativeFailure("Open original peer"); }
        try
        {
            if (GetProcessId(process) != pid || WaitForSingleObject(process, 0) != 258)
                throw new UnauthorizedAccessException("The observed process has exited.");
            var principal = PrincipalOf(process);
            if (principal != observedPrincipal)
                throw new UnauthorizedAccessException("The original kernel peer principal differs.");
            var start = StartOf(process);
            var image = ImageOf(process);
            if (!Path.IsPathFullyQualified(image))
                throw new UnauthorizedAccessException("The process image is not absolute.");
            return new(process, pid, principal, start, Path.GetFullPath(image), owners);
        }
        catch { process.Dispose(); throw; }
    }

    internal SafeFileHandle OpenProtected(string path, bool directory = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        path = Path.GetFullPath(path);
        // The configured verifier supplies canonical absolute paths. Reparse
        // admission is refused, rather than accepting a resolved alias.
        var access = 0x00020000u | 0x00000080u | (directory ? 0u : 0x80000000u);
        var share = directory ? 7u : 1u;
        var handle = CreateFile(path, access, share, IntPtr.Zero, 3,
            0x00200000u | (directory ? 0x02000000u : 0u), IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw NativeFailure("Open original protected path"); }
        try
        {
            var identity = Identity(handle);
            if ((identity.Attributes & 0x400) != 0 ||
                ((identity.Attributes & 0x10) != 0) != directory)
                throw new UnauthorizedAccessException("Protected path has a reparse or foreign type.");
            RequireProtectedAcl(handle);
            _files.Add((path, handle, identity));
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    internal byte[] ReadBounded(SafeFileHandle handle, int maximumBytes)
    {
        if (maximumBytes < 1 || Identity(handle).Length > (ulong)maximumBytes)
            throw new InvalidDataException("The protected evidence exceeds its bound.");
        var length = checked((int)Identity(handle).Length);
        var bytes = new byte[length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var count = RandomAccess.Read(handle, bytes.AsSpan(offset), offset);
            if (count == 0) throw new EndOfStreamException("Original protected evidence ended early.");
            offset += count;
        }
        Span<byte> extra = stackalloc byte[1];
        if (RandomAccess.Read(handle, extra, offset) != 0)
            throw new InvalidDataException("Original protected evidence grew.");
        return bytes;
    }

    internal string HashOriginal(SafeFileHandle handle, long expectedBytes)
    {
        if (expectedBytes < 1 || Identity(handle).Length != (ulong)expectedBytes)
            throw new InvalidDataException("The original protected payload length differs.");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        long offset = 0;
        while (offset < expectedBytes)
        {
            var read = RandomAccess.Read(handle,
                buffer.AsSpan(0, (int)Math.Min(buffer.Length, expectedBytes - offset)), offset);
            if (read == 0) throw new EndOfStreamException("Original protected payload ended early.");
            digest.AppendData(buffer, 0, read); offset += read;
        }
        if (RandomAccess.Read(handle, buffer.AsSpan(0, 1), offset) != 0)
            throw new InvalidDataException("Original protected payload grew.");
        return Convert.ToHexString(digest.GetHashAndReset());
    }

    internal string VerifyOriginalAuthenticode(SafeFileHandle image,
        IReadOnlySet<string> publisherCertificateSha256)
    {
        var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(),
            FilePath = ImagePath, File = image.DangerousGetHandle() };
        var nativeFile = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(),
            UiChoice = 2, RevocationChecks = 1, UnionChoice = 1,
            FileInfo = nativeFile, StateAction = 1, ProviderFlags = 0x80 };
        var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
        Exception? primary = null;
        string? publisher = null;
        List<Exception> cleanup = [];
        try
        {
            Marshal.StructureToPtr(file, nativeFile, false);
            var result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            if (result != 0)
                throw new UnauthorizedAccessException($"Authenticode refused the original image: 0x{result:X8}.");
            var provider = WTHelperProvDataFromStateData(data.StateData);
            var signer = WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            var certificate = WTHelperGetProvCertFromChain(signer, 0);
            if (provider == IntPtr.Zero || signer == IntPtr.Zero || certificate == IntPtr.Zero)
                throw new UnauthorizedAccessException("The original publisher chain is missing.");
            var providerCertificate = Marshal.PtrToStructure<ProviderCertificate>(certificate);
            var context = Marshal.PtrToStructure<CertificateContext>(providerCertificate.Context);
            if (context.Bytes is < 1 or > 16384 || context.Encoded == IntPtr.Zero)
                throw new UnauthorizedAccessException("The original publisher certificate is unsupported.");
            var encoded = new byte[context.Bytes];
            Marshal.Copy(context.Encoded, encoded, 0, encoded.Length);
            using var parsed = X509CertificateLoader.LoadCertificate(encoded);
            publisher = Convert.ToHexString(SHA256.HashData(parsed.RawData));
            if (!publisherCertificateSha256.Contains(publisher))
                throw new UnauthorizedAccessException("The trusted publisher pin does not match the original image.");
        }
        catch (Exception error) { primary = error; }
        finally
        {
            try
            {
                if (data.StateData != IntPtr.Zero)
                {
                    data.StateAction = 2;
                    var result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                    if (result != 0) throw new IOException("Original Authenticode state close refused.");
                }
            }
            catch (Exception error) { cleanup.Add(error); }
            try { Marshal.DestroyStructure<TrustFile>(nativeFile); }
            catch (Exception error) { cleanup.Add(error); }
            try { Marshal.FreeHGlobal(nativeFile); }
            catch (Exception error) { cleanup.Add(error); }
            GC.KeepAlive(image);
        }
        if (primary is not null) cleanup.Insert(0, primary);
        ThrowOriginals(cleanup);
        return publisher!;
    }

    internal void RequireSame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (GetProcessId(_process) != ProcessId || WaitForSingleObject(_process, 0) != 258 ||
            StartOf(_process) != StartIdentity || PrincipalOf(_process) != Principal ||
            !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(ImageOf(_process)), ImagePath))
            throw new UnauthorizedAccessException("The original running peer changed or exited.");
        foreach (var entry in _files)
        {
            if (Identity(entry.Handle) != entry.Identity)
                throw new UnauthorizedAccessException("Original protected descriptor metadata changed.");
            RequireProtectedAcl(entry.Handle);
            using var reopened = CreateFile(entry.Path, 0x00020000 | 0x80, 7,
                IntPtr.Zero, 3, 0x00200000 | ((entry.Identity.Attributes & 0x10) != 0 ? 0x02000000u : 0), IntPtr.Zero);
            if (reopened.IsInvalid || Identity(reopened) != entry.Identity)
                throw new UnauthorizedAccessException("Original protected path no longer names the same file.");
            RequireProtectedAcl(reopened);
        }
    }

    private void RequireProtectedAcl(SafeFileHandle handle)
    {
        var result = GetSecurityInfo(handle, 1, 0x1 | 0x4,
            out _, out _, out _, out _, out var descriptor);
        if (result != 0) throw new Win32Exception((int)result, "Original protected ACL could not be read.");
        try
        {
            var length = GetSecurityDescriptorLength(descriptor);
            if (length is < 1 or > 65536)
                throw new UnauthorizedAccessException("Original protected ACL exceeds its bound.");
            var bytes = new byte[length];
            Marshal.Copy(descriptor, bytes, 0, bytes.Length);
            var acl = new RawSecurityDescriptor(bytes, 0);
            if (acl.Owner is null || !_protectedOwners.Contains(acl.Owner.Value) ||
                acl.DiscretionaryAcl is null || acl.DiscretionaryAcl.Count > 256)
                throw new UnauthorizedAccessException("Original protected owner or DACL is unavailable.");
            const int writes = 0x40000000 | 0x10000000 | 0x000D0156;
            foreach (GenericAce entry in acl.DiscretionaryAcl)
            {
                if (entry is not CommonAce ace || ace.IsCallback)
                    throw new UnauthorizedAccessException("Original protected ACL has unsupported conditional rights.");
                if (ace.AceQualifier == AceQualifier.AccessDenied) continue;
                if (ace.AceQualifier != AceQualifier.AccessAllowed ||
                    (ace.AccessMask & writes) != 0 && !_protectedOwners.Contains(ace.SecurityIdentifier.Value))
                    throw new UnauthorizedAccessException("Original protected path admits a foreign writer.");
            }
        }
        finally
        {
            if (LocalFree(descriptor) != IntPtr.Zero)
                throw new IOException("Original protected security descriptor could not be released.");
        }
    }

    private static FileIdentity Identity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info))
            throw NativeFailure("Read original file identity");
        return new(info.Volume, ((ulong)info.IndexHigh << 32) | info.IndexLow,
            ((ulong)info.Creation.High << 32) | info.Creation.Low,
            ((ulong)info.Write.High << 32) | info.Write.Low,
            ((ulong)info.SizeHigh << 32) | info.SizeLow, info.Attributes);
    }

    private static string PrincipalOf(SafeProcessHandle process)
    {
        if (!OpenProcessToken(process, 8, out var token)) throw NativeFailure("Read original peer token");
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            return identity.User?.Value is { } sid ? "windows-sid:" + sid
                : throw new UnauthorizedAccessException("Original peer SID is unavailable.");
    }
    private static string StartOf(SafeProcessHandle process)
    {
        if (!GetProcessTimes(process, out var created, out _, out _, out _))
            throw NativeFailure("Read original process birth");
        return (((ulong)created.High << 32) | created.Low).ToString("X16",
            System.Globalization.CultureInfo.InvariantCulture);
    }
    private static string ImageOf(SafeProcessHandle process)
    {
        var text = new System.Text.StringBuilder(32768); uint count = 32768;
        if (!QueryFullProcessImageName(process, 0, text, ref count) || count == 0 || count >= 32768)
            throw NativeFailure("Read original process image");
        return text.ToString();
    }
    private static Win32Exception NativeFailure(string stage) => new(Marshal.GetLastWin32Error(), stage);
    internal static void ThrowOriginals(List<Exception> failures)
    {
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Original protected Windows evidence and cleanup failed.", failures);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception> errors = [];
        for (var index = _files.Count - 1; index >= 0; index--)
            try { _files[index].Handle.Dispose(); } catch (Exception error) { errors.Add(error); }
        try { _process.Dispose(); } catch (Exception error) { errors.Add(error); }
        ThrowOriginals(errors);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Time { public uint Low, High; }
    [StructLayout(LayoutKind.Sequential)] private struct FileInfo
    {
        public uint Attributes; public Time Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TrustFile
    {
        public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr File, KnownSubject;
    }
    [StructLayout(LayoutKind.Sequential)] private struct TrustData
    {
        public uint Size; public IntPtr PolicyCallback, SipClient;
        public uint UiChoice, RevocationChecks, UnionChoice; public IntPtr FileInfo;
        public uint StateAction; public IntPtr StateData, UrlReference;
        public uint ProviderFlags, UiContext; public IntPtr SignatureSettings;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProviderCertificate
    { public uint Size; public IntPtr Context; }
    [StructLayout(LayoutKind.Sequential)] private struct CertificateContext
    { public uint Encoding; public IntPtr Encoded; public int Bytes; public IntPtr Info, Store; }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern int GetProcessId(SafeProcessHandle process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out Time creation, out Time exit, out Time kernel, out Time user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, System.Text.StringBuilder name, ref uint length);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInfo info);
    [DllImport("advapi32.dll")] private static extern uint GetSecurityInfo(SafeFileHandle handle, uint type, uint requested,
        out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor);
    [DllImport("advapi32.dll")] private static extern uint GetSecurityDescriptorLength(IntPtr descriptor);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr handle);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern uint WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint signer, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certificate);
}
