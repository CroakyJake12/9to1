using System.Runtime.InteropServices;
using System.Text;

namespace CakeOS.Canvas.App;

public enum CanvasTool : uint
{
    Pen = 0,
    Highlighter = 1,
    Eraser = 2,
    Selector = 3,
    Shape = 4,
}

public enum CanvasShape : uint
{
    Rectangle = 0,
    Ellipse = 1,
    Line = 2,
    Arrow = 3,
}

public enum CanvasEraserStyle : uint
{
    Trash = 0,
    Split = 1,
}

public readonly record struct CanvasRgba(double R, double G, double B, double A);

public readonly record struct CanvasPenStyle(CanvasRgba Color, double Width);

public readonly record struct CanvasDocumentBounds(double X, double Y, double Width, double Height);

public readonly record struct CanvasSvgFrame(CanvasDocumentBounds Bounds, string Svg);

/// <summary>Seam for unit-testing controller logic without the native library.</summary>
public interface ICanvasSession : IDisposable
{
    bool CanUndo { get; }
    bool CanRedo { get; }
    void SetTool(CanvasTool tool);
    void SetShape(CanvasShape shape);
    void SetPenStyle(CanvasTool tool, CanvasRgba color, double width);
    void SetEraser(double width, CanvasEraserStyle style);
    void SetViewportSize(double width, double height);
    void ZoomTo(double zoom);
    void PanBy(double deltaX, double deltaY);
    void BeginStroke(double x, double y, double pressure, double tiltX = 0, double tiltY = 0);
    void UpdateStroke(double x, double y, double pressure, double tiltX = 0, double tiltY = 0);
    void EndStroke(double x, double y, double pressure, double tiltX = 0, double tiltY = 0);
    bool Undo();
    bool Redo();
    CanvasSvgFrame RenderSvg();
    byte[] SaveRnote();
}

/// <summary>Managed boundary over the real Rnote engine (ABI v3).</summary>
public sealed class CanvasNativeSession : ICanvasSession
{
    private const uint ExpectedAbiVersion = 3;
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

    public static ICanvasSession CreateNew() => new CanvasNativeSession();

    public static ICanvasSession FromRnote(byte[] bytes)
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

    public void SetPenStyle(CanvasTool tool, CanvasRgba color, double width)
    {
        ThrowIfDisposed();
        EnsureOk(
            Native.cake_canvas_set_pen_style(_handle, (uint)tool, color.R, color.G, color.B, color.A, width),
            "set pen style");
    }

    public void SetEraser(double width, CanvasEraserStyle style)
    {
        ThrowIfDisposed();
        EnsureOk(Native.cake_canvas_set_eraser(_handle, width, (uint)style), "set eraser");
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
        var abi = Native.cake_canvas_abi_version();
        if (abi != ExpectedAbiVersion)
            throw new InvalidOperationException($"Canvas native ABI mismatch. Expected {ExpectedAbiVersion}, got {abi}.");
    }

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
        internal static extern CanvasStatus cake_canvas_set_pen_style(
            IntPtr handle, uint tool, double red, double green, double blue, double alpha, double width);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern CanvasStatus cake_canvas_set_eraser(IntPtr handle, double width, uint style);

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
