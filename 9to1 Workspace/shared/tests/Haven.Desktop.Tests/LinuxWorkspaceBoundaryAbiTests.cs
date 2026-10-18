using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Haven.Infrastructure;
using Microsoft.Win32.SafeHandles;

namespace Haven.Desktop.Tests;

// Calls the actual production native boundary against a private temporary directory.
// No permission authority, application selection or published file is fabricated.
public sealed class LinuxWorkspaceBoundaryAbiTests
{
    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public void Production_directory_and_anonymous_file_flags_work_on_the_actual_supported_process_ABI()
    {
        Assert.True(RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64);
        var root = Directory.CreateTempSubdirectory("9to1-linux-production-abi-").FullName;
        try
        {
            using var parent = OpenParent(root);
            using var directory = OpenAt(parent, ".", Flag("LinuxPath") | Flag("LinuxDirectory") | Flag("LinuxCloseOnExec"));
            Assert.False(directory.IsInvalid);
            using var anonymous = OpenAt(parent, ".", Flag("LinuxAnonymousWrite"), 0x180);
            Assert.False(anonymous.IsInvalid);
            using var stream = new FileStream(anonymous, FileAccess.ReadWrite);
            stream.WriteByte(0x2d);
            stream.Flush();
            stream.Position = 0;
            Assert.Equal(0x2d, stream.ReadByte());
            Assert.Empty(Directory.EnumerateFileSystemEntries(root)); // The real anonymous inode was never published.
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public void Actual_directory_open_still_refuses_a_real_symlink_after_the_ABI_correction()
    {
        Assert.True(RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64);
        var root = Directory.CreateTempSubdirectory("9to1-linux-production-guard-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "target"));
            Directory.CreateSymbolicLink(Path.Combine(root, "link"), "target");
            using var parent = OpenParent(root);
            Action openLink = () =>
            {
                using var denied = OpenAt(parent, "link", Flag("LinuxPath") | Flag("LinuxDirectory") | Flag("LinuxCloseOnExec"));
            };
            Assert.Throws<UnauthorizedAccessException>(openLink);
            Assert.True(Directory.Exists(Path.Combine(root, "target")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static SafeFileHandle OpenParent(string root)
    {
        var descriptor = Invoke<int>("LinuxOpen", root, Flag("LinuxPath") | Flag("LinuxDirectory") | Flag("LinuxCloseOnExec"), 0U);
        Assert.True(descriptor >= 0, "The actual production libc directory open must succeed.");
        return new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
    }
    private static SafeFileHandle OpenAt(SafeFileHandle parent, string path, ulong flags, ulong mode = 0) =>
        Invoke<SafeFileHandle>("OpenLinuxAt", parent, path, flags, mode, true);
    private static ulong Flag(string field) => (ulong)typeof(WorkspaceToolService)
        .GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    private static T Invoke<T>(string method, params object[] arguments)
    {
        try { return (T)typeof(WorkspaceToolService).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, arguments)!; }
        catch (TargetInvocationException error) when (error.InnerException is { } actual)
        { ExceptionDispatchInfo.Capture(actual).Throw(); throw; }
    }
}
