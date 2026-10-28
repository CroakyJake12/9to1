// BlockRenderer builds per-block editors into BlocksHost from BoardsViewModel state.
// Control names encode ids (t_{blockId}, done_/chk_ checklist names,
// cell_{blockId}_{r}_{c}, gx_/gv_/gc_/gvp_/iw_/ialt_/dvth_/dvcl_) so the
// ViewModel routes edits without hardcoding. Contract block/item ids never
// contain '_', so names split on the LAST '_' (see BoardsViewModel).

using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;

namespace CakeOS.Apps.Boards.App;

public static class BlockRenderer
{
    private static readonly ConditionalWeakTable<Panel, AbsoluteHostPlacementState> AbsolutePlacements = new();
    private const double GraphWidth = 420;
    private const double GraphHeight = 260;
    private const double BlockGutterTargetSize = 32;
    private const double BlockGutterLeadingInset = 76;
    private const double BlockContentTopInset = 2;
    private const double ChecklistCheckboxFirstLineOffset = 7;
    private const double ChecklistCheckboxTargetSize = 24;
    private const double ChecklistCheckboxTextGap = 6;
    // The CUI checkbox template reserves its own label gutter even when the
    // checklist supplies no CheckBox.Content. Reserve that space explicitly
    // so the outer label column begins after the 24px target + 6px visual gap.
    private const double ChecklistCheckboxColumnWidth = ChecklistCheckboxTargetSize + ChecklistCheckboxTextGap;
    // Quiet-but-visible block gutter: the six-dot grip and insert buttons stay
    // discoverable at rest, which is what makes them reachable at all; full
    // opacity returns when the block is selected or hovered.
    private const double QuietBlockGutterOpacity = 0.56;

    public static async Task RebuildAsync(StackPanel host, BoardsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(vm);
        var canvas = await vm.GetCanvasObjectsAsync().ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
            Rebuild(host, vm, canvas, FindAbsoluteBlocksHost(host)));
    }

    public static void Rebuild(StackPanel host, BoardsViewModel vm, IReadOnlyList<CanvasBoxView> canvasBoxes) =>
        Rebuild(host, vm, canvasBoxes, FindAbsoluteBlocksHost(host));

    public static void Rebuild(StackPanel host, BoardsViewModel vm,
        IReadOnlyList<CanvasBoxView> canvasBoxes, Panel? absoluteBlocksHost)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(vm);
        if (absoluteBlocksHost is not null) AbsolutePlacements.GetValue(absoluteBlocksHost, panel => new(panel)).Reset();
        host.Children.Clear();
        absoluteBlocksHost?.Children.Clear();

        var blocks = vm.PageBlocks;
        if ((blocks.Any(block => block.Layout == HavenRichBlockLayouts.Absolute) || canvasBoxes.Count > 0) && absoluteBlocksHost is null)
            throw new InvalidOperationException("The authored AbsoluteBlocksHost is required to render saved free-positioned blocks.");
        if (blocks.Count == 0)
        {
            host.Children.Add(BuildEmptyPageHint(vm));
        }
        var index = 0;
        while (index < blocks.Count)
        {
            var block = blocks[index];
            if (block.Layout == HavenRichBlockLayouts.Absolute)
            {
                if (IsListKind(block.Kind))
                {
                    var parentId = ParentKey(block.Id);
                    var listRun = new List<RichBoardBlock>();
                    var start = index;
                    while (index < blocks.Count && IsListKind(blocks[index].Kind) &&
                        blocks[index].Layout == HavenRichBlockLayouts.Absolute &&
                        ParentKey(blocks[index].Id) == parentId)
                        listRun.Add(blocks[index++]);
                    RenderAbsoluteList(absoluteBlocksHost!, vm, listRun, start, host);
                }
                else
                {
                    RenderAbsoluteBlock(absoluteBlocksHost!, vm, block, index, host);
                    index++;
                }
                continue;
            }
            if (IsListKind(block.Kind))
            {
                var run = new List<RichBoardBlock>();
                var runStart = index;
                while (index < blocks.Count && IsListKind(blocks[index].Kind) &&
                    blocks[index].Layout != HavenRichBlockLayouts.Absolute)
                    run.Add(blocks[index++]);
                // Locked vertical mode keeps item rows independently reorderable.
                // Free mode treats the canonical list parent as one spatial block,
                // so one handle moves the complete list and its add-item affordance.
                if (vm.IsFreeLayout && absoluteBlocksHost is not null)
                {
                    var list = new StackPanel { Orientation = Orientation.Vertical };
                    for (var offset = 0; offset < run.Count; offset++)
                        list.Children.Add(BuildChecklistRow(vm, run[offset], offset + 1));
                    list.Children.Add(BuildChecklistAddButton(vm, run[0].Id));
                    var parentId = ParentKey(run[0].Id);
                    host.Children.Add(WithSelection(vm, run[0].Id, list, parentId, runStart, host,
                        absoluteBlocksHost, placementBlockId: parentId, placementBlock: run[0]));
                }
                else
                {
                    for (var offset = 0; offset < run.Count; offset++)
                        host.Children.Add(WithSelection(vm, run[offset].Id, BuildChecklistRow(vm, run[offset], offset + 1),
                            run[offset].Id, runStart + offset, host, absoluteBlocksHost,
                            placementBlockId: ParentKey(run[offset].Id), placementBlock: run[offset]));
                    host.Children.Add(BuildChecklistAddButton(vm, run[0].Id));
                }
                continue;
            }
            host.Children.Add(WithSelection(vm, block.Id, BuildBlock(vm, block), block.Id, index, host,
                absoluteBlocksHost, placementBlockId: block.Id, placementBlock: block));
            index++;
        }

        if (absoluteBlocksHost is not null)
        {
            foreach (var box in canvasBoxes) RenderCanvasObject(absoluteBlocksHost, vm, box);
            RenderAbsoluteBlocksHostExtent(absoluteBlocksHost, blocks);
            if (canvasBoxes.Count > 0)
            {
                absoluteBlocksHost.Height = Math.Max(double.IsNaN(absoluteBlocksHost.Height) ? 0 : absoluteBlocksHost.Height,
                    canvasBoxes.Max(box => box.Y + box.Height + 24));
                absoluteBlocksHost.MinWidth = canvasBoxes.Max(box => box.X + box.Width + 24);
            }
            else absoluteBlocksHost.MinWidth = 0;
        }
        // UX27-TEST-032: a Native page is one seamless editing surface. The old automatically
        // appended "Freeform canvas" section, its empty-state text and its "+ Box" region made
        // every page look like a second editor was bolted underneath it. Absolute content is
        // already rendered through the authored AbsoluteBlocksHost, and pen ink through the
        // page InkOverlayLayer. Previously saved canvas objects join that same spatial layer,
        // without creating a second scrollbox or injecting empty-state controls.
        vm.ApplyAuthoringState(host);
    }

    private static void RenderCanvasObject(Panel layer, BoardsViewModel vm, CanvasBoxView box)
    {
        var pageId = vm.SelectedPageId;
        var width = Math.Clamp(box.Width, 24, 5000);
        var height = Math.Clamp(box.Height, 24, 5000);
        var border = new Border
        {
            Name = "canvas_" + box.Id, Width = width, Height = height,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Background = BoardsTheme.CardBrush, BorderBrush = BoardsTheme.BorderBrush,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10),
            Focusable = vm.IsEditMode && !vm.IsCanvasLocked,
            Child = new TextBlock { Text = string.IsNullOrEmpty(box.Text) ? box.Kind : box.Text,
                FontSize = 13, Foreground = BoardsTheme.TextBrush, TextWrapping = TextWrapping.Wrap },
        };
        AutomationProperties.SetName(border, "Canvas object " + (string.IsNullOrEmpty(box.Text) ? box.Kind : box.Text));
        layer.Children.Add(border);
        var placement = AbsolutePlacements.GetValue(layer, panel => new(panel));
        void Place(double x, double y) => placement.Add(border, border, x, y, width, height);
        Place(box.X, box.Y);
        Point? pressed = null;
        IPointer? pointer = null;
        var position = new Point(box.X, box.Y);
        bool CanMove() => vm.IsEditMode && !vm.IsCanvasLocked && vm.SelectedPageId == pageId;
        border.PointerPressed += (_, e) =>
        {
            if (!CanMove() || !e.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;
            pressed = e.GetPosition(layer);
            position = new Point(box.X, box.Y);
            border.Focus();
            pointer = e.Pointer;
            e.Pointer.Capture(border);
            e.Handled = true;
        };
        border.PointerMoved += (_, e) =>
        {
            if (pressed is not { } start || !CanMove()) return;
            var current = e.GetPosition(layer);
            position = new Point(Math.Max(0, box.X + current.X - start.X), Math.Max(0, box.Y + current.Y - start.Y));
            Place(position.X, position.Y);
            e.Handled = true;
        };
        border.PointerReleased += async (_, e) =>
        {
            if (pressed is null) return;
            pressed = null;
            pointer = null;
            e.Pointer.Capture(null);
            if (CanMove()) await vm.MoveCanvasBoxAsync(box.Id, position.X, position.Y);
            else Place(box.X, box.Y);
            e.Handled = true;
        };
        border.PointerCaptureLost += (_, _) =>
        {
            if (pressed is null) return;
            pressed = null;
            pointer = null;
            Place(box.X, box.Y);
        };
        border.KeyDown += async (_, e) =>
        {
            if (!CanMove()) return;
            if (e.Key == Key.Escape && pressed is not null)
            {
                pressed = null;
                pointer?.Capture(null);
                pointer = null;
                Place(box.X, box.Y); e.Handled = true; return;
            }
            var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : 8;
            var (dx, dy) = e.Key switch { Key.Left => (-step, 0), Key.Right => (step, 0),
                Key.Up => (0, -step), Key.Down => (0, step), _ => (0, 0) };
            if (dx == 0 && dy == 0) return;
            e.Handled = true;
            await vm.MoveCanvasBoxAsync(box.Id, box.X + dx, box.Y + dy);
        };
    }

    private static Panel? FindAbsoluteBlocksHost(StackPanel blocksHost)
    {
        var root = blocksHost.GetVisualAncestors().OfType<Control>().LastOrDefault() ?? blocksHost;
        return BoardsCuiControlFinder.FindByAutomationId<Panel>(root, "AbsoluteBlocksHost");
    }

    private static void RenderAbsoluteBlocksHostExtent(Panel absoluteHost, IReadOnlyList<RichBoardBlock> blocks)
    {
        var persistedExtent = blocks.Where(block => block.Layout == HavenRichBlockLayouts.Absolute)
            .Select(block => block.Y + block.Height + 24)
            .DefaultIfEmpty(0)
            .Max();
        var renderedExtent = AbsolutePlacements.TryGetValue(absoluteHost, out var state) && state.BottomExtent > 0
            ? state.BottomExtent + 24
            : 0;
        var extent = Math.Max(persistedExtent, renderedExtent);
        absoluteHost.Height = extent > 0 ? extent : double.NaN;
    }

    private static void RenderAbsoluteBlock(
        Panel absoluteHost, BoardsViewModel vm, RichBoardBlock block, int blockIndex, StackPanel flowHost)
    {
        var control = WithSelection(vm, block.Id, BuildBlock(vm, block), block.Id, blockIndex,
            flowHost, absoluteHost, block.Id, block, absolutePositioned: true);
        absoluteHost.Children.Add(control);
        PlaceAbsoluteBlock(absoluteHost, control, block, block.Width, block.Height);
    }

    private static void RenderAbsoluteList(
        Panel absoluteHost, BoardsViewModel vm, IReadOnlyList<RichBoardBlock> items, int blockIndex, StackPanel flowHost)
    {
        if (items.Count == 0) return;
        var list = new StackPanel { Orientation = Orientation.Vertical, Width = items[0].Width };
        for (var offset = 0; offset < items.Count; offset++)
            list.Children.Add(BuildChecklistRow(vm, items[offset], offset + 1));
        var parentId = ParentKey(items[0].Id);
        list.Children.Add(BuildChecklistAddButton(vm, parentId));
        var control = WithSelection(vm, items[0].Id, list, parentId, blockIndex,
            flowHost, absoluteHost, parentId, items[0], absolutePositioned: true);
        list.Measure(new Size(items[0].Width, double.PositiveInfinity));
        absoluteHost.Children.Add(control);
        PlaceAbsoluteBlock(absoluteHost, control, items[0], items[0].Width,
            Math.Max(items[0].Height, list.DesiredSize.Height));
    }

    private static void PlaceAbsoluteBlock(
        Panel absoluteHost, Control control, RichBoardBlock block, double contentWidth, double contentHeight)
    {
        var width = contentWidth + BlockGutterLeadingInset;
        var height = contentHeight + BlockContentTopInset * 2;
        control.Width = width;
        control.MinHeight = height;
        control.Measure(new Size(width, double.PositiveInfinity));
        height = Math.Max(height, control.DesiredSize.Height);
        control.Height = height;
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Top;
        control.ZIndex = block.ZOrder;

        var left = block.X - BlockGutterLeadingInset;
        var top = block.Y - BlockContentTopInset;
        var coordinateTarget = control is Border { Child: Control child } ? child : control;
        AbsolutePlacements.GetValue(absoluteHost, panel => new(panel)).Add(control, coordinateTarget,
            left, top, width, height);
    }

    private sealed class AbsoluteHostPlacementState
    {
        private sealed record Placement(Control Cell, Control CoordinateTarget,
            double Left, double Top, double Width, double Height);

        private readonly Panel _host;
        private readonly List<Placement> _placements = [];
        private Size _lastSize;
        private bool _hasSize;
        public double BottomExtent => _placements.Select(placement => placement.Top + placement.Height).DefaultIfEmpty(0).Max();

        public AbsoluteHostPlacementState(Panel host)
        {
            _host = host;
            _host.SizeChanged += OnSizeChanged;
        }

        public void Reset()
        {
            foreach (var placement in _placements)
            {
                if (_host is Canvas)
                {
                    Canvas.SetLeft(placement.Cell, double.NaN);
                    Canvas.SetTop(placement.Cell, double.NaN);
                }
                else
                {
                    CuiLayoutProperties.Detach(placement.CoordinateTarget);
                }
            }
            _placements.Clear();
            _hasSize = false;
        }

        public void Add(Control cell, Control coordinateTarget, double left, double top, double width, double height)
        {
            if (_host is Canvas)
            {
                Canvas.SetLeft(cell, left);
                Canvas.SetTop(cell, top);
                return;
            }
            var placement = new Placement(cell, coordinateTarget, left, top, width, height);
            var index = _placements.FindIndex(value => ReferenceEquals(value.Cell, cell));
            if (index < 0) _placements.Add(placement);
            else _placements[index] = placement;
            Apply(placement);
        }

        private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (_hasSize && _lastSize == e.NewSize) return;
            _hasSize = true;
            _lastSize = e.NewSize;
            foreach (var placement in _placements) Apply(placement);
        }

        private void Apply(Placement placement)
        {
            var xPixel = placement.Left + placement.Width / 2 - _host.Bounds.Width / 2;
            var yPixel = _host.Bounds.Height / 2 - (placement.Top + placement.Height / 2);
            if (!CuiLayoutProperties.SetLiteral(placement.CoordinateTarget, "XPixel",
                    xPixel.ToString(CultureInfo.InvariantCulture), source: "Boards native block placement") ||
                !CuiLayoutProperties.SetLiteral(placement.CoordinateTarget, "YPixel",
                    yPixel.ToString(CultureInfo.InvariantCulture), source: "Boards native block placement"))
                throw new InvalidOperationException("The authored AbsoluteBlocksHost does not expose CUI pixel placement.");
        }
    }

    private static Control BuildEmptyPageHint(BoardsViewModel vm)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 24, 0, 24) };
        panel.Children.Add(new TextBlock
        {
            Text = "Start typing or press + to add content.",
            FontSize = 15,
            Foreground = BoardsTheme.SecondaryTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        });
        var add = new Button
        {
            Content = "+ Add your first block",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(16, 8),
        };
        ToolTip.SetTip(add, "Add a paragraph block");
        Avalonia.Automation.AutomationProperties.SetName(add, "Add your first block");
        add.Click += (_, _) => _ = vm.DispatchAsync("AddBlock", null);
        panel.Children.Add(add);
        return panel;
    }

    /// <summary>Subtle selection outline: transparent until selected, then an accent bar.</summary>
    private static Control WithSelection(
        BoardsViewModel vm, string selectId, Control inner, string insertAfterBlockId, int blockIndex,
        StackPanel host, Panel? absoluteBlocksHost = null,
        string? placementBlockId = null, RichBoardBlock? placementBlock = null,
        bool absolutePositioned = false)
    {
        var selected = string.Equals(vm.SelectedBlockId, selectId, StringComparison.Ordinal);
        // The 64px gutter occupies the page-card margin before the text column;
        // the two 32px targets remain outside document flow and the block text
        // aligns with the page title/content start.
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("32,32,*") };
        Button? insert = null;
        Control? gripControl = null;
        if (vm.IsEditMode)
        {
            insert = ToolbarBuilder.CreateInsertButton(
                vm, "Insert a block after this note", insertAfterBlockId) as Button
                ?? throw new InvalidOperationException("The insert affordance must remain a button.");
            ConfigureBlockGutterButton(insert, insertAction: true);
            insert.Margin = new Thickness(0);
            insert.Opacity = 0;

            var grip = BlockGripCui.Build(vm, selectId);
            gripControl = grip;
            grip.MinWidth = BlockGutterTargetSize;
            grip.MinHeight = BlockGutterTargetSize;
            grip.HorizontalAlignment = HorizontalAlignment.Left;
            grip.VerticalAlignment = VerticalAlignment.Top;
            CuiPropertyPresentation.Set(grip, "VerticalAlignment", VerticalAlignment.Top,
                CuiPropertyLevel.Instance, "Boards block gutter alignment");
            grip.Opacity = 0;
            if (grip.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Name == "BlockGripToggle") is { } toggle)
                ConfigureBlockGutterButton(toggle, insertAction: false);
            grip.Margin = new Thickness(0);
            Grid.SetColumn(grip, 0);
            row.Children.Add(grip);

            Grid.SetColumn(insert, 1);
            row.Children.Add(insert);
        }
        else
        {
            // Keep the content start aligned between modes without leaving
            // an edit command in the read-only surface.
            row.Children.Add(new Border
            {
                Width = BlockGutterTargetSize,
                Height = BlockGutterTargetSize,
                IsHitTestVisible = false,
                Focusable = false,
            });
            var gripSlot = new Border
            {
                Width = BlockGutterTargetSize,
                Height = BlockGutterTargetSize,
                IsHitTestVisible = false,
                Focusable = false,
            };
            Grid.SetColumn(gripSlot, 1);
            row.Children.Add(gripSlot);
        }
        Grid.SetColumn(inner, 2);
        row.Children.Add(inner);
        var border = new Border
        {
            Child = row,
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = selected ? BoardsTheme.AccentBrush : Brushes.Transparent,
            Padding = new Thickness(9, 2, 0, 2),
            Margin = absolutePositioned ? new Thickness(0) : new Thickness(-BlockGutterLeadingInset, 0, 0, 10),
            Background = Brushes.Transparent,
        };
        border.Tag = new BlockReorderRow(selectId, blockIndex);
        var hovering = false;
        var focused = false;
        void UpdateGutterEmphasis()
        {
            // Quiet-but-visible at rest; full opacity on selection or hover.
            var opacity = selected || hovering || focused ? 1 : QuietBlockGutterOpacity;
            if (insert is not null) insert.Opacity = opacity;
            if (gripControl is not null)
            {
                // CUI theme projection can outrank a native Avalonia opacity
                // assignment on the authored container. Publish the state at
                // instance priority so the whole nested grip follows too.
                CuiPropertyPresentation.Set(gripControl, "Opacity", opacity,
                    CuiPropertyLevel.Instance, "Boards quiet block gutter visibility");
                gripControl.Opacity = opacity;
            }
        }
        UpdateGutterEmphasis();
        border.PointerEntered += (_, _) => { hovering = true; UpdateGutterEmphasis(); };
        border.PointerExited += (_, _) => { hovering = false; UpdateGutterEmphasis(); };
        border.AddHandler(InputElement.GotFocusEvent, (_, _) => { focused = true; UpdateGutterEmphasis(); }, RoutingStrategies.Bubble);
        border.AddHandler(InputElement.LostFocusEvent, (_, _) => { focused = false; UpdateGutterEmphasis(); }, RoutingStrategies.Bubble);
        if (gripControl is not null)
        {
            if (placementBlock is not null && placementBlockId is not null && absoluteBlocksHost is not null &&
                vm.IsFreeLayout)
            {
                if (vm.IsEditMode)
                    BlockPlacementGesture.Attach(gripControl, border, inner, absoluteBlocksHost, vm,
                        vm.SelectedPageId, placementBlockId, placementBlock);
                else if (placementBlock.Layout == HavenRichBlockLayouts.Flow && vm.IsEditMode)
                    BlockReorderPointer.Attach(gripControl, host, vm, selectId, blockIndex);
            }
            else if (placementBlock?.Layout != HavenRichBlockLayouts.Absolute)
            {
                BlockReorderPointer.Attach(gripControl, host, vm, selectId, blockIndex);
            }
        }
        if (placementBlock?.Layout == HavenRichBlockLayouts.Absolute &&
            placementBlockId is not null && vm.IsEditMode)
        {
            var pageId = vm.SelectedPageId;
            var menu = new ContextMenu();
            var toFlow = new MenuItem { Header = "Move to Vertical Flow" };
            Avalonia.Automation.AutomationProperties.SetName(toFlow, "Move block to Vertical Flow");
            toFlow.Click += async (_, _) => await vm.MoveBlockToVerticalFlowAsync(pageId, placementBlockId);
            menu.Items.Add(toFlow);
            border.ContextMenu = menu;
        }
        return border;
    }

    private static void ConfigureBlockGutterButton(Button button, bool insertAction)
    {
        // CUI's theme projection writes at a higher native value priority than
        // these ordinary Avalonia assignments. Record the compact geometry as
        // an instance declaration so it wins over the 34px theme minimum and
        // the shared button padding.
        const string geometrySource = "Boards block gutter geometry";
        CuiPropertyPresentation.Set(button, "Width", BlockGutterTargetSize,
            CuiPropertyLevel.Instance, geometrySource);
        CuiPropertyPresentation.Set(button, "Height", BlockGutterTargetSize,
            CuiPropertyLevel.Instance, geometrySource);
        CuiPropertyPresentation.Set(button, "MinWidth", BlockGutterTargetSize,
            CuiPropertyLevel.Instance, geometrySource);
        CuiPropertyPresentation.Set(button, "MinHeight", BlockGutterTargetSize,
            CuiPropertyLevel.Instance, geometrySource);
        CuiPropertyPresentation.Set(button, "MaxWidth", BlockGutterTargetSize,
            CuiPropertyLevel.Instance, geometrySource);
        CuiPropertyPresentation.Set(button, "MaxHeight", BlockGutterTargetSize,
            CuiPropertyLevel.Instance, geometrySource);
        CuiPropertyPresentation.Set(button, "Padding", new Thickness(0),
            CuiPropertyLevel.Instance, geometrySource);
        CuiPropertyPresentation.Set(button, "BorderWidth", new Thickness(0),
            CuiPropertyLevel.Instance, geometrySource);
        CuiPropertyPresentation.Set(button, "VerticalAlignment", VerticalAlignment.Top,
            CuiPropertyLevel.Instance, geometrySource);
        button.VerticalAlignment = VerticalAlignment.Top;
        if (insertAction)
        {
            if (button.Content is Image addGlyph)
            {
                addGlyph.Width = 18;
                addGlyph.Height = 18;
            }
            return;
        }
        // The drag grip keeps its glyph and a fixed top target, the same
        // invariant the quiet-gutter tests assert.
        var glyph = BoardsIcons.Glyph(BoardsIcons.Grip, 14, "Drag block handle");
        CuiPropertyPresentation.Set(button, "Chromeless", true,
            CuiPropertyLevel.Instance, "Boards block drag grip interaction");
        CuiPropertyPresentation.Set(button, "Background", Brushes.Transparent,
            CuiPropertyLevel.Instance, "Boards block drag grip surface");
        CuiPropertyPresentation.Set(button, "CornerRadius", new CornerRadius(0),
            CuiPropertyLevel.Instance, "Boards block drag grip shape");
        CuiPropertyPresentation.Set(button, "Content", glyph,
            CuiPropertyLevel.Instance, "Boards block drag grip glyph");
        button.Background = Brushes.Transparent;
        button.Content = glyph;
        button.VerticalAlignment = VerticalAlignment.Top;
    }

    private static void ConfigureBlockGutterButton(Button button) =>
        ConfigureBlockGutterButton(button, insertAction: false);

    // ----- Block dispatch -----

    private static Control BuildBlock(BoardsViewModel vm, RichBoardBlock block) => block.Kind switch
    {
        "heading" or "paragraph" => BuildTextBlock(vm, block),
        "table" => BuildTable(vm, block),
        "divider" => BuildDivider(vm, block),
        "image" => BuildImage(vm, block),
        "graph" => BuildGraph(vm, block),
        _ => BuildTextBlock(vm, block),
    };

    private static bool IsListKind(string kind) =>
        kind is "checklist" or "bulleted" or "numbered";

    private static bool TryOpenInlineInsertMenu(TextBox editor)
    {
        foreach (var row in editor.GetVisualAncestors().OfType<Grid>())
        {
            var button = row.Children.OfType<Button>().FirstOrDefault(ToolbarBuilder.HasInsertMenu);
            if (button is not null && ToolbarBuilder.OpenInsertMenu(button)) return true;
        }
        return false;
    }

    private static StackPanel BlockShell(BoardsViewModel vm, RichBoardBlock block)
    {
        // No visible header: blocks read as document content. Management lives
        // in the block context menu (right-click) and the contextual strip.
        var shell = new StackPanel { Orientation = Orientation.Vertical };
        if (vm.IsEditMode)
        {
            var menu = new ContextMenu();
            var delete = new MenuItem { Header = "Delete block" };
            Avalonia.Automation.AutomationProperties.SetName(delete, "Delete block");
            var captured = block.Id;
            delete.Click += async (_, _) => await vm.DeleteBlockAsync(captured);
            menu.Items.Add(delete);
            shell.ContextMenu = menu;
        }
        if (!string.IsNullOrEmpty(block.AttachmentId))
            shell.Children.Add(BuildAttachmentRow(vm, block));
        return shell;
    }

    // ----- Text -----

    private static Control BuildTextBlock(BoardsViewModel vm, RichBoardBlock block)
    {
        var isHeading = block.Kind == "heading";
        var isCode = string.Equals(block.StyleId, "code", StringComparison.Ordinal);
        var style = vm.StyleList.FirstOrDefault(s => s.Id == block.StyleId);
        var shell = BlockShell(vm, block);
        var box = new TextBox
        {
            Name = "t_" + block.Id,
            Text = block.Text ?? string.Empty,
            FontSize = ResolveFontSize(block, style, isHeading ? 26 : 14),
            FontWeight = block.Bold || (style?.Bold == true) || isHeading ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = block.Italic || (style?.Italic == true) ? FontStyle.Italic : FontStyle.Normal,
            AcceptsReturn = true,
            IsReadOnly = !vm.IsEditMode,
            TextWrapping = TextWrapping.Wrap,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 6),
            Margin = new Thickness(Math.Max(0, block.IndentLevel) * 20, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        // The page title is authored by CUI and has a zero horizontal inset.
        // Keep native block text on that same visible text column even when a
        // CUI theme supplies a stronger default TextBox padding.
        CuiPropertyPresentation.Set(box, "Padding", new Thickness(0, 6),
            CuiPropertyLevel.Instance, "Boards document text column alignment");
        var family = !string.IsNullOrEmpty(block.FontFamily) ? block.FontFamily : style?.FontFamily;
        if (!string.IsNullOrEmpty(family))
            box.FontFamily = new FontFamily(family);
        box.TextAlignment = ParseAlignment(block.Alignment != "inherit" ? block.Alignment : style?.Alignment ?? "inherit");
        if (TryBrush(block.Background, out var background))
            box.Background = background;
        else if (TryBrush(style?.Background, out var styleBackground))
            box.Background = styleBackground;
        if (TryBrush(block.Foreground, out var foreground))
            box.Foreground = foreground;
        else if (TryBrush(style?.Foreground, out var styleForeground))
            box.Foreground = styleForeground;
        else
            box.Foreground = BoardsTheme.ContrastText(
                !string.IsNullOrEmpty(block.Background) ? block.Background : style?.Background);
        ThemeApplier.SetDocumentTextSurface(box, box.Background ?? Brushes.Transparent);
        ToolTip.SetTip(box, (isHeading ? "Heading" : "Paragraph") + " — select to format");
        AutomationProperties.SetName(box, (isHeading ? "Heading" : "Paragraph") + " editor");
        var captured = block.Id;
        box.TextChanged += (_, _) =>
        {
            if (vm.IsEditMode && string.Equals(box.Text, "/", StringComparison.Ordinal) &&
                box.CaretIndex == 1 && box.SelectionStart == box.SelectionEnd &&
                TryOpenInlineInsertMenu(box))
            {
                // The slash triggers the existing searchable insertion menu but
                // never becomes document content; clearing it raises this handler
                // with the empty value before search receives subsequent typing.
                box.Text = string.Empty;
                box.CaretIndex = 0;
                return;
            }
            vm.EditText("t_" + captured, box.Text ?? string.Empty);
        };
        box.GotFocus += (_, _) => vm.FocusBlock(captured);
        box.AddHandler(InputElement.KeyDownEvent, async (_, args) =>
        {
            if (!vm.IsEditMode) return;
            if (args.Key == Key.Enter)
            {
                // Code retains the text editor's newline/IME behavior. Exiting it
                // is an explicit command and leaves the original code intact.
                if (isCode)
                {
                    if (args.KeyModifiers.HasFlag(KeyModifiers.Control) &&
                        !args.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        args.Handled = true;
                        await vm.InsertKindAsync("paragraph", captured, initialText: string.Empty, focusEditor: true);
                    }
                    return;
                }
                // Owner decision 27 October: Enter ends the current block and starts the
                // next one; Shift+Enter adds a line break inside the same BlockID.
                if (args.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    return;
                }
                args.Handled = true;
                await vm.SplitTextBlockRangeAsync(captured, box.SelectionStart, box.SelectionEnd);
            }
            else if (args.Key == Key.Back && box.SelectionStart == box.SelectionEnd && box.CaretIndex == 0)
            {
                if (await vm.MergeTextBlockWithPreviousAsync(captured))
                    args.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        box.AttachedToVisualTree += (_, _) =>
        {
            if (!vm.TryConsumePendingTextFocus(vm.SelectedPageId, captured, out var caretIndex)) return;
            box.Focus();
            box.CaretIndex = Math.Clamp(caretIndex, 0, (box.Text ?? string.Empty).Length);
        };
        vm.BindTextSelection(box, captured);
        shell.Children.Add(box);
        return shell;
    }

    // Renderer projection only: the SAME persisted RichTextRun values shape the
    // existing input. No parallel document/editor or visual-only text overlay.
    internal static void UpdateTextPresentation(TextBox input, RichBoardBlock block, RichStyleView? style)
    {
        if (!BoardTextRanges.Matches(block) || !string.Equals(input.Text ?? string.Empty, block.Text, StringComparison.Ordinal))
        {
            CuiFormattedTextInput.SetPresentation(input, null);
            return;
        }
        static Color? Colour(string? value) => Color.TryParse(value, out var colour) ? colour : null;
        static BaselineAlignment Baseline(string value) => value switch
        {
            "subscript" => BaselineAlignment.Subscript, "superscript" => BaselineAlignment.Superscript,
            _ => BaselineAlignment.Baseline
        };
        var runs = new List<CuiFormattedTextRun>();
        if (block.Runs.Count > 0)
        {
            foreach (var run in block.Runs.Where(run => run.Text.Length > 0))
                runs.Add(new CuiFormattedTextRun(run.Text,
                    run.Bold ? FontWeight.Bold : FontWeight.Normal, run.Italic ? FontStyle.Italic : FontStyle.Normal,
                    run.FontFamily, run.FontSize, run.Underline, run.StrikeThrough,
                    Baseline(run.Baseline.ToString().ToLowerInvariant()), Colour(run.Foreground), Colour(run.Background)));
        }
        else if (block.Text.Length > 0)
        {
            // Empty run lists use the existing block/named-style presentation.
            // Persisted explicit run booleans override style booleans above;
            // empty/zero run font and colour values inherit the input defaults.
            runs.Add(new CuiFormattedTextRun(block.Text, input.FontWeight, input.FontStyle,
                Underline: block.Underline || style?.Underline == true,
                StrikeThrough: block.Strike || style?.Strike == true,
                Baseline: Baseline(block.Baseline != "normal" ? block.Baseline : style?.Baseline ?? "normal")));
        }
        var current = CuiFormattedTextInput.GetPresentation(input);
        if (current is not null && current.Text == block.Text && current.Runs.SequenceEqual(runs)) return;
        CuiFormattedTextInput.SetPresentation(input, new CuiFormattedText(block.Text, runs));
    }

    private static double ResolveFontSize(RichBoardBlock block, RichStyleView? style, double fallback)
    {
        if (block.FontSize > 0)
            return block.FontSize;
        if (style is not null && style.FontSize > 0)
            return style.FontSize;
        return fallback;
    }

    // ----- Checklist -----

    private static Control BuildChecklistAddButton(BoardsViewModel vm, string firstBlockId)
    {
        var add = new Button
        {
            Content = "+ Add item",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = BoardsTheme.SecondaryTextBrush,
            FontSize = 13,
            Padding = new Thickness(28, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        add.IsVisible = vm.IsEditMode;
        add.Opacity = 0;
        var hovering = false;
        var focused = false;
        void UpdateVisibility() => add.Opacity = hovering || focused ? 1 : 0;
        add.PointerEntered += (_, _) => { hovering = true; UpdateVisibility(); };
        add.PointerExited += (_, _) => { hovering = false; UpdateVisibility(); };
        add.GotFocus += (_, _) => { focused = true; UpdateVisibility(); };
        add.LostFocus += (_, _) => { focused = false; UpdateVisibility(); };
        ToolTip.SetTip(add, "Add a checklist item");
        AutomationProperties.SetName(add, "Add checklist item");
        add.Click += async (_, _) => await vm.AddChecklistItemAsync(ParentKey(firstBlockId));
        return add;
    }

    private static Control BuildChecklistRow(BoardsViewModel vm, RichBoardBlock item, int number = 0)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{ChecklistCheckboxColumnWidth},*,Auto"),
            Margin = new Thickness(Math.Max(0, item.Level) * 16, 3, 0, 3),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var suffix = NameSuffix(item.Id);
        var check = BuildListMarker(vm, item, number, suffix);
        var text = new TextBox
        {
            Name = "chk_" + suffix,
            Text = item.Text ?? string.Empty,
            IsReadOnly = !vm.IsEditMode,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 4),
            FontWeight = item.Bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = item.Italic ? FontStyle.Italic : FontStyle.Normal,
            Foreground = item.IsChecked ? BoardsTheme.SecondaryTextBrush : BoardsTheme.TextBrush,
        };
        CuiPropertyPresentation.Set(text, "Padding", new Thickness(0, 4),
            CuiPropertyLevel.Instance, "Boards checklist label geometry");
        ThemeApplier.SetDocumentTextSurface(text, Brushes.Transparent);
        ToolTip.SetTip(text, "Checklist item text (Enter adds next, empty Backspace removes)");
        AutomationProperties.SetName(text, "Checklist item text");
        var erase = new Button
        {
            Content = BoardsIcons.Glyph(BoardsIcons.Close, 11, "Remove checklist item"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = BoardsTheme.SecondaryTextBrush,
            Padding = new Thickness(6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
            IsVisible = vm.IsEditMode,
        };
        ToolTip.SetTip(erase, "Remove this item");
        AutomationProperties.SetName(erase, "Remove checklist item");
        erase.VerticalAlignment = VerticalAlignment.Top;
        erase.Margin = new Thickness(0, 2, 0, 0);
        var viewId = item.Id;
        if (check is CheckBox checkbox)
            checkbox.IsCheckedChanged += (_, _) => vm.EditCheck("done_" + suffix, checkbox.IsChecked == true);
        text.TextChanged += (_, _) => vm.EditText("chk_" + suffix, text.Text ?? string.Empty);
        text.GotFocus += (_, _) =>
        {
            vm.FocusBlock(viewId);
            erase.Opacity = 1;
        };
        text.LostFocus += (_, _) => erase.Opacity = 0;
        row.PointerEntered += (_, _) => erase.Opacity = 1;
        row.PointerExited += (_, _) =>
        {
            if (!text.IsFocused)
                erase.Opacity = 0;
        };
        text.AddHandler(InputElement.KeyDownEvent, async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                // UX27-BOARD-016/017: Enter splits the row at the caret (or adds the next
                // same-kind row at the end); Shift+Enter stays inside the row as a soft break.
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    var current = text.Text ?? string.Empty;
                    var (start, end) = HavenRichNotesOps.NormalizeTextRange(current, text.SelectionStart, text.SelectionEnd);
                    var soft = current.Remove(start, end - start).Insert(start, "\n");
                    text.Text = soft;
                    text.CaretIndex = start + 1;
                    vm.CommitListSoftBreak(viewId, soft);
                }
                else
                    await vm.SplitTextBlockRangeAsync(viewId, text.SelectionStart, text.SelectionEnd);
            }
            else if (e.Key == Key.Back && string.IsNullOrEmpty(text.Text))
            {
                e.Handled = true;
                await vm.ChecklistBackspaceAsync(viewId, text.Text ?? string.Empty);
            }
            else if (e.Key == Key.Tab)
            {
                e.Handled = true;
                vm.ChecklistIndent(viewId, e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
            }
        }, RoutingStrategies.Tunnel);
        text.AttachedToVisualTree += (_, _) =>
        {
            if (!vm.TryConsumePendingTextFocus(vm.SelectedPageId, viewId, out var caret)) return;
            text.Focus();
            text.CaretIndex = Math.Clamp(caret, 0, (text.Text ?? string.Empty).Length);
        };
        erase.Click += async (_, _) => await vm.DeleteBlockAsync(viewId);
        Grid.SetColumn(check, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(erase, 2);
        row.Children.Add(check);
        row.Children.Add(text);
        row.Children.Add(erase);
        return row;
    }

    /// <summary>The leading marker for a list row. A checklist keeps its interactive checkbox;
    /// bulleted and numbered rows show a bullet or the row position, so the three list kinds stay
    /// visually distinct instead of all rendering as checkboxes.</summary>
    private static Control BuildListMarker(BoardsViewModel vm, RichBoardBlock item, int number, string suffix)
    {
        const string checklistGeometrySource = "Boards checklist checkbox geometry";
        var markerMargin = new Thickness(0, ChecklistCheckboxFirstLineOffset, ChecklistCheckboxTextGap, 0);
        if (item.Kind is "bulleted" or "numbered")
        {
            var glyph = new TextBlock
            {
                Name = "marker_" + suffix,
                Text = item.Kind == "numbered" ? $"{Math.Max(1, number)}." : "•",
                FontSize = 14,
                FontWeight = item.Bold ? FontWeight.Bold : FontWeight.Normal,
                Foreground = BoardsTheme.SecondaryTextBrush,
                Width = ChecklistCheckboxTargetSize,
                MinWidth = ChecklistCheckboxTargetSize,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = markerMargin,
            };
            AutomationProperties.SetName(glyph, item.Kind == "numbered" ? "Numbered list item" : "Bulleted list item");
            return glyph;
        }
        var check = new CheckBox
        {
            Name = "done_" + suffix,
            IsChecked = item.IsChecked,
            Width = ChecklistCheckboxTargetSize,
            Height = ChecklistCheckboxTargetSize,
            MinWidth = ChecklistCheckboxTargetSize,
            MinHeight = ChecklistCheckboxTargetSize,
            MaxWidth = ChecklistCheckboxTargetSize,
            MaxHeight = ChecklistCheckboxTargetSize,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = markerMargin,
        };
        CuiPropertyPresentation.Set(check, "Width", ChecklistCheckboxTargetSize, CuiPropertyLevel.Instance, checklistGeometrySource);
        CuiPropertyPresentation.Set(check, "Height", ChecklistCheckboxTargetSize, CuiPropertyLevel.Instance, checklistGeometrySource);
        CuiPropertyPresentation.Set(check, "MinWidth", ChecklistCheckboxTargetSize, CuiPropertyLevel.Instance, checklistGeometrySource);
        CuiPropertyPresentation.Set(check, "MinHeight", ChecklistCheckboxTargetSize, CuiPropertyLevel.Instance, checklistGeometrySource);
        CuiPropertyPresentation.Set(check, "MaxWidth", ChecklistCheckboxTargetSize, CuiPropertyLevel.Instance, checklistGeometrySource);
        CuiPropertyPresentation.Set(check, "MaxHeight", ChecklistCheckboxTargetSize, CuiPropertyLevel.Instance, checklistGeometrySource);
        CuiPropertyPresentation.Set(check, "Margin", markerMargin,
            CuiPropertyLevel.Instance, checklistGeometrySource);
        AutomationProperties.SetName(check, "Checklist item done");
        return check;
    }

    // ----- Table -----

    private static Control BuildTable(BoardsViewModel vm, RichBoardBlock block)
    {
        var shell = BlockShell(vm, block);
        var rows = block.TableRows > 0 ? block.TableRows : DerivedMax(block, row: true) + 1;
        var cols = block.TableCols > 0 ? block.TableCols : DerivedMax(block, row: false) + 1;
        rows = Math.Clamp(rows, 1, 100);
        cols = Math.Clamp(cols, 1, 50);

        var tableCornerRadius = new CornerRadius(Math.Clamp(block.TableStyle.CornerRadius, 0, 16));
        var tableBorderWidth = Math.Clamp(block.TableStyle.BorderWidth > 0 ? block.TableStyle.BorderWidth : 1, 0.5, 8);
        var tableBorder = new Border
        {
            Name = "BoardTableFrame_" + block.Id,
            BorderBrush = TableBorderBrush(block),
            BorderThickness = new Thickness(tableBorderWidth),
            CornerRadius = tableCornerRadius,
            Margin = new Thickness(0, 4, 0, 4),
            Width = block.TableStyle.Width > 0 ? block.TableStyle.Width : double.NaN,
            HorizontalAlignment = block.TableStyle.Alignment switch
            {
                "left" => HorizontalAlignment.Left,
                "center" => HorizontalAlignment.Center,
                "right" => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Stretch,
            },
        };
        var hasTableBackground = TryBrush(block.TableStyle.Background, out var tableBackground);
        if (hasTableBackground)
            tableBorder.Background = tableBackground;
        else
            tableBorder.Background = BoardsTheme.CardBrush;

        var grid = new Grid();
        for (var r = 0; r < rows; r++)
        {
            var height = r < block.RowHeights.Count ? block.RowHeights[r] : 0;
            grid.RowDefinitions.Add(new RowDefinition(height > 0 ? new GridLength(height) : GridLength.Auto));
        }
        for (var c = 0; c < cols; c++)
        {
            var width = c < block.ColumnWidths.Count && block.ColumnWidths[c] > 0
                ? block.ColumnWidths[c]
                : double.NaN;
            grid.ColumnDefinitions.Add(new ColumnDefinition(
                double.IsNaN(width) ? GridLength.Star : new GridLength(width)));
        }

        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                var key = $"{r},{c}";
                var isHeaderCell = block.TableStyle.HasHeaderRow && r == 0 ||
                    block.TableStyle.HasHeaderColumn && c == 0;
                // Defaults follow the active CUI theme. Explicit cell and table
                // colours below still win when the document carries an override.
                var palette = BoardsTheme.SharedPalette;
                var headerBackground = new SolidColorBrush(palette.AccentSoft);
                block.CellSpans.TryGetValue(key, out var cellSpan);
                if (cellSpan?.CoveredByCellId is not null) continue;
                var rowSpan = Math.Clamp(cellSpan?.RowSpan ?? 1, 1, rows - r);
                var columnSpan = Math.Clamp(cellSpan?.ColumnSpan ?? 1, 1, cols - c);
                block.TableCells.TryGetValue(key, out var cellText);
                block.CellStyles.TryGetValue(key, out var cellStyle);
                var box = new TextBox
                {
                    Name = $"cell_{block.Id}_{r}_{c}",
                    Text = cellText ?? string.Empty,
                    FontSize = 14,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(8, 6),
                    FontWeight = cellStyle?.Bold == true || isHeaderCell ? FontWeight.Bold : FontWeight.Normal,
                    FontStyle = cellStyle?.Italic == true ? FontStyle.Italic : FontStyle.Normal,
                    MinHeight = 30,
                };
                string? resolvedBackgroundHex = null;
                if (cellStyle is not null && TryBrush(cellStyle.Background, out var cellBackground))
                {
                    box.Background = cellBackground;
                    resolvedBackgroundHex = cellStyle.Background;
                }
                else if (hasTableBackground)
                {
                    box.Background = tableBackground;
                    resolvedBackgroundHex = block.TableStyle.Background;
                }
                else if (isHeaderCell)
                {
                    box.Background = headerBackground;
                    resolvedBackgroundHex = headerBackground.Color.ToString();
                }
                else if (block.TableStyle.AlternatingRows && r % 2 == 1)
                {
                    box.Background = new SolidColorBrush(palette.Panel3);
                    resolvedBackgroundHex = palette.Panel3.ToString();
                }
                if (cellStyle is not null && TryBrush(cellStyle.Foreground, out var cellForeground))
                    box.Foreground = cellForeground;
                else
                    box.Foreground = BoardsTheme.ContrastText(resolvedBackgroundHex);
                ThemeApplier.SetDocumentTextSurface(box, box.Background ?? Brushes.Transparent);
                box.TextAlignment = ParseAlignment(cellStyle?.AlignH ?? "inherit");
                box.VerticalAlignment = cellStyle?.AlignV switch
                {
                    "top" => VerticalAlignment.Top,
                    "bottom" => VerticalAlignment.Bottom,
                    _ => VerticalAlignment.Center,
                };
                AutomationProperties.SetName(box, $"Table cell row {r + 1} column {c + 1}");
                var rr = r;
                var cc = c;
                var captured = block.Id;
                box.TextChanged += (_, _) => vm.EditText($"cell_{captured}_{rr}_{cc}", box.Text ?? string.Empty);
                box.GotFocus += (_, _) => vm.FocusCell(captured, rr, cc);
                var frame = new Border
                {
                    Child = box,
                    BorderBrush = BoardsTheme.BorderBrush,
                    BorderThickness = new Thickness(0, 0, c + columnSpan < cols ? 1 : 0, r + rowSpan < rows ? 1 : 0),
                    Background = box.Background,
                };
                box.Background = Brushes.Transparent;
                Grid.SetRow(frame, r);
                Grid.SetColumn(frame, c);
                Grid.SetRowSpan(frame, rowSpan);
                Grid.SetColumnSpan(frame, columnSpan);
                grid.Children.Add(frame);
            }
        // Clip cell fills to the rounded table interior without clipping the
        // outer stroke itself. Clipping the frame can shave the border at its
        // corners and at the maximum stroke width.
        tableBorder.Child = new Border
        {
            Name = "BoardTableClip_" + block.Id,
            CornerRadius = new CornerRadius(Math.Max(0, tableCornerRadius.TopLeft - tableBorderWidth)),
            ClipToBounds = true,
            Child = grid,
        };
        shell.Children.Add(tableBorder);
        return shell;
    }

    // ----- Divider -----

    private static Control BuildDivider(BoardsViewModel vm, RichBoardBlock block)
    {
        var shell = BlockShell(vm, block);
        var divider = block.Divider ?? new RichDividerView();
        // Line only; thickness/style/color live in the contextual strip when selected.
        if (divider.LineStyle != "none")
        {
            if (divider.LineStyle is "dashed" or "dotted")
            {
                shell.Children.Add(new TextBlock
                {
                    Text = divider.LineStyle == "dashed" ? "— — — — — — —" : "· · · · · · · · ·",
                    FontSize = 14,
                    Foreground = ParseBrushOrDefault(divider.Color, "#FF5F6368"),
                    Margin = new Thickness(0, 8, 0, 8),
                });
            }
            else
            {
                shell.Children.Add(new Rectangle
                {
                    Height = Math.Clamp(divider.Thickness, 1, 12),
                    Fill = ParseBrushOrDefault(divider.Color, "#FF5F6368"),
                    Margin = new Thickness(0, 8, 0, 8),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                });
            }
        }
        return shell;
    }

    // ----- Image -----

    private static Control BuildImage(BoardsViewModel vm, RichBoardBlock block)
    {
        var shell = BlockShell(vm, block);
        var image = block.Image;
        var selected = vm.SelectedBlockId == block.Id;
        // Bytes resolve asynchronously from the contract (the view never hauls
        // payloads); a placeholder shows until the bitmap lands.
        var frame = new Border
        {
            BorderBrush = BoardsTheme.AccentBrush,
            BorderThickness = new Thickness(selected ? 2 : 0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(selected ? 4 : 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 4),
            Child = new TextBlock
            {
                Text = "Loading image…",
                FontSize = 13,
                Foreground = BoardsTheme.SecondaryTextBrush,
            },
        };
        var captured = block.Id;
        var alignment = image?.Alignment;
        var alt = image?.AltText;
        var displayName = image?.DisplayName ?? "image";
        var width = image?.Width ?? 0;
        var height = image?.Height ?? 0;
        var keepAspect = image?.KeepAspectRatio != false;
        var rotation = image?.RotationDegrees ?? 0;
        var cropLeft = image?.CropLeft ?? 0;
        var cropTop = image?.CropTop ?? 0;
        var cropRight = image?.CropRight ?? 0;
        var cropBottom = image?.CropBottom ?? 0;
        var fitMode = image?.FitMode ?? "contain";
        var caption = image?.Caption ?? string.Empty;
        frame.PointerPressed += (_, _) => vm.FocusBlock(captured);
        _ = LoadImageIntoAsync(frame, vm, captured, alignment, alt, displayName, width, height,
            keepAspect, rotation, cropLeft, cropTop, cropRight, cropBottom, fitMode, caption);
        shell.Children.Add(frame);
        return shell;
    }

    private static async Task LoadImageIntoAsync(
        Border frame, BoardsViewModel vm, string blockId,
        string? alignment, string? altText, string displayName, double width, double height,
        bool keepAspect, double rotation, double cropLeft, double cropTop, double cropRight, double cropBottom,
        string fitMode, string caption)
    {
        // Embedded bytes resolve synchronously (cached per document version);
        // only sidecar payloads take the async path below.
        var immediate = vm.TryResolveImageBytes(blockId);
        if (immediate is not null)
        {
            SetImageContent(frame, immediate, alignment, altText, displayName, width, height,
                keepAspect, rotation, cropLeft, cropTop, cropRight, cropBottom, fitMode, caption);
            return;
        }
        byte[]? bytes = null;
        try
        {
            bytes = await vm.ResolveImageBytesAsync(blockId).ConfigureAwait(false);
        }
        catch
        {
            bytes = null;
        }
        if (bytes is null)
        {
            SetImagePlaceholder(frame, displayName);
            return;
        }
        var captured = bytes;
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            SetImageContent(frame, captured, alignment, altText, displayName, width, height,
                keepAspect, rotation, cropLeft, cropTop, cropRight, cropBottom, fitMode, caption);
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                SetImageContent(frame, captured, alignment, altText, displayName, width, height,
                    keepAspect, rotation, cropLeft, cropTop, cropRight, cropBottom, fitMode, caption));
    }

    private static void SetImagePlaceholder(Border frame, string displayName) =>
        frame.Child = new TextBlock
        {
            Text = displayName == "image (choose a file)"
                ? "No image bytes yet — use Replace to choose a file."
                : "Image unavailable: " + displayName,
            FontSize = 13,
            Foreground = BoardsTheme.SecondaryTextBrush,
            Margin = new Thickness(0, 4, 0, 4),
        };

    private static void SetImageContent(
        Border frame, byte[] bytes, string? alignment, string? altText, string displayName, double width,
        double height, bool keepAspect, double rotation, double cropLeft, double cropTop,
        double cropRight, double cropBottom, string fitMode, string caption)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            IImage source = bitmap;
            var left = Math.Clamp((int)Math.Round(bitmap.PixelSize.Width * cropLeft), 0, bitmap.PixelSize.Width - 1);
            var top = Math.Clamp((int)Math.Round(bitmap.PixelSize.Height * cropTop), 0, bitmap.PixelSize.Height - 1);
            var right = Math.Clamp((int)Math.Round(bitmap.PixelSize.Width * (1 - cropRight)), left + 1, bitmap.PixelSize.Width);
            var bottom = Math.Clamp((int)Math.Round(bitmap.PixelSize.Height * (1 - cropBottom)), top + 1, bitmap.PixelSize.Height);
            if (left > 0 || top > 0 || right < bitmap.PixelSize.Width || bottom < bitmap.PixelSize.Height)
                source = new CroppedBitmap(bitmap, new PixelRect(left, top, right - left, bottom - top));
            var stretch = fitMode switch
            {
                "cover" => Stretch.UniformToFill,
                "fill" => Stretch.Fill,
                "natural" => Stretch.None,
                _ => Stretch.Uniform
            };
            var fitHeight = height > 0 ? height : fitMode == "cover" && width > 0
                ? Math.Min(720, width * 0.67)
                : double.NaN;
            var control = new Image
            {
                Source = source,
                Width = fitMode == "natural" || width <= 0 ? double.NaN : width,
                Height = fitMode == "cover" ? fitHeight : keepAspect || height <= 0 ? double.NaN : height,
                MaxWidth = 720,
                MaxHeight = 720,
                Stretch = stretch,
                HorizontalAlignment = alignment switch
                {
                    "center" => HorizontalAlignment.Center,
                    "right" => HorizontalAlignment.Right,
                    _ => HorizontalAlignment.Left,
                },
                RenderTransform = Math.Abs(rotation) < 0.001 ? null : new RotateTransform(rotation),
                RenderTransformOrigin = RelativePoint.Center,
                Margin = new Thickness(0, 4, 0, 4),
            };
            AutomationProperties.SetName(control, "Image: " +
                (!string.IsNullOrEmpty(altText) ? altText : displayName));
            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(control);
            if (!string.IsNullOrWhiteSpace(caption))
                content.Children.Add(new TextBlock
                {
                    Text = caption,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Foreground = BoardsTheme.SecondaryTextBrush,
                    HorizontalAlignment = control.HorizontalAlignment,
                });
            frame.Child = content;
        }
        catch
        {
            frame.Child = new TextBlock
            {
                Text = "Image bytes are not decodable: " + displayName,
                FontSize = 13,
                Foreground = BoardsTheme.ErrorBrush,
                Margin = new Thickness(0, 4, 0, 4),
            };
        }
    }

    // ----- Graph -----

    private static Control BuildGraph(BoardsViewModel vm, RichBoardBlock block)
    {
        var shell = BlockShell(vm, block);
        var graph = vm.GetGraphForRendering(block.Id) ?? new RichGraphView();
        // Rendered plot dominates; editing lives in chips + collapsed settings.
        var graphVariables = vm.GetGraphSliderValues(block.Id);
        var graphCanvas = BuildGraphCanvas(graph, graphVariables);
        shell.Children.Add(graphCanvas);
        var capturedBlock = block.Id;
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        AddSmallButton(nav, "◀", "Pan graph left", () => vm.GraphPanZoom(capturedBlock, -0.2, 0, 1));
        AddSmallButton(nav, "▶", "Pan graph right", () => vm.GraphPanZoom(capturedBlock, 0.2, 0, 1));
        AddSmallButton(nav, "▲", "Pan graph up", () => vm.GraphPanZoom(capturedBlock, 0, 0.2, 1));
        AddSmallButton(nav, "▼", "Pan graph down", () => vm.GraphPanZoom(capturedBlock, 0, -0.2, 1));
        AddSmallButton(nav, "+", "Zoom graph in", () => vm.GraphPanZoom(capturedBlock, 0, 0, 1.5));
        AddSmallButton(nav, "−", "Zoom graph out", () => vm.GraphPanZoom(capturedBlock, 0, 0, 1 / 1.5));
        shell.Children.Add(nav);
        var chips = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 2) };
        foreach (var expr in graph.Expressions)
            chips.Children.Add(BuildGraphChip(vm, block.Id, expr));
        var add = new Button
        {
            Content = "+ Expression",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = BoardsTheme.SecondaryTextBrush,
            FontSize = 13,
            Padding = new Thickness(8, 4),
            Margin = new Thickness(0, 0, 0, 4),
        };
        ToolTip.SetTip(add, "Add a graph expression");
        AutomationProperties.SetName(add, "Add graph expression");
        add.Click += (_, _) => vm.GraphAddExpression(capturedBlock);
        chips.Children.Add(add);
        chips.IsVisible = vm.IsEditMode;
        shell.Children.Add(chips);

        if (vm.IsEditMode && graph.Expressions.Count == 0)
            shell.Children.Add(new TextBlock
            {
                Text = "Add an expression such as y = sin(x).",
                FontSize = 13,
                Foreground = BoardsTheme.SecondaryTextBrush,
                Margin = new Thickness(0, 0, 0, 4),
            });

        var settings = new Expander
        {
            Header = "Graph settings",
            IsExpanded = false,
            Margin = new Thickness(0, 2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsVisible = vm.IsEditMode,
        };
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        foreach (var expr in graph.Expressions)
            panel.Children.Add(BuildGraphExpressionRow(vm, block.Id, expr));

        if (vm.IsEditMode)
        {
            foreach (var slider in graph.Sliders)
                panel.Children.Add(BuildGraphSliderEditor(vm, block.Id, slider));
            var addSlider = new Button { Content = "+ Slider", Margin = new Thickness(0, 4, 0, 4) };
            ToolTip.SetTip(addSlider, "Add a variable slider to this graph");
            AutomationProperties.SetName(addSlider, "Add graph slider");
            addSlider.Click += (_, _) => vm.AddGraphSlider(block.Id);
            panel.Children.Add(addSlider);
        }
        else
        {
            if (graph.Sliders.Count > 0)
            {
                shell.Children.Add(GraphSliderCui.Build(vm, block.Id, graph, () =>
                {
                    var updated = BuildGraphCanvas(graph, vm.GetGraphSliderValues(block.Id));
                    var currentIndex = shell.Children.IndexOf(graphCanvas);
                    if (currentIndex >= 0)
                    {
                        shell.Children.RemoveAt(currentIndex);
                        shell.Children.Insert(currentIndex, updated);
                        graphCanvas = updated;
                    }
                }));
            }
        }

        var viewport = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        AddViewportField(vm, viewport, capturedBlock, "XMin", graph.XMin);
        AddViewportField(vm, viewport, capturedBlock, "XMax", graph.XMax);
        AddViewportField(vm, viewport, capturedBlock, "YMin", graph.YMin);
        AddViewportField(vm, viewport, capturedBlock, "YMax", graph.YMax);
        var apply = new Button { Content = "Apply", Margin = new Thickness(8, 0, 0, 0) };
        ToolTip.SetTip(apply, "Apply viewport and redraw curves");
        AutomationProperties.SetName(apply, "Apply graph viewport");
        apply.Click += (_, _) => vm.RefreshAfterEdit();
        viewport.Children.Add(apply);
        panel.Children.Add(viewport);

        settings.Content = panel;
        shell.Children.Add(settings);
        return shell;
    }

    private static Control BuildGraphChip(BoardsViewModel vm, string blockId, RichGraphExpressionView expr)
    {
        var chip = new Border
        {
            Background = expr.Visible ? BoardsTheme.CardBrush : Brushes.Transparent,
            BorderBrush = BoardsTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 4),
            Margin = new Thickness(0, 0, 6, 4),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Ellipse
        {
            Width = 10,
            Height = 10,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Fill = TryBrush(expr.Color, out var dot) && dot is not null
                ? dot
                : BoardsTheme.AccentBrush,
        });
        row.Children.Add(new TextBlock
        {
            Text = expr.Text,
            FontSize = 13,
            Foreground = expr.Visible ? BoardsTheme.TextBrush : BoardsTheme.SecondaryTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var eye = new Button
        {
            Content = new TextBlock
            {
                Text = expr.Visible ? "Hide" : "Show",
                FontSize = 12,
                Foreground = BoardsTheme.SecondaryTextBrush,
            },
            Padding = new Thickness(8, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var eyeTip = expr.Visible ? "Hide expression" : "Show expression";
        ToolTip.SetTip(eye, eyeTip);
        Avalonia.Automation.AutomationProperties.SetName(eye, eyeTip);
        var exprId = expr.Id;
        var visible = expr.Visible;
        eye.Click += (_, _) => vm.ToggleGraphExpression(blockId, exprId);
        var erase = new Button
        {
            Content = BoardsIcons.Glyph(BoardsIcons.Close, 10),
            Padding = new Thickness(8, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(erase, "Remove this expression");
        Avalonia.Automation.AutomationProperties.SetName(erase, "Remove graph expression");
        erase.Click += async (_, _) => await vm.GraphDeleteExpressionAsync(blockId, exprId);
        row.Children.Add(eye);
        row.Children.Add(erase);
        chip.Child = row;
        return chip;
    }

    private static Control BuildGraphExpressionRow(BoardsViewModel vm, string blockId, RichGraphExpressionView expr)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        var visible = new CheckBox
        {
            Name = $"gv_{blockId}_{expr.Id}",
            IsChecked = expr.Visible,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        AutomationProperties.SetName(visible, "Graph expression visible");
        var text = new TextBox
        {
            Name = $"gx_{blockId}_{expr.Id}",
            Text = expr.Text,
            Width = 260,
            Margin = new Thickness(0, 0, 8, 0),
        };
        ToolTip.SetTip(text, "Plot y = sin(x), or shade an inequality such as y ≤ x^2");
        AutomationProperties.SetName(text, "Graph expression");
        var color = new TextBox
        {
            Name = $"gc_{blockId}_{expr.Id}",
            Text = expr.Color,
            Width = 110,
            Margin = new Thickness(0, 0, 8, 0),
        };
        ToolTip.SetTip(color, "Expression color (hex)");
        AutomationProperties.SetName(color, "Graph expression color");
        var erase = new Button { Content = "✕" };
        ToolTip.SetTip(erase, "Remove this expression");
        AutomationProperties.SetName(erase, "Remove graph expression");
        var exprId = expr.Id;
        visible.IsCheckedChanged += (_, _) => vm.EditCheck($"gv_{blockId}_{exprId}", visible.IsChecked == true);
        text.TextChanged += (_, _) => vm.EditText($"gx_{blockId}_{exprId}", text.Text ?? string.Empty);
        text.GotFocus += (_, _) => vm.FocusBlock(blockId);
        color.TextChanged += (_, _) => vm.EditText($"gc_{blockId}_{exprId}", color.Text ?? string.Empty);
        erase.Click += async (_, _) => await vm.GraphDeleteExpressionAsync(blockId, exprId);
        row.Children.Add(visible);
        row.Children.Add(text);
        row.Children.Add(color);
        row.Children.Add(erase);
        return row;
    }

    private static Control BuildGraphSliderEditor(BoardsViewModel vm, string blockId, RichGraphSliderView slider)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        TextBox Field(string text, double width, string accessibleName)
        {
            var input = new TextBox { Text = text, Width = width, Margin = new Thickness(0, 0, 5, 0) };
            AutomationProperties.SetName(input, accessibleName);
            return input;
        }
        var name = Field(slider.Name, 42, "Graph slider variable name");
        var minimum = Field(slider.Minimum.ToString("G"), 76, "Graph slider minimum");
        var maximum = Field(slider.Maximum.ToString("G"), 76, "Graph slider maximum");
        var step = Field(slider.Step.ToString("G"), 76, "Graph slider step");
        var defaultValue = Field(slider.DefaultValue.ToString("G"), 76, "Graph slider default value");
        ToolTip.SetTip(name, "Variable used in expressions, e.g. a");
        ToolTip.SetTip(minimum, "Minimum value");
        ToolTip.SetTip(maximum, "Maximum value");
        ToolTip.SetTip(step, "Slider step");
        ToolTip.SetTip(defaultValue, "Saved default value");
        void Commit()
        {
            if (double.TryParse(minimum.Text, out var min) && double.TryParse(maximum.Text, out var max) &&
                double.TryParse(step.Text, out var increment) && double.TryParse(defaultValue.Text, out var initial))
                vm.ConfigureGraphSlider(blockId, slider.VariableId, name.Text ?? string.Empty,
                    min, max, increment, initial);
        }
        name.LostFocus += (_, _) => Commit();
        minimum.LostFocus += (_, _) => Commit();
        maximum.LostFocus += (_, _) => Commit();
        step.LostFocus += (_, _) => Commit();
        defaultValue.LostFocus += (_, _) => Commit();
        var remove = new Button { Content = "Remove", Padding = new Thickness(6, 2) };
        ToolTip.SetTip(remove, "Remove this slider definition");
        AutomationProperties.SetName(remove, $"Remove graph slider {slider.Name}");
        remove.Click += (_, _) => vm.RemoveGraphSlider(blockId, slider.VariableId);
        row.Children.Add(new TextBlock { Text = "Variable", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
        row.Children.Add(name);
        row.Children.Add(minimum);
        row.Children.Add(maximum);
        row.Children.Add(step);
        row.Children.Add(defaultValue);
        row.Children.Add(remove);
        return row;
    }

    private static void AddViewportField(BoardsViewModel vm, Panel parent, string blockId, string field, double value)
    {
        parent.Children.Add(new TextBlock
        {
            Text = field,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0),
        });
        var box = new TextBox
        {
            Name = $"gvp_{field}_{blockId}",
            Text = value.ToString("0.##"),
            Width = 64,
        };
        AutomationProperties.SetName(box, "Graph viewport " + field);
        box.TextChanged += (_, _) => vm.EditText($"gvp_{field}_{blockId}", box.Text ?? string.Empty);
        box.GotFocus += (_, _) => vm.FocusBlock(blockId);
        parent.Children.Add(box);
    }

    private static Control BuildGraphCanvas(RichGraphView graph, IReadOnlyDictionary<string, double>? variables = null)
    {
        var canvas = new Canvas
        {
            Width = GraphWidth,
            Height = GraphHeight,
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.Parse("#FFFFFFFF")),
            Margin = new Thickness(0, 4, 0, 0),
        };
        AutomationProperties.SetName(canvas, "Graph plot");
        const double pad = 14;
        double mapX(double x) => pad + ((x - graph.XMin) / (graph.XMax - graph.XMin)) * (GraphWidth - 2 * pad);
        double mapY(double y) => GraphHeight - pad - ((y - graph.YMin) / (graph.YMax - graph.YMin)) * (GraphHeight - 2 * pad);

        if (graph.ShowGrid)
        {
            for (var i = 1; i < 10; i++)
            {
                var gx = pad + ((GraphWidth - 2 * pad) * i / 10);
                var gy = pad + ((GraphHeight - 2 * pad) * i / 10);
                canvas.Children.Add(new Line
                {
                    StartPoint = new Point(gx, pad),
                    EndPoint = new Point(gx, GraphHeight - pad),
                    Stroke = new SolidColorBrush(Color.Parse("#FFE0E0E0")),
                    StrokeThickness = 1,
                });
                canvas.Children.Add(new Line
                {
                    StartPoint = new Point(pad, gy),
                    EndPoint = new Point(GraphWidth - pad, gy),
                    Stroke = new SolidColorBrush(Color.Parse("#FFE0E0E0")),
                    StrokeThickness = 1,
                });
            }
        }
        if (graph.ShowAxes && graph.XMin < 0 && graph.XMax > 0)
            canvas.Children.Add(new Line
            {
                StartPoint = new Point(mapX(0), pad),
                EndPoint = new Point(mapX(0), GraphHeight - pad),
                Stroke = Brushes.Gray,
                StrokeThickness = 1,
            });
        if (graph.ShowAxes && graph.YMin < 0 && graph.YMax > 0)
            canvas.Children.Add(new Line
            {
                StartPoint = new Point(pad, mapY(0)),
                EndPoint = new Point(GraphWidth - pad, mapY(0)),
                Stroke = Brushes.Gray,
                StrokeThickness = 1,
            });

        var errors = new List<string>();
        foreach (var expr in graph.Expressions)
        {
            if (!expr.Visible)
                continue;
            var isInequality = HavenGraphExpression.TryGetInequality(expr.Text, out var boundaryExpression, out var shadeAbove);
            var plotExpression = isInequality ? boundaryExpression : expr.Text;
            var samples = HavenGraphExpression.Sample(
                plotExpression, expr.DomainMin, expr.DomainMax, graph.XMin, graph.XMax, 200, variables);
            if (samples.Count == 0)
            {
                var mid = (graph.XMin + graph.XMax) / 2;
                if (!HavenGraphExpression.TryEvaluate(plotExpression, mid, out _, variables))
                    errors.Add("Invalid expression \"" + expr.Text + "\" — curve hidden.");
                else
                    errors.Add("Expression \"" + expr.Text + "\" has no points in this viewport.");
                continue;
            }
            var stroke = ParseBrushOrDefault(expr.Color, "#FF1A73E8");
            if (isInequality)
                AddGraphShading(canvas, samples, shadeAbove, expr.Color, graph, mapX, mapY);
            List<Point>? run = null;
            void flush()
            {
                if (run is { Count: > 1 })
                    canvas.Children.Add(new Polyline
                    {
                        Points = run,
                        Stroke = stroke,
                        StrokeThickness = Math.Clamp(expr.LineWidth, 1, 8),
                    });
                run = null;
            }
            foreach (var (x, y) in samples)
            {
                var px = mapX(x);
                var py = mapY(y);
                if (!double.IsFinite(px) || !double.IsFinite(py)
                    || py < -GraphHeight || py > GraphHeight * 2)
                {
                    flush();
                    continue;
                }
                run ??= [];
                run.Add(new Point(px, py));
            }
            flush();
        }

        if (errors.Count > 0)
        {
            var wrap = new StackPanel { Orientation = Orientation.Vertical };
            wrap.Children.Add(canvas);
            foreach (var error in errors)
                wrap.Children.Add(new TextBlock
                {
                    Text = error,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse("#FFB00020")),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0),
                });
            return wrap;
        }
        return canvas;
    }

    private static void AddGraphShading(Canvas canvas, IReadOnlyList<(double X, double Y)> samples,
        bool shadeAbove, string colorText, RichGraphView graph, Func<double, double> mapX,
        Func<double, double> mapY)
    {
        Color source;
        try { source = Color.Parse(colorText); }
        catch { source = Color.Parse("#FF1A73E8"); }
        var fill = new SolidColorBrush(Color.FromArgb(48, source.R, source.G, source.B));
        var run = new List<Point>();
        void AddRegion()
        {
            if (run.Count < 2)
            {
                run.Clear();
                return;
            }
            var edgeY = mapY(shadeAbove ? graph.YMax : graph.YMin);
            var polygon = run.ToList();
            polygon.Add(new Point(run[^1].X, edgeY));
            polygon.Add(new Point(run[0].X, edgeY));
            canvas.Children.Add(new Polygon { Points = polygon, Fill = fill });
            run.Clear();
        }

        foreach (var (x, y) in samples)
        {
            var px = mapX(x);
            var py = mapY(y);
            if (!double.IsFinite(px) || !double.IsFinite(py) || py < -GraphHeight || py > GraphHeight * 2)
            {
                AddRegion();
                continue;
            }
            run.Add(new Point(px, py));
        }
        AddRegion();
    }

    // ----- Attachment -----

    private static Control BuildAttachmentRow(BoardsViewModel vm, RichBoardBlock block)
    {
        var card = new Border
        {
            Background = BoardsTheme.CardBrush,
            BorderBrush = BoardsTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 0, 0, 8),
            MaxWidth = 480,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(BoardsIcons.Glyph(BoardsIcons.File, 22, "Attachment"));
        var text = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(10, 0, 0, 0) };
        text.Children.Add(new TextBlock
        {
            Text = block.AttachmentName ?? "Attachment",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = BoardsTheme.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = AttachmentMeta(block),
            FontSize = 12,
            Foreground = BoardsTheme.SecondaryTextBrush,
        });
        row.Children.Add(text);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var open = new Button
        {
            Content = "Open",
            Padding = new Thickness(10, 4),
            Margin = new Thickness(0, 0, 4, 0),
        };
        ToolTip.SetTip(open, "Resolve and open the attachment");
        Avalonia.Automation.AutomationProperties.SetName(open, "Open attachment");
        var captured = block.Id;
        open.Click += async (_, _) => await vm.OpenAttachmentAsync(captured);
        var remove = new Button
        {
            Content = BoardsIcons.Glyph(BoardsIcons.More, 13, "Attachment options"),
            Padding = new Thickness(8, 4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        ToolTip.SetTip(remove, "Detach the attachment");
        Avalonia.Automation.AutomationProperties.SetName(remove, "Detach attachment");
        remove.Click += async (_, _) => await vm.RemoveAttachmentAsync(captured);
        remove.IsVisible = vm.IsEditMode;
        actions.Children.Add(open);
        actions.Children.Add(remove);
        row.Children.Add(actions);
        card.Child = row;
        return card;
    }

    private static string AttachmentMeta(RichBoardBlock block)
    {
        var ext = System.IO.Path.GetExtension(block.AttachmentName ?? string.Empty).TrimStart('.').ToUpperInvariant();
        if (string.IsNullOrEmpty(ext))
            ext = "File";
        return $"{ext} · {FormatBytes(block.AttachmentSize)}";
    }

    internal static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB",
    };


    // ----- Helpers -----

    private static void AddSmallButton(Panel parent, string content, string tip, Action tapped)
    {
        var button = new Button { Content = content, Margin = new Thickness(0, 0, 8, 0) };
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => tapped();
        parent.Children.Add(button);
    }

    private static void AddSmallButton(Panel parent, string content, string tip, Func<Task> tapped)
    {
        var button = new Button { Content = content, Margin = new Thickness(0, 0, 8, 0) };
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, tip);
        button.Click += async (_, _) => await tapped();
        parent.Children.Add(button);
    }

    private static int DerivedMax(RichBoardBlock block, bool row)
    {
        var best = 0;
        foreach (var key in block.TableCells.Keys)
        {
            var cut = key.IndexOf(',');
            if (cut < 0)
                continue;
            var part = row ? key[..cut] : key[(cut + 1)..];
            if (int.TryParse(part, out var value))
                best = Math.Max(best, value);
        }
        return best;
    }

    private static TextAlignment ParseAlignment(string alignment) => alignment?.ToLowerInvariant() switch
    {
        "center" => TextAlignment.Center,
        "right" => TextAlignment.Right,
        "justify" => TextAlignment.Justify,
        _ => TextAlignment.Left,
    };

    private static bool TryBrush(string? color, out IBrush? brush)
    {
        brush = null;
        if (string.IsNullOrWhiteSpace(color))
            return false;
        try
        {
            brush = new SolidColorBrush(Color.Parse(color.Trim()));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IBrush ParseBrushOrDefault(string? color, string fallback)
    {
        if (TryBrush(color, out var brush) && brush is not null)
            return brush;
        return new SolidColorBrush(Color.Parse(fallback));
    }

    private static IBrush TableBorderBrush(RichBoardBlock block) =>
        TryBrush(block.TableStyle.BorderColor, out var brush) && brush is not null
            ? brush
            : BoardsTheme.AccentBrush;

    /// <summary>Checklist view ids are "parentId:itemId"; plain ids map to themselves with '_' separators.</summary>
    private static string NameSuffix(string viewId) => viewId.Replace(':', '_');

    private static string ParentKey(string viewId)
    {
        var colon = viewId.IndexOf(':');
        return colon > 0 ? viewId[..colon] : viewId;
    }

    /// <summary>Pointer owner for the explicit Free placement gesture on a block grip.</summary>
    private sealed class BlockPlacementGesture
    {
        private const double DragThreshold = 6;
        private readonly Control _grip;
        private readonly Border _row;
        private readonly Control _content;
        private readonly Panel _absoluteHost;
        private readonly BoardsViewModel _viewModel;
        private readonly string _pageId;
        private readonly string _blockId;
        private readonly RichBoardBlock _block;
        private Button? _handle;
        private IPointer? _pointer;
        private Point _start;
        private double _anchorX;
        private double _anchorY;
        private double _width;
        private double _height;
        private ITransform? _originalTransform;
        private bool _pressed;
        private bool _dragging;

        private BlockPlacementGesture(Control grip, Border row, Control content,
            Panel absoluteHost, BoardsViewModel viewModel, string pageId, string blockId, RichBoardBlock block)
        {
            _grip = grip;
            _row = row;
            _content = content;
            _absoluteHost = absoluteHost;
            _viewModel = viewModel;
            _pageId = pageId;
            _blockId = blockId;
            _block = block;
            _grip.AttachedToVisualTree += OnGripAttached;
            _grip.LayoutUpdated += OnGripLayoutUpdated;
            TryAttachHandle();
        }

        public static void Attach(Control grip, Border row, Control content,
            Panel absoluteHost, BoardsViewModel viewModel, string pageId, string blockId, RichBoardBlock block)
        {
            _ = new BlockPlacementGesture(grip, row, content, absoluteHost,
                viewModel, pageId, blockId, block);
        }

        private void OnGripAttached(object? sender, VisualTreeAttachmentEventArgs e) => TryAttachHandle();
        private void OnGripLayoutUpdated(object? sender, EventArgs e) => TryAttachHandle();

        private void TryAttachHandle()
        {
            if (_handle is not null) return;
            var handle = _grip.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.Name == "BlockGripToggle");
            if (handle is null) return;
            _handle = handle;
            _grip.AttachedToVisualTree -= OnGripAttached;
            _grip.LayoutUpdated -= OnGripLayoutUpdated;
            handle.PointerPressed += OnPressed;
            handle.PointerMoved += OnMoved;
            handle.PointerReleased += OnReleased;
            handle.PointerCaptureLost += OnCaptureLost;
            handle.KeyDown += OnKeyDown;
            handle.DetachedFromVisualTree += OnDetached;
        }

        private void OnPressed(object? sender, PointerPressedEventArgs e)
        {
            var handle = _handle;
            if (handle is null || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed ||
                !_viewModel.IsEditMode || !_viewModel.CanChangeLayoutMode ||
                !string.Equals(_pageId, _viewModel.SelectedPageId, StringComparison.Ordinal) || _pressed)
                return;

            _pressed = true;
            _pointer = e.Pointer;
            _start = e.GetPosition(_absoluteHost);
            var rowPosition = e.GetPosition(_row);
            _anchorX = rowPosition.X - BlockGutterLeadingInset;
            _anchorY = rowPosition.Y - BlockContentTopInset;
            _width = _block.Layout == HavenRichBlockLayouts.Absolute
                ? _block.Width
                : Math.Max(1, _content.Bounds.Width);
            _height = _block.Layout == HavenRichBlockLayouts.Absolute
                ? _block.Height
                : Math.Max(1, _content.Bounds.Height);
        }

        private void OnMoved(object? sender, PointerEventArgs e)
        {
            var handle = _handle;
            if (handle is null || !_pressed || !ReferenceEquals(_pointer, e.Pointer)) return;
            var position = e.GetPosition(_absoluteHost);
            var delta = position - _start;
            if (!_dragging && Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y) >= DragThreshold)
            {
                if (!_viewModel.IsEditMode || !_viewModel.CanChangeLayoutMode ||
                    !string.Equals(_pageId, _viewModel.SelectedPageId, StringComparison.Ordinal))
                {
                    Cancel();
                    return;
                }
                _dragging = true;
                _originalTransform = _row.RenderTransform;
                _pointer?.Capture(handle);
                ToolTip.SetTip(handle, "Drag to place this block. Press Escape to cancel.");
            }
            if (!_dragging) return;
            _row.RenderTransform = new TranslateTransform(delta.X, delta.Y);
            e.Handled = true;
        }

        private void OnReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_pressed || !ReferenceEquals(_pointer, e.Pointer)) return;
            var wasDrag = _dragging;
            var position = e.GetPosition(_absoluteHost);
            var x = Math.Clamp(position.X - _anchorX, 0, HavenRichBlockLayoutGeometry.CoordinateLimit);
            var y = Math.Clamp(position.Y - _anchorY, 0, HavenRichBlockLayoutGeometry.CoordinateLimit);
            if (wasDrag) e.Handled = true;
            Cancel();
            if (wasDrag) _ = CommitPlacementAsync(x, y);
        }

        private async Task CommitPlacementAsync(double x, double y)
        {
            try
            {
                if (!string.Equals(_pageId, _viewModel.SelectedPageId, StringComparison.Ordinal)) return;
                await _viewModel.PlaceBlockAsync(_pageId, _blockId, x, y, _width, _height, _block.ZOrder);
            }
            catch (OperationCanceledException)
            {
                ReportStatus("Block placement was cancelled.");
            }
            catch (Exception error)
            {
                try { _viewModel.ReportBlockMoveFailure(error); }
                catch { ReportStatus("Block placement failed."); }
            }
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || !_pressed) return;
            e.Handled = true;
            Cancel();
        }

        private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (_pressed) Cancel();
        }

        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Cancel();
        private void ReportStatus(string message)
        {
            try { _viewModel.Set("StatusText", message); }
            catch { /* Status observers must not escape an input callback. */ }
        }

        private void Cancel()
        {
            var pointer = _pointer;
            var handle = _handle;
            _pointer = null;
            _pressed = false;
            _dragging = false;
            _row.RenderTransform = _originalTransform;
            _originalTransform = null;
            if (pointer is not null && handle is not null && pointer.Captured == handle)
                pointer.Capture(null);
        }
    }
}

