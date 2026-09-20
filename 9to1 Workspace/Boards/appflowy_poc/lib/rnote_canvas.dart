import 'dart:convert';
import 'dart:ffi';
import 'dart:io';
import 'dart:typed_data';

import 'package:ffi/ffi.dart';

enum RnoteTool { pen, highlighter, eraser, selector, shape }

enum RnoteShape { rectangle, ellipse, line, arrow }

class RnoteSvgFrame {
  const RnoteSvgFrame(this.x, this.y, this.width, this.height, this.svg);

  final double x;
  final double y;
  final double width;
  final double height;
  final String svg;
}

class BoardWorldViewport {
  const BoardWorldViewport({required this.centerX, required this.centerY, required this.zoom});

  final double centerX;
  final double centerY;
  final double zoom;

  Map<String, dynamic> toJson() => {
        'centerX': centerX,
        'centerY': centerY,
        'zoom': zoom,
      };

  factory BoardWorldViewport.fromJson(Map<String, dynamic> json) => BoardWorldViewport(
        centerX: (json['centerX'] as num).toDouble(),
        centerY: (json['centerY'] as num).toDouble(),
        zoom: (json['zoom'] as num).toDouble(),
      );
}

/// Thin Dart FFI owner for the shared Rnote engine. It does not rasterize or
/// synthesize strokes: the SVG and .rnote payload both come from Rnote.
class RnoteCanvasEngine {
  RnoteCanvasEngine._(this._library, this._handle) {
    if (_abi() != 2) {
      close();
      throw StateError('Rnote Canvas ABI 2 is required.');
    }
  }

  factory RnoteCanvasEngine.open() {
    final configuredPath = Platform.environment['CAKEOS_RNOTE_LIBRARY'];
    final defaultName = Platform.isWindows
        ? 'cakeos_canvas_rnote_poc.dll'
        : Platform.isMacOS
            ? 'libcakeos_canvas_rnote_poc.dylib'
            : 'libcakeos_canvas_rnote_poc.so';
    final library = DynamicLibrary.open(configuredPath ?? defaultName);
    final handle = _newFor(library)();
    if (handle == nullptr) throw StateError('Rnote Canvas engine could not be created.');
    return RnoteCanvasEngine._(library, handle);
  }

  final DynamicLibrary _library;
  Pointer<Void> _handle;

  late final int Function() _abi = _library.lookupFunction<Uint32 Function(), int Function()>(
    'cake_canvas_abi_version',
  );
  late final Pointer<Void> Function() _new = _newFor(_library);
  late final void Function(Pointer<Void>) _free = _library.lookupFunction<
      Void Function(Pointer<Void>), void Function(Pointer<Void>)>('cake_canvas_engine_free');
  late final int Function(Pointer<Void>, int) _setTool = _library.lookupFunction<
      Int32 Function(Pointer<Void>, Uint32), int Function(Pointer<Void>, int)>('cake_canvas_set_stroke_tool');
  late final int Function(Pointer<Void>, int) _setShape = _library.lookupFunction<
      Int32 Function(Pointer<Void>, Uint32), int Function(Pointer<Void>, int)>('cake_canvas_set_shape');
  late final int Function(Pointer<Void>, double, double) _setViewportSize = _library.lookupFunction<
      Int32 Function(Pointer<Void>, Double, Double), int Function(Pointer<Void>, double, double)>(
    'cake_canvas_set_viewport_size',
  );
  late final int Function(Pointer<Void>, double) _zoomTo = _library.lookupFunction<
      Int32 Function(Pointer<Void>, Double), int Function(Pointer<Void>, double)>('cake_canvas_zoom_to');
  late final int Function(Pointer<Void>, double, double) _panBy = _library.lookupFunction<
      Int32 Function(Pointer<Void>, Double, Double), int Function(Pointer<Void>, double, double)>(
    'cake_canvas_pan_by',
  );
  late final int Function(Pointer<Void>, double, double) _setViewportCenter = _library.lookupFunction<
      Int32 Function(Pointer<Void>, Double, Double), int Function(Pointer<Void>, double, double)>(
    'cake_canvas_set_viewport_center',
  );
  late final int Function(Pointer<Void>, Pointer<_NativeViewport>) _getViewport = _library.lookupFunction<
      Int32 Function(Pointer<Void>, Pointer<_NativeViewport>),
      int Function(Pointer<Void>, Pointer<_NativeViewport>)>('cake_canvas_get_viewport');
  late final int Function(Pointer<Void>, _NativeSample) _begin = _library.lookupFunction<
      Int32 Function(Pointer<Void>, _NativeSample), int Function(Pointer<Void>, _NativeSample)>(
    'cake_canvas_begin_stroke',
  );
  late final int Function(Pointer<Void>, _NativeSample) _update = _library.lookupFunction<
      Int32 Function(Pointer<Void>, _NativeSample), int Function(Pointer<Void>, _NativeSample)>(
    'cake_canvas_update_stroke',
  );
  late final int Function(Pointer<Void>, _NativeSample) _end = _library.lookupFunction<
      Int32 Function(Pointer<Void>, _NativeSample), int Function(Pointer<Void>, _NativeSample)>(
    'cake_canvas_end_stroke',
  );
  late final int Function(Pointer<Void>) _undo = _library
      .lookupFunction<Int32 Function(Pointer<Void>), int Function(Pointer<Void>)>('cake_canvas_undo');
  late final int Function(Pointer<Void>) _redo = _library
      .lookupFunction<Int32 Function(Pointer<Void>), int Function(Pointer<Void>)>('cake_canvas_redo');
  late final int Function(Pointer<Void>, Pointer<_NativeFrame>) _render = _library.lookupFunction<
      Int32 Function(Pointer<Void>, Pointer<_NativeFrame>),
      int Function(Pointer<Void>, Pointer<_NativeFrame>)>('cake_canvas_render_frame');
  late final void Function(Pointer<_NativeFrame>) _releaseFrame = _library.lookupFunction<
      Void Function(Pointer<_NativeFrame>), void Function(Pointer<_NativeFrame>)>(
    'cake_canvas_render_frame_release',
  );
  late final int Function(Pointer<Void>, Pointer<_NativeBuffer>) _save = _library.lookupFunction<
      Int32 Function(Pointer<Void>, Pointer<_NativeBuffer>),
      int Function(Pointer<Void>, Pointer<_NativeBuffer>)>('cake_canvas_save_rnote');
  late final int Function(Pointer<Uint8>, int, Pointer<Pointer<Void>>) _restore = _library.lookupFunction<
      Int32 Function(Pointer<Uint8>, IntPtr, Pointer<Pointer<Void>>),
      int Function(Pointer<Uint8>, int, Pointer<Pointer<Void>>)>(
    'cake_canvas_engine_from_rnote',
  );
  late final void Function(Pointer<_NativeBuffer>) _releaseBuffer = _library.lookupFunction<
      Void Function(Pointer<_NativeBuffer>), void Function(Pointer<_NativeBuffer>)>(
    'cake_canvas_buffer_release',
  );

  static Pointer<Void> Function() _newFor(DynamicLibrary library) => library
      .lookupFunction<Pointer<Void> Function(), Pointer<Void> Function()>('cake_canvas_engine_new');

  void setTool(RnoteTool tool) => _require(_setTool(_handle, tool.index));

  void setShape(RnoteShape shape) => _require(_setShape(_handle, shape.index));

  void setViewportSize(double width, double height) => _require(_setViewportSize(_handle, width, height));

  void setViewport(BoardWorldViewport viewport) {
    _require(_zoomTo(_handle, viewport.zoom));
    _require(_setViewportCenter(_handle, viewport.centerX, viewport.centerY));
  }

  void panBy(double deltaX, double deltaY) => _require(_panBy(_handle, deltaX, deltaY));

  BoardWorldViewport viewport() {
    final value = calloc<_NativeViewport>();
    try {
      _require(_getViewport(_handle, value));
      return BoardWorldViewport(
        centerX: value.ref.centerX,
        centerY: value.ref.centerY,
        zoom: value.ref.zoom,
      );
    } finally {
      calloc.free(value);
    }
  }

  void begin(double x, double y, {double pressure = 0.5}) => _send(_begin, x, y, pressure);

  void update(double x, double y, {double pressure = 0.5}) => _send(_update, x, y, pressure);

  void end(double x, double y, {double pressure = 0.5}) => _send(_end, x, y, pressure);

  bool undo() => _undo(_handle) == 0;

  bool redo() => _redo(_handle) == 0;

  RnoteSvgFrame render() {
    final frame = calloc<_NativeFrame>();
    try {
      _require(_render(_handle, frame));
      final bytes = Uint8List.fromList(frame.ref.data.asTypedList(frame.ref.length));
      return RnoteSvgFrame(
        frame.ref.x,
        frame.ref.y,
        frame.ref.width,
        frame.ref.height,
        utf8.decode(bytes),
      );
    } finally {
      _releaseFrame(frame);
      calloc.free(frame);
    }
  }

  Uint8List saveRnote() {
    final buffer = calloc<_NativeBuffer>();
    try {
      _require(_save(_handle, buffer));
      return Uint8List.fromList(buffer.ref.data.asTypedList(buffer.ref.length));
    } finally {
      _releaseBuffer(buffer);
      calloc.free(buffer);
    }
  }

  void restoreRnote(Uint8List bytes) {
    if (bytes.isEmpty) return;
    final input = calloc<Uint8>(bytes.length);
    final restored = calloc<Pointer<Void>>();
    try {
      input.asTypedList(bytes.length).setAll(0, bytes);
      _require(_restore(input, bytes.length, restored));
      _free(_handle);
      _handle = restored.value;
    } finally {
      calloc.free(input);
      calloc.free(restored);
    }
  }

  void close() {
    if (_handle != nullptr) {
      _free(_handle);
      _handle = nullptr;
    }
  }

  void _send(int Function(Pointer<Void>, _NativeSample) operation, double x, double y, double pressure) {
    final sample = calloc<_NativeSample>();
    try {
      sample.ref
        ..x = x
        ..y = y
        ..pressure = pressure
        ..tiltX = 0
        ..tiltY = 0;
      _require(operation(_handle, sample.ref));
    } finally {
      calloc.free(sample);
    }
  }

  static void _require(int status) {
    if (status != 0) throw StateError('Rnote Canvas operation failed with status $status.');
  }
}

final class _NativeSample extends Struct {
  @Double()
  external double x;
  @Double()
  external double y;
  @Double()
  external double pressure;
  @Double()
  external double tiltX;
  @Double()
  external double tiltY;
}

final class _NativeBuffer extends Struct {
  external Pointer<Uint8> data;
  @IntPtr()
  external int length;
}

final class _NativeFrame extends Struct {
  @Uint32()
  external int format;
  @Uint32()
  external int coordinateSpace;
  @Double()
  external double x;
  @Double()
  external double y;
  @Double()
  external double width;
  @Double()
  external double height;
  external Pointer<Uint8> data;
  @IntPtr()
  external int length;
}

final class _NativeViewport extends Struct {
  @Double()
  external double centerX;
  @Double()
  external double centerY;
  @Double()
  external double zoom;
}
