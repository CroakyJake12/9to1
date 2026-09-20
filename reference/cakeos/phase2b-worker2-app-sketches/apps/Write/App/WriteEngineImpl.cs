using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HavenOS.Apps.Write;

public sealed class WriteEngine : IWriteEngine
{
    private readonly NativeWriteEngine _native;
    private Action<WriteEngineEvent>? _eventCallback;

    public WriteEngine(string libreOfficeProgramPath = "/usr/lib/libreoffice/program", string? userProfileUrl = null)
    {
        _native = new NativeWriteEngine(libreOfficeProgramPath, userProfileUrl);
        _native.SetEventCallback(OnNativeEvent);
    }

    public void Open(string documentPathOrUrl) => _native.Open(documentPathOrUrl);
    public void Close() => _native.Close();
    public bool IsOpen() => _native.IsOpen();
    public WriteDocumentInfo GetDocumentInfo() => _native.GetDocumentInfo();
    public IReadOnlyList<WritePageInfo> GetPages() => _native.GetPages();
    public WriteTextContent GetText(WriteCursorPosition start, WriteCursorPosition end) => _native.GetText(start, end);
    public void SetText(WriteCursorPosition position, string text) => _native.SetText(position, text);
    public void InsertText(WriteCursorPosition position, string text) => _native.InsertText(position, text);
    public void DeleteText(WriteCursorPosition start, WriteCursorPosition end) => _native.DeleteText(start, end);
    public void ApplyFormat(WriteFormatRange range) => _native.ApplyFormat(range);
    public void Save() => _native.Save();
    public void SaveAs(string destinationPathOrUrl, string format = "odt") => _native.SaveAs(destinationPathOrUrl, format);
    public void Print() => _native.Print();
    public void Undo() => _native.Undo();
    public void Redo() => _native.Redo();
    public void SetCursorPosition(WriteCursorPosition position) => _native.SetCursorPosition(position);
    public WriteCursorPosition GetCursorPosition() => _native.GetCursorPosition();
    public void SetSelection(WriteSelection selection) => _native.SetSelection(selection);
    public WriteSelection? GetSelection() => _native.GetSelection();
    public void SetEventCallback(Action<WriteEngineEvent> callback) => _eventCallback = callback;

    public ValueTask DisposeAsync()
    {
        _native.Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnNativeEvent(WriteEngineEvent evt)
    {
        _eventCallback?.Invoke(evt);
    }

    private sealed class NativeWriteEngine : IDisposable
    {
        private readonly IntPtr _handle;
        private Action<WriteEngineEvent>? _callback;

        public NativeWriteEngine(string libreOfficeProgramPath, string? userProfileUrl)
        {
            _handle = NativeMethods.write_engine_create(libreOfficeProgramPath, userProfileUrl ?? "");
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create WriteEngine. Ensure libreofficekit-dev is installed.");
        }

        public void Open(string documentPathOrUrl) => NativeMethods.write_engine_open(_handle, documentPathOrUrl);
        public void Close() => NativeMethods.write_engine_close(_handle);
        public bool IsOpen() => NativeMethods.write_engine_is_open(_handle);
        
        public WriteDocumentInfo GetDocumentInfo()
        {
            var info = new NativeDocumentInfo();
            NativeMethods.write_engine_get_document_info(_handle, ref info);
            return new WriteDocumentInfo(
                Marshal.PtrToStringAnsi(info.Path) ?? "",
                Marshal.PtrToStringAnsi(info.Title) ?? "",
                info.PageCount,
                info.Size);
        }

        public IReadOnlyList<WritePageInfo> GetPages()
        {
            int count = NativeMethods.write_engine_get_page_count(_handle);
            var pages = new List<WritePageInfo>(count);
            for (int i = 0; i < count; i++)
            {
                var page = new NativePageInfo();
                NativeMethods.write_engine_get_page_info(_handle, i, ref page);
                pages.Add(new WritePageInfo(page.PageNumber, page.WidthTwips, page.HeightTwips));
            }
            return pages;
        }

        public WriteTextContent GetText(WriteCursorPosition start, WriteCursorPosition end)
        {
            var nativeStart = new NativeCursorPosition { Page = start.Page, Paragraph = start.Paragraph, Offset = start.Offset };
            var nativeEnd = new NativeCursorPosition { Page = end.Page, Paragraph = end.Paragraph, Offset = end.Offset };
            var ptr = NativeMethods.write_engine_get_text(_handle, ref nativeStart, ref nativeEnd);
            var text = Marshal.PtrToStringAnsi(ptr) ?? "";
            if (ptr != IntPtr.Zero) NativeMethods.write_engine_free_string(ptr);
            return new WriteTextContent(text, false);
        }

        public void SetText(WriteCursorPosition position, string text)
        {
            var nativePos = new NativeCursorPosition { Page = position.Page, Paragraph = position.Paragraph, Offset = position.Offset };
            NativeMethods.write_engine_set_text(_handle, ref nativePos, text);
        }

        public void InsertText(WriteCursorPosition position, string text)
        {
            var nativePos = new NativeCursorPosition { Page = position.Page, Paragraph = position.Paragraph, Offset = position.Offset };
            NativeMethods.write_engine_insert_text(_handle, ref nativePos, text);
        }

        public void DeleteText(WriteCursorPosition start, WriteCursorPosition end)
        {
            var nativeStart = new NativeCursorPosition { Page = start.Page, Paragraph = start.Paragraph, Offset = start.Offset };
            var nativeEnd = new NativeCursorPosition { Page = end.Page, Paragraph = end.Paragraph, Offset = end.Offset };
            NativeMethods.write_engine_delete_text(_handle, ref nativeStart, ref nativeEnd);
        }

        public void ApplyFormat(WriteFormatRange range)
        {
            var nativeStart = new NativeCursorPosition { Page = range.Start.Page, Paragraph = range.Start.Paragraph, Offset = range.Start.Offset };
            var nativeEnd = new NativeCursorPosition { Page = range.End.Page, Paragraph = range.End.Paragraph, Offset = range.End.Offset };
            var nativeFormat = new NativeFormat
            {
                Bold = range.Format.Bold,
                Italic = range.Format.Italic,
                Underline = range.Format.Underline,
                FontFamily = range.Format.FontFamily ?? "",
                FontSize = range.Format.FontSize,
                Color = range.Format.Color ?? ""
            };
            NativeMethods.write_engine_apply_format(_handle, ref nativeStart, ref nativeEnd, ref nativeFormat);
        }

        public void Save() => NativeMethods.write_engine_save(_handle);
        public void SaveAs(string destinationPathOrUrl, string format) => NativeMethods.write_engine_save_as(_handle, destinationPathOrUrl, format);
        public void Print() => NativeMethods.write_engine_print(_handle);
        public void Undo() => NativeMethods.write_engine_undo(_handle);
        public void Redo() => NativeMethods.write_engine_redo(_handle);
        public void SetCursorPosition(WriteCursorPosition position)
        {
            var nativePos = new NativeCursorPosition { Page = position.Page, Paragraph = position.Paragraph, Offset = position.Offset };
            NativeMethods.write_engine_set_cursor(_handle, ref nativePos);
        }

        public WriteCursorPosition GetCursorPosition()
        {
            var nativePos = new NativeCursorPosition();
            NativeMethods.write_engine_get_cursor(_handle, ref nativePos);
            return new WriteCursorPosition(nativePos.Page, nativePos.Paragraph, nativePos.Offset);
        }

        public void SetSelection(WriteSelection selection)
        {
            var nativeStart = new NativeCursorPosition { Page = selection.Start.Page, Paragraph = selection.Start.Paragraph, Offset = selection.Start.Offset };
            var nativeEnd = new NativeCursorPosition { Page = selection.End.Page, Paragraph = selection.End.Paragraph, Offset = selection.End.Offset };
            NativeMethods.write_engine_set_selection(_handle, ref nativeStart, ref nativeEnd);
        }

        public WriteSelection? GetSelection()
        {
            var nativeStart = new NativeCursorPosition();
            var nativeEnd = new NativeCursorPosition();
            if (!NativeMethods.write_engine_get_selection(_handle, ref nativeStart, ref nativeEnd))
                return null;
            return new WriteSelection(
                new WriteCursorPosition(nativeStart.Page, nativeStart.Paragraph, nativeStart.Offset),
                new WriteCursorPosition(nativeEnd.Page, nativeEnd.Paragraph, nativeEnd.Offset));
        }

        public void SetEventCallback(Action<WriteEngineEvent> callback) => _callback = callback;

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                NativeMethods.write_engine_destroy(_handle);
            }
        }

        private void OnNativeEvent(int type, IntPtr payloadPtr)
        {
            var payload = Marshal.PtrToStringAnsi(payloadPtr) ?? "";
            if (payloadPtr != IntPtr.Zero) NativeMethods.write_engine_free_string(payloadPtr);
            _callback?.Invoke(new WriteEngineEvent((WriteEngineEventType)type, payload));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeDocumentInfo
        {
            public IntPtr Path;
            public IntPtr Title;
            public int PageCount;
            public long Size;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePageInfo
        {
            public int PageNumber;
            public int WidthTwips;
            public int HeightTwips;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeCursorPosition
        {
            public int Page;
            public int Paragraph;
            public int Offset;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeFormat
        {
            [MarshalAs(UnmanagedType.I1)] public bool Bold;
            [MarshalAs(UnmanagedType.I1)] public bool Italic;
            [MarshalAs(UnmanagedType.I1)] public bool Underline;
            [MarshalAs(UnmanagedType.LPStr)] public string FontFamily;
            public int FontSize;
            [MarshalAs(UnmanagedType.LPStr)] public string Color;
        }

        private static class NativeMethods
        {
            private const string LibraryName = "cakeos-present-engine";

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr write_engine_create(string libreOfficeProgramPath, string userProfileUrl);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void write_engine_destroy(IntPtr handle);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void write_engine_open(IntPtr handle, string pathOrUrl);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void write_engine_close(IntPtr handle);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            [return: MarshalAs(UnmanagedType.I1)]
            public static extern bool write_engine_is_open(IntPtr handle);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void write_engine_get_document_info(IntPtr handle, ref NativeDocumentInfo info);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern int write_engine_get_page_count(IntPtr handle);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void write_engine_get_page_info(IntPtr handle, int index, ref NativePageInfo page);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr write_engine_get_text(IntPtr handle, ref NativeCursorPosition start, ref NativeCursorPosition end);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void write_engine_set_text(IntPtr handle, ref NativeCursorPosition position, string text);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void write_engine_insert_text(IntPtr handle, ref NativeCursorPosition position, string text);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void write_engine_delete_text(IntPtr handle, ref NativeCursorPosition start, ref NativeCursorPosition end);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void write_engine_apply_format(IntPtr handle, ref NativeCursorPosition start, ref NativeCursorPosition end, ref NativeFormat format);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void write_engine_save(IntPtr handle);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void write_engine_save_as(IntPtr handle, string destinationPathOrUrl, string format);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void write_engine_print(IntPtr handle);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void write_engine_undo(IntPtr handle);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void write_engine_redo(IntPtr handle);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void write_engine_set_cursor(IntPtr handle, ref NativeCursorPosition position);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void write_engine_get_cursor(IntPtr handle, ref NativeCursorPosition position);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void write_engine_set_selection(IntPtr handle, ref NativeCursorPosition start, ref NativeCursorPosition end);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            [return: MarshalAs(UnmanagedType.I1)]
            public static extern bool write_engine_get_selection(IntPtr handle, ref NativeCursorPosition start, ref NativeCursorPosition end);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void write_engine_set_event_callback(IntPtr handle, IntPtr callback);

            [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void write_engine_free_string(IntPtr ptr);
        }
    }
}