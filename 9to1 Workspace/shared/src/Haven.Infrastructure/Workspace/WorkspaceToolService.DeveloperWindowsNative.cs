using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService
{
    // This private port supplies physical observations, never Home/Files/Task authority.
    // Windows identities are not Unix identities and never enter the Linux fingerprint.
    private readonly record struct DeveloperWindowsIdentity(ulong Volume, ulong IdLow, ulong IdHigh,
        uint Attributes, uint Links, ulong Size, long LastWrite, long Change)
    {
        internal bool IsDirectory => (Attributes & 0x10) != 0;
        internal bool IsRegular => !IsDirectory && (Attributes & 0x400) == 0;
        internal bool SameFile(DeveloperWindowsIdentity other) => Volume == other.Volume && IdLow == other.IdLow && IdHigh == other.IdHigh;
        internal bool SameReadVersion(DeveloperWindowsIdentity other) => SameFile(other) && Size == other.Size &&
            LastWrite == other.LastWrite && Change == other.Change && Attributes == other.Attributes && Links == other.Links;
    }
    private const uint DeveloperWindowsReadControl = 0x20000, DeveloperWindowsSynchronize = 0x100000;
    private const uint DeveloperWindowsAttributes = 0x80, DeveloperWindowsRead = 1;
    private const uint DeveloperWindowsDirectoryAccess = DeveloperWindowsReadControl | DeveloperWindowsSynchronize | DeveloperWindowsAttributes | 1;
    private const uint DeveloperWindowsMutableDirectoryAccess = DeveloperWindowsDirectoryAccess | 2 | 4;

    private static void RequireDeveloperWindowsExports()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new PlatformNotSupportedException("Developer physical custody requires the supported Windows64 native ABI.");
        foreach (var entry in new[]
        {
            (Library: "ntdll.dll", Names: new[] { "NtCreateFile", "NtQueryDirectoryFile", "NtQueryInformationFile", "NtSetInformationFile", "NtQueryVolumeInformationFile", "NtFlushBuffersFileEx", "RtlNtStatusToDosError" }),
            (Library: "kernel32.dll", Names: new[] { "GetFileInformationByHandleEx", "GetFinalPathNameByHandleW", "GetFileType" }),
            (Library: "advapi32.dll", Names: new[] { "OpenThreadToken", "OpenProcessToken", "GetTokenInformation", "GetSecurityInfo", "IsValidSid", "GetLengthSid", "ConvertStringSecurityDescriptorToSecurityDescriptorW" })
        })
        {
            if (!NativeLibrary.TryLoad(entry.Library, out var library)) throw new PlatformNotSupportedException("The actual Windows native library is unavailable.");
            try { foreach (var name in entry.Names) if (!NativeLibrary.TryGetExport(library, name, out _)) throw new PlatformNotSupportedException("The actual Windows native primitive is unavailable: " + name); }
            finally { NativeLibrary.Free(library); }
        }
    }
    private static bool SafeDeveloperWindowsLeaf(string leaf)
    {
        if (string.IsNullOrWhiteSpace(leaf) || leaf.Length > 255 || leaf is "." or ".." || leaf.EndsWith('.') || leaf.EndsWith(' ') ||
            leaf.Any(c => c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' || char.IsControl(c))) return false;
        var device = leaf.Split('.')[0];
        return !new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(device, StringComparer.OrdinalIgnoreCase) &&
            !(device.Length == 4 && (device.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || device.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
              (device[3] is >= '0' and <= '9' or '\u00b9' or '\u00b2' or '\u00b3'));
    }
    private static string NormalizeDeveloperWindowsPath(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        // The proven native contract is a local DOS volume. UNC/device/object namespaces
        // require their own physical and durability contract, never an absolute fallback.
        if (full.Length < 3 || !char.IsAsciiLetter(full[0]) || full[1] != ':' || full[2] != '\\' || full.Length >= 32768)
            throw new PlatformNotSupportedException("The physical Windows root requires a bounded local DOS volume path.");
        if (full[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries).Any(value => !SafeDeveloperWindowsLeaf(value)))
            throw new UnauthorizedAccessException("The physical Windows path contains an unsupported component or alternate stream.");
        return full;
    }
    private sealed class DeveloperWindowsRootLease : IDisposable
    {
        private sealed record Held(string Path, SafeFileHandle Handle, DeveloperWindowsIdentity Identity);
        private readonly object _gate = new();
        private readonly Dictionary<string, Held> _directories = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _root;
        private readonly string _principal;
        private bool _closed;
        internal string Principal => _principal;
        internal DeveloperWindowsRootLease(string root)
        {
            RequireDeveloperWindowsExports(); _root = NormalizeDeveloperWindowsPath(root);
            if (_root.Length == 3) throw new UnauthorizedAccessException("A whole Windows volume is not a bounded developer workspace.");
            _principal = CurrentDeveloperWindowsSid();
            var errors = new List<Exception>();
            try
            {
                var volume = _root[..3];
                var first = CreateFileW(volume, DeveloperWindowsDirectoryAccess, 3, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
                if (first.IsInvalid) { var error = Marshal.GetLastPInvokeError(); first.Dispose(); throw new Win32Exception(error, "The original Windows volume could not be retained."); }
                try
                {
                    DemandDeveloperWindowsPath(first, volume, true); DemandDeveloperWindowsLocalVolume(first);
                    var identity = ReadDeveloperWindowsIdentity(first); DemandDeveloperWindowsCase(first);
                    _directories.Add(Path.TrimEndingDirectorySeparator(volume), new(volume, first, identity));
                }
                catch { first.Dispose(); throw; }
                EnsureDirectory(_root);
            }
            catch (Exception error) { AddOriginalErrors(errors, null, error); }
            if (errors.Count != 0) { try { Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); } ThrowOriginalErrors(errors); }
        }
        private void DemandOpen()
        {
            if (_closed) throw new ObjectDisposedException("original developer Windows root");
            DemandDeveloperWindowsSid(_principal);
        }
        internal void DemandCurrent()
        {
            lock (_gate)
            {
                DemandOpen();
                foreach (var item in _directories.Values)
                {
                    DemandDeveloperWindowsPath(item.Handle, item.Path, true); DemandDeveloperWindowsCase(item.Handle);
                    if (!ReadDeveloperWindowsIdentity(item.Handle).SameFile(item.Identity)) throw new UnauthorizedAccessException("A held Windows physical ancestor changed.");
                }
            }
        }
        private Held EnsureDirectory(string full)
        {
            var volume = full[..3]; var parent = _directories[Path.TrimEndingDirectorySeparator(volume)]; var path = volume;
            foreach (var leaf in full[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                path = Path.Combine(path, leaf); var key = Path.TrimEndingDirectorySeparator(path);
                if (_directories.TryGetValue(key, out var held)) { parent = held; continue; }
                var handle = OpenDeveloperWindowsAt(parent.Handle, leaf, DeveloperWindowsDirectoryAccess, 3, 1, directory: true);
                try
                {
                    DemandDeveloperWindowsPath(handle, path, true); DemandDeveloperWindowsCase(handle);
                    var identity = ReadDeveloperWindowsIdentity(handle);
                    if (identity.Volume != parent.Identity.Volume) throw new UnauthorizedAccessException("The physical Windows path crossed its retained volume.");
                    held = new(path, handle, identity); _directories.Add(key, held); parent = held;
                }
                catch { handle.Dispose(); throw; }
            }
            return parent;
        }
        internal SafeFileHandle OpenDirectory(string full, bool mutable = false)
        {
            full = NormalizeDeveloperWindowsPath(full);
            if (!IsWithinRoot(_root, full, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("The original Windows directory escapes its held root.");
            lock (_gate)
            {
                DemandCurrent();
                var held = EnsureDirectory(full);
                // Open the final component anew through its SAME held parent, never by path.
                var parentPath = Path.GetDirectoryName(full)!; var parent = _directories[Path.TrimEndingDirectorySeparator(parentPath)];
                var handle = OpenDeveloperWindowsAt(parent.Handle, Path.GetFileName(full), mutable ? DeveloperWindowsMutableDirectoryAccess : DeveloperWindowsDirectoryAccess, 3, 1, true);
                try { DemandDeveloperWindowsPath(handle, full, true); DemandDeveloperWindowsCase(handle); if (!ReadDeveloperWindowsIdentity(handle).SameFile(held.Identity)) throw new IOException("The original Windows directory identity changed."); return handle; }
                catch { handle.Dispose(); throw; }
            }
        }
        internal SafeFileHandle OpenRead(string full)
        {
            full = NormalizeDeveloperWindowsPath(full);
            if (!IsWithinRoot(_root, full, StringComparison.OrdinalIgnoreCase) || full == _root) throw new UnauthorizedAccessException("The original Windows file escapes its held root.");
            lock (_gate)
            {
                DemandCurrent(); var parent = EnsureDirectory(Path.GetDirectoryName(full)!);
                var handle = OpenDeveloperWindowsAt(parent.Handle, Path.GetFileName(full), DeveloperWindowsReadControl | DeveloperWindowsSynchronize | DeveloperWindowsAttributes | DeveloperWindowsRead, 1, 1, false);
                try
                {
                    DemandDeveloperWindowsPath(handle, full, false); var identity = ReadDeveloperWindowsIdentity(handle);
                    if (!identity.IsRegular || identity.Links != 1 || identity.Volume != parent.Identity.Volume) throw new UnauthorizedAccessException("The Windows source is not an unaliased same-volume regular file.");
                    return handle;
                }
                catch { handle.Dispose(); throw; }
            }
        }
        internal bool RetainCapturedDirectory(string path, SafeFileHandle original, DeveloperWindowsIdentity identity)
        {
            path = NormalizeDeveloperWindowsPath(path);
            lock (_gate)
            {
                DemandOpen();
                if (!IsWithinRoot(_root, path, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("The captured Windows directory escapes its original root.");
                DemandDeveloperWindowsPath(original, path, true); DemandDeveloperWindowsCase(original);
                if (!ReadDeveloperWindowsIdentity(original).SameFile(identity)) throw new IOException("The actual captured directory changed before custody transfer.");
                if (_directories.TryGetValue(path, out var retained))
                { if (!retained.Identity.SameFile(identity)) throw new IOException("The held original Windows directory identity differs from capture."); return false; }
                _directories.Add(path, new(path, original, identity)); return true;
            }
        }
        public void Dispose()
        {
            Held[] handles; lock (_gate) { if (_closed) return; _closed = true; handles = _directories.Values.Reverse().ToArray(); _directories.Clear(); }
            var errors = new List<Exception>(); foreach (var held in handles) try { held.Handle.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); } ThrowOriginalErrors(errors);
        }
    }
    private static DeveloperWindowsIdentity ReadDeveloperWindowsIdentity(SafeFileHandle handle)
    {
        if (handle.IsClosed || handle.IsInvalid) throw new ObjectDisposedException("original Windows descriptor");
        if (DeveloperWindowsGetFileType(handle) != 1) throw new UnauthorizedAccessException("The original Windows handle is not a disk filesystem object.");
        byte[] Read(int kind, int size) { var bytes = new byte[size]; if (!GetFileInformationByHandleEx(handle, kind, bytes, (uint)size)) throw new Win32Exception(Marshal.GetLastPInvokeError(), "The consumed original Windows identity field is unavailable."); return bytes; }
        var id = Read(18, 24); var standard = Read(1, 24); var basic = Read(0, 40); var tag = Read(9, 8);
        var attributes = BitConverter.ToUInt32(tag, 0); var size = BitConverter.ToInt64(standard, 8);
        if ((attributes & 0x400) != 0 || standard[20] != 0 || size < 0 ||
            ((attributes & 0x10) != 0) != (standard[21] != 0) || BitConverter.ToUInt32(basic, 32) != attributes)
            throw new UnauthorizedAccessException("The Windows descriptor is redirected, deleted or has inconsistent native kind.");
        if ((BitConverter.ToUInt64(id, 8) | BitConverter.ToUInt64(id, 16)) == 0) throw new PlatformNotSupportedException("The filesystem supplied no stable original Windows file ID.");
        DemandDeveloperWindowsSingleStream(handle, (attributes & 0x10) != 0);
        return new(BitConverter.ToUInt64(id, 0), BitConverter.ToUInt64(id, 8), BitConverter.ToUInt64(id, 16), attributes,
            BitConverter.ToUInt32(standard, 16), (ulong)size, BitConverter.ToInt64(basic, 16), BitConverter.ToInt64(basic, 24));
    }
    private static void DemandDeveloperWindowsSingleStream(SafeFileHandle handle, bool directory)
    {
        var bytes = new byte[4096]; var status = DeveloperWindowsNtQueryInformationFile(handle, out var result, bytes, (uint)bytes.Length, 22);
        DemandDeveloperWindowsStatus(status, result, "Observe original Windows stream inventory");
        var length = checked((int)result.Information.ToUInt64());
        if (directory && length == 0) return; // No data streams on this SAME observed directory.
        if (length < 24 || length > bytes.Length) throw new PlatformNotSupportedException("The filesystem supplied no bounded original Windows stream inventory.");
        var next = BitConverter.ToUInt32(bytes, 0); var size = BitConverter.ToUInt32(bytes, 4);
        if (next != 0 || size != 14 || size > length - 24 || Encoding.Unicode.GetString(bytes, 24, 14) != "::$DATA")
            throw new UnauthorizedAccessException("The original Windows file contains alternate or unsupported streams; incomplete capture refused.");
    }
    private static void DemandDeveloperWindowsPath(SafeFileHandle handle, string path, bool directory)
        => DemandExactHandlePath(handle, path, directory);
    private static void DemandDeveloperWindowsCase(SafeFileHandle directory)
    {
        var flags = new byte[4];
        if (!GetFileInformationByHandleEx(directory, 23, flags, 4)) throw new PlatformNotSupportedException("The filesystem did not provide the consumed Windows directory case capability.");
        if (BitConverter.ToUInt32(flags, 0) != 0) throw new PlatformNotSupportedException("Case-sensitive Windows directories require a distinct faithful registration path contract.");
    }
    private static void DemandDeveloperWindowsLocalVolume(SafeFileHandle handle)
    {
        var bytes = new byte[8]; var status = DeveloperWindowsNtQueryVolumeInformationFile(handle, out var result, bytes, 8, 4);
        DemandDeveloperWindowsStatus(status, result, "Observe original Windows filesystem device");
        if ((BitConverter.ToUInt32(bytes, 4) & 0x10) != 0) throw new PlatformNotSupportedException("The remote Windows filesystem has no proven original local publication/durability contract.");
    }
    [StructLayout(LayoutKind.Sequential)] private struct DeveloperWindowsUnicodeString { internal ushort Length, MaximumLength; internal IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)] private struct DeveloperWindowsObjectAttributes { internal uint Length; internal IntPtr RootDirectory, ObjectName; internal uint Attributes; internal IntPtr SecurityDescriptor, SecurityQualityOfService; }
    [StructLayout(LayoutKind.Sequential)] private struct DeveloperWindowsIoStatus { internal IntPtr Status; internal UIntPtr Information; }
    private static SafeFileHandle OpenDeveloperWindowsAt(SafeFileHandle parent, string leaf, uint access, uint sharing, uint disposition,
        bool? directory, IntPtr securityDescriptor = default)
    {
        if (!SafeDeveloperWindowsLeaf(leaf)) throw new UnauthorizedAccessException("The native child name is not a single supported Windows component.");
        var name = Marshal.StringToHGlobalUni(leaf); var unicode = new DeveloperWindowsUnicodeString { Length = checked((ushort)(leaf.Length * 2)), MaximumLength = checked((ushort)(leaf.Length * 2 + 2)), Buffer = name };
        var descriptor = Marshal.AllocHGlobal(Marshal.SizeOf<DeveloperWindowsUnicodeString>()); var retained = false;
        try
        {
            Marshal.StructureToPtr(unicode, descriptor, false); parent.DangerousAddRef(ref retained);
            var attributes = new DeveloperWindowsObjectAttributes { Length = (uint)Marshal.SizeOf<DeveloperWindowsObjectAttributes>(), RootDirectory = parent.DangerousGetHandle(), ObjectName = descriptor,
                Attributes = 0x1000 | 0x400, SecurityDescriptor = securityDescriptor }; // DONT_REPARSE + FORCE_ACCESS_CHECK, no case-folding alias.
            var options = 0x20U | 0x200000U | (directory is true ? 1U : directory is false ? 0x40U : 0U); // synchronous nonalert + open reparse itself
            var status = DeveloperWindowsNtCreateFile(out var handle, access | DeveloperWindowsSynchronize, ref attributes, out var result, IntPtr.Zero, 0x80, sharing, disposition, options, IntPtr.Zero, 0);
            if (status < 0 || status == 0x103 || unchecked((int)result.Status.ToInt64()) < 0 || unchecked((int)result.Status.ToInt64()) == 0x103)
            { handle?.Dispose(); DemandDeveloperWindowsStatus(status, result, "Open original Windows child"); }
            if (handle is null || handle.IsInvalid) { handle?.Dispose(); throw new IOException("The successful Windows child open supplied no actual handle."); }
            return handle;
        }
        finally { if (retained) parent.DangerousRelease(); Marshal.FreeHGlobal(descriptor); Marshal.FreeHGlobal(name); }
    }
    private static void DemandDeveloperWindowsStatus(int status, DeveloperWindowsIoStatus result, string operation)
    {
        var completed = unchecked((int)result.Status.ToInt64()); // IO_STATUS_BLOCK union contains a 32-bit NTSTATUS, not a sign-extended pointer.
        if (status == 0x103 || completed == 0x103) throw new PlatformNotSupportedException("The original synchronous Windows operation unexpectedly returned pending: " + operation);
        var code = status < 0 ? status : completed < 0 ? completed : 0;
        if (code == 0) return;
        var error = DeveloperWindowsRtlNtStatusToDosError(code);
        if (error is 1 or 50 or 87 or 120) throw new PlatformNotSupportedException("The filesystem does not support the required original Windows operation: " + operation);
        if (error is 2 or 3) throw new FileNotFoundException("The original Windows component is absent.");
        if (error is 4390 or 4392 or 1920) throw new UnauthorizedAccessException("The original Windows component crosses a reparse boundary.");
        throw new Win32Exception(unchecked((int)error), operation + " failed or is unknown.");
    }
    private static IReadOnlyList<string> ReadDeveloperWindowsDirectoryBatch(SafeFileHandle directory, bool restart, out bool complete)
    {
        var bytes = new byte[32768]; var status = DeveloperWindowsNtQueryDirectoryFile(directory, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out var result, bytes,
            (uint)bytes.Length, 12, false, IntPtr.Zero, restart);
        complete = status == unchecked((int)0x80000006); // STATUS_NO_MORE_FILES is the only completion witness.
        if (complete) return Array.Empty<string>();
        DemandDeveloperWindowsStatus(status, result, "Enumerate original Windows directory");
        var length = checked((int)result.Information.ToUInt64());
        if (length < 12 || length > bytes.Length) throw new InvalidDataException("The original native directory batch has an invalid layout.");
        var names = new List<string>(); var offset = 0;
        while (true)
        {
            if (length - offset < 12) throw new InvalidDataException("The native directory entry header is incomplete.");
            var next = BitConverter.ToUInt32(bytes, offset); var nameLength = BitConverter.ToUInt32(bytes, offset + 8);
            if (nameLength is 0 or > 510 || (nameLength & 1) != 0 || nameLength > length - offset - 12)
                throw new InvalidDataException("The native directory entry name is malformed or unsupported.");
            var name = Encoding.Unicode.GetString(bytes, offset + 12, checked((int)nameLength));
            if (name is not ("." or "..")) { if (!SafeDeveloperWindowsLeaf(name)) throw new InvalidDataException("The original source contains an unsupported Windows name; incomplete capture refused."); names.Add(name); }
            if (next == 0) break;
            if ((next & 7) != 0 || next < 12 + nameLength || next > length - offset - 12) throw new InvalidDataException("The native directory entry offset is malformed.");
            offset = checked(offset + (int)next);
        }
        return names;
    }
    private static string CopyDeveloperWindowsSid(IntPtr sid)
    {
        if (sid == IntPtr.Zero || !DeveloperWindowsIsValidSid(sid)) throw new UnauthorizedAccessException("The actual Windows principal SID is invalid or absent.");
        var size = DeveloperWindowsGetLengthSid(sid); if (size is < 8 or > 68) throw new InvalidDataException("The native Windows SID exceeds its bounded layout.");
        var bytes = new byte[size]; Marshal.Copy(sid, bytes, 0, bytes.Length); return Convert.ToHexString(bytes);
    }
    private static string CurrentDeveloperWindowsSid()
    {
        if (!DeveloperWindowsOpenThreadToken(DeveloperWindowsGetCurrentThread(), 8, true, out var token))
        {
            var error = Marshal.GetLastPInvokeError(); token?.Dispose();
            if (error != 1008 || !DeveloperWindowsOpenProcessToken(DeveloperWindowsGetCurrentProcess(), 8, out token))
                throw new Win32Exception(error == 1008 ? Marshal.GetLastPInvokeError() : error, "The current effective Windows token is unavailable.");
        }
        using (token)
        {
            _ = DeveloperWindowsGetTokenInformation(token, 1, IntPtr.Zero, 0, out var size);
            if (Marshal.GetLastPInvokeError() != 122 || size is < 16 or > 1024) throw new UnauthorizedAccessException("The actual effective Windows token has an unsupported user layout.");
            var bytes = Marshal.AllocHGlobal(checked((int)size));
            try { if (!DeveloperWindowsGetTokenInformation(token, 1, bytes, size, out var read) || read > size) throw new Win32Exception(Marshal.GetLastPInvokeError(), "The actual effective Windows user is unavailable."); return CopyDeveloperWindowsSid(Marshal.ReadIntPtr(bytes)); }
            finally { Marshal.FreeHGlobal(bytes); }
        }
    }
    private static void DemandDeveloperWindowsSid(string original)
    { if (CurrentDeveloperWindowsSid() != original) throw new UnauthorizedAccessException("The original Windows effective principal changed."); }
    private static void DemandDeveloperWindowsOwner(SafeFileHandle handle, string original)
    {
        DemandDeveloperWindowsSid(original);
        var error = DeveloperWindowsGetSecurityInfo(handle, 1, 1, out var owner, out _, out _, out _, out var descriptor);
        try { if (error != 0) throw new Win32Exception(unchecked((int)error), "The SAME original descriptor owner is unavailable."); if (CopyDeveloperWindowsSid(owner) != original) throw new UnauthorizedAccessException("The original Windows metadata/project descriptor belongs to another OS principal."); }
        finally { if (descriptor != IntPtr.Zero) _ = DeveloperWindowsLocalFree(descriptor); }
    }
    private static void FlushDeveloperWindowsNative(SafeFileHandle handle)
    {
        // Documented Flags=0 writes both data and metadata and synchronizes the
        // underlying storage cache. Reduced/data-only/no-sync flags are never used.
        // https://learn.microsoft.com/windows-hardware/drivers/ddi/ntifs/nf-ntifs-ntflushbuffersfileex
        var status = DeveloperWindowsNtFlushBuffersFileEx(handle, 0, IntPtr.Zero, 0, out var result);
        DemandDeveloperWindowsStatus(status, result, "Synchronize original Windows filesystem metadata");
    }
    private sealed class DeveloperWindowsPrivateDescriptor : IDisposable
    {
        internal IntPtr Pointer;
        internal DeveloperWindowsPrivateDescriptor(string originalSid)
        {
            DemandDeveloperWindowsSid(originalSid);
            var bytes = Convert.FromHexString(originalSid); var subauthorities = bytes[1];
            if (bytes.Length != 8 + 4 * subauthorities) throw new InvalidDataException("The retained Windows SID layout changed.");
            ulong authority = 0; for (var i = 2; i < 8; i++) authority = (authority << 8) | bytes[i];
            var sid = "S-" + bytes[0] + "-" + authority;
            for (var i = 0; i < subauthorities; i++) sid += "-" + BitConverter.ToUInt32(bytes, 8 + 4 * i);
            // Explicit genuine user ownership and protected inheritable user-only DACL.
            // No account/profile label is used as an OS identity or access grant.
            if (!DeveloperWindowsConvertSecurityDescriptor("O:" + sid + "D:P(A;OICI;FA;;;" + sid + ")", 1, out Pointer, out _))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "The original private Windows metadata descriptor is unavailable.");
        }
        public void Dispose() { var pointer = Interlocked.Exchange(ref Pointer, IntPtr.Zero); if (pointer != IntPtr.Zero) _ = DeveloperWindowsLocalFree(pointer); }
    }
    private static void RenameDeveloperWindowsCreateOnly(SafeFileHandle stage, SafeFileHandle destinationParent, string leaf)
    {
        if (!SafeDeveloperWindowsLeaf(leaf)) throw new UnauthorizedAccessException("The original native metadata leaf is invalid.");
        // WDK FILE_RENAME_INFORMATION (class10): BOOLEAN at0, HANDLE at8,
        // ULONG byte length at16, counted UTF16 at20 for both supported 64-bit ABIs.
        // NtSetInformationFile explicitly consumes RootDirectory and relative FileName.
        var name = Encoding.Unicode.GetBytes(leaf); var payload = new byte[24 + name.Length]; var retained = false;
        try
        {
            destinationParent.DangerousAddRef(ref retained); BitConverter.GetBytes(destinationParent.DangerousGetHandle().ToInt64()).CopyTo(payload, 8);
            BitConverter.GetBytes((uint)name.Length).CopyTo(payload, 16); name.CopyTo(payload, 20);
            var status = DeveloperWindowsNtSetInformationFile(stage, out var result, payload, (uint)payload.Length, 10);
            DemandDeveloperWindowsStatus(status, result, "Publish exact original create-only Windows metadata");
        }
        finally { if (retained) destinationParent.DangerousRelease(); }
    }
    private static void RetainDeveloperWindowsStageTimestamps(SafeFileHandle stage)
    {
        // An exclusively created metadata stage keeps its genuine creation timestamps.
        // FileBasicInformation -1 suppresses automatic changes only on THIS handle, so
        // final writer close cannot silently invalidate the acknowledged read version.
        // External handles retain ordinary timestamp/version behavior.
        // https://learn.microsoft.com/windows-hardware/drivers/ddi/wdm/ns-wdm-_file_basic_information
        var bytes = new byte[40]; BitConverter.GetBytes(-1L).CopyTo(bytes, 16); BitConverter.GetBytes(-1L).CopyTo(bytes, 24);
        var status = DeveloperWindowsNtSetInformationFile(stage, out var result, bytes, (uint)bytes.Length, 4);
        DemandDeveloperWindowsStatus(status, result, "Retain exact original Windows metadata stage timestamps");
    }
    [DllImport("ntdll.dll", EntryPoint = "NtCreateFile")] private static extern int DeveloperWindowsNtCreateFile(out SafeFileHandle handle, uint access, ref DeveloperWindowsObjectAttributes attributes, out DeveloperWindowsIoStatus status, IntPtr allocation, uint fileAttributes, uint sharing, uint disposition, uint options, IntPtr ea, uint eaLength);
    [DllImport("ntdll.dll", EntryPoint = "NtQueryDirectoryFile")] private static extern int DeveloperWindowsNtQueryDirectoryFile(SafeFileHandle handle, IntPtr evt, IntPtr apc, IntPtr context, out DeveloperWindowsIoStatus status, [Out] byte[] bytes, uint length, int information, [MarshalAs(UnmanagedType.U1)] bool single, IntPtr pattern, [MarshalAs(UnmanagedType.U1)] bool restart);
    [DllImport("ntdll.dll", EntryPoint = "NtQueryInformationFile")] private static extern int DeveloperWindowsNtQueryInformationFile(SafeFileHandle handle, out DeveloperWindowsIoStatus status, [Out] byte[] bytes, uint length, int information);
    [DllImport("ntdll.dll", EntryPoint = "NtSetInformationFile")] private static extern int DeveloperWindowsNtSetInformationFile(SafeFileHandle handle, out DeveloperWindowsIoStatus status, [In] byte[] bytes, uint length, int information);
    [DllImport("ntdll.dll", EntryPoint = "NtQueryVolumeInformationFile")] private static extern int DeveloperWindowsNtQueryVolumeInformationFile(SafeFileHandle handle, out DeveloperWindowsIoStatus status, [Out] byte[] bytes, uint length, int information);
    [DllImport("ntdll.dll", EntryPoint = "NtFlushBuffersFileEx")] private static extern int DeveloperWindowsNtFlushBuffersFileEx(SafeFileHandle handle, uint flags, IntPtr parameters, uint parametersSize, out DeveloperWindowsIoStatus status);
    [DllImport("ntdll.dll", EntryPoint = "RtlNtStatusToDosError")] private static extern uint DeveloperWindowsRtlNtStatusToDosError(int status);
    [DllImport("kernel32.dll", EntryPoint = "GetFileType", SetLastError = true)] private static extern uint DeveloperWindowsGetFileType(SafeFileHandle handle);
    [DllImport("kernel32.dll", EntryPoint = "GetCurrentProcess")] private static extern IntPtr DeveloperWindowsGetCurrentProcess();
    [DllImport("kernel32.dll", EntryPoint = "GetCurrentThread")] private static extern IntPtr DeveloperWindowsGetCurrentThread();
    [DllImport("kernel32.dll", EntryPoint = "LocalFree")] private static extern IntPtr DeveloperWindowsLocalFree(IntPtr bytes);
    [DllImport("advapi32.dll", EntryPoint = "OpenThreadToken", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeveloperWindowsOpenThreadToken(IntPtr thread, uint access, [MarshalAs(UnmanagedType.Bool)] bool asSelf, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", EntryPoint = "OpenProcessToken", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeveloperWindowsOpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", EntryPoint = "GetTokenInformation", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeveloperWindowsGetTokenInformation(SafeAccessTokenHandle token, int information, IntPtr bytes, uint length, out uint required);
    [DllImport("advapi32.dll", EntryPoint = "GetSecurityInfo")] private static extern uint DeveloperWindowsGetSecurityInfo(SafeFileHandle handle, int type, uint requested, out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor);
    [DllImport("advapi32.dll", EntryPoint = "IsValidSid")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeveloperWindowsIsValidSid(IntPtr sid);
    [DllImport("advapi32.dll", EntryPoint = "GetLengthSid")] private static extern uint DeveloperWindowsGetLengthSid(IntPtr sid);
    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeveloperWindowsConvertSecurityDescriptor(string text, uint revision, out IntPtr descriptor, out uint length);
}
