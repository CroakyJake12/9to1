using Haven.Application.Games;
using Haven.Core.Games;

namespace Haven.Application.Tests;

/// <summary>Regression coverage for G05: binding the G02/G03 widget contract to the canonical Games project document and store.
/// Verifies schema-1 backwards compatibility (prior documents reopen with null widgets), exact revision-bound add/update,
/// stale-revision rejection, and a full encode/decode round trip preserving stable IDs.</summary>
public sealed class GamesWidgetProjectRoundTripTests
{
    private static GamesSceneSnapshot MakeScene(Guid projectID, Guid sceneID)
        => new(projectID, sceneID, 1,
            [new GamesSceneNode(Guid.NewGuid(), Guid.NewGuid(), null, "Root",
                new GamesSpatialComponent(Guid.NewGuid(), new GamesVector3(0, 0, 0)))],
            []);

    private static GamesProjectDocument MakeProject(Guid projectID, Guid sceneID)
        => new(GamesProjectDocument.CurrentSchemaVersion, projectID, 1, GamesWorkspaceMode.Development, sceneID, [MakeScene(projectID, sceneID)]);

    private static GamesWidgetDocument MakeWidgets()
        => GamesWidgetDocument.Create("menu",
            [new GamesWidgetNode("root", "Root", GamesWidgetKind.Panel, null, null, null, null, null),
             new GamesWidgetNode("title", "Title", GamesWidgetKind.Text, "Play", null, null, null, "root"),
             new GamesWidgetNode("play", "PlayButton", GamesWidgetKind.Button, "Play", null, null, "OnPlay", "root")]);

    [Fact]
    public void Schema1DocumentWithoutWidgetsReopensWithNullWidgets()
    {
        var project = MakeProject(Guid.NewGuid(), Guid.NewGuid());
        Assert.Null(project.Widgets);

        var bytes = GamesProjectCodec.Encode(project);
        var reopened = GamesProjectCodec.Decode(bytes);

        Assert.Equal(project.ProjectID, reopened.ProjectID);
        Assert.Equal(project.Revision, reopened.Revision);
        Assert.Null(reopened.Widgets);
    }

    [Fact]
    public void AddWidgetsThenRoundTripPreservesStableIdsAndRevision()
    {
        var project = MakeProject(Guid.NewGuid(), Guid.NewGuid());
        var widgets = MakeWidgets();

        var added = GamesProjectEdits.AddWidgets(project, project.Revision, widgets);
        Assert.NotNull(added.Widgets);
        Assert.Equal(widgets.Revision, added.Widgets!.Revision);
        Assert.Equal(checked(project.Revision + 1), added.Revision);
        Assert.Equal(widgets.Nodes.Select(n => n.Id), added.Widgets.Nodes.Select(n => n.Id));

        var bytes = GamesProjectCodec.Encode(added);
        var reopened = GamesProjectCodec.Decode(bytes);

        Assert.NotNull(reopened.Widgets);
        Assert.Equal(added.ProjectID, reopened.ProjectID);
        Assert.Equal(added.Revision, reopened.Revision);
        Assert.Equal(widgets.ScreenId, reopened.Widgets!.ScreenId);
        Assert.Equal(widgets.Revision, reopened.Widgets.Revision);
        Assert.Equal(widgets.Nodes.Select(n => n.Id), reopened.Widgets.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void SetWidgetsRejectsStaleWidgetRevision()
    {
        var project = MakeProject(Guid.NewGuid(), Guid.NewGuid());
        var widgets = MakeWidgets();
        var added = GamesProjectEdits.AddWidgets(project, project.Revision, widgets);

        // Mutate the widget document (bumps its own revision) but pass a stale expected widget revision.
        var edited = widgets.Update("play", "Start", null, null, "OnPlay", widgets.Revision);
        Assert.Throws<InvalidOperationException>(() =>
            GamesProjectEdits.SetWidgets(added, added.Revision, edited.Revision, edited));

        // Correct expected widget revision succeeds and bumps the project revision.
        var updated = GamesProjectEdits.SetWidgets(added, added.Revision, widgets.Revision, edited);
        Assert.Equal(edited.Revision, updated.Widgets!.Revision);
        Assert.Equal(checked(added.Revision + 1), updated.Revision);
    }

    [Fact]
    public void SetWidgetsRejectsAbsentSlotWithoutThrowingNullReference()
    {
        var project = MakeProject(Guid.NewGuid(), Guid.NewGuid());
        Assert.Null(project.Widgets);
        var widgets = MakeWidgets();

        // Absent widget slot must be refused explicitly, never dereferenced (no NullReferenceException).
        var absent = Assert.Throws<InvalidOperationException>(() =>
            GamesProjectEdits.SetWidgets(project, project.Revision, null, widgets));
        Assert.Equal("GamesWidgetsAbsent", absent.Message);

        // A non-null expected revision against an absent slot is also refused as absent.
        Assert.Throws<InvalidOperationException>(() =>
            GamesProjectEdits.SetWidgets(project, project.Revision, 1, widgets));
    }

    [Fact]
    public void SetWidgetsRejectsUnchangedOrSkippedWidgetRevision()
    {
        var project = MakeProject(Guid.NewGuid(), Guid.NewGuid());
        var widgets = MakeWidgets();
        var added = GamesProjectEdits.AddWidgets(project, project.Revision, widgets);

        // Unchanged revision (edited == current) is refused: an edit must advance by exactly one.
        Assert.Throws<InvalidOperationException>(() =>
            GamesProjectEdits.SetWidgets(added, added.Revision, widgets.Revision, widgets));

        // Skipped revision (current + 2) is refused even when the expected revision matches current.
        var skipped = new GamesWidgetDocument(widgets.ScreenId, checked(widgets.Revision + 2), widgets.Nodes);
        Assert.Throws<InvalidOperationException>(() =>
            GamesProjectEdits.SetWidgets(added, added.Revision, widgets.Revision, skipped));

        // The stored widget document is unchanged after both rejections.
        Assert.Equal(widgets.Revision, added.Widgets!.Revision);
        Assert.Equal(project.Revision + 1, added.Revision);
    }

    [Fact]
    public void AddWidgetsRejectsSecondAddAndStaleProjectRevision()
    {
        var project = MakeProject(Guid.NewGuid(), Guid.NewGuid());
        var widgets = MakeWidgets();
        var added = GamesProjectEdits.AddWidgets(project, project.Revision, widgets);

        Assert.Throws<InvalidOperationException>(() => GamesProjectEdits.AddWidgets(added, added.Revision, widgets));
        Assert.Throws<InvalidOperationException>(() => GamesProjectEdits.AddWidgets(project, checked(project.Revision + 1), widgets));
    }

    [Fact]
    public void SetWidgetsRejectsNullAndUnrelatedScreen()
    {
        var project = MakeProject(Guid.NewGuid(), Guid.NewGuid());
        var widgets = MakeWidgets();
        var added = GamesProjectEdits.AddWidgets(project, project.Revision, widgets);

        // Null edited payload is refused without touching the stored widget document.
        Assert.Throws<ArgumentNullException>(() =>
            GamesProjectEdits.SetWidgets(added, added.Revision, widgets.Revision, null!));

        // A different screen identity cannot be committed under this project's widget slot.
        var unrelated = GamesWidgetDocument.Create("other", [new GamesWidgetNode("x", "X", GamesWidgetKind.Panel, null, null, null, null, null)]);
        Assert.Throws<InvalidOperationException>(() =>
            GamesProjectEdits.SetWidgets(added, added.Revision, widgets.Revision, unrelated));

        // The stored widget document is unchanged after both rejections.
        Assert.Equal(widgets.ScreenId, added.Widgets!.ScreenId);
        Assert.Equal(widgets.Revision, added.Widgets.Revision);
    }

    [Fact]
    public async Task EditorServiceAddAndSetWidgetsRoundTripThroughStore()
    {
        var projectID = Guid.NewGuid();
        var sceneID = Guid.NewGuid();
        var fileID = Guid.NewGuid();
        var structural = Guid.NewGuid();
        var store = new FakeGamesProjectStore(new GamesStoredProject(fileID, structural, Guid.NewGuid(), MakeProject(projectID, sceneID)));
        var service = new GamesProjectEditorService(store);

        var widgets = MakeWidgets();
        var added = await service.AddWidgetsAsync(fileID, structural, 1, widgets);
        Assert.NotNull(added.Project.Widgets);
        Assert.Equal(widgets.Revision, added.Project.Widgets!.Revision);
        Assert.Equal(2, added.Project.Revision);

        var edited = widgets.Update("play", "Start", null, null, "OnPlay", widgets.Revision);
        // Stale widget revision (edited's own revision is not the current slot revision) is refused before any save.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetWidgetsAsync(fileID, structural, 2, edited.Revision, edited));
        var updated = await service.SetWidgetsAsync(fileID, structural, 2, widgets.Revision, edited);
        Assert.Equal(edited.Revision, updated.Project.Widgets!.Revision);
        Assert.Equal(3, updated.Project.Revision);

        // Stale project revision must be refused without overwriting the stored draft.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetWidgetsAsync(fileID, structural, 2, edited.Revision, widgets));

        // Absent widget slot is refused through the editor service too (no NullReferenceException).
        var freshStore = new FakeGamesProjectStore(new GamesStoredProject(fileID, structural, Guid.NewGuid(), MakeProject(projectID, sceneID)));
        var freshService = new GamesProjectEditorService(freshStore);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            freshService.SetWidgetsAsync(fileID, structural, 1, null, widgets));
    }

    private sealed class FakeGamesProjectStore(GamesStoredProject initial) : ICanonicalGamesProjectStore
    {
        private GamesStoredProject _current = initial;
        public Task<GamesStoredProject> OpenAsync(Guid fileID, CancellationToken cancellationToken = default)
            => Task.FromResult(_current);
        public Task<GamesStoredProject> SaveAsync(Guid fileID, Guid expectedStructuralRevisionID, long expectedProjectRevision,
            GamesProjectDocument project, CancellationToken cancellationToken = default)
        {
            if (_current.StructuralRevisionID != expectedStructuralRevisionID || _current.Project.Revision != expectedProjectRevision)
                throw new InvalidOperationException("GamesProjectRevisionConflict");
            var captured = project.Capture();
            _current = _current with { ContentRevisionID = Guid.NewGuid(), Project = captured };
            return Task.FromResult(_current);
        }
    }
}
