using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using NineToOne.Web.Write;
using Haven.UI;

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
        private TopLevel? _topLevel;
        public WriteRetainedSceneControl Control { get; }
        public PresentationLifetime(BoardsBrowserCore core, Func<bool> reduceMotion)
        {
            _core = core;
            Control = new(reduceMotion);
            Control.SelectionChanged += SelectionChanged;
            Control.AttachedToVisualTree += Attached;
            CuiThemeResourceApplier.PaletteChanged += ThemeChanged;
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
        private void SelectionChanged(object? sender, EventArgs args) => _core.NotifySelectionChanged();
        private void ThemeChanged(object? sender, EventArgs args) => Dispatcher.UIThread.Post(Refresh);
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
                if (_disposed || !ReferenceEquals(_topLevel, attachedRoot)) return;
                _core.SetViewportWidth(attachedRoot.ClientSize.Width);
                Refresh();
            });
        }
        // The editor can be temporarily detached by responsive view changes.
        // Its surface lifetime continues observing the viewport until disposal.
        private void ViewportChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        { if (args.Property == TopLevel.ClientSizeProperty && _topLevel is not null) _core.SetViewportWidth(_topLevel.ClientSize.Width); }
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
            Control.SetInputAllowed(_core.AllowRetainedSelection);
            Control.SetReadOnly(!_core.RichEditingPermitted);
            _core.TryGetValue("LayoutUnlocked", out var unlocked);
            Control.SetWorkspaceLayoutLocked(unlocked is not true);
            if (Application.Current?.FindResource("CuiTextBrush") is ISolidColorBrush foreground)
                Control.SetWorkspaceAppearance(new HavenSolidBrush(foreground.Color.A, foreground.Color.R, foreground.Color.G, foreground.Color.B));
            if (_disposed) return;
            Control.SetEditor(_core.NativeRichEditor);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Presentation disposal grants no repository/core disposal or save. The registered owner joins those.
            _core.PropertyChanged -= OnChanged;
            CuiThemeResourceApplier.PaletteChanged -= ThemeChanged;
            if (_topLevel is not null) _topLevel.PropertyChanged -= ViewportChanged;
            Control.SelectionChanged -= SelectionChanged;
            Control.AttachedToVisualTree -= Attached;
            _topLevel = null;
            Control.Dispose();
        }
    }
}
