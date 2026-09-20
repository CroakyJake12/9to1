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
void* cake_canvas_engine_new(void);
void cake_canvas_engine_free(void* handle);

CakeCanvasStatus cake_canvas_engine_from_rnote(
    const uint8_t* data,
    size_t len,
    void** out_handle
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
