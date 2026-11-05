// In-memory IRichBoardSession used until the persistence worker's
// schema-v2 RichBoardSession lands. Backed by a static store keyed by
// path so edit -> save -> dispose -> reopen round-trips preserve content.

using System.Collections.Concurrent;
using System.Text.Json;
using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.App;

public sealed class InMemoryRichBoardSession : IRichBoardSession, IBoardSessionLifetime
{
    private static readonly ConcurrentDictionary<string, string> Store = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly Timer _autosaveTimer;
    private readonly object _gate = new();
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    // History must record the image from BEFORE an edit, which is what the contract session does
    // (RichBoardSession.MutateIfAsync captures `before` then PushHistory(before)). This double
    // used to push the already-mutated document, so one undo restored the edit instead of
    // removing it - measured on 28 October through the toolbar font-size check, which asked for
    // size 20, then 18.5, then undo, and got 18.5 back. The checkpoint below holds the last
    // recorded state and is what an undo step restores to.
    private string _checkpoint = "";
    private bool _dirty;
    private volatile bool _disposed;
    private string _status = "Ready";

    public InMemoryRichBoardSession()
    {
        _autosaveTimer = new Timer(_ => _ = AutosaveTickAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _checkpoint = Snapshot;
    }

    public RichBoardDocument Document { get; private set; } = CreateDefault();
    bool IBoardSessionLifetime.IsDisposed => _disposed;

    public string Snapshot => JsonSerializer.Serialize(Document, JsonOptions);

    public string Status
    {
        get { lock (_gate) return _status; }
        private set { lock (_gate) _status = value; }
    }

    public string? FilePath { get; private set; }

    public event EventHandler<string>? StatusChanged;

    public ValueTask OpenAsync(string? path, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        FilePath = string.IsNullOrWhiteSpace(path) ? "memory://boards/default" : path;
        if (Store.TryGetValue(FilePath, out var json) && !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                Document = JsonSerializer.Deserialize<RichBoardDocument>(json, JsonOptions) ?? CreateDefault();
            }
            catch (JsonException)
            {
                Document = CreateDefault();
            }
        }
        else
        {
            Document = CreateDefault();
        }
        lock (_gate)
        {
            _undo.Clear();
            _redo.Clear();
            _checkpoint = Snapshot;
        }
        SetStatus("Ready");
        return ValueTask.CompletedTask;
    }

    public async ValueTask SaveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        try
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            Store[FilePath ?? "memory://boards/default"] = Snapshot;
            lock (_gate) _dirty = false;
            SetStatus($"Saved {DateTime.Now:HH:mm:ss}");
        }
        catch (Exception ex)
        {
            SetStatus($"Save failed: {ex.Message}");
        }
    }

    public async ValueTask SaveAsAsync(string path, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        FilePath = path;
        await SaveAsync(cancellationToken);
    }

    public void MarkDirty()
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            // Record the state from before this edit (the checkpoint), not the state that already
            // contains it; an edit that changed nothing records nothing.
            var current = Snapshot;
            if (!string.Equals(current, _checkpoint, StringComparison.Ordinal))
            {
                _undo.Push(_checkpoint);
                while (_undo.Count > 60)
                {
                    var kept = _undo.Take(60).Reverse().ToArray();
                    _undo.Clear();
                    foreach (var entry in kept) _undo.Push(entry);
                }
                _redo.Clear();
                _checkpoint = current;
            }
            _dirty = true;
        }
        SetStatus("Unsaved changes");
        _autosaveTimer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
    }

    public IReadOnlyList<RichStyleView> Styles { get; } =
    [
        new() { Id = "normal", Name = "Paragraph", IsBuiltIn = true, BlockKind = "paragraph", FontSize = 14, LineSpacing = 1.25 },
        new() { Id = "title", Name = "Title", IsBuiltIn = true, BlockKind = "heading", Bold = true, FontSize = 32 },
        new() { Id = "subtitle", Name = "Subtitle", IsBuiltIn = true, BlockKind = "heading", FontSize = 20, Foreground = "#FF6B7280" },
        new() { Id = "heading-1", Name = "Header 1", IsBuiltIn = true, BlockKind = "heading", Bold = true, FontSize = 26 },
        new() { Id = "heading-2", Name = "Header 2", IsBuiltIn = true, BlockKind = "heading", Bold = true, FontSize = 21 },
        new() { Id = "heading-3", Name = "Header 3", IsBuiltIn = true, BlockKind = "heading", Bold = true, FontSize = 17 },
        new() { Id = "heading-4", Name = "Header 4", IsBuiltIn = true, BlockKind = "heading", Bold = true, FontSize = 15 },
        new() { Id = "heading-5", Name = "Header 5", IsBuiltIn = true, BlockKind = "heading", Bold = true, FontSize = 13 },
        new() { Id = "heading-6", Name = "Header 6", IsBuiltIn = true, BlockKind = "heading", Bold = true, FontSize = 12 },
        new() { Id = "quote", Name = "Quote", IsBuiltIn = true, BlockKind = "paragraph", Italic = true, Foreground = "#FF6B7280" },
        new() { Id = "code", Name = "Code", IsBuiltIn = true, BlockKind = "paragraph", FontFamily = "Cascadia Mono", FontSize = 13, Background = "#FFF1F3F4" },
    ];

    public bool CanUndo { get { lock (_gate) return _undo.Count > 0; } }
    public bool CanRedo { get { lock (_gate) return _redo.Count > 0; } }

    public ValueTask<bool> UndoAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string? previous;
        lock (_gate)
        {
            if (!_undo.TryPop(out previous)) return ValueTask.FromResult(false);
            _redo.Push(JsonSerializer.Serialize(Document, JsonOptions));
            _checkpoint = previous;
        }
        Document = JsonSerializer.Deserialize<RichBoardDocument>(previous, JsonOptions) ?? CreateDefault();
        lock (_gate) _dirty = true;
        SetStatus("Undone — editing…");
        _autosaveTimer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
        return ValueTask.FromResult(true);
    }

    public ValueTask<bool> RedoAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string? next;
        lock (_gate)
        {
            if (!_redo.TryPop(out next)) return ValueTask.FromResult(false);
            _undo.Push(JsonSerializer.Serialize(Document, JsonOptions));
            _checkpoint = next;
        }
        Document = JsonSerializer.Deserialize<RichBoardDocument>(next, JsonOptions) ?? CreateDefault();
        lock (_gate) _dirty = true;
        SetStatus("Redone — editing…");
        _autosaveTimer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
        return ValueTask.FromResult(true);
    }

    public ValueTask SetSectionIconAsync(string sectionId, string iconKey, string? iconColor,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var section = Document.Sections.FirstOrDefault(candidate => candidate.Id == sectionId)
            ?? throw new KeyNotFoundException("Section was not found.");
        if (!HavenRichSectionIcons.IsSupported(iconKey))
            throw new ArgumentException("Section icon key is not supported.", nameof(iconKey));
        if (!HavenRichSectionIcons.IsSupportedColor(iconColor))
            throw new ArgumentException("Section icon color must be null or a six-digit #RRGGBB value.", nameof(iconColor));
        var normalizedColor = iconColor?.ToUpperInvariant();
        if (section.IconKey == iconKey && section.IconColor == normalizedColor)
            return ValueTask.CompletedTask;
        section.IconKey = iconKey;
        section.IconColor = normalizedColor;
        section.GeneratedIcon = null;
        MarkDirty();
        return ValueTask.CompletedTask;
    }

    public ValueTask SetSectionIconEmojiAsync(string sectionId, string emoji, string? iconColor,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var section = RequireSection(sectionId);
        if (!HavenRichSectionIcons.IsPlausibleEmojiSequence(emoji))
            throw new ArgumentException("An emoji icon requires a single Unicode emoji sequence.", nameof(emoji));
        var normalizedColor = NormalizedIconColor(iconColor);
        if (section.IconKey == HavenRichSectionIcons.Emoji && section.IconEmoji == emoji &&
            section.IconColor == normalizedColor)
            return ValueTask.CompletedTask;
        NoteRecentEmoji(emoji);
        section.IconKey = HavenRichSectionIcons.Emoji;
        section.IconEmoji = emoji;
        section.IconColor = normalizedColor;
        section.GeneratedIcon = null;
        MarkDirty();
        return ValueTask.CompletedTask;
    }

    public ValueTask SetPageIconAsync(string pageId, string iconKey, string? iconColor,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var page = RequirePage(pageId);
        if (!HavenRichSectionIcons.IsSupported(iconKey))
            throw new ArgumentException("Page icon key is not supported.", nameof(iconKey));
        var normalizedColor = NormalizedIconColor(iconColor);
        if (page.IconKey == iconKey && page.IconColor == normalizedColor && page.IconEmoji is null)
            return ValueTask.CompletedTask;
        page.IconKey = iconKey;
        page.IconColor = normalizedColor;
        page.IconEmoji = null;
        MarkDirty();
        return ValueTask.CompletedTask;
    }

    public ValueTask SetPageIconEmojiAsync(string pageId, string emoji, string? iconColor,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var page = RequirePage(pageId);
        if (!HavenRichSectionIcons.IsPlausibleEmojiSequence(emoji))
            throw new ArgumentException("An emoji icon requires a single Unicode emoji sequence.", nameof(emoji));
        var normalizedColor = NormalizedIconColor(iconColor);
        if (page.IconKey == HavenRichSectionIcons.Emoji && page.IconEmoji == emoji &&
            page.IconColor == normalizedColor)
            return ValueTask.CompletedTask;
        NoteRecentEmoji(emoji);
        page.IconKey = HavenRichSectionIcons.Emoji;
        page.IconEmoji = emoji;
        page.IconColor = normalizedColor;
        MarkDirty();
        return ValueTask.CompletedTask;
    }

    public ValueTask SetBoardIconAsync(string? iconKey, string? iconEmoji, string? iconColor,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedColor = NormalizedIconColor(iconColor);
        if (string.IsNullOrWhiteSpace(iconKey))
        {
            if (Document.IconKey is null && Document.IconColor is null && Document.IconEmoji is null)
                return ValueTask.CompletedTask;
            Document.IconKey = null;
            Document.IconColor = null;
            Document.IconEmoji = null;
            MarkDirty();
            return ValueTask.CompletedTask;
        }
        if (iconKey == HavenRichSectionIcons.Emoji)
        {
            if (!HavenRichSectionIcons.IsPlausibleEmojiSequence(iconEmoji))
                throw new ArgumentException("An emoji icon requires a single Unicode emoji sequence.", nameof(iconEmoji));
            if (Document.IconKey == HavenRichSectionIcons.Emoji && Document.IconEmoji == iconEmoji &&
                Document.IconColor == normalizedColor)
                return ValueTask.CompletedTask;
            NoteRecentEmoji(iconEmoji!);
            Document.IconKey = HavenRichSectionIcons.Emoji;
            Document.IconEmoji = iconEmoji;
            Document.IconColor = normalizedColor;
            MarkDirty();
            return ValueTask.CompletedTask;
        }
        if (Document.IconKey == iconKey && Document.IconColor == normalizedColor && Document.IconEmoji is null)
            return ValueTask.CompletedTask;
        Document.IconKey = iconKey;
        Document.IconColor = normalizedColor;
        Document.IconEmoji = null;
        MarkDirty();
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearPageIconAsync(string pageId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var page = RequirePage(pageId);
        if (page.IconKey == HavenRichSectionIcons.PageDefault && page.IconColor is null && page.IconEmoji is null)
            return ValueTask.CompletedTask;
        page.IconKey = HavenRichSectionIcons.PageDefault;
        page.IconColor = null;
        page.IconEmoji = null;
        MarkDirty();
        return ValueTask.CompletedTask;
    }

    private void NoteRecentEmoji(string emoji)
    {
        Document.RecentIconEmoji.RemoveAll(remembered => string.Equals(remembered, emoji, StringComparison.Ordinal));
        Document.RecentIconEmoji.Insert(0, emoji);
        if (Document.RecentIconEmoji.Count > HavenRichNotes.RecentIconEmojiLimit)
            Document.RecentIconEmoji.RemoveRange(HavenRichNotes.RecentIconEmojiLimit,
                Document.RecentIconEmoji.Count - HavenRichNotes.RecentIconEmojiLimit);
    }

    /// <summary>
    /// Trash for the in-memory session, which is what tests and the embedded host drive. It mirrors the
    /// contract rules - the same refusals, the same subtree grouping, the same recorded-place restore - and it
    /// keeps the retained pages inside the document, on the Trash row, so undo and redo restore the tree and
    /// the Trash together instead of leaving one half of the action behind.
    /// </summary>
    public ValueTask<BoardsTrashImpact?> GetTrashImpactAsync(string pageId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var owner = FindSection(pageId);
        if (owner.section is null || owner.page is null)
            return ValueTask.FromResult((BoardsTrashImpact?)null);
        var group = TrashSubtree(owner.section, owner.page);
        return ValueTask.FromResult<BoardsTrashImpact?>(new BoardsTrashImpact(
            group.Count - 1, group.Sum(page => page.Blocks.Count), TrashRefusalFor(owner.section, owner.page)));
    }

    /// <summary>The same three questions the contract asks, in one place so the two cannot drift.</summary>
    private static string? TrashRefusalFor(RichBoardSection section, RichBoardPage page)
    {
        if (page.TabberOwnerBlockId is not null)
            return "This page is a tab inside another page; close that tab instead.";
        if (section.Pages.Count(item => item.TabberOwnerBlockId is null) <= 1)
            return "The final page of a section cannot be trashed.";
        return null;
    }

    public ValueTask<BoardsTrashResult> MovePageToTrashAsync(string pageId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var found = FindSection(pageId);
        if (found.section is null || found.page is null)
            return ValueTask.FromResult(BoardsTrashResult.Refused("That page is not in this board."));
        var (section, page) = found;
        if (TrashRefusalFor(section, page) is { } refusal)
            return ValueTask.FromResult(BoardsTrashResult.Refused(refusal));

        var group = TrashSubtree(section, page);
        foreach (var pane in section.Pages
                     .Where(item => item.TabberOwnerBlockId is not null &&
                                    group.Any(owner => owner.Blocks.Any(block =>
                                        string.Equals(block.Id, item.TabberOwnerBlockId, StringComparison.Ordinal))))
                     .Where(item => !group.Contains(item)))
            group.Add(pane);

        var entry = new RichBoardTrashEntry
        {
            PageId = page.Id,
            GroupId = Guid.NewGuid().ToString("N"),
            Title = string.IsNullOrWhiteSpace(page.Title) ? "Untitled page" : page.Title.Trim(),
            IconKey = page.IconKey,
            IconEmoji = page.IconEmoji,
            SectionId = section.Id,
            SectionTitle = section.Title,
            SubtreePageCount = group.Count - 1,
            BlockCount = group.Sum(each => each.Blocks.Count),
            TrashedUtc = DateTimeOffset.UtcNow,
            Retained = group.Select(item => new RichBoardTrashedPage
            {
                Page = item,
                ParentPageId = item.ParentPageId,
                SortIndex = section.Pages.IndexOf(item),
            }).ToList(),
        };
        foreach (var item in group)
            section.Pages.Remove(item);
        Document.Trash.Add(entry);
        MarkDirty();
        return ValueTask.FromResult(new BoardsTrashResult(true, null));
    }

    public ValueTask<BoardsTrashRestoreResult> RestorePageFromTrashAsync(string pageId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var entry = Document.Trash.FirstOrDefault(item => item.PageId == pageId);
        if (entry is null)
            return ValueTask.FromResult(BoardsTrashRestoreResult.Refused("That page is not in the Trash."));
        var section = Document.Sections.FirstOrDefault(candidate => candidate.Id == entry.SectionId);
        if (section is null)
            return ValueTask.FromResult(BoardsTrashRestoreResult.Refused(
                "The section that held this page is no longer in this board."));

        var relocated = false;
        // Shallowest first, so a parent is back in the tree before a page that records it as its parent.
        foreach (var retained in entry.Retained.OrderBy(item => DepthInTree(item, entry.Retained)))
        {
            var page = retained.Page;
            var hadParent = retained.ParentPageId is not null;
            var parentIsLive = hadParent && section.Pages.Any(candidate => candidate.Id == retained.ParentPageId);
            if (hadParent && !parentIsLive)
            {
                // A recorded parent that is not coming back cannot stay as the page's parent: the live tree must
                // not point into the Trash, so the page returns to the section root and the result says so.
                page.ParentPageId = null;
                relocated = true;
            }
            else if (hadParent)
            {
                page.ParentPageId = retained.ParentPageId;
            }
            var insertAt = hadParent && parentIsLive
                ? Math.Clamp(retained.SortIndex, 0, section.Pages.Count)
                : section.Pages.Count;
            section.Pages.Insert(insertAt, page);
        }
        Document.Trash.Remove(entry);
        MarkDirty();
        return ValueTask.FromResult(new BoardsTrashRestoreResult(true, null, section.Title, relocated));
    }

    private static int DepthInTree(RichBoardTrashedPage item, IReadOnlyList<RichBoardTrashedPage> group)
    {
        var depth = 0;
        var parent = item.ParentPageId;
        while (parent is not null && depth < group.Count)
        {
            var next = group.FirstOrDefault(candidate => candidate.Page.Id == parent);
            if (next is null)
                break;
            parent = next.ParentPageId;
            depth++;
        }
        return depth;
    }

    private static List<RichBoardPage> TrashSubtree(RichBoardSection section, RichBoardPage page)
    {
        var group = new List<RichBoardPage> { page };
        for (var index = 0; index < group.Count; index++)
            group.AddRange(section.Pages.Where(child => child.ParentPageId == group[index].Id && !group.Contains(child)));
        return group;
    }

    private (RichBoardSection? section, RichBoardPage? page) FindSection(string pageId)
    {
        foreach (var section in Document.Sections)
        {
            var page = section.Pages.FirstOrDefault(candidate => candidate.Id == pageId);
            if (page is not null)
                return (section, page);
        }
        return (null, null);
    }

    private RichBoardSection RequireSection(string sectionId) =>
        Document.Sections.FirstOrDefault(candidate => candidate.Id == sectionId)
        ?? throw new KeyNotFoundException("Section was not found.");

    private RichBoardPage RequirePage(string pageId) =>
        Document.Sections.SelectMany(section => section.Pages).FirstOrDefault(page => page.Id == pageId)
        ?? throw new KeyNotFoundException("Page was not found.");

    private static string? NormalizedIconColor(string? iconColor)
    {
        if (!HavenRichSectionIcons.IsSupportedColor(iconColor))
            throw new ArgumentException("Icon color must be null or a six-digit #RRGGBB value.", nameof(iconColor));
        return iconColor?.ToUpperInvariant();
    }

    public ValueTask SetSectionGeneratedIconAsync(string sectionId,
        HavenRichSectionGeneratedIcon generatedIcon, string? iconColor,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var section = Document.Sections.FirstOrDefault(candidate => candidate.Id == sectionId)
            ?? throw new KeyNotFoundException("Section was not found.");
        if (!HavenRichSectionIcons.IsSupportedColor(iconColor))
            throw new ArgumentException("Section icon color must be null or a six-digit #RRGGBB value.", nameof(iconColor));
        HavenRichSectionIconGeometry.Validate(generatedIcon);
        var normalizedColor = iconColor?.ToUpperInvariant();
        if (section.IconKey == HavenRichSectionIcons.Generated && section.IconColor == normalizedColor &&
            HavenRichSectionIconGeometry.AreEqual(section.GeneratedIcon, generatedIcon))
            return ValueTask.CompletedTask;
        section.IconKey = HavenRichSectionIcons.Generated;
        section.IconColor = normalizedColor;
        section.GeneratedIcon = HavenRichSectionIconGeometry.Copy(generatedIcon);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        bool flush;
        string? snapshot;
        lock (_gate)
        {
            flush = _dirty && !_disposed;
            snapshot = flush ? JsonSerializer.Serialize(Document, JsonOptions) : null;
            _disposed = true;
        }
        _autosaveTimer.Change(Timeout.Infinite, Timeout.Infinite);
        // Flush directly (not via SaveAsync: this instance is already disposed).
        if (flush && snapshot is not null)
        {
            Store[FilePath ?? "memory://boards/default"] = snapshot;
            lock (_gate) _dirty = false;
            SetStatus($"Saved {DateTime.Now:HH:mm:ss}");
        }
        _autosaveTimer.Dispose();
        await Task.CompletedTask;
    }

    private async Task AutosaveTickAsync()
    {
        bool dirty;
        lock (_gate) dirty = _dirty;
        if (!dirty)
            return;
        await SaveAsync();
    }

    private void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }

    private void ThrowIfDisposed()
    {
        lock (_gate)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(InMemoryRichBoardSession));
        }
    }

    public static void ClearStore() => Store.Clear();

    internal static RichBoardDocument CreateDefault()
    {
        return new RichBoardDocument
        {
            Title = "Untitled board",
            Sections =
            [
                new RichBoardSection
                {
                    Id = "section-1",
                    Title = "Getting started",
                    Pages =
                    [
                        new RichBoardPage
                        {
                            Id = "page-1",
                            Title = "Welcome",
                            Blocks =
                            [
                                new RichBoardBlock { Id = "block-heading", Kind = "heading", Text = "Welcome to Boards" },
                                new RichBoardBlock { Id = "block-para", Kind = "paragraph", Text = "Edit this paragraph. Multiline editing is supported." },
                                new RichBoardBlock { Id = "block-check-1", Kind = "checklist", Text = "Try the checklist", IsChecked = false },
                                new RichBoardBlock { Id = "block-check-2", Kind = "checklist", Text = "Save the board", IsChecked = false },
                                new RichBoardBlock { Id = "block-table", Kind = "table", Text = "Quarterly plan", TableCells = new Dictionary<string, string>(StringComparer.Ordinal) { ["0,0"] = "Q1", ["0,1"] = "Q2", ["1,0"] = "Q3", ["1,1"] = "Q4" } },
                            ],
                        },
                    ],
                },
            ],
        };
    }
}
