using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Linux;

public sealed partial class LinuxHomePackageArtifactStore
{
    private sealed class DirectoryLock(SafeFileHandle directory) : IDisposable
    {
        private bool _released;
        public void Dispose()
        {
            if (_released) return;
            if (Flock(directory, 8) != 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Release original package publication lock.");
            _released = true;
        }
    }
    private static IDisposable LockDirectory(SafeFileHandle directory)
    {
        // A new open file description per store instance; flock conflicts even within
        // one process. Never wait unboundedly behind a second installer operation.
        if (Flock(directory, 2 | 4) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The package publication directory is busy or cannot be locked.");
        return new DirectoryLock(directory);
    }
    private static void RenameDirectory(SafeFileHandle parent, string stage, string active, bool exchange)
    {
        if (RenameAt2(parent, stage, parent, active, exchange ? 2u : 1u) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Atomic package directory publication refused; no rename fallback is permitted.");
    }
    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int Flock(SafeFileHandle descriptor, int operation);
    [DllImport("libc", EntryPoint = "renameat2", SetLastError = true)]
    private static extern int RenameAt2(SafeFileHandle parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string source,
        SafeFileHandle destinationParent, [MarshalAs(UnmanagedType.LPUTF8Str)] string destination, uint flags);
}
