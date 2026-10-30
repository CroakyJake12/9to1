using Avalonia.Controls;

namespace CakeOS.Apps.Boards.App;

public sealed partial class BoardsViewModel
{
    private BoardsViewModel? _activeToolbarPane;
    internal event Action? ToolbarChanged;
    private static readonly string[] ToolbarRangeBindings =
    [
        "CanFormatTextRange", "TextRangeSummary", "RangeBoldSelected", "RangeItalicSelected",
        "RangeUnderlineSelected", "RangeStrikeSelected", "RangeBoldState", "RangeItalicState",
        "RangeUnderlineState", "RangeStrikeState"
    ];

    // A toolbar retains its target while it takes focus, but never across a hidden,
    // removed or detached pane. The canonical page selection still belongs to Home.
    internal BoardsViewModel ToolbarTarget => _activeToolbarPane is { } pane &&
        pane.IsCurrentToolbarPane ? pane : this;

    private bool PaneIsSelected => _paneOwner is null || _paneIdentity is { } identity &&
        _paneOwner.FindBlock(identity.OwnerBlockId)?.Tabber is { } tabs &&
        tabs.Panes.Any(pane => pane.Id == tabs.SelectedPaneId && pane.PageId == identity.PageId);

    private bool IsCurrentToolbarPane => _paneOwner is { } owner &&
        _paneIdentity is { } identity && !_disposed && !EditorLifetimeEnded &&
        owner.IsEditMode && IsEditMode && owner.SelectedPageId == identity.ParentPageId &&
        _themeSurface is { IsEffectivelyVisible: true } surface && TopLevel.GetTopLevel(surface) is not null &&
        PaneIsSelected;

    internal void ActivateToolbar()
    {
        if (_paneOwner is { } owner)
        {
            if (IsCurrentToolbarPane) owner.SetToolbarPane(this);
        }
        else SetToolbarPane(null);
    }

    private void SetToolbarPane(BoardsViewModel? pane)
    {
        if (ReferenceEquals(_activeToolbarPane, pane)) return;
        _activeToolbarPane = pane;
        RefreshTextRangeBindings();
        ToolbarChanged?.Invoke();
    }

    internal void RefreshSharedToolbar()
    {
        if (_paneOwner is { } owner && ReferenceEquals(owner.ToolbarTarget, this))
            owner.ToolbarChanged?.Invoke();
    }

    private void PublishToolbarRangeBindings()
    {
        if (_paneOwner is { } owner && ReferenceEquals(owner.ToolbarTarget, this))
            owner.CopyToolbarRangeBindings(this);
    }

    private void CopyToolbarRangeBindings(BoardsViewModel pane)
    {
        foreach (var key in ToolbarRangeBindings) Set(key, pane.Get(key));
    }

    private void ReleaseToolbar()
    {
        if (_paneOwner is { } owner && ReferenceEquals(owner._activeToolbarPane, this))
            owner.SetToolbarPane(null);
        SetToolbarPane(null);
    }
}
