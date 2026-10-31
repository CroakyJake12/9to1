using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui;

namespace CakeOS.Apps.Boards.App;

/// <summary>
/// Stage one of the owner's pinned Boards route (31 October 2026): "the intended boards surface is appflowy re-ui
/// first, then transition that re-ui into the 9to1 boards". This is the binding context for
/// <c>AppFlowyBoardStageOne.cui</c>, which re-renders AppFlowy's desktop board screen in CUI with its structure
/// unchanged. It projects the REAL current board onto the donor's group/card shape, so nothing rendered is invented:
/// group = a 9to1 Board section, card = a 9to1 Board page, the title cell = the page's first non-empty block text.
/// </summary>
/// <remarks>
/// The mapping is deliberately provisional and kept in one place. Whether 9to1 Boards carries AppFlowy's
/// database-view semantics at all (grid, calendar, per-type cell editors) is the open owner question
/// <c>OPEN-2026-10-31-BOARDS-DONOR-VIEW-SCOPE</c>, and that answer changes the Boards requirement denominator.
///
/// Donor commands belonging to stage two are reported as unavailable rather than faked: the controls bound to
/// <see cref="DonorCanFilter"/>, <c>group.CanRename</c>/<c>CanHide</c>/<c>CanDelete</c>/<c>CanCreateCard</c> and
/// <see cref="DonorCanCreateGroup"/> render disabled, and anything that still reaches <see cref="DispatchAsync"/>
/// says so in <see cref="DonorStageOneNotice"/>. Two things are genuinely live: opening a card selects the page it
/// was projected from, and collapse/expand of the hidden-groups column is view state this surface owns. Hover
/// accessories stay hidden because <c>card.Hovered</c> is false until CUI exposes the donor's hover seam — recorded
/// here rather than worked around with a fake hover.
/// </remarks>
internal sealed class AppFlowyBoardStageOneSurface : ICuiBindingContext, ICuiActionDispatcher
{
    private const double GroupColumnWidth = 280;   // donor column presentation; stage two binds the layout setting
    private const double CardWidth = 256;

    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CardItem> _cards = new(StringComparer.Ordinal);
    private readonly Func<string, Task> _openCardPage;

    public AppFlowyBoardStageOneSurface(RichBoardDocument notes, Func<string, Task> openCardPage)
    {
        ArgumentNullException.ThrowIfNull(notes);
        _openCardPage = openCardPage ?? throw new ArgumentNullException(nameof(openCardPage));

        var groups = new List<GroupCard>();
        foreach (var section in notes.Sections)
        {
            var cards = new List<CardItem>();
            foreach (var page in section.Pages)
            {
                var card = BuildCard(page);
                _cards[card.Id] = card;
                cards.Add(card);
            }
            groups.Add(new GroupCard(
                section.Id,
                section.Title,
                section.Pages.Count + (section.Pages.Count == 1 ? " card" : " cards"),
                GroupColumnWidth,
                CanRename: false,
                CanHide: false,
                CanDelete: false,
                CanCreateCard: false,
                Cards: cards));
        }

        _values["DonorBoardTitle"] = notes.Title;
        _values["DonorGroupColumns"] = groups;
        _values["DonorStageOneNotice"] =
            "AppFlowy structure projected from this real board. Rename, hide, delete and card creation belong to " +
            "stage two and are disabled here.";
        // 9to1 Boards has no hidden groups, reference views or board filters yet, so those donor branches render
        // absent rather than showing an empty donor control that does nothing.
        _values["DonorHasHiddenGroups"] = false;
        _values["DonorHiddenGroupsCollapsed"] = false;
        _values["DonorHiddenGroupsExpanded"] = false;
        _values["DonorHiddenGroups"] = Array.Empty<HiddenGroupCard>();
        _values["DonorIsViewReference"] = false;
        _values["DonorCanFilter"] = false;
        const bool canCreateGroup = false;   // 9to1 Boards has no grouping-field creation yet (stage two)
        _values["DonorCanCreateGroup"] = canCreateGroup;
        _values["DonorTrailingWidth"] = (double)GroupColumnWidth;
        // Donor board_page.dart:365 - with no creatable grouping field the trailing column is absent
        // and the donor leaves a 40-wide spacer there instead.
        _values["DonorShowsTrailingSpacer"] = !canCreateGroup;
    }

    public event Action? ExitRequested;

    public bool TryGetValue(string path, out object? value) => _values.TryGetValue(path, out value);

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        switch (command)
        {
            case "DonorExitStageOneSurface":
                ExitRequested?.Invoke();
                return ValueTask.CompletedTask;
            case "DonorExpandHiddenGroups":
                _values["DonorHiddenGroupsCollapsed"] = false;
                _values["DonorHiddenGroupsExpanded"] = true;
                return ValueTask.CompletedTask;
            case "DonorCollapseHiddenGroups":
                _values["DonorHiddenGroupsCollapsed"] = true;
                _values["DonorHiddenGroupsExpanded"] = false;
                return ValueTask.CompletedTask;
            case "DonorOpenCard" when parameter is string cardId && _cards.ContainsKey(cardId):
                // Real behaviour: the donor opens a card; the equivalent available today is selecting the page this
                // card was projected from on the actual board.
                return new ValueTask(_openCardPage(cardId));
            default:
                _values["DonorStageOneNotice"] =
                    "'" + command + "' is a stage-two command: it is not wired on this surface and nothing changed.";
                return ValueTask.CompletedTask;
        }
    }

    private static CardItem BuildCard(RichBoardPage page)
    {
        var cells = new List<CardCell>();
        foreach (var block in page.Blocks)
        {
            if (string.IsNullOrWhiteSpace(block.Text))
                continue;
            var text = block.Text.Trim();
            if (text.Length > 160)
                text = text[..160] + "…";
            var isTitle = cells.Count == 0;
            cells.Add(new CardCell(
                page.Id + "-cell-" + cells.Count,
                text,
                isTitle ? "Bold" : "Normal",
                isTitle ? "Card title" : "Card detail"));
        }
        if (cells.Count == 0)
            cells.Add(new CardCell(page.Id + "-cell-0", page.Title, "Bold", "Card title"));

        // The donor shows a cover only when the row carries one; a 9to1 page has no cover asset yet, so HasCover is
        // false and no placeholder image is invented.
        return new CardItem(page.Id, CardWidth, HasCover: false, CoverSource: string.Empty, CoverHeight: 0d,
            Hovered: false, Cells: cells);
    }

    private sealed record HiddenGroupCard(string Id, string Title, string CountLabel) : ICuiBindingContext
    {
        public bool TryGetValue(string path, out object? value)
        {
            value = path switch
            {
                "Id" => Id,
                "Title" => Title,
                "CountLabel" => CountLabel,
                _ => null,
            };
            return value is not null;
        }
    }

    private sealed record CardCell(string Id, string Text, string TitleWeight, string AccessibleName) : ICuiBindingContext
    {
        public bool TryGetValue(string path, out object? value)
        {
            value = path switch
            {
                "Id" => Id,
                "Text" => Text,
                "TitleWeight" => TitleWeight,
                "AccessibleName" => AccessibleName,
                _ => null,
            };
            return value is not null;
        }
    }

    private sealed record CardItem(string Id, double Width, bool HasCover, string CoverSource, double CoverHeight,
        bool Hovered, IReadOnlyList<CardCell> Cells) : ICuiBindingContext
    {
        public bool TryGetValue(string path, out object? value)
        {
            value = path switch
            {
                "Id" => Id,
                "Width" => Width,
                "HasCover" => HasCover,
                "CoverSource" => CoverSource,
                "CoverHeight" => CoverHeight,
                "Hovered" => Hovered,
                "Cells" => Cells,
                _ => null,
            };
            // HasCover/CoverHeight/Hovered are false or zero for a real reason (a 9to1 page has no cover asset and
            // the CUI hover seam is not wired yet), so false must still resolve rather than fall through.
            return value is not null;
        }
    }

    private sealed record GroupCard(string Id, string Title, string CountLabel, double Width, bool CanRename,
        bool CanHide, bool CanDelete, bool CanCreateCard, IReadOnlyList<CardItem> Cards) : ICuiBindingContext
    {
        public bool TryGetValue(string path, out object? value)
        {
            value = path switch
            {
                "Id" => Id,
                "Title" => Title,
                "CountLabel" => CountLabel,
                "Width" => Width,
                "CanRename" => CanRename,
                "CanHide" => CanHide,
                "CanDelete" => CanDelete,
                "CanCreateCard" => CanCreateCard,
                "Cards" => Cards,
                _ => null,
            };
            return value is not null;
        }
    }
}
