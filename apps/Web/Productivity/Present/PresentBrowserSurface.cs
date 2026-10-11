using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;

namespace NineToOne.Web.Productivity.Present;

public static class PresentBrowserSurface
{
    public static BrowserCuiSurface Create(CuiDocument document, PresentBrowserSession session, Func<bool> reduceMotion)
    {
        var lifetime = new PresentationLifetime(session, reduceMotion);
        var registry = new CuiControlRegistry();
        registry.RegisterObjectRenderer("PresentSlideCanvas", _ => lifetime.Control);
        registry.RegisterObjectRenderer("PresentThumbnails", _ => lifetime.Thumbnails);
        return new(document, session, session, lifetime, registry);
    }
    private sealed class PresentationLifetime : IDisposable
    {
        private readonly PresentBrowserSession _session;
        private bool _disposed;
        private TopLevel? _topLevel;
        public PresentRetainedSceneControl Control { get; }
        public PresentThumbnailControl Thumbnails { get; }
        public PresentationLifetime(PresentBrowserSession session, Func<bool> reduceMotion)
        {
            _session = session; Control = new(reduceMotion); Thumbnails = new(reduceMotion);
            Control.LiveTextPreviewed += Preview;
            Control.SelectionChanged += Selection;
            Thumbnails.SelectionChanged += Selection;
            Control.AttachedToVisualTree += Attached;
            session.PropertyChanged += Changed;
            Refresh();
        }
        private void Preview(object? sender, EventArgs args) => _session.RecordRetainedPreview();
        private void Selection(object? sender, EventArgs args) => _session.RecordRetainedSelection();
        private void Attached(object? sender, VisualTreeAttachmentEventArgs args)
        {
            var nextRoot = TopLevel.GetTopLevel(Control);
            if (ReferenceEquals(_topLevel, nextRoot)) return;
            if (_topLevel is not null) _topLevel.PropertyChanged -= ViewportChanged;
            _topLevel = nextRoot;
            if (_topLevel is null) return;
            _topLevel.PropertyChanged += ViewportChanged;
            var attachedRoot = _topLevel;
            Dispatcher.UIThread.Post(() =>
            {
                if (!_disposed && ReferenceEquals(_topLevel, attachedRoot))
                    _session.SetViewportWidth(attachedRoot.ClientSize.Width);
            });
        }
        // The editor is detached while compact Properties is active. Viewport
        // observation belongs to this surface lifetime so widening can restore it.
        private void ViewportChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        { if (args.Property == TopLevel.ClientSizeProperty && _topLevel is not null) _session.SetViewportWidth(_topLevel.ClientSize.Width); }
        private void Changed(object? sender, PropertyChangedEventArgs args)
        { if (Dispatcher.UIThread.CheckAccess()) Refresh(); else Dispatcher.UIThread.Post(Refresh); }
        private void Refresh()
        {
            if (_disposed) return;
            Control.SetInputAllowed(_session.IsPresentationAdmitted && _session.IsWorkspaceActive && !_session.IsBusy && !_session.IsReadOnly && !_session.HasUnknownCommitOutcome && !_session.IsPlaying && _session.Editor is not null);
            Control.SetEditor(_session.Editor);
            Thumbnails.SetEditor(_session.Editor, _session.IsPresentationAdmitted && _session.IsWorkspaceActive && !_session.IsBusy && !_session.HasUnknownCommitOutcome, !_session.IsReadOnly);
        }
        public void Dispose()
        {
            if (_disposed) return;
            if (_topLevel is not null) _topLevel.PropertyChanged -= ViewportChanged;
            _topLevel = null;
            Control.AttachedToVisualTree -= Attached;
            Control.SelectionChanged -= Selection; Thumbnails.SelectionChanged -= Selection;
            _disposed = true; _session.PropertyChanged -= Changed; Control.LiveTextPreviewed -= Preview; Control.Dispose(); Thumbnails.Dispose();
        }
    }
}
