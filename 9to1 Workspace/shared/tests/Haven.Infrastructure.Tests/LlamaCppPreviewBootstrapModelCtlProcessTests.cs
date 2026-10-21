using System.Diagnostics;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>Owned canonical modelctl subprocess settlement regressions. These drive the REAL
/// <see cref="LlamaCppPreviewBootstrap.RunModelCtlAsync"/> (internal, typed ArgumentList, no shell)
/// with a tiny fixture executable and assert full ownership settlement: real exit code, both
/// redirected stream captures, and prompt kill+join under cancellation.</summary>
public sealed class LlamaCppPreviewBootstrapModelCtlProcessTests
{
    [Fact]
    public async Task ModelCtl_subprocess_settles_stdout_stderr_and_exit_code()
    {
        if (!OperatingSystem.IsLinux()) return;
        var script = Path.Combine(Path.GetTempPath(), "haven-modelctl-fixture-" + Guid.NewGuid().ToString("N") + ".sh");
        await File.WriteAllTextAsync(script,
            "#!/bin/sh\necho out-stdout-deadbeef\nprintf 'err-stderr-cafebabe\\n' >&2\nexit 7\n");
        SetExecutable(script);
        try
        {
            var result = await LlamaCppPreviewBootstrap.RunModelCtlAsync(NewRequest(script), CancellationToken.None);
            Assert.Equal(7, result.ExitCode);
            Assert.Contains("out-stdout-deadbeef", result.StdOut);
            Assert.Contains("err-stderr-cafebabe", result.StdErr);
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Fact]
    public async Task ModelCtl_subprocess_cancellation_kills_and_settles()
    {
        if (!OperatingSystem.IsLinux()) return;
        var script = Path.Combine(Path.GetTempPath(), "haven-modelctl-sleep-" + Guid.NewGuid().ToString("N") + ".sh");
        await File.WriteAllTextAsync(script,
            "#!/bin/sh\nexec sleep 300\n"); // single long-lived process; kill must reap it promptly
        SetExecutable(script);
        try
        {
            using var cts = new CancellationTokenSource(200);
            var stopwatch = Stopwatch.StartNew();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => LlamaCppPreviewBootstrap.RunModelCtlAsync(NewRequest(script), cts.Token));
            stopwatch.Stop();
            // Owned settlement must join the killed child and drain both pipes without hanging.
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                "cancelled modelctl subprocess did not settle within the bound");
        }
        finally
        {
            File.Delete(script);
        }
    }

    private static ModelCtlRequest NewRequest(string pythonPath) =>
        new(pythonPath, "/ignored/modelctl.py", "/modelroot", "/runtimedir",
            "/seed/seed.gguf", "qwen3-06b-q6k", "Qwen3 0.6B Q6_K", "Apache-2.0",
            "https://huggingface.co/example", "chat", "allowed");

    private static void SetExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        var mode = File.GetUnixFileMode(path)
            | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(path, mode);
    }
}
