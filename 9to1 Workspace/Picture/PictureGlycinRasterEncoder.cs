using System.Runtime.InteropServices;
using HavenOS.Home.Core;
using Microsoft.Win32.SafeHandles;

namespace HavenOS.Images;

/// <summary>
/// Explicit flattened-frame raster encoding through the genuine sandboxed donor creator.
/// This pure byte transform neither overwrites a source nor saves an editable Picture
/// document. The owning Files export transaction remains responsible for destination authority.
/// </summary>
public enum PictureRasterExportFormat { Png, Jpeg, WebP, Tiff, Bmp, Tga, Ico }

public sealed class PictureGlycinRasterEncoder
{
    public byte[] EncodeFlattenedFrame(HomeProductivityRasterFrame frame, PictureRasterExportFormat format, byte compression = 50, byte quality = 90,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!OperatingSystem.IsLinux() || IntPtr.Size != 8)
            throw new PlatformNotSupportedException("This controlled Glycin creator ABI is verified only for 64-bit Linux.");
        if (compression > 100) throw new ArgumentOutOfRangeException(nameof(compression));
        if (quality > 100) throw new ArgumentOutOfRangeException(nameof(quality));
        var mime = format switch {
            PictureRasterExportFormat.Png => "image/png", PictureRasterExportFormat.Jpeg => "image/jpeg",
            PictureRasterExportFormat.WebP => "image/webp", PictureRasterExportFormat.Tiff => "image/tiff",
            PictureRasterExportFormat.Bmp => "image/bmp", PictureRasterExportFormat.Tga => "image/x-tga",
            PictureRasterExportFormat.Ico => "image/vnd.microsoft.icon", _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        if (format == PictureRasterExportFormat.Ico && (frame.Width > 256 || frame.Height > 256))
            throw new NotSupportedException("ICO export requires an explicitly resized frame no larger than 256 by 256 pixels.");
        cancellationToken.ThrowIfCancellationRequested();
        var channels = format == PictureRasterExportFormat.Jpeg ? 3 : 4;
        var captured = frame.CopyPixels();
        var rgba = new byte[checked(frame.Width * frame.Height * channels)];
        try
        {
            for (var y = 0; y < frame.Height; y++)
                for (var x = 0; x < frame.Width; x++)
                {
                    var source = y * frame.Stride + x * 4;
                    var target = (y * frame.Width + x) * channels;
                    var alpha = captured[source + 3];
                    if (captured[source] > alpha || captured[source + 1] > alpha || captured[source + 2] > alpha)
                        throw new InvalidDataException("The shared frame contains invalid premultiplied alpha samples.");
                    rgba[target] = Straight(captured[source + 2], alpha);
                    rgba[target + 1] = Straight(captured[source + 1], alpha);
                    rgba[target + 2] = Straight(captured[source], alpha);
                    if (channels == 4) rgba[target + 3] = alpha;
                    else if (alpha != 255)
                        throw new NotSupportedException("JPEG cannot retain alpha; explicitly composite a background before this export.");
                }
            using var creator = new ObjectHandle(Require(Native.gly_creator_new(mime, out var createError), createError, "initialise " + mime + " creator"));
            RequireBubblewrap(creator);
            if (format == PictureRasterExportFormat.Png && Native.gly_creator_set_encoding_compression(creator, compression) == 0)
                throw new NotSupportedException("The configured PNG creator cannot honor the requested compression setting.");
            if (format == PictureRasterExportFormat.Jpeg && Native.gly_creator_set_encoding_quality(creator, quality) == 0)
                throw new NotSupportedException("The configured JPEG creator cannot honor the requested encoding quality.");
            using var texture = new BytesHandle(Native.g_bytes_new(rgba, (nuint)rgba.Length));
            using var added = new ObjectHandle(Require(Native.gly_creator_add_frame(creator, (uint)frame.Width, (uint)frame.Height,
                channels == 3 ? 7 : 5, texture, out var frameError), frameError, "add straight raster frame"));
            cancellationToken.ThrowIfCancellationRequested();
            using var encoded = new ObjectHandle(Require(Native.gly_creator_create(creator, out var encodeError), encodeError, "encode " + mime));
            cancellationToken.ThrowIfCancellationRequested();
            using var data = new BytesHandle(Native.gly_encoded_image_get_data(encoded)); // transfer-full
            var pointer = Native.g_bytes_get_data(data, out var length);
            if (pointer == IntPtr.Zero || length is 0 || length > PictureGlycinDecoder.MaximumBufferBytes)
                throw new InvalidDataException("The donor raster output is empty or exceeds the export byte limit.");
            var result = new byte[checked((int)length)];
            Marshal.Copy(pointer, result, 0, result.Length);
            if (format == PictureRasterExportFormat.Png && !result.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            { Array.Clear(result); throw new InvalidDataException("The donor creator returned a different output format."); }
            return result;
        }
        finally { Array.Clear(captured); Array.Clear(rgba); }
    }

    private static byte Straight(byte component, byte alpha) => alpha == 0 ? (byte)0 : checked((byte)((component * 255 + alpha / 2) / alpha));

    private static void RequireBubblewrap(ObjectHandle creator)
    {
        // The pinned libglycin C creator setter incorrectly casts GlyCreator as
        // GlyLoader and disagrees with its header return type. Use the creator's
        // real, typed GObject property and verify it; never call that broken ABI.
        // glycin-core/src/gobject/creator.rs applies this property before create().
        var value = new GValue();
        Native.g_value_init(ref value, Native.gly_sandbox_selector_get_type());
        try
        {
            Native.g_value_set_enum(ref value, 1);
            Native.g_object_set_property(creator, "sandbox-selector", ref value);
            Native.g_value_set_enum(ref value, 0); // a missing/failed getter must not reuse the requested value
            Native.g_object_get_property(creator, "sandbox-selector", ref value);
            if (Native.g_value_get_enum(ref value) != 1)
                throw new UnauthorizedAccessException("The raster creator did not retain mandatory bubblewrap sandboxing.");
        }
        finally { Native.g_value_unset(ref value); }
    }

    private static IntPtr Require(IntPtr result, IntPtr error, string operation)
    {
        if (result != IntPtr.Zero && error == IntPtr.Zero) return result;
        if (result != IntPtr.Zero) Native.g_object_unref(result);
        var detail = "No native detail";
        if (error != IntPtr.Zero)
        {
            try { detail = Marshal.PtrToStringUTF8(Marshal.PtrToStructure<GError>(error).Message) ?? detail; }
            finally { Native.g_error_free(error); }
        }
        throw new IOException($"Sandboxed Glycin could not {operation}: {detail}");
    }

    [StructLayout(LayoutKind.Sequential)] private struct GValue { public nuint Type; public ulong Data0; public ulong Data1; }
    [StructLayout(LayoutKind.Sequential)] private struct GError { public uint Domain; public int Code; public IntPtr Message; }
    private sealed class ObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ObjectHandle(IntPtr pointer) : base(true) { if (pointer == IntPtr.Zero) throw new IOException("Native creator object is unavailable."); SetHandle(pointer); }
        protected override bool ReleaseHandle() { Native.g_object_unref(handle); return true; }
    }
    private sealed class BytesHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public BytesHandle(IntPtr pointer) : base(true) { if (pointer == IntPtr.Zero) throw new IOException("Native creator bytes are unavailable."); SetHandle(pointer); }
        protected override bool ReleaseHandle() { Native.g_bytes_unref(handle); return true; }
    }
    private static class Native
    {
        private const string Glycin = "libglycin.so";
        private const string Glib = "libglib-2.0.so.0";
        private const string Gobject = "libgobject-2.0.so.0";
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gly_creator_new([MarshalAs(UnmanagedType.LPUTF8Str)] string mime, out IntPtr error);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern nuint gly_sandbox_selector_get_type();
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern int gly_creator_set_encoding_quality(ObjectHandle creator, byte quality);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern int gly_creator_set_encoding_compression(ObjectHandle creator, byte compression);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gly_creator_add_frame(ObjectHandle creator, uint width, uint height, int format, BytesHandle texture, out IntPtr error);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gly_creator_create(ObjectHandle creator, out IntPtr error);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gly_encoded_image_get_data(ObjectHandle encoded);
        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr g_bytes_new(byte[] bytes, nuint length);
        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr g_bytes_get_data(BytesHandle bytes, out nuint length);
        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] internal static extern void g_bytes_unref(IntPtr bytes);
        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] internal static extern void g_error_free(IntPtr error);
        [DllImport(Gobject, CallingConvention = CallingConvention.Cdecl)] internal static extern void g_object_unref(IntPtr value);
        [DllImport(Gobject, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr g_value_init(ref GValue value, nuint type);
        [DllImport(Gobject, CallingConvention = CallingConvention.Cdecl)] internal static extern void g_value_set_enum(ref GValue value, int item);
        [DllImport(Gobject, CallingConvention = CallingConvention.Cdecl)] internal static extern int g_value_get_enum(ref GValue value);
        [DllImport(Gobject, CallingConvention = CallingConvention.Cdecl)] internal static extern void g_value_unset(ref GValue value);
        [DllImport(Gobject, CallingConvention = CallingConvention.Cdecl)] internal static extern void g_object_set_property(ObjectHandle value, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref GValue property);
        [DllImport(Gobject, CallingConvention = CallingConvention.Cdecl)] internal static extern void g_object_get_property(ObjectHandle value, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref GValue property);
    }
}
