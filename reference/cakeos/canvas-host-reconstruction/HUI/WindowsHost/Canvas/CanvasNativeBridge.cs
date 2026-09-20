using System.Runtime.InteropServices;
using System.Text;

namespace CakeOS.HuiWindowsHost.Canvas;

internal enum CanvasTool : uint
{
    Pen = 0,
    Highlighter = 1,
    Eraser = 2,
    Selector = 3,
    Shape = 4,
    Typewriter = 5,
    Tools = 6,
}

internal enum CanvasShape : uint
{
    Rectangle = 0,
    Ellipse = 1,
    Line = 2,
    Arrow = 3,
    Grid = 4,
    CoordSystem2D = 5,
    CoordSystem3D = 6,
    QuadrantCoordSystem2D = 7,
    FociEllipse = 8,
    QuadBez = 9,
    CubBez = 10,
    Polyline = 11,
    Polygon = 12,
}

internal enum CanvasBrushStyle : uint
{
    Marker = 0,
    Solid = 1,
    Textured = 2,
}

internal enum CanvasBrushBuilder : uint
{
    Simple = 0,
    Curved = 1,
    Modeled = 2,
}

internal enum CanvasShaperStyle : uint
{
    Smooth = 0,
    Rough = 1,
}

internal enum CanvasEraserStyle : uint
{
    TrashColliding = 0,
    SplitColliding = 1,
}

internal enum CanvasSelectorStyle : uint
{
    Polygon = 0,
    Rectangle = 1,
    Single = 2,
    IntersectingPath = 3,
}

internal enum CanvasToolsStyle : uint
{
    VerticalSpace = 0,
    OffsetCamera = 1,
    Zoom = 2,
    Laser = 3,
}

internal enum CanvasLayout : uint
{
    FixedSize = 0,
    ContinuousVertical = 1,
    SemiInfinite = 2,
    Infinite = 3,
}

internal enum CanvasPattern : uint
{
    None = 0,
    Lines = 1,
    Grid = 2,
    Dots = 3,
    IsometricGrid = 4,
    IsometricDots = 5,
}

internal enum CanvasDocExportFormat : uint
{
    Svg = 0,
    Pdf = 1,
    Xopp = 2,
}

internal readonly record struct CanvasDocumentBounds(double X, double Y, double Width, double Height);
internal readonly record struct CanvasSvgFrame(CanvasDocumentBounds Bounds, string Svg);
internal readonly record struct CanvasRgba(double R, double G, double B, double A);
internal readonly record struct CanvasFormatSize(double Width, double Height, double Dpi);

internal sealed class CanvasNativeSession : IDisposable
{
    private const uint ExpectedAbiVersion = 3;
    private const uint MinimumAbiVersion = 2;
    private const uint ExpectedRenderFormatSvg = 1;
    private const uint ExpectedCoordinateSpaceDocument = 1;

    private IntPtr _handle;
    private bool _disposed;

    public CanvasNativeSession()
    {
        ValidateAbi();
        _handle = Native.cake_canvas_engine_new();
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("Canvas native engine creation failed.");
    }

    private CanvasNativeSession(IntPtr handle)
    {
        ValidateAbi();
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("Canvas native engine restore returned a null handle.");
        _handle = handle;
    }

    ~CanvasNativeSession() => Dispose(disposing: false);

    public static uint AbiVersion => Native.cake_canvas_abi_version();

    /// <summary>True when the loaded native library exposes the v3 donor-parity surface.</summary>
    public static bool SupportsParityConfig
    {
        get
        {
            try { return Native.cake_canvas_abi_version() >= ExpectedAbiVersion; }
            catch { return false; }
        }
    }

    public bool CanUndo
    {
        get
        {
            ThrowIfDisposed();
            return Native.cake_canvas_can_undo(_handle) != 0;
        }
    }

    public bool CanRedo
    {
        get
        {
            ThrowIfDisposed();
            return Native.cake_canvas_can_redo(_handle) != 0;
        }
    }

    public static CanvasNativeSession FromRnote(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
            throw new ArgumentException("Canvas Rnote payload must not be empty.", nameof(bytes));

        ValidateAbi();
        var status = Native.cake_canvas_engine_from_rnote(bytes, (nuint)bytes.Length, out var handle);
        EnsureOk(status, "restore Rnote payload");
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("Canvas native Rnote restore succeeded without returning an engine handle.");

        return new CanvasNativeSession(handle);
    }

    public void SetTool(CanvasTool tool)
    {
        ThrowIfDisposed();
        EnsureOk(Native.cake_canvas_set_stroke_tool(_handle, (uint)tool), "set stroke tool");
    }

    public void SetShape(CanvasShape shape)
    {
        ThrowIfDisposed();
        EnsureOk(Native.cake_canvas_set_shape(_handle, (uint)shape), "set shape");
    }

    // ---- v3 donor-parity surface. Each method requires a native ABI-3 library;
    // against an ABI-2 library it throws a rebuild diagnostic instead of faking state. ----
    public void SetBrushStyle(CanvasBrushStyle style)
    {
        ThrowIfDisposed();
        RequireParity("set brush style");
        try { EnsureOk(Native.cake_canvas_set_brush_style(_handle, (uint)style), "set brush style"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set brush style", ex); }
    }

    public CanvasBrushStyle GetBrushStyle()
    {
        ThrowIfDisposed();
        RequireParity("get brush style");
        try
        {
            EnsureOk(Native.cake_canvas_get_brush_style(_handle, out var style), "get brush style");
            return (CanvasBrushStyle)style;
        }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("get brush style", ex); }
    }

    public void SetBrushBuilder(CanvasBrushBuilder builder)
    {
        ThrowIfDisposed();
        RequireParity("set brush builder");
        try { EnsureOk(Native.cake_canvas_set_brush_builder(_handle, (uint)builder), "set brush builder"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set brush builder", ex); }
    }

    public CanvasBrushBuilder GetBrushBuilder()
    {
        ThrowIfDisposed();
        RequireParity("get brush builder");
        try
        {
            EnsureOk(Native.cake_canvas_get_brush_builder(_handle, out var builder), "get brush builder");
            return (CanvasBrushBuilder)builder;
        }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("get brush builder", ex); }
    }

    public void SetStrokeWidth(double width)
    {
        ThrowIfDisposed();
        RequireParity("set stroke width");
        try { EnsureOk(Native.cake_canvas_set_stroke_width(_handle, width), "set stroke width"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set stroke width", ex); }
    }

    public double GetStrokeWidth()
    {
        ThrowIfDisposed();
        RequireParity("get stroke width");
        try
        {
            EnsureOk(Native.cake_canvas_get_stroke_width(_handle, out var width), "get stroke width");
            return width;
        }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("get stroke width", ex); }
    }

    public void SetStrokeColor(CanvasRgba color)
    {
        ThrowIfDisposed();
        RequireParity("set stroke color");
        try { EnsureOk(Native.cake_canvas_set_stroke_color(_handle, Rgba(color)), "set stroke color"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set stroke color", ex); }
    }

    public CanvasRgba GetStrokeColor()
    {
        ThrowIfDisposed();
        RequireParity("get stroke color");
        try
        {
            EnsureOk(Native.cake_canvas_get_stroke_color(_handle, out var color), "get stroke color");
            return new CanvasRgba(color.R, color.G, color.B, color.A);
        }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("get stroke color", ex); }
    }

    public void SetFillColor(CanvasRgba color)
    {
        ThrowIfDisposed();
        RequireParity("set fill color");
        try { EnsureOk(Native.cake_canvas_set_fill_color(_handle, Rgba(color)), "set fill color"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set fill color", ex); }
    }

    public CanvasRgba GetFillColor()
    {
        ThrowIfDisposed();
        RequireParity("get fill color");
        try
        {
            EnsureOk(Native.cake_canvas_get_fill_color(_handle, out var color), "get fill color");
            return new CanvasRgba(color.R, color.G, color.B, color.A);
        }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("get fill color", ex); }
    }

    public void SetEraserWidth(double width)
    {
        ThrowIfDisposed();
        RequireParity("set eraser width");
        try { EnsureOk(Native.cake_canvas_set_eraser_width(_handle, width), "set eraser width"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set eraser width", ex); }
    }

    public double GetEraserWidth()
    {
        ThrowIfDisposed();
        RequireParity("get eraser width");
        try
        {
            EnsureOk(Native.cake_canvas_get_eraser_width(_handle, out var width), "get eraser width");
            return width;
        }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("get eraser width", ex); }
    }

    public void SetEraserStyle(CanvasEraserStyle style)
    {
        ThrowIfDisposed();
        RequireParity("set eraser style");
        try { EnsureOk(Native.cake_canvas_set_eraser_style(_handle, (uint)style), "set eraser style"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set eraser style", ex); }
    }

    public CanvasEraserStyle GetEraserStyle()
    {
        ThrowIfDisposed();
        RequireParity("get eraser style");
        try
        {
            EnsureOk(Native.cake_canvas_get_eraser_style(_handle, out var style), "get eraser style");
            return (CanvasEraserStyle)style;
        }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("get eraser style", ex); }
    }

    public void SetShaperStyle(CanvasShaperStyle style)
    {
        ThrowIfDisposed();
        RequireParity("set shaper style");
        try { EnsureOk(Native.cake_canvas_set_shaper_style(_handle, (uint)style), "set shaper style"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set shaper style", ex); }
    }

    public CanvasShaperStyle GetShaperStyle()
    {
        ThrowIfDisposed();
        RequireParity("get shaper style");
        try
        {
            EnsureOk(Native.cake_canvas_get_shaper_style(_handle, out var style), "get shaper style");
            return (CanvasShaperStyle)style;
        }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("get shaper style", ex); }
    }

    public void SetShaperConstraintsEnabled(bool enabled)
    {
        ThrowIfDisposed();
        RequireParity("set shaper constraints");
        try { EnsureOk(Native.cake_canvas_set_shaper_constraints_enabled(_handle, enabled ? (byte)1 : (byte)0), "set shaper constraints"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set shaper constraints", ex); }
    }

    public bool GetShaperConstraintsEnabled()
    {
        ThrowIfDisposed();
        RequireParity("get shaper constraints");
        try
        {
            EnsureOk(Native.cake_canvas_get_shaper_constraints_enabled(_handle, out var enabled), "get shaper constraints");
            return enabled != 0;
        }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("get shaper constraints", ex); }
    }

    public void SetSelectorStyle(CanvasSelectorStyle style)
    {
        ThrowIfDisposed();
        RequireParity("set selector style");
        try { EnsureOk(Native.cake_canvas_set_selector_style(_handle, (uint)style), "set selector style"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set selector style", ex); }
    }

    public CanvasSelectorStyle GetSelectorStyle()
    {
        ThrowIfDisposed();
        RequireParity("get selector style");
        try
        {
            EnsureOk(Native.cake_canvas_get_selector_style(_handle, out var style), "get selector style");
            return (CanvasSelectorStyle)style;
        }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("get selector style", ex); }
    }

    public void SetSelectorLockAspect(bool locked)
    {
        ThrowIfDisposed();
        RequireParity("set selector aspect lock");
        try { EnsureOk(Native.cake_canvas_set_selector_lock_aspect(_handle, locked ? (byte)1 : (byte)0), "set selector aspect lock"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set selector aspect lock", ex); }
    }

    public bool GetSelectorLockAspect()
    {
        ThrowIfDisposed();
        RequireParity("get selector aspect lock");
        try
        {
            EnsureOk(Native.cake_canvas_get_selector_lock_aspect(_handle, out var locked), "get selector aspect lock");
            return locked != 0;
        }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("get selector aspect lock", ex); }
    }

    public void SetToolsStyle(CanvasToolsStyle style)
    {
        ThrowIfDisposed();
        RequireParity("set tools style");
        try { EnsureOk(Native.cake_canvas_set_tools_style(_handle, (uint)style), "set tools style"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set tools style", ex); }
    }

    public CanvasToolsStyle GetToolsStyle()
    {
        ThrowIfDisposed();
        RequireParity("get tools style");
        try
        {
            EnsureOk(Native.cake_canvas_get_tools_style(_handle, out var style), "get tools style");
            return (CanvasToolsStyle)style;
        }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("get tools style", ex); }
    }

    public void SetTypewriterFontSize(double size)
    {
        ThrowIfDisposed();
        RequireParity("set typewriter font size");
        try { EnsureOk(Native.cake_canvas_set_typewriter_font_size(_handle, size), "set typewriter font size"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set typewriter font size", ex); }
    }

    public double GetTypewriterFontSize()
    {
        ThrowIfDisposed();
        RequireParity("get typewriter font size");
        try
        {
            EnsureOk(Native.cake_canvas_get_typewriter_font_size(_handle, out var size), "get typewriter font size");
            return size;
        }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("get typewriter font size", ex); }
    }

    public void SetTypewriterTextWidth(double width)
    {
        ThrowIfDisposed();
        RequireParity("set typewriter text width");
        try { EnsureOk(Native.cake_canvas_set_typewriter_text_width(_handle, width), "set typewriter text width"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set typewriter text width", ex); }
    }

    public double GetTypewriterTextWidth()
    {
        ThrowIfDisposed();
        RequireParity("get typewriter text width");
        try
        {
            EnsureOk(Native.cake_canvas_get_typewriter_text_width(_handle, out var width), "get typewriter text width");
            return width;
        }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("get typewriter text width", ex); }
    }

    public void SetLayout(CanvasLayout layout)
    {
        ThrowIfDisposed();
        RequireParity("set layout");
        try { EnsureOk(Native.cake_canvas_set_layout(_handle, (uint)layout), "set layout"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set layout", ex); }
    }

    public CanvasLayout GetLayout()
    {
        ThrowIfDisposed();
        RequireParity("get layout");
        try
        {
            EnsureOk(Native.cake_canvas_get_layout(_handle, out var layout), "get layout");
            return (CanvasLayout)layout;
        }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("get layout", ex); }
    }

    public void SetBackgroundPattern(CanvasPattern pattern)
    {
        ThrowIfDisposed();
        RequireParity("set background pattern");
        try { EnsureOk(Native.cake_canvas_set_background_pattern(_handle, (uint)pattern), "set background pattern"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set background pattern", ex); }
    }

    public CanvasPattern GetBackgroundPattern()
    {
        ThrowIfDisposed();
        RequireParity("get background pattern");
        try
        {
            EnsureOk(Native.cake_canvas_get_background_pattern(_handle, out var pattern), "get background pattern");
            return (CanvasPattern)pattern;
        }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("get background pattern", ex); }
    }

    public void SetBackgroundColor(CanvasRgba color)
    {
        ThrowIfDisposed();
        RequireParity("set background color");
        try { EnsureOk(Native.cake_canvas_set_background_color(_handle, Rgba(color)), "set background color"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set background color", ex); }
    }

    public CanvasRgba GetBackgroundColor()
    {
        ThrowIfDisposed();
        RequireParity("get background color");
        try
        {
            EnsureOk(Native.cake_canvas_get_background_color(_handle, out var color), "get background color");
            return new CanvasRgba(color.R, color.G, color.B, color.A);
        }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("get background color", ex); }
    }

    public void SetPatternColor(CanvasRgba color)
    {
        ThrowIfDisposed();
        RequireParity("set pattern color");
        try { EnsureOk(Native.cake_canvas_set_pattern_color(_handle, Rgba(color)), "set pattern color"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set pattern color", ex); }
    }

    public void SetPatternSize(double width, double height)
    {
        ThrowIfDisposed();
        RequireParity("set pattern size");
        try { EnsureOk(Native.cake_canvas_set_pattern_size(_handle, width, height), "set pattern size"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set pattern size", ex); }
    }

    public void SetFormatSize(double width, double height)
    {
        ThrowIfDisposed();
        RequireParity("set format size");
        try { EnsureOk(Native.cake_canvas_set_format_size(_handle, width, height), "set format size"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set format size", ex); }
    }

    public CanvasFormatSize GetFormatSize()
    {
        ThrowIfDisposed();
        RequireParity("get format size");
        try
        {
            EnsureOk(Native.cake_canvas_get_format_size(_handle, out var size), "get format size");
            return new CanvasFormatSize(size.Width, size.Height, size.Dpi);
        }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("get format size", ex); }
    }

    public void SetFormatDpi(double dpi)
    {
        ThrowIfDisposed();
        RequireParity("set format DPI");
        try { EnsureOk(Native.cake_canvas_set_format_dpi(_handle, dpi), "set format DPI"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set format DPI", ex); }
    }

    public void SetSnapPositions(bool snap)
    {
        ThrowIfDisposed();
        RequireParity("set snap positions");
        try { EnsureOk(Native.cake_canvas_set_snap_positions(_handle, snap ? (byte)1 : (byte)0), "set snap positions"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set snap positions", ex); }
    }

    public void SetExportPrefs(bool withBackground, bool withPattern, bool optimizePrinting)
    {
        ThrowIfDisposed();
        RequireParity("set export prefs");
        try { EnsureOk(Native.cake_canvas_set_export_prefs(_handle, B(withBackground), B(withPattern), B(optimizePrinting)), "set export prefs"); }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("set export prefs", ex); }
    }

    public byte[] ExportDoc(CanvasDocExportFormat format)
    {
        ThrowIfDisposed();
        RequireParity("export document");
        try
        {
            var status = Native.cake_canvas_export_doc(_handle, (uint)format, out var buffer);
            EnsureOk(status, "export document");
            try
            {
                if (buffer.Data == IntPtr.Zero || buffer.Length == 0)
                    throw new InvalidOperationException("Canvas native bridge returned an empty export payload.");
                if (buffer.Length > int.MaxValue)
                    throw new InvalidOperationException("Canvas export payload is too large for the managed boundary.");
                var bytes = new byte[(int)buffer.Length];
                Marshal.Copy(buffer.Data, bytes, 0, bytes.Length);
                return bytes;
            }
            finally { Native.cake_canvas_buffer_release(ref buffer); }
        }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("export document", ex); }
    }

    /// <summary>Exports the current selector selection as SVG, or null when nothing is selected.</summary>
    public byte[]? TryExportSelectionSvg()
    {
        ThrowIfDisposed();
        RequireParity("export selection");
        try
        {
            var status = Native.cake_canvas_export_selection_svg(_handle, out var buffer);
            if (status == CanvasStatus.NoChange)
                return null;
            EnsureOk(status, "export selection");
            try
            {
                if (buffer.Data == IntPtr.Zero || buffer.Length == 0)
                    return null;
                if (buffer.Length > int.MaxValue)
                    throw new InvalidOperationException("Canvas selection payload is too large for the managed boundary.");
                var bytes = new byte[(int)buffer.Length];
                Marshal.Copy(buffer.Data, bytes, 0, bytes.Length);
                return bytes;
            }
            finally { Native.cake_canvas_buffer_release(ref buffer); }
        }
        catch (EntryPointNotFoundException ex) { throw ParityRebuild("export selection", ex); }
    }

    public void SetViewportSize(double width, double height)
    {
        ThrowIfDisposed();
        EnsureOk(Native.cake_canvas_set_viewport_size(_handle, width, height), "set viewport size");
    }

    public void ZoomTo(double zoom)
    {
        ThrowIfDisposed();
        EnsureOk(Native.cake_canvas_zoom_to(_handle, zoom), "zoom");
    }

    public void PanBy(double deltaX, double deltaY)
    {
        ThrowIfDisposed();
        EnsureOk(Native.cake_canvas_pan_by(_handle, deltaX, deltaY), "pan");
    }

    public void BeginStroke(double x, double y, double pressure, double tiltX = 0, double tiltY = 0)
    {
        ThrowIfDisposed();
        EnsureOk(Native.cake_canvas_begin_stroke(_handle, Sample(x, y, pressure, tiltX, tiltY)), "begin stroke");
    }

    public void UpdateStroke(double x, double y, double pressure, double tiltX = 0, double tiltY = 0)
    {
        ThrowIfDisposed();
        EnsureOk(Native.cake_canvas_update_stroke(_handle, Sample(x, y, pressure, tiltX, tiltY)), "update stroke");
    }

    public void EndStroke(double x, double y, double pressure, double tiltX = 0, double tiltY = 0)
    {
        ThrowIfDisposed();
        EnsureOk(Native.cake_canvas_end_stroke(_handle, Sample(x, y, pressure, tiltX, tiltY)), "end stroke");
    }

    public bool Undo()
    {
        ThrowIfDisposed();
        return EnsureHistoryResult(Native.cake_canvas_undo(_handle), "undo");
    }

    public bool Redo()
    {
        ThrowIfDisposed();
        return EnsureHistoryResult(Native.cake_canvas_redo(_handle), "redo");
    }

    public CanvasSvgFrame RenderSvg()
    {
        ThrowIfDisposed();
        var status = Native.cake_canvas_render_frame(_handle, out var frame);
        EnsureOk(status, "render frame");

        try
        {
            if (frame.Format != ExpectedRenderFormatSvg)
                throw new InvalidOperationException($"Canvas native bridge returned unsupported render format {frame.Format}.");
            if (frame.CoordinateSpace != ExpectedCoordinateSpaceDocument)
                throw new InvalidOperationException($"Canvas native bridge returned unsupported coordinate space {frame.CoordinateSpace}.");
            if (frame.Data == IntPtr.Zero || frame.Length == 0)
                throw new InvalidOperationException("Canvas native bridge returned an empty SVG frame.");
            if (frame.Length > int.MaxValue)
                throw new InvalidOperationException("Canvas SVG frame is too large for the managed preview boundary.");

            var bytes = new byte[(int)frame.Length];
            Marshal.Copy(frame.Data, bytes, 0, bytes.Length);
            var svg = Encoding.UTF8.GetString(bytes);
            if (!svg.Contains("<svg", StringComparison.Ordinal))
                throw new InvalidOperationException("Canvas native frame was not valid SVG text.");

            return new CanvasSvgFrame(
                new CanvasDocumentBounds(frame.X, frame.Y, frame.Width, frame.Height),
                svg);
        }
        finally
        {
            Native.cake_canvas_render_frame_release(ref frame);
        }
    }

    public byte[] SaveRnote()
    {
        ThrowIfDisposed();
        var status = Native.cake_canvas_save_rnote(_handle, out var buffer);
        EnsureOk(status, "save Rnote payload");

        try
        {
            if (buffer.Data == IntPtr.Zero || buffer.Length == 0)
                throw new InvalidOperationException("Canvas native bridge returned an empty Rnote payload.");
            if (buffer.Length > int.MaxValue)
                throw new InvalidOperationException("Canvas Rnote payload is too large for the managed preview boundary.");

            var bytes = new byte[(int)buffer.Length];
            Marshal.Copy(buffer.Data, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            Native.cake_canvas_buffer_release(ref buffer);
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle == IntPtr.Zero) return;
        Native.cake_canvas_engine_free(_handle);
        _handle = IntPtr.Zero;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void ValidateAbi()
    {
        uint abi;
        try { abi = Native.cake_canvas_abi_version(); }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException("Canvas native library 'cakeos_canvas_rnote_poc' was not found. Build apps/canvas/rnote-poc for this platform first.", ex);
        }
        if (abi < MinimumAbiVersion || abi > ExpectedAbiVersion)
            throw new InvalidOperationException($"Canvas native ABI mismatch. Expected {MinimumAbiVersion}..{ExpectedAbiVersion}, got {abi}.");
    }

    private static void RequireParity(string operation)
    {
        if (!SupportsParityConfig)
            throw new InvalidOperationException($"Canvas native {operation} requires a native library at ABI {ExpectedAbiVersion} (loaded library is older). Rebuild apps/canvas/rnote-poc and retry; no state was faked.");
    }

    private static InvalidOperationException ParityRebuild(string operation, Exception inner) =>
        new($"Canvas native {operation} is not exported by the loaded native library. Rebuild apps/canvas/rnote-poc at ABI {ExpectedAbiVersion} and retry; no state was faked.", inner);

    private static NativeRgba Rgba(CanvasRgba color) => new() { R = color.R, G = color.G, B = color.B, A = color.A };
    private static byte B(bool value) => value ? (byte)1 : (byte)0;

    private static NativePointerSample Sample(double x, double y, double pressure, double tiltX, double tiltY) => new()
    {
        X = x,
        Y = y,
        Pressure = pressure,
        TiltX = tiltX,
        TiltY = tiltY,
    };

    private static bool EnsureHistoryResult(CanvasStatus status, string operation) => status switch
    {
        CanvasStatus.Ok => true,
        CanvasStatus.NoChange => false,
        _ => throw new InvalidOperationException($"Canvas native {operation} failed with status {(int)status} ({status})."),
    };

    private static void EnsureOk(CanvasStatus status, string operation)
    {
        if (status != CanvasStatus.Ok)
            throw new InvalidOperationException($"Canvas native {operation} failed with status {(int)status} ({status}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePointerSample
    {
        public double X;
        public double Y;
        public double Pressure;
        public double TiltX;
        public double TiltY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRgba
    {
        public double R;
        public double G;
        public double B;
        public double A;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFormatSize
    {
        public double Width;
        public double Height;
        public double Dpi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRenderFrame
    {
        public uint Format;
        public uint CoordinateSpace;
        public double X;
        public double Y;
        public double Width;
        public double Height;
        public IntPtr Data;
        public nuint Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBuffer
    {
        public IntPtr Data;
        public nuint Length;
    }

    private enum CanvasStatus : int
    {
        Ok = 0,
        NoChange = 1,
        InvalidHandle = -1,
        InvalidArgument = -2,
        InvalidState = -3,
        EngineError = -4,
        Panic = -5,
    }

    private static class Native
    {
        private const string Library = "cakeos_canvas_rnote_poc";

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint cake_canvas_abi_version();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr cake_canvas_engine_new();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void cake_canvas_engine_free(IntPtr handle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_engine_from_rnote(
            [In] byte[] data,
            nuint len,
            out IntPtr outHandle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_stroke_tool(IntPtr handle, uint tool);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_shape(IntPtr handle, uint shape);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_brush_style(IntPtr handle, uint style);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_get_brush_style(IntPtr handle, out uint style);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_brush_builder(IntPtr handle, uint builder);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_get_brush_builder(IntPtr handle, out uint builder);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_stroke_width(IntPtr handle, double width);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_get_stroke_width(IntPtr handle, out double width);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_stroke_color(IntPtr handle, NativeRgba color);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_get_stroke_color(IntPtr handle, out NativeRgba color);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_fill_color(IntPtr handle, NativeRgba color);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_get_fill_color(IntPtr handle, out NativeRgba color);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_eraser_width(IntPtr handle, double width);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_get_eraser_width(IntPtr handle, out double width);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_eraser_style(IntPtr handle, uint style);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_get_eraser_style(IntPtr handle, out uint style);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_shaper_style(IntPtr handle, uint style);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_get_shaper_style(IntPtr handle, out uint style);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_shaper_constraints_enabled(IntPtr handle, byte enabled);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_get_shaper_constraints_enabled(IntPtr handle, out byte enabled);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_selector_style(IntPtr handle, uint style);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_get_selector_style(IntPtr handle, out uint style);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_selector_lock_aspect(IntPtr handle, byte locked);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_get_selector_lock_aspect(IntPtr handle, out byte locked);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_tools_style(IntPtr handle, uint style);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_get_tools_style(IntPtr handle, out uint style);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_typewriter_font_size(IntPtr handle, double size);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_get_typewriter_font_size(IntPtr handle, out double size);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_typewriter_text_width(IntPtr handle, double width);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_get_typewriter_text_width(IntPtr handle, out double width);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_layout(IntPtr handle, uint layout);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_get_layout(IntPtr handle, out uint layout);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_background_pattern(IntPtr handle, uint pattern);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_get_background_pattern(IntPtr handle, out uint pattern);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_background_color(IntPtr handle, NativeRgba color);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_get_background_color(IntPtr handle, out NativeRgba color);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_pattern_color(IntPtr handle, NativeRgba color);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_pattern_size(IntPtr handle, double width, double height);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_format_size(IntPtr handle, double width, double height);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_get_format_size(IntPtr handle, out NativeFormatSize size);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_format_dpi(IntPtr handle, double dpi);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_snap_positions(IntPtr handle, byte snap);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_export_prefs(IntPtr handle, byte withBackground, byte withPattern, byte optimizePrinting);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_export_doc(IntPtr handle, uint format, out NativeBuffer buffer);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_export_selection_svg(IntPtr handle, out NativeBuffer buffer);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_viewport_size(IntPtr handle, double width, double height);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_zoom_to(IntPtr handle, double zoom);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_pan_by(IntPtr handle, double deltaX, double deltaY);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_begin_stroke(IntPtr handle, NativePointerSample sample);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_update_stroke(IntPtr handle, NativePointerSample sample);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_end_stroke(IntPtr handle, NativePointerSample sample);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte cake_canvas_can_undo(IntPtr handle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte cake_canvas_can_redo(IntPtr handle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_undo(IntPtr handle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_redo(IntPtr handle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_render_frame(IntPtr handle, out NativeRenderFrame frame);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void cake_canvas_render_frame_release(ref NativeRenderFrame frame);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_save_rnote(IntPtr handle, out NativeBuffer buffer);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void cake_canvas_buffer_release(ref NativeBuffer buffer);
    }
}
