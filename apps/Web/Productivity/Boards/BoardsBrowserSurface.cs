using System.ComponentModel;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using NineToOne.Web.Write;

namespace NineToOne.Web.Productivity.Boards;

/// <summary>One native retained editor for the SAME canonical notebook core.</summary>
public static class BoardsBrowserSurface
{
    public static BrowserCuiSurface Create(CuiDocument document, BoardsBrowserCore core, Func<bool> reduceMotion)
    {
        var lifetime = new PresentationLifetime(core, reduceMotion);
        try
        {
            var registry = new CuiControlRegistry();
            registry.RegisterObjectRenderer("BoardsNotebookSurface", _ => lifetime.Control);
            return new(document, core, core, lifetime, registry);
        }
        catch (Exception primary)
        {
            try { lifetime.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("Boards surface acquisition and cleanup failed.", primary, cleanup); }
            throw;
        }
    }

    private sealed class PresentationLifetime : IDisposable
    {
        private readonly BoardsBrowserCore _core;
        private bool _disposed;
        public WriteRetainedSceneControl Control { get; }
        public PresentationLifetime(BoardsBrowserCore core, Func<bool> reduceMotion)
        {
            _core = core;
            Control = new(reduceMotion);
            try
            {
                core.PropertyChanged += OnChanged;
                Refresh();
            }
            catch (Exception primary)
            {
                try { Dispose(); }
                catch (Exception cleanup) { throw new AggregateException("Boards editor acquisition and cleanup failed.", primary, cleanup); }
                throw;
            }
        }
        private void OnChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (_disposed || !ReferenceEquals(sender, _core)) return;
            if (Dispatcher.UIThread.CheckAccess()) Refresh();
            else Dispatcher.UIThread.Post(Refresh);
        }
        private void Refresh()
        {
            if (_disposed) return;
            // Freeze input before replacing the exact editor. No document snapshot becomes a second model.
            Control.SetInputAllowed(_core.AllowRetainedRichInput);
            if (_disposed) return;
            Control.SetEditor(_core.NativeRichEditor);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Presentation disposal grants no repository/core disposal or save. The registered owner joins those.
            _core.PropertyChanged -= OnChanged;
            Control.Dispose();
        }
    }
}
