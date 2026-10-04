using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class CanvasWorkspacePersistenceTests
{
    [Fact]
    public async Task Create_edit_save_and_reopen_preserves_canvas_identity_and_spatial_state()
    {
        var repository = new JsonRoundTripNotesRepository();
        var surface = await CanvasAppSurface.CreateAsync(repository, "Persisted canvas");
        var documentId = surface.Document.Id;
        var note = surface.AddTextNote("Persist this idea");
        var shape = surface.AddShape("Next step");
        var connector = surface.Connect(note.Id, shape.Id, "leads to");
        Assert.NotNull(connector);

        Assert.True(surface.DrawStroke([
            new CanvasPointerSample(10, 20, 0.4, TimestampMilliseconds: 1000),
            new CanvasPointerSample(30, 40, 0.6, TimestampMilliseconds: 1016),
            new CanvasPointerSample(50, 60, 0.8, TimestampMilliseconds: 1032)
        ]));
        Assert.True(surface.Pan(0, 0, 32, -18));
        surface.SetZoom(1.5);

        var saved = await surface.SaveAsync(repository, "Canvas persistence test");
        var reopened = await CanvasAppSurface.OpenAsync(repository, documentId);

        Assert.NotNull(reopened);
        Assert.Equal(documentId, saved.DocumentId);
        Assert.Equal("Canvas persistence test", repository.LastSaveReason);
        Assert.Equal(documentId, reopened!.Document.Id);
        Assert.Equal("Persisted canvas", reopened.Document.Title);
        Assert.True(CanvasDocumentModel.IsCanvasDocument(reopened.Document));
        Assert.Equal(3, reopened.Board.Objects.Count);
        Assert.Equal("Persist this idea", reopened.Board.Objects.Single(item => item.Id == note.Id).Text);
        Assert.Equal("Next step", reopened.Board.Objects.Single(item => item.Id == shape.Id).Text);
        var reopenedConnector = reopened.Board.Objects.Single(item => item.Id == connector!.Id);
        Assert.Equal(note.Id, reopenedConnector.FromObjectId);
        Assert.Equal(shape.Id, reopenedConnector.ToObjectId);
        var stroke = Assert.Single(reopened.Board.Strokes);
        Assert.Equal(3, stroke.Points.Count);
        Assert.Equal(32, reopened.Board.OffsetX);
        Assert.Equal(-18, reopened.Board.OffsetY);
        Assert.Equal(1.5, reopened.Board.Zoom);
        Assert.Same(reopened.Board, CanvasDocumentModel.GetBoard(reopened.Document));
    }

    [Fact]
    public async Task Open_returns_null_for_missing_documents_and_rejects_non_canvas_notes()
    {
        var repository = new JsonRoundTripNotesRepository();

        Assert.Null(await CanvasAppSurface.OpenAsync(repository, Guid.NewGuid()));

        var ordinaryNotes = NotesDocument.Create("Ordinary notes");
        await repository.SaveAsync(ordinaryNotes, "Test fixture", CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => CanvasAppSurface.OpenAsync(repository, ordinaryNotes.Id));

        Assert.Contains("not a Haven Canvas document", error.Message);
    }

    private sealed class JsonRoundTripNotesRepository : INotesRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly Dictionary<Guid, string> _documents = [];

        public string LastSaveReason { get; private set; } = string.Empty;

        public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NotesDocumentSummary>>(Array.Empty<NotesDocumentSummary>());

        public Task<NotesDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_documents.TryGetValue(documentId, out var json)
                ? JsonSerializer.Deserialize<NotesDocument>(json, JsonOptions)
                : null);
        }

        public Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            document.Version = checked(document.Version + 1);
            document.UpdatedAt = DateTimeOffset.UtcNow;
            var json = JsonSerializer.Serialize(document, JsonOptions);
            _documents[document.Id] = json;
            LastSaveReason = reason;
            return Task.FromResult(new NotesSaveResult(
                document.Id,
                document.Version,
                document.UpdatedAt,
                "test-hash",
                "test://current",
                "test://version"));
        }

        public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _documents.Remove(documentId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid documentId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NotesVersionInfo>>(Array.Empty<NotesVersionInfo>());

        public Task<NotesDocument?> LoadVersionAsync(Guid documentId, string versionId, CancellationToken cancellationToken) =>
            Task.FromResult<NotesDocument?>(null);

        public Task<NotesDocument?> RecoverLatestAsync(Guid documentId, CancellationToken cancellationToken) =>
            Task.FromResult<NotesDocument?>(null);

        public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NotesSearchHit>>(Array.Empty<NotesSearchHit>());
    }
}
