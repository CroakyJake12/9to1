using System.Runtime.InteropServices;
using Haven.Core.Media;

namespace Haven.Infrastructure.Media;

/// <summary>GStreamer playback adapter. It binds the native application API directly; it never shells out to gst-launch.</summary>
public sealed class GStreamerMediaEngine : IMediaEngine
{
    private readonly GStreamerNative? _native;

    public GStreamerMediaEngine()
    {
        _native = GStreamerNative.TryCreate();
    }

    public Task<MediaEngineCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var native = _native;
        var missing = new List<string>();
        if (native is null) missing.Add("GStreamer runtime (gstreamer-1.0)");
        var ges = false;
        foreach (var name in GStreamerNative.GesLibraryNames)
        {
            if (!NativeLibrary.TryLoad(name, out var gesHandle)) continue;
            NativeLibrary.Free(gesHandle);
            ges = true;
            break;
        }
        if (!ges) missing.Add("GStreamer Editing Services (GES)");
        missing.Add("GES timeline render adapter (not implemented)");
        return Task.FromResult(new MediaEngineCapabilities(native is not null, ges, native?.Version,
            native is not null && native.HasFactory("playbin"), native is not null && native.HasFactory("playbin"), false, missing));
    }

    public Task<MediaEngineResult<IMediaPlaybackSession>> OpenPlaybackAsync(MediaAssetSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        try { source.Validate(); }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            return Task.FromResult(MediaEngineResult<IMediaPlaybackSession>.Failure(Error(MediaEngineErrorCode.UnsupportedSource, exception.Message, source.AssetId.ToString())));
        }
        if (_native is null)
            return Task.FromResult(MediaEngineResult<IMediaPlaybackSession>.Failure(Error(MediaEngineErrorCode.BackendUnavailable,
                "The GStreamer native runtime is unavailable.", source.AssetId.ToString(), retry: true)));
        if (source.SourceUri.Scheme != Uri.UriSchemeFile)
            return Task.FromResult(MediaEngineResult<IMediaPlaybackSession>.Failure(Error(MediaEngineErrorCode.UnsupportedSource,
                "GStreamer playback currently accepts Files-resolved local media only.", source.AssetId.ToString())));

        try
        {
            var session = _native.Open(source.SourceUri);
            return Task.FromResult(MediaEngineResult<IMediaPlaybackSession>.Success(session));
        }
        catch (Exception exception)
        {
            return Task.FromResult(MediaEngineResult<IMediaPlaybackSession>.Failure(Error(MediaEngineErrorCode.PipelineRejected,
                $"GStreamer could not create the playback pipeline: {exception.Message}", source.AssetId.ToString(), retry: true)));
        }
    }

    private static MediaEngineError Error(MediaEngineErrorCode code, string message, string id, bool retry = false) =>
        new(code, message, "Check the media source and installed GStreamer components, then retry.", id, retry, retry);

    private sealed class GStreamerNative : IDisposable
    {
        internal static readonly string[] GesLibraryNames = ["libges-1.0-0.dll", "libges-1.0.so.0", "libges-1.0.dylib"];
        private static readonly string[] LibraryNames = ["gstreamer-1.0-0.dll", "libgstreamer-1.0.so.0", "libgstreamer-1.0.dylib"];
        private readonly nint _library;
        private readonly GstInit _init;
        private readonly GstParseLaunch _parseLaunch;
        private readonly GstElementSetState _setState;
        private readonly GstElementGetState _getState;
        private readonly GstElementQueryPosition _queryPosition;
        private readonly GstElementSeekSimple _seekSimple;
        private readonly GstElementFactoryFind _factoryFind;
        private readonly GstObjectUnref _objectUnref;
        private readonly GstVersion _version;
        private readonly GErrorFree _errorFree;
        private readonly GErrorGetMessage _errorMessage;
        private bool _disposed;

        internal string Version { get; }

        private GStreamerNative(nint library)
        {
            _library = library;
            _init = Load<GstInit>("gst_init");
            _parseLaunch = Load<GstParseLaunch>("gst_parse_launch");
            _setState = Load<GstElementSetState>("gst_element_set_state");
            _getState = Load<GstElementGetState>("gst_element_get_state");
            _queryPosition = Load<GstElementQueryPosition>("gst_element_query_position");
            _seekSimple = Load<GstElementSeekSimple>("gst_element_seek_simple");
            _factoryFind = Load<GstElementFactoryFind>("gst_element_factory_find");
            _objectUnref = Load<GstObjectUnref>("gst_object_unref");
            _version = Load<GstVersion>("gst_version");
            _errorFree = Load<GErrorFree>("g_error_free");
            _errorMessage = Load<GErrorGetMessage>("g_error_get_message");
            _init(0, 0);
            _version(out var major, out var minor, out var micro, out _);
            Version = $"{major}.{minor}.{micro}";
        }

        internal static GStreamerNative? TryCreate()
        {
            foreach (var name in LibraryNames)
            {
                if (!NativeLibrary.TryLoad(name, out var library)) continue;
                try { return new GStreamerNative(library); }
                catch { NativeLibrary.Free(library); }
            }
            return null;
        }

        internal GStreamerPlaybackSession Open(Uri uri)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var escaped = uri.AbsoluteUri.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
            var description = $"playbin uri=\"{escaped}\"";
            var pipeline = _parseLaunch(description, out var error);
            if (error != 0)
            {
                var message = Marshal.PtrToStringUTF8(_errorMessage(error)) ?? "Unknown GStreamer parse error.";
                _errorFree(error);
                if (pipeline != 0) _objectUnref(pipeline);
                throw new InvalidOperationException(message);
            }
            if (pipeline == 0) throw new InvalidOperationException("GStreamer returned an empty pipeline.");
            return new GStreamerPlaybackSession(pipeline,
                (element, state) => _setState(element, state),
                (nint element, out int state, out int pending, ulong timeout) => _getState(element, out state, out pending, timeout),
                (nint element, int format, out long position) => _queryPosition(element, format, out position),
                (element, format, flags, position) => _seekSimple(element, format, flags, position),
                element => _objectUnref(element));
        }

        internal bool HasFactory(string name)
        {
            var factory = _factoryFind(name);
            if (factory == 0) return false;
            _objectUnref(factory);
            return true;
        }

        private T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
        public void Dispose() { if (!_disposed) { _disposed = true; NativeLibrary.Free(_library); } }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GstInit(int argc, nint argv);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GstParseLaunch([MarshalAs(UnmanagedType.LPUTF8Str)] string description, out nint error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GstElementSetState(nint element, int state);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GstElementGetState(nint element, out int state, out int pending, ulong timeout);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GstElementQueryPosition(nint element, int format, out long position);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GstElementSeekSimple(nint element, int format, int flags, long position);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GstElementFactoryFind([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GstObjectUnref(nint instance);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GstVersion(out uint major, out uint minor, out uint micro, out uint nano);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GErrorFree(nint error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GErrorGetMessage(nint error);
    }
}
