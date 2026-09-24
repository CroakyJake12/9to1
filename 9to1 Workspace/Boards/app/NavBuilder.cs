// NavBuilder renders the product navigation pane: collapsible sections,
// selectable pages with selected/hover states, inline rename, context menus,
// search filtering and subtle add affordances. No implementation ids shown.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace CakeOS.Apps.Boards.App;

public static class NavBuilder
{
    private static readonly HashSet<string> CollapsedSections = new(StringComparer.Ordinal);
    private static object? _stateDocument;

    /// <summary>Pending inline rename: set by double-click, F2 or the Rename menu item.</summary>
    private static (bool IsSection, string SectionId, string? PageId)? _pendingRename;

    /// <summary>True while an inline rename box is open (hosts must not rebuild nav).</summary>
    internal static bool RenameActive { get; private set; }

    public static void Rebuild(StackPanel host, BoardsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(vm);
        var document = vm.Session.Document;
        if (!ReferenceEquals(_stateDocument, document))
        {
            CollapsedSections.Clear();
            _pendingRename = null;
            RenameActive = false;
            _stateDocument = document;
        }
        RenameActive = false;
        host.Children.Clear();

        var filter = vm.SearchText?.Trim() ?? string.Empty;
        var sections = vm.Session.Document.Sections;
        if (sections.Count == 0)
        {
            host.Children.Add(Hint("No sections yet — add one below."));
            return;
        }
        var anyMatch = false;
        foreach (var section in sections)
        {
            var showSection = Matches(section.Title, filter)
                || section.Pages.Any(p => Matches(p.Title, filter));
            if (!showSection)
                continue;
            anyMatch = true;
            var pages = string.IsNullOrEmpty(filter)
                ? section.Pages
                : section.Pages.Where(p => Matches(p.Title, filter) || Matches(section.Title, filter)).ToList();
            var collapsed = CollapsedSections.Contains(section.Id) && string.IsNullOrEmpty(filter);
            host.Children.Add(BuildSectionHeader(host, vm, section, collapsed));
            if (collapsed)
                continue;
            if (pages.Count == 0)
                host.Children.Add(Hint("No pages — add one below."));
            foreach (var page in pages)
                host.Children.Add(BuildPageRow(host, vm, section, page));
        }
        if (!anyMatch)
            host.Children.Add(Hint($"No matches for “{filter}”."));
        TakePendingRename(host, vm);
    }

    private static bool Matches(string title, string filter) =>
        string.IsNullOrEmpty(filter) || title.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static Control Hint(string text) => new TextBlock
    {
        Text = text,
        FontSize = 12,
        Foreground = BoardsTheme.SecondaryTextBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(4, 4, 4, 4),
    };

    private static Control BuildSectionHeader(
        StackPanel host, BoardsViewModel vm, RichBoardSection section, bool collapsed)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 2) };
        var chevron = new Button
        {
            Content = BoardsIcons.Glyph(collapsed ? BoardsIcons.ChevronRight : BoardsIcons.ChevronDown, 11),
            Padding = new Thickness(4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(chevron, collapsed ? "Expand section" : "Collapse section");
        Avalonia.Automation.AutomationProperties.SetName(chevron, (collapsed ? "Expand " : "Collapse ") + section.Title);
        chevron.Click += (_, _) =>
        {
            if (collapsed)
                CollapsedSections.Remove(section.Id);
            else
                CollapsedSections.Add(section.Id);
            vm.RequestNavRebuild();
        };
        var title = new Button
        {
            Content = new TextBlock
            {
                Text = section.Title,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = BoardsTheme.TextBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(4),
        };
        ToolTip.SetTip(title, "Open first page of " + section.Title);
        Avalonia.Automation.AutomationProperties.SetName(title, "Section " + section.Title);
        title.Click += (_, _) => vm.SelectSection(section.Id);
        title.DoubleTapped += (_, _) =>
        {
            _pendingRename = (true, section.Id, null);
            vm.RequestNavRebuild();
        };
        title.KeyDown += (_, e) =>
        {
            if (e.Key == Key.F2)
            {
                _pendingRename = (true, section.Id, null);
                vm.RequestNavRebuild();
                e.Handled = true;
            }
        };
        var menu = new ContextMenu();
        AddMenuItem(menu, "Rename", () =>
        {
            _pendingRename = (true, section.Id, null);
            vm.RequestNavRebuild();
        });
        AddMenuItem(menu, "Duplicate", async () => await vm.DuplicateSectionAsync(section.Id));
        AddMenuItem(menu, "Move up", async () => await vm.MoveSectionAsync(section.Id, -1));
        AddMenuItem(menu, "Move down", async () => await vm.MoveSectionAsync(section.Id, 1));
        AddMenuItem(menu, "Delete", async () => await vm.DeleteSectionAsync(section.Id));
        title.ContextMenu = menu;
        row.Children.Add(chevron);
        row.Children.Add(title);
        return row;
    }

    private static Control BuildPageRow(
        StackPanel host, BoardsViewModel vm, RichBoardSection section, RichBoardPage page)
    {
        var selected = vm.SelectedPageId == page.Id && vm.SelectedSectionId == section.Id;
        var button = new Button
        {
            Background = selected ? BoardsTheme.AccentBrush : Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(28, 6, 8, 6),
            Margin = new Thickness(0, 1, 0, 1),
        };
        button.Content = new TextBlock
        {
            Text = page.Title,
            FontSize = 13,
            Foreground = selected ? BoardsTheme.AccentContrastBrush : BoardsTheme.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        ToolTip.SetTip(button, page.Title);
        Avalonia.Automation.AutomationProperties.SetName(button, "Page " + page.Title);
        button.Click += (_, _) => vm.SelectPage(section.Id, page.Id);
        button.DoubleTapped += (_, _) =>
        {
            _pendingRename = (false, section.Id, page.Id);
            vm.RequestNavRebuild();
        };
        button.KeyDown += (_, e) =>
        {
            if (e.Key == Key.F2)
            {
                _pendingRename = (false, section.Id, page.Id);
                vm.RequestNavRebuild();
                e.Handled = true;
            }
        };
        var menu = new ContextMenu();
        AddMenuItem(menu, "Rename", () =>
        {
            _pendingRename = (false, section.Id, page.Id);
            vm.RequestNavRebuild();
        });
        AddMenuItem(menu, "Duplicate", async () => await vm.DuplicatePageAsync(section.Id, page.Id));
        AddMenuItem(menu, "Move up", async () => await vm.MovePageAsync(section.Id, page.Id, -1));
        AddMenuItem(menu, "Move down", async () => await vm.MovePageAsync(section.Id, page.Id, 1));
        AddMenuItem(menu, "Delete", async () => await vm.DeletePageAsync(section.Id, page.Id));
        button.ContextMenu = menu;
        return button;
    }

    private static void TakePendingRename(StackPanel host, BoardsViewModel vm)
    {
        if (_pendingRename is not var (isSection, sectionId, pageId))
            return;
        _pendingRename = null;
        string current = isSection
            ? vm.Session.Document.Sections.FirstOrDefault(s => s.Id == sectionId)?.Title ?? string.Empty
            : vm.Session.Document.Sections.FirstOrDefault(s => s.Id == sectionId)?.Pages
                .FirstOrDefault(p => p.Id == pageId)?.Title ?? string.Empty;
        var box = new TextBox
        {
            Text = current,
            FontSize = 13,
            Margin = new Thickness(24, 2, 4, 2),
        };
        Avalonia.Automation.AutomationProperties.SetName(box, "Rename to");
        host.Children.Add(box);
        RenameActive = true;
        box.Focus();
        box.SelectAll();
        var done = false;
        void Commit(bool save)
        {
            if (done)
                return;
            done = true;
            RenameActive = false;
            if (save)
            {
                if (isSection)
                    vm.RenameSection(sectionId, box.Text ?? string.Empty);
                else if (pageId is not null)
                    vm.RenamePage(sectionId, pageId, box.Text ?? string.Empty);
            }
            else
            {
                vm.RequestNavRebuild();
            }
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit(save: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Commit(save: false);
                e.Handled = true;
            }
        };
        box.LostFocus += (_, _) => Commit(save: true);
    }

    private static void AddMenuItem(ContextMenu menu, string header, Action tapped)
    {
        var item = new MenuItem { Header = header };
        Avalonia.Automation.AutomationProperties.SetName(item, header);
        item.Click += (_, _) => tapped();
        menu.Items.Add(item);
    }

    private static void AddMenuItem(ContextMenu menu, string header, Func<Task> tapped)
    {
        var item = new MenuItem { Header = header };
        Avalonia.Automation.AutomationProperties.SetName(item, header);
        item.Click += async (_, _) => await tapped();
        menu.Items.Add(item);
    }
}

