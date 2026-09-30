using System.Runtime.InteropServices;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;

namespace HavenOS.Apps.Canvas;

public sealed record RnoteRenderFrame(double X, double Y, double Width, double Height, byte[] Svg);

/// <summary>Managed ownership boundary for the controlled Rnote drawing engine.</summary>
public sealed class RnoteCanvasEngine : IDisposable
{
    private const string Library = "cakeos_canvas_rnote_poc";
    private const int MaximumPayloadBytes = 256 * 1024 * 1024;
    internal const int MaximumStrokeSamples = 1_000_000;
    private readonly object _gate = new();
    private readonly EngineHandle _handle;

    private RnoteCanvasEngine(IntPtr handle)
    {
        if (handle == IntPtr.Zero) throw new InvalidOperationException("Rnote failed to create the drawing engine.");
        _handle = new EngineHandle(handle);
    }

    public static RnoteCanvasEngine Create()
    {
        VerifyAbi();
        return new RnoteCanvasEngine(Native.New());
    }

    public static RnoteCanvasEngine Open(byte[] bytes)
    {
        var captured = CaptureCompressedPayload(bytes);
        VerifyAbi();
        Check(Native.Open(captured, (nuint)captured.Length, out var handle), "open Rnote document");
        return new RnoteCanvasEngine(handle);
    }

    public static RnoteCanvasEngine ImportXopp(byte[] bytes, double dpi = 96)
    {
        var captured = CaptureCompressedPayload(bytes);
        if (!double.IsFinite(dpi) || dpi is < 1 or > 2400) throw new ArgumentOutOfRangeException(nameof(dpi));
        VerifyAbi();
        Check(Native.ImportXopp(captured, (nuint)captured.Length, dpi, out var handle), "import Xournal++ document");
        return new RnoteCanvasEngine(handle);
    }

    public void DrawStroke(IReadOnlyList<RnotePointerSample> samples, CanvasRnoteInkStyle? style = null)
        => DrawCapturedStroke(CaptureSamples(samples), style ?? CanvasRnoteInkStyle.Default);

    internal static ImmutableArray<RnotePointerSample> CaptureSamples(IReadOnlyList<RnotePointerSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var captured = ImmutableArray.CreateBuilder<RnotePointerSample>();
        // Count/indexers and a second enumeration may observe different values
        // on caller-owned lists. Bound the actual enumeration before allocation
        // can grow indefinitely; validation and native input use only this copy.
        foreach (var sample in samples)
        {
            if (captured.Count == MaximumStrokeSamples)
                throw new ArgumentException("The stroke exceeds the supported sample limit.", nameof(samples));
            captured.Add(sample);
        }
        if (captured.Count < 2 || captured.Any(sample => !sample.IsValid))
            throw new ArgumentException("A stroke requires at least two finite pointer samples with pressure in 0..1.", nameof(samples));
        return captured.ToImmutable();
    }

    internal void DrawCapturedStroke(ImmutableArray<RnotePointerSample> samples, CanvasRnoteInkStyle? style = null)
    {
        var resolved = (style ?? CanvasRnoteInkStyle.Default).ValidateAndResolve();
        lock (_gate)
        {
            EnsureOpen();
            // Match the canonical resolved brush properties retained alongside
            // the donor snapshot. Existing imported strokes keep their own style.
            Check(Native.SetTool(_handle, resolved.Tool), "select ink instrument");
            Check(Native.SetPenStyle(_handle, resolved.Tool, resolved.Red, resolved.Green, resolved.Blue, resolved.Alpha,
                (style ?? CanvasRnoteInkStyle.Default).BaseWidth), "configure ink style");
            Check(Native.Begin(_handle, samples[0]), "begin stroke");
            for (var index = 1; index < samples.Length - 1; index++) Check(Native.Append(_handle, samples[index]), "append stroke");
            Check(Native.End(_handle, samples[^1]), "end stroke");
        }
    }

    public bool Undo() { lock (_gate) { EnsureOpen(); return Changed(Native.Undo(_handle), "undo"); } }
    public bool Redo() { lock (_gate) { EnsureOpen(); return Changed(Native.Redo(_handle), "redo"); } }

    public bool SupportsStructuredSelectionExport
    {
        get
        {
            lock (_gate)
            {
                EnsureOpen();
                try { return Native.SelectionApiVersion() == 1; }
                catch (EntryPointNotFoundException) { return false; }
            }
        }
    }

    public ImmutableArray<ulong> ReadStrokeKeys()
    {
        lock (_gate)
        {
            EnsureOpen();
            RequireSelectionExport();
            var buffer = new NativeBuffer();
            try
            {
                Check(Native.StrokeKeys(_handle, out buffer), "read native stroke keys");
                if (buffer.Length == 0) return [];
                if (buffer.Length % 8 != 0 || buffer.Length > MaximumStrokeSamples * 8)
                    throw new InvalidDataException("Native stroke key buffer exceeds its packed u64 contract.");
                var bytes = Copy(buffer.Data, buffer.Length);
                var keys = ImmutableArray.CreateBuilder<ulong>(bytes.Length / 8);
                for (var offset = 0; offset < bytes.Length; offset += 8)
                    keys.Add(BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset, 8)));
                if (keys.Distinct().Count() != keys.Count) throw new InvalidDataException("The native snapshot contains duplicate stroke keys.");
                return keys.MoveToImmutable();
            }
            finally { Native.ReleaseBuffer(ref buffer); }
        }
    }

    public byte[] ExportSelectedStrokes(IEnumerable<ulong> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var captured = new List<ulong>();
        foreach (var key in keys)
        {
            if (captured.Count == MaximumStrokeSamples) throw new ArgumentException("Native selection exceeds its key limit.", nameof(keys));
            captured.Add(key);
        }
        if (captured.Count == 0 || captured.Distinct().Count() != captured.Count)
            throw new ArgumentException("Native selection requires unique nonempty stroke keys.", nameof(keys));
        var owned = captured.ToArray();
        lock (_gate)
        {
            EnsureOpen();
            RequireSelectionExport();
            var buffer = new NativeBuffer();
            try { Check(Native.ExportSelected(_handle, owned, (nuint)owned.Length, out buffer), "export selected native strokes"); return CopyDrawingPayload(buffer.Data, buffer.Length); }
            finally { Native.ReleaseBuffer(ref buffer); }
        }
    }

    private void RequireSelectionExport()
    {
        if (!SupportsStructuredSelectionExport)
            throw new NotSupportedException("The installed Rnote bridge has no structured-selection API v1.");
    }

    public byte[] Save()
    {
        lock (_gate)
        {
            EnsureOpen();
            var buffer = new NativeBuffer();
            try { Check(Native.Save(_handle, out buffer), "save drawing state"); return CopyDrawingPayload(buffer.Data, buffer.Length); }
            finally { Native.ReleaseBuffer(ref buffer); }
        }
    }

    public RnoteRenderFrame Render()
    {
        lock (_gate)
        {
            EnsureOpen();
            var frame = new NativeFrame();
            try
            {
                Check(Native.Render(_handle, out frame), "render drawing");
                if (frame.Format != 1 || frame.CoordinateSpace != 1 || !double.IsFinite(frame.X) || !double.IsFinite(frame.Y)
                    || !double.IsFinite(frame.Width) || !double.IsFinite(frame.Height) || frame.Width <= 0 || frame.Height <= 0)
                    throw new InvalidDataException("The Rnote engine returned an unsupported render frame.");
                return new RnoteRenderFrame(frame.X, frame.Y, frame.Width, frame.Height, Copy(frame.Data, frame.Length));
            }
            finally { Native.ReleaseFrame(ref frame); }
        }
    }

    public void Dispose() { lock (_gate) _handle.Dispose(); }

    private void EnsureOpen() => ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
    private static void VerifyAbi()
    {
        if (Native.AbiVersion() != 3) throw new NotSupportedException("Canvas requires Rnote bridge ABI 3.");
    }
    private static byte[] CaptureCompressedPayload(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is 0 or > MaximumPayloadBytes) throw new InvalidDataException("Drawing payload size is outside the supported limit.");
        var captured = bytes.ToArray();
        ValidateOwnedCompressedPayload(captured);
        return captured;
    }
    private static byte[] CopyDrawingPayload(IntPtr data, nuint length)
    {
        var bytes = Copy(data, length);
        // A native edited state must satisfy the same bounded representation
        // accepted by Open. Highly compressible output cannot evade the limit
        // or produce an artifact that its own importer refuses to reopen.
        ValidateOwnedCompressedPayload(bytes);
        return bytes;
    }
    private static void ValidateOwnedCompressedPayload(byte[] captured)
    {
        // Both maintained Rnote and Xournal++ readers consume gzip. Bound the
        // actual expansion before entering native parsers, using this same
        // owned copy throughout validation and the native call.
        using var source = new MemoryStream(captured, writable: false);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        Span<byte> buffer = stackalloc byte[8192];
        long expanded = 0;
        int read;
        while ((read = gzip.Read(buffer)) != 0)
        {
            expanded += read;
            if (expanded > MaximumPayloadBytes) throw new InvalidDataException("The expanded drawing payload exceeds the supported native parser limit.");
        }
        if (expanded == 0) throw new InvalidDataException("The expanded drawing payload is empty.");
    }
    private static byte[] Copy(IntPtr data, nuint length)
    {
        if (data == IntPtr.Zero || length is 0 or > MaximumPayloadBytes) throw new InvalidDataException("The Rnote engine returned an invalid payload.");
        var bytes = new byte[checked((int)length)];
        Marshal.Copy(data, bytes, 0, bytes.Length);
        return bytes;
    }
    private static bool Changed(int status, string action) { Check(status, action); return status == 0; }
    private static void Check(int status, string action)
    {
        if (status is 0 or 1) return;
        throw new InvalidOperationException($"Canvas could not {action}; Rnote status {status}.");
    }

    private sealed class EngineHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public EngineHandle(IntPtr value) : base(true) => SetHandle(value);
        protected override bool ReleaseHandle() { Native.Free(handle); return true; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeBuffer { public IntPtr Data; public nuint Length; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeFrame
    {
        public uint Format; public uint CoordinateSpace;
        public double X; public double Y; public double Width; public double Height;
        public IntPtr Data; public nuint Length;
    }
    private static class Native
    {
        [DllImport(Library, EntryPoint = "cake_canvas_abi_version", CallingConvention = CallingConvention.Cdecl)] internal static extern uint AbiVersion();
        [DllImport(Library, EntryPoint = "cake_canvas_selection_api_version", CallingConvention = CallingConvention.Cdecl)] internal static extern uint SelectionApiVersion();
        [DllImport(Library, EntryPoint = "cake_canvas_stroke_keys", CallingConvention = CallingConvention.Cdecl)] internal static extern int StrokeKeys(EngineHandle handle, out NativeBuffer buffer);
        [DllImport(Library, EntryPoint = "cake_canvas_export_selected_strokes", CallingConvention = CallingConvention.Cdecl)] internal static extern int ExportSelected(EngineHandle handle, ulong[] keys, nuint count, out NativeBuffer buffer);
        [DllImport(Library, EntryPoint = "cake_canvas_engine_new", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr New();
        [DllImport(Library, EntryPoint = "cake_canvas_engine_free", CallingConvention = CallingConvention.Cdecl)] internal static extern void Free(IntPtr handle);
        [DllImport(Library, EntryPoint = "cake_canvas_engine_from_rnote", CallingConvention = CallingConvention.Cdecl)] internal static extern int Open(byte[] bytes, nuint length, out IntPtr handle);
        [DllImport(Library, EntryPoint = "cake_canvas_engine_from_xopp", CallingConvention = CallingConvention.Cdecl)] internal static extern int ImportXopp(byte[] bytes, nuint length, double dpi, out IntPtr handle);
        [DllImport(Library, EntryPoint = "cake_canvas_begin_stroke", CallingConvention = CallingConvention.Cdecl)] internal static extern int Begin(EngineHandle handle, RnotePointerSample sample);
        [DllImport(Library, EntryPoint = "cake_canvas_update_stroke", CallingConvention = CallingConvention.Cdecl)] internal static extern int Append(EngineHandle handle, RnotePointerSample sample);
        [DllImport(Library, EntryPoint = "cake_canvas_end_stroke", CallingConvention = CallingConvention.Cdecl)] internal static extern int End(EngineHandle handle, RnotePointerSample sample);
        [DllImport(Library, EntryPoint = "cake_canvas_set_pen_style", CallingConvention = CallingConvention.Cdecl)] internal static extern int SetPenStyle(EngineHandle handle, uint tool, double red, double green, double blue, double alpha, double width);
        [DllImport(Library, EntryPoint = "cake_canvas_set_stroke_tool", CallingConvention = CallingConvention.Cdecl)] internal static extern int SetTool(EngineHandle handle, uint tool);
        [DllImport(Library, EntryPoint = "cake_canvas_undo", CallingConvention = CallingConvention.Cdecl)] internal static extern int Undo(EngineHandle handle);
        [DllImport(Library, EntryPoint = "cake_canvas_redo", CallingConvention = CallingConvention.Cdecl)] internal static extern int Redo(EngineHandle handle);
        [DllImport(Library, EntryPoint = "cake_canvas_save_rnote", CallingConvention = CallingConvention.Cdecl)] internal static extern int Save(EngineHandle handle, out NativeBuffer buffer);
        [DllImport(Library, EntryPoint = "cake_canvas_buffer_release", CallingConvention = CallingConvention.Cdecl)] internal static extern void ReleaseBuffer(ref NativeBuffer buffer);
        [DllImport(Library, EntryPoint = "cake_canvas_render_frame", CallingConvention = CallingConvention.Cdecl)] internal static extern int Render(EngineHandle handle, out NativeFrame frame);
        [DllImport(Library, EntryPoint = "cake_canvas_render_frame_release", CallingConvention = CallingConvention.Cdecl)] internal static extern void ReleaseFrame(ref NativeFrame frame);
    }
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct RnotePointerSample(double X, double Y, double Pressure, double TiltX = 0, double TiltY = 0)
{
    internal bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Pressure)
        && double.IsFinite(TiltX) && double.IsFinite(TiltY) && Pressure is >= 0 and <= 1;
}
