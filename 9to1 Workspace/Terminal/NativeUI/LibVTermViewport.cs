using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Application;

namespace HavenOS.Apps.Terminal.NativeUI;

/// <summary>Actual pinned libvterm screen over the host's existing PTY. No process is created here.</summary>
public sealed class LibVTermViewport : Control, ITerminalNativeViewport, IDisposable
{
    private const double CellWidth = 9, CellHeight = 19;
    private readonly SemaphoreSlim _input = new(1, 1);
    private ITerminalInteractiveSession? _session;
    private EventHandler<TerminalSessionOutput>? _outputHandler;
    private Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? _send;
    private TerminalScreen? _screen;
    private TerminalScreenSnapshot? _snapshot;
    private bool _enabled, _disposed;
    private int _queuedBytes, _queuedInputBytes, _historyOffset;
    public Control View => this;
    public string? Failure { get; private set; }
    public TerminalScreenSnapshot? ScreenSnapshot => _snapshot;

    public LibVTermViewport() { Focusable = true; ClipToBounds = true; MinWidth = 160; MinHeight = 240; }
    public void Attach(ITerminalInteractiveSession session, Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> sendInput)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(sendInput);
        Detach();
        _screen = new TerminalScreen();
        _session = session;
        _send = sendInput;
        Failure = null;
        _outputHandler = (_, output) => Receive(session, output);
        session.OutputReceived += _outputHandler;
        Refresh();
    }
    public void Detach()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_session is { } session && _outputHandler is { } handler) session.OutputReceived -= handler;
        _session = null; _outputHandler = null; _send = null; _enabled = false;
        _screen?.Dispose(); _screen = null; _snapshot = null; _historyOffset = 0;
        InvalidateVisual();
    }
    public void SetInteractiveInputEnabled(bool enabled)
    {
        Dispatcher.UIThread.VerifyAccess(); _enabled = enabled; InvalidateVisual();
    }
    private void Receive(ITerminalInteractiveSession session, TerminalSessionOutput output)
    {
        if (output.SessionId != session.Metadata.SessionId) return;
        // Reconstructed text is not a faithful PTY stream. Providers must expose original bytes.
        if (output.RawBytes is not { } raw)
        {
            Dispatcher.UIThread.Post(() => { if (ReferenceEquals(_session, session)) Fail("This provider does not expose original terminal bytes."); });
            return;
        }
        if (raw.Length > 1_048_576 || Interlocked.Add(ref _queuedBytes, raw.Length) > 1_048_576)
        {
            if (raw.Length <= 1_048_576) Interlocked.Add(ref _queuedBytes, -raw.Length);
            Dispatcher.UIThread.Post(() => { if (ReferenceEquals(_session, session)) Fail("Terminal output exceeded the bounded render queue."); });
            return;
        }
        var bytes = raw.ToArray();
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (!ReferenceEquals(_session, session) || _screen is null || _disposed || Failure is not null) return;
                _screen.Feed(bytes);
                Refresh();
                QueueInput(_screen.DrainResponses());
            }
            catch (Exception error) { Fail(error.Message); }
            finally { Array.Clear(bytes); Interlocked.Add(ref _queuedBytes, -bytes.Length); }
        });
    }
    private void Refresh()
    {
        _snapshot = _screen?.Snapshot(_historyOffset);
        InvalidateVisual();
    }
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_session is not { } session || _screen is null || Bounds.Width < CellWidth || Bounds.Height < CellHeight) return;
        var columns = Math.Clamp((int)(Bounds.Width / CellWidth), 1, 512);
        var rows = Math.Clamp((int)(Bounds.Height / CellHeight), 1, 256);
        if (_snapshot is { } previous && previous.Rows == rows && previous.Columns == columns) return;
        _screen.Resize(rows, columns); _historyOffset = 0; Refresh();
        ResizeSession(session, (ushort)columns, (ushort)rows);
    }
    private async void ResizeSession(ITerminalInteractiveSession session, ushort columns, ushort rows)
    {
        try { await session.ResizeAsync(columns, rows); }
        catch (Exception error) { if (ReferenceEquals(_session, session)) Fail(error.Message); }
    }
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!_enabled || _screen is null || string.IsNullOrEmpty(e.Text)) return;
        try { QueueInput(_screen.EncodeText(e.Text)); }
        catch (Exception error) { Fail(error.Message); }
        e.Handled = true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!_enabled || _screen is null) return;
        var key = e.Key switch
        {
            Key.Enter => 1, Key.Tab => 2, Key.Back => 3, Key.Escape => 4,
            Key.Up => 5, Key.Down => 6, Key.Left => 7, Key.Right => 8,
            Key.Insert => 9, Key.Delete => 10, Key.Home => 11, Key.End => 12,
            Key.PageUp => 13, Key.PageDown => 14,
            >= Key.F1 and <= Key.F24 => 257 + (int)e.Key - (int)Key.F1,
            _ => 0
        };
        var modifiers = ((e.KeyModifiers & KeyModifiers.Shift) != 0 ? 1 : 0)
            | ((e.KeyModifiers & KeyModifiers.Alt) != 0 ? 2 : 0)
            | ((e.KeyModifiers & KeyModifiers.Control) != 0 ? 4 : 0);
        if (key != 0) { QueueInput(_screen.EncodeKey(key, modifiers)); e.Handled = true; }
        else if ((modifiers & 4) != 0 && e.Key is >= Key.A and <= Key.Z)
        {
            QueueInput(_screen.EncodeCharacter(new Rune('a' + (int)e.Key - (int)Key.A), modifiers)); e.Handled = true;
        }
    }
    private async void QueueInput(byte[] bytes)
    {
        if (bytes.Length == 0) return;
        if (Interlocked.Add(ref _queuedInputBytes, bytes.Length) > 65536)
        {
            Interlocked.Add(ref _queuedInputBytes, -bytes.Length); Array.Clear(bytes);
            Fail("Terminal input exceeded the bounded queue."); return;
        }
        var session = _session; var send = _send;
        try
        {
            await _input.WaitAsync();
            try
            {
                if (!_enabled || _disposed || Failure is not null || session is null || !ReferenceEquals(_session, session) || send is null) return;
                _historyOffset = 0;
                await send(bytes, default);
            }
            finally { _input.Release(); }
        }
        catch (Exception error) { if (ReferenceEquals(_session, session)) Fail(error.Message); }
        finally { Array.Clear(bytes); Interlocked.Add(ref _queuedInputBytes, -bytes.Length); }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); Focus(); }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_snapshot is null) return;
        _historyOffset = Math.Clamp(_historyOffset + (int)(e.Delta.Y * 3), 0, _snapshot.HistoryLines);
        Refresh(); e.Handled = true;
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        if (Failure is { } failure)
        {
            context.DrawText(new FormattedText("Terminal unavailable: " + failure, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Parse("monospace")), 14, Brushes.White), new Point(8, 8));
            return;
        }
        if (_snapshot is not { } frame) return;
        for (var row = 0; row < frame.Rows; row++)
            for (var column = 0; column < frame.Columns; column++)
            {
                var cell = frame.Cells[row * frame.Columns + column];
                if (cell.Width == 0) continue;
                var fg = cell.Foreground; var bg = cell.Background;
                if ((cell.Attributes & 8) != 0) (fg, bg) = (bg, fg);
                var rect = new Rect(column * CellWidth, row * CellHeight, CellWidth * Math.Max(1, cell.Width), CellHeight);
                context.FillRectangle(new SolidColorBrush(Color.FromUInt32(bg)), rect);
                if (cell.Text.Length == 0 || (cell.Attributes & 16) != 0) continue;
                var brush = new SolidColorBrush(Color.FromUInt32(fg));
                var typeface = new Typeface(FontFamily.Parse("monospace"), (cell.Attributes & 2) != 0 ? FontStyle.Italic : FontStyle.Normal,
                    (cell.Attributes & 1) != 0 ? FontWeight.Bold : FontWeight.Normal);
                context.DrawText(new FormattedText(cell.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 14, brush), rect.Position);
                if ((cell.Attributes & 4) != 0) context.DrawLine(new Pen(brush), new Point(rect.X, rect.Bottom - 2), new Point(rect.Right, rect.Bottom - 2));
                if ((cell.Attributes & 32) != 0) context.DrawLine(new Pen(brush), new Point(rect.X, rect.Y + 9), new Point(rect.Right, rect.Y + 9));
            }
        if (_enabled && frame.CursorVisible)
            context.DrawRectangle(null, new Pen(Brushes.White), new Rect(frame.CursorColumn * CellWidth, frame.CursorRow * CellHeight, CellWidth, CellHeight));
    }
    private void Fail(string message)
    {
        Failure = message; _enabled = false;
        AutomationProperties.SetName(this, "Terminal unavailable: " + message);
        InvalidateVisual();
    }
    public void Dispose() { Dispatcher.UIThread.VerifyAccess(); if (_disposed) return; _disposed = true; Detach(); }
}
