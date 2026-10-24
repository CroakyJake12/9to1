using CakeOS.Cui;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace NineToOne.Web.Productivity.Boards;

// Find observes the already-authorised SAME canonical notebook. It owns no
// repository/index, new read grant, foreign document model or replacement editor.
public sealed partial class BoardsBrowserCore
{
    private const int MaximumBoardFindMatches = 200;
    private bool _showBoardFind;
    private long _boardFindEpoch;
    private NotesDocument? _boardFindDocument;
    private long _boardFindRevision, _boardFindGeneration;
    private BoardsFindRow[] _boardFindMatches = [];
    private string _boardFindStatus = "Find text in the current board. Table and media matches select their original block.";

    private bool TryGetFindValue(string path, out object? value)
    {
        var visible = !_disposed && !_presentationPending && _document is not null;
        value = path switch
        {
            "ShowBoardFind" => visible && _showBoardFind,
            "BoardFindMatches" => visible ? _boardFindMatches : Array.Empty<BoardsFindRow>(),
            "BoardFindStatus" => visible ? _boardFindStatus : string.Empty,
            _ => null
        };
        return path is "ShowBoardFind" or "BoardFindMatches" or "BoardFindStatus";
    }

    public Task<HomeCoreOperationResult<int>> FindBoardAsync(string query, CancellationToken token = default) => Operate(ct =>
    {
        ct.ThrowIfCancellationRequested();
        if (_presentationPending || _document is null || _pending is not null)
            return Task.FromResult(Failure<int>("OperationDenied", "Open the current board and finish recovery before finding text."));
        if (query is null || query.Length > 1024)
            return Task.FromResult(Failure<int>("InvalidArgument", "Use a search query of at most 1024 characters."));
        InvalidateBoardFind(); _showBoardFind = true;
        if (string.IsNullOrWhiteSpace(query))
        {
            _boardFindStatus = "Enter text to find in this board.";
            return Task.FromResult(Success(0));
        }
        var source = _document;
        // A detached complete canonical snapshot protects live rich/shared/unknown
        // payloads from search initialization. No snapshot becomes authored state.
        var snapshot = Clone(source);
        foreach (var section in snapshot.Sections)
            section.Pages.RemoveAll(page => BoardsWorkspaceService.IsPageDeleted(snapshot, page.Id));
        var pages = snapshot.Sections.SelectMany(section => section.Pages.Select(page => (Section: section, Page: page))).ToArray();
        var locations = new Dictionary<Guid, (NotesSection Section, NotesPage Page)>();
        foreach (var location in pages)
            foreach (var block in location.Page.Blocks)
                if (!locations.TryAdd(block.Id, location)) throw new InvalidDataException("The current board contains ambiguous block identities.");
        var editor = new WriteDocumentEditor(snapshot, null, initializeWithoutNormalization: true);
        // One extra original match proves truncation without materializing all matches.
        var matches = editor.Find(query, MaximumBoardFindMatches + 1);
        ct.ThrowIfCancellationRequested();
        _boardFindDocument = source; _boardFindRevision = DurableRevision; _boardFindGeneration = _generation;
        var epoch = _boardFindEpoch;
        _boardFindMatches = matches.Take(MaximumBoardFindMatches).Select((match, index) =>
        {
            var location = locations[match.BlockId];
            return new BoardsFindRow(this, epoch, index, source.Id, location.Page.Id,
                location.Section.Title, location.Page.Title, query, match);
        }).ToArray();
        _boardFindStatus = matches.Count > MaximumBoardFindMatches
            ? $"Showing the first {MaximumBoardFindMatches} matches for ‘{query}’. Narrow the search to see later matches."
            : $"{matches.Count} matches for ‘{query}’ in this board.";
        Status = _boardFindStatus;
        return Task.FromResult(Success(_boardFindMatches.Length));
    }, token);

    public Task<HomeCoreOperationResult<bool>> OpenBoardFindMatchAsync(BoardsFindRow row, CancellationToken token = default) => Operate(ct =>
    {
        ct.ThrowIfCancellationRequested();
        if (!IsCurrentBoardFindRow(row))
            return Task.FromResult(Failure<bool>("StaleSearchResult", "The board or search changed. Find again before opening this match."));
        var page = VisiblePages.SingleOrDefault(value => value.Id == row.PageId);
        if (page is null || page.Blocks.All(value => value.Id != row.Match.BlockId))
            return Task.FromResult(Failure<bool>("PageNotFound", "This match is no longer available in the current board."));
        // Use the actual retained editor when already on that page. A different
        // page prepares the SAME shared editor over the SAME live document with
        // pure initialization; failed matching never changes the published page.
        var candidate = ActivePageId == row.PageId && _richEditor is { } current
            ? current : new WriteDocumentEditor(_document!, row.PageId, initializeWithoutNormalization: true);
        if (!ReferenceEquals(candidate.Document, _document) || candidate.PageScopeId != row.PageId ||
            !candidate.SelectFindResult(row.Match, row.Query))
            return Task.FromResult(Failure<bool>("StaleSearchResult", "The original match changed. Find again before selecting it."));
        if (!ReferenceEquals(candidate, _richEditor))
        {
            UnbindRichEditor(); ActivePageId = row.PageId; _richEditor = candidate;
            _richEditor.Changed += OnRichEditorChanged;
        }
        _drawerOpen = false;
        _boardFindStatus = $"Selected match in ‘{row.PageTitle}’. Table and media matches select the whole original block.";
        Status = _boardFindStatus;
        return Task.FromResult(Success(true));
    }, token);

    private bool IsCurrentBoardFindRow(BoardsFindRow? row) => row is not null &&
        !_disposed && !_presentationPending && _pending is null && _document is not null &&
        ReferenceEquals(row.Owner, this) && ReferenceEquals(_boardFindDocument, _document) &&
        row.BoardId == _document.Id && row.Epoch == _boardFindEpoch &&
        _boardFindRevision == DurableRevision && _boardFindGeneration == _generation &&
        _boardFindMatches.Any(original => ReferenceEquals(original, row));
    internal bool CanOpenBoardFindRow(BoardsFindRow row) => !IsBusy && IsCurrentBoardFindRow(row);

    private void InvalidateBoardFind()
    {
        ++_boardFindEpoch; _boardFindMatches = []; _boardFindDocument = null; _showBoardFind = false;
        _boardFindStatus = "Find text in the current board. Table and media matches select their original block.";
    }
    private void InvalidateChangedBoardFind()
    {
        if (_boardFindDocument is null || ReferenceEquals(_boardFindDocument, _document) &&
            _boardFindRevision == DurableRevision && _boardFindGeneration == _generation &&
            !_disposed && !_presentationPending && _pending is null) return;
        var wasOpen = _showBoardFind; InvalidateBoardFind(); _showBoardFind = wasOpen && _document is not null;
        _boardFindStatus = "The board changed. Find again to see current matches.";
    }
    private async Task<bool> DispatchFindAsync(string command, object? parameter, CancellationToken token)
    {
        switch (command)
        {
            case "ToggleFind": _showBoardFind = !_showBoardFind; Notify(); return true;
            case "FindBoard" when parameter is string query: await FindBoardAsync(query, token); return true;
            case "OpenFindMatch" when parameter is BoardsFindRow row: await OpenBoardFindMatchAsync(row, token); return true;
            case "FindBoard": case "OpenFindMatch": Failure<bool>("InvalidArgument", "Choose a current board search or original match."); Notify(); return true;
            default: return false;
        }
    }
}

// A source-issued view selection, not a resource identity or read/write grant.
// Constructor/owner/epoch remain private to the current canonical Boards core.
public sealed class BoardsFindRow : ICuiBindingContext
{
    internal BoardsFindRow(BoardsBrowserCore owner, long epoch, int index, Guid boardId, Guid pageId,
        string sectionTitle, string pageTitle, string query, WriteFindResult match)
    {
        Owner = owner; Epoch = epoch; BoardId = boardId; PageId = pageId; PageTitle = pageTitle;
        Query = query; Match = match; Key = epoch + ":" + index;
        var snippet = match.Snippet.Length > 160 ? match.Snippet[..160] + "…" : match.Snippet;
        Label = sectionTitle + " / " + pageTitle + " · " + match.Kind + " — " + snippet;
    }
    internal BoardsBrowserCore Owner { get; }
    internal long Epoch { get; }
    internal Guid BoardId { get; }
    internal Guid PageId { get; }
    internal string PageTitle { get; }
    internal string Query { get; }
    internal WriteFindResult Match { get; }
    public string Key { get; }
    public string Label { get; }
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch { "Key" => Key, "Label" or "AccessibleName" => Label, "CanOpen" => Owner.CanOpenBoardFindRow(this), _ => null };
        return path is "Key" or "Label" or "AccessibleName" or "CanOpen";
    }
}
