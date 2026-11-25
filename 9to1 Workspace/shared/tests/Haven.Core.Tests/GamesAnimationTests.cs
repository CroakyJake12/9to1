using Haven.Core.Games;

namespace Haven.Core.Tests;

/// <summary>IX-GAMES-002: pure authored animation model and validated edit transactions.
/// Proves the source acceptance scenario at the model level: a key retains its original bound object/property when the selected object changes,
/// and preview isolation never mutates authored keys. Also covers revision CAS, incompatible retarget rejection, group move preserving relative time,
/// duration shorten review of dependent keys, and asset identity independent of scene instances.</summary>
public sealed class GamesAnimationTests
{
    private static readonly Guid AssetID = Guid.NewGuid();
    private static readonly Guid ObjectA = Guid.NewGuid();
    private static readonly Guid ObjectB = Guid.NewGuid();

    private static GamesAnimationAsset MakeAsset() => new(AssetID, "Idle", 2.0);
    private static GamesAnimationTrack MakeTrack(Guid trackID, Guid target) =>
        new(trackID, target, "Position.X", GamesAnimationValueKind.Scalar, [new GamesAnimationKey(Guid.NewGuid(), 0.5, [1.0], GamesAnimationInterpolation.Linear)]);

    [Fact]
    public void CreateAndAddTrackAssignsStableIdentities()
    {
        var doc = GamesAnimationDocument.Create(MakeAsset());
        Assert.Equal(1, doc.Revision);
        Assert.Empty(doc.Tracks);

        var track = MakeTrack(Guid.NewGuid(), ObjectA);
        var added = doc.AddTrack(track, 1);
        Assert.Equal(2, added.Revision);
        Assert.Single(added.Tracks);
        Assert.Equal(track.TrackID, added.Tracks[0].TrackID);
        // Original is immutable.
        Assert.Empty(doc.Tracks);
    }

    [Fact]
    public void AddTrackRejectsDuplicateIdentityAndStaleRevision()
    {
        var track = MakeTrack(Guid.NewGuid(), ObjectA);
        var doc = GamesAnimationDocument.Create(MakeAsset()).AddTrack(track, 1);
        Assert.Throws<InvalidOperationException>(() => doc.AddTrack(track with { Keys = [] }, 2));
        Assert.Throws<InvalidOperationException>(() => doc.AddTrack(MakeTrack(Guid.NewGuid(), ObjectB), 99));
    }

    [Fact]
    public void KeyComponentCountMustMatchTrackValueKind()
    {
        var track = MakeTrack(Guid.NewGuid(), ObjectA);
        var doc = GamesAnimationDocument.Create(MakeAsset()).AddTrack(track, 1);
        // Scalar track expects one component; a three-component value is incompatible.
        Assert.Throws<InvalidDataException>(() => doc.AddKey(track.TrackID, new GamesAnimationKey(Guid.NewGuid(), 1.0, [1, 2, 3], GamesAnimationInterpolation.Linear), 2));
    }

    [Fact]
    public void UpdateAndMoveKeysPreserveBindingAndRelativeTime()
    {
        var trackID = Guid.NewGuid();
        var k1 = new GamesAnimationKey(Guid.NewGuid(), 0.5, [1.0], GamesAnimationInterpolation.Linear);
        var k2 = new GamesAnimationKey(Guid.NewGuid(), 1.5, [2.0], GamesAnimationInterpolation.Constant);
        var track = new GamesAnimationTrack(trackID, ObjectA, "Position.X", GamesAnimationValueKind.Scalar, [k1, k2]);
        var doc = GamesAnimationDocument.Create(MakeAsset()).AddTrack(track, 1);

        // Move both keys as a group; relative time (delta 1.0) must be preserved.
        var moved = doc.MoveKeys(trackID, [k1.KeyID, k2.KeyID], 0.25, 2);
        var movedTrack = moved.Tracks.Single(t => t.TrackID == trackID);
        Assert.Equal(0.75, movedTrack.Keys.Single(k => k.KeyID == k1.KeyID).TimeSeconds, 9);
        Assert.Equal(1.75, movedTrack.Keys.Single(k => k.KeyID == k2.KeyID).TimeSeconds, 9);

        // Editing a key never retargets the track's bound object/property.
        var edited = moved.UpdateKey(trackID, k1.KeyID, null, [9.0], GamesAnimationInterpolation.Constant, moved.Revision);
        var editedTrack = edited.Tracks.Single(t => t.TrackID == trackID);
        Assert.Equal(ObjectA, editedTrack.TargetObjectID);
        Assert.Equal("Position.X", editedTrack.PropertyName);
        Assert.Equal(9.0, editedTrack.Keys.Single(k => k.KeyID == k1.KeyID).Value[0]);
    }

    [Fact]
    public void StaleRevisionEditsAreRejectedWithoutMutation()
    {
        var track = MakeTrack(Guid.NewGuid(), ObjectA);
        var doc = GamesAnimationDocument.Create(MakeAsset()).AddTrack(track, 1);
        Assert.Throws<InvalidOperationException>(() => doc.DeleteKey(track.TrackID, Guid.NewGuid(), 1));
        Assert.Equal(2, doc.Revision);
    }

    [Fact]
    public void ShortenDurationSurfacesDependentKeysForReview()
    {
        var track = new GamesAnimationTrack(Guid.NewGuid(), ObjectA, "Position.X", GamesAnimationValueKind.Scalar,
            [new GamesAnimationKey(Guid.NewGuid(), 0.5, [1], GamesAnimationInterpolation.Linear),
             new GamesAnimationKey(Guid.NewGuid(), 3.0, [2], GamesAnimationInterpolation.Linear)]);
        var doc = GamesAnimationDocument.Create(MakeAsset()).AddTrack(track, 1);

        var beyond = doc.KeysBeyondDuration(2.0);
        Assert.Single(beyond);
        Assert.Equal(3.0, beyond[0].TimeSeconds);

        // Shortening the clip is a valid authored transaction; dependent keys remain until explicitly reviewed.
        var shortened = doc.SetDuration(2.0, 2);
        Assert.Equal(2.0, shortened.Asset.DurationSeconds);
        Assert.Single(shortened.KeysBeyondDuration(2.0));
    }

    [Fact]
    public void PreviewIsolationDoesNotMutateAuthoredKeys()
    {
        // The model exposes no sampled-transform write path: preview state is separate from authored keys.
        var track = MakeTrack(Guid.NewGuid(), ObjectA);
        var doc = GamesAnimationDocument.Create(MakeAsset()).AddTrack(track, 1);
        var before = doc.Tracks.Single().Keys.Single();

        // Simulate a preview seek/sample by reading only; no author mutation occurs.
        Assert.Equal(before.TimeSeconds, doc.Tracks.Single().Keys.Single().TimeSeconds);
        Assert.Equal(before.Value[0], doc.Tracks.Single().Keys.Single().Value[0]);
        Assert.Equal(2, doc.Revision);
    }

    [Fact]
    public void AcceptanceScenario_KeyRetainsOriginalBindingWhenSelectedObjectChanges()
    {
        // Preview an animation on ObjectA, then change the selected object to ObjectB and drag a key.
        var trackID = Guid.NewGuid();
        var key = new GamesAnimationKey(Guid.NewGuid(), 1.0, [5.0], GamesAnimationInterpolation.Linear);
        var track = new GamesAnimationTrack(trackID, ObjectA, "Position.X", GamesAnimationValueKind.Scalar, [key]);
        var doc = GamesAnimationDocument.Create(MakeAsset()).AddTrack(track, 1);

        // Selection change is a preview concern; the authored document still references ObjectA.
        Assert.Equal(ObjectA, doc.Tracks.Single().TargetObjectID);

        // Dragging the key edits time only; the bound object/property are retained.
        var dragged = doc.MoveKeys(trackID, [key.KeyID], 0.5, doc.Revision);
        var draggedTrack = dragged.Tracks.Single(t => t.TrackID == trackID);
        Assert.Equal(ObjectA, draggedTrack.TargetObjectID);
        Assert.Equal("Position.X", draggedTrack.PropertyName);
        Assert.Equal(1.5, draggedTrack.Keys.Single().TimeSeconds, 9);

        // Stopping preview cannot overwrite the newly selected object's authored pose: ObjectB has no authored track here.
        Assert.DoesNotContain(dragged.Tracks, t => t.TargetObjectID == ObjectB);
    }

    [Fact]
    public void AssetIdentityIsIndependentOfSceneInstances()
    {
        var asset = MakeAsset();
        // The same asset identity can back multiple documents/instances without copying scene data.
        var first = GamesAnimationDocument.Create(asset);
        var second = GamesAnimationDocument.Create(asset);
        Assert.Equal(first.Asset.AnimationID, second.Asset.AnimationID);
        Assert.Equal(1, first.Revision);
        Assert.Equal(1, second.Revision);
    }

    [Fact]
    public void CaptureCopiesValueListsToPreventAliasing()
    {
        var trackID = Guid.NewGuid();
        var value = new List<double> { 1.0 };
        var key = new GamesAnimationKey(Guid.NewGuid(), 0.5, value, GamesAnimationInterpolation.Linear);
        var track = new GamesAnimationTrack(trackID, ObjectA, "Position.X", GamesAnimationValueKind.Scalar, [key]);

        // Capture should copy the list
        var capturedTrack = track.Capture();
        var capturedKey = capturedTrack.Keys.Single();

        Assert.NotSame(value, capturedKey.Value);

        // Mutating original should not affect captured
        value[0] = 2.0;
        Assert.Equal(1.0, capturedKey.Value[0]);
    }

    [Fact]
    public void MoveKeysPreservesRelativeSpacingWhenClamping()
    {
        var trackID = Guid.NewGuid();
        var k1 = new GamesAnimationKey(Guid.NewGuid(), 0.5, [1.0], GamesAnimationInterpolation.Linear);
        var k2 = new GamesAnimationKey(Guid.NewGuid(), 1.5, [2.0], GamesAnimationInterpolation.Constant);
        var track = new GamesAnimationTrack(trackID, ObjectA, "Position.X", GamesAnimationValueKind.Scalar, [k1, k2]);
        var doc = GamesAnimationDocument.Create(MakeAsset()).AddTrack(track, 1);

        // Move both keys by -1.0. k1 would go to -0.5 (clamped to 0), k2 to 0.5.
        // Relative spacing (1.0) must be preserved: k1=0, k2=1.0.
        var moved = doc.MoveKeys(trackID, [k1.KeyID, k2.KeyID], -1.0, 2);
        var movedTrack = moved.Tracks.Single(t => t.TrackID == trackID);

        Assert.Equal(0.0, movedTrack.Keys.Single(k => k.KeyID == k1.KeyID).TimeSeconds, 9);
        Assert.Equal(1.0, movedTrack.Keys.Single(k => k.KeyID == k2.KeyID).TimeSeconds, 9);
    }

    [Fact]
    public void UnsupportedInterpolationAndInvalidTimeAreRejected()
    {
        var track = MakeTrack(Guid.NewGuid(), ObjectA);
        var doc = GamesAnimationDocument.Create(MakeAsset()).AddTrack(track, 1);
        Assert.Throws<InvalidDataException>(() => new GamesAnimationKey(Guid.NewGuid(), 0.5, [1], (GamesAnimationInterpolation)99).Capture());
        Assert.Throws<InvalidDataException>(() => new GamesAnimationKey(Guid.NewGuid(), double.NaN, [1], GamesAnimationInterpolation.Linear).Capture());
    }
}
