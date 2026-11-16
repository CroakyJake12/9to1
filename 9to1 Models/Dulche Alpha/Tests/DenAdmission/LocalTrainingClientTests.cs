using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NineToOne.Dulche.Training;

namespace Dulche.Den.Tests;

public sealed class LocalTrainingClientTests
{
    private static readonly LocalTrainingAttempt Attempt = new(
        Guid.Parse("a907912c-ec5d-4200-87c8-d5f03d472216"), Guid.Parse("46f10d4c-91a1-44ed-91da-8c39384b55d2"));
    private static readonly LocalTrainingAttempt Later = Attempt with { AttemptId = Guid.Parse("bc39cdf4-bac0-4743-902d-7bd7570a188c") };
    private const string Readiness = """{"schema":1,"event":"result","result":{"ready":true,"job":"cpu_self_check","purpose":"software_test_only"}}""";
    private const string Error = """{"schema":1,"event":"error","code":"local_worker_unavailable","message":"The local worker could not complete this command."}""";

    [Fact]
    public async Task ActualLaunchAndResumeFramesCompleteInOrderAndStreamProgress()
    {
        var events = new RecordedProgress();
        var launch = new LocalTrainingClient.Conversation(LocalTrainingCommand.Launch, progress: events);
        using (var input = Fixture("cpu-self-check-v1.jsonl"))
            await launch.ReadOutputAsync(input);
        var baseline = launch.Complete(0);
        Assert.NotNull(baseline.AcceptedAttempt);
        Assert.Equal(LocalTrainingEventKind.Accepted, events.Values[0].Kind);
        Assert.Contains(events.Values, frame => frame.Kind == LocalTrainingEventKind.Progress);
        Assert.DoesNotContain(events.Values, frame => frame.Kind == LocalTrainingEventKind.Result);
        Assert.Equal(2, baseline.Terminal.Payload.GetProperty("completed_updates").GetInt32());
        Assert.False(baseline.OutcomeUnknown);

        var checkpoint = LocalTrainingProtocol.ReadArtifact(baseline.Terminal.Payload.GetProperty("resume_checkpoint"));
        var resume = new LocalTrainingClient.Conversation(LocalTrainingCommand.Resume, checkpoint: checkpoint);
        using (var input = Fixture("cpu-self-check-resume-v1.jsonl"))
            await resume.ReadOutputAsync(input);
        var continued = resume.Complete(0);
        Assert.NotNull(continued.AcceptedAttempt);
        Assert.Equal(checkpoint.Attempt.RunId, continued.AcceptedAttempt.RunId);
        Assert.NotEqual(checkpoint.Attempt.AttemptId, continued.AcceptedAttempt.AttemptId);
        Assert.Equal(JsonValueKind.Null, continued.Terminal.Payload.GetProperty("resume_checkpoint").ValueKind);
    }

    [Fact]
    public async Task AcknowledgementAndProgressCannotChangeAttemptOrArriveOutOfOrder()
    {
        var checkpoint = new LocalTrainingArtifact(Attempt, Guid.NewGuid(), new string('a', 64), 21);
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(
            new(LocalTrainingCommand.Launch), AttemptEvent("progress", Attempt, "running", 1)));
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(
            new(LocalTrainingCommand.Launch), AttemptEvent("result", Attempt, "passed", 2)));
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(
            new(LocalTrainingCommand.Launch), Accepted(Attempt), Accepted(Attempt)));
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(
            new(LocalTrainingCommand.Resume, checkpoint: checkpoint), Accepted(Attempt)));
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(
            new(LocalTrainingCommand.Launch), Accepted(Attempt), AttemptEvent("progress", Later, "running", 1)));
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(
            new(LocalTrainingCommand.Launch), Accepted(Attempt), AttemptEvent("progress", Attempt, "running", 1),
            AttemptEvent("progress", Attempt, "running", 1)));
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(
            new(LocalTrainingCommand.Launch), Accepted(Attempt), AttemptEvent("result", Later, "passed", 2)));
    }

    [Fact]
    public async Task TerminalResponseMustMatchCommandExitAndEndTheStream()
    {
        var completed = new LocalTrainingClient.Conversation(LocalTrainingCommand.Status);
        await Read(completed, Readiness);
        Assert.Equal(LocalTrainingEventKind.Result, completed.Complete(0).Terminal.Kind);
        Assert.Throws<InvalidDataException>(() => completed.Complete(1));
        Assert.Throws<InvalidDataException>(() => new LocalTrainingClient.Conversation(LocalTrainingCommand.Status).Complete(0));
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(new(LocalTrainingCommand.Status), Readiness, Error));
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(new(LocalTrainingCommand.Pause, Attempt), Readiness));
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(
            new(LocalTrainingCommand.Launch), Accepted(Attempt), AttemptEvent("result", Attempt, "running", 1)));

        var errorBeforeDispatch = new LocalTrainingClient.Conversation(LocalTrainingCommand.Launch);
        await Read(errorBeforeDispatch, Error);
        Assert.False(errorBeforeDispatch.Complete(1).OutcomeUnknown);
        var errorAfterDispatch = new LocalTrainingClient.Conversation(LocalTrainingCommand.Launch);
        await Read(errorAfterDispatch, Accepted(Attempt), Error);
        Assert.True(errorAfterDispatch.Complete(1).OutcomeUnknown);
        Assert.Equal(Attempt, errorAfterDispatch.Complete(1).AcceptedAttempt);
        var pauseError = new LocalTrainingClient.Conversation(LocalTrainingCommand.Pause, Attempt);
        await Read(pauseError, Error);
        Assert.True(pauseError.Complete(1).OutcomeUnknown);
    }

    [Fact]
    public async Task ListedAttemptsMustHonorPageOrderContinuationAndRequestedLimit()
    {
        var listed = new LocalTrainingClient.Conversation(LocalTrainingCommand.List, Attempt, limit: 1);
        await Read(listed, ListFrame([Later], Later));
        Assert.Equal(LocalTrainingEventKind.Result, listed.Complete(0).Terminal.Kind);
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(
            new(LocalTrainingCommand.List, Attempt), ListFrame([Attempt], Attempt)));
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(
            new(LocalTrainingCommand.List), ListFrame([Later, Attempt], null)));
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(
            new(LocalTrainingCommand.List), ListFrame([Attempt, Later], Attempt)));
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(
            new(LocalTrainingCommand.List, limit: 1), ListFrame([Attempt, Later], null)));
    }

    [Fact]
    public async Task UpdateCountsRemainBoundedAndCannotRegressAfterProgress()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(
            new(LocalTrainingCommand.Launch), Accepted(Attempt), AttemptEvent("progress", Attempt, "running", 3)));
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(
            new(LocalTrainingCommand.Launch), Accepted(Attempt), AttemptEvent("progress", Attempt, "running", 2),
            AttemptEvent("result", Attempt, "paused", 1)));
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(
            new(LocalTrainingCommand.Status, Attempt), AttemptEvent("result", Attempt, "failed", 3)));
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(
            new(LocalTrainingCommand.List), ListFrame([Attempt], null, updates: 3)));

        var interrupted = new LocalTrainingClient.Conversation(LocalTrainingCommand.Launch);
        await Read(interrupted, Accepted(Attempt), AttemptEvent("progress", Attempt, "running", 2),
            AttemptEvent("result", Attempt, "interrupted", null));
        Assert.Equal(JsonValueKind.Null, interrupted.Complete(0).Terminal.Payload.GetProperty("completed_updates").ValueKind);
    }

    [Fact]
    public async Task ByteFramingAndDiagnosticsAreBoundedWithoutUnboundedLineReads()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(
            new(LocalTrainingCommand.Status), new string('x', LocalTrainingProtocol.MaximumLineBytes + 2)));
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(new(LocalTrainingCommand.Status), "{"));
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(
            new(LocalTrainingCommand.Status), """{"schema":{},"event":"result","result":{}}"""));
        using var partial = new ChunkedStream(Encoding.UTF8.GetBytes(Readiness));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new LocalTrainingClient.Conversation(LocalTrainingCommand.Status).ReadOutputAsync(partial));
        using var diagnostics = new MemoryStream(new byte[64 * 1024 + 1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => LocalTrainingClient.DrainErrorsAsync(diagnostics, CancellationToken.None));
    }

    [Fact]
    public async Task RegistrationPinsAllFilesAndRefusesAlternateInterpreterAndStartupHooks()
    {
        using var fixture = new RegistrationFixture();
        var valid = fixture.Registration;
        using (var installation = await LocalTrainingClient.PinnedInstallation.OpenAsync(valid, CancellationToken.None))
            Assert.Equal(valid.PythonPath, installation.PythonPath);

        foreach (var invalid in new[]
        {
            valid with { PythonPath = "python.exe" },
            valid with { CliArchiveSha256 = new string('0', 64) },
            valid with { OperatorConfigSha256 = new string('0', 64) },
            valid with { PythonSha256 = new string('0', 64) }
        })
        {
            var error = await Assert.ThrowsAsync<LocalTrainingClientException>(() =>
                new LocalTrainingClient(invalid).ExecuteAsync(LocalTrainingCommand.Status));
            Assert.Equal(LocalTrainingClientFailure.RegistrationInvalid, error.Failure);
            Assert.False(error.OutcomeUnknown);
        }

        var alternate = fixture.RewriteOperator(Path.Combine(fixture.Root, "other.exe"));
        var mismatch = await Assert.ThrowsAsync<LocalTrainingClientException>(() =>
            new LocalTrainingClient(alternate).ExecuteAsync(LocalTrainingCommand.Status));
        Assert.Equal(LocalTrainingClientFailure.RegistrationInvalid, mismatch.Failure);

        var restored = fixture.RewriteOperator(valid.PythonPath);
        File.WriteAllText(Path.Combine(fixture.Root, "python314._pth"), "unexpected-directory");
        var hooks = await Assert.ThrowsAsync<LocalTrainingClientException>(() =>
            new LocalTrainingClient(restored).ExecuteAsync(LocalTrainingCommand.Status));
        Assert.Equal(LocalTrainingClientFailure.RegistrationInvalid, hooks.Failure);
    }

    private static Stream Fixture(string filename)
    {
        var assembly = typeof(LocalTrainingClientTests).Assembly;
        var name = Assert.Single(assembly.GetManifestResourceNames(),
            value => value.EndsWith("." + filename, StringComparison.Ordinal));
        return assembly.GetManifestResourceStream(name)!;
    }

    private static string Accepted(LocalTrainingAttempt attempt) => JsonSerializer.Serialize(new
    {
        schema = 1, @event = "accepted", attempt = new { run_id = attempt.RunId, attempt_id = attempt.AttemptId }
    });

    private static object Summary(LocalTrainingAttempt attempt, string status, int? updates) => new
    {
        run_id = attempt.RunId, attempt_id = attempt.AttemptId, job = "cpu_self_check", purpose = "software_test_only",
        status, reason = (string?)null, completed_updates = updates, artifacts = Array.Empty<object>(),
        resume_checkpoint = (object?)null
    };

    private static string AttemptEvent(string kind, LocalTrainingAttempt attempt, string status, int? updates) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["schema"] = 1, ["event"] = kind, [kind == "progress" ? "attempt" : "result"] = Summary(attempt, status, updates)
        });

    private static string ListFrame(LocalTrainingAttempt[] attempts, LocalTrainingAttempt? next, int updates = 2) => JsonSerializer.Serialize(new
    {
        schema = 1, @event = "result", result = new
        {
            attempts = attempts.Select(item => Summary(item, "passed", updates)),
            next = next is null ? null : new { run_id = next.RunId, attempt_id = next.AttemptId }
        }
    });

    private static async Task Read(LocalTrainingClient.Conversation conversation, params string[] lines)
    {
        using var input = new ChunkedStream(Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n"));
        await conversation.ReadOutputAsync(input);
    }

    private sealed class RecordedProgress : IProgress<LocalTrainingEvent>
    {
        public List<LocalTrainingEvent> Values { get; } = [];
        public void Report(LocalTrainingEvent value) => Values.Add(value);
    }

    private sealed class ChunkedStream(byte[] contents) : MemoryStream(contents)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(5, buffer.Length)], cancellationToken);
    }

    private sealed class RegistrationFixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("dulche-training-registration-").FullName;
        public LocalTrainingWorkerRegistration Registration { get; }

        public RegistrationFixture()
        {
            var executable = Path.Combine(Root, "python.exe");
            var archive = Path.Combine(Root, "studio-cli.zip");
            var config = Path.Combine(Root, "operator.json");
            File.WriteAllText(executable, "Synthetic pin fixture");
            File.WriteAllText(archive, "Synthetic archive pin fixture");
            File.WriteAllText(config, JsonSerializer.Serialize(new { schema = 1, python = executable, python_sha256 = Hash(executable) }));
            Registration = new(executable, Hash(executable), archive, Hash(archive), config, Hash(config));
        }

        public LocalTrainingWorkerRegistration RewriteOperator(string pythonPath)
        {
            File.WriteAllText(Registration.OperatorConfigPath, JsonSerializer.Serialize(new
            {
                schema = 1, python = pythonPath, python_sha256 = Registration.PythonSha256
            }));
            return Registration with { OperatorConfigSha256 = Hash(Registration.OperatorConfigPath) };
        }

        private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

        public void Dispose()
        {
            foreach (var filename in new[] { "python.exe", "studio-cli.zip", "operator.json", "python314._pth" })
                File.Delete(Path.Combine(Root, filename));
            Directory.Delete(Root);
        }
    }
}
