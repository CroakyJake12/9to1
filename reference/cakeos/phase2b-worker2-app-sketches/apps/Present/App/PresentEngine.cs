using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace HavenOS.Apps.Present;

public enum PixelFormat
{
    Rgba = 0,
    Bgra = 1
}

public enum KeyEventType
{
    Input = 0,
    Up = 1
}

public enum MouseEventType
{
    ButtonDown = 0,
    ButtonUp = 1,
    Move = 2
}

[StructLayout(LayoutKind.Sequential)]
public struct SlideInfo
{
    public int Index;
    [MarshalAs(UnmanagedType.LPStr)]
    public string Name;
    [MarshalAs(UnmanagedType.LPStr)]
    public string Hash;
}

[StructLayout(LayoutKind.Sequential)]
public struct SlideExtent
{
    public long WidthTwips;
    public long HeightTwips;
}

[StructLayout(LayoutKind.Sequential)]
public struct ElementSnapshot
{
    public int SlideIndex;
    public int ObjectIndex;
    public IntPtr TextArray;
    public int TextCount;
}

[StructLayout(LayoutKind.Sequential)]
public struct TileRequest
{
    public int SlideIndex;
    public int PixelWidth;
    public int PixelHeight;
    public int TileXTwips;
    public int TileYTwips;
    public int TileWidthTwips;
    public int TileHeightTwips;
}

[StructLayout(LayoutKind.Sequential)]
public struct RenderedTile
{
    public int PixelWidth;
    public int PixelHeight;
    public PixelFormat PixelFormat;
    public IntPtr Pixels;
    public int PixelsLength;
}

[StructLayout(LayoutKind.Sequential)]
public struct EngineEvent
{
    public int UpstreamType;
    [MarshalAs(UnmanagedType.LPStr)]
    public string Payload;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void EventCallbackDelegate(ref EngineEvent evt);

public sealed class PresentEngine : IDisposable
{
    private IntPtr _handle;
    private EventCallbackDelegate? _eventCallback;
    private readonly object _lock = new();

    public PresentEngine(string libreOfficeProgramPath = "/usr/lib/libreoffice/program", string? userProfileUrl = null)
    {
        var options = new NativeEngineOptions
        {
            LibreOfficeProgramPath = libreOfficeProgramPath ?? "/usr/lib/libreoffice/program",
            UserProfileUrl = userProfileUrl ?? ""
        };
        _handle = NativeMethods.present_engine_create(ref options);
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create PresentEngine");
    }

    public void Open(string documentPathOrUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPathOrUrl);
        NativeMethods.present_engine_open(_handle, documentPathOrUrl);
    }

    public void Close()
    {
        NativeMethods.present_engine_close(_handle);
    }

    public bool IsOpen()
    {
        return NativeMethods.present_engine_is_open(_handle);
    }

    public IReadOnlyList<SlideInfo> GetSlides()
    {
        int count = NativeMethods.present_engine_slides_count(_handle);
        var slides = new List<SlideInfo>(count);
        for (int i = 0; i < count; i++)
        {
            var slide = new SlideInfo();
            NativeMethods.present_engine_get_slide(_handle, i, ref slide);
            slides.Add(slide);
        }
        return slides;
    }

    public int CurrentSlide()
    {
        return NativeMethods.present_engine_current_slide(_handle);
    }

    public void SetCurrentSlide(int slideIndex)
    {
        NativeMethods.present_engine_set_current_slide(_handle, slideIndex);
    }

    public SlideExtent GetSlideExtent(int slideIndex)
    {
        var extent = new SlideExtent();
        NativeMethods.present_engine_slide_extent(_handle, slideIndex, ref extent);
        return extent;
    }

    public bool SupportsElementSnapshots()
    {
        return NativeMethods.present_engine_supports_element_snapshots(_handle);
    }

    public IReadOnlyList<ElementSnapshot> GetElementSnapshot(string documentPathOrUrl, int slideIndex)
    {
        int count = NativeMethods.present_engine_element_snapshot_count(_handle, documentPathOrUrl, slideIndex);
        var elements = new List<ElementSnapshot>(count);
        for (int i = 0; i < count; i++)
        {
            var elem = new ElementSnapshot();
            NativeMethods.present_engine_get_element_snapshot(_handle, documentPathOrUrl, slideIndex, i, ref elem);
            elements.Add(elem);
        }
        return elements;
    }

    public void SelectElement(string snapshotPathOrUrl, int slideIndex, int objectIndex)
    {
        NativeMethods.present_engine_select_element(_handle, snapshotPathOrUrl, slideIndex, objectIndex);
    }

    public void ClearElementSelection(int slideIndex, int objectIndex)
    {
        NativeMethods.present_engine_clear_element_selection(_handle, slideIndex, objectIndex);
    }

    public bool ReplaceElementText(string snapshotPathOrUrl, int slideIndex, int objectIndex, string text)
    {
        return NativeMethods.present_engine_replace_element_text(_handle, snapshotPathOrUrl, slideIndex, objectIndex, text);
    }

    public void AddSlideAfter(int slideIndex)
    {
        NativeMethods.present_engine_add_slide_after(_handle, slideIndex);
    }

    public void DuplicateSlide(int slideIndex)
    {
        NativeMethods.present_engine_duplicate_slide(_handle, slideIndex);
    }

    public void DeleteSlide(int slideIndex)
    {
        NativeMethods.present_engine_delete_slide(_handle, slideIndex);
    }

    public void MoveSlide(int fromIndex, int toIndex)
    {
        NativeMethods.present_engine_move_slide(_handle, fromIndex, toIndex);
    }

    public void Undo()
    {
        NativeMethods.present_engine_undo(_handle);
    }

    public void Redo()
    {
        NativeMethods.present_engine_redo(_handle);
    }

    public RenderedTile RenderTile(TileRequest request)
    {
        var tile = new RenderedTile();
        NativeMethods.present_engine_render_tile(_handle, ref request, ref tile);
        return tile;
    }

    public void PostKeyEvent(KeyEventType type, int charCode, int keyCode)
    {
        NativeMethods.present_engine_post_key_event(_handle, type, charCode, keyCode);
    }

    public void PostMouseEvent(MouseEventType type, int xTwips, int yTwips, int clickCount, int buttons, int modifiers)
    {
        NativeMethods.present_engine_post_mouse_event(_handle, type, xTwips, yTwips, clickCount, buttons, modifiers);
    }

    public void PostUnoCommand(string command, string jsonArguments = "{}", bool notifyWhenFinished = false)
    {
        NativeMethods.present_engine_post_uno_command(_handle, command, jsonArguments, notifyWhenFinished);
    }

    public void SaveAs(string destinationPathOrUrl, string format = "", string filterOptions = "")
    {
        NativeMethods.present_engine_save_as(_handle, destinationPathOrUrl, format, filterOptions);
    }

    public void SetEventCallback(Action<EngineEvent> callback)
    {
        lock (_lock)
        {
            _eventCallback = callback != null ? new EventCallbackDelegate(evt => callback(evt)) : null;
            NativeMethods.present_engine_set_event_callback(_handle, _eventCallback);
        }
    }

    public static string PathToFileUrl(string pathOrUrl)
    {
        var result = NativeMethods.present_engine_path_to_file_url(pathOrUrl);
        return Marshal.PtrToStringAnsi(result) ?? "";
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.present_engine_destroy(_handle);
            _handle = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeEngineOptions
    {
        [MarshalAs(UnmanagedType.LPStr)]
        public string LibreOfficeProgramPath;
        [MarshalAs(UnmanagedType.LPStr)]
        public string UserProfileUrl;
    }

    private static class NativeMethods
    {
        private const string LibraryName = "cakeos-present-engine";

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr present_engine_create(ref NativeEngineOptions options);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_destroy(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_open(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string pathOrUrl);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_close(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool present_engine_is_open(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int present_engine_slides_count(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_get_slide(IntPtr handle, int index, ref SlideInfo slide);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int present_engine_current_slide(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_set_current_slide(IntPtr handle, int slideIndex);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_slide_extent(IntPtr handle, int slideIndex, ref SlideExtent extent);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool present_engine_supports_element_snapshots(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int present_engine_element_snapshot_count(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string documentPathOrUrl, int slideIndex);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_get_element_snapshot(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string documentPathOrUrl, int slideIndex, int index, ref ElementSnapshot element);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_select_element(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string snapshotPathOrUrl, int slideIndex, int objectIndex);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_clear_element_selection(IntPtr handle, int slideIndex, int objectIndex);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool present_engine_replace_element_text(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string snapshotPathOrUrl, int slideIndex, int objectIndex, [MarshalAs(UnmanagedType.LPStr)] string text);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_add_slide_after(IntPtr handle, int slideIndex);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_duplicate_slide(IntPtr handle, int slideIndex);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_delete_slide(IntPtr handle, int slideIndex);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_move_slide(IntPtr handle, int fromIndex, int toIndex);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_undo(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_redo(IntPtr handle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_render_tile(IntPtr handle, ref TileRequest request, ref RenderedTile tile);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_post_key_event(IntPtr handle, KeyEventType type, int charCode, int keyCode);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_post_mouse_event(IntPtr handle, MouseEventType type, int xTwips, int yTwips, int clickCount, int buttons, int modifiers);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_post_uno_command(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string command, [MarshalAs(UnmanagedType.LPStr)] string jsonArguments, [MarshalAs(UnmanagedType.I1)] bool notifyWhenFinished);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_save_as(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string destinationPathOrUrl, [MarshalAs(UnmanagedType.LPStr)] string format, [MarshalAs(UnmanagedType.LPStr)] string filterOptions);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void present_engine_set_event_callback(IntPtr handle, EventCallbackDelegate callback);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr present_engine_path_to_file_url([MarshalAs(UnmanagedType.LPStr)] string pathOrUrl);
    }
}