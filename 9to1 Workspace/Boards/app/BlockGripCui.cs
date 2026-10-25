using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;

namespace CakeOS.Apps.Boards.App;

/// <summary>Authored CUI block grip, routed to the existing revision-checked typed actions.</summary>
internal static class BlockGripCui
{
    public static Control Build(BoardsViewModel viewModel, string blockId)
    {
        var context = new BlockGripContext(viewModel, blockId);
        var loader = new CuiControlLoader();
        loader.SetBindingContext(context);
        loader.SetActionDispatcher(context);
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BlockGripCui.cui");
        if (!File.Exists(path))
        {
            loader.Dispose();
            return Unavailable("The authored block actions are unavailable.");
        }
        var (root, diagnostics) = loader.LoadFile(path);
        if (root is null)
        {
            loader.Dispose();
            return Unavailable(diagnostics.FirstOrDefault()?.Message ?? "The authored block actions are unavailable.");
        }
        loader.WireBindings(root);
        root.DetachedFromVisualTree += (_, _) => loader.Dispose();
        return root;
    }

    private static Control Unavailable(string message)
    {
        var text = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        AutomationProperties.SetName(text, "Block actions unavailable");
        return text;
    }

    private sealed class BlockGripContext : ICuiWritableBindingContext, ICuiActionDispatcher
    {
        private readonly BoardsViewModel _viewModel;
        private readonly string _pageId;
        private readonly string _blockId;
        private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);
        private readonly BoardBlockActionTarget? _target;
        private bool _isExpanded;

        public BlockGripContext(BoardsViewModel viewModel, string blockId)
        {
            _viewModel = viewModel;
            _pageId = viewModel.SelectedPageId;
            _blockId = blockId;
            var blocks = viewModel.PageBlocks;
            var index = -1;
            for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
                if (blocks[blockIndex].Id == blockId) { index = blockIndex; break; }
            var eligible = index >= 0 && !blockId.Contains(':') &&
                blocks[index].Kind is "paragraph" or "heading" or "table" or "image" or "divider" or "graph";
            var target = eligible && viewModel.Session is ContractSessionAdapter adapter
                ? adapter.CaptureBlockAction(_pageId, _blockId)
                : null;
            var canManage = viewModel.IsEditMode && target is not null;
            _target = target;
            _values["CanManage"] = canManage;
            _values["CanMoveUp"] = canManage && index > 0;
            _values["CanMoveDown"] = canManage && index < blocks.Count - 1;
            _values["GripAccessibleName"] = "Open block actions for " + (eligible ? blocks[index].Kind : "block");
            if (target is not null)
            {
                _values["MoveUpRequest"] = new BoardBlockActionRequest(target, BoardBlockAction.MoveUp);
                _values["MoveDownRequest"] = new BoardBlockActionRequest(target, BoardBlockAction.MoveDown);
                _values["DuplicateRequest"] = new BoardBlockActionRequest(target, BoardBlockAction.Duplicate);
                _values["DeleteRequest"] = new BoardBlockActionRequest(target, BoardBlockAction.Delete);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool TryGetValue(string path, out object? value)
        {
            var key = path[(path.LastIndexOf('.') + 1)..];
            if (key.Equals("IsExpanded", StringComparison.OrdinalIgnoreCase))
            {
                value = _isExpanded;
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
                _isExpanded = !_isExpanded;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsExpanded"));
                return;
            }
            if (command != "ApplyBlockAction" || parameter is not BoardBlockActionRequest request || _target is null ||
                request.Target != _target || request.Target.BlockId != _blockId ||
                !Enum.IsDefined(request.Action)) return;
            _viewModel.FocusBlock(_blockId);
            await _viewModel.DispatchCuiBlockActionAsync(request, cancellationToken);
            _isExpanded = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsExpanded"));
        }
    }
}
