// NavBuilder renders the product navigation pane: collapsible sections,
// selectable pages with selected/hover states, inline rename, context menus,
// search filtering and subtle add affordances. No implementation ids shown.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CakeOS.Cui.Runtime;

namespace CakeOS.Apps.Boards.App;

public static class NavBuilder
{
    private static readonly HashSet<string> CollapsedSections = new(StringComparer.Ordinal);
    private static readonly HashSet<string> CollapsedPages = new(StringComparer.Ordinal);
    private static IRichBoardSession? _stateSession;
    private static (string SectionId, string PageId)? _lastRevealedSelection;

    /// <summary>Pending inline rename: set by double-click, F2 or the Rename menu item.</summary>
    private static (bool IsSection, string SectionId, string? PageId)? _pendingRename;

    /// <summary>True while an inline rename box is open (hosts must not rebuild nav).</summary>
    internal static bool RenameActive { get; private set; }

    public static void Rebuild(StackPanel host, BoardsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(vm);
        if (!ReferenceEquals(_stateSession, vm.Session))
        {
            CollapsedSections.Clear();
            CollapsedPages.Clear();
            _pendingRename = null;
            RenameActive = false;
            _lastRevealedSelection = null;
            _stateSession = vm.Session;
        }
        RevealSelectedPageOnNavigation(vm);
        RenameActive = false;
        host.Children.Clear();

        var filter = vm.SearchText?.Trim() ?? string.Empty;
        var sections = vm.Session.Document.Sections.Select(section => new RichBoardSection
        {
            Id = section.Id, Title = section.Title, IconKey = section.IconKey, IconColor = section.IconColor,
            GeneratedIcon = section.GeneratedIcon,
            Pages = section.Pages.Where(page => page.TabberOwnerBlockId is null ||
                !section.Pages.Any(parent => parent.Blocks.Any(block => block.Id == page.TabberOwnerBlockId &&
                    block.Tabber?.Panes.Any(pane => pane.PageId == page.Id) == true))).ToList()
        }).ToList();
        if (sections.Count == 0)
        {
            host.Children.Add(Hint("No sections yet — add one below."));
            return;
        }
        var anyMatch = false;
        foreach (var section in sections)
        {
            var matchCache = new Dictionary<string, bool>(StringComparer.Ordinal);
            bool HasMatch(RichBoardPage page)
            {
                if (matchCache.TryGetValue(page.Id, out var cached)) return cached;
                matchCache[page.Id] = false; // Defensive cycle stop even for an in-memory malformed draft.
                var matched = Matches(section.Title, filter) || Matches(page.Title, filter) ||
                    section.Pages.Where(child => child.ParentPageId == page.Id).Any(HasMatch);
                matchCache[page.Id] = matched;
                return matched;
            }
            var showSection = Matches(section.Title, filter)
                || section.Pages.Where(p => p.ParentPageId is null).Any(HasMatch);
            if (!showSection)
                continue;
            anyMatch = true;
            var collapsed = CollapsedSections.Contains(section.Id) && string.IsNullOrEmpty(filter);
            host.Children.Add(BuildSectionHeader(host, vm, section, collapsed));
            if (collapsed)
                continue;
            if (section.Pages.Count == 0)
                host.Children.Add(Hint("No pages — add one below."));
            foreach (var page in section.Pages.Where(page => page.ParentPageId is null))
                AddPageTree(page, 0);

            void AddPageTree(RichBoardPage page, int depth)
            {
                var children = section.Pages.Where(child => child.ParentPageId == page.Id).ToArray();
                var matches = Matches(page.Title, filter) || children.Any(HasMatch);
                if (matches)
                    host.Children.Add(BuildPageRow(host, vm, section, page, depth, children.Length > 0,
                        CollapsedPages.Contains(page.Id) && string.IsNullOrEmpty(filter)));
                if (matches && (string.IsNullOrEmpty(filter) && !CollapsedPages.Contains(page.Id) ||
                                !string.IsNullOrEmpty(filter)))
                    foreach (var child in children) AddPageTree(child, depth + 1);
            }
        }
        if (!anyMatch)
            host.Children.Add(Hint($"No matches for “{filter}”."));
        TakePendingRename(host, vm);
    }

    private static void RevealSelectedPageOnNavigation(BoardsViewModel vm)
    {
        var selection = (vm.SelectedSectionId, vm.SelectedPageId);
        if (_lastRevealedSelection == selection) return;
        _lastRevealedSelection = selection;
        var section = vm.Session.Document.Sections.FirstOrDefault(item => item.Id == selection.SelectedSectionId);
        if (section is null) return;
        CollapsedSections.Remove(section.Id);
        var pages = section.Pages.ToDictionary(page => page.Id, StringComparer.Ordinal);
        if (!pages.TryGetValue(selection.SelectedPageId, out var page)) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (page.ParentPageId is { } parentId && seen.Add(parentId) && pages.TryGetValue(parentId, out var parent))
        {
            CollapsedPages.Remove(parent.Id);
            page = parent;
        }
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
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("24,20,*,Auto"),
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var chevron = new Button
        {
            Content = BoardsIcons.Glyph(collapsed ? BoardsIcons.ChevronRight : BoardsIcons.ChevronDown, 11),
            Width = 24,
            Height = 30,
            Padding = new Thickness(2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        SetNavigationButtonPresentation(chevron, "Boards section disclosure", new CornerRadius(6));
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
        row.Children.Add(chevron);
        Control sectionIcon;
        if (section.GeneratedIcon is { } generatedIcon)
        {
            var image = new Image
            {
                Width = 16,
                Height = 16,
                Stretch = Avalonia.Media.Stretch.Uniform,
                Source = BoardsCuiIconResolver.CreateGeneratedIcon(generatedIcon,
                    BoardsCuiIconResolver.SemanticBrush("section", colorOverride: section.IconColor)),
                VerticalAlignment = VerticalAlignment.Center,
            };
            BoardsCuiIconResolver.ObserveGeneratedIcon(image, generatedIcon, section.IconColor);
            ToolTip.SetTip(image, "Generated section icon");
            Avalonia.Automation.AutomationProperties.SetName(image, "Generated section icon");
            sectionIcon = image;
        }
        else
        {
            var image = new Image
            {
                Source = BoardsCuiIconResolver.CreateSectionIcon(section.IconKey,
                    BoardsCuiIconResolver.SemanticBrush(section.IconKey, colorOverride: section.IconColor)),
                Width = 16,
                Height = 16,
                Stretch = Avalonia.Media.Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            BoardsCuiIconResolver.ObserveSemanticIcon(image, section.IconKey, section.IconColor);
            Avalonia.Automation.AutomationProperties.SetName(image, "Section icon");
            ToolTip.SetTip(image, "Section icon");
            sectionIcon = image;
        }
        Grid.SetColumn(sectionIcon, 1);
        row.Children.Add(sectionIcon);
        var title = new Button
        {
            Content = new TextBlock
            {
                Text = section.Title,
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Foreground = BoardsTheme.TextBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 0),
        };
        SetNavigationButtonPresentation(title, "Boards section row label", new CornerRadius(6));
        Grid.SetColumn(title, 2);
        ToolTip.SetTip(title, "Open first page of " + section.Title);
        Avalonia.Automation.AutomationProperties.SetName(title, "Section " + section.Title);
        title.Click += (_, _) => vm.SelectSection(section.Id);
        title.DoubleTapped += (_, _) =>
        {
            if (!vm.CanEdit) return;
            _pendingRename = (true, section.Id, null);
            vm.RequestNavRebuild();
        };
        title.KeyDown += (_, e) =>
        {
            if (vm.CanEdit && e.Key == Key.F2)
            {
                _pendingRename = (true, section.Id, null);
                vm.RequestNavRebuild();
                e.Handled = true;
            }
        };
        if (vm.CanEdit && vm.IsEditMode)
        {
            var menu = new ContextMenu();
            AddMenuItem(menu, "Rename", () =>
            {
                _pendingRename = (true, section.Id, null);
                vm.RequestNavRebuild();
            });
            AddMenuItem(menu, "Icon and color…", () => vm.OpenSectionIconPicker(section.Id));
            AddMenuItem(menu, "Duplicate", async () => await vm.DuplicateSectionAsync(section.Id));
            AddMenuItem(menu, "Move up", async () => await vm.MoveSectionAsync(section.Id, -1));
            AddMenuItem(menu, "Move down", async () => await vm.MoveSectionAsync(section.Id, 1));
            AddMenuItem(menu, "Delete", async () => await vm.DeleteSectionAsync(section.Id));
            title.ContextMenu = menu;
        }
        row.Children.Add(title);
        var header = new Border
        {
            Child = row,
            Margin = new Thickness(0, 1, 0, 1),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
        };
        CuiPropertyPresentation.Set(header, "Background", Brushes.Transparent,
            CuiPropertyLevel.Instance, "Boards section row surface");
        CuiPropertyPresentation.Set(header, "CornerRadius", new CornerRadius(6),
            CuiPropertyLevel.Instance, "Boards section row shape");
        return header;
    }

    private static Control BuildPageRow(
        StackPanel host, BoardsViewModel vm, RichBoardSection section, RichBoardPage page,
        int depth, bool hasChildren, bool collapsed)
    {
        var selected = vm.SelectedPageId == page.Id && vm.SelectedSectionId == section.Id;
        var rowRadius = new CornerRadius(selected ? 8 : 4);
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("3,24,20,*,Auto"),
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var disclosure = new Button
        {
            Width = 24,
            Height = 30,
            Padding = new Thickness(2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        SetNavigationButtonPresentation(disclosure, "Boards page disclosure", rowRadius);
        if (hasChildren)
        {
            disclosure.Content = BoardsIcons.Glyph(collapsed ? BoardsIcons.ChevronRight : BoardsIcons.ChevronDown, 10);
            ToolTip.SetTip(disclosure, collapsed ? "Expand subpages" : "Collapse subpages");
            Avalonia.Automation.AutomationProperties.SetName(disclosure,
                (collapsed ? "Expand subpages of " : "Collapse subpages of ") + page.Title);
            disclosure.Click += (_, _) =>
            {
                if (collapsed) CollapsedPages.Remove(page.Id); else CollapsedPages.Add(page.Id);
                vm.RequestNavRebuild();
            };
        }
        else
        {
            disclosure.IsEnabled = false;
            ToolTip.SetTip(disclosure, "No subpages");
            Avalonia.Automation.AutomationProperties.SetName(disclosure, "No subpages for " + page.Title);
        }
        var selectedMarker = new Border
        {
            Width = 3,
            Height = 18,
            CornerRadius = new CornerRadius(1),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        CuiPropertyPresentation.Set(selectedMarker, "Background",
            selected ? new SolidColorBrush(BoardsTheme.SharedPalette.AccentStrong) : Brushes.Transparent,
            CuiPropertyLevel.Instance, "Boards selected page marker");
        Grid.SetColumn(selectedMarker, 0);
        row.Children.Add(selectedMarker);
        row.Children.Add(disclosure);
        Grid.SetColumn(disclosure, 1);
        var pageIcon = new Image
        {
            Source = BoardsCuiIconResolver.CreateSectionIcon(BoardsIcons.Page,
                BoardsCuiIconResolver.SemanticBrush(BoardsIcons.Page)),
            Width = 16,
            Height = 16,
            Stretch = Avalonia.Media.Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        BoardsCuiIconResolver.ObserveSemanticIcon(pageIcon, BoardsIcons.Page);
        ToolTip.SetTip(pageIcon, "Page");
        Avalonia.Automation.AutomationProperties.SetName(pageIcon, "Page icon");
        Grid.SetColumn(pageIcon, 2);
        row.Children.Add(pageIcon);
        var button = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 0),
        };
        SetNavigationButtonPresentation(button, "Boards page row label", rowRadius);
        var pageTitle = new TextBlock
        {
            Text = page.Title,
            FontSize = 13,
            Foreground = BoardsTheme.TextBrush,
            FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        CuiPropertyPresentation.Set(pageTitle, "Foreground", pageTitle.Foreground,
            CuiPropertyLevel.Instance, "Boards page title text");
        button.Content = pageTitle;
        Grid.SetColumn(button, 3);
        ToolTip.SetTip(button, page.Title);
        Avalonia.Automation.AutomationProperties.SetName(button, "Page " + page.Title);
        button.Click += (_, _) => vm.SelectPage(section.Id, page.Id);
        button.DoubleTapped += (_, _) =>
        {
            if (!vm.CanEdit) return;
            _pendingRename = (false, section.Id, page.Id);
            vm.RequestNavRebuild();
        };
        button.KeyDown += (_, e) =>
        {
            if (vm.CanEdit && e.Key == Key.F2)
            {
                _pendingRename = (false, section.Id, page.Id);
                vm.RequestNavRebuild();
                e.Handled = true;
            }
        };
        if (vm.CanEdit)
        {
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
            AddMenuItem(menu, "Add subpage", async () => await vm.AddSubpageAsync(section.Id, page.Id));
            if (page.ParentPageId is not null)
                AddMenuItem(menu, "Move to section root", async () => await vm.SetPageParentAsync(section.Id, page.Id, null));
            var pageIndex = section.Pages.IndexOf(page);
            if (pageIndex > 0)
                AddMenuItem(menu, "Nest under previous page", async () => await vm.SetPageParentAsync(section.Id, page.Id, section.Pages[pageIndex - 1].Id));
            button.ContextMenu = menu;
        }
        row.Children.Add(button);
        var pageRow = new Border
        {
            Child = row,
            Margin = new Thickness((depth + 1) * 16, 1, 0, 1),
            CornerRadius = rowRadius,
            Background = selected ? BoardsTheme.CardBrush : Brushes.Transparent,
        };
        CuiPropertyPresentation.Set(pageRow, "Background",
            selected ? BoardsTheme.CardBrush : Brushes.Transparent,
            CuiPropertyLevel.Instance, "Boards page row surface");
        CuiPropertyPresentation.Set(pageRow, "CornerRadius", rowRadius,
            CuiPropertyLevel.Instance, "Boards page row shape");
        return pageRow;
    }

    private static void SetNavigationButtonPresentation(Button button, string source, CornerRadius rowRadius)
    {
        CuiPropertyPresentation.Set(button, "Background", Brushes.Transparent,
            CuiPropertyLevel.Instance, source + " surface");
        CuiPropertyPresentation.Set(button, "BorderWidth", new Thickness(0),
            CuiPropertyLevel.Instance, source + " border");
        // The canonical button template binds its hover frame to this property.
        // Share the owning row's geometry rather than painting square interaction states.
        CuiPropertyPresentation.Set(button, "CornerRadius", rowRadius,
            CuiPropertyLevel.Instance, source + " shape");
        CuiPropertyPresentation.Set(button, "HorizontalAlignment", HorizontalAlignment.Stretch,
            CuiPropertyLevel.Instance, source + " width");
        CuiPropertyPresentation.Set(button, "HorizontalContentAlignment", HorizontalAlignment.Left,
            CuiPropertyLevel.Instance, source + " text alignment");
        CuiPropertyPresentation.Set(button, "VerticalContentAlignment", VerticalAlignment.Center,
            CuiPropertyLevel.Instance, source + " vertical alignment");
        CuiPropertyPresentation.Set(button, "Padding", new Thickness(4, 0),
            CuiPropertyLevel.Instance, source + " padding");
    }

    private static void TakePendingRename(StackPanel host, BoardsViewModel vm)
    {
        if (!vm.CanEdit)
        {
            _pendingRename = null;
            return;
        }
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
        async Task CommitAsync(bool save)
        {
            if (done)
                return;
            done = true;
            RenameActive = false;
            if (save)
            {
                try
                {
                    if (isSection)
                        await vm.RenameSectionAsync(sectionId, box.Text ?? string.Empty);
                    else if (pageId is not null)
                        await vm.RenamePageAsync(sectionId, pageId, box.Text ?? string.Empty);
                }
                catch (Exception error)
                {
                    vm.Set("StatusText", "Rename failed: " + error.Message.Split('\n', '\r')[0]);
                    vm.RequestNavRebuild();
                }
            }
            else
            {
                vm.RequestNavRebuild();
            }
        }
        box.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await CommitAsync(save: true);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                await CommitAsync(save: false);
            }
        };
        box.LostFocus += async (_, _) => await CommitAsync(save: true);
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

