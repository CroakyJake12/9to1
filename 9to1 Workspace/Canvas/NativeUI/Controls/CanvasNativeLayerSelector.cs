using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;

namespace HavenOS.Apps.Canvas;

/// <summary>Native editor-local layer navigation. No canonical write or Home grant.
/// The owning host must supply its original current/access checks and retire this
/// control on replacement. This component never adopts a different document.</summary>
public sealed class CanvasNativeLayerSelector : UserControl, IDisposable
{
    private readonly CanvasRnoteDocument _original;
    private readonly Func<bool> _isCurrent;
    private readonly Func<CancellationToken, ValueTask<bool>> _validateOriginalAccess;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly StackPanel _rows = new();
    private readonly TextBlock _status = new();
    private readonly object _pendingGate = new();
    private readonly HashSet<Task> _pending = [];
    private long _generation;
    private bool _retired;

    public CanvasNativeLayerSelector(CanvasRnoteDocument original, Func<bool> isCurrent,
        Func<CancellationToken, ValueTask<bool>> validateOriginalAccess)
    {
        _original = original ?? throw new ArgumentNullException(nameof(original));
        _isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
        _validateOriginalAccess = validateOriginalAccess ?? throw new ArgumentNullException(nameof(validateOriginalAccess));
        Content = new StackPanel { Children = { _rows, _status } };
        Refresh();
    }

    public string Status => _status.Text ?? "";
    public Task WhenSelectionIdleAsync() { Dispatcher.UIThread.VerifyAccess(); lock (_pendingGate) return Task.WhenAll(_pending.ToArray()); }

    public void Refresh()
    {
        Dispatcher.UIThread.VerifyAccess();
        ++_generation;
        if (!Current()) return;
        string? unavailable;
        try { unavailable = _original.NativeUserLayerUnavailableReason; }
        catch (ObjectDisposedException) { Retire(); return; }
        _rows.Children.Clear();
        if (unavailable is not null) { _status.Text = unavailable; return; }
        var snapshot = _original.Snapshot;
        var page = snapshot.Pages.Single();
        var selected = _original.ActiveNativeUserLayerId;
        _rows.Children.Clear();
        var originalGeneration = _generation;
        foreach (var id in page.LayerOrder)
        {
            var layer = page.Layers.Single(value => value.LayerId == id);
            var button = new Button
            {
                Tag = id,
                Content = $"{(id == selected ? "Selected: " : "")}{layer.Name}{(!layer.IsVisible ? " · hidden" : "")}{(layer.IsLocked ? " · locked" : "")}",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };
            button.Click += (_, _) => ObserveSelection(page.PageId, id, snapshot.ArtifactId, snapshot.RevisionId, originalGeneration);
            _rows.Children.Add(button);
        }
        _status.Text = "Choose a layer. Selection is local to this editor.";
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if(e.Handled||!IsKeyboardFocusWithin||e.KeyModifiers!=KeyModifiers.None||e.Key is not (Key.Up or Key.Down)||!Current())return;
        try
        {
            if(_original.NativeUserLayerUnavailableReason is not null)return;
            var snapshot=_original.Snapshot;var page=snapshot.Pages.Single();
            var order=page.LayerOrder;var index=order.IndexOf(_original.ActiveNativeUserLayerId);
            if(index<0)return;
            var next=Math.Clamp(index+(e.Key==Key.Up?-1:1),0,order.Count-1);
            if(next==index)return;
            var generation=_generation;
            ObserveSelection(page.PageId,order[next],snapshot.ArtifactId,snapshot.RevisionId,generation,true);
            e.Handled=true;
        }
        catch(ObjectDisposedException){Retire();}
    }

    private void ObserveSelection(Guid pageId, Guid layerId, Guid artifactId, Guid revisionId, long generation, bool keyboardFocus=false)
    {
        var task = SelectAsync(pageId, layerId, artifactId, revisionId, generation, keyboardFocus);
        lock (_pendingGate) _pending.Add(task);
        _ = task.ContinueWith(completed => { lock (_pendingGate) _pending.Remove(completed); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task SelectAsync(Guid pageId, Guid layerId, Guid artifactId, Guid revisionId, long generation, bool keyboardFocus)
    {
        if (!Current() || generation != _generation) return;
        try
        {
            if (!await _validateOriginalAccess(_lifetime.Token)) { Retire(); return; }
            if (!Current() || generation != _generation) return;
            var current = _original.Snapshot;
            if (current.ArtifactId != artifactId || current.RevisionId != revisionId) { Refresh(); return; }
            _original.SelectNativeUserLayer(pageId, layerId);
            Refresh();
            if(keyboardFocus&&Current())_rows.Children.OfType<Button>().SingleOrDefault(button=>button.Tag is Guid id&&id==layerId)?.Focus();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (ObjectDisposedException) { Retire(); }
        catch (Exception error)
        {
            if (Current() && generation == _generation) _status.Text = $"Layer selection unavailable: {error.Message}";
        }
    }

    private bool Current()
    {
        if (_retired) return false;
        try { if (_isCurrent()) return true; }
        catch { }
        Retire();
        return false;
    }

    private void Retire()
    {
        if (_retired) return;
        _retired = true;
        ++_generation;
        _lifetime.Cancel();
        _rows.Children.Clear();
        _status.Text = "Original Canvas layer view is no longer available.";
    }

    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        Retire();
        _lifetime.Dispose();
    }
}
