using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Infrastructure.Native.Windows;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService
{
    // This additive internal bridge forwards the SAME private native primitives.
    // The frozen developer port's public factories, bodies and custody are unchanged.
    internal static void WindowsCustodyRequireCapabilities()
    {
        RequireDeveloperWindowsExports();
        if (!NativeLibrary.TryLoad("advapi32.dll", out var library)) throw new PlatformNotSupportedException("The actual Windows security library is unavailable.");
        try
        {
            foreach (var name in new[] { "IsValidSecurityDescriptor", "GetSecurityDescriptorControl", "GetSecurityDescriptorDacl", "GetAclInformation", "GetAce" })
                if (!NativeLibrary.TryGetExport(library, name, out _)) throw new PlatformNotSupportedException("The actual Windows security primitive is unavailable: " + name);
        }
        finally { NativeLibrary.Free(library); }
    }
    internal static string WindowsCustodyNormalizePath(string path) => NormalizeDeveloperWindowsPath(path);
    internal static bool WindowsCustodySafeLeaf(string leaf) => SafeDeveloperWindowsLeaf(leaf);
    internal static WindowsOriginalRoot WindowsCustodyRetainRoot(string root)
    {
        WindowsCustodyRequireCapabilities(); var actual = new DeveloperWindowsRootLease(root);
        try { return new(actual.Principal, actual.OpenDirectory, actual.OpenRead, actual.DemandCurrent, actual.Dispose); }
        catch { actual.Dispose(); throw; }
    }
    internal static string WindowsCustodyCurrentSid() { WindowsCustodyRequireCapabilities(); return CurrentDeveloperWindowsSid(); }
    internal static void WindowsCustodyDemandSid(string sid) { RequireDeveloperWindowsExports(); DemandDeveloperWindowsSid(sid); }
    internal static WindowsOriginalFileIdentity WindowsCustodyReadIdentity(SafeFileHandle handle)
    {
        RequireDeveloperWindowsExports(); var actual = ReadDeveloperWindowsIdentity(handle);
        return new(actual.Volume, actual.IdLow, actual.IdHigh, actual.Attributes, actual.Links, actual.Size, actual.LastWrite, actual.Change);
    }
    internal static void WindowsCustodyDemandPath(SafeFileHandle handle, string fullPath, bool directory)
    { RequireDeveloperWindowsExports(); DemandDeveloperWindowsPath(handle, NormalizeDeveloperWindowsPath(fullPath), directory); }
    internal static void WindowsCustodyDemandOwner(SafeFileHandle handle, string currentSid)
    { RequireDeveloperWindowsExports(); DemandDeveloperWindowsOwner(handle, currentSid); }
    internal static WindowsOriginalPrivateDescriptor WindowsCustodyPrivateDescriptor(string currentSid)
    {
        WindowsCustodyRequireCapabilities(); var actual = new DeveloperWindowsPrivateDescriptor(currentSid);
        try { return new(actual, () => actual.Pointer); }
        catch { actual.Dispose(); throw; }
    }
    internal static SafeFileHandle WindowsCustodyOpenRelative(SafeFileHandle parent, string leaf, uint access, uint sharing,
        WindowsOriginalCreateDisposition disposition, WindowsOriginalFileKind kind, IntPtr security)
    {
        RequireDeveloperWindowsExports();
        const uint allowed = WindowsOriginalFileCustody.ReadData | WindowsOriginalFileCustody.WriteData | WindowsOriginalFileCustody.AppendData |
            WindowsOriginalFileCustody.ReadAttributes | WindowsOriginalFileCustody.WriteAttributes | WindowsOriginalFileCustody.Delete |
            WindowsOriginalFileCustody.ReadControl | WindowsOriginalFileCustody.Synchronize;
        if ((access & ~allowed) != 0 || (sharing & ~3U) != 0 || disposition is not (WindowsOriginalCreateDisposition.OpenExisting or
            WindowsOriginalCreateDisposition.CreateNew or WindowsOriginalCreateDisposition.OpenOrCreate) ||
            kind is not (WindowsOriginalFileKind.Any or WindowsOriginalFileKind.File or WindowsOriginalFileKind.Directory))
            throw new UnauthorizedAccessException("The original internal Windows open exceeds its bounded physical access/disposition contract.");
        var parentIdentity = ReadDeveloperWindowsIdentity(parent);
        if (!parentIdentity.IsDirectory) throw new UnauthorizedAccessException("The SAME original Windows parent is not a directory.");
        DemandDeveloperWindowsCase(parent); DemandDeveloperWindowsLocalVolume(parent);
        var handle = WindowsCustodyOpenOriginalAt(parent, leaf, access, sharing, (uint)disposition,
            kind == WindowsOriginalFileKind.Any ? null : kind == WindowsOriginalFileKind.Directory, security);
        try
        {
            var identity = ReadDeveloperWindowsIdentity(handle);
            if (identity.Volume != parentIdentity.Volume || (kind == WindowsOriginalFileKind.File && !identity.IsRegular) ||
                (kind == WindowsOriginalFileKind.Directory && !identity.IsDirectory) || (!identity.IsDirectory && identity.Links != 1))
                throw new UnauthorizedAccessException("The actual relative Windows child is redirected, aliased or on another volume.");
            if (identity.IsDirectory) DemandDeveloperWindowsCase(handle);
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }
    internal static void WindowsCustodyPublishCreateOnly(SafeFileHandle stage, SafeFileHandle parent, string leaf)
    {
        RequireDeveloperWindowsExports();
        var actual = ReadDeveloperWindowsIdentity(stage); var destination = ReadDeveloperWindowsIdentity(parent);
        if (!actual.IsRegular || actual.Links != 1 || !destination.IsDirectory || actual.Volume != destination.Volume)
            throw new UnauthorizedAccessException("The exact original Windows stage and destination are not same-volume physical objects.");
        DemandDeveloperWindowsCase(parent);
        if (!SafeDeveloperWindowsLeaf(leaf)) throw new UnauthorizedAccessException("The original internal Windows publication leaf is invalid.");
        var name = Encoding.Unicode.GetBytes(leaf); var payload = new byte[24 + name.Length]; var retained = false;
        try
        {
            parent.DangerousAddRef(ref retained); BitConverter.GetBytes(parent.DangerousGetHandle().ToInt64()).CopyTo(payload, 8);
            BitConverter.GetBytes((uint)name.Length).CopyTo(payload, 16); name.CopyTo(payload, 20);
            var status = DeveloperWindowsNtSetInformationFile(stage, out var result, payload, (uint)payload.Length, 10);
            WindowsCustodyDemandOriginalStatus(status, result, "Publish exact original create-only Windows stage");
        }
        finally { if (retained) parent.DangerousRelease(); }
    }
    private static SafeFileHandle WindowsCustodyOpenOriginalAt(SafeFileHandle parent, string leaf, uint access, uint sharing,
        uint disposition, bool? directory, IntPtr security)
    {
        // The same retained-parent ABI and private declarations are reused here.
        // This separately bounded bridge preserves the raw original status which
        // the historical developer-only exception contract intentionally omitted.
        if (!SafeDeveloperWindowsLeaf(leaf)) throw new UnauthorizedAccessException("The original internal Windows child is not a single safe component.");
        var name = Marshal.StringToHGlobalUni(leaf); var retained = false;
        var descriptor = Marshal.AllocHGlobal(Marshal.SizeOf<DeveloperWindowsUnicodeString>());
        try
        {
            Marshal.StructureToPtr(new DeveloperWindowsUnicodeString { Length = checked((ushort)(leaf.Length * 2)),
                MaximumLength = checked((ushort)(leaf.Length * 2 + 2)), Buffer = name }, descriptor, false);
            parent.DangerousAddRef(ref retained);
            var attributes = new DeveloperWindowsObjectAttributes { Length = (uint)Marshal.SizeOf<DeveloperWindowsObjectAttributes>(),
                RootDirectory = parent.DangerousGetHandle(), ObjectName = descriptor, Attributes = 0x1000 | 0x400, SecurityDescriptor = security };
            var options = 0x20U | 0x200000U | (directory is true ? 1U : directory is false ? 0x40U : 0U);
            var status = DeveloperWindowsNtCreateFile(out var handle, access | DeveloperWindowsSynchronize, ref attributes,
                out var result, IntPtr.Zero, 0x80, sharing, disposition, options, IntPtr.Zero, 0);
            try { WindowsCustodyDemandOriginalStatus(status, result, "Open SAME original relative Windows child"); }
            catch { handle?.Dispose(); throw; }
            if (handle is null || handle.IsInvalid) { handle?.Dispose(); throw new IOException("The actual successful Windows child open supplied no native handle."); }
            return handle;
        }
        finally { if (retained) parent.DangerousRelease(); Marshal.FreeHGlobal(descriptor); Marshal.FreeHGlobal(name); }
    }
    private static void WindowsCustodyDemandOriginalStatus(int returned, DeveloperWindowsIoStatus result, string operation)
    {
        var completed = unchecked((int)result.Status.ToInt64());
        if (returned == 0x103 || completed == 0x103) throw new PlatformNotSupportedException("The actual synchronous Windows primitive unexpectedly returned pending: " + operation);
        if (returned >= 0 && completed >= 0) return;
        var failure = returned < 0 ? returned : completed; var win32 = DeveloperWindowsRtlNtStatusToDosError(failure);
        throw new WindowsOriginalNativeFailure(operation, returned, completed, win32);
    }
    internal static void WindowsCustodyDeleteOwnedStage(SafeFileHandle stage, string originalStagePath, WindowsOriginalFileIdentity expected)
    {
        RequireDeveloperWindowsExports(); var actual = WindowsCustodyReadIdentity(stage);
        if (!actual.IsRegular || actual.Links != 1 || !actual.SameReadVersion(expected))
            throw new UnauthorizedAccessException("The original owned Windows stage changed before SAME-handle cleanup.");
        DemandDeveloperWindowsPath(stage, NormalizeDeveloperWindowsPath(originalStagePath), false);
        var bytes = new byte[] { 1 }; // FILE_DISPOSITION_INFORMATION, class13: BOOLEAN DeleteFile.
        var status = DeveloperWindowsNtSetInformationFile(stage, out var result, bytes, 1, 13);
        WindowsCustodyDemandOriginalStatus(status, result, "Request deletion of exact original owned Windows stage");
    }
    internal static void WindowsCustodyFlush(SafeFileHandle handle) { RequireDeveloperWindowsExports(); FlushDeveloperWindowsNative(handle); }
    internal static void WindowsCustodyRetainStageTimestamps(SafeFileHandle stage) { RequireDeveloperWindowsExports(); RetainDeveloperWindowsStageTimestamps(stage); }

    internal static WindowsOriginalSecurityObservation WindowsCustodyReadSecurity(SafeFileHandle handle)
    {
        WindowsCustodyRequireCapabilities(); _ = ReadDeveloperWindowsIdentity(handle);
        var error = DeveloperWindowsGetSecurityInfo(handle, 1, 1 | 4, out var owner, out _, out var originalDacl, out _, out var descriptor);
        try
        {
            if (error != 0) throw new Win32Exception(unchecked((int)error), "The SAME original Windows security observation is unavailable.");
            if (descriptor == IntPtr.Zero || !WindowsCustodyIsValidSecurityDescriptor(descriptor) ||
                !WindowsCustodyGetSecurityDescriptorControl(descriptor, out var control, out var revision) || revision != 1 ||
                !WindowsCustodyGetSecurityDescriptorDacl(descriptor, out var present, out var dacl, out var defaulted) || dacl != originalDacl ||
                present != ((control & 4) != 0))
                throw new InvalidDataException("The actual Windows security descriptor has an unsupported native layout.");
            var sid = CopyDeveloperWindowsSid(owner); var entries = new List<WindowsOriginalAccessAce>(); byte? aclRevision = null;
            if (present && dacl != IntPtr.Zero)
            {
                var size = unchecked((ushort)Marshal.ReadInt16(dacl, 2)); var count = unchecked((ushort)Marshal.ReadInt16(dacl, 4));
                aclRevision = Marshal.ReadByte(dacl);
                var information = new byte[12];
                if (size < 8 || count > 64 || aclRevision is not (2 or 4) ||
                    !WindowsCustodyGetAclInformation(dacl, information, 12, 2) || BitConverter.ToUInt32(information, 0) != count ||
                    BitConverter.ToUInt32(information, 4) < 8 || BitConverter.ToUInt32(information, 4) > size ||
                    (ulong)BitConverter.ToUInt32(information, 4) + BitConverter.ToUInt32(information, 8) > size)
                    throw new InvalidDataException("The actual Windows DACL exceeds its bounded native layout.");
                for (uint index = 0; index < count; index++)
                {
                    if (!WindowsCustodyGetAce(dacl, index, out var ace) || ace.ToInt64() < dacl.ToInt64() + 8 || ace.ToInt64() > dacl.ToInt64() + size - 4)
                        throw new InvalidDataException("The actual Windows ACE lies outside its original DACL.");
                    var length = unchecked((ushort)Marshal.ReadInt16(ace, 2)); var offset = ace.ToInt64() - dacl.ToInt64();
                    if (length < 4 || length > size - offset) throw new InvalidDataException("The actual Windows ACE is incomplete.");
                    var bytes = new byte[length]; Marshal.Copy(ace, bytes, 0, length); uint? mask = null; string? aceSid = null;
                    // Basic allow/deny ACEs have a counted actual SID at offset8.
                    // Other types remain explicit opaque observations for store policy
                    // to reject; no callback/object ACE is silently treated as allow.
                    if (bytes[0] is 0 or 1)
                    {
                        if (length < 16) throw new InvalidDataException("The actual Windows basic ACE has no complete SID.");
                        var actualSid = IntPtr.Add(ace, 8); var sidSize = DeveloperWindowsGetLengthSid(actualSid);
                        if (sidSize != length - 8) throw new InvalidDataException("The actual Windows ACE SID length differs from its bounded entry.");
                        aceSid = CopyDeveloperWindowsSid(actualSid); mask = BitConverter.ToUInt32(bytes, 4);
                    }
                    entries.Add(new(bytes[0], bytes[1], mask, aceSid, Convert.ToHexString(bytes)));
                }
            }
            var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            { OwnerSid = sid, Control = control, Revision = revision, DaclPresent = present, DaclIsNull = present && dacl == IntPtr.Zero,
                DaclDefaulted = defaulted, AclRevision = aclRevision, Aces = entries.Select(ace => ace.RawHex).ToArray() }))).ToLowerInvariant();
            return new(sid, control, revision, present, present && dacl == IntPtr.Zero, defaulted, (control & 0x1000) != 0, aclRevision, entries.AsReadOnly(), fingerprint);
        }
        finally { if (descriptor != IntPtr.Zero) _ = DeveloperWindowsLocalFree(descriptor); }
    }
    internal static void WindowsCustodyDemandPrivateStage(SafeFileHandle handle, string currentSid)
    {
        DemandDeveloperWindowsSid(currentSid); var actual = WindowsCustodyReadSecurity(handle);
        if (actual.OwnerSid != currentSid || !actual.DaclPresent || actual.DaclIsNull || !actual.DaclProtected || actual.Dacl.Count != 1 ||
            actual.Dacl[0] is not { Type: 0, AccessMask: 0x1f01ff } ace || ace.Sid != currentSid || (ace.Flags & ~0x13) != 0)
            throw new UnauthorizedAccessException("The newly owned Windows stage lacks the exact protected actual-user private DACL.");
    }
    [DllImport("advapi32.dll", EntryPoint = "IsValidSecurityDescriptor")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WindowsCustodyIsValidSecurityDescriptor(IntPtr descriptor);
    [DllImport("advapi32.dll", EntryPoint = "GetSecurityDescriptorControl", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WindowsCustodyGetSecurityDescriptorControl(IntPtr descriptor, out ushort control, out uint revision);
    [DllImport("advapi32.dll", EntryPoint = "GetSecurityDescriptorDacl", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WindowsCustodyGetSecurityDescriptorDacl(IntPtr descriptor, [MarshalAs(UnmanagedType.Bool)] out bool present, out IntPtr dacl, [MarshalAs(UnmanagedType.Bool)] out bool defaulted);
    [DllImport("advapi32.dll", EntryPoint = "GetAclInformation", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WindowsCustodyGetAclInformation(IntPtr acl, [Out] byte[] information, uint length, int kind);
    [DllImport("advapi32.dll", EntryPoint = "GetAce", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WindowsCustodyGetAce(IntPtr acl, uint index, out IntPtr ace);
}
