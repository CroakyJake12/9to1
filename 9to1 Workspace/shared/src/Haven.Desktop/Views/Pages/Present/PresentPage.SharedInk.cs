using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using HavenOS.Apps.Canvas;
using HavenOS.Home.Core;

namespace Haven.Desktop.Views.Pages.Present;

public sealed partial class PresentPage
{
    private const int MaximumSharedInkViews = 32;
    private const long MaximumSharedInkRasterBytes = 32 * 1024 * 1024;
    private readonly PresentSharedInkResources _sharedInkResources = new();
    private readonly ContentControl _sharedInkPreview = new() { IsHitTestVisible = false,
        HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly List<(CanvasPresentInkObjectSession Session, Exception Cause)> _retainedSharedInkRetirements = [];
    private readonly HomeProductivityEngine? _sharedInkEngine;
    private Dictionary<Guid, SharedInkEntry> _sharedInkEntries = [];
    private PresentEditor? _sharedInkOwner;
    private CancellationTokenSource? _sharedInkLifetime;
    private bool _refreshingSharedInk, _sharedInkRefreshRequested;
    private Rect? _lastSharedInkClip;
    internal CanvasPresentInkViewState SharedInkRenderState { get; private set; } =
        new(false, "NoSharedDrawing", "This presentation has no shared drawing.");
    internal Exception? LastSharedInkError { get; private set; }

    private sealed record SharedInkEntry(JsonElement Payload, CanvasPresentInkObjectSession Session);

    private void InitializeSharedInkMount()
    {
        _sharedInkResources.Register(_route.SharedInkPreviewHost, _sharedInkPreview);
        Scene.LayoutUpdated += UpdateSharedInkNativeClip;
        _route.SlideCanvas.SharedObjectImageSource = _sharedInkResources.SourceFor;
        _route.PlaybackCanvas.SharedObjectImageSource = _sharedInkResources.SourceFor;
        _route.PresenterCanvas.SharedObjectImageSource = _sharedInkResources.SourceFor;
        var thumbnail = new PresentSlideCanvas { SharedObjectImageSource = _sharedInkResources.SourceFor };
        _route.SlideNavigator.SetPreviewRenderer(thumbnail.DrawPreview);
    }

    // Mirror the existing scene's rectangular ancestor clips for this low-level native child.
    // In particular, scrolling the Design panel must never let its CUI object spill across the slide.
    private void UpdateSharedInkNativeClip(object? sender, EventArgs args)
    {
        var host = _route.SharedInkPreviewHost;
        var bounds = host.Bounds;
        var clip = new Rect(bounds.X, bounds.Y, Math.Max(0, bounds.Width), Math.Max(0, bounds.Height));
        for (var parent = host.Parent; parent is not null; parent = parent.Parent)
        {
            if (!parent.IsIncluded || parent.GetValue(HavenProperties.Visibility) != HavenVisibility.Visible)
            { clip = default; break; }
            if (parent.GetValue(HavenProperties.Clip) || parent.GetValue(HavenProperties.Overflow) is HavenOverflow.Clip or HavenOverflow.Scroll)
                clip = clip.Intersect(new Rect(parent.Bounds.X, parent.Bounds.Y, Math.Max(0, parent.Bounds.Width), Math.Max(0, parent.Bounds.Height)));
        }
        clip = clip.Intersect(new Rect(0, 0, Math.Max(0, Scene.Bounds.Width), Math.Max(0, Scene.Bounds.Height)));
        var local = clip.Width <= 0 || clip.Height <= 0 ? default(Rect) : new Rect(clip.X - bounds.X, clip.Y - bounds.Y, clip.Width, clip.Height);
        if (_lastSharedInkClip == local) return;
        _lastSharedInkClip = local;
        _sharedInkPreview.Clip = new RectangleGeometry(local);
    }

    // Called by the original Present render/update body, never by Draw or the native image resolver.
    // Current-slide objects take priority; other slide thumbnails borrow the same prepared resources.
    private void RefreshSharedInkMount()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed || _originalPresentWork.IsRetiring || _editor is null) return;
        if (_refreshingSharedInk) { _sharedInkRefreshRequested = true; return; }
        _refreshingSharedInk = true;
        try
        {
            var owner = _editor;
            var sourceDocument = owner.Document;
            var generation = _editGeneration;
            if (!ReferenceEquals(_sharedInkOwner, owner))
            {
                DetachSharedInkOwner(); _sharedInkOwner = owner;
                _sharedInkLifetime = new CancellationTokenSource();
            }
            _sharedInkPreview.Content = null;
            _route.SharedInkPreviewHost.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
            _sharedInkResources.Bind(null, new Dictionary<Guid, SharedInkEntry>());
            var next = new Dictionary<Guid, SharedInkEntry>();
            var wanted = owner.Document.Slides.OrderBy(slide => slide.Id == owner.Selection.SlideId ? 0 : 1)
                .SelectMany(slide => slide.Elements).Where(element => element.Kind == PresentElementKind.SharedObject).ToArray();
            var unavailable = 0; var ready = 0; long rasterBytes = 0;
            var budgetLimited = false;
            foreach (var element in wanted)
            {
                if (_sharedInkEngine is null || element.SharedProductivityBundle is not { } payload)
                { ++unavailable; continue; }
                if (next.Count >= MaximumSharedInkViews || rasterBytes >= MaximumSharedInkRasterBytes)
                { ++unavailable; budgetLimited = true; continue; }
                SharedInkEntry? entry = null;
                try
                {
                    if (_sharedInkEntries.TryGetValue(element.Id, out var retained) && JsonElement.DeepEquals(retained.Payload, payload))
                    { entry = retained; entry.Session.Refresh(); }
                    else
                    {
                        var session = new CanvasPresentInkObjectSession(owner, element.Id, _sharedInkEngine,
                            () => IsSharedInkOwnerCurrent(owner), _sharedInkLifetime!.Token,
                            (request, token) => ApplySharedInkOwnerEditAsync(owner, request, token));
                        entry = new(payload.Clone(), session);
                    }
                    if (entry.Session.RasterByteCount > MaximumSharedInkRasterBytes - rasterBytes)
                    {
                        RetireSharedInkView(entry.Session); ++unavailable; budgetLimited = true; continue;
                    }
                    next.Add(element.Id, entry);
                    rasterBytes += entry.Session.RasterByteCount;
                    if (entry.Session.State.Ready && entry.Session.TryBorrowRasterImage(out _)) ++ready;
                    else { ++unavailable; LastSharedInkError = entry.Session.LastError; }
                }
                catch (Exception error)
                {
                    LastSharedInkError = error; ++unavailable;
                    if (entry is not null && !_sharedInkEntries.Values.Any(old => ReferenceEquals(old.Session, entry.Session)))
                        RetireSharedInkView(entry.Session);
                }
            }
            if (!IsSharedInkOwnerCurrent(owner) || !ReferenceEquals(sourceDocument, owner.Document) || generation != _editGeneration)
            {
                foreach (var abandoned in next.Values.Where(candidate => !_sharedInkEntries.Values.Any(old => ReferenceEquals(old.Session, candidate.Session))))
                    RetireSharedInkView(abandoned.Session);
                _sharedInkRefreshRequested = true;
                return;
            }
            foreach (var retired in _sharedInkEntries.Values.Where(old => !next.Values.Any(current => ReferenceEquals(old.Session, current.Session))))
                RetireSharedInkView(retired.Session);
            _sharedInkEntries = next;
            _sharedInkResources.Bind(owner, next);
            if (owner.Selection.ElementIds.Count == 1 && next.TryGetValue(owner.Selection.ElementIds[0], out var selected) &&
                selected.Session.State.Ready && selected.Session.TryBorrowRasterImage(out _))
            {
                // Only this selected CUI tree is mounted as a native child; slide pixels use the ordinary ordered draw commands.
                _sharedInkPreview.Content = selected.Session.View;
                _route.SharedInkPreviewHost.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
            }
            var retainedProperties = next.Values.Sum(entry => entry.Session.RetainedUnsupportedProperties.Count);
            SharedInkRenderState = wanted.Length == 0 ? new(false, "NoSharedDrawing", "This presentation has no shared drawing.")
                : unavailable == 0 && retainedProperties > 0 ? new(true, "RenderedWithRetainedProperties",
                    $"{ready} shared drawing(s) rendered read-only; {retainedProperties} canonical property path(s) are retained but not rendered. Use Draw to add strokes when the shared renderer is available.")
                : unavailable == 0 ? new(true, "RenderedReadOnly", $"{ready} shared drawing(s) rendered through Home/Rnote. Use Draw to add strokes when the shared renderer is available.")
                : new(false, budgetLimited ? "SharedInkRenderBudgetExceeded" : _sharedInkEngine is null ? "HomeInkOwnerUnavailable" : "SharedInkRenderUnavailable",
                    $"{ready}/{wanted.Length} shared drawing(s) rendered read-only; {unavailable} unavailable. " +
                    (budgetLimited ? "This view retains at most 32 drawings/32 MiB; current-slide drawings take priority. " : string.Empty) +
                    "Canonical payloads are retained. Draw adds new strokes only when the shared renderer is available.");
        }
        finally
        {
            _refreshingSharedInk = false;
            if (_sharedInkRefreshRequested)
            {
                _sharedInkRefreshRequested = false;
                Dispatcher.UIThread.Post(() => RunOriginalPresentEvent(() => { RefreshSharedInkMount(); PublishSharedInkState(); }));
            }
        }
    }

    private bool IsSharedInkOwnerCurrent(PresentEditor owner) => !_disposed && !_originalPresentWork.IsRetiring &&
        ReferenceEquals(_editor, owner) && ReferenceEquals(Document, owner.Document);

    private ValueTask<PresentSharedObjectUpdateResult> ApplySharedInkOwnerEditAsync(PresentEditor owner,
        CanvasPresentInkEditRequest request, CancellationToken token) => new(RunOriginalPresentAsync(async () =>
    {
        token.ThrowIfCancellationRequested();
        if (!IsSharedInkOwnerCurrent(owner) || _closePreparing)
            return new PresentSharedObjectUpdateResult(false, "OwnerUnavailable", "The original Present owner is no longer current.");
        if (_busy || Volatile.Read(ref _saveRunning) != 0)
            return new PresentSharedObjectUpdateResult(false, "OwnerBusy", "Wait for the current Present operation before editing this drawing.");
        if (_sharedInkEngine is null)
            return new PresentSharedObjectUpdateResult(false, "HomeInkOwnerUnavailable", "The original Home ink engine is unavailable.");
        SetBusy(true);
        try
        {
            var result = owner.TryReplaceSharedObject(request.DocumentId, request.ExpectedDocumentVersion, request.ElementId,
                request.ExpectedBundle, request.ReplacementBundle,
                new CanvasPresentInkBridge(_sharedInkEngine, request.DocumentId, request.ExpectedDocumentVersion));
            if (!result.Changed) return result;
            var saved = await SaveAsync("Shared drawing edit", token);
            PublishOriginalPresentSource(() => _bus.Fire("Present.Drawing.Changed"));
            return result with { Code = saved ? "Saved" : "PreparedUnsaved",
                Message = saved ? "The shared drawing was saved by the original Present repository."
                    : "The drawing changed in Present's undo history; durable save is still required." };
        }
        finally { SetBusy(false); }
    }));

    // CUI recolouring uses the SAME live object session/registered Home action
    // and the original owner transaction. It never edits raster previews.
    private SharedInkEntry? EditableSelectedSharedInk()
    {
        if (_disposed || _originalPresentWork.IsRetiring || !CanAdmitOriginalPresentation || _busy || _closePreparing ||
            Volatile.Read(ref _saveRunning) != 0 || _playback is not null || _editor is not { } editor || editor.IsLiveTextEditActive ||
            !ReferenceEquals(_sharedInkOwner, editor) || editor.Selection.ElementIds.Count != 1) return null;
        var selected = editor.SelectedElements;
        if (selected.Count != 1 || selected[0].Locked || selected[0].Kind != PresentElementKind.SharedObject ||
            selected[0].SharedProductivityBundle is not { } payload || !_sharedInkEntries.TryGetValue(selected[0].Id, out var entry) ||
            !entry.Session.CanRequestEdit || !JsonElement.DeepEquals(payload, entry.Payload)) return null;
        return entry;
    }
    private string? SelectedSharedInkColor()
    {
        var entry = EditableSelectedSharedInk();
        if (entry?.Payload is not { ValueKind: JsonValueKind.Object } payload ||
            !payload.TryGetProperty("objects", out var objects) || objects.ValueKind != JsonValueKind.Array || objects.GetArrayLength() != 1 ||
            !objects[0].TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object ||
            !content.TryGetProperty("ResolvedBrushProperties", out var brush) || brush.ValueKind != JsonValueKind.Object ||
            !brush.TryGetProperty("Color", out var color) || color.ValueKind != JsonValueKind.String) return null;
        return color.GetString();
    }
    internal Task<PresentSharedObjectUpdateResult> RecolorSelectedSharedInkAsync(string color, CancellationToken token = default) =>
        RunOriginalPresentAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            if (_freehandEnabled) return new PresentSharedObjectUpdateResult(false, "DrawingInProgress", "Finish drawing before changing an existing drawing.");
            var entry = EditableSelectedSharedInk();
            if (entry is null || _editor is not { } editor)
                return new PresentSharedObjectUpdateResult(false, "SelectedDrawingUnavailable", "Select an unlocked drawing that is ready to edit.");
            if (color is null || color.Length != 9 || color[0] != '#' || !color.Skip(1).All(Uri.IsHexDigit))
                return new PresentSharedObjectUpdateResult(false, "InvalidArgument", "Choose a valid ink colour.");
            var id = editor.Selection.ElementIds.Single();
            var action = new HomeProductivityAction("ink.color", 1, "drawing.ink", [id],
                JsonSerializer.SerializeToElement(new { color = color.ToUpperInvariant() }), editor.Document.Version);
            var result = await entry.Session.RequestEditAsync(action, token);
            if (!_disposed && !_originalPresentWork.IsRetiring)
            {
                PublishOriginalPresentSource(() => _route.SetStatus(result.Message));
                RefreshFreehandMount();
            }
            return result;
        });

    private void PublishSharedInkState()
    {
        if (SharedInkRenderState.Code is not ("NoSharedDrawing" or "RenderedReadOnly"))
            PublishOriginalPresentSource(() => _route.SetStatus(SharedInkRenderState.Message));
    }

    private void RetireSharedInkView(CanvasPresentInkObjectSession session)
    {
        try { session.Dispose(); }
        catch (Exception error) { LastSharedInkError = error; _retainedSharedInkRetirements.Add((session, error)); }
    }

    private void DetachSharedInkOwner()
    {
        _sharedInkPreview.Content = null;
        _sharedInkResources.Bind(null, new Dictionary<Guid, SharedInkEntry>());
        _sharedInkLifetime?.Cancel();
        foreach (var entry in _sharedInkEntries.Values) RetireSharedInkView(entry.Session);
        _sharedInkEntries.Clear();
        _sharedInkLifetime?.Dispose(); _sharedInkLifetime = null; _sharedInkOwner = null;
        if (_route is not null) _route.SharedInkPreviewHost.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
    }

    private void DisposeSharedInkMount()
    {
        Scene.LayoutUpdated -= UpdateSharedInkNativeClip;
        DetachSharedInkOwner();
        _sharedInkResources.Unregister(_route.SharedInkPreviewHost, _sharedInkPreview);
        _route.SlideCanvas.SharedObjectImageSource = null;
        _route.PlaybackCanvas.SharedObjectImageSource = null;
        _route.PresenterCanvas.SharedObjectImageSource = null;
        if (_retainedSharedInkRetirements.Count > 0)
            throw new AggregateException("The original shared drawing views did not retire cleanly; their owners and causes are retained.",
                _retainedSharedInkRetirements.Select(failure => failure.Cause));
    }

    /// <summary>Only exact in-memory session resources and the existing packaged/avatar resolver.
    /// No file/network/data image grant and no new persistent cache or document store.</summary>
    private sealed class PresentSharedInkResources : IHavenAvaloniaImageResolver, IHavenNativeControlRegistrar
    {
        private readonly IHavenAvaloniaImageResolver _ordinary = new HavenDesktopImageResolver();
        private readonly string _prefix = "present-ink-memory:" + Guid.NewGuid().ToString("N") + ":";
        private readonly Dictionary<HavenElement, Control> _native = [];
        private IReadOnlyDictionary<Guid, SharedInkEntry> _entries = new Dictionary<Guid, SharedInkEntry>();
        private PresentEditor? _owner;
        public void Bind(PresentEditor? owner, IReadOnlyDictionary<Guid, SharedInkEntry> entries)
        { _owner = owner; _entries = entries; }
        public string? SourceFor(PresentDocument document, PresentElement element)
        {
            if (_owner is null || !ReferenceEquals(_owner.Document, document) || !_entries.TryGetValue(element.Id, out var entry) ||
                !entry.Session.TryBorrowRasterImage(out _)) return null;
            return _prefix + element.Id.ToString("N");
        }
        public bool TryResolve(string source, out IImage? image)
        {
            image = null;
            if (!source.StartsWith("present-ink-memory:", StringComparison.Ordinal)) return _ordinary.TryResolve(source, out image);
            return source.StartsWith(_prefix, StringComparison.Ordinal) && Guid.TryParseExact(source[_prefix.Length..], "N", out var id) &&
                _entries.TryGetValue(id, out var entry) && entry.Session.TryBorrowRasterImage(out image);
        }
        public void Register(HavenElement element, Control control)
        {
            ArgumentNullException.ThrowIfNull(element); ArgumentNullException.ThrowIfNull(control);
            if (!_native.TryAdd(element, control)) throw new InvalidOperationException("This original native drawing host is already registered.");
        }
        public void Unregister(HavenElement element, Control control)
        {
            if (!_native.TryGetValue(element, out var found)) return;
            if (!ReferenceEquals(found, control)) throw new InvalidOperationException("The original native drawing host was replaced.");
            _native.Remove(element);
        }
        public bool TryCreate(HavenElement element, out Control? control)
        {
            if (_native.TryGetValue(element, out var original)) { control = original; return true; }
            control = null; return false;
        }
    }
}
