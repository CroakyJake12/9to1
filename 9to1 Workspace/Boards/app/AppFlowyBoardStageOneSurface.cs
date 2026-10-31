using System.ComponentModel;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;

namespace CakeOS.Apps.Boards.App;

/// <summary>
/// Binding context and command wiring for <c>AppFlowyBoardStageOne.cui</c>, the AppFlowy board screen re-rendered
/// in CUI. The owner's instruction on 31 October 2026 was to copy AppFlowy's UI and "route it up and whatnot… so it
/// actually works", so the donor's board commands are executed against the REAL 9to1 board rather than rendered as
/// dead controls: a card is a 9to1 page, a group is a 9to1 section, creating a card calls the session's
/// <c>AddPageAsync(sectionId)</c>, renaming a group calls <c>RenameSectionAsync</c>, deleting one calls
/// <c>DeleteSectionAsync</c>, and the trailing column calls <c>AddSectionAsync</c>. Opening a card selects that page
/// and returns to the editor surface.
/// </summary>
/// <remarks>
/// The surface re-reads the live document after every mutation and raises <see cref="PropertyChanged"/> with no
/// property name, which is the notification the CUI repeat engine reconciles on. Nothing here writes to disk by
/// itself: durability stays with the board session's own save path, so a stage-one view cannot invent a second
/// persistence route.
///
/// Two donor features stay unavailable and are reported, not faked: hiding a group (9to1 Boards has no hidden
/// section concept yet) and the filter/settings panel (no board filter model yet). Their controls render disabled
/// with the notice line stating why.
/// </remarks>
internal sealed class AppFlowyBoardStageOneSurface : ICuiBindingContext, ICuiWritableBindingContext,
    ICuiActionDispatcher, INotifyPropertyChanged
{
    // Measured donor constraints: AppFlowy board_page.dart:352 groupConstraints tightFor(width: compact ? 196 : 256);
    // AppFlowyBoard lib/src/widgets/board.dart:29-34 groupMargin horizontal 8, header padding horizontal 16,
    // body padding horizontal 12, footer padding horizontal 12, card margin horizontal 3 vertical 4.
    internal const double GroupColumnWidth = 256;
    internal const double CardWidth = 226;   // 256 column - 2 * body padding 12 - 2 * card margin 3

    private readonly Func<RichBoardDocument> _document;
    private readonly Func<string, Task> _openCardPage;
    private readonly Func<string, string?, Task> _createCardInSection;
    private readonly Func<string, string, Task> _renameSection;
    private readonly Func<string, Task> _deleteSection;
    private readonly Func<Task> _createSection;
    private readonly List<GroupCard> _groups = [];

    // AppFlowy's window chrome state, transcribed in this stage (home_sizes.dart: minimumSidebarWidth 268,
    // workspaceSectionHeight 32, searchSectionHeight 30, newPageSectionHeight 30, tabBarHeight 40,
    // tabBarWidth 200; HomeSpaceViewSizes viewHeight 30 / leftPadding 16). None of it is decorative: the tree is
    // built from this board's real sections and pages, including the real parent/child relation
    // (RichBoardPage.ParentPageId), the tab strip tracks the pages actually opened in this session, and the
    // search field filters that same real data. Sections the user collapsed are remembered per surface.
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private readonly List<string> _openTabs = [];
    private bool _searchActive;
    private string _query = string.Empty;

    public AppFlowyBoardStageOneSurface(
        Func<RichBoardDocument> document,
        Func<string, Task> openCardPage,
        Func<string, string?, Task> createCardInSection,
        Func<string, string, Task> renameSection,
        Func<string, Task> deleteSection,
        Func<Task> createSection)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _openCardPage = openCardPage ?? throw new ArgumentNullException(nameof(openCardPage));
        _createCardInSection = createCardInSection ?? throw new ArgumentNullException(nameof(createCardInSection));
        _renameSection = renameSection ?? throw new ArgumentNullException(nameof(renameSection));
        _deleteSection = deleteSection ?? throw new ArgumentNullException(nameof(deleteSection));
        _createSection = createSection ?? throw new ArgumentNullException(nameof(createSection));
        Rebuild();
    }

    public event Action? ExitRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool TryGetValue(string path, out object? value)
    {
        switch (path)
        {
            case "DonorBoardTitle":
                value = _document().Title;
                return true;
            case "DonorGroupColumns":
                value = _groups;
                return true;
            case "DonorHasHiddenGroups":
            case "DonorHiddenGroupsCollapsed":
            case "DonorHiddenGroupsExpanded":
            case "DonorIsViewReference":
            case "DonorCanFilter":
                // Absent in 9to1 Boards today: 9to1 Boards has no hidden sections, no reference views and no board
                // filter model, so those donor branches render absent instead of showing empty donor chrome.
                value = false;
                return true;
            case "DonorHiddenGroups":
                value = Array.Empty<HiddenGroupCard>();
                return true;
            case "DonorCanCreateGroup":
                value = true;
                return true;
            case "DonorShowsTrailingSpacer":
                value = false;
                return true;
            case "DonorWorkspaceName":
                value = _document().Title;
                return true;
            case "DonorWorkspaceEmoji":
                // The donor shows the space's icon here. A 9to1 board has no icon field, so this is a monogram
                // derived from the real board title rather than an invented glyph.
                value = Monogram(_document().Title);
                return true;
            case "DonorAccountName":
                value = "Local board";   // no account is attached to a standalone Boards file session
                return true;
            case "DonorSidebarSections":
                value = BuildSidebarSections();
                return true;
            case "DonorSearchActive":
                value = _searchActive;
                return true;
            case "DonorSearchIdle":
                value = !_searchActive;
                return true;
            case "DonorSidebarQuery":
                value = _query;
                return true;
            case "DonorHasSidebarMatches":
                value = BuildSidebarSections().Count > 0;
                return true;
            case "DonorOpenTabs":
                value = BuildTabs();
                return true;
            case "DonorHasOpenTabs":
                value = _openTabs.Count > 0;
                return true;
            case "DonorStageOneNotice":
                value = _notice;
                return true;
            default:
                value = null;
                return false;
        }
    }

    private string _notice =
        "AppFlowy board structure, wired to this real board: New page creates a page in that section, Rename and " +
        "Delete act on the section, and opening a page goes to its block editor.";

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        switch (command)
        {
            case "DonorExitStageOneSurface":
                ExitRequested?.Invoke();
                return;
            case "DonorOpenCard" when parameter is string pageId:
                NoteOpened(pageId);
                Raise();
                await _openCardPage(pageId).ConfigureAwait(false);
                return;
            case "DonorCreateCard" when parameter is string sectionId:
                await _createCardInSection(sectionId, null).ConfigureAwait(false);
                Rebuild();
                Raise();
                return;
            case "DonorCreateCardAtTop" when parameter is string sectionId:
                // The donor inserts at the column head; 9to1 page creation appends. Recorded honestly rather than
                // silently pretending the ordering matches.
                await _createCardInSection(sectionId, null).ConfigureAwait(false);
                _notice = "Created at the end of the column: 9to1 page creation does not yet position a page, so " +
                          "the donor's insert-at-top is not reproduced.";
                Rebuild();
                Raise();
                return;
            case "DonorBeginCreateCard" when parameter is string sectionId:
                ToggleCreating(sectionId, create: true);
                return;
            case "DonorCancelCreateCard" when parameter is string sectionId:
                ToggleCreating(sectionId, create: false);
                return;
            case "DonorCommitCreateCard":
                {
                    // The submit event arrives as CuiInputSubmitRequest and the two-way Value binding has already
                    // written the typed text into the column item, so the column is identified by its own state.
                    var target = _groups.FirstOrDefault(group => group.Creating);
                    foreach (var group in _groups)
                        group.Creating = false;
                    if (target is null)
                        return;
                    var title = (target.NewPageDraft ?? string.Empty).Trim();
                    if (title.Length == 0)
                    {
                        Rebuild();
                        Raise();
                        return;
                    }
                    await _createCardInSection(target.Id, title).ConfigureAwait(false);
                    Rebuild();
                    Raise();
                    return;
                }
            case "DonorRenameGroupStart" when parameter is string sectionId:
                foreach (var group in _groups)
                    group.Editing = group.Id == sectionId;
                Raise();
                return;
            case "DonorCommitGroupTitle":
                {
                    var target = _groups.FirstOrDefault(group => group.Editing);
                    if (target is null)
                        return;
                    var title = (target.DraftTitle ?? target.Title).Trim();
                    foreach (var group in _groups)
                        group.Editing = false;
                    if (title.Length > 0)
                    {
                        await _renameSection(target.Id, title).ConfigureAwait(false);
                        Rebuild();
                        Raise();
                    }
                    return;
                }
            case "DonorDeleteGroup" when parameter is string sectionId:
                await _deleteSection(sectionId).ConfigureAwait(false);
                Rebuild();
                Raise();
                return;
            case "DonorHideGroup":
                _notice = "Hiding a section is not wired: 9to1 Boards has no hidden-section concept yet.";
                Raise();
                return;
            case "DonorCreateGroup":
                await _createSection().ConfigureAwait(false);
                Rebuild();
                Raise();
                return;
            case "DonorOpenLayoutSettings":
            case "DonorOpenFilterEditor":
            case "DonorSwitchToGrid":
                _notice = "'" + command + "' is not wired: 9to1 Boards has no board filter, layout-setting panel or " +
                          "grid view yet, so the donor's controls for them stay out of the way instead of doing nothing.";
                Raise();
                return;
            case "DonorOpenSidebarPage":
            case "DonorOpenTab":
                // AppFlowy opens the page document here; 9to1 selects that page and routes into its block editor,
                // the same route the board card uses, and the tab strip remembers it as an open page. Two labels
                // cannot both declare a `when` pattern variable in one switch block (CS0128), hence the guard.
                if (parameter is string treePageId)
                {
                    NoteOpened(treePageId);
                    Rebuild();
                    Raise();
                    await _openCardPage(treePageId).ConfigureAwait(false);
                }
                return;
            case "DonorCloseTab" when parameter is string pageId:
                _openTabs.Remove(pageId);
                Raise();
                return;
            case "DonorToggleSection" when parameter is string sectionId:
                if (!_collapsed.Remove(sectionId))
                    _collapsed.Add(sectionId);
                Raise();
                return;
            case "DonorCreateSectionPage" when parameter is string leafSectionId:
                await _createCardInSection(leafSectionId, null).ConfigureAwait(false);
                NoteCreated(leafSectionId);
                Rebuild();
                Raise();
                return;
            case "DonorCreateRootPage":
                // AppFlowy's "+ New page" creates a page in the space. A 9to1 board page always belongs to a
                // section, so this creates the page in the board's first section (creating one when the board
                // has none) and says so in the notice instead of pretending otherwise.
                {
                    var document = _document();
                    if (document.Sections.Count == 0)
                        await _createSection().ConfigureAwait(false);
                    document = _document();
                    if (document.Sections.Count > 0)
                    {
                        await _createCardInSection(document.Sections[0].Id, null).ConfigureAwait(false);
                        NoteCreated(document.Sections[0].Id);
                    }
                    Rebuild();
                    Raise();
                    return;
                }
            case "DonorSearchPages":
                _searchActive = true;
                Raise();
                return;
            case "DonorCloseSearch":
                _searchActive = false;
                _query = string.Empty;
                Raise();
                return;
            case "DonorFilterPages":
                Raise();
                return;
            default:
                return;
        }
    }

    public bool TrySetValue(string path, object? value)
    {
        if (!string.Equals(path, "DonorSidebarQuery", StringComparison.Ordinal))
            return false;
        _query = value?.ToString() ?? string.Empty;
        Raise();
        return true;
    }

    private void NoteOpened(string pageId)
    {
        if (string.IsNullOrEmpty(pageId))
            return;
        if (!_openTabs.Contains(pageId))
            _openTabs.Add(pageId);
    }

    private void NoteCreated(string sectionId)
    {
        // The create command returns void on this adapter, so the newest page in that section is the new one.
        var section = _document().Sections.FirstOrDefault(s => s.Id == sectionId);
        var page = section?.Pages.Count > 0 ? section!.Pages[^1] : null;
        if (page is not null)
            NoteOpened(page.Id);
    }

    private static string Monogram(string title)
    {
        var trimmed = (title ?? string.Empty).Trim();
        return trimmed.Length == 0 ? "?" : char.ToUpperInvariant(trimmed[0]).ToString();
    }

    private List<SidebarSectionItem> BuildSidebarSections()
    {
        var document = _document();
        var needle = _query.Trim();
        var sections = new List<SidebarSectionItem>();
        foreach (var section in document.Sections)
        {
            var pages = new List<SidebarPageItem>();
            AppendPages(pages, section.Pages, parentId: null, depth: 0, needle, document);
            if (needle.Length > 0 && pages.Count == 0)
                continue;                                   // the donor's search filters the tree
            if (pages.Count == 0 && needle.Length == 0)
                continue;                                   // no page tree entry for an empty section
            sections.Add(new SidebarSectionItem(section.Id, section.Title, pages,
                expanded: !_collapsed.Contains(section.Id)));
        }
        return sections;
    }

    private static void AppendPages(List<SidebarPageItem> output, IEnumerable<RichBoardPage> siblings, string? parentId,
        int depth, string needle, RichBoardDocument document)
    {
        foreach (var page in siblings.Where(page => string.Equals(page.ParentPageId, parentId, StringComparison.Ordinal)))
        {
            var children = document.Sections
                .SelectMany(section => section.Pages)
                .Where(candidate => string.Equals(candidate.ParentPageId, page.Id, StringComparison.Ordinal))
                .ToList();
            var selfMatches = needle.Length == 0
                || page.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || children.Any(child => child.Title.Contains(needle, StringComparison.OrdinalIgnoreCase));
            if (selfMatches)
                output.Add(new SidebarPageItem(page.Id, new string(' ', depth * 2) + page.Title, depth));
            AppendPages(output, children, page.Id, depth + 1, needle, document);
        }
    }

    private List<TabItem> BuildTabs()
    {
        var document = _document();
        var byId = document.Sections.SelectMany(section => section.Pages)
            .ToDictionary(page => page.Id, page => page.Title, StringComparer.Ordinal);
        return _openTabs.Where(byId.ContainsKey)
            .Select(id => new TabItem(id, byId[id], "·"))
            .ToList();
    }

    private void ToggleCreating(string sectionId, bool create)
    {
        foreach (var group in _groups)
            group.Creating = create && group.Id == sectionId;
        Raise();
    }

    private void Rebuild()
    {
        var document = _document();
        var existing = _groups.ToDictionary(group => group.Id, StringComparer.Ordinal);
        _groups.Clear();
        foreach (var section in document.Sections)
        {
            var cards = section.Pages
                .Select(page => new CardItem(this, page.Id, page.Title, page.Blocks
                    .Select(block => block.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Select(text => text.Trim().Length > 160 ? text.Trim()[..160] + "…" : text.Trim())
                    .ToList()))
                .ToList();
            var group = existing.TryGetValue(section.Id, out var reused) ? reused : new GroupCard(this, section.Id);
            group.Title = section.Title;
            group.Items = cards;
            _groups.Add(group);
            foreach (var card in cards)
                card.Owner = this;
        }
    }

    private void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    private sealed record HiddenGroupCard(string Id, string Title, string CountLabel);

    /// <summary>A row in the donor's sidebar page tree. Name is the section title; Pages keeps the donor's
    /// real nesting through RichBoardPage.ParentPageId.</summary>
    private sealed class SidebarSectionItem : ICuiBindingContext
    {
        public SidebarSectionItem(string id, string name, IReadOnlyList<SidebarPageItem> pages, bool expanded)
        {
            Id = id; Name = name; Pages = pages; Expanded = expanded;
        }

        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<SidebarPageItem> Pages { get; }
        public bool Expanded { get; }

        public bool TryGetValue(string path, out object? value)
        {
            value = path switch
            {
                "Id" => Id,
                "Name" => Name,
                "Pages" => Pages,
                "Expanded" => Expanded,
                "Collapsed" => !Expanded,
                "HasPages" => Pages.Count > 0,
                "CountLabel" => Pages.Count + (Pages.Count == 1 ? " page" : " pages"),
                "AccessibleName" => Name,
                _ => null,
            };
            return value is not null;
        }
    }

    private sealed class SidebarPageItem : ICuiBindingContext
    {
        public SidebarPageItem(string id, string title, int depth) { Id = id; Title = title; Depth = depth; }

        public string Id { get; }
        public string Title { get; }
        public int Depth { get; }

        public bool TryGetValue(string path, out object? value)
        {
            value = path switch
            {
                "Id" => Id,
                "Title" => Title,
                "Emoji" => string.Empty,   // 9to1 pages carry no page icon; the donor's per-page emoji is not faked
                "Depth" => Depth,
                "AccessibleName" => "Open " + Title,
                _ => null,
            };
            return value is not null;
        }
    }

    private sealed record TabItem(string Id, string Title, string Emoji) : ICuiBindingContext
    {
        public bool TryGetValue(string path, out object? value)
        {
            value = path switch
            {
                "Id" => Id,
                "Title" => Title,
                "Emoji" => Emoji,
                "AccessibleName" => "Open " + Title,
                _ => null,
            };
            return value is not null;
        }
    }

    /// <summary>A donor card. Its title cell is the 9to1 page title, then one cell per non-empty block, because
    /// the donor's card body is a cell column whose first cell is the row title.</summary>
    private sealed class CardItem : ICuiBindingContext
    {
        public CardItem(AppFlowyBoardStageOneSurface owner, string id, string title, IReadOnlyList<string> previews)
        {
            Owner = owner;
            Id = id;
            Title = title;
            Previews = previews;
        }

        public AppFlowyBoardStageOneSurface Owner { get; set; }
        public string Id { get; }
        public string Title { get; }
        public IReadOnlyList<string> Previews { get; }

        public bool TryGetValue(string path, out object? value)
        {
            value = path switch
            {
                "Id" => Id,
                "Width" => CardWidth,
                "HasCover" => false,
                "CoverSource" => string.Empty,
                "CoverHeight" => 0d,
                "Hovered" => false,
                "Title" => Title,
                "Previews" => Previews,
                "HasPreview" => Previews.Count > 0,
                "AccessibleName" => "Open " + Title,
                _ => null,
            };
            return value is not null;
        }
    }

    /// <summary>A donor group column: a 9to1 section, with the donor's create-card editing state held per column.</summary>
    private sealed class GroupCard : ICuiBindingContext, ICuiWritableBindingContext
    {
        public GroupCard(AppFlowyBoardStageOneSurface owner, string id)
        {
            Owner = owner;
            Id = id;
        }

        public AppFlowyBoardStageOneSurface Owner { get; set; }
        public string Id { get; }
        public string Title { get; set; } = string.Empty;
        public IReadOnlyList<CardItem> Items { get; set; } = Array.Empty<CardItem>();
        public bool Creating { get; set; }
        public bool Editing { get; set; }

        public bool TryGetValue(string path, out object? value)
        {
            value = path switch
            {
                "Id" => Id,
                "Title" => Title,
                "CountLabel" => Items.Count + (Items.Count == 1 ? " page" : " pages"),
                "Width" => GroupColumnWidth,
                "Cards" => Items,
                "CanRename" => true,
                "CanDelete" => true,
                // 9to1 Boards has no hidden-section concept, so the donor's hide command stays visibly unavailable.
                "CanHide" => false,
                "CanCreateCard" => true,
                "Creating" => Creating,
                "NotCreating" => !Creating,
                "Editing" => Editing,
                "NotEditing" => !Editing,
                "DraftTitle" => DraftTitle ?? Title,
                "NewPageDraft" => NewPageDraft ?? string.Empty,
                _ => null,
            };
            return value is not null;
        }

        public string? DraftTitle { get; set; }
        public string? NewPageDraft { get; set; }

        public bool TrySetValue(string path, object? value)
        {
            switch (path)
            {
                case "DraftTitle":
                    DraftTitle = value as string;
                    return true;
                case "NewPageDraft":
                    NewPageDraft = value as string;
                    return true;
                default:
                    return false;
            }
        }
    }
}
