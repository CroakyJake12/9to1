using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Present;
using Haven.UI;
using NineToOne.Web.Productivity.Present;

internal static class PresentInteractionEngineChecks
{
    public static async Task Run(Window window, PresentRetainedSceneControl control, PresentBrowserSession session, Action<bool, string> check)
    {
        var editor = session.Editor!;
        var scene = control.GetVisualDescendants().OfType<HavenSceneControl>().Single();
        var canvas = scene.Root!;
        var pointer = (IHavenPointerInputTarget)canvas;
        var cursor = (IHavenCursorInputTarget)canvas;
        HavenRect Paper()
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); using var frame = window.CaptureRenderedFrame();
            return (HavenRect)canvas.GetType().GetProperty("PaperBounds", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(canvas)!;
        }
        Point At(double x, double y)
        {
            var paper = Paper();
            return scene.TranslatePoint(new Point(paper.X + x * paper.Width, paper.Y + y * paper.Height), window)!.Value;
        }
        HavenPoint Local(double x, double y) { var paper = Paper(); return new(paper.X + x * paper.Width, paper.Y + y * paper.Height); }
        void Select(params Guid[] ids) { editor.SelectElements(ids); control.SetEditor(editor); _ = Paper(); }
        PresentElement Item(Guid id) => editor.SelectedSlide.Elements.Single(element => element.Id == id);
        void Place(Guid id, double x, double y, double width, double height)
        {
            Select(id); var item = Item(id);
            editor.TransformSelection(x - item.X, y - item.Y, width - item.Width, height - item.Height, -item.RotationDegrees);
        }
        void Tap(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
        { window.KeyPress(key, modifiers, key == Key.Enter ? PhysicalKey.Enter : Enum.Parse<PhysicalKey>(key.ToString()), key.ToString()); window.KeyRelease(key, modifiers, key == Key.Enter ? PhysicalKey.Enter : Enum.Parse<PhysicalKey>(key.ToString()), key.ToString()); }
        void Frame(string name)
        { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(Environment.GetEnvironmentVariable("HAVEN_DATA_DIR")!, name + ".png")); }
        void DrawCheck(Func<HavenDrawingContext, bool> assertion, string message)
        { var draw = new HavenDrawingContext(); ((IHavenDrawCommandSource)canvas).Draw(draw, 1); check(assertion(draw), message); }

        Select(editor.SelectedSlide.Elements.Select(element => element.Id).ToArray()); editor.RemoveSelectedElements();
        var shapeId = editor.AddShape(editor.Selection.SlideId).Id;
        Place(shapeId, .25, .3, .2, .25); Select();
        var body = At(.35, .425);
        window.MouseMove(body);
        check(control.HoveredObjectId == shapeId && editor.Selection.ElementIds.Count == 0, "actual uncaptured pointer hover identifies the object without selecting it");
        DrawCheck(draw => draw.Commands.OfType<HavenStrokeRoundedRectCommand>().Any(command => command.Pen.Thickness == 1 && Math.Abs(command.Opacity - .55) < .001),
            "hover produces a distinct transient subtle overlay");
        Frame("engine-hover");
        window.MouseDown(body, MouseButton.Left); window.MouseUp(body, MouseButton.Left);
        check(editor.Selection.ElementIds.SequenceEqual([shapeId]) && control.InteractionState == PresentInteractionState.ObjectSelected,
            "native click selects the hovered object and reports explicit selection state");
        window.MouseMove(At(.96, .9));
        check(control.HoveredObjectId is null && editor.Selection.ElementIds.Contains(shapeId), "hover leaves without clearing selection");
        DrawCheck(draw => draw.Commands.OfType<HavenEllipseCommand>().Count() == 9, "ordinary rectangular selection exposes eight resize handles and rotation");

        Frame("engine-selected");
        var original = JsonSerializer.Serialize(Item(shapeId));
        window.MouseDown(body, MouseButton.Left); window.MouseMove(body + new Vector(40, 24));
        check(control.InteractionState == PresentInteractionState.ObjectDragging && control.LiveGeometry.Single().X > .25 && Item(shapeId).X == .25,
            "native drag displays real geometry continuously before the one owner mutation");
        Frame("engine-live-drag");
        var layoutCount = HavenUiDiagnostics.LayoutInvalidations; var structureCount = HavenUiDiagnostics.StructureInvalidations;
        window.MouseMove(body + new Vector(50, 30));
        check(HavenUiDiagnostics.LayoutInvalidations == layoutCount && HavenUiDiagnostics.StructureInvalidations == structureCount,
            "a subsequent manipulation sample requests paint without layout or CUI reconstruction");
        Tap(Key.Escape); window.MouseUp(body + new Vector(50, 30), MouseButton.Left);
        check(JsonSerializer.Serialize(Item(shapeId)) == original && control.InteractionState == PresentInteractionState.ObjectSelected,
            "Escape cancels live movement without authored geometry or stuck capture");
        window.MouseDown(body, MouseButton.Left); window.MouseMove(body + new Vector(40, 24));
        pointer.PointerCancelled(new(default, default, HavenPointerKind.Mouse)); window.MouseUp(body + new Vector(40, 24), MouseButton.Left);
        check(JsonSerializer.Serialize(Item(shapeId)) == original, "explicit capture cancellation cannot commit a stale release");

        var fitStart = control.EffectiveZoom; var firstAnchor = At(.4, .4);
        check(session.IsFitToWindow && !control.GetVisualAncestors().OfType<ScrollViewer>().Any(),
            "initial Fit has the actual constrained CUI stage without a scroll wrapper");
        window.MouseWheel(firstAnchor, new Vector(0, 1), RawInputModifiers.Control); _ = Paper();
        check(!session.IsFitToWindow && session.CanvasZoom > fitStart && control.GetVisualAncestors().OfType<ScrollViewer>().Any(),
            "Ctrl-wheel from initial Fit creates the real manual viewport and updates authoritative zoom");
        var firstAnchorAfter = At(.4, .4);
        check(Math.Sqrt(Math.Pow(firstAnchorAfter.X - firstAnchor.X, 2) + Math.Pow(firstAnchorAfter.Y - firstAnchor.Y, 2)) < 2,
            "the first Fit-to-manual zoom preserves the pointer anchor across CUI wrapper reparenting");
        await session.DispatchAsync("ZoomFit", null); _ = Paper();
        foreach (var zoom in new[] { .5, .75, 1, 1.5, 2 })
        {
            session.SetCanvasZoom(zoom); var paper = Paper();
            check(Math.Abs(paper.Width - editor.Document.SlideSize.WidthInches * 96 * zoom) < .01,
                "manual zoom uses canonical authored slide pixels at " + zoom);
            if (zoom == 2) Frame("engine-200-percent");
            // Use the actual owning router with its measured local coordinates;
            // no UI command proxy is used for off-viewport handles at high zoom.
            var router = (HavenInputRouter)typeof(HavenSceneControl).GetField("_input", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scene)!;
            Select(shapeId);
            var east = Local(.45, .425); var before = Item(shapeId).Width;
            check(cursor.GetCursor(east) == HavenCursor.ResizeHorizontal, "east handle exposes horizontal cursor at " + zoom);
            router.PointerPressed(east); router.PointerMoved(new(east.X + 20, east.Y));
            check(control.LiveGeometry.Single().Width > before, "side resize previews live width at " + zoom);
            router.CancelPointer();
            check(Item(shapeId).Width == before, "cancelled zoomed resize preserves durable dimensions at " + zoom);
        }
        await session.DispatchAsync("ZoomFit", null); _ = Paper();
        var fit = Paper(); var fitScale = control.EffectiveZoom;
        check(Math.Abs(fit.Width / fit.Height - 16d / 9) < .001 && Math.Abs(fit.X * 2 + fit.Width - canvas.Bounds.Width) < .01,
            "explicit Fit restores a centred complete 16:9 paper");
        session.SetCanvasZoom(1); _ = Paper();
        var viewport = control.GetVisualAncestors().OfType<ScrollViewer>().First();
        var normalScrollBefore = viewport.Offset;
        window.MouseWheel(At(.4, .4), new Vector(0, -2)); _ = Paper();
        check(viewport.Offset.Y > normalScrollBefore.Y, "ordinary wheel over overflowing paper reaches its CUI-owned scroll viewport");
        var request = 0d;
        void Zoom(double value) { request = value; }
        control.ZoomRequested += Zoom;
        var anchorBefore = At(.4, .4);
        window.MouseWheel(anchorBefore, new Vector(0, 1), RawInputModifiers.Control); _ = Paper();
        var anchorAfter = At(.4, .4);
        check(request > 1 && control.EffectiveZoom > 1, "native Ctrl-wheel requests the authoritative actual zoom");
        check(Math.Sqrt(Math.Pow(anchorAfter.X - anchorBefore.X, 2) + Math.Pow(anchorAfter.Y - anchorBefore.Y, 2)) < 2, "Ctrl-wheel retains the document point under the pointer");
        control.ZoomRequested -= Zoom;
        session.SetCanvasZoom(2); _ = Paper();
        viewport = control.GetVisualAncestors().OfType<ScrollViewer>().First();
        var panPoint = viewport.TranslatePoint(new Point(250, 150), window)!.Value;
        var panOffset = viewport.Offset;
        window.MouseDown(panPoint, MouseButton.Middle); window.MouseMove(panPoint - new Vector(40, 30));
        check(control.InteractionState == PresentInteractionState.CanvasPanning
            && viewport.Offset.X > panOffset.X && viewport.Offset.Y > panOffset.Y,
            "middle-button drag pans the actual viewport with an explicit captured pan state");
        window.MouseUp(panPoint - new Vector(40, 30), MouseButton.Middle);
        check(control.InteractionState != PresentInteractionState.CanvasPanning, "middle-button release clears pan capture");
        scene.FocusElement(canvas);
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.MouseDown(panPoint, MouseButton.Left); window.MouseMove(panPoint - new Vector(30, 20));
        check(control.InteractionState == PresentInteractionState.CanvasPanning, "Space plus primary drag uses the same viewport pan engine");
        window.MouseUp(panPoint - new Vector(30, 20), MouseButton.Left);
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        check(control.InteractionState != PresentInteractionState.CanvasPanning, "Space pan ends without leaving an object drag active");
        scene.FocusElement(canvas);
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        var externalFocus = window.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible && button.IsEnabled);
        externalFocus.Focus();
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.MouseDown(panPoint, MouseButton.Left);
        check(control.InteractionState != PresentInteractionState.CanvasPanning,
            "moving focus outside the canvas before Space-up clears the transient pan modifier");
        window.MouseUp(panPoint, MouseButton.Left);
        window.MouseDown(panPoint, MouseButton.Middle); window.MouseMove(panPoint - new Vector(25, 20));
        session.SetCanvasZoom(1.5); _ = Paper();
        check(control.InteractionState != PresentInteractionState.CanvasPanning,
            "authoritative manual zoom cancels an in-progress pan and releases its cursor/capture");
        window.MouseUp(panPoint - new Vector(25, 20), MouseButton.Middle);
        session.SetCanvasZoom(.75); _ = Paper();
        window.Width = 900; window.Height = 700; _ = Paper();
        check(Math.Abs(control.EffectiveZoom - .75) < .001, "resizing the window preserves explicit manual zoom");
        await session.DispatchAsync("ZoomFit", null); _ = Paper();
        check(control.EffectiveZoom < fitScale, "Fit recalculates against the smaller actual stage");
        window.Width = 1200; window.Height = 900; _ = Paper();

        foreach (var (hx, hy) in new[] { (-1,-1), (0,-1), (1,-1), (-1,0), (1,0), (-1,1), (0,1), (1,1) })
        {
            Place(shapeId, .25, .3, .2, .25); editor.RotateSelection(30); _ = Paper();
            var paper = Paper(); var item = Item(shapeId);
            var centre = At(item.X + item.Width / 2, item.Y + item.Height / 2);
            var c = Math.Cos(Math.PI / 6); var sn = Math.Sin(Math.PI / 6);
            var localX = hx * item.Width * paper.Width / 2; var localY = hy * item.Height * paper.Height / 2;
            var handle = centre + new Vector(c * localX - sn * localY, sn * localX + c * localY);
            var change = new Vector(c * hx * 14 - sn * hy * 14, sn * hx * 14 + c * hy * 14);
            var originalWidth = item.Width; var originalHeight = item.Height;
            window.MouseDown(handle, MouseButton.Left); window.MouseMove(handle + change);
            var preview = control.LiveGeometry.Single();
            check((hx == 0 ? Math.Abs(preview.Width - originalWidth) < .00001 : preview.Width > originalWidth)
                && (hy == 0 ? Math.Abs(preview.Height - originalHeight) < .00001 : preview.Height > originalHeight),
                $"rotated handle ({hx},{hy}) previews exactly the expected dimensions");
            window.MouseUp(handle + change, MouseButton.Left);
            check(Math.Abs(Item(shapeId).Width - preview.Width) < .00001 && Math.Abs(Item(shapeId).Height - preview.Height) < .00001,
                $"rotated handle ({hx},{hy}) commits the geometry already visible under the pointer");
            editor.Undo();
            check(Math.Abs(Item(shapeId).Width - originalWidth) < .00001 && Math.Abs(Item(shapeId).Height - originalHeight) < .00001,
                $"one undo restores the complete rotated resize ({hx},{hy})");
        }
        Place(shapeId, .25, .3, .2, .25);

        var tiny = editor.AddShape(editor.Selection.SlideId).Id;
        Place(tiny, .7, .7, .01, .01); Select(tiny);
        check(cursor.GetCursor(Local(.705, .705)) == HavenCursor.Move,
            "tiny objects retain a movable centre despite enlarged corner hit targets");
        var tinyCorner = Local(.71, .71);
        check(cursor.GetCursor(tinyCorner) == HavenCursor.ResizeNorthWestSouthEast,
            "tiny-object hit testing chooses the nearest real corner instead of an overlapping earlier handle");
        DrawCheck(draw => draw.Commands.OfType<HavenEllipseCommand>().Count() == 3, "tiny-object overlay suppresses overlapping side handles but retains resize and rotation");
        editor.RotateSelection(90); _ = Paper();
        var tinyPaper = Paper();
        var rotatedEast = new HavenPoint(tinyPaper.X + .705 * tinyPaper.Width, tinyPaper.Y + .705 * tinyPaper.Height + .005 * tinyPaper.Width);
        check(cursor.GetCursor(rotatedEast) != HavenCursor.Default, "rotated tiny-object handles remain actual hit targets");

        Select(shapeId); var second = editor.AddShape(editor.Selection.SlideId).Id; Place(second, .5, .3, .12, .2);
        Select(shapeId, second); var group = editor.GroupSelection()!.Value; control.SetEditor(editor); _ = Paper();
        var groupBefore = Item(group); var childBeforeX = Item(shapeId).X; var secondBeforeX = Item(second).X;
        var childPoint = At(.3, .4);
        window.MouseDown(childPoint, MouseButton.Left); window.MouseUp(childPoint, MouseButton.Left);
        check(editor.Selection.ElementIds.SequenceEqual([group]), "clicking grouped content targets its real parent group identity");
        editor.MoveSelection(.07, .04);
        check(Math.Abs(Item(shapeId).X - childBeforeX - .07) < .0001 && Math.Abs(Item(second).X - secondBeforeX - .07) < .0001,
            "group move transforms both existing child objects without detaching them");
        editor.Undo();
        check(Math.Abs(Item(shapeId).X - childBeforeX) < .0001 && Item(shapeId).ParentGroupId == group,
            "one group-move undo restores child geometry and hierarchy");
        Select(group); var groupPaper = Paper(); var groupItem = Item(group);
        var groupCentre = At(groupItem.X + groupItem.Width / 2, groupItem.Y + groupItem.Height / 2);
        var rotationHandle = At(groupItem.X + groupItem.Width / 2, groupItem.Y) - new Vector(0, 28);
        var rotationTarget = groupCentre + new Vector(groupItem.Height * groupPaper.Height / 2 + 28, 0);
        var beforeRotation = JsonSerializer.Serialize(editor.SelectedSlide.Elements.Where(element => element.Id == group || element.ParentGroupId == group));
        window.MouseDown(rotationHandle, MouseButton.Left); window.MouseMove(rotationTarget);
        check(control.InteractionState == PresentInteractionState.ObjectRotating && control.LiveGeometry.Count == 3
            && control.LiveGeometry.All(item => Math.Abs(item.RotationDegrees - 90) < .001),
            "group rotation previews every real descendant around the same parent pivot");
        Frame("engine-group-rotation");
        var projectedChild = control.LiveGeometry.Single(item => item.ElementId == shapeId);
        window.MouseUp(rotationTarget, MouseButton.Left);
        check(Math.Abs(Item(shapeId).X - projectedChild.X) < .0001 && Math.Abs(Item(shapeId).Y - projectedChild.Y) < .0001
            && Math.Abs(Item(shapeId).RotationDegrees - projectedChild.RotationDegrees) < .0001,
            "group rotation commits the child position and angle already previewed");
        editor.Undo();
        check(JsonSerializer.Serialize(editor.SelectedSlide.Elements.Where(element => element.Id == group || element.ParentGroupId == group)) == beforeRotation,
            "one undo restores parent and child rotation and position together");
        var sibling = editor.AddShape(editor.Selection.SlideId).Id; Place(sibling, .12, .12, .08, .09);
        Select(group, sibling); var beforeAlignX = Item(shapeId).X; var oldGroupX = Item(group).X;
        editor.AlignSelection(PresentAlignment.Left);
        check(Math.Abs(Item(shapeId).X - beforeAlignX - (.12 - oldGroupX)) < .0001 && Item(shapeId).ParentGroupId == group,
            "aligning a group translates its children with the same logical parent");
        editor.Undo(); Select(group);
        var maxSiblingOrder = Item(sibling).Order;
        check(editor.BringForward() && Item(shapeId).Order > Item(sibling).Order && Item(second).Order > Item(sibling).Order,
            "bringing a group forward moves all its children as one stacking unit");
        editor.Undo(); Select(group);
        var clipboard = editor.CopySelection(); var pasted = editor.Paste(clipboard);
        check(pasted.Count == 3 && editor.SelectedElements.Single().Kind == PresentElementKind.Group
            && editor.SelectedSlide.Elements.Count(element => element.ParentGroupId == editor.SelectedElements.Single().Id) == 2,
            "group duplication copies the whole hierarchy with fresh IDs and selects its one new parent");
        editor.RemoveSelectedElements();
        check(pasted.All(id => editor.SelectedSlide.Elements.All(element => element.Id != id)), "delete removes a selected group and its owned descendants together");

        var inserted = new List<(string Preset, double X, double Y, double Width, double Height)>();
        control.ShapeInsertionRequested += (preset, x, y, width, height) => inserted.Add((preset, x, y, width, height));
        var geometry = DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Ellipse);
        control.BeginShapeInsertion("ellipse", geometry);
        check(control.InteractionState == PresentInteractionState.InsertionMode && cursor.GetCursor(Local(.8, .6)) == HavenCursor.Crosshair,
            "UI insertion request enters the same engine with crosshair feedback");
        window.MouseDown(At(.65, .55), MouseButton.Left); window.MouseMove(At(.85, .8));
        check(inserted.Count == 0, "shape drag previews without prematurely creating an owner object");
        Frame("engine-live-insertion");
        window.MouseUp(At(.85, .8), MouseButton.Left);
        check(inserted.Count == 1 && Math.Abs(inserted[0].Width - .2) < .001 && Math.Abs(inserted[0].Height - .25) < .001,
            "shape drag emits exactly one normalized geometry commit on release");
        control.BeginShapeInsertion("ellipse", geometry); Tap(Key.Escape);
        window.MouseDown(At(.8, .8), MouseButton.Left); window.MouseUp(At(.8, .8), MouseButton.Left);
        check(inserted.Count == 1 && control.InteractionState != PresentInteractionState.InsertionMode,
            "Escape cancels insertion without creating an object on a later click");

        var textId = editor.AddText(editor.Selection.SlideId, "Original text").Id;
        Place(textId, .2, .4, .35, .2); Select(textId);
        await Task.Delay(600);
        var textPoint = At(.35, .5); window.MouseDown(textPoint, MouseButton.Left); window.MouseUp(textPoint, MouseButton.Left);
        check(!control.IsTextEditing, "single click still manipulates the text object after engine changes");
        Tap(Key.Enter); check(control.IsTextEditing, "Enter enters the text object's separate content editor");
        Frame("engine-text-editing");
        Tap(Key.A, RawInputModifiers.Control); window.KeyTextInput("Edited without moving its parent");
        Tap(Key.Escape);
        check(!control.IsTextEditing && Item(textId).Text == "Edited without moving its parent" && Math.Abs(Item(textId).X - .2) < .0001,
            "text editing exits with content retained and parent geometry unchanged");
        var documentId = session.DocumentId!.Value;
        var saved = await session.SaveAsync();
        var snapshot = session.GetDocumentSnapshot()!;
        check(saved.Succeeded && !session.IsDirty, "interaction work commits through the real owning repository");
        await session.CloseAsync(); var reopened = await session.OpenAsync(documentId);
        check(reopened.Succeeded && JsonSerializer.Serialize(snapshot) == JsonSerializer.Serialize(session.GetDocumentSnapshot()),
            "reopening preserves complete transformed geometry, hierarchy and text content");
    }
}
