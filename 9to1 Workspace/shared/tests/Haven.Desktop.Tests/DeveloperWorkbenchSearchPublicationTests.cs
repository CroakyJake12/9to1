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
            row.GetType().GetProperty("Path")!.GetValue(row) + "|" + row.GetType().GetField("IsPinned")!.GetValue(row)).ToArray();
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
