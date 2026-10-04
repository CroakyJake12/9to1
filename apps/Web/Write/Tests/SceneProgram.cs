using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Infrastructure;
using Haven.UI;
using NineToOne.Web;
using NineToOne.Web.Write;

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_DATA_DIR")))
    throw new ArgumentException("Set a fresh isolated HAVEN_DATA_DIR.");
await using var headless = HeadlessUnitTestSession.StartNew(typeof(WriteSceneTestApplication));
var checks = await headless.Dispatch(async () =>
{
    var assertions = 0;
    void Check(bool passed, string name)
    {
        if (!passed) throw new InvalidOperationException(name);
        ++assertions;
        Console.WriteLine("ASSERT " + name);
    }
    using (var font = AssetLoader.Open(new Uri("avares://Haven/Assets/Fonts/MontserratStatic/Montserrat-Regular.ttf")))
    {
        var digest = Convert.ToHexString(SHA256.HashData(font)).ToLowerInvariant();
        Check(digest == "3e8abe50c44c82e2242e97d1ec8c0d385c4890cdc50447bcdb8605c81a38cfb2",
            "actual packed font resolves original asset URI and matches exact owner bytes");
    }
    NotesBlock First(NotesDocument document) => document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).First();
    string Text(NotesDocument document) => string.Concat(First(document).Runs.Select(run => run.Text));
    var paths = new AppPaths();
    var repository = new NotesRepository(paths, new NotesDocumentValidator(), new ProductionDiagnostics(paths));
    if (args.Contains("initial-focus", StringComparer.Ordinal))
    {
        // Actual native focus manager + original editor/input renderer, no fake
        // FocusElement/counters. This does not exercise browser Space KeyDown.
        var sentinel = new Button { Content = "Toolbar focus" };
        using var focusHost = new WriteRetainedSceneControl(() => true);
        var layout = new DockPanel(); DockPanel.SetDock(sentinel, Dock.Top);
        layout.Children.Add(sentinel); layout.Children.Add(focusHost);
        var focusWindow = new Window { Width = 1100, Height = 800, Content = layout };
        focusWindow.Show();
        try
        {
            using (var frame = focusWindow.CaptureRenderedFrame())
                Check(frame is not null, "initial-focus actual native owner frame renders");
            var nativeScene = focusHost.GetVisualDescendants().OfType<HavenSceneControl>().Single();
            var firstDocument = NotesDocument.Create("Initial focus owner");
            var firstEditor = new WriteDocumentEditor(firstDocument);
            focusHost.SetInputAllowed(false); sentinel.Focus();
            focusHost.SetEditor(firstEditor);
            Check(ReferenceEquals(focusWindow.FocusManager?.GetFocusedElement(), sentinel),
                "initial-focus disabled new editor does not seize native focus");
            focusHost.SetInputAllowed(true);
            Check(ReferenceEquals(focusWindow.FocusManager?.GetFocusedElement(), nativeScene),
                "initial-focus new owner attachment focuses native scene once when enabled");
            focusWindow.KeyTextInput("Alpha beta gamma");
            Check(Text(firstDocument) == "Alpha beta gamma",
                "initial-focus actual routed native text reaches same original owner editor");
            sentinel.Focus(); focusHost.SetInputAllowed(false); focusHost.SetEditor(firstEditor);
            focusHost.SetInputAllowed(true);
            Check(ReferenceEquals(focusWindow.FocusManager?.GetFocusedElement(), sentinel),
                "initial-focus same editor busy release preserves real toolbar focus");
            focusWindow.KeyTextInput(" ignored");
            Check(Text(firstDocument) == "Alpha beta gamma",
                "initial-focus toolbar-focused text does not mutate original document");
            var secondDocument = NotesDocument.Create("Replacement focus owner");
            var secondEditor = new WriteDocumentEditor(secondDocument);
            focusHost.SetInputAllowed(false); focusHost.SetEditor(secondEditor); focusHost.SetEditor(null);
            focusHost.SetInputAllowed(true);
            Check(ReferenceEquals(focusWindow.FocusManager?.GetFocusedElement(), sentinel) && nativeScene.Root is null,
                "initial-focus null attachment clears pending focus before enable");
            focusHost.SetEditor(secondEditor);
            Check(ReferenceEquals(focusWindow.FocusManager?.GetFocusedElement(), nativeScene),
                "initial-focus enabled non-null replacement uses actual owner focus");
            focusHost.SetInputAllowed(false); focusHost.SetInputAllowed(true);
            Check(ReferenceEquals(focusWindow.FocusManager?.GetFocusedElement(), nativeScene),
                "initial-focus actual busy router renewal preserves already-native-focused scene");
            focusWindow.KeyTextInput("Delta echo");
            Check(Text(secondDocument) == "Delta echo",
                "initial-focus renewed actual owner router accepts native text after busy roundtrip");
            focusHost.SetInputAllowed(false); sentinel.Focus();
            focusHost.SetInputAllowed(true);
            Check(ReferenceEquals(focusWindow.FocusManager?.GetFocusedElement(), sentinel),
                "initial-focus toolbar focus acquired during busy is not stolen on enable");
            focusWindow.KeyTextInput(" ignored while toolbar focused");
            Check(Text(secondDocument) == "Delta echo",
                "initial-focus toolbar-focused native text after busy leaves canonical editor unchanged");
            sentinel.Focus(); focusHost.SetInputAllowed(false);
            focusHost.SetEditor(new WriteDocumentEditor(NotesDocument.Create("Disposed pending focus")));
            focusHost.Dispose();
            Check(ReferenceEquals(focusWindow.FocusManager?.GetFocusedElement(), sentinel) && nativeScene.Root is null,
                "initial-focus disposed pending editor leaves native toolbar focused and root detached");
            return assertions;
        }
        finally { focusWindow.Close(); }
    }
    if (args.Contains("navigation", StringComparer.Ordinal))
    {
        var a = NotesDocument.Create("Canonical document A");
        var ae = new WriteDocumentEditor(a); ae.InsertDocumentText("Original A canonical text");
        await repository.SaveAsync(a, "Real navigation A", default);
        var b = NotesDocument.Create("Canonical document B");
        var be = new WriteDocumentEditor(b); be.InsertDocumentText("Original B canonical text");
        await repository.SaveAsync(b, "Real navigation B", default);
        var faults = new NavigationReadFault(repository, b.Id);
        var feature = new WriteBrowserFeature(faults, new WriteNativeDocumentPackageStore(), () => true,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Write.cui")));
        var openedA = await feature.OpenAsync(new("app.write", "NotesDocument", a.Id.ToString("D")));
        Check(openedA.Succeeded, "actual canonical A navigation resolves owning repository document");
        var viewA = feature.Render(openedA.ViewState!);
        using var loaderA = new CuiControlLoader(viewA.ControlRegistry!);
        loaderA.SetBindingContext(viewA.Bindings); loaderA.SetActionDispatcher(viewA.Actions);
        var (rootA, diagnosticsA) = loaderA.TryLoad(viewA.Document);
        Check(rootA is not null && !diagnosticsA.Any(d => d.Severity == CuiDiagnosticSeverity.Error), "actual existing A CUI renders through original registered retained surface");
        loaderA.WireBindings(rootA!);
        var navigationWindow = new Window { Width = 1100, Height = 800, Content = rootA };
        navigationWindow.Show();
        try
        {
            using (var frame = navigationWindow.CaptureRenderedFrame()) Check(frame is not null, "actual existing A retained frame renders before navigation faults");
            viewA.Admission?.Accept();
            await feature.PrepareToCloseAsync();
            var activeA = (WriteBrowserSession)viewA.Bindings;
            var originalA = JsonSerializer.Serialize(activeA.GetDocumentSnapshot());
            var existingSceneA = rootA!.GetVisualDescendants().OfType<HavenSceneControl>().Single();
            bool AUnchanged() => activeA.DocumentId == a.Id && activeA.DurableRevision == a.Version &&
                JsonSerializer.Serialize(activeA.GetDocumentSnapshot()) == originalA && existingSceneA.Root is not null;
            var requestB = new HavenOS.Home.Core.HomeFeatureNavigationRequest("app.write", "NotesDocument", b.Id.ToString("D"));
            faults.FailList = true;
            var denied = await feature.OpenAsync(requestB);
            Console.WriteLine(JsonSerializer.Serialize(new { navigation = "ListFailure", targetOwnerLoads = faults.TargetLoads,
                visibleId = activeA.DocumentId, expectedVisibleId = a.Id, code = denied.Code }));
            using (var frame = navigationWindow.CaptureRenderedFrame()) frame!.Save(Path.Combine(paths.DataDirectory, "write-navigation-list-failure.png"));
            Check(!denied.Succeeded && denied.Request == requestB, "fallible real repository list returns original typed failed navigation request");
            Check(AUnchanged(), "failed target navigation preserves existing rendered A canonical ID/text/revision/full model and retained scene");
            faults.FailList = false;

            // A prepared B is a real owner snapshot but has no authoring authority
            // or visibility until actual candidate CUI lowering is accepted.
            using (var cancel = new CancellationTokenSource())
            {
                var cancelledOffer = await feature.OpenAsync(requestB, cancel.Token);
                Check(cancelledOffer.Succeeded && AUnchanged(), "successful B preparation leaves actual visible A unchanged");
                cancel.Cancel();
                var cancelledRender = false;
                try { feature.Render(cancelledOffer.ViewState!); }
                catch (InvalidOperationException) { cancelledRender = true; }
                Check(cancelledRender && AUnchanged(), "cancelled prepared target cannot render or replace original A");
            }
            var superseded = await feature.OpenAsync(requestB);
            var newer = await feature.OpenAsync(requestB);
            var oldRejected = false;
            try { feature.Render(superseded.ViewState!); }
            catch (InvalidOperationException) { oldRejected = true; }
            Check(newer.Succeeded && oldRejected && AUnchanged(), "superseded prepared view cannot replace current real owner presentation");
            var changedDraftOffer = await feature.OpenAsync(requestB);
            await activeA.InsertTextAsync(" retained draft while target loads");
            var changedA = JsonSerializer.Serialize(activeA.GetDocumentSnapshot());
            var changedRejected = false;
            try { feature.Render(changedDraftOffer.ViewState!); }
            catch (InvalidOperationException) { changedRejected = true; }
            Check(changedRejected && activeA.IsDirty && activeA.DocumentId == a.Id &&
                JsonSerializer.Serialize(activeA.GetDocumentSnapshot()) == changedA && existingSceneA.Root is not null,
                "old document revision/edit generation fence rejects target and preserves newly authored current draft");
            Check((await activeA.SaveAsync()).Succeeded, "preserved current A draft explicitly saves through actual owner CAS");
            originalA = JsonSerializer.Serialize(activeA.GetDocumentSnapshot());
            a.Version = activeA.DurableRevision;

            var unsupportedOffer = await feature.OpenAsync(requestB);
            var unsupported = feature.Render(unsupportedOffer.ViewState!);
            var pendingB = (WriteBrowserSession)unsupported.Bindings;
            Check(AUnchanged() && !pendingB.IsPresentationAdmitted && pendingB.IsActionAvailable("CreateDocument") == false &&
                !(await pendingB.InsertTextAsync(" disallowed before admission")).Succeeded,
                "rendered candidate binds actual B independently while all native and typed authoring remain unadmitted");
            using (var unavailableRegistry = new CuiControlLoader())
            {
                unavailableRegistry.SetBindingContext(unsupported.Bindings); unavailableRegistry.SetActionDispatcher(unsupported.Actions);
                var (_, errors) = unavailableRegistry.TryLoad(unsupported.Document);
                Check(errors.Any(d => d.Severity == CuiDiagnosticSeverity.Error), "actual unavailable renderer registry causes meaningful candidate CUI lowering failure");
            }
            unsupported.Admission!.Reject(); unsupported.Lifetime!.Dispose();
            Check(AUnchanged(), "rejected candidate CUI leaves previously visible A session and actual scene alive");

            var rollbackOffer = await feature.OpenAsync(requestB);
            var rollback = feature.Render(rollbackOffer.ViewState!);
            rollback.Admission!.Accept(); rollback.Admission.Reject(); rollback.Lifetime!.Dispose();
            await feature.PrepareToCloseAsync();
            Check(AUnchanged(), "synchronous failed handoff rollback preserves original A before prior-session retirement");

            var admittedOffer = await feature.OpenAsync(requestB);
            var admitted = feature.Render(admittedOffer.ViewState!);
            using var loaderB = new CuiControlLoader(admitted.ControlRegistry!);
            loaderB.SetBindingContext(admitted.Bindings); loaderB.SetActionDispatcher(admitted.Actions);
            var (rootB, diagnosticsB) = loaderB.TryLoad(admitted.Document);
            Check(rootB is not null && !diagnosticsB.Any(d => d.Severity == CuiDiagnosticSeverity.Error), "actual B candidate fully lowers through supplied original renderer before acceptance");
            loaderB.WireBindings(rootB!);
            Check(AUnchanged(), "valid candidate lowering itself preserves original A until explicit acceptance");
            admitted.Admission!.Accept(); navigationWindow.Content = rootB;
            viewA.Lifetime!.Dispose();
            Check((await feature.PrepareToCloseAsync()).Succeeded, "accepted handoff explicitly observes saved previous-session retirement");
            var activeB = (WriteBrowserSession)admitted.Bindings;
            Check(activeB.DocumentId == b.Id && activeB.DurableRevision == b.Version && activeB.IsPresentationAdmitted &&
                JsonSerializer.Serialize(activeB.GetDocumentSnapshot()) == JsonSerializer.Serialize((await repository.LoadAsync(b.Id, default))!),
                "accepted actual B owns its canonical ID/revision/full content without changing either stored artifact");
            await feature.DisposeAsync();
            return assertions;
        }
        finally { navigationWindow.Close(); }
    }
    if (args.Contains("picker", StringComparer.Ordinal))
    {
        var seeded = NotesDocument.Create("Existing saved owner document");
        _ = new WriteDocumentEditor(seeded);
        var seedReceipt = await repository.SaveAsync(seeded, "Real picker fixture", default);
        var feature = new WriteBrowserFeature(repository, new WriteNativeDocumentPackageStore(), () => true,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Write.cui")));
        var registry = new BrowserSurfaceRegistry();
        Check(registry.Register(feature, feature.Render, BrowserSurfaceScope.DeviceLocal).Succeeded,
            "actual canonical app.write route registers with injected real repository");
        foreach (var unsupported in new[] { "", "https://unsupported.invalid/write" })
        {
            var request = new HavenOS.Home.Core.HomeFeatureNavigationRequest("app.write", DeepLink: unsupported);
            var denied = await registry.OpenAsync(request, default);
            Check(!denied.Result.Succeeded && denied.Surface is null && denied.Result.Request == request &&
                (await repository.ListAsync(default)).Count == 1,
                "unsupported deep link is rejected before session effects and preserves original request");
        }
        var invalid = await registry.OpenAsync(new("app.write", "File", Guid.NewGuid().ToString("D")), default);
        Check(!invalid.Result.Succeeded && invalid.Surface is null, "Write route rejects unsupported fabricated Files identity");
        var opening = await registry.OpenAsync(new("app.write"), default);
        Check(opening.Result.Succeeded && opening.Surface is not null &&
            opening.Result.ViewState!.State.GetProperty("documentId").ValueKind == JsonValueKind.Null &&
            (await repository.ListAsync(default)).Count == 1,
            "empty editor route lists actual stored documents without creating a default artifact");
        var pickerSurface = opening.Surface!;
        var pickerSession = (WriteBrowserSession)pickerSurface.Bindings;
        using var pickerLoader = new CuiControlLoader(pickerSurface.ControlRegistry!);
        pickerLoader.SetBindingContext(pickerSurface.Bindings);
        pickerLoader.SetActionDispatcher(pickerSurface.Actions);
        var (pickerRoot, pickerDiagnostics) = pickerLoader.TryLoad(pickerSurface.Document);
        foreach (var diagnostic in pickerDiagnostics) Console.WriteLine(JsonSerializer.Serialize(diagnostic));
        Check(pickerRoot is not null && !pickerDiagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error),
            "actual Write picker CUI lowers with real retained registry");
        pickerLoader.WireBindings(pickerRoot!);
        pickerLoader.WireBindings(pickerRoot!);
        pickerSurface.Admission?.Accept();
        var pickerWindow = new Window { Width = 1100, Height = 800, Content = pickerRoot };
        pickerWindow.Show();
        try
        {
            using (var frame = pickerWindow.CaptureRenderedFrame())
                Check(frame is not null, "actual picker controls render through native CUI");
            Button ButtonNamed(string name) => pickerRoot!.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Content is string label && label == name);
            foreach (var button in pickerRoot!.GetVisualDescendants().OfType<Button>())
                BrowserActionAvailability.ApplyInitial(button, pickerLoader, pickerSurface.Document, pickerSurface.Actions);
            async Task PickerClick(string name, Func<bool> completed)
            {
                var button = ButtonNamed(name);
                Check(button.IsEnabled, "actual picker control " + name + " permits interaction");
                var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), pickerWindow)!.Value;
                pickerWindow.MouseDown(center, MouseButton.Left);
                pickerWindow.MouseUp(center, MouseButton.Left);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (!completed() || pickerSession.IsBusy) await Task.Delay(10, timeout.Token);
                using var frame = pickerWindow.CaptureRenderedFrame();
            }
            await PickerClick(seeded.Title, () => pickerSession.DocumentId == seeded.Id);
            Check(pickerSession.DocumentId == seeded.Id && pickerSession.DurableRevision == seedReceipt.Version &&
                JsonSerializer.Serialize(pickerSession.GetDocumentSnapshot()!.Sections) == JsonSerializer.Serialize(seeded.Sections),
                "actual typed picker opens same repository ID/revision/whole structure");
            var pickerBlock = First(pickerSession.GetDocumentSnapshot()!).Id;
            await pickerSession.SelectAsync(pickerBlock, 0, 0);
            pickerWindow.KeyTextInput("Alpha beta gamma");
            Check(Text(pickerSession.GetDocumentSnapshot()!) == "Alpha beta gamma", "picker journey actual retained raw typing edits owning document");
            await pickerSession.SelectAsync(pickerBlock, 6, 10);
            await PickerClick("Bold", () => First(pickerSession.GetDocumentSnapshot()!).Runs.Any(run => run.Bold));
            Check(registry.HasUnsavedChanges, "actual close participant reports real unsaved editor content");
            var prepared = await registry.PrepareToCloseAsync();
            Check(prepared.Succeeded && prepared.Value && !registry.HasUnsavedChanges && pickerSession.DocumentId == seeded.Id,
                "actual registry preparation durably saves while preserving current document for host close decision");
            await PickerClick("Save", () => !pickerSession.IsDirty);
            var pickerSaved = (await repository.LoadAsync(seeded.Id, default))!;
            Check(pickerSaved.Version == pickerSession.DurableRevision && First(pickerSaved).Runs is { Count: 3 } pickerRuns &&
                pickerRuns[1].Text == "beta" && pickerRuns[1].Bold, "actual picker/editor Save retains canonical ID and exact selected formatting");
            await PickerClick("Close", () => pickerSession.DocumentId is null);
            await PickerClick(seeded.Title, () => pickerSession.DocumentId == seeded.Id);
            Check(JsonSerializer.Serialize(pickerSession.GetDocumentSnapshot()!.Sections) == JsonSerializer.Serialize(pickerSaved.Sections) &&
                pickerSession.DurableRevision == pickerSaved.Version, "actual saved picker reopens same canonical revision and complete structure");
            await PickerClick("New", () => pickerSession.DocumentId is not null && pickerSession.DocumentId != seeded.Id);
            Check((await repository.ListAsync(default)).Count == 2 &&
                (await repository.LoadAsync(pickerSession.DocumentId!.Value, default))!.Version == pickerSession.DurableRevision,
                "actual New commits second real opaque document while existing document remains stored");
            var newSnapshot = pickerSession.GetDocumentSnapshot()!;
            var newSaved = (await repository.LoadAsync(newSnapshot.Id, default))!;
            Check(JsonSerializer.Serialize(newSnapshot.Sections) == JsonSerializer.Serialize(newSaved.Sections),
                "empty New commits the owning editor's initialized run IDs before first attachment");
            await PickerClick("Close", () => pickerSession.DocumentId is null);
            await PickerClick(newSaved.Title, () => pickerSession.DocumentId == newSaved.Id);
            Check(!pickerSession.IsDirty && pickerSession.DurableRevision == newSaved.Version &&
                JsonSerializer.Serialize(pickerSession.GetDocumentSnapshot()!.Sections) == JsonSerializer.Serialize(newSaved.Sections),
                "empty created document reopens with every owner object ID and revision unchanged");
            registry.ClearPrivateContext();
            Check(registry.AvailableRoutes.Contains("app.write"), "device-local Write session remains registered without transferring account authority");
            Check((await registry.PrepareToCloseAsync()).Succeeded, "actual close preparation succeeds before asynchronous removal");
            await registry.ClearAsync();
            Check(registry.AvailableRoutes.Count == 0, "explicit awaited owner close precedes full route removal");
            return assertions;
        }
        finally { pickerWindow.Close(); }
    }
    var session = new WriteBrowserSession(repository, new WriteNativeDocumentPackageStore());
    var parser = new CuiRichParser();
    // An explicit fixture path permits replaying the captured original authored CUI
    // as a negative control; the default always loads the production surface.
    var document = parser.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("WRITE_CUI_FIXTURE_PATH")
        ?? Path.Combine(AppContext.BaseDirectory, "Write.cui")));
    Check(!parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error), "actual owning Write.cui parses");
    var surface = WriteBrowserSurface.Create(document, session, () => true);
    using var loader = new CuiControlLoader(surface.ControlRegistry!);
    loader.SetBindingContext(session);
    loader.SetActionDispatcher(session);
    var (root, diagnostics) = loader.TryLoad(document);
    Check(root is not null && !diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error), "actual registered owner surface lowers");
    loader.WireBindings(root!);
    loader.WireBindings(root!); // The existing runtime promises idempotent event binding.
    var window = new Window { Width = 1100, Height = 800, Content = root };
    window.Show();
    try
    {
        using (var initialFrame = window.CaptureRenderedFrame())
            Check(initialFrame is not null, "actual native templates render before querying editor descendants");
        var host = root!.GetVisualDescendants().OfType<WriteRetainedSceneControl>().Single();
        var scene = host.GetVisualDescendants().OfType<HavenSceneControl>().Single();
        if (args.Contains("toolbar", StringComparer.Ordinal))
        {
            var buttons = root!.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Content is string).ToDictionary(button => (string)button.Content!);
            foreach (var button in buttons.Values)
                BrowserActionAvailability.ApplyInitial(button, loader, document, session);
            void State(string name, bool expected) => Check(buttons[name].IsEnabled == expected,
                "actual toolbar " + name + " enabled=" + expected);
            void Empty()
            {
                State("New", true);
                foreach (var name in new[] { "Save", "Bold", "Undo", "Redo", "Close" }) State(name, false);
            }
            void ClickStart(string name)
            {
                Check(buttons[name].IsEnabled, "actual " + name + " control permits interaction");
                var center = buttons[name].TranslatePoint(new Point(buttons[name].Bounds.Width / 2,
                    buttons[name].Bounds.Height / 2), window)!.Value;
                window.MouseDown(center, MouseButton.Left);
                window.MouseUp(center, MouseButton.Left);
            }
            async Task WaitFor(Func<bool> completed)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (!completed() || session.IsBusy) await Task.Delay(10, timeout.Token);
                using var frame = window.CaptureRenderedFrame();
            }
            async Task Click(string name, Func<bool> completed)
            {
                ClickStart(name);
                await WaitFor(completed);
            }
            Empty();
            await Click("New", () => session.DocumentId is not null);
            State("Save", true); State("Bold", true); State("Close", true);
            State("Undo", false); State("Redo", false);
            var toolbarId = session.DocumentId!.Value;
            var toolbarBlock = First(session.GetDocumentSnapshot()!).Id;
            await session.SelectAsync(toolbarBlock, 0, 0);
            window.KeyTextInput("Alpha beta gamma");
            Check(Text(session.GetDocumentSnapshot()!) == "Alpha beta gamma", "toolbar journey actual routed typing mutates owner");
            State("Undo", true); State("Redo", false);
            await session.SelectAsync(toolbarBlock, 6, 10);
            await Click("Bold", () => First(session.GetDocumentSnapshot()!).Runs.Any(run => run.Bold));
            Check(First(session.GetDocumentSnapshot()!).Runs is { Count: 3 } toolbarRuns && toolbarRuns[1].Text == "beta" && toolbarRuns[1].Bold,
                "toolbar actual Bold changes only selected structured run");
            await Click("Undo", () => First(session.GetDocumentSnapshot()!).Runs.All(run => !run.Bold));
            State("Redo", true);
            await Click("Redo", () => First(session.GetDocumentSnapshot()!).Runs.Any(run => run.Bold));
            State("Redo", false);
            var toolbarGate = (SemaphoreSlim)typeof(NotesRepository)
                .GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(repository)!;
            await toolbarGate.WaitAsync();
            try
            {
                ClickStart("Save");
                Check(session.IsBusy && session.IsDirty, "toolbar Save awaits actual repository barrier with unsaved work");
                foreach (var name in buttons.Keys) State(name, false);
            }
            finally { toolbarGate.Release(); }
            await WaitFor(() => !session.IsDirty);
            State("New", true); State("Save", true); State("Bold", true); State("Close", true);
            var toolbarStored = (await repository.LoadAsync(toolbarId, default))!;
            Check(toolbarStored.Version == session.DurableRevision && First(toolbarStored).Runs[1].Bold,
                "toolbar actual Save commits canonical revision and selected formatting");
            await Click("Close", () => session.DocumentId is null);
            Empty();
            surface.Lifetime!.Dispose();
            await session.DisposeAsync();
            return assertions;
        }
        Check((await session.CreateAsync("Rendered owner journey")).Succeeded, "actual repository creates rendered document");
        var id = session.DocumentId!.Value;
        var block = First(session.GetDocumentSnapshot()!).Id;
        await session.SelectAsync(block, 0, 0);
        using (var frame = window.CaptureRenderedFrame())
            Check(frame is not null && host.Bounds.Width > 0 && host.Bounds.Height > 0, "actual retained scene renders nonzero editor canvas");
        Check(scene.Platform == HavenPlatform.Unknown && scene.Root?.Name == "Write.Document.Surface",
            "original retained editor is attached without false platform claim");
        window.KeyTextInput("Alpha beta gamma");
        Check(Text(session.GetDocumentSnapshot()!) == "Alpha beta gamma", "actual routed text input mutates owner exactly once");
        await session.SelectAsync(block, 6, 10);
        var changes = 0;
        session.Editor!.Changed += Count;
        void Count(object? sender, EventArgs args) => ++changes;
        var bold = root!.GetVisualDescendants().OfType<Button>().Single(button => button.Content is string label && label == "Bold");
        bold.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var runs = First(session.GetDocumentSnapshot()!).Runs;
        Check(changes == 1 && runs.Count == 3 && runs[1].Text == "beta" && runs[1].Bold && !runs[0].Bold && !runs[2].Bold,
            "actual CUI bold button dispatches one owner mutation despite duplicate wiring attempt");
        using (var frame = window.CaptureRenderedFrame())
        {
            Check(frame is not null, "actual structured edit renders a captured frame");
            frame!.Save(Path.Combine(paths.DataDirectory, "write-retained-scene.png"));
        }
        if (args.Contains("layout", StringComparer.Ordinal))
        {
            var drawing = new HavenDrawingContext();
            ((IHavenDrawCommandSource)scene.Root!).Draw(drawing, 1);
            var createText = typeof(HavenSceneControl).GetMethod("CreateText",
                BindingFlags.Static | BindingFlags.NonPublic, [typeof(string), typeof(string), typeof(double),
                    typeof(int), typeof(double), typeof(IBrush), typeof(bool)])!;
            var segments = drawing.Commands.OfType<HavenTextCommand>()
                .Where(command => command.Layout.Text is "Alpha " or "beta" or " gamma").ToArray();
            Check(segments.Length == 3, "real retained layout emits expected structured text segments");
            foreach (var segment in segments)
            {
                // Use the actual backend font measurement, not a duplicated width algorithm.
                var formatted = (FormattedText)createText.Invoke(null, [segment.Layout.Text, segment.Layout.FontFamily,
                    segment.Layout.FontSize, segment.Layout.FontWeight, segment.Layout.MaxWidth, Brushes.Black, segment.Layout.Italic])!;
                Console.WriteLine(JsonSerializer.Serialize(new { text = segment.Layout.Text, width = segment.Rect.Width,
                    assignedHeight = segment.Rect.Height, actualFormattedHeight = formatted.Height }));
                Check(formatted.Height <= segment.Rect.Height + .01,
                    "actual formatted run '" + segment.Layout.Text + "' fits its assigned single line without spurious wrapping");
            }
        }
        session.Editor.Changed -= Count;
        Check((await session.UndoAsync()).Succeeded && session.Editor.CanUndo && session.Editor.CanRedo,
            "busy-input fixture has meaningful undo and redo mutations available");
        await window.Clipboard!.SetTextAsync(" clipboard blocked");

        // Hold the actual repository root gate as a deterministic IO barrier.
        // This is the unchanged real backend semaphore, never a mock repository.
        var gate = (SemaphoreSlim)typeof(NotesRepository).GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(repository)!;
        var inputField = typeof(HavenSceneControl).GetField("_input", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var previousRouter = inputField.GetValue(scene);
        await gate.WaitAsync();
        Task<HavenOS.Home.Core.HomeCoreOperationResult<bool>> saving;
        try
        {
            saving = session.SaveAsync();
            Check(session.IsBusy && !scene.IsEnabled && scene.Root!.GetValue(HavenProperties.Enabled) == false,
                "actual async repository save freezes native and retained input");
            Check(!ReferenceEquals(previousRouter, inputField.GetValue(scene)), "busy transition renews original router to invalidate captured paste identity");
            window.KeyTextInput(" blocked");
            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
            window.KeyPress(Key.Y, RawInputModifiers.Control, PhysicalKey.Y, "y");
            window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, "v");
            Check(Text(session.GetDocumentSnapshot()!) == "Alpha beta gamma" && First(session.GetDocumentSnapshot()!).Runs.All(run => !run.Bold),
                "routed typing, undo, redo and nonempty clipboard paste are denied while actual save awaits IO");
        }
        finally { gate.Release(); }
        Check((await saving).Succeeded && !session.IsDirty && scene.IsEnabled,
            "real save acknowledgment reenables retained input without duplicate edit");
        window.KeyPress(Key.Y, RawInputModifiers.Control, PhysicalKey.Y, "y");
        Check(First(session.GetDocumentSnapshot()!).Runs.Count == 3 && First(session.GetDocumentSnapshot()!).Runs[1].Bold && session.IsDirty,
            "same actual routed redo mutates owner when input is enabled");
        Check((await session.SaveAsync()).Succeeded, "routed redo saves against current real durable revision");
        Check((await repository.LoadAsync(id, default))!.Version == session.DurableRevision,
            "rendered save uses actual durable canonical revision");
        await session.CloseAsync();
        Check((await session.OpenAsync(id, readOnly: true)).Succeeded, "actual saved document reopens read-only");
        var before = JsonSerializer.Serialize(session.GetDocumentSnapshot()!.Sections);
        Check(!scene.IsEnabled && scene.Root!.GetValue(HavenProperties.Enabled) == false,
            "read-only host freezes both native and retained mutation paths");
        window.KeyTextInput(" denied");
        window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
        window.KeyPress(Key.Y, RawInputModifiers.Control, PhysicalKey.Y, "y");
        window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, "v");
        Check(JsonSerializer.Serialize(session.GetDocumentSnapshot()!.Sections) == before && !session.IsDirty,
            "actual routed typing/nonempty-paste and history-key attempts cannot mutate read-only model");
        Check((await session.CloseAsync()).Succeeded, "rendered read-only document closes safely");
        surface.Lifetime!.Dispose();
        await session.DisposeAsync();
        return assertions;
    }
    finally { window.Close(); }
}, CancellationToken.None);
Console.WriteLine(JsonSerializer.Serialize(new { assertions = checks, mode = args.FirstOrDefault() ?? "native-input",
    scope = "actual headless retained CUI/owner/local durable backend; browser GUI not executed", delayedClipboardCompletion = "NOT_RUN" }));

public sealed class WriteSceneTestApplication : Avalonia.Application
{
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<WriteSceneTestApplication>().UseSkia())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    public override void Initialize()
    {
        CuiNativeHost.InitialisePrimitiveTheme(this, "Write");
        WriteRetainedSceneResources.Register(this);
    }
}

// Faults only List after/before a genuine canonical target Load. All data,
// document identities, CAS revisions and storage remain in the actual owner.
sealed class NavigationReadFault(INotesRepository owner, Guid target) : INotesRepository
{
    public bool FailList { get; set; }
    public int TargetLoads { get; private set; }
    public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken token) =>
        FailList ? throw new IOException("Intentional library-read admission failure") : owner.ListAsync(token);
    public async Task<NotesDocument?> LoadAsync(Guid id, CancellationToken token)
    { var result = await owner.LoadAsync(id, token); if (id == target && result is not null) { ++TargetLoads; Console.WriteLine("OWNER_TARGET_LOAD_COMPLETED"); } return result; }
    public Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken token) => owner.SaveAsync(document, reason, token);
    public Task DeleteAsync(Guid id, CancellationToken token) => owner.DeleteAsync(id, token);
    public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid id, CancellationToken token) => owner.GetVersionsAsync(id, token);
    public Task<NotesDocument?> LoadVersionAsync(Guid id, string version, CancellationToken token) => owner.LoadVersionAsync(id, version, token);
    public Task<NotesDocument?> RecoverLatestAsync(Guid id, CancellationToken token) => owner.RecoverLatestAsync(id, token);
    public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken token) => owner.SearchAsync(query, token);
}
