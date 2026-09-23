using System.Text.Json;

namespace HavenOS.Apps.Motion;

internal sealed record MotionCapabilityStatus(
    string ProductMapping,
    string Route,
    bool EngineAvailable,
    bool TimelineAvailable,
    bool RenderAvailable,
    bool ExportAvailable,
    bool PersistenceAvailable,
    bool MediaInspectionAvailable,
    string Message);

internal sealed record MotionMediaInspection(
    string ProductMapping,
    string Route,
    string FileName,
    string Extension,
    string Container,
    long SizeBytes,
    bool HeaderSignatureRecognized,
    bool PlaybackAvailable,
    string Message);

internal static class MotionSurface
{
    public const string Route = "motion";

    public static MotionCapabilityStatus GetStatus() =>
        new(
            ProductMapping: "Video → Motion",
            Route: Route,
            EngineAvailable: false,
            TimelineAvailable: false,
            RenderAvailable: false,
            ExportAvailable: false,
            PersistenceAvailable: false,
            MediaInspectionAvailable: true,
            Message: "Playback, editing, rendering, and export are unavailable; read-only media inspection is supported.");

    public static int SelfTest()
    {
        var status = GetStatus();

        if (!string.Equals(status.ProductMapping, "Video → Motion", StringComparison.Ordinal))
            return 10;

        if (!string.Equals(status.Route, Route, StringComparison.Ordinal))
            return 11;

        if (status.EngineAvailable
            || status.TimelineAvailable
            || status.RenderAvailable
            || status.ExportAvailable
            || status.PersistenceAvailable)
        {
            return 12;
        }

        if (!status.MediaInspectionAvailable)
            return 13;

        var fixtureDirectory = Directory.CreateTempSubdirectory("haven-motion-self-test-");
        var mp4Path = Path.Combine(fixtureDirectory.FullName, "valid.mp4");
        var invalidMp4Path = Path.Combine(fixtureDirectory.FullName, "invalid.mp4");
        var unsupportedPath = Path.Combine(fixtureDirectory.FullName, "unsupported.txt");
        try
        {
            File.WriteAllBytes(mp4Path, [0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 0, 0]);
            var inspected = MotionMediaInspector.Inspect(mp4Path);
            if (inspected.Route != Route
                || inspected.ProductMapping != "Video → Motion"
                || inspected.Container != "ISO Base Media"
                || inspected.SizeBytes != 16
                || !inspected.HeaderSignatureRecognized
                || inspected.PlaybackAvailable)
            {
                return 14;
            }

            File.WriteAllBytes(invalidMp4Path, [0, 0, 0, 16, (byte)'n', (byte)'o', (byte)'p', (byte)'e', 0, 0, 0, 0, 0, 0, 0, 0]);
            try
            {
                _ = MotionMediaInspector.Inspect(invalidMp4Path);
                return 15;
            }
            catch (InvalidDataException)
            {
            }

            File.WriteAllBytes(unsupportedPath, []);
            try
            {
                _ = MotionMediaInspector.Inspect(unsupportedPath);
                return 16;
            }
            catch (NotSupportedException)
            {
            }

            return 0;
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(fixtureDirectory.FullName))
                File.Delete(file);
            Directory.Delete(fixtureDirectory.FullName);
        }
    }
}

internal static class MotionMediaInspector
{
    private const string SupportedExtensions = ".mp4, .m4v, .mov, .3gp, .webm, .mkv, .avi";

    public static MotionMediaInspection Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        var container = extension switch
        {
            ".mp4" or ".m4v" => "ISO Base Media",
            ".mov" => "QuickTime/ISO Base Media",
            ".3gp" => "3GPP/ISO Base Media",
            ".webm" => "WebM",
            ".mkv" => "Matroska",
            ".avi" => "AVI",
            _ => throw new NotSupportedException($"Read-only inspection supports {SupportedExtensions}.")
        };

        using var stream = File.OpenRead(fullPath);
        Span<byte> header = stackalloc byte[12];
        var bytesRead = stream.Read(header);
        var signatureRecognized = extension switch
        {
            ".mp4" or ".m4v" or ".mov" or ".3gp" => HasFourCc(header[..bytesRead], 4, "ftyp"),
            ".webm" or ".mkv" => bytesRead >= 4 && header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3,
            ".avi" => HasFourCc(header[..bytesRead], 0, "RIFF") && HasFourCc(header[..bytesRead], 8, "AVI "),
            _ => false
        };

        if (!signatureRecognized)
            throw new InvalidDataException("The file extension is recognized, but its container signature does not match.");

        return new MotionMediaInspection(
            ProductMapping: "Video → Motion",
            Route: MotionSurface.Route,
            FileName: Path.GetFileName(fullPath),
            Extension: extension,
            Container: container,
            SizeBytes: stream.Length,
            HeaderSignatureRecognized: true,
            PlaybackAvailable: false,
            Message: "Container signature recognized; this build does not decode or play media.");
    }

    private static bool HasFourCc(ReadOnlySpan<byte> header, int offset, string fourCc)
    {
        if (offset < 0 || header.Length - offset < fourCc.Length)
            return false;

        for (var index = 0; index < fourCc.Length; index++)
        {
            if (header[offset + index] != (byte)fourCc[index])
                return false;
        }

        return true;
    }
}

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "--self-test", StringComparison.Ordinal))
            return MotionSurface.SelfTest();

        if (args.Length == 2 && string.Equals(args[0], "inspect", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Console.WriteLine(JsonSerializer.Serialize(MotionMediaInspector.Inspect(args[1])));
                return 0;
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or IOException
                    or NotSupportedException
                    or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Motion media inspection failed: {exception.Message}");
                return 2;
            }
        }

        if (args.Length == 0 || (args.Length == 1 && string.Equals(args[0], "status", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine(JsonSerializer.Serialize(MotionSurface.GetStatus()));
            return 0;
        }

        Console.Error.WriteLine(
            "Motion supports status, --self-test, and inspect <media-path>; playback, editing, rendering, and export are unavailable.");
        return 2;
    }
}
