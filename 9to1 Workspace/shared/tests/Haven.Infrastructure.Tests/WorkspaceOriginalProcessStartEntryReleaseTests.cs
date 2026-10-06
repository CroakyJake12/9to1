using System.Reflection;
using System.Text.Json;
using Haven.Application;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Actual original physical process/start/cleanup controls with a synthetic
/// privately configured fence. No Home consent, installer, actor or business success is issued.</summary>
public sealed class WorkspaceOriginalProcessStartEntryReleaseTests
{
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Held_original_entry_release_keeps_same_process_and_parent_close_pending_and_retains_raw_task()
    {
        const string command = "echo $$ > child.pid; exec sleep 30";
        await using var rig = new Rig(command);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Fence.Release = () => { entered.TrySetResult(); return release.Task; };
        var actual = rig.Invocation.Tools.RunProcessAsync(rig.Request, CancellationToken.None); Task? close = null;
        try
        {
            await entered.Task.WaitAsync(Bound, CancellationToken.None);
            await WaitForRawRelease(rig.Invocation, release.Task);
            Assert.False(actual.IsCompleted); Assert.Equal(1, rig.Fence.LivePins);
            close = rig.Invocation.CloseAndDrainAsync(); Assert.Same(close, rig.Invocation.CloseAndDrainAsync());
            Assert.False(close.IsCompleted); Assert.Equal(1, rig.Fence.ReleaseCalls);
            release.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual);
            var outcome = await rig.Outcome(false);
            Assert.Null(outcome.OriginalReceiptReference); Assert.True(outcome.OutcomeUnknown);
            Assert.Equal(0, rig.Fence.LivePins); Assert.True(release.Task.IsCompletedSuccessfully);
            Assert.Contains(RawReleases(rig.Invocation), value => ReferenceEquals(value, release.Task));
            var child = Path.Combine(rig.Root, "child.pid");
            if (File.Exists(child)) AssertExited(int.Parse(File.ReadAllText(child).Trim()));
        }
        finally
        {
            release.TrySetResult();
            try { await actual; } catch { }
            if (close is not null) try { await close; } catch { }
            await rig.Outcome(false);
        }
    }

    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Faulted_OCE_first_release_preserves_raw_siblings_and_known_process_effect_without_receipt()
    {
        await using var rig = new Rig("printf actualout; exit 7");
        var first = new OperationCanceledException("Faulted original Home cleanup, no canceled Task.");
        var second = new IOException("Independent actual cleanup sibling.");
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); raw.SetException([first, second]);
        rig.Fence.Release = () => raw.Task;
        var actual = rig.Invocation.Tools.RunProcessAsync(rig.Request, CancellationToken.None);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => actual);
        Assert.True(actual.IsFaulted); Assert.True(raw.Task.IsFaulted);
        Assert.Contains(Causes(error), value => ReferenceEquals(value, first));
        Assert.Contains(Causes(error), value => ReferenceEquals(value, second));
        var outcome = await rig.Outcome(false); Assert.Null(outcome.OriginalReceiptReference); Assert.True(outcome.OutcomeUnknown);
        Assert.Contains(outcome.OriginalErrors.SelectMany(Causes), value => ReferenceEquals(value, first));
        Assert.Contains(outcome.OriginalErrors.SelectMany(Causes), value => ReferenceEquals(value, second));
        Assert.True(Assert.Single(outcome.Effects).EffectKnown); Assert.Equal(0, rig.Fence.LivePins);
        Assert.Contains(RawReleases(rig.Invocation), value => ReferenceEquals(value, raw.Task));
    }

    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Genuine_canceled_release_remains_canceled_and_cannot_issue_a_process_completion_receipt()
    {
        await using var rig = new Rig("printf actualout; exit 0");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var raw = Task.FromCanceled(canceled.Token); rig.Fence.Release = () => raw;
        var actual = rig.Invocation.Tools.RunProcessAsync(rig.Request, CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual);
        Assert.True(raw.IsCanceled); Assert.True(actual.IsCanceled);
        var outcome = await rig.Outcome(false); Assert.Null(outcome.OriginalReceiptReference); Assert.True(outcome.OutcomeUnknown);
        Assert.Equal(0, rig.Fence.LivePins); Assert.Contains(RawReleases(rig.Invocation), value => ReferenceEquals(value, raw));
    }

    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Restored_context_release_callback_cannot_join_same_invocation_and_normal_child_still_finishes()
    {
        await using var rig = new Rig("printf real-output; exit 0");
        var previous = ExecutionContext.Capture()!; Exception? denial = null; Task? wronglyReturned = null;
        var raw = Task.FromResult(true);
        rig.Fence.Release = () =>
        {
            ExecutionContext.Run(previous, _ =>
            {
                try { wronglyReturned = rig.Invocation.CloseAndDrainAsync(); }
                catch (Exception error) { denial = error; }
            }, null);
            return raw;
        };
        var result = await rig.Invocation.Tools.RunProcessAsync(rig.Request, CancellationToken.None);
        Assert.IsType<InvalidOperationException>(denial); Assert.Null(wronglyReturned);
        Assert.Equal(0, result.ExitCode); Assert.Equal("real-output", result.StandardOutput); Assert.False(result.TimedOut);
        Assert.Contains(RawReleases(rig.Invocation), value => ReferenceEquals(value, raw));
        var outcome = await rig.Outcome(true); Assert.NotNull(outcome.OriginalReceiptReference); Assert.Empty(outcome.OriginalErrors);
        Assert.Equal(1, rig.Fence.ReleaseCalls); Assert.Equal(0, rig.Fence.LivePins);
    }

    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Finite_start_refusal_still_joins_exact_release_and_attempt_pin_before_original_failure_returns()
    {
        await using var rig = new Rig("printf must-not-start");
        var exact = new IOException("Actual original final fence refused before native Start.");
        var raw = Task.FromResult(true); rig.Fence.StartRefusal = exact; rig.Fence.Release = () => raw;
        var actual = rig.Invocation.Tools.RunProcessAsync(rig.Request, CancellationToken.None);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => actual);
        Assert.Contains(Causes(error), value => ReferenceEquals(value, exact));
        Assert.Equal(0, rig.Fence.NativeStarts); Assert.Equal(0, rig.Fence.LivePins); Assert.Equal(1, rig.Fence.ReleaseCalls);
        Assert.Contains(RawReleases(rig.Invocation), value => ReferenceEquals(value, raw));
        var outcome = await rig.Outcome(false); Assert.True(outcome.KnownNoEffect); Assert.Null(outcome.OriginalReceiptReference);
    }

    [Fact]
    public void Optional_release_support_requires_same_actual_configured_fence()
    {
        var call = new OllamaToolCall("run_command", new Dictionary<string, JsonElement>());
        var original = new Fence("/unopened-original-root", call); var source = new WorkspaceToolService(new Issuer(original));
        source.DemandOriginalProcessStartEntryReleaseSupport(original);
        Assert.Throws<UnauthorizedAccessException>(() => source.DemandOriginalProcessStartEntryReleaseSupport(new Fence(original.CanonicalWorkspaceRoot, call)));
        Assert.Throws<UnauthorizedAccessException>(() => new WorkspaceToolService().DemandOriginalProcessStartEntryReleaseSupport(original));
        Assert.Equal(0, original.NativeStarts); Assert.Equal(0, original.ReleaseCalls);
    }

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);
    private static Task[] RawReleases(IWorkspaceOriginalInvocation invocation)
    {
        var type = invocation.GetType(); var gate = type.GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(invocation)!;
        lock (gate) return ((List<Task>)type.GetField("_originalProcessStartEntryReleases", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(invocation)!).ToArray();
    }
    private static async Task WaitForRawRelease(IWorkspaceOriginalInvocation invocation, Task actual)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!RawReleases(invocation).Any(value => ReferenceEquals(value, actual)))
        { Assert.True(timer.Elapsed < Bound); await Task.Delay(1, CancellationToken.None); }
    }
    private static IEnumerable<Exception> Causes(Exception error)
    { yield return error; if (error is AggregateException group) foreach (var value in group.InnerExceptions) foreach (var cause in Causes(value)) yield return cause; }
    private static void AssertExited(int pid)
    { try { using var process = System.Diagnostics.Process.GetProcessById(pid); Assert.True(process.HasExited); } catch (ArgumentException) { } }

    private sealed class Rig : IAsyncDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "astra-original-start-release-" + Guid.NewGuid().ToString("N"));
        internal Fence Fence { get; }
        internal WorkspaceToolService Source { get; }
        internal IWorkspaceOriginalInvocation Invocation { get; }
        internal ProcessRequest Request { get; }
        private bool _closeObserved;
        internal Rig(string command)
        {
            Directory.CreateDirectory(Root); var call = new OllamaToolCall("run_command", new Dictionary<string, JsonElement>
            { ["command"] = JsonSerializer.SerializeToElement(command), ["timeout_seconds"] = JsonSerializer.SerializeToElement(30) });
            Fence = new(Root, call); Source = new(new Issuer(Fence)); Request = WorkspaceToolProcessRequestFactory.CreateOriginal(Root, command, 30);
            try { Invocation = Source.AcquireOriginalInvocation(Fence); }
            catch { Directory.Delete(Root, true); throw; }
        }
        internal async Task<WorkspaceToolPhysicalOutcome> Outcome(bool completed)
        { var result = await Invocation.CompleteOriginalAsync(completed, CancellationToken.None); _closeObserved = true; return result; }
        public async ValueTask DisposeAsync()
        {
            var errors = new List<Exception>();
            try { await Invocation.CloseAndDrainAsync(); } catch (Exception error) { if (!_closeObserved) errors.Add(error); }
            try { Directory.Delete(Root, true); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException("Original process-start fixture cleanup failed.", errors);
        }
    }
    private sealed class Issuer(Fence actual) : IWorkspaceToolFinalFenceAuthority
    { public bool IsIssuedOriginal(IWorkspaceToolFinalFence same) => ReferenceEquals(actual, same); }
    private sealed class Fence(string root, OllamaToolCall call) : IWorkspaceToolFinalFence, IWorkspaceOriginalProcessStartEntryReleaseFence
    {
        public TaskRunAttemptAdmission OriginalAttempt => throw new InvalidOperationException("Synthetic physical scope, no original actor or Home consent.");
        public Guid ActionId { get; } = Guid.NewGuid(); public string CanonicalWorkspaceRoot => root; public OllamaToolCall OriginalCall => call;
        internal int NativeStarts, LivePins, ReleaseCalls; internal Exception? StartRefusal; internal Func<Task>? Release;
        public ValueTask<IAsyncDisposable?> AcquireOriginalCommitPinAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); LivePins++; return ValueTask.FromResult<IAsyncDisposable?>(new Pin(this)); }
        public void DemandOriginalEffect(string actualRoot, WorkspaceToolEffectKind kind, string target, string digest)
        { Assert.Equal(root, actualRoot); Assert.Equal(root, target); Assert.Equal(WorkspaceToolEffectKind.ProcessStart, kind); }
        public T RunOriginalEffect<T>(string actualRoot, WorkspaceToolEffectKind kind, string target, string digest, Func<T> body)
        { DemandOriginalEffect(actualRoot, kind, target, digest); Assert.Equal(1, LivePins); if (StartRefusal is { } error) throw error; NativeStarts++; return body(); }
        public void DemandExternalOriginalProcessStartEntryJoin() { }
        public Task ReleaseOriginalProcessStartEntryAsync(string actualRoot, string target, string digest)
        { Assert.Equal(root, actualRoot); Assert.Equal(root, target); Assert.Equal(64, digest.Length); ReleaseCalls++; return Release?.Invoke() ?? Task.CompletedTask; }
        private sealed class Pin(Fence owner) : IAsyncDisposable
        { public ValueTask DisposeAsync() { owner.LivePins--; return ValueTask.CompletedTask; } }
    }
}
