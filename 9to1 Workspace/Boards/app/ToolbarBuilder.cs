// Mounts the declarative adaptive toolbar and retains the existing block insertion
// popup, destination preview and keyboard routing shared with block gutter controls.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using CakeOS.Cui.Runtime;
using System.Runtime.CompilerServices;
using Avalonia.VisualTree;

namespace CakeOS.Apps.Boards.App;

public static class ToolbarBuilder
{
    private static readonly ConditionalWeakTable<Control, InsertMenuContext> InsertMenus = new();
    internal static InsertMenuContext? InsertMenuFor(Control control) =>
        InsertMenus.TryGetValue(control, out var context) ? context : null;

    public static void Rebuild(StackPanel host, BoardsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(host);
        Rebuild((Panel)host, vm);
    }

    public static void Rebuild(WrapPanel host, BoardsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(host);
        Rebuild((Panel)host, vm);
    }

    public static void Rebuild(Panel host, BoardsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(vm);
        // The adaptive context already observes selection/formatting changes.
        // Replacing it during an unrelated block render closes active menus and
        // discards typed font drafts. Insert menus capture a page/document target,
        // so only retain the root while that complete target still matches.
        if (host.Children.Count == 1 && AdaptiveToolbar.TryReuse(host.Children[0], vm)) return;
        foreach (var child in host.Children.ToArray()) AdaptiveToolbar.Dispose(child);
        host.Children.Clear();
        host.Children.Add(AdaptiveToolbar.Build(vm));
    }

    public static Control CreateInsertButton(
        BoardsViewModel vm, string? tooltip = null, string? insertAfterBlockId = null, string? previewBlockId = null)
    {
        var button = BoardsIcons.IconButton(BoardsIcons.Add, "Insert", tooltip ?? "Insert a block (grouped menu with search)");
        SetQuietToolbarControl(button, selected: false, "Boards toolbar insert action");
        AttachInsertMenu(button, vm, insertAfterBlockId, previewBlockId);
        return button;
    }

    internal static void AttachInsertMenu(Button button, BoardsViewModel vm,
        string? insertAfterBlockId = null, string? previewBlockId = null)
    {
        // Overlay hosting with the framework light dismiss replaced by Boards-owned outside-press closing.
        // Measured on this platform: with light dismiss on, presses aimed at a drawn menu row never reached
        // the row and the insertion route could not be completed at all (see BoardsPopupHosting).
        var popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom };
        BoardsPopupHosting.ConfigureOverlayPopup(popup);
        // A manually owned Popup needs the trigger's logical theme ancestry.
        button.AttachedToVisualTree += (_, _) => ((ISetLogicalParent)popup).SetParent(button);
        ((ISetLogicalParent)popup).SetParent(button);
        CuiPopupPresentation.RequestTransparency(popup);
        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), MinWidth = 300, MaxWidth = 420,
            Height = 384, MaxHeight = 384 };
        var search = new TextBox { PlaceholderText = "Search blocks and styles…", Height = 36, MinHeight = 36,
            Margin = new Thickness(0, 0, 0, 8) };
        CuiPropertyPresentation.Set(search, "Height", 36d, CuiPropertyLevel.Instance, "Boards insert search geometry");
        CuiPropertyPresentation.Set(search, "MinHeight", 36d, CuiPropertyLevel.Instance, "Boards insert search geometry");
        CuiPropertyPresentation.Set(search, "MaxHeight", 36d, CuiPropertyLevel.Instance, "Boards insert search geometry");
        var context = new InsertMenuContext(vm, popup, search)
            { AnchorBlockId = insertAfterBlockId, RowBlockId = previewBlockId ?? insertAfterBlockId };
        button.Tag = context;
        InsertMenus.Add(button, context);
        popup.Closed += (_, _) => context.Close();
        button.DetachedFromVisualTree += (_, _) => { context.Dispose(); ((ISetLogicalParent)popup).SetParent(null); };
        panel.Tag = button.Tag;
        Avalonia.Automation.AutomationProperties.SetName(search, "Search insert menu");
        var list = new StackPanel { Orientation = Orientation.Vertical, Tag = button.Tag };
        panel.Children.Add(search);
        var scroll = new ScrollViewer { Content = list,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible };
        Grid.SetRow(scroll, 1);
        panel.Children.Add(scroll);
        panel.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            if (args.Handled || !popup.IsOpen || args.KeyModifiers != KeyModifiers.None) return;
            if (args.Key == Key.Escape) { context.Close(); button.Focus(); args.Handled = true; return; }
            var choices = list.GetVisualDescendants().OfType<Control>()
                .Where(item => item is Button or MenuItem && item.IsEffectivelyEnabled &&
                    item.IsEffectivelyVisible && item.IsHitTestVisible && item.Focusable).ToArray();
            if (choices.Length == 0) return;
            var current = Array.FindIndex(choices, item => item.IsKeyboardFocusWithin);
            if (args.Key == Key.Enter && (current >= 0 || search.IsKeyboardFocusWithin))
            {
                var choice = choices[current < 0 ? 0 : current];
                // The row that is about to act must be the row that looks chosen, otherwise Enter from the
                // search field applies an item the user cannot see.
                if (current < 0) choice.Focus();
                choice.RaiseEvent(new RoutedEventArgs(choice is MenuItem ? MenuItem.ClickEvent : Button.ClickEvent));
                args.Handled = true; return;
            }
            // Home and End belong to the search field while the caret is in it: they are text-editing
            // keys there, not menu navigation.
            if ((args.Key == Key.Home || args.Key == Key.End) && search.IsKeyboardFocusWithin) return;
            var next = args.Key switch { Key.Down => Math.Min(current + 1, choices.Length - 1),
                Key.Up => current < 0 ? choices.Length - 1 : Math.Max(0, current - 1),
                Key.Home => 0, Key.End => choices.Length - 1, _ => -1 };
            if (next < 0) return;
            choices[next].Focus(); choices[next].BringIntoView(); args.Handled = true;
        }, RoutingStrategies.Tunnel);
        void Fill(string filter)
        {
            context.RetireDestination();
            list.Children.Clear();
            if (context.AnchorBlockId is not null && context.Owner is null) return;
            if (context.AnchorBlockId is not null && context.Placement is null)
            {
                list.Children.Add(context.CreateDestination());
                return;
            }
            if (context.AnchorBlockId is not null && context.Placement is not null)
            {
                list.Children.Add(DestinationHeader(context.Placement, () =>
                {
                    context.Placement = null;
                    Fill(search.Text ?? string.Empty);
                    search.Focus();
                }));
            }
            if (vm.DatabaseBlocks.IsAvailable)
            {
                AddGroup(list, "Database", [("New database", "database"), ("Tab View", "database-tabs"),
                    ("Timeline View", "database-timeline")], vm, popup, filter, context);
                if (vm.HasPendingDatabaseLink)
                    AddGroup(list, "Recovery", [("Link created database", "database-retry")], vm, popup, filter, context);
            }
            if (vm.CanAddTabber) AddGroup(list, "Containers", [("Tabber", "tabber")], vm, popup, filter, context);
            AddGroup(list, "Text", [
                ("Paragraph", "paragraph"), ("Title", "style:title"), ("Header", "style:heading-1"),
                ("Quote", "style:quote"), ("Code", "style:code"),
            ], vm, popup, filter, context);
            var customs = vm.StyleList.Where(s => !s.IsBuiltIn).ToList();
            if (customs.Count > 0)
                AddGroup(list, "Custom styles", customs.Select(s => (s.Name, "style:" + s.Id)).ToList(), vm, popup, filter, context);
            AddGroup(list, "Content", [
                ("Checklist", "checklist"), ("Bulleted list", "bulleted"), ("Numbered list", "numbered"),
                ("Table", "table"), ("Graph", "graph"), ("Image…", "image"), ("Attachment…", "attachment"),
            ], vm, popup, filter, context);
            AddGroup(list, "Other", [("Freeform box", "freeform"), ("Divider", "divider")], vm, popup, filter, context);
        }
        context.Refill = () => Fill(search.Text ?? string.Empty);
        search.TextChanged += (_, _) => Fill(search.Text ?? string.Empty);
        // A closed menu must not keep a chosen-looking row. Closing the popup does not necessarily
        // raise LostFocus on the row inside it, so the state is reconciled here as well: measured on
        // 1 November, Escape left the row that had been arrow-navigated painted as if it were still
        // selected.
        popup.Closed += (_, _) =>
        {
            foreach (var row in list.GetVisualDescendants().OfType<Button>())
            {
                SetQuietToolbarControl(row, selected: false, "Boards insertion menu item state");
                CuiPropertyPresentation.SetSelected(row, false);
            }
        };
        Fill(string.Empty);
        popup.Child = context.CreateSurface(panel);
        button.Click += (_, _) =>
        {
            OpenInsertMenu(button);
        };
    }

    public static bool OpenInsertMenu(Control button)
    {
        ArgumentNullException.ThrowIfNull(button);
        return InsertMenuFor(button)?.Open() == true;
    }

    public static bool HasInsertMenu(Control button) => InsertMenuFor(button) is not null;

    private static void AddGroup(
        StackPanel list, string heading, IReadOnlyList<(string Label, string Tag)> entries,
        BoardsViewModel vm, Popup popup, string filter, InsertMenuContext context)
    {
        var matches = string.IsNullOrWhiteSpace(filter)
            ? entries
            : entries.Where(e => e.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0)
            return;
        list.Children.Add(new TextBlock
        {
            Text = heading.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = BoardsTheme.SecondaryTextBrush,
            Margin = new Thickness(0, 6, 0, 2),
        });
        foreach (var (label, tag) in matches)
        {
            var item = new Button
            {
                Content = label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 6),
            };
            // The shared CUI button alignment resource is centred and is written at Theme level, so the
            // assignment above loses to it. Measured 1 November on the running app, every insert row sat
            // centred under its left-aligned group heading while the app's own destination menu rows sat
            // left: the same list, two alignments. This row is a menu row, so it is pinned left through
            // CUI's own property precedence instead of leaving the centred default in place.
            CuiPropertyPresentation.Set(item, "HorizontalContentAlignment", HorizontalAlignment.Left,
                CuiPropertyLevel.Instance, "Boards insertion menu row alignment");
            SetQuietToolbarControl(item, selected: false, "Boards insertion menu item");
            // Keyboard and pointer position must be visible. Measured 1 November on the running app: five
            // Down presses moved focus out of the search field and an item did activate, yet no row ever
            // showed a state, so a keyboard-only user could not see where they were. The cue is the same
            // selection treatment the toolbar already uses for a selected control, plus CUI's own
            // selection state, reconciled whenever the row gains or loses hover or keyboard focus.
            void ReconcileRowState()
            {
                var chosen = item.IsKeyboardFocusWithin || item.IsPointerOver;
                SetQuietToolbarControl(item, selected: chosen, "Boards insertion menu item state");
                CuiPropertyPresentation.SetSelected(item, chosen);
            }
            item.PointerEntered += (_, _) => ReconcileRowState();
            item.PointerExited += (_, _) => ReconcileRowState();
            item.GotFocus += (_, _) => ReconcileRowState();
            item.LostFocus += (_, _) => ReconcileRowState();
            CuiPropertyPresentation.Attach(item);
            Avalonia.Automation.AutomationProperties.SetName(item, "Insert " + label);
            if (context.AnchorBlockId is not null &&
                vm.RelativeInsertionUnavailableReason(tag, context.AnchorBlockId, context.Placement) is { } unavailable)
            {
                item.IsEnabled = false;
                ToolTip.SetTip(item, unavailable);
                Avalonia.Automation.AutomationProperties.SetHelpText(item, unavailable);
            }
            var opening = context.Opening;
            var contentVersion = context.ContentVersion;
            var owner = context.Owner;
            var placement = context.Placement;
            item.Click += (_, _) =>
            {
                context.Commit(tag, opening, contentVersion, owner, placement);
            };
            list.Children.Add(item);
        }
    }


    private static Control DestinationHeader(string placement, Action changeBack)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        row.Children.Add(new TextBlock
        {
            Text = placement switch
            {
                "above" => "ABOVE THIS BLOCK",
                "below" => "BELOW THIS BLOCK",
                _ => "REPLACE THIS BLOCK",
            },
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = BoardsTheme.AccentBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var back = new Button
        {
            Content = "Change",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 2),
            FontSize = 11,
        };
        SetQuietToolbarControl(back, selected: false, "Boards insert destination change");
        CuiPropertyPresentation.Attach(back);
        Avalonia.Automation.AutomationProperties.SetName(back, "Change the insert destination");
        back.Click += (_, _) => changeBack();
        row.Children.Add(back);
        return row;
    }

    private static void SetQuietToolbarControl(Control control, bool selected, string source)
    {
        CuiPropertyPresentation.Set(control, "Foreground", BoardsTheme.TextBrush,
            CuiPropertyLevel.Instance, source + " text");
        CuiPropertyPresentation.Set(control, "Background", selected ? BoardsTheme.CardBrush : Brushes.Transparent,
            CuiPropertyLevel.Instance, source + " surface");
        CuiPropertyPresentation.Set(control, "BorderWidth", new Thickness(0),
            CuiPropertyLevel.Instance, source + " border");
        CuiPropertyPresentation.Set(control, "CornerRadius", new CornerRadius(8),
            CuiPropertyLevel.Instance, source + " shape");
    }
}

