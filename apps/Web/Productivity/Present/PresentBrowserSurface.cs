using System.ComponentModel;
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
        return new(document, session, session, lifetime, registry);
    }
    private sealed class PresentationLifetime : IDisposable
    {
        private readonly PresentBrowserSession _session;
        private bool _disposed;
        public PresentRetainedSceneControl Control { get; }
        public PresentationLifetime(PresentBrowserSession session, Func<bool> reduceMotion)
        {
            _session = session; Control = new(reduceMotion);
            Control.LiveTextPreviewed += Preview;
            session.PropertyChanged += Changed;
            Refresh();
        }
        private void Preview(object? sender, EventArgs args) => _session.RecordRetainedPreview();
        private void Changed(object? sender, PropertyChangedEventArgs args)
        { if (Dispatcher.UIThread.CheckAccess()) Refresh(); else Dispatcher.UIThread.Post(Refresh); }
        private void Refresh()
        {
            if (_disposed) return;
            Control.SetInputAllowed(_session.IsPresentationAdmitted && !_session.IsBusy && !_session.IsReadOnly && !_session.HasUnknownCommitOutcome && _session.Editor is not null);
            Control.SetEditor(_session.Editor);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _session.PropertyChanged -= Changed; Control.LiveTextPreviewed -= Preview; Control.Dispose();
        }
    }
}
