#ifndef CAKEOS_CANVAS_H
#define CAKEOS_CANVAS_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define CAKE_CANVAS_ABI_VERSION 3
#define CAKE_CANVAS_TOOL_PEN 0
#define CAKE_CANVAS_TOOL_HIGHLIGHTER 1
#define CAKE_CANVAS_TOOL_ERASER 2
#define CAKE_CANVAS_TOOL_SELECTOR 3
#define CAKE_CANVAS_TOOL_SHAPE 4
#define CAKE_CANVAS_ERASER_TRASH 0
#define CAKE_CANVAS_ERASER_SPLIT 1
#define CAKE_CANVAS_SHAPE_RECTANGLE 0
#define CAKE_CANVAS_SHAPE_ELLIPSE 1
#define CAKE_CANVAS_SHAPE_LINE 2
#define CAKE_CANVAS_SHAPE_ARROW 3
#define CAKE_CANVAS_RENDER_FORMAT_SVG 1
#define CAKE_CANVAS_COORDINATE_SPACE_DOCUMENT 1

typedef int32_t CakeCanvasStatus;

#define CAKE_CANVAS_STATUS_OK 0
#define CAKE_CANVAS_STATUS_NO_CHANGE 1
#define CAKE_CANVAS_STATUS_INVALID_HANDLE -1
#define CAKE_CANVAS_STATUS_INVALID_ARGUMENT -2
#define CAKE_CANVAS_STATUS_INVALID_STATE -3
#define CAKE_CANVAS_STATUS_ENGINE_ERROR -4
#define CAKE_CANVAS_STATUS_PANIC -5

typedef struct {
    double x;
    double y;
    double pressure;
    double tilt_x;
    double tilt_y;
} CakeCanvasPointerSample;

typedef struct {
    uint8_t* data;
    size_t len;
} CakeCanvasBuffer;

typedef struct {
    uint32_t format;
    uint32_t coordinate_space;
    double x;
    double y;
    double width;
    double height;
    uint8_t* data;
    size_t len;
} CakeCanvasRenderFrame;

typedef struct {
    double center_x;
    double center_y;
    double zoom;
} CakeCanvasViewport;

uint32_t cake_canvas_abi_version(void);
/* ABI 3 describes the existing layouts/core bridge, not full Canvas acceptance.
 * These feature families expose selection export and read-only targeting only;
 * they do NOT promise keyed mutation or detached split-erase support. */
uint32_t cake_canvas_selection_api_version(void);
uint32_t cake_canvas_quick_erase_api_version(void);
void* cake_canvas_engine_new(void);
void cake_canvas_engine_free(void* handle);

CakeCanvasStatus cake_canvas_engine_from_rnote(
    const uint8_t* data,
    size_t len,
    void** out_handle
);

/* Real donor Xournal++ import. Input is gzip-compressed .xopp; dpi is 1..2400.
 * Compressed and expanded input are bounded to 256 MiB. Failure clears
 * out_handle; success transfers one owned engine, released by engine_free. */
CakeCanvasStatus cake_canvas_engine_from_xopp(
    const uint8_t* data, size_t len, double dpi, void** out_handle
);

/* Each handle must be a live engine owned by this library; callers serialize
 * access and release it once. All non-null pointers identify valid, correctly
 * aligned caller-owned storage for the declared length. Foreign/stale pointers
 * are outside this ABI's safety contract. Output buffers must be empty before a
 * call. Successful nonempty buffers belong to the library; use buffer_release.
 * Failure clears outputs. Reading/exporting an unfinished stroke is refused. */

/* Packed native slotmap keys: exactly len/8 little-endian u64 values, at most
 * one million. stroke_keys orders numerically; rendered_stroke_keys orders by
 * genuine donor layer/chronology. Empty documents return a null, zero buffer. */
CakeCanvasStatus cake_canvas_stroke_keys(const void* handle, CakeCanvasBuffer* out_buffer);
CakeCanvasStatus cake_canvas_rendered_stroke_keys(const void* handle, CakeCanvasBuffer* out_buffer);

/* Topmost donor hitbox at finite document coordinates. An ordinary miss is OK
 * with *out_key == 0; this read does not delete content or change history. */
CakeCanvasStatus cake_canvas_quick_erase_target(
    const void* handle, double x, double y, uint64_t* out_key
);

/* A full-fidelity native .rnote containing exactly the requested live entities.
 * Keys are a nonempty, unique, nonzero array of at most one million native u64
 * values. Unknown/trashed keys fail; the original engine/history are untouched. */
CakeCanvasStatus cake_canvas_export_selected_strokes(
    const void* handle, const uint64_t* keys, size_t count, CakeCanvasBuffer* out_buffer
);

CakeCanvasStatus cake_canvas_set_stroke_tool(void* handle, uint32_t tool);
CakeCanvasStatus cake_canvas_set_shape(void* handle, uint32_t shape);

/* Per-tool stroke appearance (ABI 3). Only pen, highlighter and shape accept
 * a style; other tools report invalid-argument. Width is in Rnote document
 * units (0.5..200.0); color channels are 0.0..1.0 sRGB + alpha. */
CakeCanvasStatus cake_canvas_set_pen_style(
    void* handle,
    uint32_t tool,
    double red,
    double green,
    double blue,
    double alpha,
    double width
);

/* Eraser width in 1.0..500.0 with trash (0) or split (1) colliding strokes. */
CakeCanvasStatus cake_canvas_set_eraser(void* handle, double width, uint32_t style);
CakeCanvasStatus cake_canvas_set_viewport_size(void* handle, double width, double height);
CakeCanvasStatus cake_canvas_zoom_to(void* handle, double zoom);
CakeCanvasStatus cake_canvas_pan_by(void* handle, double delta_x, double delta_y);
CakeCanvasStatus cake_canvas_set_viewport_center(void* handle, double center_x, double center_y);
CakeCanvasStatus cake_canvas_get_viewport(const void* handle, CakeCanvasViewport* out_viewport);

CakeCanvasStatus cake_canvas_begin_stroke(void* handle, CakeCanvasPointerSample sample);
CakeCanvasStatus cake_canvas_update_stroke(void* handle, CakeCanvasPointerSample sample);
CakeCanvasStatus cake_canvas_end_stroke(void* handle, CakeCanvasPointerSample sample);

uint8_t cake_canvas_can_undo(const void* handle);
uint8_t cake_canvas_can_redo(const void* handle);

CakeCanvasStatus cake_canvas_undo(void* handle);
CakeCanvasStatus cake_canvas_redo(void* handle);

CakeCanvasStatus cake_canvas_render_frame(const void* handle, CakeCanvasRenderFrame* out_frame);
void cake_canvas_render_frame_release(CakeCanvasRenderFrame* frame);

CakeCanvasStatus cake_canvas_save_rnote(const void* handle, CakeCanvasBuffer* out_buffer);
void cake_canvas_buffer_release(CakeCanvasBuffer* buffer);

#ifdef __cplusplus
}
#endif

#endif /* CAKEOS_CANVAS_H */
