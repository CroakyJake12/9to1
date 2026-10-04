using System.ComponentModel;
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
        public WriteRetainedSceneControl Control { get; }

        public PresentationLifetime(WriteBrowserSession session, Func<bool> reduceMotion)
        {
            _session = session;
            Control = new(reduceMotion);
            session.PropertyChanged += OnChanged;
            Refresh();
        }

        private void OnChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (Dispatcher.UIThread.CheckAccess()) Refresh();
            else Dispatcher.UIThread.Post(Refresh);
        }

        private void Refresh()
        {
            if (_disposed) return;
            // Freeze native and retained input before exposing/rebinding the owner.
            var enabled = _session.IsPresentationAdmitted && !_session.IsBusy && !_session.IsReadOnly && !_session.HasUnknownCommitOutcome && _session.Editor is not null;
            Control.SetInputAllowed(enabled);
            Control.SetEditor(_session.Editor);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.PropertyChanged -= OnChanged;
            Control.Dispose();
        }
    }
}
