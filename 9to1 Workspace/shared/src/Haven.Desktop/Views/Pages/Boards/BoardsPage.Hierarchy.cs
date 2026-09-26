using Avalonia.Controls;
using Avalonia.Layout;
using Haven.Application;
using Haven.Core;
using Haven.UI.Components;
using DomainPage = Haven.Core.NotesPage;
using DomainSection = Haven.Core.NotesSection;

namespace Haven.Desktop.Views.Pages.Boards;

public sealed partial class BoardsPage
{
    private void RebuildHierarchy()
    {
        _sections.Children.Clear();
        UpdateBoardSwitcher();
        if (_document is null) return;
        var document = _document;

        foreach (var section in _document.Sections)
        {
            var local = section;
            var button = ActionButton(
                (ReferenceEquals(section, _section) ? "• " : "") + section.Title,
                async () =>
                {
                    _section = local;
                    _page = local.Pages.OrderBy(item => item.Order).FirstOrDefault();
                    RebuildHierarchy();
                    RebuildPageTabs();
                    RebuildEditor();
                    await Task.CompletedTask;
                });
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            _sections.Children.Add(button);
            foreach (var page in section.Pages.Where(value => !BoardsWorkspaceService.IsPageDeleted(document, value.Id))
                         .OrderBy(value => value.Order))
            {
                var localPage = page;
                var pageButton = ActionButton(
                    (page.Id == _page?.Id ? "• " : "") + "↳ " + page.Title,
                    () => SelectHierarchyPageAsync(local, localPage));
                pageButton.HorizontalContentAlignment = HorizontalAlignment.Left;
                pageButton.Margin = new Avalonia.Thickness(16, 0, 0, 0);
                _sections.Children.Add(pageButton);
            }
        }

        if (_document is not null)
        {
            var deletedPages = _boards.ListDeletedPages(document);
            if (deletedPages.Count > 0)
            {
                _sections.Children.Add(new TextBlock { Text = "Deleted pages", FontSize = 11, Margin = new Avalonia.Thickness(4, 10, 0, 0) });
                foreach (var deleted in deletedPages)
                {
                    var localDeleted = deleted;
                    var restore = ActionButton("Restore · " + deleted.Title, async () => await RestorePageAsync(localDeleted.Id));
                    restore.HorizontalContentAlignment = HorizontalAlignment.Left;
                    _sections.Children.Add(restore);
                }
            }
        }
    }

    private void RebuildPageTabs()
    {
        var document = _document;
        var pages = _section?.Pages.Where(item => document is not null && !BoardsWorkspaceService.IsPageDeleted(document, item.Id))
            .OrderBy(item => item.Order).ToArray() ?? [];
        _pageTabs.SetItems(pages.Select(item =>
            new Haven.UI.Components.TabStripItem(item.Id.ToString("D"), item.Title, item.Id == _page?.Id, false)).ToArray());
    }

    private void OnPageTabInvoked(object? sender, string key)
    {
        if (_section is null || !Guid.TryParse(key, out var id)) return;
        var page = _section.Pages.FirstOrDefault(item => item.Id == id && !BoardsWorkspaceService.IsPageDeleted(_document!, item.Id));
        if (page is null) return;
        _page = page;
        RebuildPageTabs();
        RebuildEditor();
        SetStatus($"{_section.Title} · {page.Title}");
    }

    private async Task AddSectionAsync()
    {
        if (_document is null) return;
        var section = _boards.AddSection(_document);
        _section = section;
        _page = section.Pages[0];
        RebuildHierarchy();
        RebuildPageTabs();
        RebuildEditor();
        await SaveAsync("Added Boards section");
    }

    private async Task AddPageAsync()
    {
        if (_section is null || _document is null) return;
        var page = _boards.AddPage(_document, _section.Id);
        _page = page;
        RebuildPageTabs();
        RebuildEditor();
        await SaveAsync("Added Boards page");
    }

    private Task SelectHierarchyPageAsync(DomainSection section, DomainPage page)
    {
        _section = section;
        _page = page;
        RebuildHierarchy();
        RebuildPageTabs();
        RebuildEditor();
        SetStatus($"{section.Title} · {page.Title}");
        return Task.CompletedTask;
    }

    private async Task DeleteCurrentPageAsync()
    {
        if (_document is null || _section is null || _page is null) return;
        if (!_boards.DeletePage(_document, _page.Id))
        {
            SetStatus("The final page in a section cannot be moved to Trash.");
            return;
        }
        _page = _section.Pages.Where(value => !BoardsWorkspaceService.IsPageDeleted(_document, value.Id))
            .OrderBy(value => value.Order).FirstOrDefault();
        RebuildHierarchy();
        RebuildPageTabs();
        RebuildEditor();
        await SaveAsync("Moved Boards page to recoverable Trash");
    }

    private async Task RestorePageAsync(Guid pageId)
    {
        if (_document is null || !_boards.RestorePage(_document, pageId)) return;
        var section = _document.Sections.FirstOrDefault(value => value.Pages.Any(page => page.Id == pageId));
        _section = section ?? _section;
        _page = section?.Pages.FirstOrDefault(value => value.Id == pageId) ?? _page;
        RebuildHierarchy();
        RebuildPageTabs();
        RebuildEditor();
        await SaveAsync("Restored Boards page from Trash");
    }

    private async Task AddBlockAsync(NotesBlockKind kind)
    {
        if (_page is null || _document is null) return;
        _boards.AddBlock(_document, _page.Id, kind);
        RebuildEditor();
        await SaveAsync($"Added {kind} block");
    }

    private async Task AddInkAsync()
    {
        if (_page is null || _document is null) return;
        _boards.AddBlock(_document, _page.Id, NotesBlockKind.Canvas);
        RebuildEditor();
        await SaveAsync("Added Boards ink canvas");
    }

    private async Task AddLiveComponentAsync(BoardsLiveComponentKind kind)
    {
        if (_document is null || _page is null) return;
        var component = _boards.AddComponent(_document, _page, kind);
        _boards.PlaceComponent(_document, _page, component.Id);
        RebuildEditor();
        await SaveAsync($"Added Boards {kind} live component");
    }
}
