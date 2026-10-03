use std::ffi::c_void;
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::ptr;
use std::slice;

use futures::executor::block_on;

use crate::{
    CanvasCoordinateSpace, CanvasPenStyle, CanvasPointerSample, CanvasRenderFormat, CanvasShape,
    CanvasTool, HeadlessCanvasEngine,
};

pub const CAKE_CANVAS_ABI_VERSION: u32 = 3;
pub const CAKE_CANVAS_TOOL_PEN: u32 = 0;
pub const CAKE_CANVAS_TOOL_HIGHLIGHTER: u32 = 1;
pub const CAKE_CANVAS_TOOL_ERASER: u32 = 2;
pub const CAKE_CANVAS_TOOL_SELECTOR: u32 = 3;
pub const CAKE_CANVAS_TOOL_SHAPE: u32 = 4;
pub const CAKE_CANVAS_ERASER_TRASH: u32 = 0;
pub const CAKE_CANVAS_ERASER_SPLIT: u32 = 1;
pub const CAKE_CANVAS_SHAPE_RECTANGLE: u32 = 0;
pub const CAKE_CANVAS_SHAPE_ELLIPSE: u32 = 1;
pub const CAKE_CANVAS_SHAPE_LINE: u32 = 2;
pub const CAKE_CANVAS_SHAPE_ARROW: u32 = 3;
pub const CAKE_CANVAS_RENDER_FORMAT_SVG: u32 = 1;
pub const CAKE_CANVAS_COORDINATE_SPACE_DOCUMENT: u32 = 1;

#[repr(i32)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CakeCanvasStatus {
    Ok = 0,
    NoChange = 1,
    InvalidHandle = -1,
    InvalidArgument = -2,
    InvalidState = -3,
    EngineError = -4,
    Panic = -5,
}

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct CakeCanvasPointerSample {
    pub x: f64,
    pub y: f64,
    pub pressure: f64,
    pub tilt_x: f64,
    pub tilt_y: f64,
}

impl CakeCanvasPointerSample {
    fn into_internal(self) -> Option<CanvasPointerSample> {
        if !self.x.is_finite()
            || !self.y.is_finite()
            || !self.pressure.is_finite()
            || !self.tilt_x.is_finite()
            || !self.tilt_y.is_finite()
        {
            return None;
        }

        let mut sample = CanvasPointerSample::new(self.x, self.y, self.pressure);
        sample.tilt_x = self.tilt_x;
        sample.tilt_y = self.tilt_y;
        Some(sample)
    }
}

#[repr(C)]
#[derive(Debug)]
pub struct CakeCanvasBuffer {
    pub data: *mut u8,
    pub len: usize,
}

impl Default for CakeCanvasBuffer {
    fn default() -> Self {
        Self {
            data: ptr::null_mut(),
            len: 0,
        }
    }
}

#[repr(C)]
#[derive(Debug)]
pub struct CakeCanvasRenderFrame {
    pub format: u32,
    pub coordinate_space: u32,
    pub x: f64,
    pub y: f64,
    pub width: f64,
    pub height: f64,
    pub data: *mut u8,
    pub len: usize,
}

#[repr(C)]
#[derive(Debug, Default)]
pub struct CakeCanvasViewport {
    pub center_x: f64,
    pub center_y: f64,
    pub zoom: f64,
}

impl Default for CakeCanvasRenderFrame {
    fn default() -> Self {
        Self {
            format: 0,
            coordinate_space: 0,
            x: 0.0,
            y: 0.0,
            width: 0.0,
            height: 0.0,
            data: ptr::null_mut(),
            len: 0,
        }
    }
}

fn guard_status(operation: impl FnOnce() -> CakeCanvasStatus) -> CakeCanvasStatus {
    catch_unwind(AssertUnwindSafe(operation)).unwrap_or(CakeCanvasStatus::Panic)
}

fn with_engine_mut<T>(
    handle: *mut c_void,
    operation: impl FnOnce(&mut HeadlessCanvasEngine) -> T,
) -> Option<T> {
    if handle.is_null() {
        return None;
    }

    let engine = unsafe { handle.cast::<HeadlessCanvasEngine>().as_mut()? };
    Some(operation(engine))
}

fn with_engine<T>(
    handle: *const c_void,
    operation: impl FnOnce(&HeadlessCanvasEngine) -> T,
) -> Option<T> {
    if handle.is_null() {
        return None;
    }

    let engine = unsafe { handle.cast::<HeadlessCanvasEngine>().as_ref()? };
    Some(operation(engine))
}

fn owned_buffer(bytes: Vec<u8>) -> CakeCanvasBuffer {
    if bytes.is_empty() {
        return CakeCanvasBuffer::default();
    }

    let boxed = bytes.into_boxed_slice();
    let len = boxed.len();
    let data = Box::into_raw(boxed).cast::<u8>();
    CakeCanvasBuffer { data, len }
}

unsafe fn release_owned_bytes(data: *mut u8, len: usize) {
    if data.is_null() || len == 0 {
        return;
    }

    let raw_slice = ptr::slice_from_raw_parts_mut(data, len);
    unsafe {
        drop(Box::from_raw(raw_slice));
    }
}

fn tool_from_abi(tool: u32) -> Option<CanvasTool> {
    match tool {
        CAKE_CANVAS_TOOL_PEN => Some(CanvasTool::Pen),
        CAKE_CANVAS_TOOL_HIGHLIGHTER => Some(CanvasTool::Highlighter),
        CAKE_CANVAS_TOOL_ERASER => Some(CanvasTool::Eraser),
        CAKE_CANVAS_TOOL_SELECTOR => Some(CanvasTool::Selector),
        CAKE_CANVAS_TOOL_SHAPE => Some(CanvasTool::Shape),
        _ => None,
    }
}

fn shape_from_abi(shape: u32) -> Option<CanvasShape> {
    match shape {
        CAKE_CANVAS_SHAPE_RECTANGLE => Some(CanvasShape::Rectangle),
        CAKE_CANVAS_SHAPE_ELLIPSE => Some(CanvasShape::Ellipse),
        CAKE_CANVAS_SHAPE_LINE => Some(CanvasShape::Line),
        CAKE_CANVAS_SHAPE_ARROW => Some(CanvasShape::Arrow),
        _ => None,
    }
}

fn render_format_to_abi(format: CanvasRenderFormat) -> u32 {
    match format {
        CanvasRenderFormat::Svg => CAKE_CANVAS_RENDER_FORMAT_SVG,
    }
}

fn coordinate_space_to_abi(space: CanvasCoordinateSpace) -> u32 {
    match space {
        CanvasCoordinateSpace::Document => CAKE_CANVAS_COORDINATE_SPACE_DOCUMENT,
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_abi_version() -> u32 {
    CAKE_CANVAS_ABI_VERSION
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_engine_new() -> *mut c_void {
    catch_unwind(AssertUnwindSafe(|| {
        Box::into_raw(Box::new(HeadlessCanvasEngine::new())).cast::<c_void>()
    }))
    .unwrap_or(ptr::null_mut())
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_engine_free(handle: *mut c_void) {
    if handle.is_null() {
        return;
    }

    let _ = catch_unwind(AssertUnwindSafe(|| unsafe {
        drop(Box::from_raw(handle.cast::<HeadlessCanvasEngine>()));
    }));
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_engine_from_rnote(
    data: *const u8,
    len: usize,
    out_handle: *mut *mut c_void,
) -> CakeCanvasStatus {
    guard_status(|| {
        if out_handle.is_null() || data.is_null() || len == 0 {
            return CakeCanvasStatus::InvalidArgument;
        }

        unsafe {
            *out_handle = ptr::null_mut();
        }

        let bytes = unsafe { slice::from_raw_parts(data, len) }.to_vec();
        match block_on(HeadlessCanvasEngine::from_rnote(bytes)) {
            Ok(engine) => {
                unsafe {
                    *out_handle = Box::into_raw(Box::new(engine)).cast::<c_void>();
                }
                CakeCanvasStatus::Ok
            }
            Err(_) => CakeCanvasStatus::EngineError,
        }
    })
}

/// Additive ABI 3 entry point. Every input buffer is borrowed only for this call.
#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_engine_from_xopp(
    data: *const u8,
    len: usize,
    dpi: f64,
    out_handle: *mut *mut c_void,
) -> CakeCanvasStatus {
    guard_status(|| {
        if out_handle.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        unsafe { *out_handle = ptr::null_mut(); }
        if data.is_null() || len == 0 || !dpi.is_finite() || !(1.0..=2400.0).contains(&dpi) {
            return CakeCanvasStatus::InvalidArgument;
        }
        let bytes = unsafe { slice::from_raw_parts(data, len) }.to_vec();
        match block_on(HeadlessCanvasEngine::from_xopp(bytes, dpi)) {
            Ok(engine) => {
                unsafe { *out_handle = Box::into_raw(Box::new(engine)).cast::<c_void>(); }
                CakeCanvasStatus::Ok
            }
            Err(_) => CakeCanvasStatus::EngineError,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_stroke_tool(
    handle: *mut c_void,
    tool: u32,
) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(tool) = tool_from_abi(tool) else {
            return CakeCanvasStatus::InvalidArgument;
        };
        let Some(result) = with_engine_mut(handle, |engine| engine.set_tool(tool)) else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_shape(
    handle: *mut c_void,
    shape: u32,
) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(shape) = shape_from_abi(shape) else {
            return CakeCanvasStatus::InvalidArgument;
        };
        let Some(result) = with_engine_mut(handle, |engine| engine.set_shape(shape)) else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

fn styled_tool_from_abi(tool: u32) -> Option<CanvasTool> {
    match tool {
        CAKE_CANVAS_TOOL_PEN => Some(CanvasTool::Pen),
        CAKE_CANVAS_TOOL_HIGHLIGHTER => Some(CanvasTool::Highlighter),
        CAKE_CANVAS_TOOL_SHAPE => Some(CanvasTool::Shape),
        _ => None,
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_pen_style(
    handle: *mut c_void,
    tool: u32,
    red: f64,
    green: f64,
    blue: f64,
    alpha: f64,
    width: f64,
) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(tool) = styled_tool_from_abi(tool) else {
            return CakeCanvasStatus::InvalidArgument;
        };
        let style = CanvasPenStyle {
            color: [red, green, blue, alpha],
            width,
        };
        // Validate at the boundary so callers get InvalidArgument for bad
        // values and InvalidState only for a mid-stroke change.
        if handle.is_null() {
            return CakeCanvasStatus::InvalidHandle;
        }
        if style.validate().is_err() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(result) = with_engine_mut(handle, |engine| engine.set_pen_style(tool, style))
        else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_eraser(
    handle: *mut c_void,
    width: f64,
    style: u32,
) -> CakeCanvasStatus {
    guard_status(|| {
        let split = match style {
            CAKE_CANVAS_ERASER_TRASH => false,
            CAKE_CANVAS_ERASER_SPLIT => true,
            _ => return CakeCanvasStatus::InvalidArgument,
        };
        // Mirror the engine limits so bad values report InvalidArgument;
        // InvalidState then means only a mid-stroke change.
        if handle.is_null() {
            return CakeCanvasStatus::InvalidHandle;
        }
        if !width.is_finite() || !(1.0..=500.0).contains(&width) {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(result) = with_engine_mut(handle, |engine| engine.set_eraser(width, split))
        else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_viewport_size(
    handle: *mut c_void,
    width: f64,
    height: f64,
) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| engine.set_viewport_size(width, height)) else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidArgument,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_zoom_to(handle: *mut c_void, zoom: f64) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| engine.zoom_to(zoom)) else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidArgument,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_pan_by(
    handle: *mut c_void,
    delta_x: f64,
    delta_y: f64,
) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| engine.pan_by(delta_x, delta_y)) else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidArgument,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_viewport_center(
    handle: *mut c_void,
    center_x: f64,
    center_y: f64,
) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| engine.set_viewport_center(center_x, center_y)) else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidArgument,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_get_viewport(
    handle: *const c_void,
    out_viewport: *mut CakeCanvasViewport,
) -> CakeCanvasStatus {
    guard_status(|| {
        if out_viewport.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(viewport) = with_engine(handle, HeadlessCanvasEngine::viewport) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        unsafe {
            *out_viewport = CakeCanvasViewport {
                center_x: viewport.center_x,
                center_y: viewport.center_y,
                zoom: viewport.zoom,
            };
        }
        CakeCanvasStatus::Ok
    })
}

fn stroke_event(
    handle: *mut c_void,
    sample: CakeCanvasPointerSample,
    operation: impl FnOnce(&mut HeadlessCanvasEngine, CanvasPointerSample) -> anyhow::Result<()>,
) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(sample) = sample.into_internal() else {
            return CakeCanvasStatus::InvalidArgument;
        };
        let Some(result) = with_engine_mut(handle, |engine| operation(engine, sample)) else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_begin_stroke(
    handle: *mut c_void,
    sample: CakeCanvasPointerSample,
) -> CakeCanvasStatus {
    stroke_event(handle, sample, HeadlessCanvasEngine::begin_stroke)
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_update_stroke(
    handle: *mut c_void,
    sample: CakeCanvasPointerSample,
) -> CakeCanvasStatus {
    stroke_event(handle, sample, HeadlessCanvasEngine::update_stroke)
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_end_stroke(
    handle: *mut c_void,
    sample: CakeCanvasPointerSample,
) -> CakeCanvasStatus {
    stroke_event(handle, sample, HeadlessCanvasEngine::end_stroke)
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_can_undo(handle: *const c_void) -> u8 {
    with_engine(handle, HeadlessCanvasEngine::can_undo)
        .unwrap_or(false)
        .into()
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_can_redo(handle: *const c_void) -> u8 {
    with_engine(handle, HeadlessCanvasEngine::can_redo)
        .unwrap_or(false)
        .into()
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_undo(handle: *mut c_void) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, HeadlessCanvasEngine::undo) else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(true) => CakeCanvasStatus::Ok,
            Ok(false) => CakeCanvasStatus::NoChange,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_redo(handle: *mut c_void) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, HeadlessCanvasEngine::redo) else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(true) => CakeCanvasStatus::Ok,
            Ok(false) => CakeCanvasStatus::NoChange,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_render_visible_keys(
    handle: *const c_void, keys: *const u64, count: usize,
    out_frame: *mut CakeCanvasRenderFrame,
) -> CakeCanvasStatus {
    guard_status(|| {
        if out_frame.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        unsafe {
            *out_frame = CakeCanvasRenderFrame::default();
        }

        if count > 1_000_000 || count != 0 && keys.is_null() { return CakeCanvasStatus::InvalidArgument; }
        let keys = if count == 0 { &[] } else { unsafe { slice::from_raw_parts(keys, count) } };
        let Some(result) = with_engine(handle, |engine| block_on(engine.render_visible_keys(keys))) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        let frame = match result {
            Ok(frame) => frame,
            Err(_) => return CakeCanvasStatus::EngineError,
        };
        let buffer = owned_buffer(frame.bytes);

        unsafe {
            *out_frame = CakeCanvasRenderFrame {
                format: render_format_to_abi(frame.format),
                coordinate_space: coordinate_space_to_abi(frame.coordinate_space),
                x: frame.bounds.x,
                y: frame.bounds.y,
                width: frame.bounds.width,
                height: frame.bounds.height,
                data: buffer.data,
                len: buffer.len,
            };
        }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_render_frame(
    handle: *const c_void,
    out_frame: *mut CakeCanvasRenderFrame,
) -> CakeCanvasStatus {
    guard_status(|| {
        if out_frame.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        unsafe {
            *out_frame = CakeCanvasRenderFrame::default();
        }

        let Some(result) = with_engine(handle, |engine| block_on(engine.render_frame())) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        let frame = match result {
            Ok(frame) => frame,
            Err(_) => return CakeCanvasStatus::EngineError,
        };
        let buffer = owned_buffer(frame.bytes);

        unsafe {
            *out_frame = CakeCanvasRenderFrame {
                format: render_format_to_abi(frame.format),
                coordinate_space: coordinate_space_to_abi(frame.coordinate_space),
                x: frame.bounds.x,
                y: frame.bounds.y,
                width: frame.bounds.width,
                height: frame.bounds.height,
                data: buffer.data,
                len: buffer.len,
            };
        }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_render_frame_release(frame: *mut CakeCanvasRenderFrame) {
    if frame.is_null() {
        return;
    }

    let _ = catch_unwind(AssertUnwindSafe(|| unsafe {
        let frame_ref = &mut *frame;
        release_owned_bytes(frame_ref.data, frame_ref.len);
        *frame_ref = CakeCanvasRenderFrame::default();
    }));
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_save_rnote(
    handle: *const c_void,
    out_buffer: *mut CakeCanvasBuffer,
) -> CakeCanvasStatus {
    guard_status(|| {
        if out_buffer.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        unsafe {
            *out_buffer = CakeCanvasBuffer::default();
        }

        let Some(result) = with_engine(handle, |engine| block_on(engine.save_rnote())) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(bytes) => {
                unsafe {
                    *out_buffer = owned_buffer(bytes);
                }
                CakeCanvasStatus::Ok
            }
            Err(_) => CakeCanvasStatus::EngineError,
        }
    })
}

/// Additive selection API; ABI 3 drawing/lifecycle exports remain compatible.
#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_selection_api_version() -> u32 { 1 }

/// Additive keyed mutation API; drawing ABI and read-only selection stay stable.
#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_stroke_mutation_api_version() -> u32 { 1 }

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_delete_stroke(handle: *mut c_void, key: u64) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| engine.delete_stroke(key)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result { Ok(()) => CakeCanvasStatus::Ok, Err(_) => CakeCanvasStatus::InvalidArgument }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_translate_stroke(handle: *mut c_void, key: u64, delta_x: f64, delta_y: f64) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| engine.translate_stroke(key, delta_x, delta_y)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result { Ok(()) => CakeCanvasStatus::Ok, Err(_) => CakeCanvasStatus::InvalidArgument }
    })
}

/// Owned buffer of little-endian u64 donor keys, released by buffer_release.
#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_stroke_keys(handle: *const c_void, out_keys: *mut CakeCanvasBuffer) -> CakeCanvasStatus {
    guard_status(|| {
        if out_keys.is_null() { return CakeCanvasStatus::InvalidArgument; }
        unsafe { *out_keys = CakeCanvasBuffer::default(); }
        let Some(keys) = with_engine(handle, HeadlessCanvasEngine::stroke_keys) else { return CakeCanvasStatus::InvalidHandle; };
        let bytes = keys.iter().flat_map(|key| key.to_le_bytes()).collect::<Vec<_>>();
        unsafe { *out_keys = owned_buffer(bytes); }
        CakeCanvasStatus::Ok
    })
}

/// Read-only, selected-only structured export. Missing/duplicate keys reject
/// the request rather than exporting a different selection or entire document.
#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_export_selected_strokes(handle: *const c_void, keys: *const u64, key_count: usize,
    out_native: *mut CakeCanvasBuffer) -> CakeCanvasStatus {
    guard_status(|| {
        if out_native.is_null() { return CakeCanvasStatus::InvalidArgument; }
        unsafe { *out_native = CakeCanvasBuffer::default(); }
        if keys.is_null() || key_count == 0 || key_count > 1_000_000 { return CakeCanvasStatus::InvalidArgument; }
        let keys = unsafe { slice::from_raw_parts(keys, key_count) };
        let Some(result) = with_engine(handle, |engine| block_on(engine.selected_strokes_rnote(keys))) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(bytes) => { unsafe { *out_native = owned_buffer(bytes); } CakeCanvasStatus::Ok },
            Err(_) => CakeCanvasStatus::InvalidArgument,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_visible_keys_render_api_version() -> u32 { 1 }

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_user_layer_rank_api_version() -> u32 { 1 }

/// Owned little-endian uint32 ranks corresponding exactly to the supplied keys.
#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_read_user_layer_ranks(handle: *const c_void, keys: *const u64,
    count: usize, out_ranks: *mut CakeCanvasBuffer) -> CakeCanvasStatus {
    guard_status(|| {
        if out_ranks.is_null() { return CakeCanvasStatus::InvalidArgument; }
        unsafe { *out_ranks = CakeCanvasBuffer::default(); }
        if keys.is_null() || count == 0 || count > 1_000_000 { return CakeCanvasStatus::InvalidArgument; }
        let keys = unsafe { slice::from_raw_parts(keys, count) };
        let Some(result) = with_engine(handle, |engine| engine.read_user_layer_ranks(keys)) else { return CakeCanvasStatus::InvalidHandle; };
        match result {
            Ok(ranks) => { unsafe { *out_ranks = owned_buffer(ranks.iter().flat_map(|rank| rank.to_le_bytes()).collect()); } CakeCanvasStatus::Ok },
            Err(_) => CakeCanvasStatus::InvalidArgument,
        }
    })
}

/// Additive ABI: actual donor user-layer rank assignment, all-or-nothing.
#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_assign_user_layer_ranks(handle: *mut c_void, keys: *const u64,
    ranks: *const u32, count: usize) -> CakeCanvasStatus {
    guard_status(|| {
        if keys.is_null() || ranks.is_null() || count == 0 || count > 1_000_000 { return CakeCanvasStatus::InvalidArgument; }
        let keys = unsafe { slice::from_raw_parts(keys, count) };
        let ranks = unsafe { slice::from_raw_parts(ranks, count) };
        let Some(result) = with_engine_mut(handle, |engine| engine.assign_user_layer_ranks(keys, ranks)) else { return CakeCanvasStatus::InvalidHandle; };
        match result { Ok(()) => CakeCanvasStatus::Ok, Err(_) => CakeCanvasStatus::InvalidArgument }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_buffer_release(buffer: *mut CakeCanvasBuffer) {
    if buffer.is_null() {
        return;
    }

    let _ = catch_unwind(AssertUnwindSafe(|| unsafe {
        let buffer_ref = &mut *buffer;
        release_owned_bytes(buffer_ref.data, buffer_ref.len);
        *buffer_ref = CakeCanvasBuffer::default();
    }));
}

#[cfg(test)]
mod tests {
    use super::*;

    fn sample(x: f64, y: f64, pressure: f64) -> CakeCanvasPointerSample {
        CakeCanvasPointerSample {
            x,
            y,
            pressure,
            tilt_x: 0.0,
            tilt_y: 0.0,
        }
    }

    #[test]
    fn native_bridge_draws_renders_saves_and_reloads() {
        let handle = cake_canvas_engine_new();
        assert!(!handle.is_null());
        assert_eq!(cake_canvas_abi_version(), 3);

        assert_eq!(cake_canvas_begin_stroke(handle, sample(120.0, 120.0, 0.2)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_update_stroke(handle, sample(180.0, 155.0, 0.6)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_end_stroke(handle, sample(240.0, 200.0, 0.8)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_can_undo(handle), 1);

        assert_eq!(
            cake_canvas_set_stroke_tool(handle, CAKE_CANVAS_TOOL_HIGHLIGHTER),
            CakeCanvasStatus::Ok
        );
        assert_eq!(cake_canvas_begin_stroke(handle, sample(120.0, 180.0, 0.5)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_end_stroke(handle, sample(240.0, 180.0, 0.5)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_set_shape(handle, CAKE_CANVAS_SHAPE_ELLIPSE), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_set_stroke_tool(handle, CAKE_CANVAS_TOOL_SHAPE), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_begin_stroke(handle, sample(270.0, 100.0, 0.5)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_end_stroke(handle, sample(340.0, 170.0, 0.5)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_set_viewport_size(handle, 1280.0, 720.0), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_zoom_to(handle, 1.5), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_pan_by(handle, 100.0, -50.0), CakeCanvasStatus::Ok);
        let mut viewport = CakeCanvasViewport::default();
        assert_eq!(cake_canvas_get_viewport(handle, &mut viewport), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_set_viewport_center(handle, viewport.center_x, viewport.center_y), CakeCanvasStatus::Ok);

        let mut frame = CakeCanvasRenderFrame::default();
        assert_eq!(cake_canvas_render_frame(handle, &mut frame), CakeCanvasStatus::Ok);
        assert_eq!(frame.format, CAKE_CANVAS_RENDER_FORMAT_SVG);
        assert_eq!(frame.coordinate_space, CAKE_CANVAS_COORDINATE_SPACE_DOCUMENT);
        assert!(frame.width > 0.0 && frame.height > 0.0);
        assert!(frame.len > 200);
        let svg = unsafe { slice::from_raw_parts(frame.data, frame.len) };
        assert!(std::str::from_utf8(svg).unwrap().contains("<svg"));
        cake_canvas_render_frame_release(&mut frame);
        assert!(frame.data.is_null());
        assert_eq!(frame.len, 0);

        let mut native = CakeCanvasBuffer::default();
        assert_eq!(cake_canvas_save_rnote(handle, &mut native), CakeCanvasStatus::Ok);
        assert!(native.len > 100);

        let mut restored = ptr::null_mut();
        assert_eq!(
            cake_canvas_engine_from_rnote(native.data, native.len, &mut restored),
            CakeCanvasStatus::Ok
        );
        assert!(!restored.is_null());

        let mut restored_frame = CakeCanvasRenderFrame::default();
        assert_eq!(
            cake_canvas_render_frame(restored, &mut restored_frame),
            CakeCanvasStatus::Ok
        );
        assert!(restored_frame.len > 200);

        cake_canvas_render_frame_release(&mut restored_frame);
        cake_canvas_buffer_release(&mut native);
        cake_canvas_engine_free(restored);
        cake_canvas_engine_free(handle);
    }

    #[test]
    fn native_bridge_rejects_bad_handles_arguments_and_mid_stroke_changes() {
        let valid = sample(10.0, 10.0, 0.5);
        assert_eq!(
            cake_canvas_begin_stroke(ptr::null_mut(), valid),
            CakeCanvasStatus::InvalidHandle
        );

        let handle = cake_canvas_engine_new();
        assert_eq!(
            cake_canvas_set_stroke_tool(handle, 999),
            CakeCanvasStatus::InvalidArgument
        );
        assert_eq!(cake_canvas_begin_stroke(handle, valid), CakeCanvasStatus::Ok);
        assert_eq!(
            cake_canvas_set_stroke_tool(handle, CAKE_CANVAS_TOOL_ERASER),
            CakeCanvasStatus::InvalidState
        );
        assert_eq!(cake_canvas_set_shape(handle, CAKE_CANVAS_SHAPE_ARROW), CakeCanvasStatus::InvalidState);
        assert_eq!(cake_canvas_zoom_to(handle, -1.0), CakeCanvasStatus::InvalidArgument);
        assert_eq!(cake_canvas_undo(handle), CakeCanvasStatus::InvalidState);
        assert_eq!(cake_canvas_end_stroke(handle, valid), CakeCanvasStatus::Ok);

        let invalid = CakeCanvasPointerSample {
            x: f64::NAN,
            ..valid
        };
        assert_eq!(
            cake_canvas_begin_stroke(handle, invalid),
            CakeCanvasStatus::InvalidArgument
        );
        cake_canvas_engine_free(handle);
    }

    #[test]
    fn pen_style_and_eraser_options_apply_and_validate() {
        let handle = cake_canvas_engine_new();
        assert!(!handle.is_null());

        // Configurable tools accept color + width.
        assert_eq!(
            cake_canvas_set_pen_style(handle, CAKE_CANVAS_TOOL_PEN, 1.0, 0.0, 0.0, 1.0, 5.0),
            CakeCanvasStatus::Ok
        );
        assert_eq!(
            cake_canvas_set_pen_style(handle, CAKE_CANVAS_TOOL_HIGHLIGHTER, 1.0, 1.0, 0.0, 0.5, 12.0),
            CakeCanvasStatus::Ok
        );
        assert_eq!(
            cake_canvas_set_pen_style(handle, CAKE_CANVAS_TOOL_SHAPE, 0.0, 0.0, 1.0, 1.0, 3.0),
            CakeCanvasStatus::Ok
        );
        assert_eq!(
            cake_canvas_set_eraser(handle, 24.0, CAKE_CANVAS_ERASER_SPLIT),
            CakeCanvasStatus::Ok
        );
        assert_eq!(
            cake_canvas_set_eraser(handle, 8.0, CAKE_CANVAS_ERASER_TRASH),
            CakeCanvasStatus::Ok
        );

        // Tools without a style slot are rejected, not ignored.
        assert_eq!(
            cake_canvas_set_pen_style(handle, CAKE_CANVAS_TOOL_ERASER, 1.0, 0.0, 0.0, 1.0, 5.0),
            CakeCanvasStatus::InvalidArgument
        );
        assert_eq!(
            cake_canvas_set_pen_style(handle, CAKE_CANVAS_TOOL_SELECTOR, 1.0, 0.0, 0.0, 1.0, 5.0),
            CakeCanvasStatus::InvalidArgument
        );
        assert_eq!(
            cake_canvas_set_pen_style(handle, 999, 1.0, 0.0, 0.0, 1.0, 5.0),
            CakeCanvasStatus::InvalidArgument
        );

        // Out-of-range values are argument errors.
        assert_eq!(
            cake_canvas_set_pen_style(handle, CAKE_CANVAS_TOOL_PEN, 2.0, 0.0, 0.0, 1.0, 5.0),
            CakeCanvasStatus::InvalidArgument
        );
        assert_eq!(
            cake_canvas_set_pen_style(handle, CAKE_CANVAS_TOOL_PEN, 1.0, 0.0, 0.0, 1.0, 0.0),
            CakeCanvasStatus::InvalidArgument
        );
        assert_eq!(
            cake_canvas_set_pen_style(handle, CAKE_CANVAS_TOOL_PEN, f64::NAN, 0.0, 0.0, 1.0, 5.0),
            CakeCanvasStatus::InvalidArgument
        );
        assert_eq!(
            cake_canvas_set_eraser(handle, 0.5, CAKE_CANVAS_ERASER_TRASH),
            CakeCanvasStatus::InvalidArgument
        );
        assert_eq!(
            cake_canvas_set_eraser(handle, 24.0, 7),
            CakeCanvasStatus::InvalidArgument
        );
        assert_eq!(
            cake_canvas_set_pen_style(ptr::null_mut(), CAKE_CANVAS_TOOL_PEN, 1.0, 0.0, 0.0, 1.0, 5.0),
            CakeCanvasStatus::InvalidHandle
        );

        // Style changes are rejected mid-stroke like tool changes.
        assert_eq!(
            cake_canvas_begin_stroke(handle, sample(10.0, 10.0, 0.5)),
            CakeCanvasStatus::Ok
        );
        assert_eq!(
            cake_canvas_set_pen_style(handle, CAKE_CANVAS_TOOL_PEN, 0.0, 1.0, 0.0, 1.0, 5.0),
            CakeCanvasStatus::InvalidState
        );
        assert_eq!(
            cake_canvas_set_eraser(handle, 24.0, CAKE_CANVAS_ERASER_TRASH),
            CakeCanvasStatus::InvalidState
        );
        assert_eq!(
            cake_canvas_end_stroke(handle, sample(20.0, 20.0, 0.5)),
            CakeCanvasStatus::Ok
        );

        // Styled strokes still render and persist through the boundary.
        let mut frame = CakeCanvasRenderFrame::default();
        assert_eq!(cake_canvas_render_frame(handle, &mut frame), CakeCanvasStatus::Ok);
        assert!(frame.len > 200);
        cake_canvas_render_frame_release(&mut frame);

        cake_canvas_engine_free(handle);
    }
}

/// Additive read-only hit API; drawing ABI 3 and mutation API 1 unchanged.
#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_quick_erase_api_version() -> u32 { 1 }

/// out_key is zero for a miss; actual slot-map keys are nonzero. No mutation.
#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_quick_erase_target(handle: *const c_void, x: f64, y: f64, out_key: *mut u64) -> CakeCanvasStatus {
    guard_status(|| {
        if out_key.is_null() { return CakeCanvasStatus::InvalidArgument; }
        unsafe { *out_key = 0; }
        let Some(result) = with_engine(handle, |engine| engine.quick_erase_target(x, y)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(target) => { unsafe { *out_key = target.unwrap_or(0); } CakeCanvasStatus::Ok },
            Err(_) => CakeCanvasStatus::InvalidArgument,
        }
    })
}

/// Owned little-endian u64 render-order buffer, released by buffer_release.
#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_rendered_stroke_keys(handle: *const c_void, out_keys: *mut CakeCanvasBuffer) -> CakeCanvasStatus {
    guard_status(|| {
        if out_keys.is_null() { return CakeCanvasStatus::InvalidArgument; }
        unsafe { *out_keys = CakeCanvasBuffer::default(); }
        let Some(result) = with_engine(handle, HeadlessCanvasEngine::rendered_stroke_keys) else { return CakeCanvasStatus::InvalidHandle; };
        match result {
            Ok(keys) => {
                let bytes = keys.iter().flat_map(|key| key.to_le_bytes()).collect::<Vec<_>>();
                unsafe { *out_keys = owned_buffer(bytes); }
                CakeCanvasStatus::Ok
            },
            Err(_) => CakeCanvasStatus::InvalidArgument,
        }
    })
}

#[cfg(test)]
mod quick_tests {
    use super::*;
    #[test]
    fn quick_native_boundary_uses_actual_engine_render_order_and_resets_refusal_outputs() {
        struct Owned(*mut c_void);
        impl Drop for Owned { fn drop(&mut self) { cake_canvas_engine_free(self.0); } }
        let handle = Owned(cake_canvas_engine_new());
        assert!(!handle.0.is_null());
        assert_eq!(cake_canvas_quick_erase_api_version(), 1);
        for pressure in [0.4, 0.7] {
            let sample = CakeCanvasPointerSample { x: 100.0, y: 100.0, pressure, tilt_x: 0.0, tilt_y: 0.0 };
            assert_eq!(cake_canvas_begin_stroke(handle.0, sample), CakeCanvasStatus::Ok);
            assert_eq!(cake_canvas_end_stroke(handle.0, CakeCanvasPointerSample { x: 300.0, ..sample }), CakeCanvasStatus::Ok);
        }
        let mut buffer = CakeCanvasBuffer::default();
        assert_eq!(cake_canvas_rendered_stroke_keys(handle.0, &mut buffer), CakeCanvasStatus::Ok);
        assert_eq!(buffer.len, 16);
        let bytes = unsafe { std::slice::from_raw_parts(buffer.data, buffer.len) };
        let top = u64::from_le_bytes(bytes[8..16].try_into().unwrap());
        cake_canvas_buffer_release(&mut buffer);
        let mut key = u64::MAX;
        assert_eq!(cake_canvas_quick_erase_target(handle.0, 200.0, 100.0, &mut key), CakeCanvasStatus::Ok);
        assert_eq!(key, top);
        assert_eq!(cake_canvas_quick_erase_target(handle.0, 200.0, 300.0, &mut key), CakeCanvasStatus::Ok);
        assert_eq!(key, 0);
        key = u64::MAX;
        assert_eq!(cake_canvas_quick_erase_target(handle.0, f64::NAN, 100.0, &mut key), CakeCanvasStatus::InvalidArgument);
        assert_eq!(key, 0);
        key = u64::MAX;
        assert_eq!(cake_canvas_quick_erase_target(std::ptr::null(), 200.0, 100.0, &mut key), CakeCanvasStatus::InvalidHandle);
        assert_eq!(key, 0);
        assert_eq!(cake_canvas_quick_erase_target(handle.0, 200.0, 100.0, std::ptr::null_mut()), CakeCanvasStatus::InvalidArgument);
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_split_erase_api_version() -> u32 { 1 }

/// Detached native candidate + exact typed path receipt; both owned buffers.
#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_split_erase_candidate(handle: *const c_void, samples: *const CakeCanvasPointerSample,
    count: usize, width: f64, out_native: *mut CakeCanvasBuffer, out_receipt: *mut CakeCanvasBuffer) -> CakeCanvasStatus {
    guard_status(|| {
        // Reset every valid output on refusal, including aliased/one-null outputs.
        if !out_native.is_null() { unsafe { *out_native = CakeCanvasBuffer::default(); } }
        if !out_receipt.is_null() && out_receipt != out_native { unsafe { *out_receipt = CakeCanvasBuffer::default(); } }
        if out_native.is_null() || out_receipt.is_null() || out_native == out_receipt { return CakeCanvasStatus::InvalidArgument; }
        if samples.is_null() || count < 2 || count > 1_000_000 { return CakeCanvasStatus::InvalidArgument; }
        let samples = unsafe { std::slice::from_raw_parts(samples, count) }.iter().copied()
            .map(CakeCanvasPointerSample::into_internal).collect::<Option<Vec<_>>>();
        let Some(samples) = samples else { return CakeCanvasStatus::InvalidArgument; };
        let Some(result) = with_engine(handle, |engine| block_on(engine.split_erase_candidate(&samples, width))) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok((native, receipt)) => {
                unsafe { *out_native = owned_buffer(native); *out_receipt = owned_buffer(receipt); }
                CakeCanvasStatus::Ok
            },
            Err(_) => CakeCanvasStatus::InvalidArgument,
        }
    })
}

#[cfg(test)]
mod split_tests {
    use super::*;
    struct Engine(*mut c_void);
    impl Drop for Engine { fn drop(&mut self) { cake_canvas_engine_free(self.0); } }
    struct Buffer(CakeCanvasBuffer);
    impl Drop for Buffer { fn drop(&mut self) { cake_canvas_buffer_release(&mut self.0); } }
    impl Buffer {
        fn new() -> Self { Self(CakeCanvasBuffer::default()) }
        fn bytes(&self) -> Vec<u8> {
            assert!(!self.0.data.is_null());
            unsafe { std::slice::from_raw_parts(self.0.data,self.0.len).to_vec() }
        }
    }
    fn save(handle: *const c_void) -> Vec<u8> {
        let mut buffer=Buffer::new();
        assert_eq!(cake_canvas_save_rnote(handle,&mut buffer.0),CakeCanvasStatus::Ok);
        buffer.bytes()
    }
    #[test]
    fn split_native_boundary_returns_actual_two_fragment_candidate_preserves_original_and_resets_all_refusals() {
        assert_eq!(cake_canvas_abi_version(),3);
        assert_eq!(cake_canvas_selection_api_version(),1);
        assert_eq!(cake_canvas_stroke_mutation_api_version(),1);
        assert_eq!(cake_canvas_quick_erase_api_version(),1);
        assert_eq!(cake_canvas_split_erase_api_version(),1);
        let engine=Engine(cake_canvas_engine_new());assert!(!engine.0.is_null());
        let point=|index:usize| CakeCanvasPointerSample {x:100.0+index as f64*6.0,y:100.0,pressure:0.2+index as f64*0.01,tilt_x:0.0,tilt_y:0.0};
        assert_eq!(cake_canvas_begin_stroke(engine.0,point(0)),CakeCanvasStatus::Ok);
        for index in 1..35 { assert_eq!(cake_canvas_update_stroke(engine.0,point(index)),CakeCanvasStatus::Ok); }
        assert_eq!(cake_canvas_end_stroke(engine.0,point(35)),CakeCanvasStatus::Ok);
        let before=save(engine.0);
        let sample=CakeCanvasPointerSample {x:205.0,y:100.0,pressure:0.5,tilt_x:0.0,tilt_y:0.0};let samples=[sample,sample];
        let mut native=Buffer::new();let mut receipt=Buffer::new();
        assert_eq!(cake_canvas_split_erase_candidate(engine.0,samples.as_ptr(),samples.len(),1.0,&mut native.0,&mut receipt.0),CakeCanvasStatus::Ok);
        let receipt_json:serde_json::Value=serde_json::from_slice(&receipt.bytes()).unwrap();
        assert_eq!(receipt_json["schemaVersion"],1);
        assert_eq!(receipt_json["changes"].as_array().unwrap().len(),2);
        assert!(receipt_json["changes"].as_array().unwrap().iter().all(|change|!change["matchingSourceSegmentOffsets"].as_array().unwrap().is_empty()));
        let reopened=block_on(HeadlessCanvasEngine::from_rnote(native.bytes())).unwrap();assert_eq!(reopened.stroke_keys().len(),2);
        assert_eq!(before,save(engine.0));
        cake_canvas_buffer_release(&mut native.0);cake_canvas_buffer_release(&mut receipt.0);
        for width in [f64::NAN,0.0,501.0] {
            native.0.len=99;receipt.0.len=99;
            assert_eq!(cake_canvas_split_erase_candidate(engine.0,samples.as_ptr(),2,width,&mut native.0,&mut receipt.0),CakeCanvasStatus::InvalidArgument);
            assert_eq!(native.0.len,0);assert_eq!(receipt.0.len,0);assert!(native.0.data.is_null()&&receipt.0.data.is_null());
        }
        assert_eq!(cake_canvas_split_erase_candidate(std::ptr::null(),samples.as_ptr(),2,1.0,&mut native.0,&mut receipt.0),CakeCanvasStatus::InvalidHandle);
        receipt.0.len=99;
        assert_eq!(cake_canvas_split_erase_candidate(engine.0,samples.as_ptr(),2,1.0,std::ptr::null_mut(),&mut receipt.0),CakeCanvasStatus::InvalidArgument);assert_eq!(receipt.0.len,0);
        native.0.len=99;
        assert_eq!(cake_canvas_split_erase_candidate(engine.0,samples.as_ptr(),2,1.0,&mut native.0,std::ptr::null_mut()),CakeCanvasStatus::InvalidArgument);assert_eq!(native.0.len,0);
        native.0.len=99;let alias=&mut native.0 as *mut CakeCanvasBuffer;
        assert_eq!(cake_canvas_split_erase_candidate(engine.0,samples.as_ptr(),2,1.0,alias,alias),CakeCanvasStatus::InvalidArgument);assert_eq!(native.0.len,0);
        assert_eq!(cake_canvas_split_erase_candidate(engine.0,std::ptr::null(),2,1.0,&mut native.0,&mut receipt.0),CakeCanvasStatus::InvalidArgument);
        assert_eq!(cake_canvas_split_erase_candidate(engine.0,samples.as_ptr(),1,1.0,&mut native.0,&mut receipt.0),CakeCanvasStatus::InvalidArgument);
        assert_eq!(before,save(engine.0));
    }
}
