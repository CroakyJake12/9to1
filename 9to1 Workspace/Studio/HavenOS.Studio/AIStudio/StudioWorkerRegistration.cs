using System.Text.Json;
using NineToOne.Dulche.Training;

namespace HavenOS.AIStudio;

/// <summary>Deployment-only registration. No project/definition content can select it.</summary>
public static class StudioWorkerRegistration
{
    public const string EnvironmentVariable = "DULCHE_STUDIO_WORKER_REGISTRATION";
    private const int MaximumBytes = 32 * 1024;
    private static readonly string[] Fields = ["PythonPath", "PythonSha256", "CliArchivePath", "CliArchiveSha256", "OperatorConfigPath", "OperatorConfigSha256"];

    public static StudioWorkerSetup CreateController(Func<string, string?>? read = null)
    {
        try
        {
            var registration = ReadEnvironment(read);
            return new(new(registration is null ? null : new LocalTrainingClient(registration)), null);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or ArgumentException)
        {
            return new(new(null), "The private local-worker registration is invalid. Studio projects remain available; the engine self-check is disabled.");
        }
    }

    public static LocalTrainingWorkerRegistration? ReadEnvironment(Func<string, string?>? read = null)
    {
        var path = (read ?? Environment.GetEnvironmentVariable)(EnvironmentVariable);
        return string.IsNullOrWhiteSpace(path) ? null : Read(path);
    }

    public static LocalTrainingWorkerRegistration Read(string path)
    {
        DemandLocalPath(path);
        // Hold the registration file against concurrent replacement/writes while
        // reading. Client independently holds/verifies the six selected pins.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Studio worker registration exceeds its byte limit.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Studio worker registration changed while reading.");
        return Parse(bytes);
    }

    public static LocalTrainingWorkerRegistration Parse(ReadOnlyMemory<byte> data)
    {
        if (data.Length is 0 or > MaximumBytes) throw new InvalidDataException("Studio worker registration is empty or oversized.");
        using var document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 2 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Studio worker registration must be an object.");
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!Fields.Contains(property.Name, StringComparer.Ordinal) || property.Value.ValueKind != JsonValueKind.String ||
                !fields.TryAdd(property.Name, property.Value.GetString()!))
                throw new InvalidDataException("Studio worker registration contains unknown, duplicate or invalid fields.");
        }
        if (fields.Count != Fields.Length) throw new InvalidDataException("Studio worker registration is missing a pin.");
        foreach (var key in Fields)
        {
            var value = fields[key];
            if (key.EndsWith("Sha256", StringComparison.Ordinal))
            {
                if (value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
                    throw new InvalidDataException("Studio worker digests must be lowercase SHA-256.");
            }
            else DemandLocalPath(value);
        }
        return new(fields["PythonPath"], fields["PythonSha256"], fields["CliArchivePath"], fields["CliArchiveSha256"],
            fields["OperatorConfigPath"], fields["OperatorConfigSha256"]);
    }

    private static void DemandLocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || path.Any(char.IsControl) ||
            !Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidDataException("Studio worker files require absolute local paths.");
        var full = Path.GetFullPath(path);
        for (var cursor = new FileInfo(full) as FileSystemInfo; cursor is not null; cursor = cursor switch
            { FileInfo file => file.Directory, DirectoryInfo directory => directory.Parent, _ => null })
        {
            if (!cursor.Exists) throw new FileNotFoundException("A Studio worker registration path does not exist.");
            if ((cursor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Studio worker paths cannot cross reparse points.");
        }
    }
}

public sealed record StudioWorkerSetup(StudioEngineCheckController Controller, string? Error);
