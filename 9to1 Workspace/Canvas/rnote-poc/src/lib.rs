//! Approved migration proof of concept for a renderer-neutral Rnote core.
//!
//! This crate deliberately depends on `rnote-engine` with default features disabled.
//! It must not enable Rnote's `ui` feature or depend on GTK/Libadwaita.

pub mod ffi;

use std::collections::HashSet;
use std::fs::{self, File};
use std::path::Path;
use std::time::Instant;
use std::sync::Arc;
use slotmap::{Key, KeyData};
#[cfg(test)]
use std::time::{SystemTime, UNIX_EPOCH};

use anyhow::{Context, Result};
use parry2d_f64::math::Vector2;
use rnote_compose::penevent::PenEvent;
use rnote_compose::penpath::Element;
use rnote_compose::style::smooth::SmoothOptions;
use rnote_compose::utils::{add_xml_header, wrap_svg_root};
use rnote_compose::builders::ShapeBuilderType;
use rnote_compose::{Color, Transformable};
use rnote_compose::shapes::Shapeable;
use rnote_engine::engine::export::{DocExportFormat, DocExportPrefs};
use rnote_engine::engine::import::XoppImportPrefs;
use rnote_engine::engine::{EngineConfig, EngineConfigShared};
use rnote_engine::engine::EngineSnapshot;
use rnote_engine::pens::pensconfig::brushconfig::BrushStyle;
use rnote_engine::pens::pensconfig::eraserconfig::{EraserConfig, EraserStyle};
use rnote_engine::pens::{PenMode, PenStyle};
use rnote_engine::Engine;
use rnote_engine::strokes::Content;

/// One normalized CakeOS/HUI pointer sample in Canvas document coordinates.
///
/// Tilt remains in the Canvas boundary because Rnote 0.15 only accepts position
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

    /// Stable donor keys in the actual persisted snapshot. Trashed strokes are
    /// excluded by the donor's take_snapshot; keys survive save/reopen.
    pub fn stroke_keys(&self) -> Vec<u64> {
        let mut keys = self.engine.take_snapshot().stroke_components.keys()
            .map(|key| key.data().as_ffi()).collect::<Vec<_>>();
        keys.sort_unstable();
        keys
    }

    /// Run genuine SplitColliding on a detached donor, returning its exact
    /// native snapshot and finite, lossless fragment materialization receipt.
    /// Original owner state is never changed, including all refusal paths.
    pub async fn split_erase_candidate(&self, samples: &[CanvasPointerSample], width: f64) -> Result<(Vec<u8>, Vec<u8>)> {
        if self.stroke_active { anyhow::bail!("cannot split during an active stroke"); }
        if samples.len() < 2 || samples.len() > 1_000_000 || samples.iter().any(|s|
            !s.x.is_finite() || !s.y.is_finite() || !s.pressure.is_finite() || !(0.0..=1.0).contains(&s.pressure) ||
            !s.tilt_x.is_finite() || !s.tilt_y.is_finite()) {
            anyhow::bail!("split gesture samples exceed finite bounded contract");
        }
        if !width.is_finite() || !(EraserConfig::WIDTH_MIN..=EraserConfig::WIDTH_MAX).contains(&width) {
            anyhow::bail!("split eraser width must be within actual donor bounds");
        }
        let before = self.engine.take_snapshot();
        if before.stroke_components.len() > 16_384 { anyhow::bail!("split materialization exceeds retained entity budget"); }
        let before_order = self.rendered_stroke_keys()?;
        let mut candidate = Self::from_rnote(self.save_rnote().await?).await?;
        candidate.set_eraser(width, true)?;
        candidate.set_tool(CanvasTool::Eraser)?;
        candidate.begin_stroke(samples[0])?;
        for sample in &samples[1..samples.len()-1] { candidate.update_stroke(*sample)?; }
        candidate.end_stroke(samples[samples.len()-1])?;
        let after = candidate.engine.take_snapshot();
        if after.stroke_components.len() > 16_384 { anyhow::bail!("split generated too many entities"); }
        let after_order = candidate.rendered_stroke_keys()?;
        let removed = before.stroke_components.keys().filter(|key| !after.stroke_components.contains_key(*key)).collect::<Vec<_>>();
        let mut modified = Vec::new();
        for (key, stroke) in after.stroke_components.iter() {
            if let Some(old) = before.stroke_components.get(key) {
                // Any serialization failure refuses the detached candidate;
                // it must never silently classify changed donor content as unchanged.
                if serde_json::to_value(old)? != serde_json::to_value(stroke)? { modified.push(key); }
            }
        }
        let created = after.stroke_components.keys().filter(|key| !before.stroke_components.contains_key(*key)).collect::<Vec<_>>();
        if removed.len() + modified.len() + created.len() > 1024 { anyhow::bail!("split transaction exceeds 1024 affected entities"); }
        let mut parents = removed.clone(); parents.extend(modified.iter().copied());
        let mut receipt_changes = Vec::new();
        let mut comparison_budget = 4_000_000usize;
        for key in modified.iter().chain(created.iter()).copied() {
            let rnote_engine::strokes::Stroke::BrushStroke(fragment) = after.stroke_components[key].as_ref() else {
                anyhow::bail!("split materialization requires actual brushstroke path geometry");
            };
            let layer = after.chrono_components.get(key).context("fragment layer missing")?.layer;
            let candidates = if before.stroke_components.contains_key(key) { vec![key] } else { parents.clone() };
            let mut matches = Vec::new();
            for parent_key in candidates {
                let rnote_engine::strokes::Stroke::BrushStroke(parent) = before.stroke_components[parent_key].as_ref() else { continue; };
                if before.chrono_components.get(parent_key).context("source layer missing")?.layer != layer ||
                    serde_json::to_value(&parent.style)? != serde_json::to_value(&fragment.style)? { continue; }
                let offsets = exact_subpath_offsets(&parent.path, &fragment.path, &mut comparison_budget)?;
                if !offsets.is_empty() { matches.push((parent_key, offsets)); }
            }
            if matches.len() != 1 { anyhow::bail!("split fragment does not have one provable original native parent"); }
            let (source_key, offsets) = matches.pop().unwrap();
            receipt_changes.push(serde_json::json!({"nativeKey":key.data().as_ffi(), "sourceNativeKey":source_key.data().as_ffi(),
                "isNew":!before.stroke_components.contains_key(key), "matchingSourceSegmentOffsets":offsets,
                "path":&fragment.path, "style":&fragment.style, "layer":layer}));
        }
        let before_layers = before.stroke_components.keys().map(|key| {
            let layer = before.chrono_components.get(key).context("source chronology missing")?.layer;
            Ok(serde_json::json!({"nativeKey":key.data().as_ffi(), "layer":layer}))
        }).collect::<Result<Vec<_>>>()?;
        let receipt = serde_json::json!({"schemaVersion":1, "beforeRenderKeys":before_order, "beforeLayers":before_layers,
            "afterRenderKeys":after_order, "removedKeys":removed.iter().map(|key| key.data().as_ffi()).collect::<Vec<_>>(),
            "changes":receipt_changes, "geometry":"exact-retained-donor-penpath", "samplesRole":"original-input-provenance-not-fragment-polyline"});
        let receipt_bytes = serde_json::to_vec(&receipt)?;
        if receipt_bytes.len() > 64 * 1024 * 1024 { anyhow::bail!("split materialization receipt exceeds byte budget"); }
        Ok((candidate.save_rnote().await?, receipt_bytes))
    }

    /// Run Rnote's actual selector on a disposable donor candidate. Selection
    /// changes donor chronology; the candidate is NEVER adopted or persisted.
    /// Returned identities follow the original render order, not selected order.
    pub async fn preview_native_selection(&self, style: u32, samples: &[CanvasPointerSample]) -> Result<Vec<u64>> {
        use rnote_engine::pens::pensconfig::selectorconfig::SelectorStyle;
        if self.stroke_active { anyhow::bail!("selection is unavailable during an active stroke"); }
        let style = SelectorStyle::try_from(style)?;
        if samples.is_empty() || samples.len() > 8192 { anyhow::bail!("selection needs 1..=8192 points"); }
        if samples.iter().any(|p| !p.x.is_finite() || !p.y.is_finite() || !p.pressure.is_finite()
            || !(0.0..=1.0).contains(&p.pressure)) { anyhow::bail!("selection points must be finite and normalized"); }
        if matches!(style, SelectorStyle::Polygon | SelectorStyle::IntersectingPath) && samples.len() < 3 {
            anyhow::bail!("polygon/path selection requires at least three points");
        }
        let original_order = self.rendered_stroke_keys()?;
        let mut candidate = Self::from_rnote(self.save_rnote().await?).await?;
        candidate.set_tool(CanvasTool::Selector)?;
        candidate.config.write().pens_config.selector_config.style = style;
        candidate.begin_stroke(samples[0])?;
        for point in samples.iter().skip(1) { candidate.update_stroke(*point)?; }
        candidate.end_stroke(*samples.last().unwrap())?;
        let Some(content) = candidate.engine.extract_selection_content() else { return Ok(Vec::new()); };
        if content.strokes.len() > 1024 { anyhow::bail!("selection exceeds the canonical transaction entry budget"); }
        let snapshot = candidate.engine.take_snapshot();
        let mut identities = std::collections::HashMap::with_capacity(snapshot.stroke_components.len());
        for (key, stroke) in snapshot.stroke_components.iter() {
            if identities.insert(Arc::as_ptr(stroke) as usize, key.data().as_ffi()).is_some() {
                anyhow::bail!("donor entities share an ambiguous stroke allocation");
            }
        }
        let original_set = original_order.iter().copied().collect::<HashSet<_>>();
        let mut selected = HashSet::new();
        for stroke in content.strokes {
            let key = *identities.get(&(Arc::as_ptr(&stroke) as usize)).context("selected donor entity has no retained exact identity")?;
            if !original_set.contains(&key) || !selected.insert(key) { anyhow::bail!("selected donor identity is missing or duplicated"); }
        }
        Ok(original_order.into_iter().filter(|key| selected.contains(key)).collect())
    }

    /// Exact retained render chronology for canonical/native order validation.
    pub fn rendered_stroke_keys(&self) -> Result<Vec<u64>> {
        if self.stroke_active { anyhow::bail!("cannot resolve render order during an active stroke"); }
        let snapshot = self.engine.take_snapshot();
        if snapshot.stroke_components.len() > 1_000_000 { anyhow::bail!("render order exceeds entity budget"); }
        let mut ordered = Vec::with_capacity(snapshot.stroke_components.len());
        for key in snapshot.stroke_components.keys() {
            let chrono = snapshot.chrono_components.get(key).context("retained donor chronology is missing")?;
            ordered.push((key, chrono));
        }
        ordered.sort_unstable_by(|(_, a), (_, b)| a.layer.cmp(&b.layer).then_with(|| a.cmp(b)));
        if ordered.windows(2).any(|pair| pair[0].1 == pair[1].1) {
            anyhow::bail!("retained donor render order is ambiguous");
        }
        Ok(ordered.into_iter().map(|(key, _)| key.data().as_ffi()).collect())
    }

    /// Read-only Quick eraser target using the donor's own hitboxes and render
    /// chronology. Equal chronology on two hit entities is ambiguous in the
    /// donor's unstable sort, so it is refused rather than picking a slot key.
    pub fn quick_erase_target(&self, x: f64, y: f64) -> Result<Option<u64>> {
        if self.stroke_active { anyhow::bail!("cannot resolve Quick erase during an active stroke"); }
        if !x.is_finite() || !y.is_finite() { anyhow::bail!("Quick erase coordinates must be finite"); }
        let snapshot = self.engine.take_snapshot();
        if snapshot.stroke_components.len() > 1_000_000 { anyhow::bail!("Quick erase snapshot exceeds entity budget"); }
        let coord = Vector2::new(x, y);
        let mut hits = Vec::new();
        for (key, stroke) in snapshot.stroke_components.iter() {
            let chrono = snapshot.chrono_components.get(key)
                .context("Quick erase requires retained donor chronology for every entity")?;
            let hitboxes = stroke.hitboxes();
            if hitboxes.iter().any(|hitbox| !hitbox.mins.is_finite() || !hitbox.maxs.is_finite()) {
                anyhow::bail!("Quick erase donor hitbox is not finite");
            }
            if hitboxes.into_iter().any(|hitbox| hitbox.contains_local_point(coord)) {
                hits.push((key, chrono));
            }
        }
        // ChronoComponent's derived Ord places time before layer. The actual
        // donor render comparator explicitly places layer first; match that.
        hits.sort_unstable_by(|(_, a), (_, b)| a.layer.cmp(&b.layer).then_with(|| a.cmp(b)));
        let Some((key, top)) = hits.last() else { return Ok(None); };
        if hits.len() > 1 && hits[hits.len() - 2].1 == *top {
            anyhow::bail!("Quick erase render order is ambiguous");
        }
        Ok(Some(key.data().as_ffi()))
    }

    /// Export exactly the selected native entities without exporting unrelated
    /// Canvas content or source document/camera settings. The donor's original
    /// strokes, paths, styles and chronology remain structured and editable.
    pub async fn selected_strokes_rnote(&self, keys: &[u64]) -> Result<Vec<u8>> {
        if self.stroke_active { anyhow::bail!("cannot export a selection during an active stroke"); }
        if keys.is_empty() || keys.len() > 1_000_000 { anyhow::bail!("native selection must contain 1..=1000000 keys"); }
        let selected = keys.iter().map(|value| rnote_engine::store::StrokeKey::from(KeyData::from_ffi(*value)))
            .collect::<HashSet<_>>();
        if selected.len() != keys.len() { anyhow::bail!("native selection keys must be unique"); }
        let mut snapshot = self.engine.take_snapshot();
        if selected.iter().any(|key| !snapshot.stroke_components.contains_key(*key)) {
            anyhow::bail!("native selection key is not present in the current persisted snapshot");
        }
        Arc::make_mut(&mut snapshot.stroke_components).retain(|key, _| selected.contains(&key));
        Arc::make_mut(&mut snapshot.chrono_components).retain(|key, _| selected.contains(&key));
        snapshot.document = Default::default();
        snapshot.camera = Default::default();
        Self::from_snapshot(snapshot).save_rnote().await
    }

    /// Render an exact visible-key projection using maintained native snapshots.
    /// Keep the original document/camera/background and durable entities intact;
    /// hidden layers never become donor trash or disappear from saved history.
    pub async fn render_visible_keys(&self, keys: &[u64]) -> Result<CanvasRenderFrame> {
        if self.stroke_active || keys.len() > 1_000_000 { anyhow::bail!("invalid visibility projection boundary"); }
        let selected = keys.iter().map(|value| rnote_engine::store::StrokeKey::from(KeyData::from_ffi(*value))).collect::<HashSet<_>>();
        if selected.len() != keys.len() { anyhow::bail!("duplicate visible entity"); }
        let mut snapshot = self.engine.take_snapshot();
        if selected.iter().any(|key| !snapshot.stroke_components.contains_key(*key) || !snapshot.chrono_components.contains_key(*key)) { anyhow::bail!("missing visible entity or chronology"); }
        Arc::make_mut(&mut snapshot.stroke_components).retain(|key,_| selected.contains(&key));
        Arc::make_mut(&mut snapshot.chrono_components).retain(|key,_| selected.contains(&key));
        Self::from_snapshot(snapshot).render_frame().await
    }

    /// Read actual retained donor ranks for exact entities; no canonical identity
    /// or edit permission is inferred from a caller-supplied rank/key.
    pub fn read_user_layer_ranks(&self, keys: &[u64]) -> Result<Vec<u32>> {
        if keys.is_empty() || keys.len() > 1_000_000 { anyhow::bail!("bounded exact layer entities required"); }
        let snapshot = self.engine.take_snapshot();
        let mut seen = HashSet::new();
        keys.iter().map(|value| {
            let key = rnote_engine::store::StrokeKey::from(KeyData::from_ffi(*value));
            if !seen.insert(key) || !snapshot.stroke_components.contains_key(key) { anyhow::bail!("invalid layer entity"); }
            let chrono = snapshot.chrono_components.get(key).context("missing donor chronology")?;
            match chrono.layer {
                rnote_engine::store::chrono_comp::StrokeLayer::UserLayer(rank) => Ok(rank),
                _ => anyhow::bail!("reserved donor layer requires its own supported operation"),
            }
        }).collect()
    }

    /// Assign actual maintained donor user-layer ranks without changing stable
    /// entity keys, per-layer chronology, authored geometry or reserved roles.
    /// Canonical LayerID ownership/mapping is supplied by the owning document.
    pub fn assign_user_layer_ranks(&mut self, keys: &[u64], ranks: &[u32]) -> Result<()> {
        if self.stroke_active { anyhow::bail!("cannot change layers during an active stroke"); }
        if keys.is_empty() || keys.len() != ranks.len() || keys.len() > 1_000_000 {
            anyhow::bail!("layer assignment requires matched bounded entities and ranks");
        }
        let selected = keys.iter().map(|value| rnote_engine::store::StrokeKey::from(KeyData::from_ffi(*value))).collect::<Vec<_>>();
        if selected.iter().copied().collect::<HashSet<_>>().len() != keys.len() { anyhow::bail!("duplicate layer entity"); }
        let mut snapshot = self.engine.take_snapshot();
        for key in &selected {
            if !snapshot.stroke_components.contains_key(*key) { anyhow::bail!("missing layer entity"); }
            let chrono = snapshot.chrono_components.get(*key).context("missing donor chronology")?;
            if !matches!(chrono.layer, rnote_engine::store::chrono_comp::StrokeLayer::UserLayer(_)) {
                anyhow::bail!("reserved donor layer requires its own supported operation");
            }
        }
        for (key, rank) in selected.iter().zip(ranks) {
            let chrono = Arc::make_mut(&mut snapshot.chrono_components).get_mut(*key).context("missing validated donor chronology")?;
            Arc::make_mut(chrono).layer = rnote_engine::store::chrono_comp::StrokeLayer::UserLayer(*rank);
        }
        let _ = self.engine.load_snapshot(snapshot);
        Ok(())
    }

    /// Remove the exact persisted donor entity, preserving surviving slot keys,
    /// chronology, document and camera. Canonical history owns this snapshot edit.
    pub fn delete_stroke(&mut self, key: u64) -> Result<()> {
        if self.stroke_active { anyhow::bail!("cannot delete during an active stroke"); }
        let key = rnote_engine::store::StrokeKey::from(KeyData::from_ffi(key));
        let mut snapshot = self.engine.take_snapshot();
        if !snapshot.stroke_components.contains_key(key) {
            anyhow::bail!("native stroke key is not present in the current snapshot");
        }
        Arc::make_mut(&mut snapshot.stroke_components).remove(key);
        Arc::make_mut(&mut snapshot.chrono_components).remove(key);
        let _ = self.engine.load_snapshot(snapshot);
        Ok(())
    }

    /// Apply the donor's own structured stroke translation, never a rendered
    /// image transform or a reconstruction from sampled canonical geometry.
    pub fn translate_stroke(&mut self, key: u64, delta_x: f64, delta_y: f64) -> Result<()> {
        if self.stroke_active { anyhow::bail!("cannot translate during an active stroke"); }
        if !delta_x.is_finite() || !delta_y.is_finite() {
            anyhow::bail!("native stroke translation must be finite");
        }
        let key = rnote_engine::store::StrokeKey::from(KeyData::from_ffi(key));
        let mut snapshot = self.engine.take_snapshot();
        let stroke = Arc::make_mut(&mut snapshot.stroke_components).get_mut(key)
            .context("native stroke key is not present in the current snapshot")?;
        Arc::make_mut(stroke).translate(Vector2::new(delta_x, delta_y));
        let bounds = stroke.bounds();
        if !bounds.mins.is_finite() || !bounds.maxs.is_finite() {
            anyhow::bail!("translated donor geometry exceeds finite coordinates");
        }
        Arc::make_mut(stroke).update_geometry();
        let _ = self.engine.load_snapshot(snapshot);
        Ok(())
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
        Ok(Self::from_snapshot(snapshot))
    }

    /// Import Xournal++ through the controlled Rnote engine, preserving its
    /// editable stroke/image/text snapshot rather than flattening to a bitmap.
    pub async fn from_xopp(bytes: Vec<u8>, dpi: f64) -> Result<Self> {
        if !dpi.is_finite() || !(1.0..=2400.0).contains(&dpi) {
            anyhow::bail!("Xopp DPI must be finite and within 1..=2400");
        }
        let snapshot = EngineSnapshot::load_from_xopp_bytes(bytes, XoppImportPrefs { dpi })
            .await
            .context("Rnote Xopp import failed")?;
        // Normalize once at import to the donor's persisted precision. A preview
        // must represent the committed snapshot, including donor DPI rounding,
        // rather than displaying geometry that changes after the first reopen.
        let imported = Self::from_snapshot(snapshot);
        Self::from_rnote(imported.save_rnote().await?).await
    }

    fn from_snapshot(snapshot: EngineSnapshot) -> Self {
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
        canvas
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

fn exact_subpath_offsets(parent: &rnote_compose::PenPath, fragment: &rnote_compose::PenPath,
    budget: &mut usize) -> Result<Vec<usize>> {
    if fragment.segments.is_empty() || fragment.segments.len() > parent.segments.len() { return Ok(vec![]); }
    let start = serde_json::to_value(fragment.start)?;
    let segments = fragment.segments.iter().map(serde_json::to_value).collect::<std::result::Result<Vec<_>, _>>()?;
    let mut offsets = Vec::new();
    for offset in 0..=parent.segments.len()-fragment.segments.len() {
        if *budget == 0 { anyhow::bail!("split exact lineage comparison budget exceeded"); } *budget -= 1;
        let source_start = if offset == 0 { parent.start } else { parent.segments[offset-1].end() };
        if serde_json::to_value(source_start)? != start { continue; }
        let mut matches = true;
        for (source, fragment) in parent.segments[offset..offset+segments.len()].iter().zip(segments.iter()) {
            if *budget == 0 { anyhow::bail!("split exact lineage comparison budget exceeded"); } *budget -= 1;
            if serde_json::to_value(source)? != *fragment { matches = false; break; }
        }
        if matches { offsets.push(offset); }
    }
    Ok(offsets)
}

#[cfg(test)]
mod tests {
    use super::*;
    use futures::executor::block_on;

    fn stable_svg(bytes: &[u8]) -> String {
        let mut svg = String::from_utf8(bytes.to_vec()).unwrap();
        let mut ids = Vec::new();
        let mut tail = svg.as_str();
        while let Some(index) = tail.find("id=\"") {
            tail = &tail[index + 4..];
            let end = tail.find('"').unwrap();
            ids.push(tail[..end].to_owned());
            tail = &tail[end + 1..];
        }
        // Export-generated SVG resource IDs are random and have no document
        // identity semantics. Preserve/reference their topology while comparing
        // every path, colour, geometry and remaining serialized attribute.
        for (index, id) in ids.into_iter().enumerate() {
            svg = svg.replace(&format!("id=\"{id}\""), &format!("id=\"resource-{index}\""));
            svg = svg.replace(&format!("#{id}"), &format!("#resource-{index}"));
        }
        svg
    }

    #[test]
    fn controlled_donor_imports_editable_xopp_and_round_trips_rendering() {
        block_on(async {
            use flate2::{Compression, write::GzEncoder};
            use std::io::Write;
            let xml = r##"<?xml version="1.0"?><xournal creator="Canvas donor bridge" fileversion="4"><title>Imported handwriting</title><page width="595" height="842"><background type="solid" color="#ffffffff" style="plain"/><layer><stroke tool="pen" color="#000000ff" width="2">10 20 30 40 50 35</stroke></layer></page></xournal>"##;
            let mut gzip = GzEncoder::new(Vec::new(), Compression::default());
            gzip.write_all(xml.as_bytes()).unwrap();
            let canvas = HeadlessCanvasEngine::from_xopp(gzip.finish().unwrap(), 96.0).await.unwrap();
            let before = canvas.render_frame().await.unwrap();
            assert!(before.bytes.len() > 200);
            let native = canvas.save_rnote().await.unwrap();
            let restored = HeadlessCanvasEngine::from_rnote(native).await.unwrap();
            let after = restored.render_frame().await.unwrap();
            assert_eq!(before.bounds, after.bounds);
            assert_eq!(stable_svg(&before.bytes), stable_svg(&after.bytes));
            assert!(restored.debug_state_json().unwrap().contains("brushstroke"));
            assert!(HeadlessCanvasEngine::from_xopp(vec![1, 2, 3], 96.0).await.is_err());
            assert!(HeadlessCanvasEngine::from_xopp(vec![1], f64::NAN).await.is_err());
        });
    }

    fn sample_stroke() -> [CanvasPointerSample; 5] {
        [
            CanvasPointerSample::new(120.0, 120.0, 0.15),
            CanvasPointerSample::new(150.0, 140.0, 0.35),
            CanvasPointerSample::new(190.0, 160.0, 0.65),
            CanvasPointerSample::new(235.0, 190.0, 0.90),
            CanvasPointerSample::new(280.0, 220.0, 0.55),
        ]
    }

    #[test]
    fn selected_native_export_preserves_keys_and_original_paths_without_unselected_entities() {
        block_on(async {
            let mut canvas = HeadlessCanvasEngine::new();
            let first = sample_stroke();
            canvas.begin_stroke(first[0]).unwrap();
            canvas.update_stroke(first[1]).unwrap();
            canvas.end_stroke(first[2]).unwrap();
            let first_key = canvas.stroke_keys()[0];
            canvas.begin_stroke(CanvasPointerSample::new(900.0, 950.0, 0.3)).unwrap();
            canvas.end_stroke(CanvasPointerSample::new(930.0, 980.0, 0.7)).unwrap();
            assert_eq!(canvas.stroke_keys().len(), 2);
            let reopened = HeadlessCanvasEngine::from_rnote(canvas.save_rnote().await.unwrap()).await.unwrap();
            assert_eq!(canvas.stroke_keys(), reopened.stroke_keys());
            let selected = HeadlessCanvasEngine::from_rnote(reopened.selected_strokes_rnote(&[first_key]).await.unwrap()).await.unwrap();
            assert_eq!(selected.stroke_keys(), vec![first_key]);
            let original = reopened.engine.take_snapshot();
            let exported = selected.engine.take_snapshot();
            let key = rnote_engine::store::StrokeKey::from(KeyData::from_ffi(first_key));
            assert_eq!(serde_json::to_value(original.stroke_components.get(key).unwrap()).unwrap(),
                       serde_json::to_value(exported.stroke_components.get(key).unwrap()).unwrap());
            assert!(canvas.selected_strokes_rnote(&[]).await.is_err());
            assert!(canvas.selected_strokes_rnote(&[first_key, first_key]).await.is_err());
            assert!(canvas.selected_strokes_rnote(&[u64::MAX]).await.is_err());
            assert_eq!(canvas.stroke_keys().len(), 2); // read-only export
        });
    }

    #[test]
    fn keyed_mutations_use_donor_geometry_preserve_survivors_and_reject_without_side_effects() {
        block_on(async {
            let mut canvas = HeadlessCanvasEngine::new();
            canvas.begin_stroke(CanvasPointerSample::new(10.0, 20.0, 0.2)).unwrap();
            canvas.end_stroke(CanvasPointerSample::new(40.0, 60.0, 0.7)).unwrap();
            let first = canvas.stroke_keys()[0];
            canvas.begin_stroke(CanvasPointerSample::new(500.0, 600.0, 0.4)).unwrap();
            canvas.end_stroke(CanvasPointerSample::new(540.0, 660.0, 0.8)).unwrap();
            let second = *canvas.stroke_keys().iter().find(|key| **key != first).unwrap();
            let original = canvas.engine.take_snapshot();
            let first_key = rnote_engine::store::StrokeKey::from(KeyData::from_ffi(first));
            let second_key = rnote_engine::store::StrokeKey::from(KeyData::from_ffi(second));
            let first_bounds = original.stroke_components[first_key].bounds();
            let untouched = serde_json::to_value(&original.stroke_components[second_key]).unwrap();
            canvas.translate_stroke(first, 100.0, 200.0).unwrap();
            let moved = canvas.engine.take_snapshot();
            let moved_bounds = moved.stroke_components[first_key].bounds();
            assert_eq!(first_bounds.mins + Vector2::new(100.0, 200.0), moved_bounds.mins);
            assert_eq!(first_bounds.maxs + Vector2::new(100.0, 200.0), moved_bounds.maxs);
            assert_eq!(untouched, serde_json::to_value(&moved.stroke_components[second_key]).unwrap());
            let before_failure = serde_json::to_value(&moved).unwrap();
            assert!(canvas.translate_stroke(first, f64::NAN, 0.0).is_err());
            assert!(canvas.delete_stroke(u64::MAX).is_err());
            assert_eq!(before_failure, serde_json::to_value(canvas.engine.take_snapshot()).unwrap());
            canvas.delete_stroke(first).unwrap();
            assert_eq!(canvas.stroke_keys(), vec![second]);
            let reopened = HeadlessCanvasEngine::from_rnote(canvas.save_rnote().await.unwrap()).await.unwrap();
            assert_eq!(reopened.stroke_keys(), vec![second]);
            assert_eq!(untouched, serde_json::to_value(&reopened.engine.take_snapshot().stroke_components[second_key]).unwrap());
        });
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
    #[test]
    fn quick_hit_uses_real_hitboxes_layer_first_order_and_refuses_ambiguous_chronology_read_only() {
        block_on(async {
            let mut canvas = HeadlessCanvasEngine::new();
            canvas.begin_stroke(CanvasPointerSample::new(100.0, 100.0, 0.4)).unwrap();
            canvas.end_stroke(CanvasPointerSample::new(300.0, 100.0, 0.8)).unwrap();
            let first = canvas.stroke_keys()[0];
            canvas.begin_stroke(CanvasPointerSample::new(100.0, 100.0, 0.7)).unwrap();
            canvas.end_stroke(CanvasPointerSample::new(300.0, 100.0, 0.5)).unwrap();
            let last = *canvas.stroke_keys().iter().find(|key| **key != first).unwrap();
            let before = serde_json::to_value(canvas.engine.take_snapshot()).unwrap();
            assert_eq!(canvas.quick_erase_target(200.0, 100.0).unwrap(), Some(last));
            assert_eq!(canvas.quick_erase_target(200.0, 300.0).unwrap(), None);
            assert!(canvas.quick_erase_target(f64::NAN, 100.0).is_err());
            assert_eq!(before, serde_json::to_value(canvas.engine.take_snapshot()).unwrap());
            let mut snapshot = canvas.engine.take_snapshot();
            let first_key = rnote_engine::store::StrokeKey::from(KeyData::from_ffi(first));
            let last_key = rnote_engine::store::StrokeKey::from(KeyData::from_ffi(last));
            // Earlier user-layer ink must stay above later highlighter ink.
            Arc::make_mut(Arc::make_mut(&mut snapshot.chrono_components).get_mut(last_key).unwrap()).layer =
                rnote_engine::store::chrono_comp::StrokeLayer::Highlighter;
            let _ = canvas.engine.load_snapshot(snapshot);
            assert_eq!(canvas.quick_erase_target(200.0, 100.0).unwrap(), Some(first));
            let mut snapshot = canvas.engine.take_snapshot();
            let original = snapshot.chrono_components[first_key].clone();
            *Arc::make_mut(&mut snapshot.chrono_components).get_mut(last_key).unwrap() = original;
            let _ = canvas.engine.load_snapshot(snapshot);
            let ambiguous = serde_json::to_value(canvas.engine.take_snapshot()).unwrap();
            assert!(canvas.quick_erase_target(200.0, 100.0).is_err());
            assert_eq!(ambiguous, serde_json::to_value(canvas.engine.take_snapshot()).unwrap());
        });
    }
    #[test]
    fn genuine_split_materializes_exact_mixed_curve_fragments_and_original_pressure_style_identity() {
        block_on(async {
            use rnote_compose::penpath::Segment;
            use rnote_engine::strokes::{BrushStroke, Stroke};
            let mut canvas = HeadlessCanvasEngine::new();
            canvas.draw_stroke(&[CanvasPointerSample::new(0.0,0.0,0.1), CanvasPointerSample::new(160.0,0.0,0.9)]).unwrap();
            let key_u64 = canvas.stroke_keys()[0];
            let key = rnote_engine::store::StrokeKey::from(KeyData::from_ffi(key_u64));
            let e = |x, pressure| Element::new(Vector2::new(x,0.0),pressure);
            let path = rnote_compose::PenPath::new_w_segments(e(0.0,0.1), vec![
                Segment::LineTo { end:e(20.0,0.2) },
                Segment::QuadBezTo { cp:Vector2::new(30.0,8.0), end:e(40.0,0.3) },
                Segment::CubBezTo { cp1:Vector2::new(45.0,-8.0), cp2:Vector2::new(55.0,8.0), end:e(60.0,0.4) },
                Segment::LineTo { end:e(80.0,0.5) },
                Segment::LineTo { end:e(100.0,0.6) },
                Segment::QuadBezTo { cp:Vector2::new(110.0,8.0), end:e(120.0,0.7) },
                Segment::CubBezTo { cp1:Vector2::new(125.0,-8.0), cp2:Vector2::new(135.0,8.0), end:e(140.0,0.8) },
                Segment::LineTo { end:e(160.0,0.9) },
            ]);
            let mut snapshot = canvas.engine.take_snapshot();
            let Stroke::BrushStroke(brush) = snapshot.stroke_components[key].as_ref() else { panic!("genuine source is not a brushstroke"); };
            let style = brush.style.clone();
            *Arc::make_mut(&mut snapshot.stroke_components).get_mut(key).unwrap() = Arc::new(Stroke::BrushStroke(BrushStroke::from_penpath(path.clone(),style.clone())));
            let _ = canvas.engine.load_snapshot(snapshot);
            let original = serde_json::to_value(canvas.engine.take_snapshot()).unwrap();
            // Genuine typed donor factory bytes, no fake polyline/control sampling.
            let fixture = std::env::temp_dir().join("split-parent-mixed-curves.rnote");
            std::fs::write(&fixture,canvas.save_rnote().await.unwrap()).unwrap();
            let (native, receipt) = canvas.split_erase_candidate(&[
                CanvasPointerSample::new(80.0,0.0,0.5),CanvasPointerSample::new(80.0,0.0,0.5)],1.0).await.unwrap();
            let receipt: serde_json::Value = serde_json::from_slice(&receipt).unwrap();
            assert_eq!(receipt["changes"].as_array().unwrap().len(),2);
            assert!(receipt["removedKeys"].as_array().unwrap().is_empty());
            let restored = HeadlessCanvasEngine::from_rnote(native).await.unwrap();
            assert_eq!(restored.stroke_keys().len(),2);
            assert!(restored.stroke_keys().contains(&key_u64));
            for change in receipt["changes"].as_array().unwrap() {
                assert_eq!(change["sourceNativeKey"].as_u64(),Some(key_u64));
                assert_eq!(change["style"],serde_json::to_value(&style).unwrap());
                let fragment: rnote_compose::PenPath = serde_json::from_value(change["path"].clone()).unwrap();
                let offsets = exact_subpath_offsets(&path,&fragment,&mut 4_000_000usize).unwrap();
                assert!(!offsets.is_empty());
                assert!(fragment.segments.iter().any(|segment| matches!(segment,Segment::QuadBezTo { .. })));
                assert!(fragment.segments.iter().any(|segment| matches!(segment,Segment::CubBezTo { .. })));
            }
            assert_eq!(original,serde_json::to_value(canvas.engine.take_snapshot()).unwrap());
            assert!(canvas.split_erase_candidate(&[CanvasPointerSample::new(80.0,0.0,0.5);2],f64::NAN).await.is_err());
            assert_eq!(original,serde_json::to_value(canvas.engine.take_snapshot()).unwrap());
        });
    }

    #[test]
    fn genuine_user_layer_rank_changes_native_render_order_and_survives_reopen() {
        fn collect_float_bits(value: &serde_json::Value, path: &str, out: &mut std::collections::BTreeMap<String,u64>) {
            if let Some(number) = value.as_number().filter(|number| number.is_f64()) {
                out.insert(path.to_owned(), number.as_f64().unwrap().to_bits());
            }
            match value {
                serde_json::Value::Object(values) => for (key,value) in values { collect_float_bits(value,&format!("{path}/{}",key.replace('~',"~0").replace('/',"~1")),out); },
                serde_json::Value::Array(values) => for (index,value) in values.iter().enumerate() { collect_float_bits(value,&format!("{path}/{index}"),out); },
                _ => {},
            }
        }
        // Capture only controlled fixture data; emit bounded numeric evidence only
        // if an original assertion panics, then propagate that exact panic.
        let mut reopen_failure_diagnostic: Option<serde_json::Value> = None;
        let original_attempt = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        block_on(async {
            let mut canvas = HeadlessCanvasEngine::new();
            canvas.set_pen_style(CanvasTool::Pen, CanvasPenStyle { color: [1.0,0.0,0.0,1.0], width: 20.0 }).unwrap();
            canvas.draw_stroke(&sample_stroke()).unwrap();
            let first = canvas.stroke_keys()[0];
            canvas.set_pen_style(CanvasTool::Pen, CanvasPenStyle { color: [0.0,0.0,1.0,1.0], width: 20.0 }).unwrap();
            canvas.draw_stroke(&sample_stroke()).unwrap();
            let second = *canvas.stroke_keys().iter().find(|key| **key != first).unwrap();
            let keys = canvas.stroke_keys();
            let original_bytes = canvas.save_rnote().await.unwrap();
            let full_frame = stable_svg(&canvas.render_frame().await.unwrap().bytes);
            let visible_frame = stable_svg(&canvas.render_visible_keys(&[first]).await.unwrap().bytes);
            let empty_frame = stable_svg(&canvas.render_visible_keys(&[]).await.unwrap().bytes);
            assert_ne!(full_frame, visible_frame);assert_ne!(visible_frame, empty_frame);
            assert_eq!(original_bytes, canvas.save_rnote().await.unwrap());
            assert_eq!(keys, canvas.stroke_keys());
            assert_eq!(full_frame, stable_svg(&canvas.render_frame().await.unwrap().bytes));
            assert!(canvas.render_visible_keys(&[first,first]).await.is_err());
            assert!(canvas.render_visible_keys(&[u64::MAX]).await.is_err());
            let before = stable_svg(&canvas.export_svg().await.unwrap());
            canvas.assign_user_layer_ranks(&[first,second], &[1,0]).unwrap();
            assert_eq!(keys, canvas.stroke_keys());
            assert_eq!(vec![1,0], canvas.read_user_layer_ranks(&[first,second]).unwrap());
            let after = stable_svg(&canvas.export_svg().await.unwrap());
            assert_ne!(before, after);
            let bytes = canvas.save_rnote().await.unwrap();
            let mut restored = HeadlessCanvasEngine::from_rnote(bytes.clone()).await.unwrap();
            if bytes.len() <= 262_144 {
                if let (Ok(original_snapshot),Ok(reopened_snapshot)) = (
                    serde_json::to_value(canvas.engine.take_snapshot()),
                    serde_json::to_value(restored.engine.take_snapshot())) {
                    let mut original_bits = std::collections::BTreeMap::new();
                    let mut reopened_bits = std::collections::BTreeMap::new();
                    collect_float_bits(&original_snapshot,"",&mut original_bits);
                    collect_float_bits(&reopened_snapshot,"",&mut reopened_bits);
                    let native_hex = bytes.iter().map(|byte|format!("{byte:02x}")).collect::<String>();
                    reopen_failure_diagnostic = Some(serde_json::json!({
                        "schemaVersion":1,"fixture":"genuine_user_layer_rank_changes_native_render_order_and_survives_reopen",
                        "nativeCompressedBytes":bytes.len(),"originalNativeHex":native_hex,
                        "originalSnapshot":original_snapshot,"reopenedSnapshot":reopened_snapshot,
                        "originalFloatBitsByJsonPointer":original_bits,"reopenedFloatBitsByJsonPointer":reopened_bits,
                        "qualification":"Controlled synthetic native snapshot data only; exact original SVG equality and all other assertions remain authoritative."}));
                }
            }
            assert_eq!(keys, restored.stroke_keys());
            assert_eq!(vec![1,0], restored.read_user_layer_ranks(&[first,second]).unwrap());
            assert_eq!(after, stable_svg(&restored.export_svg().await.unwrap()));
            assert!(restored.assign_user_layer_ranks(&[first,first], &[0,1]).is_err());
            assert!(restored.assign_user_layer_ranks(&[first,u64::MAX], &[0,1]).is_err());
            assert!(restored.assign_user_layer_ranks(&[first], &[]).is_err());
            assert_eq!(bytes, restored.save_rnote().await.unwrap());
            restored.assign_user_layer_ranks(&[first,second], &[0,0]).unwrap();
            assert_eq!(before, stable_svg(&restored.export_svg().await.unwrap()));
        });
        }));
        if let Err(original_panic) = original_attempt {
            if let Some(diagnostic) = reopen_failure_diagnostic {
                if let Ok(text) = serde_json::to_string(&diagnostic) {
                    let line = if text.len() <= 1_048_576 {
                        format!("ASTRA_CANVAS_REOPEN_NUMERIC_FAILURE {text}\n")
                    } else {
                        format!("ASTRA_CANVAS_REOPEN_NUMERIC_FAILURE_REFUSED oversized={}\n",text.len())
                    };
                    let mut stderr = std::io::stderr().lock();
                    let _ = std::io::Write::write_all(&mut stderr,line.as_bytes());
                }
            }
            std::panic::resume_unwind(original_panic);
        }
    }

}

#[cfg(test)]
mod native_selector_tests {
    use super::*;
#[test]
fn genuine_selector_single_and_rectangle_are_read_only_with_exact_original_keys() {
    futures::executor::block_on(async {
        let mut canvas = HeadlessCanvasEngine::default();
        canvas.set_viewport_size(1000.0, 1000.0).unwrap();
        canvas.set_viewport_center(250.0, 250.0).unwrap();
        canvas.begin_stroke(CanvasPointerSample::new(100.0,100.0,0.5)).unwrap();
        canvas.end_stroke(CanvasPointerSample::new(300.0,100.0,0.5)).unwrap();
        let key = canvas.stroke_keys()[0];
        let bytes = canvas.save_rnote().await.unwrap();
        assert_eq!(canvas.preview_native_selection(2, &[CanvasPointerSample::new(200.0,100.0,0.5)]).await.unwrap(), vec![key]);
        assert_eq!(canvas.preview_native_selection(1, &[CanvasPointerSample::new(50.0,50.0,0.5),CanvasPointerSample::new(350.0,150.0,0.5)]).await.unwrap(), vec![key]);
        assert!(canvas.preview_native_selection(2, &[CanvasPointerSample::new(200.0,300.0,0.5)]).await.unwrap().is_empty());
        assert!(canvas.preview_native_selection(99, &[CanvasPointerSample::new(200.0,100.0,0.5)]).await.is_err());
        assert_eq!(bytes, canvas.save_rnote().await.unwrap());
    });
}
}
