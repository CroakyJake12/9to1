using System.Diagnostics;
using NineToOne.Os.Shell.Authority;

namespace NineToOne.Os.Shell.Tests;

public sealed class LinuxRuntimeEvidenceTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("DOTNET_STARTUP_HOOKS", false)]
    [InlineData("DOTNET_ROOT", false)]
    [InlineData("CORECLR_PROFILER_PATH", false)]
    [InlineData("LD_PRELOAD", false)]
    public async Task ActualChildProcessEnvironmentRejectsRuntimeAndLoaderOverrides(string? name, bool expected)
    {
        if (!OperatingSystem.IsLinux()) return;
        var start = new ProcessStartInfo("/usr/bin/python3") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-c"); start.ArgumentList.Add("import sys,time;print('ready',flush=True);time.sleep(30)");
        // Fixture process only: retain HOME and normal OS environment; remove inherited SDK/runtime tuning.
        foreach (var key in start.Environment.Keys.ToArray())
            if (key.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("COMPLUS_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("COR_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("LD_", StringComparison.Ordinal)) start.Environment.Remove(key);
        start.Environment["DOTNET_EnableDiagnostics"] = "0";
        if (name is not null) start.Environment[name] = "/nonexistent/fixture-loader.so";
        using var process = Process.Start(start)!;
        try
        {
            Assert.Equal("ready", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.Equal(expected, await LinuxRuntimeEvidence.HasSafeEnvironmentAsync(process.Id, CancellationToken.None));
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
    }
}
