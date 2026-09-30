using System.Collections.Immutable;

namespace HavenOS.Apps.Canvas;

public enum CanvasPrimaryTool { Select, Pan, Pen, Eraser, Insert, Tools, AI }
public enum CanvasToolOptionKind { Choice, Colour, Number }
public sealed record CanvasToolOptionDefinition(string OptionId, string DisplayName, CanvasToolOptionKind Kind,
    ImmutableArray<string> Choices = default, double? Minimum = null, double? Maximum = null);
public sealed record CanvasToolDefinition(CanvasPrimaryTool Tool, string ToolId, string DisplayName, string Icon,
    string? Shortcut, ImmutableArray<string> InputModes, string Cursor,
    ImmutableArray<CanvasToolOptionDefinition> Options, ImmutableArray<string> CapabilityRequirements);
public sealed record CanvasToolCapability(bool Available, string Reason)
{
    public static CanvasToolCapability Unavailable(string reason) => new(false, reason);
}
public sealed record CanvasToolSelection(CanvasPrimaryTool Tool, bool FocusOptions);

/// <summary>Transient editor state. The owning native host declares live input capabilities; this does not grant resource authority.</summary>
public sealed class CanvasToolState : IDisposable
{
    public static ImmutableArray<CanvasToolDefinition> Definitions { get; } =
    [
        Define(CanvasPrimaryTool.Select, "select", "Select", "cursor", "V", "default", [], "canvas.selection"),
        Define(CanvasPrimaryTool.Pan, "pan", "Pan", "hand", "H", "grab", [], "canvas.viewport.pan"),
        Define(CanvasPrimaryTool.Pen, "pen", "Pen", "pen", "P", "crosshair",
        [new("ink.kind", "Ink engine", CanvasToolOptionKind.Choice, ["solid", "marker"]),
         new("ink.colour", "Colour", CanvasToolOptionKind.Colour),
         new("ink.width", "Width", CanvasToolOptionKind.Number, Minimum: 0.5, Maximum: 200),
         new("ink.opacity", "Opacity", CanvasToolOptionKind.Number, Minimum: 0, Maximum: 1)], "canvas.ink.rnote"),
        Define(CanvasPrimaryTool.Eraser, "eraser", "Eraser", "eraser", "E", "crosshair",
        [new("eraser.mode", "Eraser mode", CanvasToolOptionKind.Choice, ["natural", "quick"])], "canvas.erase.history"),
        Define(CanvasPrimaryTool.Insert, "insert", "Insert", "plus", null, "default", [], "canvas.shared.insert"),
        Define(CanvasPrimaryTool.Tools, "tools", "Tools", "tools", null, "default", [], "canvas.donor.tools"),
        Define(CanvasPrimaryTool.AI, "ai", "AI", "sparkles", null, "default", [], "canvas.ai.shared"),
    ];

    private readonly Func<CanvasPrimaryTool, CanvasToolCapability> _capability;
    private readonly Action<CanvasToolSelection> _activate;
    private readonly CanvasAiSurfaceState? _ai;
    private bool _disposed;

    public CanvasToolState(Func<CanvasPrimaryTool, CanvasToolCapability> capability,
        Action<CanvasToolSelection> activate, CanvasAiSurfaceState? ai = null)
    {
        _capability = capability ?? throw new ArgumentNullException(nameof(capability));
        _activate = activate ?? throw new ArgumentNullException(nameof(activate));
        _ai = ai;
        if (_ai is not null) _ai.Changed += AiChanged;
    }

    public CanvasPrimaryTool Selected { get; private set; } = CanvasPrimaryTool.Select;
    public CanvasRnoteInkStyle InkStyle { get; private set; } = CanvasRnoteInkStyle.Default;
    public bool OptionsFocused { get; private set; }
    public CanvasAiSurfaceState? Ai => _ai;
    public event EventHandler? Changed;

    public CanvasToolCapability Capability(CanvasPrimaryTool tool)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enum.IsDefined(tool)) return CanvasToolCapability.Unavailable("Unknown Canvas tool");
        if (tool == CanvasPrimaryTool.AI && _ai is null) return CanvasToolCapability.Unavailable("Shared Home AI coordinator is unavailable");
        var result = _capability(tool) ?? throw new InvalidOperationException("The native tool capability resolver returned no result.");
        if (!result.Available && string.IsNullOrWhiteSpace(result.Reason))
            throw new InvalidOperationException("Unavailable Canvas tools require a capability reason.");
        return result;
    }

    public void Select(CanvasPrimaryTool tool)
    {
        var capability = Capability(tool);
        if (!capability.Available) throw new NotSupportedException(capability.Reason);
        var focus = tool == Selected && !Definitions[(int)tool].Options.IsEmpty;
        // Host activation runs first. A failed input-mode change must not leave
        // presentation claiming a tool that its surface did not activate.
        _activate(new(tool, focus));
        if (tool == CanvasPrimaryTool.AI) _ai!.SelectAiTool(); else _ai?.SelectOtherTool();
        Selected = tool;
        OptionsFocused = focus;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetInkStyle(CanvasRnoteInkStyle style)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(style);
        style.ValidateAndResolve();
        InkStyle = style;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshCapabilities() => Changed?.Invoke(this, EventArgs.Empty);

    private static CanvasToolDefinition Define(CanvasPrimaryTool tool, string id, string name, string icon,
        string? shortcut, string cursor, ImmutableArray<CanvasToolOptionDefinition> options, string capability)
        => new(tool, $"9to1.Canvas.Tool.{id}", name, icon, shortcut, ["mouse", "touch", "stylus", "keyboard"], cursor, options, [capability]);
    private void AiChanged(object? sender, EventArgs args) => Changed?.Invoke(this, EventArgs.Empty);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ai is not null) _ai.Changed -= AiChanged;
        // Neither the shared AI session nor the native host is owned here.
    }
}
