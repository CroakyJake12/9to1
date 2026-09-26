using System.Text.Json;

namespace HavenOS.Apps.Motion;

internal static class MotionProjectWorkflowTest
{
    public static int Run()
    {
        var directory = Directory.CreateTempSubdirectory("haven-motion-project-test-");
        try
        {
            var path = Path.Combine(directory.FullName, "edit.motion.json");
            var create = Invoke(["project", "create", path, "file:source-video-42", "1920", "1080", "30"]);
            if (create.ExitCode != 0) return 30;
            var created = ReadResult(create.Output);
            var sequence = created.Sequences[0];
            var track = sequence.VideoTracks[0];
            var asset = created.AssetReferences[0];

            var insert = Invoke(["project", "insert", path, "0", sequence.SequenceId.ToString(), track.TrackId.ToString(),
                asset.AssetId.ToString(), "0", "100", "700"]);
            if (insert.ExitCode != 0 || ReadResult(insert.Output).Revision != 1) return 31;
            var firstElement = ReadResult(insert.Output).Sequences[0].VideoTracks[0].Elements[0];

            var split = Invoke(["project", "split", path, "1", sequence.SequenceId.ToString(), firstElement.ElementId.ToString(), "250"]);
            if (split.ExitCode != 0) return 32;
            var edited = ReadResult(split.Output);
            var elements = edited.Sequences[0].VideoTracks[0].Elements;
            if (edited.Revision != 2 || edited.ProjectId != created.ProjectId || elements.Count != 2
                || elements[0].TimelineStart != 0 || elements[0].Duration != 250 || elements[0].SourceIn != 100 || elements[0].SourceOut != 350
                || elements[1].TimelineStart != 250 || elements[1].Duration != 350 || elements[1].SourceIn != 350 || elements[1].SourceOut != 700
                || elements.Any(element => element.AssetId != asset.AssetId)) return 33;

            var opened = Invoke(["project", "open", path]);
            if (opened.ExitCode != 0) return 34;
            var reopened = ReadResult(opened.Output);
            if (reopened.ProjectId != created.ProjectId || reopened.Revision != 2
                || reopened.Sequences[0].VideoTracks[0].Elements.Select(item => item.ElementId).SequenceEqual(elements.Select(item => item.ElementId)) is false
                || reopened.AssetReferences[0].FileId != "file:source-video-42") return 35;

            var save = Invoke(["project", "save", path, "2"], JsonSerializer.Serialize(reopened));
            if (save.ExitCode != 0) return 36;
            var conflict = Invoke(["project", "save", path, "1"], JsonSerializer.Serialize(created));
            if (conflict.ExitCode != 2 || !conflict.Error.Contains("RevisionConflict", StringComparison.Ordinal)) return 37;

            var invalidPath = Path.Combine(directory.FullName, "invalid.motion.json");
            File.WriteAllText(invalidPath, "{ not valid project data");
            var invalidOpen = Invoke(["project", "open", invalidPath]);
            if (invalidOpen.ExitCode != 2 || !invalidOpen.Error.Contains("InvalidProject", StringComparison.Ordinal)) return 38;
            File.WriteAllText(invalidPath, "{\"SchemaVersion\":1,\"ProjectId\":\"00000000-0000-0000-0000-000000000001\",\"Revision\":0,\"Sequences\":null,\"AssetReferences\":null}");
            var invalidShape = Invoke(["project", "open", invalidPath]);
            if (invalidShape.ExitCode != 2 || !invalidShape.Error.Contains("InvalidProject", StringComparison.Ordinal)) return 39;
            var afterFailure = Invoke(["project", "open", path]);
            if (afterFailure.ExitCode != 0 || ReadResult(afterFailure.Output).Revision != 2) return 40;
            return 0;
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    private static (int ExitCode, string Output, string Error) Invoke(string[] args, string input = "")
    {
        using var reader = new StringReader(input);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = MotionProjectCommands.Run(args, reader, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static MotionProject ReadResult(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("result").Deserialize<MotionProject>()
            ?? throw new InvalidDataException("Command did not return a Motion project.");
    }
}
