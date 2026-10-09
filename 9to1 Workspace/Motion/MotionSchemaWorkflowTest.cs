using System.Text.Json;

namespace HavenOS.Apps.Motion;

internal static class MotionSchemaWorkflowTest
{
    public static int Run()
    {
        const string legacy = """
        {"SchemaVersion":1,"ProjectId":"10000000-0000-0000-0000-000000000001","Sequences":[{"SequenceId":"20000000-0000-0000-0000-000000000002","Width":1920,"Height":1080,"FrameRateNumerator":30000,"FrameRateDenominator":1001,"VideoTracks":[{"TrackId":"30000000-0000-0000-0000-000000000003","Name":"Video 1","Elements":[{"ElementId":"40000000-0000-0000-0000-000000000004","TrackId":"30000000-0000-0000-0000-000000000003","AssetId":"50000000-0000-0000-0000-000000000005","TimelineStart":0,"Duration":90,"SourceIn":10,"SourceOut":100,"ProxyAssetId":null}]}]}],"AssetReferences":[{"AssetId":"50000000-0000-0000-0000-000000000005","FileId":"file:legacy-source","SourceRevisionID":null}],"CreatedAt":"2026-01-01T00:00:00+00:00","ModifiedAt":"2026-01-01T00:00:00+00:00","Revision":7}
        """;
        var folder = Directory.CreateTempSubdirectory("motion-schema-evidence-");
        var path = Path.Combine(folder.FullName, "legacy.motion.json");
        File.WriteAllText(path, legacy); var original = File.ReadAllBytes(path); var store = new MotionProjectStore();
        var project = store.Load(path); var sequence = project.Sequences[0]; var track = sequence.VideoTracks[0]; var element = track.Elements[0];
        if (project.SchemaVersion != 2 || project.Revision != 7 || track.Locked || !track.Visible || sequence.CaptionTracks is not null
            || !File.ReadAllBytes(path).SequenceEqual(original)) return 170;
        var moved = store.Move(project, 7, sequence.SequenceId, element.ElementId, 25); store.Save(path, moved, 7);
        var backup = Directory.GetFiles(folder.FullName, "*.backup");
        if (backup.Length != 1 || !File.ReadAllBytes(backup[0]).SequenceEqual(original)) return 171;
        var reopened = store.Load(path);
        if (reopened.SchemaVersion != 2 || reopened.Revision != 8 || reopened.ProjectId != project.ProjectId
            || reopened.Sequences[0].VideoTracks[0].Elements[0].ElementId != element.ElementId
            || reopened.Sequences[0].VideoTracks[0].Elements[0].TimelineStart != 25) return 172;
        var future = Path.Combine(folder.FullName, "future.motion.json");
        File.WriteAllText(future, JsonSerializer.Serialize(reopened with { SchemaVersion = 3 }));
        try { store.Load(future); return 173; } catch (InvalidDataException) { }
        var one = new MotionEditSession(store, path); var two = new MotionEditSession(store, path);
        one.Apply(p => store.Move(p, p.Revision, sequence.SequenceId, element.ElementId, 50));
        try { two.Apply(p => store.Move(p, p.Revision, sequence.SequenceId, element.ElementId, 75)); return 174; }
        catch (InvalidOperationException e) when (e.Message == "RevisionConflict") { }
        two.Reload(); two.Apply(p => store.Move(p, p.Revision, sequence.SequenceId, element.ElementId, 75));
        if (store.Load(path).Revision != 10) return 175;
        Console.WriteLine($"Motion schema migration and conflict recovery passed. Retained evidence: {path}");
        return 0;
    }
}
