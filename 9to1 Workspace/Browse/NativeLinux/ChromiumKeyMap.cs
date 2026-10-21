using Avalonia.Input;

namespace HavenOS.Apps.Browse;

/// <summary>
/// Minimal reviewed infrastructure mapping from Avalonia key events to the CDP
/// <c>Input.dispatchKeyEvent</c> fields (DOM <c>key</c>, DOM <c>code</c>, Windows
/// virtual-key code, and the CDP modifier bitmask). This is engine/transport
/// infrastructure — not application code. It intentionally has no layout logic:
/// the actual inserted character always comes from the genuine native
/// <see cref="KeyEventArgs.KeySymbol"/> (which is layout- and shift-aware), never
/// synthesized from the raw key.
/// </summary>
internal static class ChromiumKeyMap
{
    /// <summary>Maps an Avalonia logical key to its DOM UI Events <c>key</c> value
    /// (e.g. <c>a</c>, <c>Enter</c>, <c>ArrowLeft</c>).</summary>
    public static string ToDomKey(Key key) => key switch
    {
        Key.A => "a", Key.B => "b", Key.C => "c", Key.D => "d", Key.E => "e",
        Key.F => "f", Key.G => "g", Key.H => "h", Key.I => "i", Key.J => "j",
        Key.K => "k", Key.L => "l", Key.M => "m", Key.N => "n", Key.O => "o",
        Key.P => "p", Key.Q => "q", Key.R => "r", Key.S => "s", Key.T => "t",
        Key.U => "u", Key.V => "v", Key.W => "w", Key.X => "x", Key.Y => "y",
        Key.Z => "z",
        Key.D0 => "0", Key.D1 => "1", Key.D2 => "2", Key.D3 => "3", Key.D4 => "4",
        Key.D5 => "5", Key.D6 => "6", Key.D7 => "7", Key.D8 => "8", Key.D9 => "9",

        Key.Return or Key.Enter => "Enter",
        Key.Back => "Backspace",
        Key.Tab => "Tab",
        Key.Escape => "Escape",
        Key.Space => " ",
        Key.Left => "ArrowLeft", Key.Up => "ArrowUp", Key.Right => "ArrowRight", Key.Down => "ArrowDown",
        Key.Home => "Home", Key.End => "End", Key.Insert => "Insert", Key.Delete => "Delete",
        Key.PageUp => "PageUp", Key.PageDown => "PageDown",

        Key.F1 => "F1", Key.F2 => "F2", Key.F3 => "F3", Key.F4 => "F4",
        Key.F5 => "F5", Key.F6 => "F6", Key.F7 => "F7", Key.F8 => "F8",
        Key.F9 => "F9", Key.F10 => "F10", Key.F11 => "F11", Key.F12 => "F12",

        Key.LeftShift or Key.RightShift => "Shift",
        Key.LeftCtrl or Key.RightCtrl => "Control",
        Key.LeftAlt or Key.RightAlt => "Alt",
        Key.LWin or Key.RWin => "Meta",
        Key.CapsLock => "CapsLock",
        Key.NumLock => "NumLock",
        Key.Scroll => "Scroll",

        Key.OemComma => ",", Key.OemPeriod => ".", Key.OemPlus => "+",
        Key.OemMinus => "-", Key.OemSemicolon => ";", Key.OemQuestion => "?",
        Key.OemQuotes => "'", Key.OemOpenBrackets => "[", Key.OemCloseBrackets => "]",
        Key.OemBackslash => "\\", Key.OemTilde => "`", Key.OemPipe => "|",

        Key.NumPad0 => "0", Key.NumPad1 => "1", Key.NumPad2 => "2", Key.NumPad3 => "3",
        Key.NumPad4 => "4", Key.NumPad5 => "5", Key.NumPad6 => "6", Key.NumPad7 => "7",
        Key.NumPad8 => "8", Key.NumPad9 => "9",
        Key.Add => "+", Key.Subtract => "-", Key.Multiply => "*", Key.Divide => "/",
        Key.Decimal => ".",

        _ => key.ToString()
    };

    /// <summary>Maps an Avalonia physical key to its DOM UI Events <c>code</c>
    /// value (e.g. <c>KeyA</c>, <c>Enter</c>, <c>Numpad0</c>).</summary>
    public static string ToDomCode(PhysicalKey physical) => physical switch
    {
        PhysicalKey.A => "KeyA", PhysicalKey.B => "KeyB", PhysicalKey.C => "KeyC",
        PhysicalKey.D => "KeyD", PhysicalKey.E => "KeyE", PhysicalKey.F => "KeyF",
        PhysicalKey.G => "KeyG", PhysicalKey.H => "KeyH", PhysicalKey.I => "KeyI",
        PhysicalKey.J => "KeyJ", PhysicalKey.K => "KeyK", PhysicalKey.L => "KeyL",
        PhysicalKey.M => "KeyM", PhysicalKey.N => "KeyN", PhysicalKey.O => "KeyO",
        PhysicalKey.P => "KeyP", PhysicalKey.Q => "KeyQ", PhysicalKey.R => "KeyR",
        PhysicalKey.S => "KeyS", PhysicalKey.T => "KeyT", PhysicalKey.U => "KeyU",
        PhysicalKey.V => "KeyV", PhysicalKey.W => "KeyW", PhysicalKey.X => "KeyX",
        PhysicalKey.Y => "KeyY", PhysicalKey.Z => "KeyZ",

        PhysicalKey.NumPad0 => "Numpad0", PhysicalKey.NumPad1 => "Numpad1",
        PhysicalKey.NumPad2 => "Numpad2", PhysicalKey.NumPad3 => "Numpad3",
        PhysicalKey.NumPad4 => "Numpad4", PhysicalKey.NumPad5 => "Numpad5",
        PhysicalKey.NumPad6 => "Numpad6", PhysicalKey.NumPad7 => "Numpad7",
        PhysicalKey.NumPad8 => "Numpad8", PhysicalKey.NumPad9 => "Numpad9",
        PhysicalKey.NumPadAdd => "NumpadAdd", PhysicalKey.NumPadSubtract => "NumpadSubtract",
        PhysicalKey.NumPadMultiply => "NumpadMultiply", PhysicalKey.NumPadDivide => "NumpadDivide",
        PhysicalKey.NumPadDecimal => "NumpadDecimal", PhysicalKey.NumPadEnter => "NumpadEnter",
        PhysicalKey.NumPadEqual => "NumpadEqual", PhysicalKey.NumPadComma => "NumpadComma",

        _ => physical.ToString()
    };

    /// <summary>Maps an Avalonia key to its Windows virtual-key code, the code CDP
    /// <c>windowsVirtualKeyCode</c> expects.</summary>
    public static int ToWindowsVirtualKeyCode(Key key) => key switch
    {
        Key.Back => 0x08,
        Key.Tab => 0x09,
        Key.Return or Key.Enter => 0x0D,
        Key.LeftShift or Key.RightShift => 0x10,
        Key.LeftCtrl or Key.RightCtrl => 0x11,
        Key.LeftAlt or Key.RightAlt => 0x12,
        Key.Escape => 0x1B,
        Key.Space => 0x20,
        Key.PageUp => 0x21,
        Key.PageDown => 0x22,
        Key.End => 0x23,
        Key.Home => 0x24,
        Key.Left => 0x25,
        Key.Up => 0x26,
        Key.Right => 0x27,
        Key.Down => 0x28,
        Key.Insert => 0x2D,
        Key.Delete => 0x2E,

        Key.D0 => 0x30, Key.D1 => 0x31, Key.D2 => 0x32, Key.D3 => 0x33,
        Key.D4 => 0x34, Key.D5 => 0x35, Key.D6 => 0x36, Key.D7 => 0x37,
        Key.D8 => 0x38, Key.D9 => 0x39,

        Key.A => 0x41, Key.B => 0x42, Key.C => 0x43, Key.D => 0x44,
        Key.E => 0x45, Key.F => 0x46, Key.G => 0x47, Key.H => 0x48,
        Key.I => 0x49, Key.J => 0x4A, Key.K => 0x4B, Key.L => 0x4C,
        Key.M => 0x4D, Key.N => 0x4E, Key.O => 0x4F, Key.P => 0x50,
        Key.Q => 0x51, Key.R => 0x52, Key.S => 0x53, Key.T => 0x54,
        Key.U => 0x55, Key.V => 0x56, Key.W => 0x57, Key.X => 0x58,
        Key.Y => 0x59, Key.Z => 0x5A,

        Key.LWin => 0x5B, Key.RWin => 0x5C,

        Key.NumPad0 => 0x60, Key.NumPad1 => 0x61, Key.NumPad2 => 0x62,
        Key.NumPad3 => 0x63, Key.NumPad4 => 0x64, Key.NumPad5 => 0x65,
        Key.NumPad6 => 0x66, Key.NumPad7 => 0x67, Key.NumPad8 => 0x68,
        Key.NumPad9 => 0x69,
        Key.Multiply => 0x6A, Key.Add => 0x6B, Key.Subtract => 0x6D,
        Key.Decimal => 0x6E, Key.Divide => 0x6F,

        Key.F1 => 0x70, Key.F2 => 0x71, Key.F3 => 0x72, Key.F4 => 0x73,
        Key.F5 => 0x74, Key.F6 => 0x75, Key.F7 => 0x76, Key.F8 => 0x77,
        Key.F9 => 0x78, Key.F10 => 0x79, Key.F11 => 0x7A, Key.F12 => 0x7B,

        Key.CapsLock => 0x14,
        Key.NumLock => 0x90,
        Key.Scroll => 0x91,

        Key.OemSemicolon => 0xBA, Key.OemPlus => 0xBB, Key.OemComma => 0xBC,
        Key.OemMinus => 0xBD, Key.OemPeriod => 0xBE, Key.OemQuestion => 0xBF,
        Key.OemTilde => 0xC0, Key.OemOpenBrackets => 0xDB, Key.OemBackslash => 0xDC,
        Key.OemCloseBrackets => 0xDD, Key.OemQuotes => 0xDE, Key.OemPipe => 0xDC,

        _ => 0
    };

    /// <summary>Converts Avalonia keyboard modifiers to the CDP modifier bitmask
    /// (Alt=1, Ctrl=2, Meta=4, Shift=8).</summary>
    public static int ToCdpModifiers(KeyModifiers modifiers)
    {
        var result = 0;
        if (modifiers.HasFlag(KeyModifiers.Alt)) result |= 1;
        if (modifiers.HasFlag(KeyModifiers.Control)) result |= 2;
        if (modifiers.HasFlag(KeyModifiers.Meta)) result |= 4;
        if (modifiers.HasFlag(KeyModifiers.Shift)) result |= 8;
        return result;
    }

    /// <summary>Returns the CDP <c>text</c>/<c>unmodifiedText</c> to attach to a
    /// keyDown so Chromium produces the genuine character/keypress default action.
    /// Returns <c>\r</c> for Enter (so the page's default submit/Enter action fires),
    /// the native single-character symbol for printable keys, and <c>null</c> for every
    /// other control/navigation key and for Alt/Ctrl/Meta shortcuts (which must not
    /// inject a character). The printable gate is the same single-character DOM-key
    /// test the surface used before Browser21, so control keys (Tab, Backspace,
    /// Escape, arrows, …) never inject text even if the platform supplies a character
    /// symbol. The symbol is the platform-provided, layout/shift-aware character —
    /// never synthesized from the raw key.</summary>
    public static string? ToKeyDownText(Key key, string? keySymbol, int cdpModifiers)
    {
        // Alt/Ctrl/Meta shortcuts never inject text.
        if ((cdpModifiers & (1 | 2 | 4)) != 0) return null;
        if (key is Key.Return or Key.Enter) return "\r";
        // Printable single-character DOM keys (letters/digits/punctuation/Space) carry
        // their genuine native symbol; every other control/navigation key yields null.
        return ToDomKey(key).Length == 1 && !string.IsNullOrEmpty(keySymbol) && keySymbol.Length == 1
            ? keySymbol
            : null;
    }
}
