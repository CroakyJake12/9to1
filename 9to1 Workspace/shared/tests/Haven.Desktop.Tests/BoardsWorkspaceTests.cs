using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Tests;

public sealed class BoardsWorkspaceTests
{
    [Fact]
    public async Task CreateNotebook_MarksPersistsAndFilters()
    {
        var ordinary = NotesDocument.Create("Ordinary");
        var repository = new FakeNotesRepository(ordinary);
        var boards = new BoardsWorkspaceService(repository);

        var created = await boards.CreateNotebookAsync("Study board", CancellationToken.None);
        var listed = await boards.ListNotebooksAsync(CancellationToken.None);

        Assert.Equal("boards", created.Metadata[BoardsWorkspaceService.ProductKey]);
        Assert.Single(listed);
        Assert.Equal(created.Id, listed[0].Id);
        Assert.NotNull(await boards.OpenNotebookAsync(created.Id, CancellationToken.None));
        Assert.Null(await boards.OpenNotebookAsync(ordinary.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteNotebook_IsRecoverableAndPreservesCanonicalIdentityAndContents()
    {
        var repository = new FakeNotesRepository();
        var boards = new BoardsWorkspaceService(repository);
        var notebook = await boards.CreateNotebookAsync("Recoverable", CancellationToken.None);
        var stableId = notebook.Id;
        var originalBlockId = notebook.Sections[0].Pages[0].Blocks[0].Id;

        Assert.True(await boards.DeleteNotebookAsync(stableId, CancellationToken.None));
        Assert.Null(await boards.OpenNotebookAsync(stableId, CancellationToken.None));
        Assert.Empty(await boards.ListNotebooksAsync(CancellationToken.None));
        var trashed = Assert.Single(await boards.ListDeletedNotebooksAsync(CancellationToken.None));
        Assert.Equal(stableId, trashed.Id);

        Assert.True(await boards.RestoreNotebookAsync(stableId, CancellationToken.None));
        var restored = Assert.IsType<NotesDocument>(await boards.OpenNotebookAsync(stableId, CancellationToken.None));
        Assert.Equal(stableId, restored.Id);
        Assert.Equal(originalBlockId, restored.Sections[0].Pages[0].Blocks[0].Id);
        Assert.Single(await boards.ListNotebooksAsync(CancellationToken.None));
        Assert.Empty(await boards.ListDeletedNotebooksAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAndRestorePage_UsesRecoverableMetadataAndKeepsPagePayload()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("Pages", CancellationToken.None);
        var section = notebook.Sections[0];
        var first = section.Pages[0];
        var second = boards.AddPage(notebook, section.Id, "Second");
        var block = boards.AddBlock(notebook, second.Id, NotesBlockKind.Heading, "Retain me");

        Assert.True(boards.DeletePage(notebook, second.Id));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, second.Id));
        Assert.Single(boards.ListDeletedPages(notebook));
        Assert.Throws<KeyNotFoundException>(() => boards.AddBlock(notebook, second.Id, NotesBlockKind.Paragraph));
        Assert.False(boards.DeletePage(notebook, first.Id));

        Assert.True(boards.RestorePage(notebook, second.Id));
        Assert.False(BoardsWorkspaceService.IsPageDeleted(notebook, second.Id));
        Assert.Equal("Retain me", second.Blocks.Single(value => value.Id == block.Id).PlainText);
        Assert.Empty(boards.ListDeletedPages(notebook));
    }

    [Fact]
    public async Task TopicHierarchyUndoRedo_RestoresCanonicalParentAndOrderAndPersistsThroughNotesRepository()
    {
        var repository = new FakeNotesRepository();
        var boards = new BoardsWorkspaceService(repository);
        var notebook = await boards.CreateNotebookAsync("Hierarchy history", CancellationToken.None);
        var section = notebook.Sections[0];
        var root = section.Pages[0];
        var sibling = boards.AddPage(notebook, section.Id, "Sibling");
        var child = boards.AddChildPage(notebook, root.Id, "Topic");

        Assert.True(boards.UndoPageStructure(notebook));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, child.Id));
        await boards.SaveAsync(notebook, "undo topic structure", CancellationToken.None);
        var reopened = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        Assert.NotNull(reopened);
        Assert.True(boards.RedoPageStructure(reopened!));
        Assert.False(BoardsWorkspaceService.IsPageDeleted(reopened!, child.Id));
        Assert.Equal(root.Id, Assert.Single(boards.ListPageHierarchy(reopened).Where(item => item.Page.Id == child.Id)).ParentPageId);
        Assert.Contains(sibling.Id, reopened.Sections.SelectMany(value => value.Pages).Select(value => value.Id));
    }

    [Fact]
    public async Task TopicHierarchyUndoRedo_RestoresMovedSubtreeSectionOrderAndTrashRecoverability()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("Nested move history", CancellationToken.None);
        var source = notebook.Sections[0];
        var root = source.Pages[0];
        var sibling = boards.AddPage(notebook, source.Id, "Keep order");
        var child = boards.AddChildPage(notebook, root.Id, "Topic");
        var grandchild = boards.AddChildPage(notebook, child.Id, "Subtopic");
        var retainedBlock = boards.AddBlock(notebook, grandchild.Id, NotesBlockKind.Paragraph, "Keep nested content");
        var target = boards.AddSection(notebook, "Destination");

        boards.MovePage(notebook, child.Id, target.Id, 0);
        Assert.Equal(target.Id, notebook.Sections.Single(section => section.Pages.Contains(child)).Id);
        Assert.Contains(grandchild, target.Pages);
        Assert.True(boards.UndoPageStructure(notebook));
        Assert.Equal(source.Id, notebook.Sections.Single(section => section.Pages.Contains(child)).Id);
        Assert.Equal(source.Id, notebook.Sections.Single(section => section.Pages.Contains(grandchild)).Id);
        Assert.Equal(root.Id, Assert.Single(boards.ListPageHierarchy(notebook).Where(item => item.Page.Id == child.Id)).ParentPageId);
        Assert.Equal(child.Id, Assert.Single(boards.ListPageHierarchy(notebook).Where(item => item.Page.Id == grandchild.Id)).ParentPageId);
        Assert.Equal(new[] { root.Id, child.Id, grandchild.Id, sibling.Id }, source.Pages.Select(page => page.Id));

        Assert.True(boards.DeletePage(notebook, child.Id));
        var visibleGrandchild = Assert.Single(boards.ListPageHierarchy(notebook).Where(item => item.Page.Id == grandchild.Id));
        Assert.Equal(child.Id, visibleGrandchild.ParentPageId);
        Assert.Equal(root.Id, visibleGrandchild.VisibleParentPageId);
        Assert.Equal(1, visibleGrandchild.Depth);
        Assert.True(boards.UndoPageStructure(notebook));
        Assert.False(BoardsWorkspaceService.IsPageDeleted(notebook, child.Id));
        Assert.Equal(child.Id, Assert.Single(boards.ListPageHierarchy(notebook).Where(item => item.Page.Id == grandchild.Id)).VisibleParentPageId);
        Assert.True(boards.RedoPageStructure(notebook));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, child.Id));
        Assert.True(boards.RestorePage(notebook, child.Id));
        var restoredGrandchild = Assert.Single(boards.ListPageHierarchy(notebook).Where(item => item.Page.Id == grandchild.Id));
        Assert.Equal(child.Id, restoredGrandchild.ParentPageId);
        Assert.Equal(child.Id, restoredGrandchild.VisibleParentPageId);
        Assert.Equal("Keep nested content", retainedBlock.PlainText);
        await boards.SaveAsync(notebook, "persist recovered nested hierarchy", CancellationToken.None);
        var reopened = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        Assert.NotNull(reopened);
        Assert.Equal("Keep nested content", reopened!.Sections.SelectMany(section => section.Pages)
            .Single(page => page.Id == grandchild.Id).Blocks.Single(block => block.Id == retainedBlock.Id).PlainText);
        Assert.Equal(child.Id, Assert.Single(boards.ListPageHierarchy(reopened).Where(item => item.Page.Id == grandchild.Id)).ParentPageId);
    }

    [Fact]
    public async Task TopicHierarchyUndoRedo_RootPageCreationUsesRecoverableTrashWithoutChangingIdentity()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("Root page history", CancellationToken.None);
        var section = notebook.Sections[0];
        var original = section.Pages[0];
        var added = boards.AddPage(notebook, section.Id, "New topic");
        var stableId = added.Id;

        Assert.True(boards.UndoPageStructure(notebook));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, stableId));
        Assert.Contains(added, boards.ListDeletedPages(notebook));
        Assert.True(boards.RedoPageStructure(notebook));
        Assert.False(BoardsWorkspaceService.IsPageDeleted(notebook, stableId));
        Assert.Same(added, section.Pages.Single(page => page.Id == stableId));
        Assert.Same(original, section.Pages.First());
    }

    [Fact]
    public async Task InvalidCyclicTopicMove_DoesNotCreateAnUndoHistoryEntry()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("Cycle guard", CancellationToken.None);
        var root = notebook.Sections[0].Pages[0];
        var child = boards.AddChildPage(notebook, root.Id, "Child");

        Assert.Throws<InvalidOperationException>(() => boards.SetPageParent(notebook, root.Id, child.Id));
        Assert.True(boards.UndoPageStructure(notebook));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, child.Id));
        Assert.False(boards.UndoPageStructure(notebook));
    }

    [Fact]
    public async Task ReorderingTopicSiblings_UndoRedoPreservesNestedDescendantOrder()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("Sibling ordering", CancellationToken.None);
        var section = notebook.Sections[0];
        var root = section.Pages[0];
        var firstTopic = boards.AddChildPage(notebook, root.Id, "First");
        var nested = boards.AddChildPage(notebook, firstTopic.Id, "Nested");
        var secondTopic = boards.AddChildPage(notebook, root.Id, "Second");
        Guid[] ExpectedOrder() => boards.ListPageHierarchy(notebook).Select(item => item.Page.Id).ToArray();

        Assert.Equal(new[] { root.Id, firstTopic.Id, nested.Id, secondTopic.Id }, ExpectedOrder());
        boards.MovePageWithinParent(notebook, firstTopic.Id, 1);
        Assert.Equal(new[] { root.Id, secondTopic.Id, firstTopic.Id, nested.Id }, ExpectedOrder());
        Assert.True(boards.UndoPageStructure(notebook));
        Assert.Equal(new[] { root.Id, firstTopic.Id, nested.Id, secondTopic.Id }, ExpectedOrder());
        Assert.True(boards.RedoPageStructure(notebook));
        Assert.Equal(new[] { root.Id, secondTopic.Id, firstTopic.Id, nested.Id }, ExpectedOrder());
        Assert.Equal(firstTopic.Id, Assert.Single(boards.ListPageHierarchy(notebook).Where(item => item.Page.Id == nested.Id)).ParentPageId);
    }

    [Fact]
    public async Task NoOpPageMove_DoesNotConsumeThePreviousStructureUndoEntry()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("No-op move history", CancellationToken.None);
        var section = notebook.Sections[0];
        var root = section.Pages[0];
        var child = boards.AddChildPage(notebook, root.Id, "Child");
        var historyBefore = notebook.Metadata[BoardsWorkspaceService.PageStructureHistoryKey];
        var timestampBefore = notebook.UpdatedAt;

        boards.MovePage(notebook, root.Id, section.Id, 0);

        Assert.Equal(historyBefore, notebook.Metadata[BoardsWorkspaceService.PageStructureHistoryKey]);
        Assert.Equal(timestampBefore, notebook.UpdatedAt);
        Assert.True(boards.UndoPageStructure(notebook));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, child.Id));
        Assert.Contains(root, section.Pages);
        Assert.False(boards.UndoPageStructure(notebook));
    }

    [Fact]
    public async Task TopicStructureUndo_RespectsViewModeAndRetainsHistoryUntilEditModeReturns()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("View mode history", CancellationToken.None);
        var root = notebook.Sections[0].Pages[0];
        var child = boards.AddChildPage(notebook, root.Id, "Protected topic");
        boards.SetEditMode(notebook, child.Id, BoardsPageEditMode.View);
        var timestampBefore = notebook.UpdatedAt;
        var metadataBefore = notebook.Metadata.OrderBy(pair => pair.Key).ToArray();
        var pagesBefore = notebook.Sections.SelectMany(section => section.Pages).Select(page => page.Id).ToArray();

        Assert.Throws<InvalidOperationException>(() => boards.UndoPageStructure(notebook));
        Assert.False(BoardsWorkspaceService.IsPageDeleted(notebook, child.Id));
        Assert.Equal(timestampBefore, notebook.UpdatedAt);
        Assert.Equal(metadataBefore, notebook.Metadata.OrderBy(pair => pair.Key).ToArray());
        Assert.Equal(pagesBefore, notebook.Sections.SelectMany(section => section.Pages).Select(page => page.Id));
        boards.SetEditMode(notebook, child.Id, BoardsPageEditMode.Edit);
        Assert.True(boards.UndoPageStructure(notebook));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, child.Id));
    }

    [Fact]
    public async Task TopicStructureRedo_RespectsViewModeWithoutPublishingHistoryOrTimestampChanges()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("View mode redo", CancellationToken.None);
        var root = notebook.Sections[0].Pages[0];
        var child = boards.AddChildPage(notebook, root.Id, "Protected topic");
        Assert.True(boards.UndoPageStructure(notebook));
        boards.SetEditMode(notebook, child.Id, BoardsPageEditMode.View);
        var timestampBefore = notebook.UpdatedAt;
        var metadataBefore = notebook.Metadata.OrderBy(pair => pair.Key).ToArray();
        var pagesBefore = notebook.Sections.SelectMany(section => section.Pages).Select(page => page.Id).ToArray();

        Assert.Throws<InvalidOperationException>(() => boards.RedoPageStructure(notebook));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, child.Id));
        Assert.Equal(timestampBefore, notebook.UpdatedAt);
        Assert.Equal(metadataBefore, notebook.Metadata.OrderBy(pair => pair.Key).ToArray());
        Assert.Equal(pagesBefore, notebook.Sections.SelectMany(section => section.Pages).Select(page => page.Id));
    }

    [Fact]
    public async Task TopicStructureUndo_AllowsUnrelatedViewPage()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("Unrelated view page", CancellationToken.None);
        var section = notebook.Sections[0];
        var unrelated = section.Pages[0];
        var created = boards.AddPage(notebook, section.Id, "Editable page");
        boards.SetEditMode(notebook, unrelated.Id, BoardsPageEditMode.View);

        Assert.True(boards.UndoPageStructure(notebook));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, created.Id));
        Assert.Equal(BoardsPageEditMode.View, boards.GetEditMode(notebook, unrelated.Id));
    }

    [Fact]
    public async Task TopicStructureUndo_RequiresTheAffectedParentToRemainEditable()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("Parent view scope", CancellationToken.None);
        var root = notebook.Sections[0].Pages[0];
        var child = boards.AddChildPage(notebook, root.Id, "Child");
        boards.SetEditMode(notebook, root.Id, BoardsPageEditMode.View);
        var historyBefore = notebook.Metadata[BoardsWorkspaceService.PageStructureHistoryKey];

        Assert.Throws<InvalidOperationException>(() => boards.UndoPageStructure(notebook));
        Assert.Equal(historyBefore, notebook.Metadata[BoardsWorkspaceService.PageStructureHistoryKey]);
        Assert.False(BoardsWorkspaceService.IsPageDeleted(notebook, child.Id));
    }

    [Fact]
    public async Task PageTrashAndHistory_RequireEditModeForAffectedParentAndDescendants()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("Subtree trash permissions", CancellationToken.None);
        var root = notebook.Sections[0].Pages[0];
        var child = boards.AddChildPage(notebook, root.Id, "Parent topic");
        var grandchild = boards.AddChildPage(notebook, child.Id, "Nested topic");

        boards.SetEditMode(notebook, grandchild.Id, BoardsPageEditMode.View);
        var descendantViewMetadata = notebook.Metadata.OrderBy(pair => pair.Key).ToArray();
        var descendantViewTimestamp = notebook.UpdatedAt;
        Assert.Throws<InvalidOperationException>(() => boards.DeletePage(notebook, child.Id));
        Assert.Equal(descendantViewTimestamp, notebook.UpdatedAt);
        Assert.Equal(descendantViewMetadata, notebook.Metadata.OrderBy(pair => pair.Key).ToArray());
        Assert.False(BoardsWorkspaceService.IsPageDeleted(notebook, child.Id));

        boards.SetEditMode(notebook, grandchild.Id, BoardsPageEditMode.Edit);
        boards.SetEditMode(notebook, root.Id, BoardsPageEditMode.View);
        var parentViewMetadata = notebook.Metadata.OrderBy(pair => pair.Key).ToArray();
        var parentViewTimestamp = notebook.UpdatedAt;
        Assert.Throws<InvalidOperationException>(() => boards.DeletePage(notebook, child.Id));
        Assert.Equal(parentViewTimestamp, notebook.UpdatedAt);
        Assert.Equal(parentViewMetadata, notebook.Metadata.OrderBy(pair => pair.Key).ToArray());
        Assert.False(BoardsWorkspaceService.IsPageDeleted(notebook, child.Id));

        boards.SetEditMode(notebook, root.Id, BoardsPageEditMode.Edit);
        Assert.True(boards.DeletePage(notebook, child.Id));
        boards.SetEditMode(notebook, grandchild.Id, BoardsPageEditMode.View);
        var trashViewMetadata = notebook.Metadata.OrderBy(pair => pair.Key).ToArray();
        var trashViewTimestamp = notebook.UpdatedAt;
        Assert.Throws<InvalidOperationException>(() => boards.RestorePage(notebook, child.Id));
        Assert.Throws<InvalidOperationException>(() => boards.UndoPageStructure(notebook));
        Assert.Equal(trashViewTimestamp, notebook.UpdatedAt);
        Assert.Equal(trashViewMetadata, notebook.Metadata.OrderBy(pair => pair.Key).ToArray());
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, child.Id));

        boards.SetEditMode(notebook, grandchild.Id, BoardsPageEditMode.Edit);
        Assert.True(boards.UndoPageStructure(notebook));
        Assert.False(BoardsWorkspaceService.IsPageDeleted(notebook, child.Id));
        Assert.True(boards.RedoPageStructure(notebook));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, child.Id));
    }

    [Fact]
    public async Task MalformedTopicHistory_FailsClosedWithoutReplacingCanonicalMetadata()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("Malformed hierarchy history", CancellationToken.None);
        const string malformed = "{\"undo\":null,\"redo\":[]}";
        notebook.Metadata[BoardsWorkspaceService.PageStructureHistoryKey] = malformed;

        Assert.Throws<InvalidDataException>(() => boards.UndoPageStructure(notebook));
        Assert.Equal(malformed, notebook.Metadata[BoardsWorkspaceService.PageStructureHistoryKey]);
        Assert.Single(notebook.Sections[0].Pages);
    }

    [Fact]
    public async Task CyclicTopicHistory_FailsBeforeChangingAnyNotesState()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("Cyclic history", CancellationToken.None);
        var section = notebook.Sections[0];
        var root = section.Pages[0];
        var child = boards.AddChildPage(notebook, root.Id, "Child");
        var cyclicState = new
        {
            sectionIds = new[] { section.Id },
            parents = new Dictionary<Guid, Guid> { [root.Id] = child.Id, [child.Id] = root.Id },
            positions = new Dictionary<Guid, object>
            {
                [root.Id] = new { sectionId = section.Id, order = 0 },
                [child.Id] = new { sectionId = section.Id, order = 1 }
            },
            deleted = Array.Empty<Guid>()
        };
        var corruptHistory = System.Text.Json.JsonSerializer.Serialize(new { undo = new[] { cyclicState }, redo = Array.Empty<object>() });
        notebook.Metadata[BoardsWorkspaceService.PageStructureHistoryKey] = corruptHistory;
        var timestampBefore = notebook.UpdatedAt;
        var metadataBefore = notebook.Metadata.OrderBy(pair => pair.Key).ToArray();
        var pagesBefore = section.Pages.Select(page => page.Id).ToArray();

        Assert.Throws<InvalidDataException>(() => boards.UndoPageStructure(notebook));
        Assert.Equal(timestampBefore, notebook.UpdatedAt);
        Assert.Equal(metadataBefore, notebook.Metadata.OrderBy(pair => pair.Key).ToArray());
        Assert.Equal(pagesBefore, section.Pages.Select(page => page.Id));
    }

    [Fact]
    public async Task HistoryThatWouldEmptyACapturedSection_FailsBeforePublishing()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("Empty section target", CancellationToken.None);
        var section = notebook.Sections[0];
        var root = section.Pages[0];
        var invalidState = new
        {
            sectionIds = new[] { section.Id },
            parents = new Dictionary<Guid, Guid>(),
            positions = new Dictionary<Guid, object> { [root.Id] = new { sectionId = section.Id, order = 0 } },
            deleted = new[] { root.Id }
        };
        notebook.Metadata[BoardsWorkspaceService.PageStructureHistoryKey] = System.Text.Json.JsonSerializer.Serialize(
            new { undo = new[] { invalidState }, redo = Array.Empty<object>() });
        var timestampBefore = notebook.UpdatedAt;
        var metadataBefore = notebook.Metadata.OrderBy(pair => pair.Key).ToArray();
        var pagesBefore = section.Pages.ToArray();

        Assert.Throws<InvalidDataException>(() => boards.UndoPageStructure(notebook));
        Assert.Equal(timestampBefore, notebook.UpdatedAt);
        Assert.Equal(metadataBefore, notebook.Metadata.OrderBy(pair => pair.Key).ToArray());
        Assert.Equal(pagesBefore, section.Pages);
    }

    [Fact]
    public async Task UnknownParentInKnownHistory_FailsWithoutPublishingAnyNotesState()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("Orphaned hierarchy history", CancellationToken.None);
        var section = notebook.Sections[0];
        var root = section.Pages[0];
        var invalidState = new
        {
            sectionIds = new[] { section.Id },
            parents = new Dictionary<Guid, Guid> { [root.Id] = Guid.NewGuid() },
            positions = new Dictionary<Guid, object> { [root.Id] = new { sectionId = section.Id, order = 0 } },
            deleted = Array.Empty<Guid>()
        };
        notebook.Metadata[BoardsWorkspaceService.PageStructureHistoryKey] = System.Text.Json.JsonSerializer.Serialize(
            new { undo = new[] { invalidState }, redo = Array.Empty<object>() });
        var timestampBefore = notebook.UpdatedAt;
        var metadataBefore = notebook.Metadata.OrderBy(pair => pair.Key).ToArray();
        var pagesBefore = section.Pages.ToArray();

        Assert.Throws<InvalidDataException>(() => boards.UndoPageStructure(notebook));
        Assert.Equal(timestampBefore, notebook.UpdatedAt);
        Assert.Equal(metadataBefore, notebook.Metadata.OrderBy(pair => pair.Key).ToArray());
        Assert.Equal(pagesBefore, section.Pages);
    }

    [Fact]
    public async Task ScopedHierarchyUndoRedo_SerializesAndReopensThroughOwningCloneRepository()
    {
        var repository = new BoardsRecoveryAcceptanceTests.CloningNotesRepository();
        var boards = new BoardsWorkspaceService(repository);
        var notebook = await boards.CreateNotebookAsync("Serialized scoped history", CancellationToken.None);
        var sectionA = notebook.Sections[0];
        var pageA = boards.AddPage(notebook, sectionA.Id, "Page in A");
        await boards.SaveAsync(notebook, "Persist page in A", CancellationToken.None);

        boards = new BoardsWorkspaceService(repository);
        notebook = (await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None))!;
        var sectionB = boards.AddSection(notebook, "Later B");
        var rootB = sectionB.Pages.Single();
        await boards.SaveAsync(notebook, "Persist later section B", CancellationToken.None);

        boards = new BoardsWorkspaceService(repository);
        notebook = (await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None))!;
        var childB = boards.AddChildPage(notebook, rootB.Id, "Child B");
        var payload = boards.AddBlock(notebook, childB.Id, NotesBlockKind.Paragraph, "Serialized child payload");
        await boards.SaveAsync(notebook, "Persist nested child and history", CancellationToken.None);

        boards = new BoardsWorkspaceService(repository);
        notebook = (await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None))!;
        Assert.True(boards.UndoPageStructure(notebook));
        await boards.SaveAsync(notebook, "Undo child after reload", CancellationToken.None);
        boards = new BoardsWorkspaceService(repository);
        notebook = (await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None))!;
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, childB.Id));
        Assert.Equal(rootB.Id, Assert.Single(boards.ListPageHierarchy(notebook, includeDeleted: true)
            .Where(item => item.Page.Id == childB.Id)).ParentPageId);
        Assert.Equal("Serialized child payload", notebook.Sections.SelectMany(section => section.Pages)
            .Single(page => page.Id == childB.Id).Blocks.Single(block => block.Id == payload.Id).PlainText);

        Assert.True(boards.UndoPageStructure(notebook));
        await boards.SaveAsync(notebook, "Undo older A page after reload", CancellationToken.None);
        boards = new BoardsWorkspaceService(repository);
        notebook = (await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None))!;
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, pageA.Id));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, childB.Id));
        Assert.Equal(rootB.Id, Assert.Single(boards.ListPageHierarchy(notebook, includeDeleted: true)
            .Where(item => item.Page.Id == childB.Id)).ParentPageId);

        Assert.True(boards.RedoPageStructure(notebook));
        await boards.SaveAsync(notebook, "Redo A page after reload", CancellationToken.None);
        boards = new BoardsWorkspaceService(repository);
        notebook = (await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None))!;
        Assert.True(boards.RedoPageStructure(notebook));
        await boards.SaveAsync(notebook, "Redo child after reload", CancellationToken.None);
        boards = new BoardsWorkspaceService(repository);
        notebook = (await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None))!;
        Assert.False(BoardsWorkspaceService.IsPageDeleted(notebook, childB.Id));
        Assert.Equal(rootB.Id, Assert.Single(boards.ListPageHierarchy(notebook).Where(item => item.Page.Id == childB.Id)).ParentPageId);
        Assert.Equal("Serialized child payload", notebook.Sections.SelectMany(section => section.Pages)
            .Single(page => page.Id == childB.Id).Blocks.Single(block => block.Id == payload.Id).PlainText);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("[\"00000000-0000-0000-0000-000000000001\"]")]
    public async Task MalformedOrOrphanedPageTrashMetadata_FailsClosedWithoutReplacingSource(string malformed)
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("Invalid page Trash metadata", CancellationToken.None);
        var root = notebook.Sections[0].Pages[0];
        boards.AddPage(notebook, notebook.Sections[0].Id, "Keep original page");
        notebook.Metadata[BoardsWorkspaceService.DeletedPagesKey] = malformed;
        var timestampBefore = notebook.UpdatedAt;
        var metadataBefore = notebook.Metadata.OrderBy(pair => pair.Key).ToArray();
        var pagesBefore = notebook.Sections.SelectMany(section => section.Pages).Select(page => page.Id).ToArray();

        Assert.Throws<InvalidDataException>(() => boards.ListPageHierarchy(notebook));
        Assert.Throws<InvalidDataException>(() => boards.DeletePage(notebook, root.Id));
        Assert.Equal(timestampBefore, notebook.UpdatedAt);
        Assert.Equal(metadataBefore, notebook.Metadata.OrderBy(pair => pair.Key).ToArray());
        Assert.Equal(pagesBefore, notebook.Sections.SelectMany(section => section.Pages).Select(page => page.Id));
    }

    [Fact]
    public async Task OlderUndoRedo_PreservesLaterSectionNestedTrashParentAndPayloadWithoutViewInterference()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("Scoped later-section history", CancellationToken.None);
        var sectionA = notebook.Sections[0];
        var pageA = boards.AddPage(notebook, sectionA.Id, "Older page");
        var sectionB = boards.AddSection(notebook, "Later section");
        var rootB = sectionB.Pages.Single();
        var childB = boards.AddChildPage(notebook, rootB.Id, "Child");
        var grandchildB = boards.AddChildPage(notebook, childB.Id, "Grandchild");
        var payload = boards.AddBlock(notebook, grandchildB.Id, NotesBlockKind.Paragraph, "Retain deleted content");

        Assert.True(boards.UndoPageStructure(notebook)); // undo grandchild creation
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, grandchildB.Id));
        Assert.Equal(childB.Id, Assert.Single(boards.ListPageHierarchy(notebook, includeDeleted: true)
            .Where(item => item.Page.Id == grandchildB.Id)).ParentPageId);
        Assert.True(boards.UndoPageStructure(notebook)); // undo child creation
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, childB.Id));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, grandchildB.Id));
        Assert.Equal(rootB.Id, Assert.Single(boards.ListPageHierarchy(notebook, includeDeleted: true)
            .Where(item => item.Page.Id == childB.Id)).ParentPageId);
        Assert.Equal(childB.Id, Assert.Single(boards.ListPageHierarchy(notebook, includeDeleted: true)
            .Where(item => item.Page.Id == grandchildB.Id)).ParentPageId);
        boards.SetEditMode(notebook, childB.Id, BoardsPageEditMode.View);
        var parentMetadataBefore = notebook.Metadata[BoardsWorkspaceService.PageParentsKey];

        Assert.True(boards.UndoPageStructure(notebook)); // undo page A; later section is outside this operation
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, pageA.Id));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, childB.Id));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, grandchildB.Id));
        Assert.Equal(parentMetadataBefore, notebook.Metadata[BoardsWorkspaceService.PageParentsKey]);
        Assert.Contains(childB.Id.ToString("D"), notebook.Metadata[BoardsWorkspaceService.PageParentsKey]);
        Assert.Contains(grandchildB.Id.ToString("D"), notebook.Metadata[BoardsWorkspaceService.PageParentsKey]);
        Assert.Contains(childB.Id.ToString("D"), notebook.Metadata[BoardsWorkspaceService.DeletedPagesKey]);
        Assert.Contains(grandchildB.Id.ToString("D"), notebook.Metadata[BoardsWorkspaceService.DeletedPagesKey]);
        Assert.Equal(BoardsPageEditMode.View, boards.GetEditMode(notebook, childB.Id));
        Assert.Equal("Retain deleted content", grandchildB.Blocks.Single(block => block.Id == payload.Id).PlainText);

        Assert.True(boards.RedoPageStructure(notebook)); // restore page A only
        Assert.False(BoardsWorkspaceService.IsPageDeleted(notebook, pageA.Id));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, childB.Id));
        boards.SetEditMode(notebook, childB.Id, BoardsPageEditMode.Edit);
        Assert.True(boards.RedoPageStructure(notebook)); // restore child
        Assert.False(BoardsWorkspaceService.IsPageDeleted(notebook, childB.Id));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, grandchildB.Id));
        Assert.Equal(rootB.Id, Assert.Single(boards.ListPageHierarchy(notebook).Where(item => item.Page.Id == childB.Id)).ParentPageId);
        Assert.True(boards.RedoPageStructure(notebook)); // restore grandchild and retain its payload
        Assert.False(BoardsWorkspaceService.IsPageDeleted(notebook, grandchildB.Id));
        Assert.Equal(childB.Id, Assert.Single(boards.ListPageHierarchy(notebook).Where(item => item.Page.Id == grandchildB.Id)).ParentPageId);
        Assert.Equal("Retain deleted content", grandchildB.Blocks.Single(block => block.Id == payload.Id).PlainText);
        Assert.False(notebook.Metadata.ContainsKey(BoardsWorkspaceService.DeletedPagesKey));
        Assert.Empty(boards.ListDeletedPages(notebook));
    }

    [Fact]
    public async Task FailedPageCreation_DoesNotConsumeOrAddAnUndoEntry()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("Failed page creation", CancellationToken.None);
        var root = notebook.Sections[0].Pages[0];
        var child = boards.AddChildPage(notebook, root.Id, "Child");

        Assert.Throws<KeyNotFoundException>(() => boards.AddPage(notebook, Guid.NewGuid(), "Invalid"));
        Assert.True(boards.UndoPageStructure(notebook));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, child.Id));
        Assert.False(boards.UndoPageStructure(notebook));
    }

    [Fact]
    public async Task UndoPageCreation_DoesNotTrashTheOnlyPageInALaterAddedSection()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("Section-safe history", CancellationToken.None);
        var originalSection = notebook.Sections[0];
        var addedPage = boards.AddPage(notebook, originalSection.Id, "Undo this page");
        var addedSection = boards.AddSection(notebook, "Keep this section");
        var sectionPage = addedSection.Pages.Single();

        Assert.True(boards.UndoPageStructure(notebook));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, addedPage.Id));
        Assert.False(BoardsWorkspaceService.IsPageDeleted(notebook, sectionPage.Id));
        Assert.Same(sectionPage, addedSection.Pages.Single());
        Assert.Equal("Keep this section", addedSection.Title);
    }

    [Fact]
    public async Task OlderUndoAndRedo_PreserveLaterSectionNestedTrashAndPayloadMetadata()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("Later section metadata", CancellationToken.None);
        var sectionA = notebook.Sections[0];
        var pageA = boards.AddPage(notebook, sectionA.Id, "Undo A page");
        var sectionB = boards.AddSection(notebook, "Later section");
        var rootB = sectionB.Pages.Single();
        var childB = boards.AddChildPage(notebook, rootB.Id, "Child");
        var grandchildB = boards.AddChildPage(notebook, childB.Id, "Grandchild");
        var payload = boards.AddBlock(notebook, grandchildB.Id, NotesBlockKind.Paragraph, "Retain in nested Trash");

        Assert.True(boards.UndoPageStructure(notebook)); // undo grandchild creation
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, grandchildB.Id));
        Assert.Equal(childB.Id, Assert.Single(boards.ListPageHierarchy(notebook, includeDeleted: true)
            .Where(item => item.Page.Id == grandchildB.Id)).ParentPageId);
        Assert.True(boards.UndoPageStructure(notebook)); // undo child creation
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, childB.Id));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, grandchildB.Id));
        boards.SetEditMode(notebook, childB.Id, BoardsPageEditMode.View);
        var parentMetadataBeforeOlderUndo = notebook.Metadata[BoardsWorkspaceService.PageParentsKey];

        Assert.True(boards.UndoPageStructure(notebook)); // undo older page in A; B is untouched
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, pageA.Id));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, childB.Id));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, grandchildB.Id));
        Assert.Equal(BoardsPageEditMode.View, boards.GetEditMode(notebook, childB.Id));
        Assert.Equal(parentMetadataBeforeOlderUndo, notebook.Metadata[BoardsWorkspaceService.PageParentsKey]);
        Assert.Contains(childB.Id.ToString("D"), notebook.Metadata[BoardsWorkspaceService.PageParentsKey]);
        Assert.Contains(grandchildB.Id.ToString("D"), notebook.Metadata[BoardsWorkspaceService.PageParentsKey]);
        Assert.Contains(childB.Id.ToString("D"), notebook.Metadata[BoardsWorkspaceService.DeletedPagesKey]);
        Assert.Contains(grandchildB.Id.ToString("D"), notebook.Metadata[BoardsWorkspaceService.DeletedPagesKey]);
        Assert.Equal("Retain in nested Trash", grandchildB.Blocks.Single(block => block.Id == payload.Id).PlainText);

        Assert.True(boards.RedoPageStructure(notebook)); // restore A page without changing B
        Assert.False(BoardsWorkspaceService.IsPageDeleted(notebook, pageA.Id));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, childB.Id));
        boards.SetEditMode(notebook, childB.Id, BoardsPageEditMode.Edit);
        Assert.True(boards.RedoPageStructure(notebook)); // restore child
        Assert.False(BoardsWorkspaceService.IsPageDeleted(notebook, childB.Id));
        Assert.True(BoardsWorkspaceService.IsPageDeleted(notebook, grandchildB.Id));
        Assert.Equal(rootB.Id, Assert.Single(boards.ListPageHierarchy(notebook)
            .Where(item => item.Page.Id == childB.Id)).ParentPageId);
        Assert.True(boards.RedoPageStructure(notebook)); // restore grandchild and payload
        Assert.False(BoardsWorkspaceService.IsPageDeleted(notebook, grandchildB.Id));
        Assert.Equal(childB.Id, Assert.Single(boards.ListPageHierarchy(notebook)
            .Where(item => item.Page.Id == grandchildB.Id)).ParentPageId);
        Assert.Equal("Retain in nested Trash", grandchildB.Blocks.Single(block => block.Id == payload.Id).PlainText);
        Assert.False(notebook.Metadata.ContainsKey(BoardsWorkspaceService.DeletedPagesKey));
        Assert.Empty(boards.ListDeletedPages(notebook));
    }

    [Fact]
    public async Task TypedOperations_CanDeleteAndRestoreNotebookWithoutPurgingIt()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var executor = new BoardsOperationExecutor(boards);
        var notebook = await boards.CreateNotebookAsync("API", CancellationToken.None);

        await executor.ExecuteAsync(notebook.Id, new BoardsOperation(BoardsOperationKind.DeleteNotebook), CancellationToken.None);
        Assert.Null(await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None));
        await executor.ExecuteAsync(notebook.Id, new BoardsOperation(BoardsOperationKind.RestoreNotebook), CancellationToken.None);

        Assert.NotNull(await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None));
    }

    [Fact]
    public async Task TypedOperations_UndoRedoTopicStructurePersistThroughCanonicalNotesRepository()
    {
        var repository = new FakeNotesRepository();
        var boards = new BoardsWorkspaceService(repository);
        var executor = new BoardsOperationExecutor(boards);
        var notebook = await boards.CreateNotebookAsync("Typed hierarchy history", CancellationToken.None);
        var root = notebook.Sections[0].Pages[0];
        var child = boards.AddChildPage(notebook, root.Id, "Typed child");
        await boards.SaveAsync(notebook, "Persist child before typed undo", CancellationToken.None);

        var undoResult = await ExecuteHierarchyActionAsync(executor, boards, notebook.Id,
            new BoardsOperation(BoardsOperationKind.UndoPageStructure));
        Assert.Equal(new BoardsDeepLink(notebook.Id).ToString(), undoResult.ActivityTarget);
        var afterUndo = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        Assert.NotNull(afterUndo);
        Assert.True(BoardsWorkspaceService.IsPageDeleted(afterUndo!, child.Id));

        await ExecuteHierarchyActionAsync(executor, boards, notebook.Id,
            new BoardsOperation(BoardsOperationKind.RedoPageStructure));
        var afterRedo = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        Assert.NotNull(afterRedo);
        Assert.False(BoardsWorkspaceService.IsPageDeleted(afterRedo!, child.Id));
        Assert.Equal(root.Id, Assert.Single(boards.ListPageHierarchy(afterRedo!).Where(item => item.Page.Id == child.Id)).ParentPageId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteHierarchyActionAsync(executor, boards, notebook.Id,
            new BoardsOperation(BoardsOperationKind.RedoPageStructure)));
    }

    [Fact]
    public async Task TypedAddChild_RetryAfterFreshReopenReturnsReceiptAndRejectsChangedPayload()
    {
        var repository = new BoardsRecoveryAcceptanceTests.CloningNotesRepository();
        var boards = new BoardsWorkspaceService(repository);
        var executor = new BoardsOperationExecutor(boards);
        var notebook = await boards.CreateNotebookAsync("Replay-safe child", CancellationToken.None);
        var rootId = notebook.Sections[0].Pages[0].Id;
        await boards.SaveAsync(notebook, "Persist replay parent", CancellationToken.None);
        var baseDocument = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        var request = new BoardsOperation(BoardsOperationKind.AddChildPage, PageId: rootId, Text: "Original child",
            OperationId: Guid.NewGuid(), ExpectedDocumentVersion: baseDocument!.Version);

        var firstResult = await executor.ExecuteAsync(notebook.Id, request, CancellationToken.None);
        var afterCommit = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        var committedVersion = afterCommit!.Version;
        var committedHistory = afterCommit.Metadata[BoardsWorkspaceService.PageStructureHistoryKey];
        var childId = firstResult.PageId!.Value;

        var freshBoards = new BoardsWorkspaceService(repository);
        var freshExecutor = new BoardsOperationExecutor(freshBoards);
        var replayResult = await freshExecutor.ExecuteAsync(notebook.Id, request, CancellationToken.None);
        var afterReplay = await freshBoards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        Assert.NotNull(afterReplay);
        Assert.Equal(firstResult, replayResult);
        Assert.Equal(committedVersion, afterReplay!.Version);
        Assert.Equal(committedHistory, afterReplay.Metadata[BoardsWorkspaceService.PageStructureHistoryKey]);
        Assert.Single(afterReplay.Sections.SelectMany(section => section.Pages).Where(page => page.Id == childId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => freshExecutor.ExecuteAsync(notebook.Id,
            request with { Text = "Changed child payload" }, CancellationToken.None));
        var afterConflict = await freshBoards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        Assert.NotNull(afterConflict);
        Assert.Equal(committedVersion, afterConflict!.Version);
        Assert.Equal(committedHistory, afterConflict.Metadata[BoardsWorkspaceService.PageStructureHistoryKey]);
        Assert.Single(afterConflict.Sections.SelectMany(section => section.Pages).Where(page => page.Id == childId));
        Assert.Equal("Original child", afterConflict.Sections.SelectMany(section => section.Pages)
            .Single(page => page.Id == childId).Title);
    }

    [Fact]
    public async Task TypedHierarchyAction_StaleExpectedVersionConflictsBeforeMutation()
    {
        var repository = new BoardsRecoveryAcceptanceTests.CloningNotesRepository();
        var boards = new BoardsWorkspaceService(repository);
        var executor = new BoardsOperationExecutor(boards);
        var notebook = await boards.CreateNotebookAsync("Stale hierarchy request", CancellationToken.None);
        var rootId = notebook.Sections[0].Pages[0].Id;
        await boards.SaveAsync(notebook, "Persist stale request target", CancellationToken.None);
        var current = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        var currentHistory = current!.Revisions.Count;
        var currentVersion = current.Version;
        var staleRequest = new BoardsOperation(BoardsOperationKind.AddChildPage, PageId: rootId, Text: "Must not exist",
            OperationId: Guid.NewGuid(), ExpectedDocumentVersion: currentVersion - 1);

        await Assert.ThrowsAsync<NotesRevisionConflictException>(() =>
            executor.ExecuteAsync(notebook.Id, staleRequest, CancellationToken.None));
        var after = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        Assert.NotNull(after);
        Assert.Equal(currentVersion, after!.Version);
        Assert.Equal(currentHistory, after.Revisions.Count);
        Assert.Single(boards.ListPageHierarchy(after));
    }

    [Fact]
    public async Task TypedOperations_CreateReparentAndReorderNestedPagesThroughCanonicalNotesOwner()
    {
        var repository = new FakeNotesRepository();
        var boards = new BoardsWorkspaceService(repository);
        var executor = new BoardsOperationExecutor(boards);
        var notebook = await boards.CreateNotebookAsync("Typed nested operations", CancellationToken.None);
        var section = notebook.Sections[0];
        var root = section.Pages[0];
        var otherRoot = boards.AddPage(notebook, section.Id, "Other root");
        await boards.SaveAsync(notebook, "Persist second root", CancellationToken.None);

        var childResult = await ExecuteHierarchyActionAsync(executor, boards, notebook.Id,
            new BoardsOperation(BoardsOperationKind.AddChildPage, PageId: otherRoot.Id, Text: "Move to root"));
        var childId = childResult.PageId!.Value;
        await ExecuteHierarchyActionAsync(executor, boards, notebook.Id,
            new BoardsOperation(BoardsOperationKind.SetPageParent, PageId: childId, ParentPageId: root.Id));
        var secondChildResult = await ExecuteHierarchyActionAsync(executor, boards, notebook.Id,
            new BoardsOperation(BoardsOperationKind.AddChildPage, PageId: root.Id, Text: "Second child"));
        var secondChildId = secondChildResult.PageId!.Value;

        await ExecuteHierarchyActionAsync(executor, boards, notebook.Id,
            new BoardsOperation(BoardsOperationKind.MovePageWithinParent, PageId: childId, Direction: 1));
        var reopened = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        Assert.NotNull(reopened);
        var children = boards.ListPageHierarchy(reopened!).Where(item => item.ParentPageId == root.Id).ToArray();
        Assert.Equal(new[] { secondChildId, childId }, children.Select(item => item.Page.Id));
        Assert.Equal(root.Id, Assert.Single(children.Where(item => item.Page.Id == childId)).ParentPageId);
    }

    [Fact]
    public async Task TypedOperations_RenameNestedPagePersistsWithoutChangingItsIdentityOrParent()
    {
        var repository = new FakeNotesRepository();
        var boards = new BoardsWorkspaceService(repository);
        var executor = new BoardsOperationExecutor(boards);
        var notebook = await boards.CreateNotebookAsync("Typed page rename", CancellationToken.None);
        var section = boards.AddSection(notebook, "Second section");
        var root = section.Pages[0];
        var child = boards.AddChildPage(notebook, root.Id, "Old title");
        await boards.SaveAsync(notebook, "Persist nested page", CancellationToken.None);

        await ExecuteHierarchyActionAsync(executor, boards, notebook.Id,
            new BoardsOperation(BoardsOperationKind.RenamePage, PageId: child.Id, Text: "Reviewed title"));

        var reopened = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        Assert.NotNull(reopened);
        var renamed = Assert.Single(boards.ListPageHierarchy(reopened!).Where(item => item.Page.Id == child.Id));
        Assert.Equal("Reviewed title", renamed.Page.Title);
        Assert.Equal(root.Id, renamed.ParentPageId);
        Assert.Equal(child.Id, renamed.Page.Id);
    }

    [Fact]
    public async Task TypedOperations_MovePageMovesItsSubtreeAndRoundTripsThroughHierarchyHistory()
    {
        var repository = new FakeNotesRepository();
        var boards = new BoardsWorkspaceService(repository);
        var executor = new BoardsOperationExecutor(boards);
        var notebook = await boards.CreateNotebookAsync("Typed subtree move", CancellationToken.None);
        var source = notebook.Sections[0];
        var root = source.Pages[0];
        boards.AddPage(notebook, source.Id, "Keep source section");
        var child = boards.AddChildPage(notebook, root.Id, "Moved subtree");
        var grandchild = boards.AddChildPage(notebook, child.Id, "Nested payload");
        var payload = boards.AddBlock(notebook, grandchild.Id, NotesBlockKind.Paragraph, "Keep me");
        var target = boards.AddSection(notebook, "Destination");
        await boards.SaveAsync(notebook, "Persist source subtree and target section", CancellationToken.None);

        await ExecuteHierarchyActionAsync(executor, boards, notebook.Id,
            new BoardsOperation(BoardsOperationKind.MovePage, PageId: child.Id,
                TargetSectionId: target.Id, TargetIndex: 1));
        var moved = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        Assert.NotNull(moved);
        Assert.Equal(target.Id, Assert.Single(moved!.Sections.Where(section => section.Pages.Any(page => page.Id == child.Id))).Id);
        Assert.Contains(grandchild.Id, moved.Sections.SelectMany(section => section.Pages).Select(page => page.Id));
        Assert.Null(Assert.Single(boards.ListPageHierarchy(moved).Where(item => item.Page.Id == child.Id)).ParentPageId);
        Assert.Equal(child.Id, Assert.Single(boards.ListPageHierarchy(moved).Where(item => item.Page.Id == grandchild.Id)).ParentPageId);
        Assert.Equal("Keep me", grandchild.Blocks.Single(block => block.Id == payload.Id).PlainText);

        await ExecuteHierarchyActionAsync(executor, boards, notebook.Id,
            new BoardsOperation(BoardsOperationKind.UndoPageStructure));
        var undone = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        Assert.NotNull(undone);
        Assert.Equal(source.Id, Assert.Single(undone!.Sections.Where(section => section.Pages.Any(page => page.Id == child.Id))).Id);
        Assert.Equal(root.Id, Assert.Single(boards.ListPageHierarchy(undone).Where(item => item.Page.Id == child.Id)).ParentPageId);

        await ExecuteHierarchyActionAsync(executor, boards, notebook.Id,
            new BoardsOperation(BoardsOperationKind.RedoPageStructure));
        var redone = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        Assert.NotNull(redone);
        Assert.Equal(target.Id, Assert.Single(redone!.Sections.Where(section => section.Pages.Any(page => page.Id == child.Id))).Id);
        Assert.Equal("Keep me", redone.Sections.SelectMany(section => section.Pages)
            .Single(page => page.Id == grandchild.Id).Blocks.Single(block => block.Id == payload.Id).PlainText);
    }

    [Fact]
    public async Task TypedNoOpHierarchyAction_PersistsReceiptOnceWithoutChangingStructureHistory()
    {
        var repository = new FakeNotesRepository();
        var boards = new BoardsWorkspaceService(repository);
        var executor = new BoardsOperationExecutor(boards);
        var notebook = await boards.CreateNotebookAsync("Typed no-op hierarchy", CancellationToken.None);
        var root = notebook.Sections[0].Pages[0];
        var child = boards.AddChildPage(notebook, root.Id, "Child");
        await boards.SaveAsync(notebook, "Persist child", CancellationToken.None);
        var before = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        var versionBefore = before!.Version;
        var historyBefore = before.Metadata[BoardsWorkspaceService.PageStructureHistoryKey];
        var operation = new BoardsOperation(BoardsOperationKind.MovePageWithinParent, PageId: child.Id, Direction: 0,
            OperationId: Guid.NewGuid(), ExpectedDocumentVersion: versionBefore);

        var firstResult = await executor.ExecuteAsync(notebook.Id, operation, CancellationToken.None);

        var after = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        Assert.NotNull(after);
        var committedVersion = after!.Version;
        Assert.Equal(versionBefore + 1, committedVersion);
        Assert.Equal(historyBefore, after.Metadata[BoardsWorkspaceService.PageStructureHistoryKey]);
        Assert.Equal(firstResult, await executor.ExecuteAsync(notebook.Id, operation, CancellationToken.None));
        var afterReplay = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        Assert.NotNull(afterReplay);
        Assert.Equal(committedVersion, afterReplay!.Version);
        Assert.Equal(historyBefore, afterReplay.Metadata[BoardsWorkspaceService.PageStructureHistoryKey]);
    }

    private static async Task<BoardsOperationResult> ExecuteHierarchyActionAsync(
        BoardsOperationExecutor executor, IBoardsWorkspaceService boards, Guid notebookId, BoardsOperation operation)
    {
        var current = await boards.OpenNotebookAsync(notebookId, CancellationToken.None)
            ?? throw new KeyNotFoundException("The Boards notebook was not found.");
        return await executor.ExecuteAsync(notebookId, operation with
        {
            OperationId = operation.OperationId ?? Guid.NewGuid(),
            ExpectedDocumentVersion = operation.ExpectedDocumentVersion ?? current.Version
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ViewMode_IsPersistedAndBlocksServiceAndTypedApiMutationsUntilEditModeReturns()
    {
        var repository = new FakeNotesRepository();
        var boards = new BoardsWorkspaceService(repository);
        var executor = new BoardsOperationExecutor(boards);
        var notebook = await boards.CreateNotebookAsync("Modes", CancellationToken.None);
        var section = notebook.Sections[0];
        var page = section.Pages[0];
        boards.SetEditMode(notebook, page.Id, BoardsPageEditMode.View);

        Assert.Equal(BoardsPageEditMode.View, boards.GetEditMode(notebook, page.Id));
        await boards.SaveAsync(notebook, "save view mode", CancellationToken.None);
        var reopened = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        Assert.Equal(BoardsPageEditMode.View, boards.GetEditMode(reopened!, page.Id));
        Assert.Throws<InvalidOperationException>(() => boards.AddBlock(reopened!, page.Id, NotesBlockKind.Paragraph));
        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            reopened!.Id,
            new BoardsOperation(BoardsOperationKind.AddBlock, SectionId: section.Id, PageId: page.Id,
                Text: "must not mutate", BlockKind: NotesBlockKind.Paragraph),
            CancellationToken.None));

        await executor.ExecuteAsync(reopened!.Id,
            new BoardsOperation(BoardsOperationKind.SetEditMode, PageId: page.Id, Text: "Edit"), CancellationToken.None);
        var editable = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        Assert.Equal(BoardsPageEditMode.Edit, boards.GetEditMode(editable!, page.Id));
        Assert.NotNull(boards.AddBlock(editable!, page.Id, NotesBlockKind.Paragraph, "allowed"));
    }

    [Fact]
    public async Task LockedLayoutPreservesAbsoluteCoordinatesAndBlocksPlacementIndependentlyOfEditMode()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        var notebook = await boards.CreateNotebookAsync("Layout", CancellationToken.None);
        var page = notebook.Sections[0].Pages[0];
        Assert.Equal(BoardsPageLayoutMode.Locked, boards.GetLayoutMode(notebook, page.Id));
        Assert.Throws<InvalidOperationException>(() => boards.AddCanvasObject(
            notebook, page.Id, NotesCanvasObjectKind.Text, "locked", 100, 200));

        boards.SetLayoutMode(notebook, page.Id, BoardsPageLayoutMode.Unlocked);
        var card = boards.AddCanvasObject(notebook, page.Id, NotesCanvasObjectKind.Text, "placed", 100, 200);
        Assert.True(boards.MoveCanvasObject(notebook, page.Id, card.Id, 340, 510));
        boards.SetLayoutMode(notebook, page.Id, BoardsPageLayoutMode.Locked);
        Assert.Throws<InvalidOperationException>(() => boards.MoveCanvasObject(notebook, page.Id, card.Id, 0, 0));
        Assert.Throws<InvalidOperationException>(() => boards.ResizeCanvasObject(notebook, page.Id, card.Id, 400, 300));
        Assert.Equal((340d, 510d, card.Width, card.Height), (card.X, card.Y, card.Width, card.Height));

        boards.SetEditMode(notebook, page.Id, BoardsPageEditMode.View);
        Assert.Throws<InvalidOperationException>(() => boards.SetLayoutMode(notebook, page.Id, BoardsPageLayoutMode.Unlocked));
        Assert.Equal(BoardsPageLayoutMode.Locked, boards.GetLayoutMode(notebook, page.Id));
    }

    [Fact]
    public void DeepLink_RoundTripsNotebookSectionAndPage()
    {
        var expected = new BoardsDeepLink(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var text = expected.ToString();

        Assert.True(BoardsDeepLink.TryParse(text, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task LiveComponent_UpdateRefreshesEveryPlacementAndSurvivesSave()
    {
        var repository = new FakeNotesRepository();
        var boards = new BoardsWorkspaceService(repository);
        var notebook = await boards.CreateNotebookAsync("Shared", CancellationToken.None);
        var section = notebook.Sections[0];
        var secondPage = NotesPage.CreateDefault();
        secondPage.Order = 1;
        secondPage.Title = "Second";
        section.Pages.Add(secondPage);

        var component = boards.AddComponent(notebook, section.Pages[0], BoardsLiveComponentKind.TaskList);
        boards.PlaceComponent(notebook, section.Pages[0], component.Id);
        boards.PlaceComponent(notebook, secondPage, component.Id);
        var item = component.Items[0];

        Assert.True(boards.UpdateComponentItem(notebook, component.Id, item.Id, value =>
        {
            value.Text = "Changed everywhere";
            value.Checked = true;
        }));
        await boards.SaveAsync(notebook, "test", CancellationToken.None);

        foreach (var page in section.Pages)
        {
            var placed = page.Blocks.Single(block =>
                block.Metadata.TryGetValue(BoardsWorkspaceService.ComponentIdKey, out var raw) &&
                raw == component.Id.ToString("D"));
            Assert.Equal("Changed everywhere", placed.List!.Items[0].Text);
            Assert.True(placed.List.Items[0].Checked);
        }

        var reopened = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        Assert.NotNull(reopened);
        Assert.Equal(component.Id, boards.GetComponents(reopened!)[0].Id);
    }

    [Fact]
    public async Task TypedOperations_CreateHierarchyContentAndActivityTarget()
    {
        var repository = new FakeNotesRepository();
        var boards = new BoardsWorkspaceService(repository);
        var executor = new BoardsOperationExecutor(boards);
        var notebook = await boards.CreateNotebookAsync("Agent board", CancellationToken.None);

        var sectionResult = await executor.ExecuteAsync(
            notebook.Id,
            new BoardsOperation(BoardsOperationKind.AddSection, Text: "Research"),
            CancellationToken.None);
        var pageResult = await executor.ExecuteAsync(
            notebook.Id,
            new BoardsOperation(BoardsOperationKind.AddPage, SectionId: sectionResult.SectionId,
                PageId: notebook.Sections[0].Pages[0].Id, Text: "Sources"),
            CancellationToken.None);
        var blockResult = await executor.ExecuteAsync(
            notebook.Id,
            new BoardsOperation(
                BoardsOperationKind.AddBlock,
                SectionId: sectionResult.SectionId,
                PageId: pageResult.PageId,
                Text: "Evidence",
                BlockKind: NotesBlockKind.Heading),
            CancellationToken.None);

        Assert.StartsWith("haven://boards/", blockResult.ActivityTarget, StringComparison.Ordinal);
        Assert.NotNull(blockResult.BlockId);
        var reopened = await boards.OpenNotebookAsync(notebook.Id, CancellationToken.None);
        var section = reopened!.Sections.Single(value => value.Id == sectionResult.SectionId);
        var page = section.Pages.Single(value => value.Id == pageResult.PageId);
        Assert.Contains(page.Blocks, value => value.Id == blockResult.BlockId && value.Kind == NotesBlockKind.Heading);
    }

    [Fact]
    public async Task CancelledToken_IsHonoured()
    {
        var boards = new BoardsWorkspaceService(new FakeNotesRepository());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => boards.CreateNotebookAsync("Cancelled", cts.Token));
    }

    private sealed class FakeNotesRepository(params NotesDocument[] documents) : INotesRepository
    {
        private readonly List<NotesDocument> _documents = [.. documents];

        public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<NotesDocumentSummary> result = _documents.Select(document =>
                new NotesDocumentSummary(
                    document.Id,
                    document.Title,
                    document.UpdatedAt,
                    document.Version,
                    document.Sections.Count,
                    document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).Count(),
                    NotesTextStatistics.Calculate(document).Words,
                    document.Recovery.HasUnsavedRecovery)).ToArray();
            return Task.FromResult(result);
        }

        public Task<NotesDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_documents.FirstOrDefault(document => document.Id == documentId));
        }

        public Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = _documents.FindIndex(value => value.Id == document.Id);
            if (index < 0) _documents.Add(document); else _documents[index] = document;
            document.Version++;
            document.UpdatedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(new NotesSaveResult(
                document.Id, document.Version, document.UpdatedAt, "test-hash", "current.json", $"version-{document.Version}.json"));
        }

        public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _documents.RemoveAll(value => value.Id == documentId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid documentId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<NotesVersionInfo>>([]);

        public Task<NotesDocument?> LoadVersionAsync(Guid documentId, string versionId, CancellationToken cancellationToken)
            => LoadAsync(documentId, cancellationToken);

        public Task<NotesDocument?> RecoverLatestAsync(Guid documentId, CancellationToken cancellationToken)
            => LoadAsync(documentId, cancellationToken);

        public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<NotesSearchHit>>([]);
    }
}
