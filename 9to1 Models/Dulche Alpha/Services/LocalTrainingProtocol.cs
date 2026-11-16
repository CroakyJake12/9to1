using System.Text.Json;

namespace NineToOne.Dulche.Training;

public enum LocalTrainingCommand { Status, List, Launch, Resume, Pause }
public enum LocalTrainingEventKind { Accepted, Progress, Result, Error }
public sealed record LocalTrainingAttempt(Guid RunId, Guid AttemptId);
public sealed record LocalTrainingArtifact(LocalTrainingAttempt Attempt, Guid ArtifactId, string Sha256, long SizeBytes);
public sealed record LocalTrainingEvent(LocalTrainingEventKind Kind, JsonElement Payload);

/// <summary>
/// The Studio-facing protocol for the registered CPU engine self-check. It carries
/// identifiers and observations only: runtime paths, devices, code and data are
/// operator configuration, never fields accepted from a Studio job request.
/// </summary>
public static class LocalTrainingProtocol
{
    public const int MaximumLineBytes = 32 * 1024;

    public static byte[] Request(LocalTrainingCommand command, LocalTrainingAttempt? attempt = null,
        LocalTrainingArtifact? checkpoint = null, int? limit = null)
    {
        if (!Enum.IsDefined(command)) throw new ArgumentOutOfRangeException(nameof(command));
        if (attempt is not null) Validate(attempt);
        if (checkpoint is not null) Validate(checkpoint);
        var value = new Dictionary<string, object> { ["schema"] = 1, ["command"] = command.ToString().ToLowerInvariant() };
        switch (command)
        {
            case LocalTrainingCommand.Status when checkpoint is null && limit is null:
                if (attempt is not null) AddAttempt(value, attempt);
                break;
            case LocalTrainingCommand.List when checkpoint is null && (limit is null or >= 1 and <= 50):
                if (limit is not null) value["limit"] = limit.Value;
                if (attempt is not null) value["after"] = AttemptObject(attempt);
                break;
            case LocalTrainingCommand.Launch when attempt is null && checkpoint is null && limit is null:
                break;
            case LocalTrainingCommand.Resume when attempt is null && checkpoint is not null && limit is null:
                value["checkpoint"] = ArtifactObject(checkpoint);
                break;
            case LocalTrainingCommand.Pause when attempt is not null && checkpoint is null && limit is null:
                AddAttempt(value, attempt);
                break;
            default:
                throw new ArgumentException("The command has missing or unrelated fields.");
        }
        return JsonSerializer.SerializeToUtf8Bytes(value);
    }

    public static LocalTrainingEvent ParseEvent(ReadOnlyMemory<byte> line)
    {
        if (line.Length is 0 or > MaximumLineBytes) throw new InvalidDataException("The worker frame exceeds its byte limit.");
        using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 12 });
        var root = document.RootElement;
        ValidateTree(root);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema", out var schema) ||
            !schema.TryGetInt32(out var version) || version != 1 || !root.TryGetProperty("event", out var eventName) ||
            eventName.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Unknown worker protocol envelope.");
        var kind = eventName.GetString() switch
        {
            "accepted" => LocalTrainingEventKind.Accepted,
            "progress" => LocalTrainingEventKind.Progress,
            "result" => LocalTrainingEventKind.Result,
            "error" => LocalTrainingEventKind.Error,
            _ => throw new InvalidDataException("Unknown worker event.")
        };
        if (kind == LocalTrainingEventKind.Error)
        {
            Fields(root, "schema", "event", "code", "message");
            Text(root.GetProperty("code"), 128);
            Text(root.GetProperty("message"), 2048);
            return new(kind, root.Clone());
        }
        var name = kind == LocalTrainingEventKind.Result ? "result" : "attempt";
        Fields(root, "schema", "event", name);
        var payload = root.GetProperty(name);
        if (kind == LocalTrainingEventKind.Accepted)
            ReadAttempt(payload, exact: true);
        else if (kind == LocalTrainingEventKind.Progress)
            ValidateAttemptResult(payload);
        else
            ValidateResult(payload);
        return new(kind, payload.Clone());
    }

    public static LocalTrainingAttempt ReadAttempt(JsonElement element, bool exact = true)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The attempt is not an object.");
        if (exact) Fields(element, "run_id", "attempt_id");
        return new(Id(element.GetProperty("run_id")), Id(element.GetProperty("attempt_id")));
    }

    public static LocalTrainingArtifact ReadArtifact(JsonElement element)
    {
        Fields(element, "run_id", "attempt_id", "artifact_id", "sha256", "size_bytes");
        if (!element.GetProperty("size_bytes").TryGetInt64(out var size) || size < 0)
            throw new InvalidDataException("Artifact size is invalid.");
        var hash = Text(element.GetProperty("sha256"), 64);
        if (!Digest(hash)) throw new InvalidDataException("Artifact hash is invalid.");
        return new(ReadAttempt(element, exact: false), Id(element.GetProperty("artifact_id")), hash, size);
    }

    private static void ValidateResult(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The result is not an object.");
        if (value.TryGetProperty("ready", out var ready))
        {
            Fields(value, "ready", "job", "purpose");
            if (ready.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException("Readiness must be boolean.");
            Job(value);
        }
        else if (value.TryGetProperty("attempts", out var attempts))
        {
            Fields(value, "attempts", "next");
            if (attempts.ValueKind != JsonValueKind.Array || attempts.GetArrayLength() > 50)
                throw new InvalidDataException("Attempt list exceeds its limit.");
            foreach (var attempt in attempts.EnumerateArray()) ValidateAttemptResult(attempt);
            if (value.GetProperty("next").ValueKind != JsonValueKind.Null) ReadAttempt(value.GetProperty("next"));
        }
        else ValidateAttemptResult(value);
    }

    private static void ValidateAttemptResult(JsonElement value)
    {
        Fields(value, "run_id", "attempt_id", "job", "purpose", "status", "reason", "completed_updates", "artifacts", "resume_checkpoint");
        var attempt = ReadAttempt(value, exact: false);
        Job(value);
        if (Text(value.GetProperty("status"), 32) is not ("prepared" or "running" or "passed" or "failed" or "paused" or "cancelled" or "interrupted"))
            throw new InvalidDataException("Unknown attempt state.");
        if (value.GetProperty("reason").ValueKind != JsonValueKind.Null) Text(value.GetProperty("reason"), 256);
        var updates = value.GetProperty("completed_updates");
        if (updates.ValueKind != JsonValueKind.Null && (!updates.TryGetInt32(out var count) || count < 0))
            throw new InvalidDataException("Update count is invalid.");
        var artifacts = value.GetProperty("artifacts");
        if (artifacts.ValueKind != JsonValueKind.Array || artifacts.GetArrayLength() > 128)
            throw new InvalidDataException("Artifact list exceeds its limit.");
        foreach (var item in artifacts.EnumerateArray())
            if (ReadArtifact(item).Attempt != attempt) throw new InvalidDataException("Artifact belongs to another attempt.");
        var resume = value.GetProperty("resume_checkpoint");
        if (resume.ValueKind != JsonValueKind.Null && ReadArtifact(resume).Attempt != attempt)
            throw new InvalidDataException("Resume checkpoint belongs to another attempt.");
    }

    private static void Job(JsonElement value)
    {
        if (Text(value.GetProperty("job"), 64) != "cpu_self_check" ||
            Text(value.GetProperty("purpose"), 64) != "software_test_only")
            throw new InvalidDataException("This protocol has not admitted a model-training job.");
    }
    private static void ValidateTree(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name) || names.Count > 32) throw new InvalidDataException("Duplicate or excessive JSON fields.");
                ValidateTree(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            if (value.GetArrayLength() > 128) throw new InvalidDataException("Worker array exceeds its limit.");
            foreach (var item in value.EnumerateArray()) ValidateTree(item);
        }
    }
    private static void Fields(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(expected))
            throw new InvalidDataException("Worker fields differ from the protocol.");
    }
    private static Guid Id(JsonElement value)
    {
        var text = Text(value, 36);
        if (!Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty || text != id.ToString("D"))
            throw new InvalidDataException("A canonical attempt identifier is required.");
        return id;
    }
    private static string Text(JsonElement value, int maximum)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text || text.Length is 0 ||
            text.Length > maximum || text.Any(char.IsControl)) throw new InvalidDataException("Worker text is invalid.");
        return text;
    }
    private static bool Digest(string text) => text.Length == 64 && text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static void Validate(LocalTrainingAttempt attempt)
    { if (attempt.RunId == Guid.Empty || attempt.AttemptId == Guid.Empty) throw new ArgumentException("Empty attempt identifier."); }
    private static void Validate(LocalTrainingArtifact artifact)
    {
        Validate(artifact.Attempt);
        if (artifact.ArtifactId == Guid.Empty || artifact.SizeBytes < 0 || !Digest(artifact.Sha256)) throw new ArgumentException("Invalid checkpoint reference.");
    }
    private static Dictionary<string, object> AttemptObject(LocalTrainingAttempt attempt)
    { var value = new Dictionary<string, object>(); AddAttempt(value, attempt); return value; }
    private static void AddAttempt(Dictionary<string, object> value, LocalTrainingAttempt attempt)
    { value["run_id"] = attempt.RunId.ToString("D"); value["attempt_id"] = attempt.AttemptId.ToString("D"); }
    private static Dictionary<string, object> ArtifactObject(LocalTrainingArtifact artifact)
    {
        var value = AttemptObject(artifact.Attempt);
        value["artifact_id"] = artifact.ArtifactId.ToString("D"); value["sha256"] = artifact.Sha256; value["size_bytes"] = artifact.SizeBytes;
        return value;
    }
}
