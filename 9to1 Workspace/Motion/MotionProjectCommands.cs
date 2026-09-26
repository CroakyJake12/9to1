using System.Globalization;
using System.Text.Json;

namespace HavenOS.Apps.Motion;

internal static class MotionProjectCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new() { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public static int Run(string[] args, TextReader input, TextWriter output, TextWriter error)
    {
        if (args.Length < 2 || !string.Equals(args[0], "project", StringComparison.OrdinalIgnoreCase))
            return Fail(error, "InvalidArguments", "Usage: project create|open|insert|split|save ...");

        try
        {
            var store = new MotionProjectStore();
            MotionProject result;
            switch (args[1].ToLowerInvariant())
            {
                case "create" when args.Length == 7:
                    result = store.Create(args[3], ParseInt(args[4]), ParseInt(args[5]), ParseInt(args[6]), 1);
                    store.Save(args[2], result, -1);
                    break;
                case "open" when args.Length == 3:
                    result = store.Load(args[2]);
                    break;
                case "insert" when args.Length == 10:
                {
                    var current = store.Load(args[2]);
                    var expectedRevision = ParseLong(args[3]);
                    result = store.Insert(current, expectedRevision, ParseGuid(args[4]), ParseGuid(args[5]), ParseGuid(args[6]),
                        ParseLong(args[7]), ParseLong(args[8]), ParseLong(args[9]));
                    store.Save(args[2], result, expectedRevision);
                    break;
                }
                case "split" when args.Length == 7:
                {
                    var current = store.Load(args[2]);
                    var expectedRevision = ParseLong(args[3]);
                    result = store.Split(current, expectedRevision, ParseGuid(args[4]), ParseGuid(args[5]), ParseLong(args[6]));
                    store.Save(args[2], result, expectedRevision);
                    break;
                }
                case "save" when args.Length == 4:
                {
                    var expectedRevision = ParseLong(args[3]);
                    result = JsonSerializer.Deserialize<MotionProject>(input.ReadToEnd(), JsonOptions)
                        ?? throw new InvalidDataException("Project document is empty.");
                    store.Save(args[2], result, expectedRevision);
                    break;
                }
                default:
                    return Fail(error, "InvalidArguments", "Usage: project create <path> <file-id> <width> <height> <fps>; open <path>; insert <path> <revision> <sequence-id> <track-id> <asset-id> <start> <source-in> <source-out>; split <path> <revision> <sequence-id> <element-id> <time>; save <path> <expected-revision> (project JSON on stdin)");
            }

            output.WriteLine(JsonSerializer.Serialize(new { ok = true, result }));
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException
            or IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException
            or KeyNotFoundException or JsonException)
        {
            var code = exception switch
            {
                InvalidOperationException when exception.Message == "RevisionConflict" => "RevisionConflict",
                KeyNotFoundException => exception.Message,
                JsonException or InvalidDataException => "InvalidProject",
                FileNotFoundException or DirectoryNotFoundException => "ProjectNotFound",
                UnauthorizedAccessException => "PermissionDenied",
                _ => "InvalidRequest"
            };
            return Fail(error, code, exception.Message);
        }
    }

    private static int Fail(TextWriter error, string code, string message)
    {
        error.WriteLine(JsonSerializer.Serialize(new { ok = false, error = new { code, message, target = "9to1.Motion.Project", recoverable = true } }));
        return 2;
    }

    private static int ParseInt(string value) => int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
    private static long ParseLong(string value) => long.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
    private static Guid ParseGuid(string value) => Guid.Parse(value);
}
