using System.Globalization;
using System.Text.Json;

namespace HavenOS.Apps.Wave;

internal static class WaveProjectCommands
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length < 3) throw new ArgumentException("Usage: project open|split|trim|move|duplicate|delete|gain|mixer|add-track|add-marker|move-marker|rename-marker|remove-marker|add-region|resize-region|rename-region|remove-region <path> ...");
            var project = WaveProjectStore.Open(args[2]);
            if (args[1] == "open" && args.Length == 3)
            { Console.WriteLine(JsonSerializer.Serialize(new { ok = true, result = project })); return 0; }
            if (args[1] == "export-region" && args.Length == 5)
            {
                var frames = WaveProjectExporter.ExportRegionPcm16(project, Guid.Parse(args[3]), args[4]);
                Console.WriteLine(JsonSerializer.Serialize(new { ok = true, result = new { frames, regionId = args[3] } }));
                return 0;
            }
            if (args[1] == "navigate" && args.Length == 5)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { ok = true, result = WaveTimelineAnnotations.Navigate(project, Long(args[3]), bool.Parse(args[4])) }));
                return 0;
            }
            if (args.Length < 5) throw new ArgumentException("Mutation requires expected project revision and target identity.");
            var expected = long.Parse(args[3], CultureInfo.InvariantCulture);
            var current = project;
            project = args[1] switch
            {
                "add-marker" when args.Length == 6 => WaveTimelineAnnotations.AddMarker(project, expected, Long(args[4]), args[5]),
                "move-marker" when args.Length == 6 => WaveTimelineAnnotations.UpdateMarker(project, expected,
                    Marker(project, Guid.Parse(args[4])) with { Frame = Long(args[5]) }),
                "rename-marker" when args.Length == 6 => WaveTimelineAnnotations.UpdateMarker(project, expected,
                    Marker(project, Guid.Parse(args[4])) with { Name = args[5] }),
                "remove-marker" when args.Length == 5 => WaveTimelineAnnotations.RemoveMarker(project, expected, Guid.Parse(args[4])),
                "add-region" when args.Length == 7 => WaveTimelineAnnotations.AddRegion(project, expected, Long(args[4]), Long(args[5]), args[6]),
                "resize-region" when args.Length == 7 => WaveTimelineAnnotations.UpdateRegion(project, expected,
                    Region(project, Guid.Parse(args[4])) with { StartFrame = Long(args[5]), FrameCount = Long(args[6]) }),
                "rename-region" when args.Length == 6 => WaveTimelineAnnotations.UpdateRegion(project, expected,
                    Region(project, Guid.Parse(args[4])) with { Name = args[5] }),
                "remove-region" when args.Length == 5 => WaveTimelineAnnotations.RemoveRegion(project, expected, Guid.Parse(args[4])),
                "add-track" when args.Length == 5 => WaveProjectEdits.AddTrack(project, expected, args[4]),
                "split" when args.Length == 6 => WaveProjectEdits.Split(project, expected, Guid.Parse(args[4]), Long(args[5])),
                "trim" when args.Length == 7 => WaveProjectEdits.Trim(project, expected, Guid.Parse(args[4]), Long(args[5]), Long(args[6])),
                "join" when args.Length == 6 => WaveProjectEdits.Join(project, expected, Guid.Parse(args[4]), Guid.Parse(args[5])),
                "move" when args.Length == 6 => WaveProjectEdits.Move(project, expected, Guid.Parse(args[4]), Long(args[5])),
                "duplicate" when args.Length == 6 => WaveProjectEdits.Duplicate(project, expected, Guid.Parse(args[4]), Long(args[5])),
                "delete" when args.Length == 6 => WaveProjectEdits.Delete(project, expected, Guid.Parse(args[4]), bool.Parse(args[5])),
                "gain" when args.Length == 8 => WaveProjectEdits.SetClipProcessing(project, expected, Guid.Parse(args[4]), Double(args[5]), Long(args[6]), Long(args[7])),
                "mixer" when args.Length == 9 => WaveProjectEdits.SetTrackMixer(project, expected, Guid.Parse(args[4]), Double(args[5]), Double(args[6]), bool.Parse(args[7]), bool.Parse(args[8])),
                _ => throw new ArgumentException("Invalid project command or arguments.")
            };
            WaveProjectStore.Save(args[2], project, current.Revision);
            Console.WriteLine(JsonSerializer.Serialize(new { ok = true, result = project }));
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException or IOException
            or InvalidDataException or InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException or NotSupportedException)
        {
            var code = exception switch
            {
                InvalidOperationException when exception.Message == "RevisionConflict" => "RevisionConflict",
                KeyNotFoundException => exception.Message,
                FileNotFoundException => "ProjectNotFound",
                UnauthorizedAccessException => "PermissionDenied",
                IOException => "IoError",
                NotSupportedException => "CapabilityUnavailable",
                _ => "InvalidArgument"
            };
            Console.Error.WriteLine(JsonSerializer.Serialize(new { ok = false, error = new { code, message = exception.Message, target = "9to1.Wave.Project" } }));
            return 2;
        }
    }

    private static WaveMarker Marker(WaveProject project, Guid id) => project.Markers.SingleOrDefault(item => item.MarkerId == id)
        ?? throw new KeyNotFoundException("MarkerNotFound");
    private static WaveRegion Region(WaveProject project, Guid id) => project.Regions.SingleOrDefault(item => item.RegionId == id)
        ?? throw new KeyNotFoundException("RegionNotFound");

    private static long Long(string text) => long.Parse(text, CultureInfo.InvariantCulture);
    private static double Double(string text) => double.Parse(text, CultureInfo.InvariantCulture);
}
