using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HavenOS.Images;

public sealed record PictureGlycinCicp(byte Primaries, byte Transfer, byte Matrix, byte FullRange);
public sealed record PictureGlycinFrame(uint Width, uint Height, uint Stride, byte[] BgraPremultipliedPixels,
    long DelayMicroseconds, int ColorMode, byte[]? IccProfile, PictureGlycinCicp? Cicp);

/// <summary>
/// Controlled libglycin C ABI with bounded individual frame materialization. Native package,
/// sandboxed loaders and colour-aware display must pass real acceptance before
/// this is composed into Picture's production source renderer.
/// </summary>
public sealed class PictureGlycinDecoder
{
    public const string DonorRevision = "84bed7782d1ae4486068a9ffbde691290c119909";
    public const int MaximumBufferBytes = 256 * 1024 * 1024;

    public PictureGlycinFrame DecodeFirstFrame(ReadOnlySpan<byte> encoded)
        => DecodeFirstFrame(encoded, CancellationToken.None);
    public PictureGlycinFrame DecodeFirstFrame(ReadOnlySpan<byte> encoded, CancellationToken cancellationToken)
    {
        using var session = OpenFrames(encoded, true, cancellationToken);
        return session.NextFrame(cancellationToken);
    }

    /// <summary>
    /// Opens one real sandboxed donor decoder. NextFrame follows donor frame order
    /// and loops for animation; still images permit one frame. The caller owns each
    /// returned pixel copy. This session never accumulates previous decoded frames.
    /// </summary>
    public FrameSession OpenFrames(ReadOnlySpan<byte> encoded) => OpenFrames(encoded, loopAnimation: true);
    public FrameSession OpenFrames(ReadOnlySpan<byte> encoded, bool loopAnimation) => FrameSession.Create(encoded, loopAnimation, CancellationToken.None);
    public FrameSession OpenFrames(ReadOnlySpan<byte> encoded, bool loopAnimation, CancellationToken cancellationToken) =>
        FrameSession.Create(encoded, loopAnimation, cancellationToken);

    public sealed class FrameSession : IDisposable
    {
        private readonly object _gate = new();
        private readonly GBytesHandle _bytes;
        private readonly GObjectHandle _loader;
        private readonly GObjectHandle _image;
        private readonly GObjectHandle _cancellable;
        private int _nativeOperationActive;
        private bool _disposed;
        private bool _ended;
        private const int NoMoreFramesError = 2; // GlyLoaderError from the pinned public C header.
        private readonly bool _loopAnimation;
        private FrameSession(GBytesHandle bytes, GObjectHandle loader, GObjectHandle image, GObjectHandle cancellable, bool loopAnimation)
        { _bytes = bytes; _loader = loader; _image = image; _cancellable = cancellable; _loopAnimation = loopAnimation; }
        /// <summary>Reports native frame-call activity for owning job/progress surfaces; this is not a resource grant.</summary>
        public bool IsNativeOperationActive => Volatile.Read(ref _nativeOperationActive) != 0;

        public string MimeType
        {
            get
            {
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    var mime = Marshal.PtrToStringUTF8(Native.gly_image_get_mime_type(_image));
                    if (string.IsNullOrWhiteSpace(mime) || mime.Length > 128 || !mime.Contains('/') || mime.Any(char.IsControl))
                        throw new InvalidDataException("The donor returned no valid detected MIME type.");
                    return mime;
                }
            }
        }

        internal static FrameSession Create(ReadOnlySpan<byte> encoded, bool loopAnimation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This libglycin package has not been built and verified for this platform.");
            if (encoded.IsEmpty || encoded.Length > MaximumBufferBytes)
                throw new ArgumentException("Picture encoded image is empty or exceeds the native materialization limit.", nameof(encoded));
            var captured = encoded.ToArray();
            GBytesHandle bytes;
            try { bytes = new GBytesHandle(Native.g_bytes_new(captured, (nuint)captured.Length)); }
            finally { Array.Clear(captured); }
            GObjectHandle? loader = null;
            GObjectHandle? cancellable = null;
            try
            {
                loader = new GObjectHandle(Native.gly_loader_new_for_bytes(bytes));
                cancellable = new GObjectHandle(Native.g_cancellable_new());
                BindCancellation(loader, cancellable);
                // Mandatory real Linux sandbox; namespace/loader failures never downgrade.
                Native.gly_loader_set_sandbox_selector(loader, 1);
                Native.gly_loader_set_accepted_memory_formats(loader, 1u); // premultiplied BGRA
                Native.gly_loader_set_color_convert_icc_srgb(loader, 1);
                using var registration = cancellationToken.Register(static state => Native.g_cancellable_cancel((GObjectHandle)state!), cancellable);
                var pointer = Native.gly_loader_load(loader, out var error);
                ThrowIfNativeCancelled(pointer, error, cancellable, cancellationToken);
                var image = new GObjectHandle(RequireSuccess(pointer, error, "load"));
                return new(bytes, loader, image, cancellable, loopAnimation);
            }
            catch { loader?.Dispose(); cancellable?.Dispose(); bytes.Dispose(); throw; }
        }

        /// <summary>Cancellation reaches the donor's GCancellable. A session cancelled during native work must be reopened.</summary>
        public PictureGlycinFrame NextFrame(CancellationToken cancellationToken = default) =>
            TryNextFrame(cancellationToken) ?? throw new EndOfStreamException("The non-looping image has no more frames.");

        public PictureGlycinFrame? TryNextFrame(CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                if (Native.g_cancellable_is_cancelled(_cancellable) != 0)
                    throw new OperationCanceledException("The native decoder session was cancelled; reopen its retained source.", cancellationToken);
                if (_ended) return null;
                using var request = new GObjectHandle(Native.gly_frame_request_new());
                Native.gly_frame_request_set_loop_animation(request, _loopAnimation ? 1 : 0);
                using var registration = cancellationToken.Register(static state => Native.g_cancellable_cancel((GObjectHandle)state!), _cancellable);
                IntPtr pointer, error;
                Interlocked.Exchange(ref _nativeOperationActive, 1);
                try { pointer = Native.gly_image_get_specific_frame(_image, request, out error); }
                finally { Interlocked.Exchange(ref _nativeOperationActive, 0); }
                ThrowIfNativeCancelled(pointer, error, _cancellable, cancellationToken);
                if (!_loopAnimation && pointer == IntPtr.Zero && error != IntPtr.Zero)
                {
                    var detail = Marshal.PtrToStructure<GError>(error);
                    if (detail.Domain == Native.gly_loader_error_quark() && detail.Code == NoMoreFramesError)
                    {
                        Native.g_error_free(error);
                        _ended = true;
                        cancellationToken.ThrowIfCancellationRequested();
                        return null;
                    }
                }
                using var frame = new GObjectHandle(RequireSuccess(pointer, error, "next frame"));
                var result = ReadFrame(frame);
                if (cancellationToken.IsCancellationRequested || Native.g_cancellable_is_cancelled(_cancellable) != 0)
                {
                    PictureFilesSourceRenderer.ClearFrame(result);
                    throw new OperationCanceledException("The native decoder session was cancelled; reopen its retained source.", cancellationToken);
                }
                return result;
            }
        }

        public void Dispose()
        {
            // GCancellable cancellation is thread-safe and intentionally happens before waiting for the frame lock.
            // It interrupts an in-flight donor request so closing a surface need not wait for a full decode.
            try { Native.g_cancellable_cancel(_cancellable); }
            catch (ObjectDisposedException) { }
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _image.Dispose(); _loader.Dispose(); _cancellable.Dispose(); _bytes.Dispose();
            }
        }
    }

    private static void BindCancellation(GObjectHandle loader, GObjectHandle cancellable)
    {
        var value = new GValue();
        Native.g_value_init(ref value, Native.g_cancellable_get_type());
        try
        {
            Native.g_value_set_object(ref value, cancellable.DangerousGetHandle());
            Native.g_object_set_property(loader, "cancellable", ref value);
            Native.g_value_set_object(ref value, IntPtr.Zero);
            Native.g_object_get_property(loader, "cancellable", ref value);
            if (Native.g_value_get_object(ref value) != cancellable.DangerousGetHandle())
                throw new InvalidOperationException("The native loader did not retain the required cancellation object.");
        }
        finally { Native.g_value_unset(ref value); }
    }

    private static void ThrowIfNativeCancelled(IntPtr result, IntPtr error, GObjectHandle cancellable, CancellationToken token)
    {
        if (!token.IsCancellationRequested && Native.g_cancellable_is_cancelled(cancellable) == 0) return;
        if (result != IntPtr.Zero) Native.g_object_unref(result);
        if (error != IntPtr.Zero) Native.g_error_free(error);
        throw new OperationCanceledException("The sandboxed native decoder operation was cancelled.", token);
    }

    private static PictureGlycinFrame ReadFrame(GObjectHandle frame)
    {
        var width = Native.gly_frame_get_width(frame);
        var height = Native.gly_frame_get_height(frame);
        var stride = Native.gly_frame_get_stride(frame);
        if (Native.gly_frame_get_memory_format(frame) != 0)
            throw new NotSupportedException("libglycin returned a memory format outside the requested BGRA premultiplied contract.");
        ValidateLayout(width, height, stride);
        // Frame buffer is transfer-none: keep frame alive through the copy;
        // do not unref a borrowed GBytes pointer.
        var pixels = CopyBytes(Native.gly_frame_get_buf_bytes(frame), MaximumBufferBytes);
        byte[]? icc = null;
        try
        {
            // The donor allows the last row to omit trailing stride padding.
            // Validate the required pixels without rejecting that valid layout.
            if ((ulong)stride * (height - 1) + (ulong)width * 4 > (ulong)pixels.Length)
                throw new InvalidDataException("libglycin frame bytes do not cover its declared rows and pixels.");
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
        catch
        {
            Array.Clear(pixels);
            if (icc is not null) Array.Clear(icc);
            throw;
        }
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
    [StructLayout(LayoutKind.Sequential)] private struct GValue { public nuint Type; public ulong Data0; public ulong Data1; }
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
        private const string Gobject = "libgobject-2.0.so.0";
        private const string Gio = "libgio-2.0.so.0";
        [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr g_cancellable_new();
        [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)] internal static extern nuint g_cancellable_get_type();
        [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)] internal static extern void g_cancellable_cancel(GObjectHandle cancellable);
        [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)] internal static extern int g_cancellable_is_cancelled(GObjectHandle cancellable);
        [DllImport(Gobject, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr g_value_init(ref GValue value, nuint type);
        [DllImport(Gobject, CallingConvention = CallingConvention.Cdecl)] internal static extern void g_value_set_object(ref GValue value, IntPtr item);
        [DllImport(Gobject, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr g_value_get_object(ref GValue value);
        [DllImport(Gobject, CallingConvention = CallingConvention.Cdecl)] internal static extern void g_value_unset(ref GValue value);
        [DllImport(Gobject, CallingConvention = CallingConvention.Cdecl)] internal static extern void g_object_set_property(GObjectHandle value, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref GValue property);
        [DllImport(Gobject, CallingConvention = CallingConvention.Cdecl)] internal static extern void g_object_get_property(GObjectHandle value, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref GValue property);
        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr g_bytes_new(byte[] bytes, nuint length);
        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr g_bytes_get_data(IntPtr bytes, out nuint length);
        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] internal static extern void g_bytes_unref(IntPtr bytes);
        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] internal static extern void g_error_free(IntPtr error);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gly_frame_request_new();
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern void gly_frame_request_set_loop_animation(GObjectHandle request, int loopAnimation);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gly_image_get_specific_frame(GObjectHandle image, GObjectHandle request, out IntPtr error);
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern uint gly_loader_error_quark();
        [DllImport(Glycin, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gly_image_get_mime_type(GObjectHandle image);
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
