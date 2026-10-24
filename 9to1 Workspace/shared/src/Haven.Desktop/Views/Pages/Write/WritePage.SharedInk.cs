using System.Text.Json;
using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Application;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using Haven.UI.Components;
using HavenOS.Apps.Canvas;
using HavenOS.Home.Core;

namespace Haven.Desktop.Views.Pages.Write;

public sealed partial class WritePage
{
    private readonly HomeProductivityEngine? _sharedInkEngine;
    private readonly HavenSceneControl Scene;
    private readonly WriteInkControls _sharedInkControls = new();
    private readonly Dictionary<Guid, WriteInkEntry> _sharedInkEntries = [];
    private readonly List<Task> _sharedInkRetirements = [];
    private readonly HashSet<Task> _sharedInkSubmissions = [];
    private WriteDocumentEditor? _sharedInkOwner;
    private long _sharedInkSelectionEpoch;
    private bool _refreshingSharedInk, _sharedInkMountInitialized;
    private CanvasRnoteInkStyle _sharedInkStyle = CanvasRnoteInkStyle.Default;
    private bool _sharedInkEraseMode;
    private CuiControlLoader? _sharedInkToolsLoader;
    private Control? _sharedInkToolsRoot;
    private WriteSharedInkBindings? _sharedInkToolBindings;
    internal Exception? LastSharedInkError { get; private set; }
    /// <summary>Current Home schema/handler compatibility only; native pen availability still
    /// requires the real input probe. The borrowed engine itself is never exposed or replaced.</summary>
    public HomeProductivityCompatibility? SharedDrawingCompatibility =>
        _sharedInkEngine?.GetCompatibility("write", "development", ["drawing.ink"]);


    private sealed record WriteInkEntry(WriteDocumentEditor Owner, NativeHost Host, CanvasSharedInkInput Input);

    private void InitializeSharedInkMount()
    {
        _sharedInkMountInitialized = true;
        _route.ConfigureSharedInk(SharedDrawingCompatibility?.Compatible == true);
        _route.DrawingRequested += OnDrawingRequested;
        _route.PresentationChanged += OnInkPresentationChanged;
        _route.DocumentSurface.SharedInkIsMounted = id => _sharedInkEntries.TryGetValue(id, out var entry)
            && (entry.Input.CanDraw || entry.Input.PendingSubmission is { IsCompleted: false });
        Scene.LayoutUpdated += UpdateSharedInkGeometry;
        InitializeSharedInkTools();
    }

    private void OnDrawingRequested(object? sender, EventArgs args) => RunOriginalEvent(async () =>
    {
        if (_sharedInkEngine is null || !CanEditSharedInk || _route.SharedInkEditor is not { } editor) return;
        var generation = _editGeneration;
        var epoch = _sharedInkSelectionEpoch;
        var context = InkContext(editor);
        // A real donor/raster/materializer probe is required before a blank drawing is inserted.
        // This transient layer never becomes canonical data and contains no saved sample stroke.
        var snapshot = new CanvasSharedInkOwnerSnapshot(context, Guid.NewGuid(), "insertion-probe", []);
        using (var probe = new CanvasSharedInkInput(_sharedInkEngine, () => snapshot,
            (_, _) => ValueTask.FromResult(new CanvasSharedInkCommitResult(false, "ProbeOnly", "No document mutation is allowed."))))
        {
            if (!probe.Refresh())
            {
                LastSharedInkError = probe.LastError; _route.SetStatus(probe.Status); return;
            }
        }
        if (!CanEditSharedInk || !ReferenceEquals(editor, _route.SharedInkEditor)
            || generation != _editGeneration || epoch != _sharedInkSelectionEpoch) return;
        if (!_route.InsertSharedInkDrawing()) return;
        await SaveAsync("Inserted shared drawing");
    });

    private bool CanEditSharedInk => CanPublishOriginal && !_busy && !_closePreparing && Document is not null
        && Volatile.Read(ref _saveRunning) == 0;

    private static HomeProductivityContext InkContext(WriteDocumentEditor editor) =>
        new("write", editor.Document.Id.ToString("D"), editor.Document.Version, [],
            new HashSet<string>(StringComparer.Ordinal) { "drawing.ink" });

    private CanvasSharedInkOwnerSnapshot? CaptureInkOwner(WriteDocumentEditor editor, Guid blockId)
    {
        if (!CanPublishOriginal || _closePreparing || !ReferenceEquals(editor, _route.SharedInkEditor)
            || !ReferenceEquals(editor.Document, Document) || editor.CaptureSharedInk(blockId) is not { } captured) return null;
        return new(InkContext(editor), blockId,
            $"{_editGeneration}:{captured.Epoch}:{_sharedInkSelectionEpoch}:{captured.Selection}",
            captured.Bundles);
    }

    private void OnInkPresentationChanged(object? sender, EventArgs args)
    {
        checked { ++_sharedInkSelectionEpoch; }
        RefreshSharedInkMount(); _sharedInkToolBindings?.Notify();
    }

    private void RefreshSharedInkMount()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!CanPublishOriginal || _refreshingSharedInk) return;
        _refreshingSharedInk = true;
        try
        {
            var editor = _route.SharedInkEditor;
            if (!ReferenceEquals(editor, _sharedInkOwner))
            {
                foreach (var entry in _sharedInkEntries.Values) RetireInkEntry(entry);
                _sharedInkEntries.Clear(); _sharedInkOwner = editor;
            }
            var wanted = editor?.Blocks().Where(block => block.SharedProductivityBundles is not null)
                .OrderBy(block => block.Id == editor.SelectedBlockId ? 0 : 1).Take(32).Select(block => block.Id).ToHashSet() ?? [];
            foreach (var id in _sharedInkEntries.Keys.Where(id => !wanted.Contains(id)).ToArray())
            { RetireInkEntry(_sharedInkEntries[id]); _sharedInkEntries.Remove(id); }
            if (_sharedInkEngine is null || editor is null) return;
            foreach (var id in wanted)
            {
                if (!_sharedInkEntries.TryGetValue(id, out var entry))
                {
                    var host = new NativeHost { Name = "Write.Drawing." + id.ToString("N") };
                    host.Accessibility.AccessibleName = "Shared drawing";
                    host.SetValue(HavenProperties.Clip, true);
                    var input = new CanvasSharedInkInput(_sharedInkEngine, () => CaptureInkOwner(editor, id),
                        (request, token) => new ValueTask<CanvasSharedInkCommitResult>(RunOriginalAsync<CanvasSharedInkCommitResult>(() => CommitInkAsync(editor, id, request, token))))
                        { EraserEnabled = true, EraseMode = _sharedInkEraseMode };
                    AutomationProperties.SetName(input, "Shared drawing. Pen, highlighter or eraser with mouse, pen or touch. Undo restores the previous drawing.");
                    input.AddHandler(PointerPressedEvent, (_, _) => OwnOriginalSceneCallback(() =>
                    {
                        if (!CanEditSharedInk || !ReferenceEquals(editor, _route.SharedInkEditor)) return;
                        _route.SelectSharedInk(id);
                    }), RoutingStrategies.Tunnel);
                    input.Changed += (_, _) => OwnOriginalSceneCallback(() =>
                    {
                        // The native source exists before this publication. Qualify its exact
                        // task even if donor preparation fails before the owner callback runs;
                        // a later successful stroke must not erase that original failure.
                        if (input.PendingSubmission is { } actual && _sharedInkSubmissions.Add(actual))
                            RunOriginalEvent(() => actual);
                        if (ReferenceEquals(editor, _route.SharedInkEditor) && editor.SelectedBlockId == id)
                            _route.SetStatus(input.Status);
                        _sharedInkToolBindings?.Notify();
                    });
                    entry = new(editor, host, input);
                    _sharedInkEntries.Add(id, entry);
                    _sharedInkControls.Register(host, input);
                    _route.DrawingHost.Add(host);
                    input.Style = _sharedInkStyle;
                }
                entry.Input.Refresh();
                if (entry.Input.LastError is { } error) LastSharedInkError = error;
            }
            _route.DocumentSurface.InvalidateDocument();
        }
        finally { _refreshingSharedInk = false; _sharedInkToolBindings?.Notify(); }
    }

    private async Task<CanvasSharedInkCommitResult> CommitInkAsync(WriteDocumentEditor editor, Guid id,
        CanvasSharedInkStrokeRequest request, CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        if (_sharedInkEngine is null || !CanEditSharedInk || editor.SelectedBlockId != id
            || CaptureInkOwner(editor, id) is not { } current
            || !CanvasSharedInkStrokeFactory.SameOwner(current, request.Expected)
            || editor.CaptureSharedInk(id) is not { } expected)
            return new(false, "StaleDrawing", "The drawing or selection changed; the stroke was not added.");
        if (request.Erasure is not null)
            return await CommitInkErasureAsync(editor, id, request, current, expected, cancellationToken);
        if (request.ReplacementBundles is not null)
            return new(false, "InvalidStroke", "A pen stroke cannot replace the drawing.");
        JsonElement CaptureCanonical(JsonElement proposed)
        {
            var captured = CanvasSharedInkStrokeFactory.CaptureBundle(_sharedInkEngine, current.Context, proposed);
            var incoming = _sharedInkEngine.ParseBundle(captured.GetRawText()).Objects;
            if (incoming.Any(value => ReadCapturedStroke(value).LayerId != id))
                throw new InvalidDataException("The stroke belongs to a different drawing layer.");
            var all = editor.Document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks)
                .Where(block => block.SharedProductivityBundles is not null)
                .SelectMany(block => block.SharedProductivityBundles!)
                .SelectMany(bundle => _sharedInkEngine.ParseBundle(bundle.GetRawText()).Objects)
                .Concat(incoming).ToArray();
            if (all.Select(value => value.ObjectId).Distinct().Count() != all.Length)
                throw new InvalidDataException("The shared object identity already exists in this document.");
            var scene = expected.Bundles.SelectMany(bundle => _sharedInkEngine.ParseBundle(bundle.GetRawText()).Objects)
                .Concat(incoming).ToArray();
            if (scene.Any(value => ReadCapturedStroke(value).LayerId != id))
                throw new InvalidDataException("The current drawing contains objects from a different layer; its original data is retained.");
            if (scene.Length > CanvasSharedInkStrokeFactory.MaximumObjects
                || scene.Sum(value => (long)ReadCapturedStroke(value).Samples.Count) > CanvasSharedInkStrokeFactory.MaximumSamples)
                throw new InvalidDataException("The drawing exceeds the shared engine's object or sample bound.");
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanEditSharedInk || CaptureInkOwner(editor, id) is not { } final
                || !CanvasSharedInkStrokeFactory.SameOwner(final, request.Expected))
                throw new InvalidOperationException("The original drawing changed before insertion.");
            return captured;
        }
        if (!editor.AppendSharedInk(expected, request.Bundle, CaptureCanonical))
            return new(false, "StaleDrawing", "The drawing changed; the stroke was not added.");
        // The accepted document mutation is retained even when storage refuses. Normal dirty/recovery
        // guards prevent close or a false saved claim; no native callback is allowed to roll it back.
        var saved = await SaveAsync("Added shared drawing stroke", CancellationToken.None);
        return saved ? new(true, "Saved", "Drawing stroke saved.")
            : new(false, "UnsavedDrawing", "The stroke remains in this draft. Save it before closing.");
    }

    private static CanvasInkStroke ReadCapturedStroke(HomeProductivityObject value) =>
        value.Content.Deserialize<CanvasInkStroke>() ?? throw new InvalidDataException("The canonical drawing stroke is absent.");

    private async Task<CanvasSharedInkCommitResult> CommitInkErasureAsync(WriteDocumentEditor editor, Guid id,
        CanvasSharedInkStrokeRequest request, CanvasSharedInkOwnerSnapshot current,
        WriteSharedInkSnapshot expected, CancellationToken cancellationToken)
    {
        if (_sharedInkEngine is not { } engine || request.Erasure is not { } gesture ||
            request.ReplacementBundles is not { } proposed || request.Bundle.ValueKind != JsonValueKind.Undefined)
            return new(false, "InvalidErasure", "The eraser needs its original gesture and drawing.");
        IReadOnlyList<JsonElement> CaptureCanonical(IReadOnlyList<JsonElement> source)
        {
            var verified = CanvasSharedInkStrokeFactory.CaptureWholeErase(engine, current, gesture.Samples, gesture.Width).ReplacementBundles!;
            if (verified.Count != source.Count || !verified.Zip(source).All(pair => JsonElement.DeepEquals(pair.First, pair.Second)))
                throw new InvalidDataException("The eraser result does not match the original shared drawing gesture.");
            var scene = verified.SelectMany(bundle => engine.ParseBundle(bundle.GetRawText()).Objects).ToArray();
            var other = editor.Blocks().Where(block => block.Id != id && block.SharedProductivityBundles is not null)
                .SelectMany(block => block.SharedProductivityBundles!).SelectMany(bundle => engine.ParseBundle(bundle.GetRawText()).Objects);
            var all = other.Concat(scene).ToArray();
            if (all.Select(value => value.ObjectId).Distinct().Count() != all.Length || scene.Any(value => ReadCapturedStroke(value).LayerId != id))
                throw new InvalidDataException("The current drawing has an ambiguous identity or another layer; original data is retained.");
            _ = CanvasSharedInkStrokeFactory.RenderScene(engine, current.Context, id, verified);
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanEditSharedInk || editor.SelectedBlockId != id || CaptureInkOwner(editor, id) is not { } final ||
                !CanvasSharedInkStrokeFactory.SameOwner(final, request.Expected))
                throw new InvalidOperationException("The drawing or selection changed before erasing.");
            return verified;
        }
        if (!editor.ReplaceSharedInk(expected, proposed, CaptureCanonical))
            return new(false, "StaleDrawing", "The drawing changed; nothing was erased.");
        if (expected.Bundles.Count == proposed.Count && expected.Bundles.Zip(proposed).All(pair => JsonElement.DeepEquals(pair.First, pair.Second)))
            return new(true, "NoChange", "No strokes were erased.");
        var saved = await SaveAsync("Erased shared drawing strokes", CancellationToken.None);
        return saved ? new(true, "Saved", "Drawing strokes erased and saved. Undo restores them.")
            : new(false, "UnsavedDrawing", "The erased drawing remains in this draft. Save it before closing.");
    }

    private void InitializeSharedInkTools()
    {
        _sharedInkToolBindings = new(this);
        using var stream = typeof(WritePage).Assembly.GetManifestResourceStream("Haven.Desktop.Write.SharedInk.cui")
            ?? throw new InvalidDataException("The Write drawing CUI resource is unavailable.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser(); var document = parser.Parse(reader.ReadToEnd(), "WriteSharedInk.cui");
        if (parser.Diagnostics.Diagnostics.Any(value => value.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Write drawing CUI resource is invalid.");
        _sharedInkToolsLoader = new(new CuiControlRegistry());
        _sharedInkToolsLoader.SetSurface("SharedInk", "write");
        _sharedInkToolsLoader.SetBindingContext(_sharedInkToolBindings); _sharedInkToolsLoader.SetActionDispatcher(_sharedInkToolBindings);
        var loaded = _sharedInkToolsLoader.TryLoad(document);
        if (loaded.Root is null || loaded.Diagnostics.Any(value => value.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Write drawing CUI tools could not be materialized.");
        _sharedInkToolsLoader.WireBindings(loaded.Root); _sharedInkToolsRoot = loaded.Root;
        _sharedInkControls.Register(_route.DrawingToolsHost, loaded.Root);
    }

    private void PublishSharedInkStyle()
    {
        foreach (var entry in _sharedInkEntries.Values)
        { entry.Input.Style = _sharedInkStyle; entry.Input.EraseMode = _sharedInkEraseMode; }
        _sharedInkToolBindings?.Notify();
    }

    private sealed class WriteSharedInkBindings(WritePage owner) : ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Notify() => PropertyChanged?.Invoke(this, new(null));
        public bool TryGetValue(string path, out object? value)
        {
            value = path switch
            {
                "CanDraw" => IsActionAvailable("write.ink.pen") == true,
                "CanUndo" => IsActionAvailable("write.ink.undo") == true,
                "CanRedo" => IsActionAvailable("write.ink.redo") == true,
                "DrawingTool" => (owner._sharedInkEraseMode ? "Eraser · whole strokes" : owner._sharedInkStyle.Kind == CanvasRnoteInkKind.Marker ? "Highlighter" : "Pen") +
                    " · " + owner._sharedInkStyle.Color + " · " + owner._sharedInkStyle.BaseWidth.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
                _ => null
            };
            return path is "CanDraw" or "CanUndo" or "CanRedo" or "DrawingTool";
        }
        public bool? IsActionAvailable(string command)
        {
            var editor = owner._route.SharedInkEditor;
            if (!owner.CanEditSharedInk || editor?.SelectedBlockId is not { } id ||
                !owner._sharedInkEntries.TryGetValue(id, out var entry) || !ReferenceEquals(entry.Owner, editor) || !entry.Input.CanDraw) return false;
            return command switch
            {
                "write.ink.undo" => editor.CanUndo,
                "write.ink.redo" => editor.CanRedo,
                "write.ink.pen" or "write.ink.marker" or "write.ink.erase" or
                "write.ink.colour.black" or "write.ink.colour.blue" or "write.ink.colour.red" or "write.ink.colour.green" or
                "write.ink.width.thin" or "write.ink.width.medium" or "write.ink.width.thick" => true,
                _ => false
            };
        }
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            Dispatcher.UIThread.VerifyAccess(); cancellationToken.ThrowIfCancellationRequested();
            if (parameter is not null) throw new ArgumentException("Use the current Write drawing tool without an external target.");
            if (IsActionAvailable(command) != true) return ValueTask.CompletedTask;
            var editor = owner._route.SharedInkEditor!; var selected = editor.SelectedBlockId;
            return new(owner.RunOriginalAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsActionAvailable(command) != true || !ReferenceEquals(editor, owner._route.SharedInkEditor) || editor.SelectedBlockId != selected) return;
                switch (command)
                {
                    case "write.ink.pen": owner._sharedInkEraseMode = false; owner._sharedInkStyle = owner._sharedInkStyle with { Kind = CanvasRnoteInkKind.Solid, Opacity = 1 }; break;
                    case "write.ink.marker": owner._sharedInkEraseMode = false; owner._sharedInkStyle = owner._sharedInkStyle with { Kind = CanvasRnoteInkKind.Marker, Opacity = .45 }; break;
                    case "write.ink.erase": owner._sharedInkEraseMode = true; break;
                    case "write.ink.colour.black": owner._sharedInkStyle = owner._sharedInkStyle with { Color = "#FF000000" }; break;
                    case "write.ink.colour.blue": owner._sharedInkStyle = owner._sharedInkStyle with { Color = "#FF246BCE" }; break;
                    case "write.ink.colour.red": owner._sharedInkStyle = owner._sharedInkStyle with { Color = "#FFD64545" }; break;
                    case "write.ink.colour.green": owner._sharedInkStyle = owner._sharedInkStyle with { Color = "#FF238B57" }; break;
                    case "write.ink.width.thin": owner._sharedInkStyle = owner._sharedInkStyle with { BaseWidth = 1 }; break;
                    case "write.ink.width.medium": owner._sharedInkStyle = owner._sharedInkStyle with { BaseWidth = 2.5 }; break;
                    case "write.ink.width.thick": owner._sharedInkStyle = owner._sharedInkStyle with { BaseWidth = 6 }; break;
                    case "write.ink.undo": if (editor.Undo()) await owner.SaveAsync("Undid drawing edit", CancellationToken.None); owner.RefreshSharedInkMount(); Notify(); return;
                    case "write.ink.redo": if (editor.Redo()) await owner.SaveAsync("Redid drawing edit", CancellationToken.None); owner.RefreshSharedInkMount(); Notify(); return;
                }
                owner.PublishSharedInkStyle();
            }));
        }
    }

    private void UpdateSharedInkGeometry(object? sender, EventArgs args) =>
        OwnOriginalSceneCallback(UpdateSharedInkGeometryCore);

    private void UpdateSharedInkGeometryCore()
    {
        if (!CanPublishOriginal) return;
        UpdateSharedInkToolsGeometry();
        foreach (var (id, entry) in _sharedInkEntries)
        {
            if (!_route.DocumentSurface.TryGetBlockBounds(id, out var local)) continue;
            entry.Host.SetValue(HavenProperties.Left, HavenLength.Px(local.X));
            entry.Host.SetValue(HavenProperties.Top, HavenLength.Px(local.Y));
            entry.Host.SetValue(HavenProperties.Width, HavenLength.Px(local.Width));
            entry.Host.SetValue(HavenProperties.Height, HavenLength.Px(local.Height));
            entry.Input.IsEnabled = CanEditSharedInk;
            var bounds = entry.Host.Bounds;
            var clip = new Rect(bounds.X, bounds.Y, Math.Max(0, bounds.Width), Math.Max(0, bounds.Height));
            for (var parent = entry.Host.Parent; parent is not null; parent = parent.Parent)
            {
                if (!parent.IsIncluded || parent.GetValue(HavenProperties.Visibility) != HavenVisibility.Visible)
                { clip = default; break; }
                if (parent.GetValue(HavenProperties.Clip) || parent.GetValue(HavenProperties.Overflow) is HavenOverflow.Clip or HavenOverflow.Scroll)
                    clip = clip.Intersect(new Rect(parent.Bounds.X, parent.Bounds.Y, Math.Max(0, parent.Bounds.Width), Math.Max(0, parent.Bounds.Height)));
            }
            clip = clip.Intersect(new Rect(0, 0, Math.Max(0, Scene.Bounds.Width), Math.Max(0, Scene.Bounds.Height)));
            entry.Input.Clip = new RectangleGeometry(clip.Width <= 0 || clip.Height <= 0 ? default :
                new Rect(clip.X - bounds.X, clip.Y - bounds.Y, clip.Width, clip.Height));
        }
    }

    private void UpdateSharedInkToolsGeometry()
    {
        if (_sharedInkToolsRoot is not { } tools) return;
        var host = _route.DrawingToolsHost; var bounds = host.Bounds;
        var clip = host.Parent is null ? default : new Rect(bounds.X, bounds.Y, Math.Max(0, bounds.Width), Math.Max(0, bounds.Height));
        for (var parent = host.Parent; parent is not null; parent = parent.Parent)
        {
            if (!parent.IsIncluded || parent.GetValue(HavenProperties.Visibility) != HavenVisibility.Visible) { clip = default; break; }
            if (parent.GetValue(HavenProperties.Clip) || parent.GetValue(HavenProperties.Overflow) is HavenOverflow.Clip or HavenOverflow.Scroll)
                clip = clip.Intersect(new Rect(parent.Bounds.X, parent.Bounds.Y, Math.Max(0, parent.Bounds.Width), Math.Max(0, parent.Bounds.Height)));
        }
        clip = clip.Intersect(new Rect(0, 0, Math.Max(0, Scene.Bounds.Width), Math.Max(0, Scene.Bounds.Height)));
        var local = clip.Width <= 0 || clip.Height <= 0 ? default : new Rect(clip.X - bounds.X, clip.Y - bounds.Y, clip.Width, clip.Height);
        if (tools.Clip?.Bounds != local) tools.Clip = new RectangleGeometry(local);
    }

    private void RetireInkEntry(WriteInkEntry entry)
    {
        _sharedInkControls.Unregister(entry.Host, entry.Input);
        entry.Host.Parent?.Remove(entry.Host);
        // Keep the actual returned source and original error. Close retires pointer ingress first,
        // joins its accepted owner callback, then disposes the raster on the UI thread.
        var actual = entry.Input.CloseAndDrainAsync();
        _sharedInkRetirements.Add(actual);
        if (!_work.IsRetiring) RunOriginalEvent(() => actual);
    }

    private void BeginSharedInkRetirement()
    {
        if (!_sharedInkMountInitialized) return;
        _sharedInkMountInitialized = false;
        _route.DrawingRequested -= OnDrawingRequested;
        _route.PresentationChanged -= OnInkPresentationChanged;
        Scene.LayoutUpdated -= UpdateSharedInkGeometry;
        if (_sharedInkToolsRoot is not null) _sharedInkToolsRoot.IsEnabled = false;
        foreach (var entry in _sharedInkEntries.Values) RetireInkEntry(entry);
        _sharedInkEntries.Clear();
    }

    private async Task JoinSharedInkRetirementsAsync()
    {
        var errors = new List<Exception>();
        foreach (var actual in _sharedInkRetirements)
            try { await actual.ConfigureAwait(false); }
            catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("Original Write drawing retirement failed.", errors);
    }

    private void DemandSharedInkRetired()
    {
        if (_sharedInkEntries.Count != 0 || _sharedInkRetirements.Any(actual => !actual.IsCompletedSuccessfully))
            throw new InvalidOperationException("Original drawing callbacks have not retired; retain the document and scene.");
        if (_sharedInkToolsRoot is { } tools) _sharedInkControls.Unregister(_route.DrawingToolsHost, tools);
        _sharedInkToolsLoader?.Dispose(); _sharedInkToolsLoader = null;
        _sharedInkToolsRoot = null; _sharedInkToolBindings = null;
    }

    private sealed class WriteInkControls : IHavenNativeControlRegistrar
    {
        private readonly Dictionary<HavenElement, Control> _controls = [];
        public void Register(HavenElement element, Control control) => _controls.Add(element, control);
        public void Unregister(HavenElement element, Control control)
        { if (_controls.TryGetValue(element, out var original) && ReferenceEquals(original, control)) _controls.Remove(element); }
        public bool TryCreate(HavenElement element, out Control? control) => _controls.TryGetValue(element, out control);
    }
}
