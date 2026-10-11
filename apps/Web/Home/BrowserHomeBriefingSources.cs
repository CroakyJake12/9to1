using System.Runtime.CompilerServices;
using Haven.Application;

namespace NineToOne.Web;

/// <summary>Reads only the same device-local repository already registered for Write. Never substitutes account identity.</summary>
internal static class BrowserHomeBriefingSources
{
    private static readonly ConditionalWeakTable<BrowserSurfaceRegistry, INotesRepository> Notes = new();
    internal static void Register(BrowserSurfaceRegistry registry, INotesRepository repository) => Notes.Add(registry, repository);
    internal static Func<CancellationToken, Task<IReadOnlyList<Haven.Core.NotesDocumentSummary>>>? Get(BrowserSurfaceRegistry registry) =>
        Notes.TryGetValue(registry, out var notes) ? token => ReadWriteDocuments(notes, token) : null;

    private static async Task<IReadOnlyList<Haven.Core.NotesDocumentSummary>> ReadWriteDocuments(INotesRepository notes, CancellationToken token)
    {
        var result = new List<Haven.Core.NotesDocumentSummary>();
        foreach (var row in await notes.ListAsync(token))
        {
            token.ThrowIfCancellationRequested();
            var document = await notes.LoadAsync(row.Id, token);
            // Boards shares the canonical Notes store, but keeps its own owner route.
            if (document is not null && !BoardsWorkspaceService.IsBoardsNotebook(document)) result.Add(row);
        }
        return result;
    }
}
