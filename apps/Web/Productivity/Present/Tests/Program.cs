using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Infrastructure;
using Haven.UI;
using NineToOne.Web;
using NineToOne.Web.Productivity.Present;
using NineToOne.Web.Write;

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_DATA_DIR")))
    throw new ArgumentException("Supply a fresh explicitly isolated HAVEN_DATA_DIR.");
await using var headless = HeadlessUnitTestSession.StartNew(typeof(PresentSceneTestApplication));
var assertions = await headless.Dispatch(async () =>
{
    var count = 0;
    void Check(bool pass, string oracle)
    { if (!pass) throw new InvalidOperationException(oracle); ++count; Console.WriteLine("ASSERT " + oracle); }
    Check(Application.Current!.Styles.All(style => style.GetType().Name is not ("FluentTheme" or "SimpleTheme")),
        "actual retained Present host uses CUI-owned templates without a donor default theme");
    Check(!Avalonia.Platform.AssetLoader.Exists(new Uri("avares://Haven/WriteOwnerResources/DefaultTheme.Resources.axaml")),
        "retained Present runs without the removed extracted AXAML theme resource");
    var paths = new AppPaths();
    if (args.Contains("inherited-styles", StringComparer.Ordinal)) return await PresentInheritedStyleChecks.Run(paths);
    if (args.Contains("scoped-theme", StringComparer.Ordinal)) return await PresentScopedThemeChecks.Run(paths);
    if (args.Contains("thumbnail-wheel", StringComparer.Ordinal)) return await PresentThumbnailWheelChecks.Run(paths);
    if (args.Contains("shape-text", StringComparer.Ordinal)) return await PresentShapeTextChecks.Run(paths);
    if (args.Contains("interchange", StringComparer.Ordinal)) return await PresentInterchangeChecks.Run(paths);
    if (args.Contains("title", StringComparer.Ordinal)) return await PresentTitleChecks.Run(paths);
    if (args.Contains("document-fonts", StringComparer.Ordinal)) return PresentDocumentFontChecks.Run(paths);
    var repository = new PresentRepository(paths);
    Check((await repository.ListAsync(default)).Count == 0, "actual Present repository fixture starts empty");
    if (args.Contains("admission", StringComparer.Ordinal))
    {
        var a = PresentDocument.Create("Canonical Present A"); _ = new PresentEditor(a);
        var b = PresentDocument.Create("Canonical Present B"); _ = new PresentEditor(b);
        await repository.SaveAsync(a, "Actual admission A fixture", default);
        await repository.SaveAsync(b, "Actual admission B fixture", default);
        var faults = new PresentReadFault(repository, b.Id);
        var feature = new PresentBrowserFeature(faults, () => true, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Present.cui")));
        var preparedA = await feature.OpenAsync(new("app.present", "PresentDocument", a.Id.ToString("D")));
        Check(preparedA.Succeeded, "actual canonical Present A prepares through owner repository");
        var aSurface = feature.Render(preparedA.ViewState!);
        using var aLoader = new CuiControlLoader(aSurface.ControlRegistry!);
        aLoader.SetBindingContext(aSurface.Bindings); aLoader.SetActionDispatcher(aSurface.Actions);
        var (aRoot, aErrors) = aLoader.TryLoad(aSurface.Document);
        Check(aRoot is not null && !aErrors.Any(error => error.Severity == CuiDiagnosticSeverity.Error),
            "actual Present A surface lowers original owning canvas");
        aLoader.WireBindings(aRoot!);
        var admissionWindow = new Window { Width = 1200, Height = 900, Content = aRoot };
        admissionWindow.Show();
        try
        {
            using (var frame = admissionWindow.CaptureRenderedFrame()) Check(frame is not null, "original Present A native retained frame renders");
            aSurface.Admission!.Accept(); await feature.PrepareToCloseAsync();
            var activeA = (PresentBrowserSession)aSurface.Bindings;
            var originalA = JsonSerializer.Serialize(activeA.GetDocumentSnapshot());
        var aScene = aRoot!.GetVisualDescendants().OfType<PresentRetainedSceneControl>().Single().GetVisualDescendants().OfType<HavenSceneControl>().Single();
            bool AUnchanged() => activeA.DocumentId == a.Id && activeA.DurableRevision == a.Version &&
                originalA == JsonSerializer.Serialize(activeA.GetDocumentSnapshot()) && aScene.Root is not null;
            var requestB = new HavenOS.Home.Core.HomeFeatureNavigationRequest("app.present", "PresentDocument", b.Id.ToString("D"));
            faults.FailList = true;
            var failed = await feature.OpenAsync(requestB);
            Check(!failed.Succeeded && failed.Request == requestB && faults.TargetLoads == 0 && AUnchanged(),
                "failed actual library admission does not load B or change visible A canonical content/revision/lifetime");
            faults.FailList = false;
            using (var cancelled = new CancellationTokenSource())
            {
                var offer = await feature.OpenAsync(requestB, cancelled.Token); cancelled.Cancel();
                var rejected = false; try { feature.Render(offer.ViewState!); } catch (InvalidOperationException) { rejected = true; }
                Check(offer.Succeeded && rejected && AUnchanged(), "cancelled actual B preparation cannot replace visible A");
            }
            var pending = await feature.OpenAsync(requestB);
            var pendingSurface = feature.Render(pending.ViewState!);
            var pendingSession = (PresentBrowserSession)pendingSurface.Bindings;
            Check(!pendingSession.IsPresentationAdmitted && !pendingSession.IsDirty &&
                !(await pendingSession.CreateAsync()).Succeeded && !(await pendingSession.SaveAsync()).Succeeded &&
                !(await pendingSession.EditAsync(editor => { editor.AddSlide(editor.Selection.SlideId); return true; })).Succeeded,
                "clean actual prepared B rejects direct create/save/edit before presentation admission");
            using (var candidateLoader = new CuiControlLoader(pendingSurface.ControlRegistry!))
            {
                candidateLoader.SetBindingContext(pendingSurface.Bindings); candidateLoader.SetActionDispatcher(pendingSurface.Actions);
                var (candidateRoot, errors) = candidateLoader.TryLoad(pendingSurface.Document);
                Check(candidateRoot is not null && !errors.Any(error => error.Severity == CuiDiagnosticSeverity.Error), "actual candidate Present B lowers before admission");
                candidateLoader.WireBindings(candidateRoot!);
                var candidateScene = candidateRoot!.GetVisualDescendants().OfType<PresentRetainedSceneControl>().Single().GetVisualDescendants().OfType<HavenSceneControl>().Single();
                Check(!candidateScene.IsEnabled && AUnchanged(), "actual original retained candidate scene input remains frozen while A stays visible");
            }
            pendingSurface.Admission!.Reject(); pendingSurface.Lifetime!.Dispose();
            Check(AUnchanged(), "candidate rejection disposes only unauthored B presentation, preserving live A");
            var accepted = await feature.OpenAsync(requestB);
            var bSurface = feature.Render(accepted.ViewState!);
            using var bLoader = new CuiControlLoader(bSurface.ControlRegistry!);
            bLoader.SetBindingContext(bSurface.Bindings); bLoader.SetActionDispatcher(bSurface.Actions);
            var (bRoot, bErrors) = bLoader.TryLoad(bSurface.Document);
            Check(bRoot is not null && !bErrors.Any(error => error.Severity == CuiDiagnosticSeverity.Error), "valid B CUI lowers before actual presentation acceptance");
            bLoader.WireBindings(bRoot!); Check(AUnchanged(), "valid B lowering has no effect on visible canonical A");
            bSurface.Admission!.Accept(); admissionWindow.Content = bRoot; aSurface.Lifetime!.Dispose();
            Check((await feature.PrepareToCloseAsync()).Succeeded, "accepted Present transfer observes saved previous-session retirement");
            var activeB = (PresentBrowserSession)bSurface.Bindings;
            Check(activeB.DocumentId == b.Id && activeB.DurableRevision == b.Version && activeB.IsPresentationAdmitted &&
                JsonSerializer.Serialize(activeB.GetDocumentSnapshot()) == JsonSerializer.Serialize((await repository.LoadAsync(b.Id, default))!),
                "accepted Present B preserves full stored owner model and exact canonical ID/revision");
            await feature.DisposeAsync();
            return count;
        }
        finally { admissionWindow.Close(); }
    }
    var media = new PresentTransferMedia();
    var session = new PresentBrowserSession(repository, null, new PresentPptxExportService(),
        new PresentPptxImportService(paths), media);
    var parser = new CuiRichParser();
    var document = parser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Present.cui")));
    foreach (var diagnostic in parser.Diagnostics.Diagnostics) Console.WriteLine(JsonSerializer.Serialize(diagnostic));
    Check(!parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error),
        "actual Present CUI parses without disconnected private controls");
    var surface = PresentBrowserSurface.Create(document, session, () => true);
    document = surface.Document;
    using var loader = new CuiControlLoader(surface.ControlRegistry!);
    var shellActions = new BrowserShellActions(session, () => { });
    loader.SetBindingContext(session); loader.SetActionDispatcher(shellActions);
    var (root, diagnostics) = loader.TryLoad(document);
    foreach (var diagnostic in diagnostics) Console.WriteLine(JsonSerializer.Serialize(diagnostic));
    Check(root is not null && !diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error),
        "actual registered owning Present canvas lowers through CUI");
    loader.WireBindings(root!); loader.WireBindings(root!);
    using var availability = BrowserActionAvailability.Observe(root!, loader, document, shellActions, session);
    var window = new Window { Width = 1200, Height = 900, Content = root };
    window.Show();
    try
    {
        using (var frame = window.CaptureRenderedFrame()) { Check(frame is not null, "real Present native frame renders original canvas/resource graph"); frame!.Save(Path.Combine(paths.DataDirectory, "present-empty.png")); }
        IEnumerable<Control> AuthoredTree(Control ancestor)
        {
            var visited = new HashSet<Control>(ReferenceEqualityComparer.Instance);
            var pending = new Stack<Control>(); pending.Push(ancestor);
            while (pending.TryPop(out var current))
            {
                if (!visited.Add(current)) continue;
                yield return current;
                foreach (var child in current.GetLogicalChildren().OfType<Control>()) pending.Push(child);
                if (current is CuiContainerControl container) foreach (var child in container.AuthoredChildren) pending.Push(child);
                if (current is ItemsControl items) foreach (var child in items.Items.OfType<Control>()) pending.Push(child);
            }
        }
        string ShapeFill(PresentElement element) => element.VectorShape?.Paths.FirstOrDefault()?.Fill.Color ?? element.Style.FillColor;
        Control ButtonNamed(string label)
        {
            var currentLabel = label switch { "Add text" => "Text box", "Add shape" => "Rectangle", "Move slide earlier" => "Move slide up", "Move slide later" => "Move slide down", "Delete element" => "Delete object", _ => label };
            bool LabelMatches(object? content, Control control) => Equals(content, currentLabel) ||
                AutomationProperties.GetName(control) is { } name && (name == currentLabel || name.StartsWith(currentLabel + " (", StringComparison.Ordinal));
            var controls = AuthoredTree(root!).Where(control => control switch
            {
                Button button => LabelMatches(button.Content, button) && (label != "Close" || Equals(button.Tag, "Close")),
                MenuItem item => Equals(item.Header, currentLabel),
                _ => false
            }).OrderBy(control => TopLevel.GetTopLevel(control) is not null && control.IsEffectivelyVisible
                ? 0 : control is MenuItem && control.GetLogicalAncestors().OfType<Menu>().Any(menu => menu.Name == "present-menu-bar") ? 1 : 2).ToArray();
            return controls.FirstOrDefault() ?? throw new InvalidOperationException("Missing authored command control: " + label);
        }
        async Task Click(string label, Func<bool> completed)
        {
            var button = ButtonNamed(label);
            Check(button.IsEnabled, "real Present " + label + " control enables its owning action");
            if (button is MenuItem menuItem)
            {
                var parents = new List<MenuItem>();
                Control child = menuItem;
                while (AuthoredTree(root!).OfType<MenuItem>().FirstOrDefault(item => item.Items.Contains(child)) is { } parent)
                { parents.Add(parent); child = parent; }
                foreach (var parent in parents.AsEnumerable().Reverse())
                {
                    if (parent.IsSubMenuOpen) continue;
                    var menuRoot = TopLevel.GetTopLevel(parent)!;
                    var menuPoint = parent.TranslatePoint(new Point(parent.Bounds.Width / 2, parent.Bounds.Height / 2), menuRoot)!.Value;
                    Console.WriteLine($"MENU {parent.Header}: bounds={parent.Bounds} point={menuPoint} hit={menuRoot.InputHitTest(menuPoint)}");
                    menuRoot.MouseMove(menuPoint); await Task.Delay(250);
                    if (!parent.IsSubMenuOpen) { menuRoot.MouseDown(menuPoint, MouseButton.Left); menuRoot.MouseUp(menuPoint, MouseButton.Left); }
                    using var menuFrame = window.CaptureRenderedFrame();
                    Check(parent.IsSubMenuOpen, "actual pointer opens parent menu for " + label);
                }
            }
            else if (TopLevel.GetTopLevel(button) is null)
            {
                await Click("Properties", () => { session.TryGetValue("ShowInspector", out var shown); return shown is true; });
                button = ButtonNamed(label);
            }
            button.BringIntoView(); Dispatcher.UIThread.RunJobs();
            using (var prepared = window.CaptureRenderedFrame()) { }
            var inputRoot = TopLevel.GetTopLevel(button) ?? throw new InvalidOperationException("Command remains detached: " + label);
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), inputRoot)!.Value;
            Console.WriteLine($"CLICK {label}: bounds={button.Bounds} point={point}");
            Console.WriteLine($"HIT {inputRoot.InputHitTest(point)}");
            var hit = inputRoot.InputHitTest(point) as Visual;
            Check(ReferenceEquals(hit, button) || hit?.GetVisualAncestors().Contains(button) == true,
                "physical pointer target belongs to the actual " + label + " control");
            inputRoot.MouseDown(point, MouseButton.Left); inputRoot.MouseUp(point, MouseButton.Left);
            if (label == "Add shape")
            {
                await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Check(root!.GetVisualDescendants().OfType<PresentRetainedSceneControl>().Single().InteractionState
                    == Haven.Desktop.Views.Pages.Present.PresentInteractionState.InsertionMode,
                    "physical shape gallery action has entered insertion mode before the placement gesture");
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); using (var prepared = window.CaptureRenderedFrame()) { }
                var insertionScene = root!.GetVisualDescendants().OfType<PresentRetainedSceneControl>().Single().GetVisualDescendants().OfType<HavenSceneControl>().Single();
                var paper = (HavenRect)insertionScene.Root!.GetType().GetMethod("SlideRectLocal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(insertionScene.Root, null)!;
                var place = insertionScene.TranslatePoint(new Point(paper.X + paper.Width * .42, paper.Y + paper.Height * .42), window)!.Value;
                var placementHit = window.InputHitTest(place) as Visual;
                Console.WriteLine($"SHAPE PLACE paper={paper} point={place} hit={placementHit} selected={string.Join(',', session.Editor!.SelectedElements.Select(element => element.Kind))}");
                Check(ReferenceEquals(placementHit, insertionScene) || placementHit?.GetVisualAncestors().Contains(insertionScene) == true,
                    "physical shape placement targets the actual editable slide");
                window.MouseDown(place, MouseButton.Left); window.MouseUp(place, MouseButton.Left);
                Console.WriteLine($"SHAPE PLACED selected={string.Join(',', session.Editor!.SelectedElements.Select(element => element.Kind))}");
            }
            Console.WriteLine($"AFTER CLICK status={session.Status}, error={session.ErrorCode}, busy={session.IsBusy}");
            if (session.LastOperationError is not null) Console.WriteLine(session.LastOperationError);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!completed() || session.IsBusy) await Task.Delay(10, timeout.Token);
            using var frame = window.CaptureRenderedFrame();
        }
        Check(ButtonNamed("New presentation").IsEnabled && !ButtonNamed("Save").IsEnabled && !ButtonNamed("Add slide").IsEnabled
            && !ButtonNamed("Delete slide").IsEnabled && !ButtonNamed("Move slide earlier").IsEnabled && !ButtonNamed("Move slide later").IsEnabled
            && !ButtonNamed("Delete element").IsEnabled && !ButtonNamed("Bring to front").IsEnabled && !ButtonNamed("Send to back").IsEnabled
            && !ButtonNamed("Export .pptx").IsEnabled,
            "empty real Present lifecycle enables Create and disables document-only controls, including .pptx export");
        bool HasStart() => root!.GetVisualDescendants().OfType<Control>().Any(control => control.Name == "present-start");
        bool HasCanvas() => root!.GetVisualDescendants().OfType<PresentRetainedSceneControl>().Any();
        Check(HasStart() && !HasCanvas(), "fresh Present activates a separate start view with no editor in layout, pointer or accessibility scope");
        await Click("Import .pptx", () => session.Status == "Import cancelled");
        Check(HasStart() && !HasCanvas() && session.DocumentId is null && (await repository.ListAsync(default)).Count == 0,
            "cancelled import leaves the start surface intact and creates no presentation");
        await Click("New presentation", () => session.DocumentId is not null);
        Check(!HasStart() && HasCanvas(), "the actual New action swaps start for the working editor instead of stacking them");
        var id = session.DocumentId!.Value;
        var first = (await repository.LoadAsync(id, default))!;
        Check(first.Version == session.DurableRevision && JsonSerializer.Serialize(first) == JsonSerializer.Serialize(session.GetDocumentSnapshot()),
            "actual New commits same opaque presentation ID/revision and complete original owner model");
        var scene = root!.GetVisualDescendants().OfType<PresentRetainedSceneControl>().Single().GetVisualDescendants().OfType<HavenSceneControl>().Single();
        var canvas = scene.Root!;
        if (args.Contains("structured", StringComparer.Ordinal))
        {
            await PresentStructuredRenderingChecks.Run(window, root!.GetVisualDescendants().OfType<PresentRetainedSceneControl>().Single(), session, Check);
            return count;
        }
        if (args.Contains("engine", StringComparer.Ordinal))
        {
            await PresentInteractionEngineChecks.Run(window, root!.GetVisualDescendants().OfType<PresentRetainedSceneControl>().Single(), session, Check);
            return count;
        }
        if (args.Contains("content-ui", StringComparer.Ordinal))
        {
            await PresentUiAcceptanceChecks.Run(session, window, root!, Click, Check, paths.DataDirectory);
            return count;
        }
        if (args.Contains("interaction", StringComparer.Ordinal))
        {
            var sceneControl = root!.GetVisualDescendants().OfType<PresentRetainedSceneControl>().Single();
            await Click("Add text", () => session.Editor!.SelectedElements.Any(item => item.Kind == PresentElementKind.Text));
            var owner = session.Editor!;
            var textId = owner.SelectedElements.Single().Id;
            PresentElement TextObject() => owner.SelectedSlide.Elements.Single(element => element.Id == textId);
            HavenRect Paper()
            {
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); using var frame = window.CaptureRenderedFrame();
                return (HavenRect)canvas.GetType().GetMethod("SlideRectLocal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, null)!;
            }
            Point At(double x, double y)
            {
                var paper = Paper();
                return scene.TranslatePoint(new Point(paper.X + x * paper.Width, paper.Y + y * paper.Height), window)!.Value;
            }
            var selectedText = TextObject();
            var body = At(selectedText.X + selectedText.Width * .3, selectedText.Y + selectedText.Height * .5);
            window.MouseDown(body, MouseButton.Left); window.MouseUp(body, MouseButton.Left);
            Check(!sceneControl.IsTextEditing, "single-click on an already-selected text object stays in object selection");
            // A separate press/drag must be outside the platform's double-click
            // interval; the immediately preceding click above tests selection.
            await Task.Delay(600);
            var oldX = selectedText.X; var oldY = selectedText.Y;
            window.MouseDown(body, MouseButton.Left); window.MouseMove(body + new Vector(70, 45));
            Check(!sceneControl.IsTextEditing && TextObject().X == oldX,
                "captured body drag previews movement without starting text editing or mutating history per move");
            window.MouseUp(body + new Vector(70, 45), MouseButton.Left);
            Check(TextObject().X > oldX && TextObject().Y > oldY, "selected text body drag authors actual movement");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
            Check(sceneControl.IsTextEditing, "Enter activates text editing for the selected object");
            window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a"); window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
            window.KeyTextInput("Keep this edited text");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "Escape"); window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "Escape");
            Check(!sceneControl.IsTextEditing && TextObject().Text == "Keep this edited text",
                "Escape exits editing and retains the user's typed text");
            foreach (var zoom in new[] { .5, 1d, 1.5, 2d })
            {
                sceneControl.SetZoom(zoom);
                var paper = Paper(); var item = TextObject();
                var edge = At(item.X + item.Width, item.Y + item.Height);
                var oldWidth = item.Width; var oldHeight = item.Height;
                window.MouseDown(edge, MouseButton.Left); window.MouseMove(edge + new Vector(18, 12));
                var drawing = new HavenDrawingContext(); ((IHavenDrawCommandSource)canvas).Draw(drawing, 1);
                var outline = drawing.Commands.OfType<HavenStrokeRoundedRectCommand>().Last(command => command.Pen.Thickness == 2);
                Check(outline.Rect.Width > oldWidth * paper.Width && outline.Rect.Height > oldHeight * paper.Height,
                    "resize previews actual geometry before release at zoom " + zoom);
                window.MouseUp(edge + new Vector(18, 12), MouseButton.Left);
                Check(Math.Abs(TextObject().Width - oldWidth - 18 / paper.Width) < .0001
                    && Math.Abs(TextObject().Height - oldHeight - 12 / paper.Height) < .0001,
                    "resize uses the measured paper transform at zoom " + zoom);
                Check(TextObject().Text == "Keep this edited text", "resize preserves authored text at zoom " + zoom);
            }
            await Click("Save", () => !session.IsDirty);
            await Click("Close", () => session.DocumentId is null);
            await Click(first.Title, () => session.DocumentId == id);
            Check(session.GetDocumentSnapshot()!.Slides[0].Elements.Single(element => element.Id == textId).Text == "Keep this edited text",
                "text kept by Escape survives actual save and reopening");
            return count;
        }
        if (args.Contains("manipulation", StringComparer.Ordinal))
        {
            await Click("Add shape", () => session.Editor!.SelectedElements.Any(item => item.Kind == PresentElementKind.Shape));
            await Click("Properties", () => { session.TryGetValue("ShowInspector", out var shown); return shown is true; });
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); using (var frame = window.CaptureRenderedFrame()) { }
            var manipulatedShape = session.Editor!.SelectedElements.Single();
            var beforeX = manipulatedShape.X; var beforeY = manipulatedShape.Y;
            var slideRect = (HavenRect)canvas.GetType().GetMethod("SlideRectLocal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, null)!;
            var shapePoint = scene.TranslatePoint(new Point(slideRect.X + (manipulatedShape.X + manipulatedShape.Width / 2) * slideRect.Width,
                slideRect.Y + (manipulatedShape.Y + manipulatedShape.Height / 2) * slideRect.Height), window)!.Value;
            window.MouseDown(shapePoint, MouseButton.Left); window.MouseUp(shapePoint, MouseButton.Left);
            Check(manipulatedShape.X == beforeX && manipulatedShape.Y == beforeY, "stationary pointer selection preserves exact manipulatedShape geometry");
            window.MouseDown(shapePoint, MouseButton.Left);
            window.MouseMove(shapePoint + new Vector(40, 30)); window.MouseUp(shapePoint + new Vector(40, 30), MouseButton.Left);
            Check(manipulatedShape.X > beforeX && manipulatedShape.Y > beforeY && session.Editor.CanUndo, "physical canvas drag still authors movement after the stationary-pointer guard");
            await Click("Undo", () => session.Editor!.SelectedElements.Single().X == beforeX && session.Editor.SelectedElements.Single().Y == beforeY);
            TypeInto(NumberBox("X (px)"), "0");
            TypeInto(NumberBox("Y (px)"), "0");
            manipulatedShape = session.Editor.SelectedElements.Single();
            Check(Math.Abs(manipulatedShape.X + manipulatedShape.Width / 2 - .5) < .00001 && Math.Abs(manipulatedShape.Y + manipulatedShape.Height / 2 - .5) < .00001,
                "actual Properties fields place the object centre at the slide centre");
            TypeInto(NumberBox("Y (px)"), "72");
            manipulatedShape = session.Editor.SelectedElements.Single();
            Check(Math.Abs((.5 - manipulatedShape.Y - manipulatedShape.Height / 2) * session.Editor.Document.SlideSize.HeightInches * 96 - 72) < .00001,
                "positive authored Y pixels move the object upwards using the real slide size");
            TypeInto(FillBox(), "#2244aa");
            Check(ShapeFill(session.Editor.SelectedElements.Single()) == "#2244aa", "typing a valid fill updates the real vector immediately");
            var previewDrawing = new HavenDrawingContext();
            PresentRetainedSceneControl.CreatePreviewRenderer()(previewDrawing, session.Editor.Document, session.Editor.SelectedSlide, new HavenRect(0, 0, 160, 90), 1);
            var previewScale = previewDrawing.Commands.OfType<HavenPushTransformCommand>().First().Transform;
            Check(Math.Abs(previewScale.ScaleX - previewScale.ScaleY) < .00001 && Math.Abs(previewScale.ScaleX - .125) < .00001,
                "thumbnail content uses one uniform transform of the complete canonical slide");
            Check(previewDrawing.Commands.OfType<HavenGeometryCommand>().Any(command => command.Fill == new HavenSolidBrush(255, 34, 68, 170)),
                "thumbnail paints the real authored shape fill instead of a generic accent swatch");
            await Click("Add text", () => session.Editor.SelectedElements.Any(item => item.Kind == PresentElementKind.Text));
            var textElement = session.Editor.SelectedElements.Single();
            await session.EditAsync(editor => editor.SetSelectedTextStyle(new PresentTextStyle
            {
                FontFamily = "Montserrat", FontSizePoints = 36, Bold = true, Underline = true,
                HorizontalAlignment = PresentTextHorizontalAlignment.Center, VerticalAlignment = PresentTextVerticalAlignment.Middle
            }));
            HavenTextCommand TextPaint()
            {
                var drawing = new HavenDrawingContext();
                PresentRetainedSceneControl.CreatePreviewRenderer()(drawing, session.Editor.Document, session.Editor.SelectedSlide, new HavenRect(0, 0, 640, 360), 1);
                return drawing.Commands.OfType<HavenTextCommand>().Single(command => command.Layout.Text == textElement.Text);
            }
            var textPaint = TextPaint();
            var paragraph = textPaint.Layout.Paragraph!;
            Check(paragraph is not null && paragraph.Runs[0].FontSize == 48 && paragraph.Runs[0].Underline,
                "36-point text uses 48 canonical pixels and retained underline styling");
            var line = paragraph!.Lines[0].Bounds;
            Check(Math.Abs(line.X - (textPaint.Rect.Width - line.Width) / 2) < 1,
                "center alignment positions the actual shaped glyphs in the text box");
            Check(Math.Abs(textPaint.Layout.ParagraphOrigin.Y - textPaint.Rect.Y - (textPaint.Rect.Height - paragraph.Size.Height) / 2) < 1,
                "middle alignment uses the actual shaped paragraph height");
            var caret = paragraph.CaretRect(2);
            Check(paragraph.HitTest(new HavenPoint(caret.X, caret.Y + caret.Height / 2)) == 2,
                "aligned text caret hit testing uses the same shaped glyph positions as painting");
            foreach (var (value, expected) in new (string, HavenSolidBrush)[]
            {
                ("#fff", new(255, 255, 255, 255)), ("#8fff", new(136, 255, 255, 255)),
                ("white", new(255, 255, 255, 255)), ("#ffffff", new(255, 255, 255, 255)),
                ("invalid-imported-colour", new(255, 31, 31, 31))
            })
            {
                await session.EditAsync(editor => editor.SetSelectedTextStyle(new PresentTextStyle { FontSizePoints = 36, Color = value }));
                Check(TextPaint().Brush == expected,
                    "actual paragraph renderer accepts literal or fallback colour without semantic-resource lookup: " + value);
            }
            var heightBefore = textElement.Height;
            TypeInto(NumberBox("Height (px)"), "220");
            Check(session.Editor.SelectedElements.Single().Height != heightBefore && TextPaint().Layout.FontSize == 48,
                "resizing the text box preserves authored point size rather than rescaling the font");
            await Click("Save", () => !session.IsDirty);
            await Click("Close", () => session.DocumentId is null);
            await Click(first.Title, () => session.DocumentId == id);
            var savedShape = session.GetDocumentSnapshot()!.Slides[0].Elements.Single(item => item.Id == manipulatedShape.Id);
            Check(savedShape.X == manipulatedShape.X && savedShape.Y == manipulatedShape.Y, "direct manipulation and Properties geometry survive save/reopen");
            await Click("Close", () => session.DocumentId is null);
            return count;
        }
        if (args.Contains("history", StringComparer.Ordinal))
        {
            TextBox DocumentTitle() => root!.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Presentation title");
            TypeInto(DocumentTitle(), "First saved title");
            await Click("Save", () => !session.IsDirty);
            var previousRevision = session.DurableRevision;
            TypeInto(DocumentTitle(), "Second saved title");
            await Click("Save", () => !session.IsDirty);
            var currentRevision = session.DurableRevision;
            await Click("Version history", () => { session.TryGetValue("ShowHistory", out var shown); return shown is true; });
            var versions = await ((IPresentVersionRepository)repository).ReadVersionsAsync(id, default);
            Check(versions.Select(version => version.Version).SequenceEqual(new[] { currentRevision, previousRevision }),
                "history reads actual current and previous durable revisions in order");
            var restores = root!.GetVisualDescendants().OfType<Button>().Where(button => Equals(button.Content, "Restore this version"))
                .OrderBy(button => button.TranslatePoint(default, window)!.Value.Y).ToArray();
            Check(restores.Length == 2, "history presents exactly the two available saved copies");
            using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(paths.DataDirectory, "version-history.png"));
            var restorePoint = restores[1].TranslatePoint(new Point(restores[1].Bounds.Width / 2, restores[1].Bounds.Height / 2), window)!.Value;
            window.MouseDown(restorePoint, MouseButton.Left); window.MouseUp(restorePoint, MouseButton.Left);
            await loader.WhenActionsIdleAsync(); Dispatcher.UIThread.RunJobs();
            Check(session.GetDocumentSnapshot()!.Title == "First saved title" && session.IsDirty && session.DurableRevision == currentRevision,
                "physical previous-version restore changes the owner draft without rewinding its durable CAS revision");
            Check((await repository.LoadAsync(id, default))!.Title == "Second saved title", "restoring never silently overwrites durable content");
            await Click("Undo", () => session.GetDocumentSnapshot()!.Title == "Second saved title");
            await Click("Redo", () => session.GetDocumentSnapshot()!.Title == "First saved title");
            await Click("Save", () => !session.IsDirty);
            Check(session.DurableRevision > currentRevision, "saving restored content commits a new revision");
            await Click("Close", () => session.DocumentId is null);
            await Click("First saved title", () => session.DocumentId == id);
            Check(session.GetDocumentSnapshot()!.Title == "First saved title", "restored content reopens from its original durable document identity");
            await Click("Close", () => session.DocumentId is null);
            return count;
        }
        if (args.Contains("viewport", StringComparer.Ordinal))
        {
            var accessibility = new BrowserAccessibilityBridge(); accessibility.Bind(root!);
            HavenRect CurrentSlideRect() => (HavenRect)canvas.GetType().GetMethod("SlideRectLocal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, null)!;
            void FitFrame(string name)
            {
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                using (var firstFrame = window.CaptureRenderedFrame()) { }
                window.UpdateLayout();
                using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(paths.DataDirectory, name + ".png"));
                var rect = CurrentSlideRect();
                var stage = canvas.Bounds;
                using var semantics = JsonDocument.Parse(accessibility.ReadSnapshot());
                // The unified Insert menu owns slide, text and shape actions; they are exercised through real menus below.
                foreach (var commandName in new[] { "Insert content", "Properties" })
                    Check(semantics.RootElement.GetProperty("elements").EnumerateArray().Any(element => element.GetProperty("name").GetString() == commandName),
                        name + ": actual toolbar semantics retain " + commandName);
                if (session.TryGetValue("ShowSidebar", out var sidebar) && sidebar is true)
                    Check(semantics.RootElement.GetProperty("elements").EnumerateArray().Any(element => element.GetProperty("automationId").GetString()?.StartsWith("Present.Rail.", StringComparison.Ordinal) == true),
                        name + ": actual retained thumbnail root exposes its virtual slide actions");
                Console.WriteLine($"FIT {name}: scene={scene.Bounds} stage={stage} slide={rect}");
                var ratio = session.Editor!.Document.SlideSize.WidthInches / session.Editor.Document.SlideSize.HeightInches;
                var expected = Math.Min(stage.Width - 32, (stage.Height - 32) * ratio);
                Check(rect.Width > 200 && Math.Abs(rect.Width - expected) < .1 && Math.Abs(rect.Width / rect.Height - ratio) < .00001,
                    name + ": actual editable slide fits the measured usable stage at full aspect-preserving size");
                Check(Math.Abs(rect.X + rect.Width / 2 - stage.Width / 2) < .1 && Math.Abs(rect.Y + rect.Height / 2 - stage.Height / 2) < .1,
                    name + ": slide is centred on both axes within the actual stage");
                Check(Math.Abs(scene.Bounds.Width - stage.Width) < 1 && Math.Abs(scene.Bounds.Height - stage.Height) < 1,
                    name + ": retained document canvas fills its native viewport instead of measuring from empty content");
            }
            FitFrame("fit-initial");
            Check(session.TryGetValue("ShowNotes", out var initialNotes) && initialNotes is true, "initial editor shows the real notes field");
            await Click("Collapse Slide Notes", () => { session.TryGetValue("ShowNotes", out var shown); return shown is false; }); FitFrame("fit-notes-collapsed");
            await Click("Add slide", () => session.Editor!.Document.Slides.Count == 2); FitFrame("fit-add");
            await Click("Duplicate slide", () => session.Editor!.Document.Slides.Count == 3); FitFrame("fit-duplicate");
            var selected = session.Editor!.Selection.SlideId;
            await Click("Previous slide", () => session.Editor!.Selection.SlideId != selected); FitFrame("fit-switch");
            await Click("Expand Slide Notes", () => { session.TryGetValue("ShowNotes", out var shown); return shown is true; }); FitFrame("fit-notes");
            window.Width = 390; FitFrame("fit-390");
            window.Width = 1280; FitFrame("fit-1280");
            await Click("Properties", () => { session.TryGetValue("ShowInspector", out var shown); return shown is true; });
            window.Width = 900; window.Height = 650; FitFrame("fit-900-properties");
            await Click("Close properties", () => { session.TryGetValue("ShowInspector", out var shown); return shown is false; });
            window.Width = 390; window.Height = 844; FitFrame("fit-390-after-properties");
            window.Width = 1280; window.Height = 900; FitFrame("fit-restored-after-properties");
            await Click("Properties", () => { session.TryGetValue("ShowInspector", out var shown); return shown is true; }); FitFrame("fit-properties");
            await Click("Collapse Slide Notes", () => { session.TryGetValue("ShowNotes", out var shown); return shown is false; }); FitFrame("fit-properties-no-notes");
            await Click("Close properties", () => { session.TryGetValue("ShowInspector", out var shown); return shown is false; });
            var beforeZoom = root!.GetVisualDescendants().OfType<PresentRetainedSceneControl>().Single().EffectiveZoom;
            await Click("Zoom in", () => !session.IsFitToWindow && session.CanvasZoom > beforeZoom);
            using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(paths.DataDirectory, "zoom-in.png"));
            Check(CurrentSlideRect().Width > 200, "zoom-in retains the real editable slide");
            await Click("Fit slide", () => session.IsFitToWindow); FitFrame("fit-after-zoom");
            beforeZoom = root!.GetVisualDescendants().OfType<PresentRetainedSceneControl>().Single().EffectiveZoom;
            await Click("Zoom out", () => !session.IsFitToWindow && session.CanvasZoom < beforeZoom);
            using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(paths.DataDirectory, "zoom-out.png"));
            var small = CurrentSlideRect();
            await Click("Fit slide", () => session.IsFitToWindow); FitFrame("fit-after-zoom-out");
            Check(CurrentSlideRect().Width > small.Width, "Fit restores the full calculated scale after zoom-out");
            await Click("Save", () => !session.IsDirty);
            await Click("Close", () => session.DocumentId is null);
            await Click("Untitled presentation", () => session.DocumentId == id); FitFrame("fit-reopen");
            Check(session.Editor!.Document.Slides.Count == 3, "reopened owner document preserves all three slides");
            await Click("Close", () => session.DocumentId is null);
            return count;
        }
        var contentDrawing = new HavenDrawingContext();
        ((IHavenDrawCommandSource)canvas).Draw(contentDrawing, 1);
        var titleDrawing = contentDrawing.Commands.OfType<HavenTextCommand>().Single(command => command.Layout.Text == first.Slides[0].Title);
        Check(titleDrawing.Brush == new HavenSolidBrush(255, 31, 31, 31)
            && titleDrawing.Layout.FontFamily == first.Theme.HeadingFontFamily,
            "slide title uses its saved document foreground and heading font, independent of dark application chrome");
        var local = (HavenRect)canvas.GetType().GetMethod("SlideRectLocal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, null)!;
        // Query the actual owning layout; no duplicated slide positioning model.
        var title = (HavenRect)canvas.GetType().GetMethod("TitleRect", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, [local])!;
        var titlePoint = scene.TranslatePoint(new Point(canvas.Bounds.X + title.X + title.Width / 2,
            canvas.Bounds.Y + title.Y + title.Height / 2), window)!.Value;
        window.MouseDown(titlePoint, MouseButton.Left); window.MouseUp(titlePoint, MouseButton.Left);
        window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
        window.KeyTextInput("Actual native slide title");
        Check(session.GetDocumentSnapshot()!.Slides[0].Title == "Actual native slide title" && session.IsDirty,
            "physical native title selection and routed raw typing preview owning canonical slide text");
        window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, "Enter");
        Check(session.Editor!.CanUndo && !session.Editor.IsLiveTextEditActive, "actual retained text commit creates owning undo transaction");
        var retainedEditor = session.Editor;
        var retainedDraft = JsonSerializer.Serialize(session.GetDocumentSnapshot());
        var retainedRevision = session.DurableRevision;
        await Click("Back to Present start", () => !session.IsWorkspaceActive);
        using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(paths.DataDirectory, "present-return-to-start.png"));
        Check(HasStart() && !HasCanvas() && !scene.IsEnabled && session.IsDirty && session.DurableRevision == retainedRevision,
            "return-to-start detaches and disables the editor without saving the dirty draft");
        window.KeyTextInput("inactive editor must not change");
        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, "Delete");
        Check(JsonSerializer.Serialize(session.GetDocumentSnapshot()) == retainedDraft && session.IsActionAvailable("AddShape") == false
            && !session.TrySetValue("SpeakerNotes", "inactive write") && !((await session.EditAsync(editor => { editor.AddSlide(); return true; })).Succeeded),
            "inactive document shortcuts, bindings and editing requests cannot mutate the retained presentation");
        Check((await repository.LoadAsync(id, default))!.Slides[0].Title == first.Slides[0].Title,
            "return-to-start leaves durable presentation bytes at the acknowledged revision");
        await Click("Resume presentation", () => session.IsWorkspaceActive);
        Check(!HasStart() && HasCanvas() && ReferenceEquals(retainedEditor, session.Editor) && session.Editor!.CanUndo
            && JsonSerializer.Serialize(session.GetDocumentSnapshot()) == retainedDraft,
            "resume reattaches the same editor with its complete draft and undo history");
        await Click("Back to Present start", () => !session.IsWorkspaceActive);
        await Click(first.Title, () => session.IsWorkspaceActive);
        Check(ReferenceEquals(retainedEditor, session.Editor) && JsonSerializer.Serialize(session.GetDocumentSnapshot()) == retainedDraft,
            "choosing the currently open recent presentation resumes without reloading or replacing its draft");
        var bodyPoint = scene.TranslatePoint(new Point(canvas.Bounds.X + local.X + local.Width * 0.5,
            canvas.Bounds.Y + local.Y + local.Height * 0.55), window)!.Value;
        var beforeSelection = JsonSerializer.Serialize(session.GetDocumentSnapshot());
        window.MouseDown(bodyPoint, MouseButton.Left); window.MouseUp(bodyPoint, MouseButton.Left);
        Check(beforeSelection == JsonSerializer.Serialize(session.GetDocumentSnapshot()),
            "a pointer selection without a drag cannot move or resize authored slide content");
        Check(session.Editor!.Selection.ElementIds.Count == 1
            && ButtonNamed("Delete element").IsEnabled && ButtonNamed("Rotate right").IsEnabled && ButtonNamed("Bold").IsEnabled,
            "actual retained canvas selection notifies the session so the object controls follow the native selection");
        var emptyPoint = scene.TranslatePoint(new Point(canvas.Bounds.X + local.X + local.Width * 0.5,
            canvas.Bounds.Y + local.Y + local.Height * 0.92), window)!.Value;
        window.MouseDown(emptyPoint, MouseButton.Left); window.MouseUp(emptyPoint, MouseButton.Left);
        Check(session.Editor!.Selection.ElementIds.Count == 0
            && !ButtonNamed("Delete element").IsEnabled && !ButtonNamed("Bold").IsEnabled,
            "clearing the selection on empty canvas disables the object controls again");
        Check(session.TryGetValue("ShowNotes", out var initialNotesVisible) && initialNotesVisible is true, "initial notes region is visible instead of a hidden placeholder");
        await Click("Collapse Slide Notes", () => { session.TryGetValue("ShowNotes", out var shown); return shown is false; });
        await Click("Expand Slide Notes", () => { session.TryGetValue("ShowNotes", out var shown); return shown is true; });
        var notes = root!.GetVisualDescendants().OfType<TextBox>()
            .Single(box => AutomationProperties.GetName(box) == "Slide Notes");
        var notesPoint = notes.TranslatePoint(new Point(notes.Bounds.Width / 2, notes.Bounds.Height / 2), window)!.Value;
        window.MouseDown(notesPoint, MouseButton.Left); window.MouseUp(notesPoint, MouseButton.Left);
        window.KeyTextInput("Actual speaker notes");
        Check(session.GetDocumentSnapshot()!.Slides[0].SpeakerNotes == "Actual speaker notes", "actual CUI two-way notes input invokes owning typed notes operation");
        var originalSlide = session.Editor.Selection.SlideId;
        await Click("Duplicate slide", () => session.GetDocumentSnapshot()!.Slides.Count == 2);
        var duplicated = session.Editor.SelectedSlide;
        Check(duplicated.Id != originalSlide && !duplicated.Elements.Select(element => element.Id)
            .Intersect(session.Editor.Document.Slides.Single(slide => slide.Id == originalSlide).Elements.Select(element => element.Id)).Any(),
            "native Duplicate allocates independent slide/object IDs through owning editor");
        session.TrySetValue("SpeakerNotes", "Second slide notes");
        var thumbnailScene = root!.GetVisualDescendants().OfType<PresentThumbnailControl>().Single().GetVisualDescendants().OfType<HavenSceneControl>().Single();
        var thumbnailDrawing = new HavenDrawingContext();
        ((IHavenDrawCommandSource)thumbnailScene.Root!).Draw(thumbnailDrawing, 1);
        // Preview content uses canonical document point sizes before its uniform transform;
        // the separate 11 px rail caption deliberately uses UI selection colours.
        var thumbnailText = thumbnailDrawing.Commands.OfType<HavenTextCommand>().Where(command => command.Layout.Text == session.Editor!.SelectedSlide.Title && command.Layout.FontSize > 11).ToArray();
        Check(thumbnailText.Length > 0 && thumbnailText.All(command => command.Brush == new HavenSolidBrush(255, 31, 31, 31)),
            "slide preview text retains document colours independently of its surrounding selection chrome");
        thumbnailScene.FocusElement(thumbnailScene.Root!);
        window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, "Home");
        Check(session.Editor.Selection.SlideId == originalSlide && notes.Text == "Actual speaker notes",
            "real thumbnail keyboard selection updates canonical active slide and bound notes");
        var canvasHost = root!.GetVisualDescendants().OfType<PresentRetainedSceneControl>().Single();
        Check((Guid?)typeof(PresentRetainedSceneControl).GetField("_renderedSlide", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(canvasHost) == originalSlide,
            "thumbnail navigation refreshes the actual retained canvas to the same stable SlideID");
        window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, "End");
        Check(session.Editor.Selection.SlideId == duplicated.Id && notes.Text == "Second slide notes", "thumbnail End selects last slide and refreshes notes without document mutation");
        using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(paths.DataDirectory, "present-editor-desktop.png"));
        await session.DispatchAsync("Undo", null); // Restore duplicate notes before exercising the original slide-history checks.
        await Click("Undo", () => session.GetDocumentSnapshot()!.Slides.Count == 1);
        await Click("Redo", () => session.GetDocumentSnapshot()!.Slides.Count == 2);
        Check(session.DurableRevision == first.Version && session.GetDocumentSnapshot()!.Version == first.Version,
            "native content Undo/Redo cannot reset the actual acknowledged CAS base");
        var selectedAt = session.GetDocumentSnapshot()!.Slides.FindIndex(slide => slide.Id == session.Editor!.Selection.SlideId);
        Check(ButtonNamed("Move slide earlier").IsEnabled == selectedAt > 0
            && ButtonNamed("Move slide later").IsEnabled == selectedAt < session.GetDocumentSnapshot()!.Slides.Count - 1,
            "native deck boundary availability tracks the selected slide against both deck ends");
        var movedId = session.Editor!.Selection.SlideId;
        var movedFrom = session.GetDocumentSnapshot()!.Slides.FindIndex(slide => slide.Id == movedId);
        var moveDirection = movedFrom > 0 ? "Move slide earlier" : "Move slide later";
        var movedTo = movedFrom > 0 ? movedFrom - 1 : movedFrom + 1;
        await Click(moveDirection, () =>
            session.GetDocumentSnapshot()!.Slides.FindIndex(slide => slide.Id == movedId) == movedTo
            && session.Editor!.Selection.SlideId == movedId);
        var movedAt = session.GetDocumentSnapshot()!.Slides.FindIndex(slide => slide.Id == movedId);
        Check(ButtonNamed("Move slide earlier").IsEnabled == movedAt > 0
            && ButtonNamed("Move slide later").IsEnabled == movedAt < session.GetDocumentSnapshot()!.Slides.Count - 1,
            "native slide move reorders the retained deck and re-evaluates both boundary controls");
        await Click(movedTo > 0 ? "Move slide earlier" : "Move slide later", () =>
            session.GetDocumentSnapshot()!.Slides.FindIndex(slide => slide.Id == movedId) == movedFrom
            && session.Editor!.Selection.SlideId == movedId);
        var deletedId = session.Editor!.Selection.SlideId;
        await Click("Delete slide", () => session.GetDocumentSnapshot()!.Slides.Count == 1);
        Check(!session.Editor!.Document.Slides.Any(slide => slide.Id == deletedId)
            && session.Editor.Selection.SlideId == session.GetDocumentSnapshot()!.Slides[0].Id,
            "native Delete removes the selected slide and selects the surviving slide");
        var titleBox = root!.GetVisualDescendants().OfType<TextBox>()
            .Single(box => AutomationProperties.GetName(box) == "Presentation title");
        var titleInput = titleBox.TranslatePoint(new Point(titleBox.Bounds.Width / 2, titleBox.Bounds.Height / 2), window)!.Value;
        window.MouseDown(titleInput, MouseButton.Left); window.MouseUp(titleInput, MouseButton.Left);
        window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
        window.KeyTextInput("Actual presentation title");
        Check(session.GetDocumentSnapshot()!.Title == "Actual presentation title" && session.IsDirty,
            "actual CUI two-way title input invokes owning typed presentation title operation");
        var elementCount = session.GetDocumentSnapshot()!.Slides[0].Elements.Count;
        await Click("Add text", () => session.GetDocumentSnapshot()!.Slides[0].Elements.Count == elementCount + 1);
        var addedText = session.Editor!.Selection.ElementIds.Single();
        Check(ButtonNamed("Bold").IsEnabled && ButtonNamed("Italic").IsEnabled,
            "text object selection enables the owning bold and italic style commands");
        await Click("Bold", () => session.GetDocumentSnapshot()!.Slides[0].Elements
            .Single(element => element.Id == addedText).TextStyle.Bold);
        await Click("Italic", () => session.GetDocumentSnapshot()!.Slides[0].Elements
            .Single(element => element.Id == addedText).TextStyle.Italic);
        Check(session.GetDocumentSnapshot()!.Slides[0].Elements.Single(element => element.Id == addedText)
                .TextStyle is { Bold: true, Italic: true },
            "native Bold and Italic toggle only the selected text object's own style");
        await Click("Underline", () => session.Editor!.SelectedElements.Single().TextStyle.Underline);
        Check(session.Editor!.SelectedElements.Single().TextStyle is { Bold: true, Italic: true, Underline: true },
            "underline invokes the text owner and preserves bold and italic");
        await Click("Underline", () => !session.Editor!.SelectedElements.Single().TextStyle.Underline);
        await Click("Center text", () => session.Editor!.SelectedElements.Single().TextStyle.HorizontalAlignment == PresentTextHorizontalAlignment.Center);
        await Click("Align text left", () => session.Editor!.SelectedElements.Single().TextStyle.HorizontalAlignment == PresentTextHorizontalAlignment.Left);
        await Click("Copy", () => session.Status == "Copied selected presentation object(s).");
        await Click("Delete element", () => session.GetDocumentSnapshot()!.Slides[0].Elements.Count == elementCount);
        Check(ButtonNamed("Paste").IsEnabled && !ButtonNamed("Copy").IsEnabled,
            "actual Copy stores the object payload so Paste enables while an empty selection disables Copy");
        await Click("Paste", () => session.GetDocumentSnapshot()!.Slides[0].Elements.Count == elementCount + 1);
        var pastedText = session.Editor!.Selection.ElementIds.Single();
        Check(pastedText != addedText && session.GetDocumentSnapshot()!.Slides[0].Elements
                .Single(element => element.Id == pastedText).TextStyle is { Bold: true, Italic: true },
            "actual Paste rebuilds the copied object under a new identity with its own style");
        Check(ButtonNamed("Delete element").IsEnabled && ButtonNamed("Bring to front").IsEnabled && ButtonNamed("Send to back").IsEnabled,
            "native element controls enable while the retained canvas selection is non-empty");
        await Click("Delete element", () => session.GetDocumentSnapshot()!.Slides[0].Elements.Count == elementCount);
        Check(session.Editor!.Selection.ElementIds.Count == 0
            && !ButtonNamed("Delete element").IsEnabled && !ButtonNamed("Bring to front").IsEnabled && !ButtonNamed("Send to back").IsEnabled,
            "native Delete element removes the selected object and disables the element controls once nothing is selected");
        await Click("Add shape", () => session.GetDocumentSnapshot()!.Slides[0].Elements.Count == elementCount + 1);
        var shape = session.Editor!.Selection.ElementIds.Single();
        Check(ButtonNamed("Bold").IsEnabled && ButtonNamed("Italic").IsEnabled
            && ButtonNamed("Grow").IsEnabled && ButtonNamed("Rotate right").IsEnabled,
            "editable shape text and object transforms retain their original owner command capabilities");
        var originalWidth = session.GetDocumentSnapshot()!.Slides[0].Elements.Single(element => element.Id == shape).Width;
        await Click("Grow", () => session.GetDocumentSnapshot()!.Slides[0].Elements
            .Single(element => element.Id == shape).Width > originalWidth);
        await Click("Shrink", () => Math.Abs(session.GetDocumentSnapshot()!.Slides[0].Elements
            .Single(element => element.Id == shape).Width - originalWidth) < 0.000001);
        await Click("Rotate right", () => session.GetDocumentSnapshot()!.Slides[0].Elements
            .Single(element => element.Id == shape).RotationDegrees == 15);
        await Click("Rotate left", () => session.GetDocumentSnapshot()!.Slides[0].Elements
            .Single(element => element.Id == shape).RotationDegrees == 0);
        double NudgeX() => session.GetDocumentSnapshot()!.Slides[0].Elements.Single(element => element.Id == shape).X;
        double NudgeY() => session.GetDocumentSnapshot()!.Slides[0].Elements.Single(element => element.Id == shape).Y;
        async Task Nudge(string label, double dx, double dy)
        {
            var expected = PresentSnapEngine.Snap(session.Editor!.SelectedSlide, shape, NudgeX() + dx, NudgeY() + dy);
            await Click(label, () => Math.Abs(NudgeX() - expected.X) < 0.000001 && Math.Abs(NudgeY() - expected.Y) < 0.000001);
            Check(session.Editor.Selection.ElementIds.Contains(shape), "native nudge retains the owning selected object");
        }
        // Native chrome uses the owner's snap guides. A reverse nudge need not
        // return to the old point when the first nudge snaps to a nearby object.
        await Nudge("Move object right", 0.01, 0);
        await Nudge("Move object left", -0.01, 0);
        await Nudge("Move object down", 0, 0.01);
        await Nudge("Move object up", 0, -0.01);
        await Click("Send to back", () => session.GetDocumentSnapshot()!.Slides[0].Elements[0].Id == shape);
        Check(session.Editor!.Selection.ElementIds.Contains(shape),
            "native Send to back moves the selected object to the bottom of the stack and keeps it selected");
        await Click("Bring to front", () => session.GetDocumentSnapshot()!.Slides[0].Elements[^1].Id == shape);
        await Click("Delete element", () => session.GetDocumentSnapshot()!.Slides[0].Elements.Count == elementCount);
        Check(!session.GetDocumentSnapshot()!.Slides[0].Elements.Any(element => element.Id == shape)
            && !session.GetDocumentSnapshot()!.Slides[0].Elements.Any(element => element.Id == addedText)
            && session.Editor!.Selection.ElementIds.Count == 0,
            "native Delete element restores the slide's original object stack");
        void SelectOnCanvas(double nx, double ny, bool additive)
        {
            using (var frame = window.CaptureRenderedFrame()) { }
            var currentSlide = (HavenRect)canvas.GetType().GetMethod("SlideRectLocal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, null)!;
            var point = scene.TranslatePoint(new Point(canvas.Bounds.X + currentSlide.X + currentSlide.Width * nx,
                canvas.Bounds.Y + currentSlide.Y + currentSlide.Height * ny), window)!.Value;
            var before = JsonSerializer.Serialize(session.GetDocumentSnapshot());
            window.MouseDown(point, MouseButton.Left, additive ? RawInputModifiers.Shift : RawInputModifiers.None);
            window.MouseUp(point, MouseButton.Left, additive ? RawInputModifiers.Shift : RawInputModifiers.None);
            if (before != JsonSerializer.Serialize(session.GetDocumentSnapshot()))
            {
                File.WriteAllText(Path.Combine(paths.DataDirectory, "selection-before.json"), before);
                File.WriteAllText(Path.Combine(paths.DataDirectory, "selection-after.json"), JsonSerializer.Serialize(session.GetDocumentSnapshot()));
            }
            Check(before == JsonSerializer.Serialize(session.GetDocumentSnapshot()),
                "canvas selection preserves authored geometry, including while the properties panel is open");
        }
        double XOf(Guid id) => session.GetDocumentSnapshot()!.Slides[0].Elements
            .Single(element => element.Id == id).X;
        TextBox FillBox() => root!.GetVisualDescendants().OfType<TextBox>()
            .Single(box => AutomationProperties.GetName(box) == "Fill color");
        TextBox StrokeBox() => root!.GetVisualDescendants().OfType<TextBox>()
            .Single(box => AutomationProperties.GetName(box) == "Stroke color");
        TextBox NumberBox(string name) => root!.GetVisualDescendants().OfType<NumericUpDown>()
            .Single(box => AutomationProperties.GetName(box) == name).GetVisualDescendants().OfType<TextBox>().Single();
        TextBox StrokeWidthBox() => NumberBox("Stroke width");
        void TypeInto(TextBox box, string text)
        {
            box.BringIntoView(); Dispatcher.UIThread.RunJobs();
            using (var frame = window.CaptureRenderedFrame()) { }
            var point = box.TranslatePoint(new Point(box.Bounds.Width / 2, box.Bounds.Height / 2), window)!.Value;
            var hit = window.InputHitTest(point) as Visual;
            Check(ReferenceEquals(hit, box) || hit?.GetVisualAncestors().Contains(box) == true,
                "physical input point belongs to the actual " + AutomationProperties.GetName(box) + " field");
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
            window.KeyTextInput(text);
        }
        double FontSizeOf(Guid id) => session.GetDocumentSnapshot()!.Slides[0].Elements
            .Single(element => element.Id == id).TextStyle.FontSizePoints;
        await Click("Add shape", () => session.GetDocumentSnapshot()!.Slides[0].Elements.Count == elementCount + 1);
        var propShape = session.Editor!.Selection.ElementIds.Single();
        if (session.TryGetValue("ShowInspector", out var visibleInspector) && visibleInspector is false)
            await Click("Properties", () => { session.TryGetValue("ShowInspector", out var shown); return shown is true; });
        Check(ButtonNamed("Font larger").IsEnabled && ButtonNamed("Font smaller").IsEnabled && FillBox().IsEnabled
                && session.TryGetValue("ShowShapeToolbar", out var shapeToolbar) && shapeToolbar is true
                && session.TryGetValue("ShowTextToolbar", out var textToolbar) && textToolbar is false,
            "shape selection exposes its object toolbar and immediate fill while retaining shape-text formatting commands");
        TypeInto(FillBox(), "#336699");
        Check(session.TryGetValue("FillColor", out var typedFill) && Equals(typedFill, "#336699")
                && ShapeFill(session.GetDocumentSnapshot()!.Slides[0].Elements.Single(element => element.Id == propShape)) == "#336699",
            "actual fill field and owning vector paint update together without an Apply action");
        Check(StrokeBox().IsEnabled && StrokeWidthBox().IsEnabled,
            "shape selection enables immediate outline colour and width");
        PresentElement StyledShape() => session.GetDocumentSnapshot()!.Slides[0].Elements.Single(element => element.Id == propShape);
        TypeInto(StrokeBox(), "#202020");
        Check(StyledShape().VectorShape!.Paths.All(path => path.Stroke.Enabled && path.Stroke.Color == "#202020"),
            "actual outline field writes every path of the selected real vector immediately");
        var originalStrokeWidth = StyledShape().VectorShape!.Paths[0].Stroke.Width;
        var editedStrokeWidth = originalStrokeWidth + 2;
        TypeInto(StrokeWidthBox(), editedStrokeWidth.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Check(StyledShape().VectorShape!.Paths.All(path => path.Stroke.Width == editedStrokeWidth),
            "actual outline-width field immediately updates vector geometry styling");
        await Click("Undo", () => StyledShape().VectorShape!.Paths.All(path => path.Stroke.Width == originalStrokeWidth));
        await Click("Redo", () => StyledShape().VectorShape!.Paths.All(path => path.Stroke.Width == editedStrokeWidth));
        await Click("Delete element", () => session.GetDocumentSnapshot()!.Slides[0].Elements.Count == elementCount);
        SelectOnCanvas(0.70, 0.60, additive: false);
        var propBody = session.Editor!.Selection.ElementIds.Single();
        Check(ButtonNamed("Font larger").IsEnabled && ButtonNamed("Font smaller").IsEnabled
            && !root!.GetVisualDescendants().OfType<TextBox>().Any(box => AutomationProperties.GetName(box) == "Fill color")
            && !root!.GetVisualDescendants().OfType<TextBox>().Any(box => AutomationProperties.GetName(box) == "Stroke color")
            && !root!.GetVisualDescendants().OfType<NumericUpDown>().Any(box => AutomationProperties.GetName(box) == "Stroke width"),
            "text object selection enables the font size controls and disables the shape property controls");
        var originalFontSize = FontSizeOf(propBody);
        await Click("Font larger", () => FontSizeOf(propBody) == originalFontSize + 2);
        await Click("Font smaller", () => FontSizeOf(propBody) == originalFontSize);
        Check(FontSizeOf(propBody) == originalFontSize,
            "actual Font larger and Font smaller move the selected text's own point size by two and back");
        await Click("Add shape", () => session.GetDocumentSnapshot()!.Slides[0].Elements.Count == elementCount + 1);
        var arrangeShape = session.Editor!.Selection.ElementIds.Single();
        await Click("Add text", () => session.GetDocumentSnapshot()!.Slides[0].Elements.Count == elementCount + 2);
        var arrangeText = session.Editor!.Selection.ElementIds.Single();
        var arrangeBody = session.GetDocumentSnapshot()!.Slides[0].Elements[0].Id;
        Check(!ButtonNamed("Align left").IsEnabled && !ButtonNamed("Distribute horizontally").IsEnabled
            && !ButtonNamed("Group").IsEnabled && !ButtonNamed("Ungroup").IsEnabled
            && ButtonNamed("Bring forward").IsEnabled && ButtonNamed("Send backward").IsEnabled,
            "single object selection enables the z-order nudge and disables the multi-object arrange controls");
        SelectOnCanvas(0.30, 0.39, additive: true);
        Check(session.Editor!.Selection.ElementIds.Count == 2
            && ButtonNamed("Align left").IsEnabled && ButtonNamed("Group").IsEnabled
            && !ButtonNamed("Distribute horizontally").IsEnabled,
            "two-object selection enables align and group while distribute waits for a third object");
        await Click("Align left", () => XOf(arrangeText) == XOf(arrangeShape));
        Check(XOf(arrangeText) == XOf(arrangeShape),
            "actual Align left pulls both selected objects onto one left edge");
        SelectOnCanvas(0.70, 0.60, additive: true);
        Check(session.Editor!.Selection.ElementIds.Count == 3 && ButtonNamed("Distribute horizontally").IsEnabled,
            "third selected object enables the actual Distribute horizontally control");
        await Click("Distribute horizontally", () => new[] { XOf(arrangeBody), XOf(arrangeShape), XOf(arrangeText) }
            .Distinct().Count() == 3);
        Check(new[] { XOf(arrangeBody), XOf(arrangeShape), XOf(arrangeText) }.Distinct().Count() == 3,
            "actual Distribute horizontally separates the selected left edges again");
        await Click("Group", () => session.Editor!.Selection.ElementIds.Count == 1 &&
            session.GetDocumentSnapshot()!.Slides[0].Elements.Any(element =>
                element.Id == session.Editor!.Selection.ElementIds[0] && element.Kind == PresentElementKind.Group));
        Check(session.GetDocumentSnapshot()!.Slides[0].Elements.Count == elementCount + 3,
            "actual Group nests the three selected objects under one group object");
        await Click("Ungroup", () => session.GetDocumentSnapshot()!.Slides[0].Elements.Count == elementCount + 2 &&
            !session.GetDocumentSnapshot()!.Slides[0].Elements.Any(element => element.Kind == PresentElementKind.Group));
        Check(session.Editor!.Selection.ElementIds.Count == 0 && !ButtonNamed("Align left").IsEnabled,
            "actual Ungroup removes the group object and clears the selection");
        SelectOnCanvas(0.70, 0.60, additive: false);
        var arrangeBodyIndex = session.GetDocumentSnapshot()!.Slides[0].Elements.FindIndex(element => element.Id == arrangeBody);
        await Click("Bring forward", () => session.GetDocumentSnapshot()!.Slides[0].Elements
            .FindIndex(element => element.Id == arrangeBody) == arrangeBodyIndex + 1);
        Check(session.GetDocumentSnapshot()!.Slides[0].Elements
                .FindIndex(element => element.Id == arrangeBody) == arrangeBodyIndex + 1,
            "actual Bring forward moves the object one step up the stack");
        await Click("Send backward", () => session.GetDocumentSnapshot()!.Slides[0].Elements
            .FindIndex(element => element.Id == arrangeBody) == arrangeBodyIndex);
        Check(session.GetDocumentSnapshot()!.Slides[0].Elements
                .FindIndex(element => element.Id == arrangeBody) == arrangeBodyIndex,
            "actual Send backward restores the object's original stack position");
        await Click("Save", () => !session.IsDirty);
        var saved = (await repository.LoadAsync(id, default))!;
        Check(saved.Id == id && saved.Version == session.DurableRevision && saved.Slides[0].SpeakerNotes == "Actual speaker notes" &&
            JsonSerializer.Serialize(saved) == JsonSerializer.Serialize(session.GetDocumentSnapshot()),
            "actual Save readback preserves complete slides/notes/themes/layouts/masters and identities");
        window.Width = 390;
        using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(paths.DataDirectory, "present-editor-mobile.png"));
        session.TryGetValue("ShowSidebar", out var mobileSidebar);
        Check(mobileSidebar is false && !session.IsDirty, "mobile viewport collapses slide drawer without mutating saved presentation");
        var compactProperties = root!.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Tag, "ToggleInspector") && AutomationProperties.GetName(button) == "Properties");
        var compactPoint = compactProperties.TranslatePoint(new Point(compactProperties.Bounds.Width / 2, compactProperties.Bounds.Height / 2), window)!.Value;
        var compactHit = window.InputHitTest(compactPoint) as Visual;
        Check(ReferenceEquals(compactHit, compactProperties) || compactHit?.GetVisualAncestors().Contains(compactProperties) == true,
            "the compact toolbar keeps Properties directly in pointer reach without scrolling");
        window.Width = 1200;
        using (var frame = window.CaptureRenderedFrame()) { }
        Check(session.TryGetValue("ShowEditingArea", out var editingRestored) && editingRestored is true
            && TopLevel.GetTopLevel(scene) == window,
            "widening while Properties is open restores the real editor after compact detachment");
        var exportedSlideTitle = session.GetDocumentSnapshot()!.Slides[0].Title;
        await Click("Export .pptx", () => media.Downloads.Count == 1);
        var transfer = media.Downloads.Single();
        Check(transfer.Name == "Actual presentation title.pptx" && session.DurableRevision == saved.Version && !session.IsDirty,
            "actual Export .pptx delivers the open deck under its presentation title without disturbing the saved draft");
        var pptx = Convert.FromBase64String(transfer.Base64);
        Check(pptx.Length > 4 && pptx[0] == (byte)'P' && pptx[1] == (byte)'K',
            "actual Export .pptx hands the browser a real PowerPoint package");
        using (var package = new ZipArchive(new MemoryStream(pptx), ZipArchiveMode.Read))
            Check(package.GetEntry("[Content_Types].xml") is not null && package.GetEntry("ppt/presentation.xml") is not null,
                "actual exported package carries the PowerPoint content types and presentation part");
        await Click("Close", () => session.DocumentId is null);
        media.PickPath = Path.Combine(paths.DataDirectory, "roundtrip.pptx");
        File.WriteAllBytes(media.PickPath, pptx);
        await Click("Import .pptx", () => session.DocumentId is not null && session.DocumentId != id);
        var imported = session.GetDocumentSnapshot()!;
        Check(imported.Title == "roundtrip" && imported.Slides.Count == 1 &&
            imported.Slides[0].Title == exportedSlideTitle && !session.IsDirty,
            "actual Import .pptx rebuilds the exported deck as a new stored presentation through the owner importer");
        await Click("Close", () => session.DocumentId is null);
        Check(HasStart() && !HasCanvas(), "closing a presentation returns to the populated start-only view");
        await Click(saved.Title, () => session.DocumentId == id);
        Check(!session.IsDirty && session.DurableRevision == saved.Version &&
            JsonSerializer.Serialize(session.GetDocumentSnapshot()) == JsonSerializer.Serialize(saved),
            "actual persisted picker reopens same full owner model and canonical revision");
        string PlaybackStatus() => root!.GetVisualDescendants().OfType<TextBlock>()
            .Single(block => AutomationProperties.GetName(block) == "Playback status").Text ?? string.Empty;
        Check(ButtonNamed("Present").IsEnabled && !session.IsPlaying,
            "actual Present control enables only while an editable presentation with a visible slide is open");
        await Click("Add slide", () => session.GetDocumentSnapshot()!.Slides.Count == 2);
        var playbackDeck = session.GetDocumentSnapshot()!.Slides.Select(slide => slide.Id).ToArray();
        var openingTitle = session.GetDocumentSnapshot()!.Slides[0].Title;
        thumbnailScene.FocusElement(thumbnailScene.Root!);
        window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, "Home");
        Check(session.Editor!.Selection.SlideId == playbackDeck[0], "actual thumbnail Home selects the opening slide for playback");
        await Click("Present", () => session.IsPlaying);
        Check(!ButtonNamed("Present").IsEnabled && !ButtonNamed("Add text").IsEnabled && !scene.IsEnabled
            && PlaybackStatus() == "Slide 1 of 2; animation step 0",
            "starting an actual presentation freezes the authoring chrome and the native canvas while reporting the opening frame");
        await Click("Next slide", () => session.Editor!.Selection.SlideId == playbackDeck[1]);
        Check(PlaybackStatus() == "Slide 2 of 2; animation step 0",
            "actual Next slide advances the retained frame to the following slide");
        Check(ButtonNamed("Previous slide").IsEnabled && !ButtonNamed("Next slide").IsEnabled,
            "actual playback boundary controls track both ends of the running deck");
        await Click("Previous slide", () => session.Editor!.Selection.SlideId == playbackDeck[0]);
        Check(PlaybackStatus() == "Slide 1 of 2; animation step 0",
            "actual Previous slide returns the retained frame to the opening slide");
        await Click("End presentation", () => !session.IsPlaying);
        Check(scene.IsEnabled && ButtonNamed("Add text").IsEnabled && ButtonNamed("Present").IsEnabled,
            "ending the actual presentation restores the native canvas and the authoring chrome");
        Check(session.TryGetValue("SlideHidden", out var visibleSecond) && Equals(visibleSecond, false) &&
            ButtonNamed("Hide slide").IsEnabled && session.TryGetValue("CanShowSlide", out var canShowVisible) && Equals(canShowVisible, false),
            "the slide visibility controls report the current slide as visible before hiding");
        var currentSlideId = session.Editor!.Selection.SlideId;
        bool CurrentSlideHidden() => session.GetDocumentSnapshot()!.Slides
            .Single(slide => slide.Id == currentSlideId).Hidden;
        await Click("Hide slide", CurrentSlideHidden);
        Check(CurrentSlideHidden() && session.TryGetValue("CanHideSlide", out var canHideHidden) && Equals(canHideHidden, false) && ButtonNamed("Show slide").IsEnabled,
            "actual Hide slide marks the current slide hidden and swaps the visibility controls");
        await Click("Show slide", () => !CurrentSlideHidden());
        Check(!CurrentSlideHidden() && ButtonNamed("Hide slide").IsEnabled && session.TryGetValue("CanShowSlide", out var canShowRestored) && Equals(canShowRestored, false),
            "actual Show slide restores the current slide and swaps the visibility controls back");
        var firstSlideId = session.GetDocumentSnapshot()!.Slides[0].Id;
        var openingSlideTitle = session.GetDocumentSnapshot()!.Slides[0].Title;
        thumbnailScene.FocusElement(thumbnailScene.Root!);
        window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, "Home");
        Check(session.Editor!.Selection.SlideId == firstSlideId, "actual thumbnail Home selects the slide to hide");
        await Click("Hide slide", () => session.GetDocumentSnapshot()!.Slides[0].Hidden);
        await Click("Present", () => session.IsPlaying);
        Check(session.GetDocumentSnapshot()!.Slides.Count == 2
            && session.Editor!.Selection.SlideId == session.GetDocumentSnapshot()!.Slides[1].Id
            && !ButtonNamed("Next slide").IsEnabled && !ButtonNamed("Previous slide").IsEnabled,
            "a hidden slide is excluded from the running presentation's frame list");
        await Click("End presentation", () => !session.IsPlaying);
        thumbnailScene.FocusElement(thumbnailScene.Root!);
        window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, "Home");
        Check(session.Editor!.Selection.SlideId == firstSlideId, "actual thumbnail Home selects the hidden slide for restoration");
        await Click("Show slide", () => !session.GetDocumentSnapshot()!.Slides[0].Hidden);
        Check(session.TryGetValue("TransitionKind", out var openingTransition) && Equals(openingTransition, "None") &&
            session.TryGetValue("TransitionDuration", out var openingDuration) && Equals(openingDuration, 0d) &&
            ButtonNamed("Apply transition").IsEnabled,
            "the transition controls read the current slide's own stored transition before authoring");
        var transitionSelect = root!.GetVisualDescendants().OfType<ComboBox>().Single(box => AutomationProperties.GetName(box) == "Transition kind for the current slide");
        transitionSelect.Focus(); window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, "Home");
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "ArrowDown");
        TypeInto(NumberBox("Transition duration in seconds"), "0.5");
        Check(session.TryGetValue("TransitionKind", out var typedKind) && Equals(typedKind, "Fade")
            && session.TryGetValue("TransitionDuration", out var typedDuration) && Equals(typedDuration, 0.5d),
            "actual pointer and keyboard input sets the pending transition kind and duration");
        Check(!session.TrySetValue("TransitionDuration", 40d),
            "a duration outside the owner's 0.05-30 second range is refused rather than silently clamped");
        Check(!session.TrySetValue("TransitionDuration", double.NaN) && !session.TrySetValue("TransitionKind", "999"),
            "non-finite durations and undefined transition kinds are refused");
        Check(session.TryGetValue("TransitionDuration", out var keptDuration) && Equals(keptDuration, 0.5d),
            "the rejected duration leaves the accepted value in place");
        await Click("Apply transition", () => session.GetDocumentSnapshot()!.Slides[0].Transition.Kind == PresentTransitionKind.Fade
            && Math.Abs(session.GetDocumentSnapshot()!.Slides[0].Transition.DurationSeconds - 0.5d) < 0.001);
        Check(session.TryGetValue("AnimationSteps", out var noCues) && (noCues is Array { Length: 0 }),
            "the animation list is empty before the first cue is authored");
        await Click("Add text", () => session.Editor!.Selection.ElementIds.Count == 1);
        var animatedElement = session.Editor!.Selection.ElementIds.Single();
        Check(session.TryGetValue("CanAddAnimation", out var canAnimate) && Equals(canAnimate, true) &&
            !ButtonNamed("Remove animation").IsEnabled,
            "the animation controls enable adding for a selected object and hide removal while nothing is animated");
        Check(session.TrySetValue("AnimationEffect", "Fly") && session.TrySetValue("AnimationTrigger", "WithPrevious"),
            "the animation effect and trigger inputs accept the donor's own values");
        Check(!session.TrySetValue("AnimationEffect", "Bounce"),
            "an effect the owner does not define is refused instead of stored as free text");
        await Click("Add animation", () => session.GetDocumentSnapshot()!.Slides[0].Animations.Count == 1);
        var cue = session.GetDocumentSnapshot()!.Slides[0].Animations.Single();
        Check(cue.Effect == PresentAnimationEffect.Fly && cue.Trigger == PresentAnimationTrigger.WithPrevious &&
            cue.TargetElementId == animatedElement,
            "actual Add animation records the chosen effect and trigger against the selected object");
        Check(session.TryGetValue("AnimationSteps", out var renderedCues) &&
            renderedCues is PresentAnimationRow[] rows && rows.Length == 1 &&
            rows[0].TryGetValue("Effect", out var rowEffect) && Equals(rowEffect, "Fly") &&
            rows[0].TryGetValue("Trigger", out var rowTrigger) && Equals(rowTrigger, "WithPrevious"),
            "the animation list renders the owner's own cue for the selected slide");
        Check(session.TryGetValue("CanRemoveAnimation", out var canRemove) && Equals(canRemove, true),
            "the remove control enables once the selected object has a cue");
        await Click("Undo", () => session.GetDocumentSnapshot()!.Slides[0].Animations.Count == 0);
        Check(session.GetDocumentSnapshot()!.Slides[0].Animations.Count == 0,
            "undo removes the animation cue as an admitted edit");
        await Click("Redo", () => session.GetDocumentSnapshot()!.Slides[0].Animations.Count == 1);
        await Click("Remove animation", () => session.GetDocumentSnapshot()!.Slides[0].Animations.Count == 0);
        Check(session.GetDocumentSnapshot()!.Slides[0].Transition.Kind == PresentTransitionKind.Fade,
            "removing the cue leaves the slide's authored transition untouched");
        await session.CloseAsync();
        Check((await session.OpenAsync(id, readOnly: true)).Succeeded && !scene.IsEnabled && !ButtonNamed("Add slide").IsEnabled
            && !ButtonNamed("Delete slide").IsEnabled && !ButtonNamed("Move slide earlier").IsEnabled && !titleBox.IsEnabled
            && !ButtonNamed("Add text").IsEnabled,
            "read-only route freezes actual native canvas and authoring controls");
        var readOnly = JsonSerializer.Serialize(session.GetDocumentSnapshot());
        window.KeyTextInput(" denied"); window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, "v");
        Check(readOnly == JsonSerializer.Serialize(session.GetDocumentSnapshot()) && !session.IsDirty,
            "actual routed readonly text/paste attempt preserves original model (presentation gate, not ACL)");
        await session.CloseAsync(); await session.OpenAsync(id);
        var competing = (await repository.LoadAsync(id, default))!;
        competing.Title = "Actual independent winner";
        var winner = await repository.SaveAsync(competing, "Independent owner writer", default);
        var winningBytes = SHA256.HashData(await File.ReadAllBytesAsync(winner.CurrentPath));
        await session.EditAsync(editor => editor.SetSpeakerNotes(editor.Selection.SlideId, "Preserved conflicting draft"));
        var draft = JsonSerializer.Serialize(session.GetDocumentSnapshot());
        await Click("Save", () => session.ErrorCode == "RevisionConflict");
        var finalBytes = SHA256.HashData(await File.ReadAllBytesAsync(winner.CurrentPath));
        Check(session.IsDirty && draft == JsonSerializer.Serialize(session.GetDocumentSnapshot()) && winningBytes.SequenceEqual(finalBytes),
            "actual stale Save surfaces typed conflict and preserves draft plus unchanged physical winner bytes");
        Check(!(await session.PrepareToCloseAsync()).Succeeded && session.DocumentId == id && session.IsDirty,
            "failed actual close preparation preserves open unresolved presentation draft");
        using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(paths.DataDirectory, "present-retained-native.png"));
        // Unresolved draft intentionally remains undisposed; UI lifetime may detach
        // without pretending the authoritative session was safely closed.
        surface.Lifetime!.Dispose();
        return count;
    }
    finally { window.Close(); }
}, CancellationToken.None);
Console.WriteLine(JsonSerializer.Serialize(new { assertions, scope = args.Contains("interchange", StringComparer.Ordinal) ? "source/native import-export with an independently authored PowerPoint fixture; browser/donor/full requirements unverified" : "actual native retained Present/CUI/editor/filesystem repository; browser durability/donor/full requirements unverified" }));

public sealed class PresentSceneTestApplication : Avalonia.Application
{
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<PresentSceneTestApplication>().UseSkia())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    public override void Initialize()
    { CuiNativeHost.InitialisePrimitiveTheme(this, "Present"); WriteRetainedSceneResources.Register(this); }
}

sealed class PresentReadFault(IPresentRepository actual, Guid target) : IPresentRepository
{
    public bool FailList { get; set; }
    public int TargetLoads { get; private set; }
    public Task<IReadOnlyList<PresentDocumentSummary>> ListAsync(CancellationToken token)
    { if (FailList) throw new IOException("Controlled real-owner library admission failure"); return actual.ListAsync(token); }
    public Task<PresentDocument?> LoadAsync(Guid id, CancellationToken token)
    { if (id == target) ++TargetLoads; return actual.LoadAsync(id, token); }
    public Task<PresentSaveResult> SaveAsync(PresentDocument document, string reason, CancellationToken token) => actual.SaveAsync(document, reason, token);
    public Task DeleteAsync(Guid id, CancellationToken token) => actual.DeleteAsync(id, token);
}

sealed class PresentTransferMedia : IPresentBrowserMedia
{
    public List<(string Name, string Base64)> Downloads { get; } = [];
    public string? PickPath { get; set; }
    public Task DownloadAsync(string name, string base64, CancellationToken cancellationToken = default)
    { Downloads.Add((name, base64)); return Task.CompletedTask; }
    public Task<PresentPickedFile?> PickAsync(string accept, CancellationToken cancellationToken = default) =>
        Task.FromResult(string.IsNullOrWhiteSpace(PickPath) ? null
            : new PresentPickedFile(Path.GetFileName(PickPath), Convert.ToBase64String(File.ReadAllBytes(PickPath))));
}
