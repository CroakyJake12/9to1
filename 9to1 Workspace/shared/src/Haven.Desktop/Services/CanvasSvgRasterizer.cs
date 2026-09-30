using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

namespace Haven.Desktop.Services;

public sealed record CanvasRasterFrame(int Width, int Height, int Stride, byte[] PremultipliedBgra);

/// <summary>Linux vector viewport backend. SVG is an operation rendering, never the Canvas source document.</summary>
public static class CanvasSvgRasterizer
{
    private const int MaximumSvgBytes = 16 * 1024 * 1024;
    private const int MaximumDimension = 4096;

    public static CanvasRasterFrame Render(byte[] svg, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(svg);
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The librsvg Canvas viewport is not configured on this platform.");
        if (width is < 1 or > MaximumDimension || height is < 1 or > MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(width), "Canvas viewport dimensions must be between 1 and 4096 pixels.");
        var captured = svg.ToArray();
        ValidateVectorDocument(captured);
        IntPtr error = IntPtr.Zero;
        var handle = Native.rsvg_handle_new_from_data(captured, (nuint)captured.Length, out error);
        if (error != IntPtr.Zero) Native.g_error_free(error);
        if (handle == IntPtr.Zero) throw new InvalidDataException("The native viewport could not parse the Canvas SVG frame.");
        try
        {
            var surface = Native.cairo_image_surface_create(0, width, height); // Cairo ARGB32 is native-endian premultiplied BGRA on supported little-endian Linux.
            if (surface == IntPtr.Zero) throw new InvalidOperationException("The native Canvas raster surface is unavailable.");
            try
            {
                if (!BitConverter.IsLittleEndian || Native.cairo_surface_status(surface) != 0)
                    throw new InvalidOperationException("The native Canvas raster pixel format or surface is unavailable.");
                var context = Native.cairo_create(surface);
                if (context == IntPtr.Zero) throw new InvalidOperationException("The native Canvas raster context is unavailable.");
                try
                {
                    var viewport = new Rectangle { Width = width, Height = height };
                    error = IntPtr.Zero;
                    var rendered = Native.rsvg_handle_render_document(handle, context, ref viewport, out error);
                    if (error != IntPtr.Zero) Native.g_error_free(error);
                    if (rendered == 0 || Native.cairo_status(context) != 0)
                        throw new InvalidDataException("The native viewport could not render the Canvas vector frame.");
                    Native.cairo_surface_flush(surface);
                    var stride = Native.cairo_image_surface_get_stride(surface);
                    var data = Native.cairo_image_surface_get_data(surface);
                    if (stride < checked(width * 4) || stride > checked(MaximumDimension * 4) || data == IntPtr.Zero)
                        throw new InvalidDataException("The native viewport returned invalid Canvas raster dimensions.");
                    var bytes = new byte[checked(stride * height)];
                    Marshal.Copy(data, bytes, 0, bytes.Length);
                    return new(width, height, stride, bytes);
                }
                finally { Native.cairo_destroy(context); }
            }
            finally { Native.cairo_surface_destroy(surface); }
        }
        finally { Native.g_object_unref(handle); }
    }

    private static void ValidateVectorDocument(byte[] svg)
    {
        if (svg.Length is 0 or > MaximumSvgBytes) throw new InvalidDataException("The Canvas vector frame exceeds supported limits.");
        try
        {
            using var stream = new MemoryStream(svg, false);
            using var reader = XmlReader.Create(stream, new() { DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null, MaxCharactersInDocument = MaximumSvgBytes });
            var rootSeen = false;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.ProcessingInstruction)
                    throw new InvalidDataException("External processing instructions are not Canvas vector content.");
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (!rootSeen)
                {
                    rootSeen = true;
                    if (reader.LocalName != "svg" || reader.NamespaceURI != "http://www.w3.org/2000/svg")
                        throw new InvalidDataException("The Canvas frame is not an SVG document.");
                }
                if (reader.LocalName is "image" or "style" or "foreignObject" or "script")
                    throw new NotSupportedException("This Canvas frame requires the resource-aware image or stylesheet renderer.");
                if (!reader.HasAttributes) continue;
                while (reader.MoveToNextAttribute())
                {
                    if (reader.LocalName == "href" && !reader.Value.StartsWith('#'))
                        throw new NotSupportedException("External Canvas frame resources require their owning Files leases.");
                    var value = reader.Value;
                    if (value.Contains('\\') || value.Contains("@import", StringComparison.OrdinalIgnoreCase))
                        throw new NotSupportedException("External or escaped CSS resource references are unavailable in the vector renderer.");
                    var offset = 0;
                    while ((offset = value.IndexOf("url(", offset, StringComparison.OrdinalIgnoreCase)) >= 0)
                    {
                        var end = value.IndexOf(')', offset + 4);
                        if (end < 0 || !value[(offset + 4)..end].Trim().Trim('\'', '"').StartsWith('#'))
                            throw new NotSupportedException("External Canvas paint resources require their owning Files leases.");
                        offset = end + 1;
                    }
                }
                reader.MoveToElement();
            }
            if (!rootSeen) throw new InvalidDataException("The Canvas vector frame has no document root.");
        }
        catch (XmlException error) { throw new InvalidDataException("The Canvas vector frame is malformed.", error); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle { public double X; public double Y; public double Width; public double Height; }

    private static class Native
    {
        [DllImport("librsvg-2.so.2", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr rsvg_handle_new_from_data(byte[] data, nuint length, out IntPtr error);
        [DllImport("librsvg-2.so.2", CallingConvention = CallingConvention.Cdecl)] public static extern int rsvg_handle_render_document(IntPtr handle, IntPtr context, ref Rectangle viewport, out IntPtr error);
        [DllImport("libgobject-2.0.so.0", CallingConvention = CallingConvention.Cdecl)] public static extern void g_object_unref(IntPtr value);
        [DllImport("libglib-2.0.so.0", CallingConvention = CallingConvention.Cdecl)] public static extern void g_error_free(IntPtr error);
        [DllImport("libcairo.so.2", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr cairo_image_surface_create(int format, int width, int height);
        [DllImport("libcairo.so.2", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr cairo_create(IntPtr surface);
        [DllImport("libcairo.so.2", CallingConvention = CallingConvention.Cdecl)] public static extern int cairo_surface_status(IntPtr surface);
        [DllImport("libcairo.so.2", CallingConvention = CallingConvention.Cdecl)] public static extern int cairo_status(IntPtr context);
        [DllImport("libcairo.so.2", CallingConvention = CallingConvention.Cdecl)] public static extern void cairo_surface_flush(IntPtr surface);
        [DllImport("libcairo.so.2", CallingConvention = CallingConvention.Cdecl)] public static extern int cairo_image_surface_get_stride(IntPtr surface);
        [DllImport("libcairo.so.2", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr cairo_image_surface_get_data(IntPtr surface);
        [DllImport("libcairo.so.2", CallingConvention = CallingConvention.Cdecl)] public static extern void cairo_destroy(IntPtr context);
        [DllImport("libcairo.so.2", CallingConvention = CallingConvention.Cdecl)] public static extern void cairo_surface_destroy(IntPtr surface);
    }
}
