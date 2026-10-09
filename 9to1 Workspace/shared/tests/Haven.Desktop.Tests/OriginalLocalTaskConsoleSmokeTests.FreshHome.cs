namespace Haven.Desktop.Tests;

public sealed partial class OriginalLocalTaskConsoleSmokeTests
{
    // Every caller has just received this new root from CreateTempSubdirectory.
    // That API creates the owning process's private temporary directory; no existing
    // user store is adopted here. The real Home domain still verifies euid ownership
    // and retained filesystem descriptors before publishing a usable domain.
    private static void PrepareOriginalPrivateHomeDirectory(string freshDataRoot)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("The original smoke fixture requires Linux.");

        const UnixFileMode privateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        var root = new DirectoryInfo(freshDataRoot);
        if (!root.Exists || (root.Attributes & FileAttributes.ReparsePoint) != 0 ||
            File.GetUnixFileMode(freshDataRoot) != privateMode)
            throw new UnauthorizedAccessException("The actual fresh temporary data root must already be private0700 and nonsymlink.");
        if (Directory.EnumerateFileSystemEntries(freshDataRoot).Any())
            throw new InvalidOperationException("Prepare Home only inside the SAME newly created empty temporary data root.");

        var homePath = Path.Combine(freshDataRoot, "Home");
        var home = Directory.CreateDirectory(homePath, privateMode);
        if ((home.Attributes & FileAttributes.ReparsePoint) != 0 || File.GetUnixFileMode(homePath) != privateMode)
            throw new UnauthorizedAccessException("The newly created original Home directory is not private0700 and nonsymlink.");
        // No umask, permissions on existing entries, state, actor or grant is changed.
    }
}
