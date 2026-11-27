using CakeOS.Cui.Themes;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using Avalonia.Headless;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Development;
using HavenOS.Apps.Dev;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

// Actual Page/Dev/coordinator/runtime, with the established synthetic Task issuer/file provider.
// The optional reader is composed from the EXISTING genuine Linux Home/Files/kernel fixture.
// Search-observation input is privately enrolled to isolate final Open publication. This does
// not claim original Home search admission, native frame readiness or installed acceptance.
public sealed partial class DeveloperTaskWorkspaceServiceTests
{
    [AvaloniaFact]
    public async Task Atlas_workbench_panel_switches_preserve_the_actual_source_draft_context_and_outcomes()
    {
        var token = TestContext.Current.CancellationToken;
        var f = await Fixture.CreateAsync(); DeveloperProjectWorkbenchPage? page = null;
        try
        {
            page = await CreateWorkbenchPresentationControlAsync(f, token);
            Assert.True(page.TrySetValue("DraftText", "retained source draft\nwith an unsaved change"));
            var editor = Assert.IsType<DeveloperEditorSnapshot>(SearchPublicationField("_editor").GetValue(page));
            var task = f.Current; var reads = f.Tools.ReadCalls; var metadataReads = f.Store.GetCalls;
            Assert.True(page.TryGetValue("SourceTabs", out var tabs));
            var originalTabs = Assert.IsAssignableFrom<object[]>(tabs).ToArray();
            foreach (var command in new[] { "files", "explorer-search", "explorer-create", "explorer-files", "preview",
                "mission", "project", "problems", "terminal", "tests", "tasks", "hide-output", "output", "find", "line-endings", "review", "code" })
                await page.DispatchAsync("dev.layout." + command, null, token);
            Assert.True(page.TryGetValue("DraftText", out var draft)); Assert.Equal(editor.DraftText, draft);
            Assert.Same(editor, SearchPublicationField("_editor").GetValue(page)); Assert.Equal(task, f.Current);
            Assert.True(page.TryGetValue("SourceTabs", out var after));
            var afterTabs = Assert.IsAssignableFrom<object[]>(after);
            Assert.Equal(originalTabs.Select(value => ((ICuiBindingContext)value).TryGetValue("SelectionKey", out var key) ? key : null),
                afterTabs.Select(value => ((ICuiBindingContext)value).TryGetValue("SelectionKey", out var key) ? key : null));
            Assert.Equal(reads, f.Tools.ReadCalls); Assert.Equal(metadataReads, f.Store.GetCalls);
            Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(0, f.Tools.ProcessCalls);
            Assert.True(page.HasUnsavedWork);
            page.Deactivate();
            Assert.False(page.TryGetValue("LayoutProjectLocation", out var withdrawn)); Assert.Null(withdrawn);
            Assert.False(page.IsActionAvailable("dev.layout.code")); Assert.Same(editor, SearchPublicationField("_editor").GetValue(page));
            var close = page.CloseAndDrainAsync(); await close;
            Assert.Same(close, page.CloseAndDrainAsync()); Assert.Same(editor, SearchPublicationField("_editor").GetValue(page));
        }
        finally { try { if (page is not null) await page.CloseAndDrainAsync(); } finally { await f.Dev.CloseAndDrainAsync(); } }
    }

    [AvaloniaTheory]
    [InlineData("Glow")]
    [InlineData("Professional")]
    public async Task Atlas_compiled_workbench_renderer_preserves_source_at_720_and_1440(string themeName)
    {
        // Isolated renderer evidence: the same compiled document, real page bindings, actual Dev
        // source session and the existing synthetic tool fixture. No Ready certificate is minted;
        // the page's production host still has SearchPublicationNoReadiness. Native activation is a
        // separate gate. The additional loader below belongs only to this rendering test.
        var token = TestContext.Current.CancellationToken;
        var f = await Fixture.CreateAsync(); DeveloperProjectWorkbenchPage? page = null;
        CuiControlLoader? loader = null; Window? window = null;
        using var theme = new CuiThemePreferenceContext(new WorkbenchPresentationTheme(themeName));
        try
        {
            f.Tools.TextValue = "namespace ExistingProject;\n\npublic static class Example\n{\n    public static string Name => \"Existing source\";\n}\n";
            page = await CreateWorkbenchPresentationControlAsync(f, token);
            var draft = f.Tools.TextValue.Replace("Existing source", "Unsaved draft", StringComparison.Ordinal);
            Assert.True(page.TrySetValue("DraftText", draft));
            var originalEditor = SearchPublicationField("_editor").GetValue(page);
            var reads = f.Tools.ReadCalls; var metadataReads = f.Store.GetCalls;
            var typefaces = new List<(string Requested, string Resolved, double I, double M)>();
            loader = new CuiControlLoader(Assert.IsType<CuiControlRegistry>(SearchPublicationField("_controls").GetValue(page)));
            loader.SetThemePreferenceContext(theme); loader.SetSurface("Dev");
            loader.SetBindingContext(page); loader.SetActionDispatcher(page);
            var loaded = loader.TryLoad(page.Document);
            Assert.DoesNotContain(loaded.Diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);
            var root = Assert.IsAssignableFrom<Control>(loaded.Root); loader.WireBindings(root);
            window = new Window { Width = 1440, Height = 900, Content = root }; window.Show();
            foreach (var size in new[] { new Size(1440, 900), new Size(720, 650) })
            {
                // Drive the owning page's normal size callback as well as the isolated viewport.
                page.Width = size.Width; page.Height = size.Height; page.Measure(size); page.Arrange(new Rect(size));
                window.Width = size.Width; window.Height = size.Height;
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var editor = WorkbenchControl<TextBox>(root, "dev-code-editor");
                Assert.Equal(draft, editor.Text); Assert.True(editor.IsEffectivelyVisible);
                var sourcePoint = editor.TranslatePoint(default, root); Assert.NotNull(sourcePoint);
                Assert.True(editor.Bounds.Width > 250);
                Assert.InRange(sourcePoint.Value.X, -1, root.Bounds.Width);
                Assert.True(sourcePoint.Value.X + editor.Bounds.Width <= root.Bounds.Width + 1);
                // This selected document is C#: it cannot use the CUI preview. Explicit Preview
                // remains available, and the selected .cui case is covered by the real preview test.
                Assert.False(WorkbenchControl<Control>(root, "dev-cui-designer-section").IsEffectivelyVisible);
                Assert.Equal(size.Width >= 800, WorkbenchControl<Control>(root, "dev-explorer-section").IsEffectivelyVisible);
                AssertWorkbenchControlInsideViewport(root, editor);
                AssertWorkbenchEditActionsInsideViewport(root);
                var typeface = new Typeface(editor.FontFamily, editor.FontStyle, editor.FontWeight, editor.FontStretch);
                var glyphs = typeface.GlyphTypeface;
                Assert.True(glyphs.CharacterToGlyphMap.TryGetGlyph('i', out var iGlyph), glyphs.FamilyName);
                Assert.True(glyphs.CharacterToGlyphMap.TryGetGlyph('m', out var mGlyph), glyphs.FamilyName);
                Assert.True(glyphs.TryGetHorizontalGlyphAdvance(iGlyph, out var iAdvance), glyphs.FamilyName);
                Assert.True(glyphs.TryGetHorizontalGlyphAdvance(mGlyph, out var mAdvance), glyphs.FamilyName);
                Assert.True(glyphs.Metrics.DesignEmHeight > 0);
                var emScale = editor.FontSize / glyphs.Metrics.DesignEmHeight;
                typefaces.Add((editor.FontFamily.Name, glyphs.FamilyName, iAdvance * emScale, mAdvance * emScale));
                var output = WorkbenchControl<Control>(root, "dev-output-content");
                Assert.False(output.IsEffectivelyVisible);
                await page.DispatchAsync("dev.layout.output", null, token);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.True(output.IsEffectivelyVisible);
                Assert.InRange(output.Bounds.Height, 1, size.Height == 650 ? 151 : 211);
                var outputPoint = output.TranslatePoint(default, root); Assert.NotNull(outputPoint);
                Assert.True(outputPoint.Value.Y + output.Bounds.Height <= root.Bounds.Height + 1);
                AssertWorkbenchEditActionsInsideViewport(root);
                await page.DispatchAsync("dev.layout.review", null, token);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.True(WorkbenchControl<Control>(root, "dev-editor-details").IsEffectivelyVisible);
                AssertWorkbenchControlInsideViewport(root, editor);
                AssertWorkbenchEditActionsInsideViewport(root);
                Assert.Same(editor, WorkbenchControl<TextBox>(root, "dev-code-editor"));
                Assert.Same(originalEditor, SearchPublicationField("_editor").GetValue(page));
                Assert.Equal(draft, editor.Text);
                await page.DispatchAsync("dev.layout.review", null, token);
                await page.DispatchAsync("dev.layout.hide-output", null, token);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.False(output.IsEffectivelyVisible);
                var directory = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR") ?? Environment.GetEnvironmentVariable("CUI_EVIDENCE_DIRECTORY");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    frame.Save(Path.Combine(directory, $"build-code-component-{themeName}-{size.Width:0}x{size.Height:0}.png"));
                }
            }
            await page.DispatchAsync("dev.layout.preview", null, token);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.True(WorkbenchControl<Control>(root, "dev-cui-designer-section").IsEffectivelyVisible);
            Assert.True(page.TryGetValue("DraftText", out var retained)); Assert.Equal(draft, retained);
            Assert.Equal(reads, f.Tools.ReadCalls); Assert.Equal(metadataReads, f.Store.GetCalls);
            Assert.Equal(0, f.Tools.ProcessCalls); Assert.Equal(0, f.Tools.WriteCalls);
            await loader.WhenActionsIdleAsync();
            foreach (var observed in typefaces)
                Assert.True(observed.I > 0 && Math.Abs(observed.I - observed.M) < 0.000001,
                    $"Code editor requested '{observed.Requested}', resolved '{observed.Resolved}': i={observed.I:0.######}, m={observed.M:0.######} logical-pixel advances. The authored monospace contract requires equal advances.");
        }
        finally
        {
            loader?.Dispose(); if (window is not null) { window.Content = null; window.Close(); }
            try { if (page is not null) await page.CloseAndDrainAsync(); } finally { await f.Dev.CloseAndDrainAsync(); }
        }
    }

    [AvaloniaFact]
    public async Task Atlas_workbench_output_opens_for_the_original_command_and_cannot_hide_before_settlement()
    {
        var token = TestContext.Current.CancellationToken;
        var f = await Fixture.CreateAsync(); DeveloperProjectWorkbenchPage? page = null; Task? pending = null;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            page = await CreateWorkbenchPresentationControlAsync(f, token);
            Assert.True(page.TrySetValue("DraftText", "source retained while the exact command runs"));
            var editor = SearchPublicationField("_editor").GetValue(page);
            Assert.True(page.TryGetValue("LayoutOutputVisible", out var visible)); Assert.False(Assert.IsType<bool>(visible));
            Assert.True(page.TrySetValue("Command", "echo original-owned-command"));
            f.Owner.BeforeBody = release.Task; f.Owner.OnPrepare = () => entered.TrySetResult();
            pending = page.DispatchAsync("dev.run-command", null, token).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.False(pending.IsCompleted); Assert.Equal(0, f.Tools.ProcessCalls);
            Assert.True(page.TryGetValue("LayoutOutputVisible", out visible)); Assert.True(Assert.IsType<bool>(visible));
            Assert.True(page.TryGetValue("LayoutCanHideOutput", out var canHide)); Assert.False(Assert.IsType<bool>(canHide));
            await page.DispatchAsync("dev.layout.hide-output", null, token);
            Assert.True(page.TryGetValue("LayoutOutputVisible", out visible)); Assert.True(Assert.IsType<bool>(visible));
            Assert.Same(editor, SearchPublicationField("_editor").GetValue(page));
            release.TrySetResult(); await pending;
            Assert.Equal(1, f.Tools.ProcessCalls); Assert.Equal(0, f.Tools.WriteCalls);
            Assert.True(page.TryGetValue("ProcessOutput", out var output)); Assert.Contains("observed", Assert.IsType<string>(output));
            Assert.True(page.TryGetValue("LayoutOutputVisible", out visible)); Assert.True(Assert.IsType<bool>(visible));
            Assert.True(page.TryGetValue("LayoutCanHideOutput", out canHide)); Assert.True(Assert.IsType<bool>(canHide));
            await page.DispatchAsync("dev.layout.hide-output", null, token);
            Assert.True(page.TryGetValue("LayoutOutputVisible", out visible)); Assert.False(Assert.IsType<bool>(visible));
            Assert.True(page.TryGetValue("DraftText", out var retained)); Assert.Equal("source retained while the exact command runs", retained);
        }
        finally
        {
            release.TrySetResult();
            try { if (pending is not null) await pending; }
            finally { try { if (page is not null) await page.CloseAndDrainAsync(); } finally { await f.Dev.CloseAndDrainAsync(); } }
        }
    }

    private static void AssertWorkbenchEditActionsInsideViewport(Control root)
    {
        foreach (var id in new[] { "dev-preview-edit", "dev-apply-edit", "dev-save-reviewed" })
            AssertWorkbenchControlInsideViewport(root, WorkbenchControl<Button>(root, id));
    }

    private static void AssertWorkbenchControlInsideViewport(Control root, Control control)
    {
        Assert.True(control.IsEffectivelyVisible, control.Name);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0, control.Name);
        var point = control.TranslatePoint(default, root); Assert.NotNull(point);
        Assert.InRange(point.Value.X, -1, root.Bounds.Width);
        Assert.InRange(point.Value.Y, -1, root.Bounds.Height);
        Assert.True(point.Value.X + control.Bounds.Width <= root.Bounds.Width + 1, control.Name);
        Assert.True(point.Value.Y + control.Bounds.Height <= root.Bounds.Height + 1, control.Name);
    }

    private static async Task<DeveloperProjectWorkbenchPage> CreateWorkbenchPresentationControlAsync(Fixture f, CancellationToken token)
    {
        var resolved = (await f.Dev.ResolveAsync(f.Reference, token)).Value!;
        var page = new DeveloperProjectWorkbenchPage(f.Dev, f.Tasks, resolved, f.Current.TaskId, f.Current.ExecutionId,
            f.Current.ContextId, new SearchPublicationNoReadiness(), (_, path, cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested(); Assert.Equal(f.Document.RelativePath, path);
                return Task.FromResult(DeveloperOperationResult<DeveloperCodeDocument>.Success(f.Document));
            }, () => { }, token);
        try
        {
            SearchPublicationField("_active").SetValue(page, true); // Component generation only, matching this file's existing enrollment seam.
            Assert.True(page.TrySetValue("SourcePath", f.Document.RelativePath));
            await page.DispatchAsync("dev.open-source", null, token);
            Assert.True(page.TryGetValue("DraftText", out var actual)); Assert.Equal(f.Tools.TextValue, actual);
            return page;
        }
        catch { await page.CloseAndDrainAsync(); throw; }
    }

    private static T WorkbenchControl<T>(Control root, string id) where T : Control => Assert.Single(
        global::Haven.Desktop.Tests.AuthoredCuiControls.Enumerate(root).OfType<T>(), control => control.Name == id || AutomationProperties.GetAutomationId(control) == id);

    private sealed class WorkbenchPresentationTheme(string name) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; } = new(name, 1, "build-renderer-component", true,
            CuiAccessibilitySettings.Default, CuiAppearance.Dark);
        public event EventHandler? Changed { add { } remove { } }
    }

    [AvaloniaFact]
    public Task Eight_late_query_refusals_do_not_leak_owner_capacity_or_change_existing_draft_selection() =>
        RefusedSearchPublication(false);

    [AvaloniaFact]
    public Task Eight_late_hash_refusals_do_not_leak_owner_capacity_or_change_existing_draft_selection() =>
        RefusedSearchPublication(true);

    private static async Task RefusedSearchPublication(bool changeHash)
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine optional Home/Files reader fixture requires Linux; other platforms need their owning graph.");
        var f = await Fixture.CreateAsync();
        var reader = await ActualOptionalReaderFixture();
        DeveloperProjectWorkbenchPage? page = null; TaskCompletionSource? release = null;
        var sawExpectedRefusal = false;
        try
        {
            page = await SearchPublicationPage(f, reader.Owner);
            page.TrySetValue("SourcePath", "src/preexisting.cs"); await page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken);
            page.TrySetValue("DraftText", "existing unsaved pinned draft");
            var review = (DeveloperSourceReviewSession)SearchPublicationField("_review").GetValue(page)!;
            var before = (DeveloperEditorSnapshot)SearchPublicationField("_editor").GetValue(page)!;
            var selected = SearchPublicationField("_selectedSourceTab").GetValue(page);
            var oldTabs = TabLayout(page);
            for (var i = 0; i < 8; i++)
            {
                f.Tools.TextValue = "original source";
                var key = EnrollObservedSearchInput(page, f, "src/refused-" + i + ".cs", f.Tools.TextValue);
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                release = new(TaskCreationOptions.RunContinuationsAsynchronously);
                f.Tools.BeforeFileRead = release.Task; f.Tools.ReadEntered = () => entered.TrySetResult();
                var operation = page.DispatchAsync("dev.open-search-result", key, OriginalTestBodyToken).AsTask();
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), OriginalTestBodyToken);
                    Assert.False(operation.IsCompleted); Assert.Single(OpenOwnerSnapshots(review));
                    if (changeHash) f.Tools.TextValue = "actual outside source change";
                    else page.TrySetValue("SearchQuery", "different query " + i);
                }
                finally { release.TrySetResult(); }
                await Assert.ThrowsAnyAsync<Exception>(() => operation);
                sawExpectedRefusal = true; f.Tools.BeforeFileRead = null; f.Tools.ReadEntered = null;
                Assert.Same(before, review.GetSnapshot(before.EditorId));
                Assert.Single(OpenOwnerSnapshots(review)); Assert.Equal(0, PendingOpenCount(review));
                Assert.Equal(selected, SearchPublicationField("_selectedSourceTab").GetValue(page)); Assert.Equal(oldTabs, TabLayout(page));
                Assert.Same(before, SearchPublicationField("_editor").GetValue(page));
            }
            f.Tools.TextValue = "original source";
            var ninth = EnrollObservedSearchInput(page, f, "src/ninth-valid.cs", f.Tools.TextValue);
            await page.DispatchAsync("dev.open-search-result", ninth, OriginalTestBodyToken);
            Assert.Equal(2, OpenOwnerSnapshots(review).Count); Assert.Equal(2, TabLayout(page).Length);
            Assert.Equal(0, PendingOpenCount(review)); Assert.Same(before, review.GetSnapshot(before.EditorId));
            Assert.Equal("src/ninth-valid.cs", ((DeveloperEditorSnapshot)SearchPublicationField("_editor").GetValue(page)!).Document.RelativePath);
            Assert.Equal(10, f.Tools.ReadCalls); Assert.Equal(0, f.Tools.WriteCalls);
        }
        finally
        {
            release?.TrySetResult(); f.Tools.BeforeFileRead = null; f.Tools.ReadEntered = null;
            try
            {
                if (page is not null)
                {
                    var close = page.CloseAndDrainAsync();
                    if (sawExpectedRefusal) await Assert.ThrowsAnyAsync<Exception>(() => close); // failed originals remain evidence
                    else await close;
                }
            }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Seven_pinned_and_one_clean_preview_replace_at_limit_and_dirty_existing_tab_refocuses()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine optional Home/Files reader fixture requires Linux; other platforms need their owning graph.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture();
        DeveloperProjectWorkbenchPage? page = null;
        try
        {
            page = await SearchPublicationPage(f, reader.Owner);
            for (var i = 0; i < 8; i++)
            {
                page.TrySetValue("SourcePath", "src/slot-" + i + ".cs");
                await page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken);
                if (i == 0) page.TrySetValue("DraftText", "dirty pinned source remains intact");
                else if (i < 7) await page.DispatchAsync("dev.toggle-source-tab-pin", null, OriginalTestBodyToken);
            }
            var review = (DeveloperSourceReviewSession)SearchPublicationField("_review").GetValue(page)!;
            var layout = TabLayout(page); Assert.Equal(8, layout.Length);
            var first = OpenOwnerSnapshots(review).Single(value => value.Document.RelativePath == "src/slot-0.cs");
            var oldPreview = (DeveloperEditorSnapshot)SearchPublicationField("_editor").GetValue(page)!;
            page.TrySetValue("SourcePath", "src/replacement.cs");
            await page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken);
            Assert.Equal(8, TabLayout(page).Length); Assert.Equal(8, OpenOwnerSnapshots(review).Count);
            Assert.Equal(layout.Take(7), TabLayout(page).Take(7)); Assert.Same(first, review.GetSnapshot(first.EditorId));
            Assert.Throws<KeyNotFoundException>(() => review.GetSnapshot(oldPreview.EditorId));
            Assert.Equal("src/replacement.cs", ((DeveloperEditorSnapshot)SearchPublicationField("_editor").GetValue(page)!).Document.RelativePath);
            await page.DispatchAsync("dev.select-source-tab", first.EditorId.ToString("D"), OriginalTestBodyToken);
            var focused = (DeveloperEditorSnapshot)SearchPublicationField("_editor").GetValue(page)!;
            Assert.Equal(first.EditorId, focused.EditorId); Assert.Equal(first.DraftText, focused.DraftText);
            Assert.Equal(first.DraftRevision, focused.DraftRevision); Assert.Equal(8, OpenOwnerSnapshots(review).Count);
            Assert.Equal(8, TabLayout(page).Length); Assert.Equal(0, PendingOpenCount(review));
            Assert.Equal(10, f.Tools.ReadCalls); Assert.Equal(0, f.Tools.WriteCalls);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Native_tab_pin_and_edit_commands_keep_explicit_preview_state_and_default_group()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine optional Home/Files reader fixture requires Linux; other platforms need their owning graph.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture();
        DeveloperProjectWorkbenchPage? page = null;
        try
        {
            page = await SearchPublicationPage(f, reader.Owner);
            page.TrySetValue("SourcePath", "src/preview.cs");
            await page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken);
            var tab = Assert.Single(NativeSourceTabs(page));
            Assert.True(NativeTabFlag(tab, "IsPreview")); Assert.False(NativeTabFlag(tab, "IsPinned"));
            Assert.Equal("default", NativeTabGroup(tab));

            await page.DispatchAsync("dev.toggle-source-tab-pin", null, OriginalTestBodyToken);
            Assert.True(NativeTabFlag(tab, "IsPinned")); Assert.False(NativeTabFlag(tab, "IsPreview"));
            await page.DispatchAsync("dev.toggle-source-tab-pin", null, OriginalTestBodyToken);
            Assert.False(NativeTabFlag(tab, "IsPinned")); Assert.True(NativeTabFlag(tab, "IsPreview"));
            Assert.True(page.TrySetValue("DraftText", "keep the current unsaved source"));
            Assert.True(NativeTabFlag(tab, "IsPinned")); Assert.False(NativeTabFlag(tab, "IsPreview"));
            Assert.True(page.TryGetValue("SourceTabs", out var bindings));
            var renderedTab = Assert.Single(Assert.IsAssignableFrom<System.Collections.IEnumerable>(bindings).Cast<object>());
            Assert.Contains("(pinned)", Assert.IsType<string>(renderedTab.GetType().GetProperty("Label")!.GetValue(renderedTab)));
            Assert.Equal(0, f.Tools.WriteCalls);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Restored_regular_tabs_and_other_group_previews_survive_active_group_preview_replacement()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine optional Home/Files reader fixture requires Linux; other platforms need their owning graph.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture();
        DeveloperProjectWorkbenchPage? page = null;
        try
        {
            page = await SearchPublicationPage(f, reader.Owner);
            page.TrySetValue("SourcePath", "src/regular-left.cs");
            await page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken);
            var left = Assert.Single(NativeSourceTabs(page));
            // Enrol only saved presentation metadata to isolate the post-restore replacement path.
            // Source observations and every subsequent action remain with the same actual page/owner.
            left.GetType().GetField("GroupId")!.SetValue(left, "left");
            left.GetType().GetField("IsPreview")!.SetValue(left, false);
            page.TrySetValue("SourcePath", "src/preview-right.cs");
            await page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken);
            var right = Assert.Single(NativeSourceTabs(page), tab => NativeTabPath(tab) == "src/preview-right.cs");
            right.GetType().GetField("GroupId")!.SetValue(right, "right");
            Assert.True(NativeTabFlag(right, "IsPreview"));

            await page.DispatchAsync("dev.select-source-tab", left.GetType().GetProperty("Id")!.GetValue(left)!.ToString(), OriginalTestBodyToken);
            page.TrySetValue("SourcePath", "src/preview-left.cs");
            await page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken);
            Assert.Equal(3, NativeSourceTabs(page).Length);
            Assert.Contains(left, NativeSourceTabs(page)); Assert.Contains(right, NativeSourceTabs(page));
            var preview = Assert.Single(NativeSourceTabs(page), tab => NativeTabPath(tab) == "src/preview-left.cs");
            Assert.Equal("left", NativeTabGroup(preview)); Assert.True(NativeTabFlag(preview, "IsPreview"));

            page.TrySetValue("SourcePath", "src/replacement-left.cs");
            await page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken);
            var final = NativeSourceTabs(page);
            Assert.Equal(3, final.Length); Assert.Contains(left, final); Assert.Contains(right, final);
            Assert.DoesNotContain(preview, final);
            var replacement = Assert.Single(final, tab => NativeTabPath(tab) == "src/replacement-left.cs");
            Assert.Equal("left", NativeTabGroup(replacement)); Assert.True(NativeTabFlag(replacement, "IsPreview"));
            Assert.False(NativeTabFlag(left, "IsPreview")); Assert.False(NativeTabFlag(left, "IsPinned"));
            Assert.Equal("right", NativeTabGroup(right));
            Assert.True(page.TryGetValue("SourceTabs", out var bindings));
            var regularRow = Assert.Single(Assert.IsAssignableFrom<System.Collections.IEnumerable>(bindings).Cast<object>(),
                row => Assert.IsType<string>(row.GetType().GetProperty("AccessibleName")!.GetValue(row)).StartsWith("src/regular-left.cs", StringComparison.Ordinal));
            Assert.DoesNotContain("(preview)", Assert.IsType<string>(regularRow.GetType().GetProperty("Label")!.GetValue(regularRow)));
            Assert.Equal(0, f.Tools.WriteCalls);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Unadopted_recovery_merge_keeps_originals_separate_and_vetoes_close_without_source_authority()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine optional Home/Files reader fixture requires Linux; other platforms need their owning graph.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture();
        DeveloperProjectWorkbenchPage? page = null;
        try
        {
            page = await SearchPublicationPage(f, reader.Owner);
            // Enrol protected observation data only. This fixture has no task recovery owner,
            // so typing must never make an adoption command or a source write available.
            var first = new DeveloperDraftRecoveryTab("Main.cui", DeveloperManualSourceEditOwner.Sha256("old"), "retained original", true);
            var state = new DeveloperManualRecoveryState(new(Guid.NewGuid(), f.Current.TaskId, f.Current.ExecutionId, f.Current.ContextId),
                1, [new(first, "external current source")], first.RelativePath, true);
            typeof(DeveloperProjectWorkbenchPage).GetMethod("StageTaskRecoveryConflicts", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, [state]);
            Assert.False(page.HasUnsavedWork);
            Assert.True(page.TrySetValue("RecoveredMergedDraft", "external current source plus retained changes"));
            Assert.True(page.HasUnsavedWork);
            Assert.True(page.TryGetValue("RecoveredMergedDraft", out var merged));
            Assert.Equal("external current source plus retained changes", merged);
            Assert.True(page.TryGetValue("RecoveredRetainedDraft", out var retained)); Assert.Equal(first.DraftText, retained);
            Assert.True(page.TryGetValue("RecoveredCurrentSource", out var current)); Assert.Equal("external current source", current);
            Assert.False(page.IsActionAvailable("dev.use-merged-recovered-draft"));
            Assert.Equal(0, f.Tools.WriteCalls);
            Assert.True(page.TrySetValue("RecoveredMergedDraft", first.DraftText));
            Assert.False(page.HasUnsavedWork); // Only genuinely new merge text needs an extra close veto.
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Go_to_line_uses_the_current_unsaved_editor_text_and_invalid_locations_preserve_selection()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine optional Home/Files reader fixture requires Linux; other platforms need their owning graph.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture();
        DeveloperProjectWorkbenchPage? page = null;
        try
        {
            f.Tools.TextValue = "first\r\nsecond";
            page = await SearchPublicationPage(f, reader.Owner);
            page.TrySetValue("SourcePath", "Main.cui"); await page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken);
            Assert.True(page.TrySetValue("SourceLocation", "2:3"));
            await page.DispatchAsync("dev.go-to-source-location", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("EditorTextSelection", out var actual));
            var before = Assert.IsType<CuiTextSelection>(actual); Assert.Equal(9, before.Start); Assert.Equal(0, before.Length);
            Assert.True(page.TrySetValue("SourceLocation", "900:1"));
            await page.DispatchAsync("dev.go-to-source-location", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("EditorTextSelection", out actual)); Assert.Same(before, actual);
            Assert.True(page.TrySetValue("DraftText", "new first\nnew second"));
            Assert.True(page.TryGetValue("EditorTextSelection", out actual)); Assert.Null(actual);
            page.TrySetValue("SourceLocation", "2:1"); await page.DispatchAsync("dev.go-to-source-location", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("EditorTextSelection", out actual));
            var after = Assert.IsType<CuiTextSelection>(actual); Assert.Equal(10, after.Start); Assert.True(after.RequestId > before.RequestId);
            Assert.NotEqual(before.TextSha256, after.TextSha256); Assert.Equal(0, f.Tools.WriteCalls);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Opening_search_match_selects_its_actual_saved_range_but_never_offsets_a_different_live_draft()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine optional Home/Files reader fixture requires Linux; other platforms need their owning graph.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture();
        DeveloperProjectWorkbenchPage? page = null;
        try
        {
            const string source = "line one\nsaved source target"; f.Tools.TextValue = source;
            page = await SearchPublicationPage(f, reader.Owner);
            var first = EnrollObservedSearchInput(page, f, "Main.cui", source);
            await page.DispatchAsync("dev.open-search-result", first, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("EditorTextSelection", out var actual));
            var range = Assert.IsType<CuiTextSelection>(actual);
            Assert.Equal("source", source.Substring(range.Start, range.Length));
            Assert.True(page.TrySetValue("DraftText", "a longer newer live draft"));
            var second = EnrollObservedSearchInput(page, f, "Main.cui", source);
            await page.DispatchAsync("dev.open-search-result", second, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("EditorTextSelection", out actual)); Assert.Null(actual);
            Assert.True(page.TryGetValue("DraftText", out var draft)); Assert.Equal("a longer newer live draft", draft);
            Assert.Single(NativeSourceTabs(page)); Assert.Equal(0, f.Tools.WriteCalls);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Saved_stage_selection_only_inspects_current_root_and_explicit_Run_uses_the_canonical_command_owner()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine optional Home/Files reader fixture requires Linux; other platforms need their owning graph.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture();
        DeveloperProjectWorkbenchPage? page = null;
        try
        {
            var root = Assert.Single(f.Store.Workspace.Roots);
            var stage = new DeveloperBuildStage("compile", DeveloperBuildStageKind.Compile, "Compile", [], "echo configured-stage", [], TimeSpan.FromSeconds(10));
            var task = new DeveloperTaskDefinition("build", "Build", "user", root.EnvironmentId, root.RootId.ToString("D"), [], [stage], true);
            f.Store.Workspace = f.Store.Workspace with { Tasks = [task,
                task with { TaskId = "foreign-root", WorkingDirectoryRootId = Guid.NewGuid().ToString("D") },
                task with { TaskId = "foreign-environment", EnvironmentId = "other-environment" }] };
            page = await SearchPublicationPage(f, reader.Owner);
            Assert.True(page.TryGetValue("SavedBuildStages", out var choices));
            var row = Assert.Single(Assert.IsAssignableFrom<System.Collections.IEnumerable>(choices).Cast<object>());
            var key = Assert.IsType<string>(row.GetType().GetProperty("SelectionKey")!.GetValue(row));
            await page.DispatchAsync("dev.select-build-stage", key, OriginalTestBodyToken);
            Assert.Equal(0, f.Tools.ProcessCalls); Assert.Equal(0, f.Owner.ExecuteCalls);
            Assert.True(page.TryGetValue("SelectedBuildStageSummary", out var summary));
            Assert.Contains("echo configured-stage", Assert.IsType<string>(summary)); Assert.True(page.IsActionAvailable("dev.run-stage"));
            await page.DispatchAsync("dev.run-stage", null, OriginalTestBodyToken);
            Assert.Equal(1, f.Tools.ProcessCalls); Assert.Equal(1, f.Owner.ExecuteCalls);
            var process = Assert.IsType<ProcessRequest>(f.Tools.LastProcessRequest);
            Assert.Contains("configured-stage", process.Arguments); Assert.Equal(TimeSpan.FromSeconds(10), process.Timeout);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Changed_saved_stage_and_unfulfilled_dependencies_disable_Run_without_any_process_effect()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine optional Home/Files reader fixture requires Linux; other platforms need their owning graph.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture();
        DeveloperProjectWorkbenchPage? page = null;
        try
        {
            var root = Assert.Single(f.Store.Workspace.Roots);
            var stages = new List<DeveloperBuildStage> {
                new("compile", DeveloperBuildStageKind.Compile, "Compile", [], "echo original", [], TimeSpan.FromSeconds(10)),
                new("package", DeveloperBuildStageKind.Package, "Package", ["compile"], "echo package", [], TimeSpan.FromSeconds(10)) };
            f.Store.Workspace = f.Store.Workspace with { Tasks = [new("build", "Build", "user", root.EnvironmentId, root.RootId.ToString("D"), [], stages, true)] };
            page = await SearchPublicationPage(f, reader.Owner);
            object[] Choices()
            {
                Assert.True(page.TryGetValue("SavedBuildStages", out var choices));
                return Assert.IsAssignableFrom<System.Collections.IEnumerable>(choices).Cast<object>().ToArray();
            }
            string Key(string label)
            {
                var row = Assert.Single(Choices(), choice => (string)choice.GetType().GetProperty("Label")!.GetValue(choice)! == label);
                return Assert.IsType<string>(row.GetType().GetProperty("SelectionKey")!.GetValue(row));
            }
            var oldKey = Key("Build / Compile");
            await page.DispatchAsync("dev.select-build-stage", oldKey, OriginalTestBodyToken); Assert.True(page.IsActionAvailable("dev.run-stage"));
            stages[0] = stages[0] with { Command = "echo changed-after-selection" };
            Assert.False(page.IsActionAvailable("dev.run-stage")); Assert.NotEqual(oldKey, Key("Build / Compile"));
            await page.DispatchAsync("dev.select-build-stage", Key("Build / Package"), OriginalTestBodyToken);
            Assert.False(page.IsActionAvailable("dev.run-stage"));
            Assert.True(page.TryGetValue("SelectedBuildStageSummary", out var summary)); Assert.Contains("prerequisites", Assert.IsType<string>(summary));
            // Existing explicit IDs remain a valid selection route, and capture the exact current definition.
            page.TrySetValue("StageTaskId", "build"); page.TrySetValue("StageId", "compile");
            Assert.True(page.IsActionAvailable("dev.run-stage")); Assert.Equal(0, f.Tools.ProcessCalls); Assert.Equal(0, f.Owner.ExecuteCalls);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Literal_find_and_replace_edit_the_same_unreviewed_draft_and_never_write_saved_source()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine optional Home/Files reader fixture requires Linux; other platforms need their owning graph.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture();
        DeveloperProjectWorkbenchPage? page = null;
        try
        {
            const string saved = "alpha alpha\nALPHA"; f.Tools.TextValue = saved;
            page = await SearchPublicationPage(f, reader.Owner);
            page.TrySetValue("SourcePath", "Main.cui"); await page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken);
            page.TrySetValue("SourceFindQuery", "alpha"); page.TrySetValue("SourceReplaceText", "beta");
            await page.DispatchAsync("dev.find-next-source", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("EditorTextSelection", out var selection)); Assert.Equal(0, Assert.IsType<CuiTextSelection>(selection).Start);
            Assert.True(page.IsActionAvailable("dev.replace-source-match"));
            await page.DispatchAsync("dev.replace-source-match", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out var draft)); Assert.Equal("beta alpha\nALPHA", draft);
            Assert.False(page.IsActionAvailable("dev.replace-source-match")); Assert.False(page.IsActionAvailable("dev.apply-edit"));
            await page.DispatchAsync("dev.find-next-source", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("EditorTextSelection", out selection)); Assert.Equal(5, Assert.IsType<CuiTextSelection>(selection).Start);
            page.TrySetValue("DraftText", "prefix beta alpha\nALPHA");
            Assert.False(page.IsActionAvailable("dev.replace-source-match"));
            page.TrySetValue("SourceFindMatchCase", false); page.TrySetValue("SourceReplaceText", "z");
            await page.DispatchAsync("dev.replace-all-source", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out draft)); Assert.Equal("prefix beta z\nz", draft);
            Assert.True(page.HasUnsavedWork); Assert.False(page.IsActionAvailable("dev.apply-edit"));
            Assert.Equal(saved, f.Tools.TextValue); Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(0, f.Tools.ProcessCalls);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Draft_undo_redo_keep_document_histories_separate_and_invalidate_previous_review_without_saving()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine optional Home/Files reader fixture requires Linux; other platforms need their owning graph.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture();
        DeveloperProjectWorkbenchPage? page = null;
        try
        {
            const string saved = "alpha"; f.Tools.TextValue = saved;
            page = await SearchPublicationPage(f, reader.Owner);
            page.TrySetValue("SourcePath", "Main.cui"); await page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken);
            Assert.False(page.IsActionAvailable("dev.undo-source-edit")); Assert.False(page.IsActionAvailable("dev.redo-source-edit"));
            page.TrySetValue("DraftText", "alpha typed");
            page.TrySetValue("SourceFindQuery", "alpha"); page.TrySetValue("SourceReplaceText", "beta");
            await page.DispatchAsync("dev.replace-all-source", null, OriginalTestBodyToken);
            await page.DispatchAsync("dev.undo-source-edit", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out var draft)); Assert.Equal("alpha typed", draft);
            await page.DispatchAsync("dev.preview-edit", null, OriginalTestBodyToken);
            Assert.True(page.IsActionAvailable("dev.apply-edit"));
            await page.DispatchAsync("dev.undo-source-edit", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out draft)); Assert.Equal(saved, draft);
            Assert.False(page.HasUnsavedWork); Assert.False(page.IsActionAvailable("dev.apply-edit"));
            await page.DispatchAsync("dev.redo-source-edit", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out draft)); Assert.Equal("alpha typed", draft);
            Assert.False(page.IsActionAvailable("dev.apply-edit"));
            page.TrySetValue("DraftText", "new Main draft"); Assert.False(page.IsActionAvailable("dev.redo-source-edit"));
            page.TrySetValue("SourcePath", "Other.cui"); await page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken);
            Assert.False(page.IsActionAvailable("dev.undo-source-edit")); page.TrySetValue("DraftText", "other draft");
            await page.DispatchAsync("dev.undo-source-edit", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out draft)); Assert.Equal(saved, draft);
            var main = Assert.Single(NativeSourceTabs(page), tab => NativeTabPath(tab) == "Main.cui");
            var key = Assert.IsType<Guid>(main.GetType().GetProperty("Id")!.GetValue(main)).ToString("D");
            await page.DispatchAsync("dev.select-source-tab", key, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out draft)); Assert.Equal("new Main draft", draft);
            await page.DispatchAsync("dev.undo-source-edit", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out draft)); Assert.Equal("alpha typed", draft);
            Assert.False(page.IsActionAvailable("dev.apply-edit")); Assert.True(page.HasUnsavedWork);
            Assert.Equal(saved, f.Tools.TextValue); Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(0, f.Tools.ProcessCalls);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Cui_draft_problems_and_outline_navigate_exact_current_source_and_withdraw_after_edits()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine optional Home/Files reader fixture requires Linux; other platforms need their owning graph.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture();
        DeveloperProjectWorkbenchPage? page = null;
        try
        {
            const string saved = "<Cui>\n<Page>\n<Button id=\"go\"><ActionEvent Type=\"UnknownEvent\" /></Button>\n</Page>\n</Cui>";
            f.Tools.TextValue = saved; page = await SearchPublicationPage(f, reader.Owner);
            page.TrySetValue("SourcePath", "Main.cui"); await page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken);
            var reads = f.Tools.ReadCalls; var preparations = f.Owner.PrepareCalls;
            await page.DispatchAsync("dev.analyze-cui-draft", null, OriginalTestBodyToken);
            object[] Rows(string binding)
            {
                Assert.True(page.TryGetValue(binding, out var value)); return Assert.IsAssignableFrom<System.Collections.IEnumerable>(value).Cast<object>().ToArray();
            }
            string Key(object row) => Assert.IsType<string>(row.GetType().GetProperty("SelectionKey")!.GetValue(row));
            string Label(object row) => Assert.IsType<string>(row.GetType().GetProperty("Label")!.GetValue(row));
            var problem = Assert.Single(Rows("CuiDraftProblems"), row => Label(row).Contains("CUIE001", StringComparison.Ordinal));
            var component = Assert.Single(Rows("CuiDraftOutline"), row => Label(row).Contains("go — Button", StringComparison.Ordinal));
            var oldProblemKey = Key(problem); var oldComponentKey = Key(component);
            await page.DispatchAsync("dev.go-to-cui-element", oldComponentKey, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("EditorTextSelection", out var selection));
            Assert.Equal(DeveloperManualSourceEditOwner.Sha256(saved), Assert.IsType<CuiTextSelection>(selection).TextSha256);
            Assert.True(page.TryGetValue("SourceLocation", out var location)); Assert.StartsWith("3:", Assert.IsType<string>(location));
            await page.DispatchAsync("dev.go-to-cui-problem", oldProblemKey, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("CuiDraftAnalysisStatus", out var status)); Assert.Contains("do not resolve imports", Assert.IsType<string>(status));
            Assert.Equal(reads, f.Tools.ReadCalls); Assert.Equal(preparations, f.Owner.PrepareCalls);
            page.TrySetValue("DraftText", "<Cui />");
            Assert.Empty(Rows("CuiDraftProblems")); Assert.Empty(Rows("CuiDraftOutline"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => page.DispatchAsync("dev.go-to-cui-problem", oldProblemKey, OriginalTestBodyToken).AsTask());
            await page.DispatchAsync("dev.analyze-cui-draft", null, OriginalTestBodyToken);
            Assert.Empty(Rows("CuiDraftProblems")); Assert.Empty(Rows("CuiDraftOutline"));
            Assert.True(page.IsActionAvailable("dev.analyze-cui-draft"));
            Assert.True(page.TryGetValue("CuiDraftAnalysisStatus", out status)); Assert.Contains("0 syntax/action problems", Assert.IsType<string>(status));
            page.TrySetValue("SourcePath", "Other.cs"); await page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken);
            Assert.False(page.IsActionAvailable("dev.analyze-cui-draft"));
            Assert.True(page.TryGetValue("CuiDraftAnalysisStatus", out status)); Assert.Contains("configured toolchain", Assert.IsType<string>(status));
            Assert.Equal(saved, f.Tools.TextValue); Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(0, f.Tools.ProcessCalls);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Cui_property_inspector_edits_only_the_literal_draft_value_and_preserves_review_undo_and_source_boundaries()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine optional Home/Files reader fixture requires Linux; other platforms need their owning graph.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture();
        DeveloperProjectWorkbenchPage? page = null;
        try
        {
            const string saved = "<Cui>\r\n<!-- keep this comment -->\r\n<Page><Text id='title' text='A &amp; B' font-size='18' /></Page>\r\n</Cui>";
            f.Tools.TextValue = saved; page = await SearchPublicationPage(f, reader.Owner);
            page.TrySetValue("SourcePath", "Main.cui"); await page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken);
            var reads = f.Tools.ReadCalls; var preparations = f.Owner.PrepareCalls;
            object[] Rows(string binding)
            {
                Assert.True(page.TryGetValue(binding, out var value)); return Assert.IsAssignableFrom<System.Collections.IEnumerable>(value).Cast<object>().ToArray();
            }
            string Key(object row) => Assert.IsType<string>(row.GetType().GetProperty("SelectionKey")!.GetValue(row));
            string Label(object row) => Assert.IsType<string>(row.GetType().GetProperty("Label")!.GetValue(row));
            await page.DispatchAsync("dev.analyze-cui-draft", null, OriginalTestBodyToken);
            var component = Assert.Single(Rows("CuiDraftOutline"), row => Label(row).Contains("title — Text", StringComparison.Ordinal));
            await page.DispatchAsync("dev.go-to-cui-element", Key(component), OriginalTestBodyToken);
            var property = Assert.Single(Rows("CuiInspectableProperties"), row => Label(row) == "text — text");
            var oldPropertyKey = Key(property);
            await page.DispatchAsync("dev.select-cui-property", oldPropertyKey, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("CuiPropertyDraftValue", out var value)); Assert.Equal("A & B", value);
            page.TrySetValue("CuiPropertyDraftValue", "Changed 'quoted' & < x");
            Assert.True(page.IsActionAvailable("dev.change-cui-property"));
            await page.DispatchAsync("dev.change-cui-property", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out var draft));
            Assert.Equal(saved.Replace("A &amp; B", "Changed &apos;quoted&apos; &amp; &lt; x", StringComparison.Ordinal), draft);
            Assert.True(page.HasUnsavedWork); Assert.True(page.IsActionAvailable("dev.undo-source-edit"));
            Assert.False(page.IsActionAvailable("dev.apply-edit")); Assert.Empty(Rows("CuiInspectableProperties"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => page.DispatchAsync("dev.select-cui-property", oldPropertyKey, OriginalTestBodyToken).AsTask());
            await page.DispatchAsync("dev.undo-source-edit", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out draft)); Assert.Equal(saved, draft);
            Assert.False(page.HasUnsavedWork); Assert.False(page.IsActionAvailable("dev.apply-edit"));
            Assert.Equal(reads, f.Tools.ReadCalls); Assert.Equal(preparations, f.Owner.PrepareCalls);
            Assert.Equal(saved, f.Tools.TextValue); Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(0, f.Tools.ProcessCalls);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Declared_Run_target_selection_pins_configuration_and_preserves_real_command_observation_in_same_task()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine optional Home/Files reader fixture requires Linux; other platforms need their owning graph.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture();
        DeveloperProjectWorkbenchPage? page = null;
        try
        {
            var configuration = ConfigureConsoleTarget(f); var project = Assert.Single(f.Store.Workspace.Projects);
            var declarations = new List<DeveloperRunConfiguration> { configuration, configuration with { Id = "dependent", PreLaunchTaskIds = ["restore"] } };
            f.Store.Workspace = f.Store.Workspace with { RunConfigurations = declarations, Projects = [project with { RunConfigurationIds = [configuration.Id, "dependent"] }] };
            page = await SearchPublicationPage(f, reader.Owner);
            object[] Choices()
            {
                Assert.True(page.TryGetValue("RunTargets", out var value)); return Assert.IsAssignableFrom<System.Collections.IEnumerable>(value).Cast<object>().ToArray();
            }
            string Key(string label)
            {
                var row = Assert.Single(Choices(), choice => (string)choice.GetType().GetProperty("Label")!.GetValue(choice)! == label);
                return Assert.IsType<string>(row.GetType().GetProperty("SelectionKey")!.GetValue(row));
            }
            Assert.False(page.IsActionAvailable("dev.run-selected-target"));
            await page.DispatchAsync("dev.select-run-target", Key("console-target — console"), OriginalTestBodyToken);
            Assert.True(page.IsActionAvailable("dev.run-selected-target")); Assert.Equal(0, f.Tools.ProcessCalls);
            await page.DispatchAsync("dev.run-selected-target", null, OriginalTestBodyToken);
            Assert.Equal(1, f.Tools.ProcessCalls); Assert.Equal(configuration.Id, page.OriginalDeveloperObservation!.RunConfigurationId);
            Assert.Equal(DeveloperRunConfigurationSelection.Digest(configuration), page.OriginalDeveloperObservation.RunConfigurationDefinitionSha256);
            Assert.Same(f.Tools.Process, page.OriginalDeveloperObservation.OriginalProcessResult);
            var oldKey = Key("console-target — console"); declarations[0] = configuration with { Command = "echo changed after selection" };
            Assert.False(page.IsActionAvailable("dev.run-selected-target")); Assert.NotEqual(oldKey, Key("console-target — console"));
            await page.DispatchAsync("dev.select-run-target", Key("dependent — console"), OriginalTestBodyToken);
            Assert.False(page.IsActionAvailable("dev.run-selected-target"));
            Assert.True(page.TryGetValue("SelectedRunTargetSummary", out var summary)); Assert.Contains("prelaunch dependencies", Assert.IsType<string>(summary));
            Assert.Equal(1, f.Tools.ProcessCalls);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Committed_Run_declaration_reconciliation_retains_manual_dirty_tab_history_and_invalidates_only_old_reviews()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine optional reader fixture requires Linux.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture();
        DeveloperProjectWorkbenchPage? page = null;
        try
        {
            var before = (await f.Dev.ResolveAsync(f.Reference)).Value!;
            var constructor = typeof(DeveloperProjectWorkbenchPage).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(value => value.GetParameters().Length == 16 && value.GetParameters()[9].ParameterType == typeof(DeveloperManualSourceReadOwner));
            Func<DeveloperResolvedProject, string, CancellationToken, Task<DeveloperOperationResult<DeveloperCodeDocument>>> unused =
                (_, _, _) => throw new InvalidOperationException("View-only reconciliation never requests source or Task access.");
            page = (DeveloperProjectWorkbenchPage)constructor.Invoke([f.Dev, f.Tasks, before, new SearchPublicationNoReadiness(), unused,
                (Action)(() => { }), OriginalTestBodyToken, null, null, reader.Owner, null, null, null, null, null, null]);
            SearchPublicationField("_active").SetValue(page, true); // Component-only admission; no native frame or Home grant.
            var type = typeof(DeveloperProjectWorkbenchPage);
            var stateType = type.GetNestedType("ManualTabState", BindingFlags.NonPublic)!;
            var state = stateType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(value => value.GetParameters().Length == 9).Invoke(["authored unsaved draft", "saved source", "saved source", "old reviewed after", new string('a', 64), new string('b', 64), 9L, 9L, true]);
            var tabType = type.GetNestedType("SourceTab", BindingFlags.NonPublic)!;
            var tab = tabType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(value => value.GetParameters().Length == 4).Invoke([Guid.NewGuid(), "Main.cui", null, state]);
            var history = new DeveloperDraftHistory("saved source"); history.RecordChange("saved source", "authored unsaved draft");
            tabType.GetField("DraftHistory")!.SetValue(tab, history); tabType.GetField("DraftHistoryBasis")!.SetValue(tab, new string('a', 64));
            ((System.Collections.IList)SearchPublicationField("_sourceTabs").GetValue(page)!).Add(tab);
            type.GetMethod("SelectTab", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [tab]);
            var configuration = new DeveloperRunConfiguration("manual-run", "Workspace", before.Project.ProjectId.ToString("D"), "console", [],
                before.Root.RootId.ToString("D"), "local", [], null) { Command = "printf later", TimeoutSeconds = 20 };
            var project = before.Project with { Revision = before.Project.Revision + 1, RunConfigurationIds = [configuration.Id] };
            var workspace = before.Workspace with { Revision = before.Workspace.Revision + 1, Projects = [project], RunConfigurations = [configuration] };
            var after = before with { Reference = before.Reference with { WorkspaceRevision = workspace.Revision, ProjectRevision = project.Revision }, Workspace = workspace, Project = project };
            var scope = new DeveloperDraftRecoveryScope(Guid.NewGuid(), Guid.Empty, Guid.Empty, Guid.Empty, IsManual: true);
            // Typed result input isolates view reconciliation. Actual canonical commit and native
            // Run admission are covered by the genuine publication fixture, not this component test.
            type.GetMethod("AdoptSavedRunTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page,
                [new DeveloperRunConfigurationEditResult(after, configuration, scope, 12, 7, true)]);
            Assert.Same(tab, Assert.Single(NativeSourceTabs(page))); Assert.Same(history, tabType.GetField("DraftHistory")!.GetValue(tab));
            Assert.True(history.CanUndo("authored unsaved draft"));
            Assert.True(page.TryGetValue("DraftText", out var draft)); Assert.Equal("authored unsaved draft", draft);
            Assert.Equal(9L, SearchPublicationField("_manualDraftRevision").GetValue(page));
            Assert.False((bool)SearchPublicationField("_manualReviewed").GetValue(page)!);
            Assert.Equal(new string('a', 64), SearchPublicationField("_manualBeforeSha256").GetValue(page));
            Assert.Equal(after, SearchPublicationField("_project").GetValue(page));
            Assert.Equal(12L, SearchPublicationField("_manualRecoveryGeneration").GetValue(page));
            Assert.Equal(7L, SearchPublicationField("_cleanSessionGeneration").GetValue(page));
            Assert.True(page.HasUnsavedWork); Assert.Equal(0, f.Tools.ProcessCalls); Assert.Equal(0, f.Tools.WriteCalls);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaTheory]
    [InlineData("Glow")]
    [InlineData("Professional")]
    public async Task Native_static_CUI_preview_retains_last_valid_scene_without_source_effects(string themeName)
    {
        // Keep the test body's token before native layout pumps the dispatcher. Later work
        // continues through this captured owner, never a transient ambient TestContext.
        var token = TestContext.Current.CancellationToken;
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine optional Home/Files reader fixture requires Linux; other platforms need their owning graph.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture();
        DeveloperProjectWorkbenchPage? page = null; Window? window = null;
        using var theme = new CuiThemePreferenceContext(new WorkbenchPresentationTheme(themeName));
        try
        {
            const string saved = "<Cui><Page><Container Type='Vertical Stack'><Text text='compiled &amp; literal' /></Container></Page></Cui>";
            f.Tools.TextValue = saved;
            page = await SearchPublicationPage(f, reader.Owner, new PreviewComponentReadiness(), theme);
            // Real workbench artifact, registered Object and child scene. The borrowed theme
            // is bound before activation; controlled component readiness is unchanged and
            // does not establish native installation, Home admission or a production frame.
            await page.ActivateAsync(token);
            window = new Window { Width = 1440, Height = 900, Content = page }; window.Show();
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            page.TrySetValue("SourcePath", "Main.cui"); await page.DispatchAsync("dev.open-source", null, token);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.True(page.TryGetValue("LayoutPreviewVisible", out var automaticPreview)); Assert.True(Assert.IsType<bool>(automaticPreview));
            Assert.True(page.TryGetValue("LayoutMainColumns", out var columns)); Assert.Equal("214 1fr 300", columns);
            var editor = SearchPublicationField("_editor").GetValue(page);
            var selection = SearchPublicationField("_editorTextSelection").GetValue(page);
            var nativeEditor = WorkbenchControl<TextBox>(page, "dev-code-editor");
            Assert.True(nativeEditor.TryFindResource("CuiEffectiveThemeName", out var effectiveTheme)); Assert.Equal(themeName, effectiveTheme);
            Assert.True(nativeEditor.TryFindResource("CuiWorkspaceBrush", out var workspaceBrush));
            window.Background = Assert.IsAssignableFrom<IBrush>(workspaceBrush);
            var selectedTab = Assert.Single(NativeSourceTabs(page));
            var reads = f.Tools.ReadCalls; var preparations = f.Owner.PrepareCalls;
            var slot = Assert.IsType<ContentControl>(SearchPublicationField("_cuiPreviewSlot").GetValue(page));
            await page.DispatchAsync("dev.preview-cui-draft", null, token);
            var host = Assert.IsType<CuiSceneHost>(slot.Content);
            Assert.False(host.IsHitTestVisible); Assert.Equal(CuiSceneAvailabilityState.Ready, host.Availability!.State);
            Assert.Contains(Assert.IsAssignableFrom<Control>(host.Content).GetVisualDescendants().OfType<TextBlock>(), value => value.Text == "compiled & literal");
            Assert.Same(editor, SearchPublicationField("_editor").GetValue(page));
            Assert.Same(selection, SearchPublicationField("_editorTextSelection").GetValue(page));
            Assert.True(page.TryGetValue("CuiDraftPreviewStatus", out var status)); Assert.Contains("Preview updated.", Assert.IsType<string>(status));
            var previewObservation = Assert.IsType<DeveloperCuiDraftPreview>(SearchPublicationField("_cuiPreviewLastValid").GetValue(page));
            Assert.Equal("Main.cui", previewObservation.RelativePath); Assert.Equal(DeveloperManualSourceEditOwner.Sha256(saved), previewObservation.SourceSha256);
            var previewRevision = Assert.IsType<long>(SearchPublicationField("_cuiPreviewDraftRevision").GetValue(page));
            const string unsupported = "<Cui><Page><Image Source='https://example.test/not-fetched.png' /></Page></Cui>";
            foreach (var state in new[] { "current", "last-valid" })
            {
                var expectedDraft = state == "current" ? saved : unsupported;
                if (state == "last-valid")
                {
                    Assert.True(page.TrySetValue("DraftText", unsupported));
                    await page.DispatchAsync("dev.preview-cui-draft", null, token);
                    Assert.Same(host, slot.Content);
                    Assert.True(page.TryGetValue("CuiDraftPreviewDiagnostics", out var diagnostics)); Assert.Contains("DEVCP003", Assert.IsType<string>(diagnostics));
                    Assert.True(page.TryGetValue("CuiDraftPreviewStatus", out status));
                    AssertPreviewEvidence(Assert.IsType<string>(status), current: false);
                    Assert.True(page.TryGetValue("LayoutPreviewVisible", out automaticPreview)); Assert.True(Assert.IsType<bool>(automaticPreview));
                    Assert.True(page.TryGetValue("DraftText", out var staleDraft)); Assert.Equal(unsupported, staleDraft);
                }
                var stateEditor = SearchPublicationField("_editor").GetValue(page);
                var stateSelection = SearchPublicationField("_editorTextSelection").GetValue(page);
                foreach (var size in new[] { new Size(1440, 900), new Size(720, 650) })
                {
                    window.Width = size.Width; window.Height = size.Height;
                    await page.DispatchAsync("dev.layout.code", null, token);
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    Assert.Same(nativeEditor, WorkbenchControl<TextBox>(page, "dev-code-editor"));
                    Assert.Equal(expectedDraft, nativeEditor.Text);
                    AssertWorkbenchControlInsideViewport(page, nativeEditor);
                    AssertWorkbenchEditActionsInsideViewport(page);
                    Assert.True(page.TryGetValue("LayoutMainColumns", out columns));
                    Assert.Equal(size.Width >= 1180 ? "214 1fr 300" : "1fr", columns);
                    Assert.Equal(size.Width >= 1180, WorkbenchControl<Control>(page, "dev-cui-designer-section").IsEffectivelyVisible);
                    if (size.Width < 1180)
                    {
                        var previewAction = WorkbenchControl<Button>(page, "dev-view-preview");
                        AssertWorkbenchControlInsideViewport(page, previewAction); Assert.True(previewAction.IsEffectivelyEnabled);
                        await page.DispatchAsync("dev.layout.preview", null, token);
                        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                        Assert.False(WorkbenchControl<Control>(page, "dev-editor-section").IsVisible);
                        Assert.DoesNotContain(nativeEditor.GetVisualAncestors(), ancestor => ReferenceEquals(ancestor, page));
                    }
                    var preview = WorkbenchControl<Control>(page, "dev-cui-designer-section");
                    var update = WorkbenchControl<Button>(page, "dev-preview-cui-draft");
                    update.BringIntoView(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    slot.BringIntoView(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    AssertWorkbenchControlInsideViewport(page, preview);
                    AssertWorkbenchControlInsideViewport(preview, update);
                    AssertWorkbenchControlInsideViewport(preview, slot);
                    AssertWorkbenchControlInsideViewport(page, WorkbenchControl<Button>(page, "dev-view-code"));
                    var previewStatus = Assert.Single(preview.GetVisualDescendants().OfType<TextBlock>(),
                        value => AutomationProperties.GetName(value) == "Preview status");
                    AssertWorkbenchControlInsideViewport(preview, previewStatus);
                    AssertPreviewEvidence(Assert.IsType<string>(previewStatus.Text), current: state == "current");
                    Assert.Same(host, slot.Content); Assert.Same(previewObservation, SearchPublicationField("_cuiPreviewLastValid").GetValue(page));
                    Assert.Same(selectedTab, Assert.Single(NativeSourceTabs(page)));
                    Assert.Same(stateEditor, SearchPublicationField("_editor").GetValue(page));
                    Assert.Same(stateSelection, SearchPublicationField("_editorTextSelection").GetValue(page));
                    Assert.True(page.TryGetValue("DraftText", out var retainedDraft)); Assert.Equal(expectedDraft, retainedDraft);
                    Assert.Equal(reads, f.Tools.ReadCalls); Assert.Equal(preparations, f.Owner.PrepareCalls);
                    Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(0, f.Tools.ProcessCalls);
                    var directory = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR") ?? Environment.GetEnvironmentVariable("CUI_EVIDENCE_DIRECTORY");
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                        frame.Save(Path.Combine(directory, $"build-cui-preview-{state}-{themeName}-{size.Width:0}x{size.Height:0}.png"));
                    }
                    // Returning from the narrow Preview restores the same editor and its
                    // retained write-review controls; navigation performs no source operation.
                    await page.DispatchAsync("dev.layout.code", null, token);
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    Assert.Same(nativeEditor, WorkbenchControl<TextBox>(page, "dev-code-editor"));
                    AssertWorkbenchControlInsideViewport(page, nativeEditor); AssertWorkbenchEditActionsInsideViewport(page);
                    Assert.Equal(expectedDraft, nativeEditor.Text);
                }
            }
            page.TrySetValue("DraftText", "<Cui><Page><Text text='next compiled draft' /></Page></Cui>");
            await page.DispatchAsync("dev.preview-cui-draft", null, token);
            var replacement = Assert.IsType<CuiSceneHost>(slot.Content); Assert.NotSame(host, replacement);
            Assert.Contains(Assert.IsAssignableFrom<Control>(replacement.Content).GetVisualDescendants().OfType<TextBlock>(), value => value.Text == "next compiled draft");
            Assert.Equal(reads, f.Tools.ReadCalls); Assert.Equal(preparations, f.Owner.PrepareCalls);
            Assert.Equal(saved, f.Tools.TextValue); Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(0, f.Tools.ProcessCalls);
            var scenes = Assert.IsAssignableFrom<System.Collections.IEnumerable>(SearchPublicationField("_cuiPreviewScenes").GetValue(page));
            Assert.Same(replacement, Assert.Single(scenes.Cast<object>()));
            await page.CloseAndDrainAsync(); Assert.Empty(scenes.Cast<object>());

            void AssertPreviewEvidence(string value, bool current)
            {
                var retainedSource = $"Main.cui · draft revision {previewRevision}";
                if (current)
                {
                    Assert.Contains("Preview updated.", value);
                    Assert.Contains("Showing " + retainedSource, value);
                    Assert.DoesNotContain("Last valid (stale)", value);
                }
                else
                {
                    Assert.Contains("This draft could not be previewed. Check the problems below.", value);
                    Assert.Contains("Last valid (stale): " + retainedSource, value);
                    Assert.Contains("Not a preview of the current draft.", value);
                }
            }
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally
            {
                try { if (window is not null) { window.Content = null; window.Close(); } }
                finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
            }
        }
    }

    [AvaloniaFact]
    public async Task Native_static_CUI_preview_rechecks_draft_after_readiness_and_never_mounts_stale_compilation()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine optional Home/Files reader fixture requires Linux; other platforms need their owning graph.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture();
        var readiness = new PreviewComponentReadiness(); DeveloperProjectWorkbenchPage? page = null; Task? pending = null;
        try
        {
            f.Tools.TextValue = "<Cui><Page><Text text='first valid' /></Page></Cui>";
            page = await SearchPublicationPage(f, reader.Owner, readiness); await page.ActivateAsync(OriginalTestBodyToken);
            page.TrySetValue("SourcePath", "Main.cui"); await page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken);
            await page.DispatchAsync("dev.preview-cui-draft", null, OriginalTestBodyToken);
            var slot = Assert.IsType<ContentControl>(SearchPublicationField("_cuiPreviewSlot").GetValue(page)); var valid = slot.Content;
            var previewObservation = Assert.IsType<DeveloperCuiDraftPreview>(SearchPublicationField("_cuiPreviewLastValid").GetValue(page));
            var previewRevision = Assert.IsType<long>(SearchPublicationField("_cuiPreviewDraftRevision").GetValue(page));
            page.TrySetValue("DraftText", "<Cui><Page><Text text='captured before readiness' /></Page></Cui>");
            readiness.HoldNext = true;
            pending = page.DispatchAsync("dev.preview-cui-draft", null, OriginalTestBodyToken).AsTask();
            await readiness.Entered.Task.WaitAsync(OriginalTestBodyToken); Assert.False(pending.IsCompleted);
            const string newer = "<Cui><Page><Text text='newer edit must win' /></Page></Cui>";
            Assert.True(page.TrySetValue("DraftText", newer)); readiness.Release.TrySetResult(); await pending;
            Assert.Same(valid, slot.Content);
            Assert.True(page.TryGetValue("DraftText", out var draft)); Assert.Equal(newer, draft);
            Assert.True(page.TryGetValue("CuiDraftPreviewStatus", out var status));
            Assert.Contains($"Last valid (stale): Main.cui · draft revision {previewRevision}", Assert.IsType<string>(status));
            Assert.Contains("Not a preview of the current draft.", Assert.IsType<string>(status));
            Assert.Same(previewObservation, SearchPublicationField("_cuiPreviewLastValid").GetValue(page));
            Assert.Equal(DeveloperManualSourceEditOwner.Sha256("<Cui><Page><Text text='first valid' /></Page></Cui>"), previewObservation.SourceSha256);
            var scenes = Assert.IsAssignableFrom<System.Collections.IEnumerable>(SearchPublicationField("_cuiPreviewScenes").GetValue(page));
            Assert.Same(valid, Assert.Single(scenes.Cast<object>()));
            Assert.Equal(0, f.Tools.ProcessCalls); Assert.Equal(0, f.Tools.WriteCalls);
        }
        finally
        {
            readiness.Release.TrySetResult();
            try { if (pending is not null) await pending; }
            finally
            {
                try { if (page is not null) await page.CloseAndDrainAsync(); }
                finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
            }
        }
    }

    [AvaloniaFact]
    public async Task Cui_designer_adds_compiled_literal_Text_to_same_draft_with_undo_preview_and_no_source_write()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The established optional Home/Files fixture requires Linux.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture(); DeveloperProjectWorkbenchPage? page = null;
        try
        {
            const string prefix = "<Cui>\r\n<!-- keep this comment and existing entities -->\r\n<Page><Container id='box' Type='Vertical Stack' margin='9'><Text id='kept' text='A &amp; B' />";
            const string suffix = "</Container></Page>\r\n</Cui>";
            const string saved = prefix + suffix; f.Tools.TextValue = saved;
            page = await SearchPublicationPage(f, reader.Owner, new PreviewComponentReadiness()); await page.ActivateAsync(OriginalTestBodyToken);
            page.TrySetValue("SourcePath", "Main.cui"); await page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken);
            var reads = f.Tools.ReadCalls; var preparations = f.Owner.PrepareCalls;
            await page.DispatchAsync("dev.analyze-cui-draft", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("CuiDraftOutline", out var outline));
            var row = Assert.Single(Assert.IsAssignableFrom<System.Collections.IEnumerable>(outline).Cast<object>(),
                item => ((string)item.GetType().GetProperty("Label")!.GetValue(item)!).Contains("box — Container", StringComparison.Ordinal));
            var key = Assert.IsType<string>(row.GetType().GetProperty("SelectionKey")!.GetValue(row));
            await page.DispatchAsync("dev.go-to-cui-element", key, OriginalTestBodyToken);
            page.TrySetValue("CuiNewTextId", "kept");
            await page.DispatchAsync("dev.add-cui-text-child", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out var unchanged)); Assert.Equal(saved, unchanged);
            Assert.True(page.TryGetValue("CuiTextChildStatus", out var refusal)); Assert.Contains("already exists", Assert.IsType<string>(refusal));
            const string value = "Child < & \" ' \t\r\n😀";
            page.TrySetValue("CuiNewTextId", "new-child"); page.TrySetValue("CuiNewTextValue", value);
            await page.DispatchAsync("dev.add-cui-text-child", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out var result)); var draft = Assert.IsType<string>(result);
            Assert.StartsWith(prefix, draft); Assert.EndsWith(suffix, draft);
            Assert.Equal(saved, draft.Remove(prefix.Length, draft.Length - saved.Length)); Assert.True(page.HasUnsavedWork);
            Assert.True(page.IsActionAvailable("dev.undo-source-edit")); Assert.False(page.IsActionAvailable("dev.apply-edit"));
            // Actual compiler/artifact/scene; readiness is controlled for this component,
            // without claiming a native frame or replacing any source permission owner.
            await page.DispatchAsync("dev.preview-cui-draft", null, OriginalTestBodyToken);
            var slot = Assert.IsType<ContentControl>(SearchPublicationField("_cuiPreviewSlot").GetValue(page));
            var preview = Assert.IsType<CuiSceneHost>(slot.Content);
            Assert.Contains(Assert.IsAssignableFrom<Control>(preview.Content).GetVisualDescendants().OfType<TextBlock>(), node => node.Name == "new-child" && node.Text == value);
            await page.DispatchAsync("dev.undo-source-edit", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out unchanged)); Assert.Equal(saved, unchanged);
            await page.DispatchAsync("dev.redo-source-edit", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out result)); Assert.Equal(draft, result);
            Assert.Equal(reads, f.Tools.ReadCalls); Assert.Equal(preparations, f.Owner.PrepareCalls);
            Assert.Equal(saved, f.Tools.TextValue); Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(0, f.Tools.ProcessCalls);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Cui_Text_move_remove_share_actual_compilation_preview_draft_undo_and_no_source_write()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The established optional Home/Files fixture requires Linux.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture(); DeveloperProjectWorkbenchPage? page = null;
        try
        {
            const string prefix = "<Cui>\r\n<Page id='page'><Container id='box' Type='Vertical Stack'>\r\n";
            const string first = "<Text id='first' text='First &amp; fixed' />";
            const string between = "\r\n<!-- keep this comment in place -->\r\n";
            const string second = "<Text id='second' text='Second' />";
            const string suffix = "\r\n</Container></Page>\r\n</Cui>";
            const string saved = prefix + first + between + second + suffix;
            const string moved = prefix + second + between + first + suffix;
            const string removed = prefix + second + between + suffix;
            f.Tools.TextValue = saved; page = await SearchPublicationPage(f, reader.Owner, new PreviewComponentReadiness());
            await page.ActivateAsync(OriginalTestBodyToken); page.TrySetValue("SourcePath", "Main.cui");
            await page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken);
            var tab = Assert.Single(NativeSourceTabs(page)); var reads = f.Tools.ReadCalls; var preparations = f.Owner.PrepareCalls;
            async Task SelectFirst()
            {
                await page.DispatchAsync("dev.analyze-cui-draft", null, OriginalTestBodyToken);
                Assert.True(page.TryGetValue("CuiDraftOutline", out var outline));
                var row = Assert.Single(Assert.IsAssignableFrom<System.Collections.IEnumerable>(outline).Cast<object>(),
                    item => ((string)item.GetType().GetProperty("Label")!.GetValue(item)!).Contains("first — Text", StringComparison.Ordinal));
                await page.DispatchAsync("dev.go-to-cui-element", row.GetType().GetProperty("SelectionKey")!.GetValue(row), OriginalTestBodyToken);
            }
            await SelectFirst(); Assert.False(page.IsActionAvailable("dev.move-cui-text-up")); Assert.True(page.IsActionAvailable("dev.move-cui-text-down"));
            await page.DispatchAsync("dev.move-cui-text-down", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out var draft)); Assert.Equal(moved, draft); Assert.True(page.HasUnsavedWork);
            Assert.False(page.IsActionAvailable("dev.remove-cui-text")); Assert.False(page.IsActionAvailable("dev.apply-edit"));
            await page.DispatchAsync("dev.preview-cui-draft", null, OriginalTestBodyToken);
            var slot = Assert.IsType<ContentControl>(SearchPublicationField("_cuiPreviewSlot").GetValue(page));
            var scene = Assert.IsType<CuiSceneHost>(slot.Content);
            Assert.Equal(new[] { "second", "first" }, Assert.IsAssignableFrom<Control>(scene.Content).GetVisualDescendants().OfType<TextBlock>()
                .Where(node => node.Name is "first" or "second").Select(node => node.Name).ToArray());
            await page.DispatchAsync("dev.undo-source-edit", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out draft)); Assert.Equal(saved, draft);
            await page.DispatchAsync("dev.redo-source-edit", null, OriginalTestBodyToken);
            await SelectFirst(); Assert.True(page.IsActionAvailable("dev.move-cui-text-up")); Assert.True(page.IsActionAvailable("dev.remove-cui-text"));
            await page.DispatchAsync("dev.remove-cui-text", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out draft)); Assert.Equal(removed, draft);
            await page.DispatchAsync("dev.preview-cui-draft", null, OriginalTestBodyToken);
            scene = Assert.IsType<CuiSceneHost>(slot.Content);
            Assert.DoesNotContain(Assert.IsAssignableFrom<Control>(scene.Content).GetVisualDescendants().OfType<TextBlock>(), node => node.Name == "first");
            Assert.Contains(Assert.IsAssignableFrom<Control>(scene.Content).GetVisualDescendants().OfType<TextBlock>(), node => node.Name == "second");
            await page.DispatchAsync("dev.undo-source-edit", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out draft)); Assert.Equal(moved, draft);
            await page.DispatchAsync("dev.redo-source-edit", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out draft)); Assert.Equal(removed, draft);
            Assert.Same(tab, Assert.Single(NativeSourceTabs(page))); Assert.Equal(reads, f.Tools.ReadCalls); Assert.Equal(preparations, f.Owner.PrepareCalls);
            Assert.Equal(saved, f.Tools.TextValue); Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(0, f.Tools.ProcessCalls);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Draft_line_ending_controls_convert_same_unsaved_document_with_undo_and_no_source_effects()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The established optional Home/Files fixture requires Linux.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture(); DeveloperProjectWorkbenchPage? page = null;
        try
        {
            const string saved = "first\r\nsecond\nthird\rfourth";
            const string lf = "first\nsecond\nthird\nfourth";
            const string crlf = "first\r\nsecond\r\nthird\r\nfourth";
            f.Tools.TextValue = saved; page = await SearchPublicationPage(f, reader.Owner);
            page.TrySetValue("SourcePath", "Main.cs"); await page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken);
            var tab = Assert.Single(NativeSourceTabs(page)); var reads = f.Tools.ReadCalls; var preparations = f.Owner.PrepareCalls;
            await page.DispatchAsync("dev.check-line-endings", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("LineEndingsStatus", out var status)); Assert.Contains("Mixed line endings", Assert.IsType<string>(status));
            await page.DispatchAsync("dev.use-lf-endings", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out var draft)); Assert.Equal(lf, draft); Assert.True(page.HasUnsavedWork);
            Assert.True(page.TryGetValue("LineEndingsStatus", out status)); Assert.Contains("Line endings: LF", Assert.IsType<string>(status));
            Assert.False(page.IsActionAvailable("dev.use-lf-endings")); Assert.True(page.IsActionAvailable("dev.use-crlf-endings"));
            Assert.False(page.IsActionAvailable("dev.apply-edit"));
            await page.DispatchAsync("dev.use-crlf-endings", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out draft)); Assert.Equal(crlf, draft);
            await page.DispatchAsync("dev.undo-source-edit", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out draft)); Assert.Equal(lf, draft);
            Assert.True(page.TryGetValue("LineEndingsStatus", out status)); Assert.Contains("Draft changed", Assert.IsType<string>(status));
            await page.DispatchAsync("dev.undo-source-edit", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out draft)); Assert.Equal(saved, draft);
            await page.DispatchAsync("dev.redo-source-edit", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("DraftText", out draft)); Assert.Equal(lf, draft);
            Assert.Same(tab, Assert.Single(NativeSourceTabs(page))); Assert.Equal(reads, f.Tools.ReadCalls); Assert.Equal(preparations, f.Owner.PrepareCalls);
            Assert.Equal(saved, f.Tools.TextValue); Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(0, f.Tools.ProcessCalls);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    private sealed class PreviewComponentReadiness : ICuiSceneReadiness
    {
        public bool HoldNext;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        {
            if (HoldNext) { HoldNext = false; Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            return new(CuiSceneAvailabilityState.Ready, "TEST_COMPONENT_ONLY", "Controlled component readiness; no native frame or authority witness.");
        }
    }

    [AvaloniaFact]
    public async Task Admitted_manual_document_renders_a_selectable_tab_and_separate_trailing_pin()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The Home/Files read-admission fixture requires Linux.");
        var fixture = await Fixture.CreateAsync();
        var reader = await ActualOptionalReaderFixture(canonicalSavedProject: true);
        DeveloperProjectWorkbenchPage? page = null;
        Window? window = null;
        try
        {
            page = await ManualFileNavigationPage(fixture, reader, componentReadiness: new PreviewComponentReadiness());
            window = new Window { Width = 1000, Height = 760, Content = page };
            window.Show();
            await page.ActivateAsync(OriginalTestBodyToken);
            Assert.True(page.TrySetValue("SourcePath", "seed.txt"));
            var open = page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken).AsTask();
            await reader.AcceptRead(open);
            await open;

            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var row = page.GetVisualDescendants().OfType<CuiContainerControl>().Single(control =>
                AutomationProperties.GetName(control) == "Open source documents");
            row.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var view = row.GetVisualAncestors().OfType<ScrollViewer>().First(scroll => scroll.Extent.Height > scroll.Viewport.Height);
            Assert.True(view.Offset.Y > 0);
            var visible = new Rect(row.Bounds.Size).TransformToAABB(row.TransformToVisual(window)!.Value);
            Assert.InRange(visible.Top, -1, window.ClientSize.Height);
            Assert.InRange(visible.Bottom, 0, window.ClientSize.Height + 1);
            var buttons = row.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToArray();
            var select = Assert.Single(buttons, button => button.Content is string label && label.Contains("seed.txt"));
            var close = Assert.Single(buttons, button => Equals(button.Content, "×"));
            var pin = Assert.Single(buttons, button => AutomationProperties.GetAutomationId(button) == "dev-pin-source-tab");
            Assert.True(select.IsEffectivelyEnabled);
            Assert.True(pin.IsEffectivelyEnabled);
            Assert.True(select.Focus(NavigationMethod.Tab));
            Assert.True(pin.Focus(NavigationMethod.Tab));
            Rect InRow(Control control) => new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(row)!.Value);
            var first = InRow(select);
            var last = InRow(close);
            var trailing = InRow(pin);
            Assert.True(first.Width > 0 && last.Width > 0 && trailing.Width > 0);
            Assert.True(first.Right <= last.Left + 1 && last.Right < trailing.Left);
            // The authored grid leaves a 6-DIP gap before its trailing action.
            Assert.InRange(Math.Abs((first.Left + last.Right) / 2 - (trailing.Left - 6) / 2), 0, 2);
        }
        finally
        {
            if (window is not null) { window.Content = null; window.Close(); }
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await fixture.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    private static object[] NativeSourceTabs(DeveloperProjectWorkbenchPage page) =>
        ((System.Collections.IEnumerable)SearchPublicationField("_sourceTabs").GetValue(page)!).Cast<object>().ToArray();
    private static bool NativeTabFlag(object tab, string name) => Assert.IsType<bool>(tab.GetType().GetField(name)!.GetValue(tab));
    private static string NativeTabGroup(object tab) => Assert.IsType<string>(tab.GetType().GetField("GroupId")!.GetValue(tab));
    private static string NativeTabPath(object tab) => Assert.IsType<string>(tab.GetType().GetProperty("Path")!.GetValue(tab));

    private static async Task<DeveloperProjectWorkbenchPage> SearchPublicationPage(Fixture fixture, DeveloperManualSourceReadOwner reader,
        ICuiSceneReadiness? componentReadiness = null, CuiThemePreferenceContext? componentTheme = null)
    {
        var resolved = (await fixture.Dev.ResolveAsync(fixture.Reference)).Value!;
        var documents = new Dictionary<string, DeveloperCodeDocument>(StringComparer.Ordinal);
        DeveloperCodeDocument ResolveDocument(string path)
        {
            if (!documents.TryGetValue(path, out var document))
                documents.Add(path, document = fixture.Document with { FileId = Guid.NewGuid(), CanonicalResourceId = "fixture:" + path, RelativePath = path });
            return document;
        }
        Func<DeveloperResolvedProject, string, CancellationToken, Task<DeveloperOperationResult<DeveloperCodeDocument>>> resolver =
            (_, path, _) => Task.FromResult(DeveloperOperationResult<DeveloperCodeDocument>.Success(ResolveDocument(path)));
        var page = new DeveloperProjectWorkbenchPage(fixture.Dev, fixture.Tasks, resolved,
            fixture.Current.TaskId, fixture.Current.ExecutionId, fixture.Current.ContextId, componentReadiness ?? new SearchPublicationNoReadiness(), resolver,
            () => { }, OriginalTestBodyToken, retainOriginalAcquisition: null, sourceExplorerReader: reader);
        page.BindInterfaceTheme(componentTheme); // Borrow before this helper's existing enrollment and the real host Show.
        SearchPublicationField("_active").SetValue(page, true); // component admission only, not a native frame witness
        return page;
    }

    private static string EnrollObservedSearchInput(DeveloperProjectWorkbenchPage page, Fixture fixture, string path, string savedText)
    {
        page.TrySetValue("SearchQuery", "source");
        var match = Assert.Single(new DeveloperProjectSearchMatcher("source", new()).MatchText(path, savedText, 201));
        var observation = new DeveloperProjectSearchResult(Guid.NewGuid(), fixture.Reference, "source", new(), Array.AsReadOnly(new[] { match }), false, 1, 0);
        SearchPublicationField("_structuredSearch").SetValue(page, observation);
        var map = (Dictionary<string, DeveloperProjectSearchMatch>)SearchPublicationField("_searchMatches").GetValue(page)!;
        map.Clear(); var key = observation.ObservationId.ToString("D") + ":0"; map.Add(key, match);
        Assert.True(page.IsActionAvailable("dev.open-search-result")); return key;
    }
    private static string[] TabLayout(DeveloperProjectWorkbenchPage page)
    {
        var rows = ((System.Collections.IEnumerable)SearchPublicationField("_sourceTabs").GetValue(page)!).Cast<object>();
        return rows.Select(row => row.GetType().GetProperty("Id")!.GetValue(row) + "|" +
            row.GetType().GetProperty("Path")!.GetValue(row) + "|" + row.GetType().GetField("IsPinned")!.GetValue(row) + "|" +
            NativeTabFlag(row, "IsPreview") + "|" + NativeTabGroup(row)).ToArray();
    }

    // Reuse the existing complete production graph rather than null/pretend reader dependencies.
    // Its original Home/Files state is never replaced, and its lifetime is explicitly joined.
    [AvaloniaFact]
    public async Task Current_project_file_navigation_captures_actual_manifest_pages_and_opens_through_manual_Home_READ()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine Home/Files/kernel fixture requires Linux.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture(true); DeveloperProjectWorkbenchPage? page = null;
        try
        {
            var saved = Assert.IsType<DeveloperResolvedProject>(reader.Saved);
            // Fixture seeding only; production Find/Open performs no raw filesystem IO.
            for (var index = 0; index < 105; index++)
                await File.WriteAllTextAsync(Path.Combine(saved.Root.Location, $"match-{index:D3}.cs"), "captured source " + index, OriginalTestBodyToken);
            page = await ManualFileNavigationPage(f, reader);
            Assert.Equal(Guid.Empty, SearchPublicationField("_taskId").GetValue(page));
            page.TrySetValue("FileNavigationQuery", "seed");
            var findSeed = page.DispatchAsync("dev.find-project-file", null, OriginalTestBodyToken).AsTask();
            await reader.AcceptRead(findSeed); await findSeed;
            object[] Rows() { Assert.True(page.TryGetValue("FileNavigationMatches", out var value)); return Assert.IsAssignableFrom<System.Collections.IEnumerable>(value).Cast<object>().ToArray(); }
            string Key(object row) => Assert.IsType<string>(row.GetType().GetProperty("SelectionKey")!.GetValue(row));
            string PathOf(object row) => Assert.IsType<string>(row.GetType().GetProperty("RelativePath")!.GetValue(row));
            var seedKey = Key(Assert.Single(Rows()));
            var openSeed = page.DispatchAsync("dev.open-file-navigation-result", seedKey, OriginalTestBodyToken).AsTask();
            await reader.AcceptRead(openSeed); await openSeed;
            Assert.True(page.TryGetValue("DraftText", out var original)); Assert.Equal("existing independent source\n", original);
            page.TrySetValue("DraftText", "unsaved manual source must survive");
            var seed = Assert.Single(NativeSourceTabs(page)); var oldLayout = TabLayout(page);
            page.TrySetValue("FileNavigationQuery", "match");
            var find = page.DispatchAsync("dev.find-project-file", null, OriginalTestBodyToken).AsTask();
            await reader.AcceptRead(find); await find;
            Assert.Equal(oldLayout, TabLayout(page)); Assert.Equal(50, Rows().Length);
            Assert.Equal("match-000.cs", PathOf(Rows()[0]));
            await page.DispatchAsync("dev.file-navigation-next", null, OriginalTestBodyToken);
            Assert.Equal(50, Rows().Length); Assert.Equal("match-050.cs", PathOf(Rows()[0]));
            var oldPageKey = Key(Rows()[0]);
            await page.DispatchAsync("dev.file-navigation-next", null, OriginalTestBodyToken);
            Assert.Equal(5, Rows().Length); Assert.False(page.IsActionAvailable("dev.file-navigation-next"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => page.DispatchAsync("dev.open-file-navigation-result", oldPageKey, OriginalTestBodyToken).AsTask());
            await page.DispatchAsync("dev.file-navigation-previous", null, OriginalTestBodyToken);
            var current = Rows()[0];
            var open = page.DispatchAsync("dev.open-file-navigation-result", Key(current), OriginalTestBodyToken).AsTask();
            await reader.AcceptRead(open); await open;
            Assert.True(page.TryGetValue("DraftText", out var opened)); Assert.Equal("captured source 50", opened);
            Assert.Contains(seed, NativeSourceTabs(page));
            var manual = seed.GetType().GetField("Manual")!.GetValue(seed)!;
            Assert.Equal("unsaved manual source must survive", manual.GetType().GetProperty("Draft")!.GetValue(manual));
            Assert.Equal(0, f.Owner.ExecuteCalls); Assert.Equal(0, f.Tools.ReadCalls); Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(0, f.Tools.ProcessCalls);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Current_project_file_navigation_rejects_query_changed_during_real_Home_READ_without_adopting_source()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine Home/Files/kernel fixture requires Linux.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture(true); DeveloperProjectWorkbenchPage? page = null;
        var sawRefusal = false; Task? open = null;
        try
        {
            page = await ManualFileNavigationPage(f, reader); page.TrySetValue("FileNavigationQuery", "seed");
            var find = page.DispatchAsync("dev.find-project-file", null, OriginalTestBodyToken).AsTask();
            await reader.AcceptRead(find); await find;
            Assert.True(page.TryGetValue("FileNavigationMatches", out var value));
            var row = Assert.Single(Assert.IsAssignableFrom<System.Collections.IEnumerable>(value).Cast<object>());
            var key = Assert.IsType<string>(row.GetType().GetProperty("SelectionKey")!.GetValue(row));
            open = page.DispatchAsync("dev.open-file-navigation-result", key, OriginalTestBodyToken).AsTask();
            await reader.ObserveRead(open); Assert.False(open.IsCompleted);
            page.TrySetValue("FileNavigationQuery", "newer query");
            await reader.AcceptRead(open);
            await Assert.ThrowsAnyAsync<Exception>(() => open!); sawRefusal = true;
            Assert.Empty(NativeSourceTabs(page)); Assert.Null(SearchPublicationField("_selectedSourceTab").GetValue(page));
            Assert.True(page.TryGetValue("FileNavigationMatches", out value)); Assert.Empty(Assert.IsAssignableFrom<System.Collections.IEnumerable>(value).Cast<object>());
            Assert.Equal(0, f.Tools.ProcessCalls); Assert.Equal(0, f.Tools.WriteCalls);
        }
        finally
        {
            try
            {
                if (open is { IsCompleted: false })
                {
                    await reader.AcceptRead(open);
                    try { await open; } catch { /* The same retained page original is joined below. */ }
                }
                if (page is not null)
                {
                    var close = page.CloseAndDrainAsync();
                    if (sawRefusal) await Assert.ThrowsAnyAsync<Exception>(() => close); else await close;
                }
            }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Manual_Git_buttons_use_genuine_original_process_owner_without_Task_and_preserve_dirty_source()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine Home/Files/kernel fixture requires Linux.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture(true); DeveloperProjectWorkbenchPage? page = null;
        try
        {
            var saved = Assert.IsType<DeveloperResolvedProject>(reader.Saved);
            var git = Directory.CreateDirectory(Path.Combine(saved.Root.Location, ".git")).FullName;
            Directory.CreateDirectory(Path.Combine(git, "objects")); Directory.CreateDirectory(Path.Combine(git, "refs", "heads"));
            await File.WriteAllTextAsync(Path.Combine(git, "HEAD"), "ref: refs/heads/main\n", OriginalTestBodyToken);
            await File.WriteAllTextAsync(Path.Combine(git, "config"), "[core]\nrepositoryformatversion = 0\nbare = false\n", OriginalTestBodyToken);
            page = await ManualFileNavigationPage(f, reader);
            page.TrySetValue("SourcePath", "seed.txt"); var open = page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken).AsTask();
            await reader.AcceptRead(open); await open; page.TrySetValue("DraftText", "unsaved editor text");
            var tab = Assert.Single(NativeSourceTabs(page)); Assert.True(page.IsActionAvailable("dev.git-status"));
            Assert.False(page.IsActionAvailable("dev.create-branch")); Assert.False(page.IsActionAvailable("dev.switch-branch"));
            var inspect = page.DispatchAsync("dev.git-status", null, OriginalTestBodyToken).AsTask();
            await reader.AcceptRead(inspect); await reader.AcceptExecute(inspect); await inspect;
            var observed = Assert.IsType<DeveloperManualGitObservation>(typeof(DeveloperProjectWorkbenchPage)
                .GetProperty("OriginalManualGitObservation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
            Assert.True(observed.ProcessSucceeded); Assert.Equal(DeveloperProjectGitInspectionKind.Status, observed.Operation);
            Assert.True(page.TryGetValue("ProcessOutput", out var output)); Assert.Contains("seed.txt", Assert.IsType<string>(output));
            Assert.True(page.TryGetValue("DraftText", out var draft)); Assert.Equal("unsaved editor text", draft);
            Assert.Same(tab, Assert.Single(NativeSourceTabs(page))); Assert.True(page.HasUnsavedWork);
            Assert.Equal(Guid.Empty, SearchPublicationField("_taskId").GetValue(page)); Assert.Equal(0, f.Owner.ExecuteCalls);
            Assert.Equal(0, f.Tools.ReadCalls); Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(0, f.Tools.ProcessCalls);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Manual_saved_stage_picker_runs_actual_command_without_AI_and_preserves_dirty_source()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine Home/Files/kernel fixture requires Linux.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture(true, savedStages: true); DeveloperProjectWorkbenchPage? page = null;
        try
        {
            page = await ManualFileNavigationPage(f, reader);
            page.TrySetValue("SourcePath", "seed.txt"); var open = page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken).AsTask();
            await reader.AcceptRead(open); await open; page.TrySetValue("DraftText", "unsaved stage editor text");
            var tab = Assert.Single(NativeSourceTabs(page)); Assert.False(page.IsActionAvailable("dev.run-stage"));
            Assert.True(page.TryGetValue("SavedBuildStages", out var rows));
            var items = Assert.IsAssignableFrom<System.Collections.IEnumerable>(rows).Cast<object>().ToArray();
            var compile = Assert.Single(items, item => (string)item.GetType().GetProperty("Label")!.GetValue(item)! == "Fixture build / Compile");
            var key = (string)compile.GetType().GetProperty("SelectionKey")!.GetValue(compile)!;
            await page.DispatchAsync("dev.select-build-stage", key, OriginalTestBodyToken);
            Assert.True(page.IsActionAvailable("dev.run-stage")); Assert.True(page.TryGetValue("SelectedBuildStageSummary", out var summary));
            Assert.Contains("Unsaved editor changes are kept", Assert.IsType<string>(summary));
            var run = page.DispatchAsync("dev.run-stage", null, OriginalTestBodyToken).AsTask();
            await reader.AcceptRead(run); await reader.AcceptExecute(run); await run;
            var observed = Assert.IsType<DeveloperManualStageObservation>(typeof(DeveloperProjectWorkbenchPage)
                .GetProperty("OriginalManualStageObservation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
            Assert.True(observed.ProcessSucceeded); Assert.Equal("fixture-build", observed.SavedTaskId); Assert.Equal("compile", observed.StageId);
            Assert.True(page.TryGetValue("ProcessOutput", out var output)); Assert.Contains("saved-stage-ready", Assert.IsType<string>(output));
            Assert.Contains("stage-error", Assert.IsType<string>(output)); Assert.True(page.TryGetValue("DraftText", out var draft));
            Assert.Equal("unsaved stage editor text", draft); Assert.Same(tab, Assert.Single(NativeSourceTabs(page))); Assert.True(page.HasUnsavedWork);
            var dependent = Assert.Single(items, item => (string)item.GetType().GetProperty("Label")!.GetValue(item)! == "Fixture build / Package");
            await page.DispatchAsync("dev.select-build-stage", dependent.GetType().GetProperty("SelectionKey")!.GetValue(dependent), OriginalTestBodyToken);
            Assert.False(page.IsActionAvailable("dev.run-stage"));
            Assert.Equal(Guid.Empty, SearchPublicationField("_taskId").GetValue(page)); Assert.Equal(0, f.Owner.ExecuteCalls);
            Assert.Equal(0, f.Tools.ReadCalls); Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(0, f.Tools.ProcessCalls);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Manual_reported_build_problems_use_actual_process_and_fresh_READ_keep_dirty_draft_and_reject_old_or_outside_rows()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine Home/Files/kernel fixture requires Linux.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture(true, savedStages: true); DeveloperProjectWorkbenchPage? page = null;
        try
        {
            page = await ManualFileNavigationPage(f, reader);
            Assert.True(page.TryGetValue("SavedBuildStages", out var stages));
            var stage = Assert.Single(Assert.IsAssignableFrom<System.Collections.IEnumerable>(stages).Cast<object>(),
                item => (string)item.GetType().GetProperty("Label")!.GetValue(item)! == "Fixture build / Reported problems");
            await page.DispatchAsync("dev.select-build-stage", stage.GetType().GetProperty("SelectionKey")!.GetValue(stage), OriginalTestBodyToken);
            async Task RunReportedOutput()
            {
                var run = page.DispatchAsync("dev.run-stage", null, OriginalTestBodyToken).AsTask();
                await reader.AcceptRead(run); await reader.AcceptExecute(run); await run;
            }
            object[] Problems()
            {
                Assert.True(page.TryGetValue("BuildProblems", out var rows));
                return Assert.IsAssignableFrom<System.Collections.IEnumerable>(rows).Cast<object>().ToArray();
            }
            string Key(object row) => Assert.IsType<string>(row.GetType().GetProperty("SelectionKey")!.GetValue(row));
            object SourceRow() => Assert.Single(Problems(), row => ((string)row.GetType().GetProperty("Label")!.GetValue(row)!).StartsWith("seed.txt:", StringComparison.Ordinal));
            await RunReportedOutput();
            var observed = Assert.IsType<DeveloperManualStageObservation>(typeof(DeveloperProjectWorkbenchPage)
                .GetProperty("OriginalManualStageObservation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
            Assert.Equal(3, observed.Process!.ExitCode); Assert.False(observed.ProcessSucceeded);
            Assert.Contains("DEVFIX001", observed.Process.StandardOutput); Assert.True(page.TryGetValue("BuildProblemsStatus", out var status));
            Assert.Contains("current content may differ", Assert.IsType<string>(status));
            var outside = Assert.Single(Problems(), row => ((string)row.GetType().GetProperty("Label")!.GetValue(row)!).Contains("DEVFIX002", StringComparison.Ordinal));
            Assert.False(Assert.IsType<bool>(outside.GetType().GetProperty("CanOpen")!.GetValue(outside)));
            Assert.Throws<InvalidOperationException>(() => { _ = page.DispatchAsync("dev.open-build-problem", Key(outside), OriginalTestBodyToken); });
            Assert.Empty(NativeSourceTabs(page));
            var oldKey = Key(SourceRow());
            var open = page.DispatchAsync("dev.open-build-problem", oldKey, OriginalTestBodyToken).AsTask();
            await reader.ObserveRead(open); Assert.False(open.IsCompleted); Assert.Empty(NativeSourceTabs(page));
            await reader.AcceptRead(open); await open;
            var tab = Assert.Single(NativeSourceTabs(page));
            Assert.True(page.TryGetValue("DraftText", out var text)); Assert.Equal("existing independent source\n", text);
            Assert.True(page.TryGetValue("EditorTextSelection", out var selection));
            Assert.Equal(9, Assert.IsType<CuiTextSelection>(selection).Start);
            await RunReportedOutput(); Assert.NotEqual(oldKey, Key(SourceRow()));
            Assert.Throws<InvalidOperationException>(() => { _ = page.DispatchAsync("dev.open-build-problem", oldKey, OriginalTestBodyToken); });
            page.TrySetValue("DraftText", "unsaved source kept intact"); page.TrySetValue("SourceLocation", "1:3");
            await page.DispatchAsync("dev.go-to-source-location", null, OriginalTestBodyToken);
            Assert.True(page.TryGetValue("EditorTextSelection", out var retainedSelection)); Assert.Equal(2, Assert.IsType<CuiTextSelection>(retainedSelection).Start);
            var dirtyOpen = page.DispatchAsync("dev.open-build-problem", Key(SourceRow()), OriginalTestBodyToken).AsTask();
            await reader.AcceptRead(dirtyOpen); await dirtyOpen;
            Assert.True(page.TryGetValue("DraftText", out text)); Assert.Equal("unsaved source kept intact", text);
            Assert.True(page.TryGetValue("EditorTextSelection", out selection)); Assert.Same(retainedSelection, selection);
            Assert.True(page.TryGetValue("Status", out status)); Assert.Contains("reported location was not selected", Assert.IsType<string>(status));
            Assert.Same(tab, Assert.Single(NativeSourceTabs(page))); Assert.True(page.HasUnsavedWork);
            Assert.Equal(Guid.Empty, SearchPublicationField("_taskId").GetValue(page)); Assert.Equal(0, f.Owner.ExecuteCalls);
            Assert.Equal(0, f.Tools.ReadCalls); Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(0, f.Tools.ProcessCalls);
            // The real command deliberately emits an MSBuild-format fixture then exits 3.
            // This checks real process -> decoder -> authorized Open, not an actual compiler pass.
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Reported_build_location_absent_from_fresh_saved_source_opens_actual_text_without_clamping()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine Home/Files/kernel fixture requires Linux.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture(true, savedStages: true); DeveloperProjectWorkbenchPage? page = null;
        try
        {
            page = await ManualFileNavigationPage(f, reader); Assert.True(page.TryGetValue("SavedBuildStages", out var stages));
            var stage = Assert.Single(Assert.IsAssignableFrom<System.Collections.IEnumerable>(stages).Cast<object>(),
                item => (string)item.GetType().GetProperty("Label")!.GetValue(item)! == "Fixture build / Reported problems");
            await page.DispatchAsync("dev.select-build-stage", stage.GetType().GetProperty("SelectionKey")!.GetValue(stage), OriginalTestBodyToken);
            var run = page.DispatchAsync("dev.run-stage", null, OriginalTestBodyToken).AsTask();
            await reader.AcceptRead(run); await reader.AcceptExecute(run); await run;
            // External saved-source change after the real command's captured reported location.
            await File.WriteAllTextAsync(Path.Combine(reader.Saved!.Root.Location, "seed.txt"), "tiny", OriginalTestBodyToken);
            Assert.True(page.TryGetValue("BuildProblems", out var rows));
            var row = Assert.Single(Assert.IsAssignableFrom<System.Collections.IEnumerable>(rows).Cast<object>(),
                item => ((string)item.GetType().GetProperty("Label")!.GetValue(item)!).StartsWith("seed.txt:", StringComparison.Ordinal));
            var open = page.DispatchAsync("dev.open-build-problem", row.GetType().GetProperty("SelectionKey")!.GetValue(row), OriginalTestBodyToken).AsTask();
            await reader.AcceptRead(open); await open;
            Assert.True(page.TryGetValue("DraftText", out var actual)); Assert.Equal("tiny", actual);
            Assert.True(page.TryGetValue("EditorTextSelection", out var selection)); Assert.Null(selection);
            Assert.True(page.TryGetValue("Status", out var status)); Assert.Contains("not present", Assert.IsType<string>(status));
            Assert.Single(NativeSourceTabs(page)); Assert.False(page.HasUnsavedWork);
            Assert.Equal(0, f.Tools.ReadCalls); Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(0, f.Tools.ProcessCalls);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    [AvaloniaFact]
    public async Task Manual_stage_authoring_reviews_saves_same_project_then_runs_only_after_separate_permission()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine Home/Files/kernel fixture requires Linux.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture(true); DeveloperProjectWorkbenchPage? page = null;
        try
        {
            page = await ManualFileNavigationPage(f, reader, configureMetadata: true);
            page.TrySetValue("SourcePath", "seed.txt"); var open = page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken).AsTask();
            await reader.AcceptRead(open); await open; var tab = Assert.Single(NativeSourceTabs(page));
            await page.DispatchAsync("dev.new-compile-stage", null, OriginalTestBodyToken);
            page.TrySetValue("StageEditTaskId", "build"); page.TrySetValue("StageEditStageId", "compile");
            page.TrySetValue("StageEditCommand", "printf 'native configured stage'; printf built > native-configured.txt"); page.TrySetValue("StageEditTimeout", "20");
            Assert.False(page.IsActionAvailable("dev.save-build-stage"));
            await page.DispatchAsync("dev.review-build-stage", null, OriginalTestBodyToken); Assert.True(page.IsActionAvailable("dev.save-build-stage"));
            page.TrySetValue("StageEditTimeout", "25"); Assert.False(page.IsActionAvailable("dev.save-build-stage"));
            await page.DispatchAsync("dev.review-build-stage", null, OriginalTestBodyToken);
            var save = page.DispatchAsync("dev.save-build-stage", null, OriginalTestBodyToken).AsTask();
            await reader.AcceptRead(save); await save;
            var saved = Assert.IsType<DeveloperBuildStageEditObservation>(typeof(DeveloperProjectWorkbenchPage).GetProperty("OriginalBuildStageEdit")!.GetValue(page));
            Assert.True(saved.OriginalStoreResult!.Succeeded, saved.OriginalStoreResult.Error?.Message); Assert.Empty(saved.OriginalErrors);
            var current = saved.OriginalStoreResult.Value!.Project;
            Assert.False(File.Exists(Path.Combine(current.Root.Location, "native-configured.txt")));
            Assert.True(page.IsActionAvailable("dev.run-stage")); Assert.Same(tab, Assert.Single(NativeSourceTabs(page)));
            Assert.True(page.TryGetValue("DraftText", out var draft)); Assert.Equal("existing independent source\n", draft);
            var run = page.DispatchAsync("dev.run-stage", null, OriginalTestBodyToken).AsTask();
            await reader.AcceptRead(run); await reader.AcceptExecute(run); await run;
            Assert.True(page.TryGetValue("ProcessOutput", out var output)); Assert.Contains("native configured stage", Assert.IsType<string>(output));
            Assert.Equal("built", await File.ReadAllTextAsync(Path.Combine(current.Root.Location, "native-configured.txt"), OriginalTestBodyToken));
            Assert.True(page.IsActionAvailable("dev.edit-build-stage")); await page.DispatchAsync("dev.edit-build-stage", null, OriginalTestBodyToken);
            page.TrySetValue("StageEditCommand", "printf 'edited stage'; printf edited > native-configured.txt");
            await page.DispatchAsync("dev.review-build-stage", null, OriginalTestBodyToken);
            var editSave = page.DispatchAsync("dev.save-build-stage", null, OriginalTestBodyToken).AsTask(); await reader.AcceptRead(editSave); await editSave;
            var edited = Assert.IsType<DeveloperBuildStageEditObservation>(typeof(DeveloperProjectWorkbenchPage).GetProperty("OriginalBuildStageEdit")!.GetValue(page));
            Assert.True(edited.OriginalStoreResult!.Succeeded); Assert.Equal(2, edited.OriginalStoreResult.Value!.Task.Revision);
            Assert.Contains("edited stage", Assert.Single(edited.OriginalStoreResult.Value.Task.Stages).Command!);
            Assert.Equal("built", await File.ReadAllTextAsync(Path.Combine(current.Root.Location, "native-configured.txt"), OriginalTestBodyToken));
            Assert.Same(tab, Assert.Single(NativeSourceTabs(page)));
            Assert.Equal(Guid.Empty, SearchPublicationField("_taskId").GetValue(page)); Assert.Equal(0, f.Owner.ExecuteCalls);
            Assert.Equal(0, f.Tools.ReadCalls); Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(0, f.Tools.ProcessCalls);
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
        }
    }

    private static async Task<DeveloperProjectWorkbenchPage> ManualFileNavigationPage(Fixture fixture, ActualReaderFixture reader,
        bool configureMetadata = false, ICuiSceneReadiness? componentReadiness = null)
    {
        await fixture.Dev.CloseAndDrainAsync();
        fixture.Dev = new(reader.SavedStore!, new(fixture.ConversationSource, fixture.Containers), fixture.Tasks, fixture.Owner, new(fixture.Tools));
        Func<DeveloperResolvedProject, string, CancellationToken, Task<DeveloperOperationResult<DeveloperCodeDocument>>> unused =
            (_, _, _) => throw new InvalidOperationException("Manual navigation must use the genuine Home/Files reader, not Task document identity.");
        var constructor = typeof(DeveloperProjectWorkbenchPage).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(value => value.GetParameters().Length == 16 && value.GetParameters()[9].ParameterType == typeof(DeveloperManualSourceReadOwner));
        var page = (DeveloperProjectWorkbenchPage)constructor.Invoke([fixture.Dev, fixture.Tasks, reader.Saved!, componentReadiness ?? new SearchPublicationNoReadiness(), unused,
            (Action)(() => { }), OriginalTestBodyToken, null, null, reader.Owner, null, configureMetadata ? reader.SavedStore : null, null,
            configureMetadata ? reader.Keys : null, configureMetadata ? reader.Paths : null, reader.ProcessOwner]);
        if (componentReadiness is null)
            SearchPublicationField("_active").SetValue(page, true); // Command component only; no native frame witness.
        return page;
    }

    private sealed record ActualReaderFixture(DeveloperManualSourceReadOwner Owner, IAsyncDisposable Lifetime,
        DeveloperResolvedProject? Saved, FileDeveloperWorkspaceStore? SavedStore, Func<Task, Task> AcceptRead, Func<Task, Task> ObserveRead,
        DeveloperManualProjectProcessOwner ProcessOwner, Func<Task, Task> AcceptExecute, IProviderSecretStore? Keys, IAppPaths? Paths);
    private static async Task<ActualReaderFixture> ActualOptionalReaderFixture(bool canonicalSavedProject = false, bool savedStages = false)
    {
        var type = typeof(Haven.Desktop.Tests.FilesDeveloperOriginalSourcePublicationTests)
            .GetNestedType("Rig", BindingFlags.NonPublic)!;
        var method = type.GetMethod("CreateAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        var actual = (Task)method.Invoke(null, [null, canonicalSavedProject, true, true, null, false])!;
        await actual;
        var rig = actual.GetType().GetProperty("Result")!.GetValue(actual)!;
        try
        {
            if (savedStages)
                await (Task)type.GetMethod("ConfigureSavedStagesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(rig, null)!;
            var graph = (IServiceProvider)type.GetProperty("Graph", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rig)!;
            object Required(Type service) => graph.GetService(service) ?? throw new InvalidOperationException("Missing genuine optional reader service: " + service.Name);
            var reader = new DeveloperManualSourceReadOwner((NativeFilesWorkspaceService)Required(typeof(NativeFilesWorkspaceService)),
                (FilesDeveloperOriginalSourceSelection)Required(typeof(FilesDeveloperOriginalSourceSelection)),
                (IDeveloperProjectOriginalReadAdmissionSource)Required(typeof(IDeveloperProjectOriginalReadAdmissionSource)),
                (IDeveloperProjectOriginalPhysicalCaptureSource)Required(typeof(IDeveloperProjectOriginalPhysicalCaptureSource)));
            async Task AcceptRead(Task operation)
            {
                var decide = type.GetMethod("DecideNextAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var original = (Task)decide.Invoke(rig, [operation, HavenOS.Home.Core.HomeDeveloperProjectReadAdmissionSource.ReadAction,
                    HavenOS.Home.PermissionsTrustNotifications.HomeApprovalChoice.Accept])!;
                await original;
            }
            async Task ObserveRead(Task operation)
            {
                var observe = type.GetMethod("ObservePendingAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var original = (Task)observe.Invoke(rig, [operation, HavenOS.Home.Core.HomeDeveloperProjectReadAdmissionSource.ReadAction])!;
                await original;
            }
            async Task AcceptExecute(Task operation)
            {
                var decide = type.GetMethod("DecideNextAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var original = (Task)decide.Invoke(rig, [operation, HavenOS.Home.Core.HomeDeveloperManualProjectExecutionAdmissionSource.RunAction,
                    HavenOS.Home.PermissionsTrustNotifications.HomeApprovalChoice.Accept])!;
                await original;
            }
            return new(reader, (IAsyncDisposable)rig,
                canonicalSavedProject ? (DeveloperResolvedProject)type.GetProperty("Saved", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rig)! : null,
                canonicalSavedProject ? (FileDeveloperWorkspaceStore)type.GetProperty("SavedStore", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rig)! : null,
                AcceptRead, ObserveRead, (DeveloperManualProjectProcessOwner)Required(typeof(DeveloperManualProjectProcessOwner)), AcceptExecute,
                canonicalSavedProject ? (IProviderSecretStore)Required(typeof(IProviderSecretStore)) : null,
                canonicalSavedProject ? (IAppPaths)Required(typeof(IAppPaths)) : null);
        }
        catch { await ((IAsyncDisposable)rig).DisposeAsync(); throw; }
    }
    private static FieldInfo SearchPublicationField(string name) => typeof(DeveloperProjectWorkbenchPage)
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
    private sealed class SearchPublicationNoReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => ValueTask.FromResult(
            new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable, "TEST_SOURCE_PUBLICATION_ONLY", "Component control; no native frame or readiness witness."));
    }

}
