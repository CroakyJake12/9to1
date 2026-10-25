using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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

    [AvaloniaFact]
    public async Task Native_static_CUI_preview_retains_last_valid_scene_without_source_effects()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The genuine optional Home/Files reader fixture requires Linux; other platforms need their owning graph.");
        var f = await Fixture.CreateAsync(); var reader = await ActualOptionalReaderFixture();
        DeveloperProjectWorkbenchPage? page = null;
        try
        {
            const string saved = "<Cui><Page><Container Type='Vertical Stack'><Text text='compiled &amp; literal' /></Container></Page></Cui>";
            f.Tools.TextValue = saved; page = await SearchPublicationPage(f, reader.Owner, new PreviewComponentReadiness());
            // Real workbench artifact, registered Object and child scene. Readiness is
            // a controlled component input, not a native frame/installation witness.
            await page.ActivateAsync(OriginalTestBodyToken);
            page.TrySetValue("SourcePath", "Main.cui"); await page.DispatchAsync("dev.open-source", null, OriginalTestBodyToken);
            var editor = SearchPublicationField("_editor").GetValue(page);
            var selection = SearchPublicationField("_editorTextSelection").GetValue(page);
            var reads = f.Tools.ReadCalls; var preparations = f.Owner.PrepareCalls;
            var slot = Assert.IsType<ContentControl>(SearchPublicationField("_cuiPreviewSlot").GetValue(page));
            await page.DispatchAsync("dev.preview-cui-draft", null, OriginalTestBodyToken);
            var host = Assert.IsType<CuiSceneHost>(slot.Content);
            Assert.False(host.IsHitTestVisible); Assert.Equal(CuiSceneAvailabilityState.Ready, host.Availability!.State);
            Assert.Contains(Assert.IsAssignableFrom<Control>(host.Content).GetVisualDescendants().OfType<TextBlock>(), value => value.Text == "compiled & literal");
            Assert.Same(editor, SearchPublicationField("_editor").GetValue(page));
            Assert.Same(selection, SearchPublicationField("_editorTextSelection").GetValue(page));
            Assert.True(page.TryGetValue("CuiDraftPreviewStatus", out var status)); Assert.Contains("Showing current Main.cui", Assert.IsType<string>(status));
            Assert.Contains(DeveloperManualSourceEditOwner.Sha256(saved), Assert.IsType<string>(status));
            const string unsupported = "<Cui><Page><Image Source='https://example.test/not-fetched.png' /></Page></Cui>";
            Assert.True(page.TrySetValue("DraftText", unsupported));
            await page.DispatchAsync("dev.preview-cui-draft", null, OriginalTestBodyToken);
            Assert.Same(host, slot.Content);
            Assert.True(page.TryGetValue("CuiDraftPreviewDiagnostics", out var diagnostics)); Assert.Contains("DEVCP003", Assert.IsType<string>(diagnostics));
            Assert.True(page.TryGetValue("CuiDraftPreviewStatus", out status)); Assert.Contains("last valid; stale", Assert.IsType<string>(status));
            Assert.True(page.TryGetValue("DraftText", out var draft)); Assert.Equal(unsupported, draft);
            page.TrySetValue("DraftText", "<Cui><Page><Text text='next compiled draft' /></Page></Cui>");
            await page.DispatchAsync("dev.preview-cui-draft", null, OriginalTestBodyToken);
            var replacement = Assert.IsType<CuiSceneHost>(slot.Content); Assert.NotSame(host, replacement);
            Assert.Contains(Assert.IsAssignableFrom<Control>(replacement.Content).GetVisualDescendants().OfType<TextBlock>(), value => value.Text == "next compiled draft");
            Assert.Equal(reads, f.Tools.ReadCalls); Assert.Equal(preparations, f.Owner.PrepareCalls);
            Assert.Equal(saved, f.Tools.TextValue); Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(0, f.Tools.ProcessCalls);
            var scenes = Assert.IsAssignableFrom<System.Collections.IEnumerable>(SearchPublicationField("_cuiPreviewScenes").GetValue(page));
            Assert.Same(replacement, Assert.Single(scenes.Cast<object>()));
            await page.CloseAndDrainAsync(); Assert.Empty(scenes.Cast<object>());
        }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); }
            finally { try { await f.Dev.CloseAndDrainAsync(); } finally { await reader.Lifetime.DisposeAsync(); } }
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
            page.TrySetValue("DraftText", "<Cui><Page><Text text='captured before readiness' /></Page></Cui>");
            readiness.HoldNext = true;
            pending = page.DispatchAsync("dev.preview-cui-draft", null, OriginalTestBodyToken).AsTask();
            await readiness.Entered.Task.WaitAsync(OriginalTestBodyToken); Assert.False(pending.IsCompleted);
            const string newer = "<Cui><Page><Text text='newer edit must win' /></Page></Cui>";
            Assert.True(page.TrySetValue("DraftText", newer)); readiness.Release.TrySetResult(); await pending;
            Assert.Same(valid, slot.Content);
            Assert.True(page.TryGetValue("DraftText", out var draft)); Assert.Equal(newer, draft);
            Assert.True(page.TryGetValue("CuiDraftPreviewStatus", out var status)); Assert.Contains("last valid; stale", Assert.IsType<string>(status));
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

    private static object[] NativeSourceTabs(DeveloperProjectWorkbenchPage page) =>
        ((System.Collections.IEnumerable)SearchPublicationField("_sourceTabs").GetValue(page)!).Cast<object>().ToArray();
    private static bool NativeTabFlag(object tab, string name) => Assert.IsType<bool>(tab.GetType().GetField(name)!.GetValue(tab));
    private static string NativeTabGroup(object tab) => Assert.IsType<string>(tab.GetType().GetField("GroupId")!.GetValue(tab));
    private static string NativeTabPath(object tab) => Assert.IsType<string>(tab.GetType().GetProperty("Path")!.GetValue(tab));

    private static async Task<DeveloperProjectWorkbenchPage> SearchPublicationPage(Fixture fixture, DeveloperManualSourceReadOwner reader, ICuiSceneReadiness? componentReadiness = null)
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
        var ctor = typeof(DeveloperProjectWorkbenchPage).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(value => value.GetParameters().Length == 15 && value.GetParameters()[11].ParameterType == typeof(DeveloperManualSourceReadOwner)
                && value.GetParameters()[14].ParameterType == typeof(DeveloperTaskDraftRecoveryOwner));
        var page = (DeveloperProjectWorkbenchPage)ctor.Invoke([fixture.Dev, fixture.Tasks, resolved,
            fixture.Current.TaskId, fixture.Current.ExecutionId, fixture.Current.ContextId, componentReadiness ?? new SearchPublicationNoReadiness(), resolver,
            (Action)(() => { }), OriginalTestBodyToken, null, reader, null, null, null]);
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

    private static async Task<DeveloperProjectWorkbenchPage> ManualFileNavigationPage(Fixture fixture, ActualReaderFixture reader)
    {
        await fixture.Dev.CloseAndDrainAsync();
        fixture.Dev = new(reader.SavedStore!, new(fixture.ConversationSource, fixture.Containers), fixture.Tasks, fixture.Owner, new(fixture.Tools));
        Func<DeveloperResolvedProject, string, CancellationToken, Task<DeveloperOperationResult<DeveloperCodeDocument>>> unused =
            (_, _, _) => throw new InvalidOperationException("Manual navigation must use the genuine Home/Files reader, not Task document identity.");
        var constructor = typeof(DeveloperProjectWorkbenchPage).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(value => value.GetParameters().Length == 16 && value.GetParameters()[9].ParameterType == typeof(DeveloperManualSourceReadOwner));
        var page = (DeveloperProjectWorkbenchPage)constructor.Invoke([fixture.Dev, fixture.Tasks, reader.Saved!, new SearchPublicationNoReadiness(), unused,
            (Action)(() => { }), OriginalTestBodyToken, null, null, reader.Owner, null, null, null, null, null, null]);
        SearchPublicationField("_active").SetValue(page, true); // Command component only; no native frame witness.
        return page;
    }

    private sealed record ActualReaderFixture(DeveloperManualSourceReadOwner Owner, IAsyncDisposable Lifetime,
        DeveloperResolvedProject? Saved, FileDeveloperWorkspaceStore? SavedStore, Func<Task, Task> AcceptRead, Func<Task, Task> ObserveRead);
    private static async Task<ActualReaderFixture> ActualOptionalReaderFixture(bool canonicalSavedProject = false)
    {
        var type = typeof(Haven.Desktop.Tests.FilesDeveloperOriginalSourcePublicationTests)
            .GetNestedType("Rig", BindingFlags.NonPublic)!;
        var method = type.GetMethod("CreateAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        var actual = (Task)method.Invoke(null, [null, canonicalSavedProject, true, true, null])!;
        await actual;
        var rig = actual.GetType().GetProperty("Result")!.GetValue(actual)!;
        try
        {
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
            return new(reader, (IAsyncDisposable)rig,
                canonicalSavedProject ? (DeveloperResolvedProject)type.GetProperty("Saved", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rig)! : null,
                canonicalSavedProject ? (FileDeveloperWorkspaceStore)type.GetProperty("SavedStore", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rig)! : null,
                AcceptRead, ObserveRead);
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
