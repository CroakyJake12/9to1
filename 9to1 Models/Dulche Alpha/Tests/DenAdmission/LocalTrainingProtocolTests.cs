using System.Text;
using System.Text.Json;
using NineToOne.Dulche.Training;

namespace Dulche.Den.Tests;

public sealed class LocalTrainingProtocolTests
{
    private static readonly LocalTrainingAttempt Attempt = new(
        Guid.Parse("b6069749-bb3a-45ef-9c2f-c250b7bb96c7"), Guid.Parse("dfcc8197-bbc3-4b82-954f-0456cfb2e155"));
    private static readonly LocalTrainingArtifact Checkpoint = new(Attempt,
        Guid.Parse("e757a493-352f-45cc-af16-e20166da76e2"), new string('a', 64), 1234);
    private static JsonElement ParseRequest(LocalTrainingCommand command, LocalTrainingAttempt? attempt = null,
        LocalTrainingArtifact? checkpoint = null, int? limit = null)
    { using var document = JsonDocument.Parse(LocalTrainingProtocol.Request(command, attempt, checkpoint, limit)); return document.RootElement.Clone(); }
    private static LocalTrainingEvent Frame(string value) => LocalTrainingProtocol.ParseEvent(Encoding.UTF8.GetBytes(value));
    private static string Reference(LocalTrainingAttempt? attempt = null) => Encoding.UTF8.GetString(LocalTrainingProtocol.Request(
        LocalTrainingCommand.Resume, checkpoint: Checkpoint with { Attempt = attempt ?? Attempt }));
    private static string AttemptResult(string? purpose = null, LocalTrainingAttempt? artifactAttempt = null)
    {
        using var request = JsonDocument.Parse(Reference(artifactAttempt));
        return JsonSerializer.Serialize(new
        {
            run_id = Attempt.RunId, attempt_id = Attempt.AttemptId, job = "cpu_self_check",
            purpose = purpose ?? "software_test_only", status = "passed", reason = (string?)null,
            completed_updates = 2, artifacts = new[] { request.RootElement.GetProperty("checkpoint") },
            resume_checkpoint = request.RootElement.GetProperty("checkpoint")
        });
    }

    [Fact]
    public void RequestsContainOnlyTheDeclaredCommandAndOpaqueReferences()
    {
        Assert.Equal(new[] { "schema", "command" }, ParseRequest(LocalTrainingCommand.Launch).EnumerateObject().Select(p => p.Name));
        var resume = ParseRequest(LocalTrainingCommand.Resume, checkpoint: Checkpoint);
        Assert.Equal(Checkpoint, LocalTrainingProtocol.ReadArtifact(resume.GetProperty("checkpoint")));
        Assert.Equal(Attempt, LocalTrainingProtocol.ReadAttempt(ParseRequest(LocalTrainingCommand.List, Attempt, limit: 20).GetProperty("after")));
        Assert.Equal(Attempt.RunId.ToString("D"), ParseRequest(LocalTrainingCommand.Pause, Attempt).GetProperty("run_id").GetString());
    }

    [Fact]
    public void MissingAndUnrelatedRequestFieldsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => LocalTrainingProtocol.Request(LocalTrainingCommand.Launch, Attempt));
        Assert.Throws<ArgumentException>(() => LocalTrainingProtocol.Request(LocalTrainingCommand.Resume));
        Assert.Throws<ArgumentException>(() => LocalTrainingProtocol.Request(LocalTrainingCommand.Pause));
        Assert.Throws<ArgumentException>(() => LocalTrainingProtocol.Request(LocalTrainingCommand.Status, limit: 3));
        Assert.Throws<ArgumentException>(() => LocalTrainingProtocol.Request(LocalTrainingCommand.List, limit: 51));
        Assert.Throws<ArgumentException>(() => LocalTrainingProtocol.Request(LocalTrainingCommand.Resume, checkpoint: Checkpoint with { SizeBytes = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => LocalTrainingProtocol.Request((LocalTrainingCommand)99));
    }

    [Fact]
    public void AcceptedReadinessProgressAndResultsFollowTheSameVersionedSchema()
    {
        var accepted = Frame(JsonSerializer.Serialize(new { schema = 1, @event = "accepted", attempt = new { run_id = Attempt.RunId, attempt_id = Attempt.AttemptId } }));
        Assert.Equal(Attempt, LocalTrainingProtocol.ReadAttempt(accepted.Payload));
        Assert.Equal(LocalTrainingEventKind.Result, Frame("{\"schema\":1,\"event\":\"result\",\"result\":{\"ready\":true,\"job\":\"cpu_self_check\",\"purpose\":\"software_test_only\"}}").Kind);
        Assert.Equal(LocalTrainingEventKind.Progress, Frame("{\"schema\":1,\"event\":\"progress\",\"attempt\":" + AttemptResult() + "}").Kind);
        var result = Frame("{\"schema\":1,\"event\":\"result\",\"result\":" + AttemptResult() + "}");
        Assert.Equal("passed", result.Payload.GetProperty("status").GetString());
        Assert.Equal(2, result.Payload.GetProperty("completed_updates").GetInt32());
    }

    [Fact]
    public void PublicTrainingOrCrossAttemptArtifactsCannotMasqueradeAsTheSelfCheck()
    {
        Assert.Throws<InvalidDataException>(() => Frame("{\"schema\":1,\"event\":\"result\",\"result\":" + AttemptResult("real_training") + "}"));
        Assert.Throws<InvalidDataException>(() => Frame("{\"schema\":1,\"event\":\"result\",\"result\":" + AttemptResult(artifactAttempt: Attempt with { AttemptId = Guid.NewGuid() }) + "}"));
    }

    [Theory]
    [InlineData("{\"schema\":1,\"schema\":1,\"event\":\"error\",\"code\":\"x\",\"message\":\"x\"}")]
    [InlineData("{\"schema\":2,\"event\":\"error\",\"code\":\"x\",\"message\":\"x\"}")]
    [InlineData("{\"schema\":1,\"event\":\"execute\",\"command\":\"anything\"}")]
    [InlineData("{\"schema\":1,\"event\":\"error\",\"code\":\"x\",\"message\":\"x\",\"runtime_path\":\"untrusted\"}")]
    public void AmbiguousOrExtendedFramesAreRefused(string value) => Assert.Throws<InvalidDataException>(() => Frame(value));

    [Fact]
    public void FramesAreBoundedAndPayloadSurvivesJsonDocumentDisposal()
    {
        Assert.Throws<InvalidDataException>(() => LocalTrainingProtocol.ParseEvent(new byte[LocalTrainingProtocol.MaximumLineBytes + 1]));
        Assert.Throws<InvalidDataException>(() => LocalTrainingProtocol.ParseEvent(ReadOnlyMemory<byte>.Empty));
        var error = Frame("{\"schema\":1,\"event\":\"error\",\"code\":\"Unavailable\",\"message\":\"The worker is unavailable.\"}");
        Assert.Equal("Unavailable", error.Payload.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("cpu-self-check-v1.jsonl")]
    [InlineData("cpu-self-check-resume-v1.jsonl")]
    public void ActualPythonProducerFramesAreAcceptedWithoutChangingTheirSchema(string fixture)
    {
        var assembly = typeof(LocalTrainingProtocolTests).Assembly;
        var name = Assert.Single(assembly.GetManifestResourceNames(), value => value.EndsWith("." + fixture, StringComparison.Ordinal));
        using var input = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(input, new UTF8Encoding(false, true));
        var frames = new List<LocalTrainingEvent>();
        while (reader.ReadLine() is { } line) frames.Add(Frame(line));
        Assert.Equal(LocalTrainingEventKind.Accepted, frames[0].Kind);
        var accepted = LocalTrainingProtocol.ReadAttempt(frames[0].Payload);
        Assert.Equal(1, frames.Count(frame => frame.Kind == LocalTrainingEventKind.Accepted));
        foreach (var frame in frames.Skip(1))
        {
            Assert.True(frame.Kind is LocalTrainingEventKind.Progress or LocalTrainingEventKind.Result);
            Assert.Equal(accepted, LocalTrainingProtocol.ReadAttempt(frame.Payload, exact: false));
        }
        Assert.Equal(LocalTrainingEventKind.Result, frames[^1].Kind);
        Assert.Equal("passed", frames[^1].Payload.GetProperty("status").GetString());
        Assert.Equal("software_test_only", frames[^1].Payload.GetProperty("purpose").GetString());
        Assert.Equal(2, frames[^1].Payload.GetProperty("completed_updates").GetInt32());
    }
}
