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
    internal CanvasSharedInkInput? FreehandInput => _freehandInput;

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
            { PaintPreparedScene = false, IsEnabled = false };
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
        if (slide is null || slide.Elements.Count(value => value.Kind == PresentElementKind.SharedObject) >= MaximumSharedInkViews) return null;
        // Existing objects keep their original source LayerId/placement and are painted by the
        // existing ordered slide renderer. This transparent input owns only the new stroke; the
        // original document/editor references and mutation epoch bind all current slide content.
        JsonElement[] bundles = [];
        var context = new HomeProductivityContext("present", document.Id.ToString("D"), document.Version,
            slide.Elements.Where(value => value.Kind == PresentElementKind.SharedObject).Select(value => value.Id).ToArray(),
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

    private string FreehandSelectionSignature() => _editor is null ? "" : _editor.Selection.SlideId.ToString("D") + ":" +
        string.Join(",", _editor.Selection.ElementIds.Order().Select(value => value.ToString("D")));

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
    private sealed class FreehandBindings(PresentPage owner) : ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Notify() => PropertyChanged?.Invoke(this, new(null));
        public bool TryGetValue(string path, out object? value)
        {
            value = path switch
            {
                "DrawLabel" => owner._freehandEnabled ? "Done" : owner._freehandInput?.LastError is not null ? "Retry" : "Draw",
                "Drawing" => owner._freehandEnabled,
                "CanToggle" => IsActionAvailable("present.ink.toggle") == true,
                "CanDraw" => owner._freehandInput?.CanDraw == true,
                "ToolLabel" => owner._freehandInput?.Style.Kind == CanvasRnoteInkKind.Marker ? "Highlighter" : "Pen",
                "Status" => owner._editor?.SelectedSlide.Elements.Count(value => value.Kind == PresentElementKind.SharedObject) >= MaximumSharedInkViews
                    ? "This slide has reached this view's 32 shared drawing-object limit; its original content is retained."
                    : owner._freehandInput?.Status ?? "Drawing is unavailable because the original Home ink owner is not connected.",
                _ => null
            };
            return path is "DrawLabel" or "Drawing" or "CanToggle" or "CanDraw" or "ToolLabel" or "Status";
        }
        public bool? IsActionAvailable(string command) => !owner._disposed && !owner._originalPresentWork.IsRetiring &&
            !owner._busy && !owner._closePreparing && owner._playback is null && (command switch
            {
                "present.ink.toggle" => owner._freehandEnabled || owner._freehandInput?.CanDraw == true ||
                    (owner.Document is not null && owner._freehandInput?.LastError is not null),
                "present.ink.tool" => owner._freehandEnabled && owner._freehandInput?.CanDraw == true,
                _ => false
            });
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            Dispatcher.UIThread.VerifyAccess(); cancellationToken.ThrowIfCancellationRequested();
            if (parameter is not null) throw new ArgumentException("Use the current Present drawing target.");
            if (IsActionAvailable(command) != true) return ValueTask.CompletedTask;
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
                else if (command == "present.ink.tool" && owner._freehandInput is { } input)
                    input.Style = input.Style.Kind == CanvasRnoteInkKind.Marker ? CanvasRnoteInkStyle.Default : new(CanvasRnoteInkKind.Marker, "#FFFFFF00", 12, .4);
                owner.RefreshFreehandMount();
                if (owner._freehandEnabled) owner._freehandInput?.Focus();
            });
            return ValueTask.CompletedTask;
        }
    }
}
