using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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
    var paths = new AppPaths();
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
            var aScene = aRoot!.GetVisualDescendants().OfType<HavenSceneControl>().Single();
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
                var candidateScene = candidateRoot!.GetVisualDescendants().OfType<HavenSceneControl>().Single();
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
    Check(!parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error),
        "actual Present CUI parses without disconnected private controls");
    var surface = PresentBrowserSurface.Create(document, session, () => true);
    using var loader = new CuiControlLoader(surface.ControlRegistry!);
    loader.SetBindingContext(session); loader.SetActionDispatcher(session);
    var (root, diagnostics) = loader.TryLoad(document);
    foreach (var diagnostic in diagnostics) Console.WriteLine(JsonSerializer.Serialize(diagnostic));
    Check(root is not null && !diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error),
        "actual registered owning Present canvas lowers through CUI");
    loader.WireBindings(root!); loader.WireBindings(root!);
    var window = new Window { Width = 1200, Height = 900, Content = root };
    window.Show();
    try
    {
        using (var frame = window.CaptureRenderedFrame()) Check(frame is not null, "real Present native frame renders original canvas/resource graph");
        Button ButtonNamed(string label) => root!.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Content is string name && name == label);
        foreach (var button in root!.GetVisualDescendants().OfType<Button>())
            BrowserActionAvailability.ApplyInitial(button, loader, document, session);
        async Task Click(string label, Func<bool> completed)
        {
            var button = ButtonNamed(label);
            Check(button.IsEnabled, "real Present " + label + " control enables its owning action");
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!completed() || session.IsBusy) await Task.Delay(10, timeout.Token);
            using var frame = window.CaptureRenderedFrame();
        }
        Check(ButtonNamed("New presentation").IsEnabled && !ButtonNamed("Save").IsEnabled && !ButtonNamed("Add slide").IsEnabled
            && !ButtonNamed("Delete slide").IsEnabled && !ButtonNamed("Move slide earlier").IsEnabled && !ButtonNamed("Move slide later").IsEnabled
            && !ButtonNamed("Delete element").IsEnabled && !ButtonNamed("Bring to front").IsEnabled && !ButtonNamed("Send to back").IsEnabled
            && !ButtonNamed("Export .pptx").IsEnabled,
            "empty real Present lifecycle enables Create and disables document-only controls, including .pptx export");
        await Click("New presentation", () => session.DocumentId is not null);
        var id = session.DocumentId!.Value;
        var first = (await repository.LoadAsync(id, default))!;
        Check(first.Version == session.DurableRevision && JsonSerializer.Serialize(first) == JsonSerializer.Serialize(session.GetDocumentSnapshot()),
            "actual New commits same opaque presentation ID/revision and complete original owner model");
        var scene = root!.GetVisualDescendants().OfType<HavenSceneControl>().Single();
        var canvas = scene.Root!;
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
        var bodyPoint = scene.TranslatePoint(new Point(canvas.Bounds.X + local.X + local.Width * 0.5,
            canvas.Bounds.Y + local.Y + local.Height * 0.55), window)!.Value;
        window.MouseDown(bodyPoint, MouseButton.Left); window.MouseUp(bodyPoint, MouseButton.Left);
        Check(session.Editor!.Selection.ElementIds.Count == 1
            && ButtonNamed("Delete element").IsEnabled && ButtonNamed("Rotate right").IsEnabled && ButtonNamed("Bold").IsEnabled,
            "actual retained canvas selection notifies the session so the object controls follow the native selection");
        var emptyPoint = scene.TranslatePoint(new Point(canvas.Bounds.X + local.X + local.Width * 0.5,
            canvas.Bounds.Y + local.Y + local.Height * 0.92), window)!.Value;
        window.MouseDown(emptyPoint, MouseButton.Left); window.MouseUp(emptyPoint, MouseButton.Left);
        Check(session.Editor!.Selection.ElementIds.Count == 0
            && !ButtonNamed("Delete element").IsEnabled && !ButtonNamed("Bold").IsEnabled,
            "clearing the selection on empty canvas disables the object controls again");
        var notes = root!.GetVisualDescendants().OfType<TextBox>()
            .Single(box => AutomationProperties.GetName(box) == "Speaker notes");
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
        Check(!ButtonNamed("Bold").IsEnabled && !ButtonNamed("Italic").IsEnabled
            && ButtonNamed("Grow").IsEnabled && ButtonNamed("Rotate right").IsEnabled,
            "shape selection disables the text style commands and enables the object transform commands");
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
        var nudgedX = NudgeX();
        var nudgedY = NudgeY();
        await Click("Move object right", () => NudgeX() > nudgedX);
        await Click("Move object left", () => Math.Abs(NudgeX() - nudgedX) < 0.000001);
        await Click("Move object down", () => NudgeY() > nudgedY);
        await Click("Move object up", () => Math.Abs(NudgeY() - nudgedY) < 0.000001);
        Check(NudgeX() == nudgedX && NudgeY() == nudgedY,
            "actual object nudge controls move the selected object one donor step and back on both axes");
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
            var point = scene.TranslatePoint(new Point(canvas.Bounds.X + local.X + local.Width * nx,
                canvas.Bounds.Y + local.Y + local.Height * ny), window)!.Value;
            window.MouseDown(point, MouseButton.Left, additive ? RawInputModifiers.Shift : RawInputModifiers.None);
            window.MouseUp(point, MouseButton.Left, additive ? RawInputModifiers.Shift : RawInputModifiers.None);
        }
        double XOf(Guid id) => session.GetDocumentSnapshot()!.Slides[0].Elements
            .Single(element => element.Id == id).X;
        TextBox FillBox() => root!.GetVisualDescendants().OfType<TextBox>()
            .Single(box => AutomationProperties.GetName(box) == "Fill color");
        TextBox StrokeBox() => root!.GetVisualDescendants().OfType<TextBox>()
            .Single(box => AutomationProperties.GetName(box) == "Stroke color");
        TextBox StrokeWidthBox() => root!.GetVisualDescendants().OfType<TextBox>()
            .Single(box => AutomationProperties.GetName(box) == "Stroke width");
        void TypeInto(TextBox box, string text)
        {
            var point = box.TranslatePoint(new Point(box.Bounds.Width / 2, box.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
            window.KeyTextInput(text);
        }
        double FontSizeOf(Guid id) => session.GetDocumentSnapshot()!.Slides[0].Elements
            .Single(element => element.Id == id).TextStyle.FontSizePoints;
        await Click("Add shape", () => session.GetDocumentSnapshot()!.Slides[0].Elements.Count == elementCount + 1);
        var propShape = session.Editor!.Selection.ElementIds.Single();
        Check(!ButtonNamed("Font larger").IsEnabled && !ButtonNamed("Font smaller").IsEnabled
            && ButtonNamed("Apply fill").IsEnabled && FillBox().IsEnabled,
            "shape selection enables the fill control and disables the font size controls");
        var fillInput = FillBox();
        var fillPoint = fillInput.TranslatePoint(new Point(fillInput.Bounds.Width / 2, fillInput.Bounds.Height / 2), window)!.Value;
        window.MouseDown(fillPoint, MouseButton.Left); window.MouseUp(fillPoint, MouseButton.Left);
        window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
        window.KeyTextInput("#336699");
        Check(session.GetDocumentSnapshot()!.Slides[0].Elements.Single(element => element.Id == propShape)
                .Style.FillColor != "#336699",
            "typed fill colour is held by the session until it is applied to the object");
        await Click("Apply fill", () => session.GetDocumentSnapshot()!.Slides[0].Elements
            .Single(element => element.Id == propShape).Style.FillColor == "#336699");
        Check(session.GetDocumentSnapshot()!.Slides[0].Elements.Single(element => element.Id == propShape)
                .Style.FillColor == "#336699",
            "actual Apply fill writes the typed colour into the selected shape's own style");
        Check(ButtonNamed("Apply stroke").IsEnabled && StrokeBox().IsEnabled && StrokeWidthBox().IsEnabled
                && ButtonNamed("Apply stroke width").IsEnabled,
            "shape selection also enables the donor stroke colour and stroke width controls");
        double StrokedWidth() => session.GetDocumentSnapshot()!.Slides[0].Elements
            .Single(element => element.Id == propShape).Style.StrokeWidth;
        TypeInto(StrokeBox(), "#202020");
        Check(StrokedWidth() == 0 && session.GetDocumentSnapshot()!.Slides[0].Elements
                .Single(element => element.Id == propShape).Style.StrokeColor != "#202020",
            "typed stroke colour is held by the session until it is applied to the object");
        await Click("Apply stroke", () => session.GetDocumentSnapshot()!.Slides[0].Elements
            .Single(element => element.Id == propShape).Style.StrokeColor == "#202020");
        Check(session.GetDocumentSnapshot()!.Slides[0].Elements.Single(element => element.Id == propShape)
                .Style.StrokeColor == "#202020" && StrokedWidth() >= 1,
            "actual Apply stroke writes the typed colour and a donor stroke width into the shape's own style");
        TypeInto(StrokeWidthBox(), "6");
        Check(StrokedWidth() >= 1 && StrokedWidth() != 6,
            "typed stroke width is held by the session until it is applied to the object");
        await Click("Apply stroke width", () => StrokedWidth() == 6);
        Check(StrokedWidth() == 6,
            "actual Apply stroke width moves the selected shape's own stroke width to the typed value");
        await Click("Delete element", () => session.GetDocumentSnapshot()!.Slides[0].Elements.Count == elementCount);
        SelectOnCanvas(0.70, 0.60, additive: false);
        var propBody = session.Editor!.Selection.ElementIds.Single();
        Check(ButtonNamed("Font larger").IsEnabled && ButtonNamed("Font smaller").IsEnabled
            && !ButtonNamed("Apply fill").IsEnabled && !FillBox().IsEnabled
            && !ButtonNamed("Apply stroke").IsEnabled && !StrokeBox().IsEnabled
            && !ButtonNamed("Apply stroke width").IsEnabled && !StrokeWidthBox().IsEnabled,
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
        await Click(openingTitle, () => session.Editor!.Selection.SlideId == playbackDeck[0]);
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
            ButtonNamed("Hide slide").IsEnabled && !ButtonNamed("Show slide").IsEnabled,
            "the slide visibility controls report the current slide as visible before hiding");
        var currentSlideId = session.Editor!.Selection.SlideId;
        bool CurrentSlideHidden() => session.GetDocumentSnapshot()!.Slides
            .Single(slide => slide.Id == currentSlideId).Hidden;
        await Click("Hide slide", CurrentSlideHidden);
        Check(CurrentSlideHidden() && !ButtonNamed("Hide slide").IsEnabled && ButtonNamed("Show slide").IsEnabled,
            "actual Hide slide marks the current slide hidden and swaps the visibility controls");
        await Click("Show slide", () => !CurrentSlideHidden());
        Check(!CurrentSlideHidden() && ButtonNamed("Hide slide").IsEnabled && !ButtonNamed("Show slide").IsEnabled,
            "actual Show slide restores the current slide and swaps the visibility controls back");
        var firstSlideId = session.GetDocumentSnapshot()!.Slides[0].Id;
        var openingSlideTitle = session.GetDocumentSnapshot()!.Slides[0].Title;
        await Click(openingSlideTitle, () => session.Editor!.Selection.SlideId == firstSlideId);
        await Click("Hide slide", () => session.GetDocumentSnapshot()!.Slides[0].Hidden);
        await Click("Present", () => session.IsPlaying);
        Check(session.GetDocumentSnapshot()!.Slides.Count == 2
            && session.Editor!.Selection.SlideId == session.GetDocumentSnapshot()!.Slides[1].Id
            && !ButtonNamed("Next slide").IsEnabled && !ButtonNamed("Previous slide").IsEnabled,
            "a hidden slide is excluded from the running presentation's frame list");
        await Click("End presentation", () => !session.IsPlaying);
        await Click(openingSlideTitle, () => session.Editor!.Selection.SlideId == firstSlideId);
        await Click("Show slide", () => !session.GetDocumentSnapshot()!.Slides[0].Hidden);
        Check(session.TryGetValue("TransitionKind", out var openingTransition) && Equals(openingTransition, "None") &&
            session.TryGetValue("TransitionDuration", out var openingDuration) && Equals(openingDuration, 0d) &&
            ButtonNamed("Apply transition").IsEnabled,
            "the transition controls read the current slide's own stored transition before authoring");
        Check(session.TrySetValue("TransitionKind", "Fade") && session.TrySetValue("TransitionDuration", 0.5d),
            "the two-way transition inputs accept a valid kind and duration");
        Check(!session.TrySetValue("TransitionDuration", 40d),
            "a duration outside the owner's 0.05-30 second range is refused rather than silently clamped");
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
Console.WriteLine(JsonSerializer.Serialize(new { assertions, scope = "actual native retained Present/CUI/editor/filesystem repository; browser durability/donor/full requirements unverified" }));

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
