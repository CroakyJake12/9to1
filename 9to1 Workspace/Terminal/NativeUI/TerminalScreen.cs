using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HavenOS.Apps.Terminal.NativeUI;

public sealed record TerminalScreenCell(string Text, uint Foreground, uint Background, uint Attributes, int Width);
public sealed record TerminalScreenSnapshot(int Rows, int Columns, IReadOnlyList<TerminalScreenCell> Cells,
    int CursorRow, int CursorColumn, bool CursorVisible, int HistoryLines);

/// <summary>Owning pinned libvterm screen. It has no PTY, filesystem, clipboard or command authority.</summary>
public sealed class TerminalScreen : IDisposable
{
    private readonly object _sync = new();
    private readonly Handle _handle;
    private int _rows, _columns;
    public TerminalScreen(int rows = 24, int columns = 80)
    {
        ValidateSize(rows, columns);
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The pinned terminal screen adapter is currently available on Linux.");
        _handle = Native.New(rows, columns);
        if (_handle.IsInvalid) { _handle.Dispose(); throw new InvalidOperationException("The terminal screen could not be allocated."); }
        _rows = rows; _columns = columns;
    }
    public void Feed(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > 1_048_576) throw new ArgumentOutOfRangeException(nameof(bytes));
        var copy = bytes.ToArray();
        try { lock (_sync) { Check(); Require(Native.Feed(_handle, copy, (nuint)copy.Length)); } }
        finally { Array.Clear(copy); }
    }
    public void Resize(int rows, int columns)
    {
        ValidateSize(rows, columns);
        lock (_sync) { Check(); Require(Native.Resize(_handle, rows, columns)); _rows = rows; _columns = columns; }
    }
    public byte[] EncodeCharacter(Rune character, int modifiers = 0)
    {
        ValidateModifiers(modifiers);
        lock (_sync) { Check(); Require(Native.Character(_handle, (uint)character.Value, modifiers)); return DrainCore(); }
    }
    public byte[] EncodeText(string text, int modifiers = 0)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateModifiers(modifiers);
        if (Encoding.UTF8.GetByteCount(text) > 16384) throw new ArgumentOutOfRangeException(nameof(text), "A terminal text input is limited to 16 KiB.");
        lock (_sync)
        {
            Check();
            foreach (var character in text.EnumerateRunes()) Require(Native.Character(_handle, (uint)character.Value, modifiers));
            return DrainCore();
        }
    }
    public byte[] EncodeKey(int key, int modifiers = 0)
    {
        if (key is < 1 or > 14 && key is < 257 or > 280) throw new ArgumentOutOfRangeException(nameof(key));
        ValidateModifiers(modifiers);
        lock (_sync) { Check(); Require(Native.Key(_handle, key, modifiers)); return DrainCore(); }
    }
    public byte[] DrainResponses() { lock (_sync) { Check(); return DrainCore(); } }
    public TerminalScreenSnapshot Snapshot(int historyOffset = 0)
    {
        lock (_sync)
        {
            Check();
            var history = Native.HistoryCount(_handle);
            historyOffset = Math.Clamp(historyOffset, 0, history);
            var cells = new TerminalScreenCell[_rows * _columns];
            for (var row = 0; row < _rows; row++)
                for (var column = 0; column < _columns; column++)
                {
                    NativeCell cell;
                    if (row < historyOffset)
                    {
                        if (Native.HistoryCell(_handle, history - historyOffset + row, column, out cell) == 0) cell = default;
                    }
                    else Require(Native.Cell(_handle, row - historyOffset, column, out cell));
                    cells[row * _columns + column] = cell.Project();
                }
            var visible = Native.Cursor(_handle, out var cursorRow, out var cursorColumn) != 0;
            return new(_rows, _columns, cells, cursorRow + historyOffset, cursorColumn, visible && historyOffset == 0, history);
        }
    }
    private byte[] DrainCore()
    {
        var buffer = new byte[65536];
        try
        {
            var length = Native.Drain(_handle, buffer, (nuint)buffer.Length);
            if (length < 0) throw new InvalidOperationException("The terminal response buffer overflowed.");
            return buffer.AsSpan(0, length).ToArray();
        }
        finally { Array.Clear(buffer); }
    }
    private void Check() => ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
    private static void Require(int result) { if (result != 1) throw new InvalidOperationException("The terminal screen rejected the operation."); }
    private static void ValidateSize(int rows, int columns)
    {
        if (rows is < 1 or > 256 || columns is < 1 or > 512) throw new ArgumentOutOfRangeException(nameof(rows), "Terminal dimensions exceed the bounded screen.");
    }
    private static void ValidateModifiers(int modifiers) { if ((modifiers & ~7) != 0) throw new ArgumentOutOfRangeException(nameof(modifiers)); }
    public void Dispose() { lock (_sync) _handle.Dispose(); }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCell
    {
        public uint C0, C1, C2, C3, C4, C5, Foreground, Background, Attributes;
        public int Width;
        public readonly TerminalScreenCell Project()
        {
            var text = new StringBuilder();
            foreach (var code in new[] { C0, C1, C2, C3, C4, C5 })
            {
                if (code == 0 || code == uint.MaxValue) break;
                text.Append(Rune.IsValid(code) ? new Rune(code).ToString() : Rune.ReplacementChar.ToString());
            }
            return new(text.ToString(), Foreground, Background, Attributes, Width);
        }
    }
    private sealed class Handle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public Handle() : base(true) { }
        protected override bool ReleaseHandle() { Native.Free(handle); return true; }
    }
    private static class Native
    {
        private const string Library = "haven_terminal_screen";
        [DllImport(Library, EntryPoint = "haven_screen_new")] internal static extern Handle New(int rows, int columns);
        [DllImport(Library, EntryPoint = "haven_screen_free")] internal static extern void Free(IntPtr handle);
        [DllImport(Library, EntryPoint = "haven_screen_feed")] internal static extern int Feed(Handle handle, byte[] bytes, nuint length);
        [DllImport(Library, EntryPoint = "haven_screen_resize")] internal static extern int Resize(Handle handle, int rows, int columns);
        [DllImport(Library, EntryPoint = "haven_screen_cell")] internal static extern int Cell(Handle handle, int row, int column, out NativeCell cell);
        [DllImport(Library, EntryPoint = "haven_screen_cursor")] internal static extern int Cursor(Handle handle, out int row, out int column);
        [DllImport(Library, EntryPoint = "haven_screen_history_count")] internal static extern int HistoryCount(Handle handle);
        [DllImport(Library, EntryPoint = "haven_screen_history_cell")] internal static extern int HistoryCell(Handle handle, int line, int column, out NativeCell cell);
        [DllImport(Library, EntryPoint = "haven_screen_key")] internal static extern int Key(Handle handle, int key, int modifiers);
        [DllImport(Library, EntryPoint = "haven_screen_character")] internal static extern int Character(Handle handle, uint character, int modifiers);
        [DllImport(Library, EntryPoint = "haven_screen_drain")] internal static extern int Drain(Handle handle, byte[] bytes, nuint capacity);
    }
}
