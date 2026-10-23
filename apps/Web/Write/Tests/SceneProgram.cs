using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;
using CakeOS.Cui;
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
    Check(Application.Current!.Styles.All(style => style.GetType().Name is not ("FluentTheme" or "SimpleTheme")),
        "actual retained Write host uses CUI-owned templates without a donor default theme");
    Check(!AssetLoader.Exists(new Uri("avares://Haven/WriteOwnerResources/DefaultTheme.Resources.axaml")),
        "retained Write runs without the removed extracted AXAML theme resource");
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
    if (args.Contains("semantic-selection", StringComparer.Ordinal))
    {
        // Adapter fixture only: the original editor/model/history and real retained
        // scene are used. This is not a replacement Boards document or application.
        var document = NotesDocument.Create("Semantic selection fixture");
        document.Version = 17;
        var block = First(document);
        block.PlainText = "AAbbccDD";
        block.Runs = [new() { Text = "AA" }, new() { Text = "bb", Bold = true },
            new() { Text = "cc", Italic = true }, new() { Text = "DD", Underline = true }];
        var blockId = block.Id;
        var page = document.Sections.SelectMany(section => section.Pages).Single(value => value.Blocks.Contains(block));
        var pageId = page.Id;
        var nextBlock = new NotesBlock { Kind = NotesBlockKind.Paragraph, Order = block.Order + 1,
            PlainText = "tail", Runs = [new() { Text = "tail", Underline = true }] };
        page.Blocks.Add(nextBlock);
        var nextBlockId = nextBlock.Id;
        var editor = new WriteDocumentEditor(document, pageId);
        using var semanticHost = new WriteRetainedSceneControl(() => true);
        semanticHost.SetEditor(editor);
        semanticHost.SetInputAllowed(true);
        var fixtureButton = new Button { Content = "Semantic command fixture" };
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        grid.Children.Add(semanticHost); Grid.SetRow(fixtureButton, 1); grid.Children.Add(fixtureButton);
        var semanticWindow = new Window { Width = 1000, Height = 760, Content = grid };
        NotesBlock Current() => document.Sections.SelectMany(section => section.Pages)
            .SelectMany(page => page.Blocks).Single(value => value.Id == blockId);
        int[] Marks() => Current().Runs.SelectMany(run => Enumerable.Repeat(
            (run.Bold ? 1 : 0) | (run.Italic ? 2 : 0) | (run.Underline ? 4 : 0) | (run.StrikeThrough ? 8 : 0), run.Text.Length)).ToArray();
        string Runs() => JsonSerializer.Serialize(Current().Runs);
        var originalRuns = Runs();
        semanticWindow.Show();
        try
        {
            using (var frame = semanticWindow.CaptureRenderedFrame())
                Check(frame is not null && semanticHost.Bounds.Width > 0, "semantic contract uses a genuinely rendered original retained surface");
            var retainedScene = semanticHost.GetVisualDescendants().OfType<HavenSceneControl>().Single();
            var retainedNode = retainedScene.Root;
            editor.SelectBlock(blockId, caretIndex: 6, selectionStart: 2, selectionEnd: 6);
            var selected = semanticHost.CaptureSemanticSelection();
            Check(selected.ContractVersion == 1 && selected.ArtifactId == document.Id && selected.Revision == 17 &&
                selected.PageScopeId == pageId && selected.AnchorPageId == pageId && selected.CaretPageId == pageId &&
                selected.SelectedBlockId == blockId && selected.Anchor == new WriteDocumentPosition(blockId, 2) &&
                selected.Caret == new WriteDocumentPosition(blockId, 6) && selected.RangeKind == WriteSemanticRangeKind.Block,
                "semantic snapshot identifies exact artifact/page/block and partial range without pixel inference");
            Check(selected.Bold == WriteSemanticMark.Mixed && selected.Italic == WriteSemanticMark.Mixed &&
                selected.Underline == WriteSemanticMark.Off && selected.StrikeThrough == WriteSemanticMark.Off && selected.CanEdit,
                "semantic partial range reports common and mixed marks from actual existing runs");
            Check(Runs() == originalRuns && !editor.CanUndo &&
                semanticHost.CaptureSemanticSelection().Generation == selected.Generation,
                "semantic reads do not split runs, mutate content or manufacture undo/history generations");
            var applied = semanticHost.ToggleSemanticCharacter(selected, WriteCharacterFormat.Bold);
            Check(applied.Succeeded && applied.Code == WriteSemanticActionCode.Applied &&
                Marks().SequenceEqual(new[] { 0, 0, 1, 1, 3, 3, 4, 4 }) && editor.HasSelection &&
                editor.SelectionStart == 2 && editor.SelectionEnd == 6 && editor.CaretIndex == 6,
                "typed mark operation changes only selected characters and preserves unrelated marks/caret");
            Check(ReferenceEquals(retainedScene.Root, retainedNode) && editor.CanUndo && applied.Selection!.Bold == WriteSemanticMark.On,
                "typed operation keeps original surface and original editor undo history");
            Check(semanticHost.ToggleSemanticCharacter(applied.Selection, WriteCharacterFormat.Bold).Succeeded &&
                Marks().SequenceEqual(new[] { 0, 0, 0, 0, 2, 2, 4, 4 }),
                "typed repeated toggle clears only the exact selected range");
            Check(editor.Undo() && editor.Undo() && Runs() == originalRuns,
                "original engine undo restores exact original run bytes and identities across both mark transactions");
            Check(semanticHost.ToggleSemanticCharacter(selected, WriteCharacterFormat.Bold).Code == WriteSemanticActionCode.StaleSelection &&
                Runs() == originalRuns, "undo returning to old content cannot revive a stale action generation");

            editor.SetDocumentCaret(blockId, 6);
            editor.SetDocumentCaret(blockId, 2, extendSelection: true);
            var reversed = semanticHost.CaptureSemanticSelection();
            Check(reversed.RangeKind == WriteSemanticRangeKind.Document && reversed.Anchor == new WriteDocumentPosition(blockId, 6) &&
                reversed.Caret == new WriteDocumentPosition(blockId, 2) && reversed.Bold == WriteSemanticMark.Mixed,
                "semantic document selection preserves reversed anchor/caret direction");
            Check(semanticHost.ToggleSemanticCharacter(reversed, WriteCharacterFormat.Italic).Succeeded &&
                Marks().SequenceEqual(new[] { 0, 0, 3, 3, 2, 2, 4, 4 }) &&
                editor.DocumentAnchor == reversed.Anchor && editor.DocumentCaret == reversed.Caret,
                "reversed range forwards through the same shared operation without collapsing direction");
            Check(editor.Undo() && Runs() == originalRuns, "original undo restores reversed-range formatting preimage");
            editor.SetDocumentCaret(blockId, 7); editor.SetDocumentCaret(nextBlockId, 2, extendSelection: true);
            var acrossBlocks = semanticHost.CaptureSemanticSelection();
            Check(acrossBlocks.Anchor == new WriteDocumentPosition(blockId, 7) &&
                acrossBlocks.Caret == new WriteDocumentPosition(nextBlockId, 2) && acrossBlocks.Underline == WriteSemanticMark.On,
                "cross-block semantic context retains both stable block endpoints and common selected marks");
            Check(semanticHost.ToggleSemanticCharacter(acrossBlocks, WriteCharacterFormat.Underline).Succeeded &&
                Marks().SequenceEqual(new[] { 0, 0, 1, 1, 2, 2, 4, 0 }) &&
                document.Sections.SelectMany(section => section.Pages).SelectMany(value => value.Blocks).Single(value => value.Id == nextBlockId)
                    .Runs.SelectMany(run => Enumerable.Repeat(run.Underline, run.Text.Length)).SequenceEqual(new[] { false, false, true, true }),
                "cross-block typed mark operation preserves unselected characters at both boundaries");
            Check(editor.Undo() && Runs() == originalRuns, "shared undo restores cross-block formatting without replacing editor ownership");
            var beforeMove = semanticHost.CaptureSemanticSelection();
            editor.SetDocumentCaret(blockId, 1);
            Check(semanticHost.ToggleSemanticCharacter(beforeMove, WriteCharacterFormat.Bold).Code == WriteSemanticActionCode.StaleSelection &&
                Runs() == originalRuns, "programmatic selection changes without mutation notifications still reject stale range actions");

            var beforeRevision = semanticHost.CaptureSemanticSelection();
            document.Version++;
            Check(semanticHost.ToggleSemanticCharacter(beforeRevision, WriteCharacterFormat.Bold).Code == WriteSemanticActionCode.RevisionConflict &&
                Runs() == originalRuns, "changed owning persisted revision rejects a captured action without touching content");
            document.Version = 17; // Restore only this deliberately injected fixture revision.
            var beforeOwner = semanticHost.CaptureSemanticSelection();
            semanticHost.SetEditor(new WriteDocumentEditor(NotesDocument.Create("Other owner")));
            semanticHost.SetEditor(editor);
            Check(semanticHost.ToggleSemanticCharacter(beforeOwner, WriteCharacterFormat.Bold).Code == WriteSemanticActionCode.StaleOwner &&
                Runs() == originalRuns, "editor ABA rebinding cannot revive an old owner token");
            var guarded = semanticHost.CaptureSemanticSelection();
            semanticHost.SetReadOnly(true);
            Check(semanticHost.ToggleSemanticCharacter(guarded, WriteCharacterFormat.Bold).Code == WriteSemanticActionCode.ReadOnly &&
                !semanticHost.CaptureSemanticSelection().CanEdit && Runs() == originalRuns,
                "actual host read-only fence rejects typed edits even with an older editable snapshot");
            semanticHost.SetReadOnly(false);
            Check(semanticHost.ToggleSemanticCharacter(guarded, WriteCharacterFormat.Bold).Code == WriteSemanticActionCode.StaleSelection,
                "read-only permission ABA does not revive an older action generation");
            semanticHost.SetInputAllowed(false);
            Check(semanticHost.ToggleSemanticCharacter(semanticHost.CaptureSemanticSelection(), WriteCharacterFormat.Bold).Code == WriteSemanticActionCode.InputSuspended &&
                Runs() == originalRuns, "save/navigation input suspension fences typed mutations as well as native input");
            semanticHost.SetInputAllowed(true);
            guarded = semanticHost.CaptureSemanticSelection();
            Check(semanticHost.ToggleSemanticCharacter(guarded, (WriteCharacterFormat)999).Code == WriteSemanticActionCode.UnsupportedFormat &&
                Runs() == originalRuns, "unknown formatting enum refuses without creating a mutation/history entry");
            Check(semanticHost.ToggleSemanticCharacter(guarded with { CaretPageId = Guid.NewGuid() }, WriteCharacterFormat.Bold).Code == WriteSemanticActionCode.StaleSelection &&
                Runs() == originalRuns, "a mismatched page identity cannot redirect a captured block range action");
            var wrongThread = await Task.Run(() => semanticHost.ToggleSemanticCharacter(guarded, WriteCharacterFormat.Bold));
            Check(wrongThread.Code == WriteSemanticActionCode.WrongThread && Runs() == originalRuns,
                "off-owner-thread typed operation refuses before accessing retained UI or document state");
            Check(semanticHost.ToggleSemanticCharacter(null, WriteCharacterFormat.Bold).Code == WriteSemanticActionCode.InvalidArgument &&
                semanticHost.ToggleSemanticCharacter(guarded with { ContractVersion = 999 }, WriteCharacterFormat.Bold).Code == WriteSemanticActionCode.UnsupportedVersion,
                "missing or unknown-version context refuses instead of silently selecting a different target");

            editor.SetDocumentCaret(blockId, 6); editor.SetDocumentCaret(blockId, 2, extendSelection: true);
            retainedScene.FocusElement(retainedScene.Root!);
            var focused = semanticHost.CaptureSemanticSelection();
            Check(focused.EditorHadFocus, "focus restoration precondition observes real retained editor focus");
            fixtureButton.Focus();
            Check(fixtureButton.IsKeyboardFocusWithin && !retainedScene.IsKeyboardFocusWithin,
                "actual fixture toolbar owns focus before the typed formatting request");
            Check(semanticHost.ToggleSemanticCharacter(focused, WriteCharacterFormat.StrikeThrough, restoreEditorFocus: true).Succeeded &&
                retainedScene.IsKeyboardFocusWithin && editor.DocumentAnchor == focused.Anchor && editor.DocumentCaret == focused.Caret,
                "explicit toolbar request restores previously held editor focus without losing reversed selection");
            fixtureButton.Focus();
            var toolbarFocused = semanticHost.CaptureSemanticSelection();
            Check(!toolbarFocused.EditorHadFocus &&
                semanticHost.ToggleSemanticCharacter(toolbarFocused, WriteCharacterFormat.StrikeThrough, restoreEditorFocus: true).Succeeded &&
                fixtureButton.IsKeyboardFocusWithin,
                "typed action never steals focus that the captured editor did not own");
            retainedScene.FocusElement(retainedScene.Root!);
            var beforeRetire = semanticHost.CaptureSemanticSelection();
            fixtureButton.Focus();
            var replacementDocument = NotesDocument.Create("Reentrant replacement owner");
            var replacementEditor = new WriteDocumentEditor(replacementDocument);
            var replacementBefore = JsonSerializer.Serialize(replacementDocument);
            void RetireDuringMutation(object? sender, EventArgs eventArgs) => semanticHost.SetEditor(replacementEditor);
            editor.Changed += RetireDuringMutation;
            WriteSemanticActionResult retiredDuringMutation;
            try { retiredDuringMutation = semanticHost.ToggleSemanticCharacter(beforeRetire, WriteCharacterFormat.Bold, restoreEditorFocus: true); }
            finally { editor.Changed -= RetireDuringMutation; }
            Check(retiredDuringMutation.Succeeded && retiredDuringMutation.Code == WriteSemanticActionCode.AppliedOwnerRetired &&
                retiredDuringMutation.Selection is null && JsonSerializer.Serialize(replacementDocument) == replacementBefore &&
                fixtureButton.IsKeyboardFocusWithin && semanticHost.CaptureSemanticSelection().ArtifactId == replacementDocument.Id,
                "reentrant retirement reports original-owner mutation truthfully without formatting or focusing the replacement owner");
            semanticHost.SetEditor(editor);
            var retired = semanticHost.CaptureSemanticSelection();
            semanticHost.SetEditor(null);
            Check(semanticHost.ToggleSemanticCharacter(retired, WriteCharacterFormat.Bold).Code == WriteSemanticActionCode.NoEditor &&
                semanticHost.CaptureSemanticSelection().Bold == WriteSemanticMark.Unavailable,
                "detached owner exposes unavailable semantic state and refuses captured edits");
            semanticHost.SetEditor(editor);
            retired = semanticHost.CaptureSemanticSelection();
            var beforeDispose = Runs();
            semanticHost.Dispose();
            Check(semanticHost.ToggleSemanticCharacter(retired, WriteCharacterFormat.Bold).Code == WriteSemanticActionCode.Disposed && Runs() == beforeDispose,
                "disposed adapter rejects old requests without disposing or mutating canonical document/history");
            return assertions;
        }
        finally { semanticWindow.Close(); }
    }
    // A real one-pixel PNG, so the picked image journey stages genuine image bytes.
    var fixtureImagePath = Path.Combine(Path.GetTempPath(), "write-scene-image.png");
    File.WriteAllBytes(fixtureImagePath, Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8AAAwAB/AF/9Qn9WQAAAABJRU5ErkJggg=="));
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
        var navigationWindow = new Window { Width = 1100, Height = 920, Content = rootA };
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
        var pickerWindow = new Window { Width = 1100, Height = 920, Content = pickerRoot };
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
    var session = new WriteBrowserSession(repository, new WriteNativeDocumentPackageStore(), null, null,
        new SceneImageSource(fixtureImagePath), new NotesAttachmentStore(paths, new ProductionDiagnostics(paths)));
    var parser = new CuiRichParser();
    // An explicit fixture path permits replaying the captured original authored CUI
    // as a negative control; the default always loads the production surface.
    var document = parser.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("WRITE_CUI_FIXTURE_PATH")
        ?? Path.Combine(AppContext.BaseDirectory, "Write.cui")));
    foreach (var diagnostic in parser.Diagnostics.Diagnostics) Console.WriteLine(JsonSerializer.Serialize(diagnostic));
    Check(!parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error), "actual owning Write.cui parses");
    var surface = WriteBrowserSurface.Create(document, session, () => true);
    using var loader = new CuiControlLoader(surface.ControlRegistry!);
    loader.SetBindingContext(session);
    var homeRequests = 0;
    var shellActions = new BrowserShellActions(session, () => ++homeRequests);
    loader.SetActionDispatcher(shellActions);
    var (root, diagnostics) = loader.TryLoad(document);
    foreach (var diagnostic in diagnostics) Console.WriteLine(JsonSerializer.Serialize(diagnostic));
    Check(root is not null && !diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error), "actual registered owner surface lowers");
    if (root is null) throw new InvalidOperationException("Write CUI did not produce a root.");
    // The owned Write.cui is the browser surface of record. Prove every declared
    // action and every root-scope binding still matches the live session, so markup
    // drift cannot leave a control that silently reports UnsupportedFeature.
    // A component action is either a direct command or a named reference into the
    // document's <Actions> catalogue; resolve both to the command the session sees.
    var declaredCommands = document.Components.SelectMany(Descend)
        .SelectMany(component => component.Actions.Values)
        .Select(reference => document.Actions.TryGetValue(reference.Name, out var definition)
            ? definition.Command
            : reference.Name)
        .Concat(document.Actions.Values.Select(action => action.Command))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(command => command, StringComparer.Ordinal)
        .ToArray();
    Check(declaredCommands.Length > 0, "actual owning Write.cui declares real actions to validate");
    foreach (var command in declaredCommands)
        Check(command == "ShellHome" ? shellActions.IsActionAvailable(command) == true : WriteBrowserSession.DispatchableCommands.Contains(command),
            "Write.cui action " + command + " is a dispatchable owner or shell command");
    var rootScopeBindings = document.Components.SelectMany(RootScopeBindings)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(path => path, StringComparer.Ordinal)
        .ToArray();
    Check(rootScopeBindings.Length > 0, "actual owning Write.cui declares real root-scope bindings to validate");
    foreach (var bindingPath in rootScopeBindings)
        Check(session.TryGetValue(bindingPath, out _),
            "Write.cui binding " + bindingPath + " resolves against the live session");
    foreach (var command in WriteBrowserSession.DispatchableCommands)
        Check(declaredCommands.Contains(command),
            "dispatchable session command " + command + " is still declared by Write.cui");
    static IEnumerable<CuiComponent> Descend(CuiComponent component)
    {
        yield return component;
        foreach (var child in component.Children) foreach (var nested in Descend(child)) yield return nested;
        foreach (var child in component.ElseChildren) foreach (var nested in Descend(child)) yield return nested;
    }
    // Repeat and list bodies evaluate in an item scope, so only bindings that render
    // against the session itself are checked here.
    static IEnumerable<string> RootScopeBindings(CuiComponent component)
    {
        if (component.Repeat is not null || component.List is not null) yield break;
        if (component.Condition?.Test is CuiBindingValue condition) yield return condition.Path;
        foreach (var value in component.Properties.Values)
            if (value is CuiBindingValue binding) yield return binding.Path;
        foreach (var child in component.Children) foreach (var path in RootScopeBindings(child)) yield return path;
        foreach (var child in component.ElseChildren) foreach (var path in RootScopeBindings(child)) yield return path;
    }
    loader.WireBindings(root!);
    loader.WireBindings(root!); // The existing runtime promises idempotent event binding.
    var window = new Window { Width = 1100, Height = 920, Content = root };
    window.Show();
    try
    {
        using (var initialFrame = window.CaptureRenderedFrame())
            Check(initialFrame is not null, "actual native templates render before querying editor descendants");
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
        var host = AuthoredTree(root!).OfType<WriteRetainedSceneControl>().Single();
        var scene = host.GetVisualDescendants().OfType<HavenSceneControl>().Single();
        if (args.Contains("input", StringComparer.Ordinal))
        {
            var home = root!.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "ShellHome"));
            home.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
            Check(homeRequests == 1 && session.DocumentId is null, "start Home delegates once to the real shell dispatcher without creating a document");
            var create = root!.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "CreateDocument"));
            create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
            using (var frame = window.CaptureRenderedFrame()) { }
            var point = scene.TranslatePoint(new Avalonia.Point(scene.Bounds.Width / 2, 100), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Check(session.Editor!.DocumentAnchor is null, "a completed click has a caret without a latent selection anchor");
            bool? keyHandled = null;
            window.AddHandler(InputElement.KeyDownEvent, (_, e) => keyHandled = e.Handled,
                RoutingStrategies.Bubble, handledEventsToo: true);
            window.KeyPress(Key.C, RawInputModifiers.None, PhysicalKey.C, "C");
            Check(keyHandled == false, "first printable key after a real pointer focus remains available for browser text input");
            window.KeyTextInput("C");
            window.KeyRelease(Key.C, RawInputModifiers.None, PhysicalKey.C, "C");
            Check(Text(session.GetDocumentSnapshot()!) == "C", "first separately routed character survives pointer focus");
            window.KeyTextInput("at");
            Check(Text(session.GetDocumentSnapshot()!) == "Cat", "subsequent characters retain the first character and order");
            window.KeyPress(Key.Left, RawInputModifiers.Shift, PhysicalKey.ArrowLeft, "ArrowLeft");
            window.KeyPress(Key.Right, RawInputModifiers.Shift, PhysicalKey.ArrowRight, "ArrowRight");
            Check(session.Editor.DocumentAnchor is null && !session.Editor.HasDocumentSelection,
                "extending back to the anchor collapses keyboard selection");
            window.KeyTextInput("s"); window.KeyTextInput("!");
            Check(Text(session.GetDocumentSnapshot()!) == "Cats!", "typing after a collapsed keyboard selection preserves successive characters");
            window.KeyPress(Key.Left, RawInputModifiers.Shift, PhysicalKey.ArrowLeft, "ArrowLeft");
            Check(session.Editor.SelectedDocumentText == "!", "nonempty keyboard selection still selects the exact character");
            window.KeyTextInput("?");
            Check(Text(session.GetDocumentSnapshot()!) == "Cats?", "typing still replaces a real selection exactly once");
            Check((await session.PrepareToCloseAsync()).Succeeded, "real owner saves the repaired typing sequence before retirement");
            surface.Lifetime!.Dispose(); await session.DisposeAsync(); return assertions;
        }
        if (args.Contains("launch", StringComparer.Ordinal))
        {
            bool HasWorkspace() => root!.GetLogicalDescendants().OfType<WriteRetainedSceneControl>().Any();
            bool HasStart() => root!.GetLogicalDescendants().OfType<Control>().Any(control => control.Name == "write-start");
            Check(HasStart() && !HasWorkspace() && session.DocumentId is null, "fresh Write launch composes only start actions and recents, with no editor in layout or accessibility tree");
            using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(paths.DataDirectory, "write-start.png"));
            var create = root!.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "CreateDocument"));
            create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
            using (var frame = window.CaptureRenderedFrame()) { }
            Check(session.DocumentId is not null && HasWorkspace() && !HasStart(), "actual start action activates the editor without stacking the start surface above it");
            var launchId = session.DocumentId!.Value;
            var launchBlock = First(session.GetDocumentSnapshot()!).Id;
            var retainedEditor = session.Editor;
            var retainedNode = scene.Root;
            scene.FocusElement(scene.Root!); window.KeyTextInput("Retained unsaved text");
            await session.SelectAsync(launchBlock, 0, 8);
            var revision = session.DurableRevision;
            var launchBefore = JsonSerializer.Serialize(session.GetDocumentSnapshot());
            Check(session.IsDirty && Text(session.GetDocumentSnapshot()!) == "Retained unsaved text", "actual routed typing creates a dirty owner draft");
            var back = AuthoredTree(root!).OfType<MenuItem>().Single(control => Equals(control.Tag, "ReturnToStart"));
            back.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await loader.WhenActionsIdleAsync();
            using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(paths.DataDirectory, "write-return-to-start.png"));
            Check(HasStart() && !HasWorkspace() && !scene.IsEnabled, "return-to-start detaches and gates the retained editor input and accessibility");
            Check(session.IsDirty && session.DurableRevision == revision && launchBefore == JsonSerializer.Serialize(session.GetDocumentSnapshot()), "return-to-start neither saves nor replaces the unsaved draft");
            Check(Text((await repository.LoadAsync(launchId, default))!) == "", "return-to-start does not silently publish dirty content to the owner store");
            var resume = root.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "ResumeDocument"));
            resume.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
            using (var frame = window.CaptureRenderedFrame()) { }
            Check(HasWorkspace() && !HasStart() && ReferenceEquals(retainedEditor, session.Editor) && ReferenceEquals(retainedNode, scene.Root), "resume restores the same owner editor and retained document surface");
            Check(session.Editor!.SelectionStart == 0 && session.Editor.SelectionEnd == 8 && session.Editor.CanUndo, "resume preserves selection and undo history");
            await session.UndoAsync(); Check(Text(session.GetDocumentSnapshot()!) == "", "original undo works after return and resume");
            await session.RedoAsync(); Check(Text(session.GetDocumentSnapshot()!) == "Retained unsaved text", "original redo restores authored text after resume");
            Check((await session.SaveAsync()).Succeeded, "resumed document explicitly saves through its original owner");
            Check((await session.CloseAsync()).Succeeded && HasStart() && !HasWorkspace(), "closing a saved document returns to a genuine start-only composition");
            Check((await session.OpenAsync(launchId)).Succeeded && Text(session.GetDocumentSnapshot()!) == "Retained unsaved text", "saved document reopens from actual repository after close");
            window.Width = 390; using (var frame = window.CaptureRenderedFrame()) { }
            await session.DispatchAsync("ReturnToStart", null);
            using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(paths.DataDirectory, "write-start-mobile.png"));
            Check(HasStart() && !HasWorkspace(), "narrow viewport retains start-only composition");
            await session.DispatchAsync("ResumeDocument", null);
            using (var frame = window.CaptureRenderedFrame()) { }
            Check(HasWorkspace() && !HasStart(), "narrow resume restores workspace-only composition");
            var properties = root!.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Tag, "ToggleInspector"));
            using (var frame = window.CaptureRenderedFrame()) { }
            var propertiesPoint = properties.TranslatePoint(new Point(properties.Bounds.Width / 2, properties.Bounds.Height / 2), window)!.Value;
            var propertiesHit = window.InputHitTest(propertiesPoint) as Visual;
            Check(ReferenceEquals(propertiesHit, properties) || propertiesHit?.GetVisualAncestors().Contains(properties) == true,
                "the narrow toolbar keeps the real Properties control directly in pointer reach without scrolling");
            window.MouseDown(propertiesPoint, MouseButton.Left); window.MouseUp(propertiesPoint, MouseButton.Left);
            await loader.WhenActionsIdleAsync();
            using (var frame = window.CaptureRenderedFrame()) { }
            Check(TopLevel.GetTopLevel(scene) is null, "compact properties detaches the document editor from native input");
            window.Width = 1100;
            using (var frame = window.CaptureRenderedFrame()) { }
            Check(TopLevel.GetTopLevel(scene) == window && session.TryGetValue("ShowEditingArea", out var restored) && restored is true,
                "widening while properties is open restores the retained Write editor");
            surface.Lifetime!.Dispose(); await session.DisposeAsync(); return assertions;
        }
        if (args.Contains("toolbar", StringComparer.Ordinal))
        {
            var commands = new Dictionary<string, string> { ["New"] = "CreateDocument", ["Save"] = "SaveDocument", ["Bold"] = "BoldSelection", ["Undo"] = "Undo", ["Redo"] = "Redo", ["Close"] = "CloseDocument" };
            var controls = AuthoredTree(root!).Concat(root!.GetVisualDescendants().OfType<Control>()).Distinct().ToArray();
            var buttons = commands.ToDictionary(pair => pair.Key, pair => controls.Where(control => Equals(control.Tag, pair.Value))
                .OrderByDescending(control => control is Button && control.IsEffectivelyVisible).First());
            foreach (var button in buttons.Values.OfType<Button>())
                BrowserActionAvailability.ApplyInitial(button, loader, document, session);
            void State(string name, bool expected) => Check(buttons[name].IsEnabled == expected,
                "actual toolbar " + name + " enabled=" + expected);
            void Empty()
            {
                State("New", true);
                foreach (var name in new[] { "Save", "Bold", "Italic", "Underline", "Strikethrough",
                    "A4", "Letter", "Portrait", "Landscape", "Paginated", "Continuous",
                    "Align left", "Align center", "Align right", "Justify", "Undo", "Redo", "Close",
                    "Paragraph", "Heading", "Quote", "Code", "Bullets", "Numbering", "Checklist", "Page break", "Divider", "Insert table",
                    "Add list item", "Set bullets", "Set numbering", "Set checklist",
                    "Add table row", "Add table column", "Remove table row", "Remove table column",
                    "Find previous", "Find next", "Replace all",
                    "Move block up", "Move block down", "Delete block",
                    "Split run", "Merge run", "Merge cell right", "Split cell",
                    "Set normal", "Set heading 1", "Set heading 2", "Set quote", "Set code" }) State(name, false);
            }
            void ClickStart(string name)
            {
                Check(buttons[name].IsEnabled, "actual " + name + " control permits interaction");
                if (buttons[name] is MenuItem menuItem)
                {
                    menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    return;
                }
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
            State("Save", true); State("Bold", true); State("Italic", true);
            State("Underline", true); State("Strikethrough", true);
            foreach (var page in new[] { "A4", "Letter", "Portrait", "Landscape", "Paginated", "Continuous" })
                State(page, true);
            State("Close", true);
            State("Find previous", true); State("Find next", true); State("Replace all", true);
            State("Move block up", true); State("Move block down", true); State("Delete block", false);
            State("Split run", true); State("Merge run", true);
            State("Merge cell right", false); State("Split cell", false);
            foreach (var styleName in new[] { "Set normal", "Set heading 1", "Set heading 2", "Set quote", "Set code" })
                State(styleName, true);
            State("Undo", false); State("Redo", false); State("Add list item", false);
            foreach (var listKind in new[] { "Set bullets", "Set numbering", "Set checklist" }) State(listKind, false);
            foreach (var table in new[] { "Add table row", "Add table column", "Remove table row", "Remove table column" })
                State(table, false);
            foreach (var insert in new[] { "Paragraph", "Heading", "Quote", "Code", "Bullets", "Numbering", "Checklist", "Page break", "Divider", "Insert table" })
                State(insert, true);
            var toolbarId = session.DocumentId!.Value;
            var toolbarBlock = First(session.GetDocumentSnapshot()!).Id;
            await session.SelectAsync(toolbarBlock, 0, 0);
            scene.FocusElement(scene.Root!);
            window.KeyTextInput("Alpha beta gamma");
            Check(Text(session.GetDocumentSnapshot()!) == "Alpha beta gamma", "toolbar journey actual routed typing mutates owner");
            var wordCountBlock = root!.GetVisualDescendants().OfType<TextBlock>()
                .Single(block => AutomationProperties.GetName(block) == "Document word count");
            Check(session.TryGetValue("WordCount", out var typedWords) && Equals(typedWords, 3) &&
                wordCountBlock.Text == "3",
                "toolbar reports the typed document's word count through the session and the rendered control, actual " +
                wordCountBlock.Text);
            var findInput = root!.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Text to find");
            var replaceInput = root!.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Replacement text");
            var findStatus = root!.GetVisualDescendants().OfType<TextBlock>()
                .Single(block => AutomationProperties.GetName(block) == "Find and replace status");
            Check(findInput.IsEnabled && replaceInput.IsEnabled,
                "toolbar find and replace inputs permit interaction");
            Check(session.TryGetValue("FindStatus", out var promptedQuery) &&
                Equals(promptedQuery, "Enter text to find.") && findStatus.Text == "Enter text to find.",
                "toolbar find prompts for a query before searching, actual " + findStatus.Text);
            findInput.Text = "a";
            Check(session.TryGetValue("FindMatchCount", out var lowerMatches) && Equals(lowerMatches, 5),
                "toolbar find counts every case-insensitive match in the authored paragraph");
            await Click("Find next", () => session.TryGetValue("FindStatus", out var firstMatch) &&
                Equals(firstMatch, "5 match(es); showing 1."));
            await Click("Find next", () => session.TryGetValue("FindStatus", out var secondMatch) &&
                Equals(secondMatch, "5 match(es); showing 2."));
            await Click("Find previous", () => session.TryGetValue("FindStatus", out var previousMatch) &&
                Equals(previousMatch, "5 match(es); showing 1."));
            Check(findStatus.Text == "5 match(es); showing 1.",
                "toolbar renders the live find position, actual " + findStatus.Text);
            findInput.Text = "omega";
            Check(session.TryGetValue("FindMatchCount", out var absentMatches) && Equals(absentMatches, 0) &&
                session.TryGetValue("FindStatus", out var absentStatus) &&
                Equals(absentStatus, "No matches."),
                "toolbar find reports a genuinely empty result set");
            await Click("Find next", () => session.ErrorCode == "NoMatches");
            findInput.Text = string.Empty;
            Check(session.TryGetValue("FindStatus", out var clearedStatus) &&
                Equals(clearedStatus, "Enter text to find."),
                "toolbar find returns to its prompt when the query is cleared");
            await Click("Find next", () => session.ErrorCode == "EmptyQuery");
            Check(First(session.GetDocumentSnapshot()!) is { } searchBlock && searchBlock.Runs.All(run => !run.Bold) &&
                searchBlock.Runs.Count == 1 && searchBlock.Runs[0].Text == "Alpha beta gamma",
                "find navigation is read-only and leaves the authored paragraph untouched");
            State("Undo", true); State("Redo", false);
            await session.SelectAsync(toolbarBlock, 6, 10);
            await Click("Bold", () => First(session.GetDocumentSnapshot()!).Runs.Any(run => run.Bold));
            Check(First(session.GetDocumentSnapshot()!).Runs is { Count: 3 } toolbarRuns && toolbarRuns[1].Text == "beta" && toolbarRuns[1].Bold,
                "toolbar actual Bold changes only selected structured run");
            await Click("Undo", () => First(session.GetDocumentSnapshot()!).Runs.All(run => !run.Bold));
            State("Redo", true);
            await Click("Redo", () => First(session.GetDocumentSnapshot()!).Runs.Any(run => run.Bold));
            State("Redo", false);
            await Click("Italic", () => First(session.GetDocumentSnapshot()!).Runs.Any(run => run.Italic));
            await Click("Underline", () => First(session.GetDocumentSnapshot()!).Runs.Any(run => run.Underline));
            await Click("Strikethrough", () => First(session.GetDocumentSnapshot()!).Runs.Any(run => run.StrikeThrough));
            Check(First(session.GetDocumentSnapshot()!).Runs is { Count: 3 } decorated &&
                decorated[1].Text == "beta" &&
                decorated[1].Bold && decorated[1].Italic && decorated[1].Underline && decorated[1].StrikeThrough &&
                !decorated[0].Bold && !decorated[0].Italic && !decorated[0].Underline && !decorated[0].StrikeThrough &&
                !decorated[2].Bold && !decorated[2].Italic && !decorated[2].Underline && !decorated[2].StrikeThrough,
                "toolbar Italic, Underline and Strikethrough decorate only the selected structured run");
            Check(session.TryGetValue("ParagraphAlignment", out var initialAlignment) &&
                Equals(initialAlignment, NotesTextAlignment.Left), "toolbar paragraph begins left aligned");
            await Click("Align center", () => session.TryGetValue("ParagraphAlignment", out var centered) &&
                Equals(centered, NotesTextAlignment.Center));
            Check(First(session.GetDocumentSnapshot()!).Paragraph.Alignment == NotesTextAlignment.Center &&
                First(session.GetDocumentSnapshot()!).Runs[1].Bold,
                "toolbar actual Align center changes only the selected paragraph alignment and keeps prior formatting");
            Check(session.TryGetValue("LineSpacing", out var authoredSpacing) && Equals(authoredSpacing, 1.25d),
                "toolbar paragraph reports its authored default line spacing through the two-way binding");
            var numericInputs = root!.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
            Check(numericInputs.Length == 15, "toolbar exposes one font, four paragraph, two table, one margin and seven image number input");
            var fontInput = numericInputs[0];
            var paragraphInputs = numericInputs.Skip(1).Take(4).ToArray();
            var tableInputs = numericInputs.Skip(5).Take(2).ToArray();
            var marginInput = numericInputs[7];
            var mediaInputs = numericInputs.Skip(8).Take(7).ToArray();
            for (var index = 0; index < 8; index++)
                Check(numericInputs[index].IsEnabled, "toolbar number input " + index + " permits interaction");
            Check(mediaInputs.All(input => !input.IsEnabled),
                "the image property inputs stay disabled while no image is selected");
            Check(session.TryGetValue("TableRows", out var defaultRows) && Equals(defaultRows, 3) &&
                session.TryGetValue("TableColumns", out var defaultColumns) && Equals(defaultColumns, 3),
                "toolbar table dimensions begin at the editor's authored three by three defaults");
            paragraphInputs[0].Value = 1.5m;
            paragraphInputs[1].Value = 48m;
            paragraphInputs[2].Value = -12m;
            paragraphInputs[3].Value = 6m;
            Check(session.TryGetValue("LineSpacing", out var editedSpacing) && Equals(editedSpacing, 1.5d) &&
                session.TryGetValue("LeftIndent", out var editedIndent) && Equals(editedIndent, 48d) &&
                session.TryGetValue("FirstLineIndent", out var editedFirst) && Equals(editedFirst, -12d) &&
                session.TryGetValue("SpaceAfter", out var editedAfter) && Equals(editedAfter, 6d),
                "two-way paragraph inputs write through to the owning editor's paragraph format");
            paragraphInputs[0].Value = 99m;
            Check(session.TryGetValue("LineSpacing", out var rejectedSpacing) && Equals(rejectedSpacing, 1.5d) &&
                CuiInputValidationProperties.GetHasError(paragraphInputs[0]),
                "out-of-range line spacing is rejected for validation and never reaches the owning document");
            paragraphInputs[0].Value = 1.5m;
            Check(session.TryGetValue("FontFamily", out var authoredFamily) && Equals(authoredFamily, "Montserrat") &&
                session.TryGetValue("FontSize", out var authoredSize) && Equals(authoredSize, 14d) &&
                session.TryGetValue("TextColor", out var authoredColour) && Equals(authoredColour, "#FFEEEEEE") &&
                session.TryGetValue("TextHighlight", out var authoredHighlight) && Equals(authoredHighlight, "#00000000"),
                "toolbar reports the selected run's authored font family, size and colours through the two-way bindings");
            Check(fontInput.IsEnabled, "toolbar font size input permits interaction");
            fontInput.Value = 24m;
            Check(First(session.GetDocumentSnapshot()!).Runs[1].FontSize == 24d &&
                session.TryGetValue("FontSize", out var editedSize) && Equals(editedSize, 24d),
                "two-way font size writes through to the owning structured run and reads back through the binding");
            fontInput.Value = 3m;
            Check(CuiInputValidationProperties.GetHasError(fontInput) &&
                First(session.GetDocumentSnapshot()!).Runs[1].FontSize == 24d,
                "font size below the editor's range is rejected for validation and never reaches the run");
            fontInput.Value = 18m;
            var fontFamilyInput = root!.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Font family for the selected text");
            Check(fontFamilyInput.IsEnabled, "toolbar font family input permits interaction");
            fontFamilyInput.Text = "Segoe UI";
            Check(First(session.GetDocumentSnapshot()!).Runs[1].FontFamily == "Segoe UI" &&
                session.TryGetValue("FontFamily", out var editedFamily) && Equals(editedFamily, "Segoe UI"),
                "two-way font family writes through to the owning structured run");
            var textColourInput = root!.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Text colour as a hex colour value");
            textColourInput.Text = "#112233";
            Check(First(session.GetDocumentSnapshot()!).Runs[1].Foreground == "#112233",
                "two-way text colour writes through to the owning structured run");
            textColourInput.Text = "red";
            Check(First(session.GetDocumentSnapshot()!).Runs[1].Foreground == "#112233",
                "a non-hex text colour is rejected for validation and never reaches the run");
            var highlightInput = root!.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Text highlight colour as a hex colour value");
            highlightInput.Text = "#00FF00FF";
            Check(First(session.GetDocumentSnapshot()!).Runs[1].Background == "#00FF00FF",
                "two-way highlight colour writes through to the owning structured run");
            var linkInput = root!.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Hyperlink for the selected text");
            Check(linkInput.IsEnabled, "toolbar hyperlink input permits interaction");
            Check(session.TryGetValue("Link", out var authoredLink) && Equals(authoredLink, string.Empty),
                "toolbar reports an empty hyperlink before the writer sets one");
            linkInput.Text = "https://example.test/write";
            Check(First(session.GetDocumentSnapshot()!).Runs[1].Link == "https://example.test/write" &&
                session.TryGetValue("Link", out var writtenLink) &&
                Equals(writtenLink, "https://example.test/write"),
                "two-way hyperlink writes through to the owning structured run");
            Check(First(session.GetDocumentSnapshot()!).Runs is { Count: 3 } linkedRuns &&
                linkedRuns[1].Text == "beta" && linkedRuns[0].Link is null && linkedRuns[2].Link is null,
                "setting a hyperlink on an exact run boundary leaves the run structure intact");
            linkInput.Text = string.Empty;
            Check(First(session.GetDocumentSnapshot()!).Runs[1].Link is null &&
                session.TryGetValue("Link", out var clearedLink) && Equals(clearedLink, string.Empty),
                "clearing the hyperlink input removes the link from the owning structured run");
            linkInput.Text = "https://example.test/write";
            Check(First(session.GetDocumentSnapshot()!).Runs is { Count: 3 } relinked &&
                relinked[1].Text == "beta" && relinked[1].Link == "https://example.test/write",
                "restoring the hyperlink keeps the authored run structure in place");
            await session.SelectAsync(toolbarBlock, 8, 8);
            await Click("Split run", () => First(session.GetDocumentSnapshot()!).Runs is { Count: 4 });
            Check(First(session.GetDocumentSnapshot()!).Runs is
                    [{ Text: "Alpha " }, { Text: "be" }, { Text: "ta" }, { Text: " gamma" }] splitRuns &&
                splitRuns[1].Bold && splitRuns[2].Bold &&
                splitRuns[1].Link == "https://example.test/write" &&
                splitRuns[2].Link == "https://example.test/write" &&
                Text(session.GetDocumentSnapshot()!) == "Alpha beta gamma",
                "splitting a run at the caret divides it in place and carries the formatting over");
            await Click("Merge run", () => First(session.GetDocumentSnapshot()!).Runs is { Count: 3 });
            Check(First(session.GetDocumentSnapshot()!).Runs is
                    [{ Text: "Alpha " }, { Text: "beta" }, { Text: " gamma" }] mergedRuns &&
                mergedRuns[1].Bold && mergedRuns[1].Link == "https://example.test/write" &&
                Text(session.GetDocumentSnapshot()!) == "Alpha beta gamma",
                "merging the split runs back together restores the authored run and its hyperlink");
            Check(session.TryGetValue("PageMargins", out var authoredMargins) && Equals(authoredMargins, 72d) &&
                session.TryGetValue("ShowPageNumbers", out var authoredPageNumbers) && Equals(authoredPageNumbers, true),
                "toolbar reports the authored page margin and page-number setting through the two-way bindings");
            await Click("Letter", () => session.GetDocumentSnapshot()!.PageSetup is { WidthPoints: 612d, HeightPoints: 792d });
            var sizeLabel = root!.GetVisualDescendants().OfType<TextBlock>()
                .Single(block => AutomationProperties.GetName(block) == "Current page size in points");
            Check(sizeLabel.Text == "612 x 792 pt",
                "toolbar shows the requested Letter page size through the live binding, actual " + sizeLabel.Text);
            Check(session.TryGetValue("PageLayoutMode", out var authoredMode) && Equals(authoredMode, "Continuous"),
                "toolbar reports the authored layout mode through its binding");
            await Click("Paginated", () => session.TryGetValue("PageLayoutMode", out var paginated) &&
                Equals(paginated, "Paginated"));
            await Click("Continuous", () => session.TryGetValue("PageLayoutMode", out var continuous) &&
                Equals(continuous, "Continuous"));
            await Click("A4", () => session.GetDocumentSnapshot()!.PageSetup is { WidthPoints: 595d, HeightPoints: 842d });
            Check(session.GetDocumentSnapshot()!.PageSetup.Orientation == "Portrait",
                "toolbar A4 request keeps the portrait orientation");
            await Click("Landscape", () => session.GetDocumentSnapshot()!.PageSetup is
                { WidthPoints: 842d, HeightPoints: 595d, Orientation: "Landscape" });
            await Click("Portrait", () => session.GetDocumentSnapshot()!.PageSetup is
                { WidthPoints: 595d, HeightPoints: 842d, Orientation: "Portrait" });
            Check(marginInput.IsEnabled, "toolbar page margin input permits interaction");
            marginInput.Value = 48m;
            Check(session.GetDocumentSnapshot()!.PageSetup.MarginTopPoints == 48d &&
                session.GetDocumentSnapshot()!.PageSetup.MarginLeftPoints == 48d &&
                session.TryGetValue("PageMargins", out var editedMargins) && Equals(editedMargins, 48d),
                "two-way page margins write through to every edge of the owning page setup");
            marginInput.Value = 1200m;
            Check(CuiInputValidationProperties.GetHasError(marginInput) &&
                session.GetDocumentSnapshot()!.PageSetup.MarginTopPoints == 48d,
                "a margin beyond the editor's range is rejected for validation and never reaches the page setup");
            var pageNumbers = root!.GetVisualDescendants().OfType<CheckBox>()
                .Single(check => AutomationProperties.GetName(check) == "Show page numbers");
            Check(pageNumbers.IsEnabled, "toolbar page number checkbox permits interaction");
            pageNumbers.IsChecked = true;
            Check(session.GetDocumentSnapshot()!.PageSetup.ShowPageNumbers,
                "two-way page number checkbox writes through to the owning page setup");
            IReadOnlyList<NotesBlock> ToolbarBlocks() => session.GetDocumentSnapshot()!.Sections
                .SelectMany(section => section.Pages).SelectMany(page => page.Blocks).ToArray();
            Check(ToolbarBlocks() is { Count: 1 } && ToolbarBlocks()[0].Kind == NotesBlockKind.Paragraph,
                "toolbar journey begins with its single authored paragraph");
            await Click("Bullets", () => ToolbarBlocks() is { Count: 2 } bullets &&
                bullets[1].Kind == NotesBlockKind.List && bullets[1].List!.Kind == NotesListKind.Bulleted);
            Check(ToolbarBlocks()[1].List!.Items is { Count: 1 } authoredItems &&
                authoredItems[0].Text == "List item",
                "inserted bulleted list carries the editor's authored first item");
            Check(session.TryGetValue("CanEditList", out var canEditList) && Equals(canEditList, true) &&
                session.TryGetValue("SelectedListItems", out var initialListItems) &&
                initialListItems is WriteListItemRow[] { Length: 1 },
                "toolbar journey reaches the inserted bulleted list's single authored item");
            var itemText = root!.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "List item text");
            Check(itemText.IsEnabled, "toolbar list item text input permits interaction");
            itemText.Text = "Edited bullet";
            Check(ToolbarBlocks()[1].List!.Items is [{ Text: "Edited bullet" }],
                "two-way list item text writes through to the owning list item");
            var itemCheck = root!.GetVisualDescendants().OfType<CheckBox>()
                .Single(check => AutomationProperties.GetName(check) == "List item complete");
            itemCheck.IsChecked = true;
            Check(ToolbarBlocks()[1].List!.Items is [{ Checked: true }],
                "two-way list item checkbox writes the checklist state through to the owning list item");
            var itemLevel = root!.GetVisualDescendants().OfType<NumericUpDown>()
                .Single(number => AutomationProperties.GetName(number) == "List item nesting level, from 0 to 8");
            itemLevel.Value = 9m;
            Check(CuiInputValidationProperties.GetHasError(itemLevel) &&
                ToolbarBlocks()[1].List!.Items is [{ Level: 0 }],
                "nesting level beyond the editor's range is rejected for validation and never reaches the list item");
            itemLevel.Value = 2m;
            Check(ToolbarBlocks()[1].List!.Items is [{ Level: 2 }],
                "two-way list item level writes the nesting level through to the owning list item");
            await Click("Add list item", () => ToolbarBlocks()[1].List!.Items.Count == 2 &&
                ToolbarBlocks()[1].List!.Items[1].Text == "New item");
            await Click("Set numbering", () => ToolbarBlocks()[1].List!.Kind == NotesListKind.Numbered);
            await Click("Set checklist", () => ToolbarBlocks()[1].List!.Kind == NotesListKind.Checklist);
            await Click("Set bullets", () => ToolbarBlocks()[1].List!.Kind == NotesListKind.Bulleted);
            Check(ToolbarBlocks()[1].List!.Items is [{ Text: "Edited bullet" }, { Text: "New item" }],
                "changing an existing list's kind keeps its authored items");
            await Click("Numbering", () => ToolbarBlocks() is { Count: 3 } numbered &&
                numbered[2].List!.Kind == NotesListKind.Numbered);
            await Click("Checklist", () => ToolbarBlocks() is { Count: 4 } checklists &&
                checklists[3].List!.Kind == NotesListKind.Checklist);
            await Click("Heading", () => ToolbarBlocks() is { Count: 5 } headings &&
                headings[4].Kind == NotesBlockKind.Heading && headings[4].StyleId == "heading-1");
            await Click("Quote", () => ToolbarBlocks() is { Count: 6 } quotes && quotes[5].Kind == NotesBlockKind.Quote);
            await Click("Code", () => ToolbarBlocks() is { Count: 7 } code && code[6].Kind == NotesBlockKind.Code);
            await Click("Paragraph", () => ToolbarBlocks() is { Count: 8 } paragraphs && paragraphs[7].Kind == NotesBlockKind.Paragraph);
            await Click("Divider", () => ToolbarBlocks() is { Count: 9 } dividers && dividers[8].Kind == NotesBlockKind.Divider);
            await Click("Page break", () => ToolbarBlocks() is { Count: 10 } breaks &&
                breaks[9].Kind == NotesBlockKind.Paragraph && breaks[9].Paragraph.PageBreakBefore);
            tableInputs[0].Value = 4m;
            tableInputs[1].Value = 2m;
            Check(session.TryGetValue("TableRows", out var requestedRows) && Equals(requestedRows, 4) &&
                session.TryGetValue("TableColumns", out var requestedColumns) && Equals(requestedColumns, 2),
                "two-way table inputs request a four by two insert");
            await Click("Insert table", () => ToolbarBlocks() is { Count: 11 } tables &&
                tables[10].Kind == NotesBlockKind.Table &&
                tables[10].Table is { Rows: { Count: 4 } insertedRows } &&
                insertedRows.All(row => row.Cells.Count == 2));
            Check(ToolbarBlocks() is { Count: 11 } tableShape &&
                tableShape[10].Table is { Rows: { Count: 4 } requestedShape } &&
                requestedShape.All(row => row.Cells.Count == 2),
                "inserted table carries the requested shape on every row");
            tableInputs[0].Value = 0m;
            Check(CuiInputValidationProperties.GetHasError(tableInputs[0]) &&
                session.TryGetValue("TableRows", out var rejectedRows) && Equals(rejectedRows, 4),
                "zero table rows are rejected for validation and never reach the session's insert dimensions");
            tableInputs[1].Value = 51m;
            Check(CuiInputValidationProperties.GetHasError(tableInputs[1]) &&
                session.TryGetValue("TableColumns", out var rejectedColumns) && Equals(rejectedColumns, 2),
                "fifty-one table columns are rejected for validation and never reach the session's insert dimensions");
            tableInputs[0].Value = 4m;
            tableInputs[1].Value = 2m;
            Check(session.TryGetValue("CanEditTable", out var canEditTable) && Equals(canEditTable, true) &&
                session.TryGetValue("SelectedTableRows", out var selectedRows) &&
                selectedRows is WriteTableRowRow[] { Length: 4 },
                "toolbar journey reaches the inserted four row table");
            var initialCells = root!.GetVisualDescendants().OfType<TextBox>()
                .Where(box => AutomationProperties.GetName(box) == "Table cell text").ToArray();
            Check(initialCells.Length == 8, "toolbar surface renders every cell of the four by two table");
            initialCells[0].Text = "First cell";
            Check(ToolbarBlocks()[10].Table!.Rows is [{ Cells: [{ Text: "First cell" }, ..] }, ..],
                "two-way table cell text writes through to the owning table cell");
            var cellFillInput = root!.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Table cell background colour");
            Check(session.TryGetValue("CanEditCellBackground", out var canEditCell) && Equals(canEditCell, true) &&
                cellFillInput.IsEnabled && session.TryGetValue("CellBackground", out var emptyCellFill) &&
                Equals(emptyCellFill, "#00000000"),
                "the cell fill binding reports the selected cell's own background once a cell is written");
            cellFillInput.Text = "#334455";
            Check(ToolbarBlocks()[10].Table!.Rows is [{ Cells: [{ Text: "First cell", Background: "#334455" }, ..] }, ..],
                "two-way cell fill writes the typed colour into the selected cell's own background");
            cellFillInput.Text = "red";
            Check(ToolbarBlocks()[10].Table!.Rows is [{ Cells: [{ Background: "#334455" }, ..] }, ..],
                "a non-hex cell fill is rejected for validation and never reaches the cell");
            await Click("Add table row", () => ToolbarBlocks()[10].Table!.Rows.Count == 5);
            await Click("Add table column", () => ToolbarBlocks()[10].Table!.Rows.All(row => row.Cells.Count == 3));
            Check(root!.GetVisualDescendants().OfType<TextBox>()
                .Count(box => AutomationProperties.GetName(box) == "Table cell text") == 15,
                "toolbar surface re-renders every cell after the table shape changes");
            Check(ToolbarBlocks()[10].Table!.Rows.Any(row => row.Cells.Any(cell => cell.Text == "First cell")),
                "edited cell text survives table row and column additions");
            await Click("Remove table row", () => ToolbarBlocks()[10].Table!.Rows.Count == 4);
            await Click("Remove table column", () => ToolbarBlocks()[10].Table!.Rows.All(row => row.Cells.Count == 2));
            Check(ToolbarBlocks()[10].Table!.Rows.Count == 4 &&
                ToolbarBlocks()[10].Table!.Rows.All(row => row.Cells.Count == 2),
                "row and column removals leave the table four by two again");
            var cellBoxes = root!.GetVisualDescendants().OfType<TextBox>()
                .Where(box => AutomationProperties.GetName(box) == "Table cell text").ToArray();
            Check(cellBoxes.Length == 8, "toolbar surface renders every cell before cell structure edits");
            cellBoxes[0].Text = "Working cell";
            Check(ToolbarBlocks()[10].Table!.Rows is [{ Cells: [{ Text: "Working cell" }, ..] }, ..],
                "writing a cell selects it for the structural cell commands");
            await Click("Merge cell right", () => ToolbarBlocks()[10].Table!.Rows[0].Cells.Count == 1);
            Check(ToolbarBlocks()[10].Table!.Rows is [{ Cells: [{ ColumnSpan: 2, Text: var mergedCell }] }, ..] &&
                mergedCell is not null && mergedCell.StartsWith("Working cell", StringComparison.Ordinal) &&
                ToolbarBlocks()[10].Table!.Rows.Count == 4 &&
                ToolbarBlocks()[10].Table!.Rows[1].Cells.Count == 2,
                "merging a cell right spans its neighbour into it and leaves the other rows alone");
            await Click("Split cell", () => ToolbarBlocks()[10].Table!.Rows[0].Cells.Count == 2);
            Check(ToolbarBlocks()[10].Table!.Rows is [{ Cells: [{ ColumnSpan: 1 }, ..] }, ..] &&
                ToolbarBlocks()[10].Table!.Rows.Count == 4 &&
                ToolbarBlocks()[10].Table!.Rows.All(row => row.Cells.Count == 2),
                "splitting a spanning cell restores the original four by two table shape");
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
            State("New", true); State("Save", true); State("Bold", true); State("Italic", true);
            State("Underline", true); State("Strikethrough", true);
            foreach (var page in new[] { "A4", "Letter", "Portrait", "Landscape", "Paginated", "Continuous" })
                State(page, true);
            State("Align left", true);
            State("Align center", true); State("Align right", true); State("Justify", true); State("Close", true);
            var toolbarStored = (await repository.LoadAsync(toolbarId, default))!;
            Check(toolbarStored.Version == session.DurableRevision && First(toolbarStored).Runs[1].Bold &&
                First(toolbarStored).Runs[1].Italic && First(toolbarStored).Runs[1].Underline &&
                First(toolbarStored).Runs[1].StrikeThrough && First(toolbarStored).Runs[1].FontSize == 18d &&
                First(toolbarStored).Runs[1].FontFamily == "Segoe UI" &&
                First(toolbarStored).Runs[1].Foreground == "#112233" &&
                First(toolbarStored).Runs[1].Background == "#00FF00FF" &&
                First(toolbarStored).Runs[1].Link == "https://example.test/write" &&
                First(toolbarStored).Paragraph.Alignment == NotesTextAlignment.Center &&
                First(toolbarStored).Paragraph.LineSpacing == 1.5d && First(toolbarStored).Paragraph.IndentLeft == 48d &&
                First(toolbarStored).Paragraph.FirstLineIndent == -12d && First(toolbarStored).Paragraph.SpaceAfter == 6d &&
                toolbarStored.PageSetup.WidthPoints == 595d && toolbarStored.PageSetup.HeightPoints == 842d &&
                toolbarStored.PageSetup.MarginTopPoints == 48d && toolbarStored.PageSetup.MarginLeftPoints == 48d &&
                toolbarStored.PageSetup.ShowPageNumbers &&
                toolbarStored.LayoutMode == NotesLayoutMode.Continuous,
                "toolbar actual Save commits canonical revision, character and font formatting, page setup and the full paragraph format");
            var storedBlocks = toolbarStored.Sections.SelectMany(section => section.Pages)
                .SelectMany(page => page.Blocks).ToArray();
            Check(storedBlocks is { Length: 11 } &&
                storedBlocks[1].List is { Kind: NotesListKind.Bulleted,
                    Items: [{ Text: "Edited bullet", Checked: true, Level: 2 }, { Text: "New item" }] } &&
                storedBlocks[2].List is { Kind: NotesListKind.Numbered } &&
                storedBlocks[3].List is { Kind: NotesListKind.Checklist } &&
                storedBlocks[4].Kind == NotesBlockKind.Heading && storedBlocks[4].StyleId == "heading-1" &&
                storedBlocks[5].Kind == NotesBlockKind.Quote && storedBlocks[6].Kind == NotesBlockKind.Code &&
                storedBlocks[7].Kind == NotesBlockKind.Paragraph && storedBlocks[8].Kind == NotesBlockKind.Divider &&
                storedBlocks[9].Paragraph.PageBreakBefore &&
                storedBlocks[10].Kind == NotesBlockKind.Table &&
                storedBlocks[10].Table is { Rows: { Count: 4 } storedShape } &&
                storedShape.All(row => row.Cells.Count == 2),
                "toolbar actual Save commits every inserted block structure to the owning repository");
            await Click("Close", () => session.DocumentId is null);
            Empty();
            await Click("New", () => session.DocumentId is not null);
            var replaceBlock = First(session.GetDocumentSnapshot()!).Id;
            await session.SelectAsync(replaceBlock, 0, 0);
            window.KeyTextInput("Alpha beta gamma");
            Check(Text(session.GetDocumentSnapshot()!) == "Alpha beta gamma",
                "toolbar replace journey routes typing into its own owner");
            findInput.Text = "gamma";
            replaceInput.Text = "DELTA";
            Check(session.TryGetValue("FindMatchCount", out var replacementMatches) &&
                Equals(replacementMatches, 1),
                "toolbar find counts the replacement target before replacing");
            await Click("Replace all", () => session.TryGetValue("ReplaceStatus", out var replaced) &&
                Equals(replaced, "Replaced 1 match(es).") &&
                Text(session.GetDocumentSnapshot()!) == "Alpha beta DELTA");
            var replaceStatusBlock = root!.GetVisualDescendants().OfType<TextBlock>()
                .Single(block => AutomationProperties.GetName(block) == "Replace all status");
            Check(replaceStatusBlock.Text == "Replaced 1 match(es)." && session.IsDirty &&
                session.TryGetValue("FindMatchCount", out var remainingMatches) &&
                Equals(remainingMatches, 0) &&
                session.TryGetValue("FindStatus", out var remainingStatus) &&
                Equals(remainingStatus, "No matches."),
                "toolbar replace all writes through to the owner, clears its target and renders its status, actual " +
                replaceStatusBlock.Text);
            await Click("Save", () => !session.IsDirty);
            await Click("Close", () => session.DocumentId is null);
            Empty();
            await Click("New", () => session.DocumentId is not null);
            var structureBlock = First(session.GetDocumentSnapshot()!).Id;
            await session.SelectAsync(structureBlock, 0, 0);
            window.KeyTextInput("Kept paragraph");
            Check(ToolbarBlocks() is { Count: 1 } alone && alone[0].Kind == NotesBlockKind.Paragraph &&
                alone[0].Runs is [{ Text: "Kept paragraph" }],
                "toolbar block journey begins with its single typed paragraph");
            await Click("Heading", () => ToolbarBlocks() is { Count: 2 } inserted &&
                inserted[0].Kind == NotesBlockKind.Paragraph && inserted[1].Kind == NotesBlockKind.Heading &&
                inserted[1].StyleId == "heading-1");
            State("Delete block", true);
            await Click("Move block up", () => ToolbarBlocks() is { Count: 2 } movedUp &&
                movedUp[0].Kind == NotesBlockKind.Heading && movedUp[0].StyleId == "heading-1" &&
                movedUp[1].Kind == NotesBlockKind.Paragraph && movedUp[1].Runs is [{ Text: "Kept paragraph" }]);
            await Click("Move block up", () => ToolbarBlocks() is { Count: 2 } stillUp &&
                stillUp[0].Kind == NotesBlockKind.Heading && stillUp[1].Kind == NotesBlockKind.Paragraph);
            Check(ToolbarBlocks() is { Count: 2 } refusedMove &&
                refusedMove[0].Kind == NotesBlockKind.Heading &&
                refusedMove[1].Runs is [{ Text: "Kept paragraph" }],
                "moving the first block up is refused without reordering or losing content");
            await Click("Move block down", () => ToolbarBlocks() is { Count: 2 } movedDown &&
                movedDown[0].Kind == NotesBlockKind.Paragraph && movedDown[0].Runs is [{ Text: "Kept paragraph" }] &&
                movedDown[1].Kind == NotesBlockKind.Heading);
            await Click("Delete block", () => ToolbarBlocks() is { Count: 1 } remaining &&
                remaining[0].Kind == NotesBlockKind.Paragraph && remaining[0].Runs is [{ Text: "Kept paragraph" }]);
            State("Delete block", false);
            Check(session.TryGetValue("CanDeleteBlock", out var onlyBlock) && Equals(onlyBlock, false) &&
                ToolbarBlocks() is { Count: 1 } lastOne && lastOne[0].Runs is [{ Text: "Kept paragraph" }],
                "deleting the selected block leaves the document's other block intact and hides further deletes");
            Check((await session.DeleteBlockAsync()).Succeeded &&
                ToolbarBlocks() is { Count: 1 } declined && declined[0].Runs is [{ Text: "Kept paragraph" }],
                "the editor refuses to delete the document's last block and leaves it in place");
            await session.SelectAsync(ToolbarBlocks()[0].Id, 0, 0);
            Check(session.TryGetValue("Styles", out var paragraphStyles) &&
                paragraphStyles is WriteStyleRow[] { Length: 5 } namedStyles &&
                namedStyles.Any(row => row.Id == "heading-1" && row.Name == "Heading 1") &&
                namedStyles.All(row => row.Label.StartsWith("Set ", StringComparison.Ordinal)),
                "the paragraph style catalogue reads back every named style the document itself declares");
            await Click("Set heading 1", () => ToolbarBlocks() is
                [{ Kind: NotesBlockKind.Heading, StyleId: "heading-1" }]);
            await Click("Set quote", () => ToolbarBlocks() is
                [{ Kind: NotesBlockKind.Quote, StyleId: "quote" }]);
            Check(session.TryGetValue("SelectedStyle", out var quotedStyle) && Equals(quotedStyle, "Quote") &&
                ToolbarBlocks()[0].Runs is [{ Text: "Kept paragraph" }],
                "applying a style changes the selected block's kind without touching its text");
            await Click("Set normal", () => ToolbarBlocks() is
                [{ Kind: NotesBlockKind.Paragraph, StyleId: "normal", Runs: [{ Text: "Kept paragraph" }] }]);
            Check(session.TryGetValue("SelectedStyle", out var normalStyle) && Equals(normalStyle, "Normal"),
                "the paragraph style readout tracks the selected block's authored style");
            Check(session.TryGetValue("CanInsertImage", out var canInsertImage) && Equals(canInsertImage, true) &&
                session.TryGetValue("CanEditMedia", out var canEditNoMedia) && Equals(canEditNoMedia, false),
                "the image controls enable insertion on an editable document and disable editing until an image is selected");
            var mediaNumericInputs = new[]
            {
                "Image width in points, from 24 to 10000", "Image height in points, from 24 to 10000",
                "Image rotation in degrees", "Crop fraction from the left edge, 0 to 0.95",
                "Crop fraction from the top edge, 0 to 0.95", "Crop fraction from the right edge, 0 to 0.95",
                "Crop fraction from the bottom edge, 0 to 0.95"
            }.Select(name => root!.GetVisualDescendants().OfType<NumericUpDown>()
                .Single(box => AutomationProperties.GetName(box) == name)).ToArray();
            await Click("Insert image", () => ToolbarBlocks() is [.., { Kind: NotesBlockKind.Image, Media: not null } last]
                && last.Media!.OriginalName == "write-scene-image.png");
            var mediaBlock = ToolbarBlocks()[^1];
            var media = mediaBlock.Media!;
            Check(media.AttachmentId != Guid.Empty && !string.IsNullOrWhiteSpace(media.StoredPath) &&
                    media.Sha256.Length == 64 && media.SizeBytes == new FileInfo(fixtureImagePath).Length,
                "actual Insert image stores the picked bytes in the canonical attachment store and records its identity, digest and size");
            Check(media.MediaType == "image/png" && media.Wrapping == "Inline" && media.Width == 400 && media.Height == 300,
                "the inserted image adopts the chooser's declared media type and the editor's authored default size and inline wrapping");
            await session.SelectAsync(mediaBlock.Id, 0, 0);
            Check(session.TryGetValue("CanEditMedia", out var canEditMedia) && Equals(canEditMedia, true) &&
                session.TryGetValue("MediaAlt", out var seededAlt) && Equals(seededAlt, "write-scene-image") &&
                session.TryGetValue("MediaWidth", out var insertedWidth) && Equals(insertedWidth, 400d) &&
                session.TryGetValue("MediaRotation", out var insertedRotation) && Equals(insertedRotation, 0d) &&
                mediaNumericInputs.All(input => input.IsEnabled),
                "the media property bindings report the inserted image's own width, height and rotation once selected");
            var altInput = root!.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Alternative text for the selected image");
            var captionInput = root!.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Caption for the selected image");
            var wrappingInput = root!.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Wrapping mode for the selected image");
            altInput.Text = "Rendered scene alt text";
            captionInput.Text = "Rendered scene caption";
            wrappingInput.Text = "Floating";
            var editedMedia = ToolbarBlocks()[^1].Media!;
            Check(editedMedia.AltText == "Rendered scene alt text" &&
                editedMedia.Caption == "Rendered scene caption" && editedMedia.Wrapping == "Floating",
                "the two-way alt, caption and wrapping inputs write the selected image's own properties");
            mediaNumericInputs[0].Value = 240m;
            mediaNumericInputs[1].Value = 180m;
            Check(ToolbarBlocks()[^1].Media is { Width: 240d, Height: 180d },
                "the two-way width and height inputs resize the selected image to the requested pixel size");
            mediaNumericInputs[2].Value = 45m;
            Check(Math.Abs(ToolbarBlocks()[^1].Media!.Rotation - 45d) < .0001,
                "the two-way rotation input writes the selected image's absolute angle");
            mediaNumericInputs[3].Value = .1m;
            mediaNumericInputs[4].Value = .2m;
            mediaNumericInputs[5].Value = .3m;
            Check(ToolbarBlocks()[^1].Media is { CropLeft: .1d, CropTop: .2d, CropRight: .3d, CropBottom: 0d },
                "the four crop inputs crop the selected image by the requested fractions");
            await session.SelectAsync(ToolbarBlocks()[0].Id, 0, 0);
            Check(session.TryGetValue("CanEditMedia", out var canEditDeselected) && Equals(canEditDeselected, false),
                "the media controls disable again once the selection leaves the image");
            await session.SelectAsync(mediaBlock.Id, 0, 0);
            await Click("Insert equation", () => ToolbarBlocks() is [.., { Kind: NotesBlockKind.Equation, Equation: not null } last]);
            var equationBlock = ToolbarBlocks()[^1];
            await session.SelectAsync(equationBlock.Id, 0, 0);
            Check(session.TryGetValue("CanEditEquation", out var canEditEquation) && Equals(canEditEquation, true) &&
                session.TryGetValue("EquationSource", out var seededEquationSource) &&
                Equals(seededEquationSource, "x^2 + y^2 = z^2") &&
                session.TryGetValue("EquationAlternative", out var seededEquationAlternative) &&
                Equals(seededEquationAlternative, "x squared plus y squared equals z squared"),
                "the equation property bindings read the inserted equation's own authored source and alternative");
            var equationSourceInput = root!.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Equation source");
            var equationAlternativeInput = root!.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Equation accessible alternative");
            equationSourceInput.Text = "x^2 + y^2 = r^2";
            equationAlternativeInput.Text = "a squared plus b squared";
            Check(ToolbarBlocks()[^1].Equation is { Source: "x^2 + y^2 = r^2",
                AccessibleAlternative: "a squared plus b squared" } writtenEquation,
                "the two-way source and alternative inputs write the selected equation's own fields");
            await Click("Save", () => !session.IsDirty);
            Check(ToolbarBlocks() is { Count: 3 } savedMediaBlocks && savedMediaBlocks[1].Media is
                { Sha256.Length: 64, AltText: "Rendered scene alt text", Caption: "Rendered scene caption",
                    Wrapping: "Floating", Width: 240d, Height: 180d, CropLeft: .1d, CropTop: .2d,
                    CropRight: .3d } savedMedia && Math.Abs(savedMedia.Rotation - 45d) < .0001
                && savedMediaBlocks[2].Equation is { Source: "x^2 + y^2 = r^2",
                    AccessibleAlternative: "a squared plus b squared" },
                "actual Save commits the inserted image, every one of its edited properties and the typed equation to the owning repository");
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
        scene.FocusElement(scene.Root!);
        window.KeyTextInput("Alpha beta gamma");
        Check(Text(session.GetDocumentSnapshot()!) == "Alpha beta gamma", "actual routed text input mutates owner exactly once");
        await session.SelectAsync(block, 6, 10);
        var changes = 0;
        session.Editor!.Changed += Count;
        void Count(object? sender, EventArgs args) => ++changes;
        var bold = root!.GetVisualDescendants().OfType<Button>().Single(button => button.Content is string label && label == "B");
        bold.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var runs = First(session.GetDocumentSnapshot()!).Runs;
        Check(changes == 1 && runs.Count == 3 && runs[1].Text == "beta" && runs[1].Bold && !runs[0].Bold && !runs[2].Bold,
            "actual CUI bold button dispatches one owner mutation despite duplicate wiring attempt");
        using (var frame = window.CaptureRenderedFrame())
        {
            Check(frame is not null, "actual structured edit renders a captured frame");
            frame!.Save(Path.Combine(paths.DataDirectory, "write-retained-scene.png"));
        }
        var desktopDocument = JsonSerializer.Serialize(session.GetDocumentSnapshot());
        window.Width = 390;
        using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(paths.DataDirectory, "write-mobile.png"));
        session.TryGetValue("ShowSidebar", out var narrowSidebar);
        Check(narrowSidebar is false && desktopDocument == JsonSerializer.Serialize(session.GetDocumentSnapshot()),
            "narrow Write viewport collapses Sections without document or selection data changes");
        await session.DispatchAsync("ToggleSidebar", null);
        using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(paths.DataDirectory, "write-mobile-drawer.png"));
        session.TryGetValue("SidebarColumnSpan", out var drawerSpan);
        Check(Equals(drawerSpan, 2) && desktopDocument == JsonSerializer.Serialize(session.GetDocumentSnapshot()),
            "mobile Sections drawer overlays the canvas without changing canonical content");
        window.Width = 1100;
        using (var frame = window.CaptureRenderedFrame()) { }
        scene.FocusElement(scene.Root!);
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

// Stages a real file exactly as a platform file chooser would, so the scene
// journey drives the same admission path the production browser bridge drives.
sealed class SceneImageSource(string sourcePath) : IWriteBrowserImageSource
{
    public Task<WritePickedImage?> PickImageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The fixture image does not exist.", sourcePath);
        var directory = Path.Combine(Path.GetTempPath(), "nine-to-one-write-image-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var staged = new WriteStagedPackage(Path.Combine(directory, Path.GetFileName(sourcePath)),
            Path.GetFileName(sourcePath), () =>
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
                return ValueTask.CompletedTask;
            });
        File.Copy(sourcePath, staged.Path);
        return Task.FromResult<WritePickedImage?>(new WritePickedImage("write-scene-image.png", "image/png", staged));
    }
}
