using Haven.UI;
using Haven.UI.Components;
using HuiButton = Haven.UI.Components.Button;

namespace CakeOS.HuiLinuxHost.Canvas;

/// <summary>
/// Rnote pens-sidebar equivalent: Brush (Pen), Shaper, Typewriter, Eraser,
/// Selector, Tools — in donor order — plus the Highlighter marker shortcut,
/// undo/redo, and zoom. The Highlighter selects the marker brush variant;
/// the Pen button honours the configured brush style (Solid/Textured/Marker).
/// </summary>
internal sealed class CanvasToolStrip : Container
{
    public CanvasToolStrip()
    {
        Name = "Canvas.Tools";
        Layout = HavenLayout.Horizontal;
        SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.Height, HavenLength.Px(42));
        SetValue(HavenProperties.Gap, HavenLength.Px(8));
        SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);

        PenButton = CreateButton("Canvas.Tool.Pen", "Pen", 72);
        ShaperButton = CreateButton("Canvas.Tool.Shaper", "Shaper", 82);
        TypewriterButton = CreateButton("Canvas.Tool.Typewriter", "Text", 72);
        EraserButton = CreateButton("Canvas.Tool.Eraser", "Eraser", 78);
        SelectorButton = CreateButton("Canvas.Tool.Selector", "Select", 78);
        ToolsButton = CreateButton("Canvas.Tool.Tools", "Tools", 72);
        HighlighterButton = CreateButton("Canvas.Tool.Highlighter", "Highlight", 88);
        // Back-compat alias: the v2 strip exposed a single Shape button that
        // selected the shaper pen with a rectangle builder.
        ShapeButton = ShaperButton;
        UndoButton = CreateButton("Canvas.Undo", "Undo", 72);
        RedoButton = CreateButton("Canvas.Redo", "Redo", 72);
        ZoomOutButton = CreateButton("Canvas.Zoom.Out", "−", 44);
        ZoomInButton = CreateButton("Canvas.Zoom.In", "+", 44);

        PenButton.Invoked += (_, _) => ToolRequested?.Invoke(CanvasTool.Pen);
        ShaperButton.Invoked += (_, _) =>
        {
            ShapeRequested?.Invoke(this, EventArgs.Empty);
            ToolRequested?.Invoke(CanvasTool.Shape);
        };
        TypewriterButton.Invoked += (_, _) => ToolRequested?.Invoke(CanvasTool.Typewriter);
        EraserButton.Invoked += (_, _) => ToolRequested?.Invoke(CanvasTool.Eraser);
        SelectorButton.Invoked += (_, _) => ToolRequested?.Invoke(CanvasTool.Selector);
        ToolsButton.Invoked += (_, _) => ToolRequested?.Invoke(CanvasTool.Tools);
        HighlighterButton.Invoked += (_, _) => ToolRequested?.Invoke(CanvasTool.Highlighter);
        UndoButton.Invoked += (_, _) => UndoRequested?.Invoke(this, EventArgs.Empty);
        RedoButton.Invoked += (_, _) => RedoRequested?.Invoke(this, EventArgs.Empty);
        ZoomOutButton.Invoked += (_, _) => ZoomRequested?.Invoke(this, -1);
        ZoomInButton.Invoked += (_, _) => ZoomRequested?.Invoke(this, +1);

        Add(PenButton);
        Add(ShaperButton);
        Add(TypewriterButton);
        Add(EraserButton);
        Add(SelectorButton);
        Add(ToolsButton);
        Add(HighlighterButton);
        Add(UndoButton);
        Add(RedoButton);
        Add(ZoomOutButton);
        Add(ZoomInButton);

        SetTool(CanvasTool.Pen);
        SetHistory(canUndo: false, canRedo: false);
    }

    public HuiButton PenButton { get; }
    public HuiButton ShaperButton { get; }
    public HuiButton TypewriterButton { get; }
    public HuiButton EraserButton { get; }
    public HuiButton SelectorButton { get; }
    public HuiButton ToolsButton { get; }
    public HuiButton HighlighterButton { get; }
    /// <summary>v2 alias for <see cref="ShaperButton"/> (rectangular shaper).</summary>
    public HuiButton ShapeButton { get; }
    public HuiButton UndoButton { get; }
    public HuiButton RedoButton { get; }
    public HuiButton ZoomOutButton { get; }
    public HuiButton ZoomInButton { get; }

    public event Action<CanvasTool>? ToolRequested;
    public event EventHandler? ShapeRequested;
    public event EventHandler? UndoRequested;
    public event EventHandler? RedoRequested;
    public event EventHandler<int>? ZoomRequested;

    public void SetTool(CanvasTool tool)
    {
        SetSelected(PenButton, tool == CanvasTool.Pen);
        SetSelected(ShaperButton, tool == CanvasTool.Shape);
        SetSelected(TypewriterButton, tool == CanvasTool.Typewriter);
        SetSelected(EraserButton, tool == CanvasTool.Eraser);
        SetSelected(SelectorButton, tool == CanvasTool.Selector);
        SetSelected(ToolsButton, tool == CanvasTool.Tools);
        SetSelected(HighlighterButton, tool == CanvasTool.Highlighter);
    }

    public void SetHistory(bool canUndo, bool canRedo)
    {
        UndoButton.SetValue(HavenProperties.Enabled, canUndo);
        RedoButton.SetValue(HavenProperties.Enabled, canRedo);
        UndoButton.SetState(HavenElementState.Disabled, !canUndo);
        RedoButton.SetState(HavenElementState.Disabled, !canRedo);
    }

    private static HuiButton CreateButton(string name, string content, double width)
    {
        var button = new HuiButton { Name = name, Content = content };
        button.SetValue(HavenProperties.Width, HavenLength.Px(width));
        button.SetValue(HavenProperties.Height, HavenLength.Px(36));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 10px"));
        button.SetValue(HavenProperties.FontSize, 13d);
        button.Accessibility.AccessibleName = content;
        return button;
    }

    private static void SetSelected(HuiButton button, bool selected)
    {
        button.SetState(HavenElementState.Selected, selected);
        button.Accessibility.Selected = selected;
    }
}
