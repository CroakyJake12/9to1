namespace HavenOS.Apps.Motion;

internal static class MotionAdvancedWorkflowTest
{
    public static int Run()
    {
        var folder = Directory.CreateTempSubdirectory("motion-advanced-evidence-"); var path = Path.Combine(folder.FullName, "advanced.motion.json");
        var store = new MotionProjectStore(); var initial = store.Create("file:source", 1920, 1080, 30000, 1001); store.Save(path, initial, -1);
        var session = new MotionEditSession(store, path); var sequenceId = initial.Sequences[0].SequenceId; var trackId = initial.Sequences[0].VideoTracks[0].TrackId;
        var assetId = initial.AssetReferences[0].AssetId;
        session.Apply(p => store.Insert(p, p.Revision, sequenceId, trackId, assetId, 0, 0, 30));
        session.Apply(p => store.Insert(p, p.Revision, sequenceId, trackId, assetId, 30, 40, 60));
        session.Apply(p => store.Insert(p, p.Revision, sequenceId, trackId, assetId, 50, 70, 100));
        var originals = session.Project.Sequences[0].VideoTracks[0].Elements.ToArray();
        session.Apply(p => store.Roll(p, p.Revision, sequenceId, originals[0].ElementId, originals[1].ElementId, 35));
        var rolled = session.Project.Sequences[0].VideoTracks[0].Elements;
        if (rolled[0].Duration != 35 || rolled[0].SourceOut != 35 || rolled[1].TimelineStart != 35 || rolled[1].SourceIn != 45 || rolled[1].Duration != 15 || rolled[2] != originals[2]) return 180;
        session.Undo();
        session.Apply(p => store.Slide(p, p.Revision, sequenceId, originals[1].ElementId, 35));
        var slid = session.Project.Sequences[0].VideoTracks[0].Elements;
        if (slid[0].Duration != 35 || slid[1].TimelineStart != 35 || slid[1].SourceIn != 40 || slid[1].Duration != 20
            || slid[2].TimelineStart != 55 || slid[2].SourceIn != 75 || slid[2].Duration != 25) return 181;
        session.Undo();
        session.Apply(p => store.RippleTrim(p, p.Revision, sequenceId, originals[1].ElementId, MotionTrimEdge.Start, 45));
        var trimmed = session.Project.Sequences[0].VideoTracks[0].Elements;
        if (trimmed[1].ElementId != originals[1].ElementId || trimmed[1].TimelineStart != 30 || trimmed[1].SourceIn != 45 || trimmed[1].Duration != 15
            || trimmed[2].TimelineStart != 45) return 182;
        session.Apply(p => store.AddTrack(p, p.Revision, sequenceId, "Video 2"));
        var destination = session.Project.Sequences[0].VideoTracks[1].TrackId;
        session.Apply(p => store.MoveToTrack(p, p.Revision, sequenceId, originals[1].ElementId, destination, 120));
        var moved = store.Load(path).Sequences[0].VideoTracks[1].Elements.Single();
        if (moved.ElementId != originals[1].ElementId || moved.AssetId != assetId || moved.TrackId != destination || moved.TimelineStart != 120 || moved.SourceIn != 45) return 183;
        session.Apply(p => store.SetTrackState(p, p.Revision, sequenceId, trackId, true, true));
        var snapshot = File.ReadAllBytes(path);
        try { session.Apply(p => store.MoveToTrack(p, p.Revision, sequenceId, moved.ElementId, trackId, 30)); return 184; }
        catch (InvalidOperationException e) when (e.Message == "TrackLocked") { }
        if (!File.ReadAllBytes(path).SequenceEqual(snapshot)) return 185;
        Console.WriteLine($"Motion roll, slide, ripple trim and track transfer passed. Retained evidence: {path}");
        return 0;
    }
}
