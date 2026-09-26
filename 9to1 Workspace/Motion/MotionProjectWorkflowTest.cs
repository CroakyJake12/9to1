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

            var nextRevision = reopened with { Revision = 3, ModifiedAt = DateTimeOffset.UtcNow };
            var save = Invoke(["project", "save", path, "2"], JsonSerializer.Serialize(nextRevision));
            if (save.ExitCode != 0) return 36;
            var savedBytes = File.ReadAllBytes(path);
            var noOpSave = Invoke(["project", "save", path, "3"], JsonSerializer.Serialize(nextRevision));
            if (noOpSave.ExitCode != 2 || !noOpSave.Error.Contains("RevisionConflict", StringComparison.Ordinal)
                || !File.ReadAllBytes(path).SequenceEqual(savedBytes)) return 37;
            var skippedRevision = nextRevision with { Revision = 5 };
            var skippedSave = Invoke(["project", "save", path, "3"], JsonSerializer.Serialize(skippedRevision));
            if (skippedSave.ExitCode != 2 || !skippedSave.Error.Contains("RevisionConflict", StringComparison.Ordinal)
                || !File.ReadAllBytes(path).SequenceEqual(savedBytes)) return 38;
            var mismatchedIdentity = nextRevision with { ProjectId = Guid.NewGuid(), Revision = 4 };
            var identitySave = Invoke(["project", "save", path, "3"], JsonSerializer.Serialize(mismatchedIdentity));
            if (identitySave.ExitCode != 2 || !identitySave.Error.Contains("RevisionConflict", StringComparison.Ordinal)
                || !File.ReadAllBytes(path).SequenceEqual(savedBytes)) return 39;

            var invalidPath = Path.Combine(directory.FullName, "invalid.motion.json");
            File.WriteAllText(invalidPath, "{ not valid project data");
            var invalidOpen = Invoke(["project", "open", invalidPath]);
            if (invalidOpen.ExitCode != 2 || !invalidOpen.Error.Contains("InvalidProject", StringComparison.Ordinal)) return 40;
            File.WriteAllText(invalidPath, "{\"SchemaVersion\":1,\"ProjectId\":\"00000000-0000-0000-0000-000000000001\",\"Revision\":0,\"Sequences\":null,\"AssetReferences\":null}");
            var invalidShape = Invoke(["project", "open", invalidPath]);
            if (invalidShape.ExitCode != 2 || !invalidShape.Error.Contains("InvalidProject", StringComparison.Ordinal)) return 41;
            var afterFailure = Invoke(["project", "open", path]);
            if (afterFailure.ExitCode != 0 || ReadResult(afterFailure.Output).Revision != 3) return 42;
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
