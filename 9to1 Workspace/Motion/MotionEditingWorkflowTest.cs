namespace HavenOS.Apps.Motion;

internal static class MotionEditingWorkflowTest
{
    public static async Task<int> RunAsync()
    {
        var folder = Directory.CreateTempSubdirectory("motion-editing-evidence-");
        var path = Path.Combine(folder.FullName, "edit.motion.json");
        var store = new MotionProjectStore();
        var project = store.Create("file:source-original", 1920, 1080, 30000, 1001);
        store.Save(path, project, -1);
        var sequence = project.Sequences[0]; var track = sequence.VideoTracks[0]; var asset = project.AssetReferences[0];
        var session = new MotionEditSession(store, path);
        session.Apply(p => store.Insert(p, p.Revision, sequence.SequenceId, track.TrackId, asset.AssetId, 0, 100, 200));
        var first = session.Project.Sequences[0].VideoTracks[0].Elements[0];
        session.Apply(p => store.Insert(p, p.Revision, sequence.SequenceId, track.TrackId, asset.AssetId, 40, 400, 420));
        var clips = session.Project.Sequences[0].VideoTracks[0].Elements;
        Require(clips.Count == 3 && clips[0].Duration == 40 && clips[1].SourceIn == 400 && clips[2].TimelineStart == 60 && clips[2].SourceIn == 140, "Insert splits and shifts original source mapping");
        session.Undo(); Require(session.Project.Sequences[0].VideoTracks[0].Elements.Single() == first, "Undo restores original identity");
        session.Redo(); Require(session.Project.Sequences[0].VideoTracks[0].Elements[2].ElementId == clips[2].ElementId, "Redo retains split identity");
        session.Apply(p => store.Overwrite(p, p.Revision, sequence.SequenceId, track.TrackId, asset.AssetId, 20, 700, 780));
        clips = session.Project.Sequences[0].VideoTracks[0].Elements;
        Require(clips.Count == 3 && clips[0].Duration == 20 && clips[1].Duration == 80 && clips[2].TimelineStart == 100 && clips[2].SourceIn == 180, "Overwrite preserves both outside ranges");
        session.Apply(p => store.SetCaptions(p, p.Revision, sequence.SequenceId, new(Guid.NewGuid(), "Captions", "en", [new(Guid.NewGuid(), 5, 115, "words")])));
        session.Apply(p => store.RemoveRange(p, p.Revision, sequence.SequenceId, 30, 90, true));
        var editedSequence = session.Project.Sequences[0];
        Require(editedSequence.VideoTracks[0].Elements.Last().TimelineStart == 40, "Extract closes time");
        Require(editedSequence.CaptionTracks![0].Cues.Count == 2 && editedSequence.CaptionTracks[0].Cues[1].StartFrame == 30 && editedSequence.CaptionTracks[0].Cues[1].EndFrame == 55, "Extract changes caption semantic view on same timeline");
        var beforeLock = session.Project.Revision;
        session.Apply(p => store.SetTrackState(p, p.Revision, sequence.SequenceId, track.TrackId, true, true));
        var bytes = File.ReadAllBytes(path);
        try { session.Apply(p => store.RemoveRange(p, p.Revision, sequence.SequenceId, 0, 1, true)); return 161; }
        catch (InvalidOperationException e) when (e.Message == "TrackLocked") { }
        Require(File.ReadAllBytes(path).SequenceEqual(bytes) && session.Project.Revision == beforeLock + 1, "Locked edit is atomic");
        session.Undo();
        await using var ui = new MotionCuiWorkspace(session, _ => true);
        Require(ui.TrySetValue("ClipIndex", 0), "CUI selection");
        Require(ui.TrySetValue("Start", "500"), "CUI numeric draft");
        ui.Select(ui.SelectedElementId, ui.Playhead + 1);
        Require(ui.TryGetValue("Start", out var sameClipDraft) && Equals(sameClipDraft, "500"), "Timeline keyboard selection retains SAME clip draft");
        Require(!ui.TrySetValue("ClipIndex", 1), "Different clip selection cannot erase pending fields");
        await ui.DispatchAsync("9to1.Motion.NextFrame", null);
        Require(ui.TryGetValue("Start", out var draft) && Equals(draft, "500"), "Frame transport preserves pending numeric edits");
        await ui.DispatchAsync("9to1.Motion.Move", null);
        Require(store.Load(path).Sequences[0].VideoTracks[0].Elements.Any(e => e.ElementId == first.ElementId && e.TimelineStart == 500), "CUI action persists edited range");
        await ui.DispatchAsync("9to1.Motion.Undo", null);
        Require(store.Load(path).Sequences[0].VideoTracks[0].Elements.Any(e => e.ElementId == first.ElementId && e.TimelineStart == 0), "CUI undo persists restored range");
        Require(ui.TrySetValue("CaptionIndex", 0) && ui.TrySetValue("CaptionText", "stale local draft"), "Select caption draft");
        var concurrent = store.Load(path); var concurrentTrack = concurrent.Sequences[0].CaptionTracks![0]; var cue = concurrentTrack.Cues[0];
        var corrected = MotionCaptions.UpdateCue(concurrentTrack, cue.CueId, cue.StartFrame, cue.EndFrame, "fresh external correction");
        store.Save(path, store.SetCaptions(concurrent, concurrent.Revision, sequence.SequenceId, corrected), concurrent.Revision);
        Require(ui.IsActionAvailable("9to1.Motion.Reload") == false && !ui.TrySetValue("CaptionIndex", 1), "Pending caption fields refuse implicit replacement");
        Require(!await ui.PrepareToCloseAsync(), "Pending fields refuse close preparation");
        await ui.DispatchAsync("9to1.Motion.DiscardDrafts", null);
        await ui.DispatchAsync("9to1.Motion.Reload", null);
        Require(ui.TryGetValue("CaptionText", out var loadedCaption) && Equals(loadedCaption, "fresh external correction"), "Reload replaces stale caption draft using stable cue identity");
        await using var denied = new MotionCuiWorkspace(session, _ => false);
        try { await denied.DispatchAsync("9to1.Motion.AddTrack", null); return 162; }
        catch (InvalidOperationException) { }
        await MotionOriginalLifetimeWorkflowTest.RunAsync();
        // The authored document must parse as the actual canonical CUI dialect.
        _ = MotionCuiWorkspace.LoadDocument();
        Console.WriteLine($"Motion editing workflow passed. Retained evidence: {path}");
        return 0;
    }
    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidDataException(detail); }
}
