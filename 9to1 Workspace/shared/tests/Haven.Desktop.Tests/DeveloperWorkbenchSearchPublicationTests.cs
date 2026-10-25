using System.Reflection;
using Avalonia.Headless.XUnit;
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

    private static object[] NativeSourceTabs(DeveloperProjectWorkbenchPage page) =>
        ((System.Collections.IEnumerable)SearchPublicationField("_sourceTabs").GetValue(page)!).Cast<object>().ToArray();
    private static bool NativeTabFlag(object tab, string name) => Assert.IsType<bool>(tab.GetType().GetField(name)!.GetValue(tab));
    private static string NativeTabGroup(object tab) => Assert.IsType<string>(tab.GetType().GetField("GroupId")!.GetValue(tab));
    private static string NativeTabPath(object tab) => Assert.IsType<string>(tab.GetType().GetProperty("Path")!.GetValue(tab));

    private static async Task<DeveloperProjectWorkbenchPage> SearchPublicationPage(Fixture fixture, DeveloperManualSourceReadOwner reader)
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
            .Single(value => value.GetParameters().Length == 14 && value.GetParameters()[11].ParameterType == typeof(DeveloperManualSourceReadOwner));
        var page = (DeveloperProjectWorkbenchPage)ctor.Invoke([fixture.Dev, fixture.Tasks, resolved,
            fixture.Current.TaskId, fixture.Current.ExecutionId, fixture.Current.ContextId, new SearchPublicationNoReadiness(), resolver,
            (Action)(() => { }), OriginalTestBodyToken, null, reader, null, null]);
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
    private static async Task<(DeveloperManualSourceReadOwner Owner, IAsyncDisposable Lifetime)> ActualOptionalReaderFixture()
    {
        var type = typeof(Haven.Desktop.Tests.FilesDeveloperOriginalSourcePublicationTests)
            .GetNestedType("Rig", BindingFlags.NonPublic)!;
        var method = type.GetMethod("CreateAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        var actual = (Task)method.Invoke(null, [null, false, true, true])!;
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
            return (reader, (IAsyncDisposable)rig);
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
