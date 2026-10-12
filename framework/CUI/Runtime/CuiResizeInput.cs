using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace CakeOS.Cui.Runtime;

/// <summary>A keyboard and pointer operated panel-width input. CUI owns its handle template.</summary>
public sealed class CuiResizeInput : Slider
{
    private IPointer? _pointer;
    private double _startX, _startValue;
    public bool FromStart { get; set; }
    protected override Type StyleKeyOverride => typeof(CuiResizeInput);

    public CuiResizeInput()
    {
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        Focusable = true;
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus(); _pointer = e.Pointer;
        _startX = e.GetPosition(TopLevel.GetTopLevel(this)).X; _startValue = Value;
        e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!ReferenceEquals(_pointer, e.Pointer)) return;
        var delta = e.GetPosition(TopLevel.GetTopLevel(this)).X - _startX;
        Value = Math.Clamp(_startValue + (FromStart ? -delta : delta), Minimum, Maximum);
        e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(_pointer, e.Pointer)) return;
        _pointer = null; e.Pointer.Capture(null); e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    { _pointer = null; base.OnPointerCaptureLost(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var delta = e.Key switch { Key.Left => -16, Key.Right => 16, _ => 0 };
        if (delta != 0) { Value = Math.Clamp(Value + (FromStart ? -delta : delta), Minimum, Maximum); e.Handled = true; }
        else if (e.Key is Key.Home or Key.End) { Value = e.Key == Key.Home ? Minimum : Maximum; e.Handled = true; }
        else base.OnKeyDown(e);
    }
}
