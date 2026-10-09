using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Windows;

// Assembly-internal physical observations only. Callers retain their own original
// admission, Task/result custody, cancellation, namespace-outcome and cleanup rules.
// No value here is Home consent, a selected account or an execution authority.
internal readonly record struct WindowsOriginalFileIdentity(ulong Volume, ulong IdLow, ulong IdHigh,
    uint Attributes, uint Links, ulong Size, long LastWrite, long Change)
{
    internal bool IsDirectory => (Attributes & 0x10) != 0;
    internal bool IsRegular => !IsDirectory && (Attributes & 0x400) == 0;
    internal bool SameFile(WindowsOriginalFileIdentity other) => Volume == other.Volume && IdLow == other.IdLow && IdHigh == other.IdHigh;
    internal bool SameReadVersion(WindowsOriginalFileIdentity other) => SameFile(other) && Attributes == other.Attributes &&
        Links == other.Links && Size == other.Size && LastWrite == other.LastWrite && Change == other.Change;
}

internal enum WindowsOriginalCreateDisposition : uint { OpenExisting = 1, CreateNew = 2, OpenOrCreate = 3 }
internal enum WindowsOriginalFileKind { Any, File, Directory }

internal sealed record WindowsOriginalAccessAce(byte Type, byte Flags, uint? AccessMask, string? Sid, string RawHex);
internal sealed record WindowsOriginalSecurityObservation(string OwnerSid, ushort Control, uint Revision,
    bool DaclPresent, bool DaclIsNull, bool DaclDefaulted, bool DaclProtected, byte? AclRevision,
    IReadOnlyList<WindowsOriginalAccessAce> Dacl, string Fingerprint);

internal sealed class WindowsOriginalNativeFailure : IOException
{
    internal int ReturnedStatus { get; }
    internal int CompletedStatus { get; }
    internal int? OriginalFailureStatus { get; }
    internal uint Win32Error { get; }
    internal WindowsOriginalNativeFailure(string operation, int returned, int completed, uint win32)
        : base(operation + " failed or is unknown; original NTSTATUS " + unchecked((uint)returned).ToString("X8") + "/" + unchecked((uint)completed).ToString("X8"))
    {
        ReturnedStatus = returned; CompletedStatus = completed; Win32Error = win32;
        OriginalFailureStatus = returned < 0 && completed < 0 ? (returned == completed ? returned : null)
            : returned < 0 ? returned : completed < 0 ? completed : null;
    }
}

internal sealed class WindowsOriginalRoot : IDisposable
{
    private readonly Func<string, bool, SafeFileHandle> _directory;
    private readonly Func<string, SafeFileHandle> _read;
    private readonly Action _demand, _dispose;
    internal string Principal { get; }
    internal WindowsOriginalRoot(string principal, Func<string, bool, SafeFileHandle> directory,
        Func<string, SafeFileHandle> read, Action demand, Action dispose)
    { Principal = principal; _directory = directory; _read = read; _demand = demand; _dispose = dispose; }
    internal SafeFileHandle OpenDirectory(string fullPath, bool mutable = false) => _directory(fullPath, mutable);
    internal SafeFileHandle OpenRead(string fullPath) => _read(fullPath);
    internal void DemandCurrent() => _demand();
    public void Dispose() => _dispose();
}

internal sealed class WindowsOriginalPrivateDescriptor : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<IntPtr> _pointer;
    private readonly IDisposable _owned;
    private bool _closed;
    internal WindowsOriginalPrivateDescriptor(IDisposable owned, Func<IntPtr> pointer) { _owned = owned; _pointer = pointer; }
    internal SafeFileHandle Use(Func<IntPtr, SafeFileHandle> original)
    {
        lock (_gate)
        {
            if (_closed) throw new ObjectDisposedException(nameof(WindowsOriginalPrivateDescriptor));
            var pointer = _pointer();
            if (pointer == IntPtr.Zero) throw new ObjectDisposedException(nameof(WindowsOriginalPrivateDescriptor));
            return original(pointer);
        }
    }
    public void Dispose() { lock (_gate) { if (_closed) return; _closed = true; _owned.Dispose(); } }
}

internal static class WindowsOriginalFileCustody
{
    // Native opens always add SYNCHRONIZE. Access is bounded to data/list/add,
    // attributes, DELETE and READ_CONTROL. Sharing permits READ/WRITE, never DELETE.
    internal const uint ReadData = 1, WriteData = 2, AppendData = 4, ReadAttributes = 0x80,
        WriteAttributes = 0x100, Delete = 0x10000, ReadControl = 0x20000, Synchronize = 0x100000;
    internal const uint DirectoryRead = ReadData | ReadAttributes | ReadControl | Synchronize;
    internal const uint DirectoryMutable = DirectoryRead | WriteData | AppendData;
    internal const uint FileRead = ReadData | ReadAttributes | ReadControl | Synchronize;
    internal const uint ExclusiveStage = FileRead | WriteData | WriteAttributes | Delete;

    internal static void RequireCapabilities() => WorkspaceToolService.WindowsCustodyRequireCapabilities();
    internal static string NormalizeLocalPath(string path) => WorkspaceToolService.WindowsCustodyNormalizePath(path);
    internal static bool IsSafeLeaf(string leaf) => WorkspaceToolService.WindowsCustodySafeLeaf(leaf);
    internal static WindowsOriginalRoot RetainRoot(string root) => WorkspaceToolService.WindowsCustodyRetainRoot(root);
    internal static string CurrentSid() => WorkspaceToolService.WindowsCustodyCurrentSid();
    internal static void DemandCurrentSid(string sid) => WorkspaceToolService.WindowsCustodyDemandSid(sid);
    internal static WindowsOriginalFileIdentity ReadIdentity(SafeFileHandle handle) => WorkspaceToolService.WindowsCustodyReadIdentity(handle);
    internal static void DemandPath(SafeFileHandle handle, string fullPath, bool directory) => WorkspaceToolService.WindowsCustodyDemandPath(handle, fullPath, directory);
    internal static void DemandOwner(SafeFileHandle handle, string currentSid) => WorkspaceToolService.WindowsCustodyDemandOwner(handle, currentSid);
    internal static WindowsOriginalSecurityObservation ReadSecurity(SafeFileHandle handle) => WorkspaceToolService.WindowsCustodyReadSecurity(handle);
    // This strict predicate is only for a caller's newly owned exclusive stage.
    // Existing-store ACL policy belongs to the store owner, which reads ReadSecurity.
    internal static void DemandPrivateStage(SafeFileHandle handle, string currentSid) => WorkspaceToolService.WindowsCustodyDemandPrivateStage(handle, currentSid);
    internal static WindowsOriginalPrivateDescriptor CreatePrivateDescriptor(string currentSid) => WorkspaceToolService.WindowsCustodyPrivateDescriptor(currentSid);
    internal static SafeFileHandle OpenRelative(SafeFileHandle parent, string leaf, uint access, uint sharing,
        WindowsOriginalCreateDisposition disposition, WindowsOriginalFileKind kind, WindowsOriginalPrivateDescriptor? security = null)
        => security is null ? WorkspaceToolService.WindowsCustodyOpenRelative(parent, leaf, access, sharing, disposition, kind, IntPtr.Zero)
            : security.Use(pointer => WorkspaceToolService.WindowsCustodyOpenRelative(parent, leaf, access, sharing, disposition, kind, pointer));
    internal static void PublishCreateOnly(SafeFileHandle stage, SafeFileHandle destinationParent, string safeLeaf)
        => WorkspaceToolService.WindowsCustodyPublishCreateOnly(stage, destinationParent, safeLeaf);
    internal static bool IsOriginalMissing(Exception error) => error is WindowsOriginalNativeFailure
        { OriginalFailureStatus: unchecked((int)0xc0000034) or unchecked((int)0xc000003a) };
    internal static bool IsOriginalNameCollision(Exception error) => error is WindowsOriginalNativeFailure
        { OriginalFailureStatus: unchecked((int)0xc0000035) };
    // Requests deletion of THIS exact newly owned stage. Final owning-handle close
    // and SAME-parent namespace Flush remain the caller's separate custody duties.
    internal static void DeleteOwnedStage(SafeFileHandle stage, string originalStagePath, WindowsOriginalFileIdentity expected)
        => WorkspaceToolService.WindowsCustodyDeleteOwnedStage(stage, originalStagePath, expected);
    internal static void Flush(SafeFileHandle handle) => WorkspaceToolService.WindowsCustodyFlush(handle);
    internal static void RetainStageTimestamps(SafeFileHandle exclusiveOwnedStage) => WorkspaceToolService.WindowsCustodyRetainStageTimestamps(exclusiveOwnedStage);
}
