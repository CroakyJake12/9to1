//! Approved migration proof of concept for a renderer-neutral Rnote core.
//!
//! This crate deliberately depends on `rnote-engine` with default features disabled.
//! It must not enable Rnote's `ui` feature or depend on GTK/Libadwaita.

pub mod ffi;

use std::collections::HashSet;
use std::fs::{self, File};
use std::path::Path;
use std::time::{Instant, SystemTime, UNIX_EPOCH};

use anyhow::{Context, Result};
use nalgebra::Vector2;
use rnote_compose::penevent::PenEvent;
use rnote_compose::penpath::Element;
use rnote_compose::style::smooth::SmoothOptions;
use rnote_compose::utils::{add_xml_header, wrap_svg_root};
use rnote_compose::builders::ShapeBuilderType;
use rnote_compose::Color;
use rnote_engine::engine::export::{DocExportFormat, DocExportPrefs};
use rnote_engine::engine::{EngineConfig, EngineConfigShared};
use rnote_engine::engine::EngineSnapshot;
use rnote_engine::pens::pensconfig::brushconfig::BrushStyle;
use rnote_engine::pens::pensconfig::eraserconfig::{EraserConfig, EraserStyle};
use rnote_engine::pens::{PenMode, PenStyle};
use rnote_engine::Engine;

/// One normalized CakeOS/HUI pointer sample in Canvas document coordinates.
///
/// Tilt remains in the CakeOS boundary because Rnote 0.14.2 only accepts position
/// and pressure in its core `Element` type. Keeping tilt here prevents a lossy
/// public API if the engine is extended later.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct CanvasPointerSample {
    pub x: f64,
    pub y: f64,
    pub pressure: f64,
    pub tilt_x: f64,
    pub tilt_y: f64,
}

impl CanvasPointerSample {
    pub fn new(x: f64, y: f64, pressure: f64) -> Self {
        Self {
            x,
            y,
            pressure: pressure.clamp(0.0, 1.0),
            tilt_x: 0.0,
            tilt_y: 0.0,
        }
    }

    fn rnote_element(self) -> Element {
        Element::new(Vector2::new(self.x, self.y), self.pressure)
    }
}

/// Rnote tool styles exposed at the CakeOS boundary.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum CanvasTool {
    #[default]
    Pen,
    Highlighter,
    Eraser,
    Selector,
    Shape,
}

impl CanvasTool {
    fn rnote_pen_style(self) -> PenStyle {
        match self {
            Self::Pen | Self::Highlighter => PenStyle::Brush,
            Self::Eraser => PenStyle::Eraser,
            Self::Selector => PenStyle::Selector,
            Self::Shape => PenStyle::Shaper,
        }
    }
}

/// Rnote's concrete shape builders. The shape pen is not emulated by CakeOS.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum CanvasShape {
    #[default]
    Rectangle,
    Ellipse,
    Line,
    Arrow,
}

impl CanvasShape {
    fn rnote_builder(self) -> ShapeBuilderType {
        match self {
            Self::Rectangle => ShapeBuilderType::Rectangle,
            Self::Ellipse => ShapeBuilderType::Ellipse,
            Self::Line => ShapeBuilderType::Line,
            Self::Arrow => ShapeBuilderType::Arrow,
        }
    }
}

/// Per-tool stroke appearance owned by CakeOS and applied to Rnote's live pen
/// configuration. Only tools with a real engine style slot are configurable;
/// anything else is rejected at the boundary rather than silently ignored.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct CanvasPenStyle {
    /// sRGB + alpha, each channel in 0.0..=1.0.
    pub color: [f64; 4],
    /// Stroke width in Rnote document units, 0.5..=200.0.
    pub width: f64,
}

impl CanvasPenStyle {
    pub const MIN_WIDTH: f64 = 0.5;
    pub const MAX_WIDTH: f64 = 200.0;

    fn from_smooth(options: &SmoothOptions) -> Self {
        let color = options.stroke_color.unwrap_or(Color::BLACK);
        Self {
            color: [color.r, color.g, color.b, color.a],
            width: options.stroke_width,
        }
    }

    pub(crate) fn validate(self) -> Result<()> {
        for channel in self.color {
            if !channel.is_finite() || !(0.0..=1.0).contains(&channel) {
                anyhow::bail!("Canvas pen color channels must be finite and within 0.0..=1.0");
            }
        }
        if !self.width.is_finite() || !(Self::MIN_WIDTH..=Self::MAX_WIDTH).contains(&self.width) {
            anyhow::bail!("Canvas pen width must be finite and within 0.5..=200.0");
        }
        Ok(())
    }

    fn apply_to(&self, options: &mut SmoothOptions) {
        options.stroke_color = Some(Color {
            r: self.color[0],
            g: self.color[1],
            b: self.color[2],
            a: self.color[3],
        });
        options.stroke_width = self.width;
        options.update_piet_stroke_style();
    }
}

/// Camera state expressed in Canvas document coordinates.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct CanvasViewport {
    pub center_x: f64,
    pub center_y: f64,
    pub zoom: f64,
}

/// Coordinate space used by all stable Canvas bridge geometry in this PoC.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CanvasCoordinateSpace {
    Document,
}

/// Renderer-neutral format exposed to HUI.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CanvasRenderFormat {
    Svg,
}

impl CanvasRenderFormat {
    pub const fn mime_type(self) -> &'static str {
        match self {
            Self::Svg => "image/svg+xml",
        }
    }
}

/// Document-space bounds represented by a render frame.
///
/// Rnote normalizes exported SVG coordinates to a zero-based viewBox. These
/// bounds retain the corresponding original Canvas document-space rectangle so
/// HUI can map input and viewport state to the normalized renderer payload.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct CanvasDocumentBounds {
    pub x: f64,
    pub y: f64,
    pub width: f64,
    pub height: f64,
}

/// Stable renderer-neutral payload for HUI composition.
///
/// HUI owns viewport pan/zoom and surface-to-document transforms. The bytes are
/// intentionally whole-document SVG for this first slice; dirty-region/tile
/// output is a later performance boundary.
#[derive(Debug, Clone, PartialEq)]
pub struct CanvasRenderFrame {
    pub format: CanvasRenderFormat,
    pub coordinate_space: CanvasCoordinateSpace,
    pub bounds: CanvasDocumentBounds,
    pub bytes: Vec<u8>,
}

impl CanvasRenderFrame {
    pub const fn mime_type(&self) -> &'static str {
        self.format.mime_type()
    }
}

/// Style slots mirrored from Rnote's live pen configuration.
struct SeededStyles {
    pen: CanvasPenStyle,
    marker: CanvasPenStyle,
    shaper: CanvasPenStyle,
    eraser_width: f64,
    eraser_split_colliding: bool,
}

fn seed_styles(config: &EngineConfigShared) -> SeededStyles {
    let config = config.read();
    SeededStyles {
        pen: CanvasPenStyle::from_smooth(&config.pens_config.brush_config.solid_options),
        marker: CanvasPenStyle::from_smooth(&config.pens_config.brush_config.marker_options),
        shaper: CanvasPenStyle::from_smooth(&config.pens_config.shaper_config.smooth_options),
        eraser_width: config.pens_config.eraser_config.width,
        eraser_split_colliding: matches!(
            config.pens_config.eraser_config.style,
            EraserStyle::SplitCollidingStrokes
        ),
    }
}
/// Smallest engine boundary intended to prove that Rnote can run beneath HUI
/// without embedding `rnote-ui`.
#[derive(Debug)]
pub struct HeadlessCanvasEngine {
    engine: Engine,
    stroke_active: bool,
    tool: CanvasTool,
    shape: CanvasShape,
    pen_style: CanvasPenStyle,
    marker_style: CanvasPenStyle,
    shaper_style: CanvasPenStyle,
    eraser_width: f64,
    eraser_split_colliding: bool,
    config: EngineConfigShared,
}

impl Default for HeadlessCanvasEngine {
    fn default() -> Self {
        Self::new()
    }
}

impl HeadlessCanvasEngine {
    pub fn new() -> Self {
        let mut engine = Engine::default();
        let config = EngineConfigShared::from(EngineConfig::default());
        // Keep Rnote's configuration private to the engine while retaining the
        // shared handle needed to select its real marker and shape builders.
        let _ = engine.install_config(&config, None);
        // Seed CakeOS style slots from the live engine configuration so the
        // defaults render exactly as before any explicit style change.
        let seeded = seed_styles(&config);
        let mut canvas = Self {
            engine,
            stroke_active: false,
            tool: CanvasTool::Pen,
            shape: CanvasShape::Rectangle,
            pen_style: seeded.pen,
            marker_style: seeded.marker,
            shaper_style: seeded.shaper,
            eraser_width: seeded.eraser_width,
            eraser_split_colliding: seeded.eraser_split_colliding,
            config,
        };
        canvas.configure_tool();
        canvas
    }

    fn send_pen_event(&mut self, event: PenEvent) {
        let _ = self.engine.handle_pen_event(event, Some(PenMode::Pen), Instant::now());
    }

    pub fn tool(&self) -> CanvasTool {
        self.tool
    }

    /// Change the tool used by subsequent stroke events. Tool changes are kept
    /// outside an active stroke so one gesture cannot change interpretation midway.
    pub fn set_tool(&mut self, tool: CanvasTool) -> Result<()> {
        if self.stroke_active {
            anyhow::bail!("cannot change Canvas stroke tool while a stroke is active");
        }
        self.tool = tool;
        self.configure_tool();
        Ok(())
    }

    /// Select the builder used by Rnote's shape pen.
    pub fn set_shape(&mut self, shape: CanvasShape) -> Result<()> {
        if self.stroke_active {
            anyhow::bail!("cannot change Canvas shape while a stroke is active");
        }
        self.shape = shape;
        if self.tool == CanvasTool::Shape {
            self.configure_tool();
        }
        Ok(())
    }

    fn configure_tool(&mut self) {
        {
            let mut config = self.config.write();
            config.pens_config.brush_config.style = if self.tool == CanvasTool::Highlighter {
                BrushStyle::Marker
            } else {
                BrushStyle::Solid
            };
            config.pens_config.shaper_config.builder_type = self.shape.rnote_builder();
            self.pen_style
                .apply_to(&mut config.pens_config.brush_config.solid_options);
            self.marker_style
                .apply_to(&mut config.pens_config.brush_config.marker_options);
            self.shaper_style
                .apply_to(&mut config.pens_config.shaper_config.smooth_options);
            config.pens_config.eraser_config.width = self.eraser_width;
            config.pens_config.eraser_config.style = if self.eraser_split_colliding {
                EraserStyle::SplitCollidingStrokes
            } else {
                EraserStyle::TrashCollidingStrokes
            };
        }
        let _ = self.engine.change_pen_mode(PenMode::Pen);
        let _ = self.engine.change_pen_style(self.tool.rnote_pen_style());
    }

    /// Current style slot for a configurable tool (pen, highlighter, shape).
    pub fn pen_style(&self, tool: CanvasTool) -> Option<CanvasPenStyle> {
        match tool {
            CanvasTool::Pen => Some(self.pen_style),
            CanvasTool::Highlighter => Some(self.marker_style),
            CanvasTool::Shape => Some(self.shaper_style),
            _ => None,
        }
    }

    /// Change the stroke color/width used by subsequent strokes of a
    /// configurable tool. Rejected for tools without an engine style slot,
    /// while a stroke is active, or for out-of-range values.
    pub fn set_pen_style(&mut self, tool: CanvasTool, style: CanvasPenStyle) -> Result<()> {
        if !matches!(
            tool,
            CanvasTool::Pen | CanvasTool::Highlighter | CanvasTool::Shape
        ) {
            anyhow::bail!("Canvas tool has no configurable stroke style");
        }
        if self.stroke_active {
            anyhow::bail!("cannot change Canvas pen style while a stroke is active");
        }
        style.validate()?;
        match tool {
            CanvasTool::Pen => self.pen_style = style,
            CanvasTool::Highlighter => self.marker_style = style,
            CanvasTool::Shape => self.shaper_style = style,
            _ => unreachable!("tool checked above"),
        }
        self.configure_tool();
        Ok(())
    }

    /// Current eraser width and split-colliding behaviour.
    pub fn eraser(&self) -> (f64, bool) {
        (self.eraser_width, self.eraser_split_colliding)
    }

    /// Change eraser width (1.0..=500.0, matching Rnote limits) and whether
    /// colliding strokes are split instead of trashed. Applies to subsequent
    /// eraser strokes; rejected while a stroke is active.
    pub fn set_eraser(&mut self, width: f64, split_colliding: bool) -> Result<()> {
        if self.stroke_active {
            anyhow::bail!("cannot change Canvas eraser while a stroke is active");
        }
        if !width.is_finite()
            || !(EraserConfig::WIDTH_MIN..=EraserConfig::WIDTH_MAX).contains(&width)
        {
            anyhow::bail!("Canvas eraser width must be finite and within 1.0..=500.0");
        }
        self.eraser_width = width;
        self.eraser_split_colliding = split_colliding;
        self.configure_tool();
        Ok(())
    }

    /// Begin a freehand stroke. Device arbitration (mouse/touch/stylus/palm
    /// rejection) intentionally stays above this adapter in HUI.
    pub fn begin_stroke(&mut self, sample: CanvasPointerSample) -> Result<()> {
        if self.stroke_active {
            anyhow::bail!("a Canvas stroke is already active");
        }

        self.send_pen_event(PenEvent::Down {
            element: sample.rnote_element(),
            modifier_keys: HashSet::new(),
        });
        self.stroke_active = true;
        Ok(())
    }

    /// Append a pressure-sensitive sample to the active stroke.
    pub fn update_stroke(&mut self, sample: CanvasPointerSample) -> Result<()> {
        if !self.stroke_active {
            anyhow::bail!("cannot update a Canvas stroke before begin_stroke");
        }

        self.send_pen_event(PenEvent::Down {
            element: sample.rnote_element(),
            modifier_keys: HashSet::new(),
        });
        Ok(())
    }

    /// Finalize the active stroke at the supplied release sample.
    pub fn end_stroke(&mut self, sample: CanvasPointerSample) -> Result<()> {
        if !self.stroke_active {
            anyhow::bail!("cannot end a Canvas stroke before begin_stroke");
        }

        self.send_pen_event(PenEvent::Up {
            element: sample.rnote_element(),
            modifier_keys: HashSet::new(),
        });
        self.stroke_active = false;
        Ok(())
    }

    pub fn stroke_active(&self) -> bool {
        self.stroke_active
    }

    pub fn can_undo(&self) -> bool {
        self.engine.can_undo()
    }

    pub fn can_redo(&self) -> bool {
        self.engine.can_redo()
    }

    /// Undo one completed Canvas operation. History changes are rejected while a
    /// stroke is active so HUI cannot create an ambiguous partial-stroke state.
    pub fn undo(&mut self) -> Result<bool> {
        if self.stroke_active {
            anyhow::bail!("cannot undo while a Canvas stroke is active");
        }
        if !self.engine.can_undo() {
            return Ok(false);
        }

        let _ = self.engine.undo(Instant::now());
        Ok(true)
    }

    /// Redo one completed Canvas operation. History changes are rejected while a
    /// stroke is active for the same lifecycle reason as `undo`.
    pub fn redo(&mut self) -> Result<bool> {
        if self.stroke_active {
            anyhow::bail!("cannot redo while a Canvas stroke is active");
        }
        if !self.engine.can_redo() {
            return Ok(false);
        }

        let _ = self.engine.redo(Instant::now());
        Ok(true)
    }

    /// Convenience helper for tests/importers that already have a complete stroke.
    pub fn draw_stroke(&mut self, samples: &[CanvasPointerSample]) -> Result<()> {
        if samples.len() < 2 {
            anyhow::bail!("a stroke requires at least two pointer samples");
        }

        self.begin_stroke(samples[0])?;
        for sample in &samples[1..samples.len() - 1] {
            self.update_stroke(*sample)?;
        }
        self.end_stroke(*samples.last().expect("length checked above"))
    }

    /// Current storage/document extent. This is intentionally distinct from a
    /// render frame's content/page bounds in Rnote Infinite layout.
    pub fn document_bounds(&self) -> CanvasDocumentBounds {
        CanvasDocumentBounds {
            x: self.engine.document.x,
            y: self.engine.document.y,
            width: self.engine.document.width,
            height: self.engine.document.height,
        }
    }

    /// Change the Rnote camera's viewport size without affecting document coordinates.
    pub fn set_viewport_size(&mut self, width: f64, height: f64) -> Result<()> {
        if !width.is_finite() || !height.is_finite() || width <= 0.0 || height <= 0.0 {
            anyhow::bail!("Canvas viewport size must be finite and positive");
        }
        let _ = self.engine.camera.set_size(Vector2::new(width, height));
        Ok(())
    }

    /// Set the real Rnote camera zoom. Rnote clamps supported zoom bounds itself.
    pub fn zoom_to(&mut self, zoom: f64) -> Result<()> {
        if !zoom.is_finite() || zoom <= 0.0 {
            anyhow::bail!("Canvas zoom must be finite and positive");
        }
        let _ = self.engine.camera.zoom_to(zoom);
        Ok(())
    }

    /// Pan the real Rnote camera in Canvas document coordinates.
    pub fn pan_by(&mut self, delta_x: f64, delta_y: f64) -> Result<()> {
        if !delta_x.is_finite() || !delta_y.is_finite() {
            anyhow::bail!("Canvas pan deltas must be finite");
        }
        let center = self.engine.camera.viewport_center();
        let _ = self.engine.camera.set_viewport_center(center + Vector2::new(delta_x, delta_y));
        Ok(())
    }

    pub fn set_viewport_center(&mut self, center_x: f64, center_y: f64) -> Result<()> {
        if !center_x.is_finite() || !center_y.is_finite() {
            anyhow::bail!("Canvas viewport center must be finite");
        }
        let _ = self.engine.camera.set_viewport_center(Vector2::new(center_x, center_y));
        Ok(())
    }

    pub fn viewport(&self) -> CanvasViewport {
        let center = self.engine.camera.viewport_center();
        CanvasViewport {
            center_x: center.x,
            center_y: center.y,
            zoom: self.engine.camera.zoom(),
        }
    }

    /// Produce renderer-neutral SVG bytes using the engine export path.
    /// This is the first HUI-consumable rendering proof; viewport tile exposure
    /// remains a later optimization rather than importing GTK/GSK into HUI.
    pub async fn export_svg(&self) -> Result<Vec<u8>> {
        let prefs = DocExportPrefs {
            export_format: DocExportFormat::Svg,
            ..DocExportPrefs::default()
        };

        self.engine
            .export_doc("CakeOS Canvas PoC".to_string(), Some(prefs))
            .await
            .context("Rnote SVG export channel closed")?
            .context("Rnote SVG export failed")
    }

    /// Produce the stable renderer-neutral frame consumed by HUI.
    ///
    /// Rnote's document exporter first selects the page range containing content,
    /// then `StrokeContent::gen_svg` normalizes that rectangle to a zero-based SVG
    /// viewBox. The frame therefore carries the original document-space content
    /// rectangle while the SVG bytes remain normalized. HUI subtracts the frame
    /// origin when selecting a source rectangle and keeps its viewport in document
    /// coordinates.
    pub async fn render_frame(&self) -> Result<CanvasRenderFrame> {
        let prefs = DocExportPrefs {
            export_format: DocExportFormat::Svg,
            ..DocExportPrefs::default()
        };
        let content = self.engine.extract_document_content();
        let source_bounds = content
            .bounds()
            .context("Rnote document content has no renderable bounds")?;
        let generated = content
            .gen_svg(
                prefs.with_background,
                prefs.with_pattern,
                prefs.optimize_printing,
                0.0,
            )?
            .context("Rnote document SVG generation returned no content")?;
        let bytes = add_xml_header(
            wrap_svg_root(
                generated.svg_data.as_str(),
                Some(generated.bounds),
                Some(generated.bounds),
                false,
            )
            .as_str(),
        )
        .into_bytes();
        let bounds = CanvasDocumentBounds {
            x: source_bounds.mins[0],
            y: source_bounds.mins[1],
            width: source_bounds.maxs[0] - source_bounds.mins[0],
            height: source_bounds.maxs[1] - source_bounds.mins[1],
        };

        Ok(CanvasRenderFrame {
            format: CanvasRenderFormat::Svg,
            coordinate_space: CanvasCoordinateSpace::Document,
            bounds,
            bytes,
        })
    }

    /// Save a native Rnote payload for compatibility/interchange testing.
    pub async fn save_rnote(&self) -> Result<Vec<u8>> {
        self.engine
            .save_as_rnote_bytes("canvas-poc.rnote".to_string())
            .await
            .context("Rnote save channel closed")?
            .context("Rnote save failed")
    }

    /// Commit a complete Rnote document by replacing a same-directory temporary
    /// file. A failed write leaves the previous document available for reopen.
    pub async fn save_rnote_atomically(&self, path: impl AsRef<Path>) -> Result<()> {
        let target = path.as_ref();
        let parent = target
            .parent()
            .filter(|path| !path.as_os_str().is_empty())
            .context("Canvas Rnote path must have a parent directory")?;
        let name = target
            .file_name()
            .context("Canvas Rnote path must name a file")?
            .to_string_lossy();
        fs::create_dir_all(parent).context("create Canvas Rnote parent directory")?;
        let temporary = parent.join(format!(".{name}.{}.tmp", std::process::id()));
        let bytes = self.save_rnote().await?;

        let write_result = (|| -> Result<()> {
            let mut file = File::create(&temporary).context("create temporary Canvas Rnote file")?;
            use std::io::Write;
            file.write_all(&bytes).context("write temporary Canvas Rnote file")?;
            file.sync_all().context("sync temporary Canvas Rnote file")?;
            fs::rename(&temporary, target).context("replace Canvas Rnote file")?;
            Ok(())
        })();
        if write_result.is_err() {
            let _ = fs::remove_file(&temporary);
        }
        write_result
    }

    /// Restore a fresh engine from native Rnote bytes.
    pub async fn from_rnote(bytes: Vec<u8>) -> Result<Self> {
        let snapshot = EngineSnapshot::load_from_rnote_bytes(bytes)
            .await
            .context("Rnote snapshot load failed")?;
        let mut engine = Engine::default();
        let config = EngineConfigShared::from(EngineConfig::default());
        let _ = engine.install_config(&config, None);
        let _ = engine.load_snapshot(snapshot);
        let seeded = seed_styles(&config);
        let mut canvas = Self {
            engine,
            stroke_active: false,
            tool: CanvasTool::Pen,
            shape: CanvasShape::Rectangle,
            pen_style: seeded.pen,
            marker_style: seeded.marker,
            shaper_style: seeded.shaper,
            eraser_width: seeded.eraser_width,
            eraser_split_colliding: seeded.eraser_split_colliding,
            config,
        };
        canvas.configure_tool();
        Ok(canvas)
    }

    pub async fn from_rnote_file(path: impl AsRef<Path>) -> Result<Self> {
        let bytes = fs::read(path.as_ref()).context("read Canvas Rnote file")?;
        Self::from_rnote(bytes).await
    }

    /// Expose a debug-only JSON snapshot for mechanical assertions in the PoC.
    pub fn debug_state_json(&self) -> Result<String> {
        self.engine
            .export_state_as_json()
            .context("Rnote state serialization failed")
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use futures::executor::block_on;

    fn sample_stroke() -> [CanvasPointerSample; 5] {
        [
            CanvasPointerSample::new(120.0, 120.0, 0.15),
            CanvasPointerSample::new(150.0, 140.0, 0.35),
            CanvasPointerSample::new(190.0, 160.0, 0.65),
            CanvasPointerSample::new(235.0, 190.0, 0.90),
            CanvasPointerSample::new(280.0, 220.0, 0.55),
        ]
    }

    fn trashed_stroke_count(canvas: &HeadlessCanvasEngine) -> usize {
        canvas
            .debug_state_json()
            .expect("debug engine state")
            .matches("\"trashed\": true")
            .count()
    }

    #[test]
    fn pressure_is_clamped_but_tilt_is_preserved_at_hui_boundary() {
        let mut sample = CanvasPointerSample::new(1.0, 2.0, 2.5);
        sample.tilt_x = 37.0;
        sample.tilt_y = -22.0;
        assert_eq!(sample.pressure, 1.0);
        assert_eq!(sample.tilt_x, 37.0);
        assert_eq!(sample.tilt_y, -22.0);
    }

    #[test]
    fn incremental_stroke_boundary_enforces_event_lifecycle() {
        let samples = sample_stroke();
        let mut canvas = HeadlessCanvasEngine::new();

        assert!(canvas.update_stroke(samples[1]).is_err());
        assert!(canvas.end_stroke(samples[1]).is_err());

        canvas.begin_stroke(samples[0]).unwrap();
        assert!(canvas.stroke_active());
        assert!(canvas.begin_stroke(samples[1]).is_err());
        assert!(canvas.set_tool(CanvasTool::Eraser).is_err());
        assert!(canvas.undo().is_err());
        assert!(canvas.redo().is_err());

        canvas.update_stroke(samples[1]).unwrap();
        canvas.update_stroke(samples[2]).unwrap();
        canvas.end_stroke(samples[3]).unwrap();
        assert!(!canvas.stroke_active());
    }

    #[test]
    fn completed_stroke_participates_in_undo_redo_history() {
        let mut canvas = HeadlessCanvasEngine::new();
        assert!(!canvas.can_undo());
        assert!(!canvas.undo().unwrap());

        canvas.draw_stroke(&sample_stroke()).unwrap();
        assert!(canvas.can_undo());

        assert!(canvas.undo().unwrap());
        assert!(canvas.can_redo());

        assert!(canvas.redo().unwrap());
        assert!(!canvas.can_redo());
        assert!(canvas.can_undo());
    }

    #[test]
    fn renderer_neutral_frame_carries_export_coordinate_metadata() {
        block_on(async {
            let mut canvas = HeadlessCanvasEngine::new();
            canvas.draw_stroke(&sample_stroke()).unwrap();
            let storage_bounds = canvas.document_bounds();

            let frame = canvas.render_frame().await.unwrap();
            assert_eq!(frame.format, CanvasRenderFormat::Svg);
            assert_eq!(frame.coordinate_space, CanvasCoordinateSpace::Document);
            assert_eq!(frame.mime_type(), "image/svg+xml");
            assert!(frame.bounds.width > 0.0);
            assert!(frame.bounds.height > 0.0);
            assert!(frame.bounds.x <= 120.0 && frame.bounds.x + frame.bounds.width >= 280.0);
            assert!(frame.bounds.y <= 120.0 && frame.bounds.y + frame.bounds.height >= 220.0);
            assert!(frame.bounds.width < storage_bounds.width);
            assert!(frame.bounds.height < storage_bounds.height);
            let svg = std::str::from_utf8(&frame.bytes).unwrap();
            assert!(svg.contains("<svg"));
            assert!(svg.contains("viewBox=\"0.000 0.000"));
            assert!(frame.bytes.len() > 200, "render frame unexpectedly empty");
        });
    }

    #[test]
    fn eraser_trashes_stroke_and_history_restores_state() {
        let samples = sample_stroke();
        let mut canvas = HeadlessCanvasEngine::new();
        canvas.draw_stroke(&samples).unwrap();
        assert_eq!(trashed_stroke_count(&canvas), 0);

        canvas.set_tool(CanvasTool::Eraser).unwrap();
        canvas.begin_stroke(samples[2]).unwrap();
        canvas.end_stroke(samples[2]).unwrap();
        assert_eq!(trashed_stroke_count(&canvas), 1, "eraser did not trash the colliding stroke");

        assert!(canvas.undo().unwrap());
        assert!(canvas.can_redo());
        assert_eq!(trashed_stroke_count(&canvas), 0, "undo did not restore the erased stroke");

        assert!(canvas.redo().unwrap());
        assert_eq!(trashed_stroke_count(&canvas), 1, "redo did not reapply the eraser state");
    }

    #[test]
    fn headless_engine_draws_exports_saves_and_reloads() {
        block_on(async {
            let mut canvas = HeadlessCanvasEngine::new();
            canvas.draw_stroke(&sample_stroke()).unwrap();

            let svg_before = canvas.export_svg().await.unwrap();
            let svg_text = std::str::from_utf8(&svg_before).unwrap();
            assert!(svg_text.contains("<svg"));
            assert!(svg_before.len() > 200, "SVG unexpectedly empty");

            let native = canvas.save_rnote().await.unwrap();
            assert!(native.len() > 100, ".rnote payload unexpectedly empty");

            let restored = HeadlessCanvasEngine::from_rnote(native).await.unwrap();
            assert!(!restored.stroke_active());
            assert_eq!(restored.tool(), CanvasTool::Pen);
            let svg_after = restored.export_svg().await.unwrap();
            assert!(svg_after.len() > 200, "reloaded SVG unexpectedly empty");

            let state = restored.debug_state_json().unwrap();
            assert!(state.contains("stroke_components"));
        });
    }

    #[test]
    fn pen_styles_default_to_engine_values_and_round_trip() {
        let mut canvas = HeadlessCanvasEngine::new();

        // Seeded from the live engine: pen is black, marker is wide.
        let pen = canvas.pen_style(CanvasTool::Pen).unwrap();
        assert_eq!(pen.color, [0.0, 0.0, 0.0, 1.0]);
        assert!(pen.width > 0.0);
        let marker = canvas.pen_style(CanvasTool::Highlighter).unwrap();
        assert!(marker.width >= pen.width);
        assert!(canvas.pen_style(CanvasTool::Eraser).is_none());
        assert!(canvas.pen_style(CanvasTool::Selector).is_none());

        let red = CanvasPenStyle {
            color: [1.0, 0.0, 0.0, 1.0],
            width: 5.0,
        };
        canvas.set_pen_style(CanvasTool::Pen, red).unwrap();
        assert_eq!(canvas.pen_style(CanvasTool::Pen).unwrap(), red);

        // Rejections leave the stored style untouched.
        assert!(canvas.set_pen_style(CanvasTool::Eraser, red).is_err());
        assert!(
            canvas
                .set_pen_style(CanvasTool::Pen, CanvasPenStyle {
                    color: [1.0, 0.0, 0.0, 1.0],
                    width: 0.0,
                })
                .is_err()
        );
        assert_eq!(canvas.pen_style(CanvasTool::Pen).unwrap(), red);

        let (width, split) = canvas.eraser();
        assert!(width >= 1.0);
        assert!(!split);
        canvas.set_eraser(30.0, true).unwrap();
        assert_eq!(canvas.eraser(), (30.0, true));
        assert!(canvas.set_eraser(0.0, false).is_err());
        assert_eq!(canvas.eraser(), (30.0, true));
    }

    #[test]
    fn rnote_tools_camera_and_atomic_reopen_preserve_world_coordinates() {
        block_on(async {
            let root = std::env::temp_dir().join(format!(
                "cakeos-rnote-atomic-{}-{}",
                std::process::id(),
                SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos()
            ));
            let path = root.join("canvas.rnote");
            let mut canvas = HeadlessCanvasEngine::new();
            canvas.set_viewport_size(1280.0, 720.0).unwrap();
            canvas.zoom_to(1.75).unwrap();
            canvas.pan_by(320.0, -180.0).unwrap();

            canvas.set_tool(CanvasTool::Pen).unwrap();
            canvas.draw_stroke(&sample_stroke()).unwrap();
            canvas.set_tool(CanvasTool::Highlighter).unwrap();
            canvas.draw_stroke(&[
                CanvasPointerSample::new(140.0, 170.0, 0.5),
                CanvasPointerSample::new(260.0, 170.0, 0.5),
            ]).unwrap();
            canvas.set_shape(CanvasShape::Ellipse).unwrap();
            canvas.set_tool(CanvasTool::Shape).unwrap();
            canvas.draw_stroke(&[
                CanvasPointerSample::new(300.0, 100.0, 0.5),
                CanvasPointerSample::new(380.0, 180.0, 0.5),
            ]).unwrap();
            canvas.set_tool(CanvasTool::Selector).unwrap();
            canvas.draw_stroke(&[
                CanvasPointerSample::new(100.0, 90.0, 0.5),
                CanvasPointerSample::new(410.0, 90.0, 0.5),
                CanvasPointerSample::new(410.0, 240.0, 0.5),
                CanvasPointerSample::new(100.0, 240.0, 0.5),
                CanvasPointerSample::new(100.0, 90.0, 0.5),
            ]).unwrap();

            let before = canvas.render_frame().await.unwrap();
            let viewport = canvas.viewport();
            canvas.save_rnote_atomically(&path).await.unwrap();
            assert!(path.exists());
            assert!(root.read_dir().unwrap().all(|entry| {
                !entry.unwrap().file_name().to_string_lossy().ends_with(".tmp")
            }));

            let restored = HeadlessCanvasEngine::from_rnote_file(&path).await.unwrap();
            let after = restored.render_frame().await.unwrap();
            assert_eq!(before.bounds, after.bounds);
            assert_eq!(restored.viewport(), viewport);
            let _ = fs::remove_dir_all(root);
        });
    }
}
