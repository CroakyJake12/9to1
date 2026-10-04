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
            var overflowing = nextRevision with { Revision = 4, Sequences = nextRevision.Sequences.Select(item => item with
            {
                VideoTracks = item.VideoTracks.Select(videoTrack => videoTrack with
                {
                    Elements = videoTrack.Elements.Select(element => element with { TimelineStart = long.MaxValue }).ToArray()
                }).ToArray()
            }).ToArray() };
            var overflowSave = Invoke(["project", "save", path, "3"], JsonSerializer.Serialize(overflowing));
            if (overflowSave.ExitCode != 2 || !overflowSave.Error.Contains("InvalidProject", StringComparison.Ordinal)
                || !File.ReadAllBytes(path).SequenceEqual(savedBytes)) return 43;
            var overflowInsert = Invoke(["project", "insert", path, "3", sequence.SequenceId.ToString(), track.TrackId.ToString(),
                asset.AssetId.ToString(), long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), "0", "1"]);
            if (overflowInsert.ExitCode != 2 || !File.ReadAllBytes(path).SequenceEqual(savedBytes)) return 44;
            // End-to-end CLI edits use the persisted sequence frame clock and leave source identity intact.
            var moving = Invoke(["project", "move", path, "3", sequence.SequenceId.ToString(), firstElement.ElementId.ToString(), "1000"]);
            if (moving.ExitCode != 0) return 45;
            var moved = ReadResult(moving.Output); var originalId = firstElement.ElementId;
            var selected = moved.Sequences[0].VideoTracks[0].Elements.Single(item => item.ElementId == originalId);
            if (selected.TimelineStart != 1000 || selected.SourceIn != 100 || selected.Duration != 250) return 46;
            var slipping = Invoke(["project", "slip", path, "4", sequence.SequenceId.ToString(), originalId.ToString(), "500"]);
            if (slipping.ExitCode != 0) return 47;
            selected = ReadResult(slipping.Output).Sequences[0].VideoTracks[0].Elements.Single(item => item.ElementId == originalId);
            if (selected.TimelineStart != 1000 || selected.SourceIn != 500 || selected.SourceOut != 750 || selected.Duration != 250) return 48;
            var trimming = Invoke(["project", "trim", path, "5", sequence.SequenceId.ToString(), originalId.ToString(), "550", "700"]);
            if (trimming.ExitCode != 0) return 49;
            var final = new MotionProjectStore().Load(path);
            selected = final.Sequences[0].VideoTracks[0].Elements.Single(item => item.ElementId == originalId);
            if (final.Revision != 6 || selected.TimelineStart != 1050 || selected.Duration != 150
                || selected.SourceIn != 550 || selected.SourceOut != 700 || selected.AssetId != asset.AssetId
                || selected.TrackId != track.TrackId || final.AssetReferences[0] != created.AssetReferences[0]
                || final.Sequences[0].VideoTracks[0].Elements.Single(item => item.ElementId != originalId) != elements[1]) return 50;
            var stableBytes = File.ReadAllBytes(path);
            foreach (var invalid in new[]
            {
                new[] { "project", "move", path, "5", sequence.SequenceId.ToString(), originalId.ToString(), "0" },
                new[] { "project", "move", path, "6", sequence.SequenceId.ToString(), originalId.ToString(), long.MaxValue.ToString() },
                new[] { "project", "slip", path, "6", sequence.SequenceId.ToString(), originalId.ToString(), long.MaxValue.ToString() },
                new[] { "project", "trim", path, "6", sequence.SequenceId.ToString(), originalId.ToString(), "549", "700" },
                new[] { "project", "trim", path, "6", sequence.SequenceId.ToString(), originalId.ToString(), "550", "701" },
                new[] { "project", "trim", path, "6", sequence.SequenceId.ToString(), originalId.ToString(), "600", "600" },
                new[] { "project", "move", path, "6", Guid.NewGuid().ToString(), originalId.ToString(), "0" }
            })
                if (Invoke(invalid).ExitCode != 2 || !File.ReadAllBytes(path).SequenceEqual(stableBytes)) return 51;
            var frameStore = new MotionProjectStore();
            var fractional = frameStore.Create("file:fractional-source", 1920, 1080, 30000, 1001);
            var fractionalSequence = fractional.Sequences[0]; var fractionalTrack = fractionalSequence.VideoTracks[0];
            fractional = frameStore.Insert(fractional, 0, fractionalSequence.SequenceId, fractionalTrack.TrackId,
                fractional.AssetReferences[0].AssetId, long.MaxValue - 1000, long.MaxValue - 1000, long.MaxValue);
            var boundaryId = fractional.Sequences[0].VideoTracks[0].Elements[0].ElementId;
            fractional = frameStore.Trim(fractional, 1, fractionalSequence.SequenceId, boundaryId,
                long.MaxValue - 500, long.MaxValue - 100);
            var boundary = fractional.Sequences[0].VideoTracks[0].Elements[0];
            if (boundary.TimelineStart != long.MaxValue - 500 || boundary.Duration != 400
                || fractional.Sequences[0].FrameRateNumerator != 30000 || fractional.Sequences[0].FrameRateDenominator != 1001) return 52;
            var fractionalPath = Path.Combine(directory.FullName, "fractional.motion.json");
            // First persisted project revision must be zero; then advance the actual sequential CAS chain.
            var fractionalInitial = frameStore.Create("file:fractional-source", 1920, 1080, 30000, 1001);
            frameStore.Save(fractionalPath, fractionalInitial, -1);
            var fs = fractionalInitial.Sequences[0]; var ft = fs.VideoTracks[0];
            var fractionalInsert = frameStore.Insert(fractionalInitial, 0, fs.SequenceId, ft.TrackId,
                fractionalInitial.AssetReferences[0].AssetId, 1001, 30000, 31001);
            frameStore.Save(fractionalPath, fractionalInsert, 0);
            var fe = fractionalInsert.Sequences[0].VideoTracks[0].Elements[0];
            var fractionalMove = frameStore.Move(fractionalInsert, 1, fs.SequenceId, fe.ElementId, 2002);
            frameStore.Save(fractionalPath, fractionalMove, 1);
            var fractionalReopen = frameStore.Load(fractionalPath);
            if (fractionalReopen.Sequences[0].FrameRateDenominator != 1001
                || fractionalReopen.Sequences[0].VideoTracks[0].Elements[0].TimelineStart != 2002
                || fractionalReopen.Sequences[0].VideoTracks[0].Elements[0].SourceIn != 30000) return 53;
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
