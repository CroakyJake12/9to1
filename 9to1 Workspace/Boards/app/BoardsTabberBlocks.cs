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
    private readonly Dictionary<string, ControlLifetime> _controls = [];
    private readonly object _ownershipGate = new();
    private readonly List<Task> _retirements = [];
    private Task? _disposeTask;
    private bool _disposed;
    private readonly Dictionary<string, string> _viewSelection = [];
    public bool HasUnsavedDrafts
    { get { lock (_ownershipGate) return _editors.Values.Any(editor => editor.Model.HasUnsettledDatabaseWork); } }

    private sealed class ControlLifetime(Func<bool> validate)
    {
        private int _retired;
        internal bool IsCurrent()
        {
            if (Volatile.Read(ref _retired) != 0) return false;
            if (validate()) return true;
            Retire(); return false;
        }
        internal void Retire() => Interlocked.Exchange(ref _retired, 1);
    }

    // Projection publication can run under the adapter gate off UI. Revoke only here;
    // UI detachment and asynchronous drains happen separately, never blocking that gate.
    internal void ValidateOwners()
    {
        lock (_ownershipGate)
        {
            foreach (var controls in _controls.Values) _ = controls.IsCurrent();
            foreach (var editor in _editors.Values) _ = editor.Model.PaneLifetimeIsCurrent;
        }
    }
    internal void Reconcile()
    {
        lock (_ownershipGate)
        {
            if (_disposed) return;
            var missing = _editors.Where(pair => !pair.Value.Model.PaneLifetimeIsCurrent).ToArray();
            foreach (var pair in missing)
            {
                _editors.Remove(pair.Key);
                _retirements.Add(pair.Value.DisposeAsync().AsTask());
            }
        }
    }

    public Control Mount(RichBoardBlock block)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Reconcile();
        var lifetime = new ControlLifetime(owner.CaptureTabberControls(block));
        lock (_ownershipGate)
        {
            if (_controls.Remove(block.Id, out var previous)) previous.Retire();
            _controls.Add(block.Id, lifetime);
        }
        var tabber = block.Tabber ?? throw new InvalidDataException("Tabber metadata is missing.");
        var panel = new StackPanel { Spacing = 8 };
        var selectedID = !owner.IsEditMode && _viewSelection.TryGetValue(block.Id, out var viewID)
            && tabber.Panes.Any(pane => pane.Id == viewID) ? viewID : tabber.SelectedPaneId;
        var selected = tabber.Panes.Single(pane => pane.Id == selectedID);
        var name = new TextBox { Text = selected.Title, Width = 180, IsReadOnly = !owner.IsEditMode };
        var actions = new HashSet<Button>();
        Task Apply(string action, string? paneId = null) => !lifetime.IsCurrent() ? Task.CompletedTask :
            owner.ChangeTabberAsync(block.Id, action, paneId, controlsCurrent: lifetime.IsCurrent,
                pendingPaneTitle: action != "remove" && name.Text != selected.Title ? name.Text : null);
        var tabs = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var pane in tabber.Panes)
        {
            var button = new Button { Content = pane.Title,
                Margin = new Avalonia.Thickness(0, 0, 6, 0) };
            Avalonia.Automation.AutomationProperties.SetName(button, "Open tab " + pane.Title);
            button.Click += async (_, _) =>
            {
                if (!lifetime.IsCurrent()) return;
                if (owner.IsEditMode) await Apply("select", pane.Id);
                else { _viewSelection[block.Id] = pane.Id; owner.RefreshAfterEdit(); }
            };
            actions.Add(button); tabs.Children.Add(button);
        }
        var add = new Button { Content = "+", IsEnabled = owner.IsEditMode };
        Avalonia.Automation.AutomationProperties.SetName(add, "Add tab");
        add.Click += async (_, _) => await Apply("add");
        actions.Add(add);
        tabs.Children.Add(add); panel.Children.Add(tabs);
        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        Avalonia.Automation.AutomationProperties.SetName(name, "Tab name");
        async Task Rename() { if (lifetime.IsCurrent() && name.Text != selected.Title) await owner.ChangeTabberAsync(block.Id, "rename", selected.Id, name.Text, lifetime.IsCurrent); }
        name.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await Rename(); } };
        // Moving focus to a tab action must not rebuild its controls before Click.
        // That action commits the pending name and its own change together. Leaving
        // the action group without activating it still commits the name on blur.
        panel.LostFocus += async (_, args) =>
        { if (args.NewFocusedElement is not Button button || !actions.Contains(button)) await Rename(); };
        var remove = new Button { Content = "Remove tab", IsEnabled = owner.IsEditMode && tabber.Panes.Count > 1 };
        remove.Click += async (_, _) => await Apply("remove", selected.Id);
        actions.Add(remove);
        options.Children.Add(name); options.Children.Add(remove); panel.Children.Add(options);
        PaneEditor editor;
        lock (_ownershipGate)
        {
            if (!_editors.TryGetValue(selected.PageId, out editor!))
            {
                editor = new PaneEditor(owner.CreatePaneModel(selected.PageId));
                _editors.Add(selected.PageId, editor);
            }
        }
        editor.Model.SyncPaneMode(owner.IsEditMode);
        if (editor.Root.Parent is Panel old) old.Children.Remove(editor.Root);
        panel.Children.Add(editor.Root);
        editor.Queue();
        return panel;
    }

    public async Task<IBoardsPresentationClosePreparation?> PrepareAsync(CancellationToken token)
    {
        Task[] retirements;
        PaneEditor[] editors;
        lock (_ownershipGate) { editors = _editors.Values.ToArray(); retirements = _retirements.ToArray(); }
        await Task.WhenAll(retirements);
        var preparations = new List<IBoardsPresentationClosePreparation>();
        foreach (var editor in editors)
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
    public ValueTask DisposeAsync()
    { lock (_ownershipGate) return new(_disposeTask ??= DisposeCoreAsync()); }
    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        Task[] retirements;
        lock (_ownershipGate)
        {
            foreach (var controls in _controls.Values) controls.Retire();
            _controls.Clear();
            foreach (var editor in _editors.Values) _retirements.Add(editor.DisposeAsync().AsTask());
            _editors.Clear(); retirements = _retirements.ToArray();
        }
        await Task.WhenAll(retirements);
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
        private volatile bool _disposed;
        private readonly object _disposeGate = new();
        private Task? _disposeTask;
        public PaneEditor(BoardsViewModel model)
        {
            Model = model;
            Model.BindThemeSurface(Root);
            var viewport = new Grid();
            viewport.Children.Add(_blocks); viewport.Children.Add(_absolute); viewport.Children.Add(_ink);
            Root.Children.Add(_toolbar); Root.Children.Add(viewport);
            _inkOwner = new(_ink, model);
            Model.RebuildRequested += Queue;
            Model.SelectionChanged += RefreshToolbar;
        }
        private void RefreshToolbar()
        {
            if (_disposed) return;
            using var themeScope = BoardsTheme.UseSurface(Root);
            ToolbarBuilder.Rebuild(_toolbar, Model);
        }
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
                    using var themeScope = BoardsTheme.UseSurface(Root);
                    BlockRenderer.Rebuild(_blocks, Model, boxes, _absolute);
                    RefreshToolbar();
                });
                await _inkOwner.RenderAsync();
            }
            catch (Exception error) { Model.Set("StatusText", "Tab content could not be rendered: " + error.Message); }
            finally { _gate.Release(); }
        }
        public ValueTask DisposeAsync()
        {
            lock (_disposeGate) return new(_disposeTask ??= DisposeCoreAsync());
        }
        private async Task DisposeCoreAsync()
        {
            _disposed = true;
            Model.RetirePane();
            var renders = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Root.IsEnabled = false;
                Model.RebuildRequested -= Queue; Model.SelectionChanged -= RefreshToolbar;
                Model.Dispose(); _inkOwner.Dispose();
                return _renders.ToArray();
            });
            await Task.WhenAll(renders);
            await _inkOwner.DrainAsync();
            await Model.DisposeDatabasesAsync();
            _gate.Dispose();
        }
    }
}
