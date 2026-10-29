using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace CakeOS.Apps.Boards.App;

/// <summary>Authored CUI block grip, routed to the existing revision-checked typed actions.</summary>
internal static class BlockGripCui
{
    public static Control Build(BoardsViewModel viewModel, string blockId)
    {
        var context = new BlockGripContext(viewModel, blockId);
        var loader = BoardsCuiDocuments.CreateLoader();
        loader.SetBindingContext(context);
        loader.SetActionDispatcher(context);
        (Control? Root, IReadOnlyList<CuiDiagnostic> Diagnostics) loaded;
        try
        {
            loaded = loader.TryLoad(BoardsCuiDocuments.Load(BoardsCuiDocuments.BlockGripSourceName));
        }
        catch (Exception error)
        {
            loader.Dispose();
            return Unavailable(error.Message);
        }
        var (root, diagnostics) = loaded;
        if (root is null)
        {
            loader.Dispose();
            return Unavailable(diagnostics.FirstOrDefault()?.Message ?? "The authored block actions are unavailable.");
        }
        loader.WireBindings(root);
        root.DetachedFromVisualTree += (_, _) => loader.Dispose();

        // The menu itself is authored in CUI (BlockActionsMenuCui.cui) but hosted by a C#-owned Popup,
        // exactly as BoardProperties hosts its authored content in a Window (Program.cs:694) and the
        // insert menu hosts its list in a Popup (ToolbarBuilder.cs:409). This boundary is forced by
        // geometry, not preference: the gutter gives the grip a 32-unit grid column, so a 232-unit menu
        // cannot live in the row's own subtree without being clipped or pushing the text column.
        // The alternative - a native CUI `Container Type="Menu"` - was examined and rejected for this
        // surface because its own press handling conflicts with the grip's drag-to-move gesture that the
        // block rows already route (BoardsBlockActionsTests.Actual_grip_pointer_routes...).
        if (root.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Name == "BlockGripToggle")
            is not Button trigger)
            return root;
        var menuLoader = BoardsCuiDocuments.CreateLoader();
        menuLoader.SetBindingContext(context);
        menuLoader.SetActionDispatcher(context);
        Control? menu;
        try
        {
            var (menuRoot, menuDiagnostics) = menuLoader.TryLoad(
                BoardsCuiDocuments.Load(BoardsCuiDocuments.BlockActionsMenuSourceName));
            if (menuRoot is null)
            {
                menuLoader.Dispose();
                return Unavailable(menuDiagnostics.FirstOrDefault()?.Message
                    ?? "The authored block actions menu is unavailable.");
            }
            menuLoader.WireBindings(menuRoot);
            menu = menuRoot;
        }
        catch (Exception error)
        {
            menuLoader.Dispose();
            return Unavailable(error.Message);
        }

        // The floating surface. Parented to the trigger at construction, the way Haven's own add-menu
        // popup is asserted (ChatAddMenuNativePopupTests.cs:60): that is what makes Avalonia host the
        // surface in the owner window's overlay layer instead of an unreachable native popup window.
        // Measured 29 October: deferring SetParent to AttachedToVisualTree left the menu unreachable
        // from the board window. Popup.Parent is read-only, so the typed ISetLogicalParent route is
        // required, and a manually owned Popup needs the trigger as its logical parent or the floating
        // surface loses the Boards theme resources.
        var popup = new Popup
        {
            PlacementTarget = trigger,
            Placement = PlacementMode.Bottom,
            IsLightDismissEnabled = true,
            Child = menu,
        };
        var host = new BlockActionsHost(popup, menu);
        popup.Opened += (_, _) => host.OpenedCount++;
        popup.Closed += (_, _) => host.ClosedCount++;
        ((ISetLogicalParent)popup).SetParent(trigger);
        trigger.DetachedFromVisualTree += (_, _) =>
        {
            popup.Close();
            ((ISetLogicalParent)popup).SetParent(null);
            menuLoader.Dispose();
        };
        CuiPopupPresentation.RequestTransparency(popup);
        context.Host = host;

        // The handle is published through a side table keyed on the grip's own controls, NOT
        // through any Tag. Measured 29 October: every Tag slot belongs to somebody else - BlockRenderer
        // tags the row Border and the grip root with BlockReorderRow, the CUI loader assigns Button.Tag
        // while wiring (CuiControlLoader.cs:3586) and assigns its own reference to the menu document
        // root (CuiControlLoader.cs:3808) - so a Tag-based handle was silently overwritten and every
        // menu test went red at once while the code that set it looked correct. A weak side table keyed
        // on the trigger and the menu cannot be clobbered by those writers and does not keep the
        // surface alive after the row is rebuilt.
        RegisterHost(trigger, menu, host);

        // Open and close follow the authored state, and every stage is counted so a stuck menu names the
        // broken link (press, click, authored command received, open attempted, actually opened, closed)
        // instead of asserting on a bare null. The secondary click path is a measured accommodation, not
        // a preference: the authored ToggleActions command was observed NOT arriving at this context for
        // the gutter trigger on 29 October, while the same authored path works for the inline menu.
        // It still routes through the single state owner so a second open cannot double-fire.
        context.ExpansionChanged = expanded =>
        {
            if (expanded)
            {
                host.OpenAttempts++;
                if (!popup.IsOpen) popup.Open();
                if (!popup.IsOpen)
                {
                    host.OpenAttempts++;
                    trigger.Focus();
                    popup.IsOpen = true;
                }
            }
            else
            {
                popup.Close();
                menuLoader.WhenActionsIdleAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            }
        };
        trigger.PointerPressed += (_, _) => host.PressCount++;
        trigger.Click += (_, _) =>
        {
            host.ClickCount++;
            if (context.IsExpanded) return;
            host.FallbackClickOpens++;
            context.ForceExpandedForDiagnostics(true);
        };
        return root;
    }

    /// <summary>The authored menu surface plus the C#-owned Popup that floats it, reachable from the
    /// menu control's Tag. The menu is not in the block row's subtree, so anything looking for its rows
    /// must go through this handle (BoardsBlockActionsTests does exactly that). The counters record the
    /// open chain: presses, clicks, authored commands received, open attempts, actual opens, closes, and
    /// how often the secondary click path had to present the menu.</summary>
    private static readonly ConditionalWeakTable<Control, BlockActionsHost> Hosts = new();

    /// <summary>Attaches the surface handle to both controls the grip owns, so a caller holding either
    /// the gutter trigger or the menu recovers the surface without depending on a Tag slot.</summary>
    private static void RegisterHost(Control trigger, Control menu, BlockActionsHost host)
    {
        Hosts.AddOrUpdate(trigger, host);
        Hosts.AddOrUpdate(menu, host);
    }

    /// <summary>Finds the block-actions surface a grip control belongs to, or null when the control is
    /// the fallback (unavailable) surface rather than a real authored grip.</summary>
    public static BlockActionsHost? HostFor(Control control) =>
        Hosts.TryGetValue(control, out var host) ? host : null;

    internal sealed record BlockActionsHost(Popup Popup, Control Menu)
    {
        public int PressCount { get; internal set; }
        public int ClickCount { get; internal set; }
        public int ToggleCommandsReceived { get; internal set; }
        public int OpenAttempts { get; internal set; }
        public int OpenedCount { get; internal set; }
        public int ClosedCount { get; internal set; }
        public int FallbackClickOpens { get; internal set; }
    }

    private static Control Unavailable(string message)
    {
        var text = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        AutomationProperties.SetName(text, "Block actions unavailable");
        return text;
    }

    // INotifyPropertyChanged is load-bearing here, not decorative: the CUI loader refreshes
    // authored bindings only when the binding context `is INotifyPropertyChanged`
    // (CuiControlLoader.cs:262). Declaring only the bare event left every binding in this
    // document frozen at its load-time value, so the grip's `Active="{Binding IsExpanded}"`
    // action row could never appear: the click reached ToggleActions and IsExpanded became
    // true, with no subscriber to act on it (measured 28 October).
    internal sealed class BlockGripContext : INotifyPropertyChanged, ICuiWritableBindingContext, ICuiActionDispatcher
    {
        private readonly BoardsViewModel _viewModel;
        private readonly string _pageId;
        private readonly string _blockId;
        private readonly IRichBoardSession _session;
        private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);
        private BoardBlockActionTarget? _target;
        private bool _isExpanded, _moveExpanded, _designExpanded;

        /// <summary>Reports the menu's open/closed state to the hosting C# surface. The authored
        /// document owns which rows exist and what they mean; only the Popup needs this signal.</summary>
        public Action<bool>? ExpansionChanged;

        /// <summary>Set by the hosting surface so a command that closes the menu (a typed action, a
        /// turn-into, or a light dismissal) can retire the floating Popup without owning it.</summary>
        public Action? CloseRequested;

        /// <summary>The surface this context drives, set by Build; used to count the open chain so
        /// a stuck menu can name the broken link instead of failing on a bare null.</summary>
        internal BlockActionsHost? Host;

        /// <summary>Single owner of the expansion state, so the authored command path and the
        /// secondary click path cannot diverge or double-open the surface.</summary>
        internal void ForceExpandedForDiagnostics(bool expanded)
        {
            if (expanded && !_isExpanded) RefreshTarget();
            _isExpanded = expanded;
            if (!expanded) _moveExpanded = _designExpanded = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsExpanded"));
            ExpansionChanged?.Invoke(expanded);
        }

        public bool IsExpanded => _isExpanded;

        public BlockGripContext(BoardsViewModel viewModel, string blockId)
        {
            _viewModel = viewModel;
            _pageId = viewModel.SelectedPageId;
            _blockId = blockId;
            _session = viewModel.Session;
            RefreshTarget();
        }

        private void RefreshTarget()
        {
            var viewModel = _viewModel;
            var blockId = _blockId;
            var blocks = viewModel.PageBlocks;
            var index = -1;
            for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
                if (blocks[blockIndex].Id == blockId) { index = blockIndex; break; }
            var eligible = index >= 0 &&
                blocks[index].Kind is "paragraph" or "heading" or "table" or "image" or "divider" or "graph" or
                    "checklist" or "bulleted" or "numbered" or "database" or "tabber";
            var target = eligible && viewModel.SelectedPageId == _pageId &&
                ReferenceEquals(_session, viewModel.Session) && viewModel.Session is ContractSessionAdapter adapter
                ? adapter.CaptureBlockAction(_pageId, _blockId)
                : null;
            var canManage = viewModel.IsEditMode && target is not null;
            _target = target;
            _values["CanManage"] = canManage;
            _values["CanMoveUp"] = canManage && target!.ViewIndex > 0;
            _values["CanMoveDown"] = canManage && target!.ViewIndex < target.ViewCount - 1;
            _values["GripAccessibleName"] = "Open block actions for " + (eligible ? blocks[index].Kind : "block");
            // The submenu rows carry their own expansion state so a screen reader can tell the owner
            // whether Move Block or Design is open, rather than hearing the same label twice.
            _values["MoveMenuDescription"] = _moveExpanded ? "Move block submenu expanded" : "Move block submenu collapsed";
            _values["DesignMenuDescription"] = _designExpanded ? "Design submenu expanded" : "Design submenu collapsed";
            if (target is not null)
            {
                _values["MoveUpRequest"] = new BoardBlockActionRequest(target, BoardBlockAction.MoveUp);
                _values["MoveDownRequest"] = new BoardBlockActionRequest(target, BoardBlockAction.MoveDown);
                _values["DuplicateRequest"] = new BoardBlockActionRequest(target, BoardBlockAction.Duplicate);
                _values["DeleteRequest"] = new BoardBlockActionRequest(target, BoardBlockAction.Delete);
            }
            else
            {
                foreach (var key in new[] { "MoveUpRequest", "MoveDownRequest", "DuplicateRequest", "DeleteRequest" })
                    _values[key] = null;
            }
            foreach (var key in _values.Keys)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool TryGetValue(string path, out object? value)
        {
            var key = path[(path.LastIndexOf('.') + 1)..];
            switch (key)
            {
                case "IsExpanded" when path.EndsWith("IsExpanded", StringComparison.OrdinalIgnoreCase):
                    value = _isExpanded;
                    return true;
                case "IsMoveExpanded":
                    value = _moveExpanded;
                    return true;
                case "IsDesignExpanded":
                    value = _designExpanded;
                    return true;
            }
            return _values.TryGetValue(key, out value);
        }

        public bool TrySetValue(string path, object? value) => false;

        public async ValueTask DispatchAsync(string command, object? parameter,
            CancellationToken cancellationToken = default)
        {
            if (command == "ToggleActions")
            {
                if (Host is not null) Host.ToggleCommandsReceived++;
                // Ordinary typing updates the existing editor without rebuilding its grip.
                // Capture the same block's current ticket only when opening the menu; an
                // already open menu retains its revision fence until it is closed.
                if (!_isExpanded) RefreshTarget(); else _moveExpanded = _designExpanded = false;
                _isExpanded = !_isExpanded;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsExpanded"));
                ExpansionChanged?.Invoke(_isExpanded);
                return;
            }
            // Submenu toggles open one group at a time, matching the mockup's single open panel.
            if (command == "ToggleMoveMenu" || command == "ToggleDesignMenu")
            {
                var move = command == "ToggleMoveMenu";
                if (move) _moveExpanded = !_moveExpanded; else _designExpanded = !_designExpanded;
                if (move) _designExpanded = false; else _moveExpanded = false;
                RefreshTarget();
                foreach (var name in new[] { "IsMoveExpanded", "IsDesignExpanded", "MoveMenuDescription", "DesignMenuDescription" })
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
                return;
            }
            // The Design submenu's "turn into" list re-types this block in place. The block identity
            // changes because a new block is built by the same factory the plus menu uses, so the grip is
            // rebuilt for the new row rather than pretending the old ticket survived.
            if (command.StartsWith("TurnInto:", StringComparison.OrdinalIgnoreCase))
            {
                if (_target is null || !_isExpanded) return;
                var kind = command["TurnInto:".Length..].Trim().ToLowerInvariant();
                if (kind.Length == 0) return;
                _isExpanded = _moveExpanded = _designExpanded = false;
                RefreshTarget();
                ExpansionChanged?.Invoke(false);
                await _viewModel.InsertKindRelativeToAsync(kind, _blockId, "replace");
                foreach (var name in new[] { "IsExpanded", "IsMoveExpanded", "IsDesignExpanded" })
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
                return;
            }
            if (command != "ApplyBlockAction" || parameter is not BoardBlockActionRequest request || _target is null ||
                request.Target != _target || request.Target.BlockId != _blockId ||
                !Enum.IsDefined(request.Action)) return;
            _viewModel.FocusBlock(_blockId);
            await _viewModel.DispatchCuiBlockActionAsync(request, cancellationToken);
            _isExpanded = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsExpanded"));
            ExpansionChanged?.Invoke(false);
        }
    }
}
