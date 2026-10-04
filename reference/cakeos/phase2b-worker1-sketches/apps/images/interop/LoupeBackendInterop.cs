using System.Runtime.InteropServices;

namespace CakeOS.Images.Interop;

public static class LoupeBackendInterop
{
    private const string Library = "cakeos_images_backend";

    public const uint ExpectedAbiVersion = 1;

    public enum Status : int
    {
        Ok = 0,
        InvalidHandle = -1,
        InvalidArgument = -2,
        InvalidState = -3,
        DecodeError = -4,
        RenderError = -5,
        MetadataError = -6,
        ColorManagementError = -7,
        IoError = -8,
        Panic = -9,
    }

    public enum TransformOp : uint
    {
        Rotate90 = 0,
        Rotate180 = 1,
        Rotate270 = 2,
        FlipHorizontal = 3,
        FlipVertical = 4,
        ExifAuto = 5,
    }

    public enum RenderIntent : int
    {
        Perceptual = 0,
        RelativeColorimetric = 1,
        Saturation = 2,
        AbsoluteColorimetric = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ImageInfo
    {
        public int Width;
        public int Height;
        public int HasAlpha;
        public IntPtr ColorSpace;
        public IntPtr Format;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Metadata
    {
        public IntPtr ExifJson;
        public IntPtr XmpJson;
        public IntPtr IptcJson;
        public int Width;
        public int Height;
        public int Orientation;
        public IntPtr ColorProfile;
        public double DpiX;
        public double DpiY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Viewport
    {
        public double X;
        public double Y;
        public double Scale;
        public int TargetWidth;
        public int TargetHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RenderResult
    {
        public IntPtr CairoSurface;
        public int Width;
        public int Height;
        public int Stride;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ThumbnailResult
    {
        public IntPtr Data;
        public nuint Length;
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TransformResult
    {
        public IntPtr NewHandle;
        public int Lossless;
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint cake_images_abi_version();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr cake_images_decode(string path, out ImageInfo info);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern void cake_images_free(IntPtr handle);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern Status cake_images_render(IntPtr handle, Viewport viewport, out RenderResult result);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern Status cake_images_metadata_read(IntPtr handle, out Metadata metadata);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern void cake_images_metadata_free(ref Metadata metadata);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern Status cake_images_thumbnail(string path, int maxPx, out ThumbnailResult result);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern void cake_images_thumbnail_free(ref ThumbnailResult result);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern Status cake_images_transform_apply(IntPtr handle, TransformOp op, out TransformResult result);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern Status cake_images_color_profile_apply(IntPtr handle, string profilePath, int intent);

    public static string? PtrToStringUtf8(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
            return null;
        return Marshal.PtrToStringUTF8(ptr);
    }

    public static void FreeString(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero)
            Marshal.FreeHGlobal(ptr);
    }
}