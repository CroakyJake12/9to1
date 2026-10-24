using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.UI;
using HavenOS.Apps.Canvas;
using HavenOS.Home.Core;

namespace Haven.Desktop.Views.Pages.Present;

public sealed partial class PresentPage
{
    private CanvasSharedInkInput? _freehandInput;
    private CuiControlLoader? _freehandToolsLoader, _freehandInputLoader;
    private Control? _freehandToolsRoot, _freehandInputRoot;
    private FreehandBindings? _freehandBindings;
    private PresentEditor? _freehandEditor;
    private PresentDocument? _freehandDocument;
    private long _freehandOwnerEpoch, _freehandViewportEpoch;
    private Rect? _freehandPaperBounds;
    private string? _freehandClipSignature, _freehandSelectionSignature;
    private bool _freehandEnabled, _refreshingFreehand;
    private PresentEditor? _freehandEraseCheckEditor;
    private Guid _freehandEraseCheckSlideId;
    private int _freehandEraseCheckVersion = -1;
    private long _freehandEraseCheckGeneration = -1;
    private bool _freehandEraseCheckResolved;
    private string? _freehandEraseBlocker;
    internal CanvasSharedInkInput? FreehandInput => _freehandInput;
    internal ICuiActionDispatcher? FreehandCommands => _freehandBindings;

    private void InitializeFreehandMount()
    {
        _freehandBindings = new(this);
        var inputRegistry = new CuiControlRegistry();
        inputRegistry.RegisterObjectRenderer("shared.ink.input", component =>
        {
            if (component.Name != "present-freehand-input" || _freehandInput is not null)
                throw new InvalidDataException("Present freehand input must bind exactly once.");
            if (_sharedInkEngine is null) return new Control();
            _freehandInput = new(_sharedInkEngine, CaptureFreehandOwner, CommitFreehandStrokeAsync)
            { PaintPreparedScene = false, IsEnabled = false, EraserEnabled = false };
            _freehandInput.Changed += OnFreehandChanged;
            return _freehandInput;
        });
        _freehandToolsLoader = new();
        _freehandInputLoader = new(inputRegistry);
        _freehandToolsRoot = LoadFreehandCui(_freehandToolsLoader, "Tools", "PresentFreehandTools.cui");
        _freehandInputRoot = LoadFreehandCui(_freehandInputLoader, "Input", "PresentFreehandInput.cui");
        _sharedInkResources.Register(_route.FreehandToolsHost, _freehandToolsRoot);
        _sharedInkResources.Register(_route.FreehandInputHost, _freehandInputRoot);
        Scene.LayoutUpdated += UpdateFreehandLayout;
    }
    private Control LoadFreehandCui(CuiControlLoader loader, string surface, string filename)
    {
        using var stream = typeof(PresentPage).Assembly.GetManifestResourceStream("Haven.Desktop.Present." + filename)
            ?? throw new InvalidDataException("The Present freehand CUI resource is unavailable.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser(); var document = parser.Parse(reader.ReadToEnd(), filename);
        if (parser.Diagnostics.Diagnostics.Any(value => value.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Present freehand CUI resource is invalid.");
        loader.SetSurface("Freehand" + surface, "present"); loader.SetBindingContext(_freehandBindings!); loader.SetActionDispatcher(_freehandBindings!);
        var loaded = loader.TryLoad(document);
        if (loaded.Root is null || loaded.Diagnostics.Any(value => value.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Present freehand CUI could not be materialized.");
        loader.WireBindings(loaded.Root); return loaded.Root;
    }

    private CanvasSharedInkOwnerSnapshot? CaptureFreehandOwner()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed || _originalPresentWork.IsRetiring || !CanAdmitOriginalPresentation || _closePreparing || _busy ||
            Volatile.Read(ref _saveRunning) != 0 || _playback is not null || _editor is null || _editor.IsLiveTextEditActive ||
            !ReferenceEquals(_freehandEditor, _editor) || !ReferenceEquals(_freehandDocument, Document) || Document is not { } document)
            return null;
        var slide = document.Slides.SingleOrDefault(value => value.Id == _editor.Selection.SlideId);
        if (slide is null) return null;
        // Existing objects remain painted by the ordered slide renderer with source LayerId/placement
        // intact. The pointer owner captures only same-slide-layer ink; the explicit eraser capability
        // guard keeps foreign-layer payloads visible and unmodified rather than rebinding them silently.
        var inkElements = slide.Elements.Where(value => value.Kind == PresentElementKind.SharedObject &&
            value.SharedProductivityBundle is not null && _sharedInkEngine is not null).Where(value =>
            {
                try
                {
                    var objects = _sharedInkEngine!.ParseBundle(value.SharedProductivityBundle!.Value.GetRawText()).Objects;
                    return objects.Count == 1 && objects[0].ObjectId == value.Id && objects[0].ObjectType == "drawing.ink" &&
                        objects[0].Content.TryGetProperty("LayerId", out var sourceLayer) &&
                        sourceLayer.TryGetGuid(out var layerId) && layerId == slide.Id;
                }
                catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException or ArgumentException or
                    NotSupportedException or OverflowException or InvalidOperationException)
                { return false; }
            }).ToArray();
        var bundles = inkElements.Select(value => value.SharedProductivityBundle!.Value.Clone()).ToArray();
        var context = new HomeProductivityContext("present", document.Id.ToString("D"), document.Version,
            inkElements.Select(value => value.Id).ToArray(),
            new HashSet<string>(StringComparer.Ordinal) { "drawing.ink" });
        var height = Math.Max(540, 960 * document.SlideSize.HeightInches / document.SlideSize.WidthInches);
        var width = height * document.SlideSize.WidthInches / document.SlideSize.HeightInches;
        return new(context, slide.Id, string.Join(":", _editGeneration.ToString(CultureInfo.InvariantCulture),
            _freehandOwnerEpoch.ToString(CultureInfo.InvariantCulture), _freehandViewportEpoch.ToString(CultureInfo.InvariantCulture),
            FreehandSelectionSignature()), bundles)
        { LogicalWidth = width, LogicalHeight = height };
    }

    internal ValueTask<CanvasSharedInkCommitResult> CommitFreehandStrokeAsync(CanvasSharedInkStrokeRequest request,
        CancellationToken token) => new(RunOriginalPresentAsync(async () =>
    {
        token.ThrowIfCancellationRequested();
        var current = CaptureFreehandOwner();
        if (!_freehandEnabled || _sharedInkEngine is null || current is null || !CanvasSharedInkStrokeFactory.SameOwner(request.Expected, current))
            return new CanvasSharedInkCommitResult(false, "RevisionConflict", "The original presentation, slide or drawing view changed; no stroke was added.");
        var owner = _editor!; var document = owner.Document;
        if (request.Erasure is { } erasure)
            return await CommitFreehandErasureAsync(owner, document, current, request, erasure, token);
        if (FreehandSharedObjectViewCount(owner.SelectedSlide) >= MaximumSharedInkViews)
            return new CanvasSharedInkCommitResult(false, "DrawingLimitReached", "This slide has reached the 32 shared-object view limit; remove a shared object before adding another.");
        var bundle = CanvasSharedInkStrokeFactory.CaptureBundle(_sharedInkEngine, current.Context, request.Bundle);
        var parsed = _sharedInkEngine.ParseBundle(bundle.GetRawText());
        if (parsed.Objects.Count != 1 || !parsed.Objects[0].Content.TryGetProperty("LayerId", out var layer) ||
            !layer.TryGetGuid(out var layerId) || layerId != current.LayerId)
            return new CanvasSharedInkCommitResult(false, "LayerConflict", "The stroke must belong to the original selected slide.");
        // Genuine native bounds and raster capability are verified before any editor mutation.
        var scene = CanvasSharedInkStrokeFactory.RenderScene(_sharedInkEngine, current.Context, current.LayerId, [bundle]);
        var observed = CaptureFreehandOwner();
        if (!ReferenceEquals(_editor, owner) || !ReferenceEquals(Document, document) || observed is null ||
            !CanvasSharedInkStrokeFactory.SameOwner(current, observed))
            return new CanvasSharedInkCommitResult(false, "RevisionConflict", "The current slide changed while rendering the captured stroke.");
        SetBusy(true);
        try
        {
            var insertion = owner.TryInsertPreparedSharedObject(document.Id, document.Version, current.LayerId, bundle,
                new CanvasPresentInkBridge(_sharedInkEngine, document.Id, document.Version),
                scene.X / current.LogicalWidth, scene.Y / current.LogicalHeight,
                scene.Width / current.LogicalWidth, scene.Height / current.LogicalHeight);
            if (!insertion.Inserted) return new CanvasSharedInkCommitResult(false, insertion.Code, insertion.Message);
            var saved = await SaveAsync("Freehand drawing stroke", token);
            PublishOriginalPresentSource(() => _bus.Fire("Present.Drawing.Changed"));
            return new CanvasSharedInkCommitResult(true, saved ? "Saved" : "PreparedUnsaved", saved
                ? "The stroke was saved by the original presentation repository."
                : "The stroke remains in Present's undo history; durable save is still required.");
        }
        finally { SetBusy(false); }
    }));

    private async Task<CanvasSharedInkCommitResult> CommitFreehandErasureAsync(PresentEditor owner, PresentDocument document,
        CanvasSharedInkOwnerSnapshot current, CanvasSharedInkStrokeRequest request, CanvasSharedInkEraseGesture erasure,
        CancellationToken token)
    {
        if (request.Bundle.ValueKind != JsonValueKind.Undefined || request.ReplacementBundles is not { } proposed)
            return new(false, "InvalidErasure", "The shared eraser requires a validated whole-stroke replacement set.");
        IReadOnlyList<JsonElement> verified;
        try { verified = CanvasSharedInkStrokeFactory.CaptureWholeErase(_sharedInkEngine!, current, erasure.Samples, erasure.Width).ReplacementBundles!; }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or ArgumentException)
        { return new(false, "InvalidErasure", error.Message); }
        if (verified.Count != proposed.Count || !verified.Zip(proposed).All(pair => JsonElement.DeepEquals(pair.First, pair.Second)))
            return new(false, "InvalidErasure", "The proposed erase no longer matches the original Rnote whole-stroke result.");
        _ = CanvasSharedInkStrokeFactory.RenderScene(_sharedInkEngine!, current.Context, current.LayerId, verified);
        token.ThrowIfCancellationRequested();
        var observed = CaptureFreehandOwner();
        if (!ReferenceEquals(_editor, owner) || !ReferenceEquals(Document, document) || observed is null ||
            !CanvasSharedInkStrokeFactory.SameOwner(current, observed))
            return new(false, "RevisionConflict", "The selected slide or drawing changed while preparing the erase.");

        SetBusy(true);
        try
        {
            InvalidateFreehandEraseCheck();
            if (FreehandErasePlacementBlocker() is { } placementBlocker)
                return new CanvasSharedInkCommitResult(false, "ErasePlacementUnsupported", placementBlocker);
            token.ThrowIfCancellationRequested();
            var result = owner.TryApplySharedInkErase(document.Id, document.Version, current.LayerId,
                current.Bundles, verified, new CanvasPresentInkBridge(_sharedInkEngine!, document.Id, document.Version));
            if (!result.Changed) return new CanvasSharedInkCommitResult(false, result.Code, result.Message);
            var saved = await SaveAsync("Freehand erase", token);
            PublishOriginalPresentSource(() => _bus.Fire("Present.Drawing.Changed"));
            return new CanvasSharedInkCommitResult(true, saved ? "Saved" : "PreparedUnsaved", saved
                ? "The erased strokes were saved by the original presentation repository; Undo restores them."
                : result.Message);
        }
        finally { SetBusy(false); }
    }

    private string FreehandSelectionSignature() => _editor is null ? "" : _editor.Selection.SlideId.ToString("D") + ":" +
        string.Join(",", _editor.Selection.ElementIds.Order().Select(value => value.ToString("D")));

    private void InvalidateFreehandEraseCheck() => _freehandEraseCheckResolved = false;

    private string? FreehandErasePlacementBlocker()
    {
        if (_editor is not { } editor || _sharedInkEngine is not { } engine || Document is not { } document ||
            !ReferenceEquals(editor.Document, document))
            return "The original Present ink owner is unavailable; no existing drawing was changed.";
        var slide = editor.SelectedSlide;
        if (_freehandEraseCheckResolved && ReferenceEquals(_freehandEraseCheckEditor, editor) &&
            _freehandEraseCheckSlideId == slide.Id && _freehandEraseCheckVersion == document.Version &&
            _freehandEraseCheckGeneration == _editGeneration)
            return _freehandEraseBlocker;

        var height = Math.Max(540, 960 * document.SlideSize.HeightInches / document.SlideSize.WidthInches);
        var width = height * document.SlideSize.WidthInches / document.SlideSize.HeightInches;
        var context = new HomeProductivityContext("present", document.Id.ToString("D"), document.Version, [],
            new HashSet<string>(StringComparer.Ordinal) { "drawing.ink" });
        string? blocker = null;
        foreach (var element in slide.Elements.Where(value => value.Kind == PresentElementKind.SharedObject &&
            value.SharedProductivityBundle is not null))
        {
            try
            {
                var payload = element.SharedProductivityBundle!.Value;
                var objects = engine.ParseBundle(payload.GetRawText()).Objects;
                if (objects.Count == 0 || objects.All(value => value.ObjectType != "drawing.ink")) continue;
                if (objects.Count != 1 || objects[0].ObjectId != element.Id || objects[0].ObjectType != "drawing.ink" ||
                    !objects[0].Content.TryGetProperty("LayerId", out var layer) || !layer.TryGetGuid(out var layerId))
                {
                    blocker = "Whole-stroke erasing is unavailable for this shared ink bundle shape; its original payload is retained.";
                    break;
                }
                var scene = CanvasSharedInkStrokeFactory.RenderScene(engine,
                    context with { SelectedObjectIds = [objects[0].ObjectId] }, layerId, [payload]);
                blocker = ResolveFreehandErasePlacementBlocker(element, slide.Id, layerId, scene, width, height);
                if (blocker is not null) break;
            }
            catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException or
                ArgumentException or NotSupportedException or OverflowException or InvalidOperationException)
            {
                blocker = "Whole-stroke erasing is unavailable because a shared drawing cannot be mapped by the existing Home/Rnote renderer; its original payload is retained.";
                break;
            }
        }
        _freehandEraseCheckEditor = editor;
        _freehandEraseCheckSlideId = slide.Id;
        _freehandEraseCheckVersion = document.Version;
        _freehandEraseCheckGeneration = _editGeneration;
        _freehandEraseBlocker = blocker;
        _freehandEraseCheckResolved = true;
        return blocker;
    }

    /// <summary>WholeStrokeInput only supplies page-space samples and one source layer. Confirm that
    /// the visible Present element is still the identity placement of this exact Home/Rnote scene;
    /// until its owner contract carries per-object inverse transforms, do not erase from guessed coordinates.</summary>
    internal static string? ResolveFreehandErasePlacementBlocker(PresentElement element, Guid slideId,
        Guid sourceLayerId, CanvasSharedInkScene sourceScene, double logicalWidth, double logicalHeight)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(sourceScene);
        if (sourceLayerId != slideId)
            return "Whole-stroke erasing is unavailable because this canonical drawing retains a different source LayerId; its payload and identity are preserved.";
        if (!double.IsFinite(logicalWidth) || !double.IsFinite(logicalHeight) || logicalWidth <= 0 || logicalHeight <= 0 ||
            !double.IsFinite(sourceScene.X) || !double.IsFinite(sourceScene.Y) || !double.IsFinite(sourceScene.Width) ||
            !double.IsFinite(sourceScene.Height) || sourceScene.Width <= 0 || sourceScene.Height <= 0)
            return "Whole-stroke erasing is unavailable because the original drawing bounds are invalid; its payload is retained.";
        if (element.RotationDegrees != 0)
            return "Whole-stroke erasing is unavailable after this drawing was rotated; the shared input owner has no inverse placement transform.";
        var expectedX = sourceScene.X / logicalWidth;
        var expectedY = sourceScene.Y / logicalHeight;
        var expectedWidth = sourceScene.Width / logicalWidth;
        var expectedHeight = sourceScene.Height / logicalHeight;
        const double tolerance = 0.000001;
        if (Math.Abs(element.X - expectedX) > tolerance || Math.Abs(element.Y - expectedY) > tolerance ||
            Math.Abs(element.Width - expectedWidth) > tolerance || Math.Abs(element.Height - expectedHeight) > tolerance)
            return "Whole-stroke erasing is unavailable after this drawing was moved or resized; the shared input owner has no inverse placement transform.";
        return null;
    }

    private int FreehandSharedObjectViewCount(PresentSlide? slide = null) =>
        (slide ?? _editor?.SelectedSlide)?.Elements.Count(value => value.Kind == PresentElementKind.SharedObject) ?? 0;

    private void RefreshFreehandMount()
    {
        if (_disposed || _originalPresentWork.IsRetiring || _refreshingFreehand) return;
        _refreshingFreehand = true;
        try
        {
            if (!ReferenceEquals(_freehandEditor, _editor) || !ReferenceEquals(_freehandDocument, Document))
            { _freehandEditor = _editor; _freehandDocument = Document; ++_freehandOwnerEpoch; }
            var selection = FreehandSelectionSignature();
            if (_freehandSelectionSignature != selection) { _freehandSelectionSignature = selection; ++_freehandOwnerEpoch; }
            if (_freehandInput is not null)
            {
                if (_freehandEnabled)
                {
                    var eraseBlocker = FreehandErasePlacementBlocker();
                    _freehandInput.EraserEnabled = eraseBlocker is null;
                    if (eraseBlocker is not null && _freehandInput.EraseMode) _freehandInput.EraseMode = false;
                }
                _freehandInput.IsEnabled = _freehandEnabled && _playback is null && !_busy && !_closePreparing;
                _freehandInput.Refresh();
            }
            _route.FreehandInputHost.SetValue(HavenProperties.Visibility,
                _freehandEnabled && _freehandInput?.CanDraw == true && _playback is null ? HavenVisibility.Visible : HavenVisibility.Collapsed);
            _freehandBindings?.Notify();
        }
        finally { _refreshingFreehand = false; }
    }
    private void OnFreehandChanged(object? sender, EventArgs args)
    {
        if (!_refreshingFreehand) RefreshFreehandMount();
        _freehandBindings?.Notify();
    }
    private void UpdateFreehandLayout(object? sender, EventArgs args)
    {
        if (_disposed || _originalPresentWork.IsRetiring || _freehandInputRoot is null) return;
        var paper = _route.SlideCanvas.PaperBounds; var canvas = _route.SlideCanvas.Bounds; var parent = _route.CanvasOverlay.Bounds;
        var bounds = new Rect(canvas.X + paper.X, canvas.Y + paper.Y, Math.Max(0, paper.Width), Math.Max(0, paper.Height));
        var changed = _freehandPaperBounds != bounds;
        if (changed)
        {
            _freehandPaperBounds = bounds;
            var host = _route.FreehandInputHost;
            host.SetValue(HavenProperties.Margin, new HavenThickness(HavenLength.Px(bounds.X - parent.X), HavenLength.Px(bounds.Y - parent.Y), HavenLength.Px(0), HavenLength.Px(0)));
            host.SetValue(HavenProperties.Width, HavenLength.Px(bounds.Width)); host.SetValue(HavenProperties.Height, HavenLength.Px(bounds.Height));
        }
        var clip = bounds;
        for (var ancestor = _route.FreehandInputHost.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (!ancestor.IsIncluded || ancestor.GetValue(HavenProperties.Visibility) != HavenVisibility.Visible) { clip = default; break; }
            if (ancestor.GetValue(HavenProperties.Clip) || ancestor.GetValue(HavenProperties.Overflow) is HavenOverflow.Clip or HavenOverflow.Scroll)
                clip = clip.Intersect(new Rect(ancestor.Bounds.X, ancestor.Bounds.Y, Math.Max(0, ancestor.Bounds.Width), Math.Max(0, ancestor.Bounds.Height)));
        }
        clip = clip.Intersect(new Rect(0, 0, Math.Max(0, Scene.Bounds.Width), Math.Max(0, Scene.Bounds.Height)));
        Geometry nativeClip = new RectangleGeometry(clip.Width <= 0 || clip.Height <= 0 ? default : new Rect(clip.X - bounds.X, clip.Y - bounds.Y, clip.Width, clip.Height));
        // Native input must leave the existing floating toolbar, inspectors and popup dismissal
        // layers reachable; their presentation remains owned by the original scene.
        var areas = new List<Rect>();
        var overlays = _route.Root.DescendantsAndSelf().Where(value => !ReferenceEquals(value, _route.FreehandInputHost) &&
            value.GetValue(HavenProperties.LayoutParticipation) == HavenLayoutParticipation.Overlay &&
            value.GetValue(HavenProperties.ZIndex) > _route.FreehandInputHost.GetValue(HavenProperties.ZIndex));
        foreach (var overlay in overlays)
        {
            var area = new Rect(overlay.Bounds.X, overlay.Bounds.Y, Math.Max(0, overlay.Bounds.Width), Math.Max(0, overlay.Bounds.Height));
            for (HavenElement? ancestor = overlay; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (!ancestor.IsIncluded || ancestor.GetValue(HavenProperties.Visibility) != HavenVisibility.Visible) { area = default; break; }
                if (ancestor.GetValue(HavenProperties.Clip) || ancestor.GetValue(HavenProperties.Overflow) is HavenOverflow.Clip or HavenOverflow.Scroll)
                    area = area.Intersect(new Rect(ancestor.Bounds.X, ancestor.Bounds.Y, Math.Max(0, ancestor.Bounds.Width), Math.Max(0, ancestor.Bounds.Height)));
            }
            area = area.Intersect(clip);
            if (area.Width <= 0 || area.Height <= 0) continue;
            var local = new Rect(area.X - bounds.X, area.Y - bounds.Y, area.Width, area.Height); areas.Add(local);
            nativeClip = new CombinedGeometry(GeometryCombineMode.Exclude, nativeClip, new RectangleGeometry(local));
        }
        var signature = clip.ToString() + ":" + string.Join(";", areas.Select(value => value.ToString()));
        if (_freehandClipSignature != signature) { _freehandClipSignature = signature; changed = true; }
        _freehandInputRoot.Clip = nativeClip;
        if (_freehandInput is not null) _freehandInput.Clip = nativeClip;
        if (changed) { ++_freehandViewportEpoch; RefreshFreehandMount(); }
    }
    private async Task DrainFreehandInputAsync()
    {
        if (_freehandInput is not null) await _freehandInput.CloseAndDrainAsync();
    }
    private void DisposeFreehandMount()
    {
        if (Scene is not null) Scene.LayoutUpdated -= UpdateFreehandLayout;
        if (_freehandInput is not null) { _freehandInput.Changed -= OnFreehandChanged; _freehandInput.Dispose(); }
        if (_freehandToolsRoot is not null) _sharedInkResources.Unregister(_route.FreehandToolsHost, _freehandToolsRoot);
        if (_freehandInputRoot is not null) _sharedInkResources.Unregister(_route.FreehandInputHost, _freehandInputRoot);
        _freehandToolsLoader?.Dispose(); _freehandInputLoader?.Dispose(); _freehandInput = null;
    }
    internal static string? FreehandColorForCommand(string command) => command switch
    {
        "present.ink.color.black" => "#FF000000", "present.ink.color.white" => "#FFFFFFFF",
        "present.ink.color.blue" => "#FF2563EB", "present.ink.color.red" => "#FFDC2626",
        "present.ink.color.green" => "#FF16A34A", "present.ink.color.yellow" => "#FFFFFF00",
        _ => null
    };
    internal static CanvasRnoteInkStyle? ResolveFreehandStyleCommand(CanvasRnoteInkStyle current, string command) => command switch
    {
        "present.ink.pen" => current.Kind == CanvasRnoteInkKind.Solid ? current : CanvasRnoteInkStyle.Default,
        "present.ink.highlighter" => current.Kind == CanvasRnoteInkKind.Marker ? current : new(CanvasRnoteInkKind.Marker, "#FFFFFF00", 12, .4),
        "present.ink.width.fine" => current with { BaseWidth = current.Kind == CanvasRnoteInkKind.Marker ? 8 : 1.5 },
        "present.ink.width.normal" => current with { BaseWidth = current.Kind == CanvasRnoteInkKind.Marker ? 12 : 3 },
        "present.ink.width.broad" => current with { BaseWidth = current.Kind == CanvasRnoteInkKind.Marker ? 20 : 6 },
        _ => null
    };
    private static string FreehandColorName(string? color) => color?.ToUpperInvariant() switch
    {
        "#FF000000" => "Black", "#FFFFFFFF" => "White", "#FF2563EB" => "Blue",
        "#FFDC2626" => "Red", "#FF16A34A" => "Green", "#FFFFFF00" => "Yellow",
        null => "Colour", _ => color ?? "Colour"
    };
    private sealed class FreehandBindings(PresentPage owner) : ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Notify() => PropertyChanged?.Invoke(this, new(null));
        private bool CanDrawInk => owner._freehandEnabled && owner._freehandInput?.CanDraw == true;
        private bool CanConfigureEraser => owner._freehandEnabled && owner._freehandInput?.CanDraw == true &&
            owner.FreehandErasePlacementBlocker() is null;
        private bool CanConfigurePenTool => CanDrawInk && owner.FreehandSharedObjectViewCount() < MaximumSharedInkViews;
        private bool CanConfigureStroke => owner._freehandInput?.EraseMode == true ? CanConfigureEraser : CanConfigurePenTool;
        private bool CanRecolor => !owner._freehandEnabled && owner.EditableSelectedSharedInk() is not null;
        public bool TryGetValue(string path, out object? value)
        {
            value = path switch
            {
                "DrawLabel" => owner._freehandEnabled ? "Done" : owner._freehandInput?.LastError is not null ? "Retry" : "Draw",
                "Drawing" => owner._freehandEnabled,
                "CanToggle" => IsActionAvailable("present.ink.toggle") == true,
                "CanDraw" => owner._freehandInput?.CanDraw == true,
                "CanOpenTools" => CanConfigureStroke || CanConfigureEraser || CanRecolor,
                "CanConfigureStroke" => CanConfigureStroke,
                "CanConfigurePenTool" => CanConfigurePenTool,
                "CanConfigureEraser" => CanConfigureEraser,
                "EraserDescription" => owner._freehandEnabled
                    ? owner.FreehandErasePlacementBlocker() ?? "Erases complete shared Rnote strokes intersected by the gesture; Undo restores erased strokes."
                    : "Start drawing mode to erase whole shared Rnote strokes.",
                "CanChooseColor" => CanConfigurePenTool || CanRecolor,
                "ToolLabel" => owner._freehandEnabled ? owner._freehandInput?.EraseMode == true ? "Eraser" :
                    owner._freehandInput?.Style.Kind == CanvasRnoteInkKind.Marker ? "Marker" : "Pen" : "Ink",
                "ColorLabel" => "Colour · " + FreehandColorName(owner._freehandEnabled ? owner._freehandInput?.Style.Color : owner.SelectedSharedInkColor()),
                "WidthLabel" => owner._freehandInput?.EraseMode == true
                    ? "Eraser width · " + (owner._freehandInput?.EraserWidth.ToString("0.#", CultureInfo.InvariantCulture) ?? "—")
                    : "Thickness · " + (owner._freehandInput?.Style.BaseWidth.ToString("0.#", CultureInfo.InvariantCulture) ?? "—"),
                "ColorTargetHint" => owner._freehandEnabled ? owner._freehandInput?.EraseMode == true
                    ? "Erase complete strokes from this slide's shared Rnote drawing."
                    : owner.FreehandErasePlacementBlocker() ?? "Choose the colour for new strokes."
                    : "Change the selected drawing's colour.",
                "Status" => owner._freehandEnabled && owner.FreehandErasePlacementBlocker() is { } eraseBlocker ? eraseBlocker :
                    owner.FreehandSharedObjectViewCount() >= MaximumSharedInkViews
                    ? "This slide has reached the 32 shared-object view limit. Remove a shared object before adding another ink object."
                    : owner._freehandInput?.Status ?? "Drawing is unavailable because the original Home ink owner is not connected.",
                _ => null
            };
            return path is "DrawLabel" or "Drawing" or "CanToggle" or "CanDraw" or "CanOpenTools" or "CanConfigureStroke" or
                "CanConfigurePenTool" or "CanConfigureEraser" or "EraserDescription" or "CanChooseColor" or "ToolLabel" or "ColorLabel" or "WidthLabel" or "ColorTargetHint" or "Status";
        }
        public bool? IsActionAvailable(string command) => !owner._disposed && !owner._originalPresentWork.IsRetiring &&
            owner.CanAdmitOriginalPresentation && !owner._busy && !owner._closePreparing && Volatile.Read(ref owner._saveRunning) == 0 &&
            owner._playback is null && (command switch
            {
                "present.ink.toggle" => owner._freehandEnabled || owner._freehandInput?.CanDraw == true ||
                    (owner.Document is not null && owner._freehandInput?.LastError is not null),
                "present.ink.tool" or "present.ink.pen" or "present.ink.highlighter" => CanConfigurePenTool,
                "present.ink.eraser" => CanConfigureEraser,
                "present.ink.width.fine" or "present.ink.width.normal" or "present.ink.width.broad" => CanConfigureStroke,
                _ => FreehandColorForCommand(command) is not null && (CanConfigurePenTool || CanRecolor)
            });
        public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            Dispatcher.UIThread.VerifyAccess(); cancellationToken.ThrowIfCancellationRequested();
            if (parameter is not null) throw new ArgumentException("Use the current Present drawing target.");
            if (IsActionAvailable(command) != true) return;
            if (FreehandColorForCommand(command) is { } color && !owner._freehandEnabled)
            {
                await owner.RecolorSelectedSharedInkAsync(color, cancellationToken);
                return;
            }
            owner.RunOriginalPresentEvent(() =>
            {
                if (command == "present.ink.toggle")
                {
                    if (owner._freehandEnabled) owner._freehandEnabled = false;
                    else
                    {
                        owner._freehandInput?.Refresh(retryFailedRender: true);
                        owner._freehandEnabled = owner._freehandInput?.CanDraw == true;
                        if (!owner._freehandEnabled) owner.PublishOriginalPresentSource(() => owner._route.SetStatus(owner._freehandInput?.Status ?? "The original Home ink renderer is unavailable."));
                    }
                }
                else if (owner._freehandInput is { } input)
                {
                    if (command == "present.ink.eraser") input.EraseMode = true;
                    else if (command is "present.ink.pen" or "present.ink.highlighter" or "present.ink.tool") input.EraseMode = false;
                    var styleCommand = command == "present.ink.tool"
                        ? input.Style.Kind == CanvasRnoteInkKind.Marker ? "present.ink.pen" : "present.ink.highlighter" : command;
                    if (ResolveFreehandStyleCommand(input.Style, styleCommand) is { } style)
                    {
                        input.Style = style;
                        if (command.StartsWith("present.ink.width.", StringComparison.Ordinal))
                            input.EraserWidth = Math.Clamp(style.BaseWidth * 4, 1, 500);
                    }
                    else if (FreehandColorForCommand(command) is { } strokeColor) input.Style = input.Style with { Color = strokeColor };
                }
                owner.RefreshFreehandMount();
                if (owner._freehandEnabled) owner._freehandInput?.Focus();
            });
            Notify();
        }
    }
}
