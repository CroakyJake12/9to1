using System.Diagnostics;
using System.Text.Json;
using Haven.Application;
using Haven.Infrastructure;
namespace Haven.Infrastructure.Tests;

public sealed class LinuxWorkspaceProcessFactAttribute : FactAttribute
{
    public LinuxWorkspaceProcessFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Actual /bin/sh workspace smoke requires Linux."; }
}

/// <summary>Actual ordinary Runtime -> maintained physical process owner. No canonical grant/receipt is inferred.</summary>
public sealed class WorkspacePosixProcessSmokeTests
{
    [LinuxWorkspaceProcessFact]
    public async Task Actual_posix_runtime_preserves_literal_quotes_streams_and_nonzero_exit()
    {
        await WithActualWorkspaceAsync(async (root, stop, originals) =>
        {
            var runtime = new WorkspaceToolRuntime(new WorkspaceToolService());
            var original = runtime.ExecuteAsync(root, Call("printf '%s\\n' 'space and $literal' 'double \"quote\"'; printf '%s\\n' 'stderr exact' >&2; exit 7", 9), stop.Token);
            originals.Add(original); var returned = await original;
            Assert.NotNull(returned.OriginalProcessResult); var process = returned.OriginalProcessResult!;
            Assert.Equal(7, process.ExitCode); Assert.False(process.TimedOut);
            Assert.Equal("space and $literal\ndouble \"quote\"\n", process.StandardOutput);
            Assert.Equal("stderr exact\n", process.StandardError);
            Assert.True(returned.Activity.Succeeded); // Legacy activity says tool returned; typed exit remains 7.
        });
    }
    [LinuxWorkspaceProcessFact]
    public async Task Actual_posix_timeout_joins_process_tree_and_returns_typed_timeout()
    {
        await WithActualWorkspaceAsync(async (root, stop, originals) =>
        {
            var runtime = new WorkspaceToolRuntime(new WorkspaceToolService());
            var original = runtime.ExecuteAsync(root, Call("printf '%s\\n' 'before timeout'; sleep 30", 1), stop.Token);
            originals.Add(original); var returned = await original;
            Assert.NotNull(returned.OriginalProcessResult); Assert.True(returned.OriginalProcessResult!.TimedOut);
            Assert.Contains("before timeout", returned.OriginalProcessResult.StandardOutput);
        });
    }
    [LinuxWorkspaceProcessFact]
    public async Task Actual_posix_caller_cancel_after_real_start_joins_before_return()
    {
        await WithActualWorkspaceAsync(async (root, stop, originals) =>
        {
            var runtime = new WorkspaceToolRuntime(new WorkspaceToolService());
            var original = runtime.ExecuteAsync(root, Call("printf '%s' \"$$\" > actual-start.pid; exec sleep 30", 9), stop.Token);
            originals.Add(original); var watch = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(root, "actual-start.pid")))
            {
                if (original.IsCompleted) { await original; throw new InvalidOperationException("The real process exited without start proof."); }
                if (watch.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("The real shell did not acknowledge start.");
                await Task.Delay(10);
            }
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original);
            Assert.True(original.IsCanceled); // Truly canceled original is distinct from Faulted(OCE).
        });
    }
    private static OllamaToolCall Call(string command, int timeout) => new("run_command", new Dictionary<string, JsonElement>
    { ["command"] = JsonSerializer.SerializeToElement(command), ["timeout_seconds"] = JsonSerializer.SerializeToElement(timeout) });
    private static async Task WithActualWorkspaceAsync(Func<string,CancellationTokenSource,List<Task>,Task> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "haven-posix-original-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var stop = new CancellationTokenSource(); var originals = new List<Task>(); var errors = new List<Exception>();
        try { await body(root, stop, originals); }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            try { stop.Cancel(); } catch (Exception error) { errors.Add(error); }
            foreach (var original in originals)
                try { await original; }
                catch (Exception error) { if (!original.IsCanceled) errors.Add((Exception?)original.Exception ?? error); }
            try { stop.Dispose(); } catch (Exception error) { errors.Add(error); }
            try { Directory.Delete(root, true); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual workspace smoke, independent original joins and cleanup.", errors);
    }
}
