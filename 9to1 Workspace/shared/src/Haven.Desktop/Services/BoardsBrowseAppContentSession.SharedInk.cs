using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Threading;
using CakeOS.Cui;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Canvas;
using HavenOS.Home.Core;
using NineToOne.Web.Productivity.Boards;

namespace Haven.Desktop.Services;

// Native infrastructure only: CUI owns the drawing pane and controls; the SAME
// shared input/renderer owns pointer sampling. The original Notes editor owns data,
// undo and CAS. No Canvas artifact, private stroke model or store is created here.
internal sealed partial class BoardsBrowseAppContentSession
{
    private HomeProductivityEngine? _sharedInkEngine;
    private CanvasSharedInkInput? _sharedInkInput, _pageInkInput;
    private bool _pageInkMode;
    private double _pageInkWidth = 1200, _pageInkHeight = 900;
    internal CanvasSharedInkInput? OriginalPageInkInput => _pageInkInput;
    private bool _sharedInkInitialized, _refreshingSharedInk;
    private string _sharedInkStatus = "Shared drawing is unavailable in this host.";
    private CanvasRnoteInkStyle _sharedInkStyle = CanvasRnoteInkStyle.Default;
    private Task? _sharedInkClose;
    internal CanvasSharedInkInput? OriginalSharedInkInput => _sharedInkInput;

    private void InitializeSharedInkMount()
    {
        if (_sharedInkInitialized || _core is null || _presentation is null) return;
        _sharedInkInitialized = true;
        if (_route is not IBoardsOriginalSharedInkRoute route) return;
        var engine = route.BorrowOriginalSharedInkEngine();
        if (!engine.GetCompatibility("boards", "development", ["drawing.ink"]).Compatible) return;
        _sharedInkEngine = engine;
        _sharedInkInput = new(engine, CaptureNativeSharedInkOwner, QueueSharedInkCommit, _token);
        AutomationProperties.SetName(_sharedInkInput, "Shared drawing. Pen, mouse or touch. Use Undo to remove a stroke.");
        _sharedInkInput.Changed += OnSharedInkChanged;
        _presentation.SharedInkControl.Children.Add(_sharedInkInput);
        // Specialized NotebookSurface composition only: the SAME retained editor
        // supplies page origin. CUI owns all surrounding app/tool presentation.
        _pageInkInput = new(engine, CaptureNativePageInkOwner, QueuePageInkCommit, _token)
            { DocumentCoordinates = true, EraserEnabled = true, IsHitTestVisible = false };
        AutomationProperties.SetName(_pageInkInput, "Page drawing. Choose Page pen or Page eraser; Text returns to ordinary editing.");
        _pageInkInput.Changed += OnSharedInkChanged;
        _presentation.Control.Children.Add(_pageInkInput);
        _core.ConfigureSharedInk(true);
    }

    private bool CanEditNativeSharedInk => IsPublicationCurrent && !_preparing && _prepared is null &&
        _activeDispatches == 0 && _sharedInkClose is null && _core?.CanEditSharedDrawing == true;

    private static HomeProductivityContext SharedInkContext(BoardsSharedInkSnapshot snapshot) =>
        new("boards", snapshot.Content.Document.Id.ToString("D"), snapshot.Content.Document.Version, [],
            new HashSet<string>(StringComparer.Ordinal) { "drawing.ink" });

    private CanvasSharedInkOwnerSnapshot? CaptureNativeSharedInkOwner()
    {
        if (_closing || _preparing || _prepared is not null || _token.IsCancellationRequested || !_route.IsAlive ||
            _sharedInkClose is not null || _core?.CaptureSharedDrawing() is not { } snapshot) return null;
        var canvas = snapshot.Content.Block.Canvas!;
        return new(SharedInkContext(snapshot), snapshot.Content.Block.Id,
            $"{snapshot.Generation}:{snapshot.BindingEpoch}:{snapshot.SelectionEpoch}:{snapshot.Content.Epoch}:{snapshot.Content.Selection}",
            snapshot.Content.Bundles) { LogicalWidth = canvas.Width, LogicalHeight = canvas.Height };
    }

    private bool ProbeSharedDrawingInsertion()
    {
        if (_sharedInkEngine is null || _core?.NativeRichEditor is not { } editor) return false;
        var generation = _core.EditGeneration;
        var context = new HomeProductivityContext("boards", editor.Document.Id.ToString("D"), editor.Document.Version, [],
            new HashSet<string>(StringComparer.Ordinal) { "drawing.ink" });
        var snapshot = new CanvasSharedInkOwnerSnapshot(context, Guid.NewGuid(), "insertion-probe", []);
        using var probe = new CanvasSharedInkInput(_sharedInkEngine, () => snapshot,
            (_, _) => ValueTask.FromResult(new CanvasSharedInkCommitResult(false, "ProbeOnly", "No document mutation is allowed.")), _token);
        if (!probe.Refresh())
        {
            _sharedInkStatus = probe.Status; _core.ReportSharedInkUnavailable(probe.Status);
            _bindings.DrawingChanged(); return false;
        }
        return !_closing && !_preparing && _prepared is null && ReferenceEquals(editor, _core.NativeRichEditor) &&
            generation == _core.EditGeneration && _core.RichEditingPermitted;
    }

    private void OnSharedInkChanged(object? sender, EventArgs args) =>
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        {
            if (sender is CanvasSharedInkInput input && (ReferenceEquals(input, _sharedInkInput) || ReferenceEquals(input, _pageInkInput)))
            { _sharedInkStatus = input.Status; _bindings.DrawingChanged(); }
            return true;
        });

    private void RefreshSharedInk(bool retry = false)
    {
        if (_refreshingSharedInk || _sharedInkInput is null || _sharedInkClose is not null || _closing) return;
        Dispatcher.UIThread.VerifyAccess();
        _refreshingSharedInk = true;
        try
        {
            _sharedInkInput.IsEnabled = CanEditNativeSharedInk;
            if (_sharedInkInput.Style != _sharedInkStyle) _sharedInkInput.Style = _sharedInkStyle;
            _sharedInkInput.Refresh(retry);
            _sharedInkStatus = _sharedInkInput.Status;
            if (_pageInkInput is { } pageInput)
            {
                pageInput.IsEnabled = CanEditNativeSharedInk;
                pageInput.IsHitTestVisible = _pageInkMode && CanEditNativeSharedInk;
                if (pageInput.Style != _sharedInkStyle) pageInput.Style = _sharedInkStyle;
                pageInput.EraserWidth = Math.Clamp(_sharedInkStyle.BaseWidth * 4, 1, 500);
                if (pageInput.Refresh(retry) && _core is { } pageCore && pageCore.TryGetValue("PageInkCanvas", out var canvasActive) && canvasActive is true)
                { pageInput.MinWidth = _pageInkWidth; pageInput.MinHeight = _pageInkHeight; }
                else { pageInput.MinWidth = 0; pageInput.MinHeight = 0; }
                pageInput.IsHitTestVisible = _pageInkMode && CanEditNativeSharedInk && pageInput.CanDraw;
                _sharedInkStatus = _core?.PageInkDiagnostic ?? ((_pageInkMode ? "Page drawing · " : "Text editing · ") + pageInput.Status);
            }
        }
        finally { _refreshingSharedInk = false; }
    }

    private static bool IsSharedInkCommand(string command) => command.StartsWith("boards.ink.", StringComparison.Ordinal);
    private bool IsSharedInkActionAvailable(string command) =>
        command == "boards.ink.page.text" ? _pageInkInput is not null && _core?.AllowRetainedSelection == true :
        command is "boards.ink.page.pen" or "boards.ink.page.marker" or "boards.ink.page.erase" or "boards.ink.page.thin" or "boards.ink.page.thick"
            ? _pageInkInput is not null && _core?.CanEditSharedDrawing == true && _core.CapturePageSharedDrawing() is not null :
        _sharedInkInput is not null && _core?.CaptureSharedDrawing() is not null && (command == "boards.ink.refresh" ||
        _core.CanEditSharedDrawing && command is "boards.ink.pen" or "boards.ink.marker" or
            "boards.ink.black" or "boards.ink.blue" or "boards.ink.thin" or "boards.ink.thick");

    private CuiCommandOutcome ExecuteSharedInkCommand(string command)
    {
        if (command is "boards.ink.page.pen" or "boards.ink.page.marker" or "boards.ink.page.erase" or "boards.ink.page.text")
        {
            _pageInkMode = command != "boards.ink.page.text";
            _core?.ConfigurePageInkCanvas(_pageInkMode);
            if (_pageInkInput is not null) _pageInkInput.EraseMode = command == "boards.ink.page.erase";
        }
        _sharedInkStyle = command switch
        {
            "boards.ink.pen" or "boards.ink.page.pen" => _sharedInkStyle with { Kind = CanvasRnoteInkKind.Solid, Opacity = 1 },
            "boards.ink.marker" or "boards.ink.page.marker" => _sharedInkStyle with { Kind = CanvasRnoteInkKind.Marker, Opacity = .45 },
            "boards.ink.black" => _sharedInkStyle with { Color = "#FF000000" },
            "boards.ink.blue" => _sharedInkStyle with { Color = "#FF246BCE" },
            "boards.ink.thin" or "boards.ink.page.thin" => _sharedInkStyle with { BaseWidth = 1 },
            "boards.ink.thick" or "boards.ink.page.thick" => _sharedInkStyle with { BaseWidth = 6 },
            _ => _sharedInkStyle
        };
        RefreshSharedInk(command == "boards.ink.refresh"); _bindings.DrawingChanged();
        return new(true, false, "Succeeded", _sharedInkStatus, null);
    }

    private ValueTask<CanvasSharedInkCommitResult> QueueSharedInkCommit(CanvasSharedInkStrokeRequest request, CancellationToken caller)
    {
        Dispatcher.UIThread.VerifyAccess(); DemandAlive();
        if (!CanEditNativeSharedInk)
            return ValueTask.FromResult(new CanvasSharedInkCommitResult(false, "OperationDenied", "This drawing cannot be edited in its current state."));
        lock (_gate)
        {
            _actions.RemoveAll(task => task.IsCompletedSuccessfully);
            if (_actions.Count >= 128) throw new InvalidOperationException("Settle original Boards actions before drawing again.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ++_activeDispatches; ++_acceptedDispatchVersion;
            var original = CommitSharedInkOriginalAsync(start.Task, request, caller);
            _actions.Add(original); start.SetResult(); return new(original);
        }
    }

    private async Task<CanvasSharedInkCommitResult> CommitSharedInkOriginalAsync(Task start,
        CanvasSharedInkStrokeRequest request, CancellationToken caller)
    {
        await start;
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        var originals = new CloudflareOriginalTaskLedger();
        try
        {
            _bindings.SourcesChanged();
            await RevalidateOriginalAsync(scope.Token); DemandAlive(); _route.DemandPublicationCurrent();
            if (_sharedInkEngine is null || _core?.CaptureSharedDrawing() is not { } expected ||
                CaptureNativeSharedInkOwner() is not { } current || !CanvasSharedInkStrokeFactory.SameOwner(current, request.Expected))
                return new(false, "StaleDrawing", "The original drawing or selection changed; no stroke was added.");
            var engine = _sharedInkEngine;
            JsonElement Capture(JsonElement source)
            {
                var canonical = CanvasSharedInkStrokeFactory.CaptureBundle(engine, current.Context, source);
                var incoming = engine.ParseBundle(canonical.GetRawText()).Objects;
                if (incoming.Count != 1) throw new InvalidDataException("One canonical stroke is required.");
                var all = expected.Content.Document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks)
                    .Where(block => block.SharedProductivityBundles is not null).SelectMany(block => block.SharedProductivityBundles!)
                    .Concat(NotesSharedInkLayer.ReadAll(expected.Content.Document).SelectMany(layer => layer.Bundles))
                    .SelectMany(bundle => engine.ParseBundle(bundle.GetRawText()).Objects).Concat(incoming).ToArray();
                if (all.Select(value => value.ObjectId).Distinct().Count() != all.Length)
                    throw new InvalidDataException("This stroke identity already exists in the original document.");
                // This is the same bounded donor rendering used before native submission.
                // It also rejects foreign-layer objects before the undoable insertion.
                _ = CanvasSharedInkStrokeFactory.RenderScene(engine, current.Context, current.LayerId,
                    [.. expected.Content.Bundles, canonical]);
                scope.Token.ThrowIfCancellationRequested(); DemandAlive(); _route.DemandPublicationCurrent();
                if (CaptureNativeSharedInkOwner() is not { } final || !CanvasSharedInkStrokeFactory.SameOwner(final, request.Expected))
                    throw new InvalidOperationException("The original drawing changed before insertion.");
                return canonical;
            }
            var result = await _core.AppendSharedDrawingAsync(expected, request.Bundle, Capture, scope.Token);
            await RevalidateOriginalAsync(scope.Token); DemandAlive(); _route.DemandPublicationCurrent();
            return new(result.Succeeded, result.Code, result.Succeeded ? "Drawing stroke saved." : result.Message);
        }
        catch (Exception error) { originals.Retain(error); throw; }
        finally
        {
            --_activeDispatches;
            try { _bindings.SourcesChanged(); }
            catch (Exception error)
            {
                originals.Retain(error);
                throw new AggregateException("Original Boards drawing and publication notification failed.", originals.OriginalErrors);
            }
        }
    }

    private CanvasSharedInkOwnerSnapshot? CaptureNativePageInkOwner()
    {
        if (_closing || _preparing || _prepared is not null || _token.IsCancellationRequested || !_route.IsAlive ||
            _sharedInkClose is not null || _core?.CapturePageSharedDrawing() is not { } snapshot) return null;
        var content = snapshot.Content;
        _pageInkWidth = content.Width; _pageInkHeight = content.Height;
        var context = new HomeProductivityContext("boards", content.Document.Id.ToString("D"), content.Document.Version, [],
            new HashSet<string>(StringComparer.Ordinal) { "drawing.ink" });
        var bounds = _presentation?.Control.Bounds;
        return new(context, content.LayerId,
            $"page:{snapshot.Generation}:{snapshot.BindingEpoch}:{snapshot.SelectionEpoch}:{content.Epoch}:{content.MetadataEpoch}:{content.Selection}", content.Bundles)
        {
            LogicalWidth = Math.Clamp(Math.Max(content.Width, bounds?.Width ?? 0), 100, 100_000),
            LogicalHeight = Math.Clamp(Math.Max(content.Height, bounds?.Height ?? 0), 100, 100_000)
        };
    }

    private ValueTask<CanvasSharedInkCommitResult> QueuePageInkCommit(CanvasSharedInkStrokeRequest request, CancellationToken caller)
    {
        Dispatcher.UIThread.VerifyAccess(); DemandAlive();
        if (!CanEditNativeSharedInk) return ValueTask.FromResult(new CanvasSharedInkCommitResult(false, "OperationDenied", "The page cannot be edited now."));
        lock (_gate)
        {
            _actions.RemoveAll(task => task.IsCompletedSuccessfully);
            if (_actions.Count >= 128) throw new InvalidOperationException("Settle original Boards actions before drawing again.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ++_activeDispatches; ++_acceptedDispatchVersion;
            var original = CommitPageInkOriginalAsync(start.Task, request, caller);
            _actions.Add(original); start.SetResult(); return new(original);
        }
    }

    private async Task<CanvasSharedInkCommitResult> CommitPageInkOriginalAsync(Task start, CanvasSharedInkStrokeRequest request, CancellationToken caller)
    {
        await start;
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        var originals = new CloudflareOriginalTaskLedger();
        try
        {
            _bindings.SourcesChanged();
            await RevalidateOriginalAsync(scope.Token); DemandAlive(); _route.DemandPublicationCurrent();
            if (_sharedInkEngine is not { } engine || _core?.CapturePageSharedDrawing() is not { } expected ||
                CaptureNativePageInkOwner() is not { } current || !CanvasSharedInkStrokeFactory.SameOwner(current, request.Expected))
                return new(false, "StaleDrawing", "The original page or drawing changed; nothing was submitted.");
            var proposed = request.ReplacementBundles ?? [.. current.Bundles, request.Bundle];
            IReadOnlyList<JsonElement> Capture(IReadOnlyList<JsonElement> source)
            {
                var captured = source.Select(bundle => CanvasSharedInkStrokeFactory.CaptureBundle(engine, current.Context, bundle)).ToArray();
                if (request.Erasure is { } erase)
                {
                    var verified = CanvasSharedInkStrokeFactory.CaptureWholeErase(engine, current, erase.Samples, erase.Width).ReplacementBundles!;
                    if (verified.Count != captured.Length || !verified.Zip(captured).All(pair => JsonElement.DeepEquals(pair.First, pair.Second)))
                        throw new InvalidDataException("The exact donor erasure changed before the owning commit.");
                }
                else if (request.ReplacementBundles is not null || captured.Length != current.Bundles.Count + 1 ||
                    !current.Bundles.Zip(captured).All(pair => JsonElement.DeepEquals(pair.First, pair.Second)))
                    throw new InvalidDataException("A page stroke may only append to its exact current layer.");
                var allPages = expected.Content.Document.Sections.SelectMany(section => section.Pages).ToArray();
                var otherBundles = allPages.SelectMany(page => page.Blocks).Where(block => block.SharedProductivityBundles is not null)
                    .SelectMany(block => block.SharedProductivityBundles!)
                    .Concat(NotesSharedInkLayer.ReadAll(expected.Content.Document).Where(layer => layer.PageId != expected.Content.Page.Id).SelectMany(layer => layer.Bundles));
                var objects = otherBundles.Concat(captured).SelectMany(bundle => engine.ParseBundle(bundle.GetRawText()).Objects).ToArray();
                var containerIds = allPages.Select(page => page.Id).Concat(allPages.SelectMany(page => page.Blocks).Select(block => block.Id))
                    .Concat(allPages.SelectMany(page => page.CanvasObjects).Select(item => item.Id))
                    .Concat(expected.Content.Document.Sections.Select(section => section.Id)).Append(current.LayerId).Append(expected.Content.Document.Id).ToHashSet();
                if (objects.Select(value => value.ObjectId).Distinct().Count() != objects.Length || objects.Any(value => containerIds.Contains(value.ObjectId)))
                    throw new InvalidDataException("A canonical page stroke identity already exists in the original document.");
                _ = CanvasSharedInkStrokeFactory.RenderScene(engine, current.Context, current.LayerId, captured);
                scope.Token.ThrowIfCancellationRequested(); DemandAlive(); _route.DemandPublicationCurrent();
                if (CaptureNativePageInkOwner() is not { } final || !CanvasSharedInkStrokeFactory.SameOwner(final, request.Expected))
                    throw new InvalidOperationException("The original page context changed during preparation.");
                return captured;
            }
            var result = await _core.ReplacePageSharedDrawingAsync(expected, proposed, Capture, scope.Token,
                request.Erasure is null ? current.LogicalWidth : null, request.Erasure is null ? current.LogicalHeight : null);
            await RevalidateOriginalAsync(scope.Token); DemandAlive(); _route.DemandPublicationCurrent();
            return new(result.Succeeded, result.Code, result.Succeeded ? "Page drawing saved." : result.Message);
        }
        catch (Exception error) { originals.Retain(error); throw; }
        finally
        {
            --_activeDispatches;
            try { _bindings.SourcesChanged(); }
            catch (Exception error)
            { originals.Retain(error); throw new AggregateException("Original page drawing and publication failed.", originals.OriginalErrors); }
        }
    }

    private Task[] CaptureSharedInkSubmissions() => (_sharedInkInput?.OriginalSubmissions ?? []).Concat(_pageInkInput?.OriginalSubmissions ?? []).ToArray();
    private void BeginSharedInkRetirement()
    {
        if (_sharedInkClose is not null) return;
        var originals = new List<Task>();
        foreach (var input in new[] { _sharedInkInput, _pageInkInput }.OfType<CanvasSharedInkInput>())
        {
            input.IsEnabled = false; input.IsHitTestVisible = false;
            try { originals.Add(input.CloseAndDrainAsync()); }
            catch (Exception error) { originals.Add(Task.FromException(error)); }
        }
        _sharedInkClose = Task.WhenAll(originals);
        // Join both exact submission ledgers before disposing only local input views.
        // Original Home/Notes/global engine ownership is never transferred here.
    }
}
