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

/// <summary>Portable presentation over the same canonical notebook core and original retained
/// Write renderer. Browser and native hosts borrow this presentation without acquiring its
/// repository or core lifetime. All product structure remains in the shared Boards.cui.</summary>
public sealed class BoardsCuiWorkspacePresentation : IDisposable
{
    private readonly BoardsBrowserCore _core;
    private bool _disposed;
    private TopLevel? _topLevel;
    public WriteRetainedSceneControl Control { get; }
    public WriteRetainedSceneControl HistoryControl { get; }
    // Typed low-level slot only. App controls, labels and state remain in Boards.cui.
    public Panel SharedInkControl { get; } = new();
    public CuiControlRegistry ControlRegistry { get; }
    public BoardsCuiWorkspacePresentation(BoardsBrowserCore core, Func<bool> reduceMotion)
    {
        _core = core;
        Control = new(reduceMotion);
        HistoryControl = AcquireHistoryControl(Control, reduceMotion);
        Control.SelectionChanged += SelectionChanged;
        Control.AttachedToVisualTree += Attached;
        CuiThemeResourceApplier.PaletteChanged += ThemeChanged;
        try
        {
            core.PropertyChanged += OnChanged;
            Refresh();
            ControlRegistry = new CuiControlRegistry();
            BrowserCanonicalIcons.Register(ControlRegistry);
            ControlRegistry.RegisterObjectRenderer("BoardsNotebookSurface", _ => Control);
            ControlRegistry.RegisterObjectRenderer("BoardsHistorySurface", _ => HistoryControl);
            ControlRegistry.RegisterObjectRenderer("BoardsSharedInkSurface", _ => SharedInkControl);
        }
        catch (Exception primary)
        {
            try { Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("Boards editor acquisition and cleanup failed.", primary, cleanup); }
            throw;
        }
    }
    private static WriteRetainedSceneControl AcquireHistoryControl(WriteRetainedSceneControl live, Func<bool> reduceMotion)
    {
        WriteRetainedSceneControl? history = null;
        try { history = new(reduceMotion); history.SetReadOnly(true); return history; }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try { history?.Dispose(); } catch (Exception cleanup) { failures.Add(cleanup); }
            try { live.Dispose(); } catch (Exception cleanup) { failures.Add(cleanup); }
            if (failures.Count > 1) throw new AggregateException("Boards history renderer acquisition and cleanup failed.", failures);
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
        HistoryControl.SetInputAllowed(_core.AllowHistorySelection);
        HistoryControl.SetReadOnly(true);
        HistoryControl.SetWorkspaceLayoutLocked(true);
        _core.TryGetValue("LayoutUnlocked", out var unlocked);
        Control.SetWorkspaceLayoutLocked(unlocked is not true);
        if (Application.Current?.FindResource("CuiTextBrush") is ISolidColorBrush foreground)
        {
            var appearance = new HavenSolidBrush(foreground.Color.A, foreground.Color.R, foreground.Color.G, foreground.Color.B);
            Control.SetWorkspaceAppearance(appearance);
            HistoryControl.SetWorkspaceAppearance(appearance);
        }
        if (_disposed) return;
        Control.SetEditor(_core.NativeRichEditor);
        HistoryControl.SetEditor(_core.NativeHistoryEditor);
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
        SharedInkControl.Children.Clear();
        try { Control.Dispose(); } finally { HistoryControl.Dispose(); }
    }
}
