using Haven.Infrastructure;

namespace Haven.Desktop.Services;

// Owns only an explicit create-new invocation and its nonsecret SAME-process path
// observation. Every receiving check can deny; none supplies store/Task readiness.
internal static class WindowsNativePersonalTaskStoreSetupCommand
{
    private const string Option = "--create-native-private-store";
    private static string? _actualCreatedPath;
    private static int _attempted;

    internal static bool IsInvocation(string[] args) => args.Any(argument =>
        argument.StartsWith(Option, StringComparison.Ordinal));
    internal static bool HasActualSetupObservation => Volatile.Read(ref _actualCreatedPath) is not null;

    // True means continue ordinary Desktop startup with the option removed in THIS
    // process. Help/refusal exits here before AppPaths/provider/Avalonia/log setup.
    internal static bool Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length == 2 && args[0] == Option && args[1] == "--help")
            {
                output.WriteLine("--create-native-private-store");
                output.WriteLine("Create a NEW Windows64 personal store at the CURRENT HAVEN_DATA_DIR/default Haven path, then continue Desktop startup in this process. Existing stores always refuse. No alternative path is accepted; recovery remains unverified.");
                Environment.ExitCode = 0; return false;
            }
            if (args.Length != 1 || args[0] != Option)
            {
                error.WriteLine("Private-store setup refused: use exactly --create-native-private-store, or append --help for information.");
                Environment.ExitCode = 2; return false;
            }
            if (Interlocked.CompareExchange(ref _attempted, 1, 0) != 0)
                throw new InvalidOperationException("Create-new setup can only be attempted once in this Desktop process.");
            var actual = WindowsNativePersonalTaskStoreSetup.CreateNewConfiguredPersonalStore();
            Volatile.Write(ref _actualCreatedPath, actual);
            DemandCurrentConfiguredPath();
            output.WriteLine("Created the NEW configured Windows personal store. Continuing Desktop startup in this process; recovery remains unverified.");
            Environment.ExitCode = 0;
            return true;
        }
        catch (Exception actual)
        {
            ReportFailure(actual, error); Environment.ExitCode = 1; return false;
        }
    }

    internal static void ReportFailure(Exception actual, TextWriter error)
    {
        // Console only. A refusal or observed partial/unknown outcome never enters
        // Program's generic default-Haven bootstrap logger or creates Logs there.
        try { error.WriteLine("NEW Windows private-store setup/startup refused or is unknown. Inspect retained partial state before another invocation."); error.WriteLine(actual); }
        catch { /* Reporting cannot replace the original refusal. */ }
    }

    internal static void DemandCurrentConfiguredPath()
    {
        var source = Volatile.Read(ref _actualCreatedPath);
        if (source is null) return;
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess ||
            !SamePath(ConfiguredDataDirectory(), source))
            throw new UnauthorizedAccessException("The CURRENT configured data mapping differs from the actual SAME-process created store; AppPaths construction refuses.");
    }

    internal static void DemandSamePaths(AppPaths actualPaths)
    {
        var source = Volatile.Read(ref _actualCreatedPath);
        if (source is null) return;
        DemandCurrentConfiguredPath();
        if (!SamePath(actualPaths.DataDirectory, source) || !SamePath(actualPaths.DatabasePath, Path.Combine(source, "haven.db")))
            throw new UnauthorizedAccessException("The SAME actual AppPaths data/database mapping differs from the actual created store; Home/provider/SQLite registration refuses.");
    }

    private static string ConfiguredDataDirectory()
    {
        // Preserve the existing AppPaths selector/default rule exactly; no mutation
        // of HAVEN_DATA_DIR or recovery selectors and no AppPaths construction.
        var custom = Environment.GetEnvironmentVariable("HAVEN_DATA_DIR");
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrWhiteSpace(custom) ? Path.Combine(appData, "Haven") : Path.GetFullPath(custom);
    }
    private static bool SamePath(string actual, string expected) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)), StringComparison.OrdinalIgnoreCase);
}
