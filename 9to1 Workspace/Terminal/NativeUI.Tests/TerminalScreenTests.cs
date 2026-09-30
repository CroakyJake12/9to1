using System.Text;
using Xunit;

namespace HavenOS.Apps.Terminal.NativeUI.Tests;

public sealed class TerminalScreenTests
{
    [Fact]
    public void Pinned_native_screen_preserves_chunked_utf8_colors_cursor_editing_and_alternate_screen()
    {
        using var screen = new TerminalScreen(3, 12);
        screen.Feed(new byte[] { 65, 0xc3 });
        screen.Feed(new byte[] { 0xa9 });
        screen.Feed("\u001b[31mR\u001b[0m"u8);
        var frame = screen.Snapshot();
        Assert.Equal("A", frame.Cells[0].Text);
        Assert.Equal("é", frame.Cells[1].Text);
        Assert.Equal("R", frame.Cells[2].Text);
        Assert.NotEqual(frame.Cells[0].Foreground, frame.Cells[2].Foreground);
        screen.Feed("\u001b[2;4HZ\u001b[1D\u001b[P"u8);
        frame = screen.Snapshot();
        Assert.Empty(frame.Cells[15].Text);
        Assert.Equal((1, 3), (frame.CursorRow, frame.CursorColumn));
        screen.Feed("\u001b[?1049hALT\u001b[?1049l"u8);
        Assert.Equal("A", screen.Snapshot().Cells[0].Text);
        screen.Feed(Encoding.UTF8.GetBytes("\u001b[H界"));
        frame = screen.Snapshot();
        Assert.Equal("界", frame.Cells[0].Text);
        Assert.Equal(2, frame.Cells[0].Width);
        Assert.Equal(0, frame.Cells[1].Width);
    }

    [Fact]
    public void Native_screen_bounds_history_resizes_and_encodes_actual_application_cursor_mode()
    {
        using var screen = new TerminalScreen(3, 12);
        for (var line = 0; line < 300; line++) screen.Feed(Encoding.UTF8.GetBytes($"{line:D3}\r\n"));
        Assert.Equal(256, screen.Snapshot().HistoryLines);
        Assert.Equal("0", screen.Snapshot(256).Cells[0].Text);
        screen.Resize(4, 16);
        var frame = screen.Snapshot();
        Assert.Equal((4, 16, 64), (frame.Rows, frame.Columns, frame.Cells.Count));
        Assert.Equal("λ", Encoding.UTF8.GetString(screen.EncodeCharacter(new Rune('λ'))));
        Assert.Equal("\u001b[A", Encoding.UTF8.GetString(screen.EncodeKey(5)));
        screen.Feed("\u001b[?1h"u8);
        Assert.Equal("\u001bOA", Encoding.UTF8.GetString(screen.EncodeKey(5)));
        screen.Feed("\u001b[6n"u8);
        var response = Encoding.UTF8.GetString(screen.DrainResponses());
        Assert.StartsWith("\u001b[", response);
        Assert.EndsWith("R", response);
        Assert.Throws<ArgumentOutOfRangeException>(() => screen.Resize(257, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => screen.EncodeKey(900));
        Assert.Equal("multi λ", Encoding.UTF8.GetString(screen.EncodeText("multi λ")));
        Assert.Throws<ArgumentOutOfRangeException>(() => screen.EncodeText(new string('a', 16385)));
        screen.Dispose();
        Assert.Throws<ObjectDisposedException>(() => screen.Snapshot());
    }
}
