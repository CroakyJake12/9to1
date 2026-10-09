using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Core.Tests;

public sealed class WorkspaceProcessOutcomeObservationTests
{
    [Theory]
    [InlineData("run_command", 7, false)]
    [InlineData("run_tests", 1, true)]
    public async Task Actual_process_outcome_is_retained_without_relabeling_activity(string name, int exit, bool timedOut)
    {
        var result = new ProcessResult(exit, "synthetic stdout", "synthetic stderr", TimeSpan.FromSeconds(3), timedOut);
        var tools = new Tools(Task.FromResult(result));
        var returned = await new WorkspaceToolRuntime(tools).ExecuteAsync("/synthetic", Call(name), default);
        Assert.Same(result, returned.OriginalProcessResult);
        Assert.Equal(exit, returned.OriginalProcessResult!.ExitCode);
        Assert.Equal(timedOut, returned.OriginalProcessResult.TimedOut);
        Assert.True(returned.Activity.Succeeded); // Existing activity means tool returned; Dev reads the typed exit.
        Assert.Equal(1, tools.Calls);
    }

    [Fact]
    public async Task Held_actual_process_task_is_awaited_once_before_outcome_publication()
    {
        var actual = new TaskCompletionSource<ProcessResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tools = new Tools(actual.Task);
        var returned = new WorkspaceToolRuntime(tools).ExecuteAsync("/synthetic", Call("run_command"), default);
        Assert.False(returned.IsCompleted);
        var result = new ProcessResult(0, "done", "", TimeSpan.FromMilliseconds(17), false);
        actual.SetResult(result);
        Assert.Same(result, (await returned).OriginalProcessResult);
        Assert.Equal(1, tools.Calls);
    }

    [Fact]
    public async Task Observer_failure_after_actual_process_return_keeps_known_exit()
    {
        var process = new ProcessResult(23, "done", "error", TimeSpan.FromSeconds(1), false);
        var tools = new Tools(Task.FromResult(process));
        var hub = new TerminalCommandActivityHub();
        var failure = new IOException("synthetic terminal observer fault");
        hub.ActivityPublished += (_, activity) => { if (activity.Result is not null) throw failure; };
        var returned = await new WorkspaceToolRuntime(tools, commandActivity: hub).ExecuteAsync("/synthetic", Call("run_tests"), default);
        Assert.False(returned.Activity.Succeeded);
        Assert.Same(process, returned.OriginalProcessResult);
        Assert.Equal(1, tools.Calls);
    }

    [Fact]
    public async Task Read_tool_result_cannot_acquire_a_process_observation()
    {
        var tools = new Tools(Task.FromResult(new ProcessResult(0, "", "", TimeSpan.Zero, false)));
        var call = new OllamaToolCall("read_file", new Dictionary<string, JsonElement> { ["path"] = JsonSerializer.SerializeToElement("one.txt") });
        var returned = await new WorkspaceToolRuntime(tools).ExecuteAsync("/synthetic", call, default);
        Assert.Null(returned.OriginalProcessResult);
        Assert.Equal(0, tools.Calls);
    }

    private static OllamaToolCall Call(string name) => new(name, new Dictionary<string, JsonElement>
    { ["command"] = JsonSerializer.SerializeToElement("synthetic-command"), ["timeout_seconds"] = JsonSerializer.SerializeToElement(9) });
    private sealed class Tools(Task<ProcessResult> original) : IWorkspaceToolService
    {
        internal int Calls;
        public string ResolveWorkspacePath(string root, string path) => Path.Combine(root, path);
        public Task<string> ReadTextAsync(string root, string path, CancellationToken token) => Task.FromResult("read only");
        public Task WriteTextAtomicAsync(string root, string path, string content, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> SearchFilesAsync(string root, string pattern, CancellationToken token) => throw new NotSupportedException();
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken token) { Calls++; return original; }
    }
}
