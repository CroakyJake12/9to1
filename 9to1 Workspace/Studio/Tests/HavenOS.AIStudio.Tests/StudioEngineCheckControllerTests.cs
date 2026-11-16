using System.Text.Json;
using HavenOS.AIStudio;
using NineToOne.Dulche.Training;
using Xunit;

namespace HavenOS.AIStudio.Tests;

public sealed class StudioEngineCheckControllerTests
{
    private static readonly LocalTrainingAttempt Attempt = new(Guid.NewGuid(), Guid.NewGuid());
    private static readonly LocalTrainingArtifact Checkpoint = new(Attempt, Guid.NewGuid(), new string('a', 64), 100);

    private static object Id(LocalTrainingAttempt attempt) => new { run_id = attempt.RunId.ToString("D"), attempt_id = attempt.AttemptId.ToString("D") };
    private static object Artifact(LocalTrainingArtifact artifact) => new
    {
        run_id = artifact.Attempt.RunId.ToString("D"), attempt_id = artifact.Attempt.AttemptId.ToString("D"),
        artifact_id = artifact.ArtifactId.ToString("D"), sha256 = artifact.Sha256, size_bytes = artifact.SizeBytes
    };
    private static object Row(string status, bool resume = false) => new
    {
        run_id = Attempt.RunId.ToString("D"), attempt_id = Attempt.AttemptId.ToString("D"),
        job = "cpu_self_check", purpose = "software_test_only", status, reason = (string?)null,
        completed_updates = status == "passed" ? (int?)2 : null,
        artifacts = resume ? new[] { Artifact(Checkpoint) } : [], resume_checkpoint = resume ? Artifact(Checkpoint) : null
    };
    private static LocalTrainingEvent Event(string name, object payload) => LocalTrainingProtocol.ParseEvent(
        JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["schema"] = 1, ["event"] = name,
            [name == "result" ? "result" : "attempt"] = payload }));
    private static LocalTrainingResponse Result(object value) => new(Event("result", value), null);
    private static LocalTrainingResponse Ready => Result(new { ready = true, job = "cpu_self_check", purpose = "software_test_only" });
    private static LocalTrainingResponse Empty => Result(new { attempts = Array.Empty<object>(), next = (object?)null });

    [Fact]
    public async Task MissingRegistrationIsUnavailableWithoutExecution()
    {
        await using var controller = new StudioEngineCheckController(null);
        await controller.RefreshAsync();
        Assert.False(controller.State.Ready);
        Assert.False(controller.State.CanLaunch);
        Assert.Contains("not configured", controller.State.Error);
    }

    [Fact]
    public async Task LaunchProjectsObservedAcceptedProgressAndTerminal()
    {
        var client = new FakeClient((command, attempt, checkpoint, limit, progress, token) =>
        {
            if (command == LocalTrainingCommand.Status) return Task.FromResult(Ready);
            if (command == LocalTrainingCommand.List) return Task.FromResult(Empty);
            Assert.Equal(LocalTrainingCommand.Launch, command);
            progress!.Report(Event("accepted", Id(Attempt)));
            progress.Report(Event("progress", Row("running")));
            return Task.FromResult(new LocalTrainingResponse(Event("result", Row("passed", true)), Attempt));
        });
        await using var controller = new StudioEngineCheckController(client);
        await controller.RefreshAsync();
        await controller.LaunchAsync();
        Assert.Equal("passed", controller.State.Selected!.Status);
        Assert.Equal(2, controller.State.Selected.CompletedUpdates);
        Assert.Equal(Checkpoint, controller.State.Selected.RecoveryCheckpoint);
        Assert.True(controller.State.CanCheckRecovery);
        Assert.False(controller.State.RunInFlight);
        Assert.Single(controller.State.History);
    }

    [Fact]
    public async Task PauseIsSeparateCommandAndDoesNotPretendTerminalOutcome()
    {
        var finish = new TaskCompletionSource<LocalTrainingResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeClient((command, attempt, checkpoint, limit, progress, token) =>
        {
            if (command == LocalTrainingCommand.Status) return Task.FromResult(Ready);
            if (command == LocalTrainingCommand.List) return Task.FromResult(Empty);
            if (command == LocalTrainingCommand.Pause)
            {
                Assert.Equal(Attempt, attempt);
                return Task.FromResult(Result(Row("running")));
            }
            progress!.Report(Event("accepted", Id(Attempt)));
            accepted.SetResult();
            return finish.Task;
        });
        await using var controller = new StudioEngineCheckController(client);
        await controller.RefreshAsync();
        var launch = controller.LaunchAsync();
        await accepted.Task;
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.LaunchAsync());
        await controller.PauseAsync();
        Assert.True(controller.State.PauseRequested);
        Assert.Equal("running", controller.State.Selected!.Status);
        finish.SetResult(Result(Row("paused")));
        await launch;
        Assert.Equal("paused", controller.State.Selected.Status);
        Assert.False(controller.State.PauseRequested);
    }

    [Fact]
    public async Task RecoveryUsesExactReturnedArtifactAndHistoryPaginationUsesCursor()
    {
        LocalTrainingArtifact? requested = null;
        LocalTrainingAttempt? cursor = null;
        var pages = 0;
        var client = new FakeClient((command, attempt, checkpoint, limit, progress, token) =>
        {
            if (command == LocalTrainingCommand.Status) return Task.FromResult(attempt is null ? Ready : Result(Row("passed", true)));
            if (command == LocalTrainingCommand.List)
            {
                cursor = attempt;
                return Task.FromResult(Result(new { attempts = new[] { Row("passed", true) }, next = ++pages == 1 ? Id(Attempt) : null }));
            }
            requested = checkpoint;
            Assert.Equal(LocalTrainingCommand.Resume, command);
            return Task.FromResult(Result(Row("passed")));
        });
        await using var controller = new StudioEngineCheckController(client);
        await controller.RefreshAsync();
        await controller.SelectAsync(Attempt);
        await controller.LoadMoreAsync();
        Assert.Equal(Attempt, cursor);
        Assert.Null(controller.State.Next);
        await controller.CheckRecoveryAsync();
        Assert.Equal(Checkpoint, requested);
        Assert.False(controller.State.CanCheckRecovery);
    }

    [Fact]
    public async Task TransportFailurePreservesAttemptAndBlocksBlindRetry()
    {
        var client = new FakeClient((command, attempt, checkpoint, limit, progress, token) =>
        {
            if (command == LocalTrainingCommand.Status) return Task.FromResult(Ready);
            if (command == LocalTrainingCommand.List) return Task.FromResult(Empty);
            throw new LocalTrainingClientException(LocalTrainingClientFailure.TransportFailed, "failure fixture",
                acceptedAttempt: Attempt, outcomeUnknown: true);
        });
        await using var controller = new StudioEngineCheckController(client);
        await controller.RefreshAsync();
        await controller.LaunchAsync();
        Assert.True(controller.State.OutcomeUnknown);
        Assert.Equal(Attempt, controller.State.Selected!.Identity);
        Assert.False(controller.State.CanLaunch);
    }

    [Fact]
    public async Task FreshControllerReadsDurableHistoryWithoutInventingProgress()
    {
        var client = new FakeClient((command, attempt, checkpoint, limit, progress, token) => Task.FromResult(
            command == LocalTrainingCommand.Status ? Ready : Result(new { attempts = new[] { Row("interrupted") }, next = (object?)null })));
        await using var controller = new StudioEngineCheckController(client);
        await controller.RefreshAsync();
        var row = Assert.Single(controller.State.History);
        Assert.Equal("interrupted", row.Status);
        Assert.Null(row.CompletedUpdates);
        Assert.Null(row.RecoveryCheckpoint);
        Assert.Contains("software test only", StudioEngineCheckState.Title);
    }

    [Fact]
    public async Task ClosingWaitsForCooperativeCheckpointAfterPauseAcknowledgement()
    {
        var finish = new TaskCompletionSource<LocalTrainingResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken runningToken = default;
        var client = new FakeClient((command, attempt, checkpoint, limit, progress, token) =>
        {
            if (command == LocalTrainingCommand.Status) return Task.FromResult(Ready);
            if (command == LocalTrainingCommand.List) return Task.FromResult(Empty);
            if (command == LocalTrainingCommand.Pause)
            { paused.TrySetResult(); return Task.FromResult(Result(Row("running"))); }
            runningToken = token;
            progress!.Report(Event("accepted", Id(Attempt)));
            accepted.TrySetResult();
            return finish.Task.WaitAsync(token);
        });
        var controller = new StudioEngineCheckController(client);
        await controller.RefreshAsync();
        var launch = controller.LaunchAsync();
        await accepted.Task;
        var close = controller.DisposeAsync().AsTask();
        await paused.Task;
        Assert.False(close.IsCompleted);
        Assert.False(runningToken.IsCancellationRequested);
        finish.SetResult(Result(Row("paused")));
        await close;
        await launch;
        Assert.Equal("paused", controller.State.Selected!.Status);
        Assert.False(controller.State.OutcomeUnknown);
    }

    private sealed class FakeClient(Func<LocalTrainingCommand, LocalTrainingAttempt?, LocalTrainingArtifact?, int?,
        IProgress<LocalTrainingEvent>?, CancellationToken, Task<LocalTrainingResponse>> execute) : ILocalTrainingClient
    {
        public Task<LocalTrainingResponse> ExecuteAsync(LocalTrainingCommand command, LocalTrainingAttempt? attempt = null,
            LocalTrainingArtifact? checkpoint = null, int? limit = null, IProgress<LocalTrainingEvent>? progress = null,
            CancellationToken cancellationToken = default) => execute(command, attempt, checkpoint, limit, progress, cancellationToken);
    }
}
