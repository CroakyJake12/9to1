using System.Reflection;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Desktop.Tests;

[Trait("Platform", "Windows64")]
public sealed class WindowsNativePersonalTaskStoreSetupCommandTests
{
    [Theory]
    [InlineData("--help", 0)] [InlineData("--parent", 2)] [InlineData("extra", 2)]
    public void Early_informational_or_malformed_command_exits_before_AppPaths_Avalonia_or_default_logger(string argument, int exitCode)
    {
        using var fixture = new CommandFixture(requireWindows: false);
        var stdout = Console.Out; var stderr = Console.Error;
        using var output = new StringWriter(); using var error = new StringWriter();
        var defaultLogs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Haven", "Logs");
        var existed = Directory.Exists(defaultLogs);
        var log = Path.Combine(defaultLogs, "startup-bootstrap-failure.log");
        var previousLog = File.Exists(log) ? File.ReadAllBytes(log) : null;
        try
        {
            Console.SetOut(output); Console.SetError(error);
            Program.Main(["--create-native-private-store", argument]);
            Assert.Equal(exitCode, Environment.ExitCode);
            Assert.False(WindowsNativePersonalTaskStoreSetupCommand.HasActualSetupObservation);
            Assert.False(Directory.Exists(fixture.Path));
            Assert.Equal(existed, Directory.Exists(defaultLogs));
            Assert.Equal(previousLog is not null, File.Exists(log));
            if (previousLog is not null) Assert.True(previousLog.SequenceEqual(File.ReadAllBytes(log)), "The existing generic bootstrap log must remain unchanged.");
            Assert.True(argument == "--help" ? output.ToString().Contains("Existing stores always refuse", StringComparison.Ordinal) : error.ToString().Contains("refused", StringComparison.Ordinal));
        }
        finally { Console.SetOut(stdout); Console.SetError(stderr); }
    }

    [Fact]
    public void Unsupported_setup_option_suffix_refuses_early_and_preserves_ordinary_and_local_console_dispatch()
    {
        using var fixture = new CommandFixture(requireWindows: false);
        Assert.True(WindowsNativePersonalTaskStoreSetupCommand.IsInvocation(["--create-native-private-store=foreign"]));
        Assert.False(WindowsNativePersonalTaskStoreSetupCommand.IsInvocation([]));
        Assert.True(WindowsNativePersonalTaskStoreSetupCommand.IsInvocation(["--local-task-console", "--create-native-private-store"]));
        Assert.True(WindowsNativePersonalTaskStoreSetupCommand.IsInvocation(["ordinary", "--create-native-private-store"]));
        Assert.False(WindowsNativePersonalTaskStoreSetupCommand.IsInvocation(["--local-task-console", "help"]));
        Assert.False(WindowsNativePersonalTaskStoreSetupCommand.IsInvocation(["ordinary"]));
        using var output = new StringWriter(); using var error = new StringWriter();
        Assert.False(WindowsNativePersonalTaskStoreSetupCommand.Run(["--create-native-private-store=foreign"], output, error));
        Assert.False(WindowsNativePersonalTaskStoreSetupCommand.Run(["ordinary", "--create-native-private-store"], output, error));
        Assert.False(WindowsNativePersonalTaskStoreSetupCommand.Run(["--local-task-console", "--create-native-private-store"], output, error));
        Assert.Equal(2, Environment.ExitCode); Assert.False(Directory.Exists(fixture.Path));
    }

    [Fact]
    public void Actual_success_continues_same_process_with_actual_created_mapping_and_no_recovery_selector_mutation()
    {
        using var fixture = new CommandFixture();
        var recoverySelector = Environment.GetEnvironmentVariable("HAVEN_NATIVE_PERSONAL_TASK_COLD_RECOVERY");
        using var output = new StringWriter(); using var error = new StringWriter();
        Assert.True(WindowsNativePersonalTaskStoreSetupCommand.Run(["--create-native-private-store"], output, error));
        Assert.True(WindowsNativePersonalTaskStoreSetupCommand.HasActualSetupObservation);
        Assert.Equal(0, new FileInfo(Path.Combine(fixture.Path, "haven.db")).Length);
        WindowsNativePersonalTaskStoreSetupCommand.DemandCurrentConfiguredPath();
        var actualPaths = new AppPaths();
        WindowsNativePersonalTaskStoreSetupCommand.DemandSamePaths(actualPaths);
        Assert.Equal(fixture.Path, Path.TrimEndingDirectorySeparator(Path.GetFullPath(actualPaths.DataDirectory)), ignoreCase: true);
        Assert.Equal(Path.Combine(fixture.Path, "haven.db"), actualPaths.DatabasePath, ignoreCase: true);
        Assert.False(File.Exists(Path.Combine(fixture.Path, ".task-recovery-auth.v1")));
        Assert.Equal(recoverySelector, Environment.GetEnvironmentVariable("HAVEN_NATIVE_PERSONAL_TASK_COLD_RECOVERY"));
        Assert.Contains("recovery remains unverified", output.ToString(), StringComparison.Ordinal);
        Assert.Equal("", error.ToString());
        // Run returning true requests genuine ordinary startup; this headless owning
        // control does not claim that Avalonia rendered or Home/recovery is Ready.
    }

    [Fact]
    public void Actual_success_then_environment_drift_denies_before_AppPaths_can_create_foreign_root()
    {
        using var fixture = new CommandFixture();
        using var output = new StringWriter(); using var error = new StringWriter();
        Assert.True(WindowsNativePersonalTaskStoreSetupCommand.Run(["--create-native-private-store"], output, error));
        Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", fixture.Foreign);
        Assert.Throws<UnauthorizedAccessException>(WindowsNativePersonalTaskStoreSetupCommand.DemandCurrentConfiguredPath);
        Assert.False(Directory.Exists(fixture.Foreign));
        Assert.True(File.Exists(Path.Combine(fixture.Path, "haven.db")));
    }

    [Fact]
    public void Actual_foreign_AppPaths_object_denies_before_Home_provider_or_SQLite_registration()
    {
        using var fixture = new CommandFixture();
        // This deliberately constructs the test-owned foreign object BEFORE setup;
        // production's pre-construction guard never constructs a substitute object.
        Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", fixture.Foreign);
        var foreignPaths = new AppPaths();
        Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", fixture.Path);
        using var output = new StringWriter(); using var error = new StringWriter();
        Assert.True(WindowsNativePersonalTaskStoreSetupCommand.Run(["--create-native-private-store"], output, error));
        Assert.Throws<UnauthorizedAccessException>(() => WindowsNativePersonalTaskStoreSetupCommand.DemandSamePaths(foreignPaths));
        Assert.False(File.Exists(foreignPaths.DatabasePath));
        Assert.True(File.Exists(Path.Combine(fixture.Path, "haven.db")));
    }

    [Fact]
    public void Actual_existing_chosen_root_refuses_command_without_default_log_or_adoption()
    {
        using var fixture = new CommandFixture();
        Directory.CreateDirectory(fixture.Path);
        var existing = Path.Combine(fixture.Path, "existing-data"); File.WriteAllBytes(existing, [29, 31]);
        var defaultLogs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Haven", "Logs");
        var existed = Directory.Exists(defaultLogs);
        using var output = new StringWriter(); using var error = new StringWriter();
        Assert.False(WindowsNativePersonalTaskStoreSetupCommand.Run(["--create-native-private-store"], output, error));
        Assert.Equal(1, Environment.ExitCode); Assert.False(WindowsNativePersonalTaskStoreSetupCommand.HasActualSetupObservation);
        Assert.Equal(new byte[] { 29, 31 }, File.ReadAllBytes(existing));
        Assert.False(File.Exists(Path.Combine(fixture.Path, "haven.db")));
        Assert.False(File.Exists(Path.Combine(fixture.Path, ".task-recovery-auth.v1")));
        Assert.Equal(existed, Directory.Exists(defaultLogs));
        Assert.Contains("refused or is unknown", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Ordinary_startup_without_actual_setup_source_keeps_receiving_guards_inert()
    {
        using var fixture = new CommandFixture(requireWindows: false);
        WindowsNativePersonalTaskStoreSetupCommand.DemandCurrentConfiguredPath();
        Assert.False(Directory.Exists(fixture.Path));
        Assert.False(WindowsNativePersonalTaskStoreSetupCommand.HasActualSetupObservation);
    }

    private sealed class CommandFixture : IDisposable
    {
        private readonly string? _previous = Environment.GetEnvironmentVariable("HAVEN_DATA_DIR");
        private readonly int _exitCode = Environment.ExitCode;
        internal string Path { get; } = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "astra-store-command51-" + Guid.NewGuid().ToString("N"))));
        internal string Foreign => Path + "-foreign";
        internal CommandFixture(bool requireWindows = true)
        {
            if (requireWindows) Assert.True(OperatingSystem.IsWindows() && Environment.Is64BitProcess,
                "This owning control requires actual Windows64 setup; no platform skip or manufactured native success qualifies.");
            ResetNegativeTestState(); Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", Path);
        }
        public void Dispose()
        {
            Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", _previous); Environment.ExitCode = _exitCode;
            ResetNegativeTestState();
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
            if (Directory.Exists(Foreign)) Directory.Delete(Foreign, recursive: true);
        }
        private static void ResetNegativeTestState()
        {
            // Test cleanup only clears original state. No fabricated positive source
            // path or successful setup delegate is ever installed.
            var type = typeof(WindowsNativePersonalTaskStoreSetupCommand);
            type.GetField("_actualCreatedPath", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, null);
            type.GetField("_attempted", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, 0);
        }
    }
}
