use std::ffi::c_void;
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::ptr;
use std::slice;
use std::io::Read;

use futures::executor::block_on;

use crate::{
    CanvasCoordinateSpace, CanvasPenStyle, CanvasPointerSample, CanvasRenderFormat, CanvasShape,
    CanvasTool, CanvasViewport, HeadlessCanvasEngine,
};

pub const CAKE_CANVAS_ABI_VERSION: u32 = 3;
const MAXIMUM_NATIVE_PAYLOAD_BYTES: usize = 256 * 1024 * 1024;
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

/// This is the genuine selected-native-entity export family only. It does not
/// advertise keyed mutation or split-erase support, whose exports remain absent.
#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_selection_api_version() -> u32 { 1 }

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_quick_erase_api_version() -> u32 { 1 }

fn export_keys(
    handle: *const c_void,
    out_buffer: *mut CakeCanvasBuffer,
    rendered: bool,
) -> CakeCanvasStatus {
    if out_buffer.is_null() { return CakeCanvasStatus::InvalidArgument; }
    unsafe { *out_buffer = CakeCanvasBuffer::default(); }
    let Some(result) = with_engine(handle, |engine| {
        if engine.stroke_active() { return Err(CakeCanvasStatus::InvalidState); }
        let keys = if rendered { engine.rendered_stroke_keys() } else { engine.stroke_keys() };
        keys.map_err(|_| CakeCanvasStatus::EngineError)
    }) else { return CakeCanvasStatus::InvalidHandle; };
    match result {
        Ok(keys) => {
            // Explicit little-endian packing matches the managed BinaryPrimitives contract.
            let bytes = keys.into_iter().flat_map(u64::to_le_bytes).collect();
            unsafe { *out_buffer = owned_buffer(bytes); }
            CakeCanvasStatus::Ok
        }
        Err(status) => status,
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_stroke_keys(
    handle: *const c_void, out_buffer: *mut CakeCanvasBuffer,
) -> CakeCanvasStatus {
    guard_status(|| export_keys(handle, out_buffer, false))
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_rendered_stroke_keys(
    handle: *const c_void, out_buffer: *mut CakeCanvasBuffer,
) -> CakeCanvasStatus {
    guard_status(|| export_keys(handle, out_buffer, true))
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_quick_erase_target(
    handle: *const c_void, x: f64, y: f64, out_key: *mut u64,
) -> CakeCanvasStatus {
    guard_status(|| {
        if out_key.is_null() { return CakeCanvasStatus::InvalidArgument; }
        unsafe { *out_key = 0; }
        if !x.is_finite() || !y.is_finite() { return CakeCanvasStatus::InvalidArgument; }
        let Some(result) = with_engine(handle, |engine| {
            if engine.stroke_active() { return Err(CakeCanvasStatus::InvalidState); }
            engine.quick_erase_target(x, y).map_err(|_| CakeCanvasStatus::EngineError)
        }) else { return CakeCanvasStatus::InvalidHandle; };
        match result {
            Ok(key) => {
                unsafe { *out_key = key.unwrap_or(0); }
                CakeCanvasStatus::Ok
            }
            Err(status) => status,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_export_selected_strokes(
    handle: *const c_void, keys: *const u64, count: usize, out_buffer: *mut CakeCanvasBuffer,
) -> CakeCanvasStatus {
    guard_status(|| {
        if out_buffer.is_null() { return CakeCanvasStatus::InvalidArgument; }
        unsafe { *out_buffer = CakeCanvasBuffer::default(); }
        if keys.is_null() || count == 0 || count > HeadlessCanvasEngine::MAXIMUM_STROKE_KEYS {
            return CakeCanvasStatus::InvalidArgument;
        }
        let owned = unsafe { slice::from_raw_parts(keys, count) }.to_vec();
        if owned.iter().any(|key| *key == 0)
            || owned.iter().copied().collect::<std::collections::HashSet<_>>().len() != count {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(result) = with_engine(handle, |engine| {
            if engine.stroke_active() { return Err(CakeCanvasStatus::InvalidState); }
            block_on(engine.export_selected_strokes(&owned)).map_err(|_| CakeCanvasStatus::EngineError)
        }) else { return CakeCanvasStatus::InvalidHandle; };
        match result {
            Ok(bytes) if bytes.len() <= MAXIMUM_NATIVE_PAYLOAD_BYTES => {
                unsafe { *out_buffer = owned_buffer(bytes); }
                CakeCanvasStatus::Ok
            }
            _ => CakeCanvasStatus::EngineError,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_engine_from_xopp(
    data: *const u8, len: usize, dpi: f64, out_handle: *mut *mut c_void,
) -> CakeCanvasStatus {
    guard_status(|| {
        if out_handle.is_null() { return CakeCanvasStatus::InvalidArgument; }
        unsafe { *out_handle = ptr::null_mut(); }
        if data.is_null() || len == 0 || len > MAXIMUM_NATIVE_PAYLOAD_BYTES
            || !dpi.is_finite() || !(1.0..=2400.0).contains(&dpi) {
            return CakeCanvasStatus::InvalidArgument;
        }
        let owned = unsafe { slice::from_raw_parts(data, len) }.to_vec();
        // Bound expansion before handing this immutable input to the donor's
        // actual Xopp parser. Do not admit a compressed allocation bomb.
        if !bounded_gzip(&owned, MAXIMUM_NATIVE_PAYLOAD_BYTES as u64) {
            return CakeCanvasStatus::InvalidArgument;
        }
        match block_on(HeadlessCanvasEngine::from_xopp(owned, dpi)) {
            Ok(engine) => {
                unsafe { *out_handle = Box::into_raw(Box::new(engine)).cast::<c_void>(); }
                CakeCanvasStatus::Ok
            }
            Err(_) => CakeCanvasStatus::EngineError,
        }
    })
}

fn bounded_gzip(bytes: &[u8], maximum_expanded: u64) -> bool {
    let Some(limit) = maximum_expanded.checked_add(1) else { return false; };
    let mut expanded = flate2::read::MultiGzDecoder::new(bytes).take(limit);
    matches!(std::io::copy(&mut expanded, &mut std::io::sink()), Ok(size) if size <= maximum_expanded)
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
    use std::io::Write;
    use slotmap::Key;

    fn sample(x: f64, y: f64, pressure: f64) -> CakeCanvasPointerSample {
        CakeCanvasPointerSample {
            x,
            y,
            pressure,
            tilt_x: 0.0,
            tilt_y: 0.0,
        }
    }

    fn decoded_keys(handle: *const c_void, rendered: bool) -> Vec<u64> {
        let mut buffer = CakeCanvasBuffer::default();
        let status = if rendered {
            cake_canvas_rendered_stroke_keys(handle, &mut buffer)
        } else { cake_canvas_stroke_keys(handle, &mut buffer) };
        assert_eq!(status, CakeCanvasStatus::Ok);
        let keys = if buffer.len == 0 { vec![] } else {
            assert_eq!(buffer.len % 8, 0);
            unsafe { slice::from_raw_parts(buffer.data, buffer.len) }.chunks_exact(8)
                .map(|bytes| u64::from_le_bytes(bytes.try_into().unwrap())).collect()
        };
        cake_canvas_buffer_release(&mut buffer);
        assert!(buffer.data.is_null());
        assert_eq!(buffer.len, 0);
        keys
    }

    fn gzip(bytes: &[u8]) -> Vec<u8> {
        let mut encoder = flate2::write::GzEncoder::new(Vec::new(), flate2::Compression::default());
        encoder.write_all(bytes).unwrap();
        encoder.finish().unwrap()
    }

    #[test]
    fn structured_selection_preserves_donor_keys_paths_and_original_history() {
        let handle = cake_canvas_engine_new();
        assert!(!handle.is_null());
        assert_eq!(cake_canvas_selection_api_version(), 1);
        assert_eq!(cake_canvas_quick_erase_api_version(), 1);
        assert!(decoded_keys(handle, false).is_empty());
        assert_eq!(cake_canvas_begin_stroke(handle, sample(100.0, 100.0, 0.2)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_update_stroke(handle, sample(180.0, 100.0, 0.7)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_end_stroke(handle, sample(250.0, 100.0, 0.9)), CakeCanvasStatus::Ok);
        let pen = decoded_keys(handle, false)[0];
        assert_eq!(cake_canvas_set_stroke_tool(handle, CAKE_CANVAS_TOOL_HIGHLIGHTER), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_begin_stroke(handle, sample(100.0, 100.0, 0.5)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_end_stroke(handle, sample(250.0, 100.0, 0.5)), CakeCanvasStatus::Ok);
        let keys = decoded_keys(handle, false);
        assert_eq!(keys.len(), 2);
        let marker = *keys.iter().find(|&&key| key != pen).unwrap();
        // Rnote renders highlighters below user ink even when inserted later.
        assert_eq!(decoded_keys(handle, true), vec![marker, pen]);
        let original_state = with_engine(handle, |engine| engine.debug_state_json().unwrap()).unwrap();
        let mut target = 99;
        assert_eq!(cake_canvas_quick_erase_target(handle, 180.0, 100.0, &mut target), CakeCanvasStatus::Ok);
        assert_eq!(target, pen);
        assert_eq!(cake_canvas_quick_erase_target(handle, -1000.0, -1000.0, &mut target), CakeCanvasStatus::Ok);
        assert_eq!(target, 0);

        let mut buffer = CakeCanvasBuffer::default();
        assert_eq!(cake_canvas_export_selected_strokes(handle, &pen, 1, &mut buffer), CakeCanvasStatus::Ok);
        let mut selected = ptr::null_mut();
        assert_eq!(cake_canvas_engine_from_rnote(buffer.data, buffer.len, &mut selected), CakeCanvasStatus::Ok);
        assert_eq!(decoded_keys(selected, false), vec![pen]);
        let original_stroke = with_engine(handle, |engine| engine.engine.take_snapshot().stroke_components.iter()
            .find(|(key, _)| key.data().as_ffi() == pen).unwrap().1.clone()).unwrap();
        let exported_stroke = with_engine(selected, |engine| engine.engine.take_snapshot().stroke_components.iter()
            .next().unwrap().1.clone()).unwrap();
        assert_eq!(serde_json::to_value(original_stroke).unwrap(), serde_json::to_value(exported_stroke).unwrap());
        assert_eq!(with_engine(handle, |engine| engine.debug_state_json().unwrap()).unwrap(), original_state);
        // Read/export must not reset the live donor's real undo/redo stack.
        assert_eq!(cake_canvas_undo(handle), CakeCanvasStatus::Ok);
        assert_eq!(decoded_keys(handle, false), vec![pen]);
        assert_eq!(cake_canvas_redo(handle), CakeCanvasStatus::Ok);
        assert_eq!(decoded_keys(handle, false), keys);
        let mut saved = CakeCanvasBuffer::default();
        assert_eq!(cake_canvas_save_rnote(handle, &mut saved), CakeCanvasStatus::Ok);
        let mut reopened = ptr::null_mut();
        assert_eq!(cake_canvas_engine_from_rnote(saved.data, saved.len, &mut reopened), CakeCanvasStatus::Ok);
        assert_eq!(decoded_keys(reopened, true), vec![marker, pen]);
        cake_canvas_buffer_release(&mut saved);
        cake_canvas_buffer_release(&mut buffer);
        cake_canvas_engine_free(reopened);
        cake_canvas_engine_free(selected);
        cake_canvas_engine_free(handle);
    }

    #[test]
    fn selection_and_target_refuse_invalid_or_uncommitted_input_without_output() {
        let handle = cake_canvas_engine_new();
        let mut buffer = CakeCanvasBuffer::default();
        assert_eq!(cake_canvas_stroke_keys(ptr::null(), &mut buffer), CakeCanvasStatus::InvalidHandle);
        assert!(buffer.data.is_null());
        assert_eq!(cake_canvas_stroke_keys(handle, ptr::null_mut()), CakeCanvasStatus::InvalidArgument);
        let invalid = [0, 0];
        assert_eq!(cake_canvas_export_selected_strokes(handle, invalid.as_ptr(), 0, &mut buffer), CakeCanvasStatus::InvalidArgument);
        assert_eq!(cake_canvas_export_selected_strokes(handle, invalid.as_ptr(), HeadlessCanvasEngine::MAXIMUM_STROKE_KEYS + 1, &mut buffer), CakeCanvasStatus::InvalidArgument);
        assert_eq!(cake_canvas_export_selected_strokes(handle, invalid.as_ptr(), 2, &mut buffer), CakeCanvasStatus::InvalidArgument);
        let unknown = [u64::MAX];
        assert_eq!(cake_canvas_export_selected_strokes(handle, unknown.as_ptr(), 1, &mut buffer), CakeCanvasStatus::EngineError);
        assert!(buffer.data.is_null());
        assert_eq!(buffer.len, 0);
        let mut key = 123;
        assert_eq!(cake_canvas_quick_erase_target(handle, f64::NAN, 0.0, &mut key), CakeCanvasStatus::InvalidArgument);
        assert_eq!(key, 0);
        assert_eq!(cake_canvas_begin_stroke(handle, sample(100.0, 100.0, 0.5)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_stroke_keys(handle, &mut buffer), CakeCanvasStatus::InvalidState);
        assert_eq!(cake_canvas_rendered_stroke_keys(handle, &mut buffer), CakeCanvasStatus::InvalidState);
        assert_eq!(cake_canvas_quick_erase_target(handle, 100.0, 100.0, &mut key), CakeCanvasStatus::InvalidState);
        assert_eq!(key, 0);
        assert_eq!(cake_canvas_export_selected_strokes(handle, unknown.as_ptr(), 1, &mut buffer), CakeCanvasStatus::InvalidState);
        assert_eq!(cake_canvas_end_stroke(handle, sample(160.0, 100.0, 0.5)), CakeCanvasStatus::Ok);
        let actual = decoded_keys(handle, false)[0];
        let duplicate = [actual, actual];
        assert_eq!(cake_canvas_export_selected_strokes(handle, duplicate.as_ptr(), 2, &mut buffer), CakeCanvasStatus::InvalidArgument);
        assert_eq!(decoded_keys(handle, false), vec![actual]);
        cake_canvas_engine_free(handle);
    }

    #[test]
    fn xopp_import_uses_real_donor_loader_and_retains_dpi_scaling() {
        let bytes = gzip(br##"<?xml version="1.0" standalone="no"?><xournal creator="Xournal++" fileversion="4"><title>Revision fixture</title><page width="595" height="842"><background type="solid" color="#ffffffff" style="plain"/><layer><stroke tool="pen" color="#000000ff" width="2.0">10 10 30 30 60 20</stroke></layer></page></xournal>"##);
        let mut original = ptr::null_mut();
        let mut scaled = ptr::null_mut();
        assert_eq!(cake_canvas_engine_from_xopp(bytes.as_ptr(), bytes.len(), 72.0, &mut original), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_engine_from_xopp(bytes.as_ptr(), bytes.len(), 144.0, &mut scaled), CakeCanvasStatus::Ok);
        assert_eq!(decoded_keys(original, false).len(), 1);
        assert_eq!(decoded_keys(scaled, false).len(), 1);
        let original_bounds = with_engine(original, HeadlessCanvasEngine::document_bounds).unwrap();
        let scaled_bounds = with_engine(scaled, HeadlessCanvasEngine::document_bounds).unwrap();
        assert!((scaled_bounds.width - 2.0 * original_bounds.width).abs() < 0.001);
        assert!((scaled_bounds.height - 2.0 * original_bounds.height).abs() < 0.001);
        let mut frame = CakeCanvasRenderFrame::default();
        assert_eq!(cake_canvas_render_frame(original, &mut frame), CakeCanvasStatus::Ok);
        assert!(frame.len > 200);
        cake_canvas_render_frame_release(&mut frame);
        let mut saved = CakeCanvasBuffer::default();
        assert_eq!(cake_canvas_save_rnote(original, &mut saved), CakeCanvasStatus::Ok);
        let mut reopened = ptr::null_mut();
        assert_eq!(cake_canvas_engine_from_rnote(saved.data, saved.len, &mut reopened), CakeCanvasStatus::Ok);
        assert_eq!(decoded_keys(reopened, false), decoded_keys(original, false));
        cake_canvas_buffer_release(&mut saved);
        cake_canvas_engine_free(reopened);
        cake_canvas_engine_free(scaled);
        cake_canvas_engine_free(original);
    }

    #[test]
    fn xopp_invalid_payloads_and_expansion_are_refused_before_native_publication() {
        let invalid = b"not gzip";
        let mut output = 1usize as *mut c_void;
        assert_eq!(cake_canvas_engine_from_xopp(invalid.as_ptr(), invalid.len(), 96.0, &mut output), CakeCanvasStatus::InvalidArgument);
        assert!(output.is_null());
        assert_eq!(cake_canvas_engine_from_xopp(invalid.as_ptr(), invalid.len(), 0.0, &mut output), CakeCanvasStatus::InvalidArgument);
        assert_eq!(cake_canvas_engine_from_xopp(invalid.as_ptr(), MAXIMUM_NATIVE_PAYLOAD_BYTES + 1, 96.0, &mut output), CakeCanvasStatus::InvalidArgument);
        assert!(output.is_null());
        let not_xopp = gzip(b"<xournal>");
        assert_eq!(cake_canvas_engine_from_xopp(not_xopp.as_ptr(), not_xopp.len(), 96.0, &mut output), CakeCanvasStatus::EngineError);
        assert!(output.is_null());
        let bounded = gzip(&[b'a'; 64]);
        assert!(bounded_gzip(&bounded, 64));
        assert!(!bounded_gzip(&bounded, 63));
        let mut concatenated = bounded.clone();
        concatenated.extend_from_slice(&gzip(b"b"));
        assert!(!bounded_gzip(&concatenated, 64));
        assert!(!bounded_gzip(&invalid[..], 64));
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
