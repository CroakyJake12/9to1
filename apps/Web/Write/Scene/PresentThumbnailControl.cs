using Avalonia.Controls;
using Avalonia.Input;
using Haven.Application;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Present;
using Haven.UI;

namespace NineToOne.Web.Productivity.Present;

/// <summary>Hosts the maintained thumbnail navigator with the same presentation editor.</summary>
public sealed class PresentThumbnailControl : Panel, IDisposable
{
    private readonly HavenSceneControl _scene;
    private readonly PresentThumbnailNavigator _navigator = new();
    private PresentEditor? _editor;
    private bool _allowed, _disposed;
    private bool _editable;
    public event EventHandler? SelectionChanged;
    public void SetPreviewRenderer(Action<HavenDrawingContext, Haven.Core.PresentDocument, Haven.Core.PresentSlide, HavenRect, double> renderer) =>
        _navigator.SetPreviewRenderer(renderer);
    public PresentThumbnailControl(Func<bool> reduceMotion)
    {
        _scene = new(new HavenAvaloniaImageResolver(), new HavenAvaloniaNativeControlResolver(), reduceMotion) { Platform = HavenPlatform.Unknown };
        Children.Add(_scene);
        _navigator.SetValue(HavenProperties.Background, "Transparent");
        _navigator.SlideSelected += index => { if (_allowed && _editor is { } editor && index >= 0 && index < editor.Document.Slides.Count) { editor.CommitLiveTextEdit(); editor.SelectSlide(editor.Document.Slides[index].Id); Refresh(); SelectionChanged?.Invoke(this, EventArgs.Empty); } };
        _navigator.SlideReorderRequested += (from, to) => { if (_allowed && _editable && _editor is { } editor && from >= 0 && from < editor.Document.Slides.Count) { editor.CommitLiveTextEdit(); editor.MoveSlide(editor.Document.Slides[from].Id, to); } };
        ContextRequested += (_, args) =>
        {
            if (!_allowed) { args.Handled = true; return; }
            if (!args.TryGetPosition(_scene, out var point)) return;
            var item = _navigator.GetVisibleItems().FirstOrDefault(item => item.Bounds.Contains(new HavenPoint(point.X, point.Y)));
            if (item.SlideId != Guid.Empty) _navigator.ActivateSlide(item.SlideId);
            else args.Handled = true;
        };
    }
    public void SetEditor(PresentEditor? editor, bool allowed, bool editable = true)
    {
        if (_disposed) return;
        _allowed = allowed; _editable = editable; _scene.IsEnabled = allowed;
        _navigator.SetValue(HavenProperties.Enabled, allowed); _navigator.Accessibility.Enabled = allowed;
        if (!ReferenceEquals(editor, _editor)) { if (_editor is not null) _editor.Changed -= Changed; _editor = editor; if (editor is not null) editor.Changed += Changed; }
        _scene.Root = editor is null ? null : _navigator;
        Refresh();
    }
    private void Changed(object? sender, EventArgs args) => Refresh();
    private void Refresh() { if (!_disposed && _editor is { } editor) _navigator.SetDocument(editor.Document, editor.SelectedSlide.Order); }
    public void Dispose() { if (_disposed) return; _disposed = true; if (_editor is not null) _editor.Changed -= Changed; _scene.Root = null; _editor = null; }
}
