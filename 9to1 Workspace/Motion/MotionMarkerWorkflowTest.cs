using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Motion;

internal static class MotionMarkerWorkflowTest
{
    public static async Task<int> RunAsync()
    {
        var folder = Directory.CreateTempSubdirectory("motion-marker-evidence-");
        var path = Path.Combine(folder.FullName, "markers.motion.json");
        var store = new MotionProjectStore();
        var project = store.Create(Guid.NewGuid().ToString(), 1920, 1080, 30000, 1001);
        store.Save(path, project, -1);
        var sourceIds = project.AssetReferences.ToArray(); var sequenceId = project.Sequences[0].SequenceId;
        var legacy = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        legacy["SchemaVersion"] = 1;
        var legacyPath = Path.Combine(folder.FullName, "schema1.motion.json"); File.WriteAllText(legacyPath, legacy.ToJsonString());
        var originalLegacy = File.ReadAllBytes(legacyPath);
        Require(store.Load(legacyPath).Sequences[0].Markers is null && File.ReadAllBytes(legacyPath).SequenceEqual(originalLegacy), "Legacy read must preserve exact bytes and absent markers");
        var session = new MotionEditSession(store, path);
        session.Apply(p => store.AddMarker(p, p.Revision, sequenceId, 300, "Scene one"));
        var original = session.Project.Sequences[0].Markers!.Single();
        var clock = MotionProjectStore.Timebase(session.Project.Sequences[0]);
        Require(clock.SecondsNumerator == 1001 && clock.SecondsDenominator == 30000 && clock.At(original.Frame).Ticks == 300
            && session.Project.AssetReferences.SequenceEqual(sourceIds), "Markers use ORIGINAL rational frame ticks and retain source identities");
        session.Apply(p => store.UpdateMarker(p, p.Revision, sequenceId, original.MarkerId, original.Revision, 150, "First cut"));
        var updated = session.Project.Sequences[0].Markers!.Single();
        Require(updated.MarkerId == original.MarkerId && updated.Revision == 1 && updated.Frame == 150, "Marker edit retains canonical identity and advances its revision");
        var beforeRefusal = File.ReadAllBytes(path);
        Refuses(() => session.Apply(p => store.UpdateMarker(p, p.Revision, sequenceId, original.MarkerId, 0, 900, "stale")), "MarkerRevisionConflict");
        Require(File.ReadAllBytes(path).SequenceEqual(beforeRefusal), "Stale marker edit cannot write project");
        session.Undo(); Require(store.Load(path).Sequences[0].Markers!.Single() == original, "Undo persists SAME original marker");
        session.Redo(); Require(store.Load(path).Sequences[0].Markers!.Single() == updated, "Redo persists SAME updated marker");
        session.Apply(p => store.DeleteMarker(p, p.Revision, sequenceId, updated.MarkerId, updated.Revision));
        Require(store.Load(path).Sequences[0].Markers!.Count == 0, "Delete removes actual canonical marker");
        session.Undo(); Require(store.Load(path).Sequences[0].Markers!.Single() == updated, "Undo delete restores SAME marker ID");
        var sequence = session.Project.Sequences[0];
        var withClip = sequence with { VideoTracks = [sequence.VideoTracks[0] with { Elements = [new(Guid.NewGuid(), sequence.VideoTracks[0].TrackId, sourceIds[0].AssetId, 40, 20, 0, 20)] }] };
        Require(MotionMarkers.SnapFrame(withClip, 42, 3) == 40 && MotionMarkers.SnapFrame(withClip, 58, 3) == 60
            && MotionMarkers.SnapFrame(withClip, 148, 3) == 150 && MotionMarkers.SnapFrame(withClip, 145, 3) == 145,
            "Actual canonical marker and clip-edge snap threshold");
        var tie = sequence with { Markers = [new(Guid.NewGuid(), 100, "A"), new(Guid.NewGuid(), 104, "B")] };
        Require(MotionMarkers.SnapFrame(tie, 102, 2) == 100 && MotionMarkers.SnapFrame(tie, long.MaxValue, long.MaxValue) == 104,
            "Tie is deterministic and extreme frame subtraction does not overflow");
        Require(MotionMarkers.FrameAtPosition(1, 1, long.MaxValue) == long.MaxValue
            && MotionMarkers.SnapTolerance(8, 1, long.MaxValue) == long.MaxValue, "Actual native position mapping is overflow-safe");
        var duplicate = session.Project with { Sequences = [sequence with { Markers = [updated, updated] }] };
        RefusesInvalid(() => MotionProjectStore.Validate(duplicate));
        RefusesInvalid(() => MotionMarkers.Validate(new(Guid.NewGuid(), -1, "bad")));
        RefusesInvalid(() => MotionMarkers.Validate(new(Guid.NewGuid(), 0, "bad\nname")));
        await using var workspace = new MotionCuiWorkspace(session, _ => true);
        var row = MarkerRows(workspace).Single();
        await workspace.DispatchAsync("9to1.Motion.SelectMarker", row);
        Require(workspace.TrySetValue("MarkerName", "Edited from CUI") && workspace.TrySetValue("MarkerFrame", "175"), "Marker form draft admitted");
        await workspace.DispatchAsync("9to1.Motion.NextFrame", null);
        Require(workspace.TryGetValue("MarkerName", out var name) && Equals(name, "Edited from CUI"), "Unrelated transport preserves marker draft");
        await workspace.DispatchAsync("9to1.Motion.UpdateMarker", row);
        Require(store.Load(path).Sequences[0].Markers!.Single().Name == "Edited from CUI" && !workspace.HasUnsavedChanges, "Typed marker action persists real project and acknowledges only its drafts");
        var bytesAfter = File.ReadAllBytes(path);
        try { await workspace.DispatchAsync("9to1.Motion.DeleteMarker", row); throw new InvalidDataException("Stale row was accepted"); }
        catch (InvalidOperationException) { }
        Require(File.ReadAllBytes(path).SequenceEqual(bytesAfter), "Old same-key row cannot delete changed marker");
        var freshRow = MarkerRows(workspace).Single();
        workspace.TrySetValue("Start", "999");
        await workspace.DispatchAsync("9to1.Motion.JumpMarker", freshRow);
        Require(workspace.Playhead == 175 && workspace.TryGetValue("Start", out var start) && Equals(start, "999"), "Typed row jump retains unrelated draft");
        await workspace.DispatchAsync("9to1.Motion.DiscardDrafts", null);
        workspace.TrySetValue("Playhead", "173");
        await workspace.DispatchAsync("9to1.Motion.SnapPlayhead", null); Require(workspace.Playhead == 175, "Actual playhead snap action");
        await workspace.DispatchAsync("9to1.Motion.ToggleSnapping", null);
        Require(workspace.SnapTimelineFrame(173, 3) == 173, "Disabled snapping preserves exact entered frame");
        await workspace.DispatchAsync("9to1.Motion.MarkIn", null);
        workspace.TrySetValue("Playhead", "180"); await workspace.DispatchAsync("9to1.Motion.MarkOut", null);
        Require(workspace.TryGetValue("RangeStart", out var rangeIn) && Equals(rangeIn, "175")
            && workspace.TryGetValue("RangeEnd", out var rangeOut) && Equals(rangeOut, "181") && workspace.HasUnsavedChanges,
            "In/out actions keep explicit unsaved integral-frame range with exclusive out");
        await workspace.DispatchAsync("9to1.Motion.DiscardDrafts", null);
        _ = MotionCuiWorkspace.LoadDocument();
        Console.WriteLine($"Motion markers, rational frames, snapping, current-row refusal, history and reopen controls passed. Retained evidence: {folder.FullName}");
        return 0;
    }
    private static object[] MarkerRows(MotionCuiWorkspace workspace)
    {
        Require(workspace.TryGetValue("Markers", out var rows) && rows is System.Collections.IEnumerable, "Original marker rows exist");
        return ((System.Collections.IEnumerable)rows!).Cast<object>().ToArray();
    }
    private static void Refuses(Action action, string message)
    {
        try { action(); } catch (InvalidOperationException error) when (error.Message == message) { return; }
        throw new InvalidDataException("Expected exact original domain refusal: " + message);
    }
    private static void RefusesInvalid(Action action)
    { try { action(); } catch (InvalidDataException) { return; } throw new InvalidDataException("Invalid marker was accepted"); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }

    // Owning --ui-self-test invokes this AFTER the SAME native loader/window has been shown.
    // A bounded failed observer retains accepted source references; it never cancels or substitutes dispatch.
    private static readonly List<(CuiControlLoader Loader, Control Root, Task Pending)> RetainedNativeFailures = [];
    public static void RunNative(CuiControlLoader loader, Control root, MotionCuiWorkspace workspace, MotionProjectStore store, string path)
    {
        Control Find(string name) => root.GetVisualDescendants().OfType<Control>().Single(control => control.Name == name);
        void Click(Button button)
        {
            var observed = loader.Inspect(button);
            Require(button.IsEnabled && observed is { ActionsWired: true, DispatcherConnected: true }, "Actual enabled marker control is wired to SAME loader");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var actual = loader.WhenActionsIdleAsync(); var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!actual.IsCompleted && DateTime.UtcNow < deadline) Dispatcher.UIThread.RunJobs();
            if (!actual.IsCompleted) { RetainedNativeFailures.Add((loader, root, actual)); throw new TimeoutException("SAME actual marker UI dispatch remained pending; originals retained."); }
            actual.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
        }
        workspace.TrySetValue("Playhead", "45");
        ((TextBox)Find("MotionMarkerName")).Text = "Native chapter";
        Require(workspace.TryGetValue("MarkerName", out var nativeDraft) && Equals(nativeDraft, "Native chapter"), "Actual field write reaches workspace synchronously before command");
        Click((Button)Find("MotionAddMarker"));
        var added = store.Load(path).Sequences[0].Markers!.Single(marker => marker.Name == "Native chapter");
        Require(added.Frame == 45, "Actual native Add marker changes saved project");
        Click((Button)Find("MotionSelectMarkerRow"));
        ((TextBox)Find("MotionMarkerName")).Text = "Native corrected";
        ((TextBox)Find("MotionMarkerFrame")).Text = "65";
        Require(workspace.TryGetValue("MarkerFrame", out var nativeFrame) && Equals(nativeFrame, "65"), "Actual frame write reaches workspace synchronously before command");
        Click((Button)Find("MotionUpdateMarker"));
        Require(store.Load(path).Sequences[0].Markers!.Single(marker => marker.MarkerId == added.MarkerId).Frame == 65, "Actual native marker fields consumed by saved action");
        Click((Button)Find("MotionJumpMarkerRow")); Require(workspace.Playhead == 65, "Actual row jump uses current same-key receipt");
        Click((Button)Find("MotionDeleteMarker")); Require(store.Load(path).Sequences[0].Markers!.Count == 0, "Actual native delete persists");
        var undo = root.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Undo"));
        Click(undo); Require(store.Load(path).Sequences[0].Markers!.Single().MarkerId == added.MarkerId, "Actual native undo restores SAME marker");
        Console.WriteLine("Actual native CUI marker add/select/update/same-key jump/delete/undo controls passed.");
    }
}
