# Picture — image and layer editing — Picture; Product role and editing-first boundary (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1667 · v2 lines 7634–7830

Picture
Product role and editing-first boundary
9-1 Picture is the native CUI image creation, editing and composition app for raster, vector and hybrid visual work. Its primary purpose is powerful creation/editing, not passive image viewing. Picture MUST support professional-grade manual editing, simple object-first workflows, non-destructive composition, AI-assisted editing, generative image creation, sketch-to-image conversion and advanced vector tooling while remaining approachable to users who do not want to manage low-level layer structure.
Browsing/viewing/library workflows remain supported but are secondary to Create, Edit and Generate. Core crop/transform/colour/vector/text/drawing/export workflows MUST remain fully usable when AI is disabled, unavailable or disallowed.
Shared-engine reuse and fast feature parity
Picture MUST reuse the mature shared engines already used by Write, Present, Canvas and Boards whenever capability overlaps. It MUST NOT create Picture-private duplicates of shared drawing, text, shape, selection, transform, clipboard, accessibility, undo/history or embedded-object semantics merely for implementation convenience.
Required reuse includes at minimum:
* The Home-owned Shared Productivity Engine for shared text, shapes, media/object identity, selection, transform, clipboard/transfer, accessibility and typed object actions where those semantics apply.
* The Rnote-derived shared drawing/ink engine already used by Canvas and Boards for pens, strokes, pressure/tilt, eraser behaviour, stroke serialisation and drawing selection/manipulation.
* Shared shape/vector primitives, transform handles, snapping/alignment and object manipulation behaviours already exercised by Present/Canvas/Boards where applicable.
* Shared rich-text/typography primitives from Write/Present for editable text objects rather than a second incompatible text engine.
* Shared Files identity/storage, CUI runtime, Home permission/trust, persistent AI bar, revision/action conventions and cross-app artifact references.
Where Picture needs a capability that logically belongs in one of these shared engines, the implementation SHOULD extend that shared engine centrally and enable the new capability for other eligible apps rather than fork it inside Picture. This reuse is a release requirement intended to accelerate feature parity and keep behaviour identical across the ecosystem.
Canonical Picture document model
Picture MUST use a versioned editable PictureDocument rather than treating the current rendered bitmap as the sole source of truth. A document has stable DocumentID, optional backing FileID/source revision, canvas/artboard geometry, colour profile, background, canonical ElementGraph/LayerGraph, linked assets, edit history/revisions, metadata and document revision.
DocumentType supports Raster, Vector and Hybrid. Hybrid documents can contain raster content, editable vector paths/shapes, text, generated objects, masks, drawing/ink, adjustments and linked assets in one document without destructive flattening.
Every independently editable object has stable ElementID plus ElementType, parent/group identity, bounds/transform, visibility, lock state, blend/compositing properties where applicable, mask/effect references, metadata and revision. Renaming/reordering does not change identity.
Automatic composition mode and manual layers
Picture uses automatic composition/layer management by default. Typical users can select, move, erase, resize, generate, recolour or annotate objects without manually constructing a Photoshop-style layer stack. Picture automatically creates/updates the internal objects, masks, adjustment state and generated fills required to keep the edit non-destructive.
Manual Layer mode is an optional advanced view over the SAME canonical document model. It exposes explicit layers/groups, masks/clipping, order, visibility, locks, opacity, blend modes, adjustment/effect layers and names. Switching between automatic and manual views MUST NOT flatten or convert the document.
Automatic mode may group implementation details into understandable objects such as Subject, Background, Text or Generated Object while preserving the full internal structure for advanced inspection/editing.
Layer and composition semantics
The canonical layer/composition graph MUST support at minimum RasterImage, VectorShape, VectorPath, Text, Drawing/Ink, GeneratedObject, Adjustment, Fill, Mask, Group, Artboard/Canvas, Background and Linked/Smart Asset element families.
Layer/object order determines compositing order. Groups preserve child identity. Masks remain independently editable. Adjustment/effect nodes SHOULD remain non-destructive and apply to explicit target scope rather than permanently rewriting source pixels.
A user can rasterise/flatten selected content explicitly, but Picture MUST NOT silently flatten editable content merely because a tool is easier to implement that way.
Selection and semantic object extraction
Picture MUST provide a mature selection system including rectangle, ellipse, lasso, polygonal/freehand lasso, brush/refine, colour/range selection and smart/semantic selection where AI/vision capability exists. Selection operations include add, subtract, intersect, invert, feather, expand/contract and edge refinement where applicable.
Semantic/object selection can identify a subject/object/background and convert the result into an independently editable Picture element/mask. The user can also circle or roughly mark a subject and ask Picture to infer the intended object boundary.
Selection state is not itself document identity unless explicitly saved as a mask/object, but saved masks/selections receive stable IDs and participate in history/revisions.
Object-first editing
A selected object/subject MUST support direct Move, Resize, Rotate, Duplicate, Delete/Erase, Replace, Isolate, Recolour/Restyle and front/back ordering actions where compatible.
When an object is moved, resized away from its previous occupied area or erased, Picture can use generative/background reconstruction to fill exposed gaps. The original underlying elements MUST remain revision-addressable so the edit can be undone/reverted or regenerated.
Example: circling a person and dragging them left creates/updates an editable subject object, relocates that subject, reconstructs the exposed background, and preserves both the subject and generated repair as editable/revisioned state rather than destructively baking the result.
Raster editing
Core raster editing MUST include at least:
* Crop, rotate, flip, straighten, resize/resample, canvas resize and perspective correction.
* Free transform, warp/distort and perspective transform where supported.
* Exposure, brightness, contrast, highlights, shadows, whites/blacks, gamma/levels and curves.
* Temperature, tint, saturation, vibrance, hue and channel/colour adjustments.
* Sharpen, clarity/detail, denoise, blur and other declared filters/effects.
* Clone/heal/repair, redaction/blur/pixelate and local adjustment tools.
* Background removal/extraction and alpha/transparency-aware editing.
Raster operations SHOULD be represented as non-destructive adjustments/masks/transform state where technically practical. An operation that must bake pixels MUST create a revision boundary with reversible history.
Vector creation and editing
Picture MUST provide powerful vector creation/editing suitable for serious illustration, asset and graphic-design work, with depth comparable to mature Photoshop-class vector/path tooling while keeping common actions simple.
Required vector capabilities include:
* Rectangle/ellipse/polygon/star/line/arrow and shared shape primitives.
* Pen/path tool, anchor-point editing, Bézier handles, open/closed paths and path segment manipulation.
* Fill, stroke, stroke width/alignment, opacity and gradient/pattern support where available.
* Boolean path operations: union/add, subtract, intersect and exclude/XOR.
* Group/ungroup, align, distribute, snap, transform, duplicate and precise positioning.
* Masks/clipping paths and vector effects where supported.
* Text-on-path and convert-text-to-path where supported.
* Rasterise vector selection and supported raster/vector tracing or conversion workflows.
Vector objects remain vector-editable until explicitly rasterised/flattened. Raster and vector elements can coexist in the same Hybrid PictureDocument.
Text objects
Text is a first-class editable object using shared Write/Present typography primitives where applicable. Picture text tools MUST support font family/style/size, colour/fill/stroke, line height, alignment, tracking/kerning where supported, transform/rotation, text box/area text, point text and appropriate effects.
Advanced vector-text behaviours such as text on path and convert to path SHOULD reuse the vector engine. Text must remain editable as text until the user explicitly converts/rasterises it.
Drawing and painting
Picture MUST reuse the shared Rnote-derived drawing engine for sketching, painting/markup and mask/selection brush workflows rather than implementing a separate stroke format. Supported drawing behaviours inherit Canvas/Boards pen, pressure/tilt, eraser, stroke selection/manipulation and custom pen/preset capability where relevant.
Picture may expose a simplified context-specific toolbar, but underlying stroke behaviour/serialisation remains shared. Sketch strokes can be retained as editable Drawing elements, used as masks/annotations, or supplied to AI generation as structural guidance.
AI editing and generative operations
AI editing is a first-class core Picture capability rather than an optional afterthought. Picture MUST support typed, revisioned AI operations including object removal, object replacement, generative fill, generative erase, generative expand/outpainting, background remove/replace, object move/resize with scene reconstruction, style/appearance change, image restoration/enhancement, upscaling, relighting/recolour assistance and prompt-based region/full-image editing where the configured model/tool supports them.
AI operations MUST target explicit DocumentID/ElementID/SelectionID/MaskID/revision context rather than inferred screen coordinates. Results become canonical editable Picture state or explicit generated variants; they MUST NOT silently overwrite the only source.
If a selected region is moved or deleted, generated background repair is scoped to the exposed/selected region and recorded separately enough to permit undo, regenerate or variant comparison.
Generative image creation
Picture MUST create new images from text prompts, sketches, reference images, mixed prompt+reference+sketch inputs and supported style/composition guidance. Generation controls include at least output size/aspect ratio, background/transparency intent where supported, reference inputs, style guidance and variant count/options exposed by the selected model/tool.
Generated results become normal Picture documents/assets that can immediately be edited with raster/vector/text/drawing tools. Generation history and input provenance remain associated with the created revision/variant.
Sketch-to-image
Sketch-to-image is a dedicated workflow. Users can draw a rough composition/shape layout with the shared drawing engine, optionally annotate regions with text or references, and generate a detailed image while retaining the original sketch and generation relationship.
Sketch-to-image MUST support iterative workflows such as sketch -> generate -> edit -> select region -> regenerate/refine. The user may preserve the sketch as an editable overlay/reference rather than losing it after generation.
Generative region editing
The user can define a region by smart selection, lasso, brush/mask, shape or current object and invoke typed actions such as Remove, Replace, Move, Resize, Recolour, Restyle, Extend or Generate In Region.
Remove reconstructs the missing area; Replace creates replacement content constrained to the selected region/context; Move/Resize preserve the selected subject while repairing exposed background; Extend generates outside current content/canvas bounds. Generated output is revisioned and attributable to the operation/input scope.
AI-assisted composition
Picture MAY support high-level composition instructions such as rebalance composition, reposition subject, change background, restyle a poster, convert a rough concept to a polished visual or convert a sketch to a clean vector-like design. These MUST resolve to inspectable canonical Picture mutations/variants rather than an opaque final bitmap when the requested operation can remain editable.
Non-destructive adjustments and edit stack
Picture MUST preserve non-destructive editability for transforms, crop, colour/light adjustments, effects, masks, vector/text objects and generative operations where technically possible. The app maintains an ordered edit/composition graph rather than repeatedly recompressing the source image after each slider or AI action.
Users can enable/disable or modify prior compatible adjustments, reorder compatible operations, compare before/after, and revert to original/source revision. Invalid reordering that would change unsupported dependency semantics must be rejected or require an explicit bake/convert step.
Variants and revision history
Picture provides session undo/redo plus persistent revision history. Users can preserve multiple generated/manual variants, compare them and branch/save a new variant without destroying the original.
AI operations record operation type, source revision, selected region/object references, prompt/reference inputs and result revision/tool provenance where available. Generated narrative summaries are not the authority; the structured operation/revision record is.
Smart/linked assets
Picture supports linked/smart assets where appropriate, including linked raster/vector/Picture assets that retain source identity and can be transformed/effected without immediate destructive embedding. Updating the source can update the linked representation subject to revision/conflict rules.
The user can explicitly embed/freeze/snapshot a linked asset when a self-contained result is required.
Crop, canvas and artboards
Crop MUST support Free, Original, common ratios such as 1:1, 4:3, 3:2, 16:9 and 9:16, plus custom ratio and straighten/rotation. Crop remains editable until explicit flatten/export semantics require baking.
PictureDocument MAY support one or more artboards/canvases for vector/graphic workflows. Artboard identity/bounds/background are explicit and export can target one artboard, selected artboards or the composed document.
Effects, blend and masks
Picture MUST support mature effect/blend workflows including opacity, blend modes, blur/sharpen, shadow/glow and other registered effects where the rendering backend supports them. Effects should be non-destructive and target explicit elements/groups/masks.
Mask editing uses the shared drawing/selection engine where appropriate. Clipping/masking behaviour must be deterministic and preserved through save/reopen.
Presets
Users can save, rename, duplicate, reorder, apply and delete/recover reusable edit/effect presets. Presets are typed operation/property sets, not opaque flattened filters, so applied settings remain editable.
AI-assisted presets MAY exist but MUST record which operations are deterministic versus model-generated.
Comparison and preview
The editor MUST provide Original/current comparison and SHOULD provide split/side-by-side before/after. Preview rendering may use reduced resolution for interactivity but final export/save renders from the canonical source/revision at declared quality.
Expensive effects/generation/export expose progress and cancellation without corrupting the last valid document state.
File formats and import
Picture MUST support mature viewing/import/edit/export workflows for common raster formats including JPEG, PNG, WebP, GIF, BMP and TIFF, plus HEIF/HEIC, AVIF and other formats where maintained codec support exists. Animated/multi-frame formats MUST preserve animation unless the user explicitly chooses a flattening conversion.
SVG/vector import/export SHOULD preserve editable vector structure where compatible. RAW support MAY be capability-dependent but, where enabled, must preserve the RAW source and use a non-destructive development/render path.
Unknown/unsupported format features must be surfaced rather than silently stripped.
Animated and multi-frame images
Viewer/editor support for animated formats includes playback/pause, frame stepping and timeline/info. Editing operations define whether they apply to current frame, selected frames or all frames. A tool that would flatten animation MUST warn and require an explicit convert/flatten path.
Colour management and HDR
Picture MUST honour embedded colour profiles and alpha/transparency. sRGB is the safe baseline; wide-gamut/HDR display/edit behaviour is capability-negotiated where supported. Unsupported profiles/HDR paths must use a declared fallback/transform rather than silently displaying materially wrong colour.
Final export MUST allow appropriate colour-profile handling and warn when a target format cannot represent the working colour space/dynamic range.
Metadata and privacy
Picture's Info/Metadata view exposes available dimensions, format, file size, colour profile, capture date, camera/lens/exposure details, orientation, location and other supported EXIF/IPTC/XMP metadata. Unknown metadata should be preserved through non-destructive round trips where possible.
Export/privacy controls MUST support preserving metadata, removing location metadata and removing all removable metadata where the target format supports it.
Save, export and flattening
Save persists the editable Picture document/revision for the canonical asset. Save a Copy creates a new Files asset. Export renders to a selected interoperable target format/settings. Revert to Original restores/bypasses editable operations without deleting the source.
Formats that cannot store Picture's editable composition are exported as rendered/flattened outputs while the canonical Picture edit state remains separately persisted against the Picture document/asset. Flattening the editable source itself requires an explicit action.
Export options include size/resolution, format/compression/quality, transparency/background, colour profile, metadata/privacy and applicable animation/vector options.
Files and canonical asset identity
Files remains authoritative for filesystem/storage identity and operations such as open, create, rename, move, copy, delete/recover, share, provider/cloud hydration and bytes. Picture MUST NOT create a hidden duplicate file store.
A Picture asset/document can reference a canonical FileID plus source revision. Renaming/moving the file does not detach Picture state. CloudOnly content hydrates through Files and exposes progress/failure.
Cross-app interoperability
Picture assets can be embedded/referenced in Write, Present, Boards, Canvas and Spaces through canonical identity. A live reference follows the selected canonical asset/revision policy; an explicit Snapshot/Copy freezes or duplicates content.
Cross-app paste/drag/drop uses the Shared Productivity Engine/Files transfer contracts so supported raster/vector/text/drawing structure is preserved instead of automatically flattening to pixels.
Persistent AI bar
Picture MUST host the global persistent Dulche AI bar. Semantic context includes DocumentID/FileID, revision, selected ElementIDs/SelectionID/MaskID, current tool/mode, relevant composition/layer structure, canvas/artboard geometry, available metadata, linked assets and typed Picture actions.
Read-only mode can analyse/explain/describe without changing the document. Write mode performs edits only through typed Picture/shared-engine actions. AI MUST NOT simulate raw pointer gestures when semantic object/selection APIs exist.
Picture API
Picture's first-party API catalogue is exhaustive under the global API-parity rule. Minimum canonical entry points include:
9to1.Picture.Open(fileID);
9to1.Picture.Create(config);
9to1.Picture.GetDocument(documentID);
9to1.Picture.ListAssets(filters?);
9to1.Picture.ListElements(documentID, filters?);
9to1.Picture.GetElement(documentID, elementID);
9to1.Picture.AddElement(documentID, element);
9to1.Picture.UpdateElement(documentID, elementID, patch);
9to1.Picture.RemoveElement(documentID, elementID);
9to1.Picture.MoveElement(documentID, elementID, transform);
9to1.Picture.GroupElements(documentID, elementIDs);
9to1.Picture.Ungroup(documentID, groupID);
9to1.Picture.CreateSelection(documentID, selection);
9to1.Picture.UpdateSelection(documentID, selectionID, patch);
9to1.Picture.ApplyAdjustment(documentID, target, adjustment);
9to1.Picture.Crop(documentID, crop);
9to1.Picture.Resize(documentID, size);
9to1.Picture.AddMask(documentID, target, mask);
9to1.Picture.Generate(documentID?, request);
9to1.Picture.GenerateInRegion(documentID, selectionOrMaskID, request);
9to1.Picture.RemoveObject(documentID, elementOrSelectionID, options?);
9to1.Picture.ReplaceObject(documentID, elementOrSelectionID, request);
9to1.Picture.MoveObjectWithFill(documentID, elementOrSelectionID, transform, options?);
9to1.Picture.ExtendCanvas(documentID, bounds, request?);
9to1.Picture.SketchToImage(documentID, drawingElementIDs, request);
9to1.Picture.Save(documentID);
9to1.Picture.SaveCopy(documentID, destination, options?);
9to1.Picture.Export(documentID, format, options);
9to1.Picture.GetHistory(documentID);
9to1.Picture.Revert(documentID, revisionID);
Shared drawing/text/shape/selection/clipboard primitives SHOULD use their owning shared API namespace where that is the canonical operation. Picture MUST NOT duplicate the same shared action under incompatible semantics.
Automation and Spaces integration
Automations, Spaces and Dulche can invoke the exhaustive Picture action catalogue subject to permissions/capabilities. Long-running generation/render/export operations return job/run state with progress, cancellation and typed errors.
Picture publishes typed events for meaningful document/revision/generation/export changes without flooding Automations with raw pointer/stroke-move events.
Collaboration and conflict handling
Picture mutations are revision-aware. Independent edits may merge where the canonical object graph proves them non-conflicting; concurrent incompatible edits to the same element/mask/layer/revision MUST surface a conflict rather than blind last-write-wins.
AI-generated edits are attributable to a revision/action actor and are undoable/reviewable through the same history system as manual changes.
Security and generated content
AI/image generation/editing follows configured model/tool permissions, content controls, privacy/local-cloud routing policy and source-asset access permissions. Picture MUST NOT upload local/private images to a remote model/provider when the effective privacy policy forbids it.
Generated/imported external content is treated as content, not executable code. Embedded metadata/scripts MUST NOT acquire application execution privileges.
Accessibility
Picture's Create/Edit/Generate/Browse surfaces MUST be keyboard-operable for required workflows. Sliders/controls expose names, values and keyboard adjustment. Layer/object lists, tools, masks and selection state expose meaningful semantic accessibility information.
Image descriptions/alt text MAY be stored/edited where applicable. Colour-only indicators require textual/semantic equivalents. Zoom/crop/transform handles have keyboard-accessible alternatives.
Platform capability behaviour
Picture MUST provide useful Windows, 9to1-OS and web editing/creation experiences over the same PictureDocument model. Codec, RAW, HDR, GPU effect, pen/tilt and local/remote AI capability differences are explicit and queryable.
The web app MUST NOT degrade into a read-only image viewer. Unsupported platform features are disabled with a capability reason while preserving the canonical document so another capable client can continue editing.
Performance and resource behaviour
Picture MUST use resolution-appropriate decode, GPU acceleration where suitable, incremental/tiled rendering for very large images, bounded preview/thumbnail caches, background full-quality rendering and cancellable long-running jobs.
Interactive edit previews may use reduced-resolution proxies, but final save/export renders from canonical source content at requested quality. Large layer/vector graphs should avoid full-document rerenders when only a bounded element/region changes.
AI-generated previews/variants and caches are bounded and revision-addressed. Closing or cancelling generation does not corrupt the last valid document.
Failure behaviour
Picture MUST return structured failures including DocumentNotFound, ElementNotFound, InvalidSelection, InvalidMask, UnsupportedFormat, UnsupportedCodecFeature, ColourProfileUnsupported, HDRUnsupported, RevisionConflict, LinkedAssetUnavailable, GenerationUnavailable, GenerationFailed, InvalidGenerationRegion, ExportUnsupportedFeature, RenderFailed, PermissionDenied and CapabilityUnavailable in addition to shared engine/model errors.
A failed edit/generation/export leaves the prior valid document revision intact. Unsupported export features are reported before destructive flattening/conversion.
Acceptance requirements
Picture is implementation-complete only when acceptance coverage verifies at least:
* A real Raster, Vector and Hybrid PictureDocument can be created, saved, closed and reopened with editable structure intact.
* Automatic composition mode allows ordinary users to create/edit complex images without manual layer management, while Manual Layer mode exposes the same underlying canonical structure without conversion.
* Shared Write/Present/Canvas/Boards productivity, text, shape, selection/transform and Rnote-derived drawing engines are actually reused rather than reimplemented as Picture-private equivalents.
* Raster editing covers crop/transform, light/colour, blur/sharpen/repair and masks with non-destructive/revision-aware behaviour.
* Vector editing covers path/anchor/Bézier editing, fills/strokes, boolean operations, snapping/alignment, text/path integration and raster/vector conversion workflows.
* A user can circle/smart-select a subject, freely move/resize/delete/replace it, and Picture reconstructs exposed background while preserving revisioned editable state.
* Generative fill/erase/replace/extend and background replacement operate on explicit selections/objects and preserve source/revision provenance.
* Text-to-image, reference-guided creation and sketch-to-image create normal editable Picture assets/documents rather than opaque temporary results.
* Sketch-to-image can use shared drawing-engine strokes and supports iterative region refinement.
* Manual drawing/painting/markup uses the shared Rnote-derived drawing engine and retains editable stroke semantics.
* Before/after comparison, variants, undo/redo and persistent revision history survive restart.
* Raster, vector, text, generated objects, masks and linked assets coexist correctly in a Hybrid document.
* Save, Save Copy and export preserve the editable source while producing interoperable rendered/vector outputs with explicit flattening where required.
* Files identity survives rename/move/hydration and live cross-app references remain canonical.
* The persistent AI bar performs semantic typed edits against selected objects/regions and never requires raw UI simulation.
* Windows, 9to1-OS and web preserve the same canonical document model while capability-negotiating codecs/GPU/AI/pen differences.
* Large source images/layer graphs remain interactive through tiling/proxy rendering/bounded caches and final export renders at requested source quality.
* When Home is absent at process start, Picture follows the global Install Home/bootstrap contract rather than launching an incompatible private runtime.
