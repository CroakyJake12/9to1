using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.App;

/// <summary>Pane-scoped view models borrow one canonical Board session and the existing block renderer.</summary>
public sealed class BoardsTabberBlocks(BoardsViewModel owner) : IAsyncDisposable
{
    private readonly Dictionary<string, PaneEditor> _editors = [];
    private bool _disposed;
    private readonly Dictionary<string, string> _viewSelection = [];
    public bool HasUnsavedDrafts => _editors.Values.Any(editor => editor.Model.DatabaseBlocks.HasUnsavedDrafts);

    public Control Mount(RichBoardBlock block)
    {
        var tabber = block.Tabber ?? throw new InvalidDataException("Tabber metadata is missing.");
        var panel = new StackPanel { Spacing = 8 };
        var tabs = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var pane in tabber.Panes)
        {
            var button = new Button { Content = pane.Title,
                Margin = new Avalonia.Thickness(0, 0, 6, 0) };
            Avalonia.Automation.AutomationProperties.SetName(button, "Open tab " + pane.Title);
            button.Click += async (_, _) =>
            {
                if (owner.IsEditMode) await owner.ChangeTabberAsync(block.Id, "select", pane.Id);
                else { _viewSelection[block.Id] = pane.Id; owner.RefreshAfterEdit(); }
            };
            tabs.Children.Add(button);
        }
        var add = new Button { Content = "+", IsEnabled = owner.IsEditMode };
        Avalonia.Automation.AutomationProperties.SetName(add, "Add tab");
        add.Click += async (_, _) => await owner.ChangeTabberAsync(block.Id, "add");
        tabs.Children.Add(add); panel.Children.Add(tabs);
        var selectedID = !owner.IsEditMode && _viewSelection.TryGetValue(block.Id, out var viewID)
            && tabber.Panes.Any(pane => pane.Id == viewID) ? viewID : tabber.SelectedPaneId;
        var selected = tabber.Panes.Single(pane => pane.Id == selectedID);
        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var name = new TextBox { Text = selected.Title, Width = 180, IsReadOnly = !owner.IsEditMode };
        Avalonia.Automation.AutomationProperties.SetName(name, "Tab name");
        async Task Rename() { if (name.Text != selected.Title) await owner.ChangeTabberAsync(block.Id, "rename", selected.Id, name.Text); }
        name.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await Rename(); } };
        name.LostFocus += async (_, _) => await Rename();
        var remove = new Button { Content = "Remove tab", IsEnabled = owner.IsEditMode && tabber.Panes.Count > 1 };
        remove.Click += async (_, _) => await owner.ChangeTabberAsync(block.Id, "remove", selected.Id);
        options.Children.Add(name); options.Children.Add(remove); panel.Children.Add(options);
        if (!_editors.TryGetValue(selected.PageId, out var editor))
        {
            editor = new PaneEditor(owner.CreatePaneModel(selected.PageId));
            _editors.Add(selected.PageId, editor);
        }
        editor.Model.SyncPaneMode(owner.IsEditMode);
        if (editor.Root.Parent is Panel old) old.Children.Remove(editor.Root);
        panel.Children.Add(editor.Root);
        editor.Queue();
        return panel;
    }

    public async Task<IBoardsPresentationClosePreparation?> PrepareAsync(CancellationToken token)
    {
        var preparations = new List<IBoardsPresentationClosePreparation>();
        foreach (var editor in _editors.Values)
        {
            await editor.WhenIdleAsync();
            var preparation = await editor.Model.PrepareDatabaseCloseAsync(token);
            if (preparation is null)
            {
                foreach (var accepted in preparations) await accepted.AbortAsync();
                return null;
            }
            preparations.Add(preparation);
        }
        return new PanePreparation(preparations);
    }
    private sealed class PanePreparation(IReadOnlyList<IBoardsPresentationClosePreparation> preparations) : IBoardsPresentationClosePreparation
    {
        public bool IsCurrent => preparations.All(item => item.IsCurrent);
        public async Task CommitAsync(CancellationToken token = default) { foreach (var item in preparations) await item.CommitAsync(token); }
        public async Task AbortAsync() { foreach (var item in preparations) await item.AbortAsync(); }
        public async ValueTask DisposeAsync() { foreach (var item in preparations) await item.DisposeAsync(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        foreach (var editor in _editors.Values) await editor.DisposeAsync();
        _editors.Clear(); _disposed = true;
    }

    private sealed class PaneEditor : IAsyncDisposable
    {
        public BoardsViewModel Model { get; }
        public StackPanel Root { get; } = new() { Spacing = 8 };
        private readonly WrapPanel _toolbar = new();
        private readonly StackPanel _blocks = new();
        private readonly Canvas _absolute = new();
        private readonly Canvas _ink = new();
        private readonly BoardInkInteractionController _inkOwner;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly List<Task> _renders = [];
        private bool _disposed;
        public PaneEditor(BoardsViewModel model)
        {
            Model = model;
            var viewport = new Grid();
            viewport.Children.Add(_blocks); viewport.Children.Add(_absolute); viewport.Children.Add(_ink);
            Root.Children.Add(_toolbar); Root.Children.Add(viewport);
            _inkOwner = new(_ink, model);
            Model.RebuildRequested += Queue;
            Model.SelectionChanged += RefreshToolbar;
        }
        private void RefreshToolbar() { if (!_disposed) ToolbarBuilder.Rebuild(_toolbar, Model); }
        public void Queue()
        {
            if (_disposed) return;
            _renders.RemoveAll(task => task.IsCompleted);
            _renders.Add(RenderAsync());
        }
        public Task WhenIdleAsync() => Task.WhenAll(_renders.ToArray());
        private async Task RenderAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (_disposed) return;
                var boxes = await Model.GetCanvasObjectsAsync();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_disposed) return;
                    BlockRenderer.Rebuild(_blocks, Model, boxes, _absolute);
                    RefreshToolbar();
                });
                await _inkOwner.RenderAsync();
            }
            catch (Exception error) { Model.Set("StatusText", "Tab content could not be rendered: " + error.Message); }
            finally { _gate.Release(); }
        }
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await Task.WhenAll(_renders.ToArray());
            Model.RebuildRequested -= Queue; Model.SelectionChanged -= RefreshToolbar;
            _inkOwner.Dispose(); await _inkOwner.DrainAsync();
            await Model.DisposeDatabasesAsync();
            Model.Dispose(); _gate.Dispose();
        }
    }
}
