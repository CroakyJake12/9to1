using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HavenOS.Apps.Wave;

internal static class WaveMarkersRegionsWorkflowTest
{
    public static int Run()
    {
        var directory = Directory.CreateTempSubdirectory("wave-markers-regions-").FullName;
        try
        {
            var path = Path.Combine(directory, "project.9to1w");
            var source = Path.Combine(directory, "source.wav");
            using (var writer = new BinaryWriter(File.Create(source)))
            {
                writer.Write("RIFF"u8.ToArray()); writer.Write(36 + 16); writer.Write("WAVEfmt "u8.ToArray());
                writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)1); writer.Write(8000);
                writer.Write(16000); writer.Write((ushort)2); writer.Write((ushort)16);
                writer.Write("data"u8.ToArray()); writer.Write(16);
                for (short sample = 1; sample <= 8; sample++) writer.Write((short)(sample * 1000));
            }
            var sourceHash = SHA256.HashData(File.ReadAllBytes(source));
            var original = WaveProjectStore.Create("Chapter edit", 8000, 1);
            original = WaveProjectStore.AddWavClip(original, original.Tracks[0].TrackId, source, 0);
            WaveProjectStore.Save(path, original, -1);
            var beforeExport = Path.Combine(directory, "before.wav");
            WaveProjectExporter.ExportPcm16(original, beforeExport);
            var reference = new WaveTimelineReference("present", "slide", Guid.NewGuid());
            var marked = WaveTimelineAnnotations.AddMarker(original, original.Revision, 0, "Opening", "blue", "chapter", "Intro", reference);
            marked = WaveTimelineAnnotations.AddMarker(marked, marked.Revision, 4, "Next chapter");
            var markerId = marked.Markers[0].MarkerId;
            marked = WaveTimelineAnnotations.AddRegion(marked, marked.Revision, 2, 4, "Loop", "green", "loop");
            var regionId = marked.Regions[0].RegionId;
            marked = WaveTimelineAnnotations.UpdateMarker(marked, marked.Revision, marked.Markers[0] with { Name = "Renamed opening", Notes = "Revised notes" });
            marked = WaveTimelineAnnotations.UpdateRegion(marked, marked.Revision, marked.Regions[0] with { Name = "Renamed loop" });
            Require(marked.Markers[0].MarkerId == markerId && marked.Regions[0].RegionId == regionId, "Rename changed annotation IDs.");
            Require(JsonSerializer.Serialize(marked.Tracks) == JsonSerializer.Serialize(original.Tracks), "Annotation edits changed clips or mixer state.");
            WaveProjectStore.Save(path, marked, original.Revision);
            var reopened = WaveProjectStore.Open(path);
            Require(reopened.Markers.SequenceEqual(marked.Markers) && reopened.Regions.SequenceEqual(marked.Regions), "Annotation metadata/IDs did not survive physical reopen.");
            Require(reopened.ProjectId == original.ProjectId && reopened.Revision == marked.Revision, "Project identity/revision changed on reopen.");
            var range = WaveTimelineAnnotations.RegionRange(reopened, regionId);
            Require(range.Start.Ticks == 2 && range.Duration.Ticks == 4 && range.End.Ticks == 6
                && range.Contains(range.Start) && !range.Contains(range.End), "Shared region interval is not exact half-open sample time.");
            Require(WaveTimelineAnnotations.Navigate(reopened, 0, true) == 2
                && WaveTimelineAnnotations.Navigate(reopened, 2, true) == 4
                && WaveTimelineAnnotations.Navigate(reopened, 4, true) == 6
                && WaveTimelineAnnotations.Navigate(reopened, 6, true) is null
                && WaveTimelineAnnotations.Navigate(reopened, 2, false) == 0
                && WaveTimelineAnnotations.Navigate(reopened, 0, false) is null, "Navigation skipped or wrapped a canonical boundary.");
            var regionExport = Path.Combine(directory, "region.wav");
            Require(WaveProjectExporter.ExportRegionPcm16(reopened, regionId, regionExport) == 4, "Region export duration changed.");
            Require(File.ReadAllBytes(regionExport).AsSpan(44).SequenceEqual(File.ReadAllBytes(beforeExport).AsSpan(44 + 2 * sizeof(short), 4 * sizeof(short))),
                "Region export changed source offsets or exact selected samples.");
            var faded = WaveProjectEdits.SetClipProcessing(reopened, reopened.Revision, reopened.Tracks[0].Clips[0].ClipId, .5, 4, 3);
            var fadedFull = Path.Combine(directory, "faded-full.wav"); var fadedRegion = Path.Combine(directory, "faded-region.wav");
            WaveProjectExporter.ExportPcm16(faded, fadedFull); WaveProjectExporter.ExportRegionPcm16(faded, regionId, fadedRegion);
            Require(File.ReadAllBytes(fadedRegion).AsSpan(44).SequenceEqual(File.ReadAllBytes(fadedFull).AsSpan(44 + 2 * sizeof(short), 4 * sizeof(short))),
                "Selected range reset fade envelope or gain instead of using canonical clip offset.");
            var trailing = WaveTimelineAnnotations.AddRegion(reopened, reopened.Revision, 7, 4, "Trailing silence");
            var trailingExport = Path.Combine(directory, "trailing.wav");
            Require(WaveProjectExporter.ExportRegionPcm16(trailing, trailing.Regions[^1].RegionId, trailingExport) == 4, "Explicit region duration was truncated.");
            Require(File.ReadAllBytes(trailingExport).AsSpan(46).ToArray().All(value => value == 0), "Region trailing interval was not silent.");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel(); var cancelledPath = Path.Combine(directory, "cancelled.wav");
                Expect<OperationCanceledException>(() => WaveProjectExporter.ExportRegionPcm16(reopened, regionId, cancelledPath, cancelled.Token));
                Require(!File.Exists(cancelledPath), "Cancelled region export left output bytes.");
            }
            var afterExport = Path.Combine(directory, "after.wav");
            WaveProjectExporter.ExportPcm16(reopened, afterExport);
            Require(File.ReadAllBytes(beforeExport).SequenceEqual(File.ReadAllBytes(afterExport)), "Annotations changed audible render.");
            Require(sourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))), "Annotation workflow modified source bytes.");
            var persisted = File.ReadAllBytes(path);
            Expect<InvalidOperationException>(() => WaveProjectStore.Save(path, marked, original.Revision));
            Require(persisted.SequenceEqual(File.ReadAllBytes(path)), "Stale CAS changed physical project.");
            Expect<InvalidOperationException>(() => WaveTimelineAnnotations.RemoveRegion(reopened, original.Revision, regionId));
            Expect<InvalidDataException>(() => WaveTimelineAnnotations.AddRegion(reopened, reopened.Revision, long.MaxValue, 1, "Overflow"));
            Expect<InvalidDataException>(() => WaveTimelineAnnotations.AddRegion(reopened, reopened.Revision, 0, 0, "Empty"));
            Expect<InvalidDataException>(() => WaveProjectStore.Save(path, reopened with { Markers = [reopened.Markers[0], reopened.Markers[0]] }, reopened.Revision));
            Expect<InvalidDataException>(() => WaveProjectStore.Save(path, reopened with { Regions = null! }, reopened.Revision));
            Require(persisted.SequenceEqual(File.ReadAllBytes(path)), "Rejected annotation document changed persisted bytes.");
            var removed = WaveTimelineAnnotations.RemoveMarker(reopened, reopened.Revision, markerId);
            removed = WaveTimelineAnnotations.RemoveRegion(removed, removed.Revision, regionId);
            Require(!removed.Markers.Any(item => item.MarkerId == markerId) && removed.Regions.Count == 0
                && removed.Markers.Single().MarkerId == reopened.Markers[1].MarkerId, "Deletion affected unrelated annotation IDs.");
            var consoleOut = Console.Out; var consoleError = Console.Error;
            using var commandOutput = new StringWriter(); using var commandError = new StringWriter();
            try
            {
                Console.SetOut(commandOutput); Console.SetError(commandError);
                Require(WaveProjectCommands.Run(["project", "add-marker", path, reopened.Revision.ToString(), "7", "CLI chapter"]) == 0,
                    "Owning marker command failed.");
                var commandProject = WaveProjectStore.Open(path);
                Require(commandProject.Markers.Single(item => item.Name == "CLI chapter").Frame == 7, "CLI marker not durably saved.");
                var commandBytes = File.ReadAllBytes(path);
                Require(WaveProjectCommands.Run(["project", "navigate", path, "6", "true"]) == 0,
                    "Owning navigation command failed.");
                Require(commandBytes.SequenceEqual(File.ReadAllBytes(path)), "Read navigation changed project bytes.");
                Require(WaveProjectCommands.Run(["project", "add-region", path, commandProject.Revision.ToString(), "0", "0", "Invalid"]) == 2,
                    "CLI accepted empty region.");
                Require(commandBytes.SequenceEqual(File.ReadAllBytes(path)), "Invalid region command changed project bytes.");
                Require(WaveProjectCommands.Run(["project", "add-marker", path, reopened.Revision.ToString(), "1", "Stale"]) == 2,
                    "CLI accepted stale project revision.");
                Require(commandBytes.SequenceEqual(File.ReadAllBytes(path)), "Stale marker command changed project bytes.");
            }
            finally { Console.SetOut(consoleOut); Console.SetError(consoleError); }
            // Legacy documents omit collections entirely; migration must preserve audio identities and revision.
            foreach (var legacySchema in new[] { 2, 3, 4, 5 })
            {
                var json = JsonNode.Parse(JsonSerializer.Serialize(original))!.AsObject();
                json["SchemaVersion"] = legacySchema; json.Remove("Markers"); json.Remove("Regions");
                var legacyPath = Path.Combine(directory, $"legacy-{legacySchema}.9to1w");
                File.WriteAllText(legacyPath, json.ToJsonString());
                var migrated = WaveProjectStore.Open(legacyPath);
                Require(migrated.SchemaVersion == 6 && migrated.ProjectId == original.ProjectId && migrated.Revision == original.Revision
                    && migrated.Markers.Count == 0 && migrated.Regions.Count == 0
                    && JsonSerializer.Serialize(migrated.Tracks) == JsonSerializer.Serialize(original.Tracks), "Legacy migration lost audio state or invented annotations.");
            }
            Console.WriteLine("Wave markers/regions: physical persistence, stable identities, shared ranges/navigation, unchanged audio, CAS/invalid bounds, deletion and legacy schema2-5 migration passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"Wave markers/regions failed: {error.Message}"); return 1; }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
