using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HavenOS.Apps.Present;

public sealed class PresentEngine : IPresentEngine
{
    private readonly NativePresentEngine _native;
    private Action<PresentEngineEvent>? _eventCallback;

    public PresentEngine(string libreOfficeProgramPath = "/usr/lib/libreoffice/program", string? userProfileUrl = null)
    {
        _native = new NativePresentEngine(libreOfficeProgramPath, userProfileUrl);
        _native.SetEventCallback(OnNativeEvent);
    }

    public void Open(string documentPathOrUrl) => _native.Open(documentPathOrUrl);
    public void Close() => _native.Close();
    public bool IsOpen() => _native.IsOpen();
    public IReadOnlyList<PresentSlideInfo> GetSlides() => _native.GetSlides();
    public int CurrentSlide() => _native.CurrentSlide();
    public void SetCurrentSlide(int slideIndex) => _native.SetCurrentSlide(slideIndex);
    public PresentSlideExtent GetSlideExtent(int slideIndex) => _native.GetSlideExtent(slideIndex);
    public bool SupportsElementSnapshots() => _native.SupportsElementSnapshots();
    public IReadOnlyList<PresentElementSnapshot> GetElementSnapshot(string documentPathOrUrl, int slideIndex) => _native.GetElementSnapshot(documentPathOrUrl, slideIndex);
    public void SelectElement(string snapshotPathOrUrl, int slideIndex, int objectIndex) => _native.SelectElement(snapshotPathOrUrl, slideIndex, objectIndex);
    public void ClearElementSelection(int slideIndex, int objectIndex) => _native.ClearElementSelection(slideIndex, objectIndex);
    public bool ReplaceElementText(string snapshotPathOrUrl, int slideIndex, int objectIndex, string text) => _native.ReplaceElementText(snapshotPathOrUrl, slideIndex, objectIndex, text);
    public void AddSlideAfter(int slideIndex) => _native.AddSlideAfter(slideIndex);
    public void DuplicateSlide(int slideIndex) => _native.DuplicateSlide(slideIndex);
    public void DeleteSlide(int slideIndex) => _native.DeleteSlide(slideIndex);
    public void MoveSlide(int fromIndex, int toIndex) => _native.MoveSlide(fromIndex, toIndex);
    public void Undo() => _native.Undo();
    public void Redo() => _native.Redo();
    public PresentRenderedTile RenderTile(PresentTileRequest request) => _native.RenderTile(request);
    public void PostKeyEvent(KeyEventType type, int charCode, int keyCode) => _native.PostKeyEvent(type, charCode, keyCode);
    public void PostMouseEvent(MouseEventType type, int xTwips, int yTwips, int clickCount, int buttons, int modifiers) => _native.PostMouseEvent(type, xTwips, yTwips, clickCount, buttons, modifiers);
    public void PostUnoCommand(string command, string jsonArguments = "{}", bool notifyWhenFinished = false) => _native.PostUnoCommand(command, jsonArguments, notifyWhenFinished);
    public void SaveAs(string destinationPathOrUrl, string format = "", string filterOptions = "") => _native.SaveAs(destinationPathOrUrl, format, filterOptions);
    public void SetEventCallback(Action<PresentEngineEvent> callback) => _eventCallback = callback;
    public static string PathToFileUrl(string pathOrUrl) => NativePresentEngine.PathToFileUrl(pathOrUrl);

    public ValueTask DisposeAsync()
    {
        _native.Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnNativeEvent(PresentEngineEvent evt)
    {
        _eventCallback?.Invoke(evt);
    }

    private sealed class NativePresentEngine : IDisposable
    {
        private readonly IntPtr _handle;
        private readonly Action<PresentEngineEvent> _callback;

        public NativePresentEngine(string libreOfficeProgramPath, string? userProfileUrl)
        {
            _handle = NativeMethods.present_engine_create(libreOfficeProgramPath, userProfileUrl ?? "");
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create PresentEngine");
        }

        public void Open(string documentPathOrUrl) => NativeMethods.present_engine_open(_handle, documentPathOrUrl);
        public void Close() => NativeMethods.present_engine_close(_handle);
        public bool IsOpen() => NativeMethods.present_engine_is_open(_handle);
        public IReadOnlyList<PresentSlideInfo> GetSlides()
        {
            int count = NativeMethods.present_engine_slides_count(_handle);
            var slides = new List<PresentSlideInfo>(count);
            for (int i = 0; i < count; i++)
            {
                var slide = new NativeSlideInfo();
                NativeMethods.present_engine_get_slide(_handle, i, ref slide);
                slides.Add(new PresentSlideInfo(slide.Index, Marshal.PtrToStringAnsi(slide.Name) ?? "", Marshal.PtrToStringAnsi(slide.Hash) ?? ""));
                if (slide.Name != IntPtr.Zero) Marshal.FreeHGlobal(slide.Name);
                if (slide.Hash != IntPtr.Zero) Marshal.FreeHGlobal(slide.Hash);
            }
            return slides;
        }
        public int CurrentSlide() => NativeMethods.present_engine_current_slide(_handle);
        public void SetCurrentSlide(int slideIndex) => NativeMethods.present_engine_set_current_slide(_handle, slideIndex);
        public PresentSlideExtent GetSlideExtent(int slideIndex)
        {
            var extent = new NativeSlideExtent();
            NativeMethods.present_engine_slide_extent(_handle, slideIndex, ref extent);
            return new PresentSlideExtent(extent.WidthTwips, extent.HeightTwips);
        }
        public bool SupportsElementSnapshots() => NativeMethods.present_engine_supports_element_snapshots(_handle) != 0;
        public IReadOnlyList<PresentElementSnapshot> GetElementSnapshot(string documentPathOrUrl, int slideIndex)
        {
            int count = NativeMethods.present_engine_element_snapshot_count(_handle, documentPathOrUrl, slideIndex);
            var elements = new List<PresentElementSnapshot>(count);
            for (int i = 0; i < count; i++)
            {
                var elem = new NativeElementSnapshot();
                NativeMethods.present_engine_get_element_snapshot(_handle, documentPathOrUrl, slideIndex, i, ref elem);
                var texts = new List<string>(elem.TextCount);
                for (int j = 0; j < elem.TextCount; j++)
                {
                    var ptr = Marshal.ReadIntPtr(elem.TextArray, j * IntPtr.Size);
                    texts.Add(Marshal.PtrToStringAnsi(ptr) ?? "");
                    if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
                }
                if (elem.TextArray != IntPtr.Zero) Marshal.FreeHGlobal(elem.TextArray);
                elements.Add(new PresentElementSnapshot(elem.SlideIndex, elem.ObjectIndex, texts));
            }
            return elements;
        }
        public void SelectElement(string snapshotPathOrUrl, int slideIndex, int objectIndex) => NativeMethods.present_engine_select_element(_handle, snapshotPathOrUrl, slideIndex, objectIndex);
        public void ClearElementSelection(int slideIndex, int objectIndex) => NativeMethods.present_engine_clear_element_selection(_handle, slideIndex, objectIndex);
        public bool ReplaceElementText(string snapshotPathOrUrl, int slideIndex, int objectIndex, string text) => NativeMethods.present_engine_replace_element_text(_handle, snapshotPathOrUrl, slideIndex, objectIndex, text) != 0;
        public void AddSlideAfter(int slideIndex) => NativeMethods.present_engine_add_slide_after(_handle, slideIndex);
        public void DuplicateSlide(int slideIndex) => NativeMethods.present_engine_duplicate_slide(_handle, slideIndex);
        public void DeleteSlide(int slideIndex) => NativeMethods.present_engine_delete_slide(_handle, slideIndex);
        public void MoveSlide(int fromIndex, int toIndex) => NativeMethods.present_engine_move_slide(_handle, fromIndex, toIndex);
        public void Undo() => NativeMethods.present_engine_undo(_handle);
        public void Redo() => NativeMethods.present_engine_redo(_handle);
        public PresentRenderedTile RenderTile(PresentTileRequest request)
        {
            var nativeRequest = new NativeTileRequest
            {
                SlideIndex = request.SlideIndex,
                PixelWidth = request.PixelWidth,
                PixelHeight = request.PixelHeight,
                TileXTwips = request.TileXTwips,
                TileYTwips = request.TileYTwips,
                TileWidthTwips = request.TileWidthTwips,
                TileHeightTwips = request.TileHeightTwips
            };
            var nativeTile = new NativeRenderedTile();
            NativeMethods.present_engine_render_tile(_handle, ref nativeRequest, ref nativeTile);
            var pixels = new byte[nativeTile.PixelsLength];
            if (nativeTile.Pixels != IntPtr.Zero && nativeTile.PixelsLength > 0)
            {
                Marshal.Copy(nativeTile.Pixels, pixels, 0, nativeTile.PixelsLength);
                Marshal.FreeHGlobal(nativeTile.Pixels);
            }
            return new PresentRenderedTile(nativeTile.PixelWidth, nativeTile.PixelHeight, (PixelFormat)nativeTile.PixelFormat, pixels);
        }
        public void PostKeyEvent(KeyEventType type, int charCode, int keyCode) => NativeMethods.present_engine_post_key_event(_handle, type, charCode, keyCode);
        public void PostMouseEvent(MouseEventType type, int xTwips, int yTwips, int clickCount, int buttons, int modifiers) => NativeMethods.present_engine_post_mouse_event(_handle, type, xTwips, yTwips, clickCount, buttons, modifiers);
        public void PostUnoCommand(string command, string jsonArguments, bool notifyWhenFinished) => NativeMethods.present_engine_post_uno_command(_handle, command, jsonArguments, notifyWhenFinished);
        public void SaveAs(string destinationPathOrUrl, string format, string filterOptions) => NativeMethods.present_engine_save_as(_handle, destinationPathOrUrl, format, filterOptions);
        public void SetEventCallback(Action<PresentEngineEvent> callback) => _callback = callback;
        public static string PathToFileUrl(string pathOrUrl)
        {
            var ptr = NativeMethods.present_engine_path_to_file_url(pathOrUrl);
            var result = Marshal.PtrToStringAnsi(ptr) ?? "";
            if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
            return result;
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                NativeMethods.present_engine_destroy(_handle);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSlideInfo
        {
            public int Index;
            public IntPtr Name;
            public IntPtr Hash;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSlideExtent
        {
            public long WidthTwips;
            public long HeightTwips;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeElementSnapshot
        {
            public int SlideIndex;
            public int ObjectIndex;
            public IntPtr TextArray;
            public int TextCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeTileRequest
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
        private struct NativeRenderedTile
        {
            public int PixelWidth;
            public int PixelHeight;
            public int PixelFormat;
            public IntPtr Pixels;
            public int PixelsLength;
        }

        private static class NativeMethods
        {
            private const string LibraryName = "cakeos-present-engine";

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr present_engine_create(string libreOfficeProgramPath, string userProfileUrl);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void present_engine_destroy(IntPtr handle);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void present_engine_open(IntPtr handle, string pathOrUrl);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void present_engine_close(IntPtr handle);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            [return: MarshalAs(UnmanagedType.I1)]
            public static extern bool present_engine_is_open(IntPtr handle);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern int present_engine_slides_count(IntPtr handle);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void present_engine_get_slide(IntPtr handle, int index, ref NativeSlideInfo slide);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern int present_engine_current_slide(IntPtr handle);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void present_engine_set_current_slide(IntPtr handle, int slideIndex);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void present_engine_slide_extent(IntPtr handle, int slideIndex, ref NativeSlideExtent extent);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            [return: MarshalAs(UnmanagedType.I1)]
            public static extern bool present_engine_supports_element_snapshots(IntPtr handle);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern int present_engine_element_snapshot_count(IntPtr handle, string documentPathOrUrl, int slideIndex);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void present_engine_get_element_snapshot(IntPtr handle, string documentPathOrUrl, int slideIndex, int index, ref NativeElementSnapshot element);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void present_engine_select_element(IntPtr handle, string snapshotPathOrUrl, int slideIndex, int objectIndex);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void present_engine_clear_element_selection(IntPtr handle, int slideIndex, int objectIndex);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            [return: MarshalAs(UnmanagedType.I1)]
            public static extern bool present_engine_replace_element_text(IntPtr handle, string snapshotPathOrUrl, int slideIndex, int objectIndex, string text);

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
            public static extern void present_engine_render_tile(IntPtr handle, ref NativeTileRequest request, ref NativeRenderedTile tile);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void present_engine_post_key_event(IntPtr handle, KeyEventType type, int charCode, int keyCode);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void present_engine_post_mouse_event(IntPtr handle, MouseEventType type, int xTwips, int yTwips, int clickCount, int buttons, int modifiers);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void present_engine_post_uno_command(IntPtr handle, string command, string jsonArguments, [MarshalAs(UnmanagedType.I1)] bool notifyWhenFinished);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void present_engine_save_as(IntPtr handle, string destinationPathOrUrl, string format, string filterOptions);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void present_engine_set_event_callback(IntPtr handle, IntPtr callback);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr present_engine_path_to_file_url(string pathOrUrl);
        }
    }
}