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
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using HavenOS.Apps.Canvas;
using HavenOS.Home.Core;

namespace Haven.Desktop.Views.Pages.Data;

public sealed partial class DataPage
{
    private HavenSceneControl _dataScene = null!;
    private HomeProductivityEngine? _sharedInkEngine;
    private CanvasSharedInkInput? _sharedInkInput;
    private CuiControlLoader? _sharedInkLoader;
    private Control? _sharedInkCuiRoot;
    private DataSharedInkBindings? _sharedInkBindings;
    private readonly CancellationTokenSource _sharedInkLifetime = new();
    private long _sharedInkEditGeneration;
    private Rect? _sharedInkClip;
    internal CanvasSharedInkInput? SharedInkInput => _sharedInkInput;

    private void InitializeSharedInkMount(HomeProductivityEngine? engine)
    {
        try
        {
            _sharedInkEngine = engine;
            if (engine is null)
            {
                _route.SharedInkHost.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
                _dataScene = new HavenSceneControl { Name = "Scene", Root = _route.Root }; Content = _dataScene;
                return;
            }
            _sharedInkBindings = new(this);
            var registry = new CuiControlRegistry();
            registry.RegisterObjectRenderer("shared.ink.input", component =>
            {
                if (component.Name != "data-shared-ink-input" || _sharedInkInput is not null)
                    throw new InvalidDataException("The Data shared ink slot must be bound once.");
                if (engine is null) return new Control();
                _sharedInkInput = new(engine, CaptureSharedInkOwner, CommitSharedInkStroke, _sharedInkLifetime.Token) { EraserEnabled = true };
                _sharedInkInput.Changed += OnSharedInkChanged;
                return _sharedInkInput;
            });
            using var stream = typeof(DataPage).Assembly.GetManifestResourceStream("Haven.Desktop.Data.SharedInk.cui")
                ?? throw new InvalidDataException("The Data shared drawing CUI resource is unavailable.");
            using var reader = new StreamReader(stream);
            var parser = new CuiRichParser(); var document = parser.Parse(reader.ReadToEnd(), "DataSharedInk.cui");
            if (parser.Diagnostics.Diagnostics.Any(value => value.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("The Data shared drawing CUI resource is invalid.");
            _sharedInkLoader = new(registry); _sharedInkLoader.SetSurface("SharedInk", "data");
            _sharedInkLoader.SetBindingContext(_sharedInkBindings); _sharedInkLoader.SetActionDispatcher(_sharedInkBindings);
            var loaded = _sharedInkLoader.TryLoad(document);
            if (loaded.Root is null || loaded.Diagnostics.Any(value => value.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("The Data shared drawing CUI could not be materialized.");
            _sharedInkLoader.WireBindings(loaded.Root); _sharedInkCuiRoot = loaded.Root;
            _dataScene = new(new HavenDesktopImageResolver(), new DataSharedInkMount(this)) { Name = "Scene", Root = _route.Root };
            _dataScene.LayoutUpdated += OnSharedInkLayout;
            Content = _dataScene;
        }
        catch (Exception originalFailure)
        {
            try { DisposeSharedInkMount(); }
            catch (Exception cleanupFailure) { throw new AggregateException("Data shared ink construction retained source and cleanup failures.", originalFailure, cleanupFailure); }
            throw;
        }
    }

    internal CanvasSharedInkOwnerSnapshot? CaptureSharedInkOwner()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed || _busy || _saveRunning != 0 || !IsEnabled || Workbook is not { } workbook || CurrentSheet is not { } sheet)
            return null;
        var context = new HomeProductivityContext("data", workbook.Id.ToString("D"), workbook.Version,
            sheet.Drawings.Where(value => value.Kind == DataDrawingKind.SharedInk).Select(value => value.Id).ToArray(),
            new HashSet<string>(StringComparer.Ordinal) { "drawing.ink" });
        var bundles = sheet.Drawings.Where(value => value.Kind == DataDrawingKind.SharedInk).OrderBy(value => value.ZIndex)
            .Select(value => value.SharedProductivityBundle?.Clone() ?? throw new InvalidDataException("A sheet drawing lost its canonical Home bundle.")).ToArray();
        return new(context, sheet.Id, _sharedInkEditGeneration.ToString(CultureInfo.InvariantCulture) + ":" + workbook.RevisionId.ToString("D"), bundles);
    }

    internal ValueTask<CanvasSharedInkCommitResult> CommitSharedInkStroke(CanvasSharedInkStrokeRequest request, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess(); token.ThrowIfCancellationRequested();
        var current = CaptureSharedInkOwner();
        if (_sharedInkEngine is null || current is null || !CanvasSharedInkStrokeFactory.SameOwner(request.Expected, current))
            return ValueTask.FromResult(new CanvasSharedInkCommitResult(false, "RevisionConflict", "The workbook or selected sheet changed; this stroke was not added."));
        if (request.Erasure is not null)
            return ValueTask.FromResult(CommitSharedInkErasure(request, current, token));
        if (request.ReplacementBundles is not null)
            return ValueTask.FromResult(new CanvasSharedInkCommitResult(false, "InvalidStroke", "A pen stroke cannot replace the worksheet drawing."));
        var canonical = CanvasSharedInkStrokeFactory.CaptureBundle(_sharedInkEngine, current.Context, request.Bundle);
        var bundle = _sharedInkEngine.ParseBundle(canonical.GetRawText());
        if (bundle.Objects.Count != 1)
            return ValueTask.FromResult(new CanvasSharedInkCommitResult(false, "InvalidStroke", "One canonical stroke is required for worksheet insertion."));
        var value = bundle.Objects[0];
        if (!value.Content.TryGetProperty("LayerId", out var layer) || !layer.TryGetGuid(out var layerId) || layerId != current.LayerId)
            return ValueTask.FromResult(new CanvasSharedInkCommitResult(false, "LayerConflict", "The stroke targets another drawing layer."));
        var sheet = CurrentSheet!;
        if (sheet.Drawings.Any(drawing => drawing.Id == value.ObjectId))
            return ValueTask.FromResult(new CanvasSharedInkCommitResult(false, "ObjectConflict", "This stroke already exists; it was not inserted again."));
        // Native input already verified actual Rnote/raster output. This direct owner entry point also
        // validates it before mutation, so UI and typed callers share the same capability boundary.
        _ = CanvasSharedInkStrokeFactory.RenderScene(_sharedInkEngine, current.Context, current.LayerId, [.. current.Bundles, canonical]);
        token.ThrowIfCancellationRequested();
        var observed = CaptureSharedInkOwner();
        if (observed is null || !CanvasSharedInkStrokeFactory.SameOwner(current, observed))
            return ValueTask.FromResult(new CanvasSharedInkCommitResult(false, "RevisionConflict", "The selected sheet changed while rendering the stroke."));
        CaptureSpreadsheetUndo();
        new DataSheetDrawingEditor(sheet).AddSharedInk(value.ObjectId, canonical);
        _drawingIndex = sheet.Drawings.Count - 1; MarkDirty(); RenderCurrent(); _bus.Fire("Data.Drawing.Added");
        return ValueTask.FromResult(new CanvasSharedInkCommitResult(true, "Inserted", "Stroke added to this worksheet. Autosave is on."));
    }

    private CanvasSharedInkCommitResult CommitSharedInkErasure(CanvasSharedInkStrokeRequest request,
        CanvasSharedInkOwnerSnapshot current, CancellationToken token)
    {
        if (_sharedInkEngine is not { } engine || request.Erasure is not { } gesture ||
            request.ReplacementBundles is not { } proposed || request.Bundle.ValueKind != JsonValueKind.Undefined)
            return new(false, "InvalidErasure", "The eraser needs its original gesture and drawing.");
        // Only the genuine shared donor determines hits. Never trust replacement IDs or
        // approximate stroke distances supplied by a caller, even when the epoch is current.
        var verified = CanvasSharedInkStrokeFactory.CaptureWholeErase(engine, current, gesture.Samples, gesture.Width).ReplacementBundles!;
        if (verified.Count != proposed.Count || !verified.Zip(proposed).All(pair => JsonElement.DeepEquals(pair.First, pair.Second)))
            return new(false, "InvalidErasure", "The eraser result no longer matches the original gesture.");
        var sheet = CurrentSheet!;
        var ink = sheet.Drawings.Where(drawing => drawing.Kind == DataDrawingKind.SharedInk).ToArray();
        foreach (var drawing in ink)
        {
            var bundle = engine.ParseBundle(drawing.SharedProductivityBundle!.Value.GetRawText());
            if (bundle.Objects.Count != 1 || bundle.Objects[0].ObjectId != drawing.Id)
                throw new InvalidDataException("A worksheet stroke must retain its original drawing identity before erasing.");
        }
        var survivors = verified.SelectMany(bundle => engine.ParseBundle(bundle.GetRawText()).Objects).Select(value => value.ObjectId).ToHashSet();
        var removed = ink.Where(drawing => !survivors.Contains(drawing.Id)).ToArray();
        if (removed.Any(drawing => drawing.Locked))
            return new(false, "LockedDrawing", "A crossed stroke is locked. Unlock it before erasing.");
        if (removed.Length == 0)
            return new(true, "NoChange", "No strokes were erased.");
        // Prepare the same renderable canonical survivors before touching the owner/history.
        _ = CanvasSharedInkStrokeFactory.RenderScene(engine, current.Context, current.LayerId, verified);
        token.ThrowIfCancellationRequested();
        var observed = CaptureSharedInkOwner();
        if (observed is null || !ReferenceEquals(sheet, CurrentSheet) || !CanvasSharedInkStrokeFactory.SameOwner(current, observed))
            return new(false, "RevisionConflict", "The selected sheet changed; nothing was erased.");
        if (removed.Any(drawing => drawing.Locked))
            return new(false, "LockedDrawing", "A crossed stroke is locked. Unlock it before erasing.");
        CaptureSpreadsheetUndo();
        foreach (var drawing in removed) sheet.Drawings.Remove(drawing);
        _drawingIndex = Math.Clamp(_drawingIndex, 0, Math.Max(0, sheet.Drawings.Count - 1));
        MarkDirty(); RenderCurrent(); _bus.Fire("Data.Drawing.Deleted");
        return new(true, "Erased", removed.Length == 1 ? "Stroke erased. Undo restores it." : $"{removed.Length} strokes erased. Undo restores them.");
    }

    private void RefreshSharedInkMount()
    {
        if (_disposed) return;
        _sharedInkInput?.Refresh(); _sharedInkBindings?.Notify();
    }
    private void OnSharedInkChanged(object? sender, EventArgs args) => _sharedInkBindings?.Notify();
    private void OnSharedInkLayout(object? sender, EventArgs args)
    {
        if (_sharedInkCuiRoot is null) return;
        var host = _route.SharedInkHost; var bounds = host.Bounds;
        var clip = new Rect(bounds.X, bounds.Y, Math.Max(0, bounds.Width), Math.Max(0, bounds.Height));
        for (var parent = host.Parent; parent is not null; parent = parent.Parent)
        {
            if (!parent.IsIncluded || parent.GetValue(HavenProperties.Visibility) != HavenVisibility.Visible) { clip = default; break; }
            if (parent.GetValue(HavenProperties.Clip) || parent.GetValue(HavenProperties.Overflow) is HavenOverflow.Clip or HavenOverflow.Scroll)
                clip = clip.Intersect(new Rect(parent.Bounds.X, parent.Bounds.Y, Math.Max(0, parent.Bounds.Width), Math.Max(0, parent.Bounds.Height)));
        }
        clip = clip.Intersect(new Rect(0, 0, Math.Max(0, _dataScene.Bounds.Width), Math.Max(0, _dataScene.Bounds.Height)));
        var local = clip.Width <= 0 || clip.Height <= 0 ? default(Rect) : new Rect(clip.X - bounds.X, clip.Y - bounds.Y, clip.Width, clip.Height);
        if (_sharedInkClip == local) return;
        _sharedInkClip = local; _sharedInkCuiRoot.Clip = new RectangleGeometry(local);
    }
    private void DemandSharedInkSettledBeforeDispose()
    {
        if (_sharedInkInput?.PendingSubmission is { IsCompleted: false })
            throw new InvalidOperationException("Let the original drawing stroke finish before closing Data.");
    }
    private void DisposeSharedInkMount()
    {
        if (_dataScene is not null) _dataScene.LayoutUpdated -= OnSharedInkLayout;
        if (_sharedInkInput is not null) _sharedInkInput.Changed -= OnSharedInkChanged;
        _sharedInkInput?.Dispose(); _sharedInkInput = null;
        _sharedInkLoader?.Dispose(); _sharedInkLoader = null;
        _sharedInkCuiRoot = null; _sharedInkLifetime.Cancel(); _sharedInkLifetime.Dispose();
    }

    private sealed class DataSharedInkMount(DataPage owner) : IHavenAvaloniaNativeControlResolver
    {
        public bool TryCreate(HavenElement element, out Control? control)
        {
            control = null;
            if (owner._disposed || !ReferenceEquals(element, owner._route.SharedInkHost)) return false;
            control = owner._sharedInkCuiRoot; return control is not null;
        }
    }
    private sealed class DataSharedInkBindings(DataPage owner) : ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Notify() => PropertyChanged?.Invoke(this, new(null));
        public bool TryGetValue(string path, out object? value)
        {
            value = path switch
            {
                "CanDraw" => owner._sharedInkInput?.CanDraw == true,
                "CanDelete" => IsActionAvailable("data.ink.delete") == true,
                "CanUndo" => IsActionAvailable("data.ink.undo") == true,
                "CanRedo" => IsActionAvailable("data.ink.redo") == true,
                "Status" => owner._sharedInkInput?.Status ?? "Drawing is unavailable because the Home shared ink owner is not connected.",
                "DrawingContext" => owner.CurrentSheet is { } sheet ? sheet.Name + " · " + (owner._sharedInkInput?.EraseMode == true ? "Eraser · whole strokes" : owner._sharedInkInput?.Style.Kind == CanvasRnoteInkKind.Marker ? "Highlighter" : "Pen") : "Open a workbook to draw.",
                _ => null
            };
            return path is "CanDraw" or "CanDelete" or "CanUndo" or "CanRedo" or "Status" or "DrawingContext";
        }
        public bool? IsActionAvailable(string command)
        {
            var alive = !owner._disposed && !owner._busy && owner._saveRunning == 0 && owner.IsEnabled && owner.Workbook is not null;
            return alive && (command switch
            {
                "data.ink.pen" or "data.ink.marker" or "data.ink.erase" => owner._sharedInkInput?.CanDraw == true,
                "data.ink.delete" => owner.DrawingEditorForCurrent()?.SelectedDrawing is { Kind: DataDrawingKind.SharedInk, Locked: false },
                "data.ink.undo" => owner._spreadsheetUndo.Count > 0,
                "data.ink.redo" => owner._spreadsheetRedo.Count > 0,
                "data.ink.refresh" => true,
                _ => false
            });
        }
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            Dispatcher.UIThread.VerifyAccess(); cancellationToken.ThrowIfCancellationRequested();
            if (parameter is not null) throw new ArgumentException("Use the current Data drawing action without an external target.");
            if (IsActionAvailable(command) != true) return ValueTask.CompletedTask;
            switch (command)
            {
                case "data.ink.pen": owner._sharedInkInput!.EraseMode = false; owner._sharedInkInput.Style = CanvasRnoteInkStyle.Default; break;
                case "data.ink.marker": owner._sharedInkInput!.EraseMode = false; owner._sharedInkInput.Style = new(CanvasRnoteInkKind.Marker, "#FFFFFF00", 12, .4); break;
                case "data.ink.erase": owner._sharedInkInput!.EraseMode = true; break;
                case "data.ink.delete": owner.OnDeleteDrawingRequested(this, EventArgs.Empty); break;
                case "data.ink.undo": owner.UndoSpreadsheetEdit(); break;
                case "data.ink.redo": owner.RedoSpreadsheetEdit(); break;
                case "data.ink.refresh": owner._sharedInkInput?.Refresh(retryFailedRender: true); owner._sharedInkBindings?.Notify(); break;
            }
            Notify(); return ValueTask.CompletedTask;
        }
    }
}
