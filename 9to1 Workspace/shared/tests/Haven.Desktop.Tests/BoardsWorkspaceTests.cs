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
