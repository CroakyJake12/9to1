using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Windows;

public sealed partial class NativeWindowsHomeInstallerBootstrapAdmission
{
    // This boundary accepts the actual connected pipe, never a requested PID/SID.
    // Cryptographic/package admission independently consumes this observation.
    [SupportedOSPlatform("windows")]
    private static HomeNativeObservedPeer ReadOriginalRootServer(NamedPipeClientStream actual, Invocation work)
    {
        if (!actual.IsConnected || !GetNamedPipeServerProcessId(actual.SafePipeHandle, out var pid) || pid == 0 || pid > int.MaxValue)
            throw Native("GetNamedPipeServerProcessId original Root");
        var process = OpenRootProcess(0x1000, false, pid); work.Resources.Add(new(process));
        if (process.IsInvalid) throw Native("OpenProcess original Root peer");
        var opened = OpenRootToken(process, 0x0008, out var token); work.Resources.Add(new(token));
        if (!opened || token.IsInvalid) throw Native("OpenProcessToken original Root peer");
        var identity = new WindowsIdentity(token.DangerousGetHandle()); work.Resources.Add(new(identity));
        var sid = identity.User?.Value ?? throw new UnauthorizedAccessException("The actual Root server has no original OS principal.");
        if (!actual.IsConnected || !GetNamedPipeServerProcessId(actual.SafePipeHandle, out var again) || again != pid)
            throw new UnauthorizedAccessException("The actual Root transport peer changed during kernel observation.");
        return new(checked((int)pid), "windows-sid:" + sid);
    }
    [SupportedOSPlatform("windows")]
    private static (uint Rva, byte[] Bytes)? ReadOriginalRemoteCatalogue(Invocation work, string samePinnedImage)
    {
        // Resource-only mapping never runs peer code, imports or DLL initialization.
        // The resource bytes must subsequently match the independently pinned PE.
        var module = new OriginalResourceModule(LoadLibraryEx(samePinnedImage, IntPtr.Zero, 0x20 | 0x40));
        work.Resources.Add(new(module));
        if (module.Value == IntPtr.Zero) throw Native("LoadLibraryExW original Root resource-only image");
        var resource = FindResource(module.Value, new(SignedCatalogueResourceId), new(10));
        if (resource == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            return error is 1812 or 1813 or 1814 or 1815 ? null : throw new Win32Exception(error, "Actual connected Root catalogue unavailable.");
        }
        var length = SizeofResource(module.Value, resource);
        if (length is 0 or > 16 * 1024 * 1024) throw new InvalidDataException("The actual connected Root release catalogue exceeds its bound.");
        var loaded = LoadResource(module.Value, resource); var pointer = LockResource(loaded);
        if (loaded == IntPtr.Zero || pointer == IntPtr.Zero) throw Native("LoadResource/LockResource original connected Root");
        // Windows tags resource-only module handles; the image mapping base is
        // the untagged address. LOAD_LIBRARY_AS_IMAGE_RESOURCE preserves RVAs.
        var imageBase = module.Value.ToInt64() & ~3L;
        var offset = pointer.ToInt64() - imageBase;
        if (offset < 0 || offset > uint.MaxValue) throw new InvalidDataException("The actual Root resource is outside its original image.");
        var bytes = new byte[checked((int)length)]; Marshal.Copy(pointer, bytes, 0, bytes.Length);
        return ((uint)offset, bytes);
    }
    private sealed class OriginalResourceModule(IntPtr original) : IDisposable
    {
        internal readonly IntPtr Value = original; private bool _closed;
        public void Dispose()
        {
            if (_closed || Value == IntPtr.Zero) return;
            if (!FreeLibrary(Value)) throw Native("FreeLibrary original Root resource mapping");
            _closed = true;
        }
    }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle actualPipe, out uint pid);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
    private static extern SafeProcessHandle OpenRootProcess(uint rights, [MarshalAs(UnmanagedType.Bool)] bool inherited, uint pid);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("advapi32.dll", EntryPoint = "OpenProcessToken", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenRootToken(SafeProcessHandle actualProcess, uint access, out SafeAccessTokenHandle token);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr originalModule);
}
