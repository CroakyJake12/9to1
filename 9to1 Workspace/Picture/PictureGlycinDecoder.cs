using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HavenOS.Images;

public sealed record PictureGlycinCicp(byte Primaries, byte Transfer, byte Matrix, byte FullRange);
public sealed record PictureGlycinFrame(uint Width, uint Height, uint Stride, byte[] BgraPremultipliedPixels,
    long DelayMicroseconds, int ColorMode, byte[]? IccProfile, PictureGlycinCicp? Cicp);

/// <summary>
/// Controlled libglycin C ABI, first-frame materialization only. Native package,
/// sandboxed loaders and colour-aware display must pass real acceptance before
/// this is composed into Picture's production source renderer.
/// </summary>
public sealed class PictureGlycinDecoder
{
    public const string DonorRevision = "84bed7782d1ae4486068a9ffbde691290c119909";
    public const int MaximumBufferBytes = 256 * 1024 * 1024;

    public PictureGlycinFrame DecodeFirstFrame(ReadOnlySpan<byte> encoded)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This libglycin package has not been built and verified for this platform.");
        if (encoded.IsEmpty || encoded.Length > MaximumBufferBytes) throw new ArgumentException("Picture encoded image is empty or exceeds the native materialization limit.", nameof(encoded));
        var captured = encoded.ToArray();
        using var bytes = new GBytesHandle(Native.g_bytes_new(captured, (nuint)captured.Length));
        using var loader = new GObjectHandle(Native.gly_loader_new_for_bytes(bytes));
        // Require the real Linux sandbox. Never downgrade to NOT_SANDBOXED
        // when a namespace, bubblewrap or loader is unavailable.
        Native.gly_loader_set_sandbox_selector(loader, 1);
        Native.gly_loader_set_accepted_memory_formats(loader, 1u); // B8G8R8A8 premultiplied
        Native.gly_loader_set_color_convert_icc_srgb(loader, 1);
        using var image = new GObjectHandle(RequireSuccess(Native.gly_loader_load(loader, out var loadError), loadError, "load"));
        using var frame = new GObjectHandle(RequireSuccess(Native.gly_image_next_frame(image, out var frameError), frameError, "first frame"));
        var width = Native.gly_frame_get_width(frame);
        var height = Native.gly_frame_get_height(frame);
        var stride = Native.gly_frame_get_stride(frame);
        if (Native.gly_frame_get_memory_format(frame) != 0)
            throw new NotSupportedException("libglycin returned a memory format outside the requested BGRA premultiplied contract.");
        ValidateLayout(width, height, stride);
        // Frame buffer is transfer-none: keep frame alive through the copy;
        // do not unref a borrowed GBytes pointer.
        var pixels = CopyBytes(Native.gly_frame_get_buf_bytes(frame), MaximumBufferBytes);
        // The donor allows the last row to omit trailing stride padding.
        // Validate the required pixels without rejecting that valid layout.
        if ((ulong)stride * (height - 1) + (ulong)width * 4 > (ulong)pixels.Length)
            throw new InvalidDataException("libglycin frame bytes do not cover its declared rows and pixels.");
        byte[]? icc = null;
        var iccPointer = Native.gly_frame_get_color_icc_profile(frame); // transfer-full
        if (iccPointer != IntPtr.Zero)
        {
            using var profile = new GBytesHandle(iccPointer);
            icc = CopyBytes(profile.DangerousGetHandle(), 16 * 1024 * 1024);
        }
        PictureGlycinCicp? cicp = null;
        var cicpPointer = Native.gly_frame_get_color_cicp(frame); // transfer-full
        if (cicpPointer != IntPtr.Zero)
        {
            try { cicp = new(Marshal.ReadByte(cicpPointer, 0), Marshal.ReadByte(cicpPointer, 1), Marshal.ReadByte(cicpPointer, 2), Marshal.ReadByte(cicpPointer, 3)); }
            finally { Native.gly_cicp_free(cicpPointer); }
        }
        var colorMode = Native.gly_frame_get_color_mode(frame);
        if (colorMode is < 1 or > 3 || colorMode == 2 && cicp is null || colorMode == 3 && icc is null)
            throw new InvalidDataException("libglycin colour mode has no corresponding declared colour information.");
        return new(width, height, stride, pixels, Native.gly_frame_get_delay(frame), colorMode, icc, cicp);
    }

    internal static void ValidateLayout(uint width, uint height, uint stride)
    {
        if (width == 0 || height == 0 || (ulong)stride < (ulong)width * 4 || (ulong)stride * height > MaximumBufferBytes)
            throw new InvalidDataException("libglycin frame dimensions or stride exceed the bounded BGRA materialization contract.");
    }

    private static byte[] CopyBytes(IntPtr bytes, int limit)
    {
        if (bytes == IntPtr.Zero) throw new InvalidDataException("libglycin returned no required buffer.");
        var pointer = Native.g_bytes_get_data(bytes, out var length);
        if (length == 0 || length > (nuint)limit || pointer == IntPtr.Zero)
            throw new InvalidDataException("libglycin buffer is empty or exceeds its materialization limit.");
        var result = new byte[checked((int)length)];
        Marshal.Copy(pointer, result, 0, result.Length);
        return result;
    }

    private static IntPtr RequireSuccess(IntPtr result, IntPtr error, string operation)
    {
        if (error == IntPtr.Zero && result != IntPtr.Zero) return result;
        if (result != IntPtr.Zero) Native.g_object_unref(result);
        string message = "No native error detail";
        if (error != IntPtr.Zero)
        {
            try { message = Marshal.PtrToStringUTF8(Marshal.PtrToStructure<GError>(error).Message) ?? message; }
            finally { Native.g_error_free(error); }
        }
        throw new IOException($"Sandboxed libglycin {operation} failed: {message}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GError { public uint Domain; public int Code; public IntPtr Message; }
    private sealed class GObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public GObjectHandle(IntPtr pointer) : base(true)
        { if (pointer == IntPtr.Zero) throw new IOException("libglycin failed to allocate a required native object."); SetHandle(pointer); }
        protected override bool ReleaseHandle() { Native.g_object_unref(handle); return true; }
    }
    private sealed class GBytesHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public GBytesHandle(IntPtr pointer) : base(true)
        { if (pointer == IntPtr.Zero) throw new IOException("GLib failed to allocate a required byte buffer."); SetHandle(pointer); }
        protected override bool ReleaseHandle() { Native.g_bytes_unref(handle); return true; }
    }

    private static class Native
    {
        private const string Glycin = "libglycin.so";
        private const string Glib = "libglib-2.0.so.0";
        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr g_bytes_new(byte[] bytes, nuint length);
        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr g_bytes_get_data(IntPtr bytes, out nuint length);
        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] internal static extern void g_bytes_unref(IntPtr bytes);
        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] internal static extern void g_error_free(IntPtr error);
        [DllImport("libgobject-2.0.so.0", CallingConvention = CallingConvention.Cdecl)] internal static extern void g_object_unref(IntPtr value);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gly_loader_new_for_bytes(GBytesHandle bytes);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern void gly_loader_set_sandbox_selector(GObjectHandle loader, int selector);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern void gly_loader_set_accepted_memory_formats(GObjectHandle loader, uint formats);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern void gly_loader_set_color_convert_icc_srgb(GObjectHandle loader, int enabled);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gly_loader_load(GObjectHandle loader, out IntPtr error);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gly_image_next_frame(GObjectHandle image, out IntPtr error);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern uint gly_frame_get_width(GObjectHandle frame);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern uint gly_frame_get_height(GObjectHandle frame);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern uint gly_frame_get_stride(GObjectHandle frame);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern int gly_frame_get_memory_format(GObjectHandle frame);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gly_frame_get_buf_bytes(GObjectHandle frame);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern long gly_frame_get_delay(GObjectHandle frame);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern int gly_frame_get_color_mode(GObjectHandle frame);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gly_frame_get_color_icc_profile(GObjectHandle frame);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gly_frame_get_color_cicp(GObjectHandle frame);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern void gly_cicp_free(IntPtr cicp);
    }
}
