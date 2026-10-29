using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CakeOS.Apps.Boards.App;

internal sealed record BlockReorderRow(string BlockId, int Index);

/// <summary>Pointer drag owner for the authored block grip; commits through the typed session move.</summary>
internal sealed class BlockReorderPointer
{
    // UX27-BOARD-023: where no platform drag threshold is supplied, use a 4-unit logical
    // default. A larger value makes grip dragging feel dead, which the owner reported (C03).
    private const double DragThreshold = 4;

    // Owner report on 29 October: "using the 6 dots to move is a bit janky. the actual block should
    // move (animated) and snap to the new position, it shouldn't just show a line there". The vertical
    // flow grip only drew a 3-unit accent line at the would-be insertion edge: the block stayed put,
    // nothing followed the pointer, and the list jumped when the typed move committed. (The free-layout
    // grip in BlockPlacementGesture already translates the real row, so the two grips behaved
    // differently.) Now the dragged row follows the pointer, the rows it crosses glide aside to open
    // the slot, and on release the block slides into that slot before the typed move commits, so the
    // rebuild lands on the arrangement the preview already showed instead of teleporting it.
    //
    // The settle loop is driven by a render-priority timer rather than Avalonia Transitions because
    // this file already drives auto-scroll that way, and a transition that the fork silently ignores
    // would put the teleport straight back.
    private const double SettleFraction = 0.35;
    private const double SettleEpsilon = 0.4;
    private const int SettleIntervalMs = 16;
    private const int MaxSettleFrames = 60;

    private readonly Control _grip;
    private Button? _handle;
    private readonly StackPanel _host;
    private readonly BoardsViewModel _viewModel;
    private readonly string _blockId;
    private readonly int _sourceIndex;
    private IPointer? _pointer;
    private Point _start;
    private bool _pressed;
    private bool _dragging;
    private double _lastHostY;
    private int _scrollDirection;
    private ScrollViewer? _scrollViewer;
    private DispatcherTimer? _autoScrollTimer;
    private BoardBlockActionTarget? _target;
    private bool _draggedItemRow;

    // Lift state is deliberately separate from the gesture flags: a released gesture must still be
    // able to finish its slide into the slot before the visuals are taken away.
    private readonly List<Slot> _slots = [];
    private readonly Dictionary<Border, Offset> _offsets = new();
    private Border? _liftRow;
    private TranslateTransform? _liftTransform;
    private IBrush? _savedBackground;
    private BoxShadows _savedShadow;
    private int _savedZIndex;
    private int _sourceOrdinal;
    private bool _lifted;
    private DispatcherTimer? _settleTimer;
    private TaskCompletionSource? _settle;
    private int _settleFrames;
    private Border? _flipRow;
    private TranslateTransform? _flipTransform;
    private double _flipCurrent;
    private Border? _guideOutline;
    private Thickness _guideThickness;
    private IBrush? _guideBrush;

    private sealed record Slot(Border Row, int Ordinal, double Top, double Step);

    private sealed class Offset
    {
        public required Border Row;
        public required TranslateTransform Transform;
        public TranslateTransform? Initial;
        public double Current;
        public double Target;
    }

    private BlockReorderPointer(Control grip, StackPanel host, BoardsViewModel viewModel,
        string blockId, int sourceIndex)
    {
        _grip = grip;
        _host = host;
        _viewModel = viewModel;
        _blockId = blockId;
        _sourceIndex = sourceIndex;
        _grip.AttachedToVisualTree += OnGripAttached;
        _grip.LayoutUpdated += OnGripLayoutUpdated;
        TryAttachHandle();
    }

    private void TryAttachHandle()
    {
        if (_handle is not null) return;
        var handle = _grip.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(button => button.Name == "BlockGripToggle");
        if (handle is null) return;
        _handle = handle;
        _grip.AttachedToVisualTree -= OnGripAttached;
        _grip.LayoutUpdated -= OnGripLayoutUpdated;
        // Button handles a press in its class handler before ordinary instance
        // handlers run. Observe that press without stealing its normal click.
        handle.AddHandler(InputElement.PointerPressedEvent, OnPressed,
            RoutingStrategies.Bubble, handledEventsToo: true);
        handle.PointerMoved += OnMoved;
        // Consume a drag before Button can turn its release into a click. A
        // plain click is cleaned up afterwards, including handled releases.
        handle.AddHandler(InputElement.PointerReleasedEvent, OnReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        handle.PointerCaptureLost += OnCaptureLost;
        handle.KeyDown += OnKeyDown;
        handle.DetachedFromVisualTree += OnDetached;
    }

    public static void Attach(Control grip, StackPanel host, BoardsViewModel viewModel,
        string blockId, int sourceIndex)
    {
        _ = new BlockReorderPointer(grip, host, viewModel, blockId, sourceIndex);
    }

    private void OnGripAttached(object? sender, VisualTreeAttachmentEventArgs e) => TryAttachHandle();
    private void OnGripLayoutUpdated(object? sender, EventArgs e) => TryAttachHandle();

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var handle = _handle;
        if (handle is null || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed || !_viewModel.IsEditMode ||
            _viewModel.Session is not ContractSessionAdapter || _pressed) return;
        _pressed = true;
        _pointer = e.Pointer;
        _start = e.GetPosition(_host);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        var handle = _handle;
        if (handle is null || !_pressed || !ReferenceEquals(_pointer, e.Pointer)) return;
        var position = e.GetPosition(_host);
        if (!_dragging && Math.Abs(position.Y - _start.Y) >= DragThreshold)
        {
            _draggedItemRow = _blockId.Contains(':');
            _target = (_viewModel.Session as ContractSessionAdapter)?.CaptureBlockAction(_viewModel.SelectedPageId, _blockId);
            if (_target is null) { Cancel(); return; }
            _dragging = true;
            _pointer?.Capture(handle);
            ToolTip.SetTip(handle, "Drag to reorder. Press Escape to cancel.");
            _scrollViewer = _host.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
            BeginLift();
        }
        if (_dragging)
        {
            e.Handled = true;
            _lastHostY = position.Y;
            FollowPointer(position.Y);
            OpenSlot(GetDestination(position.Y));
            UpdateAutoScroll(e);
        }
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pressed || !ReferenceEquals(_pointer, e.Pointer)) return;
        if (e.Route == RoutingStrategies.Tunnel && !_dragging) return;
        try
        {
            var wasDrag = _dragging;
            var destination = wasDrag ? GetDestination(e.GetPosition(_host).Y) : null;
            var target = _target;
            // Resolved while the drag identity is still live: EndGesture below clears the flags this
            // gesture owns, and the slot mapping depends on whether this is a list row.
            var slot = wasDrag && destination is int index ? SlotOrdinalFor(index) : -1;
            if (wasDrag) e.Handled = true;
            StopAutoScroll();
            EndGesture();
            if (!wasDrag)
            {
                RestoreLift();
                return;
            }
            if (target is null || slot < 0 || slot == _sourceOrdinal || !_lifted)
            {
                // No move: the block glides back to where it came from instead of snapping.
                _ = GlideHomeAsync();
                return;
            }
            _ = SnapIntoSlotAsync(target, destination!.Value, slot);
        }
        catch (OperationCanceledException)
        {
            CancelSafely();
            ReportStatusSafely("Block move was cancelled.");
        }
        catch (Exception error)
        {
            CancelSafely();
            try { _viewModel.ReportBlockMoveFailure(error); }
            catch { /* A status observer cannot throw back into pointer event dispatch. */ }
        }
    }

    private async Task CommitReleasedMoveAsync(BoardBlockActionTarget target, int destination)
    {
        try
        {
            _viewModel.FocusBlock(target.BlockId);
            await _viewModel.DispatchCuiBlockMoveAsync(new BoardBlockMoveRequest(target, destination));
        }
        catch (OperationCanceledException)
        {
            ReportStatusSafely("Block move was cancelled.");
        }
        catch (Exception error)
        {
            try { _viewModel.ReportBlockMoveFailure(error); }
            catch { /* A status observer cannot fault the released pointer task. */ }
        }
    }

    /// <summary>Commits the typed move immediately - a model change must never wait on an animation -
    /// then glides the real rebuilt row from the point the pointer let go into its new layout position,
    /// so the block still reads as moving into the slot instead of teleporting.</summary>
    private async Task SnapIntoSlotAsync(BoardBlockActionTarget target, int destination, int slot)
    {
        var droppedTop = _liftRow is null || _slots.Count == 0
            ? double.NaN
            : _slots[_sourceOrdinal].Top + (_offsets.TryGetValue(_liftRow, out var live) ? live.Current : 0);
        RestoreLift();
        try
        {
            await CommitReleasedMoveAsync(target, destination).ConfigureAwait(false);
            if (double.IsNaN(droppedTop)) return;
            await OnUiRunAsync(() =>
            {
                _host.UpdateLayout();
                var landed = CandidateRows().FirstOrDefault(row =>
                    (row.Tag as BlockReorderRow)?.BlockId == _blockId);
                if (landed is null) return;
                var newTop = landed.TranslatePoint(new Point(0, 0), _host)?.Y ?? double.NaN;
                var offset = newTop - droppedTop;
                if (double.IsNaN(newTop) || Math.Abs(offset) < 2) return;
                // The row is translated back to where the user released it and then settles to zero.
                var transform = new TranslateTransform(0, offset);
                landed.RenderTransform = transform;
                _flipRow = landed;
                _flipTransform = transform;
                _flipCurrent = offset;
                StartSettle();
            });
        }
        catch (Exception error)
        {
            try { _viewModel.ReportBlockMoveFailure(error); }
            catch { /* A status observer cannot fault this task. */ }
        }
    }

    private static async Task OnUiRunAsync(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) { action(); return; }
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(action);
    }

    private async Task GlideHomeAsync()
    {
        try
        {
            foreach (var entry in _offsets.Values) entry.Target = 0;
            await SettleAsync().ConfigureAwait(false);
        }
        catch { /* an interrupted glide home must never fault a pointer callback */ }
        finally { RestoreLiftSafely(); }
    }

    private void BeginLift()
    {
        if (_lifted) RestoreLiftSafely();
        _slots.Clear();
        _offsets.Clear();
        _liftRow = null;
        _sourceOrdinal = -1;
        foreach (var row in CandidateRows())
        {
            var top = row.TranslatePoint(new Point(0, 0), _host)?.Y ?? double.NaN;
            if (double.IsNaN(top)) return; // an unmeasured row cannot be animated honestly
            _slots.Add(new Slot(row, _slots.Count, top, StepOf(row)));
            if (string.Equals((row.Tag as BlockReorderRow)?.BlockId, _blockId, StringComparison.Ordinal))
            {
                _liftRow = row;
                _sourceOrdinal = _slots.Count - 1;
            }
        }
        if (_liftRow is null || _sourceOrdinal < 0)
        {
            _slots.Clear();
            return;
        }
        var lifted = _liftRow;
        _savedBackground = lifted.Background;
        _savedShadow = lifted.BoxShadow;
        _savedZIndex = lifted.ZIndex;
        // The block reads as lifted: a surface under it keeps the text legible while it passes over its
        // neighbours, and ZIndex keeps it on top of them. That surface must come from THIS row's own
        // resolved resources: BoardsTheme.CardBrush falls back to the standalone palette, which is
        // compiled Light, and that is exactly what flipped the dragged block to a light surface inside
        // Home's dark window (owner report, 29 October). If nothing resolves, the row keeps its own
        // background and the shadow alone carries the lift.
        if (SurfaceBrushOf(lifted) is { } liftSurface) lifted.Background = liftSurface;
        lifted.BoxShadow = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 4, Blur = 18, Spread = 1,
            Color = Color.FromArgb(64, 0, 0, 0) });
        lifted.ZIndex = 100;
        _liftTransform = new TranslateTransform(0, 0);
        lifted.RenderTransform = _liftTransform;
        foreach (var slot in _slots)
        {
            var isLift = ReferenceEquals(slot.Row, lifted);
            var entry = new Offset
            {
                Row = slot.Row,
                Transform = isLift ? _liftTransform : new TranslateTransform(0, 0),
                Initial = isLift ? _liftTransform : slot.Row.RenderTransform as TranslateTransform,
            };
            _offsets[slot.Row] = entry;
            if (!isLift) slot.Row.RenderTransform = entry.Transform;
        }
        _lifted = true;
    }

    private void FollowPointer(double pointerY)
    {
        if (!_lifted || _liftTransform is null || _liftRow is null) return;
        if (!_host.Children.Contains(_liftRow)) { RestoreLiftSafely(); return; }
        var own = _slots[_sourceOrdinal].Top;
        var first = _slots[0].Top;
        var last = _slots[^1].Top + _slots[^1].Step;
        var delta = pointerY - _start.Y;
        delta = Math.Clamp(delta, first - own, Math.Max(first - own, last - StepOf(_liftRow) - own));
        _liftTransform.Y = delta;
        if (_offsets.TryGetValue(_liftRow, out var entry)) entry.Current = delta;
    }

    /// <summary>Moves the slot: the rows the block crosses glide aside, so the destination is the gap
    /// itself rather than a line drawn at it.</summary>
    private void OpenSlot(int? destination)
    {
        if (!_lifted || _liftRow is null) return;
        var slot = destination is int index ? SlotOrdinalFor(index) : -1;
        if (slot < 0) slot = _sourceOrdinal;
        foreach (var entry in _offsets.Values)
            if (!ReferenceEquals(entry.Row, _liftRow)) entry.Target = ShiftFor(entry.Row, slot);
        // The owner asked for the insertion line to stay while dragging for accessibility, in addition
        // to the moving block: it marks the exact edge the block will land against. It is painted on
        // the row's zero-footprint overlay instead of the row border, so it cannot shift any text.
        if (GuideRowFor(slot) is (Border guideRow, bool topEdge)) SetGuideLine(guideRow, topEdge);
        else ClearGuideLine();
        StartSettle();
    }

    /// <summary>Which row carries the guide edge for a slot, and on which side.</summary>
    private (Border Row, bool Top)? GuideRowFor(int slot)
    {
        if (slot < 0 || slot >= _slots.Count || slot == _sourceOrdinal) return null;
        if (slot > _sourceOrdinal)
            return slot + 1 < _slots.Count ? (_slots[slot + 1].Row, true) : (_slots[slot].Row, false);
        return (_slots[slot].Row, true);
    }

    private void SetGuideLine(Border row, bool topEdge)
    {
        var outline = OutlineOf(row);
        if (outline is null) { ClearGuideLine(); return; }
        if (!ReferenceEquals(outline, _guideOutline))
        {
            ClearGuideLine();
            _guideOutline = outline;
            _guideThickness = outline.BorderThickness;
            _guideBrush = outline.BorderBrush;
        }
        outline.BorderThickness = topEdge ? new Thickness(0, 3, 0, 0) : new Thickness(0, 0, 0, 3);
        outline.BorderBrush = AccentBrushOf(row) ?? Brushes.Transparent;
    }

    private void ClearGuideLine()
    {
        if (_guideOutline is null) return;
        _guideOutline.BorderThickness = _guideThickness;
        _guideOutline.BorderBrush = _guideBrush;
        _guideOutline = null;
    }

    private static Border? OutlineOf(Border row) =>
        row.GetVisualDescendants().OfType<Border>().FirstOrDefault(child => child.Name == "BlockRowOutline");

    private static IBrush? SurfaceBrushOf(Control anchor)
    {
        foreach (var key in new[] { "CuiPanel2Brush", "CuiPanelBrush" })
            if (anchor.TryFindResource(key, out var found) && found is IBrush brush) return brush;
        return null;
    }

    private static IBrush? AccentBrushOf(Control anchor) =>
        anchor.TryFindResource("CuiAccentBrush", out var found) && found is IBrush brush ? brush : null;


    /// <summary>Displacement of one sibling row when the block's final ordinal is slot: rows the block
    /// passes over move the other way, by exactly the space the block leaves behind.</summary>
    private double ShiftFor(Border row, int slot)
    {
        var own = OrdinalOf(row);
        if (own < 0 || own == _sourceOrdinal) return 0;
        var step = StepOf(_liftRow!);
        if (slot > _sourceOrdinal && own > _sourceOrdinal && own <= slot) return -step;
        if (slot < _sourceOrdinal && own >= slot && own < _sourceOrdinal) return step;
        return 0;
    }

    private void StartSettle()
    {
        if (_settleTimer is not null) return;
        _settleFrames = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Render)
        { Interval = TimeSpan.FromMilliseconds(SettleIntervalMs) };
        timer.Tick += (_, _) => SettleTick();
        _settleTimer = timer;
        timer.Start();
    }

    private void SettleTick()
    {
        var moving = false;
        if (!_lifted && _flipRow is null) { StopSettle(); return; }
        if (_flipRow is not null)
        {
            var remaining = 0 - _flipCurrent;
            if (Math.Abs(remaining) <= SettleEpsilon)
            {
                if (ReferenceEquals(_flipRow.RenderTransform, _flipTransform)) _flipRow.RenderTransform = null;
                _flipRow = null;
                _flipTransform = null;
                _flipCurrent = 0;
            }
            else
            {
                moving = true;
                _flipCurrent += remaining * SettleFraction;
                if (_flipTransform is not null) _flipTransform.Y = _flipCurrent;
            }
        }
        foreach (var entry in _offsets.Values)
        {
            // While the gesture is live the lifted row tracks the pointer directly; only its
            // neighbours settle.
            if (_dragging && ReferenceEquals(entry.Row, _liftRow)) continue;
            var difference = entry.Target - entry.Current;
            if (Math.Abs(difference) <= SettleEpsilon)
            {
                if (entry.Current != entry.Target)
                {
                    entry.Current = entry.Target;
                    entry.Transform.Y = entry.Current;
                }
                continue;
            }
            moving = true;
            entry.Current += difference * SettleFraction;
            entry.Transform.Y = entry.Current;
        }
        _settleFrames++;
        if (moving && _settleFrames <= MaxSettleFrames) return;
        var completion = _settle;
        _settle = null;
        StopSettle();
        completion?.TrySetResult();
    }

    private Task SettleAsync()
    {
        if (!_lifted || _offsets.Values.All(entry => Math.Abs(entry.Target - entry.Current) <= SettleEpsilon))
            return Task.CompletedTask;
        _settle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _settleFrames = 0; // a new wait gets its own budget even while a glide is already running
        StartSettle();
        return _settle.Task;
    }

    /// <summary>Top edge the lifted row must reach so that it fills the gap that was opened at slot.</summary>
    private double SlotTopFor(int slot)
    {
        if (_liftRow is null || slot == _sourceOrdinal || slot < 0 || slot >= _slots.Count)
            return _slots[_sourceOrdinal].Top;
        var step = StepOf(_liftRow);
        if (slot > _sourceOrdinal)
            return slot + 1 < _slots.Count
                ? _slots[slot + 1].Top - step
                : _slots[slot].Top - step + _slots[slot].Step;
        return _slots[slot].Top;
    }

    /// <summary>Turns the destination index from the model/pointer rule into the ordinal position the
    /// block will occupy inside this candidate row set.</summary>
    private int SlotOrdinalFor(int destination)
    {
        if (_slots.Count == 0) return -1;
        if (_draggedItemRow) return Math.Clamp(destination, 0, _slots.Count - 1);
        for (var ordinal = 0; ordinal < _slots.Count; ordinal++)
            if (_slots[ordinal].Row.Tag is BlockReorderRow info && info.Index == destination)
                return ordinal;
        return _slots.Count - 1;
    }

    private int OrdinalOf(Border row)
    {
        for (var ordinal = 0; ordinal < _slots.Count; ordinal++)
            if (ReferenceEquals(_slots[ordinal].Row, row)) return ordinal;
        return -1;
    }

    private IEnumerable<Border> CandidateRows()
    {
        var prefix = PrefixOf(_blockId);
        return _host.Children.OfType<Border>()
            .Where(row => row.Tag is BlockReorderRow info &&
                (prefix is null || info.BlockId.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(row => (row.Tag as BlockReorderRow)!.Index)
            .ToArray();
    }

    private static string? PrefixOf(string blockId)
    {
        var split = blockId.IndexOf(':');
        return split > 0 ? blockId[..split] + ":" : null;
    }

    private static double StepOf(Control row) => row.Bounds.Height + row.Margin.Top + row.Margin.Bottom;

    private void RestoreLift()
    {
        StopSettle();
        _settle = null;
        ClearGuideLine();
        if (_liftRow is not null)
        {
            _liftRow.Background = _savedBackground;
            _liftRow.BoxShadow = _savedShadow;
            _liftRow.ZIndex = _savedZIndex;
            if (ReferenceEquals(_liftRow.RenderTransform, _liftTransform)) _liftRow.RenderTransform = null;
        }
        foreach (var entry in _offsets.Values)
        {
            if (ReferenceEquals(entry.Row, _liftRow) || !ReferenceEquals(entry.Row.RenderTransform, entry.Transform))
                continue;
            entry.Row.RenderTransform = entry.Initial;
        }
        _offsets.Clear();
        _slots.Clear();
        _liftRow = null;
        _liftTransform = null;
        _lifted = false;
        _draggedItemRow = false;
    }

    private void RestoreLiftSafely()
    {
        try { RestoreLift(); }
        catch { /* drag visuals are best-effort; never fault a pointer or timer callback */ }
    }

    private void ReportStatusSafely(string message)
    {
        try { _viewModel.Set("StatusText", message); }
        catch { /* An error reporter cannot throw back into a pointer event dispatch. */ }
    }

    private void CancelSafely()
    {
        try { Cancel(); }
        catch { /* Restore drag visuals best-effort; never escape a pointer-release callback. */ }
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

    /// <summary>Ends the gesture - flags, pointer capture, tooltip - without disturbing the lift, so a
    /// released drag can still finish its slide into the slot.</summary>
    private void EndGesture()
    {
        var pointer = _pointer;
        StopAutoScroll();
        _pointer = null;
        _pressed = false;
        _dragging = false;
        _target = null;
        if (_handle is not null) ToolTip.SetTip(_handle, "Open block actions");
        // CaptureLost can be raised after another control has taken ownership.
        // Never release that new owner's capture while cancelling this gesture.
        if (pointer is not null && ReferenceEquals(pointer.Captured, _handle))
            pointer.Capture(null);
    }

    private void Cancel()
    {
        EndGesture();
        RestoreLift();
    }

    private void UpdateAutoScroll(PointerEventArgs e)
    {
        if (_scrollViewer is null) return;
        var pointerY = e.GetPosition(_scrollViewer).Y;
        const double edgeSize = 40;
        _scrollDirection = pointerY < edgeSize ? -1 :
            pointerY > _scrollViewer.Viewport.Height - edgeSize ? 1 : 0;
        if (_scrollDirection == 0)
        {
            StopAutoScroll();
            return;
        }
        _autoScrollTimer ??= CreateAutoScrollTimer();
        if (!_autoScrollTimer.IsEnabled) _autoScrollTimer.Start();
    }

    private DispatcherTimer CreateAutoScrollTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
        timer.Tick += (_, _) =>
        {
            if (!_dragging || _scrollViewer is null || _scrollDirection == 0) { StopAutoScroll(); return; }
            var oldOffset = _scrollViewer.Offset;
            var maximum = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
            var nextY = Math.Clamp(oldOffset.Y + _scrollDirection * 18, 0, maximum);
            if (nextY == oldOffset.Y) { StopAutoScroll(); return; }
            _scrollViewer.Offset = new Vector(oldOffset.X, nextY);
            // The host moves under the pointer, so the pointer-derived offsets must shift by the
            // same amount or the block and its slot drift apart while scrolling.
            _start = _start.WithY(_start.Y + nextY - oldOffset.Y);
            _lastHostY += nextY - oldOffset.Y;
            for (var index = 0; index < _slots.Count; index++)
                _slots[index] = _slots[index] with { Top = _slots[index].Top - (nextY - oldOffset.Y) };
            FollowPointer(_lastHostY);
            OpenSlot(GetDestination(_lastHostY));
        };
        return timer;
    }

    private void StopAutoScroll()
    {
        _scrollDirection = 0;
        _autoScrollTimer?.Stop();
    }

    private void StopSettle()
    {
        _settleTimer?.Stop();
        _settleTimer = null;
    }

    private int? GetDestination(double y) => DestinationForPointer(_host, _blockId,
        _draggedItemRow ? _target?.ViewIndex ?? 0 : _sourceIndex, y);

    internal static int? DestinationForPointer(StackPanel host, string sourceId, int sourceIndex, double y)
    {
        var split = sourceId.IndexOf(':');
        var prefix = split > 0 ? sourceId[..split] + ":" : null;
        var rows = host.Children.OfType<Border>()
            .Select(row => (Row: row, Info: row.Tag as BlockReorderRow))
            .Where(item => item.Info is not null && (prefix is null || item.Info.BlockId.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(item => item.Info!.Index).ToArray();
        if (rows.Length == 0) return null;
        for (var position = 0; position < rows.Length; position++)
        {
            var origin = rows[position].Row.TranslatePoint(new Point(0, 0), host);
            if (origin is null) continue;
            var index = prefix is null ? rows[position].Info!.Index : position;
            if (y < origin.Value.Y + rows[position].Row.Bounds.Height / 2)
                return index > sourceIndex ? index - 1 : index;
        }
        var slot = prefix is null ? rows[^1].Info!.Index + 1 : rows.Length;
        return slot > sourceIndex ? slot - 1 : slot;
    }
}
