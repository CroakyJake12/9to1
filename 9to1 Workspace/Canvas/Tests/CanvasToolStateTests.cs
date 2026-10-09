using Haven.Application;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class CanvasToolStateTests
{
    [Fact]
    public async Task Tool_activation_is_transient_and_failed_native_activation_does_not_change_selected_state()
    {
        var artifact = CanvasArtifact.Create();
        var bytes = CanvasArtifactCodec.Serialize(artifact);
        CanvasToolSelection? activation = null;
        using var tools = new CanvasToolState(tool => tool is CanvasPrimaryTool.Pen or CanvasPrimaryTool.Pan
            ? new(true, "") : CanvasToolCapability.Unavailable("Owning native adapter is unavailable"),
            selection => { if (selection.Tool == CanvasPrimaryTool.Pan) throw new InvalidOperationException("Native navigation unavailable"); activation = selection; });
        using var workspace = new CanvasCuiWorkspace((_, _) => throw new InvalidOperationException("Selecting a tool must not dispatch a document mutation"), _ => true, tools);
        workspace.Refresh(artifact, "Current");
        Assert.False(workspace.IsActionAvailable("9to1.Canvas.Ink.Red"));
        await workspace.DispatchAsync("9to1.Canvas.Tool.pen", null);
        Assert.Equal(CanvasPrimaryTool.Pen, tools.Selected);
        Assert.False(activation!.FocusOptions);
        await workspace.DispatchAsync("9to1.Canvas.Tool.pen", null);
        Assert.True(activation.FocusOptions);
        await workspace.DispatchAsync("9to1.Canvas.Ink.Red", null);
        await workspace.DispatchAsync("9to1.Canvas.Ink.Marker", null);
        await workspace.DispatchAsync("9to1.Canvas.Ink.Wider", null);
        Assert.Equal("#FFFF0000", tools.InkStyle.Color);
        Assert.Equal(CanvasRnoteInkKind.Marker, tools.InkStyle.Kind);
        Assert.Equal(3, tools.InkStyle.BaseWidth);
        Assert.Throws<ArgumentException>(() => workspace.DispatchAsync("9to1.Canvas.Ink.Red", "#FFFFFF"));
        Assert.Throws<InvalidOperationException>(() => workspace.DispatchAsync("9to1.Canvas.Tool.pan", null));
        Assert.Equal(CanvasPrimaryTool.Pen, tools.Selected);
        Assert.False(workspace.IsActionAvailable("9to1.Canvas.Tool.eraser"));
        Assert.Throws<NotSupportedException>(() => workspace.DispatchAsync("9to1.Canvas.Tool.eraser", null));
        Assert.Equal(bytes, CanvasArtifactCodec.Serialize(artifact));
    }

    [Fact]
    public void Common_pen_options_only_accept_supported_native_properties_and_capabilities_require_defined_reasons()
    {
        using var tools = new CanvasToolState(_ => new(true, ""), _ => { });
        var pen = CanvasToolState.Definitions.Single(definition => definition.Tool == CanvasPrimaryTool.Pen);
        Assert.Equal(new[] { "ink.kind", "ink.colour", "ink.width", "ink.opacity" }, pen.Options.Select(option => option.OptionId));
        Assert.Equal(new[] { "solid", "marker" }, pen.Options[0].Choices);
        var original = tools.InkStyle;
        Assert.Throws<ArgumentException>(() => tools.SetInkStyle(new(BaseWidth: double.NaN)));
        Assert.Same(original, tools.InkStyle);
        tools.SetInkStyle(new(CanvasRnoteInkKind.Marker, "#8000FF00", 18, 0.75));
        Assert.Equal(18, tools.InkStyle.BaseWidth);
        Assert.False(tools.Capability(CanvasPrimaryTool.AI).Available);
        using var invalidHost = new CanvasToolState(_ => new(false, ""), _ => { });
        Assert.Throws<InvalidOperationException>(() => invalidHost.Capability(CanvasPrimaryTool.Eraser));
    }

    [Fact]
    public void Canonical_scene_has_exact_seven_tool_order_and_one_options_host_above_toolbar()
    {
        var shell = Assert.Single(CanvasCuiWorkspace.LoadDocument().Components);
        var overlay = shell.Children.Single(component => component.AuthoredId == "canvas-floating-tools");
        Assert.Equal("canvas-tool-options", overlay.Children[0].AuthoredId);
        Assert.Equal("canvas-primary-toolbar", overlay.Children[1].AuthoredId);
        var buttons = Assert.Single(overlay.Children[1].Children).Children;
        Assert.Equal(new[] { "select", "pan", "pen", "eraser", "insert", "tools", "ai" },
            buttons.Select(button => button.AuthoredId!["canvas-tool-".Length..]));
        Assert.Equal(new[] { CanvasPrimaryTool.Select, CanvasPrimaryTool.Pan, CanvasPrimaryTool.Pen, CanvasPrimaryTool.Eraser,
            CanvasPrimaryTool.Insert, CanvasPrimaryTool.Tools, CanvasPrimaryTool.AI }, CanvasToolState.Definitions.Select(definition => definition.Tool));
    }
}
