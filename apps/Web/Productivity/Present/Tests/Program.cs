using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
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
    var session = new PresentBrowserSession(repository);
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
        Check(ButtonNamed("New presentation").IsEnabled && !ButtonNamed("Save").IsEnabled && !ButtonNamed("Add slide").IsEnabled,
            "empty real Present lifecycle enables Create and disables document-only controls");
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
        var notes = root!.GetVisualDescendants().OfType<TextBox>().Single();
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
        await Click("Save", () => !session.IsDirty);
        var saved = (await repository.LoadAsync(id, default))!;
        Check(saved.Id == id && saved.Version == session.DurableRevision && saved.Slides[0].SpeakerNotes == "Actual speaker notes" &&
            JsonSerializer.Serialize(saved) == JsonSerializer.Serialize(session.GetDocumentSnapshot()),
            "actual Save readback preserves complete slides/notes/themes/layouts/masters and identities");
        await Click("Close", () => session.DocumentId is null);
        await Click(saved.Title, () => session.DocumentId == id);
        Check(!session.IsDirty && session.DurableRevision == saved.Version &&
            JsonSerializer.Serialize(session.GetDocumentSnapshot()) == JsonSerializer.Serialize(saved),
            "actual persisted picker reopens same full owner model and canonical revision");
        await session.CloseAsync();
        Check((await session.OpenAsync(id, readOnly: true)).Succeeded && !scene.IsEnabled && !ButtonNamed("Add slide").IsEnabled,
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
