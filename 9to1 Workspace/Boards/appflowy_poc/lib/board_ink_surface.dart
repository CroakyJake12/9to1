import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';

import 'rnote_canvas.dart';

class BoardInkSurface extends StatefulWidget {
  const BoardInkSurface({
    super.key,
    required this.engine,
    required this.tool,
    required this.panMode,
    required this.onChanged,
  });

  final RnoteCanvasEngine engine;
  final RnoteTool tool;
  final bool panMode;
  final VoidCallback onChanged;

  @override
  State<BoardInkSurface> createState() => BoardInkSurfaceState();
}

class BoardInkSurfaceState extends State<BoardInkSurface> {
  RnoteSvgFrame? _frame;
  Size _size = Size.zero;
  Offset? _panStart;

  @override
  void initState() {
    super.initState();
    _refreshFrame();
  }

  @override
  void didUpdateWidget(covariant BoardInkSurface oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.tool != widget.tool && !widget.panMode) widget.engine.setTool(widget.tool);
  }

  void undo() {
    if (widget.engine.undo()) _refreshFrame(save: true);
  }

  void redo() {
    if (widget.engine.redo()) _refreshFrame(save: true);
  }

  void zoomBy(double factor) {
    final current = widget.engine.viewport();
    widget.engine.setViewport(BoardWorldViewport(
      centerX: current.centerX,
      centerY: current.centerY,
      zoom: (current.zoom * factor).clamp(0.2, 6.0),
    ));
    _refreshFrame(save: true);
  }

  void _refreshFrame({bool save = false}) {
    try {
      setState(() => _frame = widget.engine.render());
      if (save) widget.onChanged();
    } on StateError {
      // Rnote has no renderable content until the first completed operation.
    }
  }

  Offset _worldPosition(Offset localPosition) {
    final viewport = widget.engine.viewport();
    return Offset(
      (localPosition.dx - _size.width / 2) / viewport.zoom + viewport.centerX,
      (localPosition.dy - _size.height / 2) / viewport.zoom + viewport.centerY,
    );
  }

  void _onPointerDown(PointerDownEvent event) {
    widget.engine.setViewportSize(_size.width, _size.height);
    if (widget.panMode) {
      _panStart = event.localPosition;
      return;
    }
    widget.engine.setTool(widget.tool);
    final world = _worldPosition(event.localPosition);
    widget.engine.begin(world.dx, world.dy, pressure: event.pressure);
  }

  void _onPointerMove(PointerMoveEvent event) {
    if (widget.panMode) {
      final previous = _panStart;
      if (previous == null) return;
      final viewport = widget.engine.viewport();
      final delta = event.localPosition - previous;
      widget.engine.panBy(-delta.dx / viewport.zoom, -delta.dy / viewport.zoom);
      _panStart = event.localPosition;
      _refreshFrame();
      return;
    }
    final world = _worldPosition(event.localPosition);
    widget.engine.update(world.dx, world.dy, pressure: event.pressure);
  }

  void _onPointerUp(PointerEvent event) {
    if (widget.panMode) {
      _panStart = null;
      widget.onChanged();
      return;
    }
    final world = _worldPosition(event.localPosition);
    widget.engine.end(world.dx, world.dy, pressure: event.pressure);
    _refreshFrame(save: true);
  }

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        _size = constraints.biggest;
        return Listener(
          behavior: HitTestBehavior.opaque,
          onPointerDown: _onPointerDown,
          onPointerMove: _onPointerMove,
          onPointerUp: _onPointerUp,
          onPointerCancel: _onPointerUp,
          child: Stack(
            fit: StackFit.expand,
            children: [
              if (_frame case final frame?) _rnoteSvg(frame),
              Align(
                alignment: Alignment.topLeft,
                child: IgnorePointer(
                  child: Container(
                    margin: const EdgeInsets.all(12),
                    padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                    decoration: BoxDecoration(
                      color: Theme.of(context).colorScheme.surface.withOpacity(0.9),
                      borderRadius: BorderRadius.circular(6),
                    ),
                    child: Text(widget.panMode ? 'Board-world pan' : 'Rnote ${widget.tool.name}'),
                  ),
                ),
              ),
            ],
          ),
        );
      },
    );
  }

  Widget _rnoteSvg(RnoteSvgFrame frame) {
    final viewport = widget.engine.viewport();
    return Positioned(
      left: (frame.x - viewport.centerX) * viewport.zoom + _size.width / 2,
      top: (frame.y - viewport.centerY) * viewport.zoom + _size.height / 2,
      width: frame.width * viewport.zoom,
      height: frame.height * viewport.zoom,
      child: IgnorePointer(child: SvgPicture.string(frame.svg, fit: BoxFit.fill)),
    );
  }
}
