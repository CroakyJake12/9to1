using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;

namespace NineToOne.Web.Write;

/// <summary>Registered CUI presentation of the real retained editor.</summary>
public static class WriteBrowserSurface
{
    /// <summary>
    /// The owning composition root retains asynchronous session lifetime and must
    /// save/recover before account change. Surface disposal only detaches UI peers.
    /// Theme/font resources and a real authorised repository are prerequisites.
    /// </summary>
    public static BrowserCuiSurface Create(CuiDocument document, WriteBrowserSession session,
        Func<bool> reduceMotion)
    {
        var host = new PresentationLifetime(session, reduceMotion);
        var registry = new CuiControlRegistry();
        registry.RegisterObjectRenderer("WriteDocumentSurface", _ => host.Control);
        return new(document, session, session, host, registry);
    }

    private sealed class PresentationLifetime : IDisposable
    {
        private readonly WriteBrowserSession _session;
        private bool _disposed;
        private TopLevel? _topLevel;
        public WriteRetainedSceneControl Control { get; }

        public PresentationLifetime(WriteBrowserSession session, Func<bool> reduceMotion)
        {
            _session = session;
            Control = new(reduceMotion);
            Control.SelectionChanged += SelectionChanged;
            Control.AttachedToVisualTree += Attached;
            session.PropertyChanged += OnChanged;
            Refresh();
        }

        private void SelectionChanged(object? sender, EventArgs args) => _session.RefreshSelection();
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
        // Keep observing the surface viewport while its editor is detached by
        // the start view or compact Properties; Dispose releases the observer.
        private void ViewportChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        { if (args.Property == TopLevel.ClientSizeProperty && _topLevel is not null) _session.SetViewportWidth(_topLevel.ClientSize.Width); }

        private void OnChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (Dispatcher.UIThread.CheckAccess()) Refresh();
            else Dispatcher.UIThread.Post(Refresh);
        }

        private void Refresh()
        {
            if (_disposed) return;
            // Freeze native and retained input before exposing/rebinding the owner.
            var enabled = _session.IsPresentationAdmitted && _session.IsWorkspaceActive && !_session.IsBusy && !_session.IsReadOnly && !_session.HasUnknownCommitOutcome && _session.Editor is not null;
            Control.SetInputAllowed(enabled);
            Control.SetEditor(_session.Editor);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.PropertyChanged -= OnChanged;
            if (_topLevel is not null) _topLevel.PropertyChanged -= ViewportChanged;
            Control.SelectionChanged -= SelectionChanged;
            Control.AttachedToVisualTree -= Attached;
            _topLevel = null;
            Control.Dispose();
        }
    }
}
